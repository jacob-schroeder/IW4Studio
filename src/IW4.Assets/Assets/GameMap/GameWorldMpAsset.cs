using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.GameMap;

public sealed class GameWorldMpAsset : BaseAsset
{
    public const int SerializedSize = 0x08;

    public XAssetType Type => XAssetType.GameMapMp;

    // 0x00: XString. PS3 GameWorldMp body stores root+0x00 into varXString and calls Load_XString.
    public XPointer<string> NamePointer { get; init; }
    public string? Name { get; init; }

    // 0x04: G_GlassData*. PS3 allocates inline glass data when this cell is non-null.
    public XPointer<GGlassData> GlassDataPointer { get; init; }
    public GGlassData? GlassData { get; init; }
}
