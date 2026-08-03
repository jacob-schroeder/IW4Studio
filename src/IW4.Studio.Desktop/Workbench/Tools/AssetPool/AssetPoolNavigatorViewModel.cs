using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Desktop.Workbench.Selection;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Workbench.Tools.AssetPool;

/// <summary>
/// Searchable, grouped view over a detached scalar pool snapshot.
/// </summary>
public sealed class AssetPoolNavigatorViewModel : ObservableObject, IDisposable
{
    public const string UnzonedGroupName = "Runtime / unzoned";

    private readonly IWorkbenchSelectionContext _selectionContext;
    private readonly IReadOnlyList<AssetPoolSlotSnapshot> _allRows;
    private string _searchText = string.Empty;
    private IReadOnlyList<AssetPoolSlotSnapshot> _visibleRows;
    private IReadOnlyList<AssetPoolNavigatorGroup> _groups;
    private IReadOnlyList<AssetPoolNavigatorZoneGroup> _zoneGroups;
    private IReadOnlyList<AssetPoolNavigatorNode> _nodes;
    private AssetPoolSlotSnapshot? _selectedRow;
    private AssetPoolNavigatorNode? _selectedNode;
    private bool _disposed;

    public AssetPoolNavigatorViewModel(
        FastFileWorkspace workspace,
        IWorkbenchSelectionContext selectionContext,
        Func<IW4.FastFiles.Zone.XAssetType, bool> hasDesktopEditor)
        : this(
            AssetPoolNavigatorSnapshot.Capture(
                workspace,
                hasDesktopEditor),
            selectionContext)
    {
    }

    public AssetPoolNavigatorViewModel(
        AssetPoolNavigatorSnapshot snapshot,
        IWorkbenchSelectionContext selectionContext)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _selectionContext = selectionContext
            ?? throw new ArgumentNullException(nameof(selectionContext));
        SnapshotRevision = snapshot.Revision;
        _allRows = snapshot.Rows;
        _visibleRows = _allRows;
        _groups = BuildGroups(_allRows);
        _zoneGroups = BuildZoneGroups(_allRows);
        _nodes = BuildNodes(_zoneGroups);
        _selectionContext.SelectionChanged += SelectionContext_SelectionChanged;
    }

    public long SnapshotRevision { get; }

    /// <summary>Detached slot rows in stable pool-slot order.</summary>
    public IReadOnlyList<AssetPoolSlotSnapshot> AllRows => _allRows;

    public IReadOnlyList<AssetPoolSlotSnapshot> VisibleRows
    {
        get => _visibleRows;
        private set => SetProperty(ref _visibleRows, value);
    }

    public IReadOnlyList<AssetPoolNavigatorGroup> Groups
    {
        get => _groups;
        private set => SetProperty(ref _groups, value);
    }

    public IReadOnlyList<AssetPoolNavigatorZoneGroup> ZoneGroups
    {
        get => _zoneGroups;
        private set => SetProperty(ref _zoneGroups, value);
    }

    public IReadOnlyList<AssetPoolNavigatorNode> Nodes
    {
        get => _nodes;
        private set => SetProperty(ref _nodes, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            value ??= string.Empty;
            if (!SetProperty(ref _searchText, value))
                return;

            RebuildProjection();
        }
    }

    public AssetPoolSlotSnapshot? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (!SetProperty(ref _selectedRow, value))
                return;

            SelectNodeFor(value);
            if (value is not null)
                _selectionContext.Select(value.ToSelection());
            else
                _selectionContext.Clear(WorkbenchAssetSelectionSource.AssetPool);
        }
    }

    public AssetPoolNavigatorNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (!SetProperty(ref _selectedNode, value))
                return;

            AssetPoolSlotSnapshot? row = value?.Row;
            if (!ReferenceEquals(_selectedRow, row))
            {
                _selectedRow = row;
                OnPropertyChanged(nameof(SelectedRow));
            }

            if (row is not null)
                _selectionContext.Select(row.ToSelection());
            else
                _selectionContext.Clear(WorkbenchAssetSelectionSource.AssetPool);
        }
    }

    public int TotalCount => _allRows.Count;

    public int VisibleCount => VisibleRows.Count;

    public bool HasRows => VisibleCount > 0;

    public string ResultText => string.IsNullOrWhiteSpace(SearchText)
        ? $"{TotalCount:N0} asset-pool slots"
        : $"{VisibleCount:N0} of {TotalCount:N0}";

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _selectionContext.SelectionChanged -= SelectionContext_SelectionChanged;
    }

    private void RebuildProjection()
    {
        string query = SearchText.Trim();
        AssetPoolSlotSnapshot[] visible = query.Length == 0
            ? _allRows.ToArray()
            : _allRows.Where(row => Matches(row, query)).ToArray();
        VisibleRows = Array.AsReadOnly(visible);
        Groups = BuildGroups(visible);
        ZoneGroups = BuildZoneGroups(visible);
        Nodes = BuildNodes(ZoneGroups);
        SelectNodeFor(_selectedRow);
        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(ResultText));
    }

    private static bool Matches(
        AssetPoolSlotSnapshot row,
        string query) =>
        string.Join(
                ' ',
                row.AssetType,
                row.DisplayName,
                row.NormalizedName,
                row.AddressText,
                row.ProviderZone,
                row.ActiveProviderId,
                row.ActiveProviderOwner)
            .Contains(query, StringComparison.OrdinalIgnoreCase);

    private void SelectionContext_SelectionChanged(
        object? sender,
        WorkbenchSelectionChangedEventArgs args)
    {
        AssetPoolSlotSnapshot? desired =
            args.Current is
            {
                Source: WorkbenchAssetSelectionSource.AssetPool,
                Identity.AssetPoolAddress: { } address
            }
                ? _allRows.FirstOrDefault(row => row.Address == address)
                : null;
        if (ReferenceEquals(_selectedRow, desired))
            return;

        _selectedRow = desired;
        OnPropertyChanged(nameof(SelectedRow));
        SelectNodeFor(desired);
    }

    private static IReadOnlyList<AssetPoolNavigatorGroup> BuildGroups(
        IEnumerable<AssetPoolSlotSnapshot> rows)
    {
        AssetPoolNavigatorGroup[] groups = rows
            .GroupBy(row => row.AssetType)
            .OrderBy(group => group.Key.ToString(), StringComparer.OrdinalIgnoreCase)
            .Select(group => new AssetPoolNavigatorGroup(
                group.Key,
                Array.AsReadOnly(group
                    .OrderBy(row => row.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(row => row.RawAddress)
                    .ToArray())))
            .ToArray();
        return Array.AsReadOnly(groups);
    }

    private static IReadOnlyList<AssetPoolNavigatorZoneGroup> BuildZoneGroups(
        IEnumerable<AssetPoolSlotSnapshot> rows)
    {
        AssetPoolNavigatorZoneGroup[] zones = rows
            .GroupBy(
                row => string.IsNullOrWhiteSpace(row.ProviderZone)
                    ? UnzonedGroupName
                    : row.ProviderZone!,
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(
                group => group.Key == UnzonedGroupName ? 1 : 0)
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(zone => new AssetPoolNavigatorZoneGroup(
                zone.Key,
                BuildGroups(zone)))
            .ToArray();
        return Array.AsReadOnly(zones);
    }

    private static IReadOnlyList<AssetPoolNavigatorNode> BuildNodes(
        IEnumerable<AssetPoolNavigatorZoneGroup> zones) =>
        Array.AsReadOnly(zones.Select(AssetPoolNavigatorNode.ForZone).ToArray());

    private void SelectNodeFor(AssetPoolSlotSnapshot? row)
    {
        AssetPoolNavigatorNode? node = row is null
            ? null
            : Nodes
                .SelectMany(zone => zone.Children)
                .SelectMany(assetType => assetType.Children)
                .FirstOrDefault(candidate => ReferenceEquals(candidate.Row, row));
        if (ReferenceEquals(_selectedNode, node))
            return;

        _selectedNode = node;
        OnPropertyChanged(nameof(SelectedNode));
    }
}
