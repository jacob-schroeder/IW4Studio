using MapConverter.Game.IW3.Rules;

namespace MapConverter.CommandLine;

public enum SourceGame
{
    Iw3,
}

public enum SourcePlatform
{
    Pc,
    Ps3,
}

public sealed record MapConverterOptions(
    SourceGame Game,
    SourcePlatform Platform,
    string MapPath,
    string? LoadPath,
    string? IwdPath,
    string? SourceFastFileDirectory,
    string? SourceLibraryDirectory,
    string? BootstrapFastFilePath,
    int? ImageFileIndex,
    string OutputDirectory,
    string? WorldTemplatePath,
    FactionProfile Factions = FactionProfile.UsRussia,
    (string Title, string Description)? UiTextOverride = null);
