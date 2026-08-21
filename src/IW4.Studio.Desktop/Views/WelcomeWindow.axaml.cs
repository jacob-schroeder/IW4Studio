using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using IW4.Runtime.Diagnostics;
using IW4.Studio.Desktop.Persistence;
using IW4.Studio.Desktop.Themes;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Views;

public sealed partial class WelcomeWindow : Window
{
    private readonly AppSettingsStore _settingsStore;
    private readonly WelcomeViewModel _viewModel = new();
    private readonly DispatcherTimer _progressTimer;
    private readonly object _progressSync = new();
    private XAssetLoadProgress? _pendingProgress;
    private bool _isClosing;

    public WelcomeWindow()
        : this(new AppSettingsStore(Path.Combine(AppContext.BaseDirectory, "appsettings.json")))
    {
    }

    internal WelcomeWindow(AppSettingsStore settingsStore)
    {
        ArgumentNullException.ThrowIfNull(settingsStore);
        _settingsStore = settingsStore;
        InitializeComponent();
        NativeMenu.SetMenu(this, StudioMenu.CreateWelcomeNativeMenu(this, ExecuteMenuAction));
        StudioMenu.PopulateWelcomeWindowMenu(WindowMenu, this, ExecuteMenuAction);
        WindowMenu.IsVisible = !OperatingSystem.IsMacOS();
        Icon = AppIcon.Create();
        DataContext = _viewModel;
        _viewModel.SetRecentFiles(_settingsStore.LoadRecentFastFiles());
        _progressTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(80)
        };
        _progressTimer.Tick += (_, _) => ApplyPendingProgress();
        Closing += (_, _) => _isClosing = true;
    }

    public event Action<WelcomeWindow, FastFileWorkspace>? WorkspaceOpened;

    public event Action<WelcomeWindow>? AboutRequested;

    public event Action<ThemeMode>? ThemeRequested;

    internal void SetThemeMode(ThemeMode mode)
        => ThemeMenuSelection.Set(this, WindowMenu, mode);

    private async void BrowseButton_Click(object? sender, RoutedEventArgs e)
    {
        string? selectedPath = await SelectFastFileAsync();
        if (!_isClosing && selectedPath is not null)
            _viewModel.SelectedPath = selectedPath;
    }

    private async void LoadSingleButton_Click(object? sender, RoutedEventArgs e) =>
        await OpenWorkspaceAsync(withDependencies: false);

    private async void LoadDependenciesButton_Click(object? sender, RoutedEventArgs e) =>
        await OpenWorkspaceAsync(withDependencies: true);

    private void RecentFileButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path })
            _viewModel.SelectedPath = path;
    }

    private void ExecuteMenuAction(StudioMenuAction action)
    {
        switch (action)
        {
            case StudioMenuAction.ShowAbout:
                AboutRequested?.Invoke(this);
                break;
            case StudioMenuAction.SelectDarkTheme:
                ThemeRequested?.Invoke(ThemeMode.Dark);
                break;
            case StudioMenuAction.SelectLightTheme:
                ThemeRequested?.Invoke(ThemeMode.Light);
                break;
        }
    }

    private async Task<string?> SelectFastFileAsync()
    {
        try
        {
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "Open an IW4 fastfile",
                    AllowMultiple = false,
                    FileTypeFilter =
                    [
                        new FilePickerFileType("IW4 fastfiles")
                        {
                            Patterns = ["*.ff"]
                        },
                        FilePickerFileTypes.All
                    ]
                });

            if (files.Count == 0)
                return null;

            string? localPath = files[0].TryGetLocalPath();
            if (localPath is null && !_isClosing)
            {
                _viewModel.FailLoad(
                    new InvalidOperationException(
                        "The selected file is not available as a local filesystem path."));
            }

            return localPath;
        }
        catch (Exception exception)
        {
            if (!_isClosing)
            {
                _viewModel.FailLoad(
                    new InvalidOperationException(
                        "The fastfile picker could not be opened.",
                        exception));
            }

            return null;
        }
    }

    private async Task OpenWorkspaceAsync(bool withDependencies)
    {
        if (_viewModel.IsBusy)
            return;

        string selectedPath = _viewModel.SelectedPath.Trim();
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            string? pickedPath = await SelectFastFileAsync();
            if (_isClosing || pickedPath is null)
                return;

            selectedPath = pickedPath;
            _viewModel.SelectedPath = pickedPath;
        }

        if (!File.Exists(selectedPath))
        {
            _viewModel.FailLoad(
                new FileNotFoundException(
                    "Select an existing fastfile before loading.",
                    selectedPath));
            return;
        }

        selectedPath = Path.GetFullPath(selectedPath);
        _viewModel.SelectedPath = selectedPath;
        _viewModel.BeginLoad(withDependencies);
        lock (_progressSync)
            _pendingProgress = null;
        _progressTimer.Start();

        try
        {
            FastFileOpenMode mode = withDependencies
                ? new ZonePlan(FastFileOpenProfiles.ResolveForTarget(selectedPath))
                : Isolated.Instance;
            var service = new FastFileDocumentService(QueueProgress);
            var request = new FastFileDocumentOpenRequest(selectedPath, mode);
            FastFileWorkspace? workspace = await Task.Run(() => service.Open(request));
            ApplyPendingProgress();
            try
            {
                if (_isClosing)
                    return;

                SaveRecentFastFile(selectedPath);
                Action<WelcomeWindow, FastFileWorkspace> opened = WorkspaceOpened
                    ?? throw new InvalidOperationException(
                        "No application host is available to own the opened workspace.");
                opened(this, workspace);
                workspace = null;
            }
            finally
            {
                workspace?.Dispose();
            }
        }
        catch (Exception exception)
        {
            if (!_isClosing)
                _viewModel.FailLoad(exception);
        }
        finally
        {
            _progressTimer.Stop();
            lock (_progressSync)
                _pendingProgress = null;
        }
    }

    private void QueueProgress(XAssetLoadProgress progress)
    {
        lock (_progressSync)
            _pendingProgress = progress;
    }

    private void ApplyPendingProgress()
    {
        XAssetLoadProgress? progress;
        lock (_progressSync)
        {
            progress = _pendingProgress;
            _pendingProgress = null;
        }

        if (progress is { } value)
            _viewModel.ReportProgress(value);
    }

    private void SaveRecentFastFile(string path)
    {
        try
        {
            _settingsStore.SaveRecentFastFile(path);
        }
        catch (IOException)
        {
            // A workspace that opened successfully should not be blocked by its history entry.
        }
        catch (UnauthorizedAccessException)
        {
            // A workspace that opened successfully should not be blocked by its history entry.
        }
    }
}
