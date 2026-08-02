using IW4.Assets.Zone;
using IW4.FastFiles.Zone;

namespace IW4.Runtime.Assets.Lifecycle;

/// <summary>
/// Managed identity for process-global state indexed by a native typed-pool
/// allocation. It is deliberately separate from zone-memory ownership.
/// </summary>
public readonly record struct XAssetRuntimeAllocationKey
{
    private XAssetRuntimeAllocationKey(
        XAssetRuntimeAllocationKind kind,
        XAssetPoolAddress slotAddress,
        long providerId)
    {
        Kind = kind;
        SlotAddress = slotAddress;
        ProviderId = providerId;
    }

    public XAssetRuntimeAllocationKind Kind { get; }

    public XAssetPoolAddress SlotAddress { get; }

    public long ProviderId { get; }

    public static XAssetRuntimeAllocationKey ForStableSlot(XAssetPoolAddress slotAddress) =>
        new(XAssetRuntimeAllocationKind.StableSlot, slotAddress, providerId: 0);

    public static XAssetRuntimeAllocationKey ForProvider(
        XAssetPoolAddress slotAddress,
        long providerId)
    {
        if (providerId <= 0)
            throw new ArgumentOutOfRangeException(nameof(providerId));

        return new XAssetRuntimeAllocationKey(
            XAssetRuntimeAllocationKind.Provider,
            slotAddress,
            providerId);
    }

    public override string ToString() =>
        Kind == XAssetRuntimeAllocationKind.StableSlot
            ? $"{SlotAddress}:stable"
            : $"{SlotAddress}:provider:{ProviderId}";
}
