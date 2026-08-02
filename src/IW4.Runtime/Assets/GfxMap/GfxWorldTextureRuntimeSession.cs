using IW4.Assets.Zone;
using IW4.Assets.Assets.GfxMap;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets.Lifecycle.State;
using IW4.Runtime.Database;

namespace IW4.Runtime.Assets.GfxMap;

/// <summary>
/// Pins the canonical capacity-one GfxWorld texture runtime to the block
/// streams that own its RUNTIME descriptor arrays.
/// </summary>
public sealed class GfxWorldTextureRuntimeSession
{
    private readonly object _frameSequenceLock = new();

    public GfxWorldTextureRuntimeSession(
        GfxWorldAsset world,
        XAssetPool assetPool,
        IXAssetSourceMemory blocks,
        GfxWorldRuntimeState runtimeState)
    {
        (XAssetPoolAddress worldAddress, GfxWorldAsset activeWorld) =
            GfxWorldTextureRuntimeMaterializer.ResolveWorld(
                world,
                assetPool,
                blocks,
                runtimeState);

        World = activeWorld;
        WorldAddress = worldAddress;
        AssetPool = assetPool;
        Blocks = blocks;
        RuntimeState = runtimeState;
    }

    public GfxWorldAsset World { get; }

    public XAssetPoolAddress WorldAddress { get; }

    public XAssetPool AssetPool { get; }

    public IXAssetSourceMemory Blocks { get; }

    public GfxWorldRuntimeState RuntimeState { get; }

    public GfxWorldTextureState EnsureInitialized() =>
        GfxWorldTextureRuntimeInitializer.EnsureInitialized(
            World,
            AssetPool,
            Blocks,
            RuntimeState);

    public GfxWorldLightmapTextureRefreshResult RefreshLightmaps(
        GfxWorldLightmapTextureOverrideSelection selection,
        IGfxWorldRenderThreadSynchronizer synchronizer) =>
        GfxWorldLightmapTextureRefreshProcessor.R_UpdateFrameLightmapTextures(
            World,
            AssetPool,
            Blocks,
            RuntimeState,
            selection,
            synchronizer);

    public GfxWorldTextureState RequireTextureState()
    {
        GfxWorldTextureState state = RuntimeState.TextureState ??
            throw new InvalidOperationException(
                "The GfxWorld texture runtime session has not been initialized.");
        if (state.WorldAddress != WorldAddress)
        {
            throw new InvalidOperationException(
                $"The active texture state belongs to {state.WorldAddress}, not {WorldAddress}.");
        }

        return state;
    }

    /// <summary>
    /// Serializes the complete managed frontend frame sequence. Native
    /// R_SyncRenderThread protects descriptor mutation from an older consumer;
    /// it does not serialize two concurrent R_RenderScene producers.
    /// </summary>
    public T RunFrameTextureSequence<T>(Func<T> sequence)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        lock (_frameSequenceLock)
            return sequence();
    }
}
