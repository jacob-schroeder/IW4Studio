using IW4.FastFiles.Loaders.Database;
using IW4.Assets.Assets.StringTable;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;
using IW4.Runtime.IO;

namespace IW4.FastFiles.Loaders.Assets.StringTable;

public sealed class StringTableLoader
{
    public StringTableAsset LoadFromAssetPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null)
            throw new InvalidDataException("Top-level StringTable pointer is null.");

        if (pointer.Type == PointerType.Offset)
        {
            context.PointerReader.ValidateOffsetPointerRange<StringTableAsset>(
                pointer,
                StringTableAsset.SerializedSize,
                "StringTable");
            StringTableAsset canonical = context.ResolveStringTable(pointer)
                ?? throw new InvalidDataException(
                    $"Top-level StringTable pointer 0x{unchecked((uint)pointer.Raw):X8} does not resolve to a canonical StringTable asset.");
            XBlockAddress pointerCellAddress = pointer.CellAddress
                ?? throw new InvalidDataException("Packed StringTable pointer has no destination cell.");
            int canonicalRaw = canonical.RuntimeAddress?.RawValue
                ?? throw new InvalidDataException("Canonical StringTable has no runtime address.");
            context.Blocks.WriteInt32(pointerCellAddress, canonicalRaw);
            return canonical;
        }

        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
            throw new InvalidDataException(
                $"Top-level StringTable pointer 0x{unchecked((uint)pointer.Raw):X8} has unsupported type {pointer.Type}.");

        ProviderRegistrationOccurrence providerRegistration = context.BeginProviderRegistration(pointer);

        context.Blocks.Push(XFileBlockType.TEMP);
        try
        {
            XBlockAddress rootAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
            StringTableAsset stringTable = ReadStringTable(cursor, rootAddress, context);
            StringTableAsset canonical = context.DB_AddXAsset(stringTable, providerRegistration);

            return canonical;
        }
        finally
        {
            context.Blocks.Pop();
        }
    }

    // The root is staged in TEMP; its name, cell array, and cell strings are
    // materialized in LARGE before registration.
    private static StringTableAsset ReadStringTable(
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
