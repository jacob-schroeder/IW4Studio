namespace IW4.Assets.Assets.TechniqueSet;

/// <summary>
/// Serialized PS3 MaterialPass implicit world-sampler mask. OpenAssetTools
/// exposes the corresponding bit indices; PS3 stores the masks 1, 2, and 4.
/// </summary>
[Flags]
public enum MaterialCustomSamplerFlags : byte
{
    None = 0,
    ReflectionProbe = 0x01,
    PrimaryLightmap = 0x02,
    SecondaryLightmap = 0x04
}
