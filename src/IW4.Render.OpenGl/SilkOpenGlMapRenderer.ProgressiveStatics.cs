using System.Numerics;
using IW4.Render.EditorPreview;
using IW4.Render.Geometry;
using IW4.Render.OpenGl.StaticModels;
using IW4.Render.Resources;
using IW4.Render.Scheduling;
using IW4.Render.Scheduling.StaticModels;
using IW4.Render.SceneBuilding;
using IW4.Render.Visibility;

namespace IW4.Render.OpenGl;

public sealed unsafe partial class SilkOpenGlMapRenderer
{
    private const float InitialStaticTranslationPrefetchDistance = 768f;
    private const int
        ProgressiveStaticAdmissionMaximumGroupsPerFrame = 4;
    private const double ProgressiveStaticAdmissionBudgetMilliseconds = 2d;
    private const int
        ProgressiveStaticPublicationMaximumDeferredBatches = 16;
    private const int
        ProgressiveStaticPublicationMaximumAdmissionFrames = 4;
    private int _progressiveStaticAdmissionLaneCursor;
    private int _progressiveStaticUnpublishedBatchCount;
    private int _progressiveStaticUnpublishedAdmissionFrameCount;

    private void InitializeStaticSchedulingState(
        MapRenderScene scene,
        IReadOnlyList<MapRenderInstancedTexturedBatch> staticBatches,
        IReadOnlyDictionary<int, int> preparedStaticObjectLods)
    {
        _progressiveStaticAdmissionLaneCursor = 0;
        _progressiveStaticUnpublishedBatchCount = 0;
        _progressiveStaticUnpublishedAdmissionFrameCount = 0;
        foreach (MapRenderStaticModelSchedulingInfo scheduling in
                 scene.StaticModelScheduling)
        {
            _staticSchedulingByObjectIndex[scheduling.ObjectIndex] =
                scheduling;
        }
        _staticScheduling = scene.StaticModelScheduling.ToArray();

        var staticObjectIndices = new HashSet<int>();
        foreach (MapRenderStaticModelSchedulingInfo scheduling in
                 scene.StaticModelScheduling)
        {
            staticObjectIndices.Add(scheduling.ObjectIndex);
        }
        foreach (MapRenderInstancedTexturedBatch batch in staticBatches)
        {
            foreach (MapRenderStaticModelInstance instance in
                     batch.Instances)
            {
                staticObjectIndices.Add(instance.ObjectIndex);
            }
        }
        foreach (MapRenderStaticModelReceiverIdentity identity in
                 _staticReceiverExpectedIdentities)
        {
            staticObjectIndices.Add(identity.ObjectIndex);
        }
        foreach (MapRenderStaticModelReceiverIdentity identity in
                 _exactNormalCameraStaticExpectedIdentities)
        {
            staticObjectIndices.Add(identity.ObjectIndex);
        }
        _staticModelLightingObjectIndices = staticObjectIndices
            .Order()
            .ToArray();
        _conservativeUnscheduledStaticObjectIndices =
            _staticModelLightingObjectIndices
                .Where(objectIndex =>
                    !_staticSchedulingByObjectIndex.ContainsKey(
                        objectIndex))
                .ToArray();
        int staticObjectCapacity =
            _staticModelLightingObjectIndices.Length == 0
                ? 0
                : checked(
                    _staticModelLightingObjectIndices[^1] + 1);
        if (_staticModelLightingAtlas is { } lightingAtlas &&
            staticObjectCapacity > lightingAtlas.EntryCount)
        {
            throw new InvalidDataException(
                "Renderable static-model identities exceed the object-indexed model-lighting source.");
        }
        _visibleStaticObjects = new bool[staticObjectCapacity];
        _selectedStaticLodByObject = new int[staticObjectCapacity];
        Array.Fill(
            _selectedStaticLodByObject,
            UnknownStaticLodIndex);
        foreach ((int objectIndex, int lodIndex) in preparedStaticObjectLods)
        {
            if ((uint)objectIndex <
                (uint)_selectedStaticLodByObject.Length)
            {
                _selectedStaticLodByObject[objectIndex] = lodIndex;
            }
        }
        foreach (MapRenderStaticModelSchedulingInfo scheduling in
                 scene.StaticModelScheduling)
        {
            if ((uint)scheduling.ObjectIndex <
                (uint)_selectedStaticLodByObject.Length)
            {
                _selectedStaticLodByObject[scheduling.ObjectIndex] =
                    scheduling.PreparedLodIndex;
            }
        }
        _visibleStaticObjectWorklist = new int[checked(
            _staticScheduling.Length +
            _conservativeUnscheduledStaticObjectIndices.Length)];
        _visibleScheduledStaticObjectCount = 0;
        _visibleStaticObjectWorklistCount = 0;
    }

    private void SelectProgressiveStaticObjects(
        RenderCamera camera,
        float aspectRatio)
    {
        Span<Vector4> frustumPlanes =
            stackalloc Vector4[MapRenderCameraFrustum.PlaneCount];
        MapRenderCameraFrustum.BuildPlanes(
            camera,
            aspectRatio,
            frustumPlanes);
        int visibleScheduledObjectCount =
            MapRenderStaticModelLodSelector.SelectFrame(
                _staticScheduling,
                camera,
                frustumPlanes,
                cameraVisibility: null,
                _visibleStaticObjects,
                _selectedStaticLodByObject,
                _visibleStaticObjectWorklist,
                viewDistanceScale: 1f,
                nearViewScale: 1f,
                farViewScale: 1f);
        PublishVisibleStaticObjectWorklist(
            visibleScheduledObjectCount);
        _lastProgressiveStaticCamera = camera;
        _lastProgressiveStaticAspectRatio = aspectRatio;
    }

    /// <summary>
    /// Completes the compact selection emitted by the native-shaped scheduled
    /// LOD pass with only the renderable identities for which reconstruction
    /// has no scheduling row. Those identities retain the established
    /// conservative-visible fallback without re-scanning the sparse
    /// object-index address space.
    /// </summary>
    private void PublishVisibleStaticObjectWorklist(
        int visibleScheduledObjectCount)
    {
        if ((uint)visibleScheduledObjectCount >
            (uint)_staticScheduling.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(visibleScheduledObjectCount));
        }

        int outputCount = visibleScheduledObjectCount;
        foreach (int objectIndex in
                 _conservativeUnscheduledStaticObjectIndices)
        {
            if ((uint)objectIndex >=
                    (uint)_visibleStaticObjects.Length ||
                !_visibleStaticObjects[objectIndex])
            {
                continue;
            }
            if ((uint)outputCount >=
                (uint)_visibleStaticObjectWorklist.Length)
            {
                throw new InvalidOperationException(
                    "The compact static-object worklist no longer matches the retained scene topology.");
            }
            _visibleStaticObjectWorklist[outputCount++] =
                objectIndex;
        }

        _visibleScheduledStaticObjectCount =
            visibleScheduledObjectCount;
        _visibleStaticObjectWorklistCount = outputCount;
    }

    private void InitializeBaseStaticResources()
    {
        _baseStaticGroupPlan =
            MapRenderOpenGlStaticResourceGroupPlan.Create(
                _baseStaticBatches,
                requireReceiverIdentityClosure: false);
        _baseStaticResolvedGroups =
            new bool[_baseStaticGroupPlan.GroupCount];
        _baseStaticExecutableGroups =
            new bool[_baseStaticGroupPlan.GroupCount];
        _baseStaticDrawGroupCache =
            new MapRenderOpenGlProgressiveStaticDrawGroupCache(
                _baseStaticGroupPlan.GroupCount);
        _instancedTextured =
            new GlTexturedMesh[_baseStaticBatches.Length];
        StaticResourceSourceBatchCount = checked(
            StaticResourceSourceBatchCount +
            _baseStaticBatches.Length);

        ReadOnlySpan<int> requiredGroups =
            _progressiveStaticMaterializationEnabled
            ? _baseStaticGroupPlan.SelectRequiredGroups(
                _visibleStaticObjects,
                _selectedStaticLodByObject)
            : _baseStaticGroupPlan.AllGroups;
        StaticResourceResolution resolution =
            MaterializeBaseStaticGroups(requiredGroups);
        if (resolution.Resolved != 0)
            StaticResourceMaterializationWaveCount++;
    }

    private void InitializeExactNormalCameraStaticResources(
        IReadOnlyList<MapRenderInstancedTexturedBatch> batches)
    {
        ArgumentNullException.ThrowIfNull(batches);
        MapRenderInstancedTexturedBatch[] retained = batches.ToArray();
        if (retained.Length == 0)
        {
            _exactNormalCameraStaticRuntime = null;
            return;
        }
        MapRenderOpenGlStaticResourceGroupPlan plan =
            MapRenderOpenGlStaticResourceGroupPlan.Create(
                retained,
                requireReceiverIdentityClosure: true);
        var runtime = new ExactNormalCameraStaticRuntime(
            retained,
            plan);
        _exactNormalCameraStaticRuntime = runtime;
        StaticResourceSourceBatchCount = checked(
            StaticResourceSourceBatchCount + retained.Length);

        ReadOnlySpan<int> requiredGroups =
            _progressiveStaticMaterializationEnabled
                ? plan.SelectRequiredGroups(
                    _visibleStaticObjects,
                    _selectedStaticLodByObject)
                : plan.AllGroups;
        try
        {
            StaticResourceResolution resolution =
                MaterializeExactStaticGroups(
                    runtime,
                    requiredGroups,
                    isReceiverVariant: false);
            if (resolution.Resolved != 0)
                StaticResourceMaterializationWaveCount++;
        }
        catch
        {
            DeleteStaticReceiverVariant(runtime);
            _exactNormalCameraStaticRuntime = null;
            throw;
        }
    }

    private static MapRenderInstancedTexturedBatch[]
        SelectExactNormalCameraStaticBatches(
            MapRenderScene scene,
            bool usesDynamicStaticLods,
            IReadOnlyDictionary<int, int> preparedLodByObject)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(preparedLodByObject);
        IEnumerable<MapRenderInstancedTexturedBatch> source =
            scene.ExactNormalCameraStaticModelTexturedBatches;
        if (usesDynamicStaticLods)
            return source.ToArray();

        return source
            .Select(batch => batch with
            {
                Instances = batch.Instances
                    .Where(instance =>
                        !preparedLodByObject.TryGetValue(
                            instance.ObjectIndex,
                            out int preparedLod) ||
                        preparedLod == batch.LodIndex)
                    .ToArray()
            })
            .Where(batch => batch.Instances.Count > 0)
            .ToArray();
    }

    private StaticResourceResolution MaterializeBaseStaticGroups(
        ReadOnlySpan<int> groupIndices)
    {
        if (_baseStaticGroupPlan is null)
            return default;

        var resolution = new StaticResourceResolution();
        foreach (int groupIndex in groupIndices)
        {
            if ((uint)groupIndex >=
                (uint)_baseStaticResolvedGroups.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(groupIndices));
            }
            if (_baseStaticResolvedGroups[groupIndex])
                continue;

            MapRenderOpenGlStaticResourceGroupPlan.Group group =
                _baseStaticGroupPlan[groupIndex];
            var created = new GlTexturedMesh[
                group.BatchOrdinals.Length];
            bool groupReady = true;
            try
            {
                for (int passIndex = 0;
                     passIndex < group.BatchOrdinals.Length;
                     passIndex++)
                {
                    int batchOrdinal =
                        group.BatchOrdinals[passIndex];
                    MapRenderInstancedTexturedBatch batch =
                        _baseStaticBatches[batchOrdinal];
                    GlTexturedMesh mesh =
                        CreateInstancedTexturedMesh(batch);
                    created[passIndex] = mesh;
                    groupReady &= mesh.IndexCount != 0 &&
                        mesh.InstanceCount != 0 &&
                        mesh.VertexArray != 0 &&
                        mesh.InstanceBuffer != 0;
                }

                if (!groupReady)
                {
                    foreach (GlTexturedMesh mesh in created)
                        DeleteTexturedMesh(mesh);
                    _baseStaticResolvedGroups[groupIndex] = true;
                    resolution = resolution.AddRejected(
                        group.BatchOrdinals.Length);
                    continue;
                }

                for (int passIndex = 0;
                     passIndex < group.BatchOrdinals.Length;
                     passIndex++)
                {
                    int batchOrdinal =
                        group.BatchOrdinals[passIndex];
                    MapRenderInstancedTexturedBatch batch =
                        _baseStaticBatches[batchOrdinal];
                    GlTexturedMesh mesh = created[passIndex];
                    _instancedTextured[batchOrdinal] = mesh;
                    RegisterStaticInstanceBufferRuntime(
                        mesh.InstanceBuffer,
                        new StaticInstanceBufferRuntime(
                            mesh,
                            batch.Instances,
                            batch.LodIndex));
                }
            }
            catch
            {
                foreach (GlTexturedMesh mesh in created)
                {
                    if (mesh.InstanceBuffer != 0)
                    {
                        RemoveStaticInstanceBufferRuntime(
                            mesh.InstanceBuffer);
                    }
                    DeleteTexturedMesh(mesh);
                }
                foreach (int batchOrdinal in group.BatchOrdinals)
                    _instancedTextured[batchOrdinal] = default;
                throw;
            }

            _baseStaticResolvedGroups[groupIndex] = true;
            _baseStaticExecutableGroups[groupIndex] = true;
            resolution = resolution.AddMaterialized(
                group.BatchOrdinals.Length);
        }

        StaticResourceResolvedBatchCount = checked(
            StaticResourceResolvedBatchCount +
            resolution.Resolved);
        StaticResourceMaterializedBatchCount = checked(
            StaticResourceMaterializedBatchCount +
            resolution.Materialized);
        StaticResourceRejectedBatchCount = checked(
            StaticResourceRejectedBatchCount +
            resolution.Rejected);
        return resolution;
    }

    private void InitializeProgressiveStaticReceiverVariants(
        MapRenderScene scene)
    {
        MapRenderSceneReceiverVariantCatalog catalog =
            scene.ReceiverVariants ??
            throw new InvalidOperationException(
                "Progressive receiver initialization requires a retained variant catalog.");
        var result = new List<StaticReceiverVariantRuntime>(4);
        var resolution = new StaticResourceResolution();
        try
        {
            foreach (MapRenderStaticModelReceiverPage page in
                     ReceiverStaticPages)
            foreach (MapRenderTechniqueVariantAllocation allocation in
                     ReceiverAllocations)
            {
                int channelIndex = result.Count;
                var key = new MapRenderStaticModelReceiverVariantKey(
                    page,
                    allocation);
                MapRenderInstancedTexturedBatch[] batches = catalog
                    .GetStaticModelBatches(page, allocation)
                    .ToArray();
                MapRenderOpenGlStaticResourceGroupPlan plan =
                    MapRenderOpenGlStaticResourceGroupPlan.Create(
                        batches,
                        requireReceiverIdentityClosure: true);
                var channel = new StaticReceiverVariantRuntime(
                    channelIndex,
                    key,
                    batches,
                    new GlTexturedMesh[batches.Length],
                    [],
                    new Dictionary<
                        MapRenderStaticModelReceiverIdentity,
                        StaticReceiverSurfaceRuntime>(),
                    plan);
                result.Add(channel);
                StaticResourceSourceBatchCount = checked(
                    StaticResourceSourceBatchCount + batches.Length);
                resolution = resolution.Add(
                    MaterializeExactStaticGroups(
                        channel,
                        plan.SelectRequiredGroups(
                            _visibleStaticObjects,
                            _selectedStaticLodByObject),
                        isReceiverVariant: true));
            }
        }
        catch
        {
            foreach (StaticReceiverVariantRuntime channel in result)
                DeleteStaticReceiverVariant(channel);
            throw;
        }

        _staticReceiverVariants = result.ToArray();
        if (resolution.Resolved != 0)
            StaticResourceMaterializationWaveCount++;
    }

    /// <summary>
    /// Pre-admits a bounded yaw/translation neighborhood while the renderer is
    /// still behind its load screen. Nearby camera turns and movement then
    /// reuse already-published immutable meshes, programs, and receiver groups
    /// instead of compiling and uploading them synchronously on an
    /// interactive frame.
    ///
    /// Draw visibility remains exact: the yaw ring affects resource residency
    /// only, and the shared visibility/LOD arrays are restored to the caller's
    /// initial camera before this method returns.
    /// </summary>
    private void PrefetchInitialStaticNeighborhood(
        ProgressiveStaticInitialView initialView)
    {
        if (_baseStaticGroupPlan is null)
            return;

        MapRenderOpenGlProgressiveStaticPrefetchPlan prefetch =
            MapRenderOpenGlProgressiveStaticPrefetchPlan.CreateYawRing(
                initialView.Camera,
                initialView.AspectRatio);
        var baseGroups = new MapRenderOpenGlStaticResourceGroupUnion(
            _baseStaticGroupPlan.GroupCount);
        MapRenderOpenGlStaticResourceGroupUnion? exactNormalGroups =
            _exactNormalCameraStaticRuntime is { } exactNormalRuntime
                ? new MapRenderOpenGlStaticResourceGroupUnion(
                    exactNormalRuntime.ResourcePlan.GroupCount)
                : null;
        var receiverGroups =
            new MapRenderOpenGlStaticResourceGroupUnion[
                _staticReceiverVariants.Length];
        for (int channelIndex = 0;
             channelIndex < _staticReceiverVariants.Length;
             channelIndex++)
        {
            receiverGroups[channelIndex] =
                new MapRenderOpenGlStaticResourceGroupUnion(
                    _staticReceiverVariants[channelIndex]
                        .ResourcePlan.GroupCount);
        }

        try
        {
            Vector3 horizontalForward = new(
                initialView.Camera.Forward.X,
                0f,
                initialView.Camera.Forward.Z);
            horizontalForward =
                horizontalForward.LengthSquared() > 0.000001f
                    ? Vector3.Normalize(horizontalForward)
                    : -Vector3.UnitZ;
            Vector3 horizontalRight = Vector3.Normalize(
                Vector3.Cross(horizontalForward, Vector3.UnitY));
            Span<Vector3> prefetchOffsets =
            [
                Vector3.Zero,
                horizontalForward *
                    (InitialStaticTranslationPrefetchDistance * 0.5f),
                -horizontalForward *
                    (InitialStaticTranslationPrefetchDistance * 0.5f),
                horizontalForward *
                    InitialStaticTranslationPrefetchDistance,
                -horizontalForward *
                    InitialStaticTranslationPrefetchDistance,
                horizontalRight *
                    (InitialStaticTranslationPrefetchDistance * 0.5f),
                -horizontalRight *
                    (InitialStaticTranslationPrefetchDistance * 0.5f),
                horizontalRight *
                    InitialStaticTranslationPrefetchDistance,
                -horizontalRight *
                    InitialStaticTranslationPrefetchDistance
            ];
            foreach (Vector3 offset in prefetchOffsets)
            {
                MapRenderOpenGlProgressiveStaticPrefetchPlan positionRing =
                    offset == Vector3.Zero
                        ? prefetch
                        : MapRenderOpenGlProgressiveStaticPrefetchPlan
                            .CreateYawRing(
                                initialView.Camera with
                                {
                                    Position =
                                        initialView.Camera.Position + offset
                                },
                                initialView.AspectRatio);
                foreach (RenderCamera camera in positionRing.YawRing)
                {
                    SelectProgressiveStaticObjects(
                        camera,
                        prefetch.AspectRatio);
                    baseGroups.Add(
                        _baseStaticGroupPlan.SelectRequiredGroups(
                            _visibleStaticObjects,
                            _selectedStaticLodByObject));
                    if (_exactNormalCameraStaticRuntime is
                            { } prefetchExactNormalChannel &&
                        exactNormalGroups is not null)
                    {
                        exactNormalGroups.Add(
                            prefetchExactNormalChannel.ResourcePlan
                                .SelectRequiredGroups(
                                    _visibleStaticObjects,
                                    _selectedStaticLodByObject));
                    }
                    for (int channelIndex = 0;
                         channelIndex < _staticReceiverVariants.Length;
                         channelIndex++)
                    {
                        StaticReceiverVariantRuntime channel =
                            _staticReceiverVariants[channelIndex];
                        receiverGroups[channelIndex].Add(
                            channel.ResourcePlan.SelectRequiredGroups(
                                _visibleStaticObjects,
                                _selectedStaticLodByObject));
                    }
                }
            }

            StaticResourceResolution resolution =
                MaterializeBaseStaticGroups(baseGroups.Groups);
            if (_exactNormalCameraStaticRuntime is
                    { } materializationExactNormalChannel &&
                exactNormalGroups is not null)
            {
                resolution = resolution.Add(
                    MaterializeExactStaticGroups(
                        materializationExactNormalChannel,
                        exactNormalGroups.Groups,
                        isReceiverVariant: false));
            }
            for (int channelIndex = 0;
                 channelIndex < _staticReceiverVariants.Length;
                 channelIndex++)
            {
                resolution = resolution.Add(
                    MaterializeExactStaticGroups(
                        _staticReceiverVariants[channelIndex],
                        receiverGroups[channelIndex].Groups,
                        isReceiverVariant: true));
            }
            if (resolution.Resolved != 0)
                StaticResourceMaterializationWaveCount++;
            Console.WriteLine(
                $"Renderer initial static neighborhood prefetch: " +
                $"views={prefetch.YawRing.Length * prefetchOffsets.Length}, " +
                $"resolved={resolution.Resolved}, " +
                $"added={resolution.Materialized}, " +
                $"rejected={resolution.Rejected}, " +
                $"materialized={StaticResourceMaterializedBatchCount}, " +
                $"deferred={StaticResourceDeferredBatchCount}.");
        }
        finally
        {
            SelectProgressiveStaticObjects(
                prefetch.InitialCamera,
                prefetch.AspectRatio);
            _baseStaticGroupPlan.SelectRequiredGroups(
                _visibleStaticObjects,
                _selectedStaticLodByObject);
            if (_exactNormalCameraStaticRuntime is
                    { } restoredExactNormalChannel)
            {
                restoredExactNormalChannel.ResourcePlan.SelectRequiredGroups(
                    _visibleStaticObjects,
                    _selectedStaticLodByObject);
            }
            foreach (StaticReceiverVariantRuntime channel in
                     _staticReceiverVariants)
            {
                channel.ResourcePlan.SelectRequiredGroups(
                    _visibleStaticObjects,
                    _selectedStaticLodByObject);
            }
        }
    }

    private StaticResourceResolution MaterializeExactStaticGroups(
        IExactStaticVariantRuntime channel,
        ReadOnlySpan<int> groupIndices,
        bool isReceiverVariant)
    {
        var resolution = new StaticResourceResolution();
        foreach (int groupIndex in groupIndices)
        {
            if ((uint)groupIndex >=
                (uint)channel.ResolvedGroups.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(groupIndices));
            }
            if (channel.ResolvedGroups[groupIndex])
                continue;

            MapRenderOpenGlStaticResourceGroupPlan.Group group =
                channel.ResourcePlan[groupIndex];
            if (!group.ReceiverStructureReady)
            {
                channel.ResolvedGroups[groupIndex] = true;
                resolution = resolution.AddRejected(
                    group.BatchOrdinals.Length);
                continue;
            }

            MapRenderInstancedTexturedBatch[] groupBatches =
                group.BatchOrdinals
                    .Select(ordinal => channel.Batches[ordinal])
                    .ToArray();
            IReadOnlySet<AuthoredProgramGroupKey> authorized =
                AuthorizeAtomicProgramGroups(
                    groupBatches,
                    _ => true,
                    AuthoredProgramGroup,
                    PreflightAuthoredProgram);
            var created = new GlTexturedMesh[
                group.BatchOrdinals.Length];
            bool groupReady = true;
            try
            {
                for (int passIndex = 0;
                     passIndex < group.BatchOrdinals.Length;
                     passIndex++)
                {
                    GlTexturedMesh mesh =
                        CreateInstancedTexturedMesh(
                            groupBatches[passIndex],
                            authorized,
                            allowGenericFallback: false);
                    created[passIndex] = mesh;
                    if (mesh.IndexCount == 0 ||
                        mesh.RsxProgram.Handle == 0 ||
                        mesh.InstanceBuffer == 0 ||
                        (mesh.EditorDepthPrepass is not null &&
                         mesh.DepthPrepassRsxProgram.Handle == 0))
                    {
                        groupReady = false;
                    }
                }

                if (!groupReady)
                {
                    foreach (GlTexturedMesh mesh in created)
                        DeleteTexturedMesh(mesh);
                    channel.ResolvedGroups[groupIndex] = true;
                    resolution = resolution.AddRejected(
                        group.BatchOrdinals.Length);
                    continue;
                }

                var runtimes =
                    new StaticInstanceBufferRuntime[created.Length];
                for (int passIndex = 0;
                     passIndex < created.Length;
                     passIndex++)
                {
                    MapRenderInstancedTexturedBatch batch =
                        groupBatches[passIndex];
                    GlTexturedMesh mesh = created[passIndex];
                    var runtime = new StaticInstanceBufferRuntime(
                        mesh,
                        batch.Instances,
                        batch.LodIndex,
                        isReceiverVariant: isReceiverVariant,
                        isExactNormalCameraVariant:
                            !isReceiverVariant);
                    runtimes[passIndex] = runtime;
                    RegisterStaticInstanceBufferRuntime(
                        mesh.InstanceBuffer,
                        runtime);
                    channel.Meshes[
                        group.BatchOrdinals[passIndex]] = mesh;
                }

                for (int instanceIndex = 0;
                     instanceIndex <
                     group.ReceiverIdentities.Length;
                     instanceIndex++)
                {
                    var occurrences =
                        new StaticReceiverPassOccurrence[
                            runtimes.Length];
                    for (int passIndex = 0;
                         passIndex < runtimes.Length;
                         passIndex++)
                    {
                        occurrences[passIndex] = new(
                            runtimes[passIndex],
                            instanceIndex);
                    }

                    channel.Surfaces.Add(
                        group.ReceiverIdentities[instanceIndex],
                        new StaticReceiverSurfaceRuntime(
                            group.ReceiverIdentities[instanceIndex],
                            groupBatches[0].Pass.TechniquePass.TechniqueSlot,
                            occurrences));
                }
                resolution = resolution.AddMaterialized(
                    group.BatchOrdinals.Length);
                channel.ResolvedGroups[groupIndex] = true;
                channel.ExecutableGroups[groupIndex] = true;
            }
            catch
            {
                foreach (GlTexturedMesh mesh in created)
                {
                    if (mesh.InstanceBuffer != 0)
                    {
                        RemoveStaticInstanceBufferRuntime(
                            mesh.InstanceBuffer);
                    }
                    DeleteTexturedMesh(mesh);
                }
                foreach (int batchOrdinal in group.BatchOrdinals)
                    channel.Meshes[batchOrdinal] = default;
                foreach (MapRenderStaticModelReceiverIdentity identity in
                         group.ReceiverIdentities)
                {
                    channel.Surfaces.Remove(identity);
                }
                channel.ResolvedGroups[groupIndex] = false;
                channel.ExecutableGroups[groupIndex] = false;
                throw;
            }
        }

        StaticResourceResolvedBatchCount = checked(
            StaticResourceResolvedBatchCount +
            resolution.Resolved);
        StaticResourceMaterializedBatchCount = checked(
            StaticResourceMaterializedBatchCount +
            resolution.Materialized);
        StaticResourceRejectedBatchCount = checked(
            StaticResourceRejectedBatchCount +
            resolution.Rejected);
        return resolution;
    }

    private void EnsureProgressiveStaticResources(
        RenderCamera camera)
    {
        if (!_progressiveStaticMaterializationEnabled)
            return;
        if (StaticResourceDeferredBatchCount == 0)
        {
            PublishPendingProgressiveStaticDrawGroups();
            return;
        }

        float aspectRatio = (float)_width / _height;
        bool selectionChanged =
            _lastProgressiveStaticCamera != camera ||
            _lastProgressiveStaticAspectRatio != aspectRatio;
        if (selectionChanged)
        {
            if (_preparedStaticSelectionVisibility is not
                    { } preparedVisibility ||
                !TryUsePreparedStaticSelection(
                    camera,
                    preparedVisibility))
            {
                SelectProgressiveStaticObjects(
                    camera,
                    aspectRatio);
            }
            else
            {
                // BeginSunShadowDpvsPreparation already copied the immutable
                // packet's exact DPVS/LOD selection into the live buffers.
                // Resource admission must consume that selection without
                // replacing it with a frustum-only selection for the same
                // presentation camera.
                _lastProgressiveStaticCamera = camera;
                _lastProgressiveStaticAspectRatio = aspectRatio;
            }
        }

        ReadOnlySpan<int> selectedBaseGroups =
            _baseStaticGroupPlan is { } basePlan
                ? selectionChanged
                    ? basePlan.SelectRequiredGroups(
                        _visibleStaticObjects,
                        _selectedStaticLodByObject)
                    : basePlan.SelectedGroups
                : [];
        if (selectionChanged &&
            _exactNormalCameraStaticRuntime is
                { } exactNormalCameraChannel)
        {
            exactNormalCameraChannel.ResourcePlan.SelectRequiredGroups(
                _visibleStaticObjects,
                _selectedStaticLodByObject);
        }
        foreach (StaticReceiverVariantRuntime channel in
                 _staticReceiverVariants)
        {
            if (selectionChanged)
            {
                channel.ResourcePlan.SelectRequiredGroups(
                    _visibleStaticObjects,
                    _selectedStaticLodByObject);
            }
        }
        if (!HasPendingProgressiveStaticGroups(
                selectedBaseGroups))
        {
            PublishPendingProgressiveStaticDrawGroups();
            return;
        }

        IDisposable? waveShaderObjectCache =
            _activeLoadShaderObjectCache is null
                ? BeginLoadShaderObjectCache()
                : null;
        try
        {
            long materializationStarted =
                System.Diagnostics.Stopwatch.GetTimestamp();
            StaticResourceResolution resolution =
                MaterializeSelectedProgressiveStaticGroups(
                    selectedBaseGroups,
                    materializationStarted,
                    out int admittedGroupCount,
                    out bool receiverGroupMaterialized);
            long materializationFinished =
                System.Diagnostics.Stopwatch.GetTimestamp();

            if (admittedGroupCount == 0)
                return;
            StaticResourceMaterializationWaveCount++;
            if (resolution.Materialized != 0)
            {
                _progressiveStaticUnpublishedBatchCount = checked(
                    _progressiveStaticUnpublishedBatchCount +
                    resolution.Materialized);
            }
            if (_progressiveStaticUnpublishedBatchCount != 0)
            {
                _progressiveStaticUnpublishedAdmissionFrameCount =
                    checked(
                        _progressiveStaticUnpublishedAdmissionFrameCount +
                        1);
            }

            bool selectedGroupsRemainPending =
                HasPendingProgressiveStaticGroups(
                    selectedBaseGroups);
            long drawGroupRebuildStarted =
                System.Diagnostics.Stopwatch.GetTimestamp();
            bool published =
                _progressiveStaticUnpublishedBatchCount != 0 &&
                (_progressiveStaticUnpublishedBatchCount >=
                    ProgressiveStaticPublicationMaximumDeferredBatches ||
                 _progressiveStaticUnpublishedAdmissionFrameCount >=
                    ProgressiveStaticPublicationMaximumAdmissionFrames ||
                 receiverGroupMaterialized ||
                 !selectedGroupsRemainPending ||
                 StaticResourceDeferredBatchCount == 0) &&
                PublishPendingProgressiveStaticDrawGroups();
            long drawGroupRebuildFinished =
                System.Diagnostics.Stopwatch.GetTimestamp();
            // Materialization uses direct resource-setup calls and therefore
            // invalidates the frame-to-frame binding assumptions held by the
            // state shadow. Publish one exact baseline before any new group
            // can be selected or drawn.
            EstablishStateShadowBaseline();
            if (StaticResourceMaterializationWaveCount <= 5 ||
                StaticResourceMaterializationWaveCount % 60 == 0 ||
                StaticResourceDeferredBatchCount == 0)
            {
                Console.WriteLine(
                    $"Renderer progressive static materialization: " +
                    $"wave={StaticResourceMaterializationWaveCount}, " +
                    $"groups={admittedGroupCount}, " +
                    $"resolved={resolution.Resolved}, " +
                    $"added={resolution.Materialized}, " +
                    $"rejected={resolution.Rejected}, " +
                    $"published={published}, " +
                    $"queuedPublicationBatches={_progressiveStaticUnpublishedBatchCount}, " +
                    $"materialized={StaticResourceMaterializedBatchCount}, " +
                    $"deferred={StaticResourceDeferredBatchCount}, " +
                    $"resourceMs={System.Diagnostics.Stopwatch.GetElapsedTime(materializationStarted, materializationFinished).TotalMilliseconds:0.0}, " +
                    $"drawGroupsMs={System.Diagnostics.Stopwatch.GetElapsedTime(drawGroupRebuildStarted, drawGroupRebuildFinished).TotalMilliseconds:0.0}.");
            }
        }
        finally
        {
            waveShaderObjectCache?.Dispose();
        }
    }

    private bool HasPendingProgressiveStaticGroups(
        ReadOnlySpan<int> selectedBaseGroups)
    {
        if (HasPendingGroups(
                selectedBaseGroups,
                _baseStaticResolvedGroups))
        {
            return true;
        }

        if (_exactNormalCameraStaticRuntime is
                { } exactNormalCameraChannel &&
            HasPendingGroups(
                exactNormalCameraChannel.ResourcePlan.SelectedGroups,
                exactNormalCameraChannel.ResolvedGroups))
        {
            return true;
        }

        foreach (StaticReceiverVariantRuntime channel in
                 _staticReceiverVariants)
        {
            if (HasPendingGroups(
                    channel.ResourcePlan.SelectedGroups,
                    channel.ResolvedGroups))
            {
                return true;
            }
        }

        return false;
    }

    private bool PublishPendingProgressiveStaticDrawGroups()
    {
        if (_progressiveStaticUnpublishedBatchCount == 0)
            return false;

        RebuildEditorStaticDrawGroups(
            _renderSceneSnapshot ??
            throw new InvalidOperationException(
                "Progressive static resources require a retained scene snapshot."),
            _loadedIsolatedWorldSurfaceIndex.HasValue);
        _progressiveStaticUnpublishedBatchCount = 0;
        _progressiveStaticUnpublishedAdmissionFrameCount = 0;
        return true;
    }

    /// <summary>
    /// Admits atomic resource groups round-robin across base color and exact
    /// receiver lanes. Native streaming performs bounded work at frame
    /// boundaries; this host equivalent keeps OpenGL ownership on the render
    /// thread while preventing one camera turn from draining every newly
    /// visible group in a single frame.
    ///
    /// The time limit is deliberately soft because a material/program group
    /// is the minimum correctness unit and cannot be interrupted halfway.
    /// </summary>
    private StaticResourceResolution
        MaterializeSelectedProgressiveStaticGroups(
            ReadOnlySpan<int> selectedBaseGroups,
            long startedTimestamp,
            out int admittedGroupCount,
            out bool receiverGroupMaterialized)
    {
        bool hasExactNormalCameraLane =
            _exactNormalCameraStaticRuntime is not null;
        int receiverLaneOffset =
            hasExactNormalCameraLane ? 2 : 1;
        int laneCount = checked(
            receiverLaneOffset + _staticReceiverVariants.Length);
        var resolution = new StaticResourceResolution();
        admittedGroupCount = 0;
        receiverGroupMaterialized = false;
        int consecutiveEmptyLanes = 0;
        while (admittedGroupCount <
                   ProgressiveStaticAdmissionMaximumGroupsPerFrame &&
               consecutiveEmptyLanes < laneCount)
        {
            int laneIndex = _progressiveStaticAdmissionLaneCursor;
            _progressiveStaticAdmissionLaneCursor =
                (laneIndex + 1) % laneCount;

            bool admitted;
            StaticResourceResolution laneResolution;
            if (laneIndex == 0)
            {
                admitted = TryMaterializeNextBaseStaticGroup(
                    selectedBaseGroups,
                    out laneResolution);
            }
            else if (hasExactNormalCameraLane &&
                     laneIndex == 1)
            {
                admitted = TryMaterializeNextExactStaticGroup(
                    _exactNormalCameraStaticRuntime!,
                    isReceiverVariant: false,
                    out laneResolution);
            }
            else
            {
                StaticReceiverVariantRuntime channel =
                    _staticReceiverVariants[
                        laneIndex - receiverLaneOffset];
                admitted = TryMaterializeNextExactStaticGroup(
                    channel,
                    isReceiverVariant: true,
                    out laneResolution);
            }

            if (!admitted)
            {
                consecutiveEmptyLanes++;
                continue;
            }

            resolution = resolution.Add(laneResolution);
            receiverGroupMaterialized |=
                laneIndex != 0 &&
                laneResolution.Materialized != 0;
            admittedGroupCount++;
            consecutiveEmptyLanes = 0;
            if (System.Diagnostics.Stopwatch.GetElapsedTime(
                    startedTimestamp).TotalMilliseconds >=
                ProgressiveStaticAdmissionBudgetMilliseconds)
            {
                break;
            }
        }

        return resolution;
    }

    private bool TryMaterializeNextBaseStaticGroup(
        ReadOnlySpan<int> selectedGroups,
        out StaticResourceResolution resolution)
    {
        foreach (int groupIndex in selectedGroups)
        {
            if (_baseStaticResolvedGroups[groupIndex])
                continue;

            Span<int> oneGroup = stackalloc int[1];
            oneGroup[0] = groupIndex;
            resolution = MaterializeBaseStaticGroups(oneGroup);
            return true;
        }

        resolution = default;
        return false;
    }

    private bool TryMaterializeNextExactStaticGroup(
        IExactStaticVariantRuntime channel,
        bool isReceiverVariant,
        out StaticResourceResolution resolution)
    {
        foreach (int groupIndex in channel.ResourcePlan.SelectedGroups)
        {
            if (channel.ResolvedGroups[groupIndex])
                continue;

            Span<int> oneGroup = stackalloc int[1];
            oneGroup[0] = groupIndex;
            resolution = MaterializeExactStaticGroups(
                channel,
                oneGroup,
                isReceiverVariant);
            return true;
        }

        resolution = default;
        return false;
    }

    private static bool HasPendingGroups(
        ReadOnlySpan<int> selectedGroups,
        ReadOnlySpan<bool> resolvedGroups)
    {
        foreach (int groupIndex in selectedGroups)
        {
            if ((uint)groupIndex >= (uint)resolvedGroups.Length)
            {
                throw new InvalidOperationException(
                    "Static selection references an unknown resource group.");
            }
            if (!resolvedGroups[groupIndex])
                return true;
        }
        return false;
    }

    private bool IsProgressiveStaticIdentityRequired(
        MapRenderStaticModelReceiverIdentity identity) =>
        !_progressiveStaticMaterializationEnabled ||
        MapRenderOpenGlStaticResourceGroupPlan.IsSelected(
            identity.ObjectIndex,
            identity.LodIndex,
            _visibleStaticObjects,
            _selectedStaticLodByObject);

    private void RebuildEditorStaticDrawGroups(
        RenderSceneSnapshot sceneSnapshot,
        bool isolateWorldSurface)
    {
        if (_progressiveStaticMaterializationEnabled)
        {
            RebuildProgressiveEditorStaticDrawGroups();
        }
        else
        {
            (
                MapRenderInstancedTexturedBatch[] baseDrawBatches,
                GlTexturedMesh[] baseDrawMeshes) =
                    SelectMaterializedStaticDrawInputs(
                        _baseStaticGroupPlan,
                        _baseStaticExecutableGroups,
                        _baseStaticBatches,
                        _instancedTextured);
            MapRenderEditorDrawGroup<GlTexturedDrawCommand>[]
                legacyDrawGroups =
                    BuildEditorTexturedDrawGroups(
                        _renderedWorldBatches,
                        _textured,
                        baseDrawBatches,
                        baseDrawMeshes);
            _editorTexturedDrawGroups = legacyDrawGroups;
            if (sceneSnapshot.NormalCameraDraws.SourceCount != 0 &&
                MapRenderOpenGlNormalCameraDrawAdapter.TryCreate(
                    sceneSnapshot.NormalCameraDraws,
                    _textured,
                    _instancedTextured,
                    legacyDrawGroups,
                    isolatedWorldSurfaceActive: isolateWorldSurface,
                    dynamicStaticLodActive: _usesDynamicStaticLods,
                    dpvsSourceActive: _previewWorldSource is not null,
                    out MapRenderOpenGlNormalCameraDrawAdapter? drawAdapter,
                    out _) &&
                drawAdapter is not null)
            {
                _editorTexturedDrawGroups = drawAdapter.SourceGroups;
            }

            if (_exactNormalCameraStaticRuntime is
                    { } exactNormalCameraChannel)
            {
                (
                    MapRenderInstancedTexturedBatch[] exactDrawBatches,
                    GlTexturedMesh[] exactDrawMeshes) =
                        SelectMaterializedStaticDrawInputs(
                            exactNormalCameraChannel.ResourcePlan,
                            exactNormalCameraChannel.ExecutableGroups,
                            exactNormalCameraChannel.Batches,
                            exactNormalCameraChannel.Meshes);
                exactNormalCameraChannel.DrawGroups =
                    BuildEditorTexturedDrawGroups(
                        [],
                        [],
                        exactDrawBatches,
                        exactDrawMeshes);
                _editorTexturedDrawGroups =
                    _editorTexturedDrawGroups
                        .Concat(exactNormalCameraChannel.DrawGroups)
                        .ToArray();
            }

            foreach (StaticReceiverVariantRuntime channel in
                     _staticReceiverVariants)
            {
                (
                    MapRenderInstancedTexturedBatch[] channelDrawBatches,
                    GlTexturedMesh[] channelDrawMeshes) =
                        SelectMaterializedStaticDrawInputs(
                            channel.ResourcePlan,
                            channel.ExecutableGroups,
                            channel.Batches,
                            channel.Meshes);
                channel.DrawGroups = BuildEditorTexturedDrawGroups(
                    [],
                    [],
                    channelDrawBatches,
                    channelDrawMeshes);
            }
        }

        _receiverAwareEditorTexturedDrawGroups =
            BuildReceiverAwareEditorTexturedDrawGroups(
                _editorTexturedDrawGroups);
        _editorDepthPrepassDrawGroups =
            BuildDepthPrepassDrawGroupOrder(
                _editorTexturedDrawGroups);
        _receiverAwareEditorDepthPrepassDrawGroups =
            BuildDepthPrepassDrawGroupOrder(
                _receiverAwareEditorTexturedDrawGroups);

        foreach (StaticInstanceBufferRuntime runtime in
                 _staticInstanceBuffers.Values)
        {
            runtime.ResetDrawShape();
        }
        foreach (MapRenderEditorDrawGroup<GlTexturedDrawCommand> group in
                 _receiverAwareEditorTexturedDrawGroups)
        foreach (GlTexturedDrawCommand command in group.AuthoredPasses)
        {
            if (command.Mesh.InstanceBuffer == 0 ||
                !_staticInstanceBuffers.TryGetValue(
                    command.Mesh.InstanceBuffer,
                    out StaticInstanceBufferRuntime? runtime))
            {
                continue;
            }
            if (command.InstanceIndex.HasValue)
                runtime.HasIsolatedDraw = true;
            else
                runtime.HasWholeBatchDraw = true;
        }
        _staticInstanceCompactionFullInvalidationPending = true;
    }

    private void RebuildProgressiveEditorStaticDrawGroups()
    {
        _progressiveWorldDrawGroups ??=
            BuildEditorTexturedDrawGroups(
                _renderedWorldBatches,
                _textured,
                [],
                []);

        MapRenderOpenGlStaticResourceGroupPlan basePlan =
            _baseStaticGroupPlan ??
            throw new InvalidOperationException(
                "Progressive static draw groups require an immutable base resource plan.");
        MapRenderOpenGlProgressiveStaticDrawGroupCache baseCache =
            _baseStaticDrawGroupCache ??
            throw new InvalidOperationException(
                "Progressive static draw groups require a base draw-group cache.");
        CacheExecutableStaticDrawGroups(
            basePlan,
            _baseStaticExecutableGroups,
            _baseStaticBatches,
            _instancedTextured,
            baseCache);
        MapRenderEditorDrawGroup<GlTexturedDrawCommand>[] baseGroups =
            baseCache.Publish(
            _progressiveWorldDrawGroups,
            _baseStaticExecutableGroups,
            _renderedWorldBatches.Length);
        _editorTexturedDrawGroups = baseGroups;

        if (_exactNormalCameraStaticRuntime is
                { } exactNormalCameraChannel)
        {
            CacheExecutableStaticDrawGroups(
                exactNormalCameraChannel.ResourcePlan,
                exactNormalCameraChannel.ExecutableGroups,
                exactNormalCameraChannel.Batches,
                exactNormalCameraChannel.Meshes,
                exactNormalCameraChannel.DrawGroupCache);
            exactNormalCameraChannel.DrawGroups =
                exactNormalCameraChannel.DrawGroupCache.Publish(
                    [],
                    exactNormalCameraChannel.ExecutableGroups,
                    firstStaticSourceOrdinal: 0);
            _editorTexturedDrawGroups = baseGroups
                .Concat(exactNormalCameraChannel.DrawGroups)
                .ToArray();
        }

        foreach (StaticReceiverVariantRuntime channel in
                 _staticReceiverVariants)
        {
            CacheExecutableStaticDrawGroups(
                channel.ResourcePlan,
                channel.ExecutableGroups,
                channel.Batches,
                channel.Meshes,
                channel.DrawGroupCache);
            channel.DrawGroups = channel.DrawGroupCache.Publish(
                [],
                channel.ExecutableGroups,
                firstStaticSourceOrdinal: 0);
        }
    }

    private void CacheExecutableStaticDrawGroups(
        MapRenderOpenGlStaticResourceGroupPlan plan,
        ReadOnlySpan<bool> executableGroups,
        IReadOnlyList<MapRenderInstancedTexturedBatch> batches,
        IReadOnlyList<GlTexturedMesh> meshes,
        MapRenderOpenGlProgressiveStaticDrawGroupCache cache)
    {
        if (executableGroups.Length != plan.GroupCount ||
            cache.ResourceGroupCount != plan.GroupCount ||
            batches.Count != meshes.Count)
        {
            throw new InvalidOperationException(
                "Progressive static draw-group inputs no longer share one immutable resource-group space.");
        }

        for (int groupIndex = 0;
             groupIndex < executableGroups.Length;
             groupIndex++)
        {
            if (!executableGroups[groupIndex] ||
                cache.IsCreated(groupIndex))
            {
                continue;
            }

            int[] batchOrdinals = plan[groupIndex].BatchOrdinals;
            var groupBatches =
                new MapRenderInstancedTexturedBatch[
                    batchOrdinals.Length];
            var groupMeshes =
                new GlTexturedMesh[batchOrdinals.Length];
            for (int passIndex = 0;
                 passIndex < batchOrdinals.Length;
                 passIndex++)
            {
                int batchOrdinal = batchOrdinals[passIndex];
                groupBatches[passIndex] = batches[batchOrdinal];
                groupMeshes[passIndex] = meshes[batchOrdinal];
            }

            cache.Store(
                groupIndex,
                BuildEditorTexturedDrawGroups(
                    [],
                    [],
                    groupBatches,
                    groupMeshes));
        }
    }

    private static (
        MapRenderInstancedTexturedBatch[] Batches,
        GlTexturedMesh[] Meshes)
        SelectMaterializedStaticDrawInputs(
            MapRenderOpenGlStaticResourceGroupPlan? plan,
            IReadOnlyList<bool> materializedGroups,
            IReadOnlyList<MapRenderInstancedTexturedBatch> batches,
            IReadOnlyList<GlTexturedMesh> meshes)
    {
        if (plan is null || batches.Count == 0)
            return ([], []);
        if (batches.Count != meshes.Count ||
            materializedGroups.Count != plan.GroupCount)
        {
            throw new InvalidOperationException(
                "Progressive static draw inputs no longer match their immutable resource plan.");
        }

        int[] ordinals = Enumerable.Range(0, plan.GroupCount)
            .Where(groupIndex => materializedGroups[groupIndex])
            .SelectMany(groupIndex =>
                plan[groupIndex].BatchOrdinals)
            .Order()
            .ToArray();
        return (
            ordinals.Select(ordinal => batches[ordinal]).ToArray(),
            ordinals.Select(ordinal => meshes[ordinal]).ToArray());
    }

    private readonly record struct ProgressiveStaticInitialView(
        RenderCamera Camera,
        float AspectRatio);

    private readonly record struct StaticResourceResolution(
        int Resolved,
        int Materialized,
        int Rejected)
    {
        public StaticResourceResolution Add(
            StaticResourceResolution other) =>
            new(
                checked(Resolved + other.Resolved),
                checked(Materialized + other.Materialized),
                checked(Rejected + other.Rejected));

        public StaticResourceResolution AddMaterialized(
            int batchCount) =>
            new(
                checked(Resolved + batchCount),
                checked(Materialized + batchCount),
                Rejected);

        public StaticResourceResolution AddRejected(
            int batchCount) =>
            new(
                checked(Resolved + batchCount),
                Materialized,
                checked(Rejected + batchCount));
    }
}
