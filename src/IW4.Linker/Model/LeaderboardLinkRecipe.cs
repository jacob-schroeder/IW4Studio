using IW4.Assets.Assets.Leaderboard;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.Model;

/// <summary>Frozen LeaderboardDef body and its presence-owned column table.</summary>
internal sealed class LeaderboardLinkRecipe : AssetLinkRecipe
{
    private LeaderboardLinkRecipe(
        AssetKey key,
        string originalSerializedName,
        LeaderboardDefAsset definition,
        LinkAssetFreezeScope freeze)
        : base(
            key,
            originalSerializedName,
            freeze.FreezeProviderName(originalSerializedName, 0, "Asset.Name"))
    {
        LinkStorageSymbol? columns = CreateColumns(definition.Columns, freeze);
        var writer = new LinkTemplateWriter(LeaderboardDefAsset.SerializedSize);
        writer.Skip(sizeof(int));
        writer.WriteInt32(definition.Id);
        writer.WriteInt32(definition.ColumnCount);
        writer.WriteInt32(definition.XpColumnId);
        writer.WriteInt32(definition.PrestigeColumnId);
        writer.Skip(sizeof(int));
        Root = LinkStorageSymbol.SourceBytes(
            XFileBlockType.TEMP,
            writer.Complete(),
            alignment: 4,
            root => columns is null
                ? [NameOperation(root, 0)]
                : [
                    NameOperation(root, 0),
                    PresenceOperation(
                        root,
                        0x14,
                        columns,
                        "LeaderboardDef.Columns")
                ]);
    }

    internal override LinkStorageSymbol Root { get; }

    public static AssetLinkRecipe Freeze(
        AssetKey key,
        string originalSerializedName,
        LeaderboardDefAsset definition,
        LinkAssetFreezeScope freeze)
    {
        ArgumentNullException.ThrowIfNull(definition);
        IReadOnlyList<LbColumnDef> columns = definition.Columns ??
            throw new InvalidDataException("LeaderboardDef.Columns cannot be null.");
        if (originalSerializedName.StartsWith(','))
        {
            if (definition.Id != 0 ||
                definition.ColumnCount != 0 ||
                definition.XpColumnId != 0 ||
                definition.PrestigeColumnId != 0 ||
                columns.Count != 0)
            {
                throw new InvalidDataException(
                    "A comma-prefixed LeaderboardDef provider must have a zeroed reference body.");
            }

            return ExternalAssetLinkRecipe.Create(
                key,
                XAssetType.LeaderboardDef,
                originalSerializedName,
                freeze);
        }

        if (definition.ColumnCount < 0 || definition.ColumnCount != columns.Count)
        {
            throw new InvalidDataException(
                "LeaderboardDef.ColumnCount must equal the semantic column count.");
        }
        for (int index = 0; index < columns.Count; index++)
        {
            LbColumnDef column = columns[index] ?? throw new InvalidDataException(
                $"LeaderboardDef.Columns[{index}] cannot be null.");
            if (column.Pad0DTo0F is null ||
                column.Pad0DTo0F.Length is not (0 or 3))
            {
                throw new InvalidDataException(
                    $"LeaderboardDef.Columns[{index}].Pad0DTo0F must contain zero or three bytes.");
            }
        }

        return new LeaderboardLinkRecipe(key, originalSerializedName, definition, freeze);
    }

    private static LinkStorageSymbol? CreateColumns(
        IReadOnlyList<LbColumnDef> columns,
        LinkAssetFreezeScope freeze)
    {
        if (columns.Count == 0)
            return null;

        var names = new LinkStorageSymbol?[columns.Count];
        var statNames = new LinkStorageSymbol?[columns.Count];
        var writer = new LinkTemplateWriter(
            checked(columns.Count * LbColumnDef.SerializedSize));
        for (int index = 0; index < columns.Count; index++)
        {
            LbColumnDef column = columns[index];
            names[index] = freeze.FreezeOptionalXString(
                column.Name,
                column.NamePointer.Untyped,
                $"LeaderboardDef.Columns[{index}].Name");
            statNames[index] = freeze.FreezeOptionalXString(
                column.StatName,
                column.StatNamePointer.Untyped,
                $"LeaderboardDef.Columns[{index}].StatName");
            writer.Skip(sizeof(int));
            writer.WriteInt32(column.Id);
            writer.WriteInt32(column.PropertyId);
            writer.WriteByte(column.HiddenRaw);
            if (column.Pad0DTo0F.Length == 0)
                writer.Skip(3);
            else
                writer.WriteBytes(column.Pad0DTo0F);
            writer.Skip(sizeof(int));
            writer.WriteInt32((int)column.Type);
            writer.WriteInt32(column.Precision);
            writer.WriteInt32((int)column.Aggregation);
        }

        return LinkStorageSymbol.SourceBytes(
            XFileBlockType.LARGE,
            writer.Complete(),
            alignment: 4,
            table => CreateColumnOperations(table, names, statNames));
    }

    private static IEnumerable<LinkOperation> CreateColumnOperations(
        LinkStorageSymbol table,
        IReadOnlyList<LinkStorageSymbol?> names,
        IReadOnlyList<LinkStorageSymbol?> statNames)
    {
        for (int index = 0; index < names.Count; index++)
        {
            int rowOffset = checked(index * LbColumnDef.SerializedSize);
            if (names[index] is { } name)
            {
                yield return XStringOperation(
                    table,
                    rowOffset,
                    name,
                    $"LeaderboardDef.Columns[{index}].Name");
            }
            if (statNames[index] is { } statName)
            {
                yield return XStringOperation(
                    table,
                    checked(rowOffset + 0x10),
                    statName,
                    $"LeaderboardDef.Columns[{index}].StatName");
            }
        }
    }
}
