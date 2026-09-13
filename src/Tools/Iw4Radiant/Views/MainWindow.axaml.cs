using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Views;

public partial class MainWindow : Window
{
    private readonly EditorSession _session = new();
    private readonly EditorDialogs _dialogs;
    private readonly MapFileCommands _files;
    private bool _ready;
    private bool _updatingControls;

    public MainWindow()
    {
        InitializeComponent();
        _dialogs = new EditorDialogs(this, SetStatus);
        _files = new MapFileCommands(this, _session, _dialogs, FinishGestures, FrameAll, SetStatus);
        Inspector.InitializeActions(_session, _dialogs, FinishGestures);
        Materials.InitializeActions(this, _session, _dialogs, FinishGestures, SetStatus);
        Materials.CatalogChanged += Camera.ReloadTextures;
        TopView.Session = FrontView.Session = SideView.Session = _session;
        TopView.CursorStatusChanged += SetStatus;
        FrontView.CursorStatusChanged += SetStatus;
        SideView.CursorStatusChanged += SetStatus;
        var gridViews = new[] { TopView, FrontView, SideView };
        foreach (var view in gridViews)
            view.ClipStarted += () =>
            {
                if (_session.Tool != EditorTool.Clip) return;
                foreach (var other in gridViews)
                    if (!ReferenceEquals(other, view)) other.CancelGesture();
            };
        Camera.Session = _session;
        Camera.InteractionStatusChanged += SetStatus;
        Camera.ResolveTexturePath = Materials.ResolveTexturePath;
        Camera.RendererStatusChanged += (_, _) => Dispatcher.UIThread.Post(UpdateRendererStatus);
        _session.Changed += (_, _) => RefreshEditor();
        GridCombo.ItemsSource = new[] { "1", "2", "4", "8", "16", "32", "64", "128" };
        GridCombo.SelectedItem = "16";
        TransformCombo.ItemsSource = new[] { "Move", "Rotate", "Scale" };
        TransformCombo.SelectedIndex = 0;
        ClipCombo.ItemsSource = new[] { "Split both", "Keep left", "Keep right" };
        ClipCombo.SelectedIndex = 0;
        _ready = true;
        UpdateRendererStatus();
        RefreshEditor();
        SetStatus("Browse an asset folder and choose a material, then draw a brush or terrain in a grid view.");
        Closed += (_, _) => Materials.ReleaseImages();
        AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);
    }

    private void UpdateRendererStatus()
    {
        RendererErrorText.Text = Camera.RendererError;
        RendererErrorPanel.IsVisible = Camera.RendererError is not null;
    }

    private void RefreshEditor()
    {
        if (!_ready) return;
        Title = $"{(_session.IsDirty ? "*" : "")}{Path.GetFileName(_session.FilePath ?? "Untitled.map")} — Iw4Radiant";
        Camera.RefreshScene();
        UndoMenu.IsEnabled = _session.CanUndo;
        RedoMenu.IsEnabled = _session.CanRedo;
        (Button Button, EditorTool Tool)[] tools =
            [(SelectTool, EditorTool.Select), (BrushTool, EditorTool.Brush),
             (TerrainTool, EditorTool.Terrain), (SculptTool, EditorTool.Sculpt),
             (FaceTool, EditorTool.Face), (VertexTool, EditorTool.Vertex), (ClipTool, EditorTool.Clip)];
        foreach (var tool in tools)
            tool.Button.Background = new SolidColorBrush(tool.Tool == _session.Tool
                ? Color.Parse("#675435") : Color.Parse("#36383D"));
        Inspector.RefreshSelection(_session);
        _updatingControls = true;
        TransformCombo.SelectedIndex = (int)_session.TransformMode;
        ClipCombo.SelectedIndex = (int)_session.ClipMode;
        _updatingControls = false;
        int preserved = _session.Document.Entities.Sum(entity => entity.PreservedPrimitives.Count);
        CountText.Text = $"{_session.Document.Brushes.Count()} brushes · {_session.Document.Terrains.Count()} terrain · " +
            $"{_session.Document.Entities.Count} entities · {_session.Selection.Count} selected" +
            (preserved > 0 ? $" · {preserved} preserved primitives" : "");
    }

    private void SetStatus(string message) => StatusText.Text = message;
    private void FinishGestures()
    {
        TopView.CompleteGesture(); FrontView.CompleteGesture(); SideView.CompleteGesture();
        Camera.FinishGesture();
    }
    private void SetTool(EditorTool tool)
    {
        FinishGestures();
        if (_session.Tool != tool)
            _session.Selection.SetRange(_session.Selection.Items.Select(EditorSelection.Owner).Distinct().ToArray());
        _session.Tool = tool;
        _session.Refresh();
        SetStatus(tool switch
        {
            EditorTool.Brush => "Drag in a grid view to create a brush. Base and Depth set the third axis.",
            EditorTool.Terrain => "Drag a rectangle in XY to create terrain. Vertices per side controls the grid.",
            EditorTool.Sculpt => "Select terrain, choose Raise/lower, Smooth or Flatten in the inspector, then drag in XY. Escape cancels.",
            EditorTool.Face => "Click a face in the camera; Shift-click adds faces. Apply materials in the browser and adjust UVs in Surfaces.",
            EditorTool.Vertex => "Select a brush or terrain, then pick vertices. Shift-click adds vertices; drag a handle or use numeric transforms.",
            EditorTool.Clip => "Select brushes, drag a clip line in a grid view, then choose Apply clip or press Enter. Left/right follows the line direction; Escape cancels.",
            _ => "Click to select; Shift-click adds objects. Drag empty grid space for a marquee. Choose Move, Rotate or Scale for the gizmos."
        });
    }
    private void SelectTool_Click(object? sender, RoutedEventArgs e) => SetTool(EditorTool.Select);
    private void BrushTool_Click(object? sender, RoutedEventArgs e) => SetTool(EditorTool.Brush);
    private void TerrainTool_Click(object? sender, RoutedEventArgs e) => SetTool(EditorTool.Terrain);
    private void SculptTool_Click(object? sender, RoutedEventArgs e) => SetTool(EditorTool.Sculpt);
    private void FaceTool_Click(object? sender, RoutedEventArgs e) => SetTool(EditorTool.Face);
    private void VertexTool_Click(object? sender, RoutedEventArgs e) => SetTool(EditorTool.Vertex);
    private void ClipTool_Click(object? sender, RoutedEventArgs e) => SetTool(EditorTool.Clip);
    private void Undo_Click(object? sender, RoutedEventArgs e) { FinishGestures(); _session.Undo(); }
    private void Redo_Click(object? sender, RoutedEventArgs e) { FinishGestures(); _session.Redo(); }
    private async void Duplicate_Click(object? sender, RoutedEventArgs e)
    {
        FinishGestures();
        try { SelectionEditing.Duplicate(_session); }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
        { await _dialogs.MessageAsync("Duplicate selection", exception.Message); }
    }
    private async void Delete_Click(object? sender, RoutedEventArgs e)
    {
        FinishGestures();
        try { SelectionEditing.Delete(_session); }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
        { await _dialogs.MessageAsync("Delete selection", exception.Message); }
    }
    private void World_Click(object? sender, RoutedEventArgs e) { FinishGestures(); _session.Select(_session.Document.World); }
    private void Exit_Click(object? sender, RoutedEventArgs e) => Close();
    private void FrameAll_Click(object? sender, RoutedEventArgs e) => FrameAll();
    private void PreviewLights_Changed(object? sender, RoutedEventArgs e)
    {
        if (_ready)
            Camera.PreviewLighting = PreviewLights.IsChecked == true;
    }
    private void TransformMode_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _updatingControls || TransformCombo.SelectedIndex < 0) return;
        var mode = (TransformMode)TransformCombo.SelectedIndex;
        FinishGestures();
        _session.TransformMode = mode;
        if (_session.Tool is not (EditorTool.Select or EditorTool.Vertex)) SetTool(EditorTool.Select);
        else _session.Refresh();
        SetStatus($"{mode}: drag the colored handles or enter values in Transform. Escape cancels a drag.");
    }
    private void ClipMode_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _updatingControls || ClipCombo.SelectedIndex < 0) return;
        var mode = (ClipMode)ClipCombo.SelectedIndex;
        FinishGestures();
        _session.ClipMode = mode;
        _session.Refresh();
    }
    private void FrameSelection_Click(object? sender, RoutedEventArgs e)
    {
        FinishGestures();
        Camera.FrameSelection(); TopView.FrameSelection(); FrontView.FrameSelection(); SideView.FrameSelection();
    }

    private void ApplyClip_Click(object? sender, RoutedEventArgs e)
    {
        if (_dialogs.BlocksInput) return;
        if (!TopView.CommitClip() && !FrontView.CommitClip() && !SideView.CommitClip())
            SetStatus("Select brushes, choose Clip and drag a clip line in a grid view first.");
    }
    private void FrameAll()
    {
        FinishGestures();
        Camera.FrameAll(); TopView.FrameAll(); FrontView.FrameAll(); SideView.FrameAll();
    }

    private void Grid_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (!_ready || GridCombo.SelectedItem is not string value) return;
        FinishGestures();
        _session.GridSize = float.Parse(value, CultureInfo.InvariantCulture);
        BaseValue.Increment = DepthValue.Increment = (decimal)_session.GridSize;
        _session.Refresh();
    }
    private void ToolSettings_Changed(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (!_ready) return;
        FinishGestures();
        _session.BrushBottom = (float)(BaseValue.Value ?? 0);
        _session.BrushHeight = (float)(DepthValue.Value ?? 64);
        _session.Refresh();
    }
    private void Light_Click(object? sender, RoutedEventArgs e) => AddEntity("light");
    private void Player_Click(object? sender, RoutedEventArgs e) => AddEntity("info_player_start");
    private void AddEntity(string className)
    {
        FinishGestures();
        _session.Edit(() =>
        {
            var entity = new MapEntity();
            entity.Properties["classname"] = className;
            entity.Properties["origin"] = FormattableString.Invariant($"0 0 {_session.BrushBottom + _session.BrushHeight:G9}");
            if (className == "light")
            {
                entity.Properties["def"] = MapLightDefaults.Definition;
                entity.Properties["radius"] = MapLightDefaults.Radius.ToString(CultureInfo.InvariantCulture);
                entity.Properties["intensity"] = MapLightDefaults.Intensity.ToString(CultureInfo.InvariantCulture);
                entity.Properties["_color"] = MapLightDefaults.Color;
            }
            _session.Document.Entities.Add(entity);
            _session.Select(entity);
        });
    }

    private async void New_Click(object? sender, RoutedEventArgs e) => await _files.NewAsync();
    private async void Open_Click(object? sender, RoutedEventArgs e) => await _files.OpenAsync();
    private async void Save_Click(object? sender, RoutedEventArgs e) => await _files.SaveAsync(false);
    private async void SaveAs_Click(object? sender, RoutedEventArgs e) => await _files.SaveAsync(true);

    private async void Help_Click(object? sender, RoutedEventArgs e) => await _dialogs.MessageAsync("Iw4Radiant controls",
        "Q: objects · S: faces · E: vertices · C: clipper · B: brush · T: terrain · V: sculpt\n" +
        "Selection: Shift-click adds/removes items. Drag empty grid space for a marquee. Choose Move/Rotate/Scale, then drag gizmos or enter numeric values in Transform.\n" +
        "Surfaces: choose Face and click in the camera. The material browser applies to selected faces; Surfaces adjusts repeat size, shift, rotation, skew and Fit. Texture lock follows brush transforms.\n" +
        "Vertices: select an object, then its vertex handles. Shift-click adds vertices. Invalid/collapsed brush edits are rejected.\n" +
        "Clipper: select brushes, drag a line in a grid view, choose Split/Keep left/Keep right, then Apply clip or Enter. Escape discards the line.\n" +
        "Terrain: create/sculpt in XY; choose Raise/lower, Smooth or Flatten. Shift lowers. Select vertices for exact Smooth/Flatten, or two whole patches to Stitch their adjoining edges.\n" +
        "Camera: right drag orbits, middle drag pans, wheel zooms. Click geometry to select. F frames selection.\n" +
        "Lights: use the Light inspector for color, radius and intensity. Create an aim target for a spotlight and move that target to aim it; inner/outer FOV and exponent control its cone. Preview lights includes surface shadows.\n" +
        "Space duplicates; Delete removes; Ctrl/Cmd+Z undoes; Ctrl/Cmd+Shift+Z redoes.\n\n" +
        "This prototype edits iwmap 4 source. Curves and other unsupported primitives are preserved but not rendered. " +
        "IW4 material JSON color maps, DDS and PNG/JPEG/BMP previews are supported; PS3 material programs are not executed. " +
        "Materials without a matching image are omitted. Unresolved map surfaces appear as wireframe. " +
        "Lighting previews light_point_linear point/spot lights. Custom falloff assets, sunlight and bounced light are not previewed. " +
        "Integrated geometry/lighting compilation and IW4 .d3dbsp/.ff export are not implemented yet.");

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (_dialogs.BlocksInput || e.Handled) return;
        bool command = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (command && e.Key is Key.N or Key.O or Key.S)
        {
            e.Handled = true;
            if (e.Key == Key.N) New_Click(this, e);
            else if (e.Key == Key.O) Open_Click(this, e);
            else if (shift) SaveAs_Click(this, e);
            else Save_Click(this, e);
            return;
        }
        if (e.Source is Control focused && IsTextEntry(focused)) return;
        if (command && e.Key == Key.Z) { if (shift) Redo_Click(this, e); else Undo_Click(this, e); e.Handled = true; return; }
        if (command && e.Key == Key.Y) { Redo_Click(this, e); e.Handled = true; return; }
        if (command || e.KeyModifiers.HasFlag(KeyModifiers.Alt)) return;
        if (e.Key == Key.Space && e.Source is Control source && IsButtonInput(source)) return;
        switch (e.Key)
        {
            case Key.Q: SetTool(EditorTool.Select); break;
            case Key.B: SetTool(EditorTool.Brush); break;
            case Key.T: SetTool(EditorTool.Terrain); break;
            case Key.V: SetTool(EditorTool.Sculpt); break;
            case Key.S: SetTool(EditorTool.Face); break;
            case Key.E: SetTool(EditorTool.Vertex); break;
            case Key.C: SetTool(EditorTool.Clip); break;
            case Key.Delete: case Key.Back: Delete_Click(this, e); break;
            case Key.Space: Duplicate_Click(this, e); break;
            default: return;
        }
        e.Handled = true;
    }
    private static bool IsTextEntry(Control control)
    {
        for (Control? current = control; current is not null; current = current.Parent as Control)
            if (current is TextBox or NumericUpDown or ComboBox) return true;
        return false;
    }
    private static bool IsButtonInput(Control control)
    {
        for (Control? current = control; current is not null; current = current.Parent as Control)
            if (current is Button or Slider) return true;
        return false;
    }
}
