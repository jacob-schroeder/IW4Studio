using IW4.Assets.Assets.Material;

namespace IW4.Render.Scheduling;

/// <summary>
/// Exact current-frame world selector result. Surface-page membership and the
/// effective scene-light column are retained as independent facts.
/// </summary>
public sealed record MapRenderFrameTechniqueSelection(
    long Revision,
    int SurfaceIndex,
    int PrimaryLightIndex,
    MapRenderWorldSurfacePageMembership PageMembership,
    GfxDrawSurfSurfaceType SurfaceType,
    int SceneLightVariant,
    int TechniqueSlot,
    bool ShadowMapAllocated);
