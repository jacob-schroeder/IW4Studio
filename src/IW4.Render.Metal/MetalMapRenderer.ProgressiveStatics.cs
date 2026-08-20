using System.Diagnostics;
using System.Runtime.Versioning;

using IW4.Render.EditorPreview;
using IW4.Render.Resources;
using IW4.Render.Scheduling;
using IW4.Render.Scheduling.FramePlans;
using IW4.Render.Scheduling.StaticModels;
using IW4.Render.Visibility;

namespace IW4.Render.Metal;

[SupportedOSPlatform("macos")]
public sealed partial class MetalMapRenderer
{
    private const int ProgressiveStaticAdmissionMaximumGroupsPerFrame = 4;
    private const double ProgressiveStaticAdmissionBudgetMilliseconds = 2d;
    private const int ProgressiveStaticAdmissionLaneCount = 6;
    private const int ProgressiveStaticReceiverAdmissionLaneOffset = 2;

    private readonly HashSet<MapRenderEditorDrawGroup<
        RenderNormalCameraDrawSubmissionSnapshot>>
        _publishedProgressiveStaticGroups = new(
            ReferenceEqualityComparer.Instance);
    private readonly List<MapRenderEditorDrawGroup<
        RenderNormalCameraDrawSubmissionSnapshot>>
        _progressiveStaticAdmissionScratch = [];
    private readonly List<RenderSemanticIdentity>
        _progressiveStaticGeometryScratch = [];
    private readonly List<RenderSemanticIdentity>
        _progressiveStaticInstanceScratch = [];
    private MapRenderEditorDrawGroup<
        RenderNormalCameraDrawSubmissionSnapshot>[]
        _progressiveStaticGroups = [];
    private MapRenderEditorDrawGroup<
        RenderNormalCameraDrawSubmissionSnapshot>[][]
        _progressiveStaticAdmissionLanes = [];
    private int _progressiveStaticAdmissionLaneCursor;
    private RenderCamera? _progressiveStaticAdmissionCamera;
    private int _progressiveStaticPendingGroupCount;
    private long _publishedProgressiveStaticBatchCount;

    public long StaticResourceSourceBatchCount { get; private set; }

    public long StaticResourceResolvedBatchCount =>
        checked(
            _publishedProgressiveStaticBatchCount +
            StaticResourceRejectedBatchCount);

    public long StaticResourceMaterializedBatchCount =>
        _publishedProgressiveStaticBatchCount;

    public long StaticResourceRejectedBatchCount { get; private set; }

    public long StaticResourceDeferredBatchCount =>
        Math.Max(
            0,
            StaticResourceSourceBatchCount -
                StaticResourceResolvedBatchCount);

    public long StaticResourceMaterializationWaveCount { get; private set; }

    private void ConfigureProgressiveStaticResourceOwnership(
        RenderSceneSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var geometries = new HashSet<RenderSemanticIdentity>();
        var worldGeometries = new HashSet<RenderSemanticIdentity>();
        var instances = new HashSet<RenderSemanticIdentity>();
        foreach (RenderNormalCameraPreparedPassSnapshot pass in
                 snapshot.NormalCameraDraws.PreparedPasses)
        {
            if (pass.SourceKind ==
                RenderNormalCameraDrawSourceKind.World)
            {
                worldGeometries.Add(pass.Geometry.Identity);
                continue;
            }
            geometries.Add(pass.Geometry.Identity);
            if (pass.Instances is { } instanceResource)
                instances.Add(instanceResource.Identity);
        }
        geometries.ExceptWith(worldGeometries);
        _resources.ConfigureDeferredStaticResources(
            geometries,
            instances);
    }

    private void InitializeProgressiveStaticAdmission(
        RenderSceneSnapshot snapshot,
        bool progressiveResourceResidency)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ResetProgressiveStaticAdmission();
        ResetNormalCameraTextureResidency();
        if (_loadedIsolatedWorldSurfaceIndex.HasValue)
        {
            // Isolated-world presentation has no static-model lane. Retain
            // the deferred static shells so none of their native payloads are
            // materialized for a view that cannot execute them.
            return;
        }
        MapRenderEditorDrawGroup<
            RenderNormalCameraDrawSubmissionSnapshot>[] allStaticGroups =
                snapshot.NormalCameraDraws.DrawGroups
                    .Where(group => group.AuthoredPassSpan[0]
                        .PreparedPass.SourceKind ==
                        RenderNormalCameraDrawSourceKind.StaticModel)
                    .ToArray();
        for (int groupIndex = 0;
             groupIndex < allStaticGroups.Length;
             groupIndex++)
        {
            MapRenderEditorDrawGroup<
                RenderNormalCameraDrawSubmissionSnapshot> group =
                    allStaticGroups[groupIndex];
            int batchCount = group.AuthoredPassSpan.Length;
            StaticResourceSourceBatchCount = checked(
                StaticResourceSourceBatchCount + batchCount);
            if (!_normalCameraAuthorizedGroups.Contains(group))
            {
                StaticResourceRejectedBatchCount = checked(
                    StaticResourceRejectedBatchCount + batchCount);
            }
        }
        _progressiveStaticGroups = allStaticGroups
            .Where(group => _normalCameraAuthorizedGroups.Contains(group))
            .OrderBy(group => group.SourceOrdinal)
            .ToArray();
        if (!progressiveResourceResidency)
        {
            _progressiveStaticAdmissionScratch.AddRange(
                _progressiveStaticGroups);
            PublishProgressiveStaticGroups(
                _progressiveStaticAdmissionScratch,
                waitForCompletion: true);
            _progressiveStaticPendingGroupCount = 0;
            if (_publishedProgressiveStaticGroups.Count != 0)
                StaticResourceMaterializationWaveCount = 1;
            InvalidateNormalCameraReceiverSelection();
            return;
        }
        CreateProgressiveStaticAdmissionLanes();
    }

    private void PrefetchInitialProgressiveStaticNeighborhood(
        RenderCamera initialCamera,
        float initialAspectRatio)
    {
        if (_progressiveStaticGroups.Length == 0)
        {
            _progressiveStaticPendingGroupCount = 0;
            _progressiveStaticAdmissionCamera = initialCamera;
            return;
        }

        MapRenderProgressiveStaticPrefetchPlan prefetch =
            MapRenderProgressiveStaticPrefetchPlan.CreateNeighborhood(
            initialCamera,
            initialAspectRatio);
        var requiredGroups = new HashSet<MapRenderEditorDrawGroup<
            RenderNormalCameraDrawSubmissionSnapshot>>(
                ReferenceEqualityComparer.Instance);
        var initialGroups = new HashSet<MapRenderEditorDrawGroup<
            RenderNormalCameraDrawSubmissionSnapshot>>(
                ReferenceEqualityComparer.Instance);
        try
        {
            ReadOnlySpan<RenderCamera> prefetchCameras = prefetch.Cameras;
            for (int cameraIndex = 0;
                 cameraIndex < prefetchCameras.Length;
                 cameraIndex++)
            {
                SelectProgressiveStaticObjectsForPrefetch(
                    prefetchCameras[cameraIndex],
                    prefetch.AspectRatio);
                for (int groupIndex = 0;
                     groupIndex < _progressiveStaticGroups.Length;
                     groupIndex++)
                {
                    MapRenderEditorDrawGroup<
                        RenderNormalCameraDrawSubmissionSnapshot> group =
                            _progressiveStaticGroups[groupIndex];
                    if (IsProgressiveStaticGroupRequired(group))
                    {
                        requiredGroups.Add(group);
                        if (cameraIndex == 0)
                            initialGroups.Add(group);
                    }
                }
            }

            _progressiveStaticAdmissionScratch.Clear();
            for (int groupIndex = 0;
                 groupIndex < _progressiveStaticGroups.Length;
                 groupIndex++)
            {
                MapRenderEditorDrawGroup<
                    RenderNormalCameraDrawSubmissionSnapshot> group =
                        _progressiveStaticGroups[groupIndex];
                if (requiredGroups.Contains(group))
                    _progressiveStaticAdmissionScratch.Add(group);
            }
            PublishProgressiveStaticGroups(
                _progressiveStaticAdmissionScratch,
                waitForCompletion: true);
            PrefetchInitialNormalCameraTextureResidency(
                initialGroups,
                _progressiveStaticAdmissionScratch);
            if (_progressiveStaticAdmissionScratch.Count != 0)
                StaticResourceMaterializationWaveCount++;
        }
        finally
        {
            SelectProgressiveStaticObjectsForPrefetch(
                prefetch.InitialCamera,
                prefetch.AspectRatio);
            _progressiveStaticPendingGroupCount =
                CountRequiredUnpublishedProgressiveStaticGroups();
            _progressiveStaticAdmissionCamera = initialCamera;
            InvalidateNormalCameraReceiverSelection();
        }
    }

    private void SelectProgressiveStaticObjectsForPrefetch(
        RenderCamera camera,
        float aspectRatio)
    {
        MapRenderCameraFrustum.BuildPlanes(
            camera,
            aspectRatio,
            _normalCameraVisibilityFrustumPlanes);
        _normalCameraCurrentDpvsVisibility = null;
        _normalCameraVisibilityFrustumValid = true;
        Array.Copy(
            _normalCameraStaticFallbackLod,
            _normalCameraStaticSelectedLod,
            _normalCameraStaticFallbackLod.Length);
        _normalCameraStaticVisible.AsSpan().Fill(true);
        int visibleScheduledObjectCount =
            MapRenderStaticModelLodSelector.SelectFrame(
                _normalCameraStaticScheduling,
                camera,
                _normalCameraVisibilityFrustumPlanes,
                cameraVisibility: null,
                _normalCameraStaticVisible,
                _normalCameraStaticSelectedLod,
                viewDistanceScale: 1f,
                nearViewScale: 1f,
                farViewScale: 1f);
        _normalCameraStaticVisibleObjectCount = checked(
            visibleScheduledObjectCount +
            _normalCameraStaticAlwaysVisibleCount);
        _normalCameraStaticSelectionValid = true;
        _hasNormalCameraStaticSelectionCache = false;
        _normalCameraStaticSelectionKey = default;
        _normalCameraStaticSelectionDpvs = null;
    }

    private void PrepareProgressiveStaticAdmission(RenderCamera camera)
    {
        if (_progressiveStaticGroups.Length == 0)
        {
            _progressiveStaticPendingGroupCount = 0;
            _progressiveStaticAdmissionCamera = camera;
            return;
        }

        long started = Stopwatch.GetTimestamp();
        int admitted = 0;
        int consecutiveEmptyLanes = 0;
        _progressiveStaticAdmissionScratch.Clear();
        while (admitted <
                   ProgressiveStaticAdmissionMaximumGroupsPerFrame &&
               consecutiveEmptyLanes <
                   _progressiveStaticAdmissionLanes.Length &&
               (admitted == 0 ||
                Stopwatch.GetElapsedTime(started).TotalMilliseconds <
                    ProgressiveStaticAdmissionBudgetMilliseconds))
        {
            int laneIndex = _progressiveStaticAdmissionLaneCursor;
            _progressiveStaticAdmissionLaneCursor =
                (laneIndex + 1) %
                _progressiveStaticAdmissionLanes.Length;
            if (!TrySelectNextProgressiveStaticAdmissionGroup(
                    laneIndex,
                    out MapRenderEditorDrawGroup<
                        RenderNormalCameraDrawSubmissionSnapshot>? group) ||
                group is null)
            {
                consecutiveEmptyLanes++;
                continue;
            }

            _progressiveStaticAdmissionScratch.Add(group);
            admitted++;
            consecutiveEmptyLanes = 0;
        }

        PublishProgressiveStaticGroups(
            _progressiveStaticAdmissionScratch,
            waitForCompletion: false);

        _progressiveStaticPendingGroupCount =
            CountRequiredUnpublishedProgressiveStaticGroups();
        _progressiveStaticAdmissionCamera = camera;
        if (admitted != 0)
        {
            StaticResourceMaterializationWaveCount++;
            // Publication is an atomic set insertion at the frame boundary;
            // immutable Metal draw groups need no OpenGL-style queue rebuild.
            InvalidateNormalCameraReceiverSelection();
        }
    }

    private void CreateProgressiveStaticAdmissionLanes()
    {
        // Preserve OpenGL's fairness topology: generic base, exact normal
        // camera, then each page/allocation receiver channel. A lane owns
        // scheduling order only; authored multipass groups remain the atomic
        // native publication unit.
        var laneGroups = new List<MapRenderEditorDrawGroup<
            RenderNormalCameraDrawSubmissionSnapshot>>[
                ProgressiveStaticAdmissionLaneCount];
        for (int laneIndex = 0;
             laneIndex < laneGroups.Length;
             laneIndex++)
        {
            laneGroups[laneIndex] = [];
        }
        for (int groupIndex = 0;
             groupIndex < _progressiveStaticGroups.Length;
             groupIndex++)
        {
            MapRenderEditorDrawGroup<
                RenderNormalCameraDrawSubmissionSnapshot> group =
                    _progressiveStaticGroups[groupIndex];
            laneGroups[ResolveProgressiveStaticAdmissionLane(group)]
                .Add(group);
        }

        _progressiveStaticAdmissionLanes = new
            MapRenderEditorDrawGroup<
                RenderNormalCameraDrawSubmissionSnapshot>[
                    laneGroups.Length][];
        for (int laneIndex = 0;
             laneIndex < laneGroups.Length;
             laneIndex++)
        {
            _progressiveStaticAdmissionLanes[laneIndex] =
                laneGroups[laneIndex].ToArray();
        }
        _progressiveStaticAdmissionLaneCursor = 0;
    }

    private static int ResolveProgressiveStaticAdmissionLane(
        MapRenderEditorDrawGroup<
            RenderNormalCameraDrawSubmissionSnapshot> group)
    {
        RenderNormalCameraPreparedPassSnapshot pass =
            group.AuthoredPassSpan[0].PreparedPass;
        if (pass.StaticReceiverVariant is not { } receiver)
        {
            return HasGenericMaterialMarker(pass.ShaderProvenance)
                ? 0
                : 1;
        }

        int pageOffset = receiver.Page switch
        {
            MapRenderStaticModelReceiverPage.StaticModelRigidPage2 => 0,
            MapRenderStaticModelReceiverPage
                .StaticModelRigidNoSunShadowPage3 => 1,
            _ => throw new InvalidOperationException(
                "A progressive Metal static group has an unknown receiver page.")
        };
        int allocationOffset = receiver.Allocation switch
        {
            MapRenderTechniqueVariantAllocation.Unshadowed => 0,
            MapRenderTechniqueVariantAllocation.ShadowMapAllocated => 1,
            _ => throw new InvalidOperationException(
                "A progressive Metal static group has an unknown receiver allocation.")
        };
        return checked(
            ProgressiveStaticReceiverAdmissionLaneOffset +
            pageOffset * 2 +
            allocationOffset);
    }

    private bool TrySelectNextProgressiveStaticAdmissionGroup(
        int laneIndex,
        out MapRenderEditorDrawGroup<
            RenderNormalCameraDrawSubmissionSnapshot>? selectedGroup)
    {
        MapRenderEditorDrawGroup<
            RenderNormalCameraDrawSubmissionSnapshot>[] lane =
                _progressiveStaticAdmissionLanes[laneIndex];
        for (int groupIndex = 0;
             groupIndex < lane.Length;
             groupIndex++)
        {
            MapRenderEditorDrawGroup<
                RenderNormalCameraDrawSubmissionSnapshot> group =
                    lane[groupIndex];
            if (_publishedProgressiveStaticGroups.Contains(group) ||
                _progressiveStaticAdmissionScratch.Contains(group) ||
                !IsProgressiveStaticGroupRequired(group))
            {
                continue;
            }

            selectedGroup = group;
            return true;
        }

        selectedGroup = null;
        return false;
    }

    private bool IsProgressiveStaticGroupRequired(
        MapRenderEditorDrawGroup<
            RenderNormalCameraDrawSubmissionSnapshot> group)
    {
        if (!_normalCameraVisibilityGroups.TryGetValue(
                group,
                out MetalNormalCameraVisibilityGroupPlan? plan) ||
            !plan.CanFilterStaticInstances)
        {
            return true;
        }
        for (int identityIndex = 0;
             identityIndex < plan.StaticReceiverIdentities.Length;
             identityIndex++)
        {
            if (IsNormalCameraStaticIdentitySelected(
                    plan.StaticReceiverIdentities[identityIndex]))
                return true;
        }
        return false;
    }

    private int CountRequiredUnpublishedProgressiveStaticGroups()
    {
        int pending = 0;
        for (int groupIndex = 0;
             groupIndex < _progressiveStaticGroups.Length;
             groupIndex++)
        {
            MapRenderEditorDrawGroup<
                RenderNormalCameraDrawSubmissionSnapshot> group =
                    _progressiveStaticGroups[groupIndex];
            if (!_publishedProgressiveStaticGroups.Contains(group) &&
                IsProgressiveStaticGroupRequired(group))
            {
                pending++;
            }
        }
        return pending;
    }

    private void PublishProgressiveStaticGroups(
        IReadOnlyList<MapRenderEditorDrawGroup<
            RenderNormalCameraDrawSubmissionSnapshot>> groups,
        bool waitForCompletion)
    {
        if (groups.Count == 0)
            return;
        _progressiveStaticGeometryScratch.Clear();
        _progressiveStaticInstanceScratch.Clear();
        for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            ReadOnlySpan<RenderNormalCameraDrawSubmissionSnapshot> passes =
                groups[groupIndex].AuthoredPassSpan;
            for (int passIndex = 0; passIndex < passes.Length; passIndex++)
            {
                RenderNormalCameraPreparedPassSnapshot pass =
                    passes[passIndex].PreparedPass;
                _progressiveStaticGeometryScratch.Add(
                    pass.Geometry.Identity);
                if (pass.Instances is { } instanceResource)
                {
                    _progressiveStaticInstanceScratch.Add(
                        instanceResource.Identity);
                }
            }
        }
        _resources.AdmitStaticResources(
            _progressiveStaticGeometryScratch,
            _progressiveStaticInstanceScratch,
            waitForCompletion);
        for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            MapRenderEditorDrawGroup<
                RenderNormalCameraDrawSubmissionSnapshot> group =
                    groups[groupIndex];
            if (_publishedProgressiveStaticGroups.Add(group))
            {
                _publishedProgressiveStaticBatchCount = checked(
                    _publishedProgressiveStaticBatchCount +
                    group.AuthoredPassSpan.Length);
            }
        }
    }

    private bool IsProgressiveStaticGroupPublished(
        MapRenderEditorDrawGroup<
            RenderNormalCameraDrawSubmissionSnapshot> group) =>
        group.AuthoredPassSpan[0].PreparedPass.SourceKind !=
            RenderNormalCameraDrawSourceKind.StaticModel ||
        _publishedProgressiveStaticGroups.Contains(group);

    private bool IsProgressiveStaticWorkingSetSettled(
        RenderCamera requestedCamera) =>
        _progressiveStaticAdmissionCamera == requestedCamera &&
        _progressiveStaticPendingGroupCount == 0;

    private void SetProgressiveStaticAdmissionInactive(RenderCamera camera)
    {
        _progressiveStaticAdmissionCamera = camera;
        _progressiveStaticPendingGroupCount = 0;
    }

    private void ResetProgressiveStaticAdmission()
    {
        _publishedProgressiveStaticGroups.Clear();
        _progressiveStaticAdmissionScratch.Clear();
        _progressiveStaticGeometryScratch.Clear();
        _progressiveStaticInstanceScratch.Clear();
        _progressiveStaticGroups = [];
        _progressiveStaticAdmissionLanes = [];
        _progressiveStaticAdmissionLaneCursor = 0;
        _progressiveStaticAdmissionCamera = null;
        _progressiveStaticPendingGroupCount = 0;
        _publishedProgressiveStaticBatchCount = 0;
        StaticResourceSourceBatchCount = 0;
        StaticResourceRejectedBatchCount = 0;
        StaticResourceMaterializationWaveCount = 0;
    }
}
