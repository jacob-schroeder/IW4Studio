using IW4.Assets.Assets.Localize;

namespace IW4.AssetExchange.SourceFormat.Localize;

/// <summary>Writes all localized strings in a zone to its native StringEd file.</summary>
public sealed class LocalizeExchange
{
    public IReadOnlyList<string> Unlink(
        string sourceDirectory,
        string zoneName,
        uint languageMask,
        IEnumerable<LocalizeAsset> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        string normalizedZoneName = SourceOutput.NormalizeOwnedAssetName(
            zoneName,
            "Fastfile");
        if (normalizedZoneName.Contains('/'))
        {
            throw new InvalidDataException(
                $"Fastfile name '{zoneName}' is not a single source filename.");
        }

        string language = GetLanguageName(languageMask);
        LocalizeAsset[] entries = assets.ToArray();
        Validate(entries, normalizedZoneName);
        if (entries.Length == 0)
            return [];

        return new SourceOutput(sourceDirectory).WriteTextBatch([
            (
                $"{language}/localizedstrings/{normalizedZoneName}.str",
                writer => WriteStringFile(
                    writer,
                    normalizedZoneName,
                    language,
                    entries))
        ]);
    }

    private static void Validate(
        IReadOnlyList<LocalizeAsset> entries,
        string zoneName)
    {
        for (int index = 0; index < entries.Count; index++)
        {
            LocalizeAsset entry = entries[index] ??
                throw new InvalidDataException(
                    $"Fastfile '{zoneName}' localize entry {index} is null.");
            if (string.IsNullOrEmpty(entry.Name) || entry.Name.Contains('\0'))
            {
                throw new InvalidDataException(
                    $"Fastfile '{zoneName}' localize entry {index} has no valid reference.");
            }
            if (entry.Value is null || entry.Value.Contains('\0'))
            {
                throw new InvalidDataException(
                    $"Fastfile '{zoneName}' localize entry '{entry.Name}' has no valid value.");
            }
        }
    }

    private static void WriteStringFile(
        TextWriter writer,
        string zoneName,
        string language,
        IReadOnlyList<LocalizeAsset> entries)
    {
        string languageCaps = language.ToUpperInvariant();
        writer.WriteLine($"// Dumped from fastfile \"{zoneName}\".");
        writer.WriteLine("// In their original format the strings might have been separated in multiple files.");
        writer.WriteLine("VERSION             \"1\"");
        writer.WriteLine("CONFIG              \"C:/trees/cod3/cod3/bin/StringEd.cfg\"");
        writer.WriteLine("FILENOTES           \"\"");

        foreach (LocalizeAsset entry in entries)
        {
            writer.WriteLine();
            writer.Write("REFERENCE           ");
            if (RequiresQuotedReference(entry.Name!))
            {
                writer.Write('"');
                SourceText.WriteQuotedContent(writer, entry.Name!);
                writer.WriteLine('"');
            }
            else
            {
                writer.WriteLine(entry.Name);
            }

            writer.Write("LANG_");
            writer.Write(languageCaps);
            writer.Write(new string(' ', 15 - languageCaps.Length));
            writer.Write('"');
            SourceText.WriteQuotedContent(writer, entry.Value!);
            writer.WriteLine('"');
        }

        writer.WriteLine();
        writer.Write("ENDMARKER");
    }

    private static bool RequiresQuotedReference(string reference) =>
        reference.Any(character =>
            character != '_' &&
            character is not (>= '0' and <= '9') &&
            character is not (>= 'A' and <= 'Z') &&
            character is not (>= 'a' and <= 'z'));

    private static string GetLanguageName(uint languageMask) =>
        languageMask switch
        {
            0x0001 => "english",
            0x0002 => "french",
            0x0004 => "german",
            0x0008 => "italian",
            0x0010 => "spanish",
            0x0020 => "british",
            0x0040 => "russian",
            0x0080 => "polish",
            0x0100 => "korean",
            0x0200 => "taiwanese",
            0x0400 => "japanese",
            0x0800 => "chinese",
            0x1000 => "thai",
            0x2000 => "leet",
            0x4000 => "czech",
            _ => throw new InvalidDataException(
                $"Language mask 0x{languageMask:X8} does not select one supported PS3 IW4 language.")
        };
}
