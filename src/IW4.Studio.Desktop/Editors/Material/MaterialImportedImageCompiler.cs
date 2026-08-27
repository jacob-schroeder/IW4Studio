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

        ImageSourceMipLevel topLevel = levels[0];
        if (topLevel.Width is <= 0 or > ushort.MaxValue ||
            topLevel.Height is <= 0 or > ushort.MaxValue ||
            topLevel.Depth != 1)
        {
            throw new InvalidDataException(
                $"The imported image dimensions {topLevel.Width}x{topLevel.Height}x{topLevel.Depth} are not a valid IW4 2D image.");
        }
        int completeMipCount = ComputeFullMipCount(
            topLevel.Width,
            topLevel.Height);
        if (levels.Length != 1 && levels.Length != completeMipCount)
        {
            throw new InvalidDataException(
                $"Material import requires either one mip or the complete " +
                $"{completeMipCount:N0}-mip chain for " +
                $"{topLevel.Width:N0} × {topLevel.Height:N0}; the file " +
                $"contains {levels.Length:N0} mips.");
        }

        int decodedByteCount = 0;
        int expectedWidth = topLevel.Width;
        int expectedHeight = topLevel.Height;
        for (int levelIndex = 0; levelIndex < levels.Length; levelIndex++)
        {
            ImageSourceMipLevel level = levels[levelIndex];
            if (level.Width != expectedWidth ||
                level.Height != expectedHeight ||
                level.Depth != 1)
            {
                throw new InvalidDataException(
                    $"Imported mip {levelIndex} is {level.Width}x{level.Height}x{level.Depth}; expected {expectedWidth}x{expectedHeight}x1.");
            }

            int expectedBytes = checked(expectedWidth * expectedHeight * 4);
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

            expectedWidth = Math.Max(1, expectedWidth / 2);
            expectedHeight = Math.Max(1, expectedHeight / 2);
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

        int payloadByteCount = checked((decodedByteCount + 0x7f) & ~0x7f);
        var payload = new byte[payloadByteCount];
        int destinationOffset = 0;
        foreach (ImageSourceMipLevel level in levels)
        {
            ReadOnlySpan<byte> rgba = level.RgbaBytes.Span;
            for (int sourceOffset = 0;
                 sourceOffset < rgba.Length;
                 sourceOffset += 4)
            {
                payload[destinationOffset] = rgba[sourceOffset + 3];
                payload[destinationOffset + 1] = rgba[sourceOffset];
                payload[destinationOffset + 2] = rgba[sourceOffset + 1];
                payload[destinationOffset + 3] = rgba[sourceOffset + 2];
                destinationOffset += 4;
            }
        }

        byte levelCount = checked((byte)levels.Length);
        string imageName = BuildImageName(
            template.Material.Info.Name,
            textureTableOrdinal,
            levels,
            source.UsesSrgbReads);
        var image = new GfxImageAsset
        {
            Format = (byte)((byte)GfxImageBaseFormat.A8R8G8B8 |
                (byte)GfxImageFormatFlags.Linear),
            LevelCount = levelCount,
            DimensionCount = GfxImageDimension.TwoDimensional,
            TextureControl1 = 0x0001aae4,
            Width = checked((ushort)topLevel.Width),
            Height = checked((ushort)topLevel.Height),
            Depth = 1,
            MemoryLocation = GfxImageMemoryLocation.Local,
            RenderTargetPitch = checked((uint)topLevel.Width * 4u),
            MapType = MapType.TwoDimensional,
            TextureSemantic = selectedImage.TextureSemantic,
            Category = ImageCategory.LoadFromFile,
            UseSrgbReads = source.UsesSrgbReads.HasValue
                ? source.UsesSrgbReads.Value ? (byte)1 : (byte)0
                : selectedImage.UseSrgbReads,
            CardMemory = checked((uint)payload.Length),
            BaseWidth = checked((ushort)topLevel.Width),
            BaseHeight = checked((ushort)topLevel.Height),
            BaseDepth = 1,
            BaseLevelCount = levelCount,
            Cached = GfxImageCached.Auto,
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
            levels.Length);
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

    private static string BuildImageName(
        string? materialName,
        int textureTableOrdinal,
        IReadOnlyList<ImageSourceMipLevel> levels,
        bool? usesSrgbReads)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(
            HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(materialName ?? string.Empty));
        Span<byte> header = stackalloc byte[9];
        BinaryPrimitives.WriteInt32LittleEndian(
            header,
            textureTableOrdinal);
        BinaryPrimitives.WriteInt32LittleEndian(
            header[4..],
            levels.Count);
        header[8] = usesSrgbReads switch
        {
            true => 1,
            false => 0,
            null => byte.MaxValue
        };
        hash.AppendData(header);
        Span<byte> dimensions = stackalloc byte[8];
        foreach (ImageSourceMipLevel level in levels)
        {
            BinaryPrimitives.WriteInt32LittleEndian(dimensions, level.Width);
            BinaryPrimitives.WriteInt32LittleEndian(
                dimensions[4..],
                level.Height);
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
