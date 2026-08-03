using IW4.Assets.Zone;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;
using IW4.Runtime.Database;
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
        bool hasEditor)
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
        HasEditor = hasEditor;
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

    public bool HasEditor { get; }

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
            WorkspaceAssetAccess.ReadOnly,
            "RuntimeAssetPool",
            ProviderZone,
            hasEditor: HasEditor && !IsReferencePlaceholder,
            providerId: ActiveProviderId);
}

public sealed record AssetPoolNavigatorGroup(
    XAssetType AssetType,
    IReadOnlyList<AssetPoolSlotSnapshot> Rows)
{
    public string Name => AssetType.ToString();

    public int Count => Rows.Count;
}

public sealed record AssetPoolNavigatorZoneGroup(
    string Name,
    IReadOnlyList<AssetPoolNavigatorGroup> AssetTypes)
{
    public int Count => AssetTypes.Sum(group => group.Count);
}

public enum AssetPoolNavigatorNodeKind
{
    Zone,
    AssetType,
    Asset
}

/// <summary>
/// Zone → asset type → named asset projection used by the pool tree.
/// </summary>
public sealed class AssetPoolNavigatorNode
{
    private AssetPoolNavigatorNode(
        string name,
        string detail,
        string trailingText,
        AssetPoolNavigatorNodeKind kind,
        IReadOnlyList<AssetPoolNavigatorNode> children,
        AssetPoolSlotSnapshot? row)
    {
        Name = name;
        Detail = detail;
        TrailingText = trailingText;
        Kind = kind;
        Children = children;
        Row = row;
    }

    public string Name { get; }

    public string Detail { get; }

    public string TrailingText { get; }

    public AssetPoolNavigatorNodeKind Kind { get; }

    public bool IsZone => Kind == AssetPoolNavigatorNodeKind.Zone;

    public bool IsAssetType => Kind == AssetPoolNavigatorNodeKind.AssetType;

    public bool IsAsset => Kind == AssetPoolNavigatorNodeKind.Asset;

    public IReadOnlyList<AssetPoolNavigatorNode> Children { get; }

    public AssetPoolSlotSnapshot? Row { get; }

    internal static AssetPoolNavigatorNode ForZone(
        AssetPoolNavigatorZoneGroup zone) =>
        new(
            zone.Name,
            $"{zone.Count:N0} {(zone.Count == 1 ? "asset" : "assets")} · {zone.AssetTypes.Count:N0} {(zone.AssetTypes.Count == 1 ? "type" : "types")}",
            zone.Count.ToString("N0"),
            AssetPoolNavigatorNodeKind.Zone,
            Array.AsReadOnly(zone.AssetTypes.Select(ForAssetType).ToArray()),
            row: null);

    private static AssetPoolNavigatorNode ForAssetType(
        AssetPoolNavigatorGroup group) =>
        new(
            group.Name,
            string.Empty,
            group.Count.ToString("N0"),
            AssetPoolNavigatorNodeKind.AssetType,
            Array.AsReadOnly(group.Rows.Select(ForRow).ToArray()),
            row: null);

    private static AssetPoolNavigatorNode ForRow(
        AssetPoolSlotSnapshot row) =>
        new(
            row.DisplayName,
            row.HasFallbackProviders
                ? $"{row.ProviderCount:N0} providers"
                : $"slot {row.SlotIndex:N0}",
            row.AddressText,
            AssetPoolNavigatorNodeKind.Asset,
            Array.Empty<AssetPoolNavigatorNode>(),
            row);
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
        Func<XAssetType, bool> hasDesktopEditor)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(hasDesktopEditor);

        XAssetPool pool = workspace.Runtime.AssetPool;
        IReadOnlyDictionary<DbZoneHandle, string> zoneNames = workspace.LoadedZones
            .Where(zone => !zone.RuntimeZoneHandle.IsNone)
            .GroupBy(zone => zone.RuntimeZoneHandle)
            .ToDictionary(
                group => group.Key,
                group => group.First().LogicalZoneName);
        long revision = pool.Revision;
        XAssetSlot[] slots = pool.Slots.ToArray();
        AssetPoolSlotSnapshot[] rows = slots.Select(slot =>
        {
            XAssetProviderContribution provider = slot.ActiveProvider;
            string? zoneName = null;
            if (!provider.Owner.IsNone)
            {
                zoneName = zoneNames.TryGetValue(provider.Owner, out string? knownName)
                    ? knownName
                    : provider.Owner.ToString();
            }

            return new AssetPoolSlotSnapshot(
                slot.Address,
                slot.AssetType,
                slot.Name,
                XAssetStableIdentity.NormalizeLookupName(slot.Name),
                slot.Providers.Count,
                provider.Id,
                provider.Owner,
                provider.RegistrationSequence,
                provider.IsReferencePlaceholder,
                zoneName,
                hasDesktopEditor(slot.AssetType));
        }).ToArray();

        if (pool.Revision != revision)
        {
            throw new InvalidOperationException(
                "The runtime asset pool changed while its navigator snapshot was being captured.");
        }

        return new AssetPoolNavigatorSnapshot(revision, rows);
    }
}
