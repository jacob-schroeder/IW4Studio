namespace MapConverter.CommandLine;

internal enum SourceGame
{
    Iw3,
}

internal enum SourcePlatform
{
    Pc,
}

internal sealed record MapConverterOptions(
    SourceGame Game,
    SourcePlatform Platform,
    string MapPath,
    string? LoadPath,
    string? IwdPath,
    string? SourceFastFileDirectory,
    string? BootstrapFastFilePath,
    int? ImageFileIndex,
    string OutputDirectory,
    string? WorldTemplatePath);
