using IW4.Assets.Assets.LightDef;
using IW4.Render.Textures;

namespace IW4.Render.SceneBuilding;

public sealed partial class MapSceneBuilder
{
    /// <summary>
    /// Projects the immutable GfxLightImage payload selected by each canonical
    /// ComWorld primary-light row. Source-13 has no material-owned fallback:
    /// a missing LightDef or image stays null so the renderer can reject only
    /// the affected translated authored program group.
    /// </summary>
    private static IReadOnlyList<Texture?>
        BuildSceneLightAttenuationTextures(
            MapRenderWorldSceneSourceBuildResult worldSourceBuildResult,
            IW4.Runtime.Assets.Images.IGfxImagePayloadResolver imageStreams,
            RenderTextureCache textureCache,
            HashSet<RenderTextureCacheKey> failedTextureCacheKeys,
            ref int decodedTextureCount,
            ref int skippedTextureCount)
    {
        ArgumentNullException.ThrowIfNull(worldSourceBuildResult);
        ArgumentNullException.ThrowIfNull(imageStreams);
        ArgumentNullException.ThrowIfNull(textureCache);
        ArgumentNullException.ThrowIfNull(failedTextureCacheKeys);

        MapRenderWorldSceneSource? source = worldSourceBuildResult.Source;
        MapRenderWorldSceneLightSource? sceneLights =
            source?.SceneLights.Source;
        if (source is null || sceneLights is null)
            return [];

        int lightCount = sceneLights.SelectorState.SceneLightCount;
        var result = new Texture?[lightCount];
        var requests = new List<RenderTextureDecodeRequest>();
        var requestByLightIndex = new RenderTextureDecodeRequest?[lightCount];
        long assetPoolRevision = source.AssetPoolRevisionAtConstruction;
        for (int lightIndex = 0; lightIndex < lightCount; lightIndex++)
        {
            string? definitionName = sceneLights.ComWorld
                .PrimaryLights[lightIndex]?.DefName;
            if (string.IsNullOrWhiteSpace(definitionName) ||
                !source.AssetLookup.TryResolveCanonicalLightDef(
                    definitionName,
                    assetPoolRevision,
                    out LightDefAsset? definition))
            {
                continue;
            }

            IW4.Assets.Assets.Image.GfxImageAsset? seed =
                definition.Image ?? source.AssetLookup.ResolveImage(
                    definition.ImagePointer.Untyped);
            if (seed is null ||
                !source.AssetLookup.TryResolveCanonicalImage(
                    seed,
                    out IW4.Assets.Assets.Image.GfxImageAsset? image))
            {
                continue;
            }

            RenderTextureDecodeRequest request =
                RenderTextureDecodeRequest.Create(
                    image,
                    definition.SamplerState,
                    includeAuthoredMipChain: true);
            requestByLightIndex[lightIndex] = request;
            requests.Add(request);
        }

        RenderTextureDecodeBatch.DecodeUnique(
            requests,
            imageStreams,
            textureCache,
            failedTextureCacheKeys,
            ref decodedTextureCount,
            ref skippedTextureCount);

        for (int lightIndex = 0; lightIndex < requestByLightIndex.Length;
             lightIndex++)
        {
            if (requestByLightIndex[lightIndex] is not { } request)
                continue;

            if (textureCache.TryGetValue(request.Key, out Texture texture))
                result[lightIndex] = texture;
        }

        return result;
    }
}
