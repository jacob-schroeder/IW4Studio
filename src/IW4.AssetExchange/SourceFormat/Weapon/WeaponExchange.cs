using System.Globalization;
using IW4.AssetExchange.SourceFormat.InfoString;
using IW4.Assets.Assets.Weapon;
using IW4.Assets.Math;

namespace IW4.AssetExchange.SourceFormat.Weapon;

/// <summary>
/// Writes IW4 weapons and their original accuracy graphs in the native
/// WEAPONFILE/WEAPONACCUFILE source formats.
/// </summary>
public sealed class WeaponExchange
{
    private static readonly StringComparer SourcePathComparer =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private readonly object _gate = new();
    private readonly Dictionary<string, Vec2[]> _writtenAccuracyGraphs =
        new(SourcePathComparer);

    public IReadOnlyList<string> Unlink(
        string sourceDirectory,
        WeaponAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        string assetName = SourceOutput.NormalizeOwnedAssetName(
            asset.Name,
            "Weapon");

        lock (_gate)
        {
            InfoStringSourceWriter source = WeaponInfoStringSource.Create(
                asset,
                assetName);
            var writes = new List<(string RelativePath, Action<TextWriter> Write)>
            {
                ($"weapons/{assetName}", source.Write)
            };
            var newlyWrittenGraphs = new List<PendingAccuracyGraph>(2);

            AddAccuracyGraph(
                writes,
                newlyWrittenGraphs,
                assetName,
                "aivsai",
                asset.Definition!.Accuracy.AiVsAiGraphNamePointer.Raw,
                asset.Definition.Accuracy.AiVsAiGraphName,
                asset.Definition.Accuracy.OriginalAiVsAiGraphKnotsPointer.Raw,
                asset.Definition.Accuracy.OriginalAiVsAiGraphKnotCount,
                asset.Definition.Accuracy.OriginalAiVsAiGraphKnots,
                asset.Variant.AiVsAiAccuracyGraphKnotsPointer.Raw,
                asset.Variant.AiVsAiAccuracyGraphKnotCount,
                asset.Variant.AiVsAiAccuracyGraphKnots);
            AddAccuracyGraph(
                writes,
                newlyWrittenGraphs,
                assetName,
                "aivsplayer",
                asset.Definition.Accuracy.AiVsPlayerGraphNamePointer.Raw,
                asset.Definition.Accuracy.AiVsPlayerGraphName,
                asset.Definition.Accuracy.OriginalAiVsPlayerGraphKnotsPointer.Raw,
                asset.Definition.Accuracy.OriginalAiVsPlayerGraphKnotCount,
                asset.Definition.Accuracy.OriginalAiVsPlayerGraphKnots,
                asset.Variant.AiVsPlayerAccuracyGraphKnotsPointer.Raw,
                asset.Variant.AiVsPlayerAccuracyGraphKnotCount,
                asset.Variant.AiVsPlayerAccuracyGraphKnots);

            IReadOnlyList<string> paths = new SourceOutput(sourceDirectory)
                .WriteTextBatch(writes);
            foreach (PendingAccuracyGraph graph in newlyWrittenGraphs)
                _writtenAccuracyGraphs.Add(graph.Name, graph.Knots);
            return paths;
        }
    }

    private void AddAccuracyGraph(
        ICollection<(string RelativePath, Action<TextWriter> Write)> writes,
        ICollection<PendingAccuracyGraph> newlyWrittenGraphs,
        string weaponName,
        string category,
        int namePointerRaw,
        string? graphName,
        int knotsPointerRaw,
        int knotCount,
        IReadOnlyList<Vec2> knots,
        int currentKnotsPointerRaw,
        int currentKnotCount,
        IReadOnlyList<Vec2> currentKnots)
    {
        string materializedName = InfoStringSourceWriter.MaterializedString(
            namePointerRaw,
            graphName,
            $"Weapon '{weaponName}' {category} accuracy graph name");
        Vec2[] originalSnapshot = knots.ToArray();
        Vec2[] currentSnapshot = currentKnots.ToArray();
        ValidateAccuracyGraph(
            weaponName,
            category,
            materializedName,
            knotsPointerRaw,
            knotCount,
            originalSnapshot,
            currentKnotsPointerRaw,
            currentKnotCount,
            currentSnapshot);
        if (materializedName.Length == 0)
            return;

        string normalizedName = SourceOutput.NormalizeOwnedAssetName(
            materializedName,
            $"Weapon '{weaponName}' {category} accuracy graph");
        string graphAssetName = $"{category}/{normalizedName}";
        ValidateFiniteKnots(graphAssetName, originalSnapshot);
        if (_writtenAccuracyGraphs.TryGetValue(
                graphAssetName,
                out Vec2[]? writtenKnots))
        {
            if (!KnotsEqual(writtenKnots, originalSnapshot))
            {
                throw new InvalidDataException(
                    $"Weapon '{weaponName}' accuracy graph '{graphAssetName}' differs from the graph already written to that source path.");
            }

            return;
        }

        writes.Add((
            $"accuracy/{graphAssetName}",
            writer => WriteAccuracyGraph(writer, originalSnapshot)));
        newlyWrittenGraphs.Add(new PendingAccuracyGraph(
            graphAssetName,
            originalSnapshot));
    }

    private static void ValidateAccuracyGraph(
        string weaponName,
        string category,
        string graphName,
        int originalPointerRaw,
        int originalCount,
        IReadOnlyList<Vec2> originalKnots,
        int currentPointerRaw,
        int currentCount,
        IReadOnlyList<Vec2> currentKnots)
    {
        string field = $"Weapon '{weaponName}' {category} accuracy graph";
        ValidateAccuracyPayload(
            originalPointerRaw,
            originalCount,
            originalKnots,
            $"{field} original knots");
        ValidateAccuracyPayload(
            currentPointerRaw,
            currentCount,
            currentKnots,
            $"{field} current knots");

        if (graphName.Length == 0)
        {
            if (originalPointerRaw != 0 || currentPointerRaw != 0)
            {
                throw new InvalidDataException(
                    $"{field} has knot payloads without a source graph name.");
            }

            return;
        }

        if (originalPointerRaw == 0 || currentPointerRaw == 0)
        {
            throw new InvalidDataException(
                $"{field} is named but does not have both materialized original and current knot payloads.");
        }
        if (originalCount != currentCount)
        {
            throw new InvalidDataException(
                $"{field} has {originalCount} original knots and {currentCount} current knots; the IW4 source format stores only one graph.");
        }

        for (int index = 0; index < originalKnots.Count; index++)
        {
            Vec2 original = originalKnots[index];
            Vec2 current = currentKnots[index];
            if (BitConverter.SingleToInt32Bits(original.a) !=
                    BitConverter.SingleToInt32Bits(current.a) ||
                BitConverter.SingleToInt32Bits(original.b) !=
                    BitConverter.SingleToInt32Bits(current.b))
            {
                throw new InvalidDataException(
                    $"{field} differs between original and current knot {index}; the IW4 source format stores only one graph.");
            }
        }
    }

    private static void ValidateAccuracyPayload(
        int pointerRaw,
        int declaredCount,
        IReadOnlyList<Vec2> knots,
        string field)
    {
        if (pointerRaw == 0)
        {
            if (declaredCount != 0 || knots.Count != 0)
            {
                throw new InvalidDataException(
                    $"{field} has data without a serialized pointer.");
            }

            return;
        }

        if (knots.Count != declaredCount)
        {
            throw new InvalidDataException(
                $"{field} declares {declaredCount} knots but has {knots.Count} materialized.");
        }
    }

    private static void WriteAccuracyGraph(
        TextWriter writer,
        IReadOnlyList<Vec2> knots)
    {
        writer.WriteLine("WEAPONACCUFILE");
        writer.WriteLine();
        writer.WriteLine(knots.Count.ToString(CultureInfo.InvariantCulture));
        for (int index = 0; index < knots.Count; index++)
        {
            Vec2 knot = knots[index];
            writer.Write(knot.a.ToString("F4", CultureInfo.InvariantCulture));
            writer.Write(' ');
            writer.WriteLine(knot.b.ToString("F4", CultureInfo.InvariantCulture));
        }
    }

    private static void ValidateFiniteKnots(
        string graphName,
        IReadOnlyList<Vec2> knots)
    {
        for (int index = 0; index < knots.Count; index++)
        {
            Vec2 knot = knots[index];
            if (!float.IsFinite(knot.a) || !float.IsFinite(knot.b))
            {
                throw new InvalidDataException(
                    $"Accuracy graph '{graphName}' knot {index} is non-finite.");
            }
        }
    }

    private static bool KnotsEqual(
        IReadOnlyList<Vec2> first,
        IReadOnlyList<Vec2> second)
    {
        if (first.Count != second.Count)
            return false;

        for (int index = 0; index < first.Count; index++)
        {
            if (BitConverter.SingleToInt32Bits(first[index].a) !=
                    BitConverter.SingleToInt32Bits(second[index].a) ||
                BitConverter.SingleToInt32Bits(first[index].b) !=
                    BitConverter.SingleToInt32Bits(second[index].b))
            {
                return false;
            }
        }

        return true;
    }

    private sealed record PendingAccuracyGraph(
        string Name,
        Vec2[] Knots);
}
