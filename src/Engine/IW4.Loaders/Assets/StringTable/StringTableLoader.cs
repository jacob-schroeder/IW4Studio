using IW4.Loaders.Database;
using IW4.Game.Assets.StringTable;
using IW4.Game.Pointers;
using IW4.Game.Zone;
using IW4.Runtime.Database;
using IW4.Game.IO;

namespace IW4.Loaders.Assets.StringTable;

public sealed class StringTableLoader : XAssetLoader<StringTableAsset>
{
    public StringTableLoader() : base(XAssetType.StringTable, StringTableAsset.SerializedSize, "StringTable")
    {
    }

    protected override StringTableAsset RegisterAsset(
        StringTableAsset asset,
        ProviderRegistrationOccurrence providerRegistration,
        DbLoadExecutionContext context)
    {
        return context.DB_AddXAsset(asset, providerRegistration);
    }

    // The root is staged in TEMP; its name, cell array, and cell strings are
    // materialized in LARGE before registration.
    protected override StringTableAsset ReadBody(
        FastFileCursor cursor,
        XBlockAddress rootAddress,
        DbLoadExecutionContext context)
    {
        int offset = cursor.Offset;
        byte[] rootBytes = context.Blocks.Load(cursor, StringTableAsset.SerializedSize, out XBlockAddress loadedAddress);
        if (loadedAddress != rootAddress)
            throw new InvalidDataException($"StringTable pointer patched to {rootAddress}, but Load_Stream wrote its root at {loadedAddress}.");
        var rootCursor = new FastFileCursor(rootBytes, rootAddress);

        XPointer<string> namePointer = ReadXStringPointer(rootCursor, context);
        int columnCount = rootCursor.ReadInt32();
        int rowCount = rootCursor.ReadInt32();
        XPointer<StringTableCell[]> cellsPointer = context.PointerReader.ReadPointer<StringTableCell[]>(
            rootCursor,
            XPointerResolutionMode.Direct);

        if (rootCursor.Offset != StringTableAsset.SerializedSize)
            throw new InvalidDataException($"StringTable consumed 0x{rootCursor.Offset:X} bytes instead of 0x{StringTableAsset.SerializedSize:X}.");

        if (columnCount < 0 || rowCount < 0 || (long)columnCount * rowCount > 0x100000)
        {
            throw new InvalidDataException(
                $"StringTable at source 0x{offset:X} has invalid dimensions {columnCount}x{rowCount}; " +
                $"name=0x{namePointer.Raw:X8}, cells=0x{cellsPointer.Raw:X8}.");
        }


        string? name;
        IReadOnlyList<StringTableCell> cells;
        context.Blocks.Push(XFileBlockType.LARGE);
        try
        {
            name = context.PointerReader.LoadXString(cursor, namePointer);
            cells = ReadCells(cursor, cellsPointer.Untyped, checked(columnCount * rowCount), context);
        }
        finally
        {
            context.Blocks.Pop();
        }

        return new StringTableAsset
        {
            Offset = offset,
            RuntimeAddress = rootAddress,
            NamePointer = namePointer,
            Name = name,
            ColumnCount = columnCount,
            RowCount = rowCount,
            CellsPointer = cellsPointer,
            Cells = cells
        };
    }

    private static IReadOnlyList<StringTableCell> ReadCells(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        if (count < 0)
            throw new InvalidDataException($"Invalid negative StringTable cell count {count}.");

        if (pointer.Raw == 0)
            return [];

        context.Blocks.AlignCurrent(4);
        XBlockAddress pointerCellAddress = pointer.CellAddress
            ?? throw new InvalidDataException("StringTable cells pointer has no destination cell.");
        XBlockAddress cellsAddress = context.Blocks.CurrentAddress;
        context.Blocks.WriteInt32(pointerCellAddress, XPointerCodec.Encode(cellsAddress));
        byte[] cellBytes = context.Blocks.Load(cursor, checked(count * StringTableCell.SerializedSize), out XBlockAddress loadedAddress);
        if (loadedAddress != cellsAddress)
            throw new InvalidDataException($"StringTable cells pointer patched to {cellsAddress}, but Load_Stream wrote the array at {loadedAddress}.");
        var cellCursor = new FastFileCursor(cellBytes, cellsAddress);
        var cells = new StringTableCell[count];

        for (int i = 0; i < cells.Length; i++)
        {
            int rowStart = cellCursor.Offset;
            XPointer<string> stringPointer = ReadXStringPointer(cellCursor, context);
            int hash = cellCursor.ReadInt32();

            if (cellCursor.Offset - rowStart != StringTableCell.SerializedSize)
                throw new InvalidDataException($"StringTableCell consumed 0x{cellCursor.Offset - rowStart:X} bytes instead of 0x{StringTableCell.SerializedSize:X}.");

            string? value = context.PointerReader.LoadXString(cursor, stringPointer);
            cells[i] = new StringTableCell
            {
                StringPointer = stringPointer,
                String = value,
                Hash = hash
            };
        }

        return cells;
    }

    private static XPointer<string> ReadXStringPointer(
        FastFileCursor cursor,
        DbLoadExecutionContext context)
    {
        return context.PointerReader.ReadPointer<string>(cursor, XPointerResolutionMode.Direct);
    }
}
