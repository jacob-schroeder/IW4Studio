using System.Globalization;
using System.Numerics;
using Avalonia.Controls;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;
using Iw4Radiant.Viewports.Orthographic;

namespace Iw4Radiant.Views;

public partial class GeometryInspector : UserControl
{
    private bool _updating;
    private bool _ropeReady;
    private MapTerrain? _patch;
    private Func<OrthoPlane> _editPlane = () => OrthoPlane.Top;

    public GeometryInspector() => InitializeComponent();

    internal void InitializeActions(EditorSession session, EditorDialogs dialogs, Action finishGestures,
        Func<OrthoPlane> editPlane, Action<string> setStatus)
    {
        _editPlane = editPlane;
        InitializeBridge(session, dialogs, finishGestures, setStatus);
        ShapeBox.SelectionChanged += (_, _) => RefreshSelection(session);
        UseBoundsButton.Click += (_, _) =>
        {
            if (dialogs.BlocksInput || session.SelectionBounds is not { } bounds) return;
            Vector3 center = bounds.Min + (bounds.Max - bounds.Min) / 2;
            Vector3 size = LocalSize(bounds.Max - bounds.Min);
            CenterX.Text = Number(center.X); CenterY.Text = Number(center.Y); CenterZ.Text = Number(center.Z);
            SizeX.Text = Number(Math.Max(size.X, session.GridSize));
            SizeY.Text = Number(Math.Max(size.Y, session.GridSize));
            SizeZ.Text = Number(Math.Max(size.Z, session.GridSize));
        };
        CreateButton.Click += async (_, _) => await RunAsync(dialogs, finishGestures, () =>
            GeometryEditing.Create(session, Shape, ReadVector(CenterX, CenterY, CenterZ), ReadVector(SizeX, SizeY, SizeZ),
                (int)(SegmentsValue.Value ?? 8), Shape == GeometryShape.Arch ? ReadNumber(ThicknessBox) : 0,
                Orientation(), ReplaceBrushBox.IsChecked == true));
        RefineColumnsButton.Click += async (_, _) => await RunAsync(dialogs, finishGestures,
            () => GeometryEditing.EditPatches(session, patch => PatchGeometry.Refine(patch, columns: true)));
        RefineRowsButton.Click += async (_, _) => await RunAsync(dialogs, finishGestures,
            () => GeometryEditing.EditPatches(session, patch => PatchGeometry.Refine(patch, columns: false)));
        InvertButton.Click += async (_, _) => await RunAsync(dialogs, finishGestures,
            () => GeometryEditing.EditPatches(session, PatchGeometry.Invert));
        CapButton.Click += async (_, _) => await RunAsync(dialogs, finishGestures, () => GeometryEditing.CapEnds(session));
        SplitColumnsButton.Click += async (_, _) => await RunAsync(dialogs, finishGestures, () =>
            GeometryEditing.SplitSurface(session, _patch ?? throw new ArgumentException("Select one curved patch to split."),
                columns: true, (int)(SplitColumnSpan.Value ?? 1)));
        SplitRowsButton.Click += async (_, _) => await RunAsync(dialogs, finishGestures, () =>
            GeometryEditing.SplitSurface(session, _patch ?? throw new ArgumentException("Select one curved patch to split."),
                columns: false, (int)(SplitRowSpan.Value ?? 1)));
        ConvertCurveButton.Click += async (_, _) => await RunAsync(dialogs, finishGestures, () =>
        {
            MapTerrain curve = session.Selection.Count == 1 && session.Selection.Active is MapTerrain { IsCurve: true } selected
                ? selected : throw new ArgumentException("Select one whole curve to convert.");
            int steps = CurveDetailBox.SelectedIndex switch { 0 => 4, 1 => 8, 2 => 16, _ => throw new ArgumentException("Choose curve detail.") };
            int count = GeometryEditing.ConvertCurveToTerrain(session, curve, steps);
            setStatus($"Curve converted to {count} terrain {(count == 1 ? "patch" : "tiles")}. Undo restores the curve.");
        });
        ThickenCurveButton.Click += async (_, _) => await RunAsync(dialogs, finishGestures, () =>
        {
            MapTerrain curve = session.Selection.Count == 1 && session.Selection.Active is MapTerrain { IsCurve: true } selected
                ? selected : throw new ArgumentException("Select one whole curve to thicken.");
            if (CurveThickenValue.Value is not { } thickness)
                throw new ArgumentException("Enter a thickness in map units.");
            int pieces = GeometryEditing.ThickenSurface(session, curve, (float)thickness);
            setStatus($"Curve thickened into {pieces} terrain pieces. Undo restores its editable controls.");
        });
        CreateRopeButton.Click += async (_, _) => await RunAsync(dialogs, finishGestures, () =>
        {
            if (RopeThickness.Value is not { } thickness || RopeSlack.Value is not { } slack ||
                RopeSegments.Value is not { } segments)
                throw new ArgumentException("Enter rope thickness, slack, and detail.");
            int pieces = GeometryEditing.CreateRope(session, (float)thickness, (float)slack, (int)segments);
            setStatus($"Created rope as {pieces} editable terrain {(pieces == 1 ? "piece" : "pieces")}. Undo restores the endpoint markers.");
        });
        ColumnValue.ValueChanged += (_, _) => { if (!_updating) ShowControl(); };
        RowValue.ValueChanged += (_, _) => { if (!_updating) ShowControl(); };
        SelectPointButton.Click += (_, _) => SelectControls(session, dialogs, finishGestures, row: false, column: false);
        SelectAllButton.Click += (_, _) => SelectControls(session, dialogs, finishGestures, row: true, column: true);
        SelectRowButton.Click += (_, _) => SelectControls(session, dialogs, finishGestures, row: true, column: false);
        SelectColumnButton.Click += (_, _) => SelectControls(session, dialogs, finishGestures, row: false, column: true);
        ApplyPointButton.Click += async (_, _) =>
        {
            if (dialogs.BlocksInput) return;
            try
            {
                Vector3 position = ReadVector(PointX, PointY, PointZ);
                int index = ControlIndex;
                await RunAsync(dialogs, finishGestures, () =>
                {
                    if (_patch is not { } patch || (uint)index >= (uint)patch.Vertices.Length || position == patch.Vertices[index]) return;
                    session.Edit(() =>
                    {
                        patch.Vertices[index] = position;
                        session.Selection.Set(new TerrainVertexSelection(patch, index));
                        session.Tool = EditorTool.Vertex;
                    });
                });
            }
            catch (ArgumentException exception) { await dialogs.MessageAsync("Geometry", exception.Message); }
        };
        RefreshSelection(session);
    }

    internal void RefreshSelection(EditorSession session)
    {
        if (_updating) return;
        _updating = true;
        try
        {
            RefreshBridge(session);
            PlaneText.Text = _editPlane() switch
            {
                OrthoPlane.Top => "Top view: width X · depth Y · height Z.",
                OrthoPlane.Front => "Front view: width X · depth Z · height Y.",
                _ => "Side view: width Y · depth Z · height X."
            };
            SegmentFields.IsVisible = Shape is GeometryShape.Arch or GeometryShape.Stairs;
            SegmentCaption.Text = Shape == GeometryShape.Stairs ? "Steps" : "Arch segments";
            ArchFields.IsVisible = Shape == GeometryShape.Arch;
            ShapeHint.Text = Shape switch
            {
                GeometryShape.Patch => "Creates a 3 × 3 control grid centered in the current view plane. Move its controls to bend the surface.",
                GeometryShape.Arch => "Creates a solid half arch from convex brushes. Width spans the opening; height is the outer arch rise.",
                GeometryShape.Stairs => "Creates solid steps rising along the width axis. Rise = height ÷ steps; run = width ÷ steps.",
                _ => "Creates an editable quadratic curve using the browser's current material. Select it and use Cap ends to close the shape."
            };
            CreateButton.Content = Shape switch
            {
                GeometryShape.Patch => "Create patch", GeometryShape.Bevel => "Create bevel", GeometryShape.EndCap => "Create end cap",
                GeometryShape.Cylinder => "Create cylinder", GeometryShape.Arch => "Create arch", _ => "Create stairs"
            };
            UseBoundsButton.IsEnabled = session.SelectionBounds is not null;
            BrushExtendGuide.IsVisible = session.Selection.Count == 1 && session.Selection.Active is MapBrush;
            ReplaceBrushBox.IsEnabled = session.Selection.Count == 1 && session.Selection.Active is MapBrush;
            if (!ReplaceBrushBox.IsEnabled) ReplaceBrushBox.IsChecked = false;
            bool markersSelected = session.Selection.Count == 2 && session.Selection.Items.All(item =>
                item is MapEntity { ClassName: "info_null" } entity && entity.TryGetOrigin(out _));
            bool ropeReady = markersSelected && session.Selection.Items.Cast<MapEntity>().All(entity =>
                entity.Brushes.Count == 0 && entity.Terrains.Count == 0 &&
                entity.Properties.Keys.All(key => key is "classname" or "origin" or "angles" or "angle"));
            if (ropeReady && !_ropeReady) RopePanel.IsExpanded = true;
            _ropeReady = ropeReady;
            RopeReadyText.Text = !markersSelected ? "Select exactly two info_null markers to set endpoints." :
                !ropeReady ? "These markers are named, linked, or own geometry. Place two temporary markers instead." :
                string.IsNullOrWhiteSpace(session.Material) ? "Choose a material in the browser." :
                $"Ready · {session.Material}";
            CreateRopeButton.IsEnabled = ropeReady && !string.IsNullOrWhiteSpace(session.Material);
            MapTerrain[] patches = GeometryEditing.SelectedPatches(session);
            RefineColumnsButton.IsEnabled = patches.All(item => item.Width <= 7);
            RefineRowsButton.IsEnabled = patches.All(item => item.Height <= 7);
            PatchFields.IsVisible = patches.Length > 0;
            ConvertCurveFields.IsVisible = session.Selection.Count == 1 && session.Selection.Active is MapTerrain { IsCurve: true };
            ThickenCurveFields.IsVisible = session.Selection.Count == 1 &&
                session.Selection.Active is MapTerrain { IsCurve: true } curve && session.Document.World.Terrains.Contains(curve);
            SelectionHint.IsVisible = patches.Length == 0;
            ControlFields.IsVisible = patches.Length == 1;
            SplitColumnsButton.IsEnabled = SplitRowsButton.IsEnabled = patches.Length == 1;
            MapTerrain? patch = patches.Length == 1 ? patches[0] : null;
            bool changed = !ReferenceEquals(_patch, patch);
            _patch = patch;
            PatchSummary.Text = patch is not null ? $"{patch.Width} columns × {patch.Height} rows of curve controls." :
                $"{patches.Length} curved patches selected.";
            if (patch is not null)
            {
                SplitColumnSpan.Maximum = (patch.Width - 1) / 2;
                SplitRowSpan.Maximum = (patch.Height - 1) / 2;
                if (changed)
                {
                    SplitColumnSpan.Value = (SplitColumnSpan.Maximum + 1) / 2;
                    SplitRowSpan.Value = (SplitRowSpan.Maximum + 1) / 2;
                }
                ColumnValue.Maximum = patch.Width;
                RowValue.Maximum = patch.Height;
                if (session.Selection.Count == 1 && session.Selection.Active is TerrainVertexSelection point)
                {
                    ColumnValue.Value = point.Index / patch.Height + 1;
                    RowValue.Value = point.Index % patch.Height + 1;
                }
                else if (changed) { ColumnValue.Value = 1; RowValue.Value = 1; }
                if (!PointX.IsKeyboardFocusWithin && !PointY.IsKeyboardFocusWithin && !PointZ.IsKeyboardFocusWithin)
                    ShowControl();
            }
        }
        finally { _updating = false; }
    }

    internal void ShowBridge() => BridgePanel.IsExpanded = true;

    private GeometryShape Shape => (GeometryShape)Math.Max(0, ShapeBox.SelectedIndex);
    private int ControlIndex => ((int)(ColumnValue.Value ?? 1) - 1) * (_patch?.Height ?? 3) + (int)(RowValue.Value ?? 1) - 1;

    private void ShowControl()
    {
        if (_patch is not { } patch || (uint)ControlIndex >= (uint)patch.Vertices.Length) return;
        Vector3 position = patch.Vertices[ControlIndex];
        PointX.Text = Number(position.X); PointY.Text = Number(position.Y); PointZ.Text = Number(position.Z);
    }

    private void SelectControls(EditorSession session, EditorDialogs dialogs, Action finishGestures, bool row, bool column)
    {
        if (dialogs.BlocksInput || _patch is null) return;
        finishGestures();
        if (_patch is not { } patch) return;
        int x = ControlIndex / patch.Height, y = ControlIndex % patch.Height;
        session.Tool = EditorTool.Vertex;
        session.SelectRange(Enumerable.Range(0, patch.Vertices.Length)
            .Where(index => (row || index / patch.Height == x) && (column || index % patch.Height == y))
            .Select(index => new TerrainVertexSelection(patch, index)));
    }

    private async Task RunAsync(EditorDialogs dialogs, Action finishGestures, Action action)
    {
        if (_updating || dialogs.BlocksInput) return;
        try
        {
            finishGestures();
            action();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException or
            InvalidDataException or NotSupportedException or FormatException)
        { await dialogs.MessageAsync("Geometry", exception.Message); }
    }

    private Vector3 LocalSize(Vector3 world) => _editPlane() switch
    {
        OrthoPlane.Top => world, OrthoPlane.Front => new(world.X, world.Z, world.Y), _ => new(world.Y, world.Z, world.X)
    };

    private Matrix4x4 Orientation() => _editPlane() switch
    {
        OrthoPlane.Top => Matrix4x4.Identity,
        OrthoPlane.Front => new(1, 0, 0, 0, 0, 0, 1, 0, 0, -1, 0, 0, 0, 0, 0, 1),
        _ => new(0, 1, 0, 0, 0, 0, 1, 0, 1, 0, 0, 0, 0, 0, 0, 1)
    };

    private static Vector3 ReadVector(TextBox x, TextBox y, TextBox z) => new(ReadNumber(x), ReadNumber(y), ReadNumber(z));

    private static float ReadNumber(TextBox box) =>
        float.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) && float.IsFinite(value) ? value :
            throw new ArgumentException("Enter a finite number in each geometry field.");

    private static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);
}
