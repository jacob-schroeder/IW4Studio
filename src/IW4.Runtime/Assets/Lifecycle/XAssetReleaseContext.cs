using IW4.Assets.Zone;
using IW4.FastFiles.Zone;

namespace IW4.Runtime.Assets.Lifecycle;

public sealed record XAssetReleaseContext
{
    public XAssetReleaseContext(
        XAssetPoolAddress slotAddress,
        XAssetType assetType,
        string name,
        XAssetRuntimeAllocationKey allocation)
    {
        if (slotAddress.AssetType != assetType)
        {
            throw new ArgumentException(
                $"Slot type {slotAddress.AssetType} does not match release type {assetType}.",
                nameof(slotAddress));
        }
        if (allocation.SlotAddress != slotAddress)
            throw new ArgumentException("Release allocation does not belong to the supplied stable slot.", nameof(allocation));

        SlotAddress = slotAddress;
        AssetType = assetType;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Allocation = allocation;
    }

    public XAssetPoolAddress SlotAddress { get; }

    public XAssetType AssetType { get; }

    public string Name { get; }

    public XAssetRuntimeAllocationKey Allocation { get; }
}
