namespace IW4.Assets.Assets.GfxMap;

/// <summary>
/// Proven flags in the final byte of a serialized GfxSurface. Other bits are
/// deliberately unnamed and remain round-trippable.
/// </summary>
[Flags]
public enum GfxSurfaceFlags : byte
{
    None = 0,
    CastsSunShadow = 0x01
}
