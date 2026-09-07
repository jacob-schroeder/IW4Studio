using System.Globalization;

namespace MapConverter.CommandLine;

internal static class CommandLineParser
{
    internal const string HelpText =
        """
        Usage:
          mapconverter --game iw3 --platform pc --map <path> --load <path> [--iwd <path>] [--source-fastfiles <directory>] [--bootstrap-fastfile <path>] [--imagefile-index <1-20>] --output <directory>
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
          --bootstrap-fastfile <ff>  Optional IW4 PS3 fastfile supplying an
                                     owned target XModel missing from IW3 input.
                                     With --world-template, supplies the native
                                     world/model materials and their dependencies.
          --imagefile-index <1-20>   Required with an image source; selects
                                     imagefileN.pak.
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
        var hasImageFileIndex = values.TryGetValue("--imagefile-index", out var imageFileIndexValue);

        if ((hasIwd || hasSourceFastFiles) && !hasImageFileIndex)
        {
            return CommandLineParseResult.Failure(
                "Option '--imagefile-index' is required when '--iwd' or " +
                "'--source-fastfiles' is specified.");
        }

        if (!hasIwd && !hasSourceFastFiles && hasImageFileIndex)
        {
            return CommandLineParseResult.Failure(
                "Option '--iwd' or '--source-fastfiles' is required when " +
                "'--imagefile-index' is specified.");
        }

        int? imageFileIndex = null;
        if (hasImageFileIndex)
        {
            if (!int.TryParse(imageFileIndexValue, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedIndex) ||
                parsedIndex is < 1 or > 20)
            {
                return CommandLineParseResult.Failure("Option '--imagefile-index' must be an integer from 1 through 20.");
            }

            imageFileIndex = parsedIndex;
        }

        var options = new MapConverterOptions(
            SourceGame.Iw3,
            SourcePlatform.Pc,
            values["--map"],
            values.GetValueOrDefault("--load"),
            iwdPath,
            sourceFastFileDirectory,
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
