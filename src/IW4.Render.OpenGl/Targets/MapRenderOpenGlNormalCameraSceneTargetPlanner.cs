using IW4.Render.Scheduling.Lifecycle;
using IW4.Render.Scheduling.Clear;
using IW4.Render.Scheduling.FramePlans;

namespace IW4.Render.OpenGl.Targets;

/// <summary>Builds the bounded executable target-2 entry plan without GL.</summary>
public static class MapRenderOpenGlNormalCameraSceneTargetPlanner
{
    public static MapRenderOpenGlNormalCameraSceneTargetPlan CreatePs3(
        MapRenderOpenGlNormalCameraDepthStencilTargetResourceFrame resources,
        long frameRevision,
        MapRenderNormalCameraClearColorResult clearColor)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(clearColor);
        MapRenderOpenGlNormalCameraDepthStencilTargetResourceBinding binding =
            resources.GetBinding(MapRenderNormalCameraTargetKind.Scene);
        MapRenderNormalCameraTargetExtent extent = binding.Key.Extent;
        RenderFramePlan framePlan =
            RenderFramePlanner.CreateNormalCameraSceneTarget(
                frameRevision,
                MapRenderSurfaceExtents.Unified(
                    extent.LogicalWidth,
                    extent.LogicalHeight),
                clearColor);
        return LowerPs3(resources, framePlan, clearColor);
    }

    /// <summary>
    /// Resolves semantic target identities against one OpenGL resource frame.
    /// Graphics handles and API-specific target state remain in the returned
    /// backend plan and never flow back into <paramref name="framePlan"/>.
    /// </summary>
    public static MapRenderOpenGlNormalCameraSceneTargetPlan LowerPs3(
        MapRenderOpenGlNormalCameraDepthStencilTargetResourceFrame resources,
        RenderFramePlan framePlan,
        MapRenderNormalCameraClearColorResult clearColor)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(framePlan);
        ArgumentNullException.ThrowIfNull(clearColor);
        MapRenderOpenGlNormalCameraDepthStencilTargetResourceBinding binding =
            resources.GetBinding(MapRenderNormalCameraTargetKind.Scene);
        MapRenderNormalCameraTargetPlan target =
            MapRenderEditorPreviewNormalCameraRecipe.Current.GetTarget(
                MapRenderNormalCameraTargetKind.Scene);
        var antialiasing =
            new MapRenderOpenGlNormalCameraTargetAntialiasingPlan(
                target,
                binding.Key.HostSampleCount);
        return new MapRenderOpenGlNormalCameraSceneTargetPlan(
            resources,
            binding,
            framePlan,
            clearColor,
            antialiasing);
    }
}
