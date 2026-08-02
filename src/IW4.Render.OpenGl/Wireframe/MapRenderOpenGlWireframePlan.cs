using System.Numerics;

using IW4.Render.Scheduling.FramePlans;

namespace IW4.Render.OpenGl.Wireframe;

/// <summary>
/// One concrete OpenGL indexed-line submission. The host matrix contains the
/// OpenGL-only render/game basis, lower-left framebuffer compensation, and
/// RSX-to-OpenGL clip-depth conversion. The semantic draw retains the original
/// PS3-native rows.
/// </summary>
internal sealed class MapRenderOpenGlWireframeDrawCommand
{
    internal MapRenderOpenGlWireframeDrawCommand(
        RenderDrawPlan semanticDraw,
        MapRenderOpenGlWireframeResourceBinding resource,
        Matrix4x4 hostWorldViewProjection)
    {
        ArgumentNullException.ThrowIfNull(semanticDraw);
        ArgumentNullException.ThrowIfNull(resource);
        if (!IsFinite(hostWorldViewProjection))
            throw new ArgumentOutOfRangeException(
                nameof(hostWorldViewProjection));

        SemanticDraw = semanticDraw;
        Resource = resource;
        HostWorldViewProjection = hostWorldViewProjection;
    }

    public RenderDrawPlan SemanticDraw { get; }

    public MapRenderOpenGlWireframeResourceBinding Resource { get; }

    public GlMesh Mesh => Resource.Mesh;

    public Matrix4x4 HostWorldViewProjection { get; }

    private static bool IsFinite(Matrix4x4 value) =>
        float.IsFinite(value.M11) && float.IsFinite(value.M12) &&
        float.IsFinite(value.M13) && float.IsFinite(value.M14) &&
        float.IsFinite(value.M21) && float.IsFinite(value.M22) &&
        float.IsFinite(value.M23) && float.IsFinite(value.M24) &&
        float.IsFinite(value.M31) && float.IsFinite(value.M32) &&
        float.IsFinite(value.M33) && float.IsFinite(value.M34) &&
        float.IsFinite(value.M41) && float.IsFinite(value.M42) &&
        float.IsFinite(value.M43) && float.IsFinite(value.M44);
}

/// <summary>
/// Immutable lowering of one shared wireframe pass. Attachment objects and
/// their load/store intent remain tied to the exact source frame-plan graph.
/// </summary>
internal sealed class MapRenderOpenGlWireframePlan
{
    internal MapRenderOpenGlWireframePlan(
        RenderFramePlan framePlan,
        RenderAttachmentDescriptor colorAttachment,
        RenderAttachmentDescriptor depthStencilAttachment,
        RenderPassPlan pass,
        RenderColorAttachmentPlan colorAttachmentPlan,
        RenderDepthStencilAttachmentPlan depthStencilAttachmentPlan,
        MapRenderOpenGlWireframeResourceCatalog resources,
        MapRenderOpenGlWireframeDrawCommand command)
    {
        ArgumentNullException.ThrowIfNull(framePlan);
        ArgumentNullException.ThrowIfNull(colorAttachment);
        ArgumentNullException.ThrowIfNull(depthStencilAttachment);
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(colorAttachmentPlan);
        ArgumentNullException.ThrowIfNull(depthStencilAttachmentPlan);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(command);
        if (!framePlan.Attachments.Any(attachment =>
                ReferenceEquals(attachment, colorAttachment)) ||
            !framePlan.Attachments.Any(attachment =>
                ReferenceEquals(attachment, depthStencilAttachment)) ||
            !framePlan.Passes.Any(candidate =>
                ReferenceEquals(candidate, pass)) ||
            pass.ColorAttachments.Length != 1 ||
            !ReferenceEquals(
                pass.ColorAttachments[0],
                colorAttachmentPlan) ||
            !ReferenceEquals(
                pass.DepthStencilAttachment,
                depthStencilAttachmentPlan) ||
            pass.Draws.Length != 1 ||
            !ReferenceEquals(pass.Draws[0], command.SemanticDraw) ||
            !ReferenceEquals(command.Resource, resources.Binding))
        {
            throw new ArgumentException(
                "An OpenGL wireframe plan must retain the exact frame-plan object graph and scene resource binding.",
                nameof(framePlan));
        }

        FramePlan = framePlan;
        ColorAttachment = colorAttachment;
        DepthStencilAttachment = depthStencilAttachment;
        Pass = pass;
        ColorAttachmentPlan = colorAttachmentPlan;
        DepthStencilAttachmentPlan = depthStencilAttachmentPlan;
        Resources = resources;
        Command = command;
    }

    public RenderFramePlan FramePlan { get; }

    public RenderAttachmentDescriptor ColorAttachment { get; }

    public RenderAttachmentDescriptor DepthStencilAttachment { get; }

    public RenderPassPlan Pass { get; }

    public RenderColorAttachmentPlan ColorAttachmentPlan { get; }

    public RenderDepthStencilAttachmentPlan DepthStencilAttachmentPlan
        { get; }

    public MapRenderOpenGlWireframeResourceCatalog Resources { get; }

    public MapRenderOpenGlWireframeDrawCommand Command { get; }

    public float LineWidth =>
        Command.SemanticDraw.Pipeline.FixedState.Raster.LineWidth;
}
