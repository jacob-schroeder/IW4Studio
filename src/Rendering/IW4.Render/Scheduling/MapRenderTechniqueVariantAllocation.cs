namespace IW4.Render.Scheduling;

/// <summary>
/// Scene-light column allocation required by a prepared technique variant.
/// This condition is independent from Event 0x0E world page membership.
/// </summary>
public enum MapRenderTechniqueVariantAllocation
{
    Unshadowed = 0,
    ShadowMapAllocated = 1
}
