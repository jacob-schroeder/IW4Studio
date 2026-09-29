using System.Text.Json;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using IW4.Formats.SourceFormat.Image;
using IW4.Game.Assets.Image;
using IW4.Render.Textures;
using Vector3 = System.Numerics.Vector3;

namespace Iw4Radiant.Materials;

internal static class MaterialImages
{
    internal static unsafe Bitmap Load(MaterialSource material, int maximumDimension)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumDimension, 1);
        if (material.IsWater)
            return CreateWaterPreview(material, Math.Min(maximumDimension, 128));
        Bitmap? bitmap = null;
        if (!material.IsSky && material.SourceImage is { } sourceImage)
        {
            try
            {
                var exchange = new ImageExchange();
                if (exchange.ReadSourceVersion(sourceImage.Root, sourceImage.Name) == 2)
                    bitmap = ReadNativeBitmap(exchange, sourceImage.Root, sourceImage.Name);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    $"Image '{sourceImage.Name}' has invalid source metadata.", exception);
            }
        }
        if (bitmap is null &&
            (material.IsSky || Path.GetExtension(material.ImagePath).Equals(".dds", StringComparison.OrdinalIgnoreCase)))
        {
            ImageSourceMipLevel mip = ReadDdsMip(material, maximumDimension,
                material.IsSky ? ImageFileShape.Cube : ImageFileShape.TwoDimensional);
            // A sky preview shows its actual first DDS face (+X), not a synthesized environment.
            using var pixels = mip.RgbaBytes.Pin();
            bitmap = new Bitmap(PixelFormat.Rgba8888, AlphaFormat.Unpremul, (nint)pixels.Pointer,
                new PixelSize(mip.Width, mip.Height), new Vector(96, 96), checked(mip.Width * 4));
        }
        else if (bitmap is null)
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

    private static unsafe Bitmap ReadNativeBitmap(ImageExchange exchange, string root, string name)
    {
        GfxImageAsset image = exchange.Link(root, name, out IReadOnlyList<byte[]> parts);
        if (image.MapType != MapType.TwoDimensional ||
            image.DimensionCount != GfxImageDimension.TwoDimensional ||
            image.IsCubemap || image.Depth != 1)
            throw new NotSupportedException(
                $"Native image '{name}' is {image.MapType}/{image.DimensionCount} " +
                $"(cubemap={image.IsCubemap}, depth={image.Depth}); material previews require a 2D image.");
        if (!GfxImagePreviewDecoder.TryDecodeBestAvailable(image, new NativeImageParts(name, parts),
                out GfxImagePreviewSnapshot? decoded, out string reason) || decoded is null)
            throw new NotSupportedException($"Native image '{name}' cannot be previewed: {reason}");
        byte[] pixels = decoded.GetRgbaBytesCopy();
        RsxTextureSwizzleDecoder.Decode(RsxTextureCommandBuilder.FromImage(image)).ApplyToRgba(pixels);
        fixed (byte* address = pixels)
            return new Bitmap(PixelFormat.Rgba8888, AlphaFormat.Unpremul, (nint)address,
                new PixelSize(decoded.Width, decoded.Height), new Vector(96, 96), checked(decoded.Width * 4));
    }

    private static unsafe Bitmap CreateWaterPreview(MaterialSource material, int size)
    {
        var pixels = new byte[checked(size * size * 4)];
        // A static tint swatch; the camera owns the GPU simulation and reflections.
        Vector3 color = new(MathF.Sqrt(Math.Clamp(material.WaterColor.X, 0, 1)),
            MathF.Sqrt(Math.Clamp(material.WaterColor.Y, 0, 1)),
            MathF.Sqrt(Math.Clamp(material.WaterColor.Z, 0, 1)));
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int offset = (y * size + x) * 4;
            pixels[offset] = (byte)(Math.Clamp(color.X, 0, 1) * 255);
            pixels[offset + 1] = (byte)(Math.Clamp(color.Y, 0, 1) * 255);
            pixels[offset + 2] = (byte)(Math.Clamp(color.Z, 0, 1) * 255);
            pixels[offset + 3] = byte.MaxValue;
        }
        fixed (byte* address = pixels)
            return new Bitmap(PixelFormat.Rgba8888, AlphaFormat.Unpremul, (nint)address,
                new PixelSize(size, size), new Vector(96, 96), checked(size * 4));
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
