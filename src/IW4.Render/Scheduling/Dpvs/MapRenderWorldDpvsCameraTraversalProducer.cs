using IW4.Assets.Assets.GfxMap;
using IW4.Render.Scheduling.Clear;

namespace IW4.Render.Scheduling.Dpvs;

/// <summary>
/// Operational producer for the camera command set and sky input consumed by
/// EditorPreview visibility.
/// </summary>
public static class MapRenderWorldDpvsCameraTraversalProducer
{
    public static MapRenderWorldDpvsCameraTraversalBuildResult Build(
        GfxWorldAsset world,
        RenderCamera camera,
        MapRenderNormalCameraFramebufferExtent framebuffer,
        MapRenderNormalCameraFarPlaneState farPlaneState,
        MapRenderWorldDpvsPortalTraversalSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        return Build(
            world,
            camera,
            framebuffer,
            farPlaneState,
            settings,
            new MapRenderWorldDpvsWorkingSet(world));
    }

    internal static MapRenderWorldDpvsCameraTraversalBuildResult Build(
        GfxWorldAsset world,
        RenderCamera camera,
        MapRenderNormalCameraFramebufferExtent framebuffer,
        MapRenderNormalCameraFarPlaneState farPlaneState,
        MapRenderWorldDpvsPortalTraversalSettings? settings,
        MapRenderWorldDpvsWorkingSet workingSet)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(farPlaneState);
        ArgumentNullException.ThrowIfNull(workingSet);
        workingSet.ValidateWorld(world);
        settings ??= MapRenderWorldDpvsPortalTraversalSettings.Ps3Default;

        MapRenderWorldDpvsNormalCameraFrameBuildResult cameraFrameResult =
            MapRenderWorldDpvsNormalCameraFrameProducer.Build(
                camera,
                framebuffer,
                farPlaneState);
        if (!cameraFrameResult.IsSuccess)
        {
            MapRenderWorldDpvsNormalCameraFrameFailure failure =
                cameraFrameResult.Failure!;
            return Failed(new(
                MapRenderWorldDpvsCameraTraversalFailureKind.CameraFrameBuildFailed,
                failure.Detail,
                CameraFrameFailure: failure.Kind));
        }
        MapRenderWorldDpvsNormalCameraFrame cameraFrame =
            cameraFrameResult.Frame!;

        MapRenderWorldDpvsCameraCellResolutionResult cellResult =
            MapRenderWorldDpvsCameraCellResolver.Resolve(
                world,
                cameraFrame.Origin,
                workingSet.CameraCellResolver);
        if (!cellResult.IsSuccess)
        {
            MapRenderWorldDpvsCameraCellFailure failure = cellResult.Failure!;
            return Failed(new(
                MapRenderWorldDpvsCameraTraversalFailureKind.CameraCellResolutionFailed,
                failure.Detail,
                CameraCellFailure: failure.Kind));
        }
        int cameraCellIndex = cellResult.CellIndex!.Value;

        MapRenderWorldDpvsPortalTraversalWorkspace traversalWorkspace =
            workingSet.PortalTraversal;
        traversalWorkspace.Begin();
        try
        {
            var context = new MapRenderWorldDpvsPortalTraversalContext(
                workingSet.Topology,
                traversalWorkspace,
                cameraFrame,
                settings);
            if (!context.TryValidateWorld())
                return Failed(context.Failure!);
            if (settings.SkipPvs)
            {
                return Failed(new(
                    MapRenderWorldDpvsCameraTraversalFailureKind.SkipPvsDisablesCellCommands,
                    "Native r_skipPvs suppresses every camera cell command; no preview visibility command set can be materialized."));
            }

            IReadOnlyList<MapRenderWorldDpvsCellCullCommandData> commands;
            MapRenderWorldDpvsCameraSkyCullInput skyCullInput =
                cameraFrame.SkyCullInput;
            if (cameraCellIndex < 0)
            {
                context.TryBuildOutsideWorldCommands(out commands);
            }
            else if (settings.SingleCell)
            {
                context.TryBuildSingleCellCommand(
                    cameraCellIndex,
                    out commands);
                skyCullInput =
                    MapRenderWorldDpvsCameraSkyCullInput.Disabled;
            }
            else if (!context.TryTraverse(cameraCellIndex, out commands))
            {
                return Failed(context.Failure!);
            }

            var commandSet = new MapRenderWorldDpvsViewCommandSet(
                MapRenderWorldDpvsViewIndex.Camera,
                MapRenderWorldDpvsCommandOrigin.CameraPortalTraversal,
                commands,
                cameraStartCellIndex: cameraCellIndex);
            return MapRenderWorldDpvsCameraTraversalBuildResult.Succeeded(
                new(
                    cameraCellIndex,
                    cameraFrame,
                    commandSet,
                    skyCullInput));
        }
        finally
        {
            traversalWorkspace.Exit();
        }
    }

    private static MapRenderWorldDpvsCameraTraversalBuildResult Failed(
        MapRenderWorldDpvsCameraTraversalFailure failure) =>
        MapRenderWorldDpvsCameraTraversalBuildResult.Failed(failure);
}
