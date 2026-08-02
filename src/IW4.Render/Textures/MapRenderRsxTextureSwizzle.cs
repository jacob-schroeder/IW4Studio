namespace IW4.Render.Textures;

/// <summary>
/// A host RGBA texture-channel selector after decoding the RSX A-R-G-B
/// SET_TEXTURE_CONTROL1 remap table.
/// </summary>
public enum MapRenderRsxTextureSwizzleSource
{
    Zero,
    One,
    Red,
    Green,
    Blue,
    Alpha
}

public readonly record struct MapRenderRsxTextureSwizzle(
    MapRenderRsxTextureSwizzleSource Red,
    MapRenderRsxTextureSwizzleSource Green,
    MapRenderRsxTextureSwizzleSource Blue,
    MapRenderRsxTextureSwizzleSource Alpha)
{
    public string CacheIdentity => $"{Red},{Green},{Blue},{Alpha}";
}
