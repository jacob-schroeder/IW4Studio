using System.Diagnostics;
using Silk.NET.OpenGL;

namespace IW4.Render.OpenGl.Programs;

/// <summary>
/// Load-scoped ownership for compiled OpenGL shader objects. A linked program
/// never mutates an attached shader, so one exact stage/source object can be
/// attached to every program that uses that same source during a renderer
/// load. The cache must be disposed before the load returns.
/// </summary>
internal sealed class MapRenderOpenGlLoadShaderObjectCache : IDisposable
{
    private readonly Func<ShaderType, string, uint> _compile;
    private readonly Action<uint> _delete;
    private readonly Dictionary<MapRenderOpenGlShaderObjectKey, uint>
        _shaderObjects = [];
    private readonly HashSet<uint> _ownedHandles = [];
    private readonly int _ownerThreadId =
        Environment.CurrentManagedThreadId;
    private long _requestCount;
    private long _cacheHitCount;
    private long _compileAttemptCount;
    private long _successfulCompilationCount;
    private long _vertexCompilationCount;
    private long _fragmentCompilationCount;
    private TimeSpan _compileElapsed;
    private bool _disposed;

    internal MapRenderOpenGlLoadShaderObjectCache(
        Func<ShaderType, string, uint> compile,
        Action<uint> delete)
    {
        ArgumentNullException.ThrowIfNull(compile);
        ArgumentNullException.ThrowIfNull(delete);
        _compile = compile;
        _delete = delete;
    }

    internal uint GetOrCompile(ShaderType type, string exactSource)
    {
        EnsureUsableOnOwnerThread();
        ArgumentNullException.ThrowIfNull(exactSource);

        _requestCount = checked(_requestCount + 1);
        var key = new MapRenderOpenGlShaderObjectKey(type, exactSource);
        if (_shaderObjects.TryGetValue(key, out uint cached))
        {
            _cacheHitCount = checked(_cacheHitCount + 1);
            return cached;
        }

        _compileAttemptCount = checked(_compileAttemptCount + 1);
        long started = Stopwatch.GetTimestamp();
        uint handle;
        try
        {
            handle = _compile(type, exactSource);
        }
        finally
        {
            _compileElapsed += Stopwatch.GetElapsedTime(started);
        }

        if (handle == 0)
        {
            throw new InvalidOperationException(
                "OpenGL shader compiler returned the reserved zero handle.");
        }
        if (!_ownedHandles.Add(handle))
        {
            throw new InvalidOperationException(
                $"OpenGL shader compiler returned already-owned handle {handle} for another exact shader source.");
        }

        try
        {
            _shaderObjects.Add(key, handle);
        }
        catch
        {
            _ownedHandles.Remove(handle);
            _delete(handle);
            throw;
        }

        _successfulCompilationCount =
            checked(_successfulCompilationCount + 1);
        if (type == ShaderType.VertexShader)
        {
            _vertexCompilationCount =
                checked(_vertexCompilationCount + 1);
        }
        else if (type == ShaderType.FragmentShader)
        {
            _fragmentCompilationCount =
                checked(_fragmentCompilationCount + 1);
        }

        return handle;
    }

    internal MapRenderOpenGlShaderObjectCacheTelemetry CreateTelemetry()
    {
        EnsureOwnerThread();
        return new MapRenderOpenGlShaderObjectCacheTelemetry(
            RequestCount: _requestCount,
            CacheHitCount: _cacheHitCount,
            CompileAttemptCount: _compileAttemptCount,
            SuccessfulCompilationCount: _successfulCompilationCount,
            VertexCompilationCount: _vertexCompilationCount,
            FragmentCompilationCount: _fragmentCompilationCount,
            CompileElapsed: _compileElapsed);
    }

    public void Dispose()
    {
        EnsureOwnerThread();
        if (_disposed)
            return;

        _disposed = true;
        List<Exception>? failures = null;
        foreach (uint handle in _shaderObjects.Values)
        {
            try
            {
                _delete(handle);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        _shaderObjects.Clear();
        _ownedHandles.Clear();
        if (failures is not null)
        {
            throw new AggregateException(
                "One or more load-scoped OpenGL shader objects could not be deleted.",
                failures);
        }
    }

    private void EnsureUsableOnOwnerThread()
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException(
                "The OpenGL shader-object cache may only be used and disposed on its owning render thread.");
        }
    }
}

/// <summary>
/// Dictionary identity for one exact OpenGL shader stage and source. Hashing
/// is only an index optimization; equality always compares the complete source
/// ordinally, so hash collisions cannot alias different GLSL.
/// </summary>
internal readonly struct MapRenderOpenGlShaderObjectKey :
    IEquatable<MapRenderOpenGlShaderObjectKey>
{
    internal MapRenderOpenGlShaderObjectKey(
        ShaderType type,
        string exactSource)
    {
        ArgumentNullException.ThrowIfNull(exactSource);
        Type = type;
        ExactSource = exactSource;
    }

    internal ShaderType Type { get; }

    internal string ExactSource { get; }

    public bool Equals(MapRenderOpenGlShaderObjectKey other) =>
        Type == other.Type &&
        string.Equals(
            ExactSource,
            other.ExactSource,
            StringComparison.Ordinal);

    public override bool Equals(object? obj) =>
        obj is MapRenderOpenGlShaderObjectKey other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(
            (int)Type,
            StringComparer.Ordinal.GetHashCode(ExactSource));

    public static bool operator ==(
        MapRenderOpenGlShaderObjectKey left,
        MapRenderOpenGlShaderObjectKey right) =>
        left.Equals(right);

    public static bool operator !=(
        MapRenderOpenGlShaderObjectKey left,
        MapRenderOpenGlShaderObjectKey right) =>
        !left.Equals(right);
}

internal readonly record struct MapRenderOpenGlShaderObjectCacheTelemetry(
    long RequestCount,
    long CacheHitCount,
    long CompileAttemptCount,
    long SuccessfulCompilationCount,
    long VertexCompilationCount,
    long FragmentCompilationCount,
    TimeSpan CompileElapsed);
