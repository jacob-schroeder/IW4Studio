using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using IW4.Studio.Desktop.Editors.Gsc;
using IW4.Studio.Desktop.Lifecycle;
using IW4.Studio.Desktop.Rendering;
using IW4.Studio.Desktop.Themes;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Desktop.Workbench.Composition;
using IW4.Studio.Desktop.Workbench.Tools;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Views;

public sealed partial class EditorWindow : Window
{
    private readonly DestructiveNavigationCoordinator _navigationCoordinator;
    private readonly IUnsavedChangesDialog _unsavedChangesDialog;
    private readonly TransactionalSaveAsService _saveAsService = new();
    private FastFileRenderViewService? _renderViewService;
    private Task? _renderViewServiceDisposal;
    private StudioWorkbenchViewModel? _workbench;
    private GscEngineReferenceWindow? _gscEngineReferenceWindow;
    private readonly HashSet<MapRenderWindow> _livePreviewWindows = [];
    private int _saveAsInProgress;
    private int _navigationInProgress;
    private bool _approvedCloseRetry;
    private bool _disposed;

    public EditorWindow()
        : this(new DestructiveNavigationCoordinator())
    {
    }

    internal EditorWindow(DestructiveNavigationCoordinator navigationCoordinator)
    {
        _navigationCoordinator = navigationCoordinator ?? throw new ArgumentNullException(nameof(navigationCoordinator));
        InitializeComponent();
        NativeMenu.SetMenu(this, StudioMenu.CreateNativeMenu(this, ExecuteMenuAction));
        StudioMenu.PopulateWindowMenu(WindowMenu, this, ExecuteMenuAction);
        WindowMenu.IsVisible = !OperatingSystem.IsMacOS();
        Icon = AppIcon.Create();
        _unsavedChangesDialog = new AvaloniaUnsavedChangesDialog(this);
    }

    internal EditorWindow(
        FastFileWorkspace workspace,
        DestructiveNavigationCoordinator navigationCoordinator)
        : this(navigationCoordinator)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var constructionRollback = new List<Action>();
        try
        {
            var workbench = new StudioWorkbenchViewModel(workspace);
            constructionRollback.Add(workbench.Dispose);
            workbench.LivePreviewRequested += Workbench_LivePreviewRequested;
            constructionRollback.Add(() =>
                workbench.LivePreviewRequested -= Workbench_LivePreviewRequested);
            workbench.EditorTabCloseRequested +=
                Workbench_EditorTabCloseRequested;
            constructionRollback.Add(() =>
                workbench.EditorTabCloseRequested -=
                    Workbench_EditorTabCloseRequested);
            workbench.EditorTabsCloseRequested +=
                Workbench_EditorTabsCloseRequested;
            constructionRollback.Add(() =>
                workbench.EditorTabsCloseRequested -=
                    Workbench_EditorTabsCloseRequested);
            workbench.EngineBuiltInReferenceRequested +=
                Workbench_EngineBuiltInReferenceRequested;
            constructionRollback.Add(() =>
                workbench.EngineBuiltInReferenceRequested -=
                    Workbench_EngineBuiltInReferenceRequested);
            DataContext = workbench;
            constructionRollback.Add(() =>
            {
                if (ReferenceEquals(DataContext, workbench))
                    DataContext = null;
            });
            Title = $"{Path.GetFileName(workspace.Document.Request.Path)} — IW4 Studio";
            EventHandler closedHandler = (_, _) => DisposeEditor();
            Closed += closedHandler;
            constructionRollback.Add(() => Closed -= closedHandler);
            _workbench = workbench;
        }
        catch
        {
            _workbench = null;
            for (int index = constructionRollback.Count - 1; index >= 0; index--)
            {
                try
                {
                    constructionRollback[index]();
                }
                catch
                {
                    // Construction must report its original failure.
                }
            }

            throw;
        }
    }

    public event Action<EditorWindow>? WelcomeRequested;

    public event Action<EditorWindow>? AboutRequested;

    public event Action<ThemeMode>? ThemeRequested;

    /// <summary>
    /// Lets the application lifetime distinguish an approved close that can
    /// naturally become an OnLastWindowClose shutdown from Open Another,
    /// which intentionally replaces the main window first.
    /// </summary>
    internal event Action<DestructiveNavigationAction>? ApprovedCloseRequested;

    internal void SetThemeMode(ThemeMode mode)
        => ThemeMenuSelection.Set(this, WindowMenu, mode);

    /// <summary>
    /// Called by the application after the open-another guard has already
    /// authorized replacing this workspace. The token is consumed by exactly
    /// one synchronous Avalonia close retry.
    /// </summary>
    internal void CloseAfterApprovedNavigation(DestructiveNavigationAction action)
    {
        PrepareApprovedCloseRetry();
        ApprovedCloseRequested?.Invoke(action);
        Close();
    }

    /// <summary>
    /// Arms the synchronous close event that Avalonia will issue while an
    /// approved application shutdown is retried.
    /// </summary>
    internal void PrepareApprovedCloseRetry() => _approvedCloseRetry = true;

    internal void DisposeAfterFailedOpen()
    {
        try
        {
            PrepareApprovedCloseRetry();
            Close();
        }
        finally
        {
            DisposeEditor();
        }
    }

    /// <summary>
    /// Routes an application-lifetime shutdown through the same guard used by
    /// every window-originated destructive action.
    /// </summary>
    internal Task<DestructiveNavigationResult> RequestApplicationShutdownAsync(
        Func<Task> shutdownAsync) =>
        RequestNavigationAsync(DestructiveNavigationAction.ApplicationShutdown, shutdownAsync);

    private async void OpenAnotherButton_Click(object? sender, RoutedEventArgs e) =>
        await RequestOpenAnotherAsync();

    private async void SaveAsButton_Click(object? sender, RoutedEventArgs e) =>
        await RequestSaveAsAsync();

    private void RevertWeaponDraftButton_Click(
        object? sender,
        RoutedEventArgs e) =>
        _workbench?.SelectedWeaponEditor?.RevertDraft();

    private void ApplyWeaponDraftButton_Click(
        object? sender,
        RoutedEventArgs e) =>
        _workbench?.SelectedWeaponEditor?.ApplyDraft();

    private void ActivateLivePreview()
    {
        if (_disposed || _workbench is not { } workbench)
            return;

        workbench.ActivateTool(StudioToolIds.LivePreview);
    }

    private async void Workbench_LivePreviewRequested(
        object? sender,
        EventArgs e)
    {
        if (_disposed)
            return;

        if (_renderViewServiceDisposal is { } pendingDisposal)
        {
            try
            {
                await pendingDisposal;
            }
            finally
            {
                if (ReferenceEquals(
                        _renderViewServiceDisposal,
                        pendingDisposal))
                {
                    _renderViewServiceDisposal = null;
                }
            }
        }
        if (_disposed || _workbench is not { } workbench)
            return;

        FastFileRenderViewService renderViewService =
            _renderViewService ??= new FastFileRenderViewService();
        var renderWindow = new MapRenderWindow(
            workbench.Workspace,
            workbench.TargetFileName,
            renderViewService);
        _livePreviewWindows.Add(renderWindow);
        renderWindow.Closed += LivePreviewWindow_Closed;
        workbench.ConsoleOutput.Append(
            Workbench.Tools.ConsoleOutput.ConsoleOutputLevel.Information,
            "Live Preview",
            "Opening the native in-game rendering preview.");
        renderWindow.Show(this);
    }

    private void LivePreviewWindow_Closed(object? sender, EventArgs e)
    {
        if (sender is not MapRenderWindow renderWindow)
            return;

        renderWindow.Closed -= LivePreviewWindow_Closed;
        _livePreviewWindows.Remove(renderWindow);
        if (_livePreviewWindows.Count != 0)
            return;

        FastFileRenderViewService? renderViewService = _renderViewService;
        _renderViewService = null;
        if (renderViewService is null)
            return;

        if (_disposed)
        {
            renderViewService.Dispose();
            return;
        }

        _renderViewServiceDisposal = Task.Run(renderViewService.Dispose);
    }

    private async void Workbench_EditorTabCloseRequested(
        object? sender,
        WorkbenchEditorTabCloseRequestedEventArgs args)
    {
        if (_disposed ||
            _workbench is not { } workbench ||
            !ReferenceEquals(sender, workbench) ||
            Volatile.Read(ref _saveAsInProgress) != 0)
        {
            return;
        }

        await _navigationCoordinator.CloseEditorTabAsync(
            workbench.Editor.EditingSession,
            args.Tab.IsDirty,
            _unsavedChangesDialog,
            () =>
            {
                if (!_disposed && ReferenceEquals(_workbench, workbench))
                    workbench.CloseEditorTab(args.Tab);
                return Task.CompletedTask;
            });
    }

    private async void Workbench_EditorTabsCloseRequested(
        object? sender,
        WorkbenchEditorTabsCloseRequestedEventArgs args)
    {
        if (_disposed ||
            _workbench is not { } workbench ||
            !ReferenceEquals(sender, workbench) ||
            Volatile.Read(ref _saveAsInProgress) != 0)
        {
            return;
        }

        WorkbenchEditorTabViewModel[] tabs = args.Tabs
            .Where(workbench.OpenEditorTabs.Contains)
            .Distinct()
            .ToArray();
        if (tabs.Length == 0)
            return;

        await _navigationCoordinator.CloseEditorTabsAsync(
            workbench.Editor.EditingSession,
            tabs.Count(tab => tab.IsDirty),
            _unsavedChangesDialog,
            () =>
            {
                if (!_disposed && ReferenceEquals(_workbench, workbench))
                {
                    foreach (WorkbenchEditorTabViewModel tab in tabs
                        .OrderBy(tab => ReferenceEquals(
                            tab,
                            workbench.SelectedEditorTab)))
                    {
                        workbench.CloseEditorTab(tab);
                    }
                }
                return Task.CompletedTask;
            });
    }

    private void Workbench_EngineBuiltInReferenceRequested(
        object? sender,
        GscEngineBuiltInNavigationRequestedEventArgs args)
    {
        if (_disposed)
            return;

        if (_gscEngineReferenceWindow is null)
        {
            _gscEngineReferenceWindow = new GscEngineReferenceWindow(
                args.BuiltIn);
            _gscEngineReferenceWindow.Closed += (_, _) =>
                _gscEngineReferenceWindow = null;
            _gscEngineReferenceWindow.Show(this);
            return;
        }

        _gscEngineReferenceWindow.NavigateTo(args.BuiltIn);
        _gscEngineReferenceWindow.Activate();
    }

    private async void ExecuteMenuAction(StudioMenuAction action)
    {
        switch (action)
        {
            case StudioMenuAction.ShowAbout:
                AboutRequested?.Invoke(this);
                break;
            case StudioMenuAction.SaveAs:
                await RequestSaveAsAsync();
                break;
            case StudioMenuAction.ExtractZone:
                await RequestExtractZoneAsync();
                break;
            case StudioMenuAction.DumpSourceAssets:
                await RequestDumpSourceAssetsAsync();
                break;
            case StudioMenuAction.OpenAnother:
                await RequestOpenAnotherAsync();
                break;
            case StudioMenuAction.Exit:
                await RequestCloseAsync(DestructiveNavigationAction.Exit);
                break;
            case StudioMenuAction.LivePreview:
                ActivateLivePreview();
                break;
            case StudioMenuAction.SelectDarkTheme:
                ThemeRequested?.Invoke(ThemeMode.Dark);
                break;
            case StudioMenuAction.SelectLightTheme:
                ThemeRequested?.Invoke(ThemeMode.Light);
                break;
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_approvedCloseRetry)
        {
            _approvedCloseRetry = false;
            base.OnClosing(e);
            return;
        }

        if (!_disposed && _workbench is not null)
        {
            // Avalonia requires this synchronous boundary to be cancelled
            // before the asynchronous confirmation begins. Reentrant close
            // events are harmless: the coordinator coalesces them.
            e.Cancel = true;
            _ = RequestCloseAsync(DestructiveNavigationAction.WindowClose);
        }

        base.OnClosing(e);
    }

    private Task RequestOpenAnotherAsync() =>
        RequestNavigationAsync(
            DestructiveNavigationAction.OpenAnother,
            () =>
            {
                WelcomeRequested?.Invoke(this);
                return Task.CompletedTask;
            });

    private Task RequestCloseAsync(DestructiveNavigationAction action) =>
        RequestNavigationAsync(
            action,
            () =>
            {
                CloseAfterApprovedNavigation(action);
                return Task.CompletedTask;
            });

    private async Task<DestructiveNavigationResult> RequestNavigationAsync(
        DestructiveNavigationAction action,
        Func<Task> proceedAsync)
    {
        if (_disposed || _workbench is not { } workbench)
            return DestructiveNavigationResult.Cancelled;
        if (Volatile.Read(ref _saveAsInProgress) != 0)
            return DestructiveNavigationResult.Coalesced;
        if (Interlocked.CompareExchange(
                ref _navigationInProgress,
                1,
                comparand: 0) != 0)
        {
            return DestructiveNavigationResult.Coalesced;
        }

        Control? editorContent = Content as Control;
        bool wasContentEnabled = editorContent?.IsEnabled == true;
        if (editorContent is not null)
            editorContent.IsEnabled = false;

        try
        {
            return await _navigationCoordinator.NavigateAsync(
                workbench.Editor.EditingSession,
                action,
                _unsavedChangesDialog,
                proceedAsync,
                workbench.CanSaveAs ? RequestSaveAsAsync : null,
                stagedEditorChanges: CaptureStagedEditorChanges);
        }
        finally
        {
            if (!_disposed && editorContent is not null)
                editorContent.IsEnabled = wasContentEnabled;
            Volatile.Write(ref _navigationInProgress, 0);
        }
    }

    private Task<WorkspaceSaveOutcome> RequestSaveAsAsync()
    {
        if (_disposed ||
            _workbench is not { } workbench ||
            !workbench.CanSaveAs)
        {
            return Task.FromResult(WorkspaceSaveOutcome.Cancellation);
        }
        if (Interlocked.CompareExchange(
                ref _saveAsInProgress,
                1,
                comparand: 0) != 0)
        {
            return Task.FromResult(WorkspaceSaveOutcome.Cancellation);
        }

        return RequestSaveAsCoreAsync(workbench);
    }

    private async Task<WorkspaceSaveOutcome> RequestSaveAsCoreAsync(
        StudioWorkbenchViewModel workbench)
    {
        string originalTitle = Title ?? "IW4 Studio";
        try
        {
            string? destination =
                await SelectSaveDestinationAsync(workbench.TargetFileName);
            if (destination is null || _disposed)
                return WorkspaceSaveOutcome.Cancellation;

            var progress = new Progress<SaveAsProgress>(value =>
                Title = $"{originalTitle} — {value.Message}");
            SaveAsResult result = await Task.Run(() => _saveAsService.SaveAs(
                workbench.Editor.EditingSession,
                new SaveAsRequest(destination, AllowOverwrite: true),
                progress,
                workbench.Editor.EditingSession.CancellationToken));
            workbench.RefreshAfterSave();
            workbench.LogSaveResult(result);
            if (!result.Succeeded && !_disposed)
            {
                await ShowSaveResultAsync(
                    result.Cancelled,
                    result.Diagnostics);
            }

            return result.Succeeded
                ? WorkspaceSaveOutcome.Success
                : result.Cancelled
                    ? WorkspaceSaveOutcome.Cancellation
                    : WorkspaceSaveOutcome.Failure;
        }
        finally
        {
            Volatile.Write(ref _saveAsInProgress, 0);
            if (!_disposed)
                Title = originalTitle;
        }
    }

    private async Task<string?> SelectSaveDestinationAsync(string suggestedFileName)
    {
        try
        {
            IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save validated IW4 fastfile as",
                SuggestedFileName = SaveFilePickerName.WithoutDefaultExtension(
                    suggestedFileName,
                    "ff"),
                DefaultExtension = "ff",
                ShowOverwritePrompt = true,
                FileTypeChoices =
                [
                    new FilePickerFileType("IW4 fastfiles") { Patterns = ["*.ff"] },
                    FilePickerFileTypes.All
                ]
            });
            return file?.TryGetLocalPath();
        }
        catch
        {
            return null;
        }
    }

    private async Task RequestExtractZoneAsync()
    {
        if (_disposed || _workbench is not { } workbench)
            return;

        try
        {
            string filePrefix = Path.GetFileNameWithoutExtension(
                workbench.TargetFileName);
            IStorageFile? destination = await StorageProvider.SaveFilePickerAsync(
                new FilePickerSaveOptions
                {
                    Title = "Extract decoded IW4 zone",
                    SuggestedFileName = filePrefix,
                    DefaultExtension = "zone",
                    ShowOverwritePrompt = true,
                    FileTypeChoices =
                    [
                        new FilePickerFileType("IW4 zone files")
                        {
                            Patterns = ["*.zone"]
                        },
                        FilePickerFileTypes.All
                    ]
                });
            if (destination is null || _disposed)
                return;

            await using Stream output = await destination.OpenWriteAsync();
            if (!output.CanSeek)
            {
                throw new NotSupportedException(
                    "The selected storage destination does not support a safe replacement write.");
            }

            output.SetLength(0);
            output.Position = 0;
            await output.WriteAsync(workbench.Workspace.LoadedZone.ZoneBytes);
            await output.FlushAsync();
            workbench.ConsoleOutput.Append(
                Workbench.Tools.ConsoleOutput.ConsoleOutputLevel.Information,
                "Extract Zone",
                $"Extracted decoded zone to '{destination.Name}'.");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (_disposed)
                return;

            workbench.ConsoleOutput.Append(
                Workbench.Tools.ConsoleOutput.ConsoleOutputLevel.Error,
                "Extract Zone",
                $"Zone extraction failed: {exception.Message}");
            if (workbench.DockLayout.State.Bottom.ActiveToolId !=
                StudioToolIds.ConsoleOutput)
            {
                _ = workbench.ActivateTool(StudioToolIds.ConsoleOutput);
            }
        }
    }

    private async Task RequestDumpSourceAssetsAsync()
    {
        if (_disposed || _workbench is not { } workbench)
            return;

        string? destinationDirectory = await SelectSourceDumpDirectoryAsync();
        if (destinationDirectory is null || _disposed)
            return;

        await workbench.DumpSourceAssetsAsync(destinationDirectory);
    }

    private async Task<string?> SelectSourceDumpDirectoryAsync()
    {
        try
        {
            IReadOnlyList<IStorageFolder> folders =
                await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Select source asset dump folder",
                    AllowMultiple = false
                });
            string? destinationDirectory = folders.Count == 0
                ? null
                : folders[0].TryGetLocalPath();
            return string.IsNullOrWhiteSpace(destinationDirectory)
                ? null
                : destinationDirectory;
        }
        catch
        {
            return null;
        }
    }

    private async Task ShowSaveResultAsync(
        bool cancelled,
        IReadOnlyList<string> diagnostics)
    {
        var window = new Window
        {
            Title = cancelled ? "Save As cancelled" : "Save As blocked",
            Width = 620,
            Height = 340,
            MinWidth = 480,
            MinHeight = 240,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new TextBox
            {
                IsReadOnly = true,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                AcceptsReturn = true,
                Text = string.Join(
                    Environment.NewLine,
                    diagnostics.DefaultIfEmpty(
                        "No diagnostic was supplied.")),
                Margin = new Avalonia.Thickness(18)
            }
        };
        await window.ShowDialog(this);
    }

    private SupplementalUnsavedChanges CaptureStagedEditorChanges()
    {
        if (_disposed || _workbench is not { } workbench)
            return SupplementalUnsavedChanges.Clean;

        int changedItemCount = checked(
            workbench.OpenEditorTabs.Count(tab => tab.IsDirty) +
            (workbench.FastFileDetails.HasDraftChanges ? 1 : 0));
        return changedItemCount == 0
            ? SupplementalUnsavedChanges.Clean
            : new SupplementalUnsavedChanges(
                IsDirty: true,
                ChangedItemCount: changedItemCount);
    }

    private void DisposeEditor()
    {
        if (_disposed)
            return;

        _disposed = true;
        foreach (MapRenderWindow livePreviewWindow in
            _livePreviewWindows.ToArray())
        {
            try
            {
                livePreviewWindow.Close();
            }
            catch
            {
                // One child window cannot block workspace disposal.
            }
        }
        _livePreviewWindows.Clear();
        _renderViewService?.Dispose();
        _renderViewService = null;
        _renderViewServiceDisposal?.GetAwaiter().GetResult();
        _renderViewServiceDisposal = null;
        _gscEngineReferenceWindow?.Close();
        _gscEngineReferenceWindow = null;
        if (_workbench is not null)
        {
            _workbench.LivePreviewRequested -=
                Workbench_LivePreviewRequested;
            _workbench.EditorTabCloseRequested -=
                Workbench_EditorTabCloseRequested;
            _workbench.EditorTabsCloseRequested -=
                Workbench_EditorTabsCloseRequested;
            _workbench.EngineBuiltInReferenceRequested -=
                Workbench_EngineBuiltInReferenceRequested;
            _workbench.Dispose();
            _workbench = null;
        }
    }
}
