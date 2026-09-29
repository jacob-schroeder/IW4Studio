using System.Text;
using IW4.Game.Assets.StringTable;

namespace IW4.Formats.SourceFormat.StringTable;

/// <summary>
/// Reads and writes an IW4 StringTable using the OpenAssetTools CSV convention.
/// Serialized cell hashes are derived data and are intentionally omitted.
/// </summary>
public sealed class StringTableExchange
{
    /// <summary>
    /// Imports CSV values, treating every record as data. CSV has no stored
    /// hashes, so imported cells start with zero hashes like new editor cells.
    /// </summary>
    public StringTableAsset ReadCsv(TextReader reader, string? assetName)
    {
        ArgumentNullException.ThrowIfNull(reader);

        List<List<string>> rows = [];
        List<string> fields = [];
        StringBuilder field = new();
        int columnCount = 0;
        int line = 1;
        int record = 1;
        bool fieldStarted = false;
        bool inQuotes = false;
        bool afterQuote = false;

        void CompleteField()
        {
            fields.Add(field.ToString());
            field.Clear();
            fieldStarted = false;
            afterQuote = false;
        }

        void CompleteRecord()
        {
            CompleteField();
            columnCount = Math.Max(columnCount, fields.Count);
            rows.Add(fields);
            fields = [];
            record++;
        }

        int next;
        while ((next = reader.Read()) != -1)
        {
            char character = (char)next;
            if (character == '\0')
                throw new InvalidDataException($"CSV record {record}, line {line} contains an embedded null.");

            if (character is '\r' or '\n')
            {
                bool hasLf = character == '\r' && reader.Peek() == '\n';
                if (hasLf)
                    reader.Read();
                if (inQuotes)
                {
                    field.Append(character);
                    if (hasLf)
                        field.Append('\n');
                }
                else
                {
                    CompleteRecord();
                }
                line++;
                continue;
            }

            if (inQuotes)
            {
                if (character == '"')
                {
                    if (reader.Peek() == '"')
                    {
                        reader.Read();
                        field.Append('"');
                    }
                    else
                    {
                        inQuotes = false;
                        afterQuote = true;
                    }
                }
                else
                {
                    field.Append(character);
                }
                continue;
            }

            if (character == ',')
            {
                CompleteField();
                continue;
            }
            if (afterQuote)
                throw new InvalidDataException($"CSV record {record}, line {line} has characters after a closing quote.");
            if (character == '"')
            {
                if (fieldStarted)
                    throw new InvalidDataException($"CSV record {record}, line {line} has a quote in an unquoted field.");
                inQuotes = true;
            }
            else
            {
                field.Append(character);
            }
            fieldStarted = true;
        }

        if (inQuotes)
            throw new InvalidDataException($"CSV record {record}, line {line} has an unterminated quoted field.");
        if (fields.Count != 0 || fieldStarted)
            CompleteRecord();
        if (rows.Count == 0)
            throw new InvalidDataException("CSV file contains no rows.");

        int cellCount = checked(rows.Count * columnCount);
        StringTableCell[] cells = new StringTableCell[cellCount];
        int index = 0;
        foreach (List<string> row in rows)
        {
            for (int column = 0; column < columnCount; column++)
            {
                cells[index++] = new StringTableCell
                {
                    String = column < row.Count ? row[column] : string.Empty,
                    Hash = 0
                };
            }
        }

        return new StringTableAsset
        {
            Name = assetName,
            RowCount = rows.Count,
            ColumnCount = columnCount,
            Cells = cells
        };
    }

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
            value.Contains('\n') ||
            value.Contains('\r');
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
