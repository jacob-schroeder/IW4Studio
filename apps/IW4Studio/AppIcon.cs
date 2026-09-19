using Avalonia.Controls;
using Avalonia.Platform;
using SkiaSharp;
using Svg.Skia;

namespace IW4.Studio.Desktop;

internal static class AppIcon
{
    private static readonly Uri LogoUri = new("avares:///Resources/logo.svg");

    public static WindowIcon? Create()
    {
        try
        {
            AssetLoader.SetDefaultAssembly(typeof(AppIcon).Assembly);
            using var source = AssetLoader.Open(LogoUri, null);
            using var svg = new SKSvg();
            svg.Load(source);

            using var png = new MemoryStream();
            if (!svg.Save(png, SKColors.Transparent, SKEncodedImageFormat.Png, 100, 1, 1))
                return null;

            png.Position = 0;
            return new WindowIcon(png);
        }
        catch
        {
            return null;
        }
    }
}
