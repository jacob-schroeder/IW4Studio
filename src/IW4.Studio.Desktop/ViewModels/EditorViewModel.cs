using Avalonia.Controls;
using IW4.Studio.Desktop.Editors;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.ViewModels;

/// <summary>
/// Catalog-driven Desktop explorer and editor host. Target ownership is read
/// exclusively from <see cref="WorkspaceAssetCatalog"/>; runtime pool state
/// is never enumerated for explorer rows or authoring decisions.
/// </summary>
public sealed class EditorViewModel : ObservableObject, IDisposable
{
    private readonly AssetAuthoringAdapterRegistry _authoringRegistry;
    private readonly AssetEditorViewRegistry _viewRegistry;
    private readonly AssetExplorerEntryViewModel[] _allEntries;
    private readonly IReadOnlyDictionary<AssetExplorerItemIdentity, AssetExplorerEntryViewModel> _entriesByIdentity;
    private readonly Dictionary<AssetExplorerItemIdentity, AssetExplorerTabViewModel> _tabs = [];
    private string _searchText = string.Empty;
    private IReadOnlyList<AssetTreeNode> _assetGroups = Array.Empty<AssetTreeNode>();
    private AssetTreeNode? _selectedNode;
    private AssetExplorerTabViewModel? _selectedTab;
    private AssetExplorerItemIdentity? _selectedIdentity;
    private int _visibleAssetCount;
    private bool _suppressTreeSelection;
    private bool _disposed;

    public EditorViewModel(
        FastFileWorkspace workspace,
        AssetAuthoringAdapterRegistry? authoringRegistry = null,
        AssetEditorViewRegistry? viewRegistry = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        Workspace = workspace;
        EditingSession = new FastFileEditingSession(workspace);
        _authoringRegistry = authoringRegistry ?? AssetAuthoringAdapterRegistry.CreateDefault();
        _viewRegistry = viewRegistry ?? AssetEditorViewRegistry.CreateDefault();
        TargetFileName = Path.GetFileName(workspace.Document.Request.Path);
        TargetPath = Path.GetFullPath(workspace.Document.Request.Path);
        ModeName = workspace.ZonePlanProfileName is null
            ? "Single file"
            : $"{workspace.ZonePlanProfileName}.elf dependencies";
        ActiveZoneCount = workspace.ActiveZones.Count;
        LoadedZoneCount = workspace.LoadedZones.Count;
        ActiveZoneNames = string.Join(
            "  ·  ",
            workspace.ActiveZones.Select(zone => zone.LogicalZoneName));

        _allEntries = workspace.AssetCatalog.Entries
            .Select(entry => new AssetExplorerEntryViewModel(
                entry,
                _authoringRegistry.TryGetAdapter(entry.AssetType, out _),
                _viewRegistry.TryGetFactory(entry.AssetType, out _)))
            .ToArray();
        _entriesByIdentity = _allEntries.ToDictionary(entry => entry.Identity);
        CatalogEntries = Array.AsReadOnly(_allEntries);
        AssetCount = _allEntries.Length;
        TargetRowCount = workspace.AssetCatalog.TargetEntries.Count;
        DependencyAssetCount = workspace.AssetCatalog.DependencyEntries.Count;
        AssetTypeCount = _allEntries.Select(entry => entry.AssetType).Distinct().Count();
        RebuildAssetGroups();
    }

    public FastFileWorkspace Workspace { get; }

    public FastFileEditingSession EditingSession { get; }

    public IReadOnlyList<AssetExplorerEntryViewModel> CatalogEntries { get; }

    public IReadOnlyList<AssetExplorerTabViewModel> OpenTabs =>
        Array.AsReadOnly(_tabs.Values
            .OrderBy(tab => tab.Entry.Entry.TargetRowIdentity?.SerializedIndex ?? int.MaxValue)
            .ThenBy(tab => tab.Entry.Entry.DependencyIdentity?.ProviderId.Value ?? long.MaxValue)
            .ToArray());

    public string TargetFileName { get; }

    public string TargetPath { get; }

    public string ModeName { get; }

    public int ActiveZoneCount { get; }

    public int LoadedZoneCount { get; }

    public string ActiveZoneNames { get; }

    public int AssetCount { get; }

    public string AssetCountText => AssetCount.ToString("N0");

    public int TargetRowCount { get; }

    public string TargetRowCountText => TargetRowCount.ToString("N0");

    public int DependencyAssetCount { get; }

    public string DependencyAssetCountText => DependencyAssetCount.ToString("N0");

    public int AssetTypeCount { get; }

    public string AssetTypeCountText => AssetTypeCount.ToString("N0");

    public string ActiveZoneCountText => ActiveZoneCount.ToString("N0");

    public string SearchText
    {
        get => _searchText;
        set
        {
            value ??= string.Empty;
            if (!SetProperty(ref _searchText, value))
                return;

            RebuildAssetGroups();
        }
    }

    public IReadOnlyList<AssetTreeNode> AssetGroups
    {
        get => _assetGroups;
        private set => SetProperty(ref _assetGroups, value);
    }

    public int VisibleAssetCount
    {
        get => _visibleAssetCount;
        private set
        {
            if (!SetProperty(ref _visibleAssetCount, value))
                return;

            OnPropertyChanged(nameof(SearchResultText));
            OnPropertyChanged(nameof(HasSearchResults));
            OnPropertyChanged(nameof(HasNoSearchResults));
        }
    }

    public string SearchResultText => string.IsNullOrWhiteSpace(SearchText)
        ? $"{AssetCount:N0} catalog entries"
        : $"{VisibleAssetCount:N0} of {AssetCount:N0}";

    public bool HasSearchResults => VisibleAssetCount > 0;

    public bool HasNoSearchResults => !HasSearchResults;

    public AssetTreeNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (!SetProperty(ref _selectedNode, value) || _suppressTreeSelection)
                return;

            if (value?.ExplorerEntry is { } entry)
                SelectEntry(entry.Identity, synchronizeTree: false);
        }
    }

    public AssetExplorerTabViewModel? SelectedTab
    {
        get => _selectedTab;
        private set
        {
            if (!SetProperty(ref _selectedTab, value))
                return;

            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(HasNoSelection));
            OnPropertyChanged(nameof(SelectedName));
            OnPropertyChanged(nameof(SelectedKind));
            OnPropertyChanged(nameof(SelectedDescription));
            OnPropertyChanged(nameof(SelectedOwnershipBadge));
            OnPropertyChanged(nameof(SelectedResolutionBadge));
            OnPropertyChanged(nameof(SelectedAccessBadge));
            OnPropertyChanged(nameof(SelectedEditorBadge));
            OnPropertyChanged(nameof(SelectedProviderZone));
            OnPropertyChanged(nameof(SelectedInspectorReason));
            OnPropertyChanged(nameof(SelectedHostedView));
            OnPropertyChanged(nameof(HasSelectedHostedView));
            OnPropertyChanged(nameof(HasStructuralInspector));
            OnPropertyChanged(nameof(SelectedMode));
            OnPropertyChanged(nameof(OpenTabs));
        }
    }

    public bool HasSelection => SelectedTab is not null;

    public bool HasNoSelection => !HasSelection;

    public string SelectedName => SelectedTab?.Entry.Name ?? string.Empty;

    public string SelectedKind => SelectedTab?.Entry.AssetType.ToString() ?? string.Empty;

    public string SelectedDescription => SelectedTab?.Entry.Description ?? string.Empty;

    public string SelectedOwnershipBadge => SelectedTab?.Entry.OwnershipBadge ?? string.Empty;

    public string SelectedResolutionBadge => SelectedTab?.Entry.ResolutionBadge ?? string.Empty;

    public string SelectedAccessBadge => SelectedTab?.Entry.AccessBadge ?? string.Empty;

    public string SelectedEditorBadge => SelectedTab?.Entry.EditorBadge ?? string.Empty;

    public string SelectedProviderZone => SelectedTab?.Entry.ProviderZone ?? string.Empty;

    public string SelectedInspectorReason => SelectedTab?.InspectorReason ?? string.Empty;

    public AssetEditorMode? SelectedMode => SelectedTab?.BackendEditor?.Mode;

    public Control? SelectedHostedView => SelectedTab?.HostedView;

    public bool HasSelectedHostedView => SelectedHostedView is not null;

    public bool HasStructuralInspector => SelectedTab?.StructuralInspector is not null;

    /// <summary>
    /// Save As is always available. The save transaction performs the single
    /// authoritative validation pass and reports actionable errors.
    /// </summary>
    public bool CanSaveAs => true;

    public void RefreshSaveAvailability() =>
        OnPropertyChanged(nameof(CanSaveAs));

    /// <summary>
    /// Rebuilds catalog projections after filtering or a completed dependency
    /// load without clearing selected identity, open tabs, or session drafts.
    /// </summary>
    public void RefreshExplorer() => RebuildAssetGroups();

    public void SelectEntry(AssetExplorerItemIdentity identity) =>
        SelectEntry(identity, synchronizeTree: true);

    public bool CloseSelectedTab()
    {
        if (SelectedTab is not { } tab)
            return false;

        CloseTab(tab.Entry.Identity);
        return true;
    }

    public void CloseTab(AssetExplorerItemIdentity identity)
    {
        if (!_tabs.Remove(identity, out AssetExplorerTabViewModel? tab))
            return;

        tab.Dispose();
        if (_selectedIdentity == identity)
        {
            _selectedIdentity = null;
            SelectedTab = null;
            SetSelectedNode(null);
        }

        OnPropertyChanged(nameof(OpenTabs));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        foreach (AssetExplorerTabViewModel tab in _tabs.Values)
            tab.Dispose();
        _tabs.Clear();
        EditingSession.Dispose();
    }

    private void SelectEntry(AssetExplorerItemIdentity identity, bool synchronizeTree)
    {
        ThrowIfDisposed();
        if (!_entriesByIdentity.TryGetValue(identity, out AssetExplorerEntryViewModel? entry))
        {
            throw new KeyNotFoundException("The selected explorer identity is not part of this workspace catalog.");
        }

        _selectedIdentity = identity;
        if (identity.TargetRowIdentity is { } targetRow)
            EditingSession.SelectRow(targetRow);
        else
            EditingSession.SelectRow(null);

        if (!_tabs.TryGetValue(identity, out AssetExplorerTabViewModel? tab))
        {
            tab = CreateTab(entry);
            _tabs.Add(identity, tab);
        }

        SelectedTab = tab;
        if (synchronizeTree)
            SetSelectedNode(FindNode(identity));
    }

    private AssetExplorerTabViewModel CreateTab(AssetExplorerEntryViewModel entry)
    {
        if (!entry.HasUsableEditor)
        {
            return new AssetExplorerTabViewModel(
                entry,
                StructuralAssetInspector.Create(
                    entry.Entry,
                    $"No usable Desktop editor is available for this {entry.AssetType} catalog entry."),
                viewHost: null);
        }

        AssetEditorSurface surface = _authoringRegistry.CreateSurface(EditingSession, entry.Entry);
        if (surface is not AssetEditorSession editorSession)
            return new AssetExplorerTabViewModel(entry, surface, viewHost: null);

        if (_viewRegistry.TryGetFactory(entry.AssetType, out _))
        {
            return new AssetExplorerTabViewModel(
                entry,
                editorSession,
                _viewRegistry.Create(editorSession));
        }

        return new AssetExplorerTabViewModel(
            entry,
            StructuralAssetInspector.Create(
                entry.Entry,
                $"A backend adapter is registered for '{entry.AssetType}', but no Desktop editor view factory is registered."),
            viewHost: null);
    }

    private void RebuildAssetGroups()
    {
        if (_disposed)
            return;

        AssetExplorerEntryViewModel[] visible = _allEntries
            .Where(MatchesSearch)
            .ToArray();
        AssetExplorerEntryViewModel[] targetRows = visible
            .Where(entry => entry.IsTargetRow)
            .ToArray();
        AssetExplorerEntryViewModel[] dependencies = visible
            .Where(entry => !entry.IsTargetRow)
            .ToArray();
        var groups = new List<AssetTreeNode>();
        AddGroup(groups, "SELECTED ZONE ROWS", "▰", targetRows, "Serialized target rows in immutable source order.");
        AddGroup(groups, "DEPENDENCY CONTENT", "◆", dependencies, "Read-only active dependency content not represented by a target row.");

        AssetGroups = Array.AsReadOnly(groups.ToArray());
        VisibleAssetCount = visible.Length;
        SetSelectedNode(_selectedIdentity is { } identity ? FindNode(identity) : null);
        OnPropertyChanged(nameof(SearchResultText));
    }

    private bool MatchesSearch(AssetExplorerEntryViewModel entry)
    {
        string query = SearchText.Trim();
        if (query.Length == 0)
            return true;

        return string.Join(
                ' ',
                entry.AssetType,
                entry.Name,
                entry.NormalizedName,
                entry.Origin,
                entry.ProviderZone,
                entry.OwnershipBadge,
                entry.ResolutionBadge,
                entry.AccessBadge,
                entry.EditorBadge)
            .Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static void AddGroup(
        ICollection<AssetTreeNode> groups,
        string title,
        string icon,
        IReadOnlyList<AssetExplorerEntryViewModel> entries,
        string description)
    {
        if (entries.Count == 0)
            return;

        AssetTreeNode[] children = entries
            .Select(entry => new AssetTreeNode(
                entry.Name,
                entry.Detail,
                entry.Icon,
                entry.AssetType.ToString(),
                entry.Description,
                entry.ProviderZone,
                entry.ToolTipText,
                isGroup: false,
                explorerEntry: entry))
            .ToArray();
        groups.Add(new AssetTreeNode(
            title,
            children.Length.ToString("N0"),
            icon,
            "Catalog projection",
            description,
            string.Empty,
            description,
            isGroup: true,
            children));
    }

    private AssetTreeNode? FindNode(AssetExplorerItemIdentity identity) =>
        AssetGroups
            .SelectMany(group => group.Children)
            .FirstOrDefault(node => node.ExplorerEntry?.Identity == identity);

    private void SetSelectedNode(AssetTreeNode? node)
    {
        _suppressTreeSelection = true;
        try
        {
            SelectedNode = node;
        }
        finally
        {
            _suppressTreeSelection = false;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(EditorViewModel));
    }
}
