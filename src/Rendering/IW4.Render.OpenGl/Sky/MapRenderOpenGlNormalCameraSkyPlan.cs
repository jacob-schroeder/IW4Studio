using System.Collections.Immutable;
using System.Numerics;

using IW4.Render.Resources;
using IW4.Render.Scheduling.FramePlans;

namespace IW4.Render.OpenGl.Sky;

/// <summary>
/// One concrete OpenGL sky submission. It retains the exact semantic draw and
/// catalog binding while owning only the backend-lowered host matrix.
/// </summary>
internal sealed class MapRenderOpenGlNormalCameraSkyDrawCommand
{
    internal MapRenderOpenGlNormalCameraSkyDrawCommand(
        RenderDrawPlan semanticDraw,
        MapRenderOpenGlNormalCameraSkyResourceBinding resource,
        Matrix4x4 hostViewProjection)
    {
        ArgumentNullException.ThrowIfNull(semanticDraw);
        ArgumentNullException.ThrowIfNull(resource);
        SemanticDraw = semanticDraw;
        Resource = resource;
        HostViewProjection = hostViewProjection;
    }

    public RenderDrawPlan SemanticDraw { get; }

    public MapRenderOpenGlNormalCameraSkyResourceBinding Resource { get; }

    public GlSkyMesh Mesh => Resource.Mesh;

    public Matrix4x4 HostViewProjection { get; }
}

/// <summary>
/// Immutable lowering of the exact normal-camera sky pass. Command order is
/// identical to the backend-neutral pass and remains tied to one frame-plan
/// instance and one scene-lifetime OpenGL catalog.
/// </summary>
internal sealed class MapRenderOpenGlNormalCameraSkyPlan
{
    internal MapRenderOpenGlNormalCameraSkyPlan(
        RenderFramePlan framePlan,
        RenderPassPlan skyPass,
        MapRenderOpenGlNormalCameraSkyResourceCatalog resources,
        ImmutableArray<MapRenderOpenGlNormalCameraSkyDrawCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(framePlan);
        ArgumentNullException.ThrowIfNull(skyPass);
        ArgumentNullException.ThrowIfNull(resources);
        if (commands.IsDefault || commands.Any(command => command is null))
        {
            throw new ArgumentException(
                "OpenGL sky commands must be initialized and non-null.",
                nameof(commands));
        }
        if (!framePlan.Passes.Any(pass => ReferenceEquals(pass, skyPass)))
        {
            throw new ArgumentException(
                "The OpenGL sky pass must be the exact pass owned by its frame plan.",
                nameof(skyPass));
        }
        if (commands.Length != skyPass.Draws.Length)
        {
            throw new ArgumentException(
                "OpenGL sky command count must exactly match semantic draw count.",
                nameof(commands));
        }
        for (var index = 0; index < commands.Length; index++)
        {
            if (!ReferenceEquals(
                    commands[index].SemanticDraw,
                    skyPass.Draws[index]))
            {
                throw new ArgumentException(
                    "Every OpenGL sky command must retain its exact semantic draw object and order.",
                    nameof(commands));
            }
        }

        FramePlan = framePlan;
        SkyPass = skyPass;
        Resources = resources;
        Commands = commands;
    }

    public RenderFramePlan FramePlan { get; }

    public RenderPassPlan SkyPass { get; }

    public MapRenderOpenGlNormalCameraSkyResourceCatalog Resources { get; }

    public ImmutableArray<MapRenderOpenGlNormalCameraSkyDrawCommand> Commands
        { get; }
}
