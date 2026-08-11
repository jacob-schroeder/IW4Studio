using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.GfxMap;

public sealed class GfxStaticModelDrawInst
{
    public const int SerializedSize = 0x2C;

    public GfxPackedPlacement Placement { get; init; } = new();
    public XPointer<XModelAsset> ModelPointer { get; init; }
    public XModelAsset? Model { get; init; }
    public ushort CullDist { get; init; }
    public ushort LightingHandle { get; init; }
    public byte ReflectionProbeIndex { get; init; }
    public byte PrimaryLightIndex { get; init; }
    public byte Flags { get; init; }
    public byte FirstMaterialSkinIndex { get; init; }
    public GfxColor GroundLighting { get; init; } // 0x28
}
