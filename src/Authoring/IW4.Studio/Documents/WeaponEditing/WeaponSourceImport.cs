using System.Globalization;
using IW4.Formats.SourceFormat.Weapon;
using IW4.Game.Assets.Weapon;
using IW4.Game.Math;
using IW4.Runtime.Assets;

namespace IW4.Studio.Documents;

internal static partial class WeaponSourceImport
{
    internal static WeaponAsset Read(
        Stream source, WeaponAsset baseline, XAssetPool pool, string sourcePath)
    {
        var draft = new WeaponDraft(baseline);
        IReadOnlyDictionary<string, string> changes = WeaponExchange.ReadSourceChanges(
            source, baseline, out IReadOnlySet<string> suppliedGraphKeys);
        foreach ((string key, string value) in changes)
        {
            if (key is "aiVsAiAccuracyGraph" or "aiVsPlayerAccuracyGraph")
                continue;
            if (TryApplyField(draft, key, value, pool) || TryApplyCollectionField(draft, key, value, pool))
                continue;
            throw new InvalidDataException($"Cannot import a change to weapon field '{key}'.");
        }
        ApplyAccuracyGraph(draft, baseline, changes, suppliedGraphKeys,
            sourcePath, true, "aiVsAiAccuracyGraph", "aivsai");
        ApplyAccuracyGraph(draft, baseline, changes, suppliedGraphKeys,
            sourcePath, false, "aiVsPlayerAccuracyGraph", "aivsplayer");
        return draft.ToAsset();
    }

    private static void ApplyAccuracyGraph(
        WeaponDraft draft, WeaponAsset baseline,
        IReadOnlyDictionary<string, string> changes,
        IReadOnlySet<string> suppliedGraphKeys,
        string sourcePath, bool aiVsAi, string key, string category)
    {
        if (!suppliedGraphKeys.Contains(key))
            return;
        WeaponAccuracyFields accuracy = baseline.Definition!.Accuracy;
        string? originalName = aiVsAi
            ? accuracy.AiVsAiGraphName : accuracy.AiVsPlayerGraphName;
        string name = changes.TryGetValue(key, out string? changedName)
            ? changedName ?? string.Empty : originalName ?? string.Empty;
        if (name.Length == 0)
        {
            if (changedName is not null)
                draft.SetSourceAccuracyGraph(aiVsAi, null, []);
            return;
        }

        bool sameName = string.Equals(name, originalName, StringComparison.Ordinal);
        IReadOnlyList<Vec2>? knots = WeaponExchange.ReadAccuracyGraph(
            sourcePath, category, name, allowMissing: sameName);
        if (knots is null)
            return;
        IReadOnlyList<Vec2> original = aiVsAi
            ? accuracy.OriginalAiVsAiGraphKnots
            : accuracy.OriginalAiVsPlayerGraphKnots;
        if (sameName && MatchesWrittenAccuracyGraph(original, knots))
            return;
        draft.SetSourceAccuracyGraph(aiVsAi, name, knots);
    }

    private static bool MatchesWrittenAccuracyGraph(
        IReadOnlyList<Vec2> original, IReadOnlyList<Vec2> source)
    {
        if (original.Count != source.Count)
            return false;
        for (int index = 0; index < original.Count; index++)
        {
            if (BitConverter.SingleToInt32Bits(WrittenFloat(original[index].a)) !=
                    BitConverter.SingleToInt32Bits(source[index].a) ||
                BitConverter.SingleToInt32Bits(WrittenFloat(original[index].b)) !=
                    BitConverter.SingleToInt32Bits(source[index].b))
                return false;
        }
        return true;
    }

    private static float WrittenFloat(float value) =>
        float.Parse(value.ToString("F4", CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture);
}
