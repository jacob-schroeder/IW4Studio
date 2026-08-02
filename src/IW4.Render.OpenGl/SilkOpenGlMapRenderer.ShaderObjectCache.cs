using IW4.Render.OpenGl.Programs;

namespace IW4.Render.OpenGl;

public sealed unsafe partial class SilkOpenGlMapRenderer
{
    private MapRenderOpenGlLoadShaderObjectCache?
        _activeLoadShaderObjectCache;

    private LoadShaderObjectCacheScope BeginLoadShaderObjectCache()
    {
        if (_activeLoadShaderObjectCache is not null)
        {
            throw new InvalidOperationException(
                "An OpenGL renderer load shader-object cache is already active.");
        }

        var cache = new MapRenderOpenGlLoadShaderObjectCache(
            CompileShader,
            _gl.DeleteShader);
        _activeLoadShaderObjectCache = cache;
        return new LoadShaderObjectCacheScope(this, cache);
    }

    private sealed class LoadShaderObjectCacheScope : IDisposable
    {
        private SilkOpenGlMapRenderer? _owner;
        private readonly MapRenderOpenGlLoadShaderObjectCache _cache;

        internal LoadShaderObjectCacheScope(
            SilkOpenGlMapRenderer owner,
            MapRenderOpenGlLoadShaderObjectCache cache)
        {
            _owner = owner;
            _cache = cache;
        }

        internal MapRenderOpenGlShaderObjectCacheTelemetry Telemetry =>
            _cache.CreateTelemetry();

        public void Dispose()
        {
            SilkOpenGlMapRenderer? owner = _owner;
            if (owner is null)
                return;

            _owner = null;
            if (!ReferenceEquals(
                    owner._activeLoadShaderObjectCache,
                    _cache))
            {
                throw new InvalidOperationException(
                    "The active OpenGL load shader-object cache changed before its scope ended.");
            }

            owner._activeLoadShaderObjectCache = null;
            _cache.Dispose();
        }
    }
}
