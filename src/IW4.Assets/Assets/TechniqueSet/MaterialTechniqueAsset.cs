using IW4.Assets.Assets;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.TechniqueSet;

public sealed class MaterialTechniqueAsset
{
    public const int SerializedSize = 0x08;

    public int Offset { get; init; }
    // Materialized destination of the 0x08 technique root. Inline owners need
    // this identity because later technique-set slots may reuse the same root
    // through a packed direct pointer while retaining the original -1 source
    // sentinel in their semantic pointer record.
    public XBlockAddress? DestinationAddress { get; init; }
    public XString NamePointer { get; init; }
    public string? Name { get; init; }
    public ushort Flags { get; init; }
    public ushort PassCount { get; init; }
    public IReadOnlyList<MaterialPassAsset> Passes { get; init; } = [];
}
