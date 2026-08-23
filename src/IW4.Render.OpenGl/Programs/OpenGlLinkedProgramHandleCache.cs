using Silk.NET.OpenGL;

namespace IW4.Render.OpenGl.Programs;

/// <summary>
/// Share-group linked-handle reuse for exact GLSL pairs. Semantic renderer
/// metadata (for example the sampler destinations retained by
/// <see cref="GlRsxProgram"/>) deliberately does not enter this identity:
/// OpenGL linking depends on the complete shader sources and base link
/// profile, while each semantic wrapper retains its own uniform metadata.
/// The usage lane prevents simultaneous contexts from mutating one shared
/// program's uniform state; released lanes retain handles for sequential use.
/// </summary>
internal sealed class OpenGlLinkedProgramHandleCache
{
    private readonly Dictionary<
        OpenGlLinkedProgramHandleCacheKey,
        OpenGlLinkedProgramHandleResolution> _resolutions = [];
    private readonly HashSet<uint> _ownedHandles = [];
    private readonly OpenGlProgramBinaryDiskCache? _programBinaries;
    private readonly string _linkProfileIdentity;
    private readonly int _maximumEntryCount;
    private readonly int _ownerThreadId =
        Environment.CurrentManagedThreadId;
    private long _semanticRequestCount;
    private long _uniqueLinkAttemptCount;
    private long _successfulUniqueLinkCount;
    private long _linkReuseCount;
    private long _failedUniqueLinkCount;
    private long _capacityBypassCount;
    private long _programBinaryLoadAttemptCount;
    private long _programBinaryLoadHitCount;
    private long _programBinaryStoreCount;

    internal OpenGlLinkedProgramHandleCache(
        string linkProfileIdentity,
        int maximumEntryCount = int.MaxValue,
        OpenGlProgramBinaryDiskCache? programBinaries = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(linkProfileIdentity);
        if (maximumEntryCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumEntryCount));
        _linkProfileIdentity = linkProfileIdentity;
        _maximumEntryCount = maximumEntryCount;
        _programBinaries = programBinaries;
    }

    internal OpenGlLinkedProgramHandleResolution GetOrLink(
        GL currentContextApi,
        string vertexGlsl,
        string pixelGlsl,
        Func<uint> link,
        int usageLane = 0)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(currentContextApi);
        ArgumentNullException.ThrowIfNull(vertexGlsl);
        ArgumentNullException.ThrowIfNull(pixelGlsl);
        ArgumentNullException.ThrowIfNull(link);
        if (usageLane < 0)
            throw new ArgumentOutOfRangeException(nameof(usageLane));

        _semanticRequestCount =
            checked(_semanticRequestCount + 1);
        var key = new OpenGlLinkedProgramHandleCacheKey(
            usageLane,
            OpenGlProgramKey.Create(
                vertexGlsl,
                pixelGlsl,
                _linkProfileIdentity));
        if (_resolutions.TryGetValue(
                key,
                out OpenGlLinkedProgramHandleResolution cached))
        {
            _linkReuseCount = checked(_linkReuseCount + 1);
            return cached with { IsReuse = true };
        }

        bool cacheResolution = _resolutions.Count < _maximumEntryCount;
        if (!cacheResolution)
        {
            _capacityBypassCount =
                checked(_capacityBypassCount + 1);
        }
        OpenGlLinkedProgramHandleResolution resolution;
        try
        {
            bool isProgramBinaryLoad = false;
            uint handle = 0;
            if (_programBinaries?.IsAvailable == true)
            {
                _programBinaryLoadAttemptCount = checked(
                    _programBinaryLoadAttemptCount + 1);
                if (_programBinaries.TryLoad(
                        currentContextApi,
                        key.ProgramKey,
                        vertexGlsl,
                        pixelGlsl,
                        out handle))
                {
                    isProgramBinaryLoad = true;
                    _programBinaryLoadHitCount = checked(
                        _programBinaryLoadHitCount + 1);
                }
            }

            if (!isProgramBinaryLoad)
            {
                _uniqueLinkAttemptCount = checked(
                    _uniqueLinkAttemptCount + 1);
                handle = link();
            }
            if (handle == 0)
            {
                throw new InvalidOperationException(
                    "OpenGL linker returned the reserved zero program handle.");
            }
            if (_ownedHandles.Contains(handle))
            {
                throw new InvalidOperationException(
                    $"OpenGL linker returned already-owned program handle {handle} for another exact GLSL pair.");
            }

            if (cacheResolution)
                _ownedHandles.Add(handle);
            if (!isProgramBinaryLoad)
            {
                _successfulUniqueLinkCount = checked(
                    _successfulUniqueLinkCount + 1);
                if (_programBinaries?.TryStore(
                        currentContextApi,
                        key.ProgramKey,
                        vertexGlsl,
                        pixelGlsl,
                        handle) == true)
                {
                    _programBinaryStoreCount = checked(
                        _programBinaryStoreCount + 1);
                }
            }
            resolution = new(
                Handle: handle,
                FailureReason: null,
                IsReuse: false,
                IsProgramBinaryLoad: isProgramBinaryLoad,
                CacheOwnsHandle: cacheResolution,
                IsCacheResident: cacheResolution);
        }
        catch (InvalidOperationException exception)
        {
            _failedUniqueLinkCount =
                checked(_failedUniqueLinkCount + 1);
            resolution = new(
                Handle: 0,
                FailureReason: exception.Message,
                IsReuse: false,
                IsProgramBinaryLoad: false,
                CacheOwnsHandle: false,
                IsCacheResident: cacheResolution);
        }

        if (cacheResolution)
            _resolutions.Add(key, resolution);
        return resolution;
    }

    internal IReadOnlySet<uint> OwnedHandles
    {
        get
        {
            EnsureOwnerThread();
            return _ownedHandles;
        }
    }

    internal OpenGlLinkedProgramHandleCacheTelemetry
        CreateTelemetry()
    {
        EnsureOwnerThread();
        return new(
            SemanticRequestCount: _semanticRequestCount,
            UniqueLinkAttemptCount: _uniqueLinkAttemptCount,
            SuccessfulUniqueLinkCount: _successfulUniqueLinkCount,
            LinkReuseCount: _linkReuseCount,
            FailedUniqueLinkCount: _failedUniqueLinkCount,
            CapacityBypassCount: _capacityBypassCount,
            ProgramBinaryPersistenceEnabled:
                _programBinaries?.IsAvailable == true,
            ProgramBinaryLoadAttemptCount:
                _programBinaryLoadAttemptCount,
            ProgramBinaryLoadHitCount:
                _programBinaryLoadHitCount,
            ProgramBinaryStoreCount:
                _programBinaryStoreCount,
            CachedEntryCount: _resolutions.Count,
            CachedHandleCount: _ownedHandles.Count,
            MaximumEntryCount: _maximumEntryCount);
    }

    internal void Clear()
    {
        EnsureOwnerThread();
        _resolutions.Clear();
        _ownedHandles.Clear();
        _semanticRequestCount = 0;
        _uniqueLinkAttemptCount = 0;
        _successfulUniqueLinkCount = 0;
        _linkReuseCount = 0;
        _failedUniqueLinkCount = 0;
        _capacityBypassCount = 0;
        _programBinaryLoadAttemptCount = 0;
        _programBinaryLoadHitCount = 0;
        _programBinaryStoreCount = 0;
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException(
                "The OpenGL linked-program cache may only be used on its owning render thread.");
        }
    }
}

internal readonly record struct OpenGlLinkedProgramHandleCacheKey(
    int UsageLane,
    OpenGlProgramKey ProgramKey);

internal readonly record struct
    OpenGlLinkedProgramHandleResolution(
        uint Handle,
        string? FailureReason,
        bool IsReuse,
        bool IsProgramBinaryLoad,
        bool CacheOwnsHandle,
        bool IsCacheResident)
{
    internal bool IsReady =>
        Handle != 0 &&
        FailureReason is null;
}

internal readonly record struct
    OpenGlLinkedProgramHandleCacheTelemetry(
        long SemanticRequestCount,
        long UniqueLinkAttemptCount,
        long SuccessfulUniqueLinkCount,
        long LinkReuseCount,
        long FailedUniqueLinkCount,
        long CapacityBypassCount,
        bool ProgramBinaryPersistenceEnabled,
        long ProgramBinaryLoadAttemptCount,
        long ProgramBinaryLoadHitCount,
        long ProgramBinaryStoreCount,
        int CachedEntryCount,
        int CachedHandleCount,
        int MaximumEntryCount);
