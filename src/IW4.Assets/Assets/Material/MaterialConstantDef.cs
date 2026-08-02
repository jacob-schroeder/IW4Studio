using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.TechniqueSet;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.Material;

public sealed class MaterialConstantDef
{
    public const int SerializedSize = 0x20;

    public uint NameHash { get; init; }
    public IReadOnlyList<byte> NameBytes { get; init; } = [];
    public MaterialVec4 Literal { get; init; }
}
