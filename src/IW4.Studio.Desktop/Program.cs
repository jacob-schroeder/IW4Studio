using Avalonia;
using IW4.Studio.Desktop.Rendering.WorldViewport;

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
            .With(
                WorldViewportPlatformGraphicsPolicy
                    .CreateNativePlatformOptions())
            .WithInterFont();
    }
}
