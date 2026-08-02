using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.ComWorld;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;
using IW4.Runtime.Assets.GfxMap;
using IW4.Render.Assets;
using IW4.Render.Scheduling;
using IW4.Runtime.Assets.Images;

namespace IW4.Render.SceneBuilding;

internal static class MapRenderWorldSceneSourceFactory
{
    internal static MapRenderWorldSceneSourceBuildResult Create(
        string fastFilePath,
        GfxWorldAsset world,
        GfxWorldTextureRuntimeSession textureRuntime,
        RenderAssetLookup assetLookup,
        IGfxImagePayloadResolver imageStreams)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fastFilePath);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(textureRuntime);
        ArgumentNullException.ThrowIfNull(assetLookup);
        ArgumentNullException.ThrowIfNull(imageStreams);

        XAssetPool assetPool = textureRuntime.AssetPool;
        long poolRevision = assetPool.Revision;
        if (!ReferenceEquals(textureRuntime.World, world))
        {
            return MapRenderWorldSceneSourceBuildResult.Failed(
                MapRenderWorldSceneSourceBuildFailureKind
                    .RuntimeWorldIdentityMismatch,
                "The world texture runtime belongs to another GfxWorld instance.");
        }
        if (!MapRenderAssetProviderSnapshotFactory.TryCapture(
                assetPool,
                world,
                XAssetType.GfxMap,
                poolRevision,
                out GfxWorldAsset? canonicalWorld,
                out XAssetActiveProviderSnapshot? worldProvider) ||
            canonicalWorld is null ||
            worldProvider is null ||
            !ReferenceEquals(canonicalWorld, world))
        {
            return MapRenderWorldSceneSourceBuildResult.Failed(
                MapRenderWorldSceneSourceBuildFailureKind
                    .CanonicalWorldProviderUnavailable,
                $"GfxWorld '{world.Name ?? string.Empty}' has no exact active canonical provider snapshot.");
        }
        if (assetPool.Revision != poolRevision)
        {
            return MapRenderWorldSceneSourceBuildResult.Failed(
                MapRenderWorldSceneSourceBuildFailureKind
                    .AssetPoolRevisionChanged,
                $"The canonical asset pool changed during world-source capture: start={poolRevision};end={assetPool.Revision}.");
        }

        MapRenderWorldSceneLightSourceBuildResult sceneLights =
            CaptureSceneLights(assetPool, world, poolRevision);
        long endRevision = assetPool.Revision;
        if (endRevision != poolRevision)
        {
            return MapRenderWorldSceneSourceBuildResult.Failed(
                MapRenderWorldSceneSourceBuildFailureKind
                    .AssetPoolRevisionChanged,
                $"The canonical asset pool changed during world-source capture: start={poolRevision};end={endRevision}.");
        }
        if (sceneLights.Source is { } lightSource &&
            lightSource.Provider.PoolRevision != worldProvider.PoolRevision)
        {
            return MapRenderWorldSceneSourceBuildResult.Failed(
                MapRenderWorldSceneSourceBuildFailureKind
                    .AssetPoolRevisionChanged,
                $"The exact GfxWorld and ComWorld providers were captured at different pool revisions: world={worldProvider.PoolRevision};comWorld={lightSource.Provider.PoolRevision}.");
        }
        var source = new MapRenderWorldSceneSource(
            fastFilePath,
            world,
            new XAssetHandle<GfxWorldAsset>(worldProvider.SlotAddress),
            worldProvider,
            textureRuntime,
            assetLookup,
            imageStreams,
            sceneLights);
        return MapRenderWorldSceneSourceBuildResult.Succeeded(source);
    }

    private static MapRenderWorldSceneLightSourceBuildResult CaptureSceneLights(
        XAssetPool assetPool,
        GfxWorldAsset world,
        long poolRevision)
    {
        XAssetPoolEntry[] candidates = assetPool.Entries
            .Where(entry => entry.AssetType == XAssetType.ComMap)
            .ToArray();
        if (candidates.Length == 0)
        {
            return FailedLights(
                MapRenderWorldSceneLightSourceFailureKind.ComWorldUnavailable,
                "The canonical pool contains no active ComWorld slot.");
        }
        if (candidates.Length != 1)
        {
            return FailedLights(
                MapRenderWorldSceneLightSourceFailureKind.ComWorldAmbiguous,
                $"The canonical pool contains {candidates.Length} active ComWorld slots.");
        }
        if (candidates[0].Asset is not ComWorldAsset comWorld ||
            candidates[0].IsReferencePlaceholder ||
            !MapRenderAssetProviderSnapshotFactory.TryCapture(
                assetPool,
                comWorld,
                XAssetType.ComMap,
                poolRevision,
                out ComWorldAsset? canonical,
                out XAssetActiveProviderSnapshot? provider) ||
            canonical is null ||
            provider is null ||
            !ReferenceEquals(canonical, comWorld))
        {
            return FailedLights(
                MapRenderWorldSceneLightSourceFailureKind
                    .CanonicalComWorldProviderUnavailable,
                "The active ComWorld slot has no exact canonical provider snapshot.");
        }

        try
        {
            MapRenderSceneLightSelectorAssetState selector =
                MapRenderComWorldLightSelectorAdapter.Create(world, comWorld);
            return new MapRenderWorldSceneLightSourceBuildResult(
                new MapRenderWorldSceneLightSource(
                    comWorld,
                    new XAssetHandle<ComWorldAsset>(provider.SlotAddress),
                    provider,
                    selector),
                null);
        }
        catch (Exception exception) when (exception is InvalidDataException or
                                         ArgumentOutOfRangeException)
        {
            return FailedLights(
                MapRenderWorldSceneLightSourceFailureKind
                    .ComWorldProjectionInvalid,
                exception.Message);
        }
    }

    private static MapRenderWorldSceneLightSourceBuildResult FailedLights(
        MapRenderWorldSceneLightSourceFailureKind kind,
        string detail) =>
        new(null, new MapRenderWorldSceneLightSourceFailure(kind, detail));
}
