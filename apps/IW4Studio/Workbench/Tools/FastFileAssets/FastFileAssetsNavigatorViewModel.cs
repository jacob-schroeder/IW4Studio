using IW4.Game.Assets;
using IW4.Game.Assets.GfxMap;
using IW4.Game.Zone;
using IW4.Linker.Contracts;
using IW4.Runtime.Assets;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Desktop.Workbench.Selection;
using IW4.Studio.Desktop.Workbench.Tools.AssetPool;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Workbench.Tools.FastFileAssets;

/// <summary>Category and Contents projection of the live workspace catalog.</summary>
public sealed partial class FastFileAssetsNavigatorViewModel : ObservableObject, IDisposable
{
    private readonly IWorkbenchSelectionContext _selectionContext;
    private readonly EditorViewModel _editor;
    private readonly AssetPoolNavigatorSnapshot _assetPool;
    private readonly IReadOnlyDictionary<long, AssetPoolSlotSnapshot> _slotsByProvider;
    private readonly Func<BaseAsset, TargetZoneRowIdentity?, WorkspaceAssetCatalogEntry> _applyImportedAsset;
    private readonly Func<AssetPackageImportPreview,
        IReadOnlyDictionary<AssetKey, AssetPackageConflictChoice>, int> _applyPackageImport;
    private readonly Dictionary<(long, XAssetType), AssetExplorerEntryViewModel> _targetsByProvider = [];
    private readonly Dictionary<long, AssetExplorerEntryViewModel> _dependenciesByProvider = [];
    private IReadOnlyList<AssetExplorerEntryViewModel> _allRows;
    private IReadOnlyList<AssetExplorerEntryViewModel> _targetSorted;
    private IReadOnlyList<AssetExplorerEntryViewModel> _dependenciesSorted;
    private IReadOnlyList<FastFileContentsRow> _targetContents = [];
    private IReadOnlyList<FastFileContentsRow> _dependencyContents = [];
    private IReadOnlyList<FastFileContentsRow> _poolContents = [];
    private IReadOnlyList<FastFileContentsRow> _categoryRows = [];
    private IReadOnlyList<FastFileContentsRow> _contentsRows = [];
    private IReadOnlyList<FastFileAssetNavigatorGroup> _groups;
    private FastFileAssetContentsScope _contentsScope;
    private FastFileAssetNavigatorGroup? _selectedCategory;
    private FastFileContentsRow? _selectedContentsRow;
    private AssetExplorerEntryViewModel? _selectedRow;
    private string _contentsSearchText = string.Empty;
    private string _importStatusMessage = string.Empty;
    private bool _rebuildingCategories;
    private bool _staticContentsCaptured;
    private bool _disposed;

    public FastFileAssetsNavigatorViewModel(
        EditorViewModel editor,
        IWorkbenchSelectionContext selectionContext,
        Func<BaseAsset, TargetZoneRowIdentity?, WorkspaceAssetCatalogEntry> applyImportedAsset,
        Func<AssetPackageImportPreview,
            IReadOnlyDictionary<AssetKey, AssetPackageConflictChoice>, int> applyPackageImport,
        AssetPoolNavigatorSnapshot assetPool)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _selectionContext = selectionContext ?? throw new ArgumentNullException(nameof(selectionContext));
        _applyImportedAsset = applyImportedAsset ?? throw new ArgumentNullException(nameof(applyImportedAsset));
        _applyPackageImport = applyPackageImport ?? throw new ArgumentNullException(nameof(applyPackageImport));
        _assetPool = assetPool ?? throw new ArgumentNullException(nameof(assetPool));
        _slotsByProvider = _assetPool.Rows.ToDictionary(slot => slot.ActiveProviderId.Value);
        _allRows = [];
        _targetSorted = [];
        _dependenciesSorted = [];
        _groups = [];
        LocalizeTable = new LocalizeTableViewModel(editor.EditingSession);
        CaptureCatalogRows();
        RebuildCategories(null);
        _selectionContext.SelectionChanged += SelectionContext_SelectionChanged;
        _editor.EditingSession.TargetRowsChanged += EditingSession_TargetRowsChanged;
    }

    /// <summary>Target rows in current authored document order.</summary>
    public IReadOnlyList<AssetExplorerEntryViewModel> AllRows => _allRows;

    public IReadOnlyList<FastFileAssetNavigatorGroup> Groups
    {
        get => _groups;
        private set => SetProperty(ref _groups, value);
    }

    public event EventHandler? ContentsRequested;
    internal event EventHandler<WorkbenchAssetSelection>? AssetOpenedFromContents;

    internal FastFileEditingSession EditingSession => _editor.EditingSession;
    internal int ApplyPackageImport(
        AssetPackageImportPreview preview,
        IReadOnlyDictionary<AssetKey, AssetPackageConflictChoice> choices) =>
        _applyPackageImport(preview, choices);

    public FastFileAssetContentsScope ContentsScope
    {
        get => _contentsScope;
        set => SetContentsScope(value, requestContents: true);
    }

    private void SetContentsScope(FastFileAssetContentsScope value, bool requestContents)
    {
        if (!SetProperty(ref _contentsScope, value, nameof(ContentsScope)))
            return;
        OnPropertyChanged(nameof(IsThisFastFileScope));
        OnPropertyChanged(nameof(IsDependenciesScope));
        OnPropertyChanged(nameof(IsAssetPoolScope));
        RebuildCategories(_selectedCategory?.AssetType);
        if (requestContents)
            RequestContents();
    }

    public bool IsThisFastFileScope
    {
        get => ContentsScope == FastFileAssetContentsScope.ThisFastFile;
        set { if (value) ContentsScope = FastFileAssetContentsScope.ThisFastFile; }
    }

    public bool IsDependenciesScope
    {
        get => ContentsScope == FastFileAssetContentsScope.Dependencies;
        set { if (value) ContentsScope = FastFileAssetContentsScope.Dependencies; }
    }

    public bool IsAssetPoolScope
    {
        get => ContentsScope == FastFileAssetContentsScope.AssetPool;
        set { if (value) ContentsScope = FastFileAssetContentsScope.AssetPool; }
    }

    public FastFileAssetNavigatorGroup? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            // Ignore the ListBox's transient deselection and stale items on refresh.
            if (_rebuildingCategories || value is null ||
                !Groups.Any(group => ReferenceEquals(group, value)))
                return;
            SetSelectedCategory(value);
        }
    }

    public LocalizeTableViewModel LocalizeTable { get; }
    public bool IsLocalizeTable => SelectedCategory?.AssetType == XAssetType.Localize;
    public string ContentsTitle => IsLocalizeTable ? "Localize" : "Assets";
    public string SearchPrompt => IsLocalizeTable ? "Search keys or values…" : "Search asset names…";

    public bool IsAllTypesSelected => SelectedCategory is null;

    public void SelectAllTypes()
    {
        if (SelectedCategory is null)
            RequestContents();
        else
            SetSelectedCategory(null);
    }

    private void SetSelectedCategory(
        FastFileAssetNavigatorGroup? value,
        bool requestContents = true)
    {
        if (!SetProperty(ref _selectedCategory, value, nameof(SelectedCategory)))
            return;
        OnPropertyChanged(nameof(IsAllTypesSelected));
        if (_contentsSearchText.Length != 0)
        {
            _contentsSearchText = string.Empty;
            OnPropertyChanged(nameof(ContentsSearchText));
        }
        RebuildCategoryRows();
        if (requestContents)
            RequestContents();
    }

    public string ContentsSearchText
    {
        get => _contentsSearchText;
        set
        {
            value ??= string.Empty;
            if (SetProperty(ref _contentsSearchText, value))
                RebuildContentsRows();
        }
    }

    public IReadOnlyList<FastFileContentsRow> ContentsRows
    {
        get => _contentsRows;
        private set => SetProperty(ref _contentsRows, value);
    }

    /// <summary>Single click highlights; OpenContentsRow opens the editor.</summary>
    public FastFileContentsRow? SelectedContentsRow
    {
        get => _selectedContentsRow;
        set => SetProperty(ref _selectedContentsRow, value);
    }

    public bool HasContentsRows => ContentsRows.Count != 0;
    public int TargetContentsCount => _allRows.Count;
    public int DependencyContentsCount => _dependenciesSorted.Count;
    public int PoolContentsCount => _poolContents.Count;
    public string ContentsResultText => $"{ContentsRows.Count:N0} of {_categoryRows.Count:N0}";
    public string LocalizeResultText => ContentsRows.Count == _categoryRows.Count
        ? ContentsRows.Count == 1 ? "1 string" : $"{ContentsRows.Count:N0} strings"
        : $"{ContentsResultText} strings";
    public string EmptyContentsText =>
        (ContentsScope switch
        {
            FastFileAssetContentsScope.ThisFastFile => TargetContentsCount,
            FastFileAssetContentsScope.Dependencies => DependencyContentsCount,
            _ => PoolContentsCount
        }) == 0
                ? ContentsScope switch
                {
                    FastFileAssetContentsScope.ThisFastFile => "This fastfile has no assets yet.",
                    FastFileAssetContentsScope.Dependencies => "No dependency assets are loaded.",
                    _ => "The asset pool has no slots."
                }
                : IsLocalizeTable ? "No matching strings." : "No asset names match your search.";

    public void RequestContents()
    {
        if (!_disposed)
            ContentsRequested?.Invoke(this, EventArgs.Empty);
    }

    public void OpenContentsRow(FastFileContentsRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        FastFileContentsRow? live = ContentsRows.FirstOrDefault(candidate =>
            SameContentsIdentity(candidate, row));
        if (live is null)
            return; // A stale row gesture can arrive while the list is replaced.
        SelectedContentsRow = live;
        if (live.PoolSlot is { } slot)
            OpenFromContents(slot.ToSelection());
        else if (live.CatalogEntry is { } catalogEntry)
            OpenFromContents(ToSelection(catalogEntry));
    }

    internal void OpenCatalogEntry(WorkspaceAssetCatalogEntry entry)
    {
        var identity = AssetExplorerItemIdentity.From(entry);
        var row = _editor.CatalogEntries.FirstOrDefault(candidate => candidate.Identity == identity);
        if (row is not null)
            OpenSelection(ToSelection(row));
        else if (entry.DependencyIdentity is { } provider && _slotsByProvider.TryGetValue(provider.ProviderId, out var slot))
            TryOpenPoolRow(slot.Address, slot.ActiveProviderId);
    }

    internal bool TryOpenPoolRow(XAssetPoolAddress address, XAssetProviderId providerId)
    {
        FastFileContentsRow? row = _poolContents.FirstOrDefault(candidate =>
            candidate.PoolAddress == address &&
            candidate.PoolSlot?.ActiveProviderId == providerId);
        if (row?.PoolSlot is not { } slot)
            return false;

        RevealPoolRow(row);
        OpenSelection(slot.ToSelection());
        return true;
    }

    public AssetExplorerEntryViewModel? SelectedRow
    {
        get => _selectedRow;
        set
        {
            AssetExplorerEntryViewModel? live = value is null ? null :
                _allRows.FirstOrDefault(row => row.Identity == value.Identity);
            if (!SetProperty(ref _selectedRow, live))
                return;
            if (live is not null)
            {
                SelectContentsForTarget(live);
                _selectionContext.Select(ToSelection(live));
            }
            else
                _selectionContext.Clear(WorkbenchAssetSelectionSource.FastFileAssets);
        }
    }

    public int TotalCount => _allRows.Count;

    public string ImportStatusMessage
    {
        get => _importStatusMessage;
        private set
        {
            if (!SetProperty(ref _importStatusMessage, value))
                return;
            OnPropertyChanged(nameof(HasImportStatus));
        }
    }

    public bool HasImportStatus => !string.IsNullOrWhiteSpace(ImportStatusMessage);
    public bool CanAddAssets => AddableAssetTypes.Count != 0;
    public IReadOnlyList<XAssetType> AddableAssetTypes => _editor.AddableAssetTypes;

    public string? ValidateNewAssetName(
        XAssetType assetType,
        string name) => _editor.ValidateNewAssetName(assetType, name);

    public void AddAsset(
        XAssetType assetType,
        string name)
    {
        SelectTargetRow(_editor.AddAsset(assetType, name));
    }

    internal BaseAsset CaptureAssetSourceDefinition(TargetZoneRowIdentity identity) =>
        _editor.EditingSession.CaptureAssetSourceDefinition(identity);

    internal string? ValidateAssetSourceImportName(
        XAssetType type,
        string name,
        out WorkspaceAssetCatalogEntry? replacement)
    {
        replacement = null;
        if (_disposed)
            return "The fastfile is closed.";

        string? error = _editor.EditingSession.ValidateNewAssetSourceName(type, name);
        if (error is not null)
            replacement = _editor.EditingSession.FindAssetSourceReplacement(type, name);
        return replacement is null ? error : null;
    }

    internal async Task ImportAssetSourceAsync(
        Stream source,
        XAssetType type,
        string name,
        TargetZoneRowIdentity? replaceRow,
        string? sourcePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CancellationToken cancellationToken = _editor.EditingSession.CancellationToken;
        BaseAsset definition = await Task.Run(
            () => AssetSourceTransfer.Read(source, type, name, sourcePath), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        WorkspaceAssetCatalogEntry entry = _applyImportedAsset(definition, replaceRow);
        SelectTargetRow(entry);
        // Replacing source changes the value without replacing the catalog row.
        if (replaceRow.HasValue && type == XAssetType.Localize && SelectedContentsRow is { } imported)
            LocalizeTable.SetRows([imported]);
        ImportStatusMessage = replaceRow is null
            ? $"Imported {entry.OriginalName}."
            : $"Replaced {entry.OriginalName} from source.";
    }

    internal void ReportAssetSourceExport(string message)
    {
        if (!_disposed)
            ImportStatusMessage = message;
    }

    private void SelectTargetRow(WorkspaceAssetCatalogEntry entry, bool openEditor = true)
    {
        TargetZoneRowIdentity identity = entry.TargetRowIdentity
            ?? throw new InvalidDataException("The asset has no stable target-row identity.");
        AssetExplorerEntryViewModel row = _allRows.SingleOrDefault(candidate =>
            candidate.Identity.TargetRowIdentity == identity)
            ?? throw new InvalidDataException(
                "The fastfile asset navigator did not project the target row.");
        SelectTargetRow(row, openEditor);
    }

    private void SelectTargetRow(AssetExplorerEntryViewModel row, bool openEditor = true)
    {
        ContentsScope = FastFileAssetContentsScope.ThisFastFile;
        ContentsSearchText = string.Empty;
        SelectedCategory = Groups.FirstOrDefault(group =>
            group.AssetType == CategoryType(row));
        SelectedContentsRow = ContentsRows.FirstOrDefault(candidate =>
            candidate.CatalogIdentity == row.Identity);
        if (openEditor)
            OpenFromContents(ToSelection(row));
        else
            RequestContents();
    }

    public int? SuggestedD3dbspFragmentProgramUploadCapacity
    {
        get
        {
            int[] capacities = _editor.EditingSession
                .CaptureCurrentTargetAssets([XAssetType.GfxMap])
                .Definitions
                .Select(definition => definition.Definition)
                .OfType<GfxWorldAsset>()
                .Select(world => world.FragmentProgramUploadCapacity)
                .Where(capacity => capacity > 0)
                .Distinct()
                .Take(2)
                .ToArray();
            return capacities.Length == 1 ? capacities[0] : null;
        }
    }

    public async Task<D3dbspWorkspaceImportResult> ImportD3dbspAsync(
        string inputPath,
        string assetName,
        bool forceFullbright,
        int fragmentProgramUploadCapacity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetName);
        if (_disposed)
            throw new ObjectDisposedException(nameof(FastFileAssetsNavigatorViewModel));

        D3dbspWorkspaceImportResult imported =
            await _editor.EditingSession.ImportD3dbspAsync(
                inputPath,
                assetName,
                forceFullbright,
                fragmentProgramUploadCapacity);
        TargetZoneRowIdentity anchorIdentity = imported.TargetRows
            .FirstOrDefault(entry => entry.AssetType == XAssetType.GfxMap)
            ?.TargetRowIdentity ??
            imported.TargetRows.FirstOrDefault()?.TargetRowIdentity ??
            throw new InvalidDataException(
                "The imported D3DBSP did not create a selectable target row.");
        AssetExplorerEntryViewModel anchor = _allRows
            .SingleOrDefault(row => row.Identity.TargetRowIdentity == anchorIdentity)
            ?? throw new InvalidDataException(
                "The fastfile asset navigator did not project the imported D3DBSP group.");
        SelectTargetRow(anchor);
        string discardedLighting = imported.DiscardedLightByteCount == 0
            ? string.Empty
            : $" Discarded {imported.DiscardedLightByteCount:N0} compiled light bytes using the lossy fullbright workaround.";
        ImportStatusMessage =
            $"Imported {Path.GetFileName(inputPath)} as {imported.AssetName}; " +
            $"replaced {imported.ReplacedRowCount} and added {imported.AddedRowCount} target rows." +
            discardedLighting;
        return imported;
    }

    public void ReportD3dbspImportFailure(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ImportStatusMessage = $"D3DBSP import failed: {message}";
    }

    public async Task ImportRawFileFolderAsync(string folderPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ImportStatusMessage = "Importing RawFiles...";
        (int importedCount, int skippedCount) =
            await _editor.EditingSession.ImportRawFileFolderAsync(folderPath);
        if (_disposed)
            return;

        ImportStatusMessage =
            $"Imported {importedCount:N0} RawFiles; skipped {skippedCount:N0} existing assets. " +
            "Paths are relative to the selected folder.";
    }

    public void ReportRawFileImportFailure(string message)
    {
        if (!_disposed)
            ImportStatusMessage = $"RawFile folder import failed: {message}";
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _editor.EditingSession.TargetRowsChanged -= EditingSession_TargetRowsChanged;
        _selectionContext.SelectionChanged -= SelectionContext_SelectionChanged;
    }

    private void EditingSession_TargetRowsChanged(object? sender, EventArgs args)
    {
        if (_disposed)
            return;
        TargetZoneRowIdentity? selectedIdentity = _selectedRow?.Identity.TargetRowIdentity;
        CaptureCatalogRows();
        _selectedRow = selectedIdentity is { } identity
            ? _allRows.FirstOrDefault(row => row.Identity.TargetRowIdentity == identity)
            : null;
        if (selectedIdentity is not null && _selectedRow is null)
            _selectionContext.Clear(WorkbenchAssetSelectionSource.FastFileAssets);
        OnPropertyChanged(nameof(AllRows));
        OnPropertyChanged(nameof(SelectedRow));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(TargetContentsCount));
        RebuildCategories(_selectedCategory?.AssetType);
    }

    private void CaptureCatalogRows()
    {
        IReadOnlyList<AssetExplorerEntryViewModel> catalog = _editor.CatalogEntries;
        _allRows = Array.AsReadOnly(catalog.Where(row => row.IsTargetRow).ToArray());
        _targetSorted = Array.AsReadOnly(_allRows
            .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Identity.TargetRowIdentity?.SerializedIndex ?? int.MaxValue)
            .ToArray());
        if (!_staticContentsCaptured)
        {
            _dependenciesSorted = Array.AsReadOnly(catalog
                .Where(row => row.Identity.DependencyIdentity is not null)
                .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray());
        }
        string targetFile = _editor.TargetFileName;
        _targetsByProvider.Clear();
        foreach (AssetExplorerEntryViewModel row in _allRows)
        {
            if (row.Entry.ResolvedProvider is { } resolved &&
                (row.Origin != WorkspaceAssetOrigin.TargetOwnedDefinition || resolved.Zone.IsTarget))
                _targetsByProvider.TryAdd((resolved.ProviderId, row.AssetType), row);
        }
        _targetContents = Array.AsReadOnly(_targetSorted.Select(row =>
        {
            AssetPoolSlotSnapshot? provider = row.Entry.ResolvedProvider is { } resolved
                ? _slotsByProvider.GetValueOrDefault(resolved.ProviderId)
                : null;
            string sourceHint = row.Entry.IsReferencePlaceholder
                ? provider is null
                    ? $"Serialized reference in {targetFile}; no active provider."
                    : $"Serialized reference in {targetFile}; active provider: {provider.ProviderFileName}."
                : $"Serialized in {targetFile}.";
            return new FastFileContentsRow(row, null, targetFile, sourceHint);
        }).ToArray());
        if (_staticContentsCaptured)
            return;
        _dependencyContents = Array.AsReadOnly(_dependenciesSorted.Select(row =>
        {
            long providerId = row.Identity.DependencyIdentity!.Value.ProviderId;
            _dependenciesByProvider.Add(providerId, row);
            AssetPoolSlotSnapshot? provider = _slotsByProvider.GetValueOrDefault(providerId);
            string file = provider?.ProviderFileName ?? "Unknown source";
            return new FastFileContentsRow(row, null, file,
                provider is null
                    ? "The dependency provider has no known physical fastfile."
                    : file == "Unzoned"
                        ? "The dependency provider is unzoned; no physical fastfile is known."
                        : file == "Unknown source"
                            ? $"Dependency provider {provider.ActiveProviderOwner} has no known physical fastfile."
                        : $"Dependency provider: {file}.");
        }).ToArray());
        _poolContents = Array.AsReadOnly(_assetPool.Rows.Select(slot =>
        {
            return new FastFileContentsRow(null, slot, slot.ProviderFileName,
                slot.ProviderFileName == "Unzoned"
                    ? "Active pool provider is unzoned; no physical fastfile is known."
                    : slot.ProviderFileName == "Unknown source"
                        ? $"Active pool provider {slot.ActiveProviderOwner} has no known physical fastfile."
                    : $"Active pool provider: {slot.ProviderFileName}.",
                ResolvePoolCatalogEntry);
        }).ToArray());
        _staticContentsCaptured = true;
    }

    private AssetExplorerEntryViewModel? ResolvePoolCatalogEntry(AssetPoolSlotSnapshot slot) =>
        _targetsByProvider.GetValueOrDefault((slot.ActiveProviderId.Value, slot.AssetType))
        ?? _dependenciesByProvider.GetValueOrDefault(slot.ActiveProviderId.Value);

    private void SelectionContext_SelectionChanged(
        object? sender, WorkbenchSelectionChangedEventArgs args)
    {
        if (args.Current is
            { Source: WorkbenchAssetSelectionSource.AssetPool,
              Identity.AssetPoolAddress: { } poolAddress } &&
            _poolContents.FirstOrDefault(row => row.PoolAddress == poolAddress &&
                row.PoolSlot?.ActiveProviderId == args.Current.ProviderId) is { } poolRow)
        {
            SetProperty(ref _selectedRow, null, nameof(SelectedRow));
            RevealPoolRow(poolRow);
            return;
        }
        AssetExplorerEntryViewModel? desired = args.Current is
            { Source: WorkbenchAssetSelectionSource.FastFileAssets,
              Identity.TargetRowIdentity: { } identity }
                ? _allRows.FirstOrDefault(row => row.Identity.TargetRowIdentity == identity)
                : null;
        if (ReferenceEquals(_selectedRow, desired))
            return;
        _selectedRow = desired;
        OnPropertyChanged(nameof(SelectedRow));
        if (desired is not null)
            SelectContentsForTarget(desired);
    }

    private void RevealPoolRow(FastFileContentsRow row)
    {
        if (ContentsScope != FastFileAssetContentsScope.AssetPool)
            SetContentsScope(FastFileAssetContentsScope.AssetPool, requestContents: false);

        FastFileContentsRow? visible = ContentsRows.FirstOrDefault(candidate =>
            SameContentsIdentity(candidate, row));
        if (visible is null)
        {
            XAssetType type = CategoryType(row.AssetType, row.NormalizedName);
            FastFileAssetNavigatorGroup? category = Groups.FirstOrDefault(group =>
                group.AssetType == type);
            if (_selectedCategory?.AssetType != type)
                SetSelectedCategory(category, requestContents: false);
            else
                ContentsSearchText = string.Empty;
            visible = ContentsRows.FirstOrDefault(candidate =>
                SameContentsIdentity(candidate, row));
        }
        SelectedContentsRow = visible;
    }

    private void RebuildCategories(XAssetType? preferredType)
    {
        _rebuildingCategories = true;
        try
        {
            Groups = Array.AsReadOnly(ScopeContentsRows()
                .GroupBy(row => CategoryType(row.AssetType, row.NormalizedName))
                .OrderBy(group => group.Key.ToString(), StringComparer.OrdinalIgnoreCase)
                .Select(group => new FastFileAssetNavigatorGroup(group.Key, group.Count()))
                .ToArray());
            FastFileAssetNavigatorGroup? selected = preferredType is { } type
                ? Groups.FirstOrDefault(group => group.AssetType == type)
                : null;
            if (!ReferenceEquals(_selectedCategory, selected))
            {
                _selectedCategory = selected;
                OnPropertyChanged(nameof(SelectedCategory));
                OnPropertyChanged(nameof(IsAllTypesSelected));
            }
            RebuildCategoryRows();
        }
        finally
        {
            _rebuildingCategories = false;
        }
    }

    private void RebuildCategoryRows()
    {
        IReadOnlyList<FastFileContentsRow> scopeRows = ScopeContentsRows();
        _categoryRows = _selectedCategory is { } category
            ? Array.AsReadOnly(scopeRows.Where(row =>
                CategoryType(row.AssetType, row.NormalizedName) == category.AssetType).ToArray())
            : scopeRows;
        if (IsLocalizeTable)
            LocalizeTable.SetRows(_categoryRows);
        OnPropertyChanged(nameof(IsLocalizeTable));
        OnPropertyChanged(nameof(ContentsTitle));
        OnPropertyChanged(nameof(SearchPrompt));
        RebuildContentsRows();
    }

    private void OpenFromContents(WorkbenchAssetSelection selection)
    {
        OpenSelection(selection);
        AssetOpenedFromContents?.Invoke(this, selection);
    }

    private void OpenSelection(WorkbenchAssetSelection selection)
    {
        if (Equals(_selectionContext.Current, selection) && TryRevealLocalize(selection))
            return;
        _selectionContext.Select(selection);
    }

    internal bool TryRevealLocalize(WorkbenchAssetSelection selection)
    {
        if (selection.AssetType != XAssetType.Localize)
            return false;
        FastFileContentsRow? row;
        FastFileAssetContentsScope scope;
        if (selection.Identity.AssetPoolAddress is { } address)
        {
            scope = FastFileAssetContentsScope.AssetPool;
            row = _poolContents.FirstOrDefault(r => r.PoolAddress == address &&
                r.PoolSlot?.ActiveProviderId == selection.ProviderId);
        }
        else if (selection.Identity.TargetRowIdentity is { } target)
        {
            scope = FastFileAssetContentsScope.ThisFastFile;
            row = _targetContents.FirstOrDefault(r => r.CatalogIdentity?.TargetRowIdentity == target);
        }
        else if (selection.Identity.DependencyIdentity is { } dependency)
        {
            scope = FastFileAssetContentsScope.Dependencies;
            row = _dependencyContents.FirstOrDefault(r => r.CatalogIdentity?.DependencyIdentity == dependency);
        }
        else
            return false;
        if (row is null)
            return false;

        SetContentsScope(scope, requestContents: false);
        SetSelectedCategory(Groups.First(g => g.AssetType == XAssetType.Localize), requestContents: false);
        if (!ContentsRows.Contains(row))
            ContentsSearchText = string.Empty;
        SelectedContentsRow = row;
        if (row.Localize is { IsLongValue: true } longValue)
            LocalizeTable.Expand(longValue);
        RequestContents();
        return true;
    }

    internal void ApplyLocalizeChanges()
    {
        try
        {
            int count = LocalizeTable.ApplyAll();
            if (count != 0)
                ImportStatusMessage = count == 1 ? "Applied 1 Localize change." : $"Applied {count:N0} Localize changes.";
            RebuildContentsRows();
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or
            ArgumentException or NotSupportedException or OverflowException)
        {
            LocalizeTable.SetRows(_categoryRows);
            ImportStatusMessage = ex.Message;
        }
    }

    internal void DiscardLocalizeChanges()
    {
        LocalizeTable.DiscardAll();
        RebuildContentsRows();
    }

    private void RebuildContentsRows()
    {
        FastFileContentsRow? selectedRow = _selectedContentsRow;
        string query = ContentsSearchText.Trim();
        ContentsRows = query.Length == 0
            ? _categoryRows
            : Array.AsReadOnly(_categoryRows.Where(row => IsLocalizeTable
                ? LocalizeTable.MatchesSearch(row, query)
                : row.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                row.NormalizedName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                row.AssetType.ToString().Contains(query, StringComparison.OrdinalIgnoreCase) ||
                row.SourceFile.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray());
        FastFileContentsRow? selected = selectedRow is not null
            ? ContentsRows.FirstOrDefault(row => SameContentsIdentity(row, selectedRow))
            : null;
        if (!ReferenceEquals(_selectedContentsRow, selected))
        {
            _selectedContentsRow = selected;
            OnPropertyChanged(nameof(SelectedContentsRow));
        }
        OnPropertyChanged(nameof(HasContentsRows));
        OnPropertyChanged(nameof(ContentsResultText));
        OnPropertyChanged(nameof(LocalizeResultText));
        OnPropertyChanged(nameof(EmptyContentsText));
    }

    private void SelectContentsForTarget(AssetExplorerEntryViewModel row)
    {
        if (ContentsScope == FastFileAssetContentsScope.ThisFastFile)
            SelectedContentsRow = ContentsRows.FirstOrDefault(candidate =>
                candidate.CatalogIdentity == row.Identity);
    }

    private IReadOnlyList<FastFileContentsRow> ScopeContentsRows() => ContentsScope switch
    {
        FastFileAssetContentsScope.ThisFastFile => _targetContents,
        FastFileAssetContentsScope.Dependencies => _dependencyContents,
        FastFileAssetContentsScope.AssetPool => _poolContents,
        _ => throw new InvalidDataException($"Unknown Contents scope '{ContentsScope}'.")
    };

    private static bool SameContentsIdentity(FastFileContentsRow left, FastFileContentsRow right) =>
        left.PoolAddress is { } poolAddress
            ? right.PoolAddress == poolAddress &&
              left.PoolSlot?.ActiveProviderId == right.PoolSlot?.ActiveProviderId
            : left.CatalogIdentity is { } identity && right.CatalogIdentity == identity;

    private static XAssetType CategoryType(AssetExplorerEntryViewModel row) =>
        CategoryType(row.AssetType, row.NormalizedName);

    private static XAssetType CategoryType(XAssetType type, string normalizedName) =>
        type == XAssetType.RawFile &&
        IW4.Gsc.Character.CharacterScriptProjection.IsCharacterPath(normalizedName)
            ? XAssetType.Character : type;

    private static WorkbenchAssetSelection ToSelection(AssetExplorerEntryViewModel row)
    {
        WorkbenchAssetSelectionIdentity identity = row.Identity.TargetRowIdentity is { } target
            ? WorkbenchAssetSelectionIdentity.ForTargetRow(target)
            : WorkbenchAssetSelectionIdentity.ForDependency(row.Identity.DependencyIdentity
                ?? throw new InvalidDataException("The catalog entry has no stable identity."));
        WorkspaceAssetAccess access = row.Entry.IsGeneratedConfigStringBaseline &&
            row.Access == WorkspaceAssetAccess.Editable
                ? WorkspaceAssetAccess.ReadOnly : row.Access;
        return new WorkbenchAssetSelection(identity, row.AssetType, row.Name,
            row.NormalizedName, access, row.Origin.ToString(), row.ProviderZone,
            row.HasUsableEditor);
    }
}
