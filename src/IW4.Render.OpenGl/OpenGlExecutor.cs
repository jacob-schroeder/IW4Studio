using IW4.Render.OpenGl.Targets;
using IW4.Render.OpenGl.Sky;
using IW4.Render.OpenGl.Diagnostics;
using IW4.Render.OpenGl.Wireframe;
using IW4.Render.Scheduling.FramePlans;

namespace IW4.Render.OpenGl;

/// <summary>
/// Concrete OpenGL lowering/execution owner. It consumes immutable core intent
/// while API resources, state and replay mechanics remain backend-private.
/// </summary>
internal sealed class OpenGlExecutor
{
    private readonly MapRenderOpenGlNormalCameraSceneTargetReplayer _replayer;

    public OpenGlExecutor(
        MapRenderOpenGlNormalCameraSceneTargetReplayer replayer)
    {
        ArgumentNullException.ThrowIfNull(replayer);
        _replayer = replayer;
    }

    public void ExecuteSceneTarget(
        RenderFramePlan framePlan,
        MapRenderOpenGlNormalCameraSceneTargetPlan loweredPlan)
    {
        ArgumentNullException.ThrowIfNull(framePlan);
        ArgumentNullException.ThrowIfNull(loweredPlan);
        if (!ReferenceEquals(framePlan, loweredPlan.FramePlan))
        {
            throw new ArgumentException(
                "OpenGL execution requires the exact core frame plan that was lowered.",
                nameof(framePlan));
        }

        MapRenderOpenGlNormalCameraSceneTargetReplayResult replay =
            _replayer.Replay(loweredPlan);
        if (replay !=
            MapRenderOpenGlNormalCameraSceneTargetReplayResult.BoundAndCleared)
        {
            throw new InvalidOperationException(
                $"OpenGL target-2 execution returned unexpected replay state {replay}.");
        }
    }

    public void ExecuteSky(
        RenderFramePlan framePlan,
        MapRenderOpenGlNormalCameraSkyPlan loweredPlan,
        IMapRenderOpenGlNormalCameraSkyReplayApi api)
    {
        ArgumentNullException.ThrowIfNull(framePlan);
        ArgumentNullException.ThrowIfNull(loweredPlan);
        ArgumentNullException.ThrowIfNull(api);
        if (!ReferenceEquals(framePlan, loweredPlan.FramePlan))
        {
            throw new ArgumentException(
                "OpenGL execution requires the exact core frame plan that was lowered.",
                nameof(framePlan));
        }

        // Validate the complete immutable replay sequence before the first GL
        // draw so a malformed backend plan cannot partially mutate context
        // state. The concrete DrawSky implementation retains its established
        // one-default-state-reset-per-draw behavior.
        if (loweredPlan.Commands.Length != loweredPlan.SkyPass.Draws.Length)
        {
            throw new InvalidOperationException(
                "The OpenGL sky command sequence no longer matches its semantic pass.");
        }
        for (var index = 0; index < loweredPlan.Commands.Length; index++)
        {
            if (!ReferenceEquals(
                    loweredPlan.Commands[index].SemanticDraw,
                    loweredPlan.SkyPass.Draws[index]))
            {
                throw new InvalidOperationException(
                    "The OpenGL sky command sequence no longer retains exact semantic draw order.");
            }
        }

        foreach (MapRenderOpenGlNormalCameraSkyDrawCommand command in
                 loweredPlan.Commands)
        {
            api.DrawSky(command);
        }
    }

    public void ExecuteDiagnostics(
        RenderFramePlan framePlan,
        MapRenderOpenGlNormalCameraDiagnosticPlan loweredPlan,
        IMapRenderOpenGlNormalCameraDiagnosticReplayApi api)
    {
        ArgumentNullException.ThrowIfNull(framePlan);
        ArgumentNullException.ThrowIfNull(loweredPlan);
        ArgumentNullException.ThrowIfNull(api);
        if (!ReferenceEquals(framePlan, loweredPlan.FramePlan))
        {
            throw new ArgumentException(
                "OpenGL execution requires the exact core frame plan that was lowered.",
                nameof(framePlan));
        }

        // Validate the complete immutable replay sequence before the first GL
        // state transition so a malformed backend plan cannot partially
        // mutate context state. The false/true uniform transitions are
        // intentional even when either category has no draws.
        if (loweredPlan.Commands.Length !=
            loweredPlan.DiagnosticsPass.Draws.Length)
        {
            throw new InvalidOperationException(
                "The OpenGL diagnostic command sequence no longer matches its semantic pass.");
        }
        bool reachedInstanced = false;
        for (var index = 0; index < loweredPlan.Commands.Length; index++)
        {
            MapRenderOpenGlNormalCameraDiagnosticDrawCommand command =
                loweredPlan.Commands[index];
            if (!ReferenceEquals(
                    command.SemanticDraw,
                    loweredPlan.DiagnosticsPass.Draws[index]))
            {
                throw new InvalidOperationException(
                    "The OpenGL diagnostic command sequence no longer retains exact semantic draw order.");
            }
            if (command.IsInstanced)
            {
                reachedInstanced = true;
            }
            else if (reachedInstanced)
            {
                throw new InvalidOperationException(
                    "The OpenGL diagnostic command sequence interleaves non-instanced draws after instanced draws.");
            }
        }

        api.SetUseInstancing(enabled: false);
        foreach (MapRenderOpenGlNormalCameraDiagnosticDrawCommand command in
                 loweredPlan.Commands)
        {
            if (command.IsInstanced)
                continue;
            api.DrawNonInstanced(command);
        }

        api.SetUseInstancing(enabled: true);
        foreach (MapRenderOpenGlNormalCameraDiagnosticDrawCommand command in
                 loweredPlan.Commands)
        {
            if (!command.IsInstanced)
                continue;
            api.DrawInstanced(command);
        }
    }

    public void ExecuteWireframe(
        RenderFramePlan framePlan,
        MapRenderOpenGlWireframePlan loweredPlan,
        IMapRenderOpenGlWireframeReplayApi api)
    {
        ArgumentNullException.ThrowIfNull(framePlan);
        ArgumentNullException.ThrowIfNull(loweredPlan);
        ArgumentNullException.ThrowIfNull(api);
        if (!ReferenceEquals(framePlan, loweredPlan.FramePlan))
        {
            throw new ArgumentException(
                "OpenGL execution requires the exact core frame plan that was lowered.",
                nameof(framePlan));
        }

        // Validate every immutable relationship before the first state change.
        // This keeps malformed plans from partially mutating the context.
        if (loweredPlan.Pass.Draws.Length != 1 ||
            !ReferenceEquals(
                loweredPlan.Pass.Draws[0],
                loweredPlan.Command.SemanticDraw) ||
            !ReferenceEquals(
                loweredPlan.Resources.Binding,
                loweredPlan.Command.Resource) ||
            loweredPlan.Command.SemanticDraw.Instances is not null ||
            loweredPlan.Command.SemanticDraw.Range.InstanceCount != 1 ||
            loweredPlan.Command.SemanticDraw.Geometry.Topology !=
                RenderPrimitiveTopology.LineList ||
            loweredPlan.Command.SemanticDraw.Geometry.IndexFormat !=
                RenderIndexFormat.Unsigned32 ||
            loweredPlan.LineWidth != 1.25f)
        {
            throw new InvalidOperationException(
                "The OpenGL wireframe command no longer retains its exact non-instanced LineList/U32 semantic draw and prepared resources.");
        }

        api.PrepareNonInstancedSolidProgram(
            loweredPlan.Command.HostWorldViewProjection);
        api.ApplyExactWireframeFixedState(
            loweredPlan.Command.SemanticDraw.Pipeline.FixedState);
        api.SetLineWidth(loweredPlan.LineWidth);
        api.DrawLinesUnsignedInt(loweredPlan.Command);
    }

}
