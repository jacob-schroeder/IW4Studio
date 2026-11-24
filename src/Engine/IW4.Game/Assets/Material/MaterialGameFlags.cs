namespace IW4.Game.Assets.Material;

/// <summary>
/// Serialized material routing flags.
/// </summary>
[Flags]
public enum MaterialGameFlags : byte
{
    None = 0,
    /// <summary>Excludes this material from receiving projected marks on PS3.
    /// The original authoring property that sets this bit is unknown.</summary>
    NoImpactMarks = 0x01,
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
    ShadowCasterRouteMask = CastsShadow | MaterialSpecificShadowCaster,

    /// <summary>
    /// A material with either bit set is excluded as a projected-mark receiver
    /// on PS3. Stock layered materials retain each bit only if every input
    /// layer sets that same bit.
    /// </summary>
    MarkReceiverExclusionMask = NoImpactMarks | NoMarks
}
