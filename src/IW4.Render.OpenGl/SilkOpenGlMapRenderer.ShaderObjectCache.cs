using IW4.Render.OpenGl.Programs;

namespace IW4.Render.OpenGl;

public sealed unsafe partial class SilkOpenGlMapRenderer
{
    private MapRenderOpenGlLoadShaderObjectCache?
        _activeLoadShaderObjectCache;

    private LoadShaderObjectCacheScope BeginLoadShaderObjectCache(
        bool cacheAuthoredProgramPreparations = false)
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
        try
        {
            if (cacheAuthoredProgramPreparations)
            {
                // Invocation preparation is valid only during the initial
                // synchronous Load, while all source-affecting preview state
                // used to build constant plans is stable.
                BeginAuthoredProgramPreparationCache();
            }
        }
        catch
        {
            _activeLoadShaderObjectCache = null;
            cache.Dispose();
            throw;
        }
        return new LoadShaderObjectCacheScope(
            this,
            cache,
            cacheAuthoredProgramPreparations);
    }

    private sealed class LoadShaderObjectCacheScope : IDisposable
    {
        private SilkOpenGlMapRenderer? _owner;
        private readonly MapRenderOpenGlLoadShaderObjectCache _cache;
        private readonly bool _ownsAuthoredProgramPreparations;

        internal LoadShaderObjectCacheScope(
            SilkOpenGlMapRenderer owner,
            MapRenderOpenGlLoadShaderObjectCache cache,
            bool ownsAuthoredProgramPreparations)
        {
            _owner = owner;
            _cache = cache;
            _ownsAuthoredProgramPreparations =
                ownsAuthoredProgramPreparations;
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
            try
            {
                _cache.Dispose();
            }
            finally
            {
                if (_ownsAuthoredProgramPreparations)
                    owner.ClearAuthoredProgramPreparationCache();
            }
        }
    }
}
