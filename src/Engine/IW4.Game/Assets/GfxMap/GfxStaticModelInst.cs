using IW4.Game.Math;

namespace IW4.Game.Assets.GfxMap;

public sealed class GfxStaticModelInst
{
    public const int SerializedSize = 0x24;

    public Bounds Bounds { get; init; } = new(); // 0x00: midpoint + half-size
    public Vec3 LightingOrigin { get; init; }    // 0x18
}
