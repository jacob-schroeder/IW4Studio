using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Controls;
using IW4.FastFiles.Zone;
using IW4.Studio.Desktop.Editors;
using IW4.Studio.Desktop.Editors.AssetReferences;
using IW4.Studio.Desktop.Editors.Gsc;
using IW4.Studio.Desktop.Editors.Menu;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Desktop.Workbench.Docking;
using IW4.Studio.Desktop.Workbench.Navigation;
using IW4.Studio.Desktop.Workbench.Selection;
using IW4.Studio.Desktop.Workbench.Tools;
using IW4.Studio.Desktop.Workbench.Tools.AssetPool;
using IW4.Studio.Desktop.Workbench.Tools.ConsoleOutput;
using IW4.Studio.Desktop.Workbench.Tools.Diagnostics;
using IW4.Studio.Desktop.Workbench.Tools.DependencyGraph;
using IW4.Studio.Desktop.Workbench.Tools.FastFileAssets;
using IW4.Studio.Desktop.Workbench.Tools.FastFileDetails;
using IW4.Studio.Desktop.Workbench.Tools.GscFindings;
using IW4.Studio.Desktop.Workbench.Tools.GscUsages;
using IW4.Studio.Desktop.Workbench.Tools.ImageFilePak;
using IW4.Studio.Desktop.Workbench.Tools.MapEditor;
using IW4.Studio.Desktop.Workbench.Tools.MapRender;
using IW4.Studio.Desktop.Workbench.Tools.Properties;
using IW4.Studio.Desktop.Workbench.Tools.ZoneDetails;
using IW4.Studio.Documents;
using IW4.Studio.Documents.AssetReferences;
using IW4.Studio.Documents.MenuEditing;
using IW4.Studio.Gsc;
using IW4.Studio.Rendering;

namespace IW4.Studio.Desktop.Workbench.Composition;

/// <summary>
/// Window-local composition root for the Studio workbench. The editor,
/// navigators, selection, tools, and dock controller remain independently
/// testable; this type only connects their public seams.
/// </summary>
public sealed class StudioWorkbenchViewModel : ObservableObject, IDisposable
{
    private const string LivePreviewDiagnosticSource = "Live Preview";

    private readonly WorkbenchSelectionContext _selectionContext = new();
    private readonly CancellationTokenSource _gscWorkspaceWarmupCancellation = new();
    private readonly WorkbenchAssetSelectionRouter _selectionRouter;
    private readonly GscWorkspaceIndexService _gscWorkspace;
    private readonly Task _gscWorkspaceWarmup;
    private readonly GscSourceNavigationBroker _gscSourceNavigation;
    private readonly GscWorkbenchNavigator _gscWorkbenchNavigator;
    private readonly GscUsagesPresenter _gscUsagesPresenter;
    private readonly IReadOnlyDictionary<string, StudioToolRegistration> _registrationsById;
    private readonly WorkbenchEditorDiagnosticsBridge _editorDiagnosticsBridge;
    private readonly MenuEditingCoordinator _menuEditingCoordinator;
    private readonly MenuTextResourceResolver _menuTextResourceResolver;
    private readonly ObservableCollection<WorkbenchEditorTabViewModel> _openEditorTabs = [];
    private readonly Dictionary<WorkbenchEditorTabKey, WorkbenchEditorTabViewModel>
        _editorTabsByKey = [];
    private WorkbenchEditorTabViewModel? _selectedEditorTab;
    private bool _disposed;

    public StudioWorkbenchViewModel(FastFileWorkspace workspace)
    {
        Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        OpenEditorTabs = new ReadOnlyObservableCollection<WorkbenchEditorTabViewModel>(
            _openEditorTabs);
        var editingSession = new FastFileEditingSession(workspace);
        AssetAuthoringAdapterRegistry authoringRegistry =
            AssetAuthoringAdapterRegistry.CreateDefault();
        _menuEditingCoordinator = new MenuEditingCoordinator(
            editingSession,
            authoringRegistry);
        var assetReferenceCatalog = new WorkspaceAssetReferenceCatalog(
            editingSession);
        var menuMaterialResolver = new MenuPreviewMaterialResolver(workspace);
        _menuTextResourceResolver = new MenuTextResourceResolver(
            editingSession);
        var assetReferencePicker = new AssetReferencePickerService(
            assetReferenceCatalog,
            menuMaterialResolver);
        _gscWorkspace = new GscWorkspaceIndexService(editingSession);
        _gscSourceNavigation = new GscSourceNavigationBroker();
        GscUsages = new GscUsagesToolViewModel();
        _gscUsagesPresenter = new GscUsagesPresenter(
            GscUsages,
            _gscSourceNavigation);
        AssetEditorViewRegistry editorViewRegistry =
            AssetEditorViewRegistry.CreateDefault(
                _gscWorkspace,
                _gscSourceNavigation,
                _gscUsagesPresenter);
        editorViewRegistry.Register(new MenuEditorViewFactory(
            _menuEditingCoordinator,
            assetReferencePicker,
            menuMaterialResolver,
            _menuTextResourceResolver));
        editorViewRegistry.Register(new MenuFileEditorViewFactory(
            _menuEditingCoordinator,
            assetReferencePicker,
            menuMaterialResolver,
            _menuTextResourceResolver));
        Editor = new EditorViewModel(
            workspace,
            editingSession,
            authoringRegistry,
            viewRegistry: editorViewRegistry);
        _selectionRouter = new WorkbenchAssetSelectionRouter(
            workspace.AssetCatalog,
            Editor.EditingSession.Document);
        Func<XAssetType, bool> hasDesktopEditor = assetType =>
            editorViewRegistry.TryGetFactory(assetType, out _);

        FastFileAssets = new FastFileAssetsNavigatorViewModel(
            Editor,
            _selectionContext,
            hasDesktopEditor);
        AssetPool = new AssetPoolNavigatorViewModel(
            workspace,
            _selectionContext,
            hasDesktopEditor);
        _gscWorkbenchNavigator = new GscWorkbenchNavigator(
            workspace,
            _gscWorkspace,
            FastFileAssets,
            AssetPool,
            Editor);
        ImageFilePak = new ImageFilePakToolViewModel(
            workspace,
            _selectionContext);
        ConsoleOutput = new ConsoleOutputBuffer();
        Diagnostics = new DiagnosticsAggregator();
        GscFindings = new GscFindingsToolViewModel();
        _editorDiagnosticsBridge =
            new WorkbenchEditorDiagnosticsBridge(
                Editor,
                Diagnostics,
                GscFindings);
        LivePreview = new MapRenderToolViewModel(workspace);
        MapEditor = new MapEditorToolViewModel(
            workspace,
            Editor.EditingSession);
        Properties = new PropertiesToolViewModel(
            _selectionContext,
            ImageFilePak);
        FastFileDetails = new FastFileDetailsToolViewModel(workspace);
        ZoneDetails = new ZoneDetailsToolViewModel(workspace);
        DependencyGraph = new DependencyGraphToolViewModel(workspace);

        StudioToolContext toolContext = new(
            FastFileAssets,
            AssetPool,
            ImageFilePak,
            ConsoleOutput,
            Diagnostics,
            GscFindings,
            GscUsages,
            LivePreview,
            MapEditor,
            Properties,
            FastFileDetails,
            ZoneDetails,
            DependencyGraph);
        Registrations = StudioToolRegistry.CreateDefault(toolContext);
        _registrationsById = Registrations.ToDictionary(
            registration => registration.Descriptor.Id,
            StringComparer.Ordinal);
        DockLayout = new DockLayoutController(
            Registrations.Select(registration => registration.Descriptor),
            new DockLayoutOptions(
                new DockRegionSizeLimits(minimum: 240, maximum: 520, initial: 300),
                new DockRegionSizeLimits(minimum: 140, maximum: 420, initial: 220),
                new DockRegionSizeLimits(minimum: 260, maximum: 520, initial: 320)));

        _selectionContext.SelectionChanged += SelectionContext_SelectionChanged;
        _gscSourceNavigation.NavigationRequested +=
            GscSourceNavigation_NavigationRequested;
        _gscSourceNavigation.EngineBuiltInNavigationRequested +=
            GscSourceNavigation_EngineBuiltInNavigationRequested;
        _gscUsagesPresenter.PresentationRequested +=
            GscUsagesPresenter_PresentationRequested;
        _editorDiagnosticsBridge.GscFindingsPresented +=
            EditorDiagnosticsBridge_GscFindingsPresented;
        Editor.PropertyChanged += Editor_PropertyChanged;
        Editor.EditingSession.TargetRowsChanged += EditingSession_TargetRowsChanged;
        DockLayout.State.Left.PropertyChanged += DockRegion_PropertyChanged;
        DockLayout.State.Bottom.PropertyChanged += DockRegion_PropertyChanged;
        DockLayout.State.Right.PropertyChanged += DockRegion_PropertyChanged;

        AppendInitialOutput();
        Diagnostics.ReplaceBySource(
            "Workspace",
            [
                new WorkbenchDiagnostic(
                    "catalog-ready",
                    WorkbenchDiagnosticSeverity.Information,
                    "Workspace",
                    $"Workspace catalog loaded with {Editor.AssetCountText} entries.")
            ]);

        CancellationToken warmupCancellation =
            _gscWorkspaceWarmupCancellation.Token;
        _gscWorkspaceWarmup = ObserveGscWorkspaceWarmupAsync(
            _gscWorkspace.WarmBaseSnapshotAsync(warmupCancellation),
            warmupCancellation);
    }

    public event EventHandler? LivePreviewRequested
    {
        add => LivePreview.LaunchRequested += value;
        remove => LivePreview.LaunchRequested -= value;
    }

    [Obsolete("Use LivePreviewRequested.")]
    public event EventHandler? MapRenderRequested
    {
        add => LivePreview.LaunchRequested += value;
        remove => LivePreview.LaunchRequested -= value;
    }

    public event EventHandler? MapEditorRequested
    {
        add => MapEditor.LaunchRequested += value;
        remove => MapEditor.LaunchRequested -= value;
    }

    public event EventHandler<GscEngineBuiltInNavigationRequestedEventArgs>?
        EngineBuiltInReferenceRequested;

    public event EventHandler<WorkbenchEditorTabCloseRequestedEventArgs>?
        EditorTabCloseRequested;

    public event EventHandler<WorkbenchEditorTabsCloseRequestedEventArgs>?
        EditorTabsCloseRequested;

    public FastFileWorkspace Workspace { get; }

    public ReadOnlyObservableCollection<WorkbenchEditorTabViewModel>
        OpenEditorTabs { get; }

    public WorkbenchEditorTabViewModel? SelectedEditorTab
    {
        get => _selectedEditorTab;
        set
        {
            if (value is not null &&
                !ReferenceEquals(_selectedEditorTab, value))
            {
                ActivateEditorTab(value);
            }
        }
    }

    public bool HasOpenEditorTabs => OpenEditorTabs.Count != 0;

    public bool HasNoOpenEditorTabs => !HasOpenEditorTabs;

    public EditorViewModel Editor { get; }

    public FastFileAssetsNavigatorViewModel FastFileAssets { get; }

    public AssetPoolNavigatorViewModel AssetPool { get; }

    public ImageFilePakToolViewModel ImageFilePak { get; }

    public ConsoleOutputBuffer ConsoleOutput { get; }

    public DiagnosticsAggregator Diagnostics { get; }

    public GscFindingsToolViewModel GscFindings { get; }

    public GscUsagesToolViewModel GscUsages { get; }

    public MapRenderToolViewModel LivePreview { get; }

    [Obsolete("Use LivePreview.")]
    public MapRenderToolViewModel MapRender => LivePreview;

    public MapEditorToolViewModel MapEditor { get; }

    public PropertiesToolViewModel Properties { get; }

    public FastFileDetailsToolViewModel FastFileDetails { get; }

    public ZoneDetailsToolViewModel ZoneDetails { get; }

    public DependencyGraphToolViewModel DependencyGraph { get; }

    public IReadOnlyList<StudioToolRegistration> Registrations { get; }

    public DockLayoutController DockLayout { get; }

    public IReadOnlyList<DockToolState> LeftTools => DockLayout.State.Left.Tools;

    public IReadOnlyList<DockToolState> BottomTools => DockLayout.State.Bottom.Tools;

    public IReadOnlyList<DockToolState> RightTools => DockLayout.State.Right.Tools;

    public bool IsLeftOpen => DockLayout.State.Left.IsOpen;

    public bool IsBottomOpen => DockLayout.State.Bottom.IsOpen;

    public bool IsRightOpen => DockLayout.State.Right.IsOpen;

    public double LeftSize => DockLayout.State.Left.Size;

    public double BottomSize => DockLayout.State.Bottom.Size;

    public double RightSize => DockLayout.State.Right.Size;

    public GridLength LeftPaneWidth =>
        IsLeftOpen ? new GridLength(LeftSize) : new GridLength(0);

    public GridLength BottomPaneHeight =>
        IsBottomOpen ? new GridLength(BottomSize) : new GridLength(0);

    public GridLength RightPaneWidth =>
        IsRightOpen ? new GridLength(RightSize) : new GridLength(0);

    public string LeftTitle => DockLayout.State.Left.ActiveTool?.Title ?? string.Empty;

    public string BottomTitle => DockLayout.State.Bottom.ActiveTool?.Title ?? string.Empty;

    public string RightTitle => DockLayout.State.Right.ActiveTool?.Title ?? string.Empty;

    public string? LeftActiveToolId => DockLayout.State.Left.ActiveToolId;

    public string? BottomActiveToolId => DockLayout.State.Bottom.ActiveToolId;

    public string? RightActiveToolId => DockLayout.State.Right.ActiveToolId;

    public Control? ActiveLeftContent => ContentFor(DockLayout.State.Left.ActiveToolId);

    public Control? ActiveBottomContent => ContentFor(DockLayout.State.Bottom.ActiveToolId);

    public Control? ActiveRightContent => ContentFor(DockLayout.State.Right.ActiveToolId);

    public bool HasSelection => SelectedEditorTab is not null;

    public bool HasNoSelection => !HasSelection;

    public bool HasEditorFallback =>
        HasSelection && !HasSelectedHostedView;

    public Control? SelectedHostedView =>
        SelectedEditorTab?.HostedView;

    public bool HasSelectedHostedView => SelectedHostedView is not null;

    public string SelectedName =>
        SelectedEditorTab?.Title ?? string.Empty;

    public string SelectedKind =>
        SelectedEditorTab?.Kind ?? string.Empty;

    public string SelectedAccessBadge =>
        SelectedEditorTab?.Selection.Source switch
        {
            WorkbenchAssetSelectionSource.AssetPool =>
                "RUNTIME INSPECTION",
            WorkbenchAssetSelectionSource.ImageFilePak =>
                "READ ONLY",
            _ => SelectedEditorTab?.AccessBadge ?? string.Empty
        };

    public string SelectedProviderZone =>
        SelectedEditorTab?.ProviderZone ?? string.Empty;

    public string EditorFallbackHeading =>
        SelectedEditorTab?.Route is { IsResolved: false }
            ? "No workspace editor target was found"
            : SelectedEditorTab?.Route is { OpensCatalogEditor: false } &&
              SelectedEditorTab?.Selection.Source == WorkbenchAssetSelectionSource.AssetPool
                ? "Runtime preview is not implemented yet"
                : "No editor exists for this resource yet";

    public string EditorFallbackMessage =>
        !string.IsNullOrWhiteSpace(SelectedEditorTab?.Route?.UnavailableReason)
            ? SelectedEditorTab!.Route!.UnavailableReason!
            : string.IsNullOrWhiteSpace(SelectedKind)
            ? "This resource can still be inspected in the Properties tool."
            : $"A {SelectedKind} editor has not been implemented in IW4 Studio. " +
              "The resource remains available for metadata inspection.";

    public string TargetFileName => Editor.TargetFileName;

    public string TargetPath => Editor.TargetPath;

    public string ModeName => Editor.ModeName;

    public string SearchResultText => Editor.SearchResultText;

    public string AssetCountText => Editor.AssetCountText;

    public string TargetRowCountText => Editor.TargetRowCountText;

    public string DependencyAssetCountText => Editor.DependencyAssetCountText;

    public bool CanSaveAs => Editor.CanSaveAs;

    public DockActivationResult ActivateTool(string toolId)
    {
        DockActivationResult result = DockLayout.ActivateTool(toolId);
        switch (result)
        {
            case DockActivationResult.Opened:
            case DockActivationResult.Switched:
                if (toolId == StudioToolIds.MapEditor)
                    _ = MapEditor.EnsurePrepared();

                ConsoleOutput.Append(
                    ConsoleOutputLevel.Debug,
                    "Workbench",
                    $"Opened {_registrationsById[toolId].Descriptor.Title}.");
                break;
            case DockActivationResult.Collapsed:
                ConsoleOutput.Append(
                    ConsoleOutputLevel.Debug,
                    "Workbench",
                    $"Collapsed {_registrationsById[toolId].Descriptor.Title}.");
                break;
        }

        return result;
    }

    public bool CollapseRegion(DockRegion region) =>
        DockLayout.CollapseRegion(region);

    public double ResizeRegion(DockRegion region, double size) =>
        DockLayout.ResizeRegion(region, size);

    public DockMoveResult MoveTool(
        string toolId,
        DockRegion region,
        int targetIndex) =>
        DockLayout.MoveTool(toolId, region, targetIndex);

    public void RefreshAfterSave() =>
        Editor.RefreshAfterSave();

    public void RequestCloseEditorTab(WorkbenchEditorTabViewModel tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (!_openEditorTabs.Contains(tab))
            return;

        EditorTabCloseRequested?.Invoke(
            this,
            new WorkbenchEditorTabCloseRequestedEventArgs(tab));
    }

    public void RequestCloseOtherEditorTabs(WorkbenchEditorTabViewModel tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (!_openEditorTabs.Contains(tab))
            return;

        RequestCloseEditorTabs(
            _openEditorTabs
                .Where(candidate => !ReferenceEquals(candidate, tab))
                .ToArray());
    }

    public void RequestCloseAllEditorTabs() =>
        RequestCloseEditorTabs(_openEditorTabs.ToArray());

    internal bool CloseEditorTab(WorkbenchEditorTabViewModel tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        int closingIndex = _openEditorTabs.IndexOf(tab);
        if (closingIndex < 0)
            return false;

        bool wasSelected = ReferenceEquals(_selectedEditorTab, tab);
        WorkbenchAssetSelection closingSelection = tab.Selection;
        if (wasSelected)
        {
            _selectedEditorTab = null;
            OnPropertyChanged(nameof(SelectedEditorTab));
        }

        _editorTabsByKey.Remove(tab.Key);
        _openEditorTabs.RemoveAt(closingIndex);
        if (tab.CatalogEditor is { } catalogEditor)
            Editor.CloseEditor(catalogEditor.Entry.Identity);
        tab.Dispose();
        OnPropertyChanged(nameof(HasOpenEditorTabs));
        OnPropertyChanged(nameof(HasNoOpenEditorTabs));

        if (!wasSelected)
            return true;

        if (_openEditorTabs.Count != 0)
        {
            int replacementIndex = Math.Min(
                closingIndex,
                _openEditorTabs.Count - 1);
            ActivateEditorTab(_openEditorTabs[replacementIndex]);
            return true;
        }

        Editor.DeactivateSelection();
        if (Equals(_selectionContext.Current, closingSelection))
            _selectionContext.Clear(closingSelection.Source);
        NotifyCenterSelectionChanged();
        return true;
    }

    private void RequestCloseEditorTabs(
        IReadOnlyList<WorkbenchEditorTabViewModel> tabs)
    {
        if (tabs.Count == 0)
            return;

        EditorTabsCloseRequested?.Invoke(
            this,
            new WorkbenchEditorTabsCloseRequestedEventArgs(tabs));
    }

    private void ActivateEditorTab(WorkbenchEditorTabViewModel tab)
    {
        if (!_openEditorTabs.Contains(tab))
            return;

        _selectionContext.Select(tab.Selection);
    }

    public void ReportRenderProgress(string message)
    {
        LivePreview.ReportProgress(message);
        ConsoleOutput.Append(
            ConsoleOutputLevel.Debug,
            "Live Preview",
            message);
    }

    public void ReportRenderResult(RenderViewSceneBuildResult result)
    {
        LivePreview.ReportResult(result);
        if (result.IsRenderable)
        {
            ConsoleOutput.Append(
                ConsoleOutputLevel.Information,
                "Live Preview",
                "Render scene ready.");
            Diagnostics.ReplaceBySource(
                LivePreviewDiagnosticSource,
                [
                    new WorkbenchDiagnostic(
                        "scene-ready",
                        WorkbenchDiagnosticSeverity.Information,
                        LivePreviewDiagnosticSource,
                        "Render scene ready.")
                ]);
        }
        else
        {
            string reason = result.NonRenderableReason
                ?? "No renderable map assets were found.";
            ConsoleOutput.Append(
                ConsoleOutputLevel.Warning,
                "Live Preview",
                reason);
            Diagnostics.ReplaceBySource(
                LivePreviewDiagnosticSource,
                [
                    new WorkbenchDiagnostic(
                        "scene-unavailable",
                        WorkbenchDiagnosticSeverity.Warning,
                        LivePreviewDiagnosticSource,
                        reason)
                ]);
        }
    }

    public void ReportRenderFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        LivePreview.ReportFailure(exception);
        ConsoleOutput.Append(
            ConsoleOutputLevel.Error,
            "Live Preview",
            exception.Message);
        Diagnostics.ReplaceBySource(
            LivePreviewDiagnosticSource,
            [
                new WorkbenchDiagnostic(
                    "scene-failed",
                    WorkbenchDiagnosticSeverity.Error,
                    LivePreviewDiagnosticSource,
                    exception.Message)
            ]);
    }

    public void LogSaveResult(SaveAsResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        ConsoleOutput.Append(
            result.Succeeded
                ? ConsoleOutputLevel.Information
                : result.Cancelled
                    ? ConsoleOutputLevel.Warning
                    : ConsoleOutputLevel.Error,
            "Save As",
            result.Succeeded
                ? "Fastfile saved successfully."
                : result.Cancelled
                    ? "Save As was cancelled."
                    : string.Join(
                        " ",
                        result.Diagnostics.DefaultIfEmpty(
                            "Save As was blocked.")));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (!_gscWorkspaceWarmup.IsCompleted)
            _gscWorkspaceWarmupCancellation.Cancel();
        _selectionContext.SelectionChanged -= SelectionContext_SelectionChanged;
        _gscSourceNavigation.NavigationRequested -=
            GscSourceNavigation_NavigationRequested;
        _gscSourceNavigation.EngineBuiltInNavigationRequested -=
            GscSourceNavigation_EngineBuiltInNavigationRequested;
        _gscUsagesPresenter.PresentationRequested -=
            GscUsagesPresenter_PresentationRequested;
        _editorDiagnosticsBridge.GscFindingsPresented -=
            EditorDiagnosticsBridge_GscFindingsPresented;
        Editor.PropertyChanged -= Editor_PropertyChanged;
        Editor.EditingSession.TargetRowsChanged -= EditingSession_TargetRowsChanged;
        DockLayout.State.Left.PropertyChanged -= DockRegion_PropertyChanged;
        DockLayout.State.Bottom.PropertyChanged -= DockRegion_PropertyChanged;
        DockLayout.State.Right.PropertyChanged -= DockRegion_PropertyChanged;
        _editorDiagnosticsBridge.Dispose();
        _gscUsagesPresenter.Dispose();
        FastFileAssets.Dispose();
        AssetPool.Dispose();
        ImageFilePak.Dispose();
        MapEditor.Dispose();
        LivePreview.Dispose();
        Properties.Dispose();
        Diagnostics.Dispose();
        foreach (WorkbenchEditorTabViewModel tab in _openEditorTabs)
            tab.Dispose();
        _openEditorTabs.Clear();
        _editorTabsByKey.Clear();
        _menuEditingCoordinator.Dispose();
        _menuTextResourceResolver.Dispose();
        Editor.Dispose();
        _gscWorkspaceWarmupCancellation.Dispose();
    }

    private Control? ContentFor(string? toolId) =>
        toolId is not null &&
        _registrationsById.TryGetValue(toolId, out StudioToolRegistration? registration)
            ? registration.Content
            : null;

    private async Task ObserveGscWorkspaceWarmupAsync(
        Task<GscWorkspaceSnapshot> warmup,
        CancellationToken cancellationToken)
    {
        try
        {
            await warmup;
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!_disposed)
            {
                ConsoleOutput.Append(
                    ConsoleOutputLevel.Warning,
                    "GSC",
                    $"Workspace index warm-up failed; the next GSC request will retry. {exception.Message}");
            }
        }
    }

    private void EditorDiagnosticsBridge_GscFindingsPresented(
        object? sender,
        EventArgs args)
    {
        if (!_disposed &&
            DockLayout.State.Bottom.ActiveToolId != StudioToolIds.GscFindings)
        {
            _ = ActivateTool(StudioToolIds.GscFindings);
        }
    }

    private void GscUsagesPresenter_PresentationRequested(
        object? sender,
        EventArgs args)
    {
        if (!_disposed &&
            DockLayout.State.Bottom.ActiveToolId != StudioToolIds.GscUsages)
        {
            _ = ActivateTool(StudioToolIds.GscUsages);
        }
    }

    private void GscSourceNavigation_NavigationRequested(
        object? sender,
        GscSourceNavigationRequestedEventArgs args)
    {
        if (_disposed)
            return;

        string? failureReason = _gscWorkbenchNavigator.NavigateTo(args.Location);
        if (failureReason is not null)
        {
            ConsoleOutput.Append(
                ConsoleOutputLevel.Warning,
                "GSC navigation",
                $"Could not navigate to '{args.Location.Path}': {failureReason}");
        }
    }

    private void GscSourceNavigation_EngineBuiltInNavigationRequested(
        object? sender,
        GscEngineBuiltInNavigationRequestedEventArgs args)
    {
        if (!_disposed)
            EngineBuiltInReferenceRequested?.Invoke(this, args);
    }

    private void AppendInitialOutput()
    {
        ConsoleOutput.Append(
            ConsoleOutputLevel.Information,
            "IW4 Studio",
            $"Workspace: {TargetPath}");
        ConsoleOutput.Append(
            ConsoleOutputLevel.Information,
            "Catalog",
            $"Indexed {AssetCountText} catalog entries.");
        ConsoleOutput.Append(
            ConsoleOutputLevel.Information,
            "Catalog",
            $"Indexed {TargetRowCountText} target rows.");
        ConsoleOutput.Append(
            ConsoleOutputLevel.Information,
            "Asset Pool",
            $"Captured {AssetPool.TotalCount:N0} runtime slots at revision {AssetPool.SnapshotRevision:N0}.");
        ConsoleOutput.Append(
            ConsoleOutputLevel.Information,
            "Workspace",
            "Workspace catalog loaded.");
    }

    private void SelectionContext_SelectionChanged(
        object? sender,
        WorkbenchSelectionChangedEventArgs args)
    {
        WorkbenchAssetSelection? selection = args.Current;
        if (selection is null)
        {
            NotifyCenterSelectionChanged();
            return;
        }

        WorkbenchAssetSelectionRoute? route =
            selection.Source == WorkbenchAssetSelectionSource.ImageFilePak
                ? null
                : _selectionRouter.Resolve(selection);
        AssetEditorHostViewModel? catalogEditor = null;

        if (route is
            {
                CatalogEntry: { } entry,
                OpensCatalogEditor: true
            })
        {
            catalogEditor = Editor.SelectEntry(
                AssetExplorerItemIdentity.From(entry));
        }
        else
        {
            Editor.DeactivateSelection();
        }

        WorkbenchEditorTabKey key =
            WorkbenchEditorTabKey.Create(selection, route);
        if (!_editorTabsByKey.TryGetValue(
                key,
                out WorkbenchEditorTabViewModel? tab))
        {
            Control? standaloneView = null;
            ImageFilePakEntryViewModel? streamedImage = null;
            IDisposable? ownedContent = null;
            if (selection.Identity.StreamedImageIdentity is { } imageIdentity)
            {
                streamedImage = ImageFilePak.RequireEntry(imageIdentity);
                var preview = new ImageFilePakPreviewViewModel(streamedImage);
                standaloneView = new ImageFilePakPreviewView
                {
                    DataContext = preview
                };
                ownedContent = preview;
            }

            tab = new WorkbenchEditorTabViewModel(
                key,
                selection,
                route,
                catalogEditor,
                standaloneView,
                streamedImage,
                ownedContent);
            _editorTabsByKey.Add(key, tab);
            _openEditorTabs.Add(tab);
            OnPropertyChanged(nameof(HasOpenEditorTabs));
            OnPropertyChanged(nameof(HasNoOpenEditorTabs));
        }
        else
        {
            tab.UpdateSelection(selection, route);
        }

        SetSelectedEditorTab(tab);

        if (selection.Source == WorkbenchAssetSelectionSource.ImageFilePak)
        {
            ConsoleOutput.Append(
                ConsoleOutputLevel.Information,
                "Imagefile.pak",
                $"Selected streamed image '{selection.DisplayName}'.");
        }
        else if (route?.OpensCatalogEditor == true)
        {
            ConsoleOutput.Append(
                ConsoleOutputLevel.Information,
                selection.Source == WorkbenchAssetSelectionSource.AssetPool
                    ? "Asset Pool"
                    : "Fastfile Assets",
                $"Selected {selection.AssetType} '{selection.DisplayName}'.");
        }
        else
        {
            ConsoleOutput.Append(
                ConsoleOutputLevel.Warning,
                "Workbench",
                route?.UnavailableReason ??
                $"Could not resolve {selection.AssetType} '{selection.DisplayName}' into the workspace catalog.");
        }

        NotifyCenterSelectionChanged();
    }

    private void SetSelectedEditorTab(WorkbenchEditorTabViewModel tab)
    {
        if (ReferenceEquals(_selectedEditorTab, tab))
            return;

        _selectedEditorTab = tab;
        OnPropertyChanged(nameof(SelectedEditorTab));
    }

    private void Editor_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(EditorViewModel.SelectedEditorHost))
        {
            if (ReferenceEquals(
                    SelectedEditorTab?.CatalogEditor,
                    Editor.SelectedEditorHost))
            {
                NotifyCenterSelectionChanged();
            }
        }
        else if (args.PropertyName == nameof(EditorViewModel.CanSaveAs))
        {
            OnPropertyChanged(nameof(CanSaveAs));
        }
        else if (args.PropertyName == nameof(EditorViewModel.SearchResultText))
        {
            OnPropertyChanged(nameof(SearchResultText));
        }
        else if (args.PropertyName == nameof(EditorViewModel.AssetCountText))
        {
            OnPropertyChanged(nameof(AssetCountText));
        }
        else if (args.PropertyName == nameof(EditorViewModel.TargetRowCountText))
        {
            OnPropertyChanged(nameof(TargetRowCountText));
        }
        else if (args.PropertyName == nameof(EditorViewModel.DependencyAssetCountText))
        {
            OnPropertyChanged(nameof(DependencyAssetCountText));
        }
    }

    private void EditingSession_TargetRowsChanged(
        object? sender,
        EventArgs args)
    {
        if (_disposed)
            return;

        HashSet<TargetZoneRowIdentity> liveRows = Editor.EditingSession.Document.Rows
            .Select(entry => entry.TargetRowIdentity ??
                throw new InvalidDataException(
                    "A live authoring row has no stable target-row identity."))
            .ToHashSet();
        WorkbenchEditorTabViewModel[] removedRowTabs = _openEditorTabs
            .Where(tab =>
                tab.Selection.Identity.TargetRowIdentity is { } identity &&
                identity.DocumentId == Editor.EditingSession.Document.DocumentId &&
                !liveRows.Contains(identity))
            .ToArray();
        foreach (WorkbenchEditorTabViewModel tab in removedRowTabs)
            CloseEditorTab(tab);
    }

    private void NotifyCenterSelectionChanged()
    {
        Properties.SetDocumentSelection(
            SelectedEditorTab?.Selection,
            SelectedEditorTab?.StreamedImage);
        Properties.SetEditorSource(
            SelectedEditorTab?.CatalogEditor?.HostedViewModel);
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasNoSelection));
        OnPropertyChanged(nameof(HasEditorFallback));
        OnPropertyChanged(nameof(SelectedHostedView));
        OnPropertyChanged(nameof(HasSelectedHostedView));
        OnPropertyChanged(nameof(SelectedName));
        OnPropertyChanged(nameof(SelectedKind));
        OnPropertyChanged(nameof(SelectedAccessBadge));
        OnPropertyChanged(nameof(SelectedProviderZone));
        OnPropertyChanged(nameof(EditorFallbackHeading));
        OnPropertyChanged(nameof(EditorFallbackMessage));
    }

    private void DockRegion_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs args)
    {
        if (sender is not DockRegionState region)
            return;

        switch (region.Region)
        {
            case DockRegion.Left:
                OnPropertyChanged(nameof(IsLeftOpen));
                OnPropertyChanged(nameof(LeftSize));
                OnPropertyChanged(nameof(LeftPaneWidth));
                OnPropertyChanged(nameof(LeftTitle));
                OnPropertyChanged(nameof(LeftActiveToolId));
                OnPropertyChanged(nameof(ActiveLeftContent));
                break;
            case DockRegion.Bottom:
                OnPropertyChanged(nameof(IsBottomOpen));
                OnPropertyChanged(nameof(BottomSize));
                OnPropertyChanged(nameof(BottomPaneHeight));
                OnPropertyChanged(nameof(BottomTitle));
                OnPropertyChanged(nameof(BottomActiveToolId));
                OnPropertyChanged(nameof(ActiveBottomContent));
                break;
            case DockRegion.Right:
                OnPropertyChanged(nameof(IsRightOpen));
                OnPropertyChanged(nameof(RightSize));
                OnPropertyChanged(nameof(RightPaneWidth));
                OnPropertyChanged(nameof(RightTitle));
                OnPropertyChanged(nameof(RightActiveToolId));
                OnPropertyChanged(nameof(ActiveRightContent));
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}
