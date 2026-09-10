namespace MapConverter.Game.IW3.PC.Extraction;

internal sealed record Iw3ZoneManifestEntry(
    string AssetType,
    string AssetName,
    bool IsReference);

/// <summary>
/// Reads and writes the deterministic IW3 zone-source manifest.
/// </summary>
internal sealed class Iw3ZoneManifest
{
    private Iw3ZoneManifest(IReadOnlyList<Iw3ZoneManifestEntry> entries)
    {
        Entries = entries;
    }

    internal IReadOnlyList<Iw3ZoneManifestEntry> Entries { get; }

    internal static void Write(string path, IEnumerable<Iw3ZoneManifestEntry> entries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(entries);

        var lines = new List<string> { ">game,IW3" };
        foreach (Iw3ZoneManifestEntry entry in entries
                     .OrderBy(entry => entry.AssetType, StringComparer.Ordinal)
                     .ThenBy(entry => entry.AssetName, StringComparer.Ordinal))
        {
            if (!IsValidEntry(entry.AssetType, entry.AssetName))
            {
                throw new InvalidDataException(
                    $"Cannot write invalid IW3 zone entry '{entry.AssetType},{entry.AssetName}'.");
            }
            lines.Add($"{entry.AssetType},{(entry.IsReference ? "," : "")}{entry.AssetName}");
        }

        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("The IW3 zone manifest requires a file path.", nameof(path));
        Directory.CreateDirectory(directory);
        File.WriteAllLines(fullPath, lines, new System.Text.UTF8Encoding(false));
    }

    internal static Iw3ZoneManifest Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The IW3 zone manifest does not exist.", fullPath);

        var entries = new List<Iw3ZoneManifestEntry>();
        bool sawGameDirective = false;
        int lineNumber = 0;
        foreach (string sourceLine in File.ReadLines(fullPath))
        {
            lineNumber++;
            string line = sourceLine.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                continue;

            if (line.StartsWith('>'))
            {
                if (sawGameDirective ||
                    !string.Equals(line, ">game,IW3", StringComparison.Ordinal))
                {
                    throw ManifestError(
                        fullPath,
                        lineNumber,
                        $"unsupported directive '{line}'");
                }

                sawGameDirective = true;
                continue;
            }

            int firstComma = line.IndexOf(',');
            if (firstComma <= 0 || firstComma == line.Length - 1)
            {
                throw ManifestError(fullPath, lineNumber, "expected 'type,name'");
            }

            string assetType = line[..firstComma];
            string encodedName = line[(firstComma + 1)..];
            bool isReference = encodedName.StartsWith(',');
            string assetName = isReference ? encodedName[1..] : encodedName;
            if (!IsValidAssetName(assetName))
            {
                throw ManifestError(
                    fullPath,
                    lineNumber,
                    $"invalid {assetType} asset name '{assetName}'");
            }
            if (!IsValidAssetType(assetType))
            {
                throw ManifestError(
                    fullPath,
                    lineNumber,
                    $"invalid asset type '{assetType}'");
            }

            entries.Add(new Iw3ZoneManifestEntry(
                assetType,
                assetName,
                isReference));
        }

        if (!sawGameDirective)
        {
            throw new InvalidDataException(
                $"IW3 zone manifest '{fullPath}' has no '>game,IW3' directive.");
        }

        return new Iw3ZoneManifest(Array.AsReadOnly(entries.ToArray()));
    }

    private static bool IsValidAssetType(string value) =>
        !string.IsNullOrEmpty(value) && value.All(character => character is >= 'a' and <= 'z' or '_');

    internal static bool IsValidEntry(string type, string name) =>
        IsValidAssetType(type) && IsValidAssetName(name);

    private static bool IsValidAssetName(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.IndexOfAny([',', '\0', '\r', '\n']) < 0 &&
        string.Equals(value, value.Trim(), StringComparison.Ordinal);

    private static InvalidDataException ManifestError(
        string path,
        int lineNumber,
        string message) => new(
            $"IW3 zone manifest '{path}' line {lineNumber}: {message}.");
}
