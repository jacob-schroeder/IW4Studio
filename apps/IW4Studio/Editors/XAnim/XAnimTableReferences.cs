using System.Runtime.CompilerServices;
using IW4.Game.Assets.StringTable;
using IW4.Runtime.Assets;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Editors.XAnim;

internal static class XAnimTableReferences
{
    private static readonly ConditionalWeakTable<StringTableAsset, ILookup<string, string?>> Indexes = new();

    internal static IEnumerable<(string Source, string? Model)> Find(
        FastFileWorkspace workspace, string animationName)
    {
        string lookup = XAssetStableIdentity.NormalizeLookupName(animationName);
        foreach (var entry in workspace.LoadedZone.Context.AssetPool.Entries)
        {
            if (entry.IsReferencePlaceholder || entry.Asset is not StringTableAsset table)
                continue;
            foreach (string? model in Indexes.GetValue(table, Index)[lookup].Distinct(StringComparer.OrdinalIgnoreCase))
                yield return (entry.Name, model);
        }
    }

    private static ILookup<string, string?> Index(StringTableAsset table) =>
        ReadRows(table).ToLookup(row => XAssetStableIdentity.NormalizeLookupName(row.Clip), row => row.Model);

    private static IEnumerable<(string Clip, string? Model)> ReadRows(StringTableAsset table)
    {
        if (table.RowCount <= 0 || table.ColumnCount <= 0 ||
            (long)table.RowCount * table.ColumnCount != table.Cells.Count)
            yield break;

        string path = XAssetStableIdentity.NormalizeLookupName(table.Name ?? "");
        bool configStrings = table.ColumnCount == 2 &&
            path.StartsWith("mp/configstrings/configstrings_ps3_", StringComparison.Ordinal) &&
            path.EndsWith(".csv", StringComparison.Ordinal);
        var animColumns = new List<int>();
        var modelColumns = new List<int>();
        for (int column = 0; column < table.ColumnCount; column++)
        {
            string? header = table.Cells[column].String?.Trim().ToLowerInvariant();
            if (header is "xanim" or "anim" or "animation")
                animColumns.Add(column);
            if (header is "model" or "xmodel")
                modelColumns.Add(column);
        }

        for (int row = 0; row < table.RowCount; row++)
        {
            int start = row * table.ColumnCount;
            string? first = table.Cells[start].String;
            // PS3 default_mp.elf 0x1ACCFC: MP animation configstrings use base 4004,
            // count 63, with slot zero reserved.
            bool animationSlot = configStrings && int.TryParse(first, out int id) && id is > 4004 and < 4067;
            bool assetRow = string.Equals(first, "xanim", StringComparison.OrdinalIgnoreCase) &&
                !(row == 0 && modelColumns.Count > 0);
            if (table.ColumnCount >= 2 && (animationSlot || assetRow) && table.Cells[start + 1].String is { Length: > 0 } value)
                yield return (value, null);

            if (row == 0)
                continue;
            string? model = modelColumns.Count == 1 ? table.Cells[start + modelColumns[0]].String : null;
            foreach (int column in animColumns)
            {
                if (table.Cells[start + column].String is { Length: > 0 } clip)
                    yield return (clip, string.IsNullOrWhiteSpace(model) ? null : model);
            }
        }
    }
}
