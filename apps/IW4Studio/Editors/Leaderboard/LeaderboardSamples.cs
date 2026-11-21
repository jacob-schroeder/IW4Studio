using System.Globalization;
using IW4.Game.Assets.Leaderboard;

namespace IW4.Studio.Desktop.Editors.Leaderboard;

internal sealed record LeaderboardStatChoice(string? Key, string Label, string Group)
{
    public override string ToString() => Label;
}

internal sealed record LeaderboardRoleChoice(int Id, string Label)
{
    public override string ToString() => Label;
}

internal static class LeaderboardSamples
{
    // Observed StatName values in the eleven PS3 code_post_gfx_mp boards.
    // The four results per player are illustrative and never enter the draft.
    private sealed record Stat(string Key, string Label, double[] Results);
    private static readonly Stat[] Stats =
    [
        new("score", "Score", [820, 1380, 1980, 2620]),
        new("experience", "Experience (XP)", [18200, 23600, 29700, 36500]),
        new("prestige", "Prestige", [5, 5, 5, 6]),
        new("timePlayedTotal", "Total time played", [5362, 5508, 5670, 5826]),
        new("kills", "Kills", [125, 139, 148, 162]),
        new("deaths", "Deaths", [93, 100, 109, 119]),
        new("kdRatio", "Kill/death ratio", [1.34, 1.39, 1.36, 1.36]),
        new("killStreak", "Longest kill streak", [7, 8, 8, 9]),
        new("wins", "Wins", [42, 43, 43, 44]),
        new("losses", "Losses", [19, 19, 20, 20]),
        new("winLossRatio", "Win/loss ratio", [2.21, 2.26, 2.15, 2.20]),
        new("winStreak", "Longest win streak", [5, 5, 5, 6]),
        new("accuracy", "Accuracy", [.732, .780, .810, .842]),
        new("hits", "Hits", [261, 278, 293, 306]),
        new("misses", "Misses", [84, 90, 95, 101]),
        new("round score", "Score this round", [420, 580, 610, 730]),
        new("round timePlayed", "Time this round", [91.26, 87.43, 83.1, 76.82]),
        new("round kills", "Kills this round", [5, 14, 9, 14]),
        new("round deaths", "Deaths this round", [7, 7, 9, 10]),
        new("round killStreak", "Kill streak this round", [3, 8, 4, 9]),
        new("round win", "Won this round", [1, 1, 0, 1]),
        new("round loss", "Lost this round", [0, 0, 1, 0])
    ];
    private static readonly Dictionary<string, Stat> ByKey = Stats.ToDictionary(stat => stat.Key, StringComparer.Ordinal);
    private static readonly LeaderboardStatChoice[] Choices = [new(null, "No source stat", ""),
        ..Stats.Select(stat => new LeaderboardStatChoice(stat.Key, stat.Label, stat.Key.StartsWith("round ", StringComparison.Ordinal) ? "Round" : "Career"))];
    internal static IReadOnlyList<string> Players { get; } = ["Atlas", "Mira", "Orion", "Vale"];

    internal static IReadOnlyList<LeaderboardStatChoice> Sources(string? current) =>
        string.IsNullOrEmpty(current) || ByKey.ContainsKey(current)
            ? Choices
            : [..Choices, new(current, current, "Current asset")];

    internal static bool IsPlayerName(LbColumnDef c) => string.IsNullOrEmpty(c.StatName) && c.Id == 65533;

    internal static string Value(LbColumnDef c, int player)
    {
        if (IsPlayerName(c)) return Players[player];
        if (string.IsNullOrEmpty(c.StatName) && c.Id == 65535) return (player + 1).ToString(CultureInfo.CurrentCulture);
        if (c.StatName is null || !ByKey.TryGetValue(c.StatName, out var stat)) return "—";
        var results = Results(stat, player);
        double? result = c.Aggregation switch
        {
            LbAggType.Min => results.Min(), LbAggType.Max => results.Max(), LbAggType.Sum => results.Sum(),
            LbAggType.Last => results[^1], _ => null
        };
        return result.HasValue ? Format(c, result.Value) : "—";
    }

    internal static string History(LbColumnDef c) =>
        c.StatName is { } key && ByKey.TryGetValue(key, out var stat)
            ? string.Join("  ·  ", Results(stat, 0).Select(v => Format(c, v)))
            : string.IsNullOrEmpty(c.StatName) ? "No source stat" : "No sample results for this stat";

    private static double[] Results(Stat stat, int player)
    {
        if (player == 0) return stat.Results;
        if (stat.Key is "round win" or "round loss")
            return [..Enumerable.Range(0, 4).Select(i => stat.Results[(i + player) % 4])];
        double scale = 1 - player * .08;
        bool fractional = stat.Key is "accuracy" or "kdRatio" or "winLossRatio" or "round timePlayed";
        return [..stat.Results.Select(v => fractional ? v * scale : Math.Round(v * scale))];
    }

    private static string Format(LbColumnDef c, double value)
    {
        // Limit only the illustrative display. Preserve the stored precision.
        int precision = Math.Clamp(c.Precision, 0, 9);
        var culture = CultureInfo.CurrentCulture;
        switch (c.Type)
        {
            case LbColType.Time:
                double seconds = Math.Round(value, precision);
                string secondFormat = precision == 0 ? "00" : "00." + new string('0', precision);
                return ((int)(seconds / 60)).ToString(culture) + ":" + (seconds % 60).ToString(secondFormat, culture);
            case LbColType.Percent: return (value * 100).ToString("N" + precision, culture) + "%";
            case LbColType.LevelXp:
                // These sample bands come from mp/rankTable.csv. Outside them show the XP itself.
                int level = value switch { >= 13500 and < 18200 => 7, >= 18200 and < 23600 => 8,
                    >= 23600 and < 29700 => 9, >= 29700 and < 36500 => 10, >= 36500 and < 44300 => 11, _ => 0 };
                return level > 0 ? $"Lv {level}" : value.ToString("N0", culture) + " XP";
            case LbColType.Prestige: return "P" + value.ToString("F0", culture);
            case LbColType.BigNumber: return value.ToString("N0", culture);
            case LbColType.Number: return value.ToString("N" + precision, culture);
            default: return "—";
        }
    }
}
