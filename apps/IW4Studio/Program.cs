using Avalonia;
using Avalonia.OpenGL;

namespace IW4.Studio.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // entry
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
            .With(new AvaloniaNativePlatformOptions
            {
                RenderingMode =
                [
                    AvaloniaNativeRenderingMode.OpenGl,
                    AvaloniaNativeRenderingMode.Software
                ]
            })
            .With(new Win32PlatformOptions
            {
                RenderingMode =
                [
                    Win32RenderingMode.Wgl,
                    Win32RenderingMode.Software
                ],
                WglProfiles =
                [
                    new GlVersion(GlProfileType.OpenGL, 4, 0),
                    new GlVersion(GlProfileType.OpenGL, 3, 3)
                ]
            })
            .WithInterFont();
    }
}
