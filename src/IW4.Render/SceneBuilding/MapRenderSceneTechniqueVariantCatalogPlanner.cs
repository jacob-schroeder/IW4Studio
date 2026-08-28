using IW4.Assets.Assets.GfxMap;
using IW4.Render.Scheduling;

namespace IW4.Render.SceneBuilding;

public static class MapRenderSceneTechniqueVariantCatalogPlanner
{
    public static MapRenderSceneTechniqueVariantCatalog Plan(
        GfxWorldAsset world,
        MapRenderDrawMethod drawMethod,
        MapRenderSceneLightSelectorAssetState sceneLights)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(drawMethod);
        ArgumentNullException.ThrowIfNull(sceneLights);

        int staticSurfaceCount = world.Models.Count == 0
            ? 0
            : world.Models[0].SurfaceCount;
        if (staticSurfaceCount > world.Dpvs.Surfaces.Count)
        {
            throw new InvalidDataException(
                $"GfxWorld '{world.Name}' brush model zero owns " +
                $"{staticSurfaceCount} of only {world.Dpvs.Surfaces.Count} surfaces.");
        }

        MapRenderTechniqueVariantSet?[] worldSurfaces = world.Dpvs.Surfaces
            .Select((surface, surfaceIndex) =>
                surfaceIndex < staticSurfaceCount
                    ? TryPlan(
                        drawMethod,
                        sceneLights,
                        surface.PrimaryLightIndex)
                    : null)
            .ToArray();
        MapRenderTechniqueVariantSet?[] staticDrawInstances =
            world.Dpvs.SModelDrawInsts
                .Select(drawInst => TryPlan(
                    drawMethod,
                    sceneLights,
                    drawInst.PrimaryLightIndex))
                .ToArray();
        return new(
            drawMethod,
            worldSurfaces,
            staticDrawInstances);
    }

    private static MapRenderTechniqueVariantSet? TryPlan(
        MapRenderDrawMethod drawMethod,
        MapRenderSceneLightSelectorAssetState sceneLights,
        int primaryLightIndex) =>
        (uint)primaryLightIndex < (uint)sceneLights.SceneLightCount
            ? MapRenderTechniqueVariantPlanner.Plan(
                drawMethod,
                sceneLights,
                primaryLightIndex)
            : null;
}
