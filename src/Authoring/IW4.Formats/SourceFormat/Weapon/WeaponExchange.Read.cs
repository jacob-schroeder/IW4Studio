using System.Globalization;
using System.Text;
using IW4.Formats.SourceFormat.Technique;
using IW4.Game.Assets.Weapon;
using IW4.Game.Math;

namespace IW4.Formats.SourceFormat.Weapon;

public sealed partial class WeaponExchange
{
    /// <summary>Reads loose weapon edits against an existing variant. Enum values use their native indices.</summary>
    public static IReadOnlyDictionary<string, string> ReadSourceChanges(
        Stream source, WeaponAsset baseline, out IReadOnlySet<string> suppliedGraphKeys)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(baseline);
        using var reader = new StreamReader(source, new UTF8Encoding(false, true),
            detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        string[] fields = reader.ReadToEnd().TrimEnd('\r', '\n').Split('\\');
        if (fields.Length < 3 || fields.Length % 2 != 1 || fields[0] != "WEAPONFILE")
            throw new InvalidDataException("Choose an IW4 WEAPONFILE containing field names and values.");

        string baselineName = SourceOutput.NormalizeOwnedAssetName(baseline.Name, "Weapon");
        var schema = WeaponInfoStringSource.Create(baseline, baselineName);
        var finalValues = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 1; index < fields.Length; index += 2)
        {
            string key = fields[index];
            string value = fields[index + 1];
            finalValues[key] = value;
        }
        suppliedGraphKeys = finalValues.Keys.Where(key =>
                key is "aiVsAiAccuracyGraph" or "aiVsPlayerAccuracyGraph")
            .ToHashSet(StringComparer.Ordinal);

        var changes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string key, string value) in finalValues)
        {
            if (value.Contains('\0'))
                throw new InvalidDataException($"Weapon field '{key}' contains an embedded null.");
            if (schema.GetEditedValue(key, value) is { } edited)
                changes[key] = edited;
        }
        return changes;
    }

    /// <summary>Reads a changed graph from the accuracy directory next to a loose weapons directory.</summary>
    public static IReadOnlyList<Vec2>? ReadAccuracyGraph(
        string weaponSourcePath, string category, string graphName,
        bool allowMissing = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(weaponSourcePath);
        if (category is not ("aivsai" or "aivsplayer"))
            throw new ArgumentException("Unknown accuracy graph category.", nameof(category));
        string name = SourceOutput.NormalizeOwnedAssetName(graphName, "Accuracy graph");
        string directory = Path.GetDirectoryName(Path.GetFullPath(weaponSourcePath)) ??
            throw new InvalidDataException("The weapon source has no containing directory.");
        string root = directory;
        for (DirectoryInfo? ancestor = new(directory); ancestor is not null;
             ancestor = ancestor.Parent)
        {
            if (!ancestor.Name.Equals("weapons", StringComparison.OrdinalIgnoreCase))
                continue;
            root = ancestor.Parent?.FullName ?? directory;
            break;
        }
        string path = NativeSourcePath.Resolve(root, $"accuracy/{category}", name, "");
        string[] lines;
        try
        {
            lines = File.ReadAllLines(path, new UTF8Encoding(false, true));
        }
        catch (FileNotFoundException) when (allowMissing)
        {
            return null;
        }
        catch (DirectoryNotFoundException) when (allowMissing)
        {
            return null;
        }
        catch (Exception exception) when (exception is IOException or DecoderFallbackException)
        {
            throw new InvalidDataException(
                $"Accuracy graph '{category}/{name}' is missing or unreadable at '{path}'.",
                exception);
        }
        if (lines.Length < 3 || lines[0] != "WEAPONACCUFILE" || lines[1].Length != 0 ||
            !int.TryParse(lines[2], NumberStyles.None, CultureInfo.InvariantCulture, out int count) ||
            count > ushort.MaxValue || lines.Length != count + 3)
            throw new InvalidDataException($"Accuracy graph '{category}/{name}' has an invalid WEAPONACCUFILE header or knot count.");

        var knots = new Vec2[count];
        for (int index = 0; index < count; index++)
        {
            string[] coordinates = lines[index + 3].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (coordinates.Length != 2 ||
                !float.TryParse(coordinates[0], NumberStyles.Float, CultureInfo.InvariantCulture,
                    out float x) || !float.IsFinite(x) ||
                !float.TryParse(coordinates[1], NumberStyles.Float, CultureInfo.InvariantCulture,
                    out float y) || !float.IsFinite(y))
                throw new InvalidDataException($"Accuracy graph '{category}/{name}' knot {index} is invalid.");
            knots[index] = new Vec2 { a = x, b = y };
        }
        return Array.AsReadOnly(knots);
    }
}
