using System.Globalization;
using IW4.FastFiles.Database.Streaming;

namespace MapConverter.CommandLine;

internal static class CommandLineParser
{
    internal const string HelpText =
        """
        Usage:
          mapconverter --game iw3 --platform pc --map <path> --load <path> [--iwd <path>] [--source-fastfiles <directory>] [--source-library <directory>] [--bootstrap-fastfile <path>] [--imagefile-index <-1|1-20>] --output <directory>
          mapconverter --game iw3 --platform pc --map <path> --world-template <iw4.ff> [--bootstrap-fastfile <iw4.ff>] --output <directory>

        Options:
          --game <game>              Source game. Supported: iw3.
          --platform <platform>      Source platform. Supported: pc.
          --map <path>               Source map fastfile.
          --load <path>              Source loading-screen fastfile.
          --world-template <iw4.ff>  Self-contained IW4 model-bootstrap fastfile.
                                     Builds a fullbright world-first map using
                                     the native default material and the Shipment
                                     faction/utility model bootstrap. Omits props,
                                     dynamic entities, custom scripts/FX and
                                     converted shaders. Add --iwd and
                                     --imagefile-index for native material profiles
                                     with source props and authored lighting.
                                     Add --load to include the source loading screen
                                     in the same imagefile package.
          --iwd <path>               Optional source IWD archive.
          --source-fastfiles <dir>   Optional IW3 PS3 fastfile directory used
                                     to recover stock streamed-image payloads.
          --source-library <dir>     Optional COD4 source library directory.
                                     Searches fastfiles and IWD archives recursively
                                     for stock assets. Requires --imagefile-index;
                                     cannot be combined with --world-template.
                                     Supported PS3 asset graphs can be cataloged;
                                     PS3 asset export is not supported yet.
          --bootstrap-fastfile <ff>  Optional IW4 PS3 fastfile supplying an
                                     owned target XModel missing from IW3 input.
                                     With --world-template, supplies the native
                                     world/model materials and their dependencies.
          --imagefile-index <-1|1-20>
                                     Required with an image source. Use -1 for
                                     the map's named .pak shared with its _load
                                     fastfile, or 1-20 for imagefileN.pak.
          --output <directory>       Destination directory.
          --help                     Show this help text.

        Extraction tools:
          Put mapconverter-iw3-d3dbsp and mapconverter-unlinker in the Native
          output directory or on PATH. Their paths can also be supplied through
          MAPCONVERTER_IW3_D3DBSP_CONVERTER and MAPCONVERTER_UNLINKER.
          World-first conversion also requires D3dbspLinker in Native or on
          PATH, or its executable path in MAPCONVERTER_D3DBSP_LINKER.
        """;

    private static readonly HashSet<string> ValueOptions =
    [
        "--game",
        "--platform",
        "--map",
        "--load",
        "--world-template",
        "--iwd",
        "--source-fastfiles",
        "--source-library",
        "--bootstrap-fastfile",
        "--imagefile-index",
        "--output",
    ];

    public static CommandLineParseResult Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var helpRequested = false;

        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];

            if (argument == "--help")
            {
                if (helpRequested)
                {
                    return CommandLineParseResult.Failure("Option '--help' was specified more than once.");
                }

                helpRequested = true;
                continue;
            }

            if (!ValueOptions.Contains(argument))
            {
                return CommandLineParseResult.Failure($"Unknown option '{argument}'.");
            }

            if (values.ContainsKey(argument))
            {
                return CommandLineParseResult.Failure($"Option '{argument}' was specified more than once.");
            }

            if (index + 1 >= arguments.Count || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                return CommandLineParseResult.Failure($"Option '{argument}' requires a value.");
            }

            var value = arguments[++index];
            if (string.IsNullOrWhiteSpace(value))
            {
                return CommandLineParseResult.Failure($"Option '{argument}' requires a non-empty value.");
            }

            values.Add(argument, value);
        }

        if (helpRequested)
        {
            return CommandLineParseResult.Help();
        }

        bool worldOnly = values.ContainsKey("--world-template");
        string[] requiredOptions = worldOnly
            ? ["--game", "--platform", "--map", "--output"]
            : ["--game", "--platform", "--map", "--load", "--output"];
        var missingOptions = requiredOptions.Where(option => !values.ContainsKey(option)).ToArray();
        if (missingOptions.Length > 0)
        {
            return CommandLineParseResult.Failure(
                $"Missing required option{(missingOptions.Length == 1 ? string.Empty : "s")}: {string.Join(", ", missingOptions)}.");
        }

        if (!string.Equals(values["--game"], "iw3", StringComparison.OrdinalIgnoreCase))
        {
            return CommandLineParseResult.Failure(
                $"Unsupported game '{values["--game"]}'. Supported game: iw3.");
        }

        if (!string.Equals(values["--platform"], "pc", StringComparison.OrdinalIgnoreCase))
        {
            return CommandLineParseResult.Failure(
                $"Unsupported platform '{values["--platform"]}'. Supported platform: pc.");
        }

        var hasIwd = values.TryGetValue("--iwd", out var iwdPath);
        var hasSourceFastFiles = values.TryGetValue(
            "--source-fastfiles",
            out var sourceFastFileDirectory);
        var hasSourceLibrary = values.TryGetValue(
            "--source-library",
            out var sourceLibraryDirectory);
        var hasImageFileIndex = values.TryGetValue("--imagefile-index", out var imageFileIndexValue);

        if (worldOnly && hasSourceLibrary)
        {
            return CommandLineParseResult.Failure(
                "Option '--source-library' is supported only for full conversion and " +
                "cannot be combined with '--world-template'.");
        }

        if ((hasIwd || hasSourceFastFiles || hasSourceLibrary) && !hasImageFileIndex)
        {
            return CommandLineParseResult.Failure(
                "Option '--imagefile-index' is required when '--iwd', " +
                "'--source-fastfiles', or '--source-library' is specified.");
        }

        if (!hasIwd && !hasSourceFastFiles && !hasSourceLibrary && hasImageFileIndex)
        {
            return CommandLineParseResult.Failure(
                "Option '--iwd', '--source-fastfiles', or '--source-library' is required when " +
                "'--imagefile-index' is specified.");
        }

        int? imageFileIndex = null;
        if (hasImageFileIndex)
        {
            if (imageFileIndexValue == "-1")
            {
                imageFileIndex = unchecked((int)DbHeaderImageStreamEntry.NamedFileIndex);
            }
            else if (int.TryParse(imageFileIndexValue, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedIndex) &&
                DbHeaderImageStreamEntry.IsValidPackageFileIndex(checked((uint)parsedIndex)))
            {
                imageFileIndex = parsedIndex;
            }
            else
            {
                return CommandLineParseResult.Failure("Option '--imagefile-index' must be -1 or an integer from 1 through 20.");
            }
        }

        var options = new MapConverterOptions(
            SourceGame.Iw3,
            SourcePlatform.Pc,
            values["--map"],
            values.GetValueOrDefault("--load"),
            iwdPath,
            sourceFastFileDirectory,
            sourceLibraryDirectory,
            values.GetValueOrDefault("--bootstrap-fastfile"),
            imageFileIndex,
            values["--output"],
            values.GetValueOrDefault("--world-template"));

        return CommandLineParseResult.Success(options);
    }
}

internal sealed record CommandLineParseResult(
    MapConverterOptions? Options,
    string? ErrorMessage,
    bool HelpRequested)
{
    public bool IsSuccess => Options is not null;

    public static CommandLineParseResult Success(MapConverterOptions options) => new(options, null, false);

    public static CommandLineParseResult Failure(string errorMessage) => new(null, errorMessage, false);

    public static CommandLineParseResult Help() => new(null, null, true);
}
