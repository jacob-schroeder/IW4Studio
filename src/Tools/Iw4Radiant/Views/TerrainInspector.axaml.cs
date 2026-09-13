using System.Globalization;
using Avalonia.Controls;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Views;

public partial class TerrainInspector : UserControl
{
    private bool _updating;

    public TerrainInspector() => InitializeComponent();

    internal void InitializeActions(EditorSession session, EditorDialogs dialogs, Action finishGestures)
    {
        StitchToleranceBox.Text = Number(session.GridSize);
        TerrainCount.ValueChanged += (_, _) => UpdateSettings(session, dialogs, finishGestures);
        RadiusValue.ValueChanged += (_, _) => UpdateSettings(session, dialogs, finishGestures);
        StrengthValue.ValueChanged += (_, _) => UpdateSettings(session, dialogs, finishGestures);
        SculptModeBox.SelectionChanged += (_, _) => UpdateSettings(session, dialogs, finishGestures);
        FlattenHeightBox.LostFocus += async (_, _) => await UpdateFlattenHeightAsync(session, dialogs, finishGestures);
        SmoothVerticesButton.Click += async (_, _) => await EditVerticesAsync(session, dialogs, finishGestures, flatten: false);
        FlattenVerticesButton.Click += async (_, _) => await EditVerticesAsync(session, dialogs, finishGestures, flatten: true);
        StitchButton.Click += async (_, _) => await StitchAsync(session, dialogs, finishGestures);
        RefreshSelection(session);
    }

    internal void RefreshSelection(EditorSession session)
    {
        if (_updating) return;
        _updating = true;
        try
        {
            if (!TerrainCount.IsKeyboardFocusWithin) TerrainCount.Value = session.TerrainVertices;
            if (!RadiusValue.IsKeyboardFocusWithin) RadiusValue.Value = (decimal)session.SculptRadius;
            StrengthCaption.Text = session.SculptMode == TerrainSculptMode.RaiseLower ? "Strength (units)" : "Strength (%)";
            StrengthValue.Maximum = session.SculptMode == TerrainSculptMode.RaiseLower ? 256 : 100;
            if (!StrengthValue.IsKeyboardFocusWithin) StrengthValue.Value = (decimal)session.SculptStrength;
            SculptModeBox.SelectedIndex = (int)session.SculptMode;
            if (!FlattenHeightBox.IsKeyboardFocusWithin) FlattenHeightBox.Text = Number(session.FlattenHeight);
            int count = session.Selection.Items.OfType<TerrainVertexSelection>().Count();
            VertexSelectionText.Text = count == 0 ? "Select terrain vertices for exact height edits." :
                $"{count} terrain {(count == 1 ? "vertex" : "vertices")} selected.";
            SmoothVerticesButton.IsEnabled = FlattenVerticesButton.IsEnabled = count > 0;
            StitchButton.IsEnabled = session.Selection.Count == 2 && session.Selection.Items.All(item => item is MapTerrain);
        }
        finally { _updating = false; }
    }

    private void UpdateSettings(EditorSession session, EditorDialogs dialogs, Action finishGestures)
    {
        if (_updating || dialogs.BlocksInput) return;
        _updating = true;
        try
        {
            finishGestures();
            if (TerrainCount.Value is { } count) session.TerrainVertices = Math.Clamp((int)count, 2, 16);
            if (RadiusValue.Value is { } radius) session.SculptRadius = (float)radius;
            if (StrengthValue.Value is { } strength) session.SculptStrength = (float)strength;
            if (SculptModeBox.SelectedIndex >= 0) session.SculptMode = (TerrainSculptMode)SculptModeBox.SelectedIndex;
            if (session.SculptMode != TerrainSculptMode.RaiseLower) session.SculptStrength = Math.Min(session.SculptStrength, 100);
            session.Refresh();
        }
        finally { _updating = false; }
        RefreshSelection(session);
    }

    private async Task UpdateFlattenHeightAsync(EditorSession session, EditorDialogs dialogs, Action finishGestures)
    {
        if (_updating || dialogs.BlocksInput) return;
        try
        {
            _updating = true;
            float height = ReadNumber(FlattenHeightBox, "flatten height");
            finishGestures();
            session.FlattenHeight = height;
            session.Refresh();
        }
        catch (ArgumentException exception) { await dialogs.MessageAsync("Terrain height", exception.Message); }
        finally { _updating = false; }
    }

    private async Task EditVerticesAsync(EditorSession session, EditorDialogs dialogs, Action finishGestures, bool flatten)
    {
        if (dialogs.BlocksInput) return;
        try
        {
            _updating = true;
            float height = flatten ? ReadNumber(FlattenHeightBox, "flatten height") : session.FlattenHeight;
            finishGestures();
            var edits = new List<(MapTerrain Terrain, MapTerrain Proposed, int[] Indices)>();
            foreach (var group in session.Selection.Items.OfType<TerrainVertexSelection>().GroupBy(vertex => vertex.Terrain))
            {
                int[] indices = group.Select(vertex => vertex.Index).Distinct().ToArray();
                MapTerrain proposed = group.Key.Clone();
                bool changed = flatten ? TerrainEditing.FlattenVertices(proposed, indices, height) :
                    TerrainEditing.SmoothVertices(proposed, indices);
                if (changed) edits.Add((group.Key, proposed, indices));
            }
            if (flatten) session.FlattenHeight = height;
            if (edits.Count == 0) return;
            session.Edit(() =>
            {
                foreach (var edit in edits)
                foreach (int index in edit.Indices)
                    edit.Terrain.Vertices[index].Z = edit.Proposed.Vertices[index].Z;
            });
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        { await dialogs.MessageAsync("Terrain vertices", exception.Message); }
        finally
        {
            _updating = false;
            RefreshSelection(session);
        }
    }

    private async Task StitchAsync(EditorSession session, EditorDialogs dialogs, Action finishGestures)
    {
        if (dialogs.BlocksInput) return;
        try
        {
            _updating = true;
            float tolerance = ReadNumber(StitchToleranceBox, "stitch tolerance");
            finishGestures();
            if (session.Selection.Count != 2 || !session.Selection.Items.All(item => item is MapTerrain))
                throw new ArgumentException("Select exactly two whole terrain patches to stitch.");
            MapTerrain[] terrains = session.Selection.Items.Cast<MapTerrain>().ToArray();
            MapTerrain first = terrains[0].Clone(), second = terrains[1].Clone();
            int changed = TerrainEditing.Stitch(first, second, tolerance);
            if (changed == 0)
            {
                await dialogs.MessageAsync("Stitch terrain", "Nothing changed: no adjoining edge matched, or the edge already agrees.");
                return;
            }
            session.Edit(() =>
            {
                terrains[0].Vertices = first.Vertices;
                terrains[0].TextureCoordinates = first.TextureCoordinates;
                terrains[0].Colors = first.Colors;
                terrains[1].Vertices = second.Vertices;
                terrains[1].TextureCoordinates = second.TextureCoordinates;
                terrains[1].Colors = second.Colors;
            });
            await dialogs.MessageAsync("Stitch terrain", $"Stitched the adjoining edge; updated {changed} boundary vertices.");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        { await dialogs.MessageAsync("Stitch terrain", exception.Message); }
        finally
        {
            _updating = false;
            RefreshSelection(session);
        }
    }

    private static float ReadNumber(TextBox input, string name)
    {
        if (!float.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || !float.IsFinite(value))
            throw new ArgumentException($"Enter a finite number for {name}.");
        return value;
    }

    private static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);
}
