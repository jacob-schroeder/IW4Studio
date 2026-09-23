namespace IW4.Runtime.Assets.GfxMap;

/// <summary>
/// Render-thread synchronization boundary. A changed override cache must call
/// this exactly once before any descriptor or cache mutation.
/// </summary>
public interface IGfxWorldRenderThreadSynchronizer
{
    void R_SyncRenderThread();
}
