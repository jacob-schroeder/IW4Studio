namespace IW4.Render.Materials;

/// <summary>
/// One authority for fixed-state features supported by translated RSX
/// program execution. Scene planners use the returned blockers fail-closed.
/// </summary>
public static class MapRenderStateExecutionCapability
{
    public static IReadOnlyList<string> FindBlockers(MapRenderState state)
    {
        var blockers = new List<string>(7);
        if (!state.HasState)
            blockers.Add("renderState=missing");
        if (state.AlphaTestEnabled &&
            MapRenderAlphaTest.Resolve(state) is null)
        {
            blockers.Add(
                $"renderStateAlphaTest=unsupportedTuple(" +
                $"func=0x{state.AlphaFunc:X4},ref=0x{state.AlphaRef:X2})");
        }
        if (MapRenderCull.Resolve(state) is null)
        {
            blockers.Add(
                $"renderStateCull=unsupportedTuple(" +
                $"enabled={state.CullEnabled},face=0x{state.CullFace:X4})");
        }
        if (state.PolygonMode is not 0x1B01u and not 0x1B02u)
        {
            blockers.Add(
                $"renderStatePolygonMode=unsupportedTuple(0x{state.PolygonMode:X4})");
        }
        if (state.DepthTestEnabled &&
            state.DepthFunc is < 0x0200u or > 0x0207u)
        {
            blockers.Add(
                $"renderStateDepthFunc=unsupportedTuple(0x{state.DepthFunc:X4})");
        }
        if (state.BlendEnabled &&
            !MapRenderBlend.TryResolve(state, out _))
        {
            blockers.Add(
                "renderStateBlend=unsupportedEquationOrFactorTuple");
        }
        if (state.StencilEnabled)
        {
            blockers.Add(
                "renderStateStencilMrtWriteMaskAndFaceConvention=OPEN");
        }

        return blockers.ToArray();
    }
}
