using IW4.FastFiles.Zone;
using IW4.Assets.Assets;
using IW4.FastFiles.Pointers;

namespace IW4.Runtime.Database;

public sealed record XAssetListEntrySnapshot(
    int Index,
    int SerializedOffset,
    XBlockAddress AssetPointerCellAddress,
    XAssetType Type,
    XPointer<BaseAsset> AssetPointer,
    XAssetHeaderKind HeaderKind)
{
    public int RawHeader => AssetPointer.Raw;

    public bool IsOpaqueHeader => HeaderKind == XAssetHeaderKind.Opaque;
}
