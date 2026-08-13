namespace IW4.Assets.Assets.Material;

/// <summary>
/// Proven serialized material routing flags. Unnamed bits remain valid when
/// carried through this enum and are intentionally not assigned speculative
/// meanings.
/// </summary>
[Flags]
public enum MaterialGameFlags : byte
{
    None = 0,
    HasLightmap = 0x02,
    NoMarks = 0x04,
    Sky = 0x08,
    HasReflection = 0x10,
    MagicPortal = 0x20,
    CastsShadow = 0x40,

    /// <summary>
    /// Preserves the authored material key through the delayed static-model
    /// shadow-caster queue instead of using the shared caster material.
    /// </summary>
    MaterialSpecificShadowCaster = 0x80,
    ShadowCasterRouteMask = CastsShadow | MaterialSpecificShadowCaster
}
