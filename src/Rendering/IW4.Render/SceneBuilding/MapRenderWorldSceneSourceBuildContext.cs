using IW4.Render.Assets;
using IW4.Runtime.Assets.GfxMap;
using IW4.Runtime.Assets.Images;

namespace IW4.Render.SceneBuilding;

/// <summary>
/// Internal construction result used by the EditorPreview scene builder. The
/// public source owns these dependencies after a successful build.
/// </summary>
internal sealed class MapRenderWorldSceneSourceBuildContext
{
    internal MapRenderWorldSceneSourceBuildContext(
        MapRenderWorldSceneSourceBuildResult result,
        RenderAssetLookup assetLookup,
        IGfxImagePayloadResolver imageStreams,
        GfxWorldTextureRuntimeSession? textureRuntime)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(assetLookup);
        ArgumentNullException.ThrowIfNull(imageStreams);

        Result = result;
        AssetLookup = assetLookup;
        ImageStreams = imageStreams;
        TextureRuntime = textureRuntime;
    }

    internal MapRenderWorldSceneSourceBuildResult Result { get; }

    internal RenderAssetLookup AssetLookup { get; }

    internal IGfxImagePayloadResolver ImageStreams { get; }

    internal GfxWorldTextureRuntimeSession? TextureRuntime { get; }
}
