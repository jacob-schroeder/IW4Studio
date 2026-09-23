using IW4.Game.Assets.Image;
using IW4.Game.Assets.TechniqueSet;
using IW4.Game.Pointers;
using IW4.Game.Zone;

namespace IW4.Game.Assets.Material;

public sealed class MaterialConstantDef
{
    public const int SerializedSize = 0x20;

    public uint NameHash { get; init; }
    public IReadOnlyList<byte> NameBytes { get; init; } = [];
    public MaterialVec4 Literal { get; init; }
}
