using IW4.Render.Shaders;

namespace IW4.Render.OpenGl.Shaders;

/// <summary>
/// OpenGL-owned GLSL 330 names for the supported direct CodePixel rows.
/// </summary>
internal static class OpenGlCodePixelConstantUniformLayout
{
    internal const string ArrayName = "rsxCodePixelConst";

    internal const int Count = CodeConstantLayout.Float4Count;

    internal static string ElementName(ushort codeIndex)
    {
        if (codeIndex >= Count)
            throw new ArgumentOutOfRangeException(nameof(codeIndex));

        return $"{ArrayName}[{codeIndex}]";
    }
}
