using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using IW4.AssetExchange.SourceFormat.Image;

namespace Iw4Radiant.Materials;

internal static class MaterialImages
{
    internal static unsafe Bitmap Load(MaterialSource material, int maximumDimension)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumDimension, 1);
        Bitmap bitmap;
        if (material.IsSky || Path.GetExtension(material.ImagePath).Equals(".dds", StringComparison.OrdinalIgnoreCase))
        {
            ImageSourceMipLevel mip = ReadDdsMip(material, maximumDimension,
                material.IsSky ? ImageFileShape.Cube : ImageFileShape.TwoDimensional);
            // A sky preview shows its actual first DDS face (+X), not a synthesized environment.
            using var pixels = mip.RgbaBytes.Pin();
            bitmap = new Bitmap(PixelFormat.Rgba8888, AlphaFormat.Unpremul, (nint)pixels.Pointer,
                new PixelSize(mip.Width, mip.Height), new Vector(96, 96), checked(mip.Width * 4));
        }
        else
        {
            using var stream = File.OpenRead(material.ImagePath);
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

    internal static ImageSourceMipLevel LoadCube(MaterialSource material, int maximumDimension)
    {
        ImageSourceMipLevel mip = ReadDdsMip(material, maximumDimension, ImageFileShape.Cube);
        if (mip.Width > maximumDimension || mip.Height > maximumDimension)
            throw new NotSupportedException($"Sky material '{material.Name}' has no cubemap mip within the {maximumDimension}-pixel limit.");
        return mip;
    }

    private static ImageSourceMipLevel ReadDdsMip(MaterialSource material, int maximumDimension, ImageFileShape shape)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumDimension, 1);
        if (string.IsNullOrWhiteSpace(material.ImagePath))
            throw new FileNotFoundException($"Material '{material.Name}' has no available color-map image.");
        if (!Path.GetExtension(material.ImagePath).Equals(".dds", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException($"Sky material '{material.Name}' requires a DDS cubemap.");
        using var stream = File.OpenRead(material.ImagePath);
        ImageFileDocument image = new ImageExchange().Read(stream, ImageFileFormat.Dds);
        if (image.Shape != shape)
            throw new NotSupportedException($"Image '{Path.GetFileName(material.ImagePath)}' is {image.Shape}; material '{material.Name}' requires {shape}.");
        ImageSourceMipLevel mip = image.MipLevels.FirstOrDefault(level =>
            level.Width <= maximumDimension && level.Height <= maximumDimension);
        if (mip.Width == 0) mip = image.MipLevels[^1];
        if (shape == ImageFileShape.Cube && (mip.Depth != 1 || mip.Width != mip.Height ||
            mip.RgbaBytes.Length != checked(mip.Width * mip.Height * 4 * 6)))
            throw new InvalidDataException($"Sky material '{material.Name}' requires six square RGBA cube faces.");
        return mip;
    }
}
