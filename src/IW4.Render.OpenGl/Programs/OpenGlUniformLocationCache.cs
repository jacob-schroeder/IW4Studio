namespace IW4.Render.OpenGl.Programs;

using IW4.Render.OpenGl.Shaders;
using IW4.Render.Shaders;

/// <summary>
/// Context-local cache for immutable uniform locations on linked programs.
/// OpenGL assigns a location at link time, so repeating the driver string
/// lookup for every mesh that shares a program is redundant.
/// </summary>
internal sealed class OpenGlUniformLocationCache
{
    private readonly Func<uint, string, int> _query;
    private readonly Dictionary<uint, Dictionary<string, int>> _byProgram = [];
    private readonly Dictionary<uint, OpenGlRsxUniformLocationLayout>
        _rsxLayouts = [];
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
            ProgramCount: _byProgram.Keys
                .Concat(_rsxLayouts.Keys)
                .Distinct()
                .Count());
    }

    internal OpenGlRsxUniformLocationLayout GetRsxLayout(
        uint programHandle)
    {
        EnsureOwnerThread();
        if (programHandle == 0)
            throw new ArgumentOutOfRangeException(nameof(programHandle));
        if (_rsxLayouts.TryGetValue(
                programHandle,
                out OpenGlRsxUniformLocationLayout? cached))
        {
            return cached;
        }

        var layout = new OpenGlRsxUniformLocationLayout(
            programHandle,
            this);
        _rsxLayouts.Add(programHandle, layout);
        return layout;
    }

    internal void Clear()
    {
        EnsureOwnerThread();
        _byProgram.Clear();
        _rsxLayouts.Clear();
        _requestCount = 0;
        _queryCount = 0;
        _cacheHitCount = 0;
    }

    internal int QueryLayoutLocation(
        uint programHandle,
        string exactName)
    {
        EnsureOwnerThread();
        _requestCount = checked(_requestCount + 1);
        int location = _query(programHandle, exactName);
        _queryCount = checked(_queryCount + 1);
        return location;
    }

    internal void RecordLayoutCacheHit()
    {
        EnsureOwnerThread();
        _requestCount = checked(_requestCount + 1);
        _cacheHitCount = checked(_cacheHitCount + 1);
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

/// <summary>
/// Dense location layout for the fixed RSX GLSL namespaces. The destination
/// ordinals are already validated engine identities, so array indexing avoids
/// rebuilding uniform names and rehashing strings for every material mesh
/// that shares one linked program. Sparse selected-pass literal slots retain
/// an integer dictionary because their argument ordinals are not bounded by a
/// fixed hardware register file.
/// </summary>
internal sealed class OpenGlRsxUniformLocationLayout
{
    private const int Unresolved = int.MinValue;

    private readonly uint _programHandle;
    private readonly OpenGlUniformLocationCache _owner;
    private readonly int[] _samplers = CreateSlots(16);
    private readonly int[] _vertexConstants =
        CreateSlots(RsxVertexConstantLayout.Count);
    private readonly int[] _codePixelConstants =
        CreateSlots(OpenGlCodePixelConstantUniformLayout.Count);
    private readonly Dictionary<int, int> _staticPixelConstants = [];

    internal OpenGlRsxUniformLocationLayout(
        uint programHandle,
        OpenGlUniformLocationCache owner)
    {
        if (programHandle == 0)
            throw new ArgumentOutOfRangeException(nameof(programHandle));
        _programHandle = programHandle;
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    internal int GetSampler(int destination)
    {
        if ((uint)destination >= (uint)_samplers.Length)
            throw new ArgumentOutOfRangeException(nameof(destination));
        return GetDense(
            _samplers,
            destination,
            $"rsxSampler{destination}");
    }

    internal int GetVertexConstant(int destination)
    {
        if ((uint)destination >= (uint)_vertexConstants.Length)
            throw new ArgumentOutOfRangeException(nameof(destination));
        return GetDense(
            _vertexConstants,
            destination,
            $"rsxVertexConst[{destination}]");
    }

    internal int GetCodePixelConstant(ushort codeIndex)
    {
        if (codeIndex >= _codePixelConstants.Length)
            throw new ArgumentOutOfRangeException(nameof(codeIndex));
        return GetDense(
            _codePixelConstants,
            codeIndex,
            OpenGlCodePixelConstantUniformLayout.ElementName(codeIndex));
    }

    internal int GetStaticPixelConstant(int argumentOrdinal)
    {
        if (argumentOrdinal < 0)
            throw new ArgumentOutOfRangeException(nameof(argumentOrdinal));
        if (_staticPixelConstants.TryGetValue(
                argumentOrdinal,
                out int cached))
        {
            _owner.RecordLayoutCacheHit();
            return cached;
        }

        int location = _owner.QueryLayoutLocation(
            _programHandle,
            OpenGlStaticPixelConstantUniformLayout.ElementName(
                argumentOrdinal));
        _staticPixelConstants.Add(argumentOrdinal, location);
        return location;
    }

    private int GetDense(
        int[] locations,
        int destination,
        string exactName)
    {
        int cached = locations[destination];
        if (cached != Unresolved)
        {
            _owner.RecordLayoutCacheHit();
            return cached;
        }

        int location = _owner.QueryLayoutLocation(
            _programHandle,
            exactName);
        locations[destination] = location;
        return location;
    }

    private static int[] CreateSlots(int count)
    {
        var result = new int[count];
        Array.Fill(result, Unresolved);
        return result;
    }
}

internal readonly record struct OpenGlUniformLocationCacheTelemetry(
    long RequestCount,
    long QueryCount,
    long CacheHitCount,
    int ProgramCount);
