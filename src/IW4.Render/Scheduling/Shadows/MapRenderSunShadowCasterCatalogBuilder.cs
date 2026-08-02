using IW4.Assets.Assets.GfxMap;
using IW4.Render.SceneBuilding;
using IW4.Render.Scheduling.Dpvs;

namespace IW4.Render.Scheduling.Shadows;

/// <summary>
/// Compatibility entry point for the PS3 fast-worker caster set. Repeated
/// moving-camera frames should retain a
/// <see cref="MapRenderSunShadowCasterCatalogProvider"/> instead, so world
/// validation, native mask conversion, and partition storage are reused.
/// </summary>
public static class MapRenderSunShadowCasterCatalogBuilder
{
    public static MapRenderSunShadowCasterCatalogBuildResult BuildFastWorker(
        MapRenderWorldSceneSource worldSource,
        MapRenderWorldDpvsThreeViewFrame frame)
    {
        ArgumentNullException.ThrowIfNull(worldSource);
        return BuildFastWorker(
            worldSource.World,
            frame);
    }

    public static MapRenderSunShadowCasterCatalogBuildResult BuildFastWorker(
        GfxWorldAsset world,
        MapRenderWorldDpvsThreeViewFrame frame)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(frame);
        return new MapRenderSunShadowCasterCatalogProvider(world)
            .BuildFastWorker(frame);
    }
}
