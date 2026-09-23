using IW4.Loaders.Database;
using IW4.Game.Assets.ColMap;
using IW4.Game.Pointers;
using IW4.Game.Zone;
using IW4.Game.IO;

namespace IW4.Loaders.Assets.ColMap;

public sealed class ClipMapLoader
{
    private readonly ClipMapAssetLoader _spLoader = new(XAssetType.ColMapSp);
    private readonly ClipMapAssetLoader _mpLoader = new(XAssetType.ColMapMp);

    // ColMapSp and ColMapMp share this serialized loader.
    public ClipMapAsset LoadFromAssetPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context,
        XAssetType serializedType = XAssetType.ColMapMp)
    {
        if (serializedType is not (XAssetType.ColMapSp or XAssetType.ColMapMp))
        {
            throw new ArgumentOutOfRangeException(
                nameof(serializedType),
                serializedType,
                "ClipMapLoader only accepts serialized ColMapSp or ColMapMp assets.");
        }

        return (serializedType == XAssetType.ColMapSp ? _spLoader : _mpLoader)
            .LoadFromAssetPointer(cursor, pointer, context);
    }
}
