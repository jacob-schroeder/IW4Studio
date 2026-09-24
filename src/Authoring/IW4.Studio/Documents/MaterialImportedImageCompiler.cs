using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using IW4.Formats.SourceFormat.Image;
using IW4.Game.Assets.Image;
namespace IW4.Studio.Documents;

public sealed record MaterialImageImportCandidate(
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
public static class MaterialImportedImageCompiler
{
    public static MaterialImageImportCandidate Compile(
        MaterialDraft template,
        int textureTableOrdinal,
        ImageFileDocument source)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(source);
        ImageSourceMipLevel[] levels = source.MipLevels.ToArray();
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

        string imageName = BuildImageName(template.Material.Info.Name,
            textureTableOrdinal, source.Shape, levels, source.UsesSrgbReads);
        GfxImageAsset image = ImageSourceCompiler.Compile(imageName, source,
            selectedImage.TextureSemantic,
            source.UsesSrgbReads ?? selectedImage.UsesSrgbReads);
        ImageSourceMipLevel topLevel = levels[0];
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
