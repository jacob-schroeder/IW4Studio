
namespace IW4.Render.Textures;

public sealed record TextureCubeFace(
    byte[] RgbaBytes,
    IReadOnlyList<TextureMip> MipLevels);
