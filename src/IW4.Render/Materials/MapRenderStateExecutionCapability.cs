namespace IW4.Render.Materials;

/// <summary>
/// One authority for fixed-state features supported by translated RSX
/// program execution. Scene planners use the returned blockers fail-closed.
/// </summary>
public static class MapRenderStateExecutionCapability
{
    public static IReadOnlyList<string> FindBlockers(MapRenderState state)
    {
        var blockers = new List<string>(2);
        if (state.AlphaTestEnabled &&
            MapRenderAlphaTest.Resolve(state) is null)
        {
            blockers.Add(
                $"renderStateAlphaTest=unsupportedTuple(" +
                $"func=0x{state.AlphaFunc:X4},ref=0x{state.AlphaRef:X2})");
        }
        if (state.StencilEnabled)
        {
            blockers.Add(
                "renderStateStencilMrtWriteMaskAndFaceConvention=OPEN");
        }

        return blockers.ToArray();
    }
}
