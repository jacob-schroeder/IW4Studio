using IW4.Render.OpenGl.Targets;
using IW4.Render.EditorPreview;
using IW4.Render.SceneBuilding;
using IW4.Render.Scheduling.Clear;
using IW4.Render.Scheduling.FramePlans;
using IW4.Render.Preview;
using IW4.Render.Resources;

namespace IW4.Render.OpenGl.Presentation;

/// <summary>
/// One EditorPreview-owned offscreen frame. Existing EditorPreview draws run
/// between the scene-target begin and the default presentation adapter; their
/// shaders and draw ordering are not rewritten by this plan.
/// </summary>
public sealed class EditorPresentationFrame
{
    internal EditorPresentationFrame(
        MapRenderOpenGlNormalCameraSceneTargetPlan sceneTarget,
        MapRenderOpenGlNormalCameraDefaultPresentationAdapterPlan presentation,
        MapRenderPixelExtent hostFramebufferExtent)
    {
        ArgumentNullException.ThrowIfNull(sceneTarget);
        ArgumentNullException.ThrowIfNull(presentation);
        if (!ReferenceEquals(sceneTarget, presentation.SceneTarget) ||
            !ReferenceEquals(
                sceneTarget.Binding.Resource.ColorResource,
                presentation.SceneColor.Resource) ||
            sceneTarget.FrameRevision != presentation.FrameRevision ||
            !string.Equals(
                sceneTarget.ContextIdentity,
                presentation.ContextIdentity,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Live Preview scene and presentation stages must own one exact target-2 frame.");
        }

        SceneTarget = sceneTarget;
        Presentation = presentation;
        if (hostFramebufferExtent.Width <= 0 ||
            hostFramebufferExtent.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(hostFramebufferExtent));
        }
        HostFramebufferExtent = hostFramebufferExtent;
    }

    public MapRenderOpenGlNormalCameraSceneTargetPlan SceneTarget { get; }

    public RenderFramePlan FramePlan => SceneTarget.FramePlan;

    public MapRenderOpenGlNormalCameraDefaultPresentationAdapterPlan
        Presentation { get; }

    public MapRenderPixelExtent SceneTargetExtent => new(
        Presentation.DisplayWidth,
        Presentation.DisplayHeight);

    public MapRenderPixelExtent HostFramebufferExtent { get; }

    public MapRenderSurfaceExtents SurfaceExtents => new(
        SceneTargetExtent,
        HostFramebufferExtent);

    public bool RequiresLinearHostScale =>
        SceneTargetExtent != HostFramebufferExtent;

    public string ContextIdentity => SceneTarget.ContextIdentity;

    public long FrameRevision => SceneTarget.FrameRevision;

    public uint HostBackBufferFramebufferHandle =>
        Presentation.HostBackBufferFramebufferHandle;
}

public static class EditorPresentationFramePlanner
{
    public static
        EditorPresentationFrame Create(
            MapRenderOpenGlNormalCameraTargetSet targets,
            MapRenderWorldSceneSource source,
            long frameRevision,
            MapRenderNormalCameraClearColorResult clearColor)
    {
        ArgumentNullException.ThrowIfNull(targets);
        return Create(
            targets,
            source,
            frameRevision,
            clearColor,
            new MapRenderPixelExtent(
                targets.DisplayWidth,
                targets.DisplayHeight));
    }

    public static
        EditorPresentationFrame Create(
            MapRenderOpenGlNormalCameraTargetSet targets,
            MapRenderWorldSceneSource source,
            long frameRevision,
            MapRenderNormalCameraClearColorResult clearColor,
        MapRenderPixelExtent hostFramebufferExtent,
        MapRenderEditorPreviewEffectivePostState? effectivePost = null)
    {
        ValidateInputs(targets, source, clearColor);
        MapRenderPixelExtent sceneTargetExtent = new(
            targets.DisplayWidth,
            targets.DisplayHeight);
        RenderFramePlan framePlan =
            RenderFramePlanner.CreateNormalCameraSceneTarget(
                frameRevision,
                new MapRenderSurfaceExtents(
                    sceneTargetExtent,
                    hostFramebufferExtent),
                clearColor);
        return Lower(
            targets,
            source,
            clearColor,
            hostFramebufferExtent,
            effectivePost,
            framePlan);
    }

    public static EditorPresentationFrame Create(
        MapRenderOpenGlNormalCameraTargetSet targets,
        MapRenderWorldSceneSource source,
        long frameRevision,
        MapRenderNormalCameraClearColorResult clearColor,
        MapRenderPixelExtent hostFramebufferExtent,
        RenderSceneSnapshot sceneSnapshot,
        MapRenderCamera camera,
        RenderPreviewSettings previewSettings,
        MapRenderEditorPreviewEffectivePostState? effectivePost = null)
    {
        ValidateInputs(targets, source, clearColor);
        ArgumentNullException.ThrowIfNull(sceneSnapshot);
        MapRenderPixelExtent sceneTargetExtent = new(
            targets.DisplayWidth,
            targets.DisplayHeight);
        RenderFramePlan framePlan = RenderFramePlanner.CreateNormalCameraFrame(
            frameRevision,
            new MapRenderSurfaceExtents(
                sceneTargetExtent,
                hostFramebufferExtent),
            clearColor,
            sceneSnapshot,
            camera,
            previewSettings);
        return Lower(
            targets,
            source,
            clearColor,
            hostFramebufferExtent,
            effectivePost,
            framePlan);
    }

    private static EditorPresentationFrame Lower(
        MapRenderOpenGlNormalCameraTargetSet targets,
        MapRenderWorldSceneSource source,
        MapRenderNormalCameraClearColorResult clearColor,
        MapRenderPixelExtent hostFramebufferExtent,
        MapRenderEditorPreviewEffectivePostState? effectivePost,
        RenderFramePlan framePlan)
    {
        MapRenderOpenGlNormalCameraSceneTargetPlan scene =
            MapRenderOpenGlNormalCameraSceneTargetPlanner.LowerPs3(
                targets.DepthFrame,
                framePlan,
                clearColor);
        MapRenderOpenGlNormalCameraDefaultPresentationAdapterPlan
            presentation =
                MapRenderOpenGlNormalCameraDefaultPresentationAdapterPlanner
                    .Create(scene, source, effectivePost);
        return new
            EditorPresentationFrame(
                scene,
                presentation,
                hostFramebufferExtent);
    }

    private static void ValidateInputs(
        MapRenderOpenGlNormalCameraTargetSet targets,
        MapRenderWorldSceneSource source,
        MapRenderNormalCameraClearColorResult clearColor)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(clearColor);
        if (!string.Equals(
                targets.ContextIdentity,
                targets.DepthFrame.ContextIdentity,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Live Preview target set changed its OpenGL context.",
                nameof(targets));
        }
    }
}
