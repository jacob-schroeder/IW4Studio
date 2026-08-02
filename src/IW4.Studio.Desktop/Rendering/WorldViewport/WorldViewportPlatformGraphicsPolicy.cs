using Avalonia;

namespace IW4.Studio.Desktop.Rendering.WorldViewport;

/// <summary>
/// Selects a Studio compositor backend compatible with the embedded
/// <see cref="WorldViewportControl"/>.
/// </summary>
internal static class WorldViewportPlatformGraphicsPolicy
{
    /// <summary>
    /// Avalonia 12 prefers Metal on macOS, but its
    /// <c>OpenGlControlBase</c> cannot create the OpenGL sharing resources
    /// required by the world viewport through that compositor. Native OpenGL
    /// must therefore be selected before the software fallback.
    /// </summary>
    internal static AvaloniaNativePlatformOptions
        CreateNativePlatformOptions() =>
        new()
        {
            RenderingMode =
            [
                AvaloniaNativeRenderingMode.OpenGl,
                AvaloniaNativeRenderingMode.Software
            ]
        };
}
