using IW4.Game.Zone;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents;
using IW4.Studio.Documents.AssetReferences;

namespace IW4.Studio.Desktop.Workbench.Tools.AssetDependencies;

/// <summary>A type heading or one directly related asset in the virtualized list.</summary>
public sealed class AssetDependencyListItemViewModel
{
    private AssetDependencyListItemViewModel(string heading)
    {
        IsHeading = true;
        Heading = heading;
    }

    private AssetDependencyListItemViewModel(
        AssetDependencyRelation relation,
        Action<WorkspaceAssetCatalogEntry> openAsset)
    {
        Entry = relation.Entry;
        Name = DisplayName(relation.AssetType, relation.Name);
        FastFile = string.IsNullOrWhiteSpace(relation.FastFile)
            ? "Not loaded"
            : relation.FastFile;
        _fields = relation.ReferenceFields;
        ReferenceFieldsText = _fields.Count switch
        {
            0 => "Reference field unavailable",
            1 => _fields[0],
            _ => $"{_fields[0]} (+{_fields.Count - 1:N0} more)"
        };
        OpenCommand = new ViewModelCommand(() =>
        {
            if (Entry is { } entry)
                openAsset(entry);
        }, () => Entry is not null);
    }

    public bool IsHeading { get; }
    public bool IsRow => !IsHeading;
    public string Heading { get; } = string.Empty;
    public WorkspaceAssetCatalogEntry? Entry { get; }
    public bool CanOpen => Entry is not null;
    public string Name { get; } = string.Empty;
    public string FastFile { get; } = string.Empty;
    public string ReferenceFieldsText { get; } = string.Empty;
    private readonly IReadOnlyList<string> _fields = [];
    public string ToolTipText => string.Join(Environment.NewLine, _fields);
    public ViewModelCommand? OpenCommand { get; }

    public static AssetDependencyListItemViewModel ForHeading(string heading) => new(heading);

    public static AssetDependencyListItemViewModel ForRelation(
        AssetDependencyRelation relation,
        Action<WorkspaceAssetCatalogEntry> openAsset) => new(relation, openAsset);

    public static string DisplayName(XAssetType type, string? name)
    {
        if (name is null)
            return "<unnamed asset>";

        string displayName = AssetExplorerEntryViewModel.GetDisplayName(type, name);
        return displayName.Length == 0 ? "<unnamed asset>" : displayName;
    }
}

/// <summary>Presentation state for direct asset relationships in the right dock.</summary>
public sealed class AssetDependenciesToolViewModel : ObservableObject
{
    private readonly Action<WorkspaceAssetCatalogEntry> _openAsset;
    private readonly FastFileEditingSession _editingSession;
    private WorkspaceAssetCatalogEntry? _selection;
    private IReadOnlyList<AssetDependencyRelation> _usedBy = [];
    private IReadOnlyList<AssetDependencyRelation> _uses = [];
    private IReadOnlyList<AssetDependencyListItemViewModel> _items = [];
    private bool _isUsedByScope = true;
    private bool _isLoading;
    private string _errorMessage = string.Empty;
    private string _packageStatusMessage = string.Empty;

    public AssetDependenciesToolViewModel(
        Action<WorkspaceAssetCatalogEntry> openAsset,
        FastFileEditingSession editingSession)
    {
        _openAsset = openAsset ?? throw new ArgumentNullException(nameof(openAsset));
        _editingSession = editingSession ?? throw new ArgumentNullException(nameof(editingSession));
    }

    internal FastFileEditingSession EditingSession => _editingSession;

    public WorkspaceAssetCatalogEntry? Selection => _selection;
    public bool HasSelection => _selection is not null;
    public bool CanExportPackage => _selection is not null;
    public bool HasNoSelection => !HasSelection;
    public string AssetName => _selection switch
    {
        { Origin: WorkspaceAssetOrigin.NullRow } => "<null row>",
        { Origin: WorkspaceAssetOrigin.OpaqueRow } => "<opaque row>",
        { } entry => AssetDependencyListItemViewModel.DisplayName(
            entry.AssetType,
            entry.OriginalName ?? entry.NormalizedName),
        _ => string.Empty
    };
    public string AssetType => _selection?.AssetType.ToString() ?? string.Empty;
    public bool IsLoading => _isLoading;
    public bool HasError => _errorMessage.Length != 0;
    public string ErrorMessage => _errorMessage;
    public string PackageStatusMessage
    {
        get => _packageStatusMessage;
        private set
        {
            if (SetProperty(ref _packageStatusMessage, value))
                OnPropertyChanged(nameof(HasPackageStatus));
        }
    }
    public bool HasPackageStatus => PackageStatusMessage.Length != 0;
    internal void ReportPackageStatus(string message) => PackageStatusMessage = message;
    public int UsedByCount => _usedBy.Count;
    public int UsesCount => _uses.Count;
    public bool IsUsedByScope
    {
        get => _isUsedByScope;
        set
        {
            if (SetProperty(ref _isUsedByScope, value))
            {
                OnPropertyChanged(nameof(IsUsesScope));
                RefreshItems();
            }
        }
    }

    public bool IsUsesScope
    {
        get => !_isUsedByScope;
        set { if (value) IsUsedByScope = false; }
    }

    public IReadOnlyList<AssetDependencyListItemViewModel> Items => _items;
    public bool HasItems => _items.Count != 0;
    public bool IsEmpty => HasSelection && !IsLoading && !HasError && !HasItems;
    public string EmptyText => IsUsedByScope
        ? "No direct users found in the loaded workspace."
        : _selection is { HasDefinition: false } or
            { Origin: WorkspaceAssetOrigin.NullRow or WorkspaceAssetOrigin.OpaqueRow }
            ? "Direct dependencies are unavailable for this asset."
            : "No direct dependencies found in the loaded workspace.";

    public void SetSelection(WorkspaceAssetCatalogEntry? selection)
    {
        if (ReferenceEquals(_selection, selection))
            return;

        _selection = selection;
        PackageStatusMessage = string.Empty;
        _usedBy = [];
        _uses = [];
        if (selection is null)
            _isUsedByScope = true;
        _isLoading = false;
        _errorMessage = string.Empty;
        RefreshItems();
        NotifyState();
    }

    public void SetLoading()
    {
        if (!HasSelection)
            return;

        _usedBy = [];
        _uses = [];
        _errorMessage = string.Empty;
        _isLoading = true;
        RefreshItems();
        NotifyState();
    }

    public void SetResults(
        IReadOnlyList<AssetDependencyRelation> usedBy,
        IReadOnlyList<AssetDependencyRelation> uses)
    {
        ArgumentNullException.ThrowIfNull(usedBy);
        ArgumentNullException.ThrowIfNull(uses);
        if (!HasSelection)
            return;

        _usedBy = usedBy;
        _uses = uses;
        _isLoading = false;
        _errorMessage = string.Empty;
        RefreshItems();
        NotifyState();
    }

    public void SetError(string message)
    {
        if (!HasSelection)
            return;

        _usedBy = [];
        _uses = [];
        _isLoading = false;
        _errorMessage = message;
        RefreshItems();
        NotifyState();
    }

    private void RefreshItems()
    {
        IReadOnlyList<AssetDependencyRelation> relations = IsUsedByScope ? _usedBy : _uses;
        var items = new List<AssetDependencyListItemViewModel>(relations.Count + 16);
        foreach (IGrouping<XAssetType, AssetDependencyRelation> group in relations
            .GroupBy(relation => relation.AssetType)
            .OrderBy(group => group.Key.ToString(), StringComparer.Ordinal))
        {
            items.Add(AssetDependencyListItemViewModel.ForHeading(
                $"{group.Key}  ({group.Count():N0})"));
            foreach (AssetDependencyRelation relation in group
                .OrderBy(relation => relation.Name, StringComparer.OrdinalIgnoreCase))
            {
                items.Add(AssetDependencyListItemViewModel.ForRelation(relation, _openAsset));
            }
        }

        _items = items;
        OnPropertyChanged(nameof(Items));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(Selection));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanExportPackage));
        OnPropertyChanged(nameof(HasNoSelection));
        OnPropertyChanged(nameof(AssetName));
        OnPropertyChanged(nameof(AssetType));
        OnPropertyChanged(nameof(IsUsedByScope));
        OnPropertyChanged(nameof(IsUsesScope));
        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(UsedByCount));
        OnPropertyChanged(nameof(UsesCount));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
    }
}
