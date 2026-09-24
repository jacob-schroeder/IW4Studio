using System.Text.Json;
using IW4.Game.Database;

namespace D3dbspLinker.Conversion;

/// <summary>Link settings accompanying the repository's native startup source assets.</summary>
internal sealed record Ps3MapBootstrap
{
    internal static readonly string[] FactionMaterials =
        ["faction_128_rangers", "faction_128_rangers_fade", "faction_128_ussr", "faction_128_ussr_fade"];
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
    public required string?[] ScriptStrings { get; init; }

    internal static Ps3MapBootstrap Load(string directory)
    {
        string path = Path.Combine(directory, FileName);
        if (!File.Exists(path))
            throw new FileNotFoundException("The installed linker is missing its PS3 bootstrap assets. Rebuild or reinstall D3dbspLinker from the complete repository.", path);
        Ps3MapBootstrap value = JsonSerializer.Deserialize<Ps3MapBootstrap>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("PS3 bootstrap settings are empty.");
        if (value.Format != "iw4-ps3-map-bootstrap" || value.Version != 1 || value.FragmentProgramUploadCapacity <= 0 ||
            !DbLanguageMask.IsSupported(value.LanguageMask) || !DbLanguageMask.IsSingleLanguage(value.SelectedLanguageMask) ||
            (value.SelectedLanguageMask & value.LanguageMask) == 0 || value.ScriptStrings is null ||
            value.ScriptStrings.Length > ushort.MaxValue + 1)
            throw new InvalidDataException("PS3 bootstrap settings have an unsupported format or invalid link settings.");
        return value;
    }
}
