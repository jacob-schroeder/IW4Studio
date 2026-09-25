using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System.Numerics;
using Iw4Radiant.Viewports.Camera;
using Iw4Radiant.Viewports.Orthographic;
using Iw4Radiant.Rendering;

namespace Iw4Radiant.Views;

public partial class ViewportWorkspace : UserControl
{
    private readonly (Control View, Border Panel)[] _views;
    private Control _activeView;
    private OrthoViewport _activeGrid;
    private EditorDialogs? _dialogs;
    private Action? _finishGestures;
    private bool _fourViews, _maximized, _materialsVisible = true;
    private bool _updatingFilm;
    private bool _updatingFog;
    private bool _compiledPreviewVisible;
    private bool _updatingCameraControls;
    private string? _walkError;
    private Control? _activeBeforeCompiledPreview;
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
        CameraView.RendererStatusChanged += (_, _) => Dispatcher.UIThread.Post(RefreshCameraControls);
        CameraView.WalkErrorChanged += error =>
        {
            _walkError = error;
            RefreshConsoleOutput();
            if (error is not null && !CameraView.WalkMode) ShowConsole();
        };
        CameraView.NavigationModeChanged += RefreshCameraControls;
        CameraView.FoliageBrushChanged += CameraFoliageBrush.SetBrush;
        AssetBrowserTabs.SelectionChanged += (_, args) =>
        {
            if (ReferenceEquals(args.Source, AssetBrowserTabs)) BrowserTabChanged?.Invoke();
        };
        FilmLightTint.PreserveColorScale = FilmDarkTint.PreserveColorScale = true;
        foreach (var slider in new[] { FilmBrightness, FilmContrast, FilmDesaturation })
            slider.PropertyChanged += (_, change) =>
            {
                if (change.Property == Avalonia.Controls.Primitives.RangeBase.ValueProperty)
                    UpdateFilmPreview();
            };
        FilmLightTint.SelectedColorChanged += UpdateFilmPreview;
        FilmDarkTint.SelectedColorChanged += UpdateFilmPreview;
        UpdateFilmPreview();
        FogColor.SelectedColor = FogPreview.Disabled.Color;
        foreach (var slider in new[] { FogStart, FogHalfDistance })
            slider.PropertyChanged += (_, change) =>
            {
                if (change.Property == Avalonia.Controls.Primitives.RangeBase.ValueProperty)
                    UpdateFogPreview();
            };
        FogColor.SelectedColorChanged += UpdateFogPreview;
        UpdateFogPreview();
        RefreshCameraControls();
        ApplyLayout();
    }

    internal CameraViewport Camera => CameraView;
    internal MaterialBrowser Materials => MaterialBrowserView;
    internal XModelBrowser Models => XModelBrowserView;
    internal PrefabBrowser Prefabs => PrefabBrowserView;
    internal FxSoundBrowser FxBrowser => FxBrowserView;
    internal FxSoundBrowser SoundBrowser => SoundBrowserView;
    internal bool MapFxEnabled => MapFxToggle.IsChecked == true;
    internal bool MapSoundsEnabled => MapSoundsToggle.IsChecked == true;
    internal IReadOnlyList<OrthoViewport> GridViews { get; }
    internal bool FourViews => _fourViews;
    internal bool IsMaximized => _maximized;
    internal bool MaterialsVisible => _materialsVisible;
    internal OrthoPlane ActivePlane => _activeGrid.Plane;
    internal event Action? LayoutChanged;
    internal event Action? BrowserTabChanged;
    internal event Action<bool>? MapFxPreviewChanged;
    internal event Action? MapFxPauseRequested;
    internal event Action? MapFxRestartRequested;
    internal event Action<bool>? MapSoundsPreviewChanged;

    internal void SetMapFxPlaybackState(bool active, bool paused, bool finished = false)
    {
        MapFxTransport.IsVisible = MapFxEnabled;
        MapFxPauseButton.IsEnabled = MapFxEnabled && active && !finished;
        MapFxRestartButton.IsEnabled = MapFxEnabled && active;
        ToolTip.SetTip(MapFxRestartButton, finished ? "Replay placed FX" : "Restart placed FX preview");
        Avalonia.Automation.AutomationProperties.SetName(MapFxRestartButton,
            finished ? "Replay placed FX" : "Restart placed FX preview");
        MapFxPauseIcon.IsVisible = !paused;
        MapFxResumeIcon.IsVisible = paused;
        ToolTip.SetTip(MapFxPauseButton, paused ? "Resume placed FX preview" : "Pause placed FX preview");
        Avalonia.Automation.AutomationProperties.SetName(MapFxPauseButton,
            paused ? "Resume placed FX preview" : "Pause placed FX preview");
    }

    internal void SetCompiledPreview(CompiledBspPreview? preview)
    {
        if (!_compiledPreviewVisible && preview is not null)
        {
            SaveLayout();
            _activeBeforeCompiledPreview = _activeView;
        }
        _compiledPreviewVisible = preview is not null;
        CameraView.CompiledPreview = preview;
        PreviewLights.IsEnabled = preview is null;
        if (preview is null && _activeBeforeCompiledPreview is { } active)
        {
            _activeView = active;
            _activeBeforeCompiledPreview = null;
        }
        ApplyLayout();
        RefreshCameraControls();
        if (_compiledPreviewVisible) CameraView.Focus();
    }

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
    internal void ShowFxSounds(bool isSound) => ShowBrowser(isSound ? 4 : 3);
    internal void ShowConsole() => ShowBrowser(5);

    private void RefreshConsoleOutput()
    {
        RendererErrorText.Text = string.Join(Environment.NewLine + Environment.NewLine,
            new[] { CameraView.RendererError, _walkError, CameraView.WalkPlayerError }.Where(message => !string.IsNullOrEmpty(message)));
        RendererErrorPanel.IsVisible = !string.IsNullOrEmpty(RendererErrorText.Text);
    }

    internal void DisableMapPreviews()
    {
        MapFxToggle.IsChecked = false;
        SetMapFxPlaybackState(false, false);
        MapSoundsToggle.IsChecked = false;
    }

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
        if (!ReferenceEquals(view, CameraView)) CameraView.StopWalk();
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
        if (_compiledPreviewVisible)
        {
            ColumnSplitter.IsVisible = ViewRowSplitter.IsVisible = MaterialSplitter.IsVisible =
                AssetBrowserTabs.IsVisible = false;
            LayoutGrid.ColumnDefinitions.Clear();
            LayoutGrid.RowDefinitions.Clear();
            LayoutGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            LayoutGrid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
            Place(CameraPanel, 0, 0);
            CameraPanel.IsVisible = true;
            Activate(CameraView);
            LayoutChanged?.Invoke();
            return;
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

    private void FilmPanel_Changed(object? sender, RoutedEventArgs e)
    {
        if (FilmPreviewPanel is not null)
            FilmPreviewPanel.IsVisible = FilmToggle.IsChecked == true;
        if (FilmToggle.IsChecked == true && FogToggle is not null)
            FogToggle.IsChecked = false;
    }

    private void FogPanel_Changed(object? sender, RoutedEventArgs e)
    {
        if (FogPreviewPanel is not null)
            FogPreviewPanel.IsVisible = FogToggle.IsChecked == true;
        if (FogToggle.IsChecked == true && FilmToggle is not null)
            FilmToggle.IsChecked = false;
    }

    private void FogEnabled_Changed(object? sender, RoutedEventArgs e)
    {
        if (FogControls is not null)
            FogControls.IsEnabled = FogEnabled.IsChecked == true;
        UpdateFogPreview();
    }

    private void FogReset_Click(object? sender, RoutedEventArgs e)
    {
        _updatingFog = true;
        try
        {
            FogEnabled.IsChecked = false;
            FogStart.Value = FogPreview.Disabled.StartDistance;
            FogHalfDistance.Value = FogPreview.Disabled.HalfDistance;
            FogColor.SelectedColor = FogPreview.Disabled.Color;
        }
        finally { _updatingFog = false; }
        UpdateFogPreview();
    }

    private void UpdateFogPreview()
    {
        if (_updatingFog || CameraView is null) return;
        FogStartValue.Text = $"{FogStart.Value:0}";
        FogHalfDistanceValue.Text = $"{FogHalfDistance.Value:0}";
        CameraView.FogAdjustment = new FogPreview(FogEnabled.IsChecked == true, FogColor.SelectedColor,
            (float)FogStart.Value, (float)FogHalfDistance.Value);
    }

    private void FilmReset_Click(object? sender, RoutedEventArgs e)
    {
        _updatingFilm = true;
        try
        {
            FilmBrightness.Value = 0;
            FilmContrast.Value = 1;
            FilmDesaturation.Value = 0;
            FilmLightTint.SelectedColor = FilmDarkTint.SelectedColor = Vector3.One;
        }
        finally { _updatingFilm = false; }
        UpdateFilmPreview();
    }

    private void UpdateFilmPreview()
    {
        if (_updatingFilm || CameraView is null) return;
        FilmBrightnessValue.Text = $"{FilmBrightness.Value:+0.00;-0.00;0.00}";
        FilmContrastValue.Text = $"{FilmContrast.Value:0.00}×";
        FilmDesaturationValue.Text = $"{FilmDesaturation.Value:P0}";
        CameraView.FilmAdjustment = new FilmPreview((float)FilmBrightness.Value, (float)FilmContrast.Value,
            (float)FilmDesaturation.Value, DisplayTint(FilmLightTint.SelectedColor),
            DisplayTint(FilmDarkTint.SelectedColor));
    }

    private static Vector3 DisplayTint(Vector3 linear) => new(
        MathF.Sqrt(Math.Clamp(linear.X, 0, 1)),
        MathF.Sqrt(Math.Clamp(linear.Y, 0, 1)),
        MathF.Sqrt(Math.Clamp(linear.Z, 0, 1)));

    private void FlyCamera_Changed(object? sender, RoutedEventArgs e)
    {
        if (_updatingCameraControls || CameraView is null || _dialogs?.BlocksInput == true) return;
        CameraView.FlyMode = FlyCamera.IsChecked == true;
        CameraView.Focus();
    }

    private void WalkCamera_Changed(object? sender, RoutedEventArgs e)
    {
        if (_updatingCameraControls || CameraView is null || _dialogs?.BlocksInput == true) return;
        if (WalkCamera.IsChecked == true) CameraView.StartWalk();
        else CameraView.StopWalk();
        RefreshCameraControls();
        CameraView.Focus();
    }

    private void ResetWalkCamera_Click(object? sender, RoutedEventArgs e)
    {
        if (_dialogs?.BlocksInput != true) CameraView.ResetWalk();
    }

    private void ShowWalkPlayer_Changed(object? sender, RoutedEventArgs e)
    {
        if (_updatingCameraControls || CameraView is null || _dialogs?.BlocksInput == true) return;
        CameraView.ShowWalkPlayer = ShowWalkPlayer.IsChecked == true;
        CameraView.Focus();
    }

    private void MapFx_Changed(object? sender, RoutedEventArgs e)
    {
        if (CameraView is null || _dialogs?.BlocksInput == true) return;
        SetMapFxPlaybackState(false, false);
        MapFxPreviewChanged?.Invoke(MapFxEnabled);
    }

    private void MapFxPause_Click(object? sender, RoutedEventArgs e)
    {
        if (_dialogs?.BlocksInput == true || !MapFxEnabled || !MapFxPauseButton.IsEnabled) return;
        MapFxPauseRequested?.Invoke();
    }

    private void MapFxRestart_Click(object? sender, RoutedEventArgs e)
    {
        if (_dialogs?.BlocksInput == true || !MapFxEnabled || !MapFxRestartButton.IsEnabled) return;
        MapFxRestartRequested?.Invoke();
    }

    private void MapSounds_Changed(object? sender, RoutedEventArgs e)
    {
        if (CameraView is null || _dialogs?.BlocksInput == true) return;
        MapSoundsPreviewChanged?.Invoke(MapSoundsEnabled);
    }

    private void RefreshCameraControls()
    {
        _updatingCameraControls = true;
        try
        {
            FlyCamera.IsChecked = CameraView.FlyMode;
            WalkCamera.IsChecked = CameraView.WalkMode;
            WalkCamera.IsEnabled = !_compiledPreviewVisible;
            ResetWalkCamera.IsVisible = CameraView.WalkMode;
            WalkPlayerControls.IsVisible = CameraView.WalkMode;
            ShowWalkPlayer.IsChecked = CameraView.ShowWalkPlayer;
            WalkPlayerStatus.Text = CameraView.WalkPlayerStatus;
        }
        finally { _updatingCameraControls = false; }
        RefreshConsoleOutput();
        ToolTip.SetTip(WalkCamera, CameraViewport.WalkProfile +
            "\nSolid world/group brushes, player clips and terrain/patches, including hidden geometry. Models need player clips. " +
            "No swimming, crouch/prone, mantle, ladders or moving entities.\nWASD move · Hold Shift run · Space jump · Right-drag look · R reset · Esc exit.");
        CameraControlsHint.Text = _compiledPreviewVisible
            ? "Read-only BSP · Right-drag orbit · Middle-drag pan · Scroll zoom · Fly for WASD"
            : CameraView.WalkMode
            ? (CameraView.WalkNeedsReset ? "Walk paused · Reset to recover or Esc to exit" :
               CameraView.WalkPaused ? "Walk paused · Click camera to resume · Reset to recover" : "Walk · WASD move · Hold Shift run · Space jump · Right-drag look") +
              "\nR reset · Esc restore camera · Approximate standing traversal" +
              "\nHidden geometry collides · Models need player clips · No swimming"
            : CameraView.FlyMode
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
