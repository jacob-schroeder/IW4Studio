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
        Camera.SelectionChanged += selected => { FinishGestures(); _session.Select(selected); };
        Camera.ResolveTexturePath = Materials.ResolveTexturePath;
        Camera.RendererStatusChanged += (_, _) => Dispatcher.UIThread.Post(UpdateRendererStatus);
        _session.Changed += (_, _) => RefreshEditor();
        GridCombo.ItemsSource = new[] { "1", "2", "4", "8", "16", "32", "64", "128" };
        GridCombo.SelectedItem = "16";
        _ready = true;
        UpdateRendererStatus();
        RefreshEditor();
        SetStatus("Browse an asset folder and choose a material, then draw a brush or terrain in a grid view.");
        Closed += (_, _) => Materials.ReleasePreview();
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
        Camera.Document = _session.Document;
        Camera.Selection = _session.Selection;
        Camera.RefreshScene();
        UndoMenu.IsEnabled = _session.CanUndo;
        RedoMenu.IsEnabled = _session.CanRedo;
        (Button Button, EditorTool Tool)[] tools =
            [(SelectTool, EditorTool.Select), (BrushTool, EditorTool.Brush),
             (TerrainTool, EditorTool.Terrain), (SculptTool, EditorTool.Sculpt)];
        foreach (var tool in tools)
            tool.Button.Background = new SolidColorBrush(tool.Tool == _session.Tool
                ? Color.Parse("#675435") : Color.Parse("#36383D"));
        Inspector.RefreshSelection(_session);
        int preserved = _session.Document.Entities.Sum(entity => entity.PreservedPrimitives.Count);
        CountText.Text = $"{_session.Document.Brushes.Count()} brushes · {_session.Document.Terrains.Count()} terrain · " +
            $"{_session.Document.Entities.Count} entities" + (preserved > 0 ? $" · {preserved} preserved primitives" : "");
    }

    private void SetStatus(string message) => StatusText.Text = message;
    private void FinishGestures()
    {
        TopView.CompleteGesture(); FrontView.CompleteGesture(); SideView.CompleteGesture();
    }
    private void SetTool(EditorTool tool)
    {
        FinishGestures();
        _session.Tool = tool;
        _session.Refresh();
        SetStatus(tool switch
        {
            EditorTool.Brush => "Drag in a grid view to create a brush. Base and Depth set the third axis.",
            EditorTool.Terrain => "Drag a rectangle in XY to create terrain. Vertices per side controls the grid.",
            EditorTool.Sculpt => "Select terrain, then drag in XY to raise it. Hold Shift to lower. Escape cancels a stroke.",
            _ => "Click to select; drag to move. Drag brush corners to resize or colored axes to constrain movement."
        });
    }
    private void SelectTool_Click(object? sender, RoutedEventArgs e) => SetTool(EditorTool.Select);
    private void BrushTool_Click(object? sender, RoutedEventArgs e) => SetTool(EditorTool.Brush);
    private void TerrainTool_Click(object? sender, RoutedEventArgs e) => SetTool(EditorTool.Terrain);
    private void SculptTool_Click(object? sender, RoutedEventArgs e) => SetTool(EditorTool.Sculpt);
    private void Undo_Click(object? sender, RoutedEventArgs e) { FinishGestures(); _session.Undo(); }
    private void Redo_Click(object? sender, RoutedEventArgs e) { FinishGestures(); _session.Redo(); }
    private void Duplicate_Click(object? sender, RoutedEventArgs e)
    {
        FinishGestures();
        if (_session.Selection is MapEntity && !_session.CanTransformSelection)
            SetStatus("Worldspawn and entities containing unsupported primitives cannot be duplicated in this prototype.");
        else _session.DuplicateSelection();
    }
    private void Delete_Click(object? sender, RoutedEventArgs e) { FinishGestures(); _session.DeleteSelection(); }
    private void World_Click(object? sender, RoutedEventArgs e) { FinishGestures(); _session.Select(_session.Document.World); }
    private void Exit_Click(object? sender, RoutedEventArgs e) => Close();
    private void FrameAll_Click(object? sender, RoutedEventArgs e) => FrameAll();
    private void FrameSelection_Click(object? sender, RoutedEventArgs e)
    {
        FinishGestures();
        Camera.FrameSelection(); TopView.FrameSelection(); FrontView.FrameSelection(); SideView.FrameSelection();
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
                entity.Properties["light"] = "300";
                entity.Properties["_color"] = "1 1 1";
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
        "Q: select / move · B: draw brush · T: draw terrain · V: sculpt terrain\n" +
        "Grid views: drag to create; select and drag to move; drag brush corner handles to resize. Colored axes constrain movement.\n" +
        "Terrain: create and sculpt in XY; Shift lowers. Escape cancels a gesture.\n" +
        "Camera: right drag orbits, middle drag pans, wheel zooms. Click geometry to select. F frames selection.\n" +
        "Space duplicates; Delete removes; Ctrl/Cmd+Z undoes; Ctrl/Cmd+Shift+Z redoes.\n\n" +
        "This prototype edits iwmap 4 source. Curves and other unsupported primitives are preserved but not rendered. " +
        "IW4 material JSON color maps, DDS and PNG/JPEG/BMP previews are supported; PS3 material programs are not executed. " +
        "Materials without a matching image are omitted. Unresolved map surfaces appear as wireframe. " +
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
        switch (e.Key)
        {
            case Key.Q: SetTool(EditorTool.Select); break;
            case Key.B: SetTool(EditorTool.Brush); break;
            case Key.T: SetTool(EditorTool.Terrain); break;
            case Key.V: SetTool(EditorTool.Sculpt); break;
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
}
