using IW4.Game.Zone;
using IW4.Runtime.Assets;
using IW4.Runtime.Database;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Desktop.Workbench.Selection;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Workbench.Tools.AssetPool;

/// <summary>
/// Scalar-only copy of one runtime asset-pool slot. No BaseAsset, provider,
/// source-memory, header-buffer, or slot reference survives projection.
/// </summary>
public sealed record AssetPoolSlotSnapshot
{
    internal AssetPoolSlotSnapshot(
        XAssetPoolAddress address,
        XAssetType assetType,
        string displayName,
        string normalizedName,
        int providerCount,
        XAssetProviderId activeProviderId,
        DbZoneHandle activeProviderOwner,
        long activeProviderRegistrationSequence,
        bool isReferencePlaceholder,
        string? providerZone,
        string providerFileName,
        bool hasEditor,
        bool canEdit)
    {
        Address = address;
        AssetType = assetType;
        DisplayName = displayName;
        NormalizedName = normalizedName;
        ProviderCount = providerCount;
        ActiveProviderId = activeProviderId;
        ActiveProviderOwner = activeProviderOwner;
        ActiveProviderRegistrationSequence = activeProviderRegistrationSequence;
        IsReferencePlaceholder = isReferencePlaceholder;
        ProviderZone = providerZone;
        ProviderFileName = providerFileName;
        HasEditor = hasEditor;
        CanEdit = canEdit;
    }

    public XAssetPoolAddress Address { get; }

    public int SlotIndex => Address.Slot;

    public int RawAddress => Address.RawValue;

    public XAssetType AssetType { get; }

    public string DisplayName { get; }

    public string NormalizedName { get; }

    public int ProviderCount { get; }

    public bool HasFallbackProviders => ProviderCount > 1;

    public XAssetProviderId ActiveProviderId { get; }

    public DbZoneHandle ActiveProviderOwner { get; }

    public long ActiveProviderRegistrationSequence { get; }

    public bool IsReferencePlaceholder { get; }

    public string? ProviderZone { get; }

    public string ProviderFileName { get; }

    public bool HasEditor { get; }
    public bool CanEdit { get; }

    public string TypeName => AssetType.ToString();

    public string AddressText => $"0x{unchecked((uint)RawAddress):X8}";

    public string Detail => string.IsNullOrWhiteSpace(ProviderZone)
        ? $"{TypeName} · slot {SlotIndex:N0}"
        : $"{TypeName} · {ProviderZone}";

    public WorkbenchAssetSelection ToSelection() =>
        new(
            WorkbenchAssetSelectionIdentity.ForAssetPoolSlot(Address),
            AssetType,
            DisplayName,
            NormalizedName,
            CanEdit ? WorkspaceAssetAccess.Editable : WorkspaceAssetAccess.ReadOnly,
            "RuntimeAssetPool",
            ProviderZone,
            hasEditor: HasEditor && !IsReferencePlaceholder,
            providerId: ActiveProviderId);
}

/// <summary>
/// Immutable scalar snapshot of a workspace runtime pool at one revision.
/// </summary>
public sealed class AssetPoolNavigatorSnapshot
{
    private readonly record struct CapturedSlot(
        XAssetPoolAddress Address,
        XAssetType AssetType,
        string Name,
        int ProviderCount,
        XAssetProviderId ActiveProviderId,
        DbZoneHandle ActiveProviderOwner,
        long ActiveProviderRegistrationSequence,
        bool IsReferencePlaceholder,
        string? ProviderZone,
        string ProviderFileName,
        bool HasEditableCatalogEntry);

    private readonly CapturedSlot[] _slots;
    private readonly IReadOnlyDictionary<long, int> _slotIndexByProvider;
    private readonly Func<XAssetType, string?, bool> _hasDesktopEditor;
    private readonly Lazy<IReadOnlyList<AssetPoolSlotSnapshot>> _rows;

    private AssetPoolNavigatorSnapshot(
        long revision,
        CapturedSlot[] slots,
        IReadOnlyDictionary<long, int> slotIndexByProvider,
        Func<XAssetType, string?, bool> hasDesktopEditor)
    {
        Revision = revision;
        _slots = slots;
        _slotIndexByProvider = slotIndexByProvider;
        _hasDesktopEditor = hasDesktopEditor;
        _rows = new Lazy<IReadOnlyList<AssetPoolSlotSnapshot>>(CreateRows);
    }

    public long Revision { get; }
    public int Count => _slots.Length;

    internal (string FileName, DbZoneHandle Owner)? FindProviderSource(long providerId) =>
        _slotIndexByProvider.TryGetValue(providerId, out int index)
            ? (_slots[index].ProviderFileName, _slots[index].ActiveProviderOwner)
            : null;

    internal AssetPoolSlotSnapshot? FindProviderRow(long providerId) =>
        _slotIndexByProvider.TryGetValue(providerId, out int index) ? Rows[index] : null;

    /// <summary>Stable pool-slot order as exposed by XAssetPool.Slots.</summary>
    public IReadOnlyList<AssetPoolSlotSnapshot> Rows => _rows.Value;

    private IReadOnlyList<AssetPoolSlotSnapshot> CreateRows() =>
        Array.AsReadOnly(_slots.Select(slot =>
        {
            bool hasEditor = _hasDesktopEditor(slot.AssetType, slot.Name);
            return new AssetPoolSlotSnapshot(
                slot.Address,
                slot.AssetType,
                AssetExplorerEntryViewModel.GetDisplayName(slot.AssetType, slot.Name),
                XAssetStableIdentity.NormalizeLookupName(slot.Name),
                slot.ProviderCount,
                slot.ActiveProviderId,
                slot.ActiveProviderOwner,
                slot.ActiveProviderRegistrationSequence,
                slot.IsReferencePlaceholder,
                slot.ProviderZone,
                slot.ProviderFileName,
                hasEditor,
                hasEditor && !slot.IsReferencePlaceholder && slot.HasEditableCatalogEntry);
        }).ToArray());

    public static AssetPoolNavigatorSnapshot Capture(
        FastFileWorkspace workspace,
        Func<XAssetType, string?, bool> hasDesktopEditor)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(hasDesktopEditor);

        XAssetPool pool = workspace.LoadedZone.Context.AssetPool;
        long revision = pool.Revision;
        var zonesByOwner = workspace.LoadedZones.ToDictionary(
            zone => zone.LoadResult.Context.ZoneOwner,
            zone => (zone.LogicalZoneName, FileName: Path.GetFileName(zone.PhysicalPath)));
        IReadOnlyCollection<XAssetSlot> runtimeSlots = pool.Slots;
        var slots = new CapturedSlot[runtimeSlots.Count];
        var slotIndexByProvider = new Dictionary<long, int>(runtimeSlots.Count);
        int index = 0;
        foreach (XAssetSlot slot in runtimeSlots)
        {
            XAssetProviderContribution provider = slot.ActiveProvider;
            string? zoneName = null;
            string providerFileName = "Unzoned";
            if (!provider.Owner.IsNone)
            {
                zonesByOwner.TryGetValue(provider.Owner, out var zone);
                zoneName = zone.LogicalZoneName ?? provider.Owner.ToString();
                providerFileName = zone.FileName ?? "Unknown source";
            }

            slotIndexByProvider.Add(provider.Id.Value, index);
            slots[index++] = new CapturedSlot(
                slot.Address,
                slot.AssetType,
                slot.Name,
                slot.Providers.Count,
                provider.Id,
                provider.Owner,
                provider.RegistrationSequence,
                provider.IsReferencePlaceholder,
                zoneName,
                providerFileName,
                workspace.AssetCatalog.FindEditablePoolEntry(provider.Id)?.AssetType == slot.AssetType);
        }

        if (pool.Revision != revision)
        {
            throw new InvalidOperationException(
                "The runtime asset pool changed while its navigator snapshot was being captured.");
        }

        return new AssetPoolNavigatorSnapshot(
            revision, slots, slotIndexByProvider, hasDesktopEditor);
    }
}
