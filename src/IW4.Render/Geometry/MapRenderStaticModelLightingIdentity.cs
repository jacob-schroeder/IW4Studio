using IW4.Assets.Assets.GfxMap;

namespace IW4.Render.Geometry;

/// <summary>
/// Lossless identity copied from one native GfxStaticModelDrawInst. Mask 0x02
/// (bit 1) of <see cref="Flags"/> selects the authored
/// <see cref="GroundLighting"/> color; the clear-bit path resolves the static
/// model's lighting origin through GfxLightGrid and publishes the runtime
/// handle.
/// </summary>
public readonly record struct MapRenderStaticModelLightingIdentity(
    ushort LightingHandle,
    GfxColor GroundLighting,
    byte Flags);
