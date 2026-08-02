using IW4.Assets.Assets.GfxMap;
using IW4.Runtime.Assets;
using IW4.Runtime.Assets.GfxMap;
using IW4.Runtime.Database;
using IW4.Render.Assets;
using IW4.Runtime.Assets.Images;

namespace IW4.Render.SceneBuilding;

/// <summary>
/// Builds the canonical camera-independent input retained by EditorPreview.
/// This path intentionally does not materialize preview geometry,
/// textures, shader contracts, picking data, or diagnostics.
/// </summary>
public sealed class MapRenderWorldSceneSourceBuilder
{
    public MapRenderWorldSceneSourceBuildResult Build(MapRenderInput input) =>
        BuildContext(input, input.Progress).Result;

    internal MapRenderWorldSceneSourceBuildContext BuildContext(
        MapRenderInput input,
        Action<string>? progress)
    {
        MapRenderAssetSource assets = input.AssetSource;
        GfxWorldTextureRuntimeSession? textureRuntime = null;
        if (input.GfxMap is { } activeWorld &&
            assets.AssetPool.TryGetEntry(
                activeWorld,
                out XAssetPoolEntry? worldEntry))
        {
            IXAssetSourceMemory worldBlocks =
                worldEntry.SourceBlocks ?? assets.Blocks;
            textureRuntime = new GfxWorldTextureRuntimeSession(
                activeWorld,
                assets.AssetPool,
                worldBlocks,
                assets.GfxWorldRuntime);
            var textureState = textureRuntime.EnsureInitialized();
            progress?.Invoke(
                $"world texture descriptors ready: " +
                $"reflection={textureState.ReflectionProbeRows.Count}, " +
                $"lightmaps={textureState.LightmapPrimaryRows.Count}, " +
                $"revision={textureState.Revision}");
        }

        IGfxImagePayloadResolver imageStreams =
            input.ImagePayloadResolver ??
            UnavailableGfxImagePayloadResolver.Instance;
        var assetLookup = new RenderAssetLookup(
            assets,
            imageStreams);

        MapRenderWorldSceneSourceBuildResult result;
        if (input.GfxMap is { } sourceWorld && textureRuntime is not null)
        {
            result = MapRenderWorldSceneSourceFactory.Create(
                input.FastFilePath,
                sourceWorld,
                textureRuntime,
                assetLookup,
                imageStreams);
        }
        else if (input.GfxMap is { } unavailableWorld)
        {
            result = MapRenderWorldSceneSourceBuildResult.Failed(
                MapRenderWorldSceneSourceBuildFailureKind
                    .CanonicalWorldProviderUnavailable,
                $"GfxWorld '{unavailableWorld.Name ?? string.Empty}' is not registered in the canonical asset pool with source-block ownership.");
        }
        else
        {
            result = MapRenderWorldSceneSourceBuildResult.NoWorld;
        }

        return new MapRenderWorldSceneSourceBuildContext(
            result,
            assetLookup,
            imageStreams,
            textureRuntime);
    }
}
