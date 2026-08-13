
namespace IW4.Render.Textures;

public sealed record TextureMip(
    int Width,
    int Height,
    byte[] PixelBytes);
