using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using IW4.AssetExchange.SourceFormat.Image;

namespace Iw4Radiant.Materials;

internal static class MaterialImages
{
    internal static unsafe Bitmap Load(string path, int maximumDimension)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumDimension, 1);
        Bitmap bitmap;
        using (var stream = File.OpenRead(path))
        {
            if (Path.GetExtension(path).Equals(".dds", StringComparison.OrdinalIgnoreCase))
            {
                ImageFileDocument image = new ImageExchange().Read(stream, ImageFileFormat.Dds);
                if (image.Shape != ImageFileShape.TwoDimensional)
                    throw new NotSupportedException($"Image '{Path.GetFileName(path)}' is {image.Shape}; a material color image must be two-dimensional.");
                ImageSourceMipLevel mip = image.MipLevels.FirstOrDefault(level =>
                    level.Width <= maximumDimension && level.Height <= maximumDimension);
                if (mip.Width == 0)
                    mip = image.MipLevels[^1];
                using var pixels = mip.RgbaBytes.Pin();
                bitmap = new Bitmap(PixelFormat.Rgba8888, AlphaFormat.Unpremul, (nint)pixels.Pointer,
                    new PixelSize(mip.Width, mip.Height), new Vector(96, 96), checked(mip.Width * 4));
            }
            else
                bitmap = new Bitmap(stream);
        }
        if (bitmap.PixelSize.Width <= maximumDimension && bitmap.PixelSize.Height <= maximumDimension)
            return bitmap;
        using (bitmap)
        {
            double scale = (double)maximumDimension / Math.Max(bitmap.PixelSize.Width, bitmap.PixelSize.Height);
            return bitmap.CreateScaledBitmap(new PixelSize(Math.Max(1, (int)(bitmap.PixelSize.Width * scale)),
                Math.Max(1, (int)(bitmap.PixelSize.Height * scale))), BitmapInterpolationMode.HighQuality);
        }
    }
}
