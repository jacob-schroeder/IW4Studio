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
    private AssetPoolNavigatorSnapshot(
        long revision,
        IEnumerable<AssetPoolSlotSnapshot> rows)
    {
        Revision = revision;
        Rows = Array.AsReadOnly(rows.ToArray());
    }

    public long Revision { get; }

    /// <summary>Stable pool-slot order as exposed by XAssetPool.Slots.</summary>
    public IReadOnlyList<AssetPoolSlotSnapshot> Rows { get; }

    public static AssetPoolNavigatorSnapshot Capture(
        FastFileWorkspace workspace,
        Func<XAssetType, string?, bool> hasDesktopEditor)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(hasDesktopEditor);

        XAssetPool pool = workspace.LoadedZone.Context.AssetPool;
        long revision = pool.Revision;
        XAssetSlot[] slots = pool.Slots.ToArray();
        AssetPoolSlotSnapshot[] rows = slots.Select(slot =>
        {
            XAssetProviderContribution provider = slot.ActiveProvider;
            string? zoneName = null;
            string providerFileName = "Unzoned";
            if (!provider.Owner.IsNone)
            {
                WorkspaceZone? zone = workspace.LoadedZones
                    .FirstOrDefault(candidate => candidate.LoadResult.Context.ZoneOwner == provider.Owner);
                zoneName = zone?.LogicalZoneName ?? provider.Owner.ToString();
                providerFileName = zone is null
                    ? "Unknown source"
                    : Path.GetFileName(zone.PhysicalPath);
            }

            bool hasEditor = hasDesktopEditor(slot.AssetType, slot.Name);
            return new AssetPoolSlotSnapshot(
                slot.Address,
                slot.AssetType,
                AssetExplorerEntryViewModel.GetDisplayName(slot.AssetType, slot.Name),
                XAssetStableIdentity.NormalizeLookupName(slot.Name),
                slot.Providers.Count,
                provider.Id,
                provider.Owner,
                provider.RegistrationSequence,
                provider.IsReferencePlaceholder,
                zoneName,
                providerFileName,
                hasEditor,
                hasEditor && !provider.IsReferencePlaceholder &&
                workspace.AssetCatalog.FindEditablePoolEntry(provider.Id)?.AssetType == slot.AssetType);
        }).ToArray();

        if (pool.Revision != revision)
        {
            throw new InvalidOperationException(
                "The runtime asset pool changed while its navigator snapshot was being captured.");
        }

        return new AssetPoolNavigatorSnapshot(revision, rows);
    }
}
