using IW4.Render.Diagnostics;

namespace IW4.Render.OpenGl.Diagnostics;

/// <summary>
/// Allocation-free scope for one coarse GL_TIME_ELAPSED interval. A default
/// scope means that this frame is unsampled, assigned to whole-frame sampling,
/// or that the phase ring was full; disposing it is a no-op.
/// </summary>
public readonly struct MapRenderOpenGlGpuPhaseScope : IDisposable
{
    private readonly MapRenderOpenGlGpuTimerCoordinator? _owner;
    private readonly long _token;

    internal MapRenderOpenGlGpuPhaseScope(
        MapRenderOpenGlGpuTimerCoordinator owner,
        long token)
    {
        _owner = owner;
        _token = token;
    }

    public bool IsTiming => _owner is not null;

    public void Dispose() => _owner?.EndPhase(_token);
}

/// <summary>
/// Coordinates a whole-frame query ring with one delayed query ring per coarse
/// GPU phase. One 32-frame cycle interleaves one whole-frame query and one
/// query for each of the 15 phases with 16 query-free frames. Phase samples
/// rotate across frames so GL_TIME_ELAPSED queries never nest or form a burst.
/// </summary>
public sealed class MapRenderOpenGlGpuTimerCoordinator : IDisposable
{
    private readonly MapRenderOpenGlTimeElapsedQueryRing _wholeFrameRing;
    private readonly MapRenderOpenGlTimeElapsedQueryRing[] _phaseRings;
    private readonly bool[] _phaseObservedThisFrame;
    private readonly int _samplingCycleFrameCount;
    private bool _frameActive;
    private bool _wholeFrameSampling;
    private bool _phaseAttributionSampling;
    private int _scheduledPhaseIndex = -1;
    private bool _wholeFrameQueryStarted;
    private bool _phaseQueryActive;
    private int _activePhaseIndex = -1;
    private long _activePhaseToken;
    private long _nextPhaseToken;
    private long _frameIndex;
    private long _lastObservedFrameIndex = -1;
    private int _collectPhaseIndex;
    private bool _disposed;

    public MapRenderOpenGlGpuTimerCoordinator(
        IMapRenderOpenGlTimeElapsedQueryApi api,
        int capacity = MapRenderOpenGlTimeElapsedQueryRing.DefaultCapacity,
        int minimumFrameDelay =
            MapRenderOpenGlTimeElapsedQueryRing.DefaultMinimumFrameDelay)
    {
        ArgumentNullException.ThrowIfNull(api);
        int phaseCount = Enum.GetValues<MapRenderGpuPhase>().Length;
        _samplingCycleFrameCount = checked((phaseCount + 1) * 2);
        _phaseRings = new MapRenderOpenGlTimeElapsedQueryRing[phaseCount];
        _phaseObservedThisFrame = new bool[phaseCount];

        _wholeFrameRing = new MapRenderOpenGlTimeElapsedQueryRing(
            api,
            capacity,
            minimumFrameDelay);
        int createdPhaseCount = 0;
        try
        {
            for (; createdPhaseCount < phaseCount; createdPhaseCount++)
            {
                _phaseRings[createdPhaseCount] =
                    new MapRenderOpenGlTimeElapsedQueryRing(
                        api,
                        capacity,
                        minimumFrameDelay);
            }
        }
        catch
        {
            for (int i = 0; i < createdPhaseCount; i++)
                _phaseRings[i].Dispose();
            _wholeFrameRing.Dispose();
            throw;
        }
    }

    public bool IsFrameActive => _frameActive;

    public bool IsWholeFrameSampling =>
        _frameActive && _wholeFrameSampling;

    public void BeginFrame(long frameIndex, bool enablePhaseAttribution)
    {
        ThrowIfDisposed();
        if (_frameActive)
        {
            throw new InvalidOperationException(
                "A GPU telemetry frame is already active.");
        }

        if (frameIndex < 0 || frameIndex <= _lastObservedFrameIndex)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frameIndex),
                frameIndex,
                "GPU timing frame indexes must be non-negative and increasing.");
        }

        _lastObservedFrameIndex = frameIndex;
        Array.Clear(_phaseObservedThisFrame);
        _frameIndex = frameIndex;
        int cycleFrame = checked(
            (int)(frameIndex % _samplingCycleFrameCount));
        bool queryFrame = (cycleFrame & 1) == 0;
        int querySlot = cycleFrame / 2;
        _wholeFrameSampling = queryFrame && querySlot == 0;
        _scheduledPhaseIndex = queryFrame &&
            querySlot > 0 &&
            enablePhaseAttribution
                ? querySlot - 1
                : -1;
        _phaseAttributionSampling = _scheduledPhaseIndex >= 0;
        _wholeFrameQueryStarted = false;
        _frameActive = true;

        if (_wholeFrameSampling)
        {
            _wholeFrameQueryStarted =
                _wholeFrameRing.TryBeginFrame(frameIndex);
        }
    }

    public MapRenderOpenGlGpuPhaseScope BeginPhase(MapRenderGpuPhase phase)
    {
        ThrowIfDisposed();
        if (!_frameActive)
        {
            throw new InvalidOperationException(
                "BeginFrame must be called before a GPU phase.");
        }

        int phaseIndex = (int)phase;
        if ((uint)phaseIndex >= (uint)_phaseRings.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(phase), phase, null);
        }

        if (!_phaseAttributionSampling)
            return default;

        if (_phaseQueryActive)
        {
            throw new InvalidOperationException(
                $"GPU phase {(MapRenderGpuPhase)_activePhaseIndex} is already active.");
        }

        if (_phaseObservedThisFrame[phaseIndex])
        {
            throw new InvalidOperationException(
                $"GPU phase {phase} was already observed in frame {_frameIndex}. " +
                "Only one contiguous interval can be attributed per phase.");
        }

        _phaseObservedThisFrame[phaseIndex] = true;
        if (phaseIndex != _scheduledPhaseIndex)
            return default;

        if (!_phaseRings[phaseIndex].TryBeginFrame(_frameIndex))
            return default;

        _phaseQueryActive = true;
        _activePhaseIndex = phaseIndex;
        _activePhaseToken = ++_nextPhaseToken;
        return new MapRenderOpenGlGpuPhaseScope(this, _activePhaseToken);
    }

    public void EndFrame()
    {
        ThrowIfDisposed();
        if (!_frameActive)
        {
            throw new InvalidOperationException(
                "No GPU telemetry frame is active.");
        }

        // Balance a phase if render execution exited exceptionally. Normal
        // execution ends it through the allocation-free phase scope.
        if (_phaseQueryActive)
            EndActivePhase();

        if (_wholeFrameQueryStarted && _wholeFrameRing.IsQueryActive)
            _wholeFrameRing.EndFrame();

        _wholeFrameQueryStarted = false;
        _wholeFrameSampling = false;
        _phaseAttributionSampling = false;
        _scheduledPhaseIndex = -1;
        _frameActive = false;
    }

    public bool TryCollectCompletedFrame(
        out MapRenderOpenGlGpuFrameTiming timing)
    {
        ThrowIfDisposed();
        return _wholeFrameRing.TryCollectCompleted(out timing);
    }

    /// <summary>
    /// Scans each independent phase ring once and returns the first available
    /// delayed result. It never requests a result for an unavailable query.
    /// </summary>
    public bool TryCollectCompletedPhase(
        out MapRenderOpenGlGpuPhaseTiming timing)
    {
        ThrowIfDisposed();
        for (int checkedCount = 0;
             checkedCount < _phaseRings.Length;
             checkedCount++)
        {
            int phaseIndex = _collectPhaseIndex;
            _collectPhaseIndex =
                (_collectPhaseIndex + 1) % _phaseRings.Length;
            if (!_phaseRings[phaseIndex].TryCollectCompleted(
                    out MapRenderOpenGlGpuFrameTiming frameTiming))
            {
                continue;
            }

            timing = new MapRenderOpenGlGpuPhaseTiming(
                (MapRenderGpuPhase)phaseIndex,
                frameTiming.FrameIndex,
                frameTiming.ElapsedNanoseconds,
                frameTiming.ReadbackDelayFrames);
            return true;
        }

        timing = default;
        return false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            if (_frameActive)
                EndFrame();
        }
        finally
        {
            foreach (MapRenderOpenGlTimeElapsedQueryRing ring in _phaseRings)
                ring.Dispose();
            _wholeFrameRing.Dispose();
            _disposed = true;
        }
    }

    /// <summary>
    /// Drops timer-query bookkeeping after context loss without issuing any
    /// OpenGL calls.
    /// </summary>
    public void AbandonContext()
    {
        if (_disposed)
            return;

        foreach (MapRenderOpenGlTimeElapsedQueryRing ring in _phaseRings)
            ring.AbandonContext();
        _wholeFrameRing.AbandonContext();
        _frameActive = false;
        _wholeFrameSampling = false;
        _phaseAttributionSampling = false;
        _scheduledPhaseIndex = -1;
        _wholeFrameQueryStarted = false;
        _phaseQueryActive = false;
        _activePhaseIndex = -1;
        _disposed = true;
    }

    internal void EndPhase(long token)
    {
        if (!_phaseQueryActive || token != _activePhaseToken)
            return;

        EndActivePhase();
    }

    private void EndActivePhase()
    {
        _phaseRings[_activePhaseIndex].EndFrame();
        _phaseQueryActive = false;
        _activePhaseIndex = -1;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
