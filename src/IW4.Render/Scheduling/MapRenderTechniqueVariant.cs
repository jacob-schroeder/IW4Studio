namespace IW4.Render.Scheduling;

/// <summary>
/// One exact draw-method row/column result retained for runtime selection.
/// </summary>
public sealed record MapRenderTechniqueVariant(
    MapRenderSurfaceType SurfaceType,
    MapRenderTechniqueVariantAllocation Allocation,
    int SceneLightVariant,
    int TechniqueSlot)
{
    public bool HasTechnique =>
        TechniqueSlot != MapRenderDrawMethodInitializer.NoneTechnique;
}
