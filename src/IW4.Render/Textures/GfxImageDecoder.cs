using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using IW4.Assets.Assets.Image;

using IW4.Render.Export;

namespace IW4.Render.Textures;

internal static class GfxImageDecoder
{
    private const int D3dFormatA8R8G8B8 = 21;
    private const int D3dFormatX8R8G8B8 = 22;
    private const int D3dFormatR5G6B5 = 23;
    private const int D3dFormatA1R5G5B5 = 25;
    private const int D3dFormatA4R4G4B4 = 26;
    private const int D3dFormatA8 = 28;
    private const int D3dFormatL8 = 50;
    private const int D3dFormatA8L8 = 51;
    private const int D3dFormatDxt1Le = 0x31545844;
    private const int D3dFormatDxt1Be = 0x44585431;
    private const int D3dFormatDxt3Le = 0x33545844;
    private const int D3dFormatDxt3Be = 0x44585433;
    private const int D3dFormatDxt5Le = 0x35545844;
    private const int D3dFormatDxt5Be = 0x44585435;
    private const int GcmFormatA8R8G8B8 = 0x85;
    private const int GcmFormatA8R8G8B8Alt = 0x9f;
    private const int GcmFormatB8 = 0x81;
    private const int GcmFormatDxt1 = 0x86;
    private const int GcmFormatDxt23 = 0x87;
    private const int GcmFormatDxt45 = 0x88;
    private const int GcmFormatG8B8 = 0x8b;
    private const int GcmFormatD8R8G8B8 = 0x9e;
    private const int GcmLinearFlag = 0x20;
    private const uint GcmFormatKeyG8B8 = 0x01AAAB8B;

    /// <summary>
    /// Decodes a capture-time-proven, tightly packed BC subresource without
    /// consulting an image-format declaration. This is intentionally internal:
    /// callers must first establish the authored layout proof carried by
    /// <see cref="TextureAuthoredSubresource"/>.
    /// </summary>
    internal static byte[] DecodeProvenAuthoredBc(
        AuthoredBlockCompression compression,
        IReadOnlyList<byte> payloadBytes,
        int width,
        int height)
    {
        ArgumentNullException.ThrowIfNull(payloadBytes);
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));

        return compression switch
        {
            AuthoredBlockCompression.Bc1 =>
                DecodeBc1(payloadBytes, width, height),
            AuthoredBlockCompression.Bc2 =>
                DecodeBc2(payloadBytes, width, height),
            AuthoredBlockCompression.Bc3 =>
                DecodeBc3(payloadBytes, width, height),
            _ => throw new ArgumentOutOfRangeException(
                nameof(compression),
                compression,
                "A proven BC format is required.")
        };
    }

    public static bool TryDecodePng(GfxImageAsset image, out DecodedGfxImage decoded, out string reason)
    {
        return TryDecodePng(image, image.PayloadBytes, image.Width, image.Height, out decoded, out reason);
    }

    public static bool TryDecodePng(
        GfxImageAsset image,
        IReadOnlyList<byte> payloadBytes,
        int width,
        int height,
        out DecodedGfxImage decoded,
        out string reason)
    {
        decoded = default;

        if (!TryDecodeRgba(image, payloadBytes, width, height, out DecodedRgbaGfxImage rgba, out reason))
            return false;

        decoded = new DecodedGfxImage(
            rgba.Name,
            rgba.Width,
            rgba.Height,
            rgba.Format,
            rgba.HasTransparency,
            rgba.RgbaBytes,
            PngWriter.WriteRgba(rgba.Width, rgba.Height, rgba.RgbaBytes));
        return true;
    }

    public static bool TryDecodeRgba(
        GfxImageAsset image,
        IReadOnlyList<byte> payloadBytes,
        int width,
        int height,
        out DecodedRgbaGfxImage decoded,
        out string reason)
    {
        decoded = default;
        reason = string.Empty;

        if (payloadBytes.Count == 0)
        {
            reason = "no payload bytes";
            return false;
        }

        if (width <= 0 || height <= 0)
        {
            reason = $"invalid dimensions {width}x{height}";
            return false;
        }

        if (image.Depth > 1)
        {
            reason = $"3D/depth textures are not decoded yet (depth={image.Depth})";
            return false;
        }

        ImagePixelFormat pixelFormat = ResolvePixelFormat(image.Format);
        if (pixelFormat == ImagePixelFormat.Unknown)
        {
            reason = $"unsupported format 0x{image.Format:X2}";
            return false;
        }

        uint formatKey = GfxImagePixelLayout.BuildFormatKey(image.Format, image.TextureFlags);
        if (pixelFormat == ImagePixelFormat.G8B8 && formatKey != GcmFormatKeyG8B8)
        {
            reason = $"unsupported CELL_GCM_TEXTURE_G8B8 native format key 0x{formatKey:X8}";
            return false;
        }

        int expectedBytes = GetTopMipSize(width, height, pixelFormat);
        if (payloadBytes.Count < expectedBytes)
        {
            reason = $"payload has 0x{payloadBytes.Count:X} byte(s), needs 0x{expectedBytes:X}";
            return false;
        }
        if ((pixelFormat is ImagePixelFormat.Bgra32 or
                            ImagePixelFormat.Drgb32 or
                            ImagePixelFormat.G8B8 or
                            ImagePixelFormat.Luminance8) &&
            IsSwizzledGcmFormat(image.Format) &&
            (!IsPowerOfTwo(width) || !IsPowerOfTwo(height)))
        {
            string formatName = pixelFormat switch
            {
                ImagePixelFormat.Bgra32 => "CELL_GCM_TEXTURE_A8R8G8B8",
                ImagePixelFormat.G8B8 => "CELL_GCM_TEXTURE_G8B8",
                ImagePixelFormat.Luminance8 => "CELL_GCM_TEXTURE_B8",
                _ => "CELL_GCM_TEXTURE_D8R8G8B8"
            };
            reason = $"swizzled {formatName} dimensions {width}x{height} are unsupported";
            return false;
        }

        byte[] rgba;
        try
        {
            rgba = pixelFormat switch
            {
                ImagePixelFormat.Bc1 => DecodeBc1(payloadBytes, width, height),
                ImagePixelFormat.Bc2 => DecodeBc2(payloadBytes, width, height),
                ImagePixelFormat.Bc3 => DecodeBc3(payloadBytes, width, height),
                _ => DecodeLinear(payloadBytes, width, height, pixelFormat, image.Format, expectedBytes)
            };
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or ArgumentOutOfRangeException)
        {
            reason = ex.Message;
            return false;
        }

        decoded = new DecodedRgbaGfxImage(
            image.Name ?? "unnamed_image",
            width,
            height,
            DescribeFormat(image.Format),
            HasTransparency(rgba),
            rgba);
        return true;
    }

    public static bool TryDecodeCubeRgba(
        GfxImageAsset image,
        IReadOnlyList<byte> payloadBytes,
        out DecodedRgbaGfxCube decoded,
        out string reason) =>
        TryDecodeCubeRgba(
            image,
            payloadBytes,
            image.Width,
            image.Height,
            Math.Max(1, (int)image.LevelCount),
            out decoded,
            out reason);

    public static bool TryDecodeCubeRgba(
        GfxImageAsset image,
        IReadOnlyList<byte> payloadBytes,
        int width,
        int height,
        int mipCount,
        out DecodedRgbaGfxCube decoded,
        out string reason)
    {
        decoded = default;
        reason = string.Empty;
        if (image.MapType != 5 || image.MultiFaceControl == 0)
        {
            reason = $"image is not an RSX cubemap (mapType=0x{image.MapType:X2}, multiFace=0x{image.MultiFaceControl:X2})";
            return false;
        }
        if (image.Depth != 1 || width != height || !IsPowerOfTwo(width))
        {
            reason = $"unsupported cubemap dimensions {width}x{height}x{image.Depth}";
            return false;
        }
        if (mipCount <= 0 || mipCount > 16)
        {
            reason = $"unsupported cubemap mip count {mipCount}";
            return false;
        }

        ImagePixelFormat pixelFormat = ResolvePixelFormat(image.Format);
        if (pixelFormat is not (ImagePixelFormat.Bgra32 or
                                ImagePixelFormat.Bc1 or
                                ImagePixelFormat.Bc2 or
                                ImagePixelFormat.Bc3))
        {
            reason = $"cubemap format 0x{image.Format:X2} is not decoded yet";
            return false;
        }

        var mipSizes = new int[mipCount];
        int faceDataSize = 0;
        for (int mipLevel = 0; mipLevel < mipCount; mipLevel++)
        {
            int mipWidth = Math.Max(1, width >> mipLevel);
            int mipHeight = Math.Max(1, height >> mipLevel);
            mipSizes[mipLevel] = GetTopMipSize(mipWidth, mipHeight, pixelFormat);
            faceDataSize = checked(faceDataSize + mipSizes[mipLevel]);
        }

        int faceStride = Align(faceDataSize, 128);
        int requiredBytes = checked(faceStride * 6);
        if (payloadBytes.Count != requiredBytes)
        {
            reason = $"cubemap payload has 0x{payloadBytes.Count:X} bytes, exact RSX layer/mip layout needs 0x{requiredBytes:X}";
            return false;
        }

        byte[] payload = payloadBytes as byte[] ?? payloadBytes.ToArray();
        var faces = new List<IReadOnlyList<DecodedRgbaGfxImage>>(6);
        for (int face = 0; face < 6; face++)
        {
            int faceOffset = face * faceStride;
            int mipOffset = 0;
            var decodedMips = new List<DecodedRgbaGfxImage>(mipCount);
            for (int mipLevel = 0; mipLevel < mipCount; mipLevel++)
            {
                int mipWidth = Math.Max(1, width >> mipLevel);
                int mipHeight = Math.Max(1, height >> mipLevel);
                int mipSize = mipSizes[mipLevel];
                byte[] rawMip = payload.AsSpan(faceOffset + mipOffset, mipSize).ToArray();
                if (!TryDecodeRgba(image, rawMip, mipWidth, mipHeight, out DecodedRgbaGfxImage rgba, out reason))
                    return false;
                decodedMips.Add(rgba);
                mipOffset += mipSize;
            }
            faces.Add(decodedMips);
        }

        decoded = new DecodedRgbaGfxCube(faces);
        return true;
    }

    private static byte[] DeswizzleMorton2D(byte[] source, int width, int height, int bytesPerPixel)
    {
        int log2Width = BitOperations.Log2((uint)width);
        int log2Height = BitOperations.Log2((uint)height);
        byte[] result = new byte[source.Length];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int sourcePixel = MortonIndex2D(
                    x,
                    y,
                    log2Width,
                    log2Height);
                int destinationPixel = y * width + x;
                source.AsSpan(sourcePixel * bytesPerPixel, bytesPerPixel)
                    .CopyTo(result.AsSpan(destinationPixel * bytesPerPixel, bytesPerPixel));
            }
        }
        return result;
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

    private static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;

    private static int Align(int value, int alignment) => checked((value + alignment - 1) / alignment * alignment);

    private static bool HasTransparency(byte[] rgba)
    {
        for (int index = 3; index < rgba.Length; index += 4)
        {
            if (rgba[index] < 255)
                return true;
        }

        return false;
    }

    private static int GetTopMipSize(int width, int height, ImagePixelFormat format)
    {
        return format switch
        {
            ImagePixelFormat.Bc1 => checked(Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * 8),
            ImagePixelFormat.Bc2 or ImagePixelFormat.Bc3 => checked(Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * 16),
            ImagePixelFormat.Bgra32 or ImagePixelFormat.Bgrx32 or ImagePixelFormat.Drgb32 => checked(width * height * 4),
            ImagePixelFormat.G8B8 or ImagePixelFormat.Rgb565 or ImagePixelFormat.A1Rgb555 or ImagePixelFormat.Argb4444 or ImagePixelFormat.AlphaLuminance8 => checked(width * height * 2),
            ImagePixelFormat.Alpha8 or ImagePixelFormat.Luminance8 => checked(width * height),
            _ => 0
        };
    }

    private static ImagePixelFormat ResolvePixelFormat(int format)
    {
        return StripGcmFlags(format) switch
        {
            D3dFormatA8R8G8B8 or GcmFormatA8R8G8B8 or GcmFormatA8R8G8B8Alt => ImagePixelFormat.Bgra32,
            GcmFormatD8R8G8B8 => ImagePixelFormat.Drgb32,
            GcmFormatG8B8 => ImagePixelFormat.G8B8,
            D3dFormatX8R8G8B8 => ImagePixelFormat.Bgrx32,
            D3dFormatR5G6B5 => ImagePixelFormat.Rgb565,
            D3dFormatA1R5G5B5 => ImagePixelFormat.A1Rgb555,
            D3dFormatA4R4G4B4 => ImagePixelFormat.Argb4444,
            D3dFormatA8 => ImagePixelFormat.Alpha8,
            D3dFormatL8 or GcmFormatB8 => ImagePixelFormat.Luminance8,
            D3dFormatA8L8 => ImagePixelFormat.AlphaLuminance8,
            D3dFormatDxt1Le or D3dFormatDxt1Be or GcmFormatDxt1 => ImagePixelFormat.Bc1,
            D3dFormatDxt3Le or D3dFormatDxt3Be or GcmFormatDxt23 => ImagePixelFormat.Bc2,
            D3dFormatDxt5Le or D3dFormatDxt5Be or GcmFormatDxt45 => ImagePixelFormat.Bc3,
            _ => ImagePixelFormat.Unknown
        };
    }

    internal static string DescribeFormat(int format)
    {
        return StripGcmFlags(format) switch
        {
            D3dFormatA8R8G8B8 => $"D3DFMT_A8R8G8B8 (0x{format:X2})",
            D3dFormatX8R8G8B8 => $"D3DFMT_X8R8G8B8 (0x{format:X2})",
            D3dFormatR5G6B5 => $"D3DFMT_R5G6B5 (0x{format:X2})",
            D3dFormatA1R5G5B5 => $"D3DFMT_A1R5G5B5 (0x{format:X2})",
            D3dFormatA4R4G4B4 => $"D3DFMT_A4R4G4B4 (0x{format:X2})",
            D3dFormatA8 => $"D3DFMT_A8 (0x{format:X2})",
            D3dFormatL8 => $"D3DFMT_L8 (0x{format:X2})",
            D3dFormatA8L8 => $"D3DFMT_A8L8 (0x{format:X2})",
            D3dFormatDxt1Le or D3dFormatDxt1Be => $"D3DFMT_DXT1 (0x{format:X8})",
            D3dFormatDxt3Le or D3dFormatDxt3Be => $"D3DFMT_DXT3 (0x{format:X8})",
            D3dFormatDxt5Le or D3dFormatDxt5Be => $"D3DFMT_DXT5 (0x{format:X8})",
            GcmFormatA8R8G8B8 or GcmFormatA8R8G8B8Alt => $"CELL_GCM_TEXTURE_A8R8G8B8 (0x{format:X2})",
            GcmFormatB8 => $"CELL_GCM_TEXTURE_B8 (0x{format:X2})",
            GcmFormatDxt1 => $"CELL_GCM_TEXTURE_COMPRESSED_DXT1 (0x{format:X2})",
            GcmFormatDxt23 => $"CELL_GCM_TEXTURE_COMPRESSED_DXT23 (0x{format:X2})",
            GcmFormatDxt45 => $"CELL_GCM_TEXTURE_COMPRESSED_DXT45 (0x{format:X2})",
            GcmFormatG8B8 => $"CELL_GCM_TEXTURE_G8B8 (0x{format:X2})",
            GcmFormatD8R8G8B8 => $"CELL_GCM_TEXTURE_D8R8G8B8 (0x{format:X2})",
            _ => $"Unknown (0x{format:X2})"
        };
    }

    private static int StripGcmFlags(int format)
    {
        return format is >= 0x80 and <= 0xff
            ? format & 0x9f
            : format;
    }

    private static byte[] DecodeLinear(
        IReadOnlyList<byte> payloadBytes,
        int width,
        int height,
        ImagePixelFormat format,
        int rawFormat,
        int byteCount)
    {
        byte[] payload = payloadBytes as byte[] ?? payloadBytes.ToArray();
        int swizzledBytesPerPixel = format switch
        {
            ImagePixelFormat.Bgra32 => 4,
            ImagePixelFormat.Drgb32 => 4,
            ImagePixelFormat.G8B8 => 2,
            // CELL_GCM_TEXTURE_B8 participates in the same RSX Morton/Z-order
            // storage selected by the absence of CELL_GCM_TEXTURE_LN.  Keeping
            // it on the linear path turns the primary lightmap (the per-texel
            // sun/occlusion mask) into horizontal bands before OpenGL samples
            // it.  RPCS3's convert_linear_swizzle<T> handles this exact
            // one-byte T case; the PS3 descriptor proves format 0x81 without
            // the linear flag for the authored world primary lightmap.
            ImagePixelFormat.Luminance8 => 1,
            _ => 0
        };
        if (swizzledBytesPerPixel != 0 && IsSwizzledGcmFormat(rawFormat))
        {
            payload = DeswizzleMorton2D(
                payload.AsSpan(0, byteCount).ToArray(),
                width,
                height,
                swizzledBytesPerPixel);
        }
        ReadOnlySpan<byte> data = payload.AsSpan(0, byteCount);
        byte[] pixels = new byte[checked(width * height * 4)];
        switch (format)
        {
            case ImagePixelFormat.Bgra32:
                DecodeBgra32(data, pixels, IsGcmFormat(rawFormat));
                break;
            case ImagePixelFormat.Bgrx32:
                DecodeBgrx32(data, pixels);
                break;
            case ImagePixelFormat.Drgb32:
                DecodeDrgb32(data, pixels);
                break;
            case ImagePixelFormat.G8B8:
                DecodeG8B8(data, pixels);
                break;
            case ImagePixelFormat.Rgb565:
                DecodeRgb565(data, pixels);
                break;
            case ImagePixelFormat.A1Rgb555:
                DecodeA1Rgb555(data, pixels);
                break;
            case ImagePixelFormat.Argb4444:
                DecodeArgb4444(data, pixels);
                break;
            case ImagePixelFormat.Alpha8:
                DecodeAlpha8(data, pixels);
                break;
            case ImagePixelFormat.Luminance8:
                DecodeLuminance8(data, pixels);
                break;
            case ImagePixelFormat.AlphaLuminance8:
                DecodeAlphaLuminance8(data, pixels);
                break;
        }

        return pixels;
    }

    private static void DecodeBgra32(ReadOnlySpan<byte> data, byte[] pixels, bool isGcmFormat)
    {
        for (int index = 0; index < pixels.Length; index += 4)
        {
            if (isGcmFormat)
            {
                pixels[index] = data[index + 1];
                pixels[index + 1] = data[index + 2];
                pixels[index + 2] = data[index + 3];
                pixels[index + 3] = data[index];
            }
            else
            {
                pixels[index] = data[index + 2];
                pixels[index + 1] = data[index + 1];
                pixels[index + 2] = data[index];
                pixels[index + 3] = data[index + 3];
            }
        }
    }

    private static void DecodeBgrx32(ReadOnlySpan<byte> data, byte[] pixels)
    {
        for (int index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = data[index + 2];
            pixels[index + 1] = data[index + 1];
            pixels[index + 2] = data[index];
            pixels[index + 3] = 255;
        }
    }

    private static void DecodeDrgb32(ReadOnlySpan<byte> data, byte[] pixels)
    {
        for (int index = 0; index < pixels.Length; index += 4)
        {
            // CELL_GCM_TEXTURE_D8R8G8B8 stores [D,R,G,B]. RSX sampling
            // swizzles D to constant one, so D never becomes texture alpha.
            pixels[index] = data[index + 1];
            pixels[index + 1] = data[index + 2];
            pixels[index + 2] = data[index + 3];
            pixels[index + 3] = 255;
        }
    }

    private static void DecodeG8B8(ReadOnlySpan<byte> data, byte[] pixels)
    {
        for (int pixelIndex = 0; pixelIndex < pixels.Length / 4; pixelIndex++)
        {
            byte green = data[pixelIndex * 2];
            byte blue = data[pixelIndex * 2 + 1];
            WritePixel(pixels, pixelIndex, new Rgba32(green, green, green, blue));
        }
    }

    private static void DecodeRgb565(ReadOnlySpan<byte> data, byte[] pixels)
    {
        for (int pixelIndex = 0; pixelIndex < pixels.Length / 4; pixelIndex++)
            WritePixel(pixels, pixelIndex, FromRgb565(BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(pixelIndex * 2, 2))));
    }

    private static void DecodeA1Rgb555(ReadOnlySpan<byte> data, byte[] pixels)
    {
        for (int pixelIndex = 0; pixelIndex < pixels.Length / 4; pixelIndex++)
        {
            ushort value = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(pixelIndex * 2, 2));
            WritePixel(
                pixels,
                pixelIndex,
                new Rgba32(
                    Expand5((value >> 10) & 0x1f),
                    Expand5((value >> 5) & 0x1f),
                    Expand5(value & 0x1f),
                    (value & 0x8000) == 0 ? (byte)0 : (byte)255));
        }
    }

    private static void DecodeArgb4444(ReadOnlySpan<byte> data, byte[] pixels)
    {
        for (int pixelIndex = 0; pixelIndex < pixels.Length / 4; pixelIndex++)
        {
            ushort value = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(pixelIndex * 2, 2));
            WritePixel(
                pixels,
                pixelIndex,
                new Rgba32(
                    Expand4((value >> 8) & 0xf),
                    Expand4((value >> 4) & 0xf),
                    Expand4(value & 0xf),
                    Expand4((value >> 12) & 0xf)));
        }
    }

    private static void DecodeAlpha8(ReadOnlySpan<byte> data, byte[] pixels)
    {
        for (int pixelIndex = 0; pixelIndex < data.Length; pixelIndex++)
            WritePixel(pixels, pixelIndex, new Rgba32(255, 255, 255, data[pixelIndex]));
    }

    private static void DecodeLuminance8(ReadOnlySpan<byte> data, byte[] pixels)
    {
        for (int pixelIndex = 0; pixelIndex < data.Length; pixelIndex++)
        {
            byte luminance = data[pixelIndex];
            WritePixel(pixels, pixelIndex, new Rgba32(luminance, luminance, luminance, 255));
        }
    }

    private static void DecodeAlphaLuminance8(ReadOnlySpan<byte> data, byte[] pixels)
    {
        for (int pixelIndex = 0; pixelIndex < pixels.Length / 4; pixelIndex++)
        {
            byte luminance = data[pixelIndex * 2];
            byte alpha = data[pixelIndex * 2 + 1];
            WritePixel(pixels, pixelIndex, new Rgba32(luminance, luminance, luminance, alpha));
        }
    }

    private static byte[] DecodeBc1(IReadOnlyList<byte> payloadBytes, int width, int height)
    {
        return DecodeBlocks(payloadBytes, width, height, 8, (block, pixels, rowWidth, x, y) =>
        {
            DecodeColors(block, out Rgba32 c0, out Rgba32 c1, out Rgba32 c2, out Rgba32 c3, threeColorMode: true);
            uint lookup = BinaryPrimitives.ReadUInt32LittleEndian(block[4..8]);
            WriteColorBlock(pixels, rowWidth, width, height, x, y, lookup, c0, c1, c2, c3);
        });
    }

    private static byte[] DecodeBc2(IReadOnlyList<byte> payloadBytes, int width, int height)
    {
        return DecodeBlocks(payloadBytes, width, height, 16, (block, pixels, rowWidth, x, y) =>
        {
            DecodeColors(block[8..], out Rgba32 c0, out Rgba32 c1, out Rgba32 c2, out Rgba32 c3, threeColorMode: false);
            uint lookup = BinaryPrimitives.ReadUInt32LittleEndian(block[12..16]);
            ulong alpha = BinaryPrimitives.ReadUInt64LittleEndian(block[..8]);
            WriteColorBlock(pixels, rowWidth, width, height, x, y, lookup, c0, c1, c2, c3, (pixel) =>
            {
                int a = (int)((alpha >> (pixel * 4)) & 0x0f);
                return (byte)((a << 4) | a);
            });
        });
    }

    private static byte[] DecodeBc3(IReadOnlyList<byte> payloadBytes, int width, int height)
    {
        return DecodeBlocks(payloadBytes, width, height, 16, (block, pixels, rowWidth, x, y) =>
        {
            byte[] alphas = new byte[8];
            alphas[0] = block[0];
            alphas[1] = block[1];
            if (alphas[0] > alphas[1])
            {
                for (int i = 1; i < 7; i++)
                    alphas[i + 1] = (byte)(((7 - i) * alphas[0] + i * alphas[1]) / 7);
            }
            else
            {
                for (int i = 1; i < 5; i++)
                    alphas[i + 1] = (byte)(((5 - i) * alphas[0] + i * alphas[1]) / 5);
                alphas[6] = 0;
                alphas[7] = 255;
            }

            ulong alphaBits = 0;
            for (int i = 0; i < 6; i++)
                alphaBits |= (ulong)block[2 + i] << (8 * i);

            DecodeColors(block[8..], out Rgba32 c0, out Rgba32 c1, out Rgba32 c2, out Rgba32 c3, threeColorMode: false);
            uint lookup = BinaryPrimitives.ReadUInt32LittleEndian(block[12..16]);
            WriteColorBlock(pixels, rowWidth, width, height, x, y, lookup, c0, c1, c2, c3, (pixel) =>
            {
                int index = (int)((alphaBits >> (pixel * 3)) & 0x07);
                return alphas[index];
            });
        });
    }

    private delegate void DecodeBlock(ReadOnlySpan<byte> block, byte[] pixels, int width, int x, int y);

    private static byte[] DecodeBlocks(
        IReadOnlyList<byte> payloadBytes,
        int width,
        int height,
        int blockByteCount,
        DecodeBlock decodeBlock)
    {
        int blockCountX = (width + 3) / 4;
        int blockCountY = (height + 3) / 4;
        int requiredBytes = checked(blockCountX * blockCountY * blockByteCount);
        if (payloadBytes.Count < requiredBytes)
            throw new InvalidDataException($"payload has 0x{payloadBytes.Count:X} byte(s), needs 0x{requiredBytes:X}");

        byte[] payload = payloadBytes as byte[] ?? payloadBytes.ToArray();
        byte[] pixels = new byte[checked(width * height * 4)];
        int offset = 0;
        for (int blockY = 0; blockY < blockCountY; blockY++)
        {
            for (int blockX = 0; blockX < blockCountX; blockX++)
            {
                decodeBlock(payload.AsSpan(offset, blockByteCount), pixels, width, blockX * 4, blockY * 4);
                offset += blockByteCount;
            }
        }

        return pixels;
    }

    private static void DecodeColors(
        ReadOnlySpan<byte> block,
        out Rgba32 c0,
        out Rgba32 c1,
        out Rgba32 c2,
        out Rgba32 c3,
        bool threeColorMode)
    {
        ushort packed0 = BinaryPrimitives.ReadUInt16LittleEndian(block[..2]);
        ushort packed1 = BinaryPrimitives.ReadUInt16LittleEndian(block[2..4]);
        c0 = FromRgb565(packed0);
        c1 = FromRgb565(packed1);
        if (threeColorMode && packed0 <= packed1)
        {
            c2 = Lerp(c0, c1, 1, 1, 2, 255);
            c3 = new Rgba32(0, 0, 0, 0);
        }
        else
        {
            c2 = Lerp(c0, c1, 2, 1, 3, 255);
            c3 = Lerp(c0, c1, 1, 2, 3, 255);
        }
    }

    private static void WriteColorBlock(
        byte[] pixels,
        int width,
        int imageWidth,
        int imageHeight,
        int startX,
        int startY,
        uint lookup,
        Rgba32 c0,
        Rgba32 c1,
        Rgba32 c2,
        Rgba32 c3,
        Func<int, byte>? alpha = null)
    {
        Span<Rgba32> colors = stackalloc[] { c0, c1, c2, c3 };
        for (int py = 0; py < 4; py++)
        {
            int y = startY + py;
            if (y >= imageHeight)
                continue;

            for (int px = 0; px < 4; px++)
            {
                int x = startX + px;
                if (x >= imageWidth)
                    continue;

                int pixelInBlock = py * 4 + px;
                Rgba32 color = colors[(int)((lookup >> (pixelInBlock * 2)) & 0x03)];
                int output = (y * width + x) * 4;
                pixels[output] = color.R;
                pixels[output + 1] = color.G;
                pixels[output + 2] = color.B;
                pixels[output + 3] = alpha?.Invoke(pixelInBlock) ?? color.A;
            }
        }
    }

    private static Rgba32 FromRgb565(ushort value)
    {
        int r = (value >> 11) & 0x1f;
        int g = (value >> 5) & 0x3f;
        int b = value & 0x1f;
        return new Rgba32(
            (byte)((r << 3) | (r >> 2)),
            (byte)((g << 2) | (g >> 4)),
            (byte)((b << 3) | (b >> 2)),
            255);
    }

    private static Rgba32 Lerp(Rgba32 a, Rgba32 b, int aw, int bw, int div, byte alpha)
    {
        return new Rgba32(
            (byte)((a.R * aw + b.R * bw) / div),
            (byte)((a.G * aw + b.G * bw) / div),
            (byte)((a.B * aw + b.B * bw) / div),
            alpha);
    }

    private static void WritePixel(byte[] pixels, int pixelIndex, Rgba32 color)
    {
        int index = pixelIndex * 4;
        pixels[index] = color.R;
        pixels[index + 1] = color.G;
        pixels[index + 2] = color.B;
        pixels[index + 3] = color.A;
    }

    private static byte Expand4(int value)
    {
        return (byte)((value << 4) | value);
    }

    private static byte Expand5(int value)
    {
        return (byte)((value << 3) | (value >> 2));
    }

    private static bool IsGcmFormat(int format)
    {
        return format is >= 0x80 and <= 0xff;
    }

    private static bool IsSwizzledGcmFormat(int format) =>
        IsGcmFormat(format) && (format & GcmLinearFlag) == 0;


}
