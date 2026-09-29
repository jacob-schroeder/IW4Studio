namespace IW4.Render.Textures;

/// <summary>
/// A host RGBA texture-channel selector after decoding the RSX A-R-G-B
/// SET_TEXTURE_CONTROL1 remap table.
/// </summary>
public enum RsxTextureSwizzleSource
{
    Zero,
    One,
    Red,
    Green,
    Blue,
    Alpha
}

public readonly record struct RsxTextureSwizzle(
    RsxTextureSwizzleSource Red,
    RsxTextureSwizzleSource Green,
    RsxTextureSwizzleSource Blue,
    RsxTextureSwizzleSource Alpha)
{
    public string CacheIdentity => $"{Red},{Green},{Blue},{Alpha}";

    /// <summary>Applies the native channel selection to decoded RGBA8 pixels in place.</summary>
    public void ApplyToRgba(Span<byte> pixels)
    {
        if (pixels.Length % 4 != 0)
            throw new ArgumentException("RGBA pixels must contain complete four-byte pixels.", nameof(pixels));
        if (Red == RsxTextureSwizzleSource.Red && Green == RsxTextureSwizzleSource.Green &&
            Blue == RsxTextureSwizzleSource.Blue && Alpha == RsxTextureSwizzleSource.Alpha)
            return;

        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            byte red = pixels[offset], green = pixels[offset + 1], blue = pixels[offset + 2], alpha = pixels[offset + 3];
            pixels[offset] = Sample(Red);
            pixels[offset + 1] = Sample(Green);
            pixels[offset + 2] = Sample(Blue);
            pixels[offset + 3] = Sample(Alpha);

            byte Sample(RsxTextureSwizzleSource source) => source switch
            {
                RsxTextureSwizzleSource.Zero => 0,
                RsxTextureSwizzleSource.One => byte.MaxValue,
                RsxTextureSwizzleSource.Red => red,
                RsxTextureSwizzleSource.Green => green,
                RsxTextureSwizzleSource.Blue => blue,
                RsxTextureSwizzleSource.Alpha => alpha,
                _ => throw new ArgumentOutOfRangeException(nameof(source))
            };
        }
    }
}
