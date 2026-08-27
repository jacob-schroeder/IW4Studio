using System.Buffers.Binary;

namespace IW4.AssetExchange.SourceFormat.Image;

internal static class ImageBlockCompressionDecoder
{
    internal static byte[] DecodeBc1(
        ReadOnlySpan<byte> encoded,
        int width,
        int height) =>
        DecodeBlocks(encoded, width, height, BlockFormat.Bc1);

    internal static byte[] DecodeBc2(
        ReadOnlySpan<byte> encoded,
        int width,
        int height) =>
        DecodeBlocks(encoded, width, height, BlockFormat.Bc2);

    internal static byte[] DecodeBc3(
        ReadOnlySpan<byte> encoded,
        int width,
        int height) =>
        DecodeBlocks(encoded, width, height, BlockFormat.Bc3);

    internal static byte[] DecodeBc5(
        ReadOnlySpan<byte> encoded,
        int width,
        int height) =>
        DecodeBlocks(encoded, width, height, BlockFormat.Bc5);

    private static byte[] DecodeBlocks(
        ReadOnlySpan<byte> encoded,
        int width,
        int height,
        BlockFormat format)
    {
        int blockByteCount = format == BlockFormat.Bc1 ? 8 : 16;
        int blockCountX = checked((width + 3) / 4);
        int blockCountY = checked((height + 3) / 4);
        int expectedByteCount = checked(
            blockCountX * blockCountY * blockByteCount);
        if (encoded.Length != expectedByteCount)
        {
            throw new InvalidDataException(
                $"Block-compressed mip contains {encoded.Length} byte(s); " +
                $"expected {expectedByteCount}.");
        }

        byte[] pixels = new byte[checked(width * height * 4)];
        int offset = 0;
        for (int blockY = 0; blockY < blockCountY; blockY++)
        {
            for (int blockX = 0; blockX < blockCountX; blockX++)
            {
                ReadOnlySpan<byte> block = encoded.Slice(
                    offset,
                    blockByteCount);
                switch (format)
                {
                    case BlockFormat.Bc1:
                        DecodeBc1Block(
                            block,
                            pixels,
                            width,
                            height,
                            blockX * 4,
                            blockY * 4);
                        break;
                    case BlockFormat.Bc2:
                        DecodeBc2Block(
                            block,
                            pixels,
                            width,
                            height,
                            blockX * 4,
                            blockY * 4);
                        break;
                    case BlockFormat.Bc3:
                        DecodeBc3Block(
                            block,
                            pixels,
                            width,
                            height,
                            blockX * 4,
                            blockY * 4);
                        break;
                    case BlockFormat.Bc5:
                        DecodeBc5Block(
                            block,
                            pixels,
                            width,
                            height,
                            blockX * 4,
                            blockY * 4);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(format));
                }
                offset += blockByteCount;
            }
        }
        return pixels;
    }

    private static void DecodeBc1Block(
        ReadOnlySpan<byte> block,
        byte[] pixels,
        int imageWidth,
        int imageHeight,
        int startX,
        int startY)
    {
        DecodeColors(
            block,
            out Rgba c0,
            out Rgba c1,
            out Rgba c2,
            out Rgba c3,
            threeColorMode: true);
        uint lookup = BinaryPrimitives.ReadUInt32LittleEndian(block[4..8]);
        WriteColorBlock(
            pixels,
            imageWidth,
            imageHeight,
            startX,
            startY,
            lookup,
            c0,
            c1,
            c2,
            c3,
            BlockAlphaMode.FromColor,
            alphaBits: 0,
            []);
    }

    private static void DecodeBc2Block(
        ReadOnlySpan<byte> block,
        byte[] pixels,
        int imageWidth,
        int imageHeight,
        int startX,
        int startY)
    {
        DecodeColors(
            block[8..],
            out Rgba c0,
            out Rgba c1,
            out Rgba c2,
            out Rgba c3,
            threeColorMode: false);
        uint lookup = BinaryPrimitives.ReadUInt32LittleEndian(block[12..16]);
        ulong alphaBits = BinaryPrimitives.ReadUInt64LittleEndian(block[..8]);
        WriteColorBlock(
            pixels,
            imageWidth,
            imageHeight,
            startX,
            startY,
            lookup,
            c0,
            c1,
            c2,
            c3,
            BlockAlphaMode.Explicit4Bit,
            alphaBits,
            []);
    }

    private static void DecodeBc3Block(
        ReadOnlySpan<byte> block,
        byte[] pixels,
        int imageWidth,
        int imageHeight,
        int startX,
        int startY)
    {
        Span<byte> alphas = stackalloc byte[8];
        BuildBc4Palette(block[..8], alphas);
        ulong alphaBits = ReadBc4Indices(block[..8]);

        DecodeColors(
            block[8..],
            out Rgba c0,
            out Rgba c1,
            out Rgba c2,
            out Rgba c3,
            threeColorMode: false);
        uint lookup = BinaryPrimitives.ReadUInt32LittleEndian(block[12..16]);
        WriteColorBlock(
            pixels,
            imageWidth,
            imageHeight,
            startX,
            startY,
            lookup,
            c0,
            c1,
            c2,
            c3,
            BlockAlphaMode.Interpolated3Bit,
            alphaBits,
            alphas);
    }

    private static void DecodeBc5Block(
        ReadOnlySpan<byte> block,
        byte[] pixels,
        int imageWidth,
        int imageHeight,
        int startX,
        int startY)
    {
        Span<byte> redPalette = stackalloc byte[8];
        Span<byte> greenPalette = stackalloc byte[8];
        BuildBc4Palette(block[..8], redPalette);
        BuildBc4Palette(block[8..16], greenPalette);
        ulong redBits = ReadBc4Indices(block[..8]);
        ulong greenBits = ReadBc4Indices(block[8..16]);

        for (int pixelY = 0; pixelY < 4; pixelY++)
        {
            int y = startY + pixelY;
            if (y >= imageHeight)
                continue;
            for (int pixelX = 0; pixelX < 4; pixelX++)
            {
                int x = startX + pixelX;
                if (x >= imageWidth)
                    continue;

                int pixelInBlock = pixelY * 4 + pixelX;
                int shift = pixelInBlock * 3;
                int output = checked((y * imageWidth + x) * 4);
                pixels[output] = redPalette[(int)((redBits >> shift) & 0x07)];
                pixels[output + 1] = greenPalette[(int)((greenBits >> shift) & 0x07)];
                pixels[output + 2] = 0;
                pixels[output + 3] = byte.MaxValue;
            }
        }
    }

    private static void BuildBc4Palette(
        ReadOnlySpan<byte> block,
        Span<byte> palette)
    {
        palette[0] = block[0];
        palette[1] = block[1];
        if (palette[0] > palette[1])
        {
            for (int index = 1; index < 7; index++)
            {
                palette[index + 1] = (byte)(
                    ((7 - index) * palette[0] + index * palette[1]) / 7);
            }
        }
        else
        {
            for (int index = 1; index < 5; index++)
            {
                palette[index + 1] = (byte)(
                    ((5 - index) * palette[0] + index * palette[1]) / 5);
            }
            palette[6] = 0;
            palette[7] = byte.MaxValue;
        }
    }

    private static ulong ReadBc4Indices(ReadOnlySpan<byte> block)
    {
        ulong indices = 0;
        for (int index = 0; index < 6; index++)
            indices |= (ulong)block[2 + index] << (8 * index);
        return indices;
    }

    private static void DecodeColors(
        ReadOnlySpan<byte> block,
        out Rgba c0,
        out Rgba c1,
        out Rgba c2,
        out Rgba c3,
        bool threeColorMode)
    {
        ushort packed0 = BinaryPrimitives.ReadUInt16LittleEndian(block[..2]);
        ushort packed1 = BinaryPrimitives.ReadUInt16LittleEndian(block[2..4]);
        c0 = FromRgb565(packed0);
        c1 = FromRgb565(packed1);
        if (threeColorMode && packed0 <= packed1)
        {
            c2 = Lerp(c0, c1, 1, 1, 2, byte.MaxValue);
            c3 = new Rgba(0, 0, 0, 0);
        }
        else
        {
            c2 = Lerp(c0, c1, 2, 1, 3, byte.MaxValue);
            c3 = Lerp(c0, c1, 1, 2, 3, byte.MaxValue);
        }
    }

    private static void WriteColorBlock(
        byte[] pixels,
        int imageWidth,
        int imageHeight,
        int startX,
        int startY,
        uint lookup,
        Rgba c0,
        Rgba c1,
        Rgba c2,
        Rgba c3,
        BlockAlphaMode alphaMode,
        ulong alphaBits,
        ReadOnlySpan<byte> alphaPalette)
    {
        Span<Rgba> colors = stackalloc[] { c0, c1, c2, c3 };
        for (int pixelY = 0; pixelY < 4; pixelY++)
        {
            int y = startY + pixelY;
            if (y >= imageHeight)
                continue;
            for (int pixelX = 0; pixelX < 4; pixelX++)
            {
                int x = startX + pixelX;
                if (x >= imageWidth)
                    continue;

                int pixelInBlock = pixelY * 4 + pixelX;
                Rgba color = colors[(int)(
                    (lookup >> (pixelInBlock * 2)) & 0x03)];
                byte alpha = alphaMode switch
                {
                    BlockAlphaMode.FromColor => color.A,
                    BlockAlphaMode.Explicit4Bit => Expand4((int)(
                        (alphaBits >> (pixelInBlock * 4)) & 0x0f)),
                    BlockAlphaMode.Interpolated3Bit => alphaPalette[(int)(
                        (alphaBits >> (pixelInBlock * 3)) & 0x07)],
                    _ => throw new ArgumentOutOfRangeException(nameof(alphaMode))
                };
                int output = checked((y * imageWidth + x) * 4);
                pixels[output] = color.R;
                pixels[output + 1] = color.G;
                pixels[output + 2] = color.B;
                pixels[output + 3] = alpha;
            }
        }
    }

    private static Rgba FromRgb565(ushort value)
    {
        int red = (value >> 11) & 0x1f;
        int green = (value >> 5) & 0x3f;
        int blue = value & 0x1f;
        return new Rgba(
            (byte)((red << 3) | (red >> 2)),
            (byte)((green << 2) | (green >> 4)),
            (byte)((blue << 3) | (blue >> 2)),
            byte.MaxValue);
    }

    private static Rgba Lerp(
        Rgba first,
        Rgba second,
        int firstWeight,
        int secondWeight,
        int divisor,
        byte alpha) =>
        new(
            (byte)((first.R * firstWeight + second.R * secondWeight) /
                   divisor),
            (byte)((first.G * firstWeight + second.G * secondWeight) /
                   divisor),
            (byte)((first.B * firstWeight + second.B * secondWeight) /
                   divisor),
            alpha);

    private static byte Expand4(int value) => (byte)((value << 4) | value);

    private enum BlockFormat
    {
        Bc1,
        Bc2,
        Bc3,
        Bc5
    }

    private enum BlockAlphaMode
    {
        FromColor,
        Explicit4Bit,
        Interpolated3Bit
    }

    private readonly record struct Rgba(byte R, byte G, byte B, byte A);
}
