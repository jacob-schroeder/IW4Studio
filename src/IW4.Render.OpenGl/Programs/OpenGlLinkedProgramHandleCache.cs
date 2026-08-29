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
        CachedProgram> _programs = [];
    private readonly Dictionary<
        OpenGlLinkedProgramHandleCacheKey,
        CachedProgram> _pendingCapacityBypasses = [];
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
    private long _deferredLinkSubmissionCount;

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
        if (_programs.TryGetValue(
                key,
                out CachedProgram? cached))
        {
            if (cached.IsPending)
            {
                throw new InvalidOperationException(
                    "A deferred OpenGL program link must be completed through the deferred-link path before ordinary cache use.");
            }
            _linkReuseCount = checked(_linkReuseCount + 1);
            return cached.Resolution with { IsReuse = true };
        }
        if (_pendingCapacityBypasses.ContainsKey(key))
        {
            throw new InvalidOperationException(
                "A deferred nonresident OpenGL program link must be completed through the deferred-link path before ordinary cache use.");
        }

        bool cacheResolution = _programs.Count < _maximumEntryCount;
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
            _programs.Add(key, new CachedProgram(resolution));
        return resolution;
    }

    /// <summary>
    /// Registers a source program without immediately querying LinkStatus,
    /// then completes that exact program on a later call. The creation
    /// callback may submit the link before returning or arrange submission
    /// before the completion callback can run. This permits a renderer to
    /// create every shader object before issuing any program link, avoiding
    /// driver queue synchronization at a later glCreateShader call. The final
    /// result still passes through the same binary persistence, validation,
    /// and ownership rules as the synchronous path.
    /// </summary>
    internal OpenGlLinkedProgramHandleResolution GetOrLinkDeferred(
        GL currentContextApi,
        string vertexGlsl,
        string pixelGlsl,
        Func<uint> submitLink,
        Func<uint, uint> completeLink,
        bool deferNewLinkCompletion,
        int usageLane = 0)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(currentContextApi);
        ArgumentNullException.ThrowIfNull(vertexGlsl);
        ArgumentNullException.ThrowIfNull(pixelGlsl);
        ArgumentNullException.ThrowIfNull(submitLink);
        ArgumentNullException.ThrowIfNull(completeLink);
        if (usageLane < 0)
            throw new ArgumentOutOfRangeException(nameof(usageLane));

        _semanticRequestCount = checked(_semanticRequestCount + 1);
        var key = new OpenGlLinkedProgramHandleCacheKey(
            usageLane,
            OpenGlProgramKey.Create(
                vertexGlsl,
                pixelGlsl,
                _linkProfileIdentity));
        if (_programs.TryGetValue(key, out CachedProgram? cached))
        {
            if (!cached.IsPending)
            {
                _linkReuseCount = checked(_linkReuseCount + 1);
                return cached.Resolution with { IsReuse = true };
            }

            if (deferNewLinkCompletion)
                return cached.Resolution with { IsReuse = true };

            return CompleteDeferredLink(
                currentContextApi,
                key,
                cached,
                vertexGlsl,
                pixelGlsl,
                completeLink);
        }
        if (_pendingCapacityBypasses.TryGetValue(
                key,
                out CachedProgram? pendingCapacityBypass))
        {
            if (deferNewLinkCompletion)
            {
                return pendingCapacityBypass.Resolution with
                    { IsReuse = true };
            }

            return CompleteDeferredLink(
                currentContextApi,
                key,
                pendingCapacityBypass,
                vertexGlsl,
                pixelGlsl,
                completeLink);
        }

        // A binary hit is already a validated linked program and never needs
        // to enter the deferred source-link lane.
        if (_programBinaries?.IsAvailable == true)
        {
            _programBinaryLoadAttemptCount = checked(
                _programBinaryLoadAttemptCount + 1);
            if (_programBinaries.TryLoad(
                    currentContextApi,
                    key.ProgramKey,
                    vertexGlsl,
                    pixelGlsl,
                    out uint binaryHandle))
            {
                _programBinaryLoadHitCount = checked(
                    _programBinaryLoadHitCount + 1);
                bool cacheBinary = _programs.Count < _maximumEntryCount;
                if (!cacheBinary)
                {
                    _capacityBypassCount = checked(
                        _capacityBypassCount + 1);
                }
                else
                {
                    _ownedHandles.Add(binaryHandle);
                }

                var binaryResolution = new
                    OpenGlLinkedProgramHandleResolution(
                        Handle: binaryHandle,
                        FailureReason: null,
                        IsReuse: false,
                        IsProgramBinaryLoad: true,
                        CacheOwnsHandle: cacheBinary,
                        IsCacheResident: cacheBinary,
                        IsPending: false);
                if (cacheBinary)
                    _programs.Add(key, new CachedProgram(binaryResolution));
                return binaryResolution;
            }
        }

        bool cacheSubmission = _programs.Count < _maximumEntryCount;
        if (!cacheSubmission)
        {
            _capacityBypassCount = checked(_capacityBypassCount + 1);
            if (deferNewLinkCompletion)
            {
                // Keep overflow pending handles outside the resident cache.
                // The cache owns them only until completion transfers the
                // ready handle to the renderer, preserving both the hard
                // residency bound and the all-program submission ordering.
                return SubmitDeferredCapacityBypass(
                    currentContextApi,
                    key,
                    submitLink);
            }

            return LinkWithoutCaching(
                currentContextApi,
                key.ProgramKey,
                vertexGlsl,
                pixelGlsl,
                submitLink,
                completeLink);
        }

        uint pendingHandle = 0;
        bool pendingHandleOwned = false;
        try
        {
            _uniqueLinkAttemptCount = checked(
                _uniqueLinkAttemptCount + 1);
            pendingHandle = submitLink();
            if (pendingHandle == 0)
            {
                throw new InvalidOperationException(
                    "OpenGL linker returned the reserved zero program handle.");
            }
            if (!_ownedHandles.Add(pendingHandle))
            {
                throw new InvalidOperationException(
                    $"OpenGL linker returned already-owned program handle {pendingHandle} for another exact GLSL pair.");
            }
            pendingHandleOwned = true;

            _deferredLinkSubmissionCount = checked(
                _deferredLinkSubmissionCount + 1);
            var pendingResolution = new
                OpenGlLinkedProgramHandleResolution(
                    Handle: 0,
                    FailureReason: null,
                    IsReuse: false,
                    IsProgramBinaryLoad: false,
                    CacheOwnsHandle: true,
                    IsCacheResident: true,
                    IsPending: true);
            var pending = new CachedProgram(
                pendingResolution,
                pendingHandle);
            _programs.Add(key, pending);
            if (deferNewLinkCompletion)
                return pendingResolution;

            return CompleteDeferredLink(
                currentContextApi,
                key,
                pending,
                vertexGlsl,
                pixelGlsl,
                completeLink);
        }
        catch (InvalidOperationException exception)
        {
            if (pendingHandleOwned)
            {
                _ownedHandles.Remove(pendingHandle);
                SafeDeleteProgram(currentContextApi, pendingHandle);
            }
            _failedUniqueLinkCount = checked(
                _failedUniqueLinkCount + 1);
            var failure = new OpenGlLinkedProgramHandleResolution(
                Handle: 0,
                FailureReason: exception.Message,
                IsReuse: false,
                IsProgramBinaryLoad: false,
                CacheOwnsHandle: false,
                IsCacheResident: true,
                IsPending: false);
            _programs.TryAdd(key, new CachedProgram(failure));
            return failure;
        }
    }

    private OpenGlLinkedProgramHandleResolution
        SubmitDeferredCapacityBypass(
            GL currentContextApi,
            OpenGlLinkedProgramHandleCacheKey key,
            Func<uint> submitLink)
    {
        uint pendingHandle = 0;
        bool pendingHandleOwned = false;
        try
        {
            _uniqueLinkAttemptCount = checked(
                _uniqueLinkAttemptCount + 1);
            pendingHandle = submitLink();
            if (pendingHandle == 0)
            {
                throw new InvalidOperationException(
                    "OpenGL linker returned the reserved zero program handle.");
            }
            if (!_ownedHandles.Add(pendingHandle))
            {
                throw new InvalidOperationException(
                    $"OpenGL linker returned already-owned program handle {pendingHandle} for another exact GLSL pair.");
            }
            pendingHandleOwned = true;

            var pendingResolution = new
                OpenGlLinkedProgramHandleResolution(
                    Handle: 0,
                    FailureReason: null,
                    IsReuse: false,
                    IsProgramBinaryLoad: false,
                    CacheOwnsHandle: true,
                    IsCacheResident: false,
                    IsPending: true);
            _pendingCapacityBypasses.Add(
                key,
                new CachedProgram(pendingResolution, pendingHandle));
            _deferredLinkSubmissionCount = checked(
                _deferredLinkSubmissionCount + 1);
            return pendingResolution;
        }
        catch (InvalidOperationException exception)
        {
            if (pendingHandleOwned)
            {
                _ownedHandles.Remove(pendingHandle);
                SafeDeleteProgram(currentContextApi, pendingHandle);
            }
            _failedUniqueLinkCount = checked(
                _failedUniqueLinkCount + 1);
            return new OpenGlLinkedProgramHandleResolution(
                Handle: 0,
                FailureReason: exception.Message,
                IsReuse: false,
                IsProgramBinaryLoad: false,
                CacheOwnsHandle: false,
                IsCacheResident: false,
                IsPending: false);
        }
    }

    private OpenGlLinkedProgramHandleResolution CompleteDeferredLink(
        GL currentContextApi,
        OpenGlLinkedProgramHandleCacheKey key,
        CachedProgram pending,
        string vertexGlsl,
        string pixelGlsl,
        Func<uint, uint> completeLink)
    {
        bool cacheResolution = pending.Resolution.IsCacheResident;
        try
        {
            uint handle = completeLink(pending.PendingHandle);
            if (handle == 0 || handle != pending.PendingHandle)
            {
                throw new InvalidOperationException(
                    "OpenGL deferred linker completion changed or cleared the submitted program handle.");
            }

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
            var ready = new OpenGlLinkedProgramHandleResolution(
                Handle: handle,
                FailureReason: null,
                IsReuse: false,
                IsProgramBinaryLoad: false,
                CacheOwnsHandle: cacheResolution,
                IsCacheResident: cacheResolution,
                IsPending: false);
            if (cacheResolution)
            {
                pending.Resolve(ready);
            }
            else
            {
                _pendingCapacityBypasses.Remove(key);
                _ownedHandles.Remove(handle);
            }
            return ready;
        }
        catch (InvalidOperationException exception)
        {
            _ownedHandles.Remove(pending.PendingHandle);
            SafeDeleteProgram(currentContextApi, pending.PendingHandle);
            _failedUniqueLinkCount = checked(
                _failedUniqueLinkCount + 1);
            var failure = new OpenGlLinkedProgramHandleResolution(
                Handle: 0,
                FailureReason: exception.Message,
                IsReuse: false,
                IsProgramBinaryLoad: false,
                CacheOwnsHandle: false,
                IsCacheResident: cacheResolution,
                IsPending: false);
            if (cacheResolution)
                pending.Resolve(failure);
            else
                _pendingCapacityBypasses.Remove(key);
            return failure;
        }
    }

    private OpenGlLinkedProgramHandleResolution LinkWithoutCaching(
        GL currentContextApi,
        OpenGlProgramKey programKey,
        string vertexGlsl,
        string pixelGlsl,
        Func<uint> submitLink,
        Func<uint, uint> completeLink)
    {
        uint handle = 0;
        try
        {
            _uniqueLinkAttemptCount = checked(
                _uniqueLinkAttemptCount + 1);
            handle = submitLink();
            handle = completeLink(handle);
            if (handle == 0)
            {
                throw new InvalidOperationException(
                    "OpenGL linker returned the reserved zero program handle.");
            }
            _successfulUniqueLinkCount = checked(
                _successfulUniqueLinkCount + 1);
            if (_programBinaries?.TryStore(
                    currentContextApi,
                    programKey,
                    vertexGlsl,
                    pixelGlsl,
                    handle) == true)
            {
                _programBinaryStoreCount = checked(
                    _programBinaryStoreCount + 1);
            }
            return new OpenGlLinkedProgramHandleResolution(
                Handle: handle,
                FailureReason: null,
                IsReuse: false,
                IsProgramBinaryLoad: false,
                CacheOwnsHandle: false,
                IsCacheResident: false,
                IsPending: false);
        }
        catch (InvalidOperationException exception)
        {
            if (handle != 0)
                SafeDeleteProgram(currentContextApi, handle);
            _failedUniqueLinkCount = checked(
                _failedUniqueLinkCount + 1);
            return new OpenGlLinkedProgramHandleResolution(
                Handle: 0,
                FailureReason: exception.Message,
                IsReuse: false,
                IsProgramBinaryLoad: false,
                CacheOwnsHandle: false,
                IsCacheResident: false,
                IsPending: false);
        }
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
            CachedEntryCount: _programs.Count,
            CachedHandleCount: _ownedHandles.Count,
            MaximumEntryCount: _maximumEntryCount,
            DeferredLinkSubmissionCount:
                _deferredLinkSubmissionCount,
            PendingLinkCount:
                _programs.Values.Count(program => program.IsPending) +
                _pendingCapacityBypasses.Count);
    }

    internal void Clear()
    {
        EnsureOwnerThread();
        _programs.Clear();
        _pendingCapacityBypasses.Clear();
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
        _deferredLinkSubmissionCount = 0;
    }

    internal void CompleteProgramBinaryPersistence() =>
        _programBinaries?.Dispose();

    internal void CancelPendingLinks(GL gl, int usageLane)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(gl);
        if (usageLane < 0)
            throw new ArgumentOutOfRangeException(nameof(usageLane));

        OpenGlLinkedProgramHandleCacheKey[] pendingKeys = _programs
            .Where(entry =>
                entry.Key.UsageLane == usageLane &&
                entry.Value.IsPending)
            .Select(entry => entry.Key)
            .ToArray();
        foreach (OpenGlLinkedProgramHandleCacheKey key in pendingKeys)
        {
            CachedProgram pending = _programs[key];
            _programs.Remove(key);
            _ownedHandles.Remove(pending.PendingHandle);
            SafeDeleteProgram(gl, pending.PendingHandle);
        }

        OpenGlLinkedProgramHandleCacheKey[] pendingBypassKeys =
            _pendingCapacityBypasses.Keys
                .Where(key => key.UsageLane == usageLane)
                .ToArray();
        foreach (OpenGlLinkedProgramHandleCacheKey key in pendingBypassKeys)
        {
            CachedProgram pending = _pendingCapacityBypasses[key];
            _pendingCapacityBypasses.Remove(key);
            _ownedHandles.Remove(pending.PendingHandle);
            SafeDeleteProgram(gl, pending.PendingHandle);
        }
    }

    internal void ReleaseUsageLanePrograms(GL gl, int usageLane)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(gl);
        if (usageLane < 0)
            throw new ArgumentOutOfRangeException(nameof(usageLane));

        CancelPendingLinks(gl, usageLane);
        OpenGlLinkedProgramHandleCacheKey[] keys = _programs.Keys
            .Where(key => key.UsageLane == usageLane)
            .ToArray();
        foreach (OpenGlLinkedProgramHandleCacheKey key in keys)
        {
            CachedProgram program = _programs[key];
            _programs.Remove(key);
            uint handle = program.IsPending
                ? program.PendingHandle
                : program.Resolution.Handle;
            if (handle == 0 || !_ownedHandles.Remove(handle))
                continue;
            SafeDeleteProgram(gl, handle);
        }
    }

    private static void SafeDeleteProgram(GL gl, uint handle)
    {
        try
        {
            gl.DeleteProgram(handle);
        }
        catch
        {
            // Context loss remains authoritative for driver-owned cleanup.
        }
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

internal sealed class CachedProgram
{
    internal CachedProgram(
        OpenGlLinkedProgramHandleResolution resolution,
        uint pendingHandle = 0)
    {
        Resolution = resolution;
        PendingHandle = pendingHandle;
    }

    internal OpenGlLinkedProgramHandleResolution Resolution
        { get; private set; }

    internal uint PendingHandle { get; private set; }

    internal bool IsPending => Resolution.IsPending;

    internal void Resolve(
        OpenGlLinkedProgramHandleResolution resolution)
    {
        if (!IsPending || resolution.IsPending)
        {
            throw new InvalidOperationException(
                "Only a pending OpenGL program may publish a terminal resolution.");
        }
        Resolution = resolution;
        PendingHandle = 0;
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
        bool IsCacheResident,
        bool IsPending = false)
{
    internal bool IsReady =>
        !IsPending &&
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
        int MaximumEntryCount,
        long DeferredLinkSubmissionCount,
        int PendingLinkCount);
