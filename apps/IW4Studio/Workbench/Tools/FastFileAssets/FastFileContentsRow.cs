using IW4.Game.Zone;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Desktop.Workbench.Tools.AssetPool;

namespace IW4.Studio.Desktop.Workbench.Tools.FastFileAssets;

/// <summary>One Contents row, retaining the catalog or stable pool-slot identity.</summary>
public sealed class FastFileContentsRow : ObservableObject
{
    private LocalizeTableRowViewModel? _localize;
    private readonly AssetExplorerEntryViewModel? _catalogEntry;
    private readonly Func<AssetPoolSlotSnapshot, AssetExplorerEntryViewModel?>? _resolvePoolCatalogEntry;

    public FastFileContentsRow(
        AssetExplorerEntryViewModel? catalogEntry,
        AssetPoolSlotSnapshot? poolSlot,
        string sourceFile,
        string sourceHint,
        Func<AssetPoolSlotSnapshot, AssetExplorerEntryViewModel?>? resolvePoolCatalogEntry = null)
    {
        _catalogEntry = catalogEntry;
        _resolvePoolCatalogEntry = resolvePoolCatalogEntry;
        PoolSlot = poolSlot;
        SourceFile = sourceFile;
        SourceHint = sourceHint;
    }

    public AssetExplorerEntryViewModel? CatalogEntry => PoolSlot is { } slot &&
        _resolvePoolCatalogEntry is { } resolve
            ? resolve(slot)
            : _catalogEntry;
    public AssetPoolSlotSnapshot? PoolSlot { get; }
    public LocalizeTableRowViewModel? Localize
    {
        get => _localize;
        internal set => SetProperty(ref _localize, value);
    }
    public AssetExplorerItemIdentity? CatalogIdentity => PoolSlot is null ? CatalogEntry?.Identity : null;
    public XAssetPoolAddress? PoolAddress => PoolSlot?.Address;
    public string Name => PoolSlot?.DisplayName ?? CatalogEntry?.Name ?? string.Empty;
    public string NormalizedName => PoolSlot?.NormalizedName ?? CatalogEntry?.NormalizedName ?? string.Empty;
    public XAssetType AssetType => PoolSlot?.AssetType ?? CatalogEntry?.AssetType ??
        throw new InvalidDataException("A Contents row has no asset source.");
    public string SourceFile { get; }
    public string SourceHint { get; }
    public string ToolTipText => PoolSlot is { } slot
        ? $"{slot.Detail}\n{SourceHint}\n{slot.AddressText}"
        : $"{CatalogEntry?.ToolTipText}\n{SourceHint}";
    public bool IsReferencePlaceholder => PoolSlot?.IsReferencePlaceholder ??
        CatalogEntry?.Entry.IsReferencePlaceholder == true;
    public string PlaceholderHint => PoolSlot is { } slot
        ? slot.IsReferencePlaceholder ? "The active asset is a reference." : string.Empty
        : CatalogEntry?.PlaceholderHint ?? string.Empty;
    public bool HasOverride => CatalogEntry?.OverrideHint is not null;
    public string? OverrideHint => CatalogEntry?.OverrideHint;
}
