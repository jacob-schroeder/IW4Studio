using IW4.Game.Pointers;

namespace IW4.Game.Assets.StructuredData;

public sealed class StructuredDataEnumEntry
{
    public const int SerializedSize = 0x08;

    public XString StringPointer { get; init; }
    public string? String { get; init; }
    public ushort Index { get; init; }
    public ushort Padding { get; init; }
}
