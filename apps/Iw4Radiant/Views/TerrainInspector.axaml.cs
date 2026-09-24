using System.Globalization;
using System.Numerics;
using Avalonia.Controls;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Views;

public partial class TerrainInspector : UserControl
{
    private bool _updating;
    private bool _collisionEdited;
    private MapTerrain? _splitTerrain;
    private MapTerrain[] _shownCollisionTerrains = [];
    private bool[] _shownCollisionValues = [];

    public TerrainInspector() => InitializeComponent();

    internal void InitializeActions(EditorSession session, EditorDialogs dialogs, Action finishGestures,
        Action<string> setStatus)
    {
        StitchToleranceBox.Text = Number(session.GridSize);
        TerrainCount.ValueChanged += (_, _) => UpdateSettings(session, dialogs, finishGestures);
        RadiusValue.ValueChanged += (_, _) => UpdateSettings(session, dialogs, finishGestures);
        StrengthValue.ValueChanged += (_, _) => UpdateSettings(session, dialogs, finishGestures);
        SculptModeBox.SelectionChanged += (_, _) => UpdateSettings(session, dialogs, finishGestures);
        FlattenHeightBox.LostFocus += async (_, _) => await UpdateFlattenHeightAsync(session, dialogs, finishGestures);
        SmoothVerticesButton.Click += async (_, _) => await EditVerticesAsync(session, dialogs, finishGestures, flatten: false);
        FlattenVerticesButton.Click += async (_, _) => await EditVerticesAsync(session, dialogs, finishGestures, flatten: true);
        NoiseModeBox.SelectionChanged += (_, _) => UpdateNoiseMode();
        ApplyNoiseButton.Click += async (_, _) => await EditNoiseAsync(session, dialogs, finishGestures, setStatus);
        SplitColumnButton.Click += async (_, _) => await SplitAsync(session, dialogs, finishGestures, true);
        SplitRowButton.Click += async (_, _) => await SplitAsync(session, dialogs, finishGestures, false);
        ThickenTerrainButton.Click += async (_, _) => await ThickenAsync(session, dialogs, finishGestures, setStatus);
        StitchButton.Click += async (_, _) => await StitchAsync(session, dialogs, finishGestures);
        SolidCollisionValue.IsCheckedChanged += (_, _) =>
        {
            if (_updating || dialogs.BlocksInput) return;
            _collisionEdited = true;
            ApplyCollisionButton.IsEnabled = SolidCollisionValue.IsChecked is not null;
            RevertCollisionButton.IsEnabled = true;
        };
        ApplyCollisionButton.Click += async (_, _) => await ApplyCollisionAsync(session, dialogs, finishGestures);
        RevertCollisionButton.Click += (_, _) =>
        {
            if (dialogs.BlocksInput) return;
            _collisionEdited = false;
            RefreshSelection(session);
        };
        RefreshSelection(session);
        UpdateNoiseMode();
    }

    internal void RefreshSelection(EditorSession session)
    {
        if (_updating) return;
        _updating = true;
        try
        {
            bool painting = session.Tool == EditorTool.Sculpt && session.SculptMode is TerrainSculptMode.PaintColor or TerrainSculptMode.PaintAlpha;
            if (!TerrainCount.IsKeyboardFocusWithin) TerrainCount.Value = session.TerrainVertices;
            if (!RadiusValue.IsKeyboardFocusWithin) RadiusValue.Value = (decimal)session.SculptRadius;
            StrengthCaption.Text = session.SculptMode == TerrainSculptMode.RaiseLower ? "Strength (units)" : "Strength (%)";
            StrengthValue.Maximum = session.SculptMode is TerrainSculptMode.Smooth or TerrainSculptMode.Flatten ? 100 : 256;
            if (!StrengthValue.IsKeyboardFocusWithin) StrengthValue.Value = (decimal)session.SculptStrength;
            SculptModeBox.SelectedIndex = (int)session.SculptMode;
            if (!FlattenHeightBox.IsKeyboardFocusWithin) FlattenHeightBox.Text = Number(session.FlattenHeight);
            int count = session.Selection.Items.OfType<TerrainVertexSelection>().Count();
            CreationFields.IsVisible = session.Tool == EditorTool.Terrain;
            SculptFields.IsVisible = session.Tool == EditorTool.Sculpt;
            SculptParameters.IsVisible = !painting;
            HeightFields.IsVisible = !painting && (count > 0 || session.Tool == EditorTool.Sculpt && session.SculptMode == TerrainSculptMode.Flatten);
            VertexFields.IsVisible = !painting && count > 0;
            NoiseFields.IsVisible = !painting && (session.Tool == EditorTool.Vertex || count > 0);
            MapTerrain? split = session.Selection.Count == 1 && session.Selection.Active is MapTerrain { IsCurve: false } surface
                ? surface : null;
            SplitFields.IsVisible = split is not null;
            SplitColumnButton.IsEnabled = split is { Width: >= 3 };
            SplitRowButton.IsEnabled = split is { Height: >= 3 };
            if (split is not null)
            {
                SplitColumn.Maximum = Math.Max(2, split.Width - 1);
                SplitRow.Maximum = Math.Max(2, split.Height - 1);
                if (!ReferenceEquals(split, _splitTerrain))
                {
                    SplitColumn.Value = Math.Clamp((split.Width + 1) / 2, 2, (int)SplitColumn.Maximum);
                    SplitRow.Value = Math.Clamp((split.Height + 1) / 2, 2, (int)SplitRow.Maximum);
                }
            }
            _splitTerrain = split;
            ThickenTerrainFields.IsVisible = split is not null && session.Document.World.Terrains.Contains(split);
            StitchFields.IsVisible = !painting && session.Selection.Items.Any(item => item is MapTerrain { IsCurve: false });
            RefreshCollision(session);
            TerrainContextText.IsVisible = !CreationFields.IsVisible && !SculptFields.IsVisible &&
                !VertexFields.IsVisible && !NoiseFields.IsVisible && !SplitFields.IsVisible &&
                !ThickenTerrainFields.IsVisible && !StitchFields.IsVisible && !CollisionFields.IsVisible;
            VertexSelectionText.Text = $"{count} terrain {(count == 1 ? "vertex" : "vertices")} selected.";
            int editable = session.Selection.Items.OfType<TerrainVertexSelection>()
                .Count(vertex => !session.IsPatchVertexLocked(vertex));
            NoiseSelectionText.Text = count == 0
                ? "Select terrain or curved-patch vertices in Vertex mode. Brush vertices are not supported."
                : editable == 0 ? "Selected patch controls are locked. Unlock them before applying noise."
                : $"{editable} selected terrain/patch {(editable == 1 ? "vertex" : "vertices")} can receive noise.";
            SmoothVerticesButton.IsEnabled = FlattenVerticesButton.IsEnabled = count > 0;
            StitchButton.IsEnabled = session.Selection.Count == 2 && session.Selection.Items.All(item => item is MapTerrain { IsCurve: false });
            StitchSelectionText.IsVisible = !StitchButton.IsEnabled;
            StitchSelectionText.Text = "Select two whole terrain patches to stitch.";
        }
        finally { _updating = false; }
    }

    private void UpdateNoiseMode()
    {
        PositionNoiseFields.IsVisible = NoiseModeBox.SelectedIndex != 1;
        AlphaNoiseFields.IsVisible = NoiseModeBox.SelectedIndex is 1 or 2;
    }

    private void RefreshCollision(EditorSession session)
    {
        MapTerrain[] terrains = session.Selection.Items.OfType<MapTerrain>().Where(terrain => !terrain.IsCurve).ToArray();
        CollisionFields.IsVisible = terrains.Length != 0;
        bool wholeMeshes = terrains.Length != 0 && terrains.Length == session.Selection.Count;
        SolidCollisionValue.IsEnabled = wholeMeshes;
        ApplyCollisionButton.IsEnabled = RevertCollisionButton.IsEnabled = false;
        if (!wholeMeshes)
        {
            _shownCollisionTerrains = [];
            _shownCollisionValues = [];
            _collisionEdited = false;
            SolidCollisionValue.IsChecked = null;
            CollisionSelectionText.Text = "Select whole terrain meshes to edit collision.";
            return;
        }
        try
        {
            bool[] values = terrains.Select(terrain => !TerrainContents.ReadNonColliding(terrain)).ToArray();
            if (!_shownCollisionTerrains.SequenceEqual(terrains) || !_shownCollisionValues.SequenceEqual(values))
                _collisionEdited = false;
            _shownCollisionTerrains = terrains;
            _shownCollisionValues = values;
            bool mixed = values.Any(value => value != values[0]);
            if (!_collisionEdited) SolidCollisionValue.IsChecked = mixed ? null : values[0];
            ApplyCollisionButton.IsEnabled = _collisionEdited && SolidCollisionValue.IsChecked is not null;
            RevertCollisionButton.IsEnabled = _collisionEdited;
            CollisionSelectionText.Text = mixed
                ? "Mixed collision. Choose a value and Apply to all selected meshes."
                : "Apply changes the selected whole meshes. Vertex alpha does not change collision.";
        }
        catch (Exception exception) when (exception is NotSupportedException or InvalidDataException or FormatException)
        {
            _collisionEdited = false;
            SolidCollisionValue.IsChecked = null;
            SolidCollisionValue.IsEnabled = false;
            CollisionSelectionText.Text = exception.Message;
        }
    }

    private async Task ApplyCollisionAsync(EditorSession session, EditorDialogs dialogs, Action finishGestures)
    {
        if (_updating || dialogs.BlocksInput) return;
        try
        {
            if (SolidCollisionValue.IsChecked is not { } solid)
                throw new ArgumentException("Choose whether the selected terrain meshes have solid collision.");
            _updating = true;
            finishGestures();
            MapTerrain[] terrains = session.Selection.Items.OfType<MapTerrain>().Where(terrain => !terrain.IsCurve).ToArray();
            if (terrains.Length == 0 || terrains.Length != session.Selection.Count)
                throw new ArgumentException("Select whole terrain meshes to edit collision.");
            // Validate every selected mesh before starting an undoable edit. The
            // owning directive editor preserves native detail/layer attributes.
            bool[] values = terrains.Select(TerrainContents.ReadNonColliding).ToArray();
            if (values.Any(nonColliding => nonColliding == solid))
                session.Edit(() =>
                {
                    foreach (MapTerrain terrain in terrains)
                        TerrainContents.SetNonColliding(terrain, !solid);
                });
            _collisionEdited = false;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
            NotSupportedException or InvalidDataException or FormatException)
        { await dialogs.MessageAsync("Terrain collision", exception.Message); }
        finally
        {
            _updating = false;
            RefreshSelection(session);
        }
    }

    private void UpdateSettings(EditorSession session, EditorDialogs dialogs, Action finishGestures)
    {
        if (_updating || dialogs.BlocksInput) return;
        _updating = true;
        try
        {
            finishGestures();
            if (TerrainCount.Value is { } count) session.TerrainVertices = Math.Clamp((int)count, 2, 16);
            if (SculptModeBox.SelectedIndex >= 0) session.SculptMode = (TerrainSculptMode)SculptModeBox.SelectedIndex;
            if (session.SculptMode is not (TerrainSculptMode.PaintColor or TerrainSculptMode.PaintAlpha))
            {
                if (RadiusValue.Value is { } radius) session.SculptRadius = (float)radius;
                if (StrengthValue.Value is { } strength) session.SculptStrength = (float)strength;
            }
            if (session.SculptMode is TerrainSculptMode.Smooth or TerrainSculptMode.Flatten)
                session.SculptStrength = Math.Min(session.SculptStrength, 100);
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

    private async Task EditNoiseAsync(EditorSession session, EditorDialogs dialogs, Action finishGestures,
        Action<string> setStatus)
    {
        if (dialogs.BlocksInput) return;
        try
        {
            _updating = true;
            int mode = NoiseModeBox.SelectedIndex;
            if (mode is < 0 or > 2) throw new ArgumentException("Choose a vertex noise mode.");
            Vector3 position = mode == 1 ? Vector3.Zero : new Vector3(
                ReadNoiseAmount(NoiseX, "X noise amount"), ReadNoiseAmount(NoiseY, "Y noise amount"),
                ReadNoiseAmount(NoiseZ, "Z noise amount"));
            float alphaStrength = mode == 0 ? 0 : ReadNoiseAmount(NoiseAlphaStrength, "alpha noise strength") / 100;
            finishGestures();
            TerrainVertexSelection[] selected = session.Selection.Items.OfType<TerrainVertexSelection>()
                .Where(vertex => !session.IsPatchVertexLocked(vertex) &&
                    session.Visibility.CanSelect(session.Document, vertex.Terrain)).ToArray();
            if (selected.Length == 0)
                throw new ArgumentException("Select unlocked terrain or curved-patch vertices in Vertex mode. Brush vertices are not supported.");
            var edits = new List<(MapTerrain Terrain, MapTerrain Proposed, int[] Indices)>();
            foreach (var group in selected.GroupBy(vertex => vertex.Terrain))
            {
                int[] indices = group.Select(vertex => vertex.Index).Distinct().ToArray();
                MapTerrain proposed = group.Key.Clone();
                if (TerrainEditing.NoiseVertices(proposed, indices, position, alphaStrength))
                    edits.Add((group.Key, proposed, indices));
            }
            if (edits.Count == 0)
            {
                setStatus("Vertex noise: nothing changed. Enter a nonzero amount or alpha strength.");
                return;
            }
            session.Edit(() =>
            {
                foreach (var edit in edits)
                foreach (int index in edit.Indices)
                {
                    edit.Terrain.Vertices[index] = edit.Proposed.Vertices[index];
                    if (alphaStrength > 0) edit.Terrain.Colors[index] = edit.Proposed.Colors[index];
                }
            });
            int count = edits.Sum(edit => edit.Indices.Length);
            setStatus($"Applied vertex noise to {count} selected {(count == 1 ? "vertex" : "vertices")}.");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        { await dialogs.MessageAsync("Vertex noise", exception.Message); }
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

    private async Task SplitAsync(EditorSession session, EditorDialogs dialogs, Action finishGestures, bool columns)
    {
        if (dialogs.BlocksInput) return;
        try
        {
            if (session.Selection.Count != 1 || session.Selection.Active is not MapTerrain { IsCurve: false } surface)
                throw new ArgumentException("Select one whole terrain patch to split.");
            finishGestures();
            int seam = (int)((columns ? SplitColumn.Value : SplitRow.Value) ?? 2) - 1;
            GeometryEditing.SplitSurface(session, surface, columns, seam);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        { await dialogs.MessageAsync("Split terrain", exception.Message); }
    }

    private async Task ThickenAsync(EditorSession session, EditorDialogs dialogs, Action finishGestures, Action<string> setStatus)
    {
        if (dialogs.BlocksInput) return;
        try
        {
            if (session.Selection.Count != 1 || session.Selection.Active is not MapTerrain { IsCurve: false } surface)
                throw new ArgumentException("Select one whole terrain patch to thicken.");
            if (TerrainThickenValue.Value is not { } thickness)
                throw new ArgumentException("Enter a thickness in map units.");
            finishGestures();
            int pieces = GeometryEditing.ThickenSurface(session, surface, (float)thickness);
            setStatus($"Terrain thickened behind its front side by {thickness:0} units; {pieces - 1} shell pieces added. Undo removes them.");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException or
            InvalidDataException or NotSupportedException or FormatException)
        { await dialogs.MessageAsync("Thicken terrain", exception.Message); }
    }

    private static float ReadNumber(TextBox input, string name)
    {
        if (!float.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || !float.IsFinite(value))
            throw new ArgumentException($"Enter a finite number for {name}.");
        return value;
    }

    private static float ReadNoiseAmount(NumericUpDown input, string name) => input.Value is { } value
        ? (float)value : throw new ArgumentException($"Enter {name}.");

    private static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);
}
