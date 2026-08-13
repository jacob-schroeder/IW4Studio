using System.Numerics;

using IW4.Assets.Assets.GfxMap;
using IW4.Render.Diagnostics;
using IW4.Render.Geometry;
using IW4.Render.Scheduling;
using IW4.Render.Scheduling.Clear;
using IW4.Render.Scheduling.Dpvs;
using IW4.Render.Scheduling.Shadows;
using IW4.Render.Scheduling.StaticModels;
using IW4.Render.SceneBuilding;
using IW4.Render.Visibility;

namespace IW4.Render.OpenGl;

public sealed unsafe partial class SilkOpenGlMapRenderer
{
    private SunShadowDpvsWorker? _sunShadowDpvsWorker;
    private SunShadowDpvsWorkCompletion? _activeSunShadowDpvsPacket;
    private SunShadowDpvsWorkCompletion? _retainedSunShadowDpvsPacket;
    private bool[] _activeSunShadowVisibleStaticObjects = [];
    private int[] _activeSunShadowSelectedStaticLodByObject = [];
    private int[] _activeSunShadowVisibleStaticObjectWorklist = [];
    private bool[] _retainedSunShadowVisibleStaticObjects = [];
    private int[] _retainedSunShadowSelectedStaticLodByObject = [];
    private int[] _retainedSunShadowVisibleStaticObjectWorklist = [];
    private long _copiedSunShadowDpvsTicket;
    private long _lastPresentedSunShadowDpvsTicket;
    private bool _sunShadowSynchronousBootstrapCompleted;
    private SunShadowDpvsWorkKey? _preparedStaticSelectionKey;
    private MapRenderWorldDpvsViewVisibility?
        _preparedStaticSelectionVisibility;
    private SunShadowDpvsWorkKey? _preparedStaticLightingKey;
    private MapRenderWorldDpvsViewVisibility?
        _preparedStaticLightingVisibility;
    private long _preparedStaticLightingVisibleCount;

    /// <summary>
    /// Requests the newest input camera and selects the latest immutable CPU
    /// packet whose camera will own the whole rendered frame. A packet from an
    /// older camera is never applied to newer camera matrices: returning the
    /// packet camera deliberately introduces one frame of front-end latency.
    /// If the producer misses a deadline, the last complete packet is
    /// presented again. Retaining one coherent camera/visibility/shadow
    /// packet is preferable to switching a moving frame to the unrelated
    /// conservative fallback topology for a single deadline miss.
    /// </summary>
    private RenderCamera BeginSunShadowDpvsPreparation(
        RenderCamera requestedCamera)
    {
        _activeSunShadowDpvsPacket = null;
        _preparedStaticSelectionKey = null;
        _preparedStaticSelectionVisibility = null;
        _preparedStaticLightingKey = null;
        _preparedStaticLightingVisibility = null;
        _preparedStaticLightingVisibleCount = 0;
        _frameTelemetry.SetCounter(
            MapRenderFrameCounter.CpuWorkerJobs,
            0);
        _frameTelemetry.SetCounter(
            MapRenderFrameCounter.CpuWorkerCacheHits,
            0);
        _frameTelemetry.SetCounter(
            MapRenderFrameCounter.CpuWorkerWaitMicroseconds,
            0);
        _frameTelemetry.SetCounter(
            MapRenderFrameCounter.CpuWorkerAllocatedBytes,
            0);

        if (_sunShadowDpvsWorker is not { } worker ||
            _previewWorldSource is not { } source ||
            _sunShadowVisibilityProvider is null ||
            _sunShadowAtlas is null ||
            _selectedDirectionalSunPrimaryLightIndex is null)
        {
            return requestedCamera;
        }

        var requestedKey = new SunShadowDpvsWorkKey(
            source.AssetPoolRevisionAtConstruction,
            requestedCamera,
            _width,
            _height,
            RZFar: 0f,
            RendererFallback: requestedCamera.FarPlane);
        if (!_sunShadowSynchronousBootstrapCompleted)
        {
            // The cache/provider is intentionally single-producer. Let the
            // first frame establish its exact synchronous state before the
            // persistent worker begins producing N+1 packets.
            return requestedCamera;
        }
        worker.RequestLatest(
            requestedKey,
            _nextSunShadowFrameRevision,
            _activeRenderFrameIndex);

        EnsureActiveSunShadowSelectionCapacity();
        if (worker.TryCopyLatest(
                _activeSunShadowVisibleStaticObjects,
                _activeSunShadowSelectedStaticLodByObject,
                _activeSunShadowVisibleStaticObjectWorklist,
                _copiedSunShadowDpvsTicket,
                out SunShadowDpvsWorkCompletion completion))
        {
            _copiedSunShadowDpvsTicket = completion.Ticket;
            if (IsCompleteWorkerPacket(completion) &&
                IsPacketLifecycleCompatible(completion, source))
            {
                RetainWorkerPacket(completion);
            }
        }

        if (_retainedSunShadowDpvsPacket is not
                { } retained ||
            !IsPacketLifecycleCompatible(retained, source))
        {
            // The only normal occurrence is an extent change before its first
            // compatible worker result. Re-enter the synchronous bootstrap
            // path for one frame rather than presenting an old-extent packet
            // or switching to the conservative no-packet topology.
            _sunShadowSynchronousBootstrapCompleted = false;
            return requestedCamera;
        }

        _activeSunShadowDpvsPacket = retained;
        ApplyRetainedStaticSelection(retained);
        bool newlyPresented =
            retained.Ticket != _lastPresentedSunShadowDpvsTicket;
        _frameTelemetry.SetCounter(
            MapRenderFrameCounter.CpuWorkerJobs,
            newlyPresented ? 1 : 0);
        _frameTelemetry.SetCounter(
            MapRenderFrameCounter.CpuWorkerCacheHits,
            newlyPresented ? 0 : 1);
        _frameTelemetry.SetCounter(
            MapRenderFrameCounter.CpuWorkerAllocatedBytes,
            newlyPresented ? retained.AllocatedBytes : 0);
        return retained.Key.Camera;
    }

    /// <summary>
    /// Publishes the coherent packet chosen at frame entry. The first frame
    /// and any lifecycle-incompatible extent transition bootstrap
    /// synchronously. Ordinary worker misses reuse the retained complete
    /// packet and therefore never reach this method without matching
    /// visibility, selection, and camera ownership.
    /// </summary>
    private bool TryCompleteSunShadowDpvsPreparation(
            MapRenderWorldSceneSource source,
            IMapRenderWorldDpvsNormalCameraVisibilityProvider provider,
            RenderCamera camera,
            MapRenderNormalCameraFramebufferExtent extent,
            MapRenderNormalCameraFarPlaneState farPlane,
            long revision,
            out SunShadowFrameCpuPreparation preparation)
    {
        var expectedKey = new SunShadowDpvsWorkKey(
            source.AssetPoolRevisionAtConstruction,
            camera,
            extent.Width,
            extent.Height,
            farPlane.RZFar,
            farPlane.RendererFallback);
        if (_activeSunShadowDpvsPacket is
                { } completion &&
            completion.Key == expectedKey)
        {
            bool newlyPresented =
                completion.Ticket !=
                _lastPresentedSunShadowDpvsTicket;
            _lastPresentedSunShadowDpvsTicket =
                completion.Ticket;
            preparation = new(
                completion.Result,
                RebaseCasterResult(
                    revision,
                    completion.CasterResult),
                completion.CasterPrepared,
                newlyPresented);
            return true;
        }

        if (!_sunShadowSynchronousBootstrapCompleted)
        {
            MapRenderWorldDpvsVisibilityBuildResult visibility =
                provider.Build(
                    source.World,
                    camera,
                    extent,
                    farPlane);
            PrepareStaticSelectionOnRenderThread(
                expectedKey,
                camera,
                visibility);
            RetainSynchronousBootstrapPacket(
                expectedKey,
                visibility);
            _sunShadowSynchronousBootstrapCompleted = true;
            preparation = new(
                visibility,
                CasterResult: null,
                CasterPrepared: false,
                WasScheduled: false);
            return true;
        }

        preparation = default;
        return false;
    }

    private void EnsureActiveSunShadowSelectionCapacity()
    {
        if (_activeSunShadowVisibleStaticObjects.Length !=
            _visibleStaticObjects.Length)
        {
            _activeSunShadowVisibleStaticObjects =
                new bool[_visibleStaticObjects.Length];
        }
        if (_activeSunShadowSelectedStaticLodByObject.Length !=
            _selectedStaticLodByObject.Length)
        {
            _activeSunShadowSelectedStaticLodByObject =
                new int[_selectedStaticLodByObject.Length];
        }
        if (_activeSunShadowVisibleStaticObjectWorklist.Length !=
            _visibleStaticObjectWorklist.Length)
        {
            _activeSunShadowVisibleStaticObjectWorklist =
                new int[_visibleStaticObjectWorklist.Length];
        }
    }

    private void EnsureRetainedSunShadowSelectionCapacity()
    {
        if (_retainedSunShadowVisibleStaticObjects.Length !=
            _visibleStaticObjects.Length)
        {
            _retainedSunShadowVisibleStaticObjects =
                new bool[_visibleStaticObjects.Length];
        }
        if (_retainedSunShadowSelectedStaticLodByObject.Length !=
            _selectedStaticLodByObject.Length)
        {
            _retainedSunShadowSelectedStaticLodByObject =
                new int[_selectedStaticLodByObject.Length];
        }
        if (_retainedSunShadowVisibleStaticObjectWorklist.Length !=
            _visibleStaticObjectWorklist.Length)
        {
            _retainedSunShadowVisibleStaticObjectWorklist =
                new int[_visibleStaticObjectWorklist.Length];
        }
    }

    private bool IsPacketLifecycleCompatible(
        SunShadowDpvsWorkCompletion completion,
        MapRenderWorldSceneSource source) =>
        completion.Key.SceneRevision ==
            source.AssetPoolRevisionAtConstruction &&
        completion.Key.Width == _width &&
        completion.Key.Height == _height &&
        completion.Key.RZFar == 0f &&
        completion.Key.RendererFallback ==
            completion.Key.Camera.FarPlane;

    private static bool IsCompleteWorkerPacket(
        SunShadowDpvsWorkCompletion completion) =>
        completion.Result.IsSuccess &&
        completion.CameraVisibility is not null &&
        completion.StaticSelectionPrepared &&
        completion.CasterPrepared &&
        completion.CasterResult is { IsSuccess: true };

    private void RetainWorkerPacket(
        SunShadowDpvsWorkCompletion completion)
    {
        if (_retainedSunShadowDpvsPacket is
                { } retained &&
            retained.Ticket == completion.Ticket)
        {
            return;
        }

        EnsureRetainedSunShadowSelectionCapacity();
        _activeSunShadowVisibleStaticObjects.CopyTo(
            _retainedSunShadowVisibleStaticObjects,
            0);
        _activeSunShadowSelectedStaticLodByObject.CopyTo(
            _retainedSunShadowSelectedStaticLodByObject,
            0);
        _activeSunShadowVisibleStaticObjectWorklist
            .AsSpan(0, completion.VisibleScheduledObjectCount)
            .CopyTo(_retainedSunShadowVisibleStaticObjectWorklist);
        _retainedSunShadowDpvsPacket = completion;
    }

    private void RetainSynchronousBootstrapPacket(
        SunShadowDpvsWorkKey key,
        MapRenderWorldDpvsVisibilityBuildResult visibility)
    {
        if (!visibility.IsSuccess ||
            _preparedStaticSelectionKey != key ||
            _preparedStaticSelectionVisibility is not
                { } cameraVisibility)
        {
            return;
        }

        EnsureRetainedSunShadowSelectionCapacity();
        _visibleStaticObjects.CopyTo(
            _retainedSunShadowVisibleStaticObjects,
            0);
        _selectedStaticLodByObject.CopyTo(
            _retainedSunShadowSelectedStaticLodByObject,
            0);
        _visibleStaticObjectWorklist
            .AsSpan(0, _visibleScheduledStaticObjectCount)
            .CopyTo(_retainedSunShadowVisibleStaticObjectWorklist);
        _retainedSunShadowDpvsPacket = new(
            Ticket: 0,
            SubmissionFrameIndex: _activeRenderFrameIndex,
            Key: key,
            Result: visibility,
            CasterResult: null,
            CameraVisibility: cameraVisibility,
            VisibleScheduledObjectCount:
                _visibleScheduledStaticObjectCount,
            SelectionBufferIndex: -1,
            CasterPrepared: false,
            StaticSelectionPrepared: true,
            AllocatedBytes: 0);
    }

    private void ApplyRetainedStaticSelection(
        SunShadowDpvsWorkCompletion completion)
    {
        if (!completion.StaticSelectionPrepared)
            return;

        _retainedSunShadowVisibleStaticObjects.CopyTo(
            _visibleStaticObjects,
            0);
        _retainedSunShadowSelectedStaticLodByObject.CopyTo(
            _selectedStaticLodByObject,
            0);
        _retainedSunShadowVisibleStaticObjectWorklist
            .AsSpan(0, completion.VisibleScheduledObjectCount)
            .CopyTo(_visibleStaticObjectWorklist);
        PublishVisibleStaticObjectWorklist(
            completion.VisibleScheduledObjectCount);
        _preparedStaticSelectionKey = completion.Key;
        _preparedStaticSelectionVisibility =
            completion.CameraVisibility;
    }

    private void PrepareStaticSelectionOnRenderThread(
        SunShadowDpvsWorkKey key,
        RenderCamera camera,
        MapRenderWorldDpvsVisibilityBuildResult visibility)
    {
        if (!TryGetCameraVisibility(
                visibility,
                out MapRenderWorldDpvsViewVisibility? cameraVisibility))
        {
            return;
        }

        Span<Vector4> frustumPlanes =
            stackalloc Vector4[MapRenderCameraFrustum.PlaneCount];
        MapRenderCameraFrustum.BuildPlanes(
            camera,
            (float)key.Width / key.Height,
            frustumPlanes);
        int visibleScheduledObjectCount =
            MapRenderStaticModelLodSelector.SelectFrame(
                _staticScheduling,
                camera,
                frustumPlanes,
                cameraVisibility,
                _visibleStaticObjects,
                _selectedStaticLodByObject,
                _visibleStaticObjectWorklist,
                viewDistanceScale: 1f,
                nearViewScale: 1f,
                farViewScale: 1f);
        PublishVisibleStaticObjectWorklist(
            visibleScheduledObjectCount);
        _preparedStaticSelectionKey = key;
        _preparedStaticSelectionVisibility = cameraVisibility;
    }

    private bool TryUsePreparedStaticSelection(
        RenderCamera camera,
        MapRenderWorldDpvsViewVisibility? cameraVisibility)
    {
        if (_previewWorldSource is not { } source)
            return false;

        var expectedKey = new SunShadowDpvsWorkKey(
            source.AssetPoolRevisionAtConstruction,
            camera,
            _width,
            _height,
            RZFar: 0f,
            RendererFallback: camera.FarPlane);
        return _preparedStaticSelectionKey == expectedKey &&
               ReferenceEquals(
                   _preparedStaticSelectionVisibility,
                   cameraVisibility);
    }

    private bool TryGetPreparedCameraVisibility(
        RenderCamera camera,
        out MapRenderWorldDpvsViewVisibility? cameraVisibility)
    {
        cameraVisibility = null;
        if (_previewWorldSource is not { } source)
            return false;

        var expectedKey = new SunShadowDpvsWorkKey(
            source.AssetPoolRevisionAtConstruction,
            camera,
            _width,
            _height,
            RZFar: 0f,
            RendererFallback: camera.FarPlane);
        if (_preparedStaticSelectionKey != expectedKey ||
            _preparedStaticSelectionVisibility is not { } prepared)
        {
            return false;
        }

        cameraVisibility = prepared;
        return true;
    }

    private void PrepareStaticModelLightingAdmission(
        RenderCamera camera,
        MapRenderWorldDpvsViewVisibility cameraVisibility)
    {
        if (_previewWorldSource is not { } source)
        {
            throw new InvalidOperationException(
                "Static-model lighting admission requires the retained world source.");
        }

        var key = new SunShadowDpvsWorkKey(
            source.AssetPoolRevisionAtConstruction,
            camera,
            _width,
            _height,
            RZFar: 0f,
            RendererFallback: camera.FarPlane);
        if (_preparedStaticSelectionKey != key ||
            !ReferenceEquals(
                _preparedStaticSelectionVisibility,
                cameraVisibility))
        {
            throw new InvalidOperationException(
                "Static-model lighting admission requires the exact same-frame static selection.");
        }

        _preparedStaticLightingVisibleCount =
            UpdateStaticModelLightingWorkingSet();
        _preparedStaticLightingKey = key;
        _preparedStaticLightingVisibility = cameraVisibility;
    }

    private bool TryUsePreparedStaticModelLightingAdmission(
        RenderCamera camera,
        MapRenderWorldDpvsViewVisibility? cameraVisibility,
        out long visibleStaticObjectCount)
    {
        visibleStaticObjectCount = 0;
        if (_previewWorldSource is not { } source)
            return false;

        var expectedKey = new SunShadowDpvsWorkKey(
            source.AssetPoolRevisionAtConstruction,
            camera,
            _width,
            _height,
            RZFar: 0f,
            RendererFallback: camera.FarPlane);
        if (_preparedStaticLightingKey != expectedKey ||
            !ReferenceEquals(
                _preparedStaticLightingVisibility,
                cameraVisibility))
        {
            return false;
        }

        visibleStaticObjectCount =
            _preparedStaticLightingVisibleCount;
        return true;
    }

    private static bool TryGetCameraVisibility(
        MapRenderWorldDpvsVisibilityBuildResult visibility,
        out MapRenderWorldDpvsViewVisibility? cameraVisibility)
    {
        foreach (MapRenderWorldDpvsViewVisibility completed in
                 visibility.CompletedViews)
        {
            if (completed.ViewIndex !=
                MapRenderWorldDpvsViewIndex.Camera)
            {
                continue;
            }

            cameraVisibility = completed;
            return true;
        }

        cameraVisibility = null;
        return false;
    }

    private static MapRenderSunShadowCasterCatalogBuildResult?
        RebaseCasterResult(
            long revision,
            MapRenderSunShadowCasterCatalogBuildResult? result)
    {
        if (result is not { IsSuccess: true } ||
            result.Catalog is not { } catalog)
        {
            return result;
        }

        return MapRenderSunShadowCasterCatalogBuildResult.Succeeded(
            new(
                revision,
                catalog.Partition0,
                catalog.Partition1));
    }

    private void DrainPendingSunShadowDpvsPreparation()
    {
        // Frame cleanup releases only the renderer-side packet reference. The
        // persistent worker deliberately retains its latest immutable result
        // and newest pending request across frames.
        _activeSunShadowDpvsPacket = null;
        _preparedStaticSelectionKey = null;
        _preparedStaticSelectionVisibility = null;
        _preparedStaticLightingKey = null;
        _preparedStaticLightingVisibility = null;
        _preparedStaticLightingVisibleCount = 0;
    }

    private void ResetSunShadowDpvsPipelineState()
    {
        DrainPendingSunShadowDpvsPreparation();
        _activeSunShadowVisibleStaticObjects = [];
        _activeSunShadowSelectedStaticLodByObject = [];
        _activeSunShadowVisibleStaticObjectWorklist = [];
        _retainedSunShadowDpvsPacket = null;
        _retainedSunShadowVisibleStaticObjects = [];
        _retainedSunShadowSelectedStaticLodByObject = [];
        _retainedSunShadowVisibleStaticObjectWorklist = [];
        _copiedSunShadowDpvsTicket = 0;
        _lastPresentedSunShadowDpvsTicket = 0;
        _sunShadowSynchronousBootstrapCompleted = false;
    }

    private readonly record struct SunShadowFrameCpuPreparation(
        MapRenderWorldDpvsVisibilityBuildResult Visibility,
        MapRenderSunShadowCasterCatalogBuildResult? CasterResult,
        bool CasterPrepared,
        bool WasScheduled);

    private readonly record struct SunShadowDpvsWorkKey(
        long SceneRevision,
        RenderCamera Camera,
        int Width,
        int Height,
        float RZFar,
        float RendererFallback);

    private readonly record struct SunShadowDpvsWorkRequest(
        long Ticket,
        long Revision,
        long SubmissionFrameIndex,
        SunShadowDpvsWorkKey Key);

    private readonly record struct SunShadowDpvsWorkCompletion(
        long Ticket,
        long SubmissionFrameIndex,
        SunShadowDpvsWorkKey Key,
        MapRenderWorldDpvsVisibilityBuildResult Result,
        MapRenderSunShadowCasterCatalogBuildResult? CasterResult,
        MapRenderWorldDpvsViewVisibility? CameraVisibility,
        int VisibleScheduledObjectCount,
        int SelectionBufferIndex,
        bool CasterPrepared,
        bool StaticSelectionPrepared,
        long AllocatedBytes);

    /// <summary>
    /// Persistent latest-request CPU worker with two retained selection
    /// buffers. One buffer backs the published immutable packet while the
    /// producer writes the other. The pending slot is replaceable, so camera
    /// motion cannot create a frame-preparation backlog.
    /// </summary>
    private sealed class SunShadowDpvsWorker : IDisposable
    {
        private readonly object _gate = new();
        private readonly GfxWorldAsset _world;
        private readonly IMapRenderWorldDpvsNormalCameraVisibilityProvider
            _provider;
        private readonly MapRenderSunShadowCasterCatalogProvider
            _casterProvider;
        private readonly MapRenderStaticModelSchedulingInfo[]
            _staticScheduling;
        private readonly StaticSelectionBuffer[] _selectionBuffers;
        private readonly Vector4[] _frustumPlanes =
            new Vector4[MapRenderCameraFrustum.PlaneCount];
        private readonly Thread _thread;
        private SunShadowDpvsWorkRequest _pendingRequest;
        private SunShadowDpvsWorkRequest _runningRequest;
        private SunShadowDpvsWorkCompletion _latestCompletion;
        private SunShadowDpvsWorkKey? _failedKey;
        private long _nextTicket;
        private bool _hasPending;
        private bool _hasRunning;
        private bool _hasLatest;
        private bool _hasFailure;
        private bool _stopping;
        private bool _disposed;

        public SunShadowDpvsWorker(
            GfxWorldAsset world,
            IMapRenderWorldDpvsNormalCameraVisibilityProvider provider,
            MapRenderSunShadowCasterCatalogProvider casterProvider,
            IReadOnlyList<MapRenderStaticModelSchedulingInfo>
                staticScheduling,
            ReadOnlySpan<int> initialSelectedLodByObject)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _provider = provider ??
                throw new ArgumentNullException(nameof(provider));
            _casterProvider = casterProvider ??
                throw new ArgumentNullException(nameof(casterProvider));
            ArgumentNullException.ThrowIfNull(staticScheduling);
            _staticScheduling = staticScheduling.ToArray();
            _selectionBuffers =
            [
                new(
                    initialSelectedLodByObject,
                    _staticScheduling.Length),
                new(
                    initialSelectedLodByObject,
                    _staticScheduling.Length)
            ];
            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "IW4 renderer frame-preparation worker"
            };
            _thread.Start();
        }

        public long RequestLatest(
            SunShadowDpvsWorkKey key,
            long revision,
            long submissionFrameIndex)
        {
            if (key.Width <= 0 || key.Height <= 0)
                throw new ArgumentOutOfRangeException(nameof(key));
            if (revision < 0)
                throw new ArgumentOutOfRangeException(nameof(revision));
            if (submissionFrameIndex < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(submissionFrameIndex));
            }

            lock (_gate)
            {
                ThrowIfDisposed();
                if (_stopping)
                {
                    throw new ObjectDisposedException(
                        nameof(SunShadowDpvsWorker));
                }
                if (_hasLatest &&
                    _latestCompletion.Key == key)
                {
                    _hasPending = false;
                    return _latestCompletion.Ticket;
                }
                if (_hasRunning &&
                    _runningRequest.Key == key)
                {
                    _hasPending = false;
                    return _runningRequest.Ticket;
                }
                if (_hasPending &&
                    _pendingRequest.Key == key)
                {
                    return _pendingRequest.Ticket;
                }

                long ticket = checked(++_nextTicket);
                _pendingRequest = new(
                    ticket,
                    revision,
                    submissionFrameIndex,
                    key);
                _hasPending = true;
                Monitor.PulseAll(_gate);
                return ticket;
            }
        }

        public bool TryCopyLatest(
            bool[] visibleStaticObjects,
            int[] selectedStaticLodByObject,
            int[] visibleScheduledStaticObjectIndices,
            long alreadyCopiedTicket,
            out SunShadowDpvsWorkCompletion completion)
        {
            ArgumentNullException.ThrowIfNull(visibleStaticObjects);
            ArgumentNullException.ThrowIfNull(selectedStaticLodByObject);
            ArgumentNullException.ThrowIfNull(
                visibleScheduledStaticObjectIndices);

            lock (_gate)
            {
                ThrowIfDisposed();
                if (!_hasLatest)
                {
                    completion = default;
                    return false;
                }

                completion = _latestCompletion;
                if (!completion.StaticSelectionPrepared ||
                    completion.Ticket == alreadyCopiedTicket)
                    return true;

                StaticSelectionBuffer buffer =
                    _selectionBuffers[
                        completion.SelectionBufferIndex];
                if (visibleStaticObjects.Length !=
                        buffer.VisibleByObject.Length ||
                    selectedStaticLodByObject.Length !=
                        buffer.SelectedLodByObject.Length ||
                    visibleScheduledStaticObjectIndices.Length <
                        buffer.VisibleScheduledObjectIndices.Length)
                {
                    throw new ArgumentException(
                        "The latest frame-preparation destination no longer matches the worker-owned static selection shape.");
                }
                buffer.VisibleByObject.CopyTo(
                    visibleStaticObjects,
                    0);
                buffer.SelectedLodByObject.CopyTo(
                    selectedStaticLodByObject,
                    0);
                buffer.VisibleScheduledObjectIndices
                    .AsSpan(
                        0,
                        completion.VisibleScheduledObjectCount)
                    .CopyTo(visibleScheduledStaticObjectIndices);
                return true;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                    return;
                _stopping = true;
                _hasPending = false;
                Monitor.PulseAll(_gate);
            }

            _thread.Join();
            lock (_gate)
            {
                _disposed = true;
                _hasRunning = false;
                _hasLatest = false;
                _hasFailure = false;
                Monitor.PulseAll(_gate);
            }
        }

        private void Run()
        {
            while (true)
            {
                SunShadowDpvsWorkRequest request;
                int selectionBufferIndex;
                lock (_gate)
                {
                    while (!_stopping && !_hasPending)
                        Monitor.Wait(_gate);
                    if (_stopping)
                        return;

                    request = _pendingRequest;
                    _hasPending = false;
                    _runningRequest = request;
                    _hasRunning = true;
                    selectionBufferIndex =
                        _hasLatest
                            ? 1 -
                              _latestCompletion.SelectionBufferIndex
                            : 0;
                }

                StaticSelectionBuffer selection =
                    _selectionBuffers[selectionBufferIndex];
                MapRenderWorldDpvsVisibilityBuildResult? result = null;
                MapRenderSunShadowCasterCatalogBuildResult?
                    casterResult = null;
                MapRenderWorldDpvsViewVisibility? cameraVisibility = null;
                int visibleScheduledObjectCount = 0;
                bool casterPrepared = false;
                bool staticSelectionPrepared = false;
                Exception? failure = null;
                long allocationStart =
                    GC.GetAllocatedBytesForCurrentThread();
                try
                {
                    var extent =
                        new MapRenderNormalCameraFramebufferExtent(
                            request.Key.Width,
                            request.Key.Height);
                    var farPlane = new MapRenderNormalCameraFarPlaneState(
                        request.Key.RZFar,
                        request.Key.RendererFallback);
                    result = _provider.Build(
                        _world,
                        request.Key.Camera,
                        extent,
                        farPlane) ??
                        throw new InvalidOperationException(
                            "The DPVS provider returned no typed result.");
                    if (TryGetCameraVisibility(
                            result,
                            out cameraVisibility))
                    {
                        MapRenderCameraFrustum.BuildPlanes(
                            request.Key.Camera,
                            (float)request.Key.Width /
                            request.Key.Height,
                            _frustumPlanes);
                        visibleScheduledObjectCount =
                            MapRenderStaticModelLodSelector.SelectFrame(
                                _staticScheduling,
                                request.Key.Camera,
                                _frustumPlanes,
                                cameraVisibility,
                                selection.VisibleByObject,
                                selection.SelectedLodByObject,
                                selection.VisibleScheduledObjectIndices,
                                viewDistanceScale: 1f,
                                nearViewScale: 1f,
                                farViewScale: 1f);
                        staticSelectionPrepared = true;
                    }
                    if (result.IsSuccess)
                    {
                        if (cameraVisibility is null)
                        {
                            throw new InvalidOperationException(
                                "Successful three-view DPVS preparation omitted the normal-camera view.");
                        }
                        casterResult =
                            _casterProvider.BuildFastWorker(
                                request.Revision,
                                result);
                        casterPrepared = true;
                    }
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
                long allocatedBytes = Math.Max(
                    0,
                    GC.GetAllocatedBytesForCurrentThread() -
                    allocationStart);

                lock (_gate)
                {
                    _hasRunning = false;
                    if (failure is not null)
                    {
                        _failedKey = request.Key;
                        _hasFailure = true;
                    }
                    else
                    {
                        _latestCompletion = new(
                            request.Ticket,
                            request.SubmissionFrameIndex,
                            request.Key,
                            result!,
                            casterResult,
                            cameraVisibility,
                            visibleScheduledObjectCount,
                            selectionBufferIndex,
                            casterPrepared,
                            staticSelectionPrepared,
                            allocatedBytes);
                        _hasLatest = true;
                        if (_hasFailure &&
                            _failedKey == request.Key)
                        {
                            _failedKey = null;
                            _hasFailure = false;
                        }
                    }
                    Monitor.PulseAll(_gate);
                }
            }
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        private sealed class StaticSelectionBuffer
        {
            public StaticSelectionBuffer(
                ReadOnlySpan<int> initialSelectedLodByObject,
                int schedulingCount)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(
                    schedulingCount);
                VisibleByObject =
                    new bool[initialSelectedLodByObject.Length];
                SelectedLodByObject =
                    initialSelectedLodByObject.ToArray();
                VisibleScheduledObjectIndices =
                    new int[schedulingCount];
            }

            public bool[] VisibleByObject { get; }

            public int[] SelectedLodByObject { get; }

            public int[] VisibleScheduledObjectIndices { get; }
        }
    }
}
