using IW4.Game.Math;

namespace IW4.Game.Assets.GfxMap;

// Both kinds occur in shipped PS3 worlds. Only Spot uses direction and cone fields.
public enum GfxHeroOnlyLightType : byte
{
    Spot = 2,
    Omni = 3
}

public sealed record GfxHeroOnlyLight
{
    public const int SerializedSize = 0x38;

    public GfxHeroOnlyLightType Type { get; init; }
    public IReadOnlyList<byte> Reserved01To03 { get; init; } = new byte[3];

    // Relative linear RGB light-grid intensity; no physical unit is established.
    public Vec3 Color { get; init; }
    public Vec3 Direction { get; init; }
    public Vec3 Origin { get; init; }
    // World-space distance, followed by the dimensionless outer and inner cone cosines.
    public float Radius { get; init; }
    public float CosHalfFovOuter { get; init; }
    public float CosHalfFovInner { get; init; }
    public int Exponent { get; init; }
}
