using IW4.Game.Pointers;

namespace IW4.Game.Assets.StructuredData;

public sealed class StructuredDataStructProperty
{
    public const int SerializedSize = 0x10;

    public XString NamePointer { get; init; }
    public string? Name { get; init; }
    public StructuredDataType Type { get; init; } = new();
    public uint Offset { get; init; }
}
