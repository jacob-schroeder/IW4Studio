using IW4.Assets.Assets.GfxMap;
using IW4.Render.Transforms;

namespace IW4.Render.Scheduling.Dpvs;

/// <summary>
/// Joins the exact camera-cell resolver, Event 0x0D static culler, and camera
/// sky-surface contribution. It does not synthesize portal or shadow-frustum
/// commands and never substitutes the authored fastfile SurfaceVisData arrays.
/// </summary>
public static class MapRenderWorldDpvsVisibilityProducer
{
    public static MapRenderWorldDpvsVisibilityBuildResult Build(
        GfxWorldAsset world,
        RenderCamera camera,
        MapRenderWorldDpvsCameraTraversal cameraTraversal,
        MapRenderWorldDpvsSunShadowTraversal sunShadowTraversal,
        MapRenderWorldDpvsSunShadowFullProjectionState?
            sunShadowProjection = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(cameraTraversal);
        ArgumentNullException.ThrowIfNull(sunShadowTraversal);
        var workingSet = new MapRenderWorldDpvsWorkingSet(world);
        return BuildCore(
            world,
            cameraTraversal.Commands,
            cameraTraversal.SkyCullInput,
            sunShadowTraversal.Partition0Commands,
            sunShadowTraversal.Partition1Commands,
            sunShadowProjection,
            workingSet,
            MapRenderWorldDpvsCameraCellResolutionResult.Succeeded(
                cameraTraversal.CameraCellIndex));
    }

    internal static MapRenderWorldDpvsVisibilityBuildResult Build(
        GfxWorldAsset world,
        RenderCamera camera,
        MapRenderWorldDpvsCameraTraversal cameraTraversal,
        MapRenderWorldDpvsSunShadowTraversal sunShadowTraversal,
        MapRenderWorldDpvsSunShadowFullProjectionState?
            sunShadowProjection,
        MapRenderWorldDpvsWorkingSet workingSet)
    {
        ArgumentNullException.ThrowIfNull(cameraTraversal);
        ArgumentNullException.ThrowIfNull(sunShadowTraversal);
        return BuildCore(
            world,
            cameraTraversal.Commands,
            cameraTraversal.SkyCullInput,
            sunShadowTraversal.Partition0Commands,
            sunShadowTraversal.Partition1Commands,
            sunShadowProjection,
            workingSet,
            MapRenderWorldDpvsCameraCellResolutionResult.Succeeded(
                cameraTraversal.CameraCellIndex));
    }

    public static MapRenderWorldDpvsVisibilityBuildResult Build(
        GfxWorldAsset world,
        RenderCamera camera,
        MapRenderWorldDpvsViewCommandSet? cameraPortalCommands = null,
        MapRenderWorldDpvsCameraSkyCullInput? cameraSkyCullInput = null,
        MapRenderWorldDpvsViewCommandSet? sunShadowPartition0Commands = null,
        MapRenderWorldDpvsViewCommandSet? sunShadowPartition1Commands = null,
        MapRenderWorldDpvsSunShadowFullProjectionState?
            sunShadowProjection = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        var workingSet = new MapRenderWorldDpvsWorkingSet(world);
        MapRenderWorldDpvsCameraCellResolutionResult cameraCell =
            MapRenderWorldDpvsCameraCellResolver.Resolve(
                world,
                RenderCoordinateConverter.RenderToGamePosition(
                    camera.Position),
                workingSet.CameraCellResolver);
        return BuildCore(
            world,
            cameraPortalCommands,
            cameraSkyCullInput,
            sunShadowPartition0Commands,
            sunShadowPartition1Commands,
            sunShadowProjection,
            workingSet,
            cameraCell);
    }

    private static MapRenderWorldDpvsVisibilityBuildResult BuildCore(
        GfxWorldAsset world,
        MapRenderWorldDpvsViewCommandSet? cameraPortalCommands,
        MapRenderWorldDpvsCameraSkyCullInput? cameraSkyCullInput,
        MapRenderWorldDpvsViewCommandSet? sunShadowPartition0Commands,
        MapRenderWorldDpvsViewCommandSet? sunShadowPartition1Commands,
        MapRenderWorldDpvsSunShadowFullProjectionState?
            sunShadowProjection,
        MapRenderWorldDpvsWorkingSet workingSet,
        MapRenderWorldDpvsCameraCellResolutionResult cameraCell)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(workingSet);
        ArgumentNullException.ThrowIfNull(cameraCell);
        workingSet.ValidateWorld(world);
        var completedViews = new List<MapRenderWorldDpvsViewVisibility>(3);
        var failures = new List<MapRenderWorldDpvsVisibilityFailure>(4);

        int? cameraCellIndex = cameraCell.CellIndex;
        if (!cameraCell.IsSuccess)
        {
            failures.Add(new(
                MapRenderWorldDpvsVisibilityFailureKind.CameraCellResolutionFailed,
                cameraCell.Failure!.Detail,
                MapRenderWorldDpvsViewIndex.Camera,
                cameraCell.Failure.Kind));
        }

        MapRenderWorldDpvsVisibilityFailure? cameraPreparationFailure =
            PrepareCameraView(
            cameraPortalCommands,
            cameraCellIndex,
            out bool cullCamera);
        MapRenderWorldDpvsVisibilityFailure? skyPreparationFailure =
            cameraSkyCullInput is null
                ? new(
                    MapRenderWorldDpvsVisibilityFailureKind
                        .CameraSkyCullInputUnavailable,
                    "The current frame has not supplied whether native camera sky culling is disabled or its far-plane-excluded frustum planes.",
                    MapRenderWorldDpvsViewIndex.Camera)
                : null;
        MapRenderWorldDpvsVisibilityFailure? partition0PreparationFailure =
            PrepareSecondaryView(
                sunShadowPartition0Commands,
                MapRenderWorldDpvsViewIndex.SunShadowPartition0,
                MapRenderWorldDpvsVisibilityFailureKind
                    .SunShadowPartition0CommandSetUnavailable,
                out bool cullPartition0);
        MapRenderWorldDpvsVisibilityFailure? partition1PreparationFailure =
            PrepareSecondaryView(
                sunShadowPartition1Commands,
                MapRenderWorldDpvsViewIndex.SunShadowPartition1,
                MapRenderWorldDpvsVisibilityFailureKind
                    .SunShadowPartition1CommandSetUnavailable,
                out bool cullPartition1);

        MapRenderWorldDpvsStaticCullResult? cameraStaticResult = null;
        MapRenderWorldDpvsCameraSkyCullResult? cameraSkyResult = null;
        MapRenderWorldDpvsStaticCullResult? partition0Result = null;
        MapRenderWorldDpvsStaticCullResult? partition1Result = null;
        Parallel.Invoke(
            () =>
            {
                if (cullCamera)
                {
                    cameraStaticResult =
                        MapRenderWorldDpvsStaticCellCuller.Cull(
                            world,
                            cameraPortalCommands!,
                            workingSet);
                }
            },
            () =>
            {
                if (cameraSkyCullInput is not null)
                {
                    cameraSkyResult =
                        MapRenderWorldDpvsCameraSkyCuller.Cull(
                            world,
                            cameraSkyCullInput,
                            workingSet);
                }
            },
            () =>
            {
                if (cullPartition0)
                {
                    partition0Result =
                        MapRenderWorldDpvsStaticCellCuller.Cull(
                            world,
                            sunShadowPartition0Commands!,
                            workingSet);
                }
            },
            () =>
            {
                if (cullPartition1)
                {
                    partition1Result =
                        MapRenderWorldDpvsStaticCellCuller.Cull(
                            world,
                            sunShadowPartition1Commands!,
                            workingSet);
                }
            });

        MapRenderWorldDpvsViewVisibility? cameraStaticView = null;
        if (cameraPreparationFailure is not null)
        {
            failures.Add(cameraPreparationFailure);
        }
        else if (cullCamera)
        {
            if (cameraStaticResult is null)
            {
                throw new InvalidOperationException(
                    "Parallel camera static culling published no same-frame result.");
            }
            if (cameraStaticResult.IsSuccess)
            {
                cameraStaticView = cameraStaticResult.Visibility;
            }
            else
            {
                failures.Add(StaticCullFailure(
                    cameraPortalCommands!,
                    cameraStaticResult.Failure!));
            }
        }

        MapRenderWorldDpvsCameraSkyVisibility? cameraSkyView = null;
        if (skyPreparationFailure is not null)
        {
            failures.Add(skyPreparationFailure);
        }
        else
        {
            if (cameraSkyResult is null)
            {
                throw new InvalidOperationException(
                    "Parallel camera sky culling published no same-frame result.");
            }
            if (cameraSkyResult.IsSuccess)
            {
                cameraSkyView = cameraSkyResult.Visibility;
            }
            else
            {
                failures.Add(new(
                    MapRenderWorldDpvsVisibilityFailureKind
                        .CameraSkyCullFailed,
                    cameraSkyResult.Failure!.Detail,
                    MapRenderWorldDpvsViewIndex.Camera,
                    CameraSkyCullFailure:
                        cameraSkyResult.Failure.Kind));
            }
        }

        if (cameraStaticView is not null && cameraSkyView is not null)
            completedViews.Add(CombineCameraView(cameraStaticView, cameraSkyView));

        PublishSecondaryView(
            sunShadowPartition0Commands,
            partition0PreparationFailure,
            cullPartition0,
            partition0Result,
            completedViews,
            failures);
        PublishSecondaryView(
            sunShadowPartition1Commands,
            partition1PreparationFailure,
            cullPartition1,
            partition1Result,
            completedViews,
            failures);

        MapRenderWorldSurfaceVisibilityState? visibility = null;
        if (failures.Count == 0)
        {
            MapRenderWorldDpvsViewVisibility cameraView = GetView(
                completedViews,
                MapRenderWorldDpvsViewIndex.Camera);
            MapRenderWorldDpvsViewVisibility secondaryView1 = GetView(
                completedViews,
                MapRenderWorldDpvsViewIndex.SunShadowPartition0);
            MapRenderWorldDpvsViewVisibility secondaryView2 = GetView(
                completedViews,
                MapRenderWorldDpvsViewIndex.SunShadowPartition1);
            visibility = new(
                cameraView,
                secondaryView1,
                secondaryView2);
        }

        return new(
            visibility,
            cameraCellIndex,
            completedViews,
            failures,
            visibility is null ? null : sunShadowProjection);
    }

    private static MapRenderWorldDpvsVisibilityFailure? PrepareCameraView(
        MapRenderWorldDpvsViewCommandSet? commands,
        int? cameraCellIndex,
        out bool shouldCull)
    {
        shouldCull = false;
        if (commands is null)
        {
            return new(
                MapRenderWorldDpvsVisibilityFailureKind
                    .CameraPortalCommandSetUnavailable,
                "PS3 camera portal traversal commands have not been supplied.",
                MapRenderWorldDpvsViewIndex.Camera);
        }
        if (!HasRole(
                commands,
                MapRenderWorldDpvsViewIndex.Camera,
                MapRenderWorldDpvsCommandOrigin.CameraPortalTraversal))
        {
            return RoleFailure(
                commands,
                MapRenderWorldDpvsViewIndex.Camera);
        }
        if (cameraCellIndex is null)
            return null;
        if (commands.CameraStartCellIndex != cameraCellIndex)
        {
            return new(
                MapRenderWorldDpvsVisibilityFailureKind
                    .CameraPortalStartCellMismatch,
                $"Camera portal commands start at cell {commands.CameraStartCellIndex}, but the exact resolver returned {cameraCellIndex}.",
                MapRenderWorldDpvsViewIndex.Camera);
        }

        shouldCull = true;
        return null;
    }

    private static MapRenderWorldDpvsVisibilityFailure?
        PrepareSecondaryView(
            MapRenderWorldDpvsViewCommandSet? commands,
            MapRenderWorldDpvsViewIndex expectedView,
            MapRenderWorldDpvsVisibilityFailureKind unavailableKind,
            out bool shouldCull)
    {
        shouldCull = false;
        if (commands is null)
        {
            return new(
                unavailableKind,
                $"PS3 {expectedView} frustum traversal commands have not been supplied.",
                expectedView);
        }
        if (!HasRole(
                commands,
                expectedView,
                MapRenderWorldDpvsCommandOrigin.SunShadowFrustumTraversal))
        {
            return RoleFailure(commands, expectedView);
        }

        shouldCull = true;
        return null;
    }

    private static void PublishSecondaryView(
        MapRenderWorldDpvsViewCommandSet? commands,
        MapRenderWorldDpvsVisibilityFailure? preparationFailure,
        bool wasCulled,
        MapRenderWorldDpvsStaticCullResult? result,
        List<MapRenderWorldDpvsViewVisibility> completedViews,
        List<MapRenderWorldDpvsVisibilityFailure> failures)
    {
        if (preparationFailure is not null)
        {
            failures.Add(preparationFailure);
            return;
        }
        if (!wasCulled)
            return;
        if (result is null)
        {
            throw new InvalidOperationException(
                "Parallel secondary static culling published no same-frame result.");
        }
        if (result.IsSuccess)
        {
            completedViews.Add(result.Visibility!);
            return;
        }

        failures.Add(StaticCullFailure(commands!, result.Failure!));
    }

    private static MapRenderWorldDpvsVisibilityFailure StaticCullFailure(
        MapRenderWorldDpvsViewCommandSet commands,
        MapRenderWorldDpvsStaticCullFailure failure) =>
        new(
            MapRenderWorldDpvsVisibilityFailureKind.StaticCullFailed,
            failure.Detail,
            commands.ViewIndex,
            StaticCullFailure: failure.Kind);

    private static MapRenderWorldDpvsVisibilityFailure RoleFailure(
        MapRenderWorldDpvsViewCommandSet commands,
        MapRenderWorldDpvsViewIndex expectedView) =>
        new(
            MapRenderWorldDpvsVisibilityFailureKind.CommandSetRoleMismatch,
            $"Expected {expectedView} commands but received {commands.ViewIndex}/{commands.Origin}.",
            expectedView);

    private static MapRenderWorldDpvsViewVisibility CombineCameraView(
        MapRenderWorldDpvsViewVisibility staticView,
        MapRenderWorldDpvsCameraSkyVisibility skyView)
    {
        if (staticView.ViewIndex != MapRenderWorldDpvsViewIndex.Camera ||
            staticView.SurfaceCount != skyView.SurfaceCount ||
            staticView.SurfaceBitSpan.Length !=
                skyView.SurfaceBitSpan.Length)
        {
            throw new InvalidOperationException(
                "Camera static and sky visibility do not describe the same native surface bitset.");
        }

        staticView.MergeCameraSkyBeforePublication(skyView);
        return staticView;
    }

    private static bool HasRole(
        MapRenderWorldDpvsViewCommandSet commands,
        MapRenderWorldDpvsViewIndex viewIndex,
        MapRenderWorldDpvsCommandOrigin origin) =>
        commands.ViewIndex == viewIndex && commands.Origin == origin;

    private static MapRenderWorldDpvsViewVisibility GetView(
        IReadOnlyList<MapRenderWorldDpvsViewVisibility> views,
        MapRenderWorldDpvsViewIndex viewIndex)
    {
        for (int index = 0; index < views.Count; index++)
        {
            MapRenderWorldDpvsViewVisibility view = views[index];
            if (view.ViewIndex == viewIndex)
                return view;
        }

        throw new InvalidOperationException(
            $"Completed DPVS views omitted {viewIndex}.");
    }
}
