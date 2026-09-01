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
    private readonly TransactionalSaveAsService _saveAsService = new();
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

    private async void CreateNewButton_Click(object? sender, RoutedEventArgs e) =>
        await CreateNewFastFileAsync();

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

    private async Task<string?> SelectNewFastFileDestinationAsync()
    {
        try
        {
            IStorageFile? file = await StorageProvider.SaveFilePickerAsync(
                new FilePickerSaveOptions
                {
                    Title = "Create an IW4 fastfile",
                    SuggestedFileName = SaveFilePickerName.WithoutDefaultExtension(
                        "patch.ff",
                        "ff"),
                    DefaultExtension = "ff",
                    ShowOverwritePrompt = true,
                    FileTypeChoices =
                    [
                        new FilePickerFileType("IW4 fastfiles")
                        {
                            Patterns = ["*.ff"]
                        },
                        FilePickerFileTypes.All
                    ]
                });
            if (file is null)
                return null;

            string? localPath = file.TryGetLocalPath();
            if (localPath is null && !_isClosing)
            {
                _viewModel.FailCreate(
                    new InvalidOperationException(
                        "The selected destination is not available as a local filesystem path."),
                    wasPublished: false);
            }

            return localPath;
        }
        catch (Exception exception)
        {
            if (!_isClosing)
            {
                _viewModel.FailCreate(
                    new InvalidOperationException(
                        "The fastfile destination picker could not be opened.",
                        exception),
                    wasPublished: false);
            }

            return null;
        }
    }

    private async Task CreateNewFastFileAsync()
    {
        if (_viewModel.IsBusy)
            return;

        string? destination = await SelectNewFastFileDestinationAsync();
        if (_isClosing || destination is null)
            return;

        destination = Path.GetFullPath(destination);
        _viewModel.SelectedPath = destination;
        _viewModel.BeginCreate();
        lock (_progressSync)
            _pendingProgress = null;
        _progressTimer.Start();

        bool wasPublished = false;
        try
        {
            var service = new FastFileDocumentService(QueueProgress);
            var progress = new Progress<SaveAsProgress>(value =>
            {
                if (!_isClosing)
                    _viewModel.ReportCreateProgress(value.Message);
            });
            SaveAsResult save = await Task.Run(() =>
            {
                using FastFileWorkspace blank = service.CreateBlank(
                    languageMask: 1,
                    selectedLanguageMask: 1);
                using var session = new FastFileEditingSession(blank);
                session.UpdateHeaderProperties(
                    session.HeaderMetadata with
                    {
                        FileCreationTimeRaw = checked(
                            (ulong)DateTime.UtcNow.ToFileTimeUtc())
                    },
                    session.LanguageMask);
                return _saveAsService.SaveAs(
                    session,
                    new SaveAsRequest(destination, AllowOverwrite: true),
                    progress,
                    session.CancellationToken);
            });
            if (!save.Succeeded)
                throw CreateSaveFailure(save);

            wasPublished = true;
            string authoredPath = save.DestinationPath ?? destination;
            _viewModel.ReportCreateProgress(
                "Opening the validated fastfile workspace...");
            FastFileWorkspace? workspace = await Task.Run(() =>
                service.OpenAuthoredOutput(new FastFileDocumentOpenRequest(
                    authoredPath,
                    Isolated.Instance)));
            ApplyPendingProgress();
            try
            {
                if (TransferWorkspaceToHost(authoredPath, workspace))
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
                _viewModel.FailCreate(exception, wasPublished);
        }
        finally
        {
            _progressTimer.Stop();
            lock (_progressSync)
                _pendingProgress = null;
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
                if (TransferWorkspaceToHost(selectedPath, workspace))
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

    private bool TransferWorkspaceToHost(
        string path,
        FastFileWorkspace workspace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(workspace);
        if (_isClosing)
            return false;

        SaveRecentFastFile(path);
        Action<WelcomeWindow, FastFileWorkspace> opened = WorkspaceOpened
            ?? throw new InvalidOperationException(
                "No application host is available to own the opened workspace.");
        opened(this, workspace);
        return true;
    }

    private static InvalidOperationException CreateSaveFailure(
        SaveAsResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        string summary = result.Cancelled
            ? "Fastfile creation was cancelled before publication."
            : "The new fastfile failed validation and was not published.";
        string diagnostics = string.Join(
            Environment.NewLine,
            result.Diagnostics.Where(value => !string.IsNullOrWhiteSpace(value)));
        return new InvalidOperationException(
            string.IsNullOrWhiteSpace(diagnostics)
                ? summary
                : $"{summary}{Environment.NewLine}{diagnostics}");
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
