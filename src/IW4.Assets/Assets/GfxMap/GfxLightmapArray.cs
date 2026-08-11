using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.GfxMap;

public sealed class GfxLightmapArray
{
    public const int SerializedSize = 0x08;

    public XPointer<GfxImageAsset> PrimaryPointer { get; init; }
    public GfxImageAsset? Primary { get; init; }
    public XPointer<GfxImageAsset> SecondaryPointer { get; init; }
    public GfxImageAsset? Secondary { get; init; }
}
