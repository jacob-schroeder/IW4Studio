using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Desktop.Workbench.Selection;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Workbench.Tools.FastFileAssets;

/// <summary>
/// Searchable, grouped view of target fastfile rows. The immutable
/// <see cref="AllRows"/> sequence always retains serialized source order.
/// </summary>
public sealed class FastFileAssetsNavigatorViewModel : ObservableObject, IDisposable
{
    private readonly IWorkbenchSelectionContext _selectionContext;
    private readonly IReadOnlyList<FastFileAssetNavigatorRow> _allRows;
    private string _searchText = string.Empty;
    private IReadOnlyList<FastFileAssetNavigatorRow> _visibleRows;
    private IReadOnlyList<FastFileAssetNavigatorGroup> _groups;
    private IReadOnlyList<FastFileAssetNavigatorNode> _nodes;
    private FastFileAssetNavigatorRow? _selectedRow;
    private FastFileAssetNavigatorNode? _selectedNode;
    private bool _disposed;

    public FastFileAssetsNavigatorViewModel(
        FastFileWorkspace workspace,
        IWorkbenchSelectionContext selectionContext,
        Func<IW4.FastFiles.Zone.XAssetType, bool> hasDesktopEditor)
        : this(
            FastFileAssetsNavigatorSnapshot.Capture(
                workspace,
                hasDesktopEditor),
            selectionContext)
    {
    }

    public FastFileAssetsNavigatorViewModel(
        FastFileAssetsNavigatorSnapshot snapshot,
        IWorkbenchSelectionContext selectionContext)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _selectionContext = selectionContext
            ?? throw new ArgumentNullException(nameof(selectionContext));
        _allRows = snapshot.Rows;
        _visibleRows = _allRows;
        _groups = BuildGroups(_allRows);
        _nodes = BuildNodes(_groups);
        _selectionContext.SelectionChanged += SelectionContext_SelectionChanged;
    }

    /// <summary>Target rows only, in exact serialized source order.</summary>
    public IReadOnlyList<FastFileAssetNavigatorRow> AllRows => _allRows;

    public IReadOnlyList<FastFileAssetNavigatorRow> VisibleRows
    {
        get => _visibleRows;
        private set => SetProperty(ref _visibleRows, value);
    }

    public IReadOnlyList<FastFileAssetNavigatorGroup> Groups
    {
        get => _groups;
        private set => SetProperty(ref _groups, value);
    }

    public IReadOnlyList<FastFileAssetNavigatorNode> Nodes
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

    public FastFileAssetNavigatorRow? SelectedRow
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
                _selectionContext.Clear(WorkbenchAssetSelectionSource.FastFileAssets);
        }
    }

    public FastFileAssetNavigatorNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (!SetProperty(ref _selectedNode, value))
                return;

            FastFileAssetNavigatorRow? row = value?.Row;
            if (!ReferenceEquals(_selectedRow, row))
            {
                _selectedRow = row;
                OnPropertyChanged(nameof(SelectedRow));
            }

            if (row is not null)
                _selectionContext.Select(row.ToSelection());
            else
                _selectionContext.Clear(WorkbenchAssetSelectionSource.FastFileAssets);
        }
    }

    public int TotalCount => _allRows.Count;

    public int VisibleCount => VisibleRows.Count;

    public bool HasRows => VisibleCount > 0;

    public string ResultText => string.IsNullOrWhiteSpace(SearchText)
        ? $"{TotalCount:N0} target rows"
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
        FastFileAssetNavigatorRow[] visible = query.Length == 0
            ? _allRows.ToArray()
            : _allRows.Where(row => Matches(row, query)).ToArray();
        VisibleRows = Array.AsReadOnly(visible);
        Groups = BuildGroups(visible);
        Nodes = BuildNodes(Groups);
        SelectNodeFor(_selectedRow);
        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(ResultText));
    }

    private static bool Matches(
        FastFileAssetNavigatorRow row,
        string query) =>
        string.Join(
                ' ',
                row.AssetType,
                row.DisplayName,
                row.NormalizedName,
                row.Origin,
                row.Access,
                row.ContentSource,
                row.ProviderZone)
            .Contains(query, StringComparison.OrdinalIgnoreCase);

    private void SelectionContext_SelectionChanged(
        object? sender,
        WorkbenchSelectionChangedEventArgs args)
    {
        FastFileAssetNavigatorRow? desired =
            args.Current is
            {
                Source: WorkbenchAssetSelectionSource.FastFileAssets,
                Identity.TargetRowIdentity: { } identity
            }
                ? _allRows.FirstOrDefault(row => row.Identity == identity)
                : null;
        if (ReferenceEquals(_selectedRow, desired))
            return;

        _selectedRow = desired;
        OnPropertyChanged(nameof(SelectedRow));
        SelectNodeFor(desired);
    }

    private static IReadOnlyList<FastFileAssetNavigatorGroup> BuildGroups(
        IEnumerable<FastFileAssetNavigatorRow> rows)
    {
        FastFileAssetNavigatorGroup[] groups = rows
            .GroupBy(row => row.AssetType)
            .OrderBy(group => group.Key.ToString(), StringComparer.OrdinalIgnoreCase)
            .Select(group => new FastFileAssetNavigatorGroup(
                group.Key,
                Array.AsReadOnly(group
                    .OrderBy(row => row.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(row => row.SourceIndex)
                    .ToArray())))
            .ToArray();
        return Array.AsReadOnly(groups);
    }

    private static IReadOnlyList<FastFileAssetNavigatorNode> BuildNodes(
        IEnumerable<FastFileAssetNavigatorGroup> groups) =>
        Array.AsReadOnly(groups.Select(FastFileAssetNavigatorNode.ForGroup).ToArray());

    private void SelectNodeFor(FastFileAssetNavigatorRow? row)
    {
        FastFileAssetNavigatorNode? node = row is null
            ? null
            : Nodes.SelectMany(group => group.Children)
                .FirstOrDefault(candidate => ReferenceEquals(candidate.Row, row));
        if (ReferenceEquals(_selectedNode, node))
            return;

        _selectedNode = node;
        OnPropertyChanged(nameof(SelectedNode));
    }
}
