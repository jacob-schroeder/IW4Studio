using IW4.FastFiles.Loaders.Database;
using IW4.Assets.Assets.Leaderboard;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;
using IW4.Runtime.IO;

namespace IW4.FastFiles.Loaders.Assets.Leaderboard;

public sealed class LeaderboardDefLoader
{
    public LeaderboardDefAsset LoadFromAssetPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null)
            throw new InvalidDataException("Top-level LeaderboardDef pointer is null.");

        if (pointer.Type == PointerType.Offset)
        {
            context.PointerReader.ValidateOffsetPointerRange<LeaderboardDefAsset>(
                pointer,
                LeaderboardDefAsset.SerializedSize,
                "LeaderboardDef");
            LeaderboardDefAsset canonical = context.ResolveLeaderboardDef(pointer)
                ?? throw new InvalidDataException(
                    $"Top-level LeaderboardDef pointer 0x{unchecked((uint)pointer.Raw):X8} does not resolve to a canonical LeaderboardDef asset.");
            XBlockAddress pointerCellAddress = pointer.CellAddress
                ?? throw new InvalidDataException("Packed LeaderboardDef pointer has no destination cell.");
            int canonicalRaw = canonical.RuntimeAddress?.RawValue
                ?? throw new InvalidDataException("Canonical LeaderboardDef has no runtime address.");
            context.Blocks.WriteInt32(pointerCellAddress, canonicalRaw);
            return canonical;
        }

        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
        {
            throw new InvalidDataException(
                $"Top-level LeaderboardDef pointer 0x{unchecked((uint)pointer.Raw):X8} has unsupported type {pointer.Type}.");
        }

        XBlockAddress? insertCell = pointer.Type == PointerType.Insert
            ? context.Blocks.AllocateInsertPointerCell()
            : null;

        context.Blocks.Push(XFileBlockType.TEMP);
        try
        {
            XBlockAddress rootAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
            LeaderboardDefAsset leaderboard = ReadLeaderboardDef(cursor, rootAddress, context);
            XBlockAddress pointerCellAddress = pointer.CellAddress
                ?? throw new InvalidDataException("Inline LeaderboardDef pointer has no destination cell.");
            LeaderboardDefAsset canonical = context.DB_AddXAsset(leaderboard, pointerCellAddress);

            if (insertCell is { } cell)
            {
                int canonicalRaw = canonical.RuntimeAddress?.RawValue
                    ?? throw new InvalidDataException("Canonical LeaderboardDef has no runtime address.");
                context.Blocks.WriteInt32(cell, canonicalRaw);
            }

            return canonical;
        }
        finally
        {
            context.Blocks.Pop();
        }
    }

    // The root is staged in TEMP; its name, column table, and both strings in
    // each 0x20-byte column row materialize in LARGE.
    private static LeaderboardDefAsset ReadLeaderboardDef(
        FastFileCursor cursor,
        XBlockAddress expectedRootAddress,
        DbLoadExecutionContext context)
    {
        int sourceOffset = cursor.Offset;
        byte[] rootBytes = context.Blocks.Load(
            cursor,
            LeaderboardDefAsset.SerializedSize,
            out XBlockAddress rootAddress);
        if (rootAddress != expectedRootAddress)
        {
            throw new InvalidDataException(
                $"LeaderboardDef pointer patched to {expectedRootAddress}, but root loaded at {rootAddress}.");
        }

        var rootCursor = new FastFileCursor(rootBytes, rootAddress);
        XPointer<string> namePointer = ReadXStringPointer(rootCursor, context);
        int id = rootCursor.ReadInt32();
        int columnCount = rootCursor.ReadInt32();
        int xpColumnId = rootCursor.ReadInt32();
        int prestigeColumnId = rootCursor.ReadInt32();
        int columnsCellOffset = rootCursor.Offset;
        var columnsPointer = new XPointer<LbColumnDef[]>(
            rootCursor.ReadInt32(),
            XPointerResolutionMode.Direct,
            rootCursor.AddressAt(columnsCellOffset));

        if (rootCursor.Offset != LeaderboardDefAsset.SerializedSize)
        {
            throw new InvalidDataException(
                $"LeaderboardDef consumed 0x{rootCursor.Offset:X} bytes instead of 0x{LeaderboardDefAsset.SerializedSize:X}.");
        }
        if (columnCount < 0)
        {
            throw new InvalidDataException(
                $"LeaderboardDef at source 0x{sourceOffset:X} has negative columnCount {columnCount}.");
        }

        string? name;
        IReadOnlyList<LbColumnDef> columns;
        context.Blocks.Push(XFileBlockType.LARGE);
        try
        {
            name = context.PointerReader.LoadXString(cursor, namePointer);
            columns = ReadColumns(cursor, columnsPointer, columnCount, context);
        }
        finally
        {
            context.Blocks.Pop();
        }


        return new LeaderboardDefAsset
        {
            Offset = sourceOffset,
            RuntimeAddress = rootAddress,
            NamePointer = namePointer,
            Name = name,
            Id = id,
            ColumnCount = columnCount,
            XpColumnId = xpColumnId,
            PrestigeColumnId = prestigeColumnId,
            ColumnsPointer = columnsPointer,
            Columns = columns
        };
    }

    private static IReadOnlyList<LbColumnDef> ReadColumns(
        FastFileCursor cursor,
        XPointer<LbColumnDef[]> pointer,
        int count,
        DbLoadExecutionContext context)
    {
        // This presence field is deliberately not a generic packed pointer:
        // every nonzero source value owns bytes.
        if (pointer.Raw == 0)
            return [];

        int byteCount = checked(count * LbColumnDef.SerializedSize);
        if (byteCount > cursor.Remaining)
        {
            throw new EndOfStreamException(
                $"LeaderboardDef columns require 0x{byteCount:X} source bytes, but only 0x{cursor.Remaining:X} remain.");
        }

        context.Blocks.AlignCurrent(4);
        XBlockAddress columnsAddress = context.Blocks.CurrentAddress;
        XBlockAddress columnsCellAddress = pointer.CellAddress
            ?? throw new InvalidDataException("LeaderboardDef columns pointer has no destination cell.");
        context.Blocks.WriteInt32(columnsCellAddress, XPointerCodec.Encode(columnsAddress));
        byte[] columnBytes = context.Blocks.Load(cursor, byteCount, out XBlockAddress loadedAddress);
        if (loadedAddress != columnsAddress)
        {
            throw new InvalidDataException(
                $"LeaderboardDef columns pointer patched to {columnsAddress}, but array loaded at {loadedAddress}.");
        }

        var columnCursor = new FastFileCursor(columnBytes, columnsAddress);
        var columns = new LbColumnDef[count];
        for (int index = 0; index < columns.Length; index++)
        {
            int rowStart = columnCursor.Offset;
            XPointer<string> columnNamePointer = ReadXStringPointer(columnCursor, context);
            int id = columnCursor.ReadInt32();
            int propertyId = columnCursor.ReadInt32();
            byte hiddenRaw = columnCursor.ReadByte();
            byte[] pad0DTo0F = columnCursor.ReadBytes(3);
            XPointer<string> statNamePointer = ReadXStringPointer(columnCursor, context);
            var type = (LbColType)columnCursor.ReadInt32();
            int precision = columnCursor.ReadInt32();
            var aggregation = (LbAggType)columnCursor.ReadInt32();

            if (columnCursor.Offset - rowStart != LbColumnDef.SerializedSize)
            {
                throw new InvalidDataException(
                    $"LbColumnDef[{index}] consumed 0x{columnCursor.Offset - rowStart:X} bytes instead of 0x{LbColumnDef.SerializedSize:X}.");
            }

            string? columnName = context.PointerReader.LoadXString(cursor, columnNamePointer);
            string? statName = context.PointerReader.LoadXString(cursor, statNamePointer);
            columns[index] = new LbColumnDef
            {
                NamePointer = columnNamePointer,
                Name = columnName,
                Id = id,
                PropertyId = propertyId,
                HiddenRaw = hiddenRaw,
                Pad0DTo0F = pad0DTo0F,
                StatNamePointer = statNamePointer,
                StatName = statName,
                Type = type,
                Precision = precision,
                Aggregation = aggregation
            };
        }

        return columns;
    }

    private static XPointer<string> ReadXStringPointer(
        FastFileCursor cursor,
        DbLoadExecutionContext context)
    {
        return context.PointerReader.ReadPointer<string>(cursor, XPointerResolutionMode.Direct);
    }
}
