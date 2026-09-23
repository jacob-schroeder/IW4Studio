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
}
