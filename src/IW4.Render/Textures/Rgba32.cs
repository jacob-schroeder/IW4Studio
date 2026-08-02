using System.Buffers.Binary;
using System.IO.Compression;
using IW4.Assets.Assets.Image;

namespace IW4.Render.Textures;

internal readonly record struct Rgba32(byte R, byte G, byte B, byte A);
