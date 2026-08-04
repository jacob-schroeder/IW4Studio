using System.Buffers.Binary;
using System.IO.Compression;
using IW4.Assets.Assets.Image;

namespace IW4.Render.Textures;

internal readonly record struct DecodedGfxImage(
    string Name,
    int Width,
    int Height,
    string Format,
    bool HasTransparency,
    byte[] RgbaBytes,
    byte[] PngBytes);
