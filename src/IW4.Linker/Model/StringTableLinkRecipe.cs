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

    private readonly byte[]?[] _cellValues;
    private readonly int[] _cellHashes;

    private StringTableLinkRecipe(
        AssetKey key,
        string originalSerializedName,
        int columnCount,
        int rowCount,
        byte[]?[] cellValues,
        int[] cellHashes,
        bool requireReferencePlaceholder)
        : base(
            key,
            originalSerializedName,
            requireReferencePlaceholder)
    {
        ColumnCount = columnCount;
        RowCount = rowCount;
        _cellValues = cellValues;
        _cellHashes = cellHashes;
    }

    private int ColumnCount { get; }
    private int RowCount { get; }

    public static StringTableLinkRecipe Freeze(
        AssetKey key,
        string originalSerializedName,
        StringTableAsset definition)
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

            return CreateReference(key, originalSerializedName);
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

        var values = new byte[]?[cellCount];
        var hashes = new int[cellCount];
        for (int index = 0; index < cellCount; index++)
        {
            StringTableCell cell = cells[index]
                ?? throw new InvalidDataException(
                    $"StringTable.Cells[{index}] cannot be null.");
            values[index] = FreezeOptionalXString(
                cell.String,
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
            requireReferencePlaceholder: false);
    }

    public static StringTableLinkRecipe CreateExternal(
        AssetKey key,
        string originalSerializedName) =>
        CreateReference(key, originalSerializedName);

    public override void Emit(
        ZoneEmissionWriter output,
        Action<AssetDependency, XBlockAddress, int> emitDependency)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(emitDependency);

        output.PushTempScope();
        try
        {
            output.Allocate(
                XFileBlockType.TEMP,
                StringTableAsset.SerializedSize,
                alignment: 4);
            output.WriteInt32(-1);
            output.WriteInt32(ColumnCount);
            output.WriteInt32(RowCount);
            output.WriteInt32(_cellValues.Length == 0 ? 0 : -1);

            EmitName(output);
            if (_cellValues.Length == 0)
                return;

            output.Allocate(
                XFileBlockType.LARGE,
                checked(_cellValues.Length * StringTableCell.SerializedSize),
                alignment: 4);
            for (int index = 0; index < _cellValues.Length; index++)
            {
                output.WriteInt32(XStringSourcePointer(_cellValues[index]));
                output.WriteInt32(_cellHashes[index]);
            }

            foreach (byte[]? value in _cellValues)
                EmitFrozenXString(output, value);
        }
        finally
        {
            output.PopTempScope();
        }
    }

    private static StringTableLinkRecipe CreateReference(
        AssetKey key,
        string originalSerializedName) =>
        new(
            key,
            originalSerializedName,
            columnCount: 0,
            rowCount: 0,
            cellValues: [],
            cellHashes: [],
            requireReferencePlaceholder: true);
}
