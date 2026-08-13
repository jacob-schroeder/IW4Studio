namespace IW4.Assets.Assets.Image;

/// <summary>
/// PS3 GfxImage pixel-storage sizing.
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

    private static long Align(long value, int alignment)
    {
        return checked((value + alignment - 1) / alignment * alignment);
    }
}
