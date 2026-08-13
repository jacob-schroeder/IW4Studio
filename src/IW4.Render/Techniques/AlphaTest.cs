namespace IW4.Render.Techniques;

/// <summary>Host-emulation mode for a PS3 fixed-function alpha test.</summary>
public enum AlphaTestMode
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
public static class AlphaTest
{
    public static AlphaTestMode? Resolve(RenderState state)
    {
        if (!state.AlphaTestEnabled)
            return AlphaTestMode.Disabled;

        return (state.AlphaFunc, state.AlphaRef) switch
        {
            (0x0204u, 0x00) => AlphaTestMode.GreaterZero,
            (0x0201u, 0x80) => AlphaTestMode.Less128,
            (0x0206u, 0x80) => AlphaTestMode.GreaterEqual128,
            _ => null
        };
    }
}
