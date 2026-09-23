using System.Numerics;
using System.Runtime.Versioning;

using IW4.Render.Diagnostics;
using IW4.Render.EditorPreview;
using IW4.Render.Geometry;
using IW4.Render.Resources;
using IW4.Render.Scheduling;
using IW4.Render.Scheduling.Clear;
using IW4.Render.Scheduling.Dpvs;
using IW4.Render.Scheduling.FramePlans;
using IW4.Render.Scheduling.StaticModels;
using IW4.Render.SceneBuilding;
using IW4.Render.Visibility;
using IW4.Render.World;

namespace IW4.Render.Metal;

[SupportedOSPlatform("macos")]
public sealed unsafe partial class MetalMapRenderer
{
    private readonly Vector4[] _normalCameraVisibilityFrustumPlanes =
        new Vector4[MapRenderCameraFrustum.PlaneCount];
    private readonly Dictionary<
        MapRenderEditorDrawGroup<
            RenderNormalCameraDrawSubmissionSnapshot>,
        MetalNormalCameraVisibilityGroupPlan>
        _normalCameraVisibilityGroups = new(
            ReferenceEqualityComparer.Instance);
    private readonly HashSet<MapRenderEditorDrawGroup<
        RenderNormalCameraDrawSubmissionSnapshot>>
        _normalCameraSelectedGroups = new(
            ReferenceEqualityComparer.Instance);
    private readonly Dictionary<
        MetalNormalCameraWorldReceiverCandidateKey,
        MetalNormalCameraReceiverCandidate>
        _normalCameraWorldReceiverCandidates = [];
    private readonly Dictionary<
        MetalNormalCameraStaticReceiverCandidateKey,
        MetalNormalCameraReceiverCandidate>
        _normalCameraStaticReceiverCandidates = [];
    private readonly Dictionary<MapRenderStaticModelReceiverIdentity, int>
        _normalCameraStaticRouteIdentityOrdinals = [];
    private MetalNormalCameraVisibilityGroupPlan[]
        _normalCameraBaseGroupPlans = [];
    private MetalNormalCameraVisibilityGroupPlan[]
        _normalCameraReceiverGroupPlans = [];
    private MapRenderSceneTechniqueVariantCatalog?
        _normalCameraTechniqueVariants;
    private int[] _normalCameraWorldRouteOwners = [];
    private MapRenderStaticModelReceiverIdentity[]
        _normalCameraStaticRouteIdentities = [];
    private int[] _normalCameraStaticRouteOwners = [];
    private MapRenderWorldSceneSource? _normalCameraVisibilityWorldSource;
    private MapRenderWorldDpvsCameraOnlyVisibilityCache?
        _normalCameraDpvsCache;
    private MapRenderStaticModelSchedulingInfo[]
        _normalCameraStaticScheduling = [];
    private bool[] _normalCameraStaticIdentityKnown = [];
    private bool[] _normalCameraStaticVisible = [];
    private int[] _normalCameraStaticSelectedLod = [];
    private int[] _normalCameraStaticFallbackLod = [];
    private MapRenderWorldDpvsViewVisibility?
        _normalCameraCurrentDpvsVisibility;
    private bool _normalCameraVisibilityFrustumValid;
    private bool _normalCameraStaticSelectionValid;
    private int _normalCameraStaticCandidateCount;
    private int _normalCameraStaticAlwaysVisibleCount;
    private int _normalCameraStaticVisibleObjectCount;
    private long _normalCameraWorldCandidateCount;
    private bool _hasNormalCameraStaticSelectionCache;
    private MetalNormalCameraVisibilityKey
        _normalCameraStaticSelectionKey;
    private MapRenderWorldDpvsViewVisibility?
        _normalCameraStaticSelectionDpvs;
    private long _normalCameraVisibilityPreparedFrameIndex = -1;
    private long _publishedShadowVisibilityFrameIndex = -1;
    private long _normalCameraGroupSelectionFrameIndex = -1;
    private MetalNormalCameraVisibilityKey _publishedShadowVisibilityKey;
    private MapRenderWorldDpvsViewVisibility?
        _publishedShadowCameraVisibility;

    private void CreateNormalCameraVisibilityResources(
        MapRenderScene scene,
        RenderSceneSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(snapshot);
        DeleteNormalCameraVisibilityResources();

        _normalCameraVisibilityWorldSource = scene.WorldSource;
        _normalCameraTechniqueVariants = scene.TechniqueVariants;
        if (_normalCameraVisibilityWorldSource is not null)
        {
            // Camera visibility is a normal-camera resource. Shadow caster or
            // atlas admission must never decide whether this cache exists.
            _normalCameraDpvsCache =
                new MapRenderWorldDpvsCameraOnlyVisibilityCache();
        }

        uint[] availableLodMaskByObject = CreateStaticObjectStorage(
            scene,
            out byte[] schedulingIdentityCounts,
            out uint[] blockedLodMaskByObject);
        var renderedStaticObjectByIndex =
            new bool[availableLodMaskByObject.Length];
        foreach (MapRenderEditorDrawGroup<
                     RenderNormalCameraDrawSubmissionSnapshot> group in
                 snapshot.NormalCameraDraws.DrawGroups)
        {
            MetalNormalCameraVisibilityGroupPlan plan =
                CreateVisibilityGroupPlan(
                    group,
                    _loadedIsolatedWorldSurfaceIndex);
            _normalCameraVisibilityGroups.Add(group, plan);
            bool authorized = _normalCameraAuthorizedGroups.Contains(group);
            plan.IsAuthorized = authorized;
            RenderNormalCameraPreparedPassSnapshot firstPass =
                group.AuthoredPasses[0].PreparedPass;
            bool isBaseStatic =
                firstPass.StaticReceiverVariant is null;
            if (authorized &&
                isBaseStatic &&
                firstPass.SourceKind ==
                    RenderNormalCameraDrawSourceKind.StaticModel)
            {
                foreach (MapRenderStaticModelInstance instance in
                         firstPass.StaticInstances)
                {
                    if ((uint)instance.ObjectIndex <
                        (uint)renderedStaticObjectByIndex.Length)
                    {
                        renderedStaticObjectByIndex[
                            instance.ObjectIndex] = true;
                    }
                }
            }

            if (!plan.CanFilterStaticInstances || !isBaseStatic)
                continue;

            RenderNormalCameraPreparedPassSnapshot pass =
                plan.StaticPass!;
            int lodIndex = plan.StaticLodIndex;
            uint lodBit = 1u << lodIndex;
            foreach (MapRenderStaticModelInstance instance in
                     pass.StaticInstances)
            {
                if ((uint)instance.ObjectIndex >=
                    (uint)availableLodMaskByObject.Length)
                {
                    continue;
                }
                if (authorized)
                {
                    availableLodMaskByObject[instance.ObjectIndex] |= lodBit;
                }
                else
                {
                    blockedLodMaskByObject[instance.ObjectIndex] |= lodBit;
                }
            }
        }
        MetalNormalCameraVisibilityGroupPlan[] allPlans =
            _normalCameraVisibilityGroups.Values.ToArray();
        _normalCameraBaseGroupPlans = allPlans
            .Where(plan =>
                plan.IsAuthorized && !plan.HasReceiverVariant)
            .ToArray();
        _normalCameraWorldCandidateCount =
            _loadedIsolatedWorldSurfaceIndex is int isolatedSurfaceIndex
                ? allPlans.Any(plan =>
                    plan.IsAuthorized &&
                    plan.SourceKind ==
                        RenderNormalCameraDrawSourceKind.World &&
                    plan.ContainsWorldSurface(isolatedSurfaceIndex))
                    ? 1
                    : 0
                : _normalCameraBaseGroupPlans
                    .Where(plan =>
                        plan.SourceKind ==
                            RenderNormalCameraDrawSourceKind.World)
                    .Sum(plan =>
                        plan.CanFilterWorld
                            ? (long)plan.WorldSurfaceSpanCount
                            : 1L);
        _normalCameraReceiverGroupPlans = allPlans
            .Where(plan =>
                plan.HasReceiverVariant)
            .ToArray();
        CreateNormalCameraReceiverRoutingResources(
            scene,
            snapshot,
            allPlans);
        _normalCameraSelectedGroups.EnsureCapacity(allPlans.Length);
        MarkOmittedStaticLods(
            scene,
            snapshot.NormalCameraDraws,
            blockedLodMaskByObject);

        var scheduling = new List<MapRenderStaticModelSchedulingInfo>(
            scene.StaticModelScheduling.Count);
        foreach (MapRenderStaticModelSchedulingInfo? row in
                 scene.StaticModelScheduling)
        {
            if (row is null ||
                (uint)row.ObjectIndex >=
                    (uint)_normalCameraStaticIdentityKnown.Length ||
                schedulingIdentityCounts[row.ObjectIndex] != 1 ||
                (uint)row.PreparedLodIndex >= 32u ||
                _normalCameraStaticFallbackLod[row.ObjectIndex] !=
                    row.PreparedLodIndex)
            {
                continue;
            }

            uint preparedLodBit = 1u << row.PreparedLodIndex;
            uint availableLodMask =
                availableLodMaskByObject[row.ObjectIndex] &
                ~blockedLodMaskByObject[row.ObjectIndex];
            if ((availableLodMask & preparedLodBit) == 0)
            {
                // Without the prepared fallback in this immutable snapshot,
                // selector output cannot be consumed without hiding an
                // object. Retain its complete authored ranges instead.
                continue;
            }

            _normalCameraStaticIdentityKnown[row.ObjectIndex] = true;
            scheduling.Add(row with
            {
                RenderableLodMask =
                    row.RenderableLodMask & availableLodMask
            });
        }

        _normalCameraStaticScheduling = scheduling.ToArray();
        for (int objectIndex = 0;
             objectIndex < renderedStaticObjectByIndex.Length;
             objectIndex++)
        {
            if (!renderedStaticObjectByIndex[objectIndex])
                continue;
            _normalCameraStaticCandidateCount++;
            if (!_normalCameraStaticIdentityKnown[objectIndex])
                _normalCameraStaticAlwaysVisibleCount++;
        }
        _normalCameraStaticVisibleObjectCount =
            _normalCameraStaticCandidateCount;
        if (_loadedIsolatedWorldSurfaceIndex.HasValue)
        {
            // Isolation is a world-only editor view. Keep immutable static
            // resources available to shadow execution, but publish the exact
            // normal-camera candidate set consumed by depth and color.
            _normalCameraStaticCandidateCount = 0;
            _normalCameraStaticAlwaysVisibleCount = 0;
            _normalCameraStaticVisibleObjectCount = 0;
        }
        Array.Copy(
            _normalCameraStaticFallbackLod,
            _normalCameraStaticSelectedLod,
            _normalCameraStaticFallbackLod.Length);
        _normalCameraStaticVisible.AsSpan().Fill(true);
    }

    private void DeleteNormalCameraVisibilityResources()
    {
        _normalCameraDpvsCache?.Clear();
        _normalCameraDpvsCache = null;
        _normalCameraVisibilityWorldSource = null;
        _normalCameraTechniqueVariants = null;
        _normalCameraVisibilityGroups.Clear();
        _normalCameraSelectedGroups.Clear();
        _normalCameraWorldReceiverCandidates.Clear();
        _normalCameraStaticReceiverCandidates.Clear();
        _normalCameraStaticRouteIdentityOrdinals.Clear();
        _normalCameraBaseGroupPlans = [];
        _normalCameraReceiverGroupPlans = [];
        _normalCameraWorldRouteOwners = [];
        _normalCameraStaticRouteIdentities = [];
        _normalCameraStaticRouteOwners = [];
        _normalCameraStaticScheduling = [];
        _normalCameraStaticIdentityKnown = [];
        _normalCameraStaticVisible = [];
        _normalCameraStaticSelectedLod = [];
        _normalCameraStaticFallbackLod = [];
        _normalCameraCurrentDpvsVisibility = null;
        _normalCameraVisibilityFrustumValid = false;
        _normalCameraStaticSelectionValid = false;
        _normalCameraStaticCandidateCount = 0;
        _normalCameraStaticAlwaysVisibleCount = 0;
        _normalCameraStaticVisibleObjectCount = 0;
        _normalCameraWorldCandidateCount = 0;
        _hasNormalCameraStaticSelectionCache = false;
        _normalCameraStaticSelectionKey = default;
        _normalCameraStaticSelectionDpvs = null;
        _normalCameraVisibilityPreparedFrameIndex = -1;
        _publishedShadowVisibilityFrameIndex = -1;
        _normalCameraGroupSelectionFrameIndex = -1;
        _publishedShadowVisibilityKey = default;
        _publishedShadowCameraVisibility = null;
    }

    private void ResetNormalCameraVisibilityFrameState()
    {
        _normalCameraCurrentDpvsVisibility = null;
        _normalCameraVisibilityFrustumValid = false;
        _normalCameraStaticSelectionValid = false;
        _normalCameraVisibilityPreparedFrameIndex = -1;
        _normalCameraGroupSelectionFrameIndex = -1;
        foreach (MetalNormalCameraVisibilityGroupPlan plan in
                 _normalCameraVisibilityGroups.Values)
        {
            plan.InvalidatePreparedRuns();
        }
    }

    // Shadow encoding prepares an unshadowed route while it validates the
    // normal-camera DPVS/LOD closure, then replaces it with a same-revision
    // ready selector after the atlas completes. Depth and color both enter
    // through IsNormalCameraGroupSelected, so invalidate their shared route
    // cache as one operation rather than allowing one phase to observe the
    // preflight/base owner and the other the ready receiver owner.
    private void InvalidateNormalCameraReceiverSelection()
    {
        _normalCameraGroupSelectionFrameIndex = -1;
        foreach (MetalNormalCameraVisibilityGroupPlan plan in
                 _normalCameraVisibilityGroups.Values)
        {
            plan.InvalidatePreparedRuns();
        }
    }

    private void PrepareNormalCameraVisibility(
        RenderCamera camera,
        bool recordTelemetry = true)
    {
        if (_normalCameraVisibilityPreparedFrameIndex == _frameIndex)
            return;

        using MapRenderCpuPhaseScope cpuPhase = recordTelemetry
            ? _telemetry.BeginCpuPhase(MapRenderCpuPhase.Visibility)
            : default;
        _normalCameraVisibilityPreparedFrameIndex = _frameIndex;
        var extent = new MapRenderNormalCameraFramebufferExtent(
            _surfaceExtents.SceneTarget.Width,
            _surfaceExtents.SceneTarget.Height);
        var farPlane = new MapRenderNormalCameraFarPlaneState(
            rZFar: 0f,
            rendererFallback: camera.FarPlane);
        var key = new MetalNormalCameraVisibilityKey(
            camera,
            extent,
            farPlane.RZFar,
            farPlane.RendererFallback);

        try
        {
            MapRenderCameraFrustum.BuildPlanes(
                camera,
                extent.AspectRatio,
                _normalCameraVisibilityFrustumPlanes);
            _normalCameraVisibilityFrustumValid = true;

            _normalCameraCurrentDpvsVisibility =
                ResolveNormalCameraDpvsVisibility(
                    camera,
                    extent,
                    farPlane,
                    key);

            if (_loadedIsolatedWorldSurfaceIndex.HasValue)
            {
                _normalCameraStaticVisible.AsSpan().Fill(false);
                _normalCameraStaticVisibleObjectCount = 0;
                _normalCameraStaticSelectionValid = true;
                _hasNormalCameraStaticSelectionCache = false;
                _normalCameraStaticSelectionKey = default;
                _normalCameraStaticSelectionDpvs = null;
                PrepareNormalCameraGroupSelection();
                return;
            }

            if (_hasNormalCameraStaticSelectionCache &&
                _normalCameraStaticSelectionKey == key &&
                ReferenceEquals(
                    _normalCameraStaticSelectionDpvs,
                    _normalCameraCurrentDpvsVisibility))
            {
                _normalCameraStaticSelectionValid = true;
                PrepareNormalCameraGroupSelection();
                return;
            }

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
                    _normalCameraCurrentDpvsVisibility,
                    _normalCameraStaticVisible,
                    _normalCameraStaticSelectedLod,
                    viewDistanceScale: 1f,
                    nearViewScale: 1f,
                    farViewScale: 1f);
            _normalCameraStaticVisibleObjectCount = checked(
                visibleScheduledObjectCount +
                _normalCameraStaticAlwaysVisibleCount);
            _normalCameraStaticSelectionValid = true;
            _normalCameraStaticSelectionKey = key;
            _normalCameraStaticSelectionDpvs =
                _normalCameraCurrentDpvsVisibility;
            _hasNormalCameraStaticSelectionCache = true;
            PrepareNormalCameraGroupSelection();
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            InvalidDataException or
            InvalidOperationException or
            NotSupportedException or
            OverflowException or
            AggregateException)
        {
            // Visibility is an optimization. A malformed or temporarily
            // unavailable producer must leave every authored range visible.
            _normalCameraCurrentDpvsVisibility = null;
            _normalCameraVisibilityFrustumValid = false;
            _normalCameraStaticSelectionValid = false;
            _normalCameraStaticVisibleObjectCount =
                _normalCameraStaticCandidateCount;
            _hasNormalCameraStaticSelectionCache = false;
            _normalCameraStaticSelectionKey = default;
            _normalCameraStaticSelectionDpvs = null;
            _normalCameraStaticVisible.AsSpan().Fill(true);
            Array.Copy(
                _normalCameraStaticFallbackLod,
                _normalCameraStaticSelectedLod,
                _normalCameraStaticFallbackLod.Length);
            PrepareNormalCameraGroupSelection();
        }
    }

    private void PrepareNormalCameraGroupSelection()
    {
        if (_normalCameraGroupSelectionFrameIndex == _frameIndex)
            return;
        ResetNormalCameraReceiverRoutingToBase();

        try
        {
            if (!TryGetCurrentNormalCameraReceiverSelector(
                    out MapRenderFrameTechniqueSelector? selector) ||
                selector is null ||
                _normalCameraTechniqueVariants is not { } techniques)
            {
                return;
            }

            ResolveNormalCameraWorldRouteOwners(selector, techniques);
            ResolveNormalCameraStaticRouteOwners(selector, techniques);
            foreach (MetalNormalCameraVisibilityGroupPlan receiver in
                     _normalCameraReceiverGroupPlans)
            {
                if (receiver.IsAuthorized &&
                    receiver.SelectedRouteIdentityCount != 0)
                {
                    _normalCameraSelectedGroups.Add(receiver.Group);
                }
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            InvalidDataException or
            InvalidOperationException or
            OverflowException)
        {
            ResetNormalCameraReceiverRoutingToBase();
        }
        finally
        {
            _normalCameraGroupSelectionFrameIndex = _frameIndex;
        }
    }

    private bool IsNormalCameraGroupSelected(
        MapRenderEditorDrawGroup<
            RenderNormalCameraDrawSubmissionSnapshot> group)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (_normalCameraGroupSelectionFrameIndex != _frameIndex)
            PrepareNormalCameraGroupSelection();
        if (_loadedIsolatedWorldSurfaceIndex.HasValue &&
            (!_normalCameraVisibilityGroups.TryGetValue(
                 group,
                 out MetalNormalCameraVisibilityGroupPlan? isolatedPlan) ||
             isolatedPlan.SourceKind !=
                 RenderNormalCameraDrawSourceKind.World ||
             !isolatedPlan.CanFilterWorld))
        {
            return false;
        }
        return _normalCameraSelectedGroups.Contains(group) &&
            IsProgressiveStaticGroupPublished(group);
    }

    private bool CanAuthorizeNormalCameraShadowReceiverSelector(
        MapRenderFrameTechniqueSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        if (_normalCameraTechniqueVariants is not { } techniques ||
            selector.Visibility.Camera.SurfaceCount !=
                techniques.WorldSurfaces.Count)
        {
            return false;
        }

        return CanAuthorizeNormalCameraWorldShadowReceivers(
                selector,
                techniques) &&
            CanAuthorizeNormalCameraStaticShadowReceivers(
                selector,
                techniques);
    }

    private void CreateNormalCameraReceiverRoutingResources(
        MapRenderScene scene,
        RenderSceneSnapshot snapshot,
        IReadOnlyList<MetalNormalCameraVisibilityGroupPlan> plans)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(plans);

        _normalCameraWorldReceiverCandidates.Clear();
        _normalCameraStaticReceiverCandidates.Clear();
        _normalCameraStaticRouteIdentityOrdinals.Clear();

        int worldSurfaceCount = scene.TechniqueVariants?.WorldSurfaces.Count ??
            scene.WorldSource?.World.SurfaceCount ?? 0;
        foreach (MetalNormalCameraVisibilityGroupPlan plan in plans)
        {
            foreach (MapRenderWorldSurfaceSpan[] passSpans in
                     plan.WorldPassSpans)
            foreach (MapRenderWorldSurfaceSpan span in passSpans)
            {
                worldSurfaceCount = Math.Max(
                    worldSurfaceCount,
                    checked(span.SurfaceIndex + 1));
            }
        }
        _normalCameraWorldRouteOwners = new int[worldSurfaceCount];

        var staticIdentities = new List<
            MapRenderStaticModelReceiverIdentity>();
        IReadOnlyList<MapRenderInstancedTexturedBatch> staticBatches =
            snapshot.NormalCameraDraws.Coverage ==
                RenderNormalCameraDrawCoverage
                    .PreparedWorldAndAllStaticLodBatchesWithoutDpvsSelection
                ? scene.StaticModelLodTexturedBatches
                : scene.InstancedTexturedBatches;
        foreach (MapRenderInstancedTexturedBatch batch in staticBatches)
        foreach (MapRenderStaticModelInstance instance in batch.Instances)
        {
            TryAddNormalCameraStaticRouteIdentity(
                new MapRenderStaticModelReceiverIdentity(
                    instance,
                    batch.LodIndex),
                staticIdentities);
        }
        foreach (MetalNormalCameraVisibilityGroupPlan plan in plans)
        foreach (MapRenderStaticModelReceiverIdentity identity in
                 plan.StaticReceiverIdentities)
        {
            TryAddNormalCameraStaticRouteIdentity(
                identity,
                staticIdentities);
        }

        _normalCameraStaticRouteIdentities = staticIdentities.ToArray();
        _normalCameraStaticRouteOwners = new int[
            _normalCameraStaticRouteIdentities.Length];
        foreach (MetalNormalCameraVisibilityGroupPlan plan in plans)
        {
            plan.AssignStaticRouteIdentityOrdinals(
                _normalCameraStaticRouteIdentityOrdinals);
        }

        int routeOrdinal = 1;
        foreach (MetalNormalCameraVisibilityGroupPlan receiver in
                 _normalCameraReceiverGroupPlans)
        {
            receiver.RouteOrdinal = routeOrdinal;
            routeOrdinal = checked(routeOrdinal + 1);
            RegisterNormalCameraReceiverCandidates(receiver);
        }
    }

    private void TryAddNormalCameraStaticRouteIdentity(
        MapRenderStaticModelReceiverIdentity identity,
        ICollection<MapRenderStaticModelReceiverIdentity> identities)
    {
        if (_normalCameraStaticRouteIdentityOrdinals.ContainsKey(identity))
            return;
        int ordinal = _normalCameraStaticRouteIdentityOrdinals.Count;
        _normalCameraStaticRouteIdentityOrdinals.Add(identity, ordinal);
        identities.Add(identity);
    }

    private void RegisterNormalCameraReceiverCandidates(
        MetalNormalCameraVisibilityGroupPlan receiver)
    {
        if (!receiver.IsAuthorized ||
            !receiver.ReceiverSelectionShapeValid ||
            receiver.RouteOrdinal <= 0)
        {
            return;
        }

        if (receiver.WorldReceiverVariant is { } worldVariant)
        {
            MapRenderSceneTechniqueVariantCatalog? techniques =
                _normalCameraTechniqueVariants;
            if (techniques is null)
                return;
            foreach (int surfaceIndex in
                     receiver.WorldReceiverSurfaceIndices)
            {
                if ((uint)surfaceIndex >=
                        (uint)techniques.WorldSurfaces.Count)
                {
                    continue;
                }
                var key = new MetalNormalCameraWorldReceiverCandidateKey(
                    surfaceIndex,
                    worldVariant);
                RegisterNormalCameraWorldReceiverCandidate(key, receiver);
            }
            return;
        }

        if (receiver.StaticReceiverVariant is not { } staticVariant)
            return;
        for (int offset = 0;
             offset < receiver.StaticReceiverIdentities.Length;
             offset++)
        {
            int identityOrdinal = receiver.StaticRouteIdentityOrdinals[offset];
            if (identityOrdinal < 0)
                continue;
            var key = new MetalNormalCameraStaticReceiverCandidateKey(
                identityOrdinal,
                staticVariant,
                receiver.TechniqueSlot);
            RegisterNormalCameraStaticReceiverCandidate(key, receiver);
        }
    }

    private void RegisterNormalCameraWorldReceiverCandidate(
        MetalNormalCameraWorldReceiverCandidateKey key,
        MetalNormalCameraVisibilityGroupPlan plan)
    {
        if (!_normalCameraWorldReceiverCandidates.TryGetValue(
                key,
                out MetalNormalCameraReceiverCandidate existing))
        {
            _normalCameraWorldReceiverCandidates.Add(
                key,
                new(plan, IsAmbiguous: false));
            return;
        }
        if (!ReferenceEquals(existing.Plan, plan))
        {
            _normalCameraWorldReceiverCandidates[key] = new(
                Plan: null,
                IsAmbiguous: true);
        }
    }

    private void RegisterNormalCameraStaticReceiverCandidate(
        MetalNormalCameraStaticReceiverCandidateKey key,
        MetalNormalCameraVisibilityGroupPlan plan)
    {
        if (!_normalCameraStaticReceiverCandidates.TryGetValue(
                key,
                out MetalNormalCameraReceiverCandidate existing))
        {
            _normalCameraStaticReceiverCandidates.Add(
                key,
                new(plan, IsAmbiguous: false));
            return;
        }
        if (!ReferenceEquals(existing.Plan, plan))
        {
            _normalCameraStaticReceiverCandidates[key] = new(
                Plan: null,
                IsAmbiguous: true);
        }
    }

    private void ResetNormalCameraReceiverRoutingToBase()
    {
        Array.Clear(_normalCameraWorldRouteOwners);
        Array.Clear(_normalCameraStaticRouteOwners);
        foreach (MetalNormalCameraVisibilityGroupPlan receiver in
                 _normalCameraReceiverGroupPlans)
        {
            receiver.SelectedRouteIdentityCount = 0;
        }

        _normalCameraSelectedGroups.Clear();
        foreach (MetalNormalCameraVisibilityGroupPlan basePlan in
                 _normalCameraBaseGroupPlans)
        {
            _normalCameraSelectedGroups.Add(basePlan.Group);
        }
    }

    private void ResolveNormalCameraWorldRouteOwners(
        MapRenderFrameTechniqueSelector selector,
        MapRenderSceneTechniqueVariantCatalog techniques)
    {
        if (selector.Visibility.Camera.SurfaceCount !=
                techniques.WorldSurfaces.Count ||
            selector.Visibility.Camera.SurfaceCount >
                _normalCameraWorldRouteOwners.Length)
        {
            throw new InvalidDataException(
                "Normal-camera receiver routing requires cardinality-matched world surfaces.");
        }

        ReadOnlySpan<uint> cameraSurfaceWords =
            selector.Visibility.Camera.SurfaceBitSpan;
        int surfaceCount = selector.Visibility.Camera.SurfaceCount;
        for (int surfaceIndex = 0;
             surfaceIndex < surfaceCount;
             surfaceIndex++)
        {
            if (!IsMsbFirstBitSet(cameraSurfaceWords, surfaceIndex))
            {
                // An explicitly isolated surface is an editor selection, not
                // a camera-visibility query. Retain its base route when DPVS
                // excludes it so the requested exact span cannot disappear.
                if (_loadedIsolatedWorldSurfaceIndex != surfaceIndex)
                    _normalCameraWorldRouteOwners[surfaceIndex] = -1;
                continue;
            }

            MapRenderTechniqueVariantSet? variants =
                techniques.WorldSurfaces[surfaceIndex];
            if (variants is null ||
                !selector.TryResolveWorldSurface(
                    surfaceIndex,
                    variants.PrimaryLightIndex,
                    out MapRenderFrameTechniqueSelectionValue selection))
            {
                continue;
            }
            MapRenderTechniqueVariantAllocation allocation =
                ResolveReceiverAllocation(selection.ShadowMapAllocated);
            if (!techniques.RequiresWorldReceiverVariant(
                    surfaceIndex,
                    selection.PageMembership,
                    allocation))
            {
                continue;
            }

            var key = new MetalNormalCameraWorldReceiverCandidateKey(
                surfaceIndex,
                new MapRenderWorldReceiverVariantKey(
                    selection.PageMembership,
                    allocation));
            if (TryResolveNormalCameraWorldReceiverCandidate(
                    key,
                    out MetalNormalCameraVisibilityGroupPlan? receiver) &&
                receiver is not null)
            {
                _normalCameraWorldRouteOwners[surfaceIndex] =
                    receiver.RouteOrdinal;
                receiver.SelectedRouteIdentityCount++;
            }
        }
    }

    private void ResolveNormalCameraStaticRouteOwners(
        MapRenderFrameTechniqueSelector selector,
        MapRenderSceneTechniqueVariantCatalog techniques)
    {
        for (int identityOrdinal = 0;
             identityOrdinal < _normalCameraStaticRouteIdentities.Length;
             identityOrdinal++)
        {
            MapRenderStaticModelReceiverIdentity identity =
                _normalCameraStaticRouteIdentities[identityOrdinal];
            if (!IsNormalCameraStaticIdentityVisible(identity))
            {
                _normalCameraStaticRouteOwners[identityOrdinal] = -1;
                continue;
            }
            if ((uint)identity.ObjectIndex >=
                    (uint)techniques.StaticModelDrawInstances.Count ||
                techniques.StaticModelDrawInstances[
                    identity.ObjectIndex] is null ||
                !selector.TryResolveStaticModelSurface(
                    identity,
                    out MapRenderStaticModelFrameTechniqueSelectionValue
                        selection))
            {
                // An unavailable exact selector/catalog row is an
                // implementation gap, not a visibility result. Retain the
                // base owner so geometry cannot disappear.
                continue;
            }

            var key = new MetalNormalCameraStaticReceiverCandidateKey(
                identityOrdinal,
                new MapRenderStaticModelReceiverVariantKey(
                    selection.Page,
                    ResolveReceiverAllocation(
                        selection.ShadowMapAllocated)),
                selection.TechniqueSlot);
            if (TryResolveNormalCameraStaticReceiverCandidate(
                    key,
                    out MetalNormalCameraVisibilityGroupPlan? receiver) &&
                receiver is not null)
            {
                _normalCameraStaticRouteOwners[identityOrdinal] =
                    receiver.RouteOrdinal;
                receiver.SelectedRouteIdentityCount++;
            }
        }
    }

    private bool CanAuthorizeNormalCameraWorldShadowReceivers(
        MapRenderFrameTechniqueSelector selector,
        MapRenderSceneTechniqueVariantCatalog techniques)
    {
        ReadOnlySpan<uint> cameraSurfaceWords =
            selector.Visibility.Camera.SurfaceBitSpan;
        for (int surfaceIndex = 0;
             surfaceIndex < selector.Visibility.Camera.SurfaceCount;
             surfaceIndex++)
        {
            if (!IsMsbFirstBitSet(cameraSurfaceWords, surfaceIndex) ||
                techniques.WorldSurfaces[surfaceIndex] is not
                    { } variants ||
                !selector.TryResolveWorldSurface(
                    surfaceIndex,
                    variants.PrimaryLightIndex,
                    out MapRenderFrameTechniqueSelectionValue selection) ||
                !selection.ShadowMapAllocated ||
                selection.PageMembership !=
                    MapRenderWorldSurfacePageMembership.PageZero ||
                !techniques.RequiresWorldReceiverVariant(
                    surfaceIndex,
                    selection.PageMembership,
                    MapRenderTechniqueVariantAllocation
                        .ShadowMapAllocated))
            {
                continue;
            }

            var key = new MetalNormalCameraWorldReceiverCandidateKey(
                surfaceIndex,
                new MapRenderWorldReceiverVariantKey(
                    selection.PageMembership,
                    MapRenderTechniqueVariantAllocation.ShadowMapAllocated));
            if (!TryResolveNormalCameraWorldReceiverCandidate(
                    key,
                    out _))
            {
                return false;
            }
        }
        return true;
    }

    private bool CanAuthorizeNormalCameraStaticShadowReceivers(
        MapRenderFrameTechniqueSelector selector,
        MapRenderSceneTechniqueVariantCatalog techniques)
    {
        bool hasCurrentStaticSelection =
            _normalCameraVisibilityPreparedFrameIndex == _frameIndex &&
            _normalCameraStaticSelectionValid;
        for (int identityOrdinal = 0;
             identityOrdinal < _normalCameraStaticRouteIdentities.Length;
             identityOrdinal++)
        {
            MapRenderStaticModelReceiverIdentity identity =
                _normalCameraStaticRouteIdentities[identityOrdinal];
            if (hasCurrentStaticSelection &&
                    !IsNormalCameraStaticIdentityVisible(identity) ||
                (uint)identity.ObjectIndex >=
                    (uint)techniques.StaticModelDrawInstances.Count ||
                techniques.StaticModelDrawInstances[
                    identity.ObjectIndex] is null ||
                !selector.TryResolveStaticModelSurface(
                    identity,
                    out MapRenderStaticModelFrameTechniqueSelectionValue
                        selection) ||
                !selection.ShadowMapAllocated ||
                selection.Page != MapRenderStaticModelReceiverPage
                    .StaticModelRigidPage2)
            {
                continue;
            }

            var key = new MetalNormalCameraStaticReceiverCandidateKey(
                identityOrdinal,
                new MapRenderStaticModelReceiverVariantKey(
                    selection.Page,
                    MapRenderTechniqueVariantAllocation.ShadowMapAllocated),
                selection.TechniqueSlot);
            if (!TryResolveAuthorizedNormalCameraStaticReceiverCandidate(
                    key,
                    out _))
            {
                return false;
            }
        }
        return true;
    }

    private bool TryResolveNormalCameraWorldReceiverCandidate(
        MetalNormalCameraWorldReceiverCandidateKey key,
        out MetalNormalCameraVisibilityGroupPlan? plan)
    {
        if (_normalCameraWorldReceiverCandidates.TryGetValue(
                key,
                out MetalNormalCameraReceiverCandidate candidate) &&
            !candidate.IsAmbiguous &&
            candidate.Plan is { IsAuthorized: true } authorized &&
            authorized.RouteOrdinal > 0 &&
            IsProgressiveStaticGroupPublished(authorized.Group))
        {
            plan = authorized;
            return true;
        }
        plan = null;
        return false;
    }

    private bool TryResolveNormalCameraStaticReceiverCandidate(
        MetalNormalCameraStaticReceiverCandidateKey key,
        out MetalNormalCameraVisibilityGroupPlan? plan)
    {
        if (TryResolveAuthorizedNormalCameraStaticReceiverCandidate(
                key,
                out MetalNormalCameraVisibilityGroupPlan? authorized) &&
            authorized is not null &&
            IsProgressiveStaticGroupPublished(authorized.Group))
        {
            plan = authorized;
            return true;
        }
        plan = null;
        return false;
    }

    private bool TryResolveAuthorizedNormalCameraStaticReceiverCandidate(
        MetalNormalCameraStaticReceiverCandidateKey key,
        out MetalNormalCameraVisibilityGroupPlan? plan)
    {
        if (_normalCameraStaticReceiverCandidates.TryGetValue(
                key,
                out MetalNormalCameraReceiverCandidate candidate) &&
            !candidate.IsAmbiguous &&
            candidate.Plan is { IsAuthorized: true } authorized &&
            authorized.RouteOrdinal > 0)
        {
            plan = authorized;
            return true;
        }
        plan = null;
        return false;
    }

    private bool IsNormalCameraStaticIdentityVisible(
        MapRenderStaticModelReceiverIdentity identity)
    {
        int objectIndex = identity.ObjectIndex;
        return IsStaticModelLightingObjectAdmitted(objectIndex) &&
            IsNormalCameraStaticIdentitySelected(identity);
    }

    private bool IsNormalCameraStaticIdentitySelected(
        MapRenderStaticModelReceiverIdentity identity)
    {
        int objectIndex = identity.ObjectIndex;
        if (!_normalCameraStaticSelectionValid ||
            (uint)objectIndex >=
                (uint)_normalCameraStaticIdentityKnown.Length)
        {
            return true;
        }
        if (_normalCameraStaticIdentityKnown[objectIndex])
        {
            return _normalCameraStaticVisible[objectIndex] &&
                _normalCameraStaticSelectedLod[objectIndex] ==
                    identity.LodIndex;
        }

        int fallbackLod = _normalCameraStaticFallbackLod[objectIndex];
        return fallbackLod < 0 || fallbackLod == identity.LodIndex;
    }

    private static bool IsMsbFirstBitSet(
        ReadOnlySpan<uint> words,
        int index)
    {
        int wordIndex = index >> 5;
        return (uint)wordIndex < (uint)words.Length &&
            (words[wordIndex] &
                (0x8000_0000u >> (index & 31))) != 0;
    }

    private static MapRenderTechniqueVariantAllocation
        ResolveReceiverAllocation(bool shadowMapAllocated) =>
        shadowMapAllocated
            ? MapRenderTechniqueVariantAllocation.ShadowMapAllocated
            : MapRenderTechniqueVariantAllocation.Unshadowed;

    private void PublishShadowNormalCameraVisibility(
        RenderCamera camera,
        MapRenderNormalCameraFramebufferExtent extent,
        MapRenderNormalCameraFarPlaneState farPlane,
        MapRenderWorldDpvsVisibilityBuildResult visibility)
    {
        ArgumentNullException.ThrowIfNull(farPlane);
        ArgumentNullException.ThrowIfNull(visibility);
        if (!visibility.IsSuccess ||
            _normalCameraVisibilityWorldSource is not { } worldSource ||
            !ReferenceEquals(_shadowWorldSource, worldSource))
        {
            return;
        }

        MapRenderWorldDpvsViewVisibility? cameraVisibility = null;
        foreach (MapRenderWorldDpvsViewVisibility completed in
                 visibility.CompletedViews)
        {
            if (completed.ViewIndex !=
                MapRenderWorldDpvsViewIndex.Camera)
            {
                continue;
            }
            if (cameraVisibility is not null)
                return;
            cameraVisibility = completed;
        }
        if (cameraVisibility is null ||
            !HasMatchingWorldCardinality(cameraVisibility, worldSource))
        {
            return;
        }

        _publishedShadowVisibilityKey = new(
            camera,
            extent,
            farPlane.RZFar,
            farPlane.RendererFallback);
        _publishedShadowCameraVisibility = cameraVisibility;
        _publishedShadowVisibilityFrameIndex = _frameIndex;

        // Render normally enters shadow encoding before the scene encoder,
        // but keep that ordering an invariant rather than an assumption. A
        // newly published three-view camera result supersedes any same-frame
        // camera-only traversal and its base/receiver ownership map.
        if (_normalCameraVisibilityPreparedFrameIndex == _frameIndex)
        {
            _normalCameraVisibilityPreparedFrameIndex = -1;
            _normalCameraCurrentDpvsVisibility = null;
            _normalCameraStaticSelectionValid = false;
        }
        InvalidateNormalCameraReceiverSelection();
    }

    private int PrepareNormalCameraVisibleRuns(
        MapRenderEditorDrawGroup<
            RenderNormalCameraDrawSubmissionSnapshot> group,
        out MetalNormalCameraVisibilityGroupPlan? preparedPlan,
        out int visibleInstanceCount)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (!_normalCameraVisibilityGroups.TryGetValue(
                group,
                out MetalNormalCameraVisibilityGroupPlan? plan) ||
            _normalCameraVisibilityPreparedFrameIndex != _frameIndex)
        {
            preparedPlan = null;
            visibleInstanceCount =
                group.AuthoredPasses[0].Range.InstanceCount;
            return 1;
        }
        preparedPlan = plan;
        if (plan.PreparedFrameIndex == _frameIndex)
        {
            visibleInstanceCount = plan.VisibleInstanceCount;
            return plan.VisibleRunCount;
        }

        if (plan.CanFilterWorld)
        {
            bool isolatesWorldSurface =
                _loadedIsolatedWorldSurfaceIndex.HasValue;
            if (!isolatesWorldSurface &&
                _normalCameraCurrentDpvsVisibility is null &&
                _normalCameraVisibilityFrustumValid &&
                !MapRenderCameraFrustum.Intersects(
                    plan.WorldBounds,
                    _normalCameraVisibilityFrustumPlanes))
            {
                plan.PublishWorldCulled(_frameIndex);
            }
            else
            {
                MapRenderWorldDpvsViewVisibility? dpvs =
                    isolatesWorldSurface
                        ? null
                        : _normalCameraCurrentDpvsVisibility;
                ReadOnlySpan<uint> surfaceWords = dpvs is null
                    ? default
                    : dpvs.SurfaceBitSpan;
                plan.PublishWorld(
                    _frameIndex,
                    surfaceWords,
                    dpvs is not null,
                    _normalCameraWorldRouteOwners,
                    _loadedIsolatedWorldSurfaceIndex);
            }
            visibleInstanceCount = plan.VisibleInstanceCount;
            return plan.VisibleRunCount;
        }

        if (!plan.CanFilterStaticInstances)
        {
            plan.PublishOriginal(_frameIndex);
            visibleInstanceCount = plan.VisibleInstanceCount;
            return plan.VisibleRunCount;
        }

        int firstInstance = plan.FirstInstance;
        int endInstance = checked(firstInstance + plan.SourceInstanceCount);
        int runCount = 0;
        int visibleCount = 0;
        int runStart = -1;
        for (int instanceIndex = firstInstance;
             instanceIndex < endInstance;
             instanceIndex++)
        {
            int instanceOffset = instanceIndex - firstInstance;
            bool visible = IsNormalCameraStaticIdentityVisible(
                    plan.StaticReceiverIdentities[instanceOffset]) &&
                plan.IsStaticRouteIdentitySelected(
                    instanceOffset,
                    _normalCameraStaticRouteOwners);
            if (visible)
            {
                visibleCount++;
                if (runStart < 0)
                    runStart = instanceIndex;
                continue;
            }

            if (runStart >= 0)
            {
                plan.StaticVisibleRuns[runCount++] = new(
                    runStart,
                    instanceIndex - runStart,
                    PreserveSourceRange: false);
                runStart = -1;
            }
        }
        if (runStart >= 0)
        {
            plan.StaticVisibleRuns[runCount++] = new(
                runStart,
                endInstance - runStart,
                PreserveSourceRange: false);
        }

        plan.PublishStatic(_frameIndex, runCount, visibleCount);
        visibleInstanceCount = plan.VisibleInstanceCount;
        return runCount;
    }

    private static RenderDrawRange ApplyVisibleRun(
        RenderDrawRange source,
        MetalNormalCameraVisibilityGroupPlan? plan,
        int passIndex,
        int runIndex)
    {
        if (plan is null || plan.PreservesOriginalRange)
            return source;
        if (plan.CanFilterWorld)
        {
            MapRenderWorldVisibleRun worldRun =
                plan.ResolveWorldVisibleRun(passIndex, runIndex);
            return new RenderDrawRange(
                checked(source.FirstIndex + worldRun.FirstIndex),
                worldRun.IndexCount,
                source.BaseVertex,
                source.FirstInstance,
                source.InstanceCount);
        }

        MetalNormalCameraVisibleInstanceRun run =
            plan.StaticVisibleRuns[runIndex];
        return run.PreserveSourceRange
            ? source
            : new RenderDrawRange(
                source.FirstIndex,
                source.IndexCount,
                source.BaseVertex,
                run.FirstInstance,
                run.InstanceCount);
    }

    private static int ResolveVisibleRunCount(
        MetalNormalCameraVisibilityGroupPlan? plan,
        int passIndex,
        int fallbackRunCount) =>
        plan?.ResolveVisibleRunCount(passIndex) ?? fallbackRunCount;

    private uint[] CreateStaticObjectStorage(
        MapRenderScene scene,
        out byte[] schedulingIdentityCounts,
        out uint[] blockedLodMaskByObject)
    {
        int objectCapacity = 0;
        if (_normalCameraVisibilityWorldSource is { } worldSource)
        {
            uint worldObjectCount = worldSource.World.Dpvs.SModelCount;
            if (worldObjectCount <= int.MaxValue)
                objectCapacity = (int)worldObjectCount;
        }
        else
        {
            foreach (MapRenderStaticModelSchedulingInfo? row in
                     scene.StaticModelScheduling)
            {
                if (row is not null && row.ObjectIndex >= 0)
                {
                    objectCapacity = Math.Max(
                        objectCapacity,
                        checked(row.ObjectIndex + 1));
                }
            }
            foreach (MapRenderInstancedTexturedBatch batch in
                     scene.StaticModelLodTexturedBatches)
            {
                foreach (MapRenderStaticModelInstance instance in
                         batch.Instances)
                {
                    if (instance.ObjectIndex >= 0)
                    {
                        objectCapacity = Math.Max(
                            objectCapacity,
                            checked(instance.ObjectIndex + 1));
                    }
                }
            }
        }

        _normalCameraStaticIdentityKnown = new bool[objectCapacity];
        _normalCameraStaticVisible = new bool[objectCapacity];
        _normalCameraStaticSelectedLod = new int[objectCapacity];
        _normalCameraStaticFallbackLod = new int[objectCapacity];
        Array.Fill(_normalCameraStaticFallbackLod, -1);
        foreach (MapRenderInstancedTexturedBatch batch in
                 scene.InstancedTexturedBatches)
        {
            if ((uint)batch.LodIndex >= 32u)
                continue;
            foreach (MapRenderStaticModelInstance instance in
                     batch.Instances)
            {
                if ((uint)instance.ObjectIndex >= (uint)objectCapacity)
                    continue;
                int current =
                    _normalCameraStaticFallbackLod[instance.ObjectIndex];
                if (current == -1)
                {
                    _normalCameraStaticFallbackLod[
                        instance.ObjectIndex] = batch.LodIndex;
                }
                else if (current != batch.LodIndex)
                {
                    _normalCameraStaticFallbackLod[
                        instance.ObjectIndex] = -2;
                }
            }
        }
        schedulingIdentityCounts = new byte[objectCapacity];
        foreach (MapRenderStaticModelSchedulingInfo? row in
                 scene.StaticModelScheduling)
        {
            if (row is null ||
                (uint)row.ObjectIndex >= (uint)objectCapacity)
            {
                continue;
            }
            schedulingIdentityCounts[row.ObjectIndex] =
                schedulingIdentityCounts[row.ObjectIndex] == 0
                    ? (byte)1
                    : (byte)2;
        }
        blockedLodMaskByObject = new uint[objectCapacity];
        return new uint[objectCapacity];
    }

    private MapRenderWorldDpvsViewVisibility?
        ResolveNormalCameraDpvsVisibility(
            RenderCamera camera,
            MapRenderNormalCameraFramebufferExtent extent,
            MapRenderNormalCameraFarPlaneState farPlane,
            MetalNormalCameraVisibilityKey key)
    {
        if (_publishedShadowVisibilityFrameIndex == _frameIndex &&
            _publishedShadowVisibilityKey == key)
        {
            return _publishedShadowCameraVisibility;
        }
        if (_normalCameraVisibilityWorldSource is not { } worldSource ||
            _normalCameraDpvsCache is not { } cache)
        {
            return null;
        }

        try
        {
            MapRenderWorldDpvsCameraOnlyVisibilityBuildResult result =
                cache.Build(
                    worldSource.World,
                    camera,
                    extent,
                    farPlane);
            return result.IsSuccess &&
                   HasMatchingWorldCardinality(
                       result.Visibility!,
                       worldSource)
                ? result.Visibility
                : null;
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            InvalidDataException or
            InvalidOperationException or
            NotSupportedException or
            OverflowException or
            AggregateException)
        {
            // An unavailable DPVS traversal does not invalidate the already
            // built exact host frustum. Static selection and world bounds can
            // continue conservatively without DPVS bits.
            return null;
        }
    }

    private static void MarkOmittedStaticLods(
        MapRenderScene scene,
        RenderNormalCameraDrawSnapshot snapshot,
        Span<uint> blockedLodMaskByObject)
    {
        IReadOnlyList<MapRenderInstancedTexturedBatch> sourceBatches =
            snapshot.Coverage ==
                RenderNormalCameraDrawCoverage
                    .PreparedWorldAndAllStaticLodBatchesWithoutDpvsSelection
                ? scene.StaticModelLodTexturedBatches
                : scene.InstancedTexturedBatches;
        foreach (RenderNormalCameraDrawOmissionSnapshot omission in
                 snapshot.Omissions)
        {
            if (omission.SourceKind !=
                    RenderNormalCameraDrawSourceKind.StaticModel ||
                omission.StaticReceiverVariant.HasValue ||
                omission.CollectionOrdinal is not { } collectionOrdinal ||
                (uint)collectionOrdinal >= (uint)sourceBatches.Count)
            {
                continue;
            }

            MapRenderInstancedTexturedBatch batch =
                sourceBatches[collectionOrdinal];
            if ((uint)batch.LodIndex >= 32u)
                continue;
            uint lodBit = 1u << batch.LodIndex;
            foreach (MapRenderStaticModelInstance instance in
                     batch.Instances)
            {
                if ((uint)instance.ObjectIndex <
                    (uint)blockedLodMaskByObject.Length)
                {
                    blockedLodMaskByObject[instance.ObjectIndex] |= lodBit;
                }
            }
        }
    }

    private static MetalNormalCameraVisibilityGroupPlan
        CreateVisibilityGroupPlan(
            MapRenderEditorDrawGroup<
                RenderNormalCameraDrawSubmissionSnapshot> group,
            int? isolatedWorldSurfaceIndex)
    {
        ReadOnlySpan<RenderNormalCameraDrawSubmissionSnapshot>
            authoredPasses = group.AuthoredPassSpan;
        if (authoredPasses.IsEmpty)
            return MetalNormalCameraVisibilityGroupPlan.FailOpen(group, 1);

        RenderNormalCameraDrawSubmissionSnapshot firstDraw =
            authoredPasses[0];
        RenderNormalCameraPreparedPassSnapshot firstPass =
            firstDraw.PreparedPass;
        if (firstPass.SourceKind ==
            RenderNormalCameraDrawSourceKind.World)
        {
            RenderBounds bounds = RenderBounds.Empty;
            var worldPassSpans = new MapRenderWorldSurfaceSpan[
                authoredPasses.Length][];
            for (int passIndex = 0;
                 passIndex < authoredPasses.Length;
                 passIndex++)
            {
                RenderNormalCameraDrawSubmissionSnapshot draw =
                    authoredPasses[passIndex];
                RenderNormalCameraPreparedPassSnapshot pass =
                    draw.PreparedPass;
                if (pass.SourceKind !=
                    RenderNormalCameraDrawSourceKind.World)
                {
                    return MetalNormalCameraVisibilityGroupPlan.FailOpen(
                        group,
                        firstDraw.Range.InstanceCount);
                }

                bool hasPassSpans = isolatedWorldSurfaceIndex is int
                    isolatedSurfaceIndex
                        ? TryCreateIsolatedWorldSurfaceSpans(
                            pass,
                            isolatedSurfaceIndex,
                            out MapRenderWorldSurfaceSpan[] passSpans)
                        : MapRenderWorldSurfaceSpanCatalog.TryCreate(
                            pass,
                            out passSpans);
                if (!hasPassSpans)
                {
                    return MetalNormalCameraVisibilityGroupPlan.FailOpen(
                        group,
                        firstDraw.Range.InstanceCount);
                }
                worldPassSpans[passIndex] = passSpans;
                bounds = bounds
                    .Include(pass.LocalBounds.Min)
                    .Include(pass.LocalBounds.Max);
            }
            return bounds.IsValid || isolatedWorldSurfaceIndex.HasValue
                ? MetalNormalCameraVisibilityGroupPlan.World(
                    group,
                    worldPassSpans,
                    bounds,
                    firstDraw.Range.InstanceCount)
                : MetalNormalCameraVisibilityGroupPlan.FailOpen(
                    group,
                    firstDraw.Range.InstanceCount);
        }

        int? lodIndex = firstPass.LodIndex;
        if (lodIndex is null || (uint)lodIndex.Value >= 32u)
        {
            return MetalNormalCameraVisibilityGroupPlan.FailOpen(
                group,
                firstDraw.Range.InstanceCount);
        }
        int firstInstance = firstDraw.Range.FirstInstance;
        int instanceCount = firstDraw.Range.InstanceCount;
        if (firstInstance < 0 ||
            instanceCount <= 0 ||
            firstInstance > firstPass.StaticInstances.Length - instanceCount)
        {
            return MetalNormalCameraVisibilityGroupPlan.FailOpen(
                group,
                instanceCount);
        }

        for (int passIndex = 0;
             passIndex < authoredPasses.Length;
             passIndex++)
        {
            RenderNormalCameraDrawSubmissionSnapshot draw =
                authoredPasses[passIndex];
            RenderNormalCameraPreparedPassSnapshot pass =
                draw.PreparedPass;
            if (pass.SourceKind !=
                    RenderNormalCameraDrawSourceKind.StaticModel ||
                pass.LodIndex != lodIndex ||
                draw.Range.FirstInstance != firstInstance ||
                draw.Range.InstanceCount != instanceCount ||
                !pass.StaticInstances.AsSpan().SequenceEqual(
                    firstPass.StaticInstances.AsSpan()))
            {
                return MetalNormalCameraVisibilityGroupPlan.FailOpen(
                    group,
                    instanceCount);
            }
        }

        return MetalNormalCameraVisibilityGroupPlan.Static(
            group,
            firstPass,
            lodIndex.Value,
            firstInstance,
            instanceCount);
    }

    private static bool TryCreateIsolatedWorldSurfaceSpans(
        RenderNormalCameraPreparedPassSnapshot pass,
        int isolatedSurfaceIndex,
        out MapRenderWorldSurfaceSpan[] spans)
    {
        spans = [];
        if (isolatedSurfaceIndex < 0 ||
            pass.SourceKind != RenderNormalCameraDrawSourceKind.World)
        {
            return false;
        }

        var matches = new List<MapRenderWorldSurfaceSpan>();
        foreach (RenderMaterialPickRangeSnapshot range in pass.PickRanges)
        {
            if (range.Kind != MapRenderPickKind.GfxSurface ||
                range.SurfaceIndex != isolatedSurfaceIndex)
            {
                continue;
            }

            int endIndex;
            try
            {
                endIndex = checked(range.FirstIndex + range.IndexCount);
            }
            catch (OverflowException)
            {
                return false;
            }
            if (range.FirstIndex < 0 ||
                range.IndexCount <= 0 ||
                range.IndexCount % 3 != 0 ||
                endIndex > pass.Geometry.IndexCount)
            {
                return false;
            }

            matches.Add(new MapRenderWorldSurfaceSpan(
                isolatedSurfaceIndex,
                range.FirstIndex,
                range.IndexCount,
                RenderBounds.Empty));
        }

        if (matches.Count == 0)
            return false;
        spans = matches.ToArray();
        return true;
    }

    private static bool HasMatchingWorldCardinality(
        MapRenderWorldDpvsViewVisibility visibility,
        MapRenderWorldSceneSource worldSource) =>
        visibility.ViewIndex == MapRenderWorldDpvsViewIndex.Camera &&
        visibility.SurfaceCount == worldSource.World.SurfaceCount &&
        worldSource.World.Dpvs.SModelCount <= int.MaxValue &&
        visibility.StaticModelCount ==
            (int)worldSource.World.Dpvs.SModelCount;

    private readonly record struct MetalNormalCameraVisibilityKey(
        RenderCamera Camera,
        MapRenderNormalCameraFramebufferExtent Extent,
        float RZFar,
        float RendererFallback);

    private readonly record struct MetalNormalCameraVisibleInstanceRun(
        int FirstInstance,
        int InstanceCount,
        bool PreserveSourceRange);

    private readonly record struct
        MetalNormalCameraWorldReceiverCandidateKey(
            int SurfaceIndex,
            MapRenderWorldReceiverVariantKey Variant);

    private readonly record struct
        MetalNormalCameraStaticReceiverCandidateKey(
            int IdentityOrdinal,
            MapRenderStaticModelReceiverVariantKey Variant,
            int TechniqueSlot);

    private readonly record struct MetalNormalCameraReceiverCandidate(
        MetalNormalCameraVisibilityGroupPlan? Plan,
        bool IsAmbiguous);

    private sealed class MetalNormalCameraVisibilityGroupPlan
    {
        private MetalNormalCameraVisibilityGroupPlan(
            MapRenderEditorDrawGroup<
                RenderNormalCameraDrawSubmissionSnapshot> group,
            MapRenderWorldSurfaceSpan[][]? worldPassSpans,
            RenderBounds worldBounds,
            RenderNormalCameraPreparedPassSnapshot? staticPass,
            int staticLodIndex,
            int firstInstance,
            int sourceInstanceCount)
        {
            Group = group ?? throw new ArgumentNullException(nameof(group));
            WorldPassSpans = worldPassSpans ?? [];
            WorldSpans = WorldPassSpans.Length == 0
                ? []
                : WorldPassSpans[0];
            WorldPassVisibleRuns = new MapRenderWorldVisibleRun[
                WorldPassSpans.Length][];
            WorldPassVisibleRunCounts = new int[WorldPassSpans.Length];
            WorldPassVisibleSurfaceCounts = new int[
                WorldPassSpans.Length];
            WorldPassVisibleIndexCounts = new long[WorldPassSpans.Length];
            int maximumWorldSurfaceIndex = -1;
            for (int passIndex = 0;
                 passIndex < WorldPassSpans.Length;
                 passIndex++)
            {
                WorldPassVisibleRuns[passIndex] =
                    new MapRenderWorldVisibleRun[
                        WorldPassSpans[passIndex].Length];
                foreach (MapRenderWorldSurfaceSpan span in
                         WorldPassSpans[passIndex])
                {
                    maximumWorldSurfaceIndex = Math.Max(
                        maximumWorldSurfaceIndex,
                        span.SurfaceIndex);
                }
            }
            WorldRouteVisibleWords = maximumWorldSurfaceIndex < 0
                ? []
                : new uint[checked(
                    (maximumWorldSurfaceIndex + 32) / 32)];
            WorldBounds = worldBounds;
            StaticPass = staticPass;
            StaticLodIndex = staticLodIndex;
            FirstInstance = firstInstance;
            SourceInstanceCount = Math.Max(1, sourceInstanceCount);
            StaticVisibleRuns = new MetalNormalCameraVisibleInstanceRun[
                SourceInstanceCount];
            StaticRouteIdentityOrdinals = new int[SourceInstanceCount];
            StaticRouteIdentityOrdinals.AsSpan().Fill(-1);

            RenderNormalCameraPreparedPassSnapshot firstPass =
                group.AuthoredPassSpan[0].PreparedPass;
            WorldReceiverVariant = firstPass.WorldReceiverVariant;
            StaticReceiverVariant = firstPass.StaticReceiverVariant;
            SourceKind = firstPass.SourceKind;
            GeometryIndexCount = firstPass.Geometry.IndexCount;
            SceneLightIndex = firstPass.SceneLightIndex;
            TechniqueSlot = firstPass.SourcePass.TechniqueSlot;
            bool commonReceiverShapeValid = true;
            ReadOnlySpan<RenderNormalCameraDrawSubmissionSnapshot>
                authoredPasses = group.AuthoredPassSpan;
            for (int passIndex = 0;
                 passIndex < authoredPasses.Length;
                 passIndex++)
            {
                RenderNormalCameraPreparedPassSnapshot pass =
                    authoredPasses[passIndex].PreparedPass;
                if (pass.WorldReceiverVariant != WorldReceiverVariant ||
                    pass.StaticReceiverVariant != StaticReceiverVariant ||
                    pass.SourceKind != SourceKind ||
                    pass.Geometry.IndexCount != GeometryIndexCount ||
                    pass.SceneLightIndex != SceneLightIndex ||
                    pass.SourcePass.TechniqueSlot != TechniqueSlot)
                {
                    commonReceiverShapeValid = false;
                    break;
                }
            }

            if (CanFilterWorld)
            {
                WorldReceiverSurfaceIndices = commonReceiverShapeValid
                    ? CreateCompleteWorldReceiverSurfaceIndices(
                        WorldPassSpans)
                    : [];
                StaticReceiverIdentities = [];
                ReceiverSelectionShapeValid =
                    WorldReceiverSurfaceIndices.Length != 0;
            }
            else if (CanFilterStaticInstances)
            {
                WorldReceiverSurfaceIndices = [];
                StaticReceiverIdentities = new
                    MapRenderStaticModelReceiverIdentity[
                        SourceInstanceCount];
                for (int offset = 0;
                     offset < SourceInstanceCount;
                     offset++)
                {
                    MapRenderStaticModelInstance instance =
                        staticPass!.StaticInstances[
                            checked(firstInstance + offset)];
                    StaticReceiverIdentities[offset] = new(
                        instance,
                        staticLodIndex);
                    if (instance.PrimaryLightIndex != SceneLightIndex)
                        commonReceiverShapeValid = false;
                }
                ReceiverSelectionShapeValid = commonReceiverShapeValid;
            }
            else
            {
                WorldReceiverSurfaceIndices = [];
                StaticReceiverIdentities = [];
                ReceiverSelectionShapeValid = false;
            }
        }

        internal MapRenderEditorDrawGroup<
            RenderNormalCameraDrawSubmissionSnapshot> Group { get; }

        internal RenderNormalCameraDrawSourceKind SourceKind { get; }

        internal MapRenderWorldReceiverVariantKey? WorldReceiverVariant
            { get; }

        internal MapRenderStaticModelReceiverVariantKey?
            StaticReceiverVariant { get; }

        internal bool HasReceiverVariant =>
            WorldReceiverVariant.HasValue ||
            StaticReceiverVariant.HasValue;

        internal bool IsAuthorized { get; set; }

        internal int RouteOrdinal { get; set; }

        internal int SelectedRouteIdentityCount { get; set; }

        internal int ExpectedRouteOwner => HasReceiverVariant
            ? RouteOrdinal
            : 0;

        internal int GeometryIndexCount { get; }

        internal byte SceneLightIndex { get; }

        internal int TechniqueSlot { get; }

        internal int[] WorldReceiverSurfaceIndices { get; }

        internal MapRenderStaticModelReceiverIdentity[]
            StaticReceiverIdentities { get; }

        internal int[] StaticRouteIdentityOrdinals { get; }

        internal bool ReceiverSelectionShapeValid { get; private set; }

        internal bool CanFilterWorld => WorldSpans.Length != 0;

        internal bool CanFilterStaticInstances => StaticPass is not null;

        internal MapRenderWorldSurfaceSpan[][] WorldPassSpans { get; }

        internal MapRenderWorldSurfaceSpan[] WorldSpans { get; }

        internal MapRenderWorldVisibleRun[][] WorldPassVisibleRuns { get; }

        internal uint[] WorldRouteVisibleWords { get; }

        internal int[] WorldPassVisibleRunCounts { get; }

        internal int[] WorldPassVisibleSurfaceCounts { get; }

        internal long[] WorldPassVisibleIndexCounts { get; }

        internal RenderBounds WorldBounds { get; }

        internal RenderNormalCameraPreparedPassSnapshot? StaticPass { get; }

        internal int StaticLodIndex { get; }

        internal int FirstInstance { get; }

        internal int SourceInstanceCount { get; }

        internal MetalNormalCameraVisibleInstanceRun[] StaticVisibleRuns
            { get; }

        internal long PreparedFrameIndex { get; private set; } = -1;

        internal int VisibleRunCount { get; private set; } = 1;

        internal int VisibleInstanceCount { get; private set; }

        internal int VisibleWorldSurfaceCount { get; private set; }

        internal long VisibleWorldIndexCount { get; private set; }

        internal int WorldSurfaceSpanCount =>
            WorldPassSpans.Sum(spans => spans.Length);

        internal bool ContainsWorldSurface(int surfaceIndex)
        {
            foreach (MapRenderWorldSurfaceSpan[] passSpans in WorldPassSpans)
            foreach (MapRenderWorldSurfaceSpan span in passSpans)
            {
                if (span.SurfaceIndex == surfaceIndex)
                    return true;
            }
            return false;
        }

        internal bool PreservesOriginalRange { get; private set; } = true;

        internal void InvalidatePreparedRuns() => PreparedFrameIndex = -1;

        internal void AssignStaticRouteIdentityOrdinals(
            IReadOnlyDictionary<MapRenderStaticModelReceiverIdentity, int>
                ordinals)
        {
            ArgumentNullException.ThrowIfNull(ordinals);
            for (int offset = 0;
                 offset < StaticReceiverIdentities.Length;
                 offset++)
            {
                if (ordinals.TryGetValue(
                        StaticReceiverIdentities[offset],
                        out int ordinal))
                {
                    StaticRouteIdentityOrdinals[offset] = ordinal;
                }
            }
        }

        internal bool IsStaticRouteIdentitySelected(
            int instanceOffset,
            ReadOnlySpan<int> routeOwners)
        {
            if ((uint)instanceOffset >=
                (uint)StaticRouteIdentityOrdinals.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(instanceOffset));
            }
            int identityOrdinal =
                StaticRouteIdentityOrdinals[instanceOffset];
            if ((uint)identityOrdinal >= (uint)routeOwners.Length)
                return !HasReceiverVariant;
            return routeOwners[identityOrdinal] == ExpectedRouteOwner;
        }

        internal void PublishOriginal(long frameIndex)
        {
            PreparedFrameIndex = frameIndex;
            VisibleRunCount = 1;
            VisibleInstanceCount = SourceInstanceCount;
            VisibleWorldSurfaceCount = CanFilterWorld ? WorldSpans.Length : 0;
            VisibleWorldIndexCount = CanFilterWorld
                ? WorldSpans.Sum(span => (long)span.IndexCount)
                : 0;
            PreservesOriginalRange = true;
        }

        internal void PublishWorld(
            long frameIndex,
            ReadOnlySpan<uint> surfaceWords,
            bool hasDpvsVisibility,
            ReadOnlySpan<int> routeOwners,
            int? isolatedSurfaceIndex)
        {
            PreparedFrameIndex = frameIndex;
            VisibleRunCount = 0;
            VisibleWorldSurfaceCount = 0;
            VisibleWorldIndexCount = 0;
            Array.Clear(WorldRouteVisibleWords);
            for (int passIndex = 0;
                 passIndex < WorldPassSpans.Length;
                 passIndex++)
            {
                foreach (MapRenderWorldSurfaceSpan span in
                         WorldPassSpans[passIndex])
                {
                    if ((!isolatedSurfaceIndex.HasValue ||
                         span.SurfaceIndex == isolatedSurfaceIndex.Value) &&
                        IsWorldSurfaceRouteSelected(
                            span.SurfaceIndex,
                            routeOwners) &&
                        IsWorldSurfaceDpvsVisible(
                            span.SurfaceIndex,
                            surfaceWords,
                            hasDpvsVisibility))
                    {
                        SetWorldSurfaceVisible(
                            WorldRouteVisibleWords,
                            span.SurfaceIndex);
                    }
                }
            }

            for (int passIndex = 0;
                 passIndex < WorldPassSpans.Length;
                 passIndex++)
            {
                MapRenderWorldSurfaceCompactionResult compaction =
                    MapRenderWorldSurfaceRunCompactor.Compact(
                        WorldPassSpans[passIndex],
                        WorldRouteVisibleWords,
                        hasDpvsVisibility: true,
                        frustum: null,
                        WorldPassVisibleRuns[passIndex]);
                WorldPassVisibleRunCounts[passIndex] =
                    compaction.RunCount;
                WorldPassVisibleSurfaceCounts[passIndex] =
                    compaction.VisibleSurfaceSpanCount;
                WorldPassVisibleIndexCounts[passIndex] =
                    compaction.VisibleIndexCount;
                VisibleRunCount = checked(
                    VisibleRunCount + compaction.RunCount);
                VisibleWorldSurfaceCount = checked(
                    VisibleWorldSurfaceCount +
                    compaction.VisibleSurfaceSpanCount);
                VisibleWorldIndexCount = checked(
                    VisibleWorldIndexCount +
                    compaction.VisibleIndexCount);
            }
            VisibleInstanceCount = VisibleRunCount == 0 ? 0 : 1;
            PreservesOriginalRange = false;
        }

        private bool IsWorldSurfaceRouteSelected(
            int surfaceIndex,
            ReadOnlySpan<int> routeOwners)
        {
            if ((uint)surfaceIndex >= (uint)routeOwners.Length)
                return !HasReceiverVariant;
            return routeOwners[surfaceIndex] == ExpectedRouteOwner;
        }

        private static bool IsWorldSurfaceDpvsVisible(
            int surfaceIndex,
            ReadOnlySpan<uint> surfaceWords,
            bool hasDpvsVisibility)
        {
            if (!hasDpvsVisibility)
                return true;
            int wordIndex = surfaceIndex >> 5;
            return (uint)wordIndex >= (uint)surfaceWords.Length ||
                (surfaceWords[wordIndex] &
                    (0x8000_0000u >> (surfaceIndex & 31))) != 0;
        }

        private static void SetWorldSurfaceVisible(
            Span<uint> surfaceWords,
            int surfaceIndex)
        {
            int wordIndex = surfaceIndex >> 5;
            if ((uint)wordIndex >= (uint)surfaceWords.Length)
            {
                throw new InvalidOperationException(
                    "The normal-camera world route mask does not cover an authored surface.");
            }
            surfaceWords[wordIndex] |=
                0x8000_0000u >> (surfaceIndex & 31);
        }

        private static int[] CreateCompleteWorldReceiverSurfaceIndices(
            IReadOnlyList<MapRenderWorldSurfaceSpan[]> passSpans)
        {
            if (passSpans.Count == 0 || passSpans[0].Length == 0)
                return [];

            var result = new List<int>(passSpans[0].Length);
            ReadOnlySpan<MapRenderWorldSurfaceSpan> firstPass = passSpans[0];
            for (int candidateIndex = 0;
                 candidateIndex < firstPass.Length;
                 candidateIndex++)
            {
                MapRenderWorldSurfaceSpan candidate =
                    firstPass[candidateIndex];
                bool complete = true;
                for (int passIndex = 0;
                     passIndex < passSpans.Count && complete;
                     passIndex++)
                {
                    int matchingSpanCount = 0;
                    foreach (MapRenderWorldSurfaceSpan span in
                             passSpans[passIndex])
                    {
                        if (span.SurfaceIndex != candidate.SurfaceIndex)
                            continue;
                        matchingSpanCount++;
                        if (span.IndexCount != candidate.IndexCount)
                        {
                            complete = false;
                            break;
                        }
                    }
                    complete &= matchingSpanCount == 1;
                }
                if (complete)
                    result.Add(candidate.SurfaceIndex);
            }
            return result.ToArray();
        }

        internal void PublishWorldCulled(long frameIndex)
        {
            PreparedFrameIndex = frameIndex;
            VisibleRunCount = 0;
            VisibleInstanceCount = 0;
            VisibleWorldSurfaceCount = 0;
            VisibleWorldIndexCount = 0;
            Array.Clear(WorldPassVisibleRunCounts);
            Array.Clear(WorldPassVisibleSurfaceCounts);
            Array.Clear(WorldPassVisibleIndexCounts);
            PreservesOriginalRange = false;
        }

        internal int ResolveVisibleRunCount(int passIndex)
        {
            if (!CanFilterWorld)
                return VisibleRunCount;
            if ((uint)passIndex >=
                (uint)WorldPassVisibleRunCounts.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(passIndex));
            }
            return WorldPassVisibleRunCounts[passIndex];
        }

        internal MapRenderWorldVisibleRun ResolveWorldVisibleRun(
            int passIndex,
            int runIndex)
        {
            if (!CanFilterWorld ||
                (uint)passIndex >= (uint)WorldPassVisibleRuns.Length ||
                (uint)runIndex >=
                    (uint)WorldPassVisibleRunCounts[passIndex])
            {
                throw new ArgumentOutOfRangeException();
            }
            return WorldPassVisibleRuns[passIndex][runIndex];
        }

        internal void PublishStatic(
            long frameIndex,
            int runCount,
            int visibleInstanceCount)
        {
            PreparedFrameIndex = frameIndex;
            VisibleRunCount = runCount;
            VisibleInstanceCount = visibleInstanceCount;
            VisibleWorldSurfaceCount = 0;
            VisibleWorldIndexCount = 0;
            PreservesOriginalRange = false;
        }

        internal static MetalNormalCameraVisibilityGroupPlan FailOpen(
            MapRenderEditorDrawGroup<
                RenderNormalCameraDrawSubmissionSnapshot> group,
            int sourceInstanceCount) =>
            new(
                group,
                worldPassSpans: null,
                RenderBounds.Empty,
                staticPass: null,
                staticLodIndex: -1,
                firstInstance: 0,
                sourceInstanceCount);

        internal static MetalNormalCameraVisibilityGroupPlan World(
            MapRenderEditorDrawGroup<
                RenderNormalCameraDrawSubmissionSnapshot> group,
            MapRenderWorldSurfaceSpan[][] passSpans,
            RenderBounds bounds,
            int sourceInstanceCount) =>
            new(
                group,
                passSpans,
                bounds,
                staticPass: null,
                staticLodIndex: -1,
                firstInstance: 0,
                sourceInstanceCount);

        internal static MetalNormalCameraVisibilityGroupPlan Static(
            MapRenderEditorDrawGroup<
                RenderNormalCameraDrawSubmissionSnapshot> group,
            RenderNormalCameraPreparedPassSnapshot pass,
            int lodIndex,
            int firstInstance,
            int sourceInstanceCount) =>
            new(
                group,
                worldPassSpans: null,
                RenderBounds.Empty,
                pass,
                lodIndex,
                firstInstance,
                sourceInstanceCount);
    }
}
