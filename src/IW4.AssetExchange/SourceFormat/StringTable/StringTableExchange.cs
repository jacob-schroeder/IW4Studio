using IW4.Assets.Assets.StringTable;

namespace IW4.AssetExchange.SourceFormat.StringTable;

/// <summary>
/// Writes an IW4 StringTable using the OpenAssetTools CSV convention.
/// Serialized cell hashes are derived data and are intentionally omitted.
/// </summary>
public sealed class StringTableExchange
{
    public IReadOnlyList<string> Unlink(
        string sourceDirectory,
        StringTableAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        string assetName = SourceOutput.NormalizeOwnedAssetName(
            asset.Name,
            "StringTable");
        Validate(asset, assetName);

        return new SourceOutput(sourceDirectory).WriteTextBatch([
            (assetName, writer => WriteCsv(writer, asset))
        ]);
    }

    private static void Validate(
        StringTableAsset asset,
        string assetName)
    {
        if (asset.RowCount < 0 || asset.ColumnCount < 0)
        {
            throw new InvalidDataException(
                $"StringTable '{assetName}' has negative dimensions " +
                $"{asset.RowCount}x{asset.ColumnCount}.");
        }

        int expectedCellCount;
        try
        {
            expectedCellCount = checked(asset.RowCount * asset.ColumnCount);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException(
                $"StringTable '{assetName}' dimensions overflow its cell count.",
                exception);
        }
        if (asset.Cells.Count != expectedCellCount)
        {
            throw new InvalidDataException(
                $"StringTable '{assetName}' has {asset.Cells.Count} cells; " +
                $"expected {expectedCellCount}.");
        }

        for (int index = 0; index < asset.Cells.Count; index++)
        {
            StringTableCell cell = asset.Cells[index] ??
                throw new InvalidDataException(
                    $"StringTable '{assetName}' cell {index} is null.");
            if (cell.String?.Contains('\0') == true)
            {
                throw new InvalidDataException(
                    $"StringTable '{assetName}' cell {index} contains an embedded null.");
            }
        }
    }

    private static void WriteCsv(
        TextWriter writer,
        StringTableAsset asset)
    {
        for (int row = 0; row < asset.RowCount; row++)
        {
            for (int column = 0; column < asset.ColumnCount; column++)
            {
                if (column != 0)
                    writer.Write(',');
                string value = asset.Cells[
                    checked(column + row * asset.ColumnCount)].String ??
                    string.Empty;
                WriteCsvColumn(writer, value);
            }

            writer.WriteLine();
        }
    }

    private static void WriteCsvColumn(
        TextWriter writer,
        string value)
    {
        bool containsQuote = value.Contains('"');
        bool requiresQuotes = containsQuote ||
            value.Contains(',') ||
            value.Contains('\n');
        if (!requiresQuotes)
        {
            writer.Write(value);
            return;
        }

        writer.Write('"');
        if (containsQuote)
            writer.Write(value.Replace("\"", "\"\"", StringComparison.Ordinal));
        else
            writer.Write(value);
        writer.Write('"');
    }
}
