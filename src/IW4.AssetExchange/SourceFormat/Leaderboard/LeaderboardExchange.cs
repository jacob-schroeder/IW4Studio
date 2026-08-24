using System.Text.Encodings.Web;
using System.Text.Json;
using IW4.Assets.Assets.Leaderboard;

namespace IW4.AssetExchange.SourceFormat.Leaderboard;

/// <summary>Writes IW4 leaderboard definitions in the OpenAssetTools JSON format.</summary>
public sealed class LeaderboardExchange
{
    private static readonly JsonWriterOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = true,
        IndentCharacter = ' ',
        IndentSize = 4
    };

    public IReadOnlyList<string> Unlink(
        string sourceDirectory,
        LeaderboardDefAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        string assetName = SourceOutput.NormalizeOwnedAssetName(
            asset.Name,
            "LeaderboardDef");
        Validate(asset, assetName);

        return new SourceOutput(sourceDirectory).WriteBinaryBatch([
            (
                $"leaderboards/{assetName}.json",
                stream => WriteJson(stream, asset))
        ]);
    }

    private static void Validate(
        LeaderboardDefAsset asset,
        string assetName)
    {
        if (asset.ColumnCount < 0 || asset.ColumnCount != asset.Columns.Count)
        {
            throw new InvalidDataException(
                $"LeaderboardDef '{assetName}' declares {asset.ColumnCount} columns but contains {asset.Columns.Count}.");
        }

        for (int index = 0; index < asset.Columns.Count; index++)
        {
            LbColumnDef column = asset.Columns[index] ??
                throw new InvalidDataException(
                    $"LeaderboardDef '{assetName}' column {index} is null.");
            if (string.IsNullOrEmpty(column.Name) ||
                column.Name.Contains('\0'))
            {
                throw new InvalidDataException(
                    $"LeaderboardDef '{assetName}' column {index} has no valid name.");
            }
            if (column.StatName?.Contains('\0') == true)
            {
                throw new InvalidDataException(
                    $"LeaderboardDef '{assetName}' column {index} stat name contains an embedded null.");
            }
            _ = GetColumnType(column.Type, assetName, index);
            _ = GetAggregation(column.Aggregation, assetName, index);
        }
    }

    private static void WriteJson(
        Stream stream,
        LeaderboardDefAsset asset)
    {
        using (var writer = new Utf8JsonWriter(stream, JsonOptions))
        {
            // OAT uses nlohmann::json here, whose object keys are ordered.
            writer.WriteStartObject();
            writer.WriteString("_game", "iw4");
            writer.WriteString("_type", "leaderboard");
            writer.WriteNumber("_version", 1);
            writer.WriteStartArray("columns");
            for (int index = 0; index < asset.Columns.Count; index++)
            {
                LbColumnDef column = asset.Columns[index];
                writer.WriteStartObject();
                writer.WriteString(
                    "aggregationFunction",
                    GetAggregation(column.Aggregation, asset.Name!, index));
                writer.WriteNumber("colId", column.Id);
                if (column.Hidden)
                    writer.WriteBoolean("hidden", true);
                writer.WriteString("name", column.Name);
                if (column.Precision != 0)
                    writer.WriteNumber("precision", column.Precision);
                if (column.PropertyId != 0)
                    writer.WriteNumber("propertyId", column.PropertyId);
                if (!string.IsNullOrEmpty(column.StatName))
                    writer.WriteString("statName", column.StatName);
                writer.WriteString(
                    "type",
                    GetColumnType(column.Type, asset.Name!, index));
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteNumber("id", asset.Id);
            if (asset.PrestigeColumnId >= 0)
                writer.WriteNumber("prestigeColId", asset.PrestigeColumnId);
            if (asset.XpColumnId >= 0)
                writer.WriteNumber("xpColId", asset.XpColumnId);
            writer.WriteEndObject();
            writer.Flush();
        }

        stream.WriteByte((byte)'\n');
    }

    private static string GetColumnType(
        LbColType type,
        string assetName,
        int columnIndex) => type switch
        {
            LbColType.Number => "number",
            LbColType.Time => "time",
            LbColType.LevelXp => "levelxp",
            LbColType.Prestige => "prestige",
            LbColType.BigNumber => "bignumber",
            LbColType.Percent => "percent",
            _ => throw new InvalidDataException(
                $"LeaderboardDef '{assetName}' column {columnIndex} has unsupported type {type}.")
        };

    private static string GetAggregation(
        LbAggType aggregation,
        string assetName,
        int columnIndex) => aggregation switch
        {
            LbAggType.Min => "min",
            LbAggType.Max => "max",
            LbAggType.Sum => "sum",
            LbAggType.Last => "last",
            _ => throw new InvalidDataException(
                $"LeaderboardDef '{assetName}' column {columnIndex} has unsupported aggregation {aggregation}.")
        };
}
