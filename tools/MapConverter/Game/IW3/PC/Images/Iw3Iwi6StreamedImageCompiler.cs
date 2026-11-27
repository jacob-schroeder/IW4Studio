using MapConverter.Game.IW3.PC.Assets;
using IW4.Game.Assets.Image;

namespace MapConverter.Game.IW3.PC.Images;

internal sealed record Iw3Iwi6StreamedImageCompilation(
    GfxImageAsset Image,
    IReadOnlyList<ReadOnlyMemory<byte>> StreamPartPayloads)
{
    internal GfxImageAsset ToResidentImage()
    {
        if (Image.MapType != MapType.TwoDimensional || Image.IsCubemap ||
            Image.TextureSemantic != TextureSemantic.TwoDimensional)
            throw new InvalidDataException($"Image '{Image.Name}' is not a two-dimensional preview.");
        if (Image.PayloadByteCount != 0)
            return Image;

        int[] partByteCounts = GfxImageStreamData.ValidateProfileAndComputePartByteCounts(Image.StreamData);
        int activePartCount = Array.FindLastIndex(partByteCounts, count => count != 0) + 1;
        if (activePartCount == 0 || StreamPartPayloads.Count != GfxImageStreamData.EntryCount)
            throw new InvalidDataException($"Image '{Image.Name}' has no complete stream payload.");
        var payload = new byte[partByteCounts.Sum()];
        int offset = 0;
        for (int part = activePartCount - 1; part >= 0; part--)
        {
            if (StreamPartPayloads[part].Length != partByteCounts[part])
                throw new InvalidDataException($"Image '{Image.Name}' has an incomplete stream part {part}.");
            StreamPartPayloads[part].Span.CopyTo(payload.AsSpan(offset, partByteCounts[part]));
            offset += partByteCounts[part];
        }
        GfxImageStreamData topPart = Image.StreamData[activePartCount - 1];
        return Iw3Iwi6StreamedImageCompiler.CreateResidentImage(
            Image.Name ?? throw new InvalidDataException("Compiled image has no name."),
            Image.Format, checked((byte)topPart.LevelMarker), topPart.Width, topPart.Height,
            false, payload, Image.TextureSemantic, Image.UseSrgbReads != 0);
    }
}

/// <summary>
/// Converts one IW3 IWI6 image into an owned PS3 IW4 GfxImage.
/// Ordinary 2D images use four ordered imagefile parts; cubemaps and no-picmip
/// images retain their full payload in the fastfile.
/// IWI stores mips smallest-first; PS3 payloads use top-level-first order,
/// retaining BC blocks or converting bitmap pixels to native swizzled ARGB storage.
/// </summary>
internal static class Iw3Iwi6StreamedImageCompiler
{
    private const uint TextureControl1 = 0x0001aae4;

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
                "A material-image semantic is required.");
        }
        Iwi6Header header = Iwi6Header.Read(iwiBytes, imageName);
        GfxImageBaseFormat baseFormat = header.Format switch
        {
            Iwi6Format.Rgba or Iwi6Format.Rgb or Iwi6Format.Luminance => GfxImageBaseFormat.A8R8G8B8,
            Iwi6Format.Dxt1 => GfxImageBaseFormat.CompressedDxt1,
            Iwi6Format.Dxt3 => GfxImageBaseFormat.CompressedDxt23,
            Iwi6Format.Dxt5 => GfxImageBaseFormat.CompressedDxt45,
            _ => throw new NotSupportedException(
                $"IWI6 format 0x{(byte)header.Format:X2} is not supported; " +
                "RGB, RGBA, luminance, DXT1, DXT3, or DXT5 is required.")
        };
        Iwi6Flags flags = header.Flags;
        // Clamp flags do not change pixel layout. Per-use addressing is
        // retained by the source material's sampler state.
        if ((flags & ~(Iwi6Flags.NoPicMip | Iwi6Flags.NoMipMaps | Iwi6Flags.CubeMap | Iwi6Flags.LegacyNormals | Iwi6Flags.ClampU | Iwi6Flags.ClampV)) != 0)
        {
            throw new NotSupportedException(
                $"IWI6 flags 0x{(byte)flags:X2} request an unsupported image shape or layout.");
        }
        if ((flags & Iwi6Flags.LegacyNormals) != 0 &&
            ((flags & Iwi6Flags.CubeMap) != 0 || baseFormat != GfxImageBaseFormat.CompressedDxt45 ||
             semantic != TextureSemantic.NormalMap || useSrgbReads))
        {
            throw new NotSupportedException(
                "Legacy IWI6 normals require linear two-dimensional DXT5 data and the NormalMap semantic.");
        }
        // Legacy DXT5 normal channels are authored in alpha/green and decoded
        // by the material shader. Retain the compressed blocks unchanged.

        ushort width = header.Width;
        ushort height = header.Height;
        ushort depth = header.Depth;
        bool isCubemap = (flags & Iwi6Flags.CubeMap) != 0;
        if (depth != 1)
        {
            throw new NotSupportedException(
                $"IWI6 depth {depth} is not supported; a two-dimensional image requires depth 1.");
        }
        if (isCubemap && width != height)
            throw new InvalidDataException("An IWI6 cubemap requires square faces.");
        if (isCubemap && !System.Numerics.BitOperations.IsPow2((uint)width))
            throw new InvalidDataException("An IWI6 cubemap requires power-of-two face dimensions.");

        int mipCount = (flags & Iwi6Flags.NoMipMaps) != 0
            ? 1
            : ComputeFullMipCount(width, height);
        byte format = (byte)baseFormat;
        int[] mipByteCounts = ComputeMipByteCounts(
            format,
            mipCount,
            width,
            height);
        // Bitmap formats expand into the existing four-byte PS3 ARGB format.
        // IWI file extents still describe the original source pixels.
        int[] sourceMipByteCounts = header.Format switch
        {
            Iwi6Format.Rgb => mipByteCounts.Select(byteCount => checked(byteCount / 4 * 3)).ToArray(),
            Iwi6Format.Luminance => mipByteCounts.Select(byteCount => byteCount / 4).ToArray(),
            _ => mipByteCounts
        };
        int faceCount = isCubemap ? 6 : 1;
        int sourceFaceByteCount = sourceMipByteCounts.Aggregate(
            0,
            (total, byteCount) => checked(total + byteCount));
        int sourcePayloadByteCount = checked(sourceFaceByteCount * faceCount);

        int sourceFileByteCount = checked(Iwi6Header.Size + sourcePayloadByteCount);
        if (iwiBytes.Length != sourceFileByteCount)
        {
            throw new InvalidDataException(
                $"IWI6 contains {iwiBytes.Length:N0} byte(s); its " +
                $"{width}x{height} format and mip profile require " +
                $"{sourceFileByteCount:N0} byte(s).");
        }
        ValidatePicmipFileSizes(
            header,
            sourceMipByteCounts,
            faceCount,
            sourceFileByteCount);

        int alignedPayloadByteCount = GfxImagePixelLayout.ComputePayloadByteCount(
            new GfxImageFormat(format),
            checked((byte)mipCount),
            isCubemap,
            new GfxImageTextureRemap(TextureControl1),
            width,
            height,
            depth);
        int faceStride = alignedPayloadByteCount / faceCount;
        int targetFaceByteCount = mipByteCounts.Aggregate(
            0,
            (total, byteCount) => checked(total + byteCount));
        if (alignedPayloadByteCount % faceCount != 0 ||
            faceStride < targetFaceByteCount ||
            faceStride - targetFaceByteCount >= 0x80)
        {
            throw new InvalidDataException(
                "The proven PS3 image payload alignment is inconsistent.");
        }

        var payload = new byte[alignedPayloadByteCount];
        int[] destinationOffsets = ComputeDestinationOffsets(mipByteCounts);
        int sourceOffset = Iwi6Header.Size;
        for (int mipLevel = mipByteCounts.Length - 1;
             mipLevel >= 0;
             mipLevel--)
        {
            int byteCount = mipByteCounts[mipLevel];
            int sourceByteCount = sourceMipByteCounts[mipLevel];
            // Source: mip-major, six consecutive faces at each mip. Native:
            // face-major, each complete top-first mip chain aligned to 0x80.
            for (int face = 0; face < faceCount; face++)
            {
                ReadOnlySpan<byte> sourceMip = iwiBytes.Slice(sourceOffset, sourceByteCount);
                Span<byte> destinationMip = payload.AsSpan(
                    checked(face * faceStride + destinationOffsets[mipLevel]), byteCount);
                if (baseFormat == GfxImageBaseFormat.A8R8G8B8)
                {
                    byte[] linear;
                    if (header.Format is Iwi6Format.Rgb or Iwi6Format.Luminance)
                    {
                        linear = new byte[byteCount];
                        int sourcePixelSize = header.Format == Iwi6Format.Rgb ? 3 : 1;
                        for (int sourcePixel = 0, targetPixel = 0;
                             sourcePixel < sourceMip.Length;
                             sourcePixel += sourcePixelSize, targetPixel += 4)
                        {
                            if (header.Format == Iwi6Format.Rgb)
                                sourceMip.Slice(sourcePixel, 3).CopyTo(linear.AsSpan(targetPixel, 3));
                            else
                                linear.AsSpan(targetPixel, 3).Fill(sourceMip[sourcePixel]);
                            linear[targetPixel + 3] = byte.MaxValue;
                        }
                    }
                    else
                    {
                        linear = sourceMip.ToArray();
                    }
                    GfxImagePixelLayout.ReverseFourBytePixelOrder(linear);
                    GfxImagePixelLayout.SwizzleMorton2D(
                        linear,
                        Math.Max(1, width >> mipLevel),
                        Math.Max(1, height >> mipLevel),
                        bytesPerPixel: 4).CopyTo(destinationMip);
                }
                else
                {
                    sourceMip.CopyTo(destinationMip);
                }
                sourceOffset = checked(sourceOffset + sourceByteCount);
            }
        }
        if (sourceOffset != iwiBytes.Length)
            throw new InvalidDataException("IWI6 mip traversal did not consume the source payload.");

        if (isCubemap || (flags & Iwi6Flags.NoPicMip) != 0)
        {
            // The PS3 wire image has no noPicmip field. Full resident storage
            // preserves the source resolution without stream quality reduction.
            GfxImageAsset image = CreateResidentImage(imageName, format, checked((byte)mipCount),
                width, height, isCubemap, payload, semantic, useSrgbReads);
            return new Iw3Iwi6StreamedImageCompilation(
                image,
                Array.AsReadOnly(new ReadOnlyMemory<byte>[GfxImageStreamData.EntryCount]));
        }

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
    /// DXT or swizzled ARGB payload. The payload remains in top-level-first mip order.
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
            (byte)GfxImageBaseFormat.A8R8G8B8 and not
            (byte)GfxImageBaseFormat.CompressedDxt1 and not
            (byte)GfxImageBaseFormat.CompressedDxt23 and not
            (byte)GfxImageBaseFormat.CompressedDxt45)
        {
            throw new NotSupportedException(
                $"PS3 GfxImage format 0x{format:X2} is not supported; " +
                "ARGB, DXT1, DXT3, or DXT5 is required.");
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

    internal static GfxImageAsset CreateResidentImage(
        string imageName, byte format, byte mipCount, ushort width, ushort height,
        bool isCubemap, byte[] payload, TextureSemantic semantic,
        bool useSrgbReads)
    {
        return new GfxImageAsset
        {
            Format = format,
            LevelCount = mipCount,
            DimensionCount = GfxImageDimension.TwoDimensional,
            MultiFaceControl = isCubemap ? (byte)1 : (byte)0,
            TextureControl1 = TextureControl1,
            Width = width,
            Height = height,
            Depth = 1,
            MemoryLocation = GfxImageMemoryLocation.Local,
            MapType = isCubemap ? MapType.Cube : MapType.TwoDimensional,
            TextureSemantic = semantic,
            Category = ImageCategory.LoadFromFile,
            UseSrgbReads = useSrgbReads ? (byte)1 : (byte)0,
            CardMemory = checked((uint)payload.Length),
            BaseWidth = width,
            BaseHeight = height,
            BaseDepth = 1,
            BaseLevelCount = mipCount,
            Cached = GfxImageCached.No,
            StreamData = Enumerable.Repeat(new GfxImageStreamData(0, 0, 0), GfxImageStreamData.EntryCount).ToArray(),
            PayloadByteCount = payload.Length,
            PayloadBytes = payload,
            Name = imageName
        };
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
        Iwi6Header header,
        IReadOnlyList<int> mipByteCounts,
        int faceCount,
        int sourceFileByteCount)
    {
        uint serializedFileSize = header.FileSizeForPicmip[0];
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
            int expected = Iwi6Header.Size;
            for (int mipLevel = firstRetainedMip;
                 mipLevel < mipByteCounts.Count;
                 mipLevel++)
            {
                expected = checked(expected + mipByteCounts[mipLevel] * faceCount);
            }

            uint serialized = header.FileSizeForPicmip[picmip];
            if (picmip >= mipByteCounts.Count && serialized == 0)
                continue;
            if (serialized != expected)
            {
                throw new InvalidDataException(
                    $"IWI6 picmip file size {picmip} is {serialized:N0}; " +
                    $"expected {expected:N0}.");
            }
        }
    }
}
