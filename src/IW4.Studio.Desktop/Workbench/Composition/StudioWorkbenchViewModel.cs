using System.ComponentModel;
using Avalonia.Controls;
using IW4.Studio.Desktop.Editors;
using IW4.Studio.Desktop.Editors.Gsc;
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
    private readonly Control _imageFilePakPreviewView;
    private WorkbenchAssetSelection? _currentSelection;
    private WorkbenchAssetSelectionRoute? _currentRoute;
    private bool _disposed;

    public StudioWorkbenchViewModel(FastFileWorkspace workspace)
    {
        Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _gscWorkspace = new GscWorkspaceIndexService(workspace);
        _gscSourceNavigation = new GscSourceNavigationBroker();
        GscUsages = new GscUsagesToolViewModel();
        _gscUsagesPresenter = new GscUsagesPresenter(
            GscUsages,
            _gscSourceNavigation);
        Editor = new EditorViewModel(
            workspace,
            viewRegistry: AssetEditorViewRegistry.CreateDefault(
                _gscWorkspace,
                _gscSourceNavigation,
                _gscUsagesPresenter));
        _selectionRouter = new WorkbenchAssetSelectionRouter(
            workspace.AssetCatalog);

        FastFileAssets = new FastFileAssetsNavigatorViewModel(
            workspace,
            _selectionContext);
        AssetPool = new AssetPoolNavigatorViewModel(
            workspace,
            _selectionContext);
        _gscWorkbenchNavigator = new GscWorkbenchNavigator(
            workspace,
            _gscWorkspace,
            FastFileAssets,
            AssetPool,
            Editor);
        ImageFilePak = new ImageFilePakToolViewModel(
            workspace,
            _selectionContext);
        _imageFilePakPreviewView = new ImageFilePakPreviewView
        {
            DataContext = ImageFilePak
        };
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
        _gscUsagesPresenter.PresentationRequested +=
            GscUsagesPresenter_PresentationRequested;
        _editorDiagnosticsBridge.GscFindingsPresented +=
            EditorDiagnosticsBridge_GscFindingsPresented;
        Editor.PropertyChanged += Editor_PropertyChanged;
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

    public FastFileWorkspace Workspace { get; }

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

    public bool HasSelection => _currentSelection is not null;

    public bool HasNoSelection => !HasSelection;

    public bool HasEditorFallback =>
        HasSelection && !HasSelectedHostedView;

    public Control? SelectedHostedView =>
        _currentSelection?.Source ==
        WorkbenchAssetSelectionSource.ImageFilePak
            ? _imageFilePakPreviewView
            : _currentRoute?.OpensCatalogEditor == true
                ? Editor.SelectedHostedView
                : null;

    public bool HasSelectedHostedView => SelectedHostedView is not null;

    public string SelectedName =>
        _currentSelection?.DisplayName ?? string.Empty;

    public string SelectedKind =>
        _currentSelection?.AssetType.ToString() ?? string.Empty;

    public string SelectedAccessBadge =>
        _currentSelection?.Source switch
        {
            WorkbenchAssetSelectionSource.AssetPool =>
                "RUNTIME INSPECTION",
            WorkbenchAssetSelectionSource.ImageFilePak =>
                "READ ONLY",
            _ => Editor.SelectedAccessBadge
        };

    public string SelectedProviderZone =>
        _currentSelection?.ProviderZone
        ?? Editor.SelectedProviderZone;

    public string EditorFallbackHeading =>
        _currentRoute is { IsResolved: false }
            ? "No workspace editor target was found"
            : _currentRoute is { OpensCatalogEditor: false } &&
              _currentSelection?.Source == WorkbenchAssetSelectionSource.AssetPool
                ? "Runtime preview is not implemented yet"
                : "No editor exists for this resource yet";

    public string EditorFallbackMessage =>
        !string.IsNullOrWhiteSpace(_currentRoute?.UnavailableReason)
            ? _currentRoute!.UnavailableReason!
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

    public void RefreshSaveAvailability() =>
        Editor.RefreshSaveAvailability();

    public void CloseCurrentSelection()
    {
        if (_currentSelection is { } selection)
            _selectionContext.Clear(selection.Source);
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
        _gscUsagesPresenter.PresentationRequested -=
            GscUsagesPresenter_PresentationRequested;
        _editorDiagnosticsBridge.GscFindingsPresented -=
            EditorDiagnosticsBridge_GscFindingsPresented;
        Editor.PropertyChanged -= Editor_PropertyChanged;
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
        _currentSelection = selection;
        _currentRoute = selection is not null &&
                        selection.Source !=
                        WorkbenchAssetSelectionSource.ImageFilePak
            ? _selectionRouter.Resolve(selection)
            : null;

        if (selection is null)
        {
            Editor.CloseSelectedTab();
            NotifyCenterSelectionChanged();
            return;
        }

        if (selection.Source ==
            WorkbenchAssetSelectionSource.ImageFilePak)
        {
            Editor.CloseSelectedTab();
            ConsoleOutput.Append(
                ConsoleOutputLevel.Information,
                "Imagefile.pak",
                $"Selected streamed image '{selection.DisplayName}'.");
            NotifyCenterSelectionChanged();
            return;
        }

        if (_currentRoute is
            {
                CatalogEntry: { } entry,
                OpensCatalogEditor: true
            })
        {
            Editor.SelectEntry(AssetExplorerItemIdentity.From(entry));
            ConsoleOutput.Append(
                ConsoleOutputLevel.Information,
                selection.Source == WorkbenchAssetSelectionSource.AssetPool
                    ? "Asset Pool"
                    : "Fastfile Assets",
                $"Selected {selection.AssetType} '{selection.DisplayName}'.");
        }
        else
        {
            Editor.CloseSelectedTab();
            ConsoleOutput.Append(
                ConsoleOutputLevel.Warning,
                "Workbench",
                _currentRoute?.UnavailableReason ??
                $"Could not resolve {selection.AssetType} '{selection.DisplayName}' into the workspace catalog.");
        }

        NotifyCenterSelectionChanged();
    }

    private void Editor_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(EditorViewModel.SelectedTab))
        {
            NotifyCenterSelectionChanged();
        }
        else if (args.PropertyName == nameof(EditorViewModel.CanSaveAs))
        {
            OnPropertyChanged(nameof(CanSaveAs));
        }
        else if (args.PropertyName == nameof(EditorViewModel.SearchResultText))
        {
            OnPropertyChanged(nameof(SearchResultText));
        }
    }

    private void NotifyCenterSelectionChanged()
    {
        Properties.SetEditorPropertiesSource(
            _currentRoute?.OpensCatalogEditor == true
                ? Editor.SelectedTab?.HostedViewModel as IAssetEditorProperties
                : null);
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
