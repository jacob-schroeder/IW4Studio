using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Iw4Radiant.Viewports.Camera;
using Iw4Radiant.Viewports.Orthographic;

namespace Iw4Radiant.Views;

public partial class ViewportWorkspace : UserControl
{
    private readonly (Control View, Border Panel)[] _views;
    private Control _activeView;
    private OrthoViewport _activeGrid;
    private EditorDialogs? _dialogs;
    private Action? _finishGestures;
    private bool _fourViews, _maximized, _materialsVisible = true;
    private GridLength[] _twoColumns = [new(1, GridUnitType.Star), new(5), new(1.2, GridUnitType.Star)];
    private GridLength[] _fourColumns = [new(1, GridUnitType.Star), new(5), new(1, GridUnitType.Star)];
    private GridLength[] _twoRows = [new(1, GridUnitType.Star), new(5), new(270)];
    private GridLength[] _fourRows = [new(1, GridUnitType.Star), new(5), new(1, GridUnitType.Star), new(5), new(250)];

    public ViewportWorkspace()
    {
        InitializeComponent();
        _views = [(CameraView, CameraPanel), (TopView, TopPanel), (FrontView, FrontPanel), (SideView, SidePanel)];
        GridViews = [TopView, FrontView, SideView];
        _activeView = _activeGrid = TopView;
        foreach (var entry in _views)
        {
            void ActivateVisibleView()
            {
                if (_dialogs?.BlocksInput != true && entry.Panel.IsEffectivelyVisible && entry.Panel.IsEffectivelyEnabled)
                    Activate(entry.View);
            }
            entry.Panel.AddHandler(PointerPressedEvent, (_, _) => ActivateVisibleView(), RoutingStrategies.Tunnel);
            entry.Panel.AddHandler(GotFocusEvent, (_, _) => ActivateVisibleView(), RoutingStrategies.Bubble, handledEventsToo: true);
        }
        CameraView.RendererStatusChanged += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            RendererErrorText.Text = CameraView.RendererError;
            RendererErrorPanel.IsVisible = CameraView.RendererError is not null;
        });
        CameraView.NavigationModeChanged += RefreshCameraControls;
        CameraView.FoliageBrushChanged += CameraFoliageBrush.SetBrush;
        RefreshCameraControls();
        ApplyLayout();
    }

    internal CameraViewport Camera => CameraView;
    internal MaterialBrowser Materials => MaterialBrowserView;
    internal XModelBrowser Models => XModelBrowserView;
    internal PrefabBrowser Prefabs => PrefabBrowserView;
    internal IReadOnlyList<OrthoViewport> GridViews { get; }
    internal bool FourViews => _fourViews;
    internal bool IsMaximized => _maximized;
    internal bool MaterialsVisible => _materialsVisible;
    internal OrthoPlane ActivePlane => _activeGrid.Plane;
    internal event Action? LayoutChanged;

    internal void InitializeActions(EditorDialogs dialogs, Action finishGestures)
    {
        _dialogs = dialogs;
        _finishGestures = finishGestures;
    }

    internal void SetFourViews(bool enabled)
    {
        if (_dialogs?.BlocksInput == true) return;
        _finishGestures?.Invoke();
        SaveLayout();
        _fourViews = enabled;
        _maximized = false;
        ApplyLayout();
        FocusActiveView();
    }

    internal void ToggleMaximize()
    {
        if (_dialogs?.BlocksInput == true) return;
        _finishGestures?.Invoke();
        SaveLayout();
        _maximized = !_maximized;
        ApplyLayout();
        FocusActiveView();
    }

    internal void ToggleMaterials()
    {
        if (_dialogs?.BlocksInput == true) return;
        _finishGestures?.Invoke();
        SaveLayout();
        _materialsVisible = _maximized || !_materialsVisible;
        _maximized = false;
        ApplyLayout();
        FocusActiveView();
    }

    internal void ShowModels() => ShowBrowser(1);
    internal void ShowPrefabs() => ShowBrowser(2);

    private void ShowBrowser(int index)
    {
        if (_dialogs?.BlocksInput == true) return;
        _finishGestures?.Invoke();
        SaveLayout();
        _materialsVisible = true;
        _maximized = false;
        AssetBrowserTabs.SelectedIndex = index;
        ApplyLayout();
    }

    internal void ShowGrid(OrthoPlane plane)
    {
        if (_dialogs?.BlocksInput == true) return;
        _finishGestures?.Invoke();
        SaveLayout();
        _activeGrid = GridViews.First(view => view.Plane == plane);
        Activate(_activeGrid);
        ApplyLayout();
        FocusActiveView();
    }

    internal void ShowCamera()
    {
        if (_dialogs?.BlocksInput == true) return;
        _finishGestures?.Invoke();
        SaveLayout();
        Activate(CameraView);
        ApplyLayout();
        FocusActiveView();
    }

    internal void CyclePlane() => ShowGrid((OrthoPlane)(((int)_activeGrid.Plane + 1) % GridViews.Count));

    internal void FocusActiveView()
    {
        if (_dialogs?.BlocksInput != true && _activeView.IsEffectivelyVisible && _activeView.IsEffectivelyEnabled)
            _activeView.Focus();
    }

    private void Activate(Control view)
    {
        _activeView = view;
        if (view is OrthoViewport grid) _activeGrid = grid;
        foreach (var entry in _views) entry.Panel.Classes.Set("activeViewport", ReferenceEquals(entry.View, view));
    }

    private void SaveLayout()
    {
        if (_maximized) return;
        GridLength[] columns = LayoutGrid.ColumnDefinitions.Select(column => column.Width).ToArray();
        GridLength[] rows = LayoutGrid.RowDefinitions.Select(row => row.Height).ToArray();
        GridLength[] previousRows = _fourViews ? _fourRows : _twoRows;
        if (!_materialsVisible)
        {
            rows[^1] = previousRows[^1];
            rows[^2] = previousRows[^2];
        }
        if (_fourViews) { _fourColumns = columns; _fourRows = rows; }
        else { _twoColumns = columns; _twoRows = rows; }
    }

    private void ApplyLayout()
    {
        // Keep every view attached: layout changes preserve navigation and GL resources.
        foreach (var entry in _views)
        {
            entry.Panel.IsVisible = false;
            Place(entry.Panel, 0, 0);
        }
        ColumnSplitter.IsVisible = !_maximized;
        ViewRowSplitter.IsVisible = !_maximized && _fourViews;
        MaterialSplitter.IsVisible = AssetBrowserTabs.IsVisible = !_maximized && _materialsVisible;
        LayoutGrid.ColumnDefinitions.Clear();
        LayoutGrid.RowDefinitions.Clear();
        foreach (GridLength width in _maximized ? [new GridLength(1, GridUnitType.Star)] : _fourViews ? _fourColumns : _twoColumns)
            LayoutGrid.ColumnDefinitions.Add(new ColumnDefinition(width) { MinWidth = width.IsStar ? 160 : 0 });
        GridLength[] heights = _maximized ? [new(1, GridUnitType.Star)] : (_fourViews ? _fourRows : _twoRows).ToArray();
        if (!_maximized && !_materialsVisible) heights[^1] = heights[^2] = new GridLength(0);
        for (int i = 0; i < heights.Length; i++)
            LayoutGrid.RowDefinitions.Add(new RowDefinition(heights[i])
            {
                MinHeight = heights[i].IsStar ? 120 : !_maximized && _materialsVisible && i == heights.Length - 1 ? 220 : 0
            });
        if (_maximized)
            _views.First(entry => ReferenceEquals(entry.View, _activeView)).Panel.IsVisible = true;
        else if (_fourViews)
        {
            Place(TopPanel, 0, 0); Place(CameraPanel, 2, 0);
            Place(FrontPanel, 0, 2); Place(SidePanel, 2, 2);
            foreach (var entry in _views) entry.Panel.IsVisible = true;
            Place(ColumnSplitter, 1, 0, rowSpan: 3);
            Place(ViewRowSplitter, 0, 1, columnSpan: 3);
            Place(MaterialSplitter, 0, 3, columnSpan: 3);
            Place(AssetBrowserTabs, 0, 4, columnSpan: 3);
        }
        else
        {
            var gridPanel = _views.First(entry => ReferenceEquals(entry.View, _activeGrid)).Panel;
            Place(gridPanel, 0, 0, rowSpan: 3); gridPanel.IsVisible = true;
            Place(CameraPanel, 2, 0); CameraPanel.IsVisible = true;
            Place(ColumnSplitter, 1, 0, rowSpan: 3);
            Place(MaterialSplitter, 2, 1);
            Place(AssetBrowserTabs, 2, 2);
        }
        foreach (var entry in _views)
            if (!entry.Panel.IsVisible && entry.View is OrthoViewport grid) grid.CancelGesture();
        Activate(_activeView);
        LayoutChanged?.Invoke();
    }

    private static void Place(Control control, int column, int row, int columnSpan = 1, int rowSpan = 1)
    {
        Grid.SetColumn(control, column); Grid.SetRow(control, row);
        Grid.SetColumnSpan(control, columnSpan); Grid.SetRowSpan(control, rowSpan);
    }

    private void PreviewLights_Changed(object? sender, RoutedEventArgs e)
    {
        if (CameraView is not null) CameraView.PreviewLighting = PreviewLights.IsChecked == true;
    }

    private void FlyCamera_Changed(object? sender, RoutedEventArgs e)
    {
        if (CameraView is null || _dialogs?.BlocksInput == true) return;
        CameraView.FlyMode = FlyCamera.IsChecked == true;
        CameraView.Focus();
    }

    private void RefreshCameraControls()
    {
        FlyCamera.IsChecked = CameraView.FlyMode;
        CameraControlsHint.Text = CameraView.FlyMode
            ? "Fly · WASD move · Q/E down/up · Right-drag look\nShift faster · Scroll move · End frame · Esc orbit"
            : "Right-drag orbit · Shift+right-drag or middle-drag pan\nHold right + WASD move · Scroll zoom · End frame · Right-click objects";
    }

    private void Maximize_Click(object? sender, RoutedEventArgs e)
    {
        if (_dialogs?.BlocksInput == true || sender is not Button { Tag: string tag } || !int.TryParse(tag, out int index)) return;
        Activate(_views[index].View);
        ToggleMaximize();
    }

    private void CyclePlane_Click(object? sender, RoutedEventArgs e) => CyclePlane();
}
