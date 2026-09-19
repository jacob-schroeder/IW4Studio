using Avalonia;
using Avalonia.OpenGL;

namespace Iw4Radiant;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new AvaloniaNativePlatformOptions
            {
                RenderingMode = [AvaloniaNativeRenderingMode.OpenGl, AvaloniaNativeRenderingMode.Software]
            })
            .With(new Win32PlatformOptions
            {
                RenderingMode = [Win32RenderingMode.Wgl, Win32RenderingMode.Software],
                WglProfiles = [new GlVersion(GlProfileType.OpenGL, 3, 3)]
            })
            .With(new X11PlatformOptions
            {
                RenderingMode = [X11RenderingMode.Glx, X11RenderingMode.Egl, X11RenderingMode.Software],
                GlProfiles = [new GlVersion(GlProfileType.OpenGL, 3, 2), new GlVersion(GlProfileType.OpenGLES, 3, 0)]
            });
    }
}
