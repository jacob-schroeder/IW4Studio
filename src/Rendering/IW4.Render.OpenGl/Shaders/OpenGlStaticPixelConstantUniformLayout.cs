namespace IW4.Render.OpenGl.Shaders;

/// <summary>
/// OpenGL-owned GLSL names for selected-pass material/literal pixel constants.
/// The selected-pass argument ordinal is a stable patch-layout slot, not a
/// material value, so identical RSX instruction topology shares one program.
/// </summary>
internal static class OpenGlStaticPixelConstantUniformLayout
{
    private const string Prefix = "rsxStaticPixelConst";

    internal static string ElementName(int argumentOrdinal)
    {
        if (argumentOrdinal < 0)
            throw new ArgumentOutOfRangeException(nameof(argumentOrdinal));

        return $"{Prefix}{argumentOrdinal}";
    }
}
