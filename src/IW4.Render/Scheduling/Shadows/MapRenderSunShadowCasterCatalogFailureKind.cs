namespace IW4.Render.Scheduling.Shadows;

public enum MapRenderSunShadowCasterCatalogFailureKind
{
    InvalidWorldSurfaceCardinality = 0,
    InvalidWorldStaticModelCardinality,
    FrameSurfaceCardinalityMismatch,
    FrameStaticModelCardinalityMismatch,
    SurfaceCasterMaskUnavailable
}
