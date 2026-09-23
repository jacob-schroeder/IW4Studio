using IW4.Game.Pointers;

namespace IW4.Game.Assets.StructuredData;

public sealed class StructuredDataStruct
{
    public const int SerializedSize = 0x10;

    public int PropertyCount { get; init; }
    public XPointer<StructuredDataStructProperty[]> PropertiesPointer { get; init; }
    public int Size { get; init; }
    public uint BitOffset { get; init; }
    public IReadOnlyList<StructuredDataStructProperty> Properties { get; set; } = [];
}
