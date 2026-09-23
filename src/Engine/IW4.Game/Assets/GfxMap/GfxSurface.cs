using IW4.Game.Assets.Image;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.XModel;
using IW4.Game.Pointers;
using IW4.Game.Zone;

namespace IW4.Game.Assets.GfxMap;

public sealed class GfxSurface
{
    public const int SerializedSize = 0x1C;

    public SrfTriangles Triangles { get; init; } = new();
    public XPointer<MaterialAsset> MaterialPointer { get; init; }
    public MaterialAsset? Material { get; init; }
    public byte LightmapIndex { get; init; }
    public byte ReflectionProbeIndex { get; init; }
    public byte PrimaryLightIndex { get; init; }
    public GfxSurfaceFlags Flags { get; init; }
}
