using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using IW4.Formats.Image;
using IW4.Formats.SourceFormat.Image;
using IW4.Game.Assets.Image;
using IW4.Render.Textures;
using IW4.Runtime.Assets.Images;
using SkiaSharp;

namespace IW4.Studio.Desktop.Editors.Image;

internal static class ImageEditorPixels
{
    private const int MaximumPngDimension = 16_384;
    private const int MaximumDecodedPngBytes = 256 * 1024 * 1024;
    private const int MaximumEncodedPngBytes = MaximumDecodedPngBytes + 16 * 1024 * 1024;

    internal static (IReadOnlyList<ImageEditorMip> Mips, string Status) LoadAsset(
        GfxImageAsset image,
        IGfxImagePayloadResolver resolver,
        CancellationToken cancellationToken)
    {
        if (image.IsCubemap || image.MapType == MapType.Cube)
            return ([], "Cubemap preview is unavailable.");
        if (image.MapType == MapType.ThreeDimensional || image.Depth > 1 ||
            image.DimensionCount == GfxImageDimension.ThreeDimensional)
            return ([], "Volume image preview is unavailable.");
        if (image.MapType != MapType.TwoDimensional || image.DimensionCount != GfxImageDimension.TwoDimensional)
            return ([], $"Preview is unavailable for {image.MapType}/{image.DimensionCount} images.");

        var (width, height, expectedCount) = GetSourceExtent(image);
        var sourceSize = new PixelSize(width, height);
        var decodedMips = new SortedDictionary<int, ImageEditorMip>();
        string streamReason = string.Empty;
        string decodeReason = "no resident image payload";
        if (image.StreamImageIndex is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (resolver.TryResolveMipPayloads(image, out IReadOnlyList<GfxImagePayload> mips, out streamReason))
            {
                foreach (GfxImagePayload mip in mips)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int level = FindLevel(sourceSize, mip.Width, mip.Height);
                    if (level >= 0 && !decodedMips.ContainsKey(level))
                        AddMip(image, decodedMips, level, mip.Width, mip.Height, mip.Payload, out decodeReason);
                }
            }
        }

        int residentLevel = FindLevel(sourceSize, image.Width, image.Height);
        if (image.PayloadBytes.Count > 0 && residentLevel >= 0)
        {
            if (!decodedMips.ContainsKey(residentLevel))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddMip(image, decodedMips, residentLevel, image.Width, image.Height, image.PayloadBytes, out decodeReason);
            }

            AddResidentTail(image, decodedMips, residentLevel, cancellationToken);
        }

        if (decodedMips.Count == 0)
        {
            string reason = string.IsNullOrWhiteSpace(streamReason) ? decodeReason : streamReason;
            return ([], $"Preview unavailable · {reason}");
        }

        ImageEditorMip[] available = decodedMips.Values.ToArray();
        expectedCount = Math.Max(expectedCount, available[^1].Level + 1);
        string status = available.Length < expectedCount
            ? $"{available.Length:N0} of {expectedCount:N0} mips available"
            : string.Empty;
        if (available[0].Level != 0)
        {
            status = status.Length == 0 ? "Full-resolution mip unavailable" :
                $"{status} · Full-resolution mip unavailable";
        }
        return (available, status);
    }

    internal static (int Width, int Height, int Levels) GetSourceExtent(GfxImageAsset image)
    {
        var sourceSize = new PixelSize(image.Width, image.Height);
        if ((long)image.BaseWidth * image.BaseHeight > (long)sourceSize.Width * sourceSize.Height)
            sourceSize = new PixelSize(image.BaseWidth, image.BaseHeight);

        int levels = Math.Max(image.BaseLevelCount, image.LevelCount);
        foreach (GfxImageStreamData part in image.StreamData)
        {
            if (part.CumulativeByteCount == 0)
                continue;
            if ((long)part.Width * part.Height > (long)sourceSize.Width * sourceSize.Height)
                sourceSize = new PixelSize(part.Width, part.Height);
            levels = Math.Max(levels, part.LevelMarker);
        }

        if (image.PayloadBytes.Count > 0)
        {
            int residentLevel = FindLevel(sourceSize, image.Width, image.Height);
            if (residentLevel >= 0)
                levels = Math.Max(levels, residentLevel + Math.Max(1, (int)image.LevelCount));
        }

        return (sourceSize.Width, sourceSize.Height, levels);
    }

    private static int FindLevel(PixelSize source, int width, int height)
    {
        if (width <= 0 || height <= 0 || width > source.Width || height > source.Height)
            return -1;

        int level = 0;
        while (source.Width > width || source.Height > height)
        {
            source = new PixelSize(Math.Max(1, source.Width / 2), Math.Max(1, source.Height / 2));
            level++;
        }
        return source.Width == width && source.Height == height ? level : -1;
    }

    private static void AddResidentTail(
        GfxImageAsset image,
        SortedDictionary<int, ImageEditorMip> decodedMips,
        int firstLevel,
        CancellationToken cancellationToken)
    {
        if (image.LevelCount <= 1 ||
            image.LevelCount > 1 + System.Numerics.BitOperations.Log2((uint)Math.Max(image.Width, image.Height)))
        {
            return;
        }

        int byteCount = GfxImagePixelLayout.ComputePayloadByteCount(
            image.FormatEncoding, image.LevelCount, image.IsCubemap, image.TextureRemap,
            image.Width, image.Height, image.Depth);
        if (image.PayloadBytes.Count != byteCount)
            return;

        uint formatKey = GfxImagePixelLayout.BuildFormatKey(image.FormatEncoding, image.TextureRemap);
        byte[] payload = image.PayloadBytes as byte[] ?? image.PayloadBytes.ToArray();
        int width = image.Width;
        int height = image.Height;
        int offset = GfxImagePixelLayout.ComputeMipByteCount(formatKey, width, height, 1);
        for (int level = 1; level < image.LevelCount; level++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
            int size = GfxImagePixelLayout.ComputeMipByteCount(formatKey, width, height, 1);
            if (size == 0 || size > payload.Length - offset)
                return;
            if (!decodedMips.ContainsKey(firstLevel + level))
            {
                AddMip(image, decodedMips, firstLevel + level, width, height,
                    new ArraySegment<byte>(payload, offset, size), out _);
            }
            offset += size;
        }
    }

    private static void AddMip(
        GfxImageAsset image,
        SortedDictionary<int, ImageEditorMip> decodedMips,
        int level,
        int width,
        int height,
        IReadOnlyList<byte> payload,
        out string reason)
    {
        if (GfxImageDecoder.TryDecodeRgba(image, payload, width, height, out DecodedRgbaGfxImage mip,
                out reason, sampleNativeChannels: true))
        {
            decodedMips.Add(level, new ImageEditorMip(level, mip.Width, mip.Height, mip.RgbaBytes));
        }
    }

    internal static Bitmap CreatePreview(ImageEditorMip mip, ImagePreviewChannel channel)
    {
        if (channel == ImagePreviewChannel.Rgba)
            return CreateBitmap(mip.Width, mip.Height, mip.RgbaBytes);

        int channelIndex = channel switch
        {
            ImagePreviewChannel.Red => 0,
            ImagePreviewChannel.Green => 1,
            ImagePreviewChannel.Blue => 2,
            ImagePreviewChannel.Alpha => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(channel))
        };
        byte[] rgba = new byte[mip.RgbaBytes.Length];
        for (int offset = 0; offset < rgba.Length; offset += 4)
        {
            byte value = mip.RgbaBytes[offset + channelIndex];
            rgba[offset] = value;
            rgba[offset + 1] = value;
            rgba[offset + 2] = value;
            rgba[offset + 3] = byte.MaxValue;
        }
        return CreateBitmap(mip.Width, mip.Height, rgba);
    }

    internal static Bitmap CreateThumbnail(ImageEditorMip mip, int maxSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSize);
        int largestSide = Math.Max(mip.Width, mip.Height);
        if (largestSide <= maxSize)
            return CreateBitmap(mip.Width, mip.Height, mip.RgbaBytes);

        double scale = (double)maxSize / largestSide;
        int width = Math.Max(1, (int)Math.Round(mip.Width * scale));
        int height = Math.Max(1, (int)Math.Round(mip.Height * scale));
        var rgba = new byte[checked(width * height * 4)];
        for (int y = 0; y < height; y++)
        {
            int sourceRow = (int)((y + 0.5) * mip.Height / height) * mip.Width;
            for (int x = 0; x < width; x++)
            {
                int sourceColumn = (int)((x + 0.5) * mip.Width / width);
                mip.RgbaBytes.AsSpan((sourceRow + sourceColumn) * 4, 4).CopyTo(rgba.AsSpan((y * width + x) * 4, 4));
            }
        }
        return CreateBitmap(width, height, rgba);
    }

    private static unsafe Bitmap CreateBitmap(int width, int height, byte[] rgba)
    {
        fixed (byte* pixels = rgba)
        {
            return new Bitmap(PixelFormat.Rgba8888, AlphaFormat.Unpremul, (nint)pixels,
                new PixelSize(width, height), new Vector(96, 96), checked(width * 4));
        }
    }

    internal static byte[] EncodePng(ImageEditorMip mip)
    {
        if (mip.Level != 0)
            throw new InvalidOperationException("Only the full-resolution mip can be exported.");
        return PngWriter.WriteRgba(mip.Width, mip.Height, mip.RgbaBytes);
    }

    internal static ImageFileDocument ReadPng(Stream stream)
    {
        if (stream.CanSeek && stream.Position == 0)
        {
            if (stream.Length > MaximumEncodedPngBytes)
                throw new InvalidDataException("The encoded PNG exceeds the 272 MiB image import limit.");
            return DecodePng(stream);
        }

        using var encoded = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int count;
        while ((count = stream.Read(buffer)) != 0)
        {
            if (encoded.Length + count > MaximumEncodedPngBytes)
                throw new InvalidDataException("The encoded PNG exceeds the 272 MiB image import limit.");
            encoded.Write(buffer, 0, count);
        }
        encoded.Position = 0;
        return DecodePng(encoded);
    }

    private static unsafe ImageFileDocument DecodePng(Stream encoded)
    {
        using SKCodec codec = SKCodec.Create(new SKManagedStream(encoded, disposeManagedStream: false)) ??
            throw new InvalidDataException("The PNG image could not be decoded.");
        if (codec.EncodedFormat != SKEncodedImageFormat.Png)
            throw new InvalidDataException("The selected file does not contain PNG image data.");

        int width = codec.Info.Width;
        int height = codec.Info.Height;
        if (width is <= 0 or > MaximumPngDimension || height is <= 0 or > MaximumPngDimension)
            throw new InvalidDataException($"PNG dimensions must be between 1 and {MaximumPngDimension:N0} pixels.");

        long byteCount = (long)width * height * 4;
        if (byteCount > MaximumDecodedPngBytes)
            throw new InvalidDataException("The decoded PNG exceeds the 256 MiB image import limit.");

        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var rgba = new byte[(int)byteCount];
        SKCodecResult result;
        fixed (byte* pixels = rgba)
            result = codec.GetPixels(info, (nint)pixels);
        if (result != SKCodecResult.Success)
            throw new InvalidDataException($"The PNG image could not be decoded ({result}).");

        return ImageFileDocument.FromRgba(width, height, rgba);
    }
}
