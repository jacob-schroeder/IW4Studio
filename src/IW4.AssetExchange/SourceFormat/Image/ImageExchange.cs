using IW4.Assets.Assets.Image;

namespace IW4.AssetExchange.SourceFormat.Image;

/// <summary>
/// Writes a decoded PS3 IW4 image to the DDS source layout consumed by
/// OpenAssetTools. The source DDS uses an uncompressed RGBA8 top level so it
/// does not mislabel RSX texture bytes as a PC texture payload.
/// </summary>
public sealed class ImageExchange
{
    private const uint DdsMagic = 0x20534444;
    private const uint HeaderSize = 124;
    private const uint HeaderFlags = 0x0000100f;
    private const uint PixelFormatSize = 32;
    private const uint PixelFormatFlags = 0x00000041;
    private const uint TextureCaps = 0x00001000;

    public IReadOnlyList<string> Unlink(
        string sourceDirectory,
        GfxImageAsset asset,
        int width,
        int height,
        ReadOnlyMemory<byte> rgbaBytes)
    {
        ArgumentNullException.ThrowIfNull(asset);
        string assetName = SourceOutput.NormalizeOwnedAssetName(
            asset.Name,
            "Image");
        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException(
                $"Image '{assetName}' has invalid decoded dimensions {width}x{height}.");
        }
        if (asset.MapType != MapType.TwoDimensional ||
            asset.DimensionCount != GfxImageDimension.TwoDimensional ||
            asset.IsCubemap ||
            asset.Depth > 1)
        {
            throw new NotSupportedException(
                $"Image '{assetName}' is not a two-dimensional texture.");
        }

        if (asset.FormatEncoding.BaseFormat ==
            GfxImageBaseFormat.Y16X16Float)
        {
            throw new NotSupportedException(
                $"Image '{assetName}' uses a floating-point format that cannot be represented by the RGBA8 source DDS writer.");
        }
        if (width != asset.Width || height != asset.Height)
        {
            throw new InvalidDataException(
                $"Image '{assetName}' decoded only {width}x{height}; its authored top level is {asset.Width}x{asset.Height}.");
        }

        int expectedByteCount;
        try
        {
            expectedByteCount = checked(width * height * 4);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException(
                $"Image '{assetName}' decoded dimensions are too large.",
                exception);
        }
        if (rgbaBytes.Length != expectedByteCount)
        {
            throw new InvalidDataException(
                $"Image '{assetName}' has {rgbaBytes.Length} decoded RGBA bytes; " +
                $"expected {expectedByteCount} for {width}x{height}.");
        }

        string cleanName = assetName.Replace('*', '_');
        var output = new SourceOutput(sourceDirectory);
        return output.WriteBinaryBatch([
            ($"images/{cleanName}.dds", stream =>
                WriteDds(stream, width, height, rgbaBytes.Span))
        ]);
    }

    private static void WriteDds(
        Stream stream,
        int width,
        int height,
        ReadOnlySpan<byte> rgbaBytes)
    {
        using var writer = new BinaryWriter(
            stream,
            System.Text.Encoding.UTF8,
            leaveOpen: true);
        writer.Write(DdsMagic);
        writer.Write(HeaderSize);
        writer.Write(HeaderFlags);
        writer.Write((uint)height);
        writer.Write((uint)width);
        writer.Write(checked((uint)width * 4));
        writer.Write(0u);
        writer.Write(1u);
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

        writer.Write(TextureCaps);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(rgbaBytes);
    }
}
