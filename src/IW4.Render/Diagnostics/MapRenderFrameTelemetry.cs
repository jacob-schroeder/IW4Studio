using System.Diagnostics;

namespace IW4.Render.Diagnostics;

/// <summary>
/// Coarse CPU phases that are useful when separating scene work, OpenGL
/// submission, and presentation. A phase may be entered more than once during
/// a frame; its elapsed time is accumulated into one per-frame sample.
/// </summary>
public enum MapRenderCpuPhase
{
    StaticResourceAdmission,
    FrameSetup,
    SunShadow,
    SceneTarget,
    Visibility,
    QueueBuild,
    Sky,
    DepthPrepass,
    ProcessedFloatZ,
    WorldGeometry,
    StaticModels,
    EditorOverlay,
    Presentation,
    SwapOrPresent
}

/// <summary>
/// Coarse, non-overlapping GPU command intervals used by the editor preview.
/// The whole-frame timer and these attribution timers are sampled on separate
/// frames because OpenGL does not permit nested GL_TIME_ELAPSED queries.
/// </summary>
public enum MapRenderGpuPhase
{
    SunShadow,
    SceneTarget,
    FrameSetup,
    Sky,
    Diagnostics,
    DepthPrepass,
    WorldOpaque,
    WorldCutout,
    StaticOpaque,
    StaticCutout,
    ProcessedFloatZ,
    Translucent,
    Wireframe,
    EditorOverlay,
    Presentation
}

public readonly record struct MapRenderOpenGlGpuFrameTiming(
    long FrameIndex,
    ulong ElapsedNanoseconds,
    int ReadbackDelayFrames)
{
    public double ElapsedMilliseconds => ElapsedNanoseconds / 1_000_000.0;
}

public readonly record struct MapRenderOpenGlGpuPhaseTiming(
    MapRenderGpuPhase Phase,
    long FrameIndex,
    ulong ElapsedNanoseconds,
    int ReadbackDelayFrames)
{
    public double ElapsedMilliseconds => ElapsedNanoseconds / 1_000_000.0;
}

/// <summary>
/// Per-frame work and state-change counters. These distinguish useful draws
/// from managed-to-OpenGL submission traffic without tracing individual calls.
/// </summary>
public enum MapRenderFrameCounter
{
    SceneTargetWidth,
    SceneTargetHeight,
    HostFramebufferWidth,
    HostFramebufferHeight,
    WorldCandidates,
    WorldVisible,
    WorldVisibleRuns,
    WorldCandidateTriangles,
    WorldVisibleTriangles,
    StaticModelCandidates,
    StaticModelsVisible,
    DrawCalls,
    LogicalDrawCommands,
    MultiDrawApiCalls,
    MultiDrawCommands,
    StaticExecutionBundleBinds,
    StaticExecutionBundleReuses,
    Triangles,
    OpenGlCalls,
    StateShadowElidedCalls,
    ProgramChanges,
    VertexArrayChanges,
    FramebufferChanges,
    BufferChanges,
    TextureChanges,
    SamplerChanges,
    RenderStateChanges,
    UniformUpdates,
    ShaderProgramCompilations,

    /// <summary>
    /// Full-resolution scene textures resident after this frame.
    /// Stable one-pixel fallback objects are excluded.
    /// </summary>
    TextureResidentCount,

    /// <summary>
    /// Estimated full-resolution scene texture storage resident after this
    /// frame. Compressed uploads report authored BC bytes.
    /// </summary>
    TextureResidentBytes,

    TextureResidencyUploadCount,
    TextureResidencyUploadBytes,
    TextureResidencyEvictionCount,
    TextureResidencyEvictionBytes,

    /// <summary>
    /// Visible nonresident textures deferred by the residency or per-frame
    /// upload budget.
    /// </summary>
    TextureResidencyDeferredCount,

    /// <summary>
    /// Authored BC payload bytes uploaded without RGBA expansion this frame.
    /// </summary>
    TextureAuthoredBcUploadBytes,

    /// <summary>
    /// Unique decoded compatibility payload bytes still strongly retained by
    /// scene textures after direct authored-BC ownership is resolved.
    /// </summary>
    TextureDecodedFallbackRetainedBytes,

    /// <summary>
    /// Unique authored BC payload bytes retained by direct-upload textures.
    /// </summary>
    TextureAuthoredBcSourceBytes,

    /// <summary>
    /// Changed static-instance payloads submitted to a rotating OpenGL
    /// upload buffer during this frame.
    /// </summary>
    StaticInstanceUploadCalls,

    /// <summary>
    /// Total changed static-instance payload bytes submitted this frame.
    /// </summary>
    StaticInstanceUploadBytes,

    /// <summary>
    /// Static-instance runtimes which advanced to a new ring slot this frame.
    /// Multiple updates to one runtime in the same frame retain one slot.
    /// </summary>
    StaticInstanceUploadRingAdvances,

    /// <summary>
    /// Renderable static-object identities compared with retained
    /// visibility and LOD state during this frame.
    /// </summary>
    StaticInstanceChangeCandidates,

    /// <summary>
    /// Static-object identities changed by visibility, LOD, exact receiver
    /// ownership, or model-lighting assignment during this frame.
    /// </summary>
    StaticInstanceChangedObjects,

    /// <summary>
    /// Static-instance runtimes whose source rows were rescanned this frame.
    /// </summary>
    StaticInstanceRuntimesRescanned,

    /// <summary>
    /// Static-instance source rows covered by runtime rescans this frame.
    /// </summary>
    StaticInstanceRowsRescanned,

    /// <summary>
    /// Physical model-lighting atlas entries retained by the native-shaped
    /// static-object working set after this frame's admission.
    /// </summary>
    StaticModelLightingResidentEntries,

    /// <summary>
    /// Visible static objects hidden because no model-lighting entry was
    /// available and no native-age-eligible entry could be recycled.
    /// </summary>
    StaticModelLightingAllocationMisses,

    /// <summary>
    /// Previously unused model-lighting entries assigned this frame.
    /// </summary>
    StaticModelLightingNewAssignments,

    /// <summary>
    /// Native-age-eligible model-lighting entries reassigned this frame.
    /// </summary>
    StaticModelLightingRecycledAssignments,

    /// <summary>
    /// Exact CPU frame-preparation jobs executed by persistent renderer
    /// workers. A stationary-frame reuse does not contribute a job.
    /// </summary>
    CpuWorkerJobs,

    /// <summary>
    /// Exact prepared-frame results reused without waking a worker.
    /// </summary>
    CpuWorkerCacheHits,

    /// <summary>
    /// Render-thread wait at the CPU preparation handoff, in microseconds.
    /// Work overlapped with earlier frame stages is deliberately excluded.
    /// </summary>
    CpuWorkerWaitMicroseconds,

    /// <summary>
    /// Managed bytes allocated by the render thread between BeginCpuFrame and
    /// EndCpuFrame. This is a thread-local allocation counter, so concurrent
    /// worker allocations are reported separately.
    /// </summary>
    RenderThreadAllocatedBytes,

    /// <summary>
    /// Managed bytes allocated while producing the newly presented persistent
    /// worker packet. Reusing a stationary packet reports zero.
    /// </summary>
    CpuWorkerAllocatedBytes,

    /// <summary>
    /// Render passes which reached an actual backend execution point. The
    /// scene color pass, a non-empty depth prepass, each completed sun-shadow
    /// partition, and each completed fullscreen post pass contribute once.
    /// </summary>
    Passes,

    /// <summary>Completed sun-shadow atlas partition passes.</summary>
    SunShadowPasses,

    /// <summary>
    /// Logical sun-shadow draw commands. One multi-draw contributes its exact
    /// command count, while DrawCalls continues to count the single GL call.
    /// </summary>
    SunShadowLogicalDrawCommands,

    /// <summary>Completed fullscreen post-processing passes.</summary>
    PostPasses,

    /// <summary>Logical fullscreen post-processing draw commands.</summary>
    PostLogicalDrawCommands
}

/// <summary>
/// Monotonic timestamp source used by <see cref="MapRenderFrameTelemetry"/>.
/// The interface keeps the production hot path on Stopwatch while allowing
/// deterministic timing tests.
/// </summary>
public interface IMapRenderTelemetryClock
{
    long Frequency { get; }

    long GetTimestamp();
}

public sealed class MapRenderStopwatchTelemetryClock : IMapRenderTelemetryClock
{
    public static MapRenderStopwatchTelemetryClock Instance { get; } = new();

    public long Frequency => Stopwatch.Frequency;

    private MapRenderStopwatchTelemetryClock()
    {
    }

    public long GetTimestamp() => Stopwatch.GetTimestamp();
}

public readonly record struct MapRenderMetricSnapshot(
    int SampleCount,
    double Latest,
    double Average,
    double Minimum,
    double Maximum,
    double P50,
    double P95,
    double P99)
{
    public bool HasSamples => SampleCount > 0;

    public static MapRenderMetricSnapshot Empty { get; } = new(
        SampleCount: 0,
        Latest: 0,
        Average: 0,
        Minimum: 0,
        Maximum: 0,
        P50: 0,
        P95: 0,
        P99: 0);
}

public sealed record MapRenderCpuPhaseTelemetrySnapshot(
    MapRenderCpuPhase Phase,
    MapRenderMetricSnapshot Milliseconds);

public sealed record MapRenderGpuPhaseTelemetrySnapshot(
    MapRenderGpuPhase Phase,
    MapRenderMetricSnapshot Milliseconds,
    long? LastFrameIndex,
    int LastReadbackDelayFrames,
    long LatestDrawCalls,
    long LatestTriangles);

public sealed record MapRenderFrameCounterTelemetrySnapshot(
    MapRenderFrameCounter Counter,
    long Latest,
    double RollingAverage,
    long RollingMaximum);

public sealed record MapRenderFrameTelemetrySnapshot(
    long CpuFrameCount,
    long PresentedFrameCount,
    double PresentedFramesPerSecond,
    MapRenderMetricSnapshot PresentedFrameMilliseconds,
    MapRenderMetricSnapshot CpuFrameMilliseconds,
    MapRenderMetricSnapshot GpuFrameMilliseconds,
    long? LastPresentedFrameIndex,
    long? LastGpuFrameIndex,
    int LastGpuReadbackDelayFrames,
    IReadOnlyList<MapRenderCpuPhaseTelemetrySnapshot> CpuPhases,
    IReadOnlyList<MapRenderGpuPhaseTelemetrySnapshot> GpuPhases,
    IReadOnlyList<MapRenderFrameCounterTelemetrySnapshot> Counters);

public readonly record struct MapRenderCpuFrameTiming(
    long FrameIndex,
    double ElapsedMilliseconds);

/// <summary>
/// Allocation-free scope returned by <see cref="MapRenderFrameTelemetry.BeginCpuPhase"/>.
/// CPU phases are intentionally sequential; overlapping phase scopes would
/// make their sum misleading and are rejected.
/// </summary>
public readonly struct MapRenderCpuPhaseScope : IDisposable
{
    private readonly MapRenderFrameTelemetry? _owner;
    private readonly long _token;

    internal MapRenderCpuPhaseScope(
        MapRenderFrameTelemetry owner,
        long token)
    {
        _owner = owner;
        _token = token;
    }

    public void Dispose() => _owner?.EndCpuPhase(_token);
}

/// <summary>
/// Rolling render telemetry. Frame recording is allocation-free; allocations
/// and percentile sorting occur only when <see cref="CreateSnapshot"/> is
/// requested. This type is intended for one render thread and is not thread
/// safe.
/// </summary>
public sealed class MapRenderFrameTelemetry
{
    public const int DefaultRollingFrameCapacity = 240;

    private readonly IMapRenderTelemetryClock _clock;
    private readonly RollingMetric _presentedFrameMilliseconds;
    private readonly RollingMetric _cpuFrameMilliseconds;
    private readonly RollingMetric _gpuFrameMilliseconds;
    private readonly RollingMetric[] _cpuPhaseMilliseconds;
    private readonly RollingMetric[] _gpuPhaseMilliseconds;
    private readonly RollingMetric[] _counterValues;
    private readonly long[] _currentCpuPhaseTicks;
    private readonly long[] _currentCounterValues;
    private readonly long[] _lastCounterValues;
    private readonly long[] _currentGpuPhaseDrawCalls;
    private readonly long[] _currentGpuPhaseTriangles;
    private readonly long[] _lastGpuPhaseDrawCalls;
    private readonly long[] _lastGpuPhaseTriangles;
    private long _nextFrameIndex;
    private long _cpuFrameCount;
    private long _presentedFrameCount;
    private bool _cpuFrameActive;
    private long _activeCpuFrameIndex;
    private long _cpuFrameStartTimestamp;
    private long _cpuFrameStartAllocatedBytes;
    private bool _cpuPhaseActive;
    private MapRenderCpuPhase _activeCpuPhase;
    private long _cpuPhaseStartTimestamp;
    private long _activeCpuPhaseToken;
    private long _nextCpuPhaseToken;
    private long? _lastPresentedFrameIndex;
    private long _lastPresentedTimestamp;
    private long? _lastGpuFrameIndex;
    private int _lastGpuReadbackDelayFrames;
    private readonly long?[] _lastGpuPhaseFrameIndexes;
    private readonly int[] _lastGpuPhaseReadbackDelayFrames;

    public MapRenderFrameTelemetry(
        int rollingFrameCapacity = DefaultRollingFrameCapacity,
        IMapRenderTelemetryClock? clock = null)
    {
        if (rollingFrameCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rollingFrameCapacity),
                rollingFrameCapacity,
                "Rolling frame capacity must be positive.");
        }

        _clock = clock ?? MapRenderStopwatchTelemetryClock.Instance;
        if (_clock.Frequency <= 0)
        {
            throw new ArgumentException(
                "The telemetry clock frequency must be positive.",
                nameof(clock));
        }

        _presentedFrameMilliseconds = new RollingMetric(rollingFrameCapacity);
        _cpuFrameMilliseconds = new RollingMetric(rollingFrameCapacity);
        _gpuFrameMilliseconds = new RollingMetric(rollingFrameCapacity);

        int cpuPhaseCount = Enum.GetValues<MapRenderCpuPhase>().Length;
        _cpuPhaseMilliseconds = CreateMetrics(
            cpuPhaseCount,
            rollingFrameCapacity);
        _currentCpuPhaseTicks = new long[cpuPhaseCount];

        int gpuPhaseCount = Enum.GetValues<MapRenderGpuPhase>().Length;
        _gpuPhaseMilliseconds = CreateMetrics(
            gpuPhaseCount,
            rollingFrameCapacity);
        _lastGpuPhaseFrameIndexes = new long?[gpuPhaseCount];
        _lastGpuPhaseReadbackDelayFrames = new int[gpuPhaseCount];
        _currentGpuPhaseDrawCalls = new long[gpuPhaseCount];
        _currentGpuPhaseTriangles = new long[gpuPhaseCount];
        _lastGpuPhaseDrawCalls = new long[gpuPhaseCount];
        _lastGpuPhaseTriangles = new long[gpuPhaseCount];

        int counterCount = Enum.GetValues<MapRenderFrameCounter>().Length;
        _counterValues = CreateMetrics(counterCount, rollingFrameCapacity);
        _currentCounterValues = new long[counterCount];
        _lastCounterValues = new long[counterCount];
    }

    public bool IsCpuFrameActive => _cpuFrameActive;

    public long CpuFrameCount => _cpuFrameCount;

    public long PresentedFrameCount => _presentedFrameCount;

    /// <summary>
    /// Allocation-free rolling presentation rate for lightweight HUDs.
    /// Full telemetry snapshots remain available for diagnostics, but they
    /// sort percentile samples and should not be created once per frame.
    /// </summary>
    public double PresentedFramesPerSecond =>
        _presentedFrameMilliseconds.Average > 0
            ? 1000.0 / _presentedFrameMilliseconds.Average
            : 0;

    public long BeginCpuFrame()
    {
        if (_cpuFrameActive)
        {
            throw new InvalidOperationException(
                "A CPU frame is already being recorded.");
        }

        Array.Clear(_currentCpuPhaseTicks);
        Array.Clear(_currentCounterValues);
        Array.Clear(_currentGpuPhaseDrawCalls);
        Array.Clear(_currentGpuPhaseTriangles);
        _activeCpuFrameIndex = _nextFrameIndex++;
        _cpuFrameStartTimestamp = _clock.GetTimestamp();
        _cpuFrameStartAllocatedBytes =
            GC.GetAllocatedBytesForCurrentThread();
        _cpuFrameActive = true;
        return _activeCpuFrameIndex;
    }

    public MapRenderCpuPhaseScope BeginCpuPhase(MapRenderCpuPhase phase)
    {
        EnsureCpuFrameActive();
        ValidatePhase(phase);
        if (_cpuPhaseActive)
        {
            throw new InvalidOperationException(
                $"CPU phase {_activeCpuPhase} is already being recorded. " +
                "CPU telemetry phases must not overlap.");
        }

        _cpuPhaseActive = true;
        _activeCpuPhase = phase;
        _cpuPhaseStartTimestamp = _clock.GetTimestamp();
        _activeCpuPhaseToken = ++_nextCpuPhaseToken;
        return new MapRenderCpuPhaseScope(this, _activeCpuPhaseToken);
    }

    public void SetCounter(MapRenderFrameCounter counter, long value)
    {
        EnsureCpuFrameActive();
        ValidateCounter(counter);
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "A render counter cannot be negative.");
        }

        _currentCounterValues[(int)counter] = value;
    }

    public void AddCounter(MapRenderFrameCounter counter, long value = 1)
    {
        EnsureCpuFrameActive();
        ValidateCounter(counter);
        int index = (int)counter;
        long result = checked(_currentCounterValues[index] + value);
        if (result < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "A render counter cannot become negative.");
        }

        _currentCounterValues[index] = result;
    }

    public void AddGpuPhaseWork(
        MapRenderGpuPhase phase,
        long drawCalls,
        long triangles)
    {
        EnsureCpuFrameActive();
        int phaseIndex = (int)phase;
        if ((uint)phaseIndex >= (uint)_gpuPhaseMilliseconds.Length)
            throw new ArgumentOutOfRangeException(nameof(phase), phase, null);
        if (drawCalls < 0)
            throw new ArgumentOutOfRangeException(nameof(drawCalls));
        if (triangles < 0)
            throw new ArgumentOutOfRangeException(nameof(triangles));

        _currentGpuPhaseDrawCalls[phaseIndex] = checked(
            _currentGpuPhaseDrawCalls[phaseIndex] + drawCalls);
        _currentGpuPhaseTriangles[phaseIndex] = checked(
            _currentGpuPhaseTriangles[phaseIndex] + triangles);
    }

    public MapRenderCpuFrameTiming EndCpuFrame()
    {
        EnsureCpuFrameActive();
        if (_cpuPhaseActive)
        {
            throw new InvalidOperationException(
                $"CPU phase {_activeCpuPhase} must end before the CPU frame.");
        }

        long endTimestamp = _clock.GetTimestamp();
        long allocatedBytes =
            GC.GetAllocatedBytesForCurrentThread() -
            _cpuFrameStartAllocatedBytes;
        _currentCounterValues[
            (int)MapRenderFrameCounter.RenderThreadAllocatedBytes] =
            Math.Max(0, allocatedBytes);
        double frameMilliseconds = TicksToMilliseconds(
            ElapsedTicks(_cpuFrameStartTimestamp, endTimestamp));
        _cpuFrameMilliseconds.Add(frameMilliseconds);

        for (int i = 0; i < _cpuPhaseMilliseconds.Length; i++)
        {
            _cpuPhaseMilliseconds[i].Add(
                TicksToMilliseconds(_currentCpuPhaseTicks[i]));
        }

        for (int i = 0; i < _counterValues.Length; i++)
        {
            long value = _currentCounterValues[i];
            _lastCounterValues[i] = value;
            _counterValues[i].Add(value);
        }

        Array.Copy(
            _currentGpuPhaseDrawCalls,
            _lastGpuPhaseDrawCalls,
            _currentGpuPhaseDrawCalls.Length);
        Array.Copy(
            _currentGpuPhaseTriangles,
            _lastGpuPhaseTriangles,
            _currentGpuPhaseTriangles.Length);

        long completedFrameIndex = _activeCpuFrameIndex;
        _cpuFrameActive = false;
        _cpuFrameCount++;
        return new MapRenderCpuFrameTiming(
            completedFrameIndex,
            frameMilliseconds);
    }

    /// <summary>
    /// Records a completed presentation. Call this after the windowing layer's
    /// render/swap operation, separately from <see cref="EndCpuFrame"/>, so
    /// VSync and presentation pacing are represented in the FPS interval.
    /// </summary>
    public void RecordPresentedFrame(long frameIndex)
    {
        if (frameIndex < 0 || frameIndex >= _nextFrameIndex)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frameIndex),
                frameIndex,
                "Presented frame index was not issued by BeginCpuFrame.");
        }

        if (_lastPresentedFrameIndex is long lastFrameIndex &&
            frameIndex <= lastFrameIndex)
        {
            throw new InvalidOperationException(
                "Presented frame indexes must be recorded in increasing order.");
        }

        long timestamp = _clock.GetTimestamp();
        if (_lastPresentedFrameIndex.HasValue)
        {
            _presentedFrameMilliseconds.Add(
                TicksToMilliseconds(
                    ElapsedTicks(_lastPresentedTimestamp, timestamp)));
        }

        _lastPresentedTimestamp = timestamp;
        _lastPresentedFrameIndex = frameIndex;
        _presentedFrameCount++;
    }

    public void RecordGpuFrameTiming(MapRenderOpenGlGpuFrameTiming timing)
    {
        if (_lastGpuFrameIndex is long lastFrameIndex &&
            timing.FrameIndex <= lastFrameIndex)
        {
            throw new InvalidOperationException(
                "GPU frame timings must be recorded in increasing frame order.");
        }

        _gpuFrameMilliseconds.Add(timing.ElapsedMilliseconds);
        _lastGpuFrameIndex = timing.FrameIndex;
        _lastGpuReadbackDelayFrames = timing.ReadbackDelayFrames;
    }

    public void RecordGpuPhaseTiming(MapRenderOpenGlGpuPhaseTiming timing)
    {
        int phaseIndex = (int)timing.Phase;
        if ((uint)phaseIndex >= (uint)_gpuPhaseMilliseconds.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timing),
                timing.Phase,
                "Unknown GPU telemetry phase.");
        }

        if (_lastGpuPhaseFrameIndexes[phaseIndex] is long lastFrameIndex &&
            timing.FrameIndex <= lastFrameIndex)
        {
            throw new InvalidOperationException(
                $"GPU {timing.Phase} timings must be recorded in increasing frame order.");
        }

        _gpuPhaseMilliseconds[phaseIndex].Add(timing.ElapsedMilliseconds);
        _lastGpuPhaseFrameIndexes[phaseIndex] = timing.FrameIndex;
        _lastGpuPhaseReadbackDelayFrames[phaseIndex] =
            timing.ReadbackDelayFrames;
    }

    public MapRenderFrameTelemetrySnapshot CreateSnapshot()
    {
        MapRenderMetricSnapshot presented =
            _presentedFrameMilliseconds.CreateSnapshot();
        double presentedFps = PresentedFramesPerSecond;

        var phases = new MapRenderCpuPhaseTelemetrySnapshot[
            _cpuPhaseMilliseconds.Length];
        for (int i = 0; i < phases.Length; i++)
        {
            phases[i] = new MapRenderCpuPhaseTelemetrySnapshot(
                (MapRenderCpuPhase)i,
                _cpuPhaseMilliseconds[i].CreateSnapshot());
        }

        var counters = new MapRenderFrameCounterTelemetrySnapshot[
            _counterValues.Length];
        for (int i = 0; i < counters.Length; i++)
        {
            MapRenderMetricSnapshot metric =
                _counterValues[i].CreateSnapshot();
            counters[i] = new MapRenderFrameCounterTelemetrySnapshot(
                (MapRenderFrameCounter)i,
                _lastCounterValues[i],
                metric.Average,
                checked((long)metric.Maximum));
        }

        var gpuPhases = new MapRenderGpuPhaseTelemetrySnapshot[
            _gpuPhaseMilliseconds.Length];
        for (int i = 0; i < gpuPhases.Length; i++)
        {
            gpuPhases[i] = new MapRenderGpuPhaseTelemetrySnapshot(
                (MapRenderGpuPhase)i,
                _gpuPhaseMilliseconds[i].CreateSnapshot(),
                _lastGpuPhaseFrameIndexes[i],
                _lastGpuPhaseReadbackDelayFrames[i],
                _lastGpuPhaseDrawCalls[i],
                _lastGpuPhaseTriangles[i]);
        }

        return new MapRenderFrameTelemetrySnapshot(
            _cpuFrameCount,
            _presentedFrameCount,
            presentedFps,
            presented,
            _cpuFrameMilliseconds.CreateSnapshot(),
            _gpuFrameMilliseconds.CreateSnapshot(),
            _lastPresentedFrameIndex,
            _lastGpuFrameIndex,
            _lastGpuReadbackDelayFrames,
            phases,
            gpuPhases,
            counters);
    }

    internal void EndCpuPhase(long token)
    {
        // A copied scope may be disposed twice. Only the currently active token
        // owns the phase, so repeated disposal is deliberately a no-op.
        if (!_cpuPhaseActive || token != _activeCpuPhaseToken)
            return;

        long endTimestamp = _clock.GetTimestamp();
        int phaseIndex = (int)_activeCpuPhase;
        _currentCpuPhaseTicks[phaseIndex] = checked(
            _currentCpuPhaseTicks[phaseIndex] +
            ElapsedTicks(_cpuPhaseStartTimestamp, endTimestamp));
        _cpuPhaseActive = false;
    }

    private static RollingMetric[] CreateMetrics(int count, int capacity)
    {
        var metrics = new RollingMetric[count];
        for (int i = 0; i < metrics.Length; i++)
            metrics[i] = new RollingMetric(capacity);
        return metrics;
    }

    private void EnsureCpuFrameActive()
    {
        if (!_cpuFrameActive)
        {
            throw new InvalidOperationException(
                "BeginCpuFrame must be called before recording frame telemetry.");
        }
    }

    private void ValidatePhase(MapRenderCpuPhase phase)
    {
        if ((uint)(int)phase >= (uint)_cpuPhaseMilliseconds.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(phase), phase, null);
        }
    }

    private void ValidateCounter(MapRenderFrameCounter counter)
    {
        if ((uint)(int)counter >= (uint)_counterValues.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(counter), counter, null);
        }
    }

    private static long ElapsedTicks(long start, long end)
    {
        if (end < start)
        {
            throw new InvalidOperationException(
                "The telemetry clock moved backwards.");
        }

        return end - start;
    }

    private double TicksToMilliseconds(long ticks) =>
        ticks * 1000.0 / _clock.Frequency;

    private sealed class RollingMetric
    {
        private readonly double[] _values;
        private int _count;
        private int _nextIndex;
        private double _sum;
        private double _latest;

        public RollingMetric(int capacity)
        {
            _values = new double[capacity];
        }

        public double Average => _count == 0
            ? 0
            : _sum / _count;

        public void Add(double value)
        {
            if (!double.IsFinite(value) || value < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "A telemetry metric must be finite and non-negative.");
            }

            if (_count == _values.Length)
                _sum -= _values[_nextIndex];
            else
                _count++;

            _values[_nextIndex] = value;
            _sum += value;
            _latest = value;
            _nextIndex = (_nextIndex + 1) % _values.Length;
        }

        public MapRenderMetricSnapshot CreateSnapshot()
        {
            if (_count == 0)
                return MapRenderMetricSnapshot.Empty;

            var sorted = new double[_count];
            Array.Copy(_values, sorted, _count);
            Array.Sort(sorted);
            return new MapRenderMetricSnapshot(
                _count,
                _latest,
                _sum / _count,
                sorted[0],
                sorted[^1],
                Percentile(sorted, 0.50),
                Percentile(sorted, 0.95),
                Percentile(sorted, 0.99));
        }

        private static double Percentile(double[] sorted, double percentile)
        {
            if (sorted.Length == 1)
                return sorted[0];

            double position = (sorted.Length - 1) * percentile;
            int lower = (int)Math.Floor(position);
            int upper = (int)Math.Ceiling(position);
            if (lower == upper)
                return sorted[lower];

            double fraction = position - lower;
            return sorted[lower] +
                   ((sorted[upper] - sorted[lower]) * fraction);
        }
    }
}
