using IW4.Assets.Assets.GfxMap;
using IW4.Render.Scheduling.Clear;

namespace IW4.Render.Scheduling.Dpvs;

/// <summary>
/// Exact-input cache for the immutable three-view DPVS result. Editor preview
/// commonly submits several frames without changing any camera/projection
/// input; repeating portal traversal and both sun-frustum culls in that case
/// cannot change the result. A changed world reference, provider identity or
/// revision, camera, framebuffer extent, or far-plane input always misses.
/// </summary>
public sealed class MapRenderWorldDpvsNormalCameraVisibilityCache :
    IMapRenderWorldDpvsNormalCameraVisibilityProvider
{
    private readonly object _gate = new();
    private readonly IMapRenderWorldDpvsNormalCameraVisibilityProvider
        _provider;
    private GfxWorldAsset? _world;
    private CacheKey? _key;
    private MapRenderWorldDpvsVisibilityBuildResult? _result;

    public MapRenderWorldDpvsNormalCameraVisibilityCache(
        IMapRenderWorldDpvsNormalCameraVisibilityProvider provider)
    {
        _provider = provider ??
            throw new ArgumentNullException(nameof(provider));
    }

    public string ProducerIdentity => _provider.ProducerIdentity;

    public long SourceRevision => _provider.SourceRevision;

    public long HitCount { get; private set; }

    public long MissCount { get; private set; }

    public MapRenderWorldDpvsVisibilityBuildResult Build(
        GfxWorldAsset world,
        RenderCamera camera,
        MapRenderNormalCameraFramebufferExtent framebufferExtent,
        MapRenderNormalCameraFarPlaneState farPlane)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(farPlane);

        string producerIdentity = _provider.ProducerIdentity;
        long sourceRevision = _provider.SourceRevision;
        var key = new CacheKey(
            producerIdentity,
            sourceRevision,
            camera,
            framebufferExtent,
            farPlane.RZFar,
            farPlane.RendererFallback);
        lock (_gate)
        {
            if (ReferenceEquals(_world, world) &&
                _key == key &&
                _result is not null)
            {
                HitCount++;
                return _result;
            }

            MapRenderWorldDpvsVisibilityBuildResult result =
                _provider.Build(
                    world,
                    camera,
                    framebufferExtent,
                    farPlane) ??
                throw new InvalidOperationException(
                    $"Three-view provider '{producerIdentity}' returned no typed result.");
            if (!string.Equals(
                    producerIdentity,
                    _provider.ProducerIdentity,
                    StringComparison.Ordinal) ||
                sourceRevision != _provider.SourceRevision)
            {
                throw new InvalidOperationException(
                    "Three-view provider identity or source revision changed during Build.");
            }

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
            _key = null;
            _result = null;
        }
    }

    private readonly record struct CacheKey(
        string ProducerIdentity,
        long SourceRevision,
        RenderCamera Camera,
        MapRenderNormalCameraFramebufferExtent FramebufferExtent,
        float RZFar,
        float RendererFallback);
}
