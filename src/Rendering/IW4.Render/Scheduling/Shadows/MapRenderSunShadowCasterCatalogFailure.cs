namespace IW4.Render.Scheduling.Shadows;

public sealed record MapRenderSunShadowCasterCatalogFailure(
    MapRenderSunShadowCasterCatalogFailureKind Kind,
    string Detail,
    int? ElementIndex = null);
