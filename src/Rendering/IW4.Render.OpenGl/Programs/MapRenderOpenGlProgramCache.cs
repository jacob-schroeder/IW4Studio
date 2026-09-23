using IW4.Render.OpenGl.Diagnostics;
using IW4.Render.OpenGl.Shaders;
using IW4.Render.Diagnostics;

namespace IW4.Render.OpenGl.Programs;

/// <summary>
/// Context-local program metadata and optional share-group linked-handle
/// reuse. No method may migrate compilation, lookup, or deletion to another
/// render thread.
/// </summary>
public sealed class MapRenderOpenGlProgramCache : IDisposable
{
    private readonly IMapRenderOpenGlProgramCompiler _compiler;
    private readonly MapRenderOpenGlShaderCompilationCounter
        _compilationCounter;
    private readonly OpenGlSharedProgramCache.UsageLease?
        _sharedProgramUsage;
    private readonly IMapRenderOpenGlLinkedProgramDescriber?
        _linkedProgramDescriber;
    private readonly string _contextIdentity;
    private readonly string _linkProfileIdentity;
    private readonly Dictionary<OpenGlProgramKey, MapRenderOpenGlProgramResource> _resources = [];
    private readonly HashSet<uint> _ownedHandles = [];
    private readonly int _ownerThreadId;
    private bool _disposed;

    public MapRenderOpenGlProgramCache(
        IMapRenderOpenGlProgramCompiler compiler)
        : this(compiler, new MapRenderOpenGlShaderCompilationCounter())
    {
    }

    internal MapRenderOpenGlProgramCache(
        IMapRenderOpenGlProgramCompiler compiler,
        MapRenderOpenGlShaderCompilationCounter compilationCounter)
        : this(
            compiler,
            compilationCounter,
            sharedProgramUsage: null)
    {
    }

    internal MapRenderOpenGlProgramCache(
        IMapRenderOpenGlProgramCompiler compiler,
        MapRenderOpenGlShaderCompilationCounter compilationCounter,
        OpenGlSharedProgramCache.UsageLease? sharedProgramUsage)
    {
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(compilationCounter);
        ArgumentException.ThrowIfNullOrWhiteSpace(compiler.ContextIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(compiler.LinkProfileIdentity);
        _compiler = compiler;
        _compilationCounter = compilationCounter;
        _sharedProgramUsage = sharedProgramUsage;
        if (sharedProgramUsage is not null &&
            compiler is not IMapRenderOpenGlLinkedProgramDescriber)
        {
            throw new ArgumentException(
                "A shared OpenGL program cache requires a compiler that can describe an existing linked handle.",
                nameof(compiler));
        }
        _linkedProgramDescriber =
            compiler as IMapRenderOpenGlLinkedProgramDescriber;
        _contextIdentity = compiler.ContextIdentity;
        _linkProfileIdentity = compiler.LinkProfileIdentity;
        _ownerThreadId = Environment.CurrentManagedThreadId;
    }

    public string ContextIdentity
    {
        get
        {
            EnsureUsableOnOwnerThread();
            return _contextIdentity;
        }
    }

    public int Count
    {
        get
        {
            EnsureUsableOnOwnerThread();
            return _resources.Count;
        }
    }

    /// <summary>
    /// Exact context-local linker/profile identity included in every program
    /// key owned by this cache.
    /// </summary>
    public string LinkProfileIdentity
    {
        get
        {
            EnsureUsableOnOwnerThread();
            return _linkProfileIdentity;
        }
    }

    public MapRenderOpenGlProgramResource GetOrCompile(
        string vertexGlsl,
        string pixelGlsl)
    {
        EnsureUsableOnOwnerThread();
        OpenGlProgramKey key = OpenGlProgramKey.Create(
            vertexGlsl,
            pixelGlsl,
            _linkProfileIdentity);
        return GetOrCompile(key, vertexGlsl, pixelGlsl);
    }

    /// <summary>
    /// Compiles fragment source that has passed backend-owned RSX lowering and
    /// retained its typed provenance through OpenGL-only composition.
    /// </summary>
    internal MapRenderOpenGlProgramResource GetOrCompileAuthored(
        string vertexGlsl,
        OpenGlAuthoredFragmentSource pixelSource)
    {
        ArgumentNullException.ThrowIfNull(pixelSource);
        return GetOrCompile(vertexGlsl, pixelSource.ExactGlsl);
    }

    public MapRenderOpenGlProgramResource GetOrCompile(
        OpenGlProgramKey key,
        string vertexGlsl,
        string pixelGlsl)
    {
        EnsureUsableOnOwnerThread();
        ArgumentNullException.ThrowIfNull(vertexGlsl);
        ArgumentNullException.ThrowIfNull(pixelGlsl);

        if (!key.MatchesSourcesForCompilerProfile(
                vertexGlsl,
                pixelGlsl,
                _linkProfileIdentity))
        {
            throw new ArgumentException(
                "OpenGL program key does not match the exact sources and cache link profile.",
                nameof(key));
        }
        if (_resources.TryGetValue(key, out MapRenderOpenGlProgramResource? cached))
            return cached;
        MapRenderOpenGlProgramResource resource;
        bool cacheOwnsHandle = false;
        if (_sharedProgramUsage is null)
        {
            _compilationCounter.RecordProgramCompilationAttempt();
            resource =
                _compiler.Compile(key, vertexGlsl, pixelGlsl) ??
                throw new InvalidOperationException(
                    "OpenGL program compiler returned no resource.");
        }
        else
        {
            MapRenderOpenGlProgramResource? compiledResource = null;
            OpenGlLinkedProgramHandleResolution resolution =
                _sharedProgramUsage.GetOrLink(
                    vertexGlsl,
                    pixelGlsl,
                    () =>
                    {
                        _compilationCounter
                            .RecordProgramCompilationAttempt();
                        compiledResource =
                            _compiler.Compile(
                                key,
                                vertexGlsl,
                                pixelGlsl) ??
                            throw new InvalidOperationException(
                                "OpenGL program compiler returned no resource.");
                        return compiledResource.Handle;
                    });
            if (!resolution.IsReady)
            {
                throw new InvalidOperationException(
                    resolution.FailureReason ??
                    "OpenGL shared-program linking failed.");
            }

            cacheOwnsHandle = resolution.CacheOwnsHandle;
            resource = compiledResource ??
                _linkedProgramDescriber!.DescribeLinkedProgram(
                    key,
                    resolution.Handle,
                    vertexGlsl,
                    pixelGlsl);
        }

        if (!cacheOwnsHandle &&
            _ownedHandles.Contains(resource.Handle))
        {
            throw new InvalidOperationException(
                $"OpenGL compiler returned already-owned program handle {resource.Handle} for another key.");
        }
        ValidateCompiledResource(
            resource,
            key,
            vertexGlsl,
            pixelGlsl,
            deleteOnFailure: !cacheOwnsHandle);

        if (!cacheOwnsHandle)
            _ownedHandles.Add(resource.Handle);
        _resources.Add(key, resource);
        return resource;
    }

    public bool TryGet(
        OpenGlProgramKey key,
        out MapRenderOpenGlProgramResource? resource)
    {
        EnsureUsableOnOwnerThread();
        return _resources.TryGetValue(key, out resource);
    }

    public void Dispose()
    {
        EnsureOwnerThread();
        if (_disposed)
            return;

        _disposed = true;
        List<Exception>? failures = null;
        foreach (uint handle in _ownedHandles)
        {
            try
            {
                _compiler.DeleteProgram(handle);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        _resources.Clear();
        _ownedHandles.Clear();
        if (failures is not null)
        {
            throw new AggregateException(
                "One or more OpenGL program handles could not be deleted.",
                failures);
        }
    }

    private void ValidateCompiledResource(
        MapRenderOpenGlProgramResource resource,
        OpenGlProgramKey expectedKey,
        string vertexGlsl,
        string pixelGlsl,
        bool deleteOnFailure)
    {
        bool valid = resource.Key == expectedKey &&
                     string.Equals(
                         resource.VertexGlslSha256,
                         OpenGlProgramKey.HashExactText(vertexGlsl),
                         StringComparison.Ordinal) &&
                     string.Equals(
                         resource.PixelGlslSha256,
                         OpenGlProgramKey.HashExactText(pixelGlsl),
                         StringComparison.Ordinal);
        if (valid)
            return;

        if (deleteOnFailure)
            _compiler.DeleteProgram(resource.Handle);
        throw new InvalidOperationException(
            "OpenGL compiler returned a resource whose key or source hashes do not match the compile request.");
    }

    private void EnsureUsableOnOwnerThread()
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureCompilerIdentity();
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException(
                "OpenGL program cache may only be used and disposed on its owning render thread.");
        }
    }

    private void EnsureCompilerIdentity()
    {
        if (!string.Equals(
                _compiler.ContextIdentity,
                _contextIdentity,
                StringComparison.Ordinal) ||
            !string.Equals(
                _compiler.LinkProfileIdentity,
                _linkProfileIdentity,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "OpenGL compiler context or link-profile identity changed after cache creation.");
        }
    }
}

/// <summary>
/// Renderer-lifetime, program-level compilation-attempt counter shared by
/// direct renderer programs and context-local presentation caches. One
/// attempt may compile multiple shader stages but advances this counter once.
/// The owning OpenGL render thread provides synchronization.
/// </summary>
internal sealed class MapRenderOpenGlShaderCompilationCounter
{
    private long _programCompilationCount;

    internal long ProgramCompilationCount => _programCompilationCount;

    internal void RecordProgramCompilationAttempt() =>
        _programCompilationCount = checked(_programCompilationCount + 1);

    internal long CountSince(long earlierProgramCompilationCount)
    {
        if (earlierProgramCompilationCount < 0 ||
            earlierProgramCompilationCount > _programCompilationCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(earlierProgramCompilationCount));
        }

        return _programCompilationCount - earlierProgramCompilationCount;
    }

    internal void RecordFrameDelta(
        MapRenderFrameTelemetry frameTelemetry,
        long earlierProgramCompilationCount)
    {
        ArgumentNullException.ThrowIfNull(frameTelemetry);
        frameTelemetry.SetCounter(
            MapRenderFrameCounter.ShaderProgramCompilations,
            CountSince(earlierProgramCompilationCount));
    }
}
