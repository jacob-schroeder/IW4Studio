namespace IW4.Assets.Codecs.Image;

internal static class GfxImageChannelCodec
{
    internal static void RgbaToArgb(Span<byte> pixels)
    {
        if (pixels.Length % 4 != 0)
            throw new ArgumentException("RGBA pixels require complete four-byte groups.", nameof(pixels));
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            (pixels[offset], pixels[offset + 1], pixels[offset + 2], pixels[offset + 3]) =
                (pixels[offset + 3], pixels[offset], pixels[offset + 1], pixels[offset + 2]);
        }
    }

    internal static void BgraToRgba(Span<byte> pixels)
    {
        if (pixels.Length % 4 != 0)
            throw new ArgumentException("BGRA pixels require complete four-byte groups.", nameof(pixels));
        for (int offset = 0; offset < pixels.Length; offset += 4)
            (pixels[offset], pixels[offset + 2]) = (pixels[offset + 2], pixels[offset]);
    }
}
