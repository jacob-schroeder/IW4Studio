namespace IW4.Render.Materials;

/// <summary>Host-emulation mode for a PS3 fixed-function alpha test.</summary>
public enum MapRenderAlphaTestMode
{
    Disabled = 0,
    GreaterZero,
    Less128,
    GreaterEqual128
}

/// <summary>
/// Canonical classification of the PS3 alpha-function/reference tuples the
/// renderer can emulate. Backend code owns the resulting shader expression.
/// </summary>
internal static class MapRenderAlphaTest
{
    internal static MapRenderAlphaTestMode? Resolve(MapRenderState state)
    {
        if (!state.AlphaTestEnabled)
            return MapRenderAlphaTestMode.Disabled;

        return (state.AlphaFunc, state.AlphaRef) switch
        {
            (0x0204u, 0x00) => MapRenderAlphaTestMode.GreaterZero,
            (0x0201u, 0x80) => MapRenderAlphaTestMode.Less128,
            (0x0206u, 0x80) => MapRenderAlphaTestMode.GreaterEqual128,
            _ => null
        };
    }
}
