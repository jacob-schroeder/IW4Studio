using IW4.FastFiles.Zone;

namespace IW4.Assets.Zone;

public readonly record struct XAssetPoolAddress(
    XAssetType AssetType,
    int Slot,
    int RawValue)
{
    public override string ToString() =>
        $"ASSET_POOL:{AssetType}[{Slot}]@0x{unchecked((uint)RawValue):X8}";
}
