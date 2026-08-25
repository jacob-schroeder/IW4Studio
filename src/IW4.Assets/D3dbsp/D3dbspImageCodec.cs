using System.Buffers.Binary;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.Image;

namespace IW4.Assets.D3dbsp;

internal readonly record struct D3dbspLightmapTile(
    int RuntimeLightmapIndex,
    int TileX,
    int TileY,
    int TilesWide,
    int TilesHigh,
    byte D3dbspLightmapIndex)
{
    // Native import merges source coordinates as
    // runtime = (source + tile) / atlasTileCount. Export applies this inverse.
    public float ToD3dbspU(float runtimeU) =>
        runtimeU * TilesWide - TileX;

    public float ToD3dbspV(float runtimeV) =>
        runtimeV * TilesHigh - TileY;
}

internal static class D3dbspImageCodec
{
    private const int LightmapPrimaryWidth = 1024;
    private const int LightmapPrimaryHeight = 1024;
    private const int LightmapSecondaryWidth = 512;
    private const int LightmapSecondaryPlaneHeight = 512;
    private const int LightmapSecondaryHeight = 1024;
    private const int LightmapPrimaryByteCount = 1024 * 1024;
    private const int LightmapSecondaryPlaneByteCount = 512 * 512 * 4;
    private const int LightmapSecondaryByteCount = 2 * LightmapSecondaryPlaneByteCount;
    private const int LightmapByteCount = LightmapPrimaryByteCount + LightmapSecondaryByteCount;

    private const int ReflectionProbeEdgeLength = 64;
    private const int ReflectionProbeMipCount = 7;
    private const int ReflectionProbeFaceCount = 6;
    private const int ReflectionProbeTopMipByteCount = 64 * 64 * 4;
    private const int ReflectionProbeFacePixelByteCount = 21_844;
    private const int ReflectionProbeFaceStride = 21_888;
    private const int ReflectionProbeRuntimeByteCount = ReflectionProbeFaceCount * ReflectionProbeFaceStride;
    private const int ReflectionProbeDiskPixelByteCount = 131_064;
    private const int ReflectionProbeDiskRowByteCount = 131_140;
    private const int ReflectionProbeColorCorrectionNameByteCount = 64;

    private const uint PrimaryLightmapFormatKey = 0x01A9FF81;
    private const uint ColorImageFormatKey = 0x01AAE485;

    public static (byte[] Data, IReadOnlyList<D3dbspLightmapTile> Tiles)
        EncodeLightBytes(IReadOnlyList<GfxLightmapArray> lightmaps)
    {
        ArgumentNullException.ThrowIfNull(lightmaps);

        var layouts = new LightmapLayout[lightmaps.Count];
        int tileCount = 0;
        for (int index = 0; index < lightmaps.Count; index++)
        {
            GfxLightmapArray lightmap = lightmaps[index] ??
                throw new InvalidDataException($"GfxWorld lightmap row {index} is null.");
            GfxImageAsset primary = lightmap.Primary ??
                throw new InvalidDataException($"GfxWorld lightmap row {index} has no primary image.");
            GfxImageAsset secondary = lightmap.Secondary ??
                throw new InvalidDataException($"GfxWorld lightmap row {index} has no secondary image.");

            ValidateTwoDimensionalImage(
                primary,
                PrimaryLightmapFormatKey,
                bytesPerPixel: 1,
                $"GfxWorld lightmap row {index} primary image");
            ValidateTwoDimensionalImage(
                secondary,
                ColorImageFormatKey,
                bytesPerPixel: 4,
                $"GfxWorld lightmap row {index} secondary image");

            if (primary.Width % LightmapPrimaryWidth != 0 ||
                primary.Height % LightmapPrimaryHeight != 0)
            {
                throw new NotSupportedException(
                    $"GfxWorld lightmap row {index} primary image dimensions " +
                    $"{primary.Width}x{primary.Height} are not whole 1024x1024 d3dbsp tiles.");
            }

            int tilesWide = primary.Width / LightmapPrimaryWidth;
            int tilesHigh = primary.Height / LightmapPrimaryHeight;
            if (tilesWide == 0 || tilesHigh == 0 ||
                secondary.Width != checked(tilesWide * LightmapSecondaryWidth) ||
                secondary.Height != checked(tilesHigh * LightmapSecondaryHeight))
            {
                throw new InvalidDataException(
                    $"GfxWorld lightmap row {index} primary and secondary atlas dimensions do not share the native tile layout.");
            }

            tileCount = checked(tileCount + tilesWide * tilesHigh);
            layouts[index] = new LightmapLayout(
                primary,
                secondary,
                tilesWide,
                tilesHigh);
        }

        // Surface lightmap index 31 is the no-lightmap sentinel.
        if (tileCount > 31)
        {
            throw new NotSupportedException(
                $"The runtime lightmap atlases contain {tileCount} d3dbsp tiles; v22 supports at most 31.");
        }

        var data = new byte[checked(tileCount * LightmapByteCount)];
        var tiles = new D3dbspLightmapTile[tileCount];
        int outputIndex = 0;
        for (int lightmapIndex = 0; lightmapIndex < layouts.Length; lightmapIndex++)
        {
            LightmapLayout layout = layouts[lightmapIndex];
            byte[] primary = ToLinearPixels(
                layout.Primary,
                layout.Primary.Width,
                layout.Primary.Height,
                bytesPerPixel: 1);
            byte[] secondary = ToLinearPixels(
                layout.Secondary,
                layout.Secondary.Width,
                layout.Secondary.Height,
                bytesPerPixel: 4);
            ReverseFourBytePixelOrder(secondary);

            for (int tileY = 0; tileY < layout.TilesHigh; tileY++)
            {
                for (int tileX = 0; tileX < layout.TilesWide; tileX++)
                {
                    Span<byte> destination = data.AsSpan(
                        checked(outputIndex * LightmapByteCount),
                        LightmapByteCount);
                    CopyRectangle(
                        secondary,
                        layout.Secondary.Width,
                        sourceX: checked(tileX * LightmapSecondaryWidth),
                        sourceY: checked(tileY * LightmapSecondaryPlaneHeight),
                        LightmapSecondaryWidth,
                        LightmapSecondaryPlaneHeight,
                        bytesPerPixel: 4,
                        destination[..LightmapSecondaryPlaneByteCount]);
                    CopyRectangle(
                        secondary,
                        layout.Secondary.Width,
                        sourceX: checked(tileX * LightmapSecondaryWidth),
                        sourceY: checked((layout.TilesHigh + tileY) * LightmapSecondaryPlaneHeight),
                        LightmapSecondaryWidth,
                        LightmapSecondaryPlaneHeight,
                        bytesPerPixel: 4,
                        destination.Slice(
                            LightmapSecondaryPlaneByteCount,
                            LightmapSecondaryPlaneByteCount));
                    CopyRectangle(
                        primary,
                        layout.Primary.Width,
                        sourceX: checked(tileX * LightmapPrimaryWidth),
                        sourceY: checked(tileY * LightmapPrimaryHeight),
                        LightmapPrimaryWidth,
                        LightmapPrimaryHeight,
                        bytesPerPixel: 1,
                        destination[LightmapSecondaryByteCount..]);

                    tiles[outputIndex] = new D3dbspLightmapTile(
                        lightmapIndex,
                        tileX,
                        tileY,
                        layout.TilesWide,
                        layout.TilesHigh,
                        checked((byte)outputIndex));
                    outputIndex++;
                }
            }
        }

        return (data, Array.AsReadOnly(tiles));
    }

    public static IReadOnlyList<GfxLightmapArray> DecodeLightBytes(
        ReadOnlySpan<byte> data)
    {
        if (data.Length % LightmapByteCount != 0)
        {
            throw new InvalidDataException(
                $"The LightBytes lump length {data.Length} is not divisible by {LightmapByteCount}.");
        }

        int count = data.Length / LightmapByteCount;
        if (count > 31)
        {
            throw new InvalidDataException(
                $"The LightBytes lump contains {count} lightmaps; v22 supports at most 31.");
        }

        var lightmaps = new GfxLightmapArray[count];
        for (int index = 0; index < count; index++)
        {
            ReadOnlySpan<byte> row = data.Slice(
                checked(index * LightmapByteCount),
                LightmapByteCount);
            byte[] secondaryLinear = row[..LightmapSecondaryByteCount].ToArray();
            ReverseFourBytePixelOrder(secondaryLinear);
            byte[] primaryLinear = row[LightmapSecondaryByteCount..].ToArray();
            lightmaps[index] = new GfxLightmapArray
            {
                Primary = CreateTwoDimensionalImage(
                    $"*lightmap{index}_primary",
                    GfxImageBaseFormat.B8,
                    textureControl1: 0x0001A9FF,
                    LightmapPrimaryWidth,
                    LightmapPrimaryHeight,
                    ImageCategory.Lightmap,
                    FromLinearPixels(
                        primaryLinear,
                        LightmapPrimaryWidth,
                        LightmapPrimaryHeight,
                        bytesPerPixel: 1)),
                Secondary = CreateTwoDimensionalImage(
                    $"*lightmap{index}_secondary",
                    GfxImageBaseFormat.A8R8G8B8,
                    textureControl1: 0x0001AAE4,
                    LightmapSecondaryWidth,
                    LightmapSecondaryHeight,
                    ImageCategory.Lightmap,
                    FromLinearPixels(
                        secondaryLinear,
                        LightmapSecondaryWidth,
                        LightmapSecondaryHeight,
                        bytesPerPixel: 4))
            };
        }

        return Array.AsReadOnly(lightmaps);
    }

    public static byte[] EncodeReflectionProbes(
        IReadOnlyList<GfxImageAsset?> images,
        IReadOnlyList<GfxReflectionProbe> origins)
    {
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(origins);
        if (images.Count != origins.Count)
        {
            throw new InvalidDataException(
                "GfxWorld reflection-probe image and origin tables have different counts.");
        }
        if (images.Count == 0)
        {
            throw new InvalidDataException(
                "GfxWorld reflection-probe tables do not contain the native default probe.");
        }
        if (images.Count > byte.MaxValue)
        {
            throw new NotSupportedException(
                $"GfxWorld has {images.Count} reflection probes; IW4 supports at most {byte.MaxValue} including the default probe.");
        }

        int authoredCount = images.Count - 1;
        var data = new byte[checked(authoredCount * ReflectionProbeDiskRowByteCount)];
        for (int authoredIndex = 0; authoredIndex < authoredCount; authoredIndex++)
        {
            int runtimeIndex = authoredIndex + 1;
            GfxImageAsset image = images[runtimeIndex] ??
                throw new InvalidDataException(
                    $"GfxWorld reflection-probe image row {runtimeIndex} is null.");
            ValidateReflectionProbeImage(image, runtimeIndex);
            GfxReflectionProbe origin = origins[runtimeIndex] ??
                throw new InvalidDataException(
                    $"GfxWorld reflection-probe origin row {runtimeIndex} is null.");
            Span<byte> row = data.AsSpan(
                checked(authoredIndex * ReflectionProbeDiskRowByteCount),
                ReflectionProbeDiskRowByteCount);
            WriteFiniteSingle(row, 0, origin.OffsetX, runtimeIndex);
            WriteFiniteSingle(row, 4, origin.OffsetY, runtimeIndex);
            WriteFiniteSingle(row, 8, origin.OffsetZ, runtimeIndex);
            // Runtime probe texels are already cooked. Canonical rows retain
            // those texels directly and discard the unavailable source profile.
            row.Slice(12, ReflectionProbeColorCorrectionNameByteCount).Clear();
            EncodeReflectionProbePixels(
                image,
                row[(12 + ReflectionProbeColorCorrectionNameByteCount)..]);
        }

        return data;
    }

    public static (
        IReadOnlyList<GfxImageAsset?> Images,
        IReadOnlyList<GfxReflectionProbe> Origins) DecodeReflectionProbes(
            ReadOnlySpan<byte> data)
    {
        if (data.Length % ReflectionProbeDiskRowByteCount != 0)
        {
            throw new InvalidDataException(
                $"The ReflectionProbes lump length {data.Length} is not divisible by {ReflectionProbeDiskRowByteCount}.");
        }

        int authoredCount = data.Length / ReflectionProbeDiskRowByteCount;
        if (authoredCount >= byte.MaxValue)
        {
            throw new InvalidDataException(
                $"The ReflectionProbes lump contains {authoredCount} authored probes; IW4 supports at most {byte.MaxValue - 1} plus the default probe.");
        }

        var images = new GfxImageAsset?[authoredCount + 1];
        var origins = new GfxReflectionProbe[authoredCount + 1];
        images[0] = CreateReflectionProbeImage(
            0,
            CreateDefaultReflectionProbePixels());
        origins[0] = new GfxReflectionProbe(0, 0, 0);

        for (int authoredIndex = 0; authoredIndex < authoredCount; authoredIndex++)
        {
            int runtimeIndex = authoredIndex + 1;
            ReadOnlySpan<byte> row = data.Slice(
                checked(authoredIndex * ReflectionProbeDiskRowByteCount),
                ReflectionProbeDiskRowByteCount);
            origins[runtimeIndex] = new GfxReflectionProbe(
                ReadFiniteSingle(row, 0, runtimeIndex),
                ReadFiniteSingle(row, 4, runtimeIndex),
                ReadFiniteSingle(row, 8, runtimeIndex));
            images[runtimeIndex] = CreateReflectionProbeImage(
                runtimeIndex,
                DecodeReflectionProbePixels(
                    row[(12 + ReflectionProbeColorCorrectionNameByteCount)..]));
        }

        return (Array.AsReadOnly(images), Array.AsReadOnly(origins));
    }

    private static void ValidateTwoDimensionalImage(
        GfxImageAsset image,
        uint requiredFormatKey,
        int bytesPerPixel,
        string description)
    {
        if (image.MapType != MapType.TwoDimensional ||
            image.DimensionCount != GfxImageDimension.TwoDimensional ||
            image.IsCubemap || image.Depth != 1 || image.LevelCount != 1)
        {
            throw new InvalidDataException(
                $"{description} is not a single-level two-dimensional PS3 image.");
        }
        uint formatKey = GfxImagePixelLayout.BuildFormatKey(
            image.FormatEncoding,
            image.TextureRemap);
        if (formatKey != requiredFormatKey)
        {
            throw new NotSupportedException(
                $"{description} uses format key 0x{formatKey:X8}; expected 0x{requiredFormatKey:X8}.");
        }
        int requiredBytes = checked(image.Width * image.Height * bytesPerPixel);
        ValidatePayload(image, requiredBytes, description);
        if (!image.FormatEncoding.IsLinear &&
            (!IsPowerOfTwo(image.Width) || !IsPowerOfTwo(image.Height)))
        {
            throw new NotSupportedException(
                $"{description} uses a swizzled non-power-of-two layout.");
        }
    }

    private static void ValidateReflectionProbeImage(
        GfxImageAsset image,
        int index)
    {
        string description = $"GfxWorld reflection-probe image row {index}";
        if (image.MapType != MapType.Cube ||
            image.DimensionCount != GfxImageDimension.TwoDimensional ||
            !image.IsCubemap || image.Depth != 1 ||
            image.Width != ReflectionProbeEdgeLength ||
            image.Height != ReflectionProbeEdgeLength ||
            image.LevelCount != ReflectionProbeMipCount)
        {
            throw new InvalidDataException(
                $"{description} is not the native 64x64 seven-level cubemap layout.");
        }
        uint formatKey = GfxImagePixelLayout.BuildFormatKey(
            image.FormatEncoding,
            image.TextureRemap);
        if (formatKey != ColorImageFormatKey)
        {
            throw new NotSupportedException(
                $"{description} uses format key 0x{formatKey:X8}; expected 0x{ColorImageFormatKey:X8}.");
        }
        ValidatePayload(image, ReflectionProbeRuntimeByteCount, description);
    }

    private static void ValidatePayload(
        GfxImageAsset image,
        int requiredBytes,
        string description)
    {
        if (image.PayloadBytes.Count != requiredBytes)
        {
            throw new InvalidDataException(
                $"{description} has {image.PayloadBytes.Count} payload bytes; expected {requiredBytes}.");
        }
        if (image.PayloadByteCount != image.PayloadBytes.Count)
        {
            throw new InvalidDataException(
                $"{description} declares {image.PayloadByteCount} payload bytes but materializes {image.PayloadBytes.Count}.");
        }
    }

    private static byte[] ToLinearPixels(
        GfxImageAsset image,
        int width,
        int height,
        int bytesPerPixel)
    {
        byte[] source = image.PayloadBytes as byte[] ?? image.PayloadBytes.ToArray();
        return image.FormatEncoding.IsLinear
            ? source.ToArray()
            : DeswizzleMorton2D(source, width, height, bytesPerPixel);
    }

    private static byte[] FromLinearPixels(
        ReadOnlySpan<byte> pixels,
        int width,
        int height,
        int bytesPerPixel) =>
        SwizzleMorton2D(pixels, width, height, bytesPerPixel);

    private static void CopyRectangle(
        ReadOnlySpan<byte> source,
        int sourceWidth,
        int sourceX,
        int sourceY,
        int width,
        int height,
        int bytesPerPixel,
        Span<byte> destination)
    {
        int rowByteCount = checked(width * bytesPerPixel);
        if (destination.Length != checked(rowByteCount * height))
            throw new ArgumentException("The rectangle destination has the wrong length.", nameof(destination));
        for (int row = 0; row < height; row++)
        {
            int sourceOffset = checked(
                ((sourceY + row) * sourceWidth + sourceX) * bytesPerPixel);
            source.Slice(sourceOffset, rowByteCount)
                .CopyTo(destination.Slice(checked(row * rowByteCount), rowByteCount));
        }
    }

    private static GfxImageAsset CreateTwoDimensionalImage(
        string name,
        GfxImageBaseFormat format,
        uint textureControl1,
        ushort width,
        ushort height,
        ImageCategory category,
        byte[] payload) => new()
    {
        Format = (byte)format,
        LevelCount = 1,
        DimensionCount = GfxImageDimension.TwoDimensional,
        TextureControl1 = textureControl1,
        Width = width,
        Height = height,
        Depth = 1,
        MemoryLocation = GfxImageMemoryLocation.Local,
        MapType = MapType.TwoDimensional,
        TextureSemantic = TextureSemantic.Function,
        Category = category,
        CardMemory = checked((uint)payload.Length),
        BaseWidth = width,
        BaseHeight = height,
        BaseDepth = 1,
        BaseLevelCount = 1,
        Cached = GfxImageCached.No,
        PayloadByteCount = payload.Length,
        PayloadBytes = payload,
        Name = name
    };

    private static GfxImageAsset CreateReflectionProbeImage(
        int index,
        byte[] payload) => new()
    {
        Format = (byte)GfxImageBaseFormat.A8R8G8B8,
        LevelCount = ReflectionProbeMipCount,
        DimensionCount = GfxImageDimension.TwoDimensional,
        MultiFaceControl = 1,
        TextureControl1 = 0x0001AAE4,
        Width = ReflectionProbeEdgeLength,
        Height = ReflectionProbeEdgeLength,
        Depth = 1,
        MemoryLocation = GfxImageMemoryLocation.Local,
        MapType = MapType.Cube,
        TextureSemantic = TextureSemantic.Function,
        Category = ImageCategory.AutoGenerated,
        CardMemory = checked((uint)payload.Length),
        BaseWidth = ReflectionProbeEdgeLength,
        BaseHeight = ReflectionProbeEdgeLength,
        BaseDepth = 1,
        BaseLevelCount = ReflectionProbeMipCount,
        Cached = GfxImageCached.No,
        PayloadByteCount = payload.Length,
        PayloadBytes = payload,
        Name = $"*reflection_probe{index}"
    };

    private static void EncodeReflectionProbePixels(
        GfxImageAsset image,
        Span<byte> destination)
    {
        if (destination.Length != ReflectionProbeDiskPixelByteCount)
            throw new ArgumentException("The reflection-probe destination has the wrong length.", nameof(destination));

        byte[] payload = image.PayloadBytes as byte[] ?? image.PayloadBytes.ToArray();
        for (int face = 0; face < ReflectionProbeFaceCount; face++)
        {
            int runtimeMipOffset = checked(face * ReflectionProbeFaceStride);
            int tailOffset = checked(
                ReflectionProbeFaceCount * ReflectionProbeTopMipByteCount +
                face * (ReflectionProbeFacePixelByteCount - ReflectionProbeTopMipByteCount));
            for (int mip = 0; mip < ReflectionProbeMipCount; mip++)
            {
                int edge = System.Math.Max(1, ReflectionProbeEdgeLength >> mip);
                int mipByteCount = checked(edge * edge * 4);
                ReadOnlySpan<byte> runtimeMip = payload.AsSpan(
                    runtimeMipOffset,
                    mipByteCount);
                byte[] linear = image.FormatEncoding.IsLinear
                    ? runtimeMip.ToArray()
                    : DeswizzleMorton2D(runtimeMip, edge, edge, bytesPerPixel: 4);
                ReverseFourBytePixelOrder(linear);

                int diskOffset = mip == 0
                    ? checked(face * ReflectionProbeTopMipByteCount)
                    : tailOffset;
                linear.CopyTo(destination[diskOffset..]);
                runtimeMipOffset = checked(runtimeMipOffset + mipByteCount);
                if (mip != 0)
                    tailOffset = checked(tailOffset + mipByteCount);
            }
        }
    }

    private static byte[] DecodeReflectionProbePixels(
        ReadOnlySpan<byte> source)
    {
        if (source.Length != ReflectionProbeDiskPixelByteCount)
            throw new ArgumentException("The reflection-probe source has the wrong length.", nameof(source));

        var payload = new byte[ReflectionProbeRuntimeByteCount];
        for (int face = 0; face < ReflectionProbeFaceCount; face++)
        {
            int runtimeMipOffset = checked(face * ReflectionProbeFaceStride);
            int tailOffset = checked(
                ReflectionProbeFaceCount * ReflectionProbeTopMipByteCount +
                face * (ReflectionProbeFacePixelByteCount - ReflectionProbeTopMipByteCount));
            for (int mip = 0; mip < ReflectionProbeMipCount; mip++)
            {
                int edge = System.Math.Max(1, ReflectionProbeEdgeLength >> mip);
                int mipByteCount = checked(edge * edge * 4);
                int diskOffset = mip == 0
                    ? checked(face * ReflectionProbeTopMipByteCount)
                    : tailOffset;
                byte[] linear = source.Slice(diskOffset, mipByteCount).ToArray();
                ReverseFourBytePixelOrder(linear);
                byte[] runtimeMip = SwizzleMorton2D(
                    linear,
                    edge,
                    edge,
                    bytesPerPixel: 4);
                runtimeMip.CopyTo(payload, runtimeMipOffset);
                runtimeMipOffset = checked(runtimeMipOffset + mipByteCount);
                if (mip != 0)
                    tailOffset = checked(tailOffset + mipByteCount);
            }
        }

        return payload;
    }

    private static byte[] CreateDefaultReflectionProbePixels()
    {
        var diskPixels = new byte[ReflectionProbeDiskPixelByteCount];
        for (int offset = 0; offset < diskPixels.Length; offset += 4)
        {
            diskPixels[offset] = 0;
            diskPixels[offset + 1] = 0;
            diskPixels[offset + 2] = byte.MaxValue;
            diskPixels[offset + 3] = byte.MaxValue;
        }
        return DecodeReflectionProbePixels(diskPixels);
    }

    private static void ReverseFourBytePixelOrder(Span<byte> pixels)
    {
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            (pixels[offset], pixels[offset + 3]) =
                (pixels[offset + 3], pixels[offset]);
            (pixels[offset + 1], pixels[offset + 2]) =
                (pixels[offset + 2], pixels[offset + 1]);
        }
    }

    private static byte[] DeswizzleMorton2D(
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

    private static byte[] SwizzleMorton2D(
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

    private static float ReadFiniteSingle(
        ReadOnlySpan<byte> data,
        int offset,
        int index)
    {
        float value = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);
        if (!float.IsFinite(value))
        {
            throw new InvalidDataException(
                $"Reflection probe row {index} has a non-finite origin component.");
        }
        return value;
    }

    private static void WriteFiniteSingle(
        Span<byte> data,
        int offset,
        float value,
        int index)
    {
        if (!float.IsFinite(value))
        {
            throw new InvalidDataException(
                $"Reflection probe row {index} has a non-finite origin component.");
        }
        BinaryPrimitives.WriteSingleLittleEndian(
            data[offset..],
            value == 0.0f ? 0.0f : value);
    }

    private static bool IsPowerOfTwo(int value) =>
        value > 0 && (value & (value - 1)) == 0;

    private sealed record LightmapLayout(
        GfxImageAsset Primary,
        GfxImageAsset Secondary,
        int TilesWide,
        int TilesHigh);
}
