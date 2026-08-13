namespace IW4.Render.Textures;

/// <summary>
/// Canonical host-side layout of a decoded texture payload.
/// </summary>
public enum DecodedTexturePixelFormat : byte
{
    Rgba8Unorm = 0,
    Rg16Float = 1
}
