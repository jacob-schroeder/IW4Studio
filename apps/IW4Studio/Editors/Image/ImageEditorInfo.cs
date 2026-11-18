using IW4.Game.Assets.Image;

namespace IW4.Studio.Desktop.Editors.Image;

/// <summary>Describes the whole texture, including higher levels in the image package.</summary>
public sealed class ImageEditorInfo
{
    internal ImageEditorInfo(GfxImageAsset image)
    {
        GfxImageStreamData[] parts = image.StreamData.Where(part => part.HasStreamingData).ToArray();
        var (width, height, levels) = ImageEditorPixels.GetSourceExtent(image);
        Dimensions = image.Depth > 1 ? $"{width} × {height} × {image.Depth}" : $"{width} × {height}";
        MipChain = $"{levels} {(levels == 1 ? "level" : "levels")}";
        Shape = image.MapType switch
        {
            MapType.TwoDimensional => "2D texture",
            MapType.ThreeDimensional => "Volume texture",
            MapType.Cube => "Cubemap",
            MapType.OneDimensional => "1D texture",
            _ => image.MapType.ToString()
        };
        Format = image.FormatEncoding.BaseFormat switch
        {
            GfxImageBaseFormat.CompressedDxt1 => "DXT1",
            GfxImageBaseFormat.CompressedDxt23 => "DXT3",
            GfxImageBaseFormat.CompressedDxt45 => "DXT5",
            var value => value.ToString()
        };
        Storage = parts.Length == 0
            ? image.PayloadByteCount > 0 ? "Inline" : "No pixel payload"
            : $"Streamed · {parts.Length} {(parts.Length == 1 ? "part" : "parts")}";
        NativeFormat = $"0x{image.Format:X2} · {image.FormatEncoding.BaseFormat}";
        ResidentHeader = $"{image.Width} × {image.Height} × {image.Depth} · {image.LevelCount} " +
            (image.LevelCount == 1 ? "mip" : "mips");
        InlinePayload = $"{image.PayloadByteCount:N0} B";
        Summary = $"{Shape}   ·   {Dimensions}   ·   {Format}";
    }

    public string Summary { get; }
    public string Shape { get; }
    public string Format { get; }
    public string Dimensions { get; }
    public string MipChain { get; }
    public string Storage { get; }
    public string NativeFormat { get; }
    public string ResidentHeader { get; }
    public string InlinePayload { get; }
}
