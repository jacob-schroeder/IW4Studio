using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.TechniqueSet;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.Material;

public sealed class MaterialTextureDef
{
    public const int SerializedSize = 0x0c;

    public uint NameHash { get; init; }
    public byte NameStart { get; init; }
    public byte NameEnd { get; init; }
    public byte SamplerState { get; init; }
    public byte Semantic { get; init; }
    public XPointerReference DataPointer { get; init; }
    public GfxImageAsset? Image { get; init; }
    public GfxImageAsset? IncomingImage { get; init; }
    public MaterialWater? Water { get; init; }
}
