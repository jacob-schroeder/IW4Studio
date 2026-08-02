using IW4.Assets.Assets.GfxMap;
using IW4.Render.Scheduling.Clear;

namespace IW4.Render.Scheduling.Dpvs;

/// <summary>
/// Composes the camera portal, Event 0x0D static, and camera
/// sky cull paths without constructing the two sun-shadow views required by
/// multi-view shadow rendering.
/// </summary>
public static class MapRenderWorldDpvsCameraOnlyVisibilityProducer
{
    public static MapRenderWorldDpvsCameraOnlyVisibilityBuildResult Build(
        GfxWorldAsset world,
        MapRenderCamera camera,
        MapRenderNormalCameraFramebufferExtent framebufferExtent,
        MapRenderNormalCameraFarPlaneState farPlane,
        MapRenderWorldDpvsPortalTraversalSettings? portalTraversalSettings =
            null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(world);
        return Build(
            world,
            camera,
            framebufferExtent,
            farPlane,
            portalTraversalSettings,
            new MapRenderWorldDpvsWorkingSet(world),
            cancellationToken);
    }

    internal static MapRenderWorldDpvsCameraOnlyVisibilityBuildResult Build(
        GfxWorldAsset world,
        MapRenderCamera camera,
        MapRenderNormalCameraFramebufferExtent framebufferExtent,
        MapRenderNormalCameraFarPlaneState farPlane,
        MapRenderWorldDpvsPortalTraversalSettings? portalTraversalSettings,
        MapRenderWorldDpvsWorkingSet workingSet,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(farPlane);
        ArgumentNullException.ThrowIfNull(workingSet);
        workingSet.ValidateWorld(world);
        cancellationToken.ThrowIfCancellationRequested();

        MapRenderWorldDpvsCameraTraversalBuildResult traversalResult =
            MapRenderWorldDpvsCameraTraversalProducer.Build(
                world,
                camera,
                framebufferExtent,
                farPlane,
                portalTraversalSettings,
                workingSet);
        cancellationToken.ThrowIfCancellationRequested();
        if (!traversalResult.IsSuccess)
        {
            MapRenderWorldDpvsCameraTraversalFailure failure =
                traversalResult.Failure!;
            return Failed(new(
                MapRenderWorldDpvsCameraOnlyVisibilityFailureKind
                    .CameraTraversalFailed,
                failure.Detail,
                CameraTraversalFailure: failure.Kind));
        }

        MapRenderWorldDpvsCameraTraversal traversal =
            traversalResult.Traversal!;
        MapRenderWorldDpvsStaticCullResult? staticResult = null;
        MapRenderWorldDpvsCameraSkyCullResult? skyResult = null;
        Parallel.Invoke(
            () =>
            {
                staticResult = MapRenderWorldDpvsStaticCellCuller.Cull(
                    world,
                    traversal.Commands,
                    workingSet);
            },
            () =>
            {
                skyResult = MapRenderWorldDpvsCameraSkyCuller.Cull(
                    world,
                    traversal.SkyCullInput,
                    workingSet);
            });
        cancellationToken.ThrowIfCancellationRequested();
        if (staticResult is null || skyResult is null)
        {
            throw new InvalidOperationException(
                "Parallel camera DPVS culling did not publish both same-frame results.");
        }
        if (!staticResult.IsSuccess)
        {
            MapRenderWorldDpvsStaticCullFailure failure =
                staticResult.Failure!;
            return Failed(new(
                MapRenderWorldDpvsCameraOnlyVisibilityFailureKind
                    .StaticCullFailed,
                failure.Detail,
                StaticCullFailure: failure.Kind));
        }

        if (!skyResult.IsSuccess)
        {
            MapRenderWorldDpvsCameraSkyCullFailure failure =
                skyResult.Failure!;
            return Failed(new(
                MapRenderWorldDpvsCameraOnlyVisibilityFailureKind
                    .CameraSkyCullFailed,
                failure.Detail,
                CameraSkyCullFailure: failure.Kind));
        }

        MapRenderWorldDpvsViewVisibility staticVisibility =
            staticResult.Visibility!;
        MapRenderWorldDpvsCameraSkyVisibility skyVisibility =
            skyResult.Visibility!;
        if (staticVisibility.SurfaceCount != skyVisibility.SurfaceCount ||
            staticVisibility.SurfaceBitSpan.Length !=
            skyVisibility.SurfaceBitSpan.Length)
        {
            throw new InvalidOperationException(
                "Camera static and sky visibility cardinalities differ.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        staticVisibility.MergeCameraSkyBeforePublication(
            skyVisibility);
        cancellationToken.ThrowIfCancellationRequested();

        return MapRenderWorldDpvsCameraOnlyVisibilityBuildResult.Succeeded(
            staticVisibility,
            traversal.CameraCellIndex);
    }

    private static MapRenderWorldDpvsCameraOnlyVisibilityBuildResult Failed(
        MapRenderWorldDpvsCameraOnlyVisibilityFailure failure) =>
        MapRenderWorldDpvsCameraOnlyVisibilityBuildResult.Failed(failure);
}
