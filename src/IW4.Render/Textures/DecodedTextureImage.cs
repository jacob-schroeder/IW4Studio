namespace IW4.Render.Textures;

internal readonly record struct DecodedTextureImage(
    string Name,
    int Width,
    int Height,
    string Format,
    DecodedTexturePixelFormat PixelFormat,
    bool HasTransparency,
    byte[] PixelBytes);
