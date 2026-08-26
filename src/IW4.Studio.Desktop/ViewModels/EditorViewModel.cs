using IW4.Studio.Desktop.Editors;
using IW4.Studio.Documents;
using IW4.Assets.D3dbsp;
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
    private readonly Dictionary<EditorHostKey, AssetEditorHostViewModel> _editorHosts = [];
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
        FastFileEditingSession editingSession,
        AssetAuthoringAdapterRegistry? authoringRegistry = null,
        AssetEditorViewRegistry? viewRegistry = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(editingSession);
        if (!ReferenceEquals(editingSession.Workspace, workspace))
        {
            throw new ArgumentException(
                "The editing session belongs to another fastfile workspace.",
                nameof(editingSession));
        }

        Workspace = workspace;
        EditingSession = editingSession;
        _authoringRegistry = authoringRegistry ?? AssetAuthoringAdapterRegistry.CreateDefault();
        _viewRegistry = viewRegistry ?? AssetEditorViewRegistry.CreateDefault();
        AddableAssetTypes = _authoringRegistry.AddableAssetTypes;
        TargetFileName = Path.GetFileName(workspace.SourcePath);
        TargetPath = Path.GetFullPath(workspace.SourcePath);
        ModeName = workspace.ZonePlanProfileName is null
            ? "Single file"
            : workspace.ZonePlanProfileName + ".elf dependencies";
        ActiveZoneCount = workspace.ActiveZones.Count;
        LoadedZoneCount = workspace.LoadedZones.Count;
        ActiveZoneNames = string.Join(
            ", ",
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

    public string? ValidateNewAssetName(XAssetType assetType, string name) =>
        EditingSession.ValidateNewAssetName(assetType, name);

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

    public void CloseEditor(AssetEditorHostViewModel editorHost)
    {
        ArgumentNullException.ThrowIfNull(editorHost);
        EditorHostKey? cachedKey = _editorHosts
            .Where(pair => ReferenceEquals(pair.Value, editorHost))
            .Select(pair => (EditorHostKey?)pair.Key)
            .FirstOrDefault();
        if (cachedKey is not { } key || !_editorHosts.Remove(key))
            return;

        editorHost.Dispose();
        if (ReferenceEquals(SelectedEditorHost, editorHost))
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
                        _viewRegistry.TryGetFactory(
                            entry.AssetType,
                            entry.OriginalName ?? entry.NormalizedName,
                            out _));
            })
            .ToArray();
        var rebuiltByIdentity = rebuilt.ToDictionary(entry => entry.Identity);

        HashSet<EditorHostKey> liveHostKeys = rebuilt
            .Select(EditorHostKey.From)
            .ToHashSet();
        EditorHostKey[] removedHostKeys = _editorHosts
            .Where(pair =>
                !liveHostKeys.Contains(pair.Key) ||
                !rebuiltByIdentity.ContainsKey(pair.Value.Entry.Identity))
            .Select(pair => pair.Key)
            .ToArray();
        foreach (EditorHostKey key in removedHostKeys)
        {
            if (_editorHosts.Remove(key, out AssetEditorHostViewModel? host))
                host.Dispose();
        }

        if (_selectedIdentity is { } selectedIdentity &&
            !rebuiltByIdentity.ContainsKey(selectedIdentity))
        {
            _selectedIdentity = null;
            SelectedEditorHost = null;
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
        if (_selectedIdentity is { } liveSelectedIdentity)
            _ = SelectEntry(liveSelectedIdentity, synchronizeTree: false);
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

        EditorHostKey editorHostKey = EditorHostKey.From(entry);
        AssetExplorerEntryViewModel hostEntry = ResolveEditorHostEntry(entry);
        if (!_editorHosts.TryGetValue(editorHostKey, out AssetEditorHostViewModel? editorHost))
        {
            editorHost = CreateEditorHost(hostEntry);
            _editorHosts.Add(editorHostKey, editorHost);
        }
        else if (hostEntry.HasUsableEditor &&
                 (editorHost.StructuralInspector is not null ||
                  editorHost.Entry.Identity != hostEntry.Identity))
        {
            editorHost.Dispose();
            editorHost = CreateEditorHost(hostEntry);
            _editorHosts[editorHostKey] = editorHost;
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
        if (_viewRegistry.TryGetFactory(
                entry.AssetType,
                entry.Entry.OriginalName ?? entry.Entry.NormalizedName,
                out _))
        {
            return new AssetEditorHostViewModel(
                entry,
                surface,
                _viewRegistry.Create(surface));
        }

        return new AssetEditorHostViewModel(
            entry,
            StructuralAssetInspector.Create(
                entry.Entry,
                $"A backend adapter is registered for '{entry.AssetType}', but no Desktop editor view factory is registered."),
            viewHost: null);
    }

    private AssetExplorerEntryViewModel ResolveEditorHostEntry(
        AssetExplorerEntryViewModel selectedEntry)
    {
        if (!EditorHostKey.TryGetD3dbspName(selectedEntry, out string? normalizedName))
            return selectedEntry;

        return _allEntries
            .Where(entry =>
                EditorHostKey.TryGetD3dbspName(entry, out string? candidateName) &&
                string.Equals(candidateName, normalizedName, StringComparison.Ordinal))
            .OrderBy(entry => entry.HasUsableEditor ? 0 : 1)
            .ThenBy(entry => entry.IsTargetRow ? 0 : 1)
            .ThenBy(entry => entry.AssetType == XAssetType.GfxMap ? 0 : 1)
            .ThenBy(entry => entry.AssetType)
            .First();
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

    private readonly record struct EditorHostKey(
        AssetExplorerItemIdentity? Identity,
        string? D3dbspNormalizedName)
    {
        public static EditorHostKey From(AssetExplorerEntryViewModel entry) =>
            TryGetD3dbspName(entry, out string? normalizedName)
                ? new EditorHostKey(Identity: null, normalizedName)
                : new EditorHostKey(entry.Identity, D3dbspNormalizedName: null);

        public static bool TryGetD3dbspName(
            AssetExplorerEntryViewModel entry,
            out string? normalizedName)
        {
            ArgumentNullException.ThrowIfNull(entry);
            normalizedName = entry.NormalizedName;
            return D3dbspAssetTypeFacts.IsMultiplayerType(entry.AssetType) &&
                   D3dbspAssetTypeFacts.IsD3dbspName(entry.Name) &&
                   !string.IsNullOrWhiteSpace(normalizedName);
        }
    }
}
