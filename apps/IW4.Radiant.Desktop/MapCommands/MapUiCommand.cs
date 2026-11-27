using System.Text.Json;
using IW4.Formats.SourceFormat.Image;
using IW4.Game.Assets.Image;
using IW4.Game.Assets.Localize;
using IW4.Linker.Packaging;

namespace IW4.Radiant.Desktop.MapCommands;

internal static class MapUiCommand
{
    internal static int Build(
        string mapName, string previewImageName, string rawDirectory, string metadataPath, string output)
    {
        if (string.IsNullOrEmpty(mapName) || mapName.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
            throw new ArgumentException($"Invalid map name '{mapName}'.", nameof(mapName));

        Metadata metadata = JsonSerializer.Deserialize<Metadata>(File.ReadAllBytes(metadataPath)) ??
            throw new InvalidDataException("UI metadata is empty.");
        foreach (string value in new[] { metadata.Title, metadata.Description, metadata.Factions })
        {
            if (string.IsNullOrWhiteSpace(value) || value.Contains('\0'))
                throw new InvalidDataException("UI metadata requires a title, description, and faction profile.");
        }

        GfxImageAsset sourceImage = new ImageExchange().Link(rawDirectory, previewImageName);
        byte[] bytes = Ps3MapUiZoneBuilder.Build($"preview_{mapName}", sourceImage,
        [
            new LocalizeAsset { Name = mapName, Value = metadata.Title },
            new LocalizeAsset { Name = $"{mapName}_desc", Value = metadata.Description },
            new LocalizeAsset { Name = $"{mapName}_factions", Value = metadata.Factions }
        ]);
        string outputPath = Path.GetFullPath(output);
        string outputDirectory = Path.GetDirectoryName(outputPath) ??
            throw new ArgumentException("The UI fastfile requires an output file path.", nameof(output));
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllBytes(outputPath, bytes);
        return 0;
    }

    private sealed record Metadata(string Title, string Description, string Factions);
}
