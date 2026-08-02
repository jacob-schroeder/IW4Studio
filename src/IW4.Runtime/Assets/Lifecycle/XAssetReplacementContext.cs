using IW4.Assets.Zone;
using IW4.FastFiles.Zone;

namespace IW4.Runtime.Assets.Lifecycle;

public sealed record XAssetReplacementContext
{
    public XAssetReplacementContext(
        XAssetPoolAddress slotAddress,
        XAssetType assetType,
        string name,
        XAssetRuntimeAllocationKey sourceAllocation,
        XAssetRuntimeAllocationKey destinationAllocation,
        int mode)
    {
        if (slotAddress.AssetType != assetType)
        {
            throw new ArgumentException(
                $"Slot type {slotAddress.AssetType} does not match replacement type {assetType}.",
                nameof(slotAddress));
        }
        if (mode is < 0 or > 3)
            throw new ArgumentOutOfRangeException(nameof(mode));
        if (sourceAllocation.SlotAddress != slotAddress)
            throw new ArgumentException("Replacement source does not belong to the supplied stable slot.", nameof(sourceAllocation));
        if (destinationAllocation.SlotAddress != slotAddress)
            throw new ArgumentException("Replacement destination does not belong to the supplied stable slot.", nameof(destinationAllocation));
        if (sourceAllocation == destinationAllocation)
            throw new ArgumentException("Replacement source and destination allocations must be distinct.", nameof(sourceAllocation));

        SlotAddress = slotAddress;
        AssetType = assetType;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        SourceAllocation = sourceAllocation;
        DestinationAllocation = destinationAllocation;
        Mode = mode;
    }

    public XAssetPoolAddress SlotAddress { get; }

    public XAssetType AssetType { get; }

    public string Name { get; }

    /// <summary>
    /// Fallback/provider allocation whose runtime state is moving into the
    /// stable destination allocation.
    /// </summary>
    public XAssetRuntimeAllocationKey SourceAllocation { get; }

    public XAssetRuntimeAllocationKey DestinationAllocation { get; }

    public int Mode { get; }
}
