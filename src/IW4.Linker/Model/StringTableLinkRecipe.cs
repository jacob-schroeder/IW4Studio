using IW4.Assets.Assets.StringTable;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.Model;

/// <summary>
/// Frozen StringTable recipe. The complete row-major cell table is emitted
/// before any cell XString bodies, matching the native counted-array walk.
/// </summary>
internal sealed class StringTableLinkRecipe : AssetLinkRecipe
{
    private const int MaximumCellCount = 0x100000;

    private StringTableLinkRecipe(
        AssetKey key,
        string originalSerializedName,
        int columnCount,
        int rowCount,
        LinkStorageSymbol?[] cellValues,
        int[] cellHashes,
        LinkAssetFreezeScope freeze)
        : base(
            key,
            originalSerializedName,
            freeze.FreezeProviderName(originalSerializedName, 0, "Asset.Name"))
    {
        LinkStorageSymbol? cells = cellValues.Length == 0
            ? null
            : CreateCellStorage(cellValues, cellHashes);
        var writer = new LinkTemplateWriter(StringTableAsset.SerializedSize);
        writer.Skip(sizeof(int));
        writer.WriteInt32(columnCount);
        writer.WriteInt32(rowCount);
        writer.Skip(sizeof(int));
        Root = LinkStorageSymbol.SourceBytes(
            XFileBlockType.TEMP,
            writer.Complete(),
            alignment: 4,
            root => cells is null
                ? [NameOperation(root, 0)]
                : [
                    NameOperation(root, 0),
                    PresenceOperation(root, 0x0c, cells, "StringTable.Cells")
                ]);
    }

    internal override LinkStorageSymbol Root { get; }

    public static AssetLinkRecipe Freeze(
        AssetKey key,
        string originalSerializedName,
        StringTableAsset definition,
        LinkAssetFreezeScope freeze)
    {
        ArgumentNullException.ThrowIfNull(definition);
        IReadOnlyList<StringTableCell> cells = definition.Cells
            ?? throw new InvalidDataException("StringTable cells cannot be null.");
        if (originalSerializedName.StartsWith(','))
        {
            if (definition.ColumnCount != 0 ||
                definition.RowCount != 0 ||
                cells.Count != 0)
            {
                throw new InvalidDataException(
                    "A comma-prefixed StringTable provider must have zero dimensions and cells.");
            }

            return ExternalAssetLinkRecipe.Create(
                key,
                XAssetType.StringTable,
                originalSerializedName,
                freeze);
        }
        if (definition.ColumnCount < 0 || definition.RowCount < 0)
        {
            throw new InvalidDataException(
                "StringTable row and column counts cannot be negative.");
        }

        long wideCellCount = (long)definition.ColumnCount * definition.RowCount;
        if (wideCellCount > MaximumCellCount)
        {
            throw new InvalidDataException(
                $"StringTable dimensions require {wideCellCount} cells; " +
                $"the loader supports at most {MaximumCellCount}.");
        }

        int cellCount = checked((int)wideCellCount);
        if (cells.Count != cellCount)
        {
            throw new InvalidDataException(
                $"StringTable contains {cells.Count} cell(s); " +
                $"its {definition.RowCount}x{definition.ColumnCount} dimensions require {cellCount}.");
        }

        var values = new LinkStorageSymbol?[cellCount];
        var hashes = new int[cellCount];
        for (int index = 0; index < cellCount; index++)
        {
            StringTableCell cell = cells[index]
                ?? throw new InvalidDataException(
                    $"StringTable.Cells[{index}] cannot be null.");
            values[index] = freeze.FreezeOptionalXString(
                cell.String,
                cell.StringPointer.Untyped,
                $"StringTable.Cells[{index}].String");
            hashes[index] = cell.Hash;
        }

        return new StringTableLinkRecipe(
            key,
            originalSerializedName,
            definition.ColumnCount,
            definition.RowCount,
            values,
            hashes,
            freeze);
    }

    private static LinkStorageSymbol CreateCellStorage(
        IReadOnlyList<LinkStorageSymbol?> values,
        IReadOnlyList<int> hashes)
    {
        var writer = new LinkTemplateWriter(
            checked(values.Count * StringTableCell.SerializedSize));
        for (int index = 0; index < values.Count; index++)
        {
            writer.Skip(sizeof(int));
            writer.WriteInt32(hashes[index]);
        }

        return LinkStorageSymbol.SourceBytes(
            XFileBlockType.LARGE,
            writer.Complete(),
            alignment: 4,
            cells => values
                .Select((value, index) => (value, index))
                .Where(item => item.value is not null)
                .Select(item => XStringOperation(
                    cells,
                    checked(item.index * StringTableCell.SerializedSize),
                    item.value!,
                    $"StringTable.Cells[{item.index}].String")));
    }
}
