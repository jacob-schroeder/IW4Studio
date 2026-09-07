using System.Buffers.Binary;
using IW4.Assets.Assets.Image;

namespace MapConverter.Game.IW3.PC.Images;

internal sealed record Iw3Iwi6StreamedImageCompilation(
    GfxImageAsset Image,
    IReadOnlyList<ReadOnlyMemory<byte>> StreamPartPayloads);

/// <summary>
/// Converts one two-dimensional IW3 IWI6 block-compressed image into an owned
/// streamed PS3 IW4 GfxImage and its four ordered imagefile payload parts.
/// IWI stores mips smallest-first; PS3 stream payloads retain the same BC block
/// contents in top-level-first order.
/// </summary>
internal static class Iw3Iwi6StreamedImageCompiler
{
    private const int HeaderSize = 0x1c;
    private const byte Version = 6;
    private const byte NoMipMaps = 0x02;
    private const uint TextureControl1 = 0x0001aae4;

    private const byte IwiDxt1 = 0x0b;
    private const byte IwiDxt3 = 0x0c;
    private const byte IwiDxt5 = 0x0d;

    internal static Iw3Iwi6StreamedImageCompilation Compile(
        string imageName,
        ReadOnlySpan<byte> iwiBytes,
        TextureSemantic semantic,
        bool useSrgbReads)
    {
        ValidateImageName(imageName);
        if (!Enum.IsDefined(semantic) || semantic == TextureSemantic.WaterMap)
        {
            throw new ArgumentOutOfRangeException(
                nameof(semantic),
                semantic,
                "A streamed two-dimensional material-image semantic is required.");
        }
        if (iwiBytes.Length < HeaderSize)
            throw new InvalidDataException("IWI6 data is shorter than its 0x1C-byte header.");
        if (iwiBytes[0] != 'I' || iwiBytes[1] != 'W' || iwiBytes[2] != 'i')
            throw new InvalidDataException("The IWI magic is invalid.");
        if (iwiBytes[3] != Version)
        {
            throw new NotSupportedException(
                $"IWI version {iwiBytes[3]} is not supported; IW3 IWI6 is required.");
        }

        GfxImageBaseFormat baseFormat = iwiBytes[4] switch
        {
            IwiDxt1 => GfxImageBaseFormat.CompressedDxt1,
            IwiDxt3 => GfxImageBaseFormat.CompressedDxt23,
            IwiDxt5 => GfxImageBaseFormat.CompressedDxt45,
            _ => throw new NotSupportedException(
                $"IWI6 format 0x{iwiBytes[4]:X2} is not supported; " +
                "DXT1, DXT3, or DXT5 is required.")
        };
        byte flags = iwiBytes[5];
        if ((flags & ~NoMipMaps) != 0)
        {
            throw new NotSupportedException(
                $"IWI6 flags 0x{flags:X2} request an unsupported image shape or layout.");
        }

        ushort width = ReadDimension(iwiBytes, 0x06, "width");
        ushort height = ReadDimension(iwiBytes, 0x08, "height");
        ushort depth = ReadDimension(iwiBytes, 0x0a, "depth");
        if (depth != 1)
        {
            throw new NotSupportedException(
                $"IWI6 depth {depth} is not supported; a two-dimensional image requires depth 1.");
        }

        int mipCount = (flags & NoMipMaps) != 0
            ? 1
            : ComputeFullMipCount(width, height);
        byte format = (byte)baseFormat;
        int[] mipByteCounts = ComputeMipByteCounts(
            format,
            mipCount,
            width,
            height);
        int sourcePayloadByteCount = mipByteCounts.Aggregate(
            0,
            (total, byteCount) => checked(total + byteCount));

        int sourceFileByteCount = checked(HeaderSize + sourcePayloadByteCount);
        if (iwiBytes.Length != sourceFileByteCount)
        {
            throw new InvalidDataException(
                $"IWI6 contains {iwiBytes.Length:N0} byte(s); its " +
                $"{width}x{height} format and mip profile require " +
                $"{sourceFileByteCount:N0} byte(s).");
        }
        ValidatePicmipFileSizes(
            iwiBytes,
            mipByteCounts,
            sourceFileByteCount);

        int alignedPayloadByteCount = GfxImagePixelLayout.ComputePayloadByteCount(
            new GfxImageFormat(format),
            checked((byte)mipCount),
            isCubemap: false,
            new GfxImageTextureRemap(TextureControl1),
            width,
            height,
            depth);
        if (alignedPayloadByteCount < sourcePayloadByteCount ||
            alignedPayloadByteCount - sourcePayloadByteCount >= 0x80)
        {
            throw new InvalidDataException(
                "The proven PS3 image payload alignment is inconsistent.");
        }

        var payload = new byte[alignedPayloadByteCount];
        int[] destinationOffsets = ComputeDestinationOffsets(mipByteCounts);
        int sourceOffset = HeaderSize;
        for (int mipLevel = mipByteCounts.Length - 1;
             mipLevel >= 0;
             mipLevel--)
        {
            int byteCount = mipByteCounts[mipLevel];
            iwiBytes.Slice(sourceOffset, byteCount).CopyTo(
                payload.AsSpan(destinationOffsets[mipLevel], byteCount));
            sourceOffset = checked(sourceOffset + byteCount);
        }
        if (sourceOffset != iwiBytes.Length)
            throw new InvalidDataException("IWI6 mip traversal did not consume the source payload.");

        return CompileTopLevelFirstPs3Payload(
            imageName,
            format,
            checked((byte)mipCount),
            width,
            height,
            payload,
            semantic,
            useSrgbReads);
    }

    /// <summary>
    /// Authors the IW4 streamed-image wrapper around an already-native PS3
    /// DXT payload. The payload remains in top-level-first mip order.
    /// </summary>
    internal static Iw3Iwi6StreamedImageCompilation
        CompileTopLevelFirstPs3Payload(
            string imageName,
            byte format,
            byte mipCount,
            ushort width,
            ushort height,
            ReadOnlyMemory<byte> payload,
            TextureSemantic semantic,
            bool useSrgbReads)
    {
        ValidateImageName(imageName);
        if (!Enum.IsDefined(semantic) || semantic == TextureSemantic.WaterMap)
        {
            throw new ArgumentOutOfRangeException(
                nameof(semantic),
                semantic,
                "A streamed two-dimensional material-image semantic is required.");
        }
        if (width == 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height == 0)
            throw new ArgumentOutOfRangeException(nameof(height));
        if (mipCount == 0 || mipCount > ComputeFullMipCount(width, height))
        {
            throw new InvalidDataException(
                $"PS3 image mip count {mipCount} is invalid for {width}x{height}.");
        }
        if (format is not
            (byte)GfxImageBaseFormat.CompressedDxt1 and not
            (byte)GfxImageBaseFormat.CompressedDxt23 and not
            (byte)GfxImageBaseFormat.CompressedDxt45)
        {
            throw new NotSupportedException(
                $"PS3 GfxImage format 0x{format:X2} is not supported; " +
                "DXT1, DXT3, or DXT5 is required.");
        }

        int[] mipByteCounts = ComputeMipByteCounts(
            format,
            mipCount,
            width,
            height);
        int expectedPayloadByteCount = GfxImagePixelLayout.ComputePayloadByteCount(
            new GfxImageFormat(format),
            mipCount,
            isCubemap: false,
            new GfxImageTextureRemap(TextureControl1),
            width,
            height,
            depth: 1);
        if (payload.Length != expectedPayloadByteCount)
        {
            throw new InvalidDataException(
                $"PS3 GfxImage payload contains {payload.Length:N0} byte(s); " +
                $"its {width}x{height} format and mip profile require " +
                $"{expectedPayloadByteCount:N0} byte(s).");
        }

        int[] mipOffsets = ComputeDestinationOffsets(mipByteCounts);
        CreateStreamParts(
            width,
            height,
            mipByteCounts,
            mipOffsets,
            payload,
            out GfxImageStreamData[] streamData,
            out ReadOnlyMemory<byte>[] streamPartPayloads);
        var image = new GfxImageAsset
        {
            Format = format,
            LevelCount = 1,
            DimensionCount = GfxImageDimension.TwoDimensional,
            TextureControl1 = TextureControl1,
            Width = 1,
            Height = 1,
            Depth = 1,
            MemoryLocation = GfxImageMemoryLocation.Local,
            MapType = MapType.TwoDimensional,
            TextureSemantic = semantic,
            Category = ImageCategory.LoadFromFile,
            UseSrgbReads = useSrgbReads ? (byte)1 : (byte)0,
            CardMemory = 0,
            BaseWidth = 1,
            BaseHeight = 1,
            BaseDepth = 1,
            BaseLevelCount = 1,
            Cached = GfxImageCached.Auto,
            StreamData = streamData,
            PayloadByteCount = 0,
            PayloadBytes = [],
            Name = imageName
        };
        return new Iw3Iwi6StreamedImageCompilation(
            image,
            Array.AsReadOnly(streamPartPayloads));
    }

    private static void CreateStreamParts(
        int width,
        int height,
        IReadOnlyList<int> mipByteCounts,
        IReadOnlyList<int> mipOffsets,
        ReadOnlyMemory<byte> payload,
        out GfxImageStreamData[] streamData,
        out ReadOnlyMemory<byte>[] streamPartPayloads)
    {
        int higherPartCount = 0;
        int maximumHigherPartCount = GfxImageStreamData.EntryCount - 1;
        while (higherPartCount < maximumHigherPartCount &&
               higherPartCount < mipByteCounts.Count - 1 &&
               mipByteCounts[higherPartCount] % 0x80 == 0)
        {
            higherPartCount++;
        }
        int activePartCount = higherPartCount + 1;
        int tailFirstMip = higherPartCount;
        streamData = new GfxImageStreamData[GfxImageStreamData.EntryCount];
        streamPartPayloads = new ReadOnlyMemory<byte>[GfxImageStreamData.EntryCount];
        int cumulativeByteCount = 0;
        for (int partIndex = 0; partIndex < activePartCount; partIndex++)
        {
            int mipIndex = tailFirstMip - partIndex;
            int byteCount = partIndex == 0
                ? checked(payload.Length - mipOffsets[mipIndex])
                : mipByteCounts[mipIndex];
            if (byteCount <= 0 || byteCount % 0x80 != 0)
            {
                throw new InvalidDataException(
                    "Every PS3 GfxImage stream part must have a positive " +
                    "0x80-aligned byte count.");
            }
            ReadOnlyMemory<byte> partPayload = payload.Slice(
                mipOffsets[mipIndex],
                byteCount);
            cumulativeByteCount = checked(cumulativeByteCount + byteCount);
            if (cumulativeByteCount > 0x03ffffff)
            {
                throw new NotSupportedException(
                    "A streamed GfxImage cumulative payload exceeds the PS3 26-bit field.");
            }

            int levelMarker = checked(mipByteCounts.Count - mipIndex);
            if (levelMarker > 0x3f)
            {
                throw new NotSupportedException(
                    "A streamed GfxImage mip profile exceeds the PS3 six-bit level marker.");
            }
            int partWidth = Math.Max(1, width >> mipIndex);
            int partHeight = Math.Max(1, height >> mipIndex);
            streamData[partIndex] = new GfxImageStreamData(
                checked((ushort)partWidth),
                checked((ushort)partHeight),
                checked(((uint)levelMarker << 26) | (uint)cumulativeByteCount));
            streamPartPayloads[partIndex] = partPayload;
        }

        for (int partIndex = activePartCount;
             partIndex < GfxImageStreamData.EntryCount;
             partIndex++)
        {
            streamData[partIndex] = new GfxImageStreamData(0, 0, 0);
            streamPartPayloads[partIndex] = ReadOnlyMemory<byte>.Empty;
        }
    }

    private static void ValidateImageName(string imageName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageName);
        if (imageName[0] == ',' || imageName.Contains('\0') ||
            !string.Equals(imageName, imageName.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A streamed GfxImage requires an owned wire name without NUL or surrounding whitespace.",
                nameof(imageName));
        }
        if (imageName.Any(character => character > byte.MaxValue))
        {
            throw new ArgumentException(
                "A streamed GfxImage name must be representable as Latin-1.",
                nameof(imageName));
        }
    }

    private static ushort ReadDimension(
        ReadOnlySpan<byte> source,
        int offset,
        string fieldName)
    {
        ushort value = BinaryPrimitives.ReadUInt16LittleEndian(
            source.Slice(offset, sizeof(ushort)));
        return value != 0
            ? value
            : throw new InvalidDataException($"IWI6 {fieldName} cannot be zero.");
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

    private static int[] ComputeDestinationOffsets(
        IReadOnlyList<int> mipByteCounts)
    {
        var offsets = new int[mipByteCounts.Count];
        for (int index = 1; index < offsets.Length; index++)
        {
            offsets[index] = checked(
                offsets[index - 1] + mipByteCounts[index - 1]);
        }
        return offsets;
    }

    private static int[] ComputeMipByteCounts(
        byte format,
        int mipCount,
        int width,
        int height)
    {
        var formatEncoding = new GfxImageFormat(format);
        uint formatKey = GfxImagePixelLayout.BuildFormatKey(
            formatEncoding,
            new GfxImageTextureRemap(TextureControl1));
        var mipByteCounts = new int[mipCount];
        for (int mipLevel = 0; mipLevel < mipByteCounts.Length; mipLevel++)
        {
            mipByteCounts[mipLevel] = GfxImagePixelLayout.ComputeMipByteCount(
                formatKey,
                Math.Max(1, width >> mipLevel),
                Math.Max(1, height >> mipLevel),
                depth: 1);
            if (mipByteCounts[mipLevel] <= 0)
            {
                throw new NotSupportedException(
                    $"PS3 GfxImage format 0x{format:X2} has no proven payload layout.");
            }
        }
        return mipByteCounts;
    }

    private static void ValidatePicmipFileSizes(
        ReadOnlySpan<byte> source,
        IReadOnlyList<int> mipByteCounts,
        int sourceFileByteCount)
    {
        uint serializedFileSize = BinaryPrimitives.ReadUInt32LittleEndian(
            source.Slice(0x0c, sizeof(uint)));
        if (serializedFileSize != sourceFileByteCount)
        {
            throw new InvalidDataException(
                $"IWI6 picmip file size 0 is {serializedFileSize:N0}; " +
                $"expected {sourceFileByteCount:N0}.");
        }

        for (int picmip = 1; picmip < 4; picmip++)
        {
            int firstRetainedMip = Math.Min(
                picmip,
                mipByteCounts.Count - 1);
            int expected = HeaderSize;
            for (int mipLevel = firstRetainedMip;
                 mipLevel < mipByteCounts.Count;
                 mipLevel++)
            {
                expected = checked(expected + mipByteCounts[mipLevel]);
            }

            uint serialized = BinaryPrimitives.ReadUInt32LittleEndian(
                source.Slice(0x0c + picmip * sizeof(uint), sizeof(uint)));
            if (serialized != expected)
            {
                throw new InvalidDataException(
                    $"IWI6 picmip file size {picmip} is {serialized:N0}; " +
                    $"expected {expected:N0}.");
            }
        }
    }
}
