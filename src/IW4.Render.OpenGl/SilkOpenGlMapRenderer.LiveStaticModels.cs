using System.Numerics;
using IW4.Render.EditorPreview;
using IW4.Render.Geometry;
using IW4.Render.OpenGl.Shadows;
using IW4.Render.OpenGl.StaticModels;

namespace IW4.Render.OpenGl;

public sealed unsafe partial class SilkOpenGlMapRenderer
{
    private int? _loadedStaticModelSourceCount;
    private bool[]? _liveStaticModelVisibilityByObjectIndex;
    private MapRenderLiveSceneProjection?
        _latestCommittedStaticModelProjection;
    private MapRenderTransientStaticModelTranslation?
        _transientStaticModelTranslation;

    private void CaptureLiveStaticModelSourceAuthority(
        MapRenderScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        int? atlasCount = scene.StaticModelLightingAtlas?.EntryCount;
        int? worldCount =
            scene.WorldSource?.World.Dpvs.SModelDrawInsts.Count;
        if (atlasCount is { } exactAtlasCount &&
            worldCount is { } exactWorldCount &&
            exactAtlasCount != exactWorldCount)
        {
            throw new InvalidDataException(
                "The loaded Gfx static-model atlas and world source do not share one ordinal cardinality.");
        }

        _loadedStaticModelSourceCount = atlasCount ?? worldCount;
        if (_loadedStaticModelSourceCount is null &&
            !HasStaticModelTopology(scene))
        {
            _loadedStaticModelSourceCount = 0;
        }
        _liveStaticModelVisibilityByObjectIndex = null;
    }

    private static bool HasStaticModelTopology(MapRenderScene scene) =>
        scene.StaticModelScheduling.Count != 0 ||
        scene.InstancedTexturedBatches.Count != 0 ||
        scene.StaticModelLodTexturedBatches.Count != 0 ||
        scene.ExactNormalCameraStaticModelTexturedBatches.Count != 0 ||
        scene.ShadowAllocatedStaticModelTexturedBatches.Count != 0 ||
        scene.SunShadowStaticCasterBatches.Count != 0 ||
        scene.ReceiverVariants?.StaticModels.Values.Any(
            batches => batches.Count != 0) == true;

    private void ResetLiveStaticModelProjectionState()
    {
        _loadedStaticModelSourceCount = null;
        _liveStaticModelVisibilityByObjectIndex = null;
        _latestCommittedStaticModelProjection = null;
        _transientStaticModelTranslation = null;
    }

    /// <summary>
    /// Applies one renderer-local static-model translation draft over the
    /// latest committed semantic projection. This renderer-thread operation
    /// neither advances nor reuses the semantic document revision.
    /// </summary>
    public void ApplyTransientStaticModelTranslation(
        int sourceOrdinal,
        Vector3 gameOrigin)
    {
        ThrowIfUnavailable();
        if (!_loaded)
        {
            throw new InvalidOperationException(
                "A map scene must be loaded before applying a transient static-model translation.");
        }

        var transient = new MapRenderTransientStaticModelTranslation(
            sourceOrdinal,
            gameOrigin);
        MapRenderLiveSceneProjection committed =
            _latestCommittedStaticModelProjection ??
            throw new InvalidOperationException(
                "A complete committed static-model projection must be applied before a transient translation.");
        MapRenderLiveSceneProjection effective =
            MapRenderTransientStaticModelProjectionComposer.Compose(
                committed,
                transient);

        ApplyLiveStaticModelProjection(effective);
        _transientStaticModelTranslation = transient;
    }

    /// <summary>
    /// Clears the renderer-local translation draft and restores the exact
    /// latest committed static-model projection.
    /// </summary>
    public void ClearTransientStaticModelTranslation()
    {
        ThrowIfUnavailable();
        if (!_loaded)
        {
            throw new InvalidOperationException(
                "A map scene must be loaded before clearing a transient static-model translation.");
        }
        if (_transientStaticModelTranslation is null)
            return;

        MapRenderLiveSceneProjection committed =
            _latestCommittedStaticModelProjection ??
            throw new InvalidOperationException(
                "The active transient static-model translation has no committed projection to restore.");
        ApplyLiveStaticModelProjection(committed);
        _transientStaticModelTranslation = null;
    }

    private MapRenderLiveSceneProjection
        ComposeEffectiveStaticModelProjection(
            MapRenderLiveSceneProjection committed)
    {
        ArgumentNullException.ThrowIfNull(committed);
        if (!committed.HasStaticModelTranslationCatalog)
            return committed;

        return MapRenderTransientStaticModelProjectionComposer.Compose(
            committed,
            _transientStaticModelTranslation);
    }

    private void RetainCommittedStaticModelProjection(
        MapRenderLiveSceneProjection committed)
    {
        ArgumentNullException.ThrowIfNull(committed);
        if (committed.HasStaticModelTranslationCatalog)
            _latestCommittedStaticModelProjection = committed;
    }

    private bool IsLiveStaticModelEditorVisible(int objectIndex) =>
        _liveStaticModelVisibilityByObjectIndex is not
            { } liveVisibility ||
        (uint)objectIndex >= (uint)liveVisibility.Length ||
        liveVisibility[objectIndex];

    private void ApplyLiveStaticModelProjection(
        MapRenderLiveSceneProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        if (!projection.HasStaticModelTranslationCatalog)
            return;
        if (_loadedStaticModelSourceCount is not { } loadedSourceCount)
        {
            throw new InvalidOperationException(
                "The loaded scene has static-model topology but no authoritative Gfx source cardinality.");
        }

        StaticInstanceBufferRuntime[] instanceRuntimes =
            _staticInstanceBuffers.Values.ToArray();
        var retainedBatchOwners = new List<(
            MapRenderInstancedTexturedBatch[] Batches,
            int BatchIndex)>();
        var instanceBatches =
            new List<IReadOnlyList<MapRenderStaticModelInstance>>(
                instanceRuntimes.Length +
                _baseStaticBatches.Length);
        foreach (StaticInstanceBufferRuntime runtime in instanceRuntimes)
            instanceBatches.Add(runtime.Instances);
        AddRetainedBatches(_baseStaticBatches);
        if (_exactNormalCameraStaticRuntime is { } exactNormalCamera)
            AddRetainedBatches(exactNormalCamera.Batches);
        foreach (StaticReceiverVariantRuntime receiver in
                 _staticReceiverVariants)
        {
            AddRetainedBatches(receiver.Batches);
        }

        MapRenderOpenGlLiveStaticModelState staged =
            MapRenderOpenGlLiveStaticModelStateReconciler.Reconcile(
                projection,
                loadedSourceCount,
                _staticScheduling,
                instanceBatches,
                _sunShadowStaticCasterRuntimes.Select(
                    runtime => runtime.Batch).ToArray(),
                _sunShadowStaticCasterExpectations);

        bool schedulingChanged =
            !_staticScheduling.SequenceEqual(staged.Scheduling);
        bool visibilityChanged =
            _liveStaticModelVisibilityByObjectIndex is not { } current ||
            !current.AsSpan().SequenceEqual(
                staged.VisibilityByObjectIndex);
        if (staged.SunShadowCasterBatches.Length !=
            _sunShadowStaticCasterRuntimes.Length)
        {
            throw new InvalidOperationException(
                "The staged sun-shadow caster topology changed cardinality.");
        }
        for (int runtimeIndex = 0;
             runtimeIndex < _sunShadowStaticCasterRuntimes.Length;
             runtimeIndex++)
        {
            _sunShadowStaticCasterRuntimes[runtimeIndex]
                .ValidateReplacementBatch(
                    staged.SunShadowCasterBatches[runtimeIndex]);
        }
        var stagedSunShadowCasterIndex =
            new MapRenderOpenGlSunShadowStaticCasterIndex(
                staged.SunShadowCasterBatches,
                staged.SunShadowCasterExpectations);
        SunShadowDpvsWorker? priorSchedulingWorker =
            _sunShadowDpvsWorker;
        SunShadowDpvsWorker? stagedSchedulingWorker =
            StageLiveStaticModelSchedulingWorker(
                schedulingChanged,
                staged.Scheduling);

        int stagedBatchIndex = 0;
        for (int runtimeIndex = 0;
             runtimeIndex < instanceRuntimes.Length;
             runtimeIndex++, stagedBatchIndex++)
        {
            StaticInstanceBufferRuntime runtime =
                instanceRuntimes[runtimeIndex];
            MapRenderStaticModelInstance[] replacement =
                staged.InstanceBatches[stagedBatchIndex];
            if (runtime.Instances.AsSpan().SequenceEqual(replacement))
                continue;

            replacement.CopyTo(runtime.Instances, 0);
            runtime.HasLivePlacementChangePending = true;
        }
        foreach ((MapRenderInstancedTexturedBatch[] batches,
                     int batchIndex) in retainedBatchOwners)
        {
            MapRenderStaticModelInstance[] replacement =
                staged.InstanceBatches[stagedBatchIndex++];
            if (batches[batchIndex].Instances.SequenceEqual(replacement))
                continue;

            batches[batchIndex] = batches[batchIndex] with
            {
                Instances = replacement
            };
        }
        if (stagedBatchIndex != staged.InstanceBatches.Length)
        {
            throw new InvalidOperationException(
                "The staged static-model batch topology diverged before commit.");
        }

        _staticScheduling = staged.Scheduling;
        _staticSchedulingByObjectIndex.Clear();
        foreach ((int objectIndex,
                     MapRenderStaticModelSchedulingInfo scheduling) in
                 staged.SchedulingByObjectIndex)
        {
            _staticSchedulingByObjectIndex.Add(
                objectIndex,
                scheduling);
        }
        _liveStaticModelVisibilityByObjectIndex =
            staged.VisibilityByObjectIndex;
        for (int runtimeIndex = 0;
             runtimeIndex < _sunShadowStaticCasterRuntimes.Length;
             runtimeIndex++)
        {
            _sunShadowStaticCasterRuntimes[runtimeIndex].ReplaceBatch(
                staged.SunShadowCasterBatches[runtimeIndex]);
        }
        _sunShadowStaticCasterExpectations =
            staged.SunShadowCasterExpectations;
        _sunShadowStaticCasterIndex = stagedSunShadowCasterIndex;
        _currentSunShadowCasterAdmissionReused = false;

        if (schedulingChanged)
        {
            _lastProgressiveStaticCamera = null;
            priorSchedulingWorker?.Dispose();
            _sunShadowDpvsWorker = stagedSchedulingWorker;
            ResetSunShadowDpvsPipelineState();
        }
        if (schedulingChanged || visibilityChanged ||
            instanceRuntimes.Any(
                runtime => runtime.HasLivePlacementChangePending))
        {
            _staticInstanceCompactionFullInvalidationPending = true;
        }

        void AddRetainedBatches(
            MapRenderInstancedTexturedBatch[] batches)
        {
            for (int batchIndex = 0;
                 batchIndex < batches.Length;
                 batchIndex++)
            {
                retainedBatchOwners.Add((batches, batchIndex));
                instanceBatches.Add(batches[batchIndex].Instances);
            }
        }
    }

    private SunShadowDpvsWorker?
        StageLiveStaticModelSchedulingWorker(
            bool schedulingChanged,
            IReadOnlyList<MapRenderStaticModelSchedulingInfo>
                stagedScheduling)
    {
        ArgumentNullException.ThrowIfNull(stagedScheduling);
        if (!schedulingChanged || _sunShadowDpvsWorker is null)
            return _sunShadowDpvsWorker;
        if (_previewWorldSource is not { } source ||
            _sunShadowVisibilityProvider is null ||
            _sunShadowCasterCatalogProvider is null)
        {
            throw new InvalidOperationException(
                "The active sun-shadow scheduler lost its retained world providers.");
        }

        return new SunShadowDpvsWorker(
            source.World,
            _sunShadowVisibilityProvider,
            _sunShadowCasterCatalogProvider,
            stagedScheduling,
            _selectedStaticLodByObject);
    }
}
