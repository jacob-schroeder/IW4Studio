using IW4.Game.Zone;
using IW4.Game.Assets;
using IW4.Game.Pointers;

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
