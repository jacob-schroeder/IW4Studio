using IW4.Assets.Assets.GfxMap;
using IW4.Runtime.Assets;
using IW4.Runtime.Assets.GfxMap;
using IW4.Render.Assets;
using IW4.Runtime.Assets.Images;

namespace IW4.Render.SceneBuilding;

/// <summary>
/// Immutable loaded-scene ownership for the EditorPreview world pipeline. This
/// retains only canonical asset/runtime inputs that exist independently of a
/// camera frame; DPVS, selectors, constants, matrices, and target clear state
/// deliberately do not belong here.
/// </summary>
public sealed class MapRenderWorldSceneSource
{
    internal MapRenderWorldSceneSource(
        string fastFilePath,
        GfxWorldAsset world,
        XAssetHandle<GfxWorldAsset> worldHandle,
        XAssetActiveProviderSnapshot worldProvider,
        GfxWorldTextureRuntimeSession textureRuntime,
        RenderAssetLookup assetLookup,
        IGfxImagePayloadResolver imageStreams,
        MapRenderWorldSceneLightSourceBuildResult sceneLights)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fastFilePath);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(worldProvider);
        ArgumentNullException.ThrowIfNull(textureRuntime);
        ArgumentNullException.ThrowIfNull(assetLookup);
        ArgumentNullException.ThrowIfNull(imageStreams);
        ArgumentNullException.ThrowIfNull(sceneLights);
        if (worldHandle.IsNone ||
            worldHandle.Address != worldProvider.SlotAddress ||
            worldProvider.SlotAddress != textureRuntime.WorldAddress ||
            worldProvider.PoolRevision < 0 ||
            worldProvider.IsReferencePlaceholder ||
            !worldProvider.IsActiveCanonicalProvider ||
            !worldProvider.CanonicalProjectionMatchesProviderAsset ||
            !ReferenceEquals(world, textureRuntime.World))
        {
            throw new ArgumentException(
                "World-source identities do not describe one active canonical GfxWorld provider.",
                nameof(worldProvider));
        }
        if (sceneLights.Source is { } lightSource &&
            lightSource.Provider.PoolRevision != worldProvider.PoolRevision)
        {
            throw new ArgumentException(
                "World and scene-light providers must belong to one asset-pool revision.",
                nameof(sceneLights));
        }

        FastFilePath = Path.GetFullPath(fastFilePath);
        World = world;
        WorldHandle = worldHandle;
        WorldProvider = worldProvider with { };
        TextureRuntime = textureRuntime;
        AssetLookup = assetLookup;
        ImageStreams = imageStreams;
        SceneLights = sceneLights;
    }

    public string FastFilePath { get; }

    public GfxWorldAsset World { get; }

    public XAssetHandle<GfxWorldAsset> WorldHandle { get; }

    /// <summary>
    /// Active provider identity at scene construction. Later per-frame asset
    /// snapshots must still validate the live pool revision independently.
    /// </summary>
    public XAssetActiveProviderSnapshot WorldProvider { get; }

    public GfxWorldTextureRuntimeSession TextureRuntime { get; }

    public RenderAssetLookup AssetLookup { get; }

    public IGfxImagePayloadResolver ImageStreams { get; }

    public MapRenderWorldSceneLightSourceBuildResult SceneLights { get; }

    public long AssetPoolRevisionAtConstruction => WorldProvider.PoolRevision;
}
