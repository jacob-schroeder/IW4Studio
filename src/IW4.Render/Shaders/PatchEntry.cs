using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;

namespace IW4.Render.Shaders;

internal readonly record struct PatchEntry(
    uint DefaultConstantOffset,
    IReadOnlyList<ushort> PatchOffsets);
