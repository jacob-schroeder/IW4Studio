namespace MapConverter.Game.IW3.PC.Extraction;

internal sealed record Iw3ZoneManifestEntry(
    string AssetType,
    string AssetName,
    bool IsReference);

/// <summary>
/// Reads the deterministic IW3 zone-source manifest emitted by
/// OpenAssetTools after a fastfile is unlinked.
/// </summary>
internal sealed class Iw3ZoneManifest
{
    private Iw3ZoneManifest(IReadOnlyList<Iw3ZoneManifestEntry> entries)
    {
        Entries = entries;
    }

    internal IReadOnlyList<Iw3ZoneManifestEntry> Entries { get; }

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
            if (assetName.Length == 0 || assetName.Contains(',') ||
                assetName.Contains('\0') ||
                !string.Equals(assetName, assetName.Trim(), StringComparison.Ordinal))
            {
                throw ManifestError(
                    fullPath,
                    lineNumber,
                    $"invalid {assetType} asset name '{assetName}'");
            }
            if (assetType.Any(character =>
                    character is < 'a' or > 'z'))
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

    private static InvalidDataException ManifestError(
        string path,
        int lineNumber,
        string message) => new(
            $"IW3 zone manifest '{path}' line {lineNumber}: {message}.");
}
