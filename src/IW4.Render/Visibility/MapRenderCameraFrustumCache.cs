namespace IW4.Render.Visibility;

/// <summary>
/// Exact-key single-camera cache. Interactive preview commonly renders many
/// frames without moving the camera, so plane extraction is paid only when a
/// projection input actually changes.
/// </summary>
public sealed class MapRenderCameraFrustumCache
{
    private readonly object _gate = new();
    private CacheKey? _key;
    private MapRenderCameraFrustum? _frustum;

    public long HitCount { get; private set; }

    public long MissCount { get; private set; }

    public MapRenderCameraFrustum GetOrCreate(
        MapRenderCamera camera,
        float aspectRatio)
    {
        var key = new CacheKey(camera, aspectRatio);
        lock (_gate)
        {
            if (_key == key && _frustum is not null)
            {
                HitCount++;
                return _frustum;
            }

            MapRenderCameraFrustum frustum =
                MapRenderCameraFrustum.Create(camera, aspectRatio);
            _key = key;
            _frustum = frustum;
            MissCount++;
            return frustum;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _key = null;
            _frustum = null;
        }
    }

    private readonly record struct CacheKey(
        MapRenderCamera Camera,
        float AspectRatio);
}
