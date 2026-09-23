using IW4.Game.Pointers;

namespace IW4.Game.Assets.StringTable;

public sealed class StringTableCell
{
    public const int SerializedSize = 0x08;

    // 0x00: XString resolved after the complete cell table has been copied.
    public XString StringPointer { get; init; }
    public string? String { get; init; }

    // 0x04: copied int32 hash.
    public int Hash { get; init; }
}
