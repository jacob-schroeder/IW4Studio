namespace IW4.Render.Scheduling;

/// <summary>
/// Exact current-frame selector result for one rigid static-model material
/// surface. Three-view page ownership and scene-light allocation remain
/// independent inputs; neither axis is inferred from the other.
/// </summary>
public sealed record MapRenderStaticModelFrameTechniqueSelection(
    long Revision,
    MapRenderStaticModelReceiverIdentity Identity,
    MapRenderStaticModelReceiverPage Page,
    MapRenderSurfaceType SurfaceType,
    int SceneLightVariant,
    int TechniqueSlot,
    bool ShadowMapAllocated);
