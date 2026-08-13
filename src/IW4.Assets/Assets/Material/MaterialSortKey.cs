namespace IW4.Assets.Assets.Material;

/// <summary>
/// Proven names in the six-bit material sort domain. Unnamed ordinals remain
/// valid and are intentionally left unassigned.
/// </summary>
public enum MaterialSortKey : byte
{
    OpaqueAmbient = 0,
    Opaque = 1,
    Sky = 2,
    Skybox = 3,
    DecalBottom1 = 6,
    DecalBottom2 = 7,
    DecalBottom3 = 8,
    DecalStatic = 9,
    DecalMiddle1 = 10,
    DecalMiddle2 = 11,
    DecalMiddle3 = 12,
    DecalWeaponImpact = 13,
    DecalTop1 = 14,
    DecalTop2 = 15,
    DecalTop3 = 16,
    Multiplicative = 17,
    BannerCurtain = 18,
    TransparentWater = 20,
    WindowInside = 24,
    WindowOutside = 25,
    Distortion = 43,
    BeforeEffectsBottom = 44,
    BeforeEffectsMiddle = 45,
    AdditiveBlend = 47,
    EffectAutoSort = 48,
    AfterEffectsBottom = 49,
    AfterEffectsMiddle = 50,
    AfterEffectsTop = 51,
    ViewmodelEffect = 53
}
