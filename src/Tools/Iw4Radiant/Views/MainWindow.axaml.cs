using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;
using Iw4Radiant.Viewports.Orthographic;

namespace Iw4Radiant.Views;

public partial class MainWindow : Window
{
    private readonly EditorSession _session = new();
    private readonly EditorDialogs _dialogs;
    private readonly MapFileCommands _files;
    private bool _ready;
    private bool _updatingControls;
    private bool _inspectorVisible = true;
    private GridLength _inspectorWidth = new(300);
    private EditorTool _shownTool;

    public MainWindow()
    {
        InitializeComponent();
        _dialogs = new EditorDialogs(this, SetStatus);
        _files = new MapFileCommands(this, _session, _dialogs, FinishGestures, FrameAll, SetStatus);
        Inspector.InitializeActions(_session, _dialogs, FinishGestures, Workspace.Materials,
            () => Workspace.ActivePlane, name => ResolveMaterial(name)?.Surface.SupportsAlpha == true,
            name => ResolveMaterial(name)?.UsesVertexColor == true);
        Workspace.InitializeActions(_dialogs, FinishGestures);
        Workspace.LayoutChanged += RefreshLayoutControls;
        Workspace.Materials.InitializeActions(this, _session, _dialogs, FinishGestures, SetStatus);
        InitializeAuthoring();
        var gridViews = Workspace.GridViews;
        foreach (var view in gridViews)
        {
            view.Session = _session;
            view.CursorStatusChanged += SetStatus;
            view.BrushKindRequested += ApplyBrushKind;
            view.EntityInspectorRequested += entity =>
            {
                if (!ReferenceEquals(_session.Selection.Active, entity)) _session.Select(entity);
                if (entity.ClassName == "func_group") Inspector.ShowOrganization();
                else if (entity.ClassName == "misc_prefab") Workspace.ShowPrefabs();
                else Inspector.ShowEntity();
            };
            view.ModelsRequested += Workspace.ShowModels;
            view.PrefabsRequested += Workspace.ShowPrefabs;
            view.OrganizationRequested += Inspector.ShowOrganization;
            view.ClipPreviewChanged += RefreshClipControls;
            view.ClipStarted += () =>
            {
                if (_session.Tool != EditorTool.Clip) return;
                foreach (var other in gridViews)
                    if (!ReferenceEquals(other, view)) other.CancelGesture();
            };
        }
        Workspace.Camera.Session = _session;
        Workspace.Camera.InteractionStatusChanged += SetStatus;
        Workspace.Camera.BrushKindRequested += ApplyBrushKind;
        Workspace.Camera.ResolveMaterial = ResolveMaterial;
        _session.Changed += (_, _) => RefreshEditor();
        GridCombo.ItemsSource = GridSizes;
        GridCombo.SelectedItem = "16";
        TransformCombo.ItemsSource = new[] { "Move", "Rotate", "Scale" };
        TransformCombo.SelectedIndex = 0;
        ClipCombo.ItemsSource = new[] { "Split both", "Keep left", "Keep right" };
        ClipCombo.SelectedIndex = 0;
        _ready = true;
        RefreshLayoutControls();
        RefreshEditor();
        SetStatus("Browse an asset folder and choose a material, then draw a brush or terrain in a grid view.");
        Closed += (_, _) => { Inspector.ReleaseImages(); Workspace.Materials.ReleaseImages(); Workspace.Models.ReleaseImages(); };
        Deactivated += (_, _) => Workspace.Camera.FinishGesture(cancel: true);
        AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);
    }

    private void RefreshEditor()
    {
        if (!_ready) return;
        if (_shownTool != _session.Tool)
        {
            _shownTool = _session.Tool;
            if (_shownTool is EditorTool.Terrain or EditorTool.Sculpt) Workspace.ShowGrid(OrthoPlane.Top);
        }
        Title = $"{(_session.IsDirty ? "*" : "")}{Path.GetFileName(_session.FilePath ?? "Untitled.map")} — Iw4Radiant";
        Workspace.Camera.RefreshScene();
        UndoMenu.IsEnabled = UndoToolbar.IsEnabled = _session.CanUndo;
        RedoMenu.IsEnabled = RedoToolbar.IsEnabled = _session.CanRedo;
        ApplyPlayerClipMenu.IsEnabled = PlayerClipEditing.CanApply(_session);
        (ToggleButton Button, EditorTool Tool)[] tools =
            [(TerrainTool, EditorTool.Terrain), (SculptTool, EditorTool.Sculpt),
             (FaceTool, EditorTool.Face), (VertexTool, EditorTool.Vertex), (ClipTool, EditorTool.Clip)];
        foreach (var tool in tools)
            tool.Button.IsChecked = tool.Tool == _session.Tool;
        CreationOptions.IsVisible = _session.Tool is EditorTool.Terrain or EditorTool.Select;
        CreationOptions.IsEnabled = _session.Tool == EditorTool.Terrain ||
            _session.SelectionVolumeMode != SelectionVolumeMode.None || _session.Selection.Count == 0;
        CreationHint.Text = _session.SelectionVolumeMode switch
        {
            SelectionVolumeMode.CompleteTall => "Drag to select objects fully inside the projected rectangle · Esc cancels",
            SelectionVolumeMode.PartialTall => "Drag to select objects touching the projected rectangle · Esc cancels",
            SelectionVolumeMode.Touching => "Base and Depth define the finite volume · drag to select touching objects",
            SelectionVolumeMode.Inside => "Base and Depth define the finite volume · drag to select contained objects",
            _ => "Drag to preview · release to create · Esc cancels"
        };
        ClipOptions.IsVisible = _session.Tool == EditorTool.Clip;
        ToolOptions.IsVisible = CreationOptions.IsVisible || ClipOptions.IsVisible;
        RefreshClipControls();
        RefreshPhaseBControls();
        Inspector.RefreshSelection(_session);
        Workspace.Prefabs.RefreshSelection(_session);
        _updatingControls = true;
        TransformCombo.SelectedIndex = (int)_session.TransformMode;
        ClipCombo.SelectedIndex = (int)_session.ClipMode;
        _updatingControls = false;
        int preserved = _session.Document.Entities.Sum(entity => entity.PreservedPrimitives.Count);
        CountText.Text = $"{_session.Document.Brushes.Count()} brushes · {_session.Document.Terrains.Count(t => !t.IsCurve)} terrain · " +
            $"{_session.Document.Terrains.Count(t => t.IsCurve)} curves · " +
            $"{_session.Document.Entities.Count} entities · {_session.Selection.Count} selected" +
            (preserved > 0 ? $" · {preserved} preserved primitives" : "");
    }

    private void SetStatus(string message) => StatusText.Text = message;
    private void FinishGestures()
    {
        foreach (var view in Workspace.GridViews) view.CompleteGesture();
        Workspace.Camera.FinishGesture();
        if (_session.SelectionVolumeMode != SelectionVolumeMode.None)
        {
            _session.SelectionVolumeMode = SelectionVolumeMode.None;
            _session.Refresh();
        }
    }
    private void SetTool(EditorTool tool)
    {
        FinishGestures();
        if (_session.HasPlacement) _session.CancelPlacement();
        if (tool != EditorTool.Select) _session.SelectionVolumeMode = SelectionVolumeMode.None;
        if (_session.Tool != tool)
            _session.Selection.SetRange(_session.Selection.Items.Select(EditorSelection.Owner).Distinct().ToArray());
        _session.Tool = tool;
        if (tool is EditorTool.Terrain or EditorTool.Sculpt) Workspace.ShowGrid(OrthoPlane.Top);
        else if (tool == EditorTool.Clip) Workspace.ShowGrid(Workspace.ActivePlane);
        else if (tool == EditorTool.Face) Workspace.ShowCamera();
        _session.Refresh();
        Inspector.ShowTool(tool);
        SetStatus(tool switch
        {
            EditorTool.Terrain => "Drag a rectangle in XY to create terrain. Vertices per side controls the grid.",
            EditorTool.Sculpt => "Select terrain, choose Raise/lower, Smooth or Flatten in the inspector, then drag in XY. Escape cancels.",
            EditorTool.Face => "Shift-click a face in the camera to select or deselect it. Apply materials in the browser and adjust UVs in Surface.",
            EditorTool.Vertex => "Shift-click vertices to select or deselect them; drag a selected handle or use numeric transforms.",
            EditorTool.Clip => "Select brushes, drag a clip line in a grid view, then choose Apply clip or press Enter. Left/right follows the line direction; Escape cancels.",
            _ => "Drag in a grid to create a brush when nothing is selected. Shift-click or Shift-drag to select/deselect; Esc clears selection."
        });
    }
    private void TerrainTool_Click(object? sender, RoutedEventArgs e) => ToggleTool(EditorTool.Terrain);
    private void SculptTool_Click(object? sender, RoutedEventArgs e) => ToggleTool(EditorTool.Sculpt);
    private void FaceTool_Click(object? sender, RoutedEventArgs e) => ToggleTool(EditorTool.Face);
    private void VertexTool_Click(object? sender, RoutedEventArgs e) => ToggleTool(EditorTool.Vertex);
    private void ClipTool_Click(object? sender, RoutedEventArgs e) => ToggleTool(EditorTool.Clip);
    private void ToggleTool(EditorTool tool) => SetTool(_session.Tool == tool ? EditorTool.Select : tool);
    private void ActivateTool(EditorTool tool)
    {
        if (_session.Tool != tool || _session.HasPlacement) SetTool(tool);
    }
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
    private void World_Click(object? sender, RoutedEventArgs e)
    {
        FinishGestures();
        _session.Select(_session.Document.World);
        Inspector.ShowEntity();
    }
    private void Exit_Click(object? sender, RoutedEventArgs e) => Close();
    private void FrameAll_Click(object? sender, RoutedEventArgs e) => FrameAll();
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
        Workspace.Camera.FrameSelection();
        foreach (var view in Workspace.GridViews) view.FrameSelection();
    }

    private void ApplyClip_Click(object? sender, RoutedEventArgs e)
    {
        if (_dialogs.BlocksInput) return;
        if (!Workspace.GridViews.Any(view => view.CommitClip()))
            SetStatus("Select brushes, choose Clip and drag a clip line in a grid view first.");
    }
    private void CancelClip_Click(object? sender, RoutedEventArgs e)
    {
        if (_dialogs.BlocksInput) return;
        foreach (var view in Workspace.GridViews) view.CancelGesture();
        SetStatus("Clip preview cancelled.");
    }
    private void RefreshClipControls()
    {
        if (!_ready) return;
        bool ready = Workspace.GridViews.Any(view => view.HasClipPreview);
        ApplyClipButton.IsEnabled = CancelClipButton.IsEnabled = ready;
        ClipHint.Text = ready ? "Preview ready · Enter applies · Esc cancels" : "Drag a clip line in a grid view";
    }
    private void FrameAll()
    {
        FinishGestures();
        Workspace.Camera.FrameAll();
        foreach (var view in Workspace.GridViews) view.FrameAll();
    }

    private void RefreshLayoutControls()
    {
        if (!_ready) return;
        bool showInspector = _inspectorVisible && !Workspace.IsMaximized;
        if (Inspector.IsVisible) _inspectorWidth = EditorArea.ColumnDefinitions[2].Width;
        Inspector.IsVisible = InspectorSplitter.IsVisible = showInspector;
        EditorArea.ColumnDefinitions[1].Width = new GridLength(showInspector ? 5 : 0);
        EditorArea.ColumnDefinitions[2].MinWidth = showInspector ? 280 : 0;
        EditorArea.ColumnDefinitions[2].Width = showInspector ? _inspectorWidth : new GridLength(0);
        FourViewsButton.IsChecked = Workspace.FourViews;
        MaximizeButton.IsChecked = Workspace.IsMaximized;
        MaterialsButton.IsChecked = Workspace.MaterialsVisible && !Workspace.IsMaximized;
        InspectorButton.IsChecked = showInspector;
    }

    private void FourViews_Click(object? sender, RoutedEventArgs e) => Workspace.SetFourViews(FourViewsButton.IsChecked == true);
    private void TwoViews_Click(object? sender, RoutedEventArgs e) => Workspace.SetFourViews(false);
    private void FourViewsMenu_Click(object? sender, RoutedEventArgs e) => Workspace.SetFourViews(true);
    private void Maximize_Click(object? sender, RoutedEventArgs e) => Workspace.ToggleMaximize();
    private void Materials_Click(object? sender, RoutedEventArgs e) => Workspace.ToggleMaterials();
    private void Models_Click(object? sender, RoutedEventArgs e) => Workspace.ShowModels();
    private void Prefabs_Click(object? sender, RoutedEventArgs e) => Workspace.ShowPrefabs();
    private void Geometry_Click(object? sender, RoutedEventArgs e) => ShowInspectorSection(Inspector.ShowGeometry);
    private void Gameplay_Click(object? sender, RoutedEventArgs e) => ShowInspectorSection(Inspector.ShowEntity);
    private void Organization_Click(object? sender, RoutedEventArgs e) => ShowInspectorSection(Inspector.ShowOrganization);

    private void ShowInspectorSection(Action show)
    {
        if (_dialogs.BlocksInput) return;
        FinishGestures();
        _inspectorVisible = true;
        if (Workspace.IsMaximized) Workspace.ToggleMaximize();
        RefreshLayoutControls();
        show();
    }

    private void Environment_Click(object? sender, RoutedEventArgs e)
    {
        if (_dialogs.BlocksInput) return;
        FinishGestures();
        _inspectorVisible = true;
        if (Workspace.IsMaximized) Workspace.ToggleMaximize();
        RefreshLayoutControls();
        Inspector.ShowEnvironment();
    }

    private void Inspector_Click(object? sender, RoutedEventArgs e)
    {
        if (_dialogs.BlocksInput) return;
        FinishGestures();
        _inspectorVisible = Workspace.IsMaximized || !_inspectorVisible;
        if (Workspace.IsMaximized) Workspace.ToggleMaximize();
        RefreshLayoutControls();
        Workspace.FocusActiveView();
    }

    private void Plane_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string name } && Enum.TryParse(name, out OrthoPlane plane)) Workspace.ShowGrid(plane);
    }

    private void Grid_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (!_ready || GridCombo.SelectedItem is not string value) return;
        FinishGestures();
        _session.GridSize = float.Parse(value, CultureInfo.InvariantCulture);
        BaseValue.Increment = DepthValue.Increment = (decimal)_session.GridSize;
        _session.Refresh();
        SetStatus($"Grid: {value} · [ decreases · ] increases");
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
        GameplayEntityEditing.Place(_session, className,
            new System.Numerics.Vector3(0, 0, _session.BrushBottom + _session.BrushHeight));
        Inspector.ShowEntity();
    }

    private async void New_Click(object? sender, RoutedEventArgs e) => await _files.NewAsync();
    private async void Open_Click(object? sender, RoutedEventArgs e) => await _files.OpenAsync();
    private async void Save_Click(object? sender, RoutedEventArgs e) => await _files.SaveAsync(false);
    private async void SaveAs_Click(object? sender, RoutedEventArgs e) => await _files.SaveAsync(true);

    private async void Help_Click(object? sender, RoutedEventArgs e) => await _dialogs.MessageAsync("Iw4Radiant controls",
        "Workspace: use View for two/four views and XY/XZ/YZ. Ctrl/Cmd+Tab cycles the 2D plane; Ctrl/Cmd+Space maximizes/restores the active view. Toolbar toggles show materials and the inspector. Drag dividers to resize.\n" +
        "Inspector: Selection, Surface and Entity tabs keep related controls together. Terrain appears for terrain tools or selections. Revert discards un-applied field changes; Apply edits the map.\n" +
        "Q/Esc: default brush workflow · S: faces · E: vertices · X: clipper · T: terrain · V: sculpt. Click an active tool again to leave it.\n" +
        "Selection: Shift-click selects/deselects. Shift-drag paints selection or deselection, starting with the first object. Plain left-drag draws a brush when nothing is selected; otherwise it moves the selected geometry. Escape clears selection.\n" +
        "Surfaces: choose Face and Shift-click in the camera. Surface adjusts horizontal/vertical shift and repeat size, rotation, skew and Fit. Texture lock follows brush transforms.\n" +
        "Vertices: Shift-click an object's vertex handles. Drag selected handles to edit. Invalid/collapsed brush edits are rejected.\n" +
        "Clipper: select brushes, drag a line in a grid view, choose Split/Keep left/Keep right, then Apply or Enter. Cancel or Escape discards the preview.\n" +
        "Terrain: create/sculpt in XY; choose Raise/lower, Smooth or Flatten. Shift lowers. Select vertices for exact Smooth/Flatten, or two whole patches to Stitch their adjoining edges.\n" +
        "Camera: Shift-click selects; right-click lists overlapping objects and their materials. Right-drag orbits; Shift+right-drag or middle-drag pans; scroll zooms. Hold right and use WASD to move, Q/E down/up. End frames selection in camera and 2D views.\n" +
        "Fly: enable Fly in the camera header, then use WASD to move, Q/E down/up, right-drag to look, and Shift for speed. Scroll moves forward/back. Escape returns to orbit. Movement keys apply only while the camera is focused.\n" +
        "Lights: select a light and open Entity for color, radius and intensity. Expand Target and cone to create a spotlight target. The camera bulb button toggles lighting and shadows.\n" +
        "Environment: open the sun tab to author sunlight with Apply/Revert and to assign different sky materials to world brush faces. Drag the sun direction control to aim; Apply commits. Skies can enclose selected geometry and remain independent materials.\n" +
        "Materials: click a thumbnail to choose the material for new geometry; Apply to Selection repaints selected surfaces. In Use shows map materials and combines with search. Preview shows image details and Size adjusts the tiles.\n" +
        "Grid: [ decreases and ] increases. Keys 1–9 choose 1, 2, 4, 8, 16, 32, 64, 256 and 512. The grid list also includes 0.25, 0.5 and 128. F opens visibility filters; M opens map statistics.\n" +
        "Classic toolbar: Modify mirrors flip/rotate, texture projection, CSG and patch commands. CT/PT select through the map; Touching/Inside use Base and Depth as a finite selection volume. Axis locks constrain movement. Cubic clipping, alpha preview and quick category visibility affect only the editor view.\n" +
        "Space duplicates; Delete removes; Ctrl/Cmd+Z undoes; Ctrl/Cmd+Shift+Z redoes.\n\n" +
        "Models and prefabs: open Create or the asset browser tabs. Choose Place, then click a camera surface or grid; Shift repeats and Escape cancels. Models support surface alignment, Drop, Find and Replace. Prefabs use native .map files with Edit source, Reload, Make unique and Explode.\n" +
        "Player collision: Create → Player clip draws an invisible brush or converts selected whole world brushes. Shape a separate volume around a model; magenta outlines mark clip brushes. Model collision alone does not block players. Choose a material thumbnail to resume ordinary brush creation.\n" +
        "Geometry: Create opens native patches, bevels, caps, cylinders, arches and stairs; select a curve to refine or edit its control points.\n" +
        "Organization: use Layers for native layer/group authoring, hide/freeze/isolate and restore. Hidden objects are excluded from viewports; frozen objects cannot be selected or edited.\n" +
        "Terrain detail: fill or brush-paint vertex color and alpha, add a blend overlay with an available alpha material, or project a native mesh decal from a selected brush face.\n" +
        "Gameplay: Entity contains verified IW4 spawns, triggers and script objects. Select a source and destination to connect target to targetname; links are visible in the viewports.\n\n" +
        "This editor uses iwmap 4 source. Unrecognized primitives are preserved on save. " +
        "IW4 material JSON color maps, DDS and PNG/JPEG/BMP previews are supported; PS3 material programs are not executed. " +
        "Materials without a matching image remain unavailable. Unresolved ordinary map surfaces use a tiled DEFAULT fallback; unavailable skies remain omitted. " +
        "Lighting previews light_point_linear point/spot lights and authored direct sunlight with shadows. Sky surfaces use available IW4 sky cubemaps. Custom falloff assets, ambient/diffuse sky lighting and bounced light are not previewed. " +
        "Build → Build PS3 map compiles a saved map into .d3dbsp and .ff using D3dbspLinker. " +
        Compilation.MapCompiler.Scope);

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (_dialogs.BlocksInput || e.Handled) return;
        if (e.Key == Key.Escape && _session.HasPlacement)
        {
            _session.CancelPlacement();
            SetStatus("Placement cancelled.");
            e.Handled = true;
            return;
        }
        if (Workspace.Camera.HandleNavigationKeyDown(e)) return;
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
        if (command && e.Key == Key.Space) { Workspace.ToggleMaximize(); e.Handled = true; return; }
        if (command && e.Key == Key.Tab) { Workspace.CyclePlane(); e.Handled = true; return; }
        if (command && e.Key == Key.Z) { if (shift) Redo_Click(this, e); else Undo_Click(this, e); e.Handled = true; return; }
        if (command && e.Key == Key.Y) { Redo_Click(this, e); e.Handled = true; return; }
        if (command || e.KeyModifiers.HasFlag(KeyModifiers.Alt)) return;
        if (e.Key == Key.Space && e.Source is Control source && IsButtonInput(source)) return;
        switch (e.Key)
        {
            case Key.Q: ActivateTool(EditorTool.Select); break;
            case Key.T: ActivateTool(EditorTool.Terrain); break;
            case Key.V: ActivateTool(EditorTool.Sculpt); break;
            case Key.S: ActivateTool(EditorTool.Face); break;
            case Key.E: ActivateTool(EditorTool.Vertex); break;
            case Key.X: ActivateTool(EditorTool.Clip); break;
            case Key.F: Filters_Click(this, e); break;
            case Key.M: Statistics_Click(this, e); break;
            case Key.End: FrameSelection_Click(this, e); break;
            case Key.OemOpenBrackets: ChangeGrid(-1); break;
            case Key.OemCloseBrackets: ChangeGrid(1); break;
            case Key.D1: GridCombo.SelectedItem = "1"; break;
            case Key.D2: GridCombo.SelectedItem = "2"; break;
            case Key.D3: GridCombo.SelectedItem = "4"; break;
            case Key.D4: GridCombo.SelectedItem = "8"; break;
            case Key.D5: GridCombo.SelectedItem = "16"; break;
            case Key.D6: GridCombo.SelectedItem = "32"; break;
            case Key.D7: GridCombo.SelectedItem = "64"; break;
            case Key.D8: GridCombo.SelectedItem = "256"; break;
            case Key.D9: GridCombo.SelectedItem = "512"; break;
            case Key.Escape:
                if (Workspace.Camera.HasPointerGesture || Workspace.GridViews.Any(view => view.HasActiveGesture)) return;
                _session.Select(null);
                SetTool(EditorTool.Select);
                break;
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
