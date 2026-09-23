namespace IW4.Render.Scheduling.Shadows;

public sealed class MapRenderSunShadowCasterCatalogBuildResult
{
    private MapRenderSunShadowCasterCatalogBuildResult(
        MapRenderSunShadowCasterCatalog? catalog,
        MapRenderSunShadowCasterCatalogFailure? failure)
    {
        if ((catalog is null) == (failure is null))
        {
            throw new ArgumentException(
                "A caster-catalog result requires exactly one catalog or typed failure.");
        }

        Catalog = catalog;
        Failure = failure;
    }

    public MapRenderSunShadowCasterCatalog? Catalog { get; }

    public MapRenderSunShadowCasterCatalogFailure? Failure { get; }

    public bool IsSuccess => Catalog is not null;

    internal static MapRenderSunShadowCasterCatalogBuildResult Succeeded(
        MapRenderSunShadowCasterCatalog catalog) => new(catalog, null);

    internal static MapRenderSunShadowCasterCatalogBuildResult Failed(
        MapRenderSunShadowCasterCatalogFailure failure) =>
        new(null, failure);
}
