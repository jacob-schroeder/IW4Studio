using IW4.Assets.Assets.Material;

namespace IW4.Render.Scheduling;

/// <summary>
/// One exact draw-method row/column result retained for runtime selection.
/// </summary>
public sealed record MapRenderTechniqueVariant(
    GfxDrawSurfSurfaceType SurfaceType,
    MapRenderTechniqueVariantAllocation Allocation,
    int SceneLightVariant,
    int TechniqueSlot)
{
    public bool HasTechnique =>
        TechniqueSlot != MapRenderDrawMethodInitializer.NoneTechnique;
}
