using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.TechniqueSet;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.Material;

public sealed class GfxStateBits
{
    public const int SerializedSize = 0x08;

    public XPointerReference LoadBitsPointer { get; init; }
    public IReadOnlyList<uint> LoadBits { get; init; } = [];

    /// <summary>
    /// Number of compiled RSX command words, excluding the appended return
    /// command.
    /// </summary>
    public uint CommandWordCount { get; init; }
}
