using IW4.Assets.Assets.Image;

namespace IW4.AssetExchange.SourceFormat.Image;

/// <summary>
/// Writes a decoded PS3 IW4 image to the DDS source layout consumed by
/// OpenAssetTools. The source DDS uses uncompressed RGBA8 mip levels so it
/// does not mislabel RSX texture bytes as a PC texture payload.
/// </summary>
public sealed class ImageExchange
{
    private const uint DdsMagic = 0x20534444;
    private const uint HeaderSize = 124;
    private const uint HeaderFlags = 0x0000100f;
    private const uint HeaderMipMapCount = 0x00020000;
    private const uint HeaderDepth = 0x00800000;
    private const uint PixelFormatSize = 32;
    private const uint PixelFormatFlags = 0x00000041;
    private const uint ComplexCaps = 0x00000008;
    private const uint TextureCaps = 0x00001000;
    private const uint MipMapCaps = 0x00400000;
    private const uint CubeCaps = 0x0000fe00;
    private const uint VolumeCaps = 0x00200000;

    public IReadOnlyList<string> Unlink(
        string sourceDirectory,
        GfxImageAsset asset,
        IReadOnlyList<ImageSourceMipLevel> mipLevels)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(mipLevels);
        string assetName = SourceOutput.NormalizeOwnedAssetName(
            asset.Name,
            "Image");
        if (asset.FormatEncoding.BaseFormat ==
            GfxImageBaseFormat.Y16X16Float)
        {
            throw new NotSupportedException(
                $"Image '{assetName}' uses a floating-point format that " +
                "cannot be represented by the RGBA8 source DDS writer.");
        }

        ImageShape shape = GetImageShape(asset, assetName);
        ImageSourceMipLevel[] levels = mipLevels.ToArray();
        if (levels.Length == 0)
        {
            throw new InvalidDataException(
                $"Image '{assetName}' has no decoded mip levels.");
        }

        ImageSourceMipLevel topLevel = levels[0];
        if (topLevel.Width <= 0 ||
            topLevel.Height <= 0 ||
            topLevel.Depth <= 0)
        {
            throw new InvalidDataException(
                $"Image '{assetName}' has invalid decoded dimensions " +
                $"{topLevel.Width}x{topLevel.Height}x{topLevel.Depth}.");
        }
        if (shape == ImageShape.Cube &&
            topLevel.Width != topLevel.Height)
        {
            throw new InvalidDataException(
                $"Cubemap image '{assetName}' has non-square decoded dimensions " +
                $"{topLevel.Width}x{topLevel.Height}.");
        }
        if (shape != ImageShape.Volume && topLevel.Depth != 1)
        {
            throw new InvalidDataException(
                $"Image '{assetName}' has invalid decoded depth {topLevel.Depth} " +
                $"for its {shape} shape.");
        }

        int expectedWidth = topLevel.Width;
        int expectedHeight = topLevel.Height;
        int expectedDepth = topLevel.Depth;
        int faceCount = shape == ImageShape.Cube ? 6 : 1;
        for (int levelIndex = 0; levelIndex < levels.Length; levelIndex++)
        {
            ImageSourceMipLevel level = levels[levelIndex];
            if (level.Width != expectedWidth ||
                level.Height != expectedHeight ||
                level.Depth != expectedDepth)
            {
                throw new InvalidDataException(
                    $"Image '{assetName}' mip {levelIndex} has decoded dimensions " +
                    $"{level.Width}x{level.Height}x{level.Depth}; expected " +
                    $"{expectedWidth}x{expectedHeight}x{expectedDepth}.");
            }

            long expectedByteCount;
            try
            {
                expectedByteCount = checked(
                    (long)expectedWidth * expectedHeight * expectedDepth *
                    faceCount * 4);
            }
            catch (OverflowException exception)
            {
                throw new InvalidDataException(
                    $"Image '{assetName}' mip {levelIndex} dimensions are too large.",
                    exception);
            }
            if (level.RgbaBytes.Length != expectedByteCount)
            {
                throw new InvalidDataException(
                    $"Image '{assetName}' mip {levelIndex} has " +
                    $"{level.RgbaBytes.Length} decoded RGBA bytes; expected " +
                    $"{expectedByteCount} for {faceCount} face(s) at " +
                    $"{expectedWidth}x{expectedHeight}x{expectedDepth}.");
            }

            expectedWidth = Math.Max(1, expectedWidth / 2);
            expectedHeight = Math.Max(1, expectedHeight / 2);
            if (shape == ImageShape.Volume)
                expectedDepth = Math.Max(1, expectedDepth / 2);
        }

        string cleanName = assetName.Replace('*', '_');
        var output = new SourceOutput(sourceDirectory);
        return output.WriteBinaryBatch([
            ($"images/{cleanName}.dds", stream =>
                WriteDds(stream, levels, shape))
        ]);
    }

    private static void WriteDds(
        Stream stream,
        IReadOnlyList<ImageSourceMipLevel> mipLevels,
        ImageShape shape)
    {
        ImageSourceMipLevel topLevel = mipLevels[0];
        bool hasMipMaps = mipLevels.Count > 1;
        bool isCube = shape == ImageShape.Cube;
        bool isVolume = shape == ImageShape.Volume;
        uint headerFlags = HeaderFlags;
        uint caps = TextureCaps;
        uint caps2 = 0;
        if (hasMipMaps)
        {
            headerFlags |= HeaderMipMapCount;
            caps |= ComplexCaps | MipMapCaps;
        }
        if (isCube)
        {
            caps |= ComplexCaps;
            caps2 |= CubeCaps;
        }
        if (isVolume)
        {
            headerFlags |= HeaderDepth;
            caps |= ComplexCaps;
            caps2 |= VolumeCaps;
        }

        using var writer = new BinaryWriter(
            stream,
            System.Text.Encoding.UTF8,
            leaveOpen: true);
        writer.Write(DdsMagic);
        writer.Write(HeaderSize);
        writer.Write(headerFlags);
        writer.Write((uint)topLevel.Height);
        writer.Write((uint)topLevel.Width);
        writer.Write(checked((uint)topLevel.Width * 4));
        writer.Write(isVolume ? (uint)topLevel.Depth : 0u);
        writer.Write((uint)mipLevels.Count);
        for (int index = 0; index < 11; index++)
            writer.Write(0u);

        writer.Write(PixelFormatSize);
        writer.Write(PixelFormatFlags);
        writer.Write(0u);
        writer.Write(32u);
        writer.Write(0x000000ffu);
        writer.Write(0x0000ff00u);
        writer.Write(0x00ff0000u);
        writer.Write(0xff000000u);

        writer.Write(caps);
        writer.Write(caps2);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);

        foreach (ImageSourceMipLevel mipLevel in mipLevels)
            writer.Write(mipLevel.RgbaBytes.Span);
    }

    private static ImageShape GetImageShape(
        GfxImageAsset asset,
        string assetName)
    {
        if (asset.MapType == MapType.TwoDimensional &&
            asset.DimensionCount == GfxImageDimension.TwoDimensional &&
            !asset.IsCubemap &&
            asset.Depth == 1)
        {
            return ImageShape.TwoDimensional;
        }
        if (asset.MapType == MapType.Cube &&
            asset.DimensionCount == GfxImageDimension.TwoDimensional &&
            asset.IsCubemap &&
            asset.Depth == 1)
        {
            return ImageShape.Cube;
        }
        if (asset.MapType == MapType.ThreeDimensional &&
            asset.DimensionCount == GfxImageDimension.ThreeDimensional &&
            !asset.IsCubemap)
        {
            return ImageShape.Volume;
        }

        throw new NotSupportedException(
            $"Image '{assetName}' has unsupported map shape " +
            $"{asset.MapType}/{asset.DimensionCount} " +
            $"(cubemap={asset.IsCubemap}, depth={asset.Depth}).");
    }

    private enum ImageShape
    {
        TwoDimensional,
        Cube,
        Volume
    }
}
