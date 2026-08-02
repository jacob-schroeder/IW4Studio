using IW4.Assets.Assets.GfxMap;
using IW4.Render.Scheduling.Clear;

namespace IW4.Render.Scheduling.Dpvs;

/// <summary>
/// Reuses the immutable camera-only DPVS result while world identity and every
/// camera/projection input are unchanged. Both successes and typed failures
/// are cached; changing any exact input performs a fresh traversal.
/// </summary>
public sealed class MapRenderWorldDpvsCameraOnlyVisibilityCache
{
    private readonly object _gate = new();
    private readonly MapRenderWorldDpvsPortalTraversalSettings _settings;
    private GfxWorldAsset? _world;
    private MapRenderWorldDpvsWorkingSet? _workingSet;
    private CacheKey? _key;
    private MapRenderWorldDpvsCameraOnlyVisibilityBuildResult? _result;

    public MapRenderWorldDpvsCameraOnlyVisibilityCache(
        MapRenderWorldDpvsPortalTraversalSettings? settings = null)
    {
        _settings = settings ??
            MapRenderWorldDpvsPortalTraversalSettings.Ps3Default;
    }

    public long HitCount { get; private set; }

    public long MissCount { get; private set; }

    public MapRenderWorldDpvsCameraOnlyVisibilityBuildResult Build(
        GfxWorldAsset world,
        MapRenderCamera camera,
        MapRenderNormalCameraFramebufferExtent framebufferExtent,
        MapRenderNormalCameraFarPlaneState farPlane,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(farPlane);

        var key = new CacheKey(
            camera,
            framebufferExtent,
            farPlane.RZFar,
            farPlane.RendererFallback);
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ReferenceEquals(_world, world) &&
                _key == key &&
                _result is not null)
            {
                HitCount++;
                return _result;
            }

            if (_workingSet is null ||
                !ReferenceEquals(_workingSet.Topology.World, world))
            {
                _workingSet = new(world);
            }

            MapRenderWorldDpvsCameraOnlyVisibilityBuildResult result =
                MapRenderWorldDpvsCameraOnlyVisibilityProducer.Build(
                    world,
                    camera,
                    framebufferExtent,
                    farPlane,
                    _settings,
                    _workingSet,
                    cancellationToken);
            _world = world;
            _key = key;
            _result = result;
            MissCount++;
            return result;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _world = null;
            _workingSet = null;
            _key = null;
            _result = null;
        }
    }

    private readonly record struct CacheKey(
        MapRenderCamera Camera,
        MapRenderNormalCameraFramebufferExtent FramebufferExtent,
        float RZFar,
        float RendererFallback);
}
