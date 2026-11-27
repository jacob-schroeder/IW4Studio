using MapConverter.Game.IW3.PC.World;
using MapConverter.IW3.PC.Assets;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using MapConverter.Game.IW3.PC.Conversion;
using MapConverter.IW3.PC.Readers;

namespace MapConverter.Game.IW3.PC.Extraction;

internal sealed record Iw3PcExtractionRequest(
    string MapFastFilePath,
    string LoadFastFilePath,
    string? IwdPath,
    string? SourceLibraryDirectory,
    string ScratchDirectory);

internal sealed record Iw3PcExtractionResult(
    string BackendDescription,
    Iw3PcWorldExtraction World,
    string D3dbspPath,
    string MapAssetDirectory,
    string MapZoneSourcePath,
    string LoadAssetDirectory,
    string LoadZoneSourcePath,
    Iw3PcSourceLibrary? SourceLibrary,
    Iw3PcVehicleRecipes VehicleRecipes,
    (string? Title, string? Description) MenuText);

/// <summary>
/// Reads IW3 PC source assets and converts the map BSP
/// into the version consumed by the IW4 PS3 linker.
/// </summary>
internal static class Iw3PcExtractionBackend
{
    internal const string Description = "IW3 PC fastfile reader and v22 D3DBSP writer";

    public static async Task<Iw3PcExtractionResult> ExtractAsync(
        Iw3PcExtractionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var mapFastFilePath = ValidateFastFile(
            request.MapFastFilePath,
            nameof(request.MapFastFilePath),
            "Map fastfile");
        var loadFastFilePath = ValidateFastFile(
            request.LoadFastFilePath,
            nameof(request.LoadFastFilePath),
            "Load fastfile");
        string? iwdPath = request.IwdPath is null
            ? null
            : ValidateIwd(request.IwdPath, nameof(request.IwdPath));
        string? sourceLibraryDirectory = request.SourceLibraryDirectory is null
            ? null
            : ValidateExistingDirectory(
                request.SourceLibraryDirectory,
                nameof(request.SourceLibraryDirectory),
                "IW3 source library");
        var scratchDirectory = ValidateExistingDirectory(
            request.ScratchDirectory,
            nameof(request.ScratchDirectory),
            "Scratch directory");

        if (PathComparer.Equals(mapFastFilePath, loadFastFilePath))
        {
            throw new ArgumentException(
                "Map and load fastfiles must be different files.",
                nameof(request));
        }

        var mapName = GetZoneName(mapFastFilePath, nameof(request.MapFastFilePath));
        var loadName = GetZoneName(loadFastFilePath, nameof(request.LoadFastFilePath));
        var d3dbspPath = Path.Combine(scratchDirectory, $"{mapName}.d3dbsp");
        var mapAssetDirectory = Path.Combine(scratchDirectory, "map-assets");
        var loadAssetDirectory = Path.Combine(scratchDirectory, "load-assets");
        var mapZoneSourcePath = Path.Combine(mapAssetDirectory, "zone_source", $"{mapName}.zone");
        var loadZoneSourcePath = Path.Combine(loadAssetDirectory, "zone_source", $"{loadName}.zone");

        RejectExistingOutput(d3dbspPath);
        RejectExistingOutput(mapAssetDirectory);
        RejectExistingOutput(loadAssetDirectory);

        Iw3PcSourceLibrary? sourceLibrary = sourceLibraryDirectory is null
            ? null
            : await Iw3PcSourceLibrary.ResolveAsync(
                mapFastFilePath,
                loadFastFilePath,
                iwdPath,
                sourceLibraryDirectory,
                scratchDirectory,
                cancellationToken).ConfigureAwait(false);

        var menuText = sourceLibrary?.MenuText ?? default;
        Iw3PcWorldExtraction world;
        if (sourceLibrary is null)
        {
            var mapZone = Iw3PcZone.Load(mapFastFilePath, cancellationToken);
            menuText = ReadMenuText(mapZone, mapName, menuText);
            world = Iw3PcWorldConverter.Convert(mapZone, d3dbspPath, textured: true, cancellationToken);
            await ExtractStandaloneAsync(mapZone, mapFastFilePath, mapAssetDirectory, iwdPath,
                worldOnly: false, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            world = await sourceLibrary.ConvertWorldAsync(mapFastFilePath, d3dbspPath, cancellationToken)
                .ConfigureAwait(false);
        }

        Iw3PcVehicleRecipes vehicleRecipes = await Iw3PcVehicleRecipes.ReadAsync(
            mapFastFilePath, d3dbspPath, mapName, sourceLibrary, cancellationToken).ConfigureAwait(false);

        if (sourceLibrary is not null)
        {
            Directory.CreateDirectory(mapAssetDirectory);
            Directory.CreateDirectory(loadAssetDirectory);
            await sourceLibrary.ExtractAsync(
                mapFastFilePath, mapAssetDirectory, mapZoneSourcePath,
                cancellationToken).ConfigureAwait(false);
            await sourceLibrary.ExtractAsync(
                loadFastFilePath, loadAssetDirectory, loadZoneSourcePath,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var loadZone = Iw3PcZone.Load(loadFastFilePath, cancellationToken);
            menuText = ReadMenuText(loadZone, mapName, menuText);
            await ExtractStandaloneAsync(loadZone, loadFastFilePath,
                loadAssetDirectory, iwdPath, worldOnly: false, cancellationToken).ConfigureAwait(false);
        }

        return new Iw3PcExtractionResult(
            Description,
            world,
            d3dbspPath,
            mapAssetDirectory,
            mapZoneSourcePath,
            loadAssetDirectory,
            loadZoneSourcePath,
            sourceLibrary,
            vehicleRecipes,
            menuText);
    }

    internal static (string? Title, string? Description) ReadMenuText(Iw3PcZone zone, string mapName,
        (string? Title, string? Description) existing) =>
        (existing.Title ?? zone.ReadLocalize(mapName), existing.Description ?? zone.ReadLocalize(mapName + "_desc"));

    internal static async Task ExtractStandaloneAsync(Iw3PcZone zone, string fastFilePath,
        string outputDirectory, string? iwdPath, bool worldOnly, CancellationToken token)
    {
        string name = GetZoneName(fastFilePath, nameof(fastFilePath));
        var catalog = Iw3PcSourceLibrary.CreateCatalog(zone);
        // Files come from the last registration of each concrete name in the typed pool.
        // The zone manifest also retains references and records hidden by later definitions.
        var records = catalog.Assets.Where(asset => !asset.IsReference)
            .Where(asset => asset.Type is "material" or "xmodel" or "image" or "rawfile" ||
                !worldOnly && asset.Type is ("mapents" or "physpreset" or "techniqueset"))
            .GroupBy(asset => (asset.Type, asset.Key)).Select(group => group.Last()).ToArray();
        Iw3PcAssetExporter.ExportSelected(zone, records.Select(asset => asset.Id).ToArray(), outputDirectory, token);
        var entries = catalog.Assets.Where(asset => asset.Type != "localize")
            .Select(asset => new Iw3ZoneManifestEntry(asset.Type, asset.Name.Trim(), asset.IsReference));
        if (catalog.Assets.Any(asset => asset.Type == "localize"))
            entries = entries.Append(new Iw3ZoneManifestEntry("localize", name, false));
        Iw3ZoneManifest.Write(Path.Combine(outputDirectory, "zone_source", name + ".zone"), entries);
        if (!worldOnly)
        {
            foreach (string directory in new[] { "techsets", "techniques", "shader_bin" })
                Directory.CreateDirectory(Path.Combine(outputDirectory, directory));
        }
        string[] externalImages = records.Where(asset => asset.Type == "image" &&
                !Iw3PcImageExporter.HasEmbeddedPixels(zone, zone.Assets[asset.Id]))
            .Select(asset => Iw3PcImageExporter.FileName(asset.Name)).ToArray();
        await Iw3PcIwdReader.ExtractExternalImages(fastFilePath, iwdPath, externalImages, outputDirectory, token)
            .ConfigureAwait(false);
    }

    internal static async Task<string> RunToolAsync(
        string operationName,
        string executablePath,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };

        try
        {
            if (!process.Start())
            {
                throw new Iw3PcExtractionException(
                    $"{operationName} could not start '{executablePath}'.");
            }
        }
        catch (Iw3PcExtractionException)
        {
            throw;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            throw new Iw3PcExtractionException(
                $"{operationName} could not start '{executablePath}': {exception.Message}",
                exception);
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await DrainOutputAsync(standardOutputTask, standardErrorTask).ConfigureAwait(false);
            throw;
        }

        var standardOutput = await standardOutputTask.ConfigureAwait(false);
        var standardError = await standardErrorTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new Iw3PcExtractionException(
                BuildFailureMessage(
                    operationName,
                    executablePath,
                    process.ExitCode,
                    standardOutput,
                    standardError));
        }

        return standardOutput;
    }

    private static string ValidateExistingFile(string path, string parameterName, string description)
    {
        var fullPath = NormalizePath(path, parameterName);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"{description} was not found: '{fullPath}'.", fullPath);
        }

        return fullPath;
    }

    private static string ValidateFastFile(string path, string parameterName, string description)
    {
        var fullPath = ValidateExistingFile(path, parameterName, description);
        if (!string.Equals(Path.GetExtension(fullPath), ".ff", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"{description} must have a .ff extension: '{fullPath}'.", parameterName);
        }

        return fullPath;
    }

    private static string ValidateIwd(string path, string parameterName)
    {
        var fullPath = ValidateExistingFile(path, parameterName, "IWD archive");
        if (!string.Equals(Path.GetExtension(fullPath), ".iwd", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"IWD archive must have an .iwd extension: '{fullPath}'.",
                parameterName);
        }

        return fullPath;
    }

    private static string ValidateExistingDirectory(string path, string parameterName, string description)
    {
        var fullPath = NormalizePath(path, parameterName);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"{description} was not found: '{fullPath}'.");
        }

        return fullPath;
    }

    private static string NormalizePath(string path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be empty.", parameterName);
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException($"Path is invalid: '{path}'.", parameterName, exception);
        }
    }

    private static string GetZoneName(string fastFilePath, string parameterName)
    {
        var zoneName = Path.GetFileNameWithoutExtension(fastFilePath);
        if (string.IsNullOrWhiteSpace(zoneName))
        {
            throw new ArgumentException(
                $"Fastfile path does not contain a valid zone name: '{fastFilePath}'.",
                parameterName);
        }

        return zoneName;
    }

    private static void RejectExistingOutput(string path)
    {
        if (File.Exists(path) || Directory.Exists(path))
        {
            throw new IOException(
                $"Extraction output already exists: '{path}'. Use a new or empty scratch directory to avoid stale data.");
        }
    }

    private static string BuildFailureMessage(
        string operationName,
        string executablePath,
        int exitCode,
        string standardOutput,
        string standardError)
    {
        var message = new StringBuilder()
            .Append(operationName)
            .Append(" failed with exit code ")
            .Append(exitCode)
            .Append(" while running '")
            .Append(executablePath)
            .Append("'.");

        AppendCapturedOutput(message, "stderr", standardError);
        AppendCapturedOutput(message, "stdout", standardOutput);

        return message.ToString();
    }

    private static void AppendCapturedOutput(StringBuilder message, string label, string output)
    {
        var trimmedOutput = output.Trim();
        if (trimmedOutput.Length == 0)
        {
            return;
        }

        const int maximumLength = 8 * 1024;
        if (trimmedOutput.Length > maximumLength)
        {
            trimmedOutput = $"...{trimmedOutput[^maximumLength..]}";
        }

        message.AppendLine()
            .Append(label)
            .AppendLine(":")
            .Append(trimmedOutput);
    }

    private static async Task DrainOutputAsync(Task<string> standardOutputTask, Task<string> standardErrorTask)
    {
        try
        {
            await Task.WhenAll(standardOutputTask, standardErrorTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
