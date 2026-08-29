using Silk.NET.OpenGL;

namespace IW4.Render.OpenGl.Programs;

/// <summary>
/// Owns linked OpenGL programs for one context share group. The owner must
/// create, use, and dispose this cache on one render thread, and a context in
/// the share group must be current when it is disposed. Active renderers use
/// separate lanes because OpenGL program uniforms are mutable shared-object
/// state; a released lane is reused by a later sequential renderer.
/// </summary>
public sealed class OpenGlSharedProgramCache : IDisposable
{
    public const string LinkProfileIdentity =
        "silk-opengl-3.3-core";
    public const int DefaultMaximumEntryCount = 2048;
    private const int DefaultMaximumPersistentBinaryEntryCount = 16384;

    private readonly GL _deletionApi;
    private readonly OpenGlLinkedProgramHandleCache _handles;
    private readonly SortedSet<int> _availableUsageLanes = [];
    private readonly HashSet<int> _activeUsageLanes = [];
    private readonly int _ownerThreadId;
    private int _nextUsageLane;
    private bool _disposed;

    public OpenGlSharedProgramCache(
        GL deletionApi,
        int maximumEntryCount = DefaultMaximumEntryCount,
        string? programBinaryCacheDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(deletionApi);
        if (maximumEntryCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumEntryCount));

        if (programBinaryCacheDirectory is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(
                programBinaryCacheDirectory);
        }

        _deletionApi = deletionApi;
        OpenGlProgramBinaryDiskCache? programBinaries =
            programBinaryCacheDirectory is null
                ? null
                : new OpenGlProgramBinaryDiskCache(
                    deletionApi,
                    programBinaryCacheDirectory,
                    DefaultMaximumPersistentBinaryEntryCount);
        _handles = new OpenGlLinkedProgramHandleCache(
            LinkProfileIdentity,
            maximumEntryCount,
            programBinaries);
        _ownerThreadId = Environment.CurrentManagedThreadId;
    }

    public int CachedEntryCount =>
        CreateTelemetry().CachedEntryCount;

    public int CachedProgramCount =>
        CreateTelemetry().CachedHandleCount;

    public int MaximumEntryCount =>
        CreateTelemetry().MaximumEntryCount;

    public long LinkRequestCount =>
        CreateTelemetry().SemanticRequestCount;

    public long UniqueLinkAttemptCount =>
        CreateTelemetry().UniqueLinkAttemptCount;

    public long SuccessfulLinkCount =>
        CreateTelemetry().SuccessfulUniqueLinkCount;

    public long LinkReuseCount =>
        CreateTelemetry().LinkReuseCount;

    public long FailedLinkCount =>
        CreateTelemetry().FailedUniqueLinkCount;

    public long CapacityBypassCount =>
        CreateTelemetry().CapacityBypassCount;

    public bool ProgramBinaryPersistenceEnabled =>
        CreateTelemetry().ProgramBinaryPersistenceEnabled;

    public long ProgramBinaryLoadAttemptCount =>
        CreateTelemetry().ProgramBinaryLoadAttemptCount;

    public long ProgramBinaryLoadHitCount =>
        CreateTelemetry().ProgramBinaryLoadHitCount;

    public long ProgramBinaryStoreCount =>
        CreateTelemetry().ProgramBinaryStoreCount;

    public int ActiveUsageLaneCount
    {
        get
        {
            EnsureUsableOnOwnerThread();
            return _activeUsageLanes.Count;
        }
    }

    internal UsageLease AcquireUsageLease(GL currentContextApi)
    {
        EnsureUsableOnOwnerThread();
        ArgumentNullException.ThrowIfNull(currentContextApi);
        int usageLane;
        if (_availableUsageLanes.Count != 0)
        {
            usageLane = _availableUsageLanes.Min;
            _availableUsageLanes.Remove(usageLane);
        }
        else
        {
            usageLane = _nextUsageLane;
            _nextUsageLane = checked(_nextUsageLane + 1);
        }

        if (!_activeUsageLanes.Add(usageLane))
        {
            throw new InvalidOperationException(
                $"OpenGL program-cache usage lane {usageLane} is already active.");
        }
        return new UsageLease(
            this,
            currentContextApi,
            usageLane);
    }

    private OpenGlLinkedProgramHandleResolution GetOrLink(
        int usageLane,
        GL currentContextApi,
        string vertexGlsl,
        string pixelGlsl,
        Func<uint> link)
    {
        EnsureUsableOnOwnerThread();
        if (!_activeUsageLanes.Contains(usageLane))
        {
            throw new InvalidOperationException(
                $"OpenGL program-cache usage lane {usageLane} is not active.");
        }
        return _handles.GetOrLink(
            currentContextApi,
            vertexGlsl,
            pixelGlsl,
            link,
            usageLane);
    }

    private OpenGlLinkedProgramHandleResolution GetOrLinkDeferred(
        int usageLane,
        GL currentContextApi,
        string vertexGlsl,
        string pixelGlsl,
        Func<uint> submitLink,
        Func<uint, uint> completeLink,
        bool deferNewLinkCompletion)
    {
        EnsureUsableOnOwnerThread();
        if (!_activeUsageLanes.Contains(usageLane))
        {
            throw new InvalidOperationException(
                $"OpenGL program-cache usage lane {usageLane} is not active.");
        }
        return _handles.GetOrLinkDeferred(
            currentContextApi,
            vertexGlsl,
            pixelGlsl,
            submitLink,
            completeLink,
            deferNewLinkCompletion,
            usageLane);
    }

    private void ReleaseUsageLanePrograms(
        int usageLane,
        GL currentContextApi)
    {
        EnsureUsableOnOwnerThread();
        ArgumentNullException.ThrowIfNull(currentContextApi);
        if (!_activeUsageLanes.Contains(usageLane))
        {
            throw new InvalidOperationException(
                $"OpenGL program-cache usage lane {usageLane} is not active.");
        }
        _handles.ReleaseUsageLanePrograms(
            currentContextApi,
            usageLane);
    }

    private void CancelPendingLinks(
        int usageLane,
        GL currentContextApi)
    {
        EnsureUsableOnOwnerThread();
        ArgumentNullException.ThrowIfNull(currentContextApi);
        if (!_activeUsageLanes.Contains(usageLane))
        {
            throw new InvalidOperationException(
                $"OpenGL program-cache usage lane {usageLane} is not active.");
        }
        _handles.CancelPendingLinks(currentContextApi, usageLane);
    }

    internal OpenGlLinkedProgramHandleCacheTelemetry
        CreateTelemetry()
    {
        EnsureUsableOnOwnerThread();
        return _handles.CreateTelemetry();
    }

    public void Dispose()
    {
        EnsureOwnerThread();
        if (_disposed)
            return;
        if (_activeUsageLanes.Count != 0)
        {
            throw new InvalidOperationException(
                "The OpenGL share-group program cache cannot be disposed while renderer usage lanes are active.");
        }

        _disposed = true;
        List<Exception>? failures = null;
        try
        {
            foreach (uint handle in _handles.OwnedHandles)
            {
                try
                {
                    _deletionApi.DeleteProgram(handle);
                }
                catch (Exception exception)
                {
                    (failures ??= []).Add(exception);
                }
            }
        }
        finally
        {
            _handles.Clear();
            _handles.CompleteProgramBinaryPersistence();
            _availableUsageLanes.Clear();
            _activeUsageLanes.Clear();
        }

        if (failures is not null)
        {
            throw new AggregateException(
                "One or more share-group OpenGL programs could not be deleted.",
                failures);
        }
    }

    /// <summary>
    /// Relinquishes managed handle ownership after the entire owning context
    /// has been lost. The driver owns reclamation; this method deliberately
    /// performs no OpenGL deletion.
    /// </summary>
    internal void AbandonContext()
    {
        EnsureOwnerThread();
        if (_disposed)
            return;
        if (_activeUsageLanes.Count != 0)
        {
            throw new InvalidOperationException(
                "The OpenGL program cache cannot abandon a context while renderer usage lanes are active.");
        }

        _disposed = true;
        _handles.Clear();
        _handles.CompleteProgramBinaryPersistence();
        _availableUsageLanes.Clear();
        _activeUsageLanes.Clear();
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
                "The OpenGL share-group program cache may only be used and disposed on its owning render thread.");
        }
    }

    private void ReleaseUsageLane(
        GL currentContextApi,
        int usageLane)
    {
        EnsureUsableOnOwnerThread();
        ArgumentNullException.ThrowIfNull(currentContextApi);
        if (!_activeUsageLanes.Remove(usageLane))
        {
            throw new InvalidOperationException(
                $"OpenGL program-cache usage lane {usageLane} was released twice.");
        }
        // An interrupted map load may leave submitted links that have not yet
        // reached LinkStatus. They cannot be inherited by a later renderer,
        // whose synchronous callback does not own their completion contract.
        _handles.CancelPendingLinks(currentContextApi, usageLane);
        _availableUsageLanes.Add(usageLane);
    }

    internal sealed class UsageLease : IDisposable
    {
        private OpenGlSharedProgramCache? _owner;
        private readonly GL _currentContextApi;

        internal UsageLease(
            OpenGlSharedProgramCache owner,
            GL currentContextApi,
            int usageLane)
        {
            _owner = owner;
            _currentContextApi = currentContextApi;
            UsageLane = usageLane;
        }

        internal int UsageLane { get; }

        internal bool ProgramBinaryPersistenceEnabled
        {
            get
            {
                OpenGlSharedProgramCache owner =
                    _owner ?? throw new ObjectDisposedException(
                        nameof(UsageLease));
                return owner.ProgramBinaryPersistenceEnabled;
            }
        }

        internal OpenGlLinkedProgramHandleResolution GetOrLink(
            string vertexGlsl,
            string pixelGlsl,
            Func<uint> link)
        {
            OpenGlSharedProgramCache owner =
                _owner ?? throw new ObjectDisposedException(
                    nameof(UsageLease));
            return owner.GetOrLink(
                UsageLane,
                _currentContextApi,
                vertexGlsl,
                pixelGlsl,
                link);
        }

        internal OpenGlLinkedProgramHandleResolution GetOrLinkDeferred(
            string vertexGlsl,
            string pixelGlsl,
            Func<uint> submitLink,
            Func<uint, uint> completeLink,
            bool deferNewLinkCompletion)
        {
            OpenGlSharedProgramCache owner =
                _owner ?? throw new ObjectDisposedException(
                    nameof(UsageLease));
            return owner.GetOrLinkDeferred(
                UsageLane,
                _currentContextApi,
                vertexGlsl,
                pixelGlsl,
                submitLink,
                completeLink,
                deferNewLinkCompletion);
        }

        /// <summary>
        /// Releases this renderer's previous scene programs while preserving
        /// the independently bounded persistent binary cache. A map renderer
        /// invokes this only after deleting every resource that can reference
        /// the old handles, preventing sixteen sequential maps from pinning
        /// the resident handle cache at its ceiling and turning every later
        /// program into a renderer-owned capacity bypass.
        /// </summary>
        internal void ReleaseScenePrograms()
        {
            OpenGlSharedProgramCache owner =
                _owner ?? throw new ObjectDisposedException(
                    nameof(UsageLease));
            owner.ReleaseUsageLanePrograms(
                UsageLane,
                _currentContextApi);
        }

        internal void CancelPendingLinks()
        {
            OpenGlSharedProgramCache owner =
                _owner ?? throw new ObjectDisposedException(
                    nameof(UsageLease));
            owner.CancelPendingLinks(
                UsageLane,
                _currentContextApi);
        }

        public void Dispose()
        {
            OpenGlSharedProgramCache? owner =
                Interlocked.Exchange(ref _owner, null);
            owner?.ReleaseUsageLane(_currentContextApi, UsageLane);
        }
    }
}
