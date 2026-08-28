using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using IW4.AssetExchange.SourceFormat.Image;
using IW4.Assets.Assets.Image;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Editors.Material;

internal sealed record MaterialImageImportCandidate(
    MaterialDraft Draft,
    GfxImageAsset Image,
    int TextureTableOrdinal,
    int Width,
    int Height,
    int Depth,
    ImageFileShape Shape,
    int MipCount);

/// <summary>
/// Converts a decoded desktop source image into one detached, inline PS3
/// GfxImage and repoints only the selected Material texture row.
/// </summary>
internal static class MaterialImportedImageCompiler
{
    private const int MaximumDecodedByteCount = 256 * 1024 * 1024;

    internal static MaterialImageImportCandidate Compile(
        MaterialDraft template,
        int textureTableOrdinal,
        ImageFileDocument source)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(source);
        ImageSourceMipLevel[] levels = source.MipLevels.ToArray();
        if (levels.Length == 0)
            throw new InvalidDataException("The imported image has no mip levels.");
        if (levels.Length > byte.MaxValue)
        {
            throw new InvalidDataException(
                $"The imported image has {levels.Length:N0} mip levels; IW4 stores at most {byte.MaxValue:N0}.");
        }

        if ((uint)textureTableOrdinal >=
            (uint)template.Material.Textures.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(textureTableOrdinal));
        }
        GfxImageAsset selectedImage =
            template.Material.Textures[textureTableOrdinal].Image ??
            throw new InvalidDataException(
                "The selected Material texture row has no image to replace.");
        ImageFileShape selectedShape = GetImageShape(selectedImage);
        if (source.Shape != selectedShape)
        {
            throw new InvalidDataException(
                $"The imported {DescribeShape(source.Shape)} image cannot " +
                $"replace the selected {DescribeShape(selectedShape)} " +
                "Material texture. Imports must preserve the texture " +
                "sampler's dimensional shape.");
        }

        ImageSourceMipLevel topLevel = levels[0];
        if (topLevel.Width is <= 0 or > ushort.MaxValue ||
            topLevel.Height is <= 0 or > ushort.MaxValue ||
            topLevel.Depth is <= 0 or > ushort.MaxValue)
        {
            throw new InvalidDataException(
                $"The imported image dimensions {topLevel.Width}x" +
                $"{topLevel.Height}x{topLevel.Depth} are not valid IW4 " +
                "image dimensions.");
        }
        if (source.Shape == ImageFileShape.TwoDimensional &&
            topLevel.Depth != 1)
        {
            throw new InvalidDataException(
                $"The imported two-dimensional image has depth " +
                $"{topLevel.Depth:N0}; expected 1.");
        }
        if (source.Shape == ImageFileShape.Cube &&
            (topLevel.Depth != 1 || topLevel.Width != topLevel.Height))
        {
            throw new InvalidDataException(
                $"The imported cubemap dimensions {topLevel.Width}x" +
                $"{topLevel.Height}x{topLevel.Depth} are invalid; cubemap " +
                "faces must be square and have depth 1.");
        }
        int completeMipCount = ComputeFullMipCount(
            topLevel.Width,
            topLevel.Height,
            source.Shape == ImageFileShape.Volume
                ? topLevel.Depth
                : 1);
        if (levels.Length != 1 && levels.Length != completeMipCount)
        {
            throw new InvalidDataException(
                $"Material import requires either one mip or the complete " +
                $"{completeMipCount:N0}-mip chain for " +
                $"{topLevel.Width:N0} × {topLevel.Height:N0} × " +
                $"{topLevel.Depth:N0}; the file " +
                $"contains {levels.Length:N0} mips.");
        }

        long decodedByteCount = 0;
        long cubeFaceChainByteCount = 0;
        int expectedWidth = topLevel.Width;
        int expectedHeight = topLevel.Height;
        int expectedDepth = topLevel.Depth;
        int faceCount = source.Shape == ImageFileShape.Cube ? 6 : 1;
        for (int levelIndex = 0; levelIndex < levels.Length; levelIndex++)
        {
            ImageSourceMipLevel level = levels[levelIndex];
            if (level.Width != expectedWidth ||
                level.Height != expectedHeight ||
                level.Depth != expectedDepth)
            {
                throw new InvalidDataException(
                    $"Imported mip {levelIndex} is {level.Width}x" +
                    $"{level.Height}x{level.Depth}; expected " +
                    $"{expectedWidth}x{expectedHeight}x{expectedDepth}.");
            }

            long expectedBytesLong = checked(
                (long)expectedWidth * expectedHeight * expectedDepth *
                faceCount * 4);
            if (expectedBytesLong > int.MaxValue)
            {
                throw new InvalidDataException(
                    $"Imported mip {levelIndex} is too large to compile.");
            }
            int expectedBytes = checked((int)expectedBytesLong);
            if (level.RgbaBytes.Length != expectedBytes)
            {
                throw new InvalidDataException(
                    $"Imported mip {levelIndex} contains {level.RgbaBytes.Length:N0} RGBA bytes; expected {expectedBytes:N0}.");
            }

            decodedByteCount = checked(decodedByteCount + expectedBytes);
            if (decodedByteCount > MaximumDecodedByteCount)
            {
                throw new InvalidDataException(
                    $"The decoded image exceeds the {MaximumDecodedByteCount / (1024 * 1024):N0} MiB Material import limit.");
            }
            if (source.Shape == ImageFileShape.Cube)
            {
                cubeFaceChainByteCount = checked(
                    cubeFaceChainByteCount + expectedBytes / faceCount);
            }

            expectedWidth = Math.Max(1, expectedWidth / 2);
            expectedHeight = Math.Max(1, expectedHeight / 2);
            expectedDepth = source.Shape == ImageFileShape.Volume
                ? Math.Max(1, expectedDepth / 2)
                : 1;
        }

        int payloadByteCount = source.Shape == ImageFileShape.Cube
            ? checked(AlignTo128(cubeFaceChainByteCount) * faceCount)
            : AlignTo128(decodedByteCount);
        var payload = new byte[payloadByteCount];
        if (source.Shape == ImageFileShape.Cube)
        {
            int faceStride = AlignTo128(cubeFaceChainByteCount);
            for (int faceOrdinal = 0;
                 faceOrdinal < faceCount;
                 faceOrdinal++)
            {
                int destinationOffset = checked(faceOrdinal * faceStride);
                foreach (ImageSourceMipLevel level in levels)
                {
                    int faceByteCount = checked(
                        level.Width * level.Height * 4);
                    ReadOnlySpan<byte> sourceFace = level.RgbaBytes.Span.Slice(
                        checked(faceOrdinal * faceByteCount),
                        faceByteCount);
                    WriteA8R8G8B8(
                        sourceFace,
                        payload.AsSpan(destinationOffset, faceByteCount));
                    destinationOffset = checked(
                        destinationOffset + faceByteCount);
                }
            }
        }
        else
        {
            int destinationOffset = 0;
            foreach (ImageSourceMipLevel level in levels)
            {
                WriteA8R8G8B8(
                    level.RgbaBytes.Span,
                    payload.AsSpan(
                        destinationOffset,
                        level.RgbaBytes.Length));
                destinationOffset = checked(
                    destinationOffset + level.RgbaBytes.Length);
            }
        }

        byte levelCount = checked((byte)levels.Length);
        string imageName = BuildImageName(
            template.Material.Info.Name,
            textureTableOrdinal,
            source.Shape,
            levels,
            source.UsesSrgbReads);
        ushort imageDepth = checked((ushort)(
            source.Shape == ImageFileShape.Volume
                ? topLevel.Depth
                : 1));
        var image = new GfxImageAsset
        {
            Format = (byte)((byte)GfxImageBaseFormat.A8R8G8B8 |
                (byte)GfxImageFormatFlags.Linear),
            LevelCount = levelCount,
            DimensionCount = source.Shape == ImageFileShape.Volume
                ? GfxImageDimension.ThreeDimensional
                : GfxImageDimension.TwoDimensional,
            MultiFaceControl = source.Shape == ImageFileShape.Cube
                ? (byte)1
                : (byte)0,
            TextureControl1 = 0x0001aae4,
            Width = checked((ushort)topLevel.Width),
            Height = checked((ushort)topLevel.Height),
            Depth = imageDepth,
            MemoryLocation = GfxImageMemoryLocation.Local,
            RenderTargetPitch = checked((uint)topLevel.Width * 4u),
            MapType = source.Shape switch
            {
                ImageFileShape.TwoDimensional => MapType.TwoDimensional,
                ImageFileShape.Cube => MapType.Cube,
                ImageFileShape.Volume => MapType.ThreeDimensional,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(source),
                    source.Shape,
                    "An imported image shape is required.")
            },
            TextureSemantic = selectedImage.TextureSemantic,
            Category = ImageCategory.LoadFromFile,
            UseSrgbReads = source.UsesSrgbReads.HasValue
                ? source.UsesSrgbReads.Value ? (byte)1 : (byte)0
                : selectedImage.UseSrgbReads,
            CardMemory = checked((uint)payload.Length),
            BaseWidth = checked((ushort)topLevel.Width),
            BaseHeight = checked((ushort)topLevel.Height),
            BaseDepth = imageDepth,
            BaseLevelCount = levelCount,
            // Inline fastfile pixels are zone-owned; cached images enter the
            // engine's independent card-memory release path during unload.
            Cached = GfxImageCached.No,
            PayloadByteCount = payload.Length,
            PayloadBytes = payload,
            Name = imageName
        };

        int expectedPayloadByteCount = GfxImagePixelLayout.ComputePayloadByteCount(
            image.FormatEncoding,
            image.LevelCount,
            image.IsCubemap,
            image.TextureRemap,
            image.Width,
            image.Height,
            image.Depth);
        if (expectedPayloadByteCount != payload.Length)
        {
            throw new InvalidDataException(
                $"The compiled IW4 image payload is {payload.Length:N0} byte(s); its descriptor requires {expectedPayloadByteCount:N0}.");
        }

        MaterialDraft candidate = template.WithTextureImage(
            textureTableOrdinal,
            image);
        return new MaterialImageImportCandidate(
            candidate,
            image,
            textureTableOrdinal,
            topLevel.Width,
            topLevel.Height,
            topLevel.Depth,
            source.Shape,
            levels.Length);
    }

    private static ImageFileShape GetImageShape(GfxImageAsset image)
    {
        if (image.MapType == MapType.TwoDimensional &&
            image.DimensionCount == GfxImageDimension.TwoDimensional &&
            !image.IsCubemap &&
            image.Depth == 1)
        {
            return ImageFileShape.TwoDimensional;
        }
        if (image.MapType == MapType.Cube &&
            image.DimensionCount == GfxImageDimension.TwoDimensional &&
            image.IsCubemap &&
            image.Depth == 1 &&
            image.Width == image.Height)
        {
            return ImageFileShape.Cube;
        }
        if (image.MapType == MapType.ThreeDimensional &&
            image.DimensionCount == GfxImageDimension.ThreeDimensional &&
            !image.IsCubemap &&
            image.Depth > 0)
        {
            return ImageFileShape.Volume;
        }

        throw new InvalidDataException(
            $"The selected Material image has unsupported shape fields " +
            $"mapType={image.MapType}, dimension={image.DimensionCount}, " +
            $"cubemap={image.IsCubemap}, depth={image.Depth}.");
    }

    private static string DescribeShape(ImageFileShape shape) => shape switch
    {
        ImageFileShape.TwoDimensional => "two-dimensional",
        ImageFileShape.Cube => "cubemap",
        ImageFileShape.Volume => "volume",
        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null)
    };

    private static int ComputeFullMipCount(
        int width,
        int height,
        int depth)
    {
        int count = 1;
        while (width > 1 || height > 1 || depth > 1)
        {
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
            depth = Math.Max(1, depth / 2);
            count++;
        }
        return count;
    }

    private static int AlignTo128(long byteCount) => checked((int)(
        (byteCount + 0x7f) & ~0x7fL));

    private static void WriteA8R8G8B8(
        ReadOnlySpan<byte> rgba,
        Span<byte> destination)
    {
        if (rgba.Length != destination.Length || rgba.Length % 4 != 0)
        {
            throw new ArgumentException(
                "RGBA source and A8R8G8B8 destination lengths must match.",
                nameof(destination));
        }

        for (int offset = 0; offset < rgba.Length; offset += 4)
        {
            destination[offset] = rgba[offset + 3];
            destination[offset + 1] = rgba[offset];
            destination[offset + 2] = rgba[offset + 1];
            destination[offset + 3] = rgba[offset + 2];
        }
    }

    private static string BuildImageName(
        string? materialName,
        int textureTableOrdinal,
        ImageFileShape shape,
        IReadOnlyList<ImageSourceMipLevel> levels,
        bool? usesSrgbReads)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(
            HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(materialName ?? string.Empty));
        Span<byte> header = stackalloc byte[10];
        BinaryPrimitives.WriteInt32LittleEndian(
            header,
            textureTableOrdinal);
        BinaryPrimitives.WriteInt32LittleEndian(
            header[4..],
            levels.Count);
        header[8] = checked((byte)shape);
        header[9] = usesSrgbReads switch
        {
            true => 1,
            false => 0,
            null => byte.MaxValue
        };
        hash.AppendData(header);
        Span<byte> dimensions = stackalloc byte[12];
        foreach (ImageSourceMipLevel level in levels)
        {
            BinaryPrimitives.WriteInt32LittleEndian(dimensions, level.Width);
            BinaryPrimitives.WriteInt32LittleEndian(
                dimensions[4..],
                level.Height);
            BinaryPrimitives.WriteInt32LittleEndian(
                dimensions[8..],
                level.Depth);
            hash.AppendData(dimensions);
            hash.AppendData(level.RgbaBytes.Span);
        }

        string digest = Convert.ToHexString(hash.GetHashAndReset())
            .ToLowerInvariant()[..16];
        string safeMaterial = SafeNamePart(materialName);
        return $"{safeMaterial}_studio_image_{textureTableOrdinal}_{digest}";
    }

    private static string SafeNamePart(string? value)
    {
        string normalized = string.IsNullOrWhiteSpace(value)
            ? "material"
            : value.Trim();
        char[] characters = normalized
            .Select(character => char.IsAsciiLetterOrDigit(character) ||
                                 character is '_' or '-'
                ? char.ToLowerInvariant(character)
                : '_')
            .ToArray();
        string result = new string(characters).Trim('_');
        if (string.IsNullOrEmpty(result))
            result = "material";
        return result.Length <= 48 ? result : result[..48];
    }
}
