using IW4.Game.Assets.Image;
using IW4.Game.Pointers;

namespace IW4.Game.Assets.GfxMap;

public sealed class GfxLightmapArray
{
    public const int SerializedSize = 0x08;

    public XPointer<GfxImageAsset> PrimaryPointer { get; init; }
    public GfxImageAsset? Primary { get; init; }
    public XPointer<GfxImageAsset> SecondaryPointer { get; init; }
    public GfxImageAsset? Secondary { get; init; }
}
