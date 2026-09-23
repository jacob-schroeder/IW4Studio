using IW4.Game.Assets;
using IW4.Game.Pointers;

namespace IW4.Game.Assets.TechniqueSet;

public sealed class MaterialTechniqueAsset
{
    public const int SerializedSize = 0x08;

    public int Offset { get; init; }
    public XString NamePointer { get; init; }
    public string? Name { get; init; }
    public MaterialTechniqueFlags Flags { get; init; }
    public ushort PassCount { get; init; }
    public IReadOnlyList<MaterialPassAsset> Passes { get; init; } = [];
}
