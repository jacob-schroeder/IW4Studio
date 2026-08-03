using IW4.Studio.Desktop.Editors;
using IW4.Studio.Documents;
using IW4.FastFiles.Zone;

namespace IW4.Studio.Desktop.ViewModels;

/// <summary>
/// Document-driven Desktop explorer and editor host. Opened dependency content
/// remains catalog-backed, while target ownership follows the live editing
/// document. Runtime pool state is never used as an authoring authority.
/// </summary>
public sealed class EditorViewModel : ObservableObject, IDisposable
{
    private readonly AssetAuthoringAdapterRegistry _authoringRegistry;
    private readonly AssetEditorViewRegistry _viewRegistry;
    private AssetExplorerEntryViewModel[] _allEntries = [];
    private Dictionary<AssetExplorerItemIdentity, AssetExplorerEntryViewModel> _entriesByIdentity = [];
    private readonly Dictionary<AssetExplorerItemIdentity, AssetEditorHostViewModel> _editorHosts = [];
    private string _searchText = string.Empty;
    private IReadOnlyList<AssetTreeNode> _assetGroups = Array.Empty<AssetTreeNode>();
    private AssetTreeNode? _selectedNode;
    private AssetEditorHostViewModel? _selectedEditorHost;
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
        AddableAssetTypes = Array.AsReadOnly(
            EditingSession.AddableAssetTypes
                .Where(assetType => _authoringRegistry.TryGetAdapter(assetType, out _))
                .ToArray());
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

        CatalogEntries = Array.Empty<AssetExplorerEntryViewModel>();
        RebuildCatalogEntries(notify: false);
        EditingSession.TargetRowsChanged += EditingSession_TargetRowsChanged;
    }

    public FastFileWorkspace Workspace { get; }

    public FastFileEditingSession EditingSession { get; }

    public IReadOnlyList<XAssetType> AddableAssetTypes { get; }

    public IReadOnlyList<AssetExplorerEntryViewModel> CatalogEntries { get; private set; }

    public string TargetFileName { get; }

    public string TargetPath { get; }

    public string ModeName { get; }

    public int ActiveZoneCount { get; }

    public int LoadedZoneCount { get; }

    public string ActiveZoneNames { get; }

    public int AssetCount { get; private set; }

    public string AssetCountText => AssetCount.ToString("N0");

    public int TargetRowCount { get; private set; }

    public string TargetRowCountText => TargetRowCount.ToString("N0");

    public int DependencyAssetCount { get; private set; }

    public string DependencyAssetCountText => DependencyAssetCount.ToString("N0");

    public int AssetTypeCount { get; private set; }

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

    public AssetEditorHostViewModel? SelectedEditorHost
    {
        get => _selectedEditorHost;
        private set => SetProperty(ref _selectedEditorHost, value);
    }

    /// <summary>
    /// Save As is always available. The save transaction performs the single
    /// authoritative validation pass and reports actionable errors.
    /// </summary>
    public bool CanSaveAs => true;

    public string? ValidateNewAssetName(string name) =>
        EditingSession.ValidateNewAssetName(name);

    public WorkspaceAssetCatalogEntry AddAsset(
        XAssetType assetType,
        string name) =>
        _authoringRegistry.AddAsset(EditingSession, assetType, name);

    public void RefreshAfterSave()
    {
        OnPropertyChanged(nameof(CanSaveAs));
        foreach (AssetEditorHostViewModel editorHost in _editorHosts.Values)
            editorHost.RefreshState();
    }

    /// <summary>
    /// Rebuilds catalog projections after filtering or a completed dependency
    /// load without clearing selected identity, editor hosts, or session drafts.
    /// </summary>
    public void RefreshExplorer() => RebuildAssetGroups();

    public AssetEditorHostViewModel SelectEntry(
        AssetExplorerItemIdentity identity) =>
        SelectEntry(identity, synchronizeTree: true);

    public void DeactivateSelection()
    {
        _selectedIdentity = null;
        EditingSession.SelectRow(null);
        SelectedEditorHost = null;
        SetSelectedNode(null);
    }

    public void CloseEditor(AssetExplorerItemIdentity identity)
    {
        if (!_editorHosts.Remove(identity, out AssetEditorHostViewModel? editorHost))
            return;

        editorHost.Dispose();
        if (_selectedIdentity == identity)
        {
            _selectedIdentity = null;
            SelectedEditorHost = null;
            SetSelectedNode(null);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        EditingSession.TargetRowsChanged -= EditingSession_TargetRowsChanged;
        foreach (AssetEditorHostViewModel editorHost in _editorHosts.Values)
            editorHost.Dispose();
        _editorHosts.Clear();
        EditingSession.Dispose();
    }

    private void EditingSession_TargetRowsChanged(object? sender, EventArgs args)
    {
        if (!_disposed)
            RebuildCatalogEntries(notify: true);
    }

    private void RebuildCatalogEntries(bool notify)
    {
        WorkspaceAssetCatalogEntry[] entries =
        [
            .. EditingSession.Document.Rows,
            .. Workspace.AssetCatalog.DependencyEntries
        ];
        Dictionary<AssetExplorerItemIdentity, AssetExplorerEntryViewModel> previous =
            _entriesByIdentity;
        AssetExplorerEntryViewModel[] rebuilt = entries
            .Select(entry =>
            {
                AssetExplorerItemIdentity identity =
                    AssetExplorerItemIdentity.From(entry);
                return previous.TryGetValue(
                           identity,
                           out AssetExplorerEntryViewModel? existing) &&
                       ReferenceEquals(existing.Entry, entry)
                    ? existing
                    : new AssetExplorerEntryViewModel(
                        entry,
                        _authoringRegistry.TryGetAdapter(entry.AssetType, out _),
                        _viewRegistry.TryGetFactory(entry.AssetType, out _));
            })
            .ToArray();
        var rebuiltByIdentity = rebuilt.ToDictionary(entry => entry.Identity);

        AssetExplorerItemIdentity[] removedIdentities = previous.Keys
            .Where(identity => !rebuiltByIdentity.ContainsKey(identity))
            .ToArray();
        foreach (AssetExplorerItemIdentity identity in removedIdentities)
        {
            if (_editorHosts.Remove(identity, out AssetEditorHostViewModel? host))
                host.Dispose();
            if (_selectedIdentity == identity)
            {
                _selectedIdentity = null;
                SelectedEditorHost = null;
            }
        }

        _allEntries = rebuilt;
        _entriesByIdentity = rebuiltByIdentity;
        CatalogEntries = Array.AsReadOnly(_allEntries);
        AssetCount = _allEntries.Length;
        TargetRowCount = EditingSession.Document.Rows.Count;
        DependencyAssetCount = Workspace.AssetCatalog.DependencyEntries.Count;
        AssetTypeCount = _allEntries
            .Select(entry => entry.AssetType)
            .Distinct()
            .Count();
        RebuildAssetGroups();

        if (!notify)
            return;

        OnPropertyChanged(nameof(CatalogEntries));
        OnPropertyChanged(nameof(AssetCount));
        OnPropertyChanged(nameof(AssetCountText));
        OnPropertyChanged(nameof(TargetRowCount));
        OnPropertyChanged(nameof(TargetRowCountText));
        OnPropertyChanged(nameof(DependencyAssetCount));
        OnPropertyChanged(nameof(DependencyAssetCountText));
        OnPropertyChanged(nameof(AssetTypeCount));
        OnPropertyChanged(nameof(AssetTypeCountText));
        OnPropertyChanged(nameof(SearchResultText));
    }

    private AssetEditorHostViewModel SelectEntry(
        AssetExplorerItemIdentity identity,
        bool synchronizeTree)
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

        if (!_editorHosts.TryGetValue(identity, out AssetEditorHostViewModel? editorHost))
        {
            editorHost = CreateEditorHost(entry);
            _editorHosts.Add(identity, editorHost);
        }

        SelectedEditorHost = editorHost;
        if (synchronizeTree)
            SetSelectedNode(FindNode(identity));

        return editorHost;
    }

    private AssetEditorHostViewModel CreateEditorHost(AssetExplorerEntryViewModel entry)
    {
        if (!entry.HasUsableEditor)
        {
            return new AssetEditorHostViewModel(
                entry,
                StructuralAssetInspector.Create(
                    entry.Entry,
                    $"No usable Desktop editor is available for this {entry.AssetType} catalog entry."),
                viewHost: null);
        }

        AssetEditorSurface surface = _authoringRegistry.CreateSurface(EditingSession, entry.Entry);
        if (surface is not AssetEditorSession editorSession)
            return new AssetEditorHostViewModel(entry, surface, viewHost: null);

        if (_viewRegistry.TryGetFactory(entry.AssetType, out _))
        {
            return new AssetEditorHostViewModel(
                entry,
                editorSession,
                _viewRegistry.Create(editorSession));
        }

        return new AssetEditorHostViewModel(
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
        AddGroup(groups, "SELECTED ZONE ROWS", "▰", targetRows, "Serialized target rows in current document order.");
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
