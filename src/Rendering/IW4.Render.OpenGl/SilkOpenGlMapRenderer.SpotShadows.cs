using System.Numerics;
using IW4.Render.Diagnostics;
using IW4.Render.Geometry.Shadows;
using IW4.Render.OpenGl.Shadows;
using IW4.Render.Scheduling;
using IW4.Render.Scheduling.Dpvs;
using IW4.Render.Scheduling.Shadows;
using IW4.Render.SceneBuilding;
using Silk.NET.OpenGL;
using SpotShadowPlan =
    IW4.Render.Scheduling.Shadows.MapRenderSpotShadowPlan;

namespace IW4.Render.OpenGl;

public sealed unsafe partial class SilkOpenGlMapRenderer
{
    private readonly string _spotShadowAtlasContextIdentity =
        $"silk-spot-shadow-atlas:{Guid.NewGuid():N}";
    private MapRenderOpenGlSpotShadowAtlasBackend? _spotShadowAtlas;
    private MapRenderSpotShadowAtlasReadyState?
        _currentSpotShadowReadyState;
    private MapRenderOpenGlSpotShadowAtlasReadyFrame?
        _currentSpotShadowBackendReadyFrame;
    private MapRenderSpotShadowPlanner? _spotShadowPlanner;
    private MapRenderOpenGlSpotShadowAtlasBackend?
        _spotShadowAtlasContentBackend;
    private MapRenderWorldDpvsVisibilityBuildResult?
        _spotShadowAtlasContentVisibility;
    private SpotShadowPlan[] _spotShadowAtlasContentPlans = [];
    private MapRenderSpotShadowAtlasEntry[]
        _spotShadowAtlasContentEntries = [];
    private MapRenderOpenGlSpotShadowEntryDescriptor[]
        _spotShadowAtlasContentDescriptors = [];
    private readonly HashSet<uint> _spotShadowAtlasContentTextureHandles = [];
    private readonly HashSet<uint> _spotShadowFrameTextureHandles = [];

    private void InitializeSpotShadowPipeline()
    {
        ClearCurrentSpotShadowFrame();
        ClearSpotShadowCasterMembership();
        _spotShadowAtlas?.Dispose();
        _spotShadowAtlas = null;

        if (_previewWorldSource is not { } source ||
            _editorPreviewSceneLightFrame is not { } sceneLights ||
            _sunShadowStaticCasterIndex is null ||
            _sceneTechniqueVariants is not { } techniqueCatalog)
        {
            return;
        }

        _spotShadowPlanner = MapRenderSpotShadowPlanner.TryCreate(
            source,
            sceneLights,
            techniqueCatalog);
        if (_spotShadowPlanner is null)
            return;

        try
        {
            _spotShadowAtlas = new MapRenderOpenGlSpotShadowAtlasBackend(
                _gl,
                _state,
                _spotShadowAtlasContextIdentity);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            ArgumentException or
            NotSupportedException or
            AggregateException)
        {
            // Local spot shadows are an independent optional target. The
            // completed directional-sun path remains usable when this context
            // cannot allocate the native 512x2048 comparison atlas.
        }
    }

    private void RenderSpotShadowFrame(
        MapRenderWorldDpvsThreeViewFrame frame,
        MapRenderSunShadowAtlasReadyState? sunAtlasReady,
        bool sunReceiverSelectionPrepared)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ClearCurrentSpotShadowFrame();
        if (_spotShadowAtlas is not { } atlas ||
            (sunAtlasReady is not null &&
             !ReferenceEquals(sunAtlasReady.Frame, frame)))
        {
            InvalidateSpotShadowAtlasContentCache();
            EnsureSunShadowReceiverSelection(
                frame,
                sunAtlasReady,
                sunReceiverSelectionPrepared);
            return;
        }

        IReadOnlyList<SpotShadowPlan> plans;
        MapRenderSpotShadowAtlasEntry[] entries;
        MapRenderOpenGlSpotShadowEntryDescriptor[]? descriptors;
        try
        {
            if (TryGetCachedSpotShadowPlanning(
                    atlas,
                    frame,
                    out SpotShadowPlan[] cachedPlans,
                    out MapRenderSpotShadowAtlasEntry[] cachedEntries,
                    out MapRenderOpenGlSpotShadowEntryDescriptor[]
                        cachedDescriptors))
            {
                plans = cachedPlans;
                entries = cachedEntries;
                descriptors = cachedDescriptors;
            }
            else
            {
                plans = CreateSpotShadowPlans(frame);
                entries = plans.Count == 0
                    ? []
                    : new MapRenderSpotShadowAtlasEntry[plans.Count];
                for (int index = 0; index < plans.Count; index++)
                    entries[index] = plans[index].Entry;
                descriptors = null;
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            ArgumentException or
            NotSupportedException)
        {
            InvalidateSpotShadowAtlasContentCache();
            EnsureSunShadowReceiverSelection(
                frame,
                sunAtlasReady,
                sunReceiverSelectionPrepared);
            return;
        }

        if (plans.Count == 0)
        {
            InvalidateSpotShadowAtlasContentCache();
            EnsureSunShadowReceiverSelection(
                frame,
                sunAtlasReady,
                sunReceiverSelectionPrepared);
            return;
        }

        bool spotCasterWriteEntered = false;
        try
        {
            bool reuseAtlasContents =
                CanReuseSpotShadowAtlasContents(atlas, plans);
            if (!reuseAtlasContents)
            {
                for (int index = 0; index < plans.Count; index++)
                {
                    SpotShadowPlan plan = plans[index];
                    EnsureSpotCasterCoverage(
                        plan,
                        frame.Projection.CameraOrigin);
                }

                // Resolve every +3 receiver before beginning the first depth
                // write. The selector remains preflight-only until both the
                // renderer-neutral and OpenGL publications are complete.
                PrepareWorldReceiverVariantSelection(
                    frame,
                    sunAtlasReady,
                    spotShadowPreflightEntries: entries);
            }

            var publication = new MapRenderSpotShadowFramePublication(
                frame,
                entries);
            if (descriptors is null)
            {
                descriptors = new MapRenderOpenGlSpotShadowEntryDescriptor[
                    entries.Length];
                for (int index = 0; index < entries.Length; index++)
                {
                    MapRenderSpotShadowAtlasEntry entry = entries[index];
                    descriptors[index] =
                        new MapRenderOpenGlSpotShadowEntryDescriptor(
                            entry.SceneLightIndex,
                            entry.AtlasSlot,
                            entry.ShadowLookupMatrix,
                            entry.Fade);
                }
            }
            if (reuseAtlasContents)
            {
                atlas.BeginReusedFrame(frame.Revision, descriptors);
                for (int index = 0; index < plans.Count; index++)
                {
                    SpotShadowPlan plan = plans[index];
                    if (!publication.RecordEntryDrawCompleted(
                            frame.Revision,
                            plan.Entry.SceneLightIndex))
                    {
                        throw new InvalidOperationException(
                            $"Reused spot-shadow tile {plan.Entry.AtlasSlot} for scene light {plan.Entry.SceneLightIndex} completed more than once.");
                    }
                }
                if (!publication.TryGetAtlasReady(
                        out MapRenderSpotShadowAtlasReadyState?
                            reusedReadyState) ||
                    reusedReadyState is null ||
                    !atlas.TryGetReadyFrame(
                        frame.Revision,
                        out MapRenderOpenGlSpotShadowAtlasReadyFrame?
                            reusedBackendReady) ||
                    reusedBackendReady is null)
                {
                    throw new InvalidOperationException(
                        "A proven unchanged spot-shadow atlas could not be published for the current frame.");
                }
                ValidateSpotShadowPublications(
                    reusedReadyState,
                    reusedBackendReady);
                // No target was modified, so the destructive-write preflight
                // ordering is unnecessary. Retain the exact receiver closure
                // only when its complete immutable and published input state
                // still matches; otherwise preserve the full fail-closed walk.
                if (!TryReuseWorldReceiverVariantSelection(
                        frame,
                        sunAtlasReady,
                        reusedReadyState))
                {
                    PrepareWorldReceiverVariantSelection(
                        frame,
                        sunAtlasReady,
                        spotAtlasReady: reusedReadyState);
                }
                _currentSpotShadowReadyState = reusedReadyState;
                _currentSpotShadowBackendReadyFrame =
                    reusedBackendReady;
                return;
            }

            InvalidateSpotShadowAtlasContentCache();
            _spotShadowFrameTextureHandles.Clear();
            atlas.BeginFrame(frame.Revision, descriptors);
            for (int index = 0; index < plans.Count; index++)
            {
                SpotShadowPlan plan = plans[index];
                MapRenderSpotShadowAtlasEntry entry = plan.Entry;
                // BeginTile can touch framebuffer, viewport, scissor, and
                // depth/stencil state before a later operation fails.
                spotCasterWriteEntered = true;
                using MapRenderOpenGlSpotShadowAtlasTileScope scope =
                    atlas.BeginTile(entry.AtlasSlot);
                Matrix4x4 hostViewProjection =
                    OpenGlRsxClipSpaceLowering
                        .CreateShadowCasterHostViewProjection(
                            entry.CasterViewProjection);
                DrawShadowCasterSelection(
                    plan.WorldSurfaceIndices,
                    plan.StaticDrawInstances,
                    frame.Projection.CameraOrigin,
                    hostViewProjection,
                    partitionRuntimeIndex: 0,
                    reuseCommittedStaticSelection: false,
                    polygonOffsetFactor:
                        Ps3SpotShadowPolygonOffsetFactor,
                    polygonOffsetUnits:
                        Ps3SpotShadowPolygonOffsetUnits);
                RecordSpotShadowTextureUsage(plan);
                scope.Complete();
                if (!publication.RecordEntryDrawCompleted(
                        frame.Revision,
                        entry.SceneLightIndex))
                {
                    throw new InvalidOperationException(
                        $"Spot-shadow tile {entry.AtlasSlot} for scene light {entry.SceneLightIndex} completed more than once.");
                }
                _frameTelemetry.AddCounter(MapRenderFrameCounter.Passes);
            }

            if (!publication.TryGetAtlasReady(
                    out MapRenderSpotShadowAtlasReadyState? readyState) ||
                readyState is null ||
                !atlas.TryGetReadyFrame(
                    frame.Revision,
                    out MapRenderOpenGlSpotShadowAtlasReadyFrame?
                        backendReady) ||
                backendReady is null)
            {
                throw new InvalidOperationException(
                    "All planned spot-shadow tiles completed without matching atomic same-revision publications.");
            }
            ValidateSpotShadowPublications(readyState, backendReady);
            AuthorizePreflightedWorldReceiverVariantSelection(
                frame,
                sunAtlasReady,
                readyState);
            _currentSpotShadowReadyState = readyState;
            _currentSpotShadowBackendReadyFrame = backendReady;
            RememberSpotShadowAtlasContents(
                atlas,
                plans,
                entries,
                descriptors);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            ArgumentException or
            NotSupportedException)
        {
            ClearCurrentSpotShadowFrame();
            InvalidateSpotShadowAtlasContentCache();
            // The directional atlas was already atomically authorized before
            // local-light work began. Rebuild that exact selection after a
            // local failure so no preflight selector can leak into drawing.
            PrepareWorldReceiverVariantSelection(frame, sunAtlasReady);
        }
        finally
        {
            if (spotCasterWriteEntered)
            {
                // Spot tiles borrow partition zero's dynamic instance-buffer
                // slice. Never allow those selected transforms to satisfy a
                // later sun cache hit.
                foreach (MapRenderOpenGlSunShadowStaticCasterRuntime runtime in
                         _sunShadowStaticCasterRuntimes)
                {
                    runtime.GetPartition(0).InvalidateSelection();
                }
                _state.SetEnabled(EnableCap.ScissorTest, false);
                ApplyDefaultRenderState();
            }
        }
    }

    private IReadOnlyList<SpotShadowPlan> CreateSpotShadowPlans(
        MapRenderWorldDpvsThreeViewFrame frame) =>
        _spotShadowPlanner?.CreateFramePlans(frame) ?? [];

    private void EnsureSpotCasterCoverage(
        SpotShadowPlan plan,
        Vector3 nativeCameraOrigin)
    {
        _sunShadowCoverageWorldScratch.Clear();
        int nativeSelectorRejectedCount = 0;
        int unsupportedCount = 0;
        int retainedUnsupportedCount = 0;
        ClassifyWorldCasterCoverage(
            plan.WorldSurfaceIndices,
            ref nativeSelectorRejectedCount,
            ref unsupportedCount,
            ref retainedUnsupportedCount);
        if (unsupportedCount != 0)
        {
            string detail = string.Join(
                " | ",
                _sunShadowUnsupportedWorldSurfaceScratch
                    .Take(retainedUnsupportedCount)
                    .Select(surfaceIndex =>
                        _sunShadowWorldCasterRejectionsBySurface.TryGetValue(
                            surfaceIndex,
                            out MapRenderSunShadowWorldCasterRejection?
                                rejection)
                            ? $"{surfaceIndex}:{rejection.Kind}:{rejection.Detail}"
                            : $"{surfaceIndex}:unclassified"));
            throw new InvalidOperationException(
                $"Spot scene light {plan.Entry.SceneLightIndex} has {unsupportedCount} world caster surface(s) without exact slot-2 payload. {detail}");
        }

        _sunShadowCoverageStaticScratch.Clear();
        foreach (MapRenderSunShadowStaticCasterIdentity identity in
                 plan.StaticDrawInstances)
        {
            _sunShadowCoverageStaticScratch.Add(identity.ObjectIndex);
        }
        int missingStatic = (_sunShadowStaticCasterIndex ??
                throw new InvalidOperationException(
                    "The static caster object index is unavailable."))
            .CountMissingExecutableExpectations(
                _sunShadowCoverageStaticScratch,
                nativeCameraOrigin);
        if (missingStatic != 0)
        {
            throw new InvalidOperationException(
                $"Spot scene light {plan.Entry.SceneLightIndex} has {missingStatic} static-model material caster surface(s) without exact slot-2 payload.");
        }
    }

    private static void ValidateSpotShadowPublications(
        MapRenderSpotShadowAtlasReadyState readyState,
        MapRenderOpenGlSpotShadowAtlasReadyFrame backendReady)
    {
        if (readyState.Revision != backendReady.FrameRevision ||
            readyState.Entries.Count !=
                backendReady.EntriesBySceneLightIndex.Count)
        {
            throw new InvalidOperationException(
                "Spot-shadow renderer and backend publications disagree on revision or entry count.");
        }
        IReadOnlyList<MapRenderSpotShadowAtlasEntry> entries =
            readyState.Entries;
        for (int index = 0; index < entries.Count; index++)
        {
            MapRenderSpotShadowAtlasEntry entry = entries[index];
            if (!backendReady.TryGetEntry(
                    entry.SceneLightIndex,
                    out MapRenderOpenGlSpotShadowReadyEntry?
                        backendEntry) ||
                backendEntry.TileIndex != entry.AtlasSlot ||
                backendEntry.LookupMatrix != entry.ShadowLookupMatrix ||
                backendEntry.Fade != entry.Fade)
            {
                throw new InvalidOperationException(
                    $"Spot scene light {entry.SceneLightIndex} has divergent renderer and backend publications.");
            }
        }
    }

    private void ClearCurrentSpotShadowFrame()
    {
        _currentSpotShadowReadyState = null;
        _currentSpotShadowBackendReadyFrame = null;
    }

    private void EnsureSunShadowReceiverSelection(
        MapRenderWorldDpvsThreeViewFrame frame,
        MapRenderSunShadowAtlasReadyState? sunAtlasReady,
        bool selectionPrepared)
    {
        if (!selectionPrepared)
        {
            PrepareWorldReceiverVariantSelection(frame, sunAtlasReady);
        }
    }

    private void ClearSpotShadowCasterMembership()
    {
        InvalidateSpotShadowAtlasContentCache();
        _spotShadowPlanner = null;
    }

    private bool CanReuseSpotShadowAtlasContents(
        MapRenderOpenGlSpotShadowAtlasBackend atlas,
        IReadOnlyList<SpotShadowPlan> plans)
    {
        if (!ReferenceEquals(atlas, _spotShadowAtlasContentBackend) ||
            !ReferenceEquals(
                _currentSunShadowVisibility,
                _spotShadowAtlasContentVisibility) ||
            plans.Count != _spotShadowAtlasContentPlans.Length)
        {
            return false;
        }

        for (int index = 0; index < plans.Count; index++)
        {
            SpotShadowPlan candidate = plans[index];
            SpotShadowPlan retained = _spotShadowAtlasContentPlans[index];
            MapRenderSpotShadowAtlasEntry candidateEntry = candidate.Entry;
            MapRenderSpotShadowAtlasEntry retainedEntry = retained.Entry;
            if (!ReferenceEquals(
                    candidate.WorldSurfaceIndices,
                    retained.WorldSurfaceIndices) ||
                !ReferenceEquals(
                    candidate.StaticDrawInstances,
                    retained.StaticDrawInstances) ||
                candidateEntry.SceneLightIndex !=
                    retainedEntry.SceneLightIndex ||
                candidateEntry.AtlasSlot != retainedEntry.AtlasSlot ||
                candidateEntry.CasterViewProjection !=
                    retainedEntry.CasterViewProjection ||
                candidateEntry.ShadowLookupMatrix !=
                    retainedEntry.ShadowLookupMatrix ||
                candidateEntry.Fade != retainedEntry.Fade)
            {
                return false;
            }
        }

        foreach (uint handle in _spotShadowAtlasContentTextureHandles)
        {
            if (!IsSunShadowCasterTextureResident(handle))
                return false;
        }
        return true;
    }

    private bool TryGetCachedSpotShadowPlanning(
        MapRenderOpenGlSpotShadowAtlasBackend atlas,
        MapRenderWorldDpvsThreeViewFrame frame,
        out SpotShadowPlan[] plans,
        out MapRenderSpotShadowAtlasEntry[] entries,
        out MapRenderOpenGlSpotShadowEntryDescriptor[] descriptors)
    {
        plans = _spotShadowAtlasContentPlans;
        entries = _spotShadowAtlasContentEntries;
        descriptors = _spotShadowAtlasContentDescriptors;
        // The visibility cache retains object identity only for an exact
        // camera/source/projection key hit. The frame references below prove
        // this current-revision wrapper was built from that retained payload.
        if (_spotShadowPlanner is not { } planner ||
            !planner.MatchesFrameCardinality(frame) ||
            !ReferenceEquals(atlas, _spotShadowAtlasContentBackend) ||
            _spotShadowAtlasContentVisibility is not { } visibility ||
            !ReferenceEquals(
                _currentSunShadowVisibility,
                visibility) ||
            !ReferenceEquals(
                frame.Projection,
                visibility.SunShadowProjection) ||
            plans.Length == 0 ||
            entries.Length != plans.Length ||
            descriptors.Length != plans.Length)
        {
            return false;
        }

        bool hasExactCameraView = false;
        for (int index = 0;
             index < visibility.CompletedViews.Count;
             index++)
        {
            MapRenderWorldDpvsViewVisibility view =
                visibility.CompletedViews[index];
            if (view.ViewIndex != MapRenderWorldDpvsViewIndex.Camera)
                continue;
            hasExactCameraView = ReferenceEquals(frame.Camera, view);
            break;
        }
        if (!hasExactCameraView)
            return false;

        for (int index = 0; index < plans.Length; index++)
        {
            MapRenderSpotShadowAtlasEntry entry = entries[index];
            MapRenderOpenGlSpotShadowEntryDescriptor descriptor =
                descriptors[index];
            if (!ReferenceEquals(
                    plans[index].Entry,
                    entry) ||
                descriptor.SceneLightIndex != entry.SceneLightIndex ||
                descriptor.TileIndex != entry.AtlasSlot ||
                descriptor.LookupMatrix != entry.ShadowLookupMatrix ||
                descriptor.Fade != entry.Fade)
            {
                return false;
            }
        }

        return true;
    }

    private void RecordSpotShadowTextureUsage(SpotShadowPlan plan)
    {
        foreach (int surfaceIndex in plan.WorldSurfaceIndices)
        {
            if (_sunShadowWorldCastersBySurface.TryGetValue(
                    surfaceIndex,
                    out MapRenderOpenGlSunShadowWorldCasterSurfaceRuntime
                        surfaceRuntime))
            {
                _spotShadowFrameTextureHandles.Add(
                    surfaceRuntime.Runtime.Mesh.CutoutTexture);
            }
        }
        foreach (MapRenderOpenGlSunShadowStaticCasterRuntime runtime in
                 _sunShadowStaticCasterRuntimes)
        {
            MapRenderOpenGlSunShadowStaticCasterPartitionRuntime partition =
                runtime.GetPartition(0);
            if (partition.InstanceCount != 0)
            {
                _spotShadowFrameTextureHandles.Add(
                    partition.Mesh.CutoutTexture);
            }
        }
    }

    private void RememberSpotShadowAtlasContents(
        MapRenderOpenGlSpotShadowAtlasBackend atlas,
        IReadOnlyList<SpotShadowPlan> plans,
        MapRenderSpotShadowAtlasEntry[] entries,
        MapRenderOpenGlSpotShadowEntryDescriptor[] descriptors)
    {
        _spotShadowAtlasContentBackend = atlas;
        _spotShadowAtlasContentVisibility = _currentSunShadowVisibility;
        _spotShadowAtlasContentPlans = plans is SpotShadowPlan[] planSnapshot
            ? planSnapshot
            : plans.ToArray();
        _spotShadowAtlasContentEntries = entries;
        _spotShadowAtlasContentDescriptors = descriptors;
        _spotShadowAtlasContentTextureHandles.Clear();
        foreach (uint handle in _spotShadowFrameTextureHandles)
            _spotShadowAtlasContentTextureHandles.Add(handle);
    }

    private void InvalidateSpotShadowAtlasContentCache()
    {
        InvalidateWorldReceiverVariantSelectionReuse();
        _spotShadowAtlasContentBackend = null;
        _spotShadowAtlasContentVisibility = null;
        _spotShadowAtlasContentPlans = [];
        _spotShadowAtlasContentEntries = [];
        _spotShadowAtlasContentDescriptors = [];
        _spotShadowAtlasContentTextureHandles.Clear();
        _spotShadowFrameTextureHandles.Clear();
    }

}
