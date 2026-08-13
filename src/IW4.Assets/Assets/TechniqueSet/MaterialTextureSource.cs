namespace IW4.Assets.Assets.TechniqueSet;

/// <summary>
/// PS3 IW4 material texture sources stored by CodePixelSampler arguments.
/// The PS3 descriptor table contains values 0 through 26; the correlated
/// Xbox-only ColorManipulation value at 27 is not part of this ABI.
/// </summary>
public enum MaterialTextureSource : uint
{
    Black = 0,
    White = 1,
    IdentityNormalMap = 2,
    ModelLighting = 3,
    LightmapPrimary = 4,
    LightmapSecondary = 5,
    ShadowMapSun = 6,
    ShadowMapSpot = 7,
    Feedback = 8,
    ResolvedPostSun = 9,
    ResolvedScene = 10,
    PostEffect0 = 11,
    PostEffect1 = 12,
    LightAttenuation = 13,
    Outdoor = 14,
    FloatZ = 15,
    ProcessedFloatZ = 16,
    RawFloatZ = 17,
    HalfParticles = 18,
    HalfParticlesZ = 19,
    CaseTexture = 20,
    CinematicY = 21,
    CinematicCr = 22,
    CinematicCb = 23,
    CinematicA = 24,
    ReflectionProbe = 25,
    AlternateScene = 26,
    Count = 27
}
