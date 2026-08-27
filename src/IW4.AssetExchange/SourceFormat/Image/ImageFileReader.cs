using System.Buffers.Binary;

namespace IW4.AssetExchange.SourceFormat.Image;

internal static class ImageFileReader
{
    private const int MaximumDimension = 16_384;
    private const long MaximumDecodedByteCount = 256L * 1024 * 1024;

    private const uint DdsMagic = 0x20534444;
    private const uint DdsHeaderSize = 124;
    private const uint DdsPixelFormatSize = 32;
    private const uint DdsRequiredFlags = 0x00001007;
    private const uint DdsPitchFlag = 0x00000008;
    private const uint DdsMipMapCountFlag = 0x00020000;
    private const uint DdsLinearSizeFlag = 0x00080000;
    private const uint DdsDepthFlag = 0x00800000;
    private const uint DdsAlphaPixels = 0x00000001;
    private const uint DdsFourCc = 0x00000004;
    private const uint DdsRgb = 0x00000040;
    private const uint DdsComplexCaps = 0x00000008;
    private const uint DdsTextureCaps = 0x00001000;
    private const uint DdsMipMapCaps = 0x00400000;
    private const uint DdsCubeCaps = 0x0000fe00;
    private const uint DdsVolumeCaps = 0x00200000;
    private const uint DdsResourceMiscTextureCube = 0x00000004;
    private const uint DdsTexture2DResourceDimension = 3;

    private const uint Iwi8NoMipMaps = 1u << 1;
    private const uint Iwi8GammaSrgb = 1u << 8;
    private const uint Iwi8GammaPwl = 1u << 9;
    private const uint Iwi8MapTypeMask = 3u << 16;
    private const uint Iwi8KnownFlags = 0x000003ffu | 0x000f0000u |
                                        0x07000000u;
    private const uint Iwi8FileHeaderSize = 32;

    internal static ImageFileDocument Read(
        Stream source,
        ImageFileFormat format)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
            throw new ArgumentException("The image source stream is not readable.", nameof(source));
        return format switch
        {
            ImageFileFormat.Dds => ReadDds(source),
            ImageFileFormat.Iwi8 => ReadIwi8(source),
            _ => throw new ArgumentOutOfRangeException(
                nameof(format),
                format,
                "An image file format is required.")
        };
    }

    private static ImageFileDocument ReadDds(Stream source)
    {
        var reader = new ImageStreamReader(source, "DDS");
        if (reader.ReadUInt32("magic") != DdsMagic)
            throw new InvalidDataException("The DDS magic is invalid.");
        if (reader.ReadUInt32("header size") != DdsHeaderSize)
            throw new InvalidDataException("The DDS header size is not 124 bytes.");

        uint headerFlags = reader.ReadUInt32("header flags");
        if ((headerFlags & DdsRequiredFlags) != DdsRequiredFlags)
        {
            throw new InvalidDataException(
                "The DDS header omits one or more required caps, size, or " +
                "pixel-format flags.");
        }

        int height = ReadDimension(reader.ReadUInt32("height"), "DDS height");
        int width = ReadDimension(reader.ReadUInt32("width"), "DDS width");
        uint pitchOrLinearSize = reader.ReadUInt32("pitch or linear size");
        uint depth = reader.ReadUInt32("depth");
        uint storedMipCount = reader.ReadUInt32("mip count");
        for (int index = 0; index < 11; index++)
            _ = reader.ReadUInt32($"reserved header word {index}");

        if (reader.ReadUInt32("pixel-format size") != DdsPixelFormatSize)
            throw new InvalidDataException("The DDS pixel-format size is not 32 bytes.");
        uint pixelFormatFlags = reader.ReadUInt32("pixel-format flags");
        uint fourCc = reader.ReadUInt32("pixel-format FourCC");
        uint rgbBitCount = reader.ReadUInt32("pixel bit count");
        uint redMask = reader.ReadUInt32("red mask");
        uint greenMask = reader.ReadUInt32("green mask");
        uint blueMask = reader.ReadUInt32("blue mask");
        uint alphaMask = reader.ReadUInt32("alpha mask");
        uint caps = reader.ReadUInt32("caps");
        uint caps2 = reader.ReadUInt32("caps2");
        _ = reader.ReadUInt32("caps3");
        _ = reader.ReadUInt32("caps4");
        _ = reader.ReadUInt32("reserved caps word");

        if ((caps & DdsTextureCaps) == 0)
            throw new InvalidDataException("The DDS is not marked as a texture.");
        if ((caps2 & DdsCubeCaps) != 0)
        {
            throw new NotSupportedException(
                "Cubemap DDS import is not supported by the Material editor.");
        }
        if ((caps2 & DdsVolumeCaps) != 0 ||
            (headerFlags & DdsDepthFlag) != 0 ||
            depth > 1)
        {
            throw new NotSupportedException(
                "Volume DDS import is not supported by the Material editor.");
        }

        int mipCount = ValidateDdsMipCount(
            storedMipCount,
            headerFlags,
            caps,
            width,
            height);
        PixelEncoding encoding;
        bool? usesSrgbReads;
        uint alphaMode = 0;
        if ((pixelFormatFlags & DdsFourCc) != 0)
        {
            if (fourCc == MakeFourCc('D', 'X', '1', '0'))
            {
                (encoding, usesSrgbReads, alphaMode) = ReadDdsDx10(reader);
            }
            else
            {
                encoding = fourCc switch
                {
                    var value when value == MakeFourCc('D', 'X', 'T', '1') =>
                        PixelEncoding.Bc1,
                    var value when value == MakeFourCc('D', 'X', 'T', '3') =>
                        PixelEncoding.Bc2,
                    var value when value == MakeFourCc('D', 'X', 'T', '5') =>
                        PixelEncoding.Bc3,
                    _ => throw new NotSupportedException(
                        $"DDS FourCC 0x{fourCc:X8} is not supported; expected " +
                        "DXT1, DXT3, DXT5, or DX10.")
                };
                usesSrgbReads = null;
            }
        }
        else
        {
            if ((pixelFormatFlags & (DdsRgb | DdsAlphaPixels)) !=
                (DdsRgb | DdsAlphaPixels) ||
                fourCc != 0 ||
                rgbBitCount != 32)
            {
                throw new NotSupportedException(
                    "Classic DDS import supports only 32-bit RGBA/BGRA or " +
                    "DXT1/DXT3/DXT5 data.");
            }

            encoding = (redMask, greenMask, blueMask, alphaMask) switch
            {
                (0x000000ff, 0x0000ff00, 0x00ff0000, 0xff000000) =>
                    PixelEncoding.Rgba32,
                (0x00ff0000, 0x0000ff00, 0x000000ff, 0xff000000) =>
                    PixelEncoding.Bgra32,
                _ => throw new NotSupportedException(
                    $"DDS channel masks R=0x{redMask:X8}, G=0x{greenMask:X8}, " +
                    $"B=0x{blueMask:X8}, A=0x{alphaMask:X8} are not " +
                    "supported RGBA/BGRA masks.")
            };
            usesSrgbReads = null;
        }

        if (alphaMode is 2 or 4)
        {
            throw new NotSupportedException(
                alphaMode == 2
                    ? "Premultiplied-alpha DX10 DDS import is not supported."
                    : "Custom-alpha DX10 DDS import is not supported.");
        }
        if (alphaMode > 4)
            throw new InvalidDataException("The DX10 DDS alpha mode is invalid.");

        MipLayout[] layouts = CreateMipLayouts(width, height, mipCount, encoding);
        ValidateDdsPitch(
            headerFlags,
            pitchOrLinearSize,
            layouts[0],
            encoding);
        ImageSourceMipLevel[] levels = ReadMipLevelsTopDown(
            reader,
            layouts,
            encoding,
            forceOpaqueAlpha: alphaMode == 3);
        reader.RequireEnd();
        return new ImageFileDocument(levels, usesSrgbReads);
    }

    private static (PixelEncoding Encoding, bool UsesSrgbReads, uint AlphaMode)
        ReadDdsDx10(ImageStreamReader reader)
    {
        uint dxgiFormat = reader.ReadUInt32("DX10 format");
        uint resourceDimension = reader.ReadUInt32("DX10 resource dimension");
        uint miscellaneousFlags = reader.ReadUInt32("DX10 miscellaneous flags");
        uint arraySize = reader.ReadUInt32("DX10 array size");
        uint alphaMode = reader.ReadUInt32("DX10 alpha mode");
        if (resourceDimension != DdsTexture2DResourceDimension)
        {
            throw new NotSupportedException(
                "Only two-dimensional DX10 DDS resources can be imported.");
        }
        if ((miscellaneousFlags & DdsResourceMiscTextureCube) != 0 ||
            arraySize == 6)
        {
            throw new NotSupportedException(
                "Cubemap DX10 DDS import is not supported by the Material editor.");
        }
        if (arraySize != 1)
        {
            throw new NotSupportedException(
                $"DX10 DDS texture arrays are not supported (array size {arraySize}).");
        }

        return dxgiFormat switch
        {
            28 => (PixelEncoding.Rgba32, false, alphaMode),
            29 => (PixelEncoding.Rgba32, true, alphaMode),
            71 => (PixelEncoding.Bc1, false, alphaMode),
            72 => (PixelEncoding.Bc1, true, alphaMode),
            74 => (PixelEncoding.Bc2, false, alphaMode),
            75 => (PixelEncoding.Bc2, true, alphaMode),
            77 => (PixelEncoding.Bc3, false, alphaMode),
            78 => (PixelEncoding.Bc3, true, alphaMode),
            87 => (PixelEncoding.Bgra32, false, alphaMode),
            91 => (PixelEncoding.Bgra32, true, alphaMode),
            _ => throw new NotSupportedException(
                $"DX10 DDS format {dxgiFormat} is not supported; expected " +
                "RGBA8, BGRA8, BC1, BC2, or BC3.")
        };
    }

    private static ImageFileDocument ReadIwi8(Stream source)
    {
        var reader = new ImageStreamReader(source, "IWI8");
        byte first = reader.ReadByte("magic byte 0");
        byte second = reader.ReadByte("magic byte 1");
        byte third = reader.ReadByte("magic byte 2");
        if (first != 'I' || second != 'W' || third != 'i')
            throw new InvalidDataException("The IWI magic is invalid.");

        byte version = reader.ReadByte("version");
        if (version != 8)
        {
            throw new NotSupportedException(
                $"IWI version {version} is not supported; IW4 requires IWI8.");
        }

        uint flags = reader.ReadUInt32("flags");
        if ((flags & ~Iwi8KnownFlags) != 0)
        {
            throw new InvalidDataException(
                $"IWI8 flags contain unknown bits 0x{flags & ~Iwi8KnownFlags:X8}.");
        }
        byte storedFormat = reader.ReadByte("format");
        if (reader.ReadByte("unused header byte") != 0)
            throw new InvalidDataException("The IWI8 unused header byte is not zero.");
        int width = ReadDimension(reader.ReadUInt16("width"), "IWI8 width");
        int height = ReadDimension(reader.ReadUInt16("height"), "IWI8 height");
        ushort depth = reader.ReadUInt16("depth");
        var fileSizeForPicmip = new uint[4];
        for (int index = 0; index < fileSizeForPicmip.Length; index++)
        {
            fileSizeForPicmip[index] = reader.ReadUInt32(
                $"picmip file size {index}");
        }

        if ((flags & Iwi8MapTypeMask) != 0)
        {
            throw new NotSupportedException(
                "Cubemap, volume, and one-dimensional IWI8 imports are not " +
                "supported by the Material editor.");
        }
        if (depth != 1)
        {
            throw new InvalidDataException(
                $"A two-dimensional IWI8 must have depth 1, found {depth}.");
        }

        uint gamma = flags & (Iwi8GammaSrgb | Iwi8GammaPwl);
        if ((gamma & Iwi8GammaPwl) != 0)
        {
            throw new NotSupportedException(
                "IWI8 piecewise-linear and gamma-2 sampling cannot be " +
                "represented by the Material editor.");
        }

        PixelEncoding encoding = storedFormat switch
        {
            0x01 => PixelEncoding.Bgra32,
            0x02 => PixelEncoding.Bgr24,
            0x03 => PixelEncoding.LuminanceAlpha,
            0x04 => PixelEncoding.Luminance,
            0x05 => PixelEncoding.Alpha,
            0x0b => PixelEncoding.Bc1,
            0x0c => PixelEncoding.Bc2,
            0x0d => PixelEncoding.Bc3,
            _ => throw new NotSupportedException(
                $"IWI8 format 0x{storedFormat:X2} is not supported; expected " +
                "bitmap RGBA/RGB/luminance-alpha/luminance/alpha or DXT1/DXT3/DXT5.")
        };
        int mipCount = (flags & Iwi8NoMipMaps) != 0
            ? 1
            : ComputeFullMipCount(width, height);
        MipLayout[] layouts = CreateMipLayouts(width, height, mipCount, encoding);
        ImageSourceMipLevel[] levels = new ImageSourceMipLevel[mipCount];
        uint currentFileSize = Iwi8FileHeaderSize;
        for (int mipLevel = mipCount - 1; mipLevel >= 0; mipLevel--)
        {
            MipLayout layout = layouts[mipLevel];
            currentFileSize = checked(
                currentFileSize + checked((uint)layout.EncodedByteCount));
            if (mipLevel < fileSizeForPicmip.Length &&
                fileSizeForPicmip[mipLevel] != currentFileSize)
            {
                throw new InvalidDataException(
                    $"IWI8 picmip file size {mipLevel} is " +
                    $"{fileSizeForPicmip[mipLevel]}, expected {currentFileSize}.");
            }

            byte[] encoded = reader.ReadBytes(
                layout.EncodedByteCount,
                $"mip {mipLevel} payload");
            levels[mipLevel] = new ImageSourceMipLevel(
                layout.Width,
                layout.Height,
                1,
                Decode(encoded, layout, encoding));
        }
        for (int index = mipCount; index < fileSizeForPicmip.Length; index++)
        {
            if (fileSizeForPicmip[index] != 0)
            {
                throw new InvalidDataException(
                    $"IWI8 unused picmip file size {index} is not zero.");
            }
        }

        reader.RequireEnd();
        bool usesSrgbReads = gamma == Iwi8GammaSrgb;
        return new ImageFileDocument(levels, usesSrgbReads);
    }

    private static ImageSourceMipLevel[] ReadMipLevelsTopDown(
        ImageStreamReader reader,
        IReadOnlyList<MipLayout> layouts,
        PixelEncoding encoding,
        bool forceOpaqueAlpha)
    {
        var levels = new ImageSourceMipLevel[layouts.Count];
        for (int mipLevel = 0; mipLevel < layouts.Count; mipLevel++)
        {
            MipLayout layout = layouts[mipLevel];
            byte[] encoded = reader.ReadBytes(
                layout.EncodedByteCount,
                $"mip {mipLevel} payload");
            byte[] rgba = Decode(encoded, layout, encoding);
            if (forceOpaqueAlpha)
                ForceOpaqueAlpha(rgba);
            levels[mipLevel] = new ImageSourceMipLevel(
                layout.Width,
                layout.Height,
                1,
                rgba);
        }
        return levels;
    }

    private static void ForceOpaqueAlpha(Span<byte> rgba)
    {
        for (int alpha = 3; alpha < rgba.Length; alpha += 4)
            rgba[alpha] = byte.MaxValue;
    }

    private static byte[] Decode(
        ReadOnlySpan<byte> encoded,
        MipLayout layout,
        PixelEncoding encoding)
    {
        switch (encoding)
        {
            case PixelEncoding.Rgba32:
                return encoded.ToArray();
            case PixelEncoding.Bgra32:
            {
                byte[] rgba = new byte[layout.DecodedByteCount];
                for (int offset = 0; offset < rgba.Length; offset += 4)
                {
                    rgba[offset] = encoded[offset + 2];
                    rgba[offset + 1] = encoded[offset + 1];
                    rgba[offset + 2] = encoded[offset];
                    rgba[offset + 3] = encoded[offset + 3];
                }
                return rgba;
            }
            case PixelEncoding.Bgr24:
            {
                byte[] rgba = new byte[layout.DecodedByteCount];
                for (int pixel = 0; pixel < layout.PixelCount; pixel++)
                {
                    int source = pixel * 3;
                    int destination = pixel * 4;
                    rgba[destination] = encoded[source + 2];
                    rgba[destination + 1] = encoded[source + 1];
                    rgba[destination + 2] = encoded[source];
                    rgba[destination + 3] = byte.MaxValue;
                }
                return rgba;
            }
            case PixelEncoding.LuminanceAlpha:
            {
                byte[] rgba = new byte[layout.DecodedByteCount];
                for (int pixel = 0; pixel < layout.PixelCount; pixel++)
                {
                    byte luminance = encoded[pixel * 2];
                    int destination = pixel * 4;
                    rgba[destination] = luminance;
                    rgba[destination + 1] = luminance;
                    rgba[destination + 2] = luminance;
                    rgba[destination + 3] = encoded[pixel * 2 + 1];
                }
                return rgba;
            }
            case PixelEncoding.Luminance:
            {
                byte[] rgba = new byte[layout.DecodedByteCount];
                for (int pixel = 0; pixel < layout.PixelCount; pixel++)
                {
                    byte luminance = encoded[pixel];
                    int destination = pixel * 4;
                    rgba[destination] = luminance;
                    rgba[destination + 1] = luminance;
                    rgba[destination + 2] = luminance;
                    rgba[destination + 3] = byte.MaxValue;
                }
                return rgba;
            }
            case PixelEncoding.Alpha:
            {
                byte[] rgba = new byte[layout.DecodedByteCount];
                for (int pixel = 0; pixel < layout.PixelCount; pixel++)
                {
                    int destination = pixel * 4;
                    rgba[destination] = byte.MaxValue;
                    rgba[destination + 1] = byte.MaxValue;
                    rgba[destination + 2] = byte.MaxValue;
                    rgba[destination + 3] = encoded[pixel];
                }
                return rgba;
            }
            case PixelEncoding.Bc1:
                return ImageBlockCompressionDecoder.DecodeBc1(
                    encoded,
                    layout.Width,
                    layout.Height);
            case PixelEncoding.Bc2:
                return ImageBlockCompressionDecoder.DecodeBc2(
                    encoded,
                    layout.Width,
                    layout.Height);
            case PixelEncoding.Bc3:
                return ImageBlockCompressionDecoder.DecodeBc3(
                    encoded,
                    layout.Width,
                    layout.Height);
            default:
                throw new ArgumentOutOfRangeException(nameof(encoding));
        }
    }

    private static MipLayout[] CreateMipLayouts(
        int width,
        int height,
        int mipCount,
        PixelEncoding encoding)
    {
        int maximumMipCount = ComputeFullMipCount(width, height);
        if (mipCount <= 0 || mipCount > maximumMipCount)
        {
            throw new InvalidDataException(
                $"The image declares {mipCount} mip levels; dimensions " +
                $"{width}x{height} permit 1 through {maximumMipCount}.");
        }

        var layouts = new MipLayout[mipCount];
        long totalDecodedBytes = 0;
        for (int mipLevel = 0; mipLevel < mipCount; mipLevel++)
        {
            int mipWidth = Math.Max(1, width >> mipLevel);
            int mipHeight = Math.Max(1, height >> mipLevel);
            int pixelCount;
            int decodedByteCount;
            int encodedByteCount;
            try
            {
                pixelCount = checked(mipWidth * mipHeight);
                decodedByteCount = checked(pixelCount * 4);
                encodedByteCount = encoding switch
                {
                    PixelEncoding.Rgba32 or PixelEncoding.Bgra32 =>
                        decodedByteCount,
                    PixelEncoding.Bgr24 => checked(pixelCount * 3),
                    PixelEncoding.LuminanceAlpha => checked(pixelCount * 2),
                    PixelEncoding.Luminance or PixelEncoding.Alpha => pixelCount,
                    PixelEncoding.Bc1 => checked(
                        Math.Max(1, (mipWidth + 3) / 4) *
                        Math.Max(1, (mipHeight + 3) / 4) * 8),
                    PixelEncoding.Bc2 or PixelEncoding.Bc3 => checked(
                        Math.Max(1, (mipWidth + 3) / 4) *
                        Math.Max(1, (mipHeight + 3) / 4) * 16),
                    _ => throw new ArgumentOutOfRangeException(nameof(encoding))
                };
                totalDecodedBytes = checked(
                    totalDecodedBytes + decodedByteCount);
            }
            catch (OverflowException exception)
            {
                throw new InvalidDataException(
                    $"Image mip {mipLevel} dimensions are too large.",
                    exception);
            }
            if (totalDecodedBytes > MaximumDecodedByteCount)
            {
                throw new InvalidDataException(
                    $"The decoded image exceeds the fixed " +
                    $"{MaximumDecodedByteCount / (1024 * 1024)} MiB limit.");
            }
            layouts[mipLevel] = new MipLayout(
                mipWidth,
                mipHeight,
                pixelCount,
                encodedByteCount,
                decodedByteCount);
        }
        return layouts;
    }

    private static int ValidateDdsMipCount(
        uint storedMipCount,
        uint headerFlags,
        uint caps,
        int width,
        int height)
    {
        int maximumMipCount = ComputeFullMipCount(width, height);
        if (storedMipCount > maximumMipCount)
        {
            throw new InvalidDataException(
                $"The DDS declares {storedMipCount} mip levels; dimensions " +
                $"{width}x{height} permit at most {maximumMipCount}.");
        }
        int mipCount = storedMipCount == 0
            ? 1
            : (int)storedMipCount;

        bool hasMipFlag = (headerFlags & DdsMipMapCountFlag) != 0;
        bool hasMipCaps = (caps & DdsMipMapCaps) != 0;
        bool hasComplexCaps = (caps & DdsComplexCaps) != 0;
        if (mipCount > 1 && (!hasMipFlag || !hasMipCaps || !hasComplexCaps))
        {
            throw new InvalidDataException(
                "The DDS declares multiple mips without the required header " +
                "and texture caps flags.");
        }
        if (mipCount == 1 && (hasMipFlag || hasMipCaps))
        {
            throw new InvalidDataException(
                "The DDS mip flags are inconsistent with its single mip level.");
        }
        return mipCount;
    }

    private static void ValidateDdsPitch(
        uint headerFlags,
        uint pitchOrLinearSize,
        MipLayout topLevel,
        PixelEncoding encoding)
    {
        bool blockCompressed = encoding is PixelEncoding.Bc1 or
            PixelEncoding.Bc2 or PixelEncoding.Bc3;
        if (blockCompressed)
        {
            if ((headerFlags & DdsPitchFlag) != 0)
            {
                throw new InvalidDataException(
                    "A block-compressed DDS cannot declare a row pitch.");
            }
            int blockByteCount = encoding == PixelEncoding.Bc1 ? 8 : 16;
            uint encodedRowByteCount = checked((uint)(
                Math.Max(1, (topLevel.Width + 3) / 4) * blockByteCount));
            if ((headerFlags & DdsLinearSizeFlag) == 0 ||
                (pitchOrLinearSize != topLevel.EncodedByteCount &&
                 pitchOrLinearSize != encodedRowByteCount))
            {
                throw new InvalidDataException(
                    $"DDS top-level compressed size is {pitchOrLinearSize}; " +
                    $"expected linear size {topLevel.EncodedByteCount} or the " +
                    $"OpenAssetTools row size {encodedRowByteCount}.");
            }
            return;
        }

        uint expectedPitch = checked((uint)topLevel.Width * 4);
        if ((headerFlags & DdsPitchFlag) == 0 ||
            pitchOrLinearSize != expectedPitch)
        {
            throw new InvalidDataException(
                $"DDS row pitch is {pitchOrLinearSize}, expected tightly " +
                $"packed RGBA/BGRA pitch {expectedPitch}.");
        }
        if ((headerFlags & DdsLinearSizeFlag) != 0)
        {
            throw new InvalidDataException(
                "An uncompressed DDS cannot declare a compressed linear size.");
        }
    }

    private static int ReadDimension(uint value, string fieldName)
    {
        if (value == 0 || value > MaximumDimension)
        {
            throw new InvalidDataException(
                $"{fieldName} {value} is outside the supported range " +
                $"1..{MaximumDimension}.");
        }
        return checked((int)value);
    }

    private static int ComputeFullMipCount(int width, int height)
    {
        int count = 1;
        while (width > 1 || height > 1)
        {
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
            count++;
        }
        return count;
    }

    private static uint MakeFourCc(char first, char second, char third, char fourth) =>
        (byte)first |
        ((uint)(byte)second << 8) |
        ((uint)(byte)third << 16) |
        ((uint)(byte)fourth << 24);

    private enum PixelEncoding
    {
        Rgba32,
        Bgra32,
        Bgr24,
        LuminanceAlpha,
        Luminance,
        Alpha,
        Bc1,
        Bc2,
        Bc3
    }

    private readonly record struct MipLayout(
        int Width,
        int Height,
        int PixelCount,
        int EncodedByteCount,
        int DecodedByteCount);

    private sealed class ImageStreamReader(Stream stream, string formatName)
    {
        private readonly Stream _stream = stream;
        private readonly string _formatName = formatName;

        internal byte ReadByte(string fieldName)
        {
            int value = _stream.ReadByte();
            if (value < 0)
                throw Truncated(fieldName);
            return (byte)value;
        }

        internal ushort ReadUInt16(string fieldName)
        {
            Span<byte> bytes = stackalloc byte[sizeof(ushort)];
            ReadExactly(bytes, fieldName);
            return BinaryPrimitives.ReadUInt16LittleEndian(bytes);
        }

        internal uint ReadUInt32(string fieldName)
        {
            Span<byte> bytes = stackalloc byte[sizeof(uint)];
            ReadExactly(bytes, fieldName);
            return BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        }

        internal byte[] ReadBytes(int byteCount, string fieldName)
        {
            var bytes = new byte[byteCount];
            ReadExactly(bytes, fieldName);
            return bytes;
        }

        internal void RequireEnd()
        {
            if (_stream.ReadByte() >= 0)
            {
                throw new InvalidDataException(
                    $"The {_formatName} contains trailing data after its " +
                    "declared mip payloads.");
            }
        }

        private void ReadExactly(Span<byte> destination, string fieldName)
        {
            try
            {
                _stream.ReadExactly(destination);
            }
            catch (EndOfStreamException exception)
            {
                throw Truncated(fieldName, exception);
            }
        }

        private InvalidDataException Truncated(
            string fieldName,
            Exception? innerException = null) =>
            new(
                $"The {_formatName} is truncated while reading {fieldName}.",
                innerException);
    }
}
