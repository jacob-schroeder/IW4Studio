using IW4.Game.Pointers;

namespace IW4.Game.Assets.StructuredData;

public sealed class StructuredDataEnum
{
    public const int SerializedSize = 0x0c;

    public int EntryCount { get; init; }
    public int ReservedEntryCount { get; init; }
    public XPointer<StructuredDataEnumEntry[]> EntriesPointer { get; init; }
    public IReadOnlyList<StructuredDataEnumEntry> Entries { get; set; } = [];
}
