namespace IW4.Assets.Assets.GfxMap;

/// <summary>
/// Console IW4 static-model draw-instance flags.
/// </summary>
[Flags]
public enum GfxStaticModelDrawInstFlags : byte
{
    None = 0,
    NoCastShadow = 0x01,
    GroundLighting = 0x02
}
