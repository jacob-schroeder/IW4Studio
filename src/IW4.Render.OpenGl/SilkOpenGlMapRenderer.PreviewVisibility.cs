using System.Numerics;
using Silk.NET.OpenGL;

using IW4.Render.EditorPreview;
using IW4.Render.Diagnostics;
using IW4.Render.Execution;
using IW4.Render.Geometry;
using IW4.Render.Lighting;
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
        if (TryReusePreviewVisibilityPublication(
                camera,
                targetExtent,
                visibleStaticObjectCount))
        {
            RecordUnchangedStaticInstanceCompactionTelemetry();
            RecordPreviewVisibilityTelemetry(
                _previewVisibilityWorldCount,
                _previewVisibilityWorldRunCount,
                _previewVisibilityWorldIndexCount,
                visibleStaticObjectCount);
            return;
        }

        InvalidatePreviewVisibilityPublication();
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
                MapRenderWorldSurfaceCompactionResult compaction =
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

                MapRenderWorldSurfaceCompactionResult compaction =
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
        RecordPreviewVisibilityTelemetry(
            visibleWorldCount,
            visibleWorldRunCount,
            visibleWorldIndexCount,
            visibleStaticObjectCount);
        CommitPreviewVisibilityPublication(
            camera,
            targetExtent,
            visibleStaticObjectCount,
            visibleWorldCount,
            visibleWorldRunCount,
            visibleWorldIndexCount);
    }

    private bool TryReusePreviewVisibilityPublication(
        RenderCamera camera,
        MapRenderPixelExtent targetExtent,
        long visibleStaticObjectCount)
    {
        if (!_hasPreviewVisibilityPublication ||
            _activeSunShadowDpvsPacket is not { } packet ||
            packet.Ticket != _previewVisibilityPacketTicket ||
            packet.Key != _previewVisibilityPacketKey ||
            _previewVisibilitySceneGeneration !=
                _previewSceneGeneration ||
            _previewVisibilityCamera != camera ||
            _previewVisibilityTargetWidth != targetExtent.Width ||
            _previewVisibilityTargetHeight != targetExtent.Height ||
            !ReferenceEquals(
                _previewVisibilityFrustum,
                _currentPreviewFrustum) ||
            !ReferenceEquals(
                _previewVisibilityDpvs,
                _currentPreviewDpvs) ||
            !ReferenceEquals(
                _previewVisibilityTexturedMeshes,
                _textured) ||
            !ReferenceEquals(
                _previewVisibilityWorldSurfaceBatches,
                _worldSurfaceBatches) ||
            !ReferenceEquals(
                _previewVisibilityWorldReceiverVariants,
                _worldReceiverVariants) ||
            !ReferenceEquals(
                _previewVisibilityFrameGroups,
                ResolvePreviewVisibilityFrameGroups()) ||
            _previewVisibilityVisibleScheduledStaticObjectCount !=
                _visibleScheduledStaticObjectCount ||
            _previewVisibilityVisibleStaticObjectCount !=
                visibleStaticObjectCount ||
            _previewVisibilityUsesDynamicStaticLods !=
                _usesDynamicStaticLods ||
            _staticInstanceCompactionFullInvalidationPending)
        {
            return false;
        }

        MapRenderStaticModelLightingWorkingSet? workingSet =
            _staticModelLightingWorkingSet;
        if (!ReferenceEquals(
                _previewVisibilityStaticLightingWorkingSet,
                workingSet) ||
            _previewVisibilityStaticLightingAssignmentGeneration !=
                (workingSet?.AssignmentGeneration ?? 0) ||
            workingSet is not null &&
            !workingSet.DirtyAssignments.IsEmpty)
        {
            return false;
        }

        return HasExactPreviousStaticVisibilitySelection() &&
               HasExactPreviousWorldReceiverSelection();
    }

    private bool HasExactPreviousStaticVisibilitySelection()
    {
        if (!_hasPreviousStaticInstanceSelection ||
            !HasValidStaticInstanceCandidateShape() ||
            !ReferenceEquals(
                _previousStaticInstanceCandidateObjectIndices,
                _staticModelLightingObjectIndices) ||
            _previousVisibleStaticObjectCount !=
                _visibleStaticObjects.Length ||
            _previousSelectedStaticLodCount !=
                _selectedStaticLodByObject.Length ||
            _previousUsesDynamicStaticLods !=
                _usesDynamicStaticLods ||
            _previousVisibleStaticObjectWorklistCount !=
                _visibleStaticObjectWorklistCount ||
            !HasValidStaticReceiverOccurrenceSnapshot())
        {
            return false;
        }

        ReadOnlySpan<int> currentWorklist =
            _visibleStaticObjectWorklist.AsSpan(
                0,
                _visibleStaticObjectWorklistCount);
        ReadOnlySpan<int> previousWorklist =
            _previousVisibleStaticObjectWorklist.AsSpan(
                0,
                _previousVisibleStaticObjectWorklistCount);
        if (!currentWorklist.SequenceEqual(previousWorklist))
            return false;

        for (int index = 0; index < currentWorklist.Length; index++)
        {
            int objectIndex = currentWorklist[index];
            if ((uint)objectIndex >=
                    (uint)_visibleStaticObjects.Length ||
                (uint)objectIndex >=
                    (uint)_previousVisibleStaticObjects.Length ||
                _visibleStaticObjects[objectIndex] !=
                    _previousVisibleStaticObjects[objectIndex])
            {
                return false;
            }
            if (_usesDynamicStaticLods &&
                _selectedStaticLodByObject[objectIndex] !=
                    _previousSelectedStaticLodByObject[objectIndex])
            {
                return false;
            }
        }

        return _selectedStaticReceiverSurfaces.SetEquals(
                   _previousSelectedStaticReceiverSurfaces) &&
               _selectedStaticReceiverOccurrences.SetEquals(
                   _previousSelectedStaticReceiverOccurrences);
    }

    private bool HasExactPreviousWorldReceiverSelection()
    {
        if (_previewVisibilityBaseWorldReceiverActive !=
            _baseWorldReceiverVisibilityActive)
        {
            return false;
        }
        if (_baseWorldReceiverVisibilityActive &&
            !_baseWorldReceiverVisibilityWords.AsSpan().SequenceEqual(
                _previewVisibilityBaseWorldReceiverWords))
        {
            return false;
        }
        if (_previewVisibilityWorldReceiverChannels.Length !=
                _worldReceiverVariants.Length ||
            _previewVisibilityWorldReceiverWords.Length !=
                _worldReceiverVariants.Length ||
            _previewVisibilityWorldReceiverCounts.Length !=
                _worldReceiverVariants.Length)
        {
            return false;
        }

        for (int channelIndex = 0;
             channelIndex < _worldReceiverVariants.Length;
             channelIndex++)
        {
            WorldReceiverVariantRuntime channel =
                _worldReceiverVariants[channelIndex];
            if (!ReferenceEquals(
                    _previewVisibilityWorldReceiverChannels[channelIndex],
                    channel) ||
                _previewVisibilityWorldReceiverCounts[channelIndex] !=
                    channel.SelectionCount ||
                !channel.SelectionWords.AsSpan().SequenceEqual(
                    _previewVisibilityWorldReceiverWords[channelIndex]))
            {
                return false;
            }
        }
        return true;
    }

    private void CommitPreviewVisibilityPublication(
        RenderCamera camera,
        MapRenderPixelExtent targetExtent,
        long visibleStaticObjectCount,
        long visibleWorldCount,
        long visibleWorldRunCount,
        long visibleWorldIndexCount)
    {
        AdvancePreviewVisibilityPublicationRevision();
        if (_activeSunShadowDpvsPacket is not { } packet ||
            _staticInstanceCompactionFullInvalidationPending ||
            !_hasPreviousStaticInstanceSelection)
        {
            return;
        }

        CaptureWorldReceiverSelectionSnapshot();
        _previewVisibilityPacketTicket = packet.Ticket;
        _previewVisibilityPacketKey = packet.Key;
        _previewVisibilitySceneGeneration = _previewSceneGeneration;
        _previewVisibilityCamera = camera;
        _previewVisibilityTargetWidth = targetExtent.Width;
        _previewVisibilityTargetHeight = targetExtent.Height;
        _previewVisibilityFrustum = _currentPreviewFrustum;
        _previewVisibilityDpvs = _currentPreviewDpvs;
        _previewVisibilityTexturedMeshes = _textured;
        _previewVisibilityWorldSurfaceBatches = _worldSurfaceBatches;
        _previewVisibilityFrameGroups =
            ResolvePreviewVisibilityFrameGroups();
        _previewVisibilityWorldReceiverVariants =
            _worldReceiverVariants;
        _previewVisibilityStaticLightingWorkingSet =
            _staticModelLightingWorkingSet;
        _previewVisibilityStaticLightingAssignmentGeneration =
            _staticModelLightingWorkingSet?.AssignmentGeneration ?? 0;
        _previewVisibilityVisibleScheduledStaticObjectCount =
            _visibleScheduledStaticObjectCount;
        _previewVisibilityVisibleStaticObjectCount =
            visibleStaticObjectCount;
        _previewVisibilityUsesDynamicStaticLods =
            _usesDynamicStaticLods;
        _previewVisibilityWorldCount = visibleWorldCount;
        _previewVisibilityWorldRunCount = visibleWorldRunCount;
        _previewVisibilityWorldIndexCount = visibleWorldIndexCount;
        _hasPreviewVisibilityPublication = true;
    }

    private void CaptureWorldReceiverSelectionSnapshot()
    {
        _previewVisibilityBaseWorldReceiverActive =
            _baseWorldReceiverVisibilityActive;
        if (_baseWorldReceiverVisibilityActive)
        {
            if (_previewVisibilityBaseWorldReceiverWords.Length !=
                _baseWorldReceiverVisibilityWords.Length)
            {
                _previewVisibilityBaseWorldReceiverWords =
                    new uint[_baseWorldReceiverVisibilityWords.Length];
            }
            _baseWorldReceiverVisibilityWords.CopyTo(
                _previewVisibilityBaseWorldReceiverWords,
                0);
        }

        int channelCount = _worldReceiverVariants.Length;
        if (_previewVisibilityWorldReceiverChannels.Length !=
            channelCount)
        {
            _previewVisibilityWorldReceiverChannels =
                new WorldReceiverVariantRuntime[channelCount];
            _previewVisibilityWorldReceiverWords =
                new uint[channelCount][];
            _previewVisibilityWorldReceiverCounts =
                new int[channelCount];
        }
        for (int channelIndex = 0;
             channelIndex < channelCount;
             channelIndex++)
        {
            WorldReceiverVariantRuntime channel =
                _worldReceiverVariants[channelIndex];
            _previewVisibilityWorldReceiverChannels[channelIndex] =
                channel;
            _previewVisibilityWorldReceiverCounts[channelIndex] =
                channel.SelectionCount;
            uint[] snapshot =
                _previewVisibilityWorldReceiverWords[channelIndex];
            if (snapshot is null ||
                snapshot.Length != channel.SelectionWords.Length)
            {
                snapshot = new uint[channel.SelectionWords.Length];
                _previewVisibilityWorldReceiverWords[channelIndex] =
                    snapshot;
            }
            channel.SelectionWords.CopyTo(snapshot, 0);
        }
    }

    private MapRenderEditorDrawGroup<GlTexturedDrawCommand>[]
        ResolvePreviewVisibilityFrameGroups() =>
            _currentWorldReceiverTechniqueSelector is not null
                ? _receiverAwareEditorTexturedDrawGroups
                : _editorTexturedDrawGroups;

    private void RecordPreviewVisibilityTelemetry(
        long visibleWorldCount,
        long visibleWorldRunCount,
        long visibleWorldIndexCount,
        long visibleStaticObjectCount)
    {
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

    private void RecordUnchangedStaticInstanceCompactionTelemetry()
    {
        int candidateCount = checked(
            _visibleStaticObjectWorklistCount +
            _previousVisibleStaticObjectWorklistCount);
        _frameTelemetry.SetCounter(
            MapRenderFrameCounter.StaticInstanceChangeCandidates,
            candidateCount);
        _frameTelemetry.SetCounter(
            MapRenderFrameCounter.StaticInstanceChangedObjects,
            0);
        _frameTelemetry.SetCounter(
            MapRenderFrameCounter.StaticInstanceRuntimesRescanned,
            0);
        _frameTelemetry.SetCounter(
            MapRenderFrameCounter.StaticInstanceRowsRescanned,
            0);
    }

    private void InvalidatePreviewVisibilityPublication()
    {
        _hasPreviewVisibilityPublication = false;
        InvalidatePreparedTexturedDrawQueue();
    }

    private void AdvancePreviewVisibilityPublicationRevision()
    {
        unchecked
        {
            _previewVisibilityPublicationRevision++;
            if (_previewVisibilityPublicationRevision == 0)
                _previewVisibilityPublicationRevision = 1;
        }
    }

    private bool CanReusePreparedTexturedDrawQueue(
        MapRenderEditorDrawGroup<GlTexturedDrawCommand>[] frameGroups,
        IReadOnlyList<
            MapRenderEditorDrawGroup<GlTexturedDrawCommand>> drawGroups) =>
        _hasPreviewVisibilityPublication &&
        _hasPreparedTexturedDrawQueue &&
        ReferenceEquals(
            _preparedTexturedDrawFrameGroups,
            frameGroups) &&
        ReferenceEquals(
            _preparedTexturedDrawGroups,
            drawGroups) &&
        _preparedTexturedDrawVisibilityRevision ==
            _previewVisibilityPublicationRevision;

    private void CommitPreparedTexturedDrawQueue(
        MapRenderEditorDrawGroup<GlTexturedDrawCommand>[] frameGroups,
        IReadOnlyList<
            MapRenderEditorDrawGroup<GlTexturedDrawCommand>> drawGroups)
    {
        if (!_hasPreviewVisibilityPublication)
            return;

        _preparedTexturedDrawFrameGroups = frameGroups;
        _preparedTexturedDrawGroups = drawGroups;
        _preparedTexturedDrawVisibilityRevision =
            _previewVisibilityPublicationRevision;
        unchecked
        {
            _preparedTexturedDrawQueueRevision++;
            if (_preparedTexturedDrawQueueRevision == 0)
                _preparedTexturedDrawQueueRevision = 1;
        }
        _hasPreparedTexturedDrawQueue = true;
    }

    private void InvalidatePreparedTexturedDrawQueue()
    {
        InvalidateTextureResidencyCaches();
        _hasPreparedTexturedDrawQueue = false;
        _preparedTexturedDrawFrameGroups = null;
        _preparedTexturedDrawGroups = null;
        _preparedTexturedDrawVisibilityRevision = 0;
    }

    private void ClearPreviewVisibilityPublicationCache()
    {
        InvalidatePreviewVisibilityPublication();
        _previewVisibilityPacketTicket = 0;
        _previewVisibilityPacketKey = default;
        _previewVisibilitySceneGeneration = 0;
        _previewVisibilityCamera = default;
        _previewVisibilityTargetWidth = 0;
        _previewVisibilityTargetHeight = 0;
        _previewVisibilityFrustum = null;
        _previewVisibilityDpvs = null;
        _previewVisibilityTexturedMeshes = null;
        _previewVisibilityWorldSurfaceBatches = null;
        _previewVisibilityFrameGroups = null;
        _previewVisibilityWorldReceiverVariants = null;
        _previewVisibilityBaseWorldReceiverActive = false;
        _previewVisibilityBaseWorldReceiverWords = [];
        _previewVisibilityWorldReceiverChannels = [];
        _previewVisibilityWorldReceiverWords = [];
        _previewVisibilityWorldReceiverCounts = [];
        _previewVisibilityStaticLightingWorkingSet = null;
        _previewVisibilityStaticLightingAssignmentGeneration = 0;
        _previewVisibilityVisibleScheduledStaticObjectCount = 0;
        _previewVisibilityVisibleStaticObjectCount = 0;
        _previewVisibilityUsesDynamicStaticLods = false;
        _previewVisibilityWorldCount = 0;
        _previewVisibilityWorldRunCount = 0;
        _previewVisibilityWorldIndexCount = 0;
        _previewVisibilityPublicationRevision = 0;
        _preparedTexturedDrawQueueRevision = 0;
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
        }
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
        _state.Uniform4(
            uniforms.Vegetation.Parameters,
            vegetation?.IsEnabled == true ? 1f : 0f,
            vegetation?.Amplitude ?? 0f,
            vegetation?.AngularFrequency ?? 0f,
            vegetation?.SpatialFrequency ?? 0f);
        _state.Uniform4(
            uniforms.Vegetation.Bounds,
            mesh.LocalMinimumHeight,
            mesh.LocalHeightRange,
            0f,
            0f);
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
