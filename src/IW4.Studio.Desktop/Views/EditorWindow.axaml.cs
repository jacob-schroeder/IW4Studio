using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using IW4.Studio.Desktop.Editors.Gsc;
using IW4.Studio.Desktop.Lifecycle;
using IW4.Studio.Desktop.Themes;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Desktop.Workbench.Composition;
using IW4.Studio.Desktop.Workbench.Tools;
using IW4.Studio.Documents;
using IW4.Studio.Rendering;

namespace IW4.Studio.Desktop.Views;

public sealed partial class EditorWindow : Window
{
    private readonly DestructiveNavigationCoordinator _navigationCoordinator;
    private readonly IUnsavedChangesDialog _unsavedChangesDialog;
    private readonly TransactionalSaveAsService _saveAsService = new();
    private readonly FastFileRenderViewService _renderViewService = new();
    private StudioWorkbenchViewModel? _workbench;
    private GscEngineReferenceWindow? _gscEngineReferenceWindow;
    private readonly HashSet<MapRenderWindow> _livePreviewWindows = [];
    private readonly RetryableRenderWarmup _renderWarmup = new();
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
        Icon = AppIcon.Create();
        _unsavedChangesDialog = new AvaloniaUnsavedChangesDialog(this);
    }

    public EditorWindow(FastFileWorkspace workspace)
        : this(workspace, new DestructiveNavigationCoordinator())
    {
    }

    internal EditorWindow(
        FastFileWorkspace workspace,
        DestructiveNavigationCoordinator navigationCoordinator)
        : this(navigationCoordinator)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        _workbench = new StudioWorkbenchViewModel(workspace);
        _workbench.LivePreviewRequested += Workbench_LivePreviewRequested;
        _workbench.EditorTabCloseRequested +=
            Workbench_EditorTabCloseRequested;
        _workbench.EditorTabsCloseRequested +=
            Workbench_EditorTabsCloseRequested;
        _workbench.EngineBuiltInReferenceRequested +=
            Workbench_EngineBuiltInReferenceRequested;
        DataContext = _workbench;
        Title = $"{Path.GetFileName(workspace.Document.Request.Path)} — IW4 Studio";
        Opened += EditorWindow_Opened;
        Closed += (_, _) => DisposeEditor();
    }

    public event Action<EditorWindow>? WelcomeRequested;

    public event Action<ThemeMode>? ThemeRequested;

    /// <summary>
    /// Lets the application lifetime distinguish an approved close that can
    /// naturally become an OnLastWindowClose shutdown from Open Another,
    /// which intentionally replaces the main window first.
    /// </summary>
    internal event Action<DestructiveNavigationAction>? ApprovedCloseRequested;

    internal void SetThemeMode(ThemeMode mode)
        => ThemeMenuSelection.Set(this, mode);

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

    /// <summary>
    /// Routes an application-lifetime shutdown through the same guard used by
    /// every window-originated destructive action.
    /// </summary>
    internal Task<DestructiveNavigationResult> RequestApplicationShutdownAsync(
        Func<Task> shutdownAsync) =>
        RequestNavigationAsync(DestructiveNavigationAction.ApplicationShutdown, shutdownAsync);

    private async void OpenAnotherMenuItem_Click(object? sender, EventArgs e) =>
        await RequestOpenAnotherAsync();

    private async void OpenAnotherButton_Click(object? sender, RoutedEventArgs e) =>
        await RequestOpenAnotherAsync();

    private async void ExitMenuItem_Click(object? sender, EventArgs e) =>
        await RequestCloseAsync(DestructiveNavigationAction.Exit);

    private async void SaveAsMenuItem_Click(object? sender, EventArgs e) =>
        await RequestSaveAsAsync();

    private async void SaveAsButton_Click(object? sender, RoutedEventArgs e) =>
        await RequestSaveAsAsync();

    private void LivePreviewMenuItem_Click(object? sender, EventArgs e)
    {
        if (_disposed || _workbench is not { } workbench)
            return;

        workbench.ActivateTool(StudioToolIds.LivePreview);
    }

    private void Workbench_LivePreviewRequested(
        object? sender,
        EventArgs e)
    {
        if (_disposed || _workbench is not { } workbench)
            return;

        var renderWindow = new MapRenderWindow(
            workbench.Workspace,
            workbench.TargetFileName,
            _renderViewService);
        _livePreviewWindows.Add(renderWindow);
        renderWindow.Closed += (_, _) =>
            _livePreviewWindows.Remove(renderWindow);
        workbench.ConsoleOutput.Append(
            Workbench.Tools.ConsoleOutput.ConsoleOutputLevel.Information,
            "Live Preview",
            "Opening the native in-game rendering preview.");
        renderWindow.Show(this);
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

    private void EditorWindow_Opened(object? sender, EventArgs e)
    {
        if (_disposed || _workbench is not { } workbench)
            return;

        EnsureRenderWarmup(workbench);
    }

    private Task<RenderViewSceneBuildResult> EnsureRenderWarmup(
        StudioWorkbenchViewModel workbench)
    {
        Task<RenderViewSceneBuildResult> warmup =
            _renderWarmup.GetOrCreate(
                () => _renderViewService.BuildSceneAsync(
                    workbench.Workspace,
                    progress: message =>
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (!_disposed)
                            {
                                _workbench?.ReportRenderProgress(
                                    message);
                            }
                        })),
                out bool created);
        if (created)
            _ = ObserveRenderWarmupAsync(warmup, workbench);
        return warmup;
    }

    private async Task ObserveRenderWarmupAsync(
        Task<RenderViewSceneBuildResult> warmup,
        StudioWorkbenchViewModel workbench)
    {
        try
        {
            RenderViewSceneBuildResult result =
                await warmup.ConfigureAwait(false);
            if (!_disposed && _renderWarmup.IsCurrent(warmup))
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!_disposed &&
                        _renderWarmup.IsCurrent(warmup))
                    {
                        workbench.ReportRenderResult(result);
                    }
                });
            }
        }
        catch (OperationCanceledException) when (_disposed)
        {
        }
        catch (Exception exception)
        {
            if (!_disposed && _renderWarmup.IsCurrent(warmup))
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!_disposed &&
                        _renderWarmup.IsCurrent(warmup))
                    {
                        workbench.ReportRenderFailure(exception);
                    }
                });
            }
        }
    }

    private void ThemeMenuItem_Click(object? sender, EventArgs e)
    {
        if (sender is NativeMenuItem { CommandParameter: string value }
            && Enum.TryParse(value, ignoreCase: true, out ThemeMode mode))
        {
            ThemeRequested?.Invoke(mode);
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
                SuggestedFileName = suggestedFileName,
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

        int changedTabCount = workbench.OpenEditorTabs.Count(tab => tab.IsDirty);
        return changedTabCount == 0
            ? SupplementalUnsavedChanges.Clean
            : new SupplementalUnsavedChanges(
                IsDirty: true,
                ChangedItemCount: changedTabCount);
    }

    private void DisposeEditor()
    {
        if (_disposed)
            return;

        _disposed = true;
        _renderViewService.Dispose();
        _gscEngineReferenceWindow?.Close();
        _gscEngineReferenceWindow = null;
        _livePreviewWindows.Clear();
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
