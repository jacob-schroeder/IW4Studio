using System.Numerics;

using IW4.Render.Scheduling.FramePlans;
using IW4.Render.Transforms;

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
        if (!MapRenderMatrixValidation.IsFinite(hostWorldViewProjection))
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
