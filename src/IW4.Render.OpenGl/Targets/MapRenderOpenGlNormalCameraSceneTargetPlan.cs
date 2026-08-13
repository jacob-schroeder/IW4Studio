using IW4.Render.Scheduling.Lifecycle;
using IW4.Render.Scheduling.Clear;
using IW4.Render.Scheduling.FramePlans;
using IW4.Render.Shaders;
using IW4.Render.OpenGl;

namespace IW4.Render.OpenGl.Targets;

/// <summary>
/// Immutable target-2 bind, logical viewport/effective surface clip, and
/// once-per-frame clear plan. It owns no draw, resolve, program, or
/// shader-readable depth view. Target 2 uses logical-size two-sample host
/// attachments while retaining the PS3 doubled-backing storage arithmetic.
/// </summary>
public sealed class MapRenderOpenGlNormalCameraSceneTargetPlan
{
    internal MapRenderOpenGlNormalCameraSceneTargetPlan(
        MapRenderOpenGlNormalCameraDepthStencilTargetResourceFrame resources,
        MapRenderOpenGlNormalCameraDepthStencilTargetResourceBinding binding,
        RenderFramePlan framePlan,
        MapRenderNormalCameraClearColorResult clearColor,
        MapRenderOpenGlNormalCameraTargetAntialiasingPlan antialiasing)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(framePlan);
        ArgumentNullException.ThrowIfNull(clearColor);
        ArgumentNullException.ThrowIfNull(antialiasing);
        if (binding.Key.Target != MapRenderNormalCameraTargetKind.Scene ||
            !ReferenceEquals(
                binding,
                resources.GetBinding(MapRenderNormalCameraTargetKind.Scene)))
        {
            throw new ArgumentException(
                "The scene execution plan requires the exact target-2 resource binding.",
                nameof(binding));
        }
        if (antialiasing.Target != MapRenderNormalCameraTargetKind.Scene ||
            antialiasing.ControlFlags !=
                RsxAntiAliasingControlFlags.Multisample ||
            antialiasing.SampleMask != ushort.MaxValue ||
            antialiasing.SurfaceAntialias !=
                RsxSurfaceAntialias.DiagonalCentered2 ||
            antialiasing.Ps3SurfaceSampleCount != 2 ||
            antialiasing.HostSampleCount != 2)
        {
            throw new ArgumentException(
                "The scene execution plan requires the exact target-2 anti-aliasing tuple.",
                nameof(antialiasing));
        }

        MapRenderOpenGlNormalCameraDepthStencilTargetKey depthKey =
            binding.Key;
        MapRenderOpenGlNormalCameraColorTargetKey colorKey =
            binding.Resource.ColorResource.Key;
        if (depthKey.HostTextureTarget !=
                MapRenderOpenGlNormalCameraTextureTarget
                    .Texture2DMultisample ||
            colorKey.HostTextureTarget != depthKey.HostTextureTarget ||
            depthKey.HostSampleCount != 2 ||
            colorKey.HostSampleCount != 2 ||
            depthKey.HostStorageWidth != depthKey.Extent.LogicalWidth ||
            depthKey.HostStorageHeight != depthKey.Extent.LogicalHeight ||
            colorKey.HostStorageWidth != colorKey.Extent.LogicalWidth ||
            colorKey.HostStorageHeight != colorKey.Extent.LogicalHeight ||
            !depthKey.HostStorageFootprintMatchesPs3Backing ||
            !colorKey.HostStorageFootprintMatchesPs3Backing)
        {
            throw new ArgumentException(
                "Target 2 requires exact logical-size two-sample RGBA8 and D24S8 host resources with the PS3 backing footprint.",
                nameof(binding));
        }

        MapRenderNormalCameraTargetExtent extent = binding.Key.Extent;
        if (extent.BackingWidth != checked(extent.LogicalWidth * 2) ||
            extent.BackingHeight != extent.LogicalHeight)
        {
            throw new ArgumentException(
                "Target 2 must retain doubled backing width and display-logical viewport dimensions.",
                nameof(binding));
        }

        RenderPassPlan semanticPass = ValidateSemanticPlan(
            framePlan,
            clearColor,
            extent,
            antialiasing.HostSampleCount);

        MapRenderSceneTargetClearPlan clear =
            MapRenderEditorPreviewNormalCameraRecipe.Current.SceneTargetClear;
        if (clear.TargetId != (int)MapRenderNormalCameraTargetKind.Scene ||
            clear.SurfaceMask !=
                (MapRenderSceneClearSurfaceMask.Rgba |
                 MapRenderSceneClearSurfaceMask.Depth |
                 MapRenderSceneClearSurfaceMask.Stencil) ||
            clear.Depth != 1.0f ||
            clear.Stencil != 0)
        {
            throw new InvalidOperationException(
                "The PS3 scene clear contract changed.");
        }

        Resources = resources;
        Binding = binding;
        FramePlan = framePlan;
        ScenePass = semanticPass;
        StencilTargetContract = new MapRenderOpenGlStencilTargetContract(
            resources.ContextIdentity,
            binding,
            clear.Stencil);
        ClearColorResult = clearColor;
        RenderColorClearValue semanticClear =
            semanticPass.ColorAttachments[0]
                .ClearValue!.Value.NormalizedColor;
        ClearColor = new MapRenderOpenGlRgbaClearColor(
            semanticClear.Red,
            semanticClear.Green,
            semanticClear.Blue,
            semanticClear.Alpha);
        Antialiasing = antialiasing;
        FragmentTargetOutputAvailability =
            new FragmentTargetOutputAvailability(
                colorKey.Ps3SurfaceTarget,
                binding.Resource.ColorResource.HostDrawBufferCount);
        RenderDepthStencilAttachmentPlan semanticDepthStencil =
            semanticPass.DepthStencilAttachment!;
        ClearSurfaceMask =
            (semanticPass.ColorAttachments[0].Load ==
                RenderAttachmentLoadRequirement.Clear
                    ? MapRenderSceneClearSurfaceMask.Rgba
                    : MapRenderSceneClearSurfaceMask.None) |
            (semanticDepthStencil.DepthLoad ==
                RenderAttachmentLoadRequirement.Clear
                    ? MapRenderSceneClearSurfaceMask.Depth
                    : MapRenderSceneClearSurfaceMask.None) |
            (semanticDepthStencil.StencilLoad ==
                RenderAttachmentLoadRequirement.Clear
                    ? MapRenderSceneClearSurfaceMask.Stencil
                    : MapRenderSceneClearSurfaceMask.None);
        ClearDepth = semanticDepthStencil.ClearDepth!.Value;
        ClearStencil = semanticDepthStencil.ClearStencil!.Value;
    }

    public MapRenderOpenGlNormalCameraDepthStencilTargetResourceFrame Resources
        { get; }

    public MapRenderOpenGlNormalCameraDepthStencilTargetResourceBinding Binding
        { get; }

    /// <summary>
    /// Exact backend-neutral intent lowered by this OpenGL plan. Resource
    /// handles and context state are owned only by the surrounding backend
    /// properties.
    /// </summary>
    public RenderFramePlan FramePlan { get; }

    public RenderPassPlan ScenePass { get; }

    /// <summary>
    /// Exact operational D24S8 ownership consumed by pass-state planning.
    /// This is frame-local state and cannot outlive or change the
    /// resource binding that created it.
    /// </summary>
    public MapRenderOpenGlStencilTargetContract StencilTargetContract
        { get; }

    public long FrameRevision => FramePlan.FrameRevision;

    public string ContextIdentity => Resources.ContextIdentity;

    public MapRenderNormalCameraTargetKind Target =>
        MapRenderNormalCameraTargetKind.Scene;

    public uint CombinedFramebufferHandle =>
        Binding.Resource.CombinedFramebufferHandle;

    public uint ColorTextureHandle =>
        Binding.Resource.ColorResource.TextureHandle;

    public MapRenderOpenGlNormalCameraTextureTarget ColorTextureTarget =>
        Binding.Resource.ColorResource.Key.HostTextureTarget;

    public MapRenderNormalCameraTargetExtent Extent => Binding.Key.Extent;

    public int ViewportX => ScenePass.Viewport.X;

    public int ViewportY => ScenePass.Viewport.Y;

    public int ViewportWidth => ScenePass.Viewport.Width;

    public int ViewportHeight => ScenePass.Viewport.Height;

    public int Ps3SurfaceClipX => 0;

    public int Ps3SurfaceClipY => 0;

    public int Ps3SurfaceClipWidth => Extent.LogicalWidth;

    public int Ps3SurfaceClipHeight => Extent.LogicalHeight;

    public int Ps3InheritedScissorX => 0;

    public int Ps3InheritedScissorY => 0;

    public int Ps3InheritedScissorWidth => 0x1000;

    public int Ps3InheritedScissorHeight => 0x1000;

    public int HostEffectiveScissorX => ScenePass.Scissor.X;

    public int HostEffectiveScissorY => ScenePass.Scissor.Y;

    public int HostEffectiveScissorWidth => ScenePass.Scissor.Width;

    public int HostEffectiveScissorHeight => ScenePass.Scissor.Height;

    public int TextureFeedbackSlotCount => 16;

    public MapRenderOpenGlRgbaClearColor ClearColor { get; }

    /// <summary>
    /// Exact semantic producer result. This target path ignores the native
    /// boolean return because its clear mask always includes RGBA.
    /// </summary>
    public MapRenderNormalCameraClearColorResult ClearColorResult { get; }

    public MapRenderSceneClearSurfaceMask ClearSurfaceMask { get; }

    public float ClearDepth { get; }

    public byte ClearStencil { get; }

    public MapRenderOpenGlNormalCameraTargetAntialiasingPlan Antialiasing
        { get; }

    public FragmentTargetOutputAvailability
        FragmentTargetOutputAvailability { get; }

    public bool EnablesEffectiveSurfaceClipScissor => true;

    public bool LeavesEffectiveSurfaceClipScissorEnabled => true;

    public bool RestoresDrawFramebuffer => false;

    public bool RestoresViewport => false;

    public bool RestoresScissor => false;

    public bool RestoresInvalidatedTextureBindings => false;

    private static RenderPassPlan ValidateSemanticPlan(
        RenderFramePlan framePlan,
        MapRenderNormalCameraClearColorResult clearColor,
        MapRenderNormalCameraTargetExtent extent,
        int hostSampleCount)
    {
        if (framePlan.Passes.Length < 1 ||
            framePlan.Attachments.Length != 2 ||
            framePlan.Passes[0].Identity !=
                RenderFramePlanner.NormalCameraScenePass ||
            framePlan.Passes[0].Purpose !=
                RenderPassPurpose.NormalCameraScene ||
            framePlan.Passes[0].Draws.Length != 0 ||
            !framePlan.PreviewRequirements.RequiresPresentation ||
            framePlan.PickingRequirements.Mode != RenderPickingMode.None)
        {
            throw new ArgumentException(
                "OpenGL target 2 requires the exact semantic normal-camera scene-entry plan.",
                nameof(framePlan));
        }

        MapRenderPixelExtent logicalExtent = new(
            extent.LogicalWidth,
            extent.LogicalHeight);
        if (framePlan.SurfaceExtents.SceneTarget != logicalExtent)
        {
            throw new ArgumentException(
                "The semantic scene target extent differs from the OpenGL target binding.",
                nameof(framePlan));
        }

        RenderAttachmentDescriptor? colorDescriptor = framePlan.Attachments
            .SingleOrDefault(attachment =>
                attachment.Identity ==
                    RenderFramePlanner.NormalCameraSceneColorAttachment);
        RenderAttachmentDescriptor? depthDescriptor = framePlan.Attachments
            .SingleOrDefault(attachment =>
                attachment.Identity ==
                    RenderFramePlanner
                        .NormalCameraSceneDepthStencilAttachment);
        if (colorDescriptor is null ||
            depthDescriptor is null ||
            colorDescriptor.Role != RenderAttachmentRole.Color ||
            colorDescriptor.PixelFormat !=
                RenderAttachmentPixelFormat.Rgba8Unorm ||
            depthDescriptor.Role != RenderAttachmentRole.DepthStencil ||
            depthDescriptor.PixelFormat !=
                RenderAttachmentPixelFormat.Depth24Stencil8 ||
            colorDescriptor.Extent != logicalExtent ||
            depthDescriptor.Extent != logicalExtent ||
            colorDescriptor.SampleCount != hostSampleCount ||
            depthDescriptor.SampleCount != hostSampleCount)
        {
            throw new ArgumentException(
                "The semantic scene attachments differ from target-2 resources.",
                nameof(framePlan));
        }

        RenderPassPlan pass = framePlan.Passes[0];
        RenderColorAttachmentPlan color = pass.ColorAttachments.Length == 1
            ? pass.ColorAttachments[0]
            : throw new ArgumentException(
                "Target 2 requires one color attachment.",
                nameof(framePlan));
        RenderDepthStencilAttachmentPlan depthStencil =
            pass.DepthStencilAttachment ?? throw new ArgumentException(
                "Target 2 requires one depth-stencil attachment.",
                nameof(framePlan));
        RenderColorClearValue expectedColor = new(
            clearColor.Red,
            clearColor.Green,
            clearColor.Blue,
            clearColor.Alpha);
        if (color.Attachment != colorDescriptor.Identity ||
            color.Load != RenderAttachmentLoadRequirement.Clear ||
            color.Store != RenderAttachmentStoreRequirement.Preserve ||
            color.ClearValue?.Kind !=
                RenderAttachmentClearValueKind.NormalizedColor ||
            color.ClearValue?.NormalizedColor != expectedColor ||
            depthStencil.Attachment != depthDescriptor.Identity ||
            depthStencil.DepthLoad !=
                RenderAttachmentLoadRequirement.Clear ||
            depthStencil.DepthStore !=
                RenderAttachmentStoreRequirement.Preserve ||
            depthStencil.ClearDepth != 1f ||
            depthStencil.StencilLoad !=
                RenderAttachmentLoadRequirement.Clear ||
            depthStencil.StencilStore !=
                RenderAttachmentStoreRequirement.Preserve ||
            depthStencil.ClearStencil != 0 ||
            pass.Viewport != new RenderViewport(
                0,
                0,
                logicalExtent.Width,
                logicalExtent.Height) ||
            pass.Scissor != new RenderScissor(
                0,
                0,
                Math.Min(logicalExtent.Width, 0x1000),
                Math.Min(logicalExtent.Height, 0x1000)))
        {
            throw new ArgumentException(
                "The semantic target-2 load/store, clear, viewport, or scissor contract changed.",
                nameof(framePlan));
        }

        return pass;
    }

}
