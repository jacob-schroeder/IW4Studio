using System.Buffers.Binary;
using System.IO.Compression;
using IW4.Assets.Assets.Image;

namespace IW4.Render.Textures;

internal readonly record struct DecodedRgbaGfxCube(
    IReadOnlyList<IReadOnlyList<DecodedRgbaGfxImage>> Faces);
