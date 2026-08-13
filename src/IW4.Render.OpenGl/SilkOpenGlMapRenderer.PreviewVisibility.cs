using System.Numerics;
using Silk.NET.OpenGL;

using IW4.Render.EditorPreview;
using IW4.Render.Diagnostics;
using IW4.Render.Execution;
using IW4.Render.Geometry;
using IW4.Render.OpenGl.Presentation;
using IW4.Render.OpenGl.Programs;
using IW4.Render.OpenGl.Scheduling;
using IW4.Render.Scheduling;
using IW4.Render.Scheduling.Clear;
using IW4.Render.Scheduling.Dpvs;
using IW4.Render.Scheduling.StaticModels;
using IW4.Render.SceneBuilding;
using IW4.Render.Shaders;
using IW4.Render.OpenGl.Diagnostics;
using IW4.Render.OpenGl.World;
using IW4.Render.Visibility;
using IW4.Render.World;

namespace IW4.Render.OpenGl;

public sealed unsafe partial class SilkOpenGlMapRenderer
{
    private void UpdatePreviewVisibility(RenderCamera camera)
    {
        MapRenderPixelExtent targetExtent =
            MapRenderOpenGlNormalCameraTargetExtentPolicy.Resolve(
                SurfaceExtents,
                _editorPreviewPresentationSession is not null);
        float aspectRatio =
            (float)targetExtent.Width / targetExtent.Height;
        _currentPreviewFrustum =
            _previewFrustumCache.GetOrCreate(camera, aspectRatio);
        _currentPreviewDpvs = TryResolvePreviewDpvs(camera);

        if (!TryUsePreparedStaticSelection(
                camera,
                _currentPreviewDpvs))
        {
            // Both backends now consume this exact shared CPU selection
            // shape. EditorPreview keeps explicit unit view scalars because
            // their native dvar producers remain OPEN rather than being
            // invented here.
            int visibleScheduledObjectCount =
                MapRenderStaticModelLodSelector.SelectFrame(
                    _staticScheduling,
                    camera,
                    _currentPreviewFrustum,
                    _currentPreviewDpvs,
                    _visibleStaticObjects,
                    _selectedStaticLodByObject,
                    _visibleStaticObjectWorklist,
                    viewDistanceScale: 1f,
                    nearViewScale: 1f,
                    farViewScale: 1f);
            PublishVisibleStaticObjectWorklist(
                visibleScheduledObjectCount);
        }
        long visibleStaticObjectCount;
        if (!TryUsePreparedStaticModelLightingAdmission(
                camera,
                _currentPreviewDpvs,
                out visibleStaticObjectCount))
        {
            visibleStaticObjectCount =
                UpdateStaticModelLightingWorkingSet();
        }
        CompactChangedStaticInstances();

        ReadOnlySpan<uint> dpvsSurfaceWords = default;
        bool hasDpvsVisibility = _currentPreviewDpvs is not null;
        if (_baseWorldReceiverVisibilityActive)
        {
            dpvsSurfaceWords = _baseWorldReceiverVisibilityWords;
            hasDpvsVisibility = true;
        }
        else if (_currentPreviewDpvs is { } previewDpvs)
        {
            dpvsSurfaceWords = previewDpvs.SurfaceBitSpan;
        }

        long visibleWorldCount = 0;
        long visibleWorldRunCount = 0;
        long visibleWorldIndexCount = 0;
        for (int batchIndex = 0;
             batchIndex < _textured.Length;
             batchIndex++)
        {
            GlTexturedMesh mesh = _textured[batchIndex];
            if (_worldSurfaceBatches[batchIndex] is { } worldBatch)
            {
                if (!hasDpvsVisibility &&
                    !worldBatch.AllowsDecodedPerSurfaceFrustumCull &&
                    !IsWorldMeshVisible(mesh))
                {
                    worldBatch.ClearVisibleRuns();
                    continue;
                }

                MapRenderCameraFrustum? spanFrustum =
                    !hasDpvsVisibility &&
                    worldBatch.AllowsDecodedPerSurfaceFrustumCull
                        ? _currentPreviewFrustum
                        : null;
                MapRenderOpenGlWorldSurfaceCompactionResult compaction =
                    worldBatch.Compact(
                        dpvsSurfaceWords,
                        hasDpvsVisibility,
                        spanFrustum);
                visibleWorldCount = checked(
                    visibleWorldCount +
                    compaction.VisibleSurfaceSpanCount);
                visibleWorldRunCount = checked(
                    visibleWorldRunCount + compaction.RunCount);
                visibleWorldIndexCount = checked(
                    visibleWorldIndexCount +
                    compaction.VisibleIndexCount);
                continue;
            }

            if (mesh.IndexCount != 0 && IsWorldMeshVisible(mesh))
            {
                visibleWorldCount++;
                visibleWorldRunCount++;
                visibleWorldIndexCount = checked(
                    visibleWorldIndexCount + mesh.IndexCount);
            }
        }

        foreach (WorldReceiverVariantRuntime channel in
                 _worldReceiverVariants)
        {
            if (channel.SelectionCount == 0)
            {
                foreach (WorldSurfaceBatchRuntime? batch in
                         channel.SurfaceBatches)
                {
                    batch?.ClearVisibleRuns();
                }
                continue;
            }

            ReadOnlySpan<uint> selected = channel.SelectionWords;
            for (int batchIndex = 0;
                 batchIndex < channel.Meshes.Length;
                 batchIndex++)
            {
                if (channel.SurfaceBatches[batchIndex] is not
                    { } worldBatch)
                {
                    continue;
                }

                MapRenderOpenGlWorldSurfaceCompactionResult compaction =
                    worldBatch.Compact(
                        selected,
                        hasDpvsVisibility: true,
                        frustum: null);
                visibleWorldCount = checked(
                    visibleWorldCount +
                    compaction.VisibleSurfaceSpanCount);
                visibleWorldRunCount = checked(
                    visibleWorldRunCount + compaction.RunCount);
                visibleWorldIndexCount = checked(
                    visibleWorldIndexCount +
                    compaction.VisibleIndexCount);
            }
        }
        _frameTelemetry.SetCounter(
            MapRenderFrameCounter.WorldVisible,
            visibleWorldCount);
        _frameTelemetry.SetCounter(
            MapRenderFrameCounter.WorldVisibleRuns,
            visibleWorldRunCount);
        _frameTelemetry.SetCounter(
            MapRenderFrameCounter.WorldVisibleTriangles,
            visibleWorldIndexCount / 3);
        _frameTelemetry.SetCounter(
            MapRenderFrameCounter.StaticModelsVisible,
            visibleStaticObjectCount);
    }

    private MapRenderWorldDpvsViewVisibility? TryResolvePreviewDpvs(
        RenderCamera camera)
    {
        if (_currentSunShadowPublication is { } shadowFrame)
            return shadowFrame.Frame.Camera;

        if (TryGetPreparedCameraVisibility(
                camera,
                out MapRenderWorldDpvsViewVisibility?
                    preparedCameraVisibility))
        {
            // A three-view attempt can fail after completing the normal
            // camera. Preserve that exact immutable view instead of starting
            // a duplicate camera-only traversal during the same frame.
            return preparedCameraVisibility;
        }

        if (_previewWorldSource is null)
            return null;

        var key = new DpvsWorkKey(
            _previewSceneGeneration,
            camera,
            _width,
            _height);
        MapRenderOpenGlLatestWorkQueue<DpvsWorkKey, DpvsWorkResult>
            worker = _previewDpvsWorker ??=
                CreatePreviewDpvsWorker(
                    _previewWorldSource,
                    _previewDpvsCache) ??
                throw new InvalidOperationException(
                    "A preview world cannot create its bounded DPVS worker.");
        if (worker.TryGetExact(
                key,
                out DpvsWorkResult? completed))
        {
            return completed!.Result.IsSuccess
                ? completed.Result.Visibility
                : null;
        }

        // Consume a same-key failure so a later request may retry after a
        // transient producer exception. Typed DPVS failures are successful
        // work results and remain exact-cacheable.
        _ = worker.TryTakeFailure(key, out _);
        worker.Request(key);

        // Until an exact result for this camera is ready, conservative
        // frustum visibility remains authoritative and cannot hide geometry.
        return null;
    }

    private void CancelPreviewDpvsWork()
    {
        _previewDpvsWorker?.Dispose();
        _previewDpvsWorker = null;
    }

    private static
        MapRenderOpenGlLatestWorkQueue<DpvsWorkKey, DpvsWorkResult>?
        CreatePreviewDpvsWorker(
            MapRenderWorldSceneSource? source,
            MapRenderWorldDpvsCameraOnlyVisibilityCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        if (source is null)
            return null;

        return new(
            "IW4 renderer latest-camera DPVS worker",
            (key, cancellationToken) =>
            {
                var extent =
                    new MapRenderNormalCameraFramebufferExtent(
                        key.Width,
                        key.Height);
                var farPlane = new MapRenderNormalCameraFarPlaneState(
                    rZFar: 0f,
                    rendererFallback: key.Camera.FarPlane);
                return new DpvsWorkResult(
                    key,
                    cache.Build(
                        source.World,
                        key.Camera,
                        extent,
                        farPlane,
                        cancellationToken));
            });
    }

    private bool IsWorldMeshVisible(GlTexturedMesh mesh)
    {
        if (_currentPreviewFrustum is { } frustum &&
            !frustum.Intersects(mesh.WorldBounds))
        {
            return false;
        }

        return mesh.WorldSurfaceIndex < 0 ||
               IsDpvsBitVisible(
                   _currentPreviewDpvs,
                   mesh.WorldSurfaceIndex);
    }

    private bool IsStaticInstanceVisible(
        GlTexturedMesh mesh,
        int instanceIndex)
    {
        if (!_staticInstanceBuffers.TryGetValue(
                mesh.InstanceBuffer,
                out StaticInstanceBufferRuntime? runtime) ||
            (uint)instanceIndex >= (uint)runtime.Instances.Length)
        {
            return true;
        }
        if (runtime.IsReceiverVariant)
        {
            if (!runtime.IsReceiverInstanceSelected(
                    instanceIndex,
                    _receiverSelectionGeneration))
                return false;
        }
        else if (IsStaticIdentityReplaced(
                     runtime,
                     runtime.Instances[instanceIndex]))
        {
            return false;
        }
        return IsStaticObjectLodVisible(
            runtime.Instances[instanceIndex].ObjectIndex,
            mesh.StaticModelLodIndex);
    }

    private uint ResolveVisibleInstanceCount(GlTexturedMesh mesh)
    {
        if (_staticInstanceBuffers.TryGetValue(
                mesh.InstanceBuffer,
                out StaticInstanceBufferRuntime? runtime) &&
            (runtime.CanCompact ||
             runtime.HasCommittedReceiverDrawCompaction))
        {
            return runtime.VisibleCount;
        }
        return mesh.InstanceCount;
    }

    private bool IsStaticObjectVisible(int objectIndex)
    {
        if ((uint)objectIndex <
            (uint)_visibleStaticObjects.Length)
        {
            // SelectFrame deliberately initializes unscheduled-but-renderable
            // objects as visible. The working-set allocator can subsequently
            // clear this bit on an admission miss, so it remains authoritative
            // for both scheduled and fallback identities.
            return _visibleStaticObjects[objectIndex];
        }
        return !_staticSchedulingByObjectIndex.ContainsKey(
            objectIndex);
    }

    private bool IsStaticObjectLodVisible(int objectIndex, int lodIndex)
    {
        if (!IsStaticObjectVisible(objectIndex))
            return false;
        if (!_usesDynamicStaticLods)
            return true;
        if (!_staticSchedulingByObjectIndex.ContainsKey(objectIndex))
        {
            if ((uint)objectIndex >=
                (uint)_selectedStaticLodByObject.Length)
            {
                return false;
            }
            int fallbackLod = _selectedStaticLodByObject[objectIndex];
            return fallbackLod == UnknownStaticLodIndex ||
                   fallbackLod == lodIndex;
        }
        return (uint)objectIndex <
                   (uint)_selectedStaticLodByObject.Length &&
               _selectedStaticLodByObject[objectIndex] == lodIndex;
    }

    private void CompactChangedStaticInstances()
    {
        _staticInstanceRescanScratch.Clear();
        _changedStaticInstanceObjectIndices.Clear();

        bool candidateShapeValid =
            HasValidStaticInstanceCandidateShape();
        bool forceFullRescan =
            _staticInstanceCompactionFullInvalidationPending ||
            !_hasPreviousStaticInstanceSelection ||
            !candidateShapeValid ||
            !ReferenceEquals(
                _previousStaticInstanceCandidateObjectIndices,
                _staticModelLightingObjectIndices) ||
            _previousVisibleStaticObjectCount !=
                _visibleStaticObjects.Length ||
            _previousSelectedStaticLodCount !=
                _selectedStaticLodByObject.Length ||
            _previousUsesDynamicStaticLods != _usesDynamicStaticLods ||
            !HasValidStaticReceiverOccurrenceSnapshot();
        int candidateCount = forceFullRescan
            ? _staticModelLightingObjectIndices.Length
            : checked(
                _visibleStaticObjectWorklistCount +
                _previousVisibleStaticObjectWorklistCount);

        try
        {
            if (forceFullRescan)
            {
                QueueAllStaticInstanceRuntimesForRescan();
            }
            else
            {
                CollectChangedStaticObjectSelections();
                CollectChangedStaticReceiverSelections();
                CollectChangedStaticModelLightingAssignments();
            }

            long rescannedRuntimeCount = 0;
            long rescannedRowCount = 0;
            foreach (StaticInstanceBufferRuntime runtime in
                     _staticInstanceRescanScratch)
            {
                int rowsRescanned =
                    CompactVisibleStaticInstances(
                        runtime.OriginalInstanceBuffer,
                        runtime);
                if (rowsRescanned == 0)
                    continue;
                rescannedRuntimeCount++;
                rescannedRowCount = checked(
                    rescannedRowCount + rowsRescanned);
            }

            CommitStaticInstanceSelectionSnapshot(
                candidateShapeValid);
            _staticInstanceCompactionFullInvalidationPending =
                !candidateShapeValid;
            _frameTelemetry.SetCounter(
                MapRenderFrameCounter.StaticInstanceChangeCandidates,
                candidateCount);
            _frameTelemetry.SetCounter(
                MapRenderFrameCounter.StaticInstanceChangedObjects,
                forceFullRescan
                    ? candidateCount
                    : _changedStaticInstanceObjectIndices.Count);
            _frameTelemetry.SetCounter(
                MapRenderFrameCounter.StaticInstanceRuntimesRescanned,
                rescannedRuntimeCount);
            _frameTelemetry.SetCounter(
                MapRenderFrameCounter.StaticInstanceRowsRescanned,
                rescannedRowCount);
        }
        catch
        {
            // A partially compacted frame cannot publish its retained-state
            // snapshot. The next frame repairs every runtime conservatively.
            _staticInstanceCompactionFullInvalidationPending = true;
            throw;
        }
        finally
        {
            foreach (StaticInstanceBufferRuntime runtime in
                     _staticInstanceRescanScratch)
            {
                runtime.IsCompactionRescanQueued = false;
            }
            _staticInstanceRescanScratch.Clear();
            _changedStaticInstanceObjectIndices.Clear();
        }
    }

    private bool HasValidStaticInstanceCandidateShape()
    {
        if (_visibleScheduledStaticObjectCount < 0 ||
            _visibleScheduledStaticObjectCount >
                _visibleStaticObjectWorklistCount ||
            _visibleStaticObjectWorklistCount < 0 ||
            _visibleStaticObjectWorklistCount >
                _visibleStaticObjectWorklist.Length)
        {
            return false;
        }

        if (_staticModelLightingObjectIndices.Length == 0)
            return true;
        int firstObjectIndex =
            _staticModelLightingObjectIndices[0];
        int lastObjectIndex =
            _staticModelLightingObjectIndices[^1];
        return firstObjectIndex >= 0 &&
               (uint)lastObjectIndex <
                   (uint)_visibleStaticObjects.Length &&
               (uint)lastObjectIndex <
                   (uint)_selectedStaticLodByObject.Length;
    }

    private bool HasValidStaticReceiverOccurrenceSnapshot()
    {
        foreach ((
                     StaticInstanceBufferRuntime runtime,
                     int instanceIndex) in
                 _selectedStaticReceiverOccurrences)
        {
            if (!IsRegisteredStaticReceiverOccurrence(
                    runtime,
                    instanceIndex))
            {
                return false;
            }
        }
        foreach ((
                     StaticInstanceBufferRuntime runtime,
                     int instanceIndex) in
                 _previousSelectedStaticReceiverOccurrences)
        {
            if (!IsRegisteredStaticReceiverOccurrence(
                    runtime,
                    instanceIndex))
            {
                return false;
            }
        }
        return true;
    }

    private bool IsRegisteredStaticReceiverOccurrence(
        StaticInstanceBufferRuntime runtime,
        int instanceIndex) =>
        runtime.IsReceiverVariant &&
        (uint)instanceIndex < (uint)runtime.Instances.Length &&
        _staticInstanceBuffers.TryGetValue(
            runtime.OriginalInstanceBuffer,
            out StaticInstanceBufferRuntime? registered) &&
        ReferenceEquals(registered, runtime);

    private void CollectChangedStaticObjectSelections()
    {
        for (int index = 0;
             index < _visibleStaticObjectWorklistCount;
             index++)
        {
            int objectIndex =
                _visibleStaticObjectWorklist[index];
            bool previousVisible =
                _previousVisibleStaticObjects[objectIndex];
            bool changed = !previousVisible;
            if (!changed &&
                _usesDynamicStaticLods)
            {
                changed =
                    _selectedStaticLodByObject[objectIndex] !=
                    _previousSelectedStaticLodByObject[objectIndex];
            }
            if (changed)
                MarkStaticInstanceObjectChanged(objectIndex);
        }

        for (int index = 0;
             index < _previousVisibleStaticObjectWorklistCount;
             index++)
        {
            int objectIndex =
                _previousVisibleStaticObjectWorklist[index];
            if (!_visibleStaticObjects[objectIndex])
                MarkStaticInstanceObjectChanged(objectIndex);
        }
    }

    private void CollectChangedStaticReceiverSelections()
    {
        foreach (MapRenderStaticModelReceiverIdentity identity in
                 _selectedStaticReceiverSurfaces)
        {
            if (!_previousSelectedStaticReceiverSurfaces.Contains(
                    identity))
            {
                MarkStaticInstanceObjectChanged(
                    identity.ObjectIndex);
            }
        }
        foreach (MapRenderStaticModelReceiverIdentity identity in
                 _previousSelectedStaticReceiverSurfaces)
        {
            if (!_selectedStaticReceiverSurfaces.Contains(identity))
            {
                MarkStaticInstanceObjectChanged(
                    identity.ObjectIndex);
            }
        }

        foreach (var occurrence in
                 _selectedStaticReceiverOccurrences)
        {
            if (!_previousSelectedStaticReceiverOccurrences.Contains(
                    occurrence))
            {
                MarkStaticInstanceObjectChanged(
                    occurrence.Runtime.Instances[
                        occurrence.InstanceIndex].ObjectIndex);
            }
        }
        foreach (var occurrence in
                 _previousSelectedStaticReceiverOccurrences)
        {
            if (!_selectedStaticReceiverOccurrences.Contains(
                    occurrence))
            {
                MarkStaticInstanceObjectChanged(
                    occurrence.Runtime.Instances[
                        occurrence.InstanceIndex].ObjectIndex);
            }
        }
    }

    private void CollectChangedStaticModelLightingAssignments()
    {
        if (_staticModelLightingWorkingSet is not { } workingSet)
            return;

        foreach (var assignment in workingSet.DirtyAssignments)
        {
            MarkStaticInstanceObjectChanged(
                assignment.ObjectIndex);
            if (assignment.ReplacedObjectIndex >= 0)
            {
                MarkStaticInstanceObjectChanged(
                    assignment.ReplacedObjectIndex);
            }
        }
    }

    private void MarkStaticInstanceObjectChanged(int objectIndex)
    {
        if (!_changedStaticInstanceObjectIndices.Add(objectIndex) ||
            !_staticInstanceRuntimesByObjectIndex.TryGetValue(
                objectIndex,
                out List<StaticInstanceBufferRuntime>? runtimes))
        {
            return;
        }

        foreach (StaticInstanceBufferRuntime runtime in runtimes)
            QueueStaticInstanceRuntimeForRescan(runtime);
    }

    private void QueueAllStaticInstanceRuntimesForRescan()
    {
        foreach (StaticInstanceBufferRuntime runtime in
                 _staticInstanceBuffers.Values)
        {
            QueueStaticInstanceRuntimeForRescan(runtime);
        }
    }

    private void QueueStaticInstanceRuntimeForRescan(
        StaticInstanceBufferRuntime runtime)
    {
        if (runtime.IsCompactionRescanQueued)
            return;
        runtime.IsCompactionRescanQueued = true;
        _staticInstanceRescanScratch.Add(runtime);
    }

    private void CommitStaticInstanceSelectionSnapshot(
        bool candidateShapeValid)
    {
        if (_previousVisibleStaticObjects.Length !=
            _visibleStaticObjects.Length)
        {
            _previousVisibleStaticObjects =
                new bool[_visibleStaticObjects.Length];
        }
        if (_previousSelectedStaticLodByObject.Length !=
            _selectedStaticLodByObject.Length)
        {
            _previousSelectedStaticLodByObject =
                new int[_selectedStaticLodByObject.Length];
        }
        if (_previousVisibleStaticObjectWorklist.Length <
            _visibleStaticObjectWorklist.Length)
        {
            _previousVisibleStaticObjectWorklist =
                new int[_visibleStaticObjectWorklist.Length];
        }

        for (int index = 0;
             index < _previousVisibleStaticObjectWorklistCount;
             index++)
        {
            int objectIndex =
                _previousVisibleStaticObjectWorklist[index];
            if ((uint)objectIndex >=
                    (uint)_previousVisibleStaticObjects.Length ||
                (uint)objectIndex >=
                    (uint)_previousSelectedStaticLodByObject.Length)
            {
                continue;
            }
            _previousVisibleStaticObjects[objectIndex] = false;
            _previousSelectedStaticLodByObject[objectIndex] =
                UnknownStaticLodIndex;
        }

        _visibleStaticObjectWorklist
            .AsSpan(0, _visibleStaticObjectWorklistCount)
            .CopyTo(_previousVisibleStaticObjectWorklist);
        _previousVisibleStaticObjectWorklistCount =
            _visibleStaticObjectWorklistCount;
        for (int index = 0;
             index < _visibleStaticObjectWorklistCount;
             index++)
        {
            int objectIndex =
                _visibleStaticObjectWorklist[index];
            _previousVisibleStaticObjects[objectIndex] = true;
            _previousSelectedStaticLodByObject[objectIndex] =
                _selectedStaticLodByObject[objectIndex];
        }
        _previousVisibleStaticObjectCount =
            _visibleStaticObjects.Length;
        _previousSelectedStaticLodCount =
            _selectedStaticLodByObject.Length;
        _previousUsesDynamicStaticLods =
            _usesDynamicStaticLods;
        _previousStaticInstanceCandidateObjectIndices =
            _staticModelLightingObjectIndices;

        _previousSelectedStaticReceiverSurfaces.Clear();
        _previousSelectedStaticReceiverSurfaces.UnionWith(
            _selectedStaticReceiverSurfaces);
        _previousSelectedStaticReceiverOccurrences.Clear();
        _previousSelectedStaticReceiverOccurrences.UnionWith(
            _selectedStaticReceiverOccurrences);
        _hasPreviousStaticInstanceSelection =
            candidateShapeValid;
    }

    private int CompactVisibleStaticInstances(
        uint instanceBuffer,
        StaticInstanceBufferRuntime runtime)
    {
        if (!runtime.CanCompact)
        {
            runtime.VisibleCount = checked((uint)runtime.Instances.Length);
            if (runtime.IsReceiverVariant)
                return 0;

            bool fullPlacementChanged =
                runtime.HasLivePlacementChangePending;
            bool sourceLayoutChanged =
                !IsIdentityStaticInstanceSelection(
                    runtime.CurrentSourceIndices.AsSpan(
                        0,
                        runtime.CurrentSourceCount),
                    runtime.Instances.Length);
            StaticModelLightingEntryStage fullLightingStage =
                StageFullStaticModelLightingEntries(
                    runtime,
                    force: sourceLayoutChanged);
            if ((sourceLayoutChanged ||
                 fullLightingStage.Changed ||
                 fullPlacementChanged) &&
                runtime.Instances.Length != 0)
            {
                float[] transforms = runtime.CompactTransforms;
                MapRenderStaticInstanceBufferPacker.PackAll(
                    runtime.Instances,
                    runtime.LightingPayload,
                    transforms,
                    ResolveStaticModelLightingCoordinates(runtime));
                UploadCompactedStaticInstanceTransforms(
                    instanceBuffer,
                    runtime,
                    transforms,
                    runtime.Instances.Length);
                runtime.HasLivePlacementChangePending = false;
            }
            if (sourceLayoutChanged ||
                fullLightingStage.Changed ||
                fullPlacementChanged)
            {
                for (int index = 0;
                     index < runtime.Instances.Length;
                     index++)
                {
                    runtime.CurrentSourceIndices[index] = index;
                }
                runtime.CurrentSourceCount =
                    runtime.Instances.Length;
                CommitFullStaticModelLightingEntries(runtime);
            }
            if (fullLightingStage.Evaluated)
            {
                CommitStaticModelLightingAssignmentGeneration(
                    runtime);
            }
            return runtime.Instances.Length;
        }

        int visibleCount = 0;
        for (int sourceIndex = 0;
             sourceIndex < runtime.Instances.Length;
             sourceIndex++)
        {
            MapRenderStaticModelInstance instance =
                runtime.Instances[sourceIndex];
            MapRenderStaticInstanceSubset.AppendSelectedSourceIndex(
                sourceIndex,
                IsStaticObjectLodVisible(
                    instance.ObjectIndex,
                    runtime.LodIndex),
                runtime.IsReceiverVariant,
                runtime.IsReceiverVariant
                    ? runtime.ReceiverSelectionGenerations[sourceIndex]
                    : 0,
                _receiverSelectionGeneration,
                !runtime.IsReceiverVariant &&
                IsStaticIdentityReplaced(
                    runtime,
                    instance),
                runtime.NextSourceIndices,
                ref visibleCount);
        }

        bool sourceSelectionChanged =
            MapRenderStaticInstanceSubset.HasChanged(
                runtime.CurrentSourceIndices,
                runtime.CurrentSourceCount,
                runtime.NextSourceIndices,
                visibleCount);
        StaticModelLightingEntryStage lightingStage =
            StageSelectedStaticModelLightingEntries(
                runtime,
                runtime.NextSourceIndices.AsSpan(
                    0,
                    visibleCount),
                force: sourceSelectionChanged);
        bool selectedPlacementChanged =
            runtime.HasLivePlacementChangePending;
        bool changed =
            sourceSelectionChanged ||
            lightingStage.Changed ||
            selectedPlacementChanged;
        if (changed && visibleCount != 0)
        {
            MapRenderStaticInstanceBufferPacker.PackSelected(
                runtime.Instances,
                runtime.NextSourceIndices.AsSpan(0, visibleCount),
                runtime.LightingPayload,
                runtime.CompactTransforms,
                ResolveStaticModelLightingCoordinates(runtime));
            UploadCompactedStaticInstanceTransforms(
                instanceBuffer,
                runtime,
                runtime.CompactTransforms,
                visibleCount);
            runtime.HasLivePlacementChangePending = false;
        }
        if (changed)
        {
            Array.Copy(
                runtime.NextSourceIndices,
                runtime.CurrentSourceIndices,
                visibleCount);
            runtime.CurrentSourceCount = visibleCount;
            CommitSelectedStaticModelLightingEntries(
                runtime,
                visibleCount);
        }
        if (lightingStage.Evaluated)
        {
            CommitStaticModelLightingAssignmentGeneration(
                runtime);
        }
        runtime.VisibleCount = checked((uint)visibleCount);
        return runtime.Instances.Length;
    }

    private bool IsSelectedExactStaticReceiver(
        MapRenderStaticModelInstance instance,
        int lodIndex) =>
        lodIndex >= 0 &&
        _selectedStaticReceiverSurfaces.Contains(
            new MapRenderStaticModelReceiverIdentity(instance, lodIndex));

    private bool IsStaticIdentityReplaced(
        StaticInstanceBufferRuntime runtime,
        MapRenderStaticModelInstance instance)
    {
        if (runtime.LodIndex < 0)
            return false;
        var identity = new MapRenderStaticModelReceiverIdentity(
            instance,
            runtime.LodIndex);
        if (_selectedStaticReceiverSurfaces.Contains(identity))
            return true;

        return !runtime.IsExactNormalCameraVariant &&
            _exactNormalCameraStaticRuntime?.Surfaces.ContainsKey(
                identity) == true;
    }

    private static bool IsDpvsBitVisible(
        MapRenderWorldDpvsViewVisibility? visibility,
        int index)
    {
        if (visibility is null || index < 0)
            return true;
        ReadOnlySpan<uint> words = visibility.SurfaceBitSpan;
        int wordIndex = index >> 5;
        if ((uint)wordIndex >= (uint)words.Length)
            return true;
        uint mask = 0x80000000u >> (index & 31);
        return (words[wordIndex] & mask) != 0;
    }

    private void ConfigureTexturedInstanceBase(
        uint instanceBuffer,
        int instanceIndex,
        uint firstAttribute = 9)
    {
        bool hasRuntime = _staticInstanceBuffers.TryGetValue(
            instanceBuffer,
            out StaticInstanceBufferRuntime? runtime);
        _state.BindArrayBuffer(
            hasRuntime
                ? runtime!.ActiveInstanceBuffer
                : instanceBuffer);
        bool hasLightingPayload = hasRuntime
            ? runtime!.InstanceFloatStride ==
                MapRenderStaticInstanceBufferPacker
                    .LightingAwareFloatStride
            : firstAttribute ==
                MapRenderOpenGlStaticModelInstancedVertexComposer
                    .FirstPlacementAttribute;
        uint instanceStride = (hasLightingPayload ? 16u : 12u) *
            sizeof(float);
        nuint instanceOffset = checked((nuint)instanceIndex * instanceStride);
        uint placementFloatOffset = hasLightingPayload ? 4u : 0u;
        if (hasLightingPayload)
        {
            _gl.VertexAttribPointer(
                MapRenderOpenGlStaticModelInstancedVertexComposer
                    .LightingPayloadAttribute,
                4,
                VertexAttribPointerType.Float,
                false,
                instanceStride,
                (void*)instanceOffset);
            _gl.VertexAttribDivisor(
                MapRenderOpenGlStaticModelInstancedVertexComposer
                    .LightingPayloadAttribute,
                1);
        }
        for (uint row = 0; row < 3; row++)
        {
            uint attribute = firstAttribute + row;
            _gl.VertexAttribPointer(
                attribute,
                4,
                VertexAttribPointerType.Float,
                false,
                instanceStride,
                (void*)(instanceOffset +
                    (placementFloatOffset + row * 4) * sizeof(float)));
            _gl.VertexAttribDivisor(attribute, 1);
        }
    }

    private void ApplyStaticModelInstancingFrame(
        MapRenderOpenGlStaticModelProgramUniforms? programUniforms,
        DerivedMatrixState matrices)
    {
        if (programUniforms is not { } uniforms)
            return;
        if (!uniforms.HasFrameRows)
        {
            throw new InvalidOperationException(
                "A translated static-model program lost its frame-row uniform layout.");
        }

        ApplyMatrixRows(uniforms.ViewRowLocations, matrices.View);
        ApplyMatrixRows(
            uniforms.ViewProjectionRowLocations,
            matrices.ViewProjection);
        _state.Uniform3(
            uniforms.EyeOffsetLocation,
            matrices.EyeOffset.X,
            matrices.EyeOffset.Y,
            matrices.EyeOffset.Z);
    }

    private void ApplyTranslatedStaticComposition(
        MapRenderOpenGlStaticModelProgramUniforms? programUniforms,
        GlTexturedMesh mesh,
        float editorTimeSeconds)
    {
        if (programUniforms is not { } uniforms)
            return;
        if (!uniforms.Vegetation.IsReady)
        {
            throw new InvalidOperationException(
                "A translated static-model program lost its Live Preview vegetation uniform layout.");
        }

        // This is the same bounded EditorPreview deformation used by the
        // generic host shader. It is not an authored RSX wind constant. Always
        // write the disabled values too, because one translated program can be
        // shared by vegetation and non-vegetation static-model batches.
        MapRenderEditorVegetationAnimationPlan? vegetation =
            mesh.VegetationAnimation;
        _state.Uniform1(
            uniforms.Vegetation.WindEnabled,
            vegetation?.IsEnabled == true ? 1 : 0);
        _state.Uniform1(uniforms.Vegetation.Time, editorTimeSeconds);
        _state.Uniform1(
            uniforms.Vegetation.Amplitude,
            vegetation?.Amplitude ?? 0f);
        _state.Uniform1(
            uniforms.Vegetation.AngularFrequency,
            vegetation?.AngularFrequency ?? 0f);
        _state.Uniform1(
            uniforms.Vegetation.SpatialFrequency,
            vegetation?.SpatialFrequency ?? 0f);
        _state.Uniform1(
            uniforms.Vegetation.LocalMinimumHeight,
            mesh.LocalMinimumHeight);
        _state.Uniform1(
            uniforms.Vegetation.LocalHeightRange,
            mesh.LocalHeightRange);
    }

    private void ApplyMatrixRows(
        IReadOnlyList<int> locations,
        Matrix4x4 matrix)
    {
        _state.Uniform4(
            locations[0],
            matrix.M11,
            matrix.M12,
            matrix.M13,
            matrix.M14);
        _state.Uniform4(
            locations[1],
            matrix.M21,
            matrix.M22,
            matrix.M23,
            matrix.M24);
        _state.Uniform4(
            locations[2],
            matrix.M31,
            matrix.M32,
            matrix.M33,
            matrix.M34);
        _state.Uniform4(
            locations[3],
            matrix.M41,
            matrix.M42,
            matrix.M43,
            matrix.M44);
    }

    private readonly record struct DpvsWorkKey(
        long SceneGeneration,
        RenderCamera Camera,
        int Width,
        int Height);

    private sealed record DpvsWorkResult(
        DpvsWorkKey Key,
        MapRenderWorldDpvsCameraOnlyVisibilityBuildResult Result);

}
