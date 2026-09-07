namespace IW4.Assets.Assets.Image;

/// <summary>
/// PS3 GfxImage pixel-storage sizing and layout conversion.
/// </summary>
public static class GfxImagePixelLayout
{
    public static int ComputePayloadByteCount(
        GfxImageFormat format,
        byte levelCount,
        bool isCubemap,
        GfxImageTextureRemap textureRemap,
        ushort width,
        ushort height,
        ushort depth)
    {
        uint formatKey = BuildFormatKey(format, textureRemap);
        long byteCount = 0;

        for (int level = 0; level < levelCount; level++)
        {
            int levelWidth = System.Math.Max(1, width >> level);
            int levelHeight = System.Math.Max(1, height >> level);
            int levelDepth = System.Math.Max(1, depth >> level);
            byteCount = checked(byteCount + ComputeMipByteCount(
                formatKey,
                levelWidth,
                levelHeight,
                levelDepth));
        }

        byteCount = Align(byteCount, 0x80);
        if (isCubemap)
            byteCount = Align(checked(byteCount * 6), 0x80);

        return checked((int)byteCount);
    }

    public static uint BuildFormatKey(
        GfxImageFormat format,
        GfxImageTextureRemap textureRemap)
    {
        // Preserve format bits 7 and 0..4 and combine them with the low
        // 24 texture-control bits.
        return (textureRemap.StorageFormatBits << 8) |
               (byte)format.BaseFormat;
    }

    public static int ComputeMipByteCount(
        uint formatKey,
        int width,
        int height,
        int depth)
    {
        if (width < 1)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height < 1)
            throw new ArgumentOutOfRangeException(nameof(height));
        if (depth < 1)
            throw new ArgumentOutOfRangeException(nameof(depth));

        long byteCount = formatKey switch
        {
            0x01AAE485 or
            0x01AAE490 or
            0x01AAE49C or
            0x01AAE49E or
            0x00AAFE9F => checked((long)width * height * depth * 4),

            0x01AAE492 or
            0x01AAAB8B => checked((long)width * height * depth * 2),

            0x01A9FF81 or
            0x0156FF81 => checked((long)width * height * depth),

            0x01A9AA86 or
            0x01AA5686 or
            0x0156AA86 or
            0x01AAE486 => checked((long)((width + 3) >> 2) * ((height + 3) >> 2) * depth * 8),

            0x01AAE487 or
            0x01AAE488 => checked((long)((width + 3) >> 2) * ((height + 3) >> 2) * depth * 16),

            // Unknown format keys have no inferred payload size.
            _ => 0
        };

        return checked((int)byteCount);
    }

    public static void ReverseFourBytePixelOrder(Span<byte> pixels)
    {
        if (pixels.Length % 4 != 0)
            throw new ArgumentException("Four-byte pixel data must contain complete pixels.", nameof(pixels));
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            (pixels[offset], pixels[offset + 3]) =
                (pixels[offset + 3], pixels[offset]);
            (pixels[offset + 1], pixels[offset + 2]) =
                (pixels[offset + 2], pixels[offset + 1]);
        }
    }

    internal static byte[] DeswizzleMorton2D(
        ReadOnlySpan<byte> source,
        int width,
        int height,
        int bytesPerPixel)
    {
        ValidatePixelBuffer(source, width, height, bytesPerPixel);
        var result = new byte[source.Length];
        int log2Width = System.Numerics.BitOperations.Log2((uint)width);
        int log2Height = System.Numerics.BitOperations.Log2((uint)height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int sourcePixel = MortonIndex2D(x, y, log2Width, log2Height);
                int destinationPixel = checked(y * width + x);
                source.Slice(
                        checked(sourcePixel * bytesPerPixel),
                        bytesPerPixel)
                    .CopyTo(result.AsSpan(
                        checked(destinationPixel * bytesPerPixel),
                        bytesPerPixel));
            }
        }
        return result;
    }

    public static byte[] SwizzleMorton2D(
        ReadOnlySpan<byte> source,
        int width,
        int height,
        int bytesPerPixel)
    {
        ValidatePixelBuffer(source, width, height, bytesPerPixel);
        var result = new byte[source.Length];
        int log2Width = System.Numerics.BitOperations.Log2((uint)width);
        int log2Height = System.Numerics.BitOperations.Log2((uint)height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int sourcePixel = checked(y * width + x);
                int destinationPixel = MortonIndex2D(x, y, log2Width, log2Height);
                source.Slice(
                        checked(sourcePixel * bytesPerPixel),
                        bytesPerPixel)
                    .CopyTo(result.AsSpan(
                        checked(destinationPixel * bytesPerPixel),
                        bytesPerPixel));
            }
        }
        return result;
    }

    private static void ValidatePixelBuffer(
        ReadOnlySpan<byte> source,
        int width,
        int height,
        int bytesPerPixel)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytesPerPixel);
        if (!IsPowerOfTwo(width) || !IsPowerOfTwo(height))
            throw new NotSupportedException("Morton image conversion requires power-of-two dimensions.");
        int required = checked(width * height * bytesPerPixel);
        if (source.Length != required)
        {
            throw new InvalidDataException(
                $"The image buffer has {source.Length} bytes; expected {required}.");
        }
    }

    private static int MortonIndex2D(
        int x,
        int y,
        int log2Width,
        int log2Height)
    {
        int index = 0;
        int outputBit = 0;
        while (log2Width > 0 || log2Height > 0)
        {
            if (log2Width > 0)
            {
                index |= (x & 1) << outputBit++;
                x >>= 1;
                log2Width--;
            }
            if (log2Height > 0)
            {
                index |= (y & 1) << outputBit++;
                y >>= 1;
                log2Height--;
            }
        }
        return index;
    }

    internal static bool IsPowerOfTwo(int value) =>
        value > 0 && (value & (value - 1)) == 0;

    private static long Align(long value, int alignment)
    {
        return checked((value + alignment - 1) / alignment * alignment);
    }
}
