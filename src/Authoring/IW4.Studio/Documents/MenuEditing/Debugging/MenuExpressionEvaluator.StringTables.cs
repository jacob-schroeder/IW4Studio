using IW4.Game.Assets.Menu;
using IW4.Game.Assets.StringTable;

namespace IW4.Studio.Documents.MenuEditing.Debugging;

public sealed partial class MenuExpressionEvaluator
{
    private const string RankTableName = "mp/rankTable.csv";
    private const int RankIdColumn = 0;
    private const int RankMinimumExperienceColumn = 2;
    private const int RankMaximumExperienceColumn = 7;
    private const int MissingRankExperience = 100_000_000;

    public static bool UsesWorkspaceStringTable(OperationEnum operation) => StringTableArity(operation) > 0;

    private static int StringTableArity(OperationEnum operation) => operation switch
    {
        OperationEnum.OP_TABLELOOKUP => 4,
        OperationEnum.OP_TABLELOOKUPBYROW => 3,
        OperationEnum.OP_EXPERIENCEFORLEVEL or OperationEnum.OP_LEVELFOREXPERIENCE => 1,
        _ => 0
    };

    private static MenuEvaluation<MenuDebugValue>? EvaluateStringTableOperation(
        OperationEnum operation,
        MenuDebugScenario scenario,
        List<MenuEvaluation<MenuDebugValue>> arguments,
        MenuDebugDependency dependency)
    {
        int arity = StringTableArity(operation);
        if (arity == 0)
            return null;
        if (arguments.Count != arity)
            return ArityError(operation, arity, arguments.Count);

        string tableName = arity == 1 ? RankTableName : arguments[0].Value.AsString();
        if (scenario.StringTableResolver?.Invoke(tableName) is not { } table)
            return Unknown($"StringTable '{tableName}' is unavailable in the preview.", operation, [dependency], arguments);

        MenuDebugValue value;
        if (arity == 1)
        {
            if (!arguments[0].Value.TryGetInt(out int rankOrExperience))
                return ConversionError(operation, arguments.ToArray());

            value = MenuDebugValue.FromInt(operation == OperationEnum.OP_EXPERIENCEFORLEVEL
                ? ExperienceForLevel(table, rankOrExperience)
                : LevelForExperience(table, rankOrExperience));
        }
        else
        {
            if (!arguments[1].Value.TryGetInt(out int rowOrColumn) ||
                !arguments[^1].Value.TryGetInt(out int resultColumn))
                return ConversionError(operation, arguments.ToArray());

            int row = operation == OperationEnum.OP_TABLELOOKUP
                ? FindStringTableRow(table, rowOrColumn, arguments[2].Value.AsString())
                : rowOrColumn;
            value = MenuDebugValue.FromString(GetStringTableCell(table, row, resultColumn)?.String ?? string.Empty);
        }

        return MenuEvaluation<MenuDebugValue>.Known(
            value,
            arguments.SelectMany(argument => argument.Dependencies).Append(dependency),
            arguments.SelectMany(argument => argument.Trace));
    }

    // PS3 StringTable row lookup (0x261bc8) starts at the last row, compares
    // the stored hash first, and then compares the text case-insensitively.
    private static int FindStringTableRow(StringTableAsset table, int column, string value)
    {
        if (column < 0 || column >= table.ColumnCount)
            return -1;

        int hash = StringTableCell.CalculateHash(value);
        for (int row = table.RowCount - 1; row >= 0; row--)
        {
            StringTableCell? cell = GetStringTableCell(table, row, column);
            if (cell is not null && cell.Hash == hash &&
                string.Equals(cell.String, value, StringComparison.OrdinalIgnoreCase))
                return row;
        }
        return -1;
    }

    private static StringTableCell? GetStringTableCell(StringTableAsset table, int row, int column)
    {
        if (row < 0 || row >= table.RowCount || column < 0 || column >= table.ColumnCount)
            return null;

        long index = (long)row * table.ColumnCount + column;
        return index < table.Cells.Count ? table.Cells[(int)index] : null;
    }

    // PS3 rank helpers (0xb4d88 and 0xb4e28) map the rank ID to minimum XP,
    // and search the table's min/max XP intervals to map XP back to a rank ID.
    private static int ExperienceForLevel(StringTableAsset table, int level)
    {
        int row = FindStringTableRow(table, RankIdColumn, MenuDebugValue.FromInt(level).AsString());
        string? experience = GetStringTableCell(table, row, RankMinimumExperienceColumn)?.String;
        return string.IsNullOrEmpty(experience) ? MissingRankExperience : ParseRankCell(experience);
    }

    private static int LevelForExperience(StringTableAsset table, int experience)
    {
        int low = 0;
        int high = table.RowCount - 1;
        int middle = high / 2;
        while (high > low)
        {
            int minimum = ParseRankCell(GetStringTableCell(table, middle, RankMinimumExperienceColumn)?.String);
            int maximum = ParseRankCell(GetStringTableCell(table, middle, RankMaximumExperienceColumn)?.String);
            if (experience < minimum)
                high = middle - 1;
            else if (experience >= maximum)
                low = middle + 1;
            else
                break;

            middle = low + (high - low) / 2;
        }
        return ParseRankCell(GetStringTableCell(table, middle, RankIdColumn)?.String);
    }

    private static int ParseRankCell(string? text)
    {
        MenuDebugValue.FromString(text ?? string.Empty).TryGetInt(out int value);
        return value;
    }
}
