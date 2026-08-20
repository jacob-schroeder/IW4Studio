using System.Numerics;
using System.Runtime.Versioning;

using IW4.Assets.Assets.ComWorld;
using IW4.Render.Diagnostics;
using IW4.Render.Geometry.Shadows;
using IW4.Render.Lighting;
using IW4.Render.Metal.Pipelines;
using IW4.Render.Metal.Resources;
using IW4.Render.Metal.Targets;
using IW4.Render.Scheduling;
using IW4.Render.Scheduling.Clear;
using IW4.Render.Scheduling.Dpvs;
using IW4.Render.Scheduling.Lighting;
using IW4.Render.Scheduling.Shadows;
using IW4.Render.SceneBuilding;
using IW4.Render.Shaders;
using IW4.Render.Techniques;
using IW4.Render.Transforms;

using SharpMetal.Metal;

namespace IW4.Render.Metal;

[SupportedOSPlatform("macos")]
public sealed unsafe partial class MetalMapRenderer
{
    private const float Ps3SunShadowPolygonOffsetFactor = 2f;
    private const float Ps3SunShadowPolygonOffsetUnits = 25f;
    private const float Ps3SpotShadowPolygonOffsetFactor = 5f;
    private const float Ps3SpotShadowPolygonOffsetUnits = 700f;

    private MetalShadowAtlases? _shadowAtlases;
    private MetalShadowCasterPipeline? _shadowCasterPipelines;
    private MetalShadowCasterResources? _shadowCasterResources;
    private MapRenderWorldSceneSource? _shadowWorldSource;
    private MapRenderWorldDpvsNormalCameraVisibilityCache?
        _shadowVisibilityProvider;
    private MapRenderSunShadowCasterCatalogProvider?
        _shadowCasterCatalogProvider;
    private MapRenderSceneTechniqueVariantCatalog?
        _shadowTechniqueVariants;
    private MapRenderSceneLightSelectorAssetState?
        _shadowSceneLightSelectors;
    private MapRenderWorldEvent20SceneLightFrameInput?
        _shadowSceneLightFrame;
    private MapRenderSpotShadowPlanner? _spotShadowPlanner;
    private uint[] _shadowAllocationBits = [];
    private int _shadowDirectionalPrimaryLightIndex = -1;
    private MapRenderSunShadowFrameSequence _shadowFrameSequence = new();
    private long _nextShadowFrameRevision;

    // A write is published for reuse only after the renderer submits its
    // owning command buffer. This prevents late drawable acquisition from
    // turning an abandoned atlas write into apparent receiver readiness.
    private bool _pendingShadowAtlasWrite;
    private MapRenderWorldDpvsThreeViewFrame? _pendingShadowFrame;
    private MapRenderWorldDpvsVisibilityBuildResult?
        _pendingShadowVisibility;
    private MapRenderSunShadowAtlasReadyState? _pendingShadowReadyState;
    // The selector used to close exact receiver coverage before an atlas
    // write. It is deliberately pending-only: a preflight has no readiness
    // token and must never become draw authority by itself.
    private MapRenderFrameTechniqueSelector?
        _pendingShadowReceiverPreflightSelector;
    private MetalSunShadowReceiverFrame? _pendingSunShadowReceiverFrame;
    private MapRenderWorldDpvsVisibilityBuildResult?
        _committedShadowVisibility;
    private MapRenderSunShadowAtlasReadyState?
        _committedShadowReadyState;
    private bool _pendingSpotShadowAtlasWrite;
    private MapRenderWorldDpvsVisibilityBuildResult?
        _pendingSpotShadowVisibility;
    private MapRenderSpotShadowAtlasReadyState?
        _pendingSpotShadowReadyState;
    private MapRenderSpotShadowPlan[] _pendingSpotShadowPlans = [];
    private MapRenderWorldDpvsVisibilityBuildResult?
        _committedSpotShadowVisibility;
    private MapRenderSpotShadowAtlasReadyState?
        _committedSpotShadowReadyState;
    private MapRenderSpotShadowPlan[] _committedSpotShadowPlans = [];

    private void CreateShadowResources(MapRenderScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        DeleteShadowResources();

        if (!TryResolveDirectionalSun(
                scene,
                out MapRenderWorldSceneSource? worldSource,
                out int directionalPrimaryLightIndex,
                out Vector3 nativeSunDirection) ||
            worldSource is null)
        {
            return;
        }

        MetalShadowCasterResources? casters = null;
        MetalShadowCasterPipeline? pipelines = null;
        MetalShadowAtlases? atlases = null;
        try
        {
            casters = MetalShadowCasterResources.Create(
                _surface.Device,
                _surface.CommandQueue,
                scene,
                FrameBufferCount);
            if (!casters.HasCasters)
                return;

            pipelines = new MetalShadowCasterPipeline(
                _surface.Device,
                _depthStencilFormat);
            atlases = new MetalShadowAtlases(
                _surface.Device,
                _depthStencilFormat);

            var frameProvider =
                new MapRenderWorldDpvsSunShadowFullFrameProvider(
                    "METAL_EDITOR_PREVIEW_PS3_FULL_SUN_SHADOW",
                    worldSource.AssetPoolRevisionAtConstruction,
                    MapRenderWorldDpvsSunShadowFullSetupState
                        .CreateViewerProfile(nativeSunDirection));
            _shadowWorldSource = worldSource;
            _shadowVisibilityProvider =
                new MapRenderWorldDpvsNormalCameraVisibilityCache(
                    new MapRenderWorldDpvsNormalCameraVisibilityProvider(
                        "METAL_EDITOR_PREVIEW_PS3_NORMAL_THREE_VIEW",
                        frameProvider));
            _shadowCasterCatalogProvider =
                new MapRenderSunShadowCasterCatalogProvider(
                    worldSource.World);
            _shadowTechniqueVariants = scene.TechniqueVariants;
            _shadowSceneLightSelectors =
                worldSource.SceneLights.Source?.SelectorState;
            _shadowSceneLightFrame = CreateSceneLightFrame(scene);
            _shadowDirectionalPrimaryLightIndex =
                directionalPrimaryLightIndex;
            if (_shadowSceneLightSelectors is { } selectors &&
                (uint)directionalPrimaryLightIndex <
                    (uint)selectors.SceneLightCount)
            {
                _shadowAllocationBits =
                    new uint[(selectors.SceneLightCount + 31) / 32];
            }
            if (_shadowSceneLightFrame is { } shadowSceneLights &&
                _shadowTechniqueVariants is { } techniqueVariants)
            {
                _spotShadowPlanner = MapRenderSpotShadowPlanner.TryCreate(
                    worldSource,
                    shadowSceneLights,
                    techniqueVariants);
            }
            _shadowCasterResources = casters;
            casters = null;
            _shadowCasterPipelines = pipelines;
            pipelines = null;
            _shadowAtlases = atlases;
            atlases = null;
            _shadowFrameSequence = new();
            _nextShadowFrameRevision = 0;
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            InvalidOperationException or
            NotSupportedException or
            OverflowException or
            AggregateException)
        {
            // Shadow selection is an atomic optional stage. An incomplete
            // caster/resource contract cannot authorize a receiver and must
            // not prevent the established unshadowed normal-camera path.
            DeleteShadowResources();
        }
        finally
        {
            atlases?.Dispose();
            pipelines?.Dispose();
            casters?.Dispose();
        }
    }

    private void DeleteShadowResources()
    {
        AbandonShadowPasses();
        _committedShadowReadyState = null;
        _committedShadowVisibility = null;
        _committedSpotShadowReadyState = null;
        _committedSpotShadowVisibility = null;
        _committedSpotShadowPlans = [];
        _shadowCasterCatalogProvider = null;
        _shadowTechniqueVariants = null;
        _shadowSceneLightSelectors = null;
        _shadowSceneLightFrame = null;
        _spotShadowPlanner = null;
        _shadowAllocationBits = [];
        _shadowDirectionalPrimaryLightIndex = -1;
        _shadowVisibilityProvider?.Clear();
        _shadowVisibilityProvider = null;
        _shadowWorldSource = null;
        _shadowCasterResources?.Dispose();
        _shadowCasterResources = null;
        _shadowCasterPipelines?.Dispose();
        _shadowCasterPipelines = null;
        _shadowAtlases?.Dispose();
        _shadowAtlases = null;
        _shadowFrameSequence = new();
        _nextShadowFrameRevision = 0;
    }

    private void EncodeShadowPasses(
        MTLCommandBuffer commandBuffer,
        RenderCamera camera)
    {
        AbandonShadowPasses();
        if (commandBuffer.NativePtr == 0)
        {
            throw new ArgumentException(
                "A Metal command buffer is required.",
                nameof(commandBuffer));
        }
        if (_shadowAtlases is null ||
            _shadowCasterPipelines is null ||
            _shadowCasterResources is null ||
            _shadowWorldSource is null ||
            _shadowVisibilityProvider is null ||
            _shadowCasterCatalogProvider is null)
        {
            return;
        }

        try
        {
            var extent = new MapRenderNormalCameraFramebufferExtent(
                _surfaceExtents.SceneTarget.Width,
                _surfaceExtents.SceneTarget.Height);
            var farPlane = new MapRenderNormalCameraFarPlaneState(
                rZFar: 0f,
                rendererFallback: camera.FarPlane);
            MapRenderWorldDpvsVisibilityBuildResult visibility =
                _shadowVisibilityProvider.Build(
                    _shadowWorldSource.World,
                    camera,
                    extent,
                    farPlane);
            if (!visibility.IsSuccess ||
                visibility.SunShadowProjection is null)
            {
                return;
            }
            PublishShadowNormalCameraVisibility(
                camera,
                extent,
                farPlane,
                visibility);
            // The receiver preflight must use the same normal-camera DPVS
            // and static-LOD selection that depth and color will consume.
            // Otherwise an unselected LOD can reject an otherwise complete
            // atlas, or a later selector can route a different owner.
            PrepareNormalCameraVisibility(camera);
            PrepareStaticModelLighting();

            // Visibility and working-set admission own independent,
            // non-overlapping CPU telemetry phases. Begin SunShadow only
            // after both close so runtime telemetry cannot throw on nesting.
            using MapRenderCpuPhaseScope cpuPhase =
                _telemetry.BeginCpuPhase(MapRenderCpuPhase.SunShadow);

            long revision = _nextShadowFrameRevision;
            _nextShadowFrameRevision = checked(revision + 1);
            MapRenderSunShadowFramePublication publication =
                _shadowFrameSequence.BeginFrame(revision, visibility);
            _pendingShadowFrame = publication.Frame;
            MapRenderSunShadowAtlasReadyState readyState;
            if (ReferenceEquals(
                    visibility,
                    _committedShadowVisibility) &&
                _committedShadowReadyState is not null)
            {
                RecordCompletedPartitions(publication);
                if (!publication.TryGetAtlasReady(
                        out MapRenderSunShadowAtlasReadyState?
                            reusedReady) ||
                    reusedReady is null)
                {
                    throw new InvalidOperationException(
                        "An unchanged Metal sun-shadow atlas could not be republished.");
                }
                readyState = reusedReady;
            }
            else
            {
                MapRenderSunShadowCasterCatalogBuildResult catalogResult =
                    _shadowCasterCatalogProvider.BuildFastWorker(
                        revision,
                        visibility);
                if (!catalogResult.IsSuccess ||
                    catalogResult.Catalog is null)
                {
                    return;
                }

                // Prove that every authored, allocated receiver channel can
                // consume this exact planned selector before clearing the
                // reusable atlas. The neutral preflight carries no readiness
                // token, so it cannot escape as render authority.
                MapRenderFrameTechniqueSelector? preflightSelector =
                    TryCreateShadowReceiverPreflightSelector(
                        publication.Frame);
                if (preflightSelector is null ||
                    !CanAuthorizeNormalCameraShadowReceiverSelector(
                        preflightSelector))
                {
                    return;
                }
                _pendingShadowReceiverPreflightSelector =
                    preflightSelector;

                _pendingShadowAtlasWrite = true;
                EncodeSunShadowAtlas(
                    commandBuffer,
                    publication,
                    catalogResult.Catalog);
                if (!publication.TryGetAtlasReady(
                        out MapRenderSunShadowAtlasReadyState?
                            completedReadyState) ||
                    completedReadyState is null)
                {
                    throw new InvalidOperationException(
                        "Both Metal sun-shadow partitions completed without an atomic readiness publication.");
                }
                readyState = completedReadyState;
            }

            _pendingShadowVisibility = visibility;
            _pendingShadowReadyState = readyState;

            MapRenderSpotShadowAtlasReadyState? spotReady = null;
            try
            {
                spotReady = EncodeSpotShadowAtlas(
                    commandBuffer,
                    readyState,
                    visibility);
            }
            catch (Exception exception) when (
                exception is ArgumentException or
                InvalidDataException or
                InvalidOperationException or
                NotSupportedException or
                OverflowException or
                AggregateException)
            {
                // Spot is an independent optional atlas. If its render pass
                // was entered, the pending write bit invalidates the prior
                // committed key when this command buffer is submitted.
                _pendingSpotShadowVisibility = null;
                _pendingSpotShadowReadyState = null;
                _pendingSpotShadowPlans = [];
            }
            if (_pendingShadowReceiverPreflightSelector is null)
            {
                MapRenderFrameTechniqueSelector? exactPreflight =
                    TryCreateShadowReceiverPreflightSelector(
                        readyState.Frame,
                        spotReady?.Entries);
                if (exactPreflight is null ||
                    !CanAuthorizeNormalCameraShadowReceiverSelector(
                        exactPreflight))
                {
                    return;
                }
                _pendingShadowReceiverPreflightSelector = exactPreflight;
            }
            _pendingSunShadowReceiverFrame =
                TryCreateSunShadowReceiverFrame(readyState, spotReady);
            if (_pendingSunShadowReceiverFrame is null)
            {
                _pendingSpotShadowVisibility = null;
                _pendingSpotShadowReadyState = null;
                _pendingSpotShadowPlans = [];
                MapRenderFrameTechniqueSelector? sunOnlyPreflight =
                    TryCreateShadowReceiverPreflightSelector(
                        readyState.Frame);
                _pendingShadowReceiverPreflightSelector =
                    sunOnlyPreflight is not null &&
                    CanAuthorizeNormalCameraShadowReceiverSelector(
                        sunOnlyPreflight)
                        ? sunOnlyPreflight
                        : null;
                _pendingSunShadowReceiverFrame =
                    TryCreateSunShadowReceiverFrame(readyState);
            }
            if (_pendingSunShadowReceiverFrame is not null)
                InvalidateNormalCameraReceiverSelection();
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            InvalidOperationException or
            NotSupportedException or
            OverflowException or
            AggregateException)
        {
            // If encoding already entered the atlas pass, committing this
            // command buffer invalidates prior depth contents. Leave the
            // pending-write bit set so CommitShadowPasses drops the old key.
            _pendingShadowVisibility = null;
            _pendingShadowReadyState = null;
            _pendingSunShadowReceiverFrame = null;
        }
    }

    private void EncodeSunShadowAtlas(
        MTLCommandBuffer commandBuffer,
        MapRenderSunShadowFramePublication publication,
        MapRenderSunShadowCasterCatalog catalog)
    {
        MetalShadowAtlases atlases = _shadowAtlases!;
        using MTLRenderPassDescriptor pass = atlases.CreateSunPass();
        _gpuPassTimer.AttachPass(pass, MapRenderGpuPhase.SunShadow);
        MTLRenderCommandEncoder encoder =
            commandBuffer.RenderCommandEncoder(pass);
        if (encoder.NativePtr == 0)
        {
            throw new InvalidOperationException(
                "Metal could not begin the sun-shadow atlas pass.");
        }

        long drawCalls = 0;
        long triangles = 0;
        try
        {
            _renderStates.ResetEncoderInheritance();
            _renderStates.ApplyRasterState(encoder, RenderState.Default);
            _telemetry.AddCounter(
                MapRenderFrameCounter.RenderStateChanges);
            for (int partitionIndex = 0;
                 partitionIndex < MetalShadowAtlases.SunPartitionCount;
                 partitionIndex++)
            {
                MetalShadowAtlasTile tile =
                    MetalShadowAtlases.GetSunPartition(partitionIndex);
                encoder.SetViewport(new MTLViewport
                {
                    originX = tile.X,
                    originY = tile.Y,
                    width = tile.Width,
                    height = tile.Height,
                    znear = 0,
                    zfar = 1
                });
                encoder.SetScissorRect(new MTLScissorRect
                {
                    x = checked((ulong)tile.X),
                    y = checked((ulong)tile.Y),
                    width = checked((ulong)tile.Width),
                    height = checked((ulong)tile.Height)
                });

                MapRenderSunShadowCasterPartition partition =
                    catalog.GetPartition(partitionIndex);
                Matrix4x4 hostViewProjection =
                    RenderCoordinateConverter.RenderToGameMatrix *
                    publication.Frame.Projection.WorldToClip(
                        partitionIndex);
                SetShadowViewProjection(
                    encoder,
                    in hostViewProjection);
                _telemetry.AddCounter(
                    MapRenderFrameCounter.UniformUpdates);
                _telemetry.AddCounter(
                    MapRenderFrameCounter.BufferChanges);

                EncodeSunShadowWorldCasters(
                    encoder,
                    partition,
                    ref drawCalls,
                    ref triangles);
                EncodeSunShadowStaticCasters(
                    encoder,
                    partition,
                    publication.Frame.Projection.CameraOrigin,
                    ref drawCalls,
                    ref triangles);

                MapRenderWorldDpvsViewIndex viewIndex =
                    partitionIndex == 0
                        ? MapRenderWorldDpvsViewIndex
                            .SunShadowPartition0
                        : MapRenderWorldDpvsViewIndex
                            .SunShadowPartition1;
                if (!publication.RecordPartitionDrawCompleted(
                        publication.Revision,
                        viewIndex))
                {
                    throw new InvalidOperationException(
                        $"Metal sun-shadow partition {partitionIndex} completed more than once.");
                }
                _telemetry.AddCounter(MapRenderFrameCounter.Passes);
                _telemetry.AddCounter(
                    MapRenderFrameCounter.SunShadowPasses);
            }
        }
        finally
        {
            encoder.EndEncoding();
            if (drawCalls != 0)
            {
                _telemetry.AddGpuPhaseWork(
                    MapRenderGpuPhase.SunShadow,
                    drawCalls,
                    triangles);
            }
        }
    }

    private MapRenderSpotShadowAtlasReadyState? EncodeSpotShadowAtlas(
        MTLCommandBuffer commandBuffer,
        MapRenderSunShadowAtlasReadyState sunReady,
        MapRenderWorldDpvsVisibilityBuildResult visibility)
    {
        ArgumentNullException.ThrowIfNull(sunReady);
        ArgumentNullException.ThrowIfNull(visibility);
        if (_spotShadowPlanner is not { } planner ||
            !planner.MatchesFrameCardinality(sunReady.Frame))
        {
            return null;
        }

        if (ReferenceEquals(
                visibility,
                _committedSpotShadowVisibility) &&
            _committedSpotShadowReadyState is { } committedReady &&
            _committedSpotShadowPlans.Length != 0)
        {
            var reusedPublication = new MapRenderSpotShadowFramePublication(
                sunReady.Frame,
                committedReady.Entries);
            foreach (MapRenderSpotShadowAtlasEntry entry in
                     committedReady.Entries)
            {
                if (!reusedPublication.RecordEntryDrawCompleted(
                        sunReady.Revision,
                        entry.SceneLightIndex))
                {
                    throw new InvalidOperationException(
                        $"Reused Metal spot-shadow tile {entry.AtlasSlot} completed more than once.");
                }
            }
            if (!reusedPublication.TryGetAtlasReady(
                    out MapRenderSpotShadowAtlasReadyState? reusedReady) ||
                reusedReady is null)
            {
                throw new InvalidOperationException(
                    "An unchanged Metal spot-shadow atlas could not be republished.");
            }
            // Reuse still changes the normal-camera +3 membership. Rebuild
            // and retain the exact preflight selector so the subsequent
            // ready selector proves the reused spot entries own the same
            // receiver routes as a freshly encoded atlas.
            MapRenderFrameTechniqueSelector? reusedPreflightSelector =
                TryCreateShadowReceiverPreflightSelector(
                    sunReady.Frame,
                    reusedReady.Entries);
            if (reusedPreflightSelector is null ||
                !CanAuthorizeNormalCameraShadowReceiverSelector(
                    reusedPreflightSelector))
            {
                return null;
            }
            _pendingShadowReceiverPreflightSelector =
                reusedPreflightSelector;
            _pendingSpotShadowVisibility = visibility;
            _pendingSpotShadowReadyState = reusedReady;
            _pendingSpotShadowPlans = _committedSpotShadowPlans;
            return reusedReady;
        }

        IReadOnlyList<MapRenderSpotShadowPlan> planned =
            planner.CreateFramePlans(sunReady.Frame);
        if (planned.Count == 0)
            return null;

        MapRenderSpotShadowPlan[] plans = planned.ToArray();
        var entries = new MapRenderSpotShadowAtlasEntry[plans.Length];
        MetalShadowCasterResources resources = _shadowCasterResources!;
        for (int index = 0; index < plans.Length; index++)
        {
            MapRenderSpotShadowPlan plan = plans[index];
            entries[index] = plan.Entry;
            resources.ValidateSpotSelection(
                plan,
                sunReady.Frame.Projection.CameraOrigin);
        }

        var publication = new MapRenderSpotShadowFramePublication(
            sunReady.Frame,
            entries);

        // Close the exact selector/resource contract before the destructive
        // clear. The neutral preflight carries the planned allocation bits
        // but no readiness token and therefore cannot authorize execution.
        MapRenderFrameTechniqueSelector? preflightSelector =
            TryCreateShadowReceiverPreflightSelector(
                sunReady.Frame,
                entries);
        if (preflightSelector is null ||
            !CanAuthorizeNormalCameraShadowReceiverSelector(
                preflightSelector))
        {
            return null;
        }
        _pendingShadowReceiverPreflightSelector = preflightSelector;

        _pendingSpotShadowAtlasWrite = true;
        EncodeSpotShadowAtlasPass(
            commandBuffer,
            publication,
            plans);
        if (!publication.TryGetAtlasReady(
                out MapRenderSpotShadowAtlasReadyState? readyState) ||
            readyState is null)
        {
            throw new InvalidOperationException(
                "All Metal spot-shadow tiles completed without an atomic readiness publication.");
        }

        _pendingSpotShadowVisibility = visibility;
        _pendingSpotShadowReadyState = readyState;
        _pendingSpotShadowPlans = plans;
        return readyState;
    }

    private void EncodeSpotShadowAtlasPass(
        MTLCommandBuffer commandBuffer,
        MapRenderSpotShadowFramePublication publication,
        IReadOnlyList<MapRenderSpotShadowPlan> plans)
    {
        MetalShadowAtlases atlases = _shadowAtlases!;
        using MTLRenderPassDescriptor pass = atlases.CreateSpotPass();
        _gpuPassTimer.AttachPass(pass, MapRenderGpuPhase.SunShadow);
        MTLRenderCommandEncoder encoder =
            commandBuffer.RenderCommandEncoder(pass);
        if (encoder.NativePtr == 0)
        {
            throw new InvalidOperationException(
                "Metal could not begin the spot-shadow atlas pass.");
        }

        long drawCalls = 0;
        long triangles = 0;
        try
        {
            _renderStates.ResetEncoderInheritance();
            _renderStates.ApplyRasterState(encoder, RenderState.Default);
            _telemetry.AddCounter(
                MapRenderFrameCounter.RenderStateChanges);
            foreach (MapRenderSpotShadowPlan plan in plans)
            {
                MapRenderSpotShadowAtlasEntry entry = plan.Entry;
                MetalShadowAtlasTile tile =
                    MetalShadowAtlases.GetSpotTile(entry.AtlasSlot);
                encoder.SetViewport(new MTLViewport
                {
                    originX = tile.X,
                    originY = tile.Y,
                    width = tile.Width,
                    height = tile.Height,
                    znear = 0,
                    zfar = 1
                });
                encoder.SetScissorRect(new MTLScissorRect
                {
                    x = checked((ulong)tile.X),
                    y = checked((ulong)tile.Y),
                    width = checked((ulong)tile.Width),
                    height = checked((ulong)tile.Height)
                });

                Matrix4x4 hostViewProjection =
                    RenderCoordinateConverter.RenderToGameMatrix *
                    entry.CasterViewProjection;
                SetShadowViewProjection(
                    encoder,
                    in hostViewProjection);
                _telemetry.AddCounter(
                    MapRenderFrameCounter.UniformUpdates);
                _telemetry.AddCounter(
                    MapRenderFrameCounter.BufferChanges);

                EncodeSpotShadowWorldCasters(
                    encoder,
                    plan,
                    ref drawCalls,
                    ref triangles);
                EncodeSpotShadowStaticCasters(
                    encoder,
                    plan,
                    publication.Frame.Projection.CameraOrigin,
                    ref drawCalls,
                    ref triangles);

                if (!publication.RecordEntryDrawCompleted(
                        publication.Revision,
                        entry.SceneLightIndex))
                {
                    throw new InvalidOperationException(
                        $"Metal spot-shadow tile {entry.AtlasSlot} for scene light {entry.SceneLightIndex} completed more than once.");
                }
                _telemetry.AddCounter(MapRenderFrameCounter.Passes);
            }
        }
        finally
        {
            encoder.EndEncoding();
            if (drawCalls != 0)
            {
                _telemetry.AddGpuPhaseWork(
                    MapRenderGpuPhase.SunShadow,
                    drawCalls,
                    triangles);
            }
        }
    }

    private void EncodeSunShadowWorldCasters(
        MTLRenderCommandEncoder encoder,
        MapRenderSunShadowCasterPartition partition,
        ref long drawCalls,
        ref long triangles) => EncodeShadowWorldCasters(
            encoder,
            partition.WorldSurfaceIndices,
            Ps3SunShadowPolygonOffsetFactor,
            Ps3SunShadowPolygonOffsetUnits,
            countAsSunShadow: true,
            ref drawCalls,
            ref triangles);

    private void EncodeSpotShadowWorldCasters(
        MTLRenderCommandEncoder encoder,
        MapRenderSpotShadowPlan plan,
        ref long drawCalls,
        ref long triangles) => EncodeShadowWorldCasters(
            encoder,
            plan.WorldSurfaceIndices,
            Ps3SpotShadowPolygonOffsetFactor,
            Ps3SpotShadowPolygonOffsetUnits,
            countAsSunShadow: false,
            ref drawCalls,
            ref triangles);

    private void EncodeShadowWorldCasters(
        MTLRenderCommandEncoder encoder,
        IReadOnlyList<int> worldSurfaceIndices,
        float polygonOffsetFactor,
        float polygonOffsetUnits,
        bool countAsSunShadow,
        ref long drawCalls,
        ref long triangles)
    {
        MetalShadowCasterResources resources =
            _shadowCasterResources!;
        resources.PrepareWorldSelection(worldSurfaceIndices);
        nint currentPipeline = 0;
        RenderState? currentState = null;
        foreach (MetalShadowWorldCasterRuntime runtime in
                 resources.WorldCasters)
        {
            if (runtime.DrawRunCount == 0)
                continue;
            BindShadowCaster(
                encoder,
                runtime.Batch.Material,
                runtime.Geometry,
                runtime.Cutout,
                instanced: false,
                polygonOffsetFactor,
                polygonOffsetUnits,
                ref currentPipeline,
                ref currentState);
            for (int index = 0;
                 index < runtime.DrawRunCount;
                 index++)
            {
                MapRenderSunShadowWorldCasterDrawRun run =
                    runtime.DrawRuns[index];
                encoder.DrawIndexedPrimitives(
                    runtime.Geometry.PrimitiveType,
                    run.IndexCount,
                    runtime.Geometry.IndexType,
                    runtime.Geometry.Buffer,
                    checked(runtime.Geometry.IndexOffset +
                        (ulong)run.FirstIndex * sizeof(uint)));
                long drawTriangles = run.IndexCount / 3;
                RecordShadowDraw(drawTriangles, countAsSunShadow);
                drawCalls++;
                triangles = checked(triangles + drawTriangles);
            }
        }
    }

    private void EncodeSunShadowStaticCasters(
        MTLRenderCommandEncoder encoder,
        MapRenderSunShadowCasterPartition partition,
        Vector3 nativeCameraOrigin,
        ref long drawCalls,
        ref long triangles) => EncodeShadowStaticCasters(
            encoder,
            partition.StaticDrawInstances,
            partition.PartitionIndex,
            nativeCameraOrigin,
            Ps3SunShadowPolygonOffsetFactor,
            Ps3SunShadowPolygonOffsetUnits,
            countAsSunShadow: true,
            ref drawCalls,
            ref triangles);

    private void EncodeSpotShadowStaticCasters(
        MTLRenderCommandEncoder encoder,
        MapRenderSpotShadowPlan plan,
        Vector3 nativeCameraOrigin,
        ref long drawCalls,
        ref long triangles) => EncodeShadowStaticCasters(
            encoder,
            plan.StaticDrawInstances,
            checked(MetalShadowAtlases.SunPartitionCount +
                plan.Entry.AtlasSlot),
            nativeCameraOrigin,
            Ps3SpotShadowPolygonOffsetFactor,
            Ps3SpotShadowPolygonOffsetUnits,
            countAsSunShadow: false,
            ref drawCalls,
            ref triangles);

    private void EncodeShadowStaticCasters(
        MTLRenderCommandEncoder encoder,
        IReadOnlyList<MapRenderSunShadowStaticCasterIdentity>
            staticDrawInstances,
        int selectionSlice,
        Vector3 nativeCameraOrigin,
        float polygonOffsetFactor,
        float polygonOffsetUnits,
        bool countAsSunShadow,
        ref long drawCalls,
        ref long triangles)
    {
        MetalShadowCasterResources resources =
            _shadowCasterResources!;
        int frameSlot = checked((int)(_frameIndex % FrameBufferCount));
        int selectedCount = resources.PrepareStaticSelection(
            staticDrawInstances,
            nativeCameraOrigin,
            frameSlot,
            selectionSlice);
        if (selectedCount == 0)
            return;

        MTLBuffer instanceBuffer =
            resources.RequireDynamicInstanceBuffer(frameSlot);
        nint currentPipeline = 0;
        RenderState? currentState = null;
        foreach (MetalShadowStaticCasterRuntime runtime in
                 resources.StaticCasters)
        {
            int instanceCount = runtime.InstanceCount(
                selectionSlice);
            if (instanceCount == 0)
                continue;
            BindShadowCaster(
                encoder,
                runtime.Batch.Material,
                runtime.Geometry,
                runtime.Cutout,
                instanced: true,
                polygonOffsetFactor,
                polygonOffsetUnits,
                ref currentPipeline,
                ref currentState);
            encoder.SetVertexBuffer(
                instanceBuffer,
                runtime.InstanceOffset(
                    selectionSlice,
                    resources.StaticInstanceCapacityPerPartition),
                1);
            _telemetry.AddCounter(
                MapRenderFrameCounter.BufferChanges);
            encoder.DrawIndexedPrimitives(
                runtime.Geometry.PrimitiveType,
                checked((ulong)runtime.Geometry.IndexCount),
                runtime.Geometry.IndexType,
                runtime.Geometry.Buffer,
                runtime.Geometry.IndexOffset,
                checked((ulong)instanceCount),
                0,
                0);
            long drawTriangles = checked(
                (long)(runtime.Geometry.IndexCount / 3) *
                instanceCount);
            RecordShadowDraw(drawTriangles, countAsSunShadow);
            drawCalls++;
            triangles = checked(triangles + drawTriangles);
        }
    }

    private void BindShadowCaster(
        MTLRenderCommandEncoder encoder,
        MapRenderSunShadowCasterMaterialPlan material,
        MetalGeometryResource geometry,
        MetalShadowTextureRuntime? cutout,
        bool instanced,
        float polygonOffsetFactor,
        float polygonOffsetUnits,
        ref nint currentPipeline,
        ref RenderState? currentState)
    {
        MTLRenderPipelineState pipeline =
            _shadowCasterPipelines!.Resolve(
                material.Kind,
                instanced);
        if (pipeline.NativePtr != currentPipeline)
        {
            encoder.SetRenderPipelineState(pipeline);
            currentPipeline = pipeline.NativePtr;
            _telemetry.AddCounter(
                MapRenderFrameCounter.ProgramChanges);
        }
        if (!currentState.HasValue ||
            currentState.Value != material.State)
        {
            _renderStates.ApplyRasterState(
                encoder,
                material.State);
            _renderStates.ApplyDepthBiasOverride(
                encoder,
                polygonOffsetFactor,
                polygonOffsetUnits);
            if (_depthStencilFormat.EmulatesDepth24)
            {
                Vector2 depthBias = _renderStates.CurrentDepthBias;
                encoder.SetFragmentBytes(
                    (nint)(&depthBias),
                    checked((ulong)sizeof(Vector2)),
                    MetalShadowCasterShaderAbi.DepthBiasBufferIndex);
                _telemetry.AddCounter(
                    MapRenderFrameCounter.BufferChanges);
                _telemetry.AddCounter(
                    MapRenderFrameCounter.UniformUpdates);
            }
            currentState = material.State;
            _telemetry.AddCounter(
                MapRenderFrameCounter.RenderStateChanges);
        }

        encoder.SetVertexBuffer(
            geometry.Buffer,
            geometry.VertexOffset,
            0);
        _telemetry.AddCounter(MapRenderFrameCounter.BufferChanges);
        if (material.Kind ==
            MapRenderSunShadowCasterMaterialKind.Cutout)
        {
            MetalShadowTextureRuntime binding = cutout ??
                throw new InvalidOperationException(
                    "A cutout caster lost its authored Metal texture binding.");
            encoder.SetFragmentTexture(
                binding.Texture.ResolveSampledTexture(
                    binding.Sampler.UsesSrgbReads),
                0);
            encoder.SetFragmentSamplerState(
                binding.Sampler.State,
                0);
            _telemetry.AddCounter(
                MapRenderFrameCounter.TextureChanges);
            _telemetry.AddCounter(
                MapRenderFrameCounter.SamplerChanges);
        }
    }

    private void RecordShadowDraw(
        long triangles,
        bool countAsSunShadow)
    {
        _telemetry.AddCounter(MapRenderFrameCounter.DrawCalls);
        _telemetry.AddCounter(
            MapRenderFrameCounter.LogicalDrawCommands);
        if (countAsSunShadow)
        {
            _telemetry.AddCounter(
                MapRenderFrameCounter.SunShadowLogicalDrawCommands);
        }
        _telemetry.AddCounter(
            MapRenderFrameCounter.Triangles,
            triangles);
    }

    private void CommitShadowPasses()
    {
        if (_pendingShadowAtlasWrite &&
            _pendingShadowReadyState is null)
        {
            _committedShadowVisibility = null;
            _committedShadowReadyState = null;
        }
        else if (_pendingShadowReadyState is not null &&
                 _pendingShadowVisibility is not null)
        {
            _committedShadowVisibility =
                _pendingShadowVisibility;
            _committedShadowReadyState =
                _pendingShadowReadyState;
        }
        if (_pendingSpotShadowAtlasWrite &&
            _pendingSpotShadowReadyState is null)
        {
            _committedSpotShadowVisibility = null;
            _committedSpotShadowReadyState = null;
            _committedSpotShadowPlans = [];
        }
        else if (_pendingSpotShadowReadyState is not null &&
                 _pendingSpotShadowVisibility is not null &&
                 _pendingSpotShadowPlans.Length != 0)
        {
            _committedSpotShadowVisibility =
                _pendingSpotShadowVisibility;
            _committedSpotShadowReadyState =
                _pendingSpotShadowReadyState;
            _committedSpotShadowPlans =
                _pendingSpotShadowPlans;
        }
        AbandonShadowPasses();
    }

    private void AbandonShadowPasses()
    {
        _pendingShadowAtlasWrite = false;
        _pendingShadowFrame = null;
        _pendingShadowVisibility = null;
        _pendingShadowReadyState = null;
        _pendingShadowReceiverPreflightSelector = null;
        _pendingSpotShadowAtlasWrite = false;
        _pendingSpotShadowVisibility = null;
        _pendingSpotShadowReadyState = null;
        _pendingSpotShadowPlans = [];
        _pendingSunShadowReceiverFrame = null;
    }

    private MetalSunShadowReceiverFrame?
        TryCreateSunShadowReceiverFrame(
            MapRenderSunShadowAtlasReadyState readyState,
            MapRenderSpotShadowAtlasReadyState? spotReadyState = null)
    {
        ArgumentNullException.ThrowIfNull(readyState);
        if (_shadowAtlases is not { } atlases ||
            _shadowTechniqueVariants is not { } techniques ||
            _shadowSceneLightSelectors is not { } sceneLights ||
            _shadowDirectionalPrimaryLightIndex < 0 ||
            (uint)_shadowDirectionalPrimaryLightIndex >=
                (uint)sceneLights.SceneLightCount ||
            _shadowAllocationBits.Length !=
                (sceneLights.SceneLightCount + 31) / 32)
        {
            return null;
        }

        try
        {
            Array.Clear(_shadowAllocationBits);
            SetShadowAllocationBit(_shadowDirectionalPrimaryLightIndex);
            if (spotReadyState is not null)
            {
                if (!ReferenceEquals(spotReadyState.Frame, readyState.Frame))
                    return null;
                foreach (MapRenderSpotShadowAtlasEntry entry in
                         spotReadyState.Entries)
                {
                    SetShadowAllocationBit(entry.SceneLightIndex);
                }
            }
            MapRenderSceneLightSelectorFrameState selectorFrame =
                sceneLights.CreateShadowReadyNormalViewSelectorFrame(
                    readyState,
                    spotReadyState,
                    _shadowAllocationBits);
            var selector = new MapRenderFrameTechniqueSelector(
                readyState.Frame,
                new MapRenderTechniqueSelectionContext(
                    techniques.DrawMethod,
                    selectorFrame));
            if (!HasMatchingShadowReceiverPreflight(selector) ||
                !CanAuthorizeNormalCameraShadowReceiverSelector(selector))
            {
                return null;
            }
            return new MetalSunShadowReceiverFrame(
                readyState,
                spotReadyState,
                selector,
                atlases.SunDepthStencil,
                atlases.SunComparisonSampler,
                atlases.SpotDepthStencil,
                atlases.SpotComparisonSampler);
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            InvalidDataException or
            InvalidOperationException or
            OverflowException)
        {
            // Atlas completion remains reusable, but no receiver may consume
            // it unless the immutable selector inputs close exactly.
            return null;
        }
    }

    private bool HasMatchingShadowReceiverPreflight(
        MapRenderFrameTechniqueSelector readySelector)
    {
        ArgumentNullException.ThrowIfNull(readySelector);
        if (_pendingShadowReceiverPreflightSelector is not { } preflight)
            return false;
        if (!ReferenceEquals(preflight.Visibility, readySelector.Visibility) ||
            !preflight.Techniques.SceneLights.IsShadowAllocationPreflight)
        {
            return false;
        }

        MapRenderSceneLightSelectorState expected =
            preflight.Techniques.SceneLights.Selectors;
        MapRenderSceneLightSelectorState actual =
            readySelector.Techniques.SceneLights.Selectors;
        if (expected.SceneLightCount != actual.SceneLightCount)
            return false;
        for (int lightIndex = 0;
             lightIndex < expected.SceneLightCount;
             lightIndex++)
        {
            if (expected.IsAlternateVariantAllocated(lightIndex) !=
                    actual.IsAlternateVariantAllocated(lightIndex) ||
                expected.GetEffectiveVariant(lightIndex) !=
                    actual.GetEffectiveVariant(lightIndex))
            {
                return false;
            }
        }
        return true;
    }

    private MapRenderFrameTechniqueSelector?
        TryCreateShadowReceiverPreflightSelector(
            MapRenderWorldDpvsThreeViewFrame frame,
            IReadOnlyList<MapRenderSpotShadowAtlasEntry>?
                spotEntries = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (_shadowTechniqueVariants is not { } techniques ||
            _shadowSceneLightSelectors is not { } sceneLights ||
            _shadowDirectionalPrimaryLightIndex < 0 ||
            (uint)_shadowDirectionalPrimaryLightIndex >=
                (uint)sceneLights.SceneLightCount ||
            _shadowAllocationBits.Length !=
                (sceneLights.SceneLightCount + 31) / 32)
        {
            return null;
        }

        try
        {
            Array.Clear(_shadowAllocationBits);
            SetShadowAllocationBit(_shadowDirectionalPrimaryLightIndex);
            MapRenderSceneLightSelectorFrameState selectorFrame =
                sceneLights.CreateSunShadowPreflightNormalViewSelectorFrame(
                    frame.Revision,
                    _shadowAllocationBits);
            if (spotEntries is not null)
            {
                foreach (MapRenderSpotShadowAtlasEntry entry in spotEntries)
                {
                    ArgumentNullException.ThrowIfNull(entry);
                    SetShadowAllocationBit(entry.SceneLightIndex);
                }
                selectorFrame = sceneLights
                    .CreateShadowPreflightNormalViewSelectorFrame(
                        frame.Revision,
                        _shadowAllocationBits);
            }
            return new MapRenderFrameTechniqueSelector(
                frame,
                new MapRenderTechniqueSelectionContext(
                    techniques.DrawMethod,
                    selectorFrame));
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            InvalidDataException or
            InvalidOperationException or
            OverflowException)
        {
            return null;
        }
    }

    private bool TryGetCurrentSunShadowReceiverFrame(
        out MetalSunShadowReceiverFrame? receiverFrame)
    {
        receiverFrame = _pendingSunShadowReceiverFrame;
        return receiverFrame is not null &&
            _pendingShadowReadyState is { } ready &&
            ReferenceEquals(receiverFrame.Publication, ready) &&
            ReferenceEquals(receiverFrame.Selector.Visibility, ready.Frame) &&
            ReferenceEquals(
                receiverFrame.Selector.Techniques.SceneLights
                    .SunShadowAtlasReady,
                ready) &&
            ReferenceEquals(
                receiverFrame.SpotPublication,
                _pendingSpotShadowReadyState) &&
            ReferenceEquals(
                receiverFrame.Selector.Techniques.SceneLights
                    .SpotShadowAtlasReady,
                _pendingSpotShadowReadyState) &&
            receiverFrame.Revision == ready.Revision &&
            receiverFrame.Texture.NativePtr != 0 &&
            receiverFrame.Sampler.NativePtr != 0 &&
            receiverFrame.SpotTexture.NativePtr != 0 &&
            receiverFrame.SpotSampler.NativePtr != 0;
    }

    private bool TryGetCurrentNormalCameraReceiverSelector(
        out MapRenderFrameTechniqueSelector? selector)
    {
        if (TryGetCurrentSunShadowReceiverFrame(
                out MetalSunShadowReceiverFrame? receiver))
        {
            selector = receiver!.Selector;
            return true;
        }
        if (_pendingShadowFrame is not { } frame ||
            _shadowTechniqueVariants is not { } techniques ||
            _shadowSceneLightSelectors is not { } sceneLights)
        {
            selector = null;
            return false;
        }

        try
        {
            MapRenderSceneLightSelectorFrameState selectorFrame =
                sceneLights.CreateUnshadowedNormalViewSelectorFrame(
                    frame.Revision);
            selector = new MapRenderFrameTechniqueSelector(
                frame,
                new MapRenderTechniqueSelectionContext(
                    techniques.DrawMethod,
                    selectorFrame));
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            InvalidDataException or
            InvalidOperationException or
            OverflowException)
        {
            selector = null;
            return false;
        }
    }

    private void SetShadowAllocationBit(int sceneLightIndex)
    {
        if ((uint)sceneLightIndex >=
            (uint)(_shadowAllocationBits.Length * 32))
        {
            throw new ArgumentOutOfRangeException(nameof(sceneLightIndex));
        }
        _shadowAllocationBits[sceneLightIndex >> 5] |=
            1u << (sceneLightIndex & 31);
    }

    private static void RecordCompletedPartitions(
        MapRenderSunShadowFramePublication publication)
    {
        if (!publication.RecordPartitionDrawCompleted(
                publication.Revision,
                MapRenderWorldDpvsViewIndex.SunShadowPartition0) ||
            !publication.RecordPartitionDrawCompleted(
                publication.Revision,
                MapRenderWorldDpvsViewIndex.SunShadowPartition1))
        {
            throw new InvalidOperationException(
                "A reused sun-shadow atlas partition completed more than once.");
        }
    }

    private static bool TryResolveDirectionalSun(
        MapRenderScene scene,
        out MapRenderWorldSceneSource? worldSource,
        out int primaryLightIndex,
        out Vector3 normalizedDirection)
    {
        worldSource = scene.WorldSource;
        primaryLightIndex = -1;
        normalizedDirection = default;
        MapRenderWorldSceneLightSource? lightSource =
            worldSource?.SceneLights.Source;
        if (worldSource is null || lightSource is null)
            return false;

        int sunIndex = worldSource.World.SunPrimaryLightIndex;
        IReadOnlyList<ComPrimaryLight> primaryLights =
            lightSource.ComWorld.PrimaryLights;
        if (sunIndex == 0 ||
            (uint)sunIndex >= (uint)primaryLights.Count)
        {
            return false;
        }
        ComPrimaryLight sun = primaryLights[sunIndex];
        if (sun.Type !=
            MapRenderEditorPreviewLightingPlanner.DirectionalLightType)
        {
            return false;
        }

        normalizedDirection = new Vector3(
            sun.Dir.X,
            sun.Dir.Y,
            sun.Dir.Z);
        float lengthSquared = normalizedDirection.LengthSquared();
        if (!float.IsFinite(lengthSquared) ||
            lengthSquared <= 1e-12f)
        {
            normalizedDirection = default;
            return false;
        }
        normalizedDirection /= MathF.Sqrt(lengthSquared);
        primaryLightIndex = sunIndex;
        return true;
    }

    private static void SetShadowViewProjection(
        MTLRenderCommandEncoder encoder,
        in Matrix4x4 viewProjection)
    {
        Matrix4x4 value = viewProjection;
        encoder.SetVertexBytes(
            (nint)(&value),
            checked((ulong)sizeof(Matrix4x4)),
            2);
    }

    private sealed class MetalSunShadowReceiverFrame
    {
        internal MetalSunShadowReceiverFrame(
            MapRenderSunShadowAtlasReadyState publication,
            MapRenderSpotShadowAtlasReadyState? spotPublication,
            MapRenderFrameTechniqueSelector selector,
            MTLTexture texture,
            MTLSamplerState sampler,
            MTLTexture spotTexture,
            MTLSamplerState spotSampler)
        {
            Publication = publication ??
                throw new ArgumentNullException(nameof(publication));
            SpotPublication = spotPublication;
            Selector = selector ??
                throw new ArgumentNullException(nameof(selector));
            if (!ReferenceEquals(selector.Visibility, publication.Frame) ||
                !ReferenceEquals(
                    selector.Techniques.SceneLights.SunShadowAtlasReady,
                    publication) ||
                !ReferenceEquals(
                    selector.Techniques.SceneLights.SpotShadowAtlasReady,
                    spotPublication) ||
                (spotPublication is not null &&
                 !ReferenceEquals(spotPublication.Frame, publication.Frame)))
            {
                throw new ArgumentException(
                    "The Metal receiver selector must own the exact completed same-frame shadow publications.",
                    nameof(selector));
            }
            if (texture.NativePtr == 0 ||
                sampler.NativePtr == 0 ||
                spotTexture.NativePtr == 0 ||
                spotSampler.NativePtr == 0)
            {
                throw new ArgumentException(
                    "The Metal receiver frame requires live sun/spot atlases and comparison samplers.");
            }
            Texture = texture;
            Sampler = sampler;
            SpotTexture = spotTexture;
            SpotSampler = spotSampler;
        }

        internal MapRenderSunShadowAtlasReadyState Publication { get; }

        internal MapRenderSpotShadowAtlasReadyState? SpotPublication
            { get; }

        internal MapRenderFrameTechniqueSelector Selector { get; }

        internal long Revision => Publication.Revision;

        internal MapRenderWorldDpvsSunShadowFullProjectionState Projection =>
            Publication.Frame.Projection;

        internal MTLTexture Texture { get; }

        internal MTLSamplerState Sampler { get; }

        internal MTLTexture SpotTexture { get; }

        internal MTLSamplerState SpotSampler { get; }

        internal bool TryGetSpotEntry(
            int sceneLightIndex,
            out MapRenderSpotShadowAtlasEntry? entry)
        {
            if (SpotPublication is { } publication)
                return publication.TryGetEntry(sceneLightIndex, out entry);
            entry = null;
            return false;
        }
    }
}
