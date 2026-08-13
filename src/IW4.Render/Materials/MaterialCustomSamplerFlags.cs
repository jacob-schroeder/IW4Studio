namespace IW4.Render.Materials;

/// <summary>
/// PS3 Event20 pass +0x0F implicit world-sampler flags.
/// Primary lightmap binding is active only when both PrimaryLightmap and
/// SecondaryLightmap are set; the secondary branch owns the primary branch.
/// </summary>
[Flags]
public enum MaterialCustomSamplerFlags : byte
{
    None = 0,
    ReflectionProbe = 0x01,
    PrimaryLightmap = 0x02,
    SecondaryLightmap = 0x04
}
