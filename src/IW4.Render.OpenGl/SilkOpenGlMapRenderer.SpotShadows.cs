using System.Numerics;
using IW4.Assets.Assets.ComWorld;
using IW4.Assets.Assets.GfxMap;
using IW4.Render.Diagnostics;
using IW4.Render.Geometry.Shadows;
using IW4.Render.OpenGl.Shadows;
using IW4.Render.Scheduling;
using IW4.Render.Scheduling.Dpvs;
using IW4.Render.Scheduling.Lighting;
using IW4.Render.Scheduling.Shadows;
using IW4.Render.SceneBuilding;
using Silk.NET.OpenGL;

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

    private void InitializeSpotShadowPipeline()
    {
        ClearCurrentSpotShadowFrame();
        _spotShadowAtlas?.Dispose();
        _spotShadowAtlas = null;

        if (_previewWorldSource is not { } source ||
            _editorPreviewSceneLightFrame is not { } sceneLights ||
            _sunShadowStaticCasterIndex is null)
        {
            return;
        }

        IReadOnlyList<GfxShadowGeometry> shadowGeometry =
            source.World.ShadowGeom;
        bool hasEligibleSpot = false;
        int firstNonSunPrimaryLightIndex = checked(
            source.World.SunPrimaryLightIndex + 1);
        for (int sceneLightIndex = firstNonSunPrimaryLightIndex;
             sceneLightIndex < sceneLights.SceneLightCount &&
             sceneLightIndex < shadowGeometry.Count;
             sceneLightIndex++)
        {
            MapRenderWorldEvent20SceneLight light =
                sceneLights.GetSceneLight(sceneLightIndex);
            if (light.Type == GfxLightType.Spot &&
                light.CanUseShadowMap)
            {
                hasEligibleSpot = true;
                break;
            }
        }
        if (!hasEligibleSpot)
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
        MapRenderSunShadowAtlasReadyState? sunAtlasReady)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ClearCurrentSpotShadowFrame();
        if (_spotShadowAtlas is not { } atlas ||
            (sunAtlasReady is not null &&
             !ReferenceEquals(sunAtlasReady.Frame, frame)))
        {
            return;
        }

        IReadOnlyList<SpotShadowPlan> plans;
        try
        {
            plans = CreateSpotShadowPlans(frame);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            ArgumentException or
            NotSupportedException)
        {
            return;
        }
        if (plans.Count == 0)
            return;

        MapRenderSpotShadowAtlasEntry[] entries = plans
            .Select(plan => plan.Entry)
            .ToArray();
        try
        {
            foreach (SpotShadowPlan plan in plans)
                EnsureSpotCasterCoverage(plan, frame.Projection.CameraOrigin);

            // Resolve every +3 receiver before beginning the first depth
            // write. The selector remains preflight-only until both the
            // renderer-neutral and OpenGL publications are complete.
            PrepareWorldReceiverVariantSelection(
                frame,
                sunAtlasReady,
                spotShadowPreflightEntries: entries);

            var publication = new MapRenderSpotShadowFramePublication(
                frame,
                entries);
            MapRenderOpenGlSpotShadowEntryDescriptor[] descriptors = entries
                .Select(entry =>
                    new MapRenderOpenGlSpotShadowEntryDescriptor(
                        entry.SceneLightIndex,
                        entry.AtlasSlot,
                        entry.ShadowLookupMatrix,
                        entry.Fade))
                .ToArray();
            atlas.BeginFrame(frame.Revision, descriptors);
            foreach (SpotShadowPlan plan in plans)
            {
                MapRenderSpotShadowAtlasEntry entry = plan.Entry;
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
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            ArgumentException or
            NotSupportedException)
        {
            ClearCurrentSpotShadowFrame();
            // The directional atlas was already atomically authorized before
            // local-light work began. Rebuild that exact selection after a
            // local failure so no preflight selector can leak into drawing.
            PrepareWorldReceiverVariantSelection(frame, sunAtlasReady);
        }
        finally
        {
            // Spot tiles borrow partition zero's dynamic instance-buffer
            // slice. Never allow those selected transforms to satisfy a later
            // sun cache hit.
            foreach (MapRenderOpenGlSunShadowStaticCasterRuntime runtime in
                     _sunShadowStaticCasterRuntimes)
            {
                runtime.GetPartition(0).InvalidateSelection();
            }
            _state.SetEnabled(EnableCap.ScissorTest, false);
            ApplyDefaultRenderState();
        }
    }

    private IReadOnlyList<SpotShadowPlan> CreateSpotShadowPlans(
        MapRenderWorldDpvsThreeViewFrame frame)
    {
        if (_previewWorldSource is not { } source ||
            _editorPreviewSceneLightFrame is not { } sceneLights ||
            _sceneTechniqueVariants is not { } techniqueCatalog ||
            !source.AssetLookup.HasCanonicalAssetPoolRevision(
                sceneLights.AssetPoolRevision))
        {
            return [];
        }

        var visibleLightIndices = new HashSet<int>();
        ReadOnlySpan<uint> visibleSurfaceWords = frame.Camera.SurfaceBitSpan;
        for (int wordIndex = 0;
             wordIndex < visibleSurfaceWords.Length;
             wordIndex++)
        {
            uint pending = visibleSurfaceWords[wordIndex];
            while (pending != 0)
            {
                int bitInWord = BitOperations.LeadingZeroCount(pending);
                int surfaceIndex = checked(wordIndex * 32 + bitInWord);
                pending &= ~(0x8000_0000u >> bitInWord);
                if (surfaceIndex >= frame.Camera.SurfaceCount)
                    break;

                if ((uint)surfaceIndex <
                        (uint)techniqueCatalog.WorldSurfaces.Count &&
                    techniqueCatalog.WorldSurfaces[surfaceIndex] is
                        { } variants)
                {
                    visibleLightIndices.Add(variants.PrimaryLightIndex);
                }
            }
        }
        var candidates = new List<SpotShadowCandidate>();
        foreach (int sceneLightIndex in visibleLightIndices)
        {
            if (sceneLightIndex <= source.World.SunPrimaryLightIndex ||
                sceneLightIndex >= sceneLights.SceneLightCount ||
                !TryCreateSpotCasterSelection(
                    source.World,
                    frame,
                    sceneLightIndex,
                    out int[] worldSurfaces,
                    out MapRenderSunShadowStaticCasterIdentity[]
                        staticDrawInstances))
            {
                continue;
            }

            MapRenderWorldEvent20SceneLight light =
                sceneLights.GetSceneLight(sceneLightIndex);
            if (light.Type != GfxLightType.Spot ||
                !light.CanUseShadowMap ||
                !MapRenderSpotShadowProjectionCalculator
                    .TryCreateAtlasEntry(
                        light,
                        sceneLightIndex,
                        atlasSlot: 0,
                        fade: 1f,
                        out _))
            {
                continue;
            }

            Vector3 eyeReference =
                frame.Projection.CameraOrigin +
                64f * frame.Projection.CameraForward;
            Vector3 focusDelta =
                light.Origin - eyeReference +
                (-light.Radius * 0.125f) * light.Direction;
            float luminance = Vector3.Dot(
                light.Color,
                new Vector3(0.2989f, 0.587f, 0.114f));
            float score =
                light.Radius * luminance / (focusDelta.Length() + 1f);
            if (!float.IsFinite(score))
                continue;

            candidates.Add(new SpotShadowCandidate(
                sceneLightIndex,
                light,
                score,
                worldSurfaces,
                staticDrawInstances));
        }

        var plans = new List<SpotShadowPlan>(
            MapRenderSpotShadowAtlasLayout.MaximumEntryCount);
        foreach (SpotShadowCandidate candidate in candidates
                     .OrderByDescending(candidate => candidate.Score)
                     .ThenBy(candidate => candidate.SceneLightIndex))
        {
            int atlasSlot = plans.Count;
            if (!MapRenderSpotShadowProjectionCalculator
                    .TryCreateAtlasEntry(
                        candidate.Light,
                        candidate.SceneLightIndex,
                        atlasSlot,
                        fade: 1f,
                        out MapRenderSpotShadowAtlasEntry? entry) ||
                entry is null)
            {
                continue;
            }
            plans.Add(new SpotShadowPlan(
                entry,
                candidate.WorldSurfaceIndices,
                candidate.StaticDrawInstances));
            if (plans.Count ==
                MapRenderSpotShadowAtlasLayout.MaximumEntryCount)
            {
                break;
            }
        }
        return plans;
    }

    private static bool TryCreateSpotCasterSelection(
        GfxWorldAsset world,
        MapRenderWorldDpvsThreeViewFrame frame,
        int sceneLightIndex,
        out int[] worldSurfaceIndices,
        out MapRenderSunShadowStaticCasterIdentity[] staticDrawInstances)
    {
        worldSurfaceIndices = [];
        staticDrawInstances = [];
        if ((uint)sceneLightIndex >= (uint)world.ShadowGeom.Count)
            return false;

        if (world.ShadowGeom[sceneLightIndex] is not
                { } geometry)
        {
            return false;
        }
        if (geometry.SurfaceCount != geometry.SortedSurfIndex.Count ||
            geometry.SModelCount != geometry.SModelIndex.Count)
        {
            return false;
        }

        var surfaces = new int[geometry.SortedSurfIndex.Count];
        for (int index = 0; index < surfaces.Length; index++)
        {
            int surfaceIndex = geometry.SortedSurfIndex[index];
            if ((uint)surfaceIndex >= (uint)frame.Camera.SurfaceCount)
                return false;
            surfaces[index] = surfaceIndex;
        }
        var statics = new MapRenderSunShadowStaticCasterIdentity[
            geometry.SModelIndex.Count];
        for (int index = 0; index < statics.Length; index++)
        {
            int objectIndex = geometry.SModelIndex[index];
            if ((uint)objectIndex >= (uint)frame.Camera.StaticModelCount)
                return false;
            statics[index] = new MapRenderSunShadowStaticCasterIdentity(
                objectIndex,
                objectIndex,
                objectIndex);
        }

        worldSurfaceIndices = surfaces;
        staticDrawInstances = statics;
        return true;
    }

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
        foreach (MapRenderSpotShadowAtlasEntry entry in readyState.Entries)
        {
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

    private sealed record SpotShadowCandidate(
        int SceneLightIndex,
        MapRenderWorldEvent20SceneLight Light,
        float Score,
        int[] WorldSurfaceIndices,
        MapRenderSunShadowStaticCasterIdentity[] StaticDrawInstances);

    private sealed record SpotShadowPlan(
        MapRenderSpotShadowAtlasEntry Entry,
        int[] WorldSurfaceIndices,
        MapRenderSunShadowStaticCasterIdentity[] StaticDrawInstances);
}
