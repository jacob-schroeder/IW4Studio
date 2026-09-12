namespace MapConverter.Game.IW3.PC.Extraction;

internal sealed record Iw3PcExtractionTools(
    string NativeMapConverterPath,
    string? UnlinkerPath);

internal static class Iw3PcExtractionToolLocator
{
    private const string MapToolEnvironmentVariable =
        "MAPCONVERTER_IW3_D3DBSP_CONVERTER";
    private const string UnlinkerEnvironmentVariable =
        "MAPCONVERTER_UNLINKER";

    internal static Iw3PcExtractionTools Find(bool requireUnlinker)
    {
        string mapConverter = FindNativeMapConverter();
        string? unlinker = requireUnlinker ? FindUnlinker() : null;
        return new Iw3PcExtractionTools(mapConverter, unlinker);
    }

    internal static string FindUnlinker() => FindRequiredTool(
        UnlinkerEnvironmentVariable,
        ["mapconverter-unlinker", "Unlinker"]);

    internal static string FindNativeMapConverter() => FindRequiredTool(
        MapToolEnvironmentVariable,
        ["mapconverter-iw3-d3dbsp", "iw3_to_v22_d3dbsp"]);

    internal static string FindWorldLinker() => FindRequiredTool(
        "MAPCONVERTER_D3DBSP_LINKER",
        ["D3dbspLinker"]);

    private static string FindRequiredTool(
        string environmentVariable,
        IReadOnlyList<string> executableNames)
    {
        string? configured = Environment.GetEnvironmentVariable(
            environmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            string configuredPath = Path.GetFullPath(configured);
            if (!File.Exists(configuredPath))
            {
                throw new FileNotFoundException(
                    $"{environmentVariable} points to a file that does not exist.",
                    configuredPath);
            }

            return configuredPath;
        }

        foreach (string executableName in executableNames)
        {
            string adjacentPath = Path.Combine(
                AppContext.BaseDirectory,
                "Native",
                executableName);
            if (File.Exists(adjacentPath))
                return adjacentPath;
        }

        string? pathValue = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathValue))
        {
            foreach (string directory in pathValue.Split(
                         Path.PathSeparator,
                         StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (string executableName in executableNames)
                {
                    string candidate = Path.Combine(directory, executableName);
                    if (File.Exists(candidate))
                        return Path.GetFullPath(candidate);
                }
            }
        }

        throw new FileNotFoundException(
            $"MapConverter could not find '{executableNames[0]}'. Place it in " +
            $"the Native output directory, add it to PATH, or set {environmentVariable}.");
    }
}
