namespace IW4.Render.OpenGl.Programs;

/// <summary>
/// Context-local cache for immutable uniform locations on linked programs.
/// OpenGL assigns a location at link time, so repeating the driver string
/// lookup for every mesh that shares a program is redundant.
/// </summary>
internal sealed class OpenGlUniformLocationCache
{
    private readonly Func<uint, string, int> _query;
    private readonly Dictionary<uint, Dictionary<string, int>> _byProgram = [];
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private long _requestCount;
    private long _queryCount;
    private long _cacheHitCount;

    internal OpenGlUniformLocationCache(
        Func<uint, string, int> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        _query = query;
    }

    internal int Get(uint programHandle, string exactName)
    {
        EnsureOwnerThread();
        if (programHandle == 0)
            throw new ArgumentOutOfRangeException(nameof(programHandle));
        ArgumentException.ThrowIfNullOrWhiteSpace(exactName);

        _requestCount = checked(_requestCount + 1);
        if (_byProgram.TryGetValue(
                programHandle,
                out Dictionary<string, int>? locations) &&
            locations.TryGetValue(exactName, out int cached))
        {
            _cacheHitCount = checked(_cacheHitCount + 1);
            return cached;
        }

        int location = _query(programHandle, exactName);
        _queryCount = checked(_queryCount + 1);
        locations ??= AddProgram(programHandle);
        locations.Add(exactName, location);
        return location;
    }

    internal OpenGlUniformLocationCacheTelemetry CreateTelemetry()
    {
        EnsureOwnerThread();
        return new(
            RequestCount: _requestCount,
            QueryCount: _queryCount,
            CacheHitCount: _cacheHitCount,
            ProgramCount: _byProgram.Count);
    }

    internal void Clear()
    {
        EnsureOwnerThread();
        _byProgram.Clear();
        _requestCount = 0;
        _queryCount = 0;
        _cacheHitCount = 0;
    }

    private Dictionary<string, int> AddProgram(uint programHandle)
    {
        var locations = new Dictionary<string, int>(
            StringComparer.Ordinal);
        _byProgram.Add(programHandle, locations);
        return locations;
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException(
                "The OpenGL uniform-location cache may only be used on its owning render thread.");
        }
    }
}

internal readonly record struct OpenGlUniformLocationCacheTelemetry(
    long RequestCount,
    long QueryCount,
    long CacheHitCount,
    int ProgramCount);
