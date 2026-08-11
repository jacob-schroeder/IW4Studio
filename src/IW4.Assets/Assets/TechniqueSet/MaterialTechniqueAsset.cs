using IW4.Assets.Assets;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.TechniqueSet;

public sealed class MaterialTechniqueAsset
{
    public const int SerializedSize = 0x08;

    public int Offset { get; init; }
    public XString NamePointer { get; init; }
    public string? Name { get; init; }
    public ushort Flags { get; init; }
    public ushort PassCount { get; init; }
    public IReadOnlyList<MaterialPassAsset> Passes { get; init; } = [];
}
