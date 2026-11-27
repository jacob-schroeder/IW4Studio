using System.Text.Json;
using IW4.Game.Database;

namespace IW4.Studio.D3dbsp;

/// <summary>Link settings accompanying the repository's native startup source assets.</summary>
internal sealed record Ps3MapBootstrap
{
    internal const string FileName = "bootstrap.json";
    internal static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public required string Format { get; init; }
    public required int Version { get; init; }
    public required int FragmentProgramUploadCapacity { get; init; }
    public required uint LanguageMask { get; init; }
    public required uint SelectedLanguageMask { get; init; }

    internal static Ps3MapBootstrap Load(string directory)
    {
        string path = Path.Combine(directory, FileName);
        if (!File.Exists(path))
            throw new FileNotFoundException("The installed linker is missing its PS3 bootstrap assets. Rebuild or reinstall the application with its bootstrap assets.", path);
        Ps3MapBootstrap value = JsonSerializer.Deserialize<Ps3MapBootstrap>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("PS3 bootstrap settings are empty.");
        if (value.Format != "iw4-ps3-map-bootstrap" || value.Version != 1 || value.FragmentProgramUploadCapacity <= 0 ||
            !DbLanguageMask.IsSupported(value.LanguageMask) || !DbLanguageMask.IsSingleLanguage(value.SelectedLanguageMask) ||
            (value.SelectedLanguageMask & value.LanguageMask) == 0)
            throw new InvalidDataException("PS3 bootstrap settings have an unsupported format or invalid link settings.");
        return value;
    }
}
