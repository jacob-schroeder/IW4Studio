using IW4.Render.Execution.FixedFunction;
using IW4.Render.Techniques;

namespace IW4.Render.Execution;

/// <summary>
/// One authority for fixed-state features supported by translated RSX
/// program execution. Scene planners use the returned blockers fail-closed.
/// </summary>
public static class RenderStateExecutionCapability
{
    public static IReadOnlyList<string> FindBlockers(RenderState state)
    {
        var blockers = new List<string>(8);
        if (!state.HasState)
            blockers.Add("renderState=missing");
        if (state.AlphaTestEnabled &&
            AlphaTest.Resolve(state) is null)
        {
            blockers.Add(
                $"renderStateAlphaTest=unsupportedTuple(" +
                $"func=0x{(uint)state.AlphaFunc:X4},ref=0x{state.AlphaRef:X2})");
        }
        if (Cull.Resolve(state) is null)
        {
            blockers.Add(
                $"renderStateCull=unsupportedTuple(" +
                $"enabled={state.CullEnabled},face=0x{(uint)state.CullFace:X4})");
        }
        if (!Enum.IsDefined(state.PolygonMode))
        {
            blockers.Add(
                $"renderStatePolygonMode=unsupportedTuple(0x{(uint)state.PolygonMode:X4})");
        }
        if (state.DepthTestEnabled &&
            !Enum.IsDefined(state.DepthFunc))
        {
            blockers.Add(
                $"renderStateDepthFunc=unsupportedTuple(0x{(uint)state.DepthFunc:X4})");
        }
        if (state.BlendEnabled &&
            !RenderBlendDecoder.TryResolve(state, out _))
        {
            blockers.Add(
                "renderStateBlend=unsupportedEquationOrFactorTuple");
        }
        if (state.StencilEnabled)
        {
            AddStencilFaceBlocker(
                blockers,
                "Front",
                state.Stencil.Front);
            AddStencilFaceBlocker(
                blockers,
                "Back",
                state.Stencil.Back);
        }
        if (!Enum.IsDefined(state.PolygonOffsetMode))
        {
            blockers.Add(
                $"renderStatePolygonOffset=unsupportedMode({(byte)state.PolygonOffsetMode})");
        }
        else if (state.PolygonOffsetMode ==
                     RenderPolygonOffsetMode.Explicit &&
                 (!float.IsFinite(state.PolygonOffsetFactor) ||
                  !float.IsFinite(state.PolygonOffsetUnits)))
        {
            blockers.Add("renderStatePolygonOffset=NONFINITE");
        }

        return blockers.ToArray();
    }

    private static void AddStencilFaceBlocker(
        ICollection<string> blockers,
        string faceName,
        StencilFaceState face)
    {
        if (Enum.IsDefined(face.Function) &&
            Enum.IsDefined(face.FailOperation) &&
            Enum.IsDefined(face.DepthFailOperation) &&
            Enum.IsDefined(face.PassOperation))
        {
            return;
        }

        blockers.Add(
            $"renderStateStencil{faceName}=unsupportedTuple(" +
            $"func=0x{(uint)face.Function:X4}," +
            $"fail=0x{(uint)face.FailOperation:X4}," +
            $"depthFail=0x{(uint)face.DepthFailOperation:X4}," +
            $"pass=0x{(uint)face.PassOperation:X4})");
    }
}
