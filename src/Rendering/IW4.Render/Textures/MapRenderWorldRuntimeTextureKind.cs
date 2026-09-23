namespace IW4.Render.Textures;

/// <summary>
/// The three Event20 runtime GfxTexture arrays, ordered by the native
/// 0x0039E080 cache/update path rather than by sampler destination number.
/// </summary>
public enum MapRenderWorldRuntimeTextureKind
{
    ReflectionProbe = 0,
    SecondaryLightmap = 1,
    PrimaryLightmap = 2
}
