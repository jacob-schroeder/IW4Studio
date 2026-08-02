using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.StringTable;

public sealed class StringTableAsset : BaseAsset
{
    public const int SerializedSize = 0x10;

    // 0x00: XString asset name.
    public XString NamePointer { get; init; }
    public string? Name { get; init; }

    // 0x04: column count used with RowCount to derive the cell count.
    public int ColumnCount { get; init; }

    // 0x08: row count used with ColumnCount to derive the cell count.
    public int RowCount { get; init; }

    // 0x0C: presence-controlled StringTableCell array materialized in LARGE.
    public XPointer<StringTableCell[]> CellsPointer { get; init; }
    public IReadOnlyList<StringTableCell> Cells { get; init; } = [];
    public int CellCount => checked(ColumnCount * RowCount);
}
