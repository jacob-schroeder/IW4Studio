using IW4.Render.Diagnostics;

namespace IW4.Render.OpenGl.Diagnostics;

/// <summary>
/// A delayed GL_TIME_ELAPSED query ring. Result availability is always checked
/// before reading a result, so a busy GPU never turns telemetry into a CPU/GPU
/// synchronization point. If every slot remains pending, new measurements are
/// skipped until a query completes.
/// </summary>
public sealed class MapRenderOpenGlTimeElapsedQueryRing : IDisposable
{
    public const int DefaultCapacity = 8;
    public const int DefaultMinimumFrameDelay = 3;

    private readonly IMapRenderOpenGlTimeElapsedQueryApi _api;
    private readonly QuerySlot[] _slots;
    private readonly int _minimumFrameDelay;
    private int _readIndex;
    private int _writeIndex;
    private int _pendingQueryCount;
    private bool _queryActive;
    private int _activeSlotIndex = -1;
    private long _lastObservedFrameIndex = -1;
    private bool _disposed;

    public MapRenderOpenGlTimeElapsedQueryRing(
        IMapRenderOpenGlTimeElapsedQueryApi api,
        int capacity = DefaultCapacity,
        int minimumFrameDelay = DefaultMinimumFrameDelay)
    {
        ArgumentNullException.ThrowIfNull(api);
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                capacity,
                "Query-ring capacity must be positive.");
        }

        if (minimumFrameDelay < 0 || minimumFrameDelay >= capacity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumFrameDelay),
                minimumFrameDelay,
                "Minimum frame delay must be non-negative and smaller than capacity.");
        }

        _api = api;
        _minimumFrameDelay = minimumFrameDelay;
        _slots = new QuerySlot[capacity];

        int createdCount = 0;
        try
        {
            for (; createdCount < _slots.Length; createdCount++)
            {
                uint query = _api.CreateQuery();
                if (query == 0)
                {
                    throw new InvalidOperationException(
                        "OpenGL returned query object zero.");
                }

                _slots[createdCount].Query = query;
            }
        }
        catch
        {
            for (int i = 0; i < createdCount; i++)
                _api.DeleteQuery(_slots[i].Query);
            throw;
        }
    }

    public int Capacity => _slots.Length;

    public int MinimumFrameDelay => _minimumFrameDelay;

    public int PendingQueryCount => _pendingQueryCount;

    public long SkippedMeasurementCount { get; private set; }

    public bool IsQueryActive => _queryActive;

    /// <summary>
    /// Begins timing a frame when a ring slot is free. A false return means the
    /// GPU has not retired enough older queries and this sample was skipped.
    /// Frame indexes must increase even across skipped samples.
    /// </summary>
    public bool TryBeginFrame(long frameIndex)
    {
        ThrowIfDisposed();
        if (_queryActive)
        {
            throw new InvalidOperationException(
                "A GL_TIME_ELAPSED query is already active.");
        }

        if (frameIndex < 0 || frameIndex <= _lastObservedFrameIndex)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frameIndex),
                frameIndex,
                "GPU timing frame indexes must be non-negative and increasing.");
        }

        _lastObservedFrameIndex = frameIndex;
        if (_pendingQueryCount == _slots.Length)
        {
            SkippedMeasurementCount++;
            return false;
        }

        ref QuerySlot slot = ref _slots[_writeIndex];
        if (slot.IsPending)
        {
            throw new InvalidOperationException(
                "The timer-query ring write slot is unexpectedly pending.");
        }

        _api.BeginTimeElapsedQuery(slot.Query);
        slot.FrameIndex = frameIndex;
        _queryActive = true;
        _activeSlotIndex = _writeIndex;
        return true;
    }

    public void EndFrame()
    {
        ThrowIfDisposed();
        if (!_queryActive)
        {
            throw new InvalidOperationException(
                "No GL_TIME_ELAPSED query is active.");
        }

        _api.EndTimeElapsedQuery();
        ref QuerySlot slot = ref _slots[_activeSlotIndex];
        slot.IsPending = true;
        _pendingQueryCount++;
        _writeIndex = (_writeIndex + 1) % _slots.Length;
        _activeSlotIndex = -1;
        _queryActive = false;
    }

    /// <summary>
    /// Reads the oldest completed sample, if one is both sufficiently delayed
    /// and reported available by OpenGL. This method never requests
    /// GL_QUERY_RESULT for an unavailable query.
    /// </summary>
    public bool TryCollectCompleted(out MapRenderOpenGlGpuFrameTiming timing)
    {
        ThrowIfDisposed();
        timing = default;
        if (_pendingQueryCount == 0)
            return false;

        ref QuerySlot slot = ref _slots[_readIndex];
        if (!slot.IsPending)
        {
            throw new InvalidOperationException(
                "The timer-query ring read slot is unexpectedly free.");
        }

        long delay = _lastObservedFrameIndex - slot.FrameIndex;
        if (delay < _minimumFrameDelay)
            return false;

        if (!_api.IsQueryResultAvailable(slot.Query))
            return false;

        ulong elapsedNanoseconds =
            _api.GetQueryResultNanoseconds(slot.Query);
        timing = new MapRenderOpenGlGpuFrameTiming(
            slot.FrameIndex,
            elapsedNanoseconds,
            checked((int)Math.Min(delay, int.MaxValue)));
        slot.IsPending = false;
        slot.FrameIndex = 0;
        _pendingQueryCount--;
        _readIndex = (_readIndex + 1) % _slots.Length;
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            // Keep the context's query state balanced even if disposal occurs
            // after a frame exits exceptionally.
            if (_queryActive)
                _api.EndTimeElapsedQuery();
        }
        finally
        {
            foreach (QuerySlot slot in _slots)
                _api.DeleteQuery(slot.Query);

            _queryActive = false;
            _activeSlotIndex = -1;
            _pendingQueryCount = 0;
            _disposed = true;
        }
    }

    /// <summary>
    /// Releases managed query bookkeeping after the owning OpenGL context has
    /// been lost. No driver call is legal on this path.
    /// </summary>
    public void AbandonContext()
    {
        if (_disposed)
            return;

        Array.Clear(_slots);
        _queryActive = false;
        _activeSlotIndex = -1;
        _pendingQueryCount = 0;
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private struct QuerySlot
    {
        public uint Query;
        public long FrameIndex;
        public bool IsPending;
    }
}
