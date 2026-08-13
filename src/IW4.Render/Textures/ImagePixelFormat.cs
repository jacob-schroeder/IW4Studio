using System.Buffers.Binary;
using System.IO.Compression;
using IW4.Assets.Assets.Image;

namespace IW4.Render.Textures;

internal enum ImagePixelFormat
{
    Unknown,
    Bgra32,
    Bgrx32,
    Drgb32,
    Rg16Float,
    G8B8,
    Rgb565,
    A1Rgb555,
    Argb4444,
    Alpha8,
    Luminance8,
    AlphaLuminance8,
    Bc1,
    Bc2,
    Bc3
}
