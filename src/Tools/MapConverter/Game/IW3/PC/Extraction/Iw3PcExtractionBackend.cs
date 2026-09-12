using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace MapConverter.Game.IW3.PC.Extraction;

internal sealed record Iw3PcExtractionRequest(
    string NativeMapConverterPath,
    string? UnlinkerPath,
    string MapFastFilePath,
    string LoadFastFilePath,
    string? IwdPath,
    string? SourceLibraryDirectory,
    bool AllowMissingSounds,
    string ScratchDirectory);

internal sealed record Iw3PcExtractionResult(
    string BackendDescription,
    string NativeConversionOutput,
    string D3dbspPath,
    string DynamicEntityPath,
    string XModelCollisionPath,
    string MapAssetDirectory,
    string MapZoneSourcePath,
    string LoadAssetDirectory,
    string LoadZoneSourcePath,
    Iw3PcSourceLibrary? SourceLibrary);

/// <summary>
/// Runs the current IW3 PC extraction tools and converts the source map BSP
/// into the version consumed by the IW4 PS3 linker.
/// </summary>
internal static class Iw3PcExtractionBackend
{
    internal const string Description =
        "Native IW3-to-v22 D3DBSP converter with OpenAssetTools Unlinker";

    private const string IncludedAssetTypes =
        "image,material,xmodel,rawfile,mapents,fx,physpreset";

    public static async Task<Iw3PcExtractionResult> ExtractAsync(
        Iw3PcExtractionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.AllowMissingSounds && request.SourceLibraryDirectory is null)
            throw new ArgumentException("Allowing missing sounds requires a source library.", nameof(request));

        var nativeMapConverterPath = ValidateExistingFile(
            request.NativeMapConverterPath,
            nameof(request.NativeMapConverterPath),
            "Native IW3 map converter");
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
        var dynamicEntityPath = Path.Combine(scratchDirectory, $"{mapName}.dynents");
        var xmodelCollisionPath = Path.Combine(scratchDirectory, $"{mapName}.modelcoll");
        var mapAssetDirectory = Path.Combine(scratchDirectory, "map-assets");
        var loadAssetDirectory = Path.Combine(scratchDirectory, "load-assets");
        var mapZoneSourcePath = Path.Combine(mapAssetDirectory, "zone_source", $"{mapName}.zone");
        var loadZoneSourcePath = Path.Combine(loadAssetDirectory, "zone_source", $"{loadName}.zone");

        RejectExistingOutput(d3dbspPath);
        RejectExistingOutput(dynamicEntityPath);
        RejectExistingOutput(xmodelCollisionPath);
        RejectExistingOutput(mapAssetDirectory);
        RejectExistingOutput(loadAssetDirectory);

        Directory.CreateDirectory(mapAssetDirectory);
        Directory.CreateDirectory(loadAssetDirectory);

        Iw3PcSourceLibrary? sourceLibrary = sourceLibraryDirectory is null
            ? null
            : await Iw3PcSourceLibrary.ResolveAsync(
                nativeMapConverterPath,
                mapFastFilePath,
                loadFastFilePath,
                iwdPath,
                sourceLibraryDirectory,
                scratchDirectory,
                request.AllowMissingSounds,
                cancellationToken).ConfigureAwait(false);

        string nativeConversionOutput = sourceLibrary is null
            ? await RunToolAsync(
                "Native IW3 map conversion", nativeMapConverterPath, scratchDirectory,
                [mapFastFilePath, d3dbspPath, dynamicEntityPath, xmodelCollisionPath],
                cancellationToken).ConfigureAwait(false)
            : await sourceLibrary.ConvertWorldAsync(
                mapFastFilePath, d3dbspPath, dynamicEntityPath, xmodelCollisionPath,
                cancellationToken).ConfigureAwait(false);

        ValidateProducedFile(d3dbspPath, "The native converter did not produce the expected D3DBSP");
        ValidateProducedFile(
            dynamicEntityPath,
            "The native converter did not produce the expected dynamic-entity sidecar");
        ValidateProducedFile(
            xmodelCollisionPath,
            "The native converter did not produce the expected XModel-collision sidecar");

        if (sourceLibrary is not null)
        {
            await sourceLibrary.ExtractAsync(
                mapFastFilePath, mapAssetDirectory, mapZoneSourcePath,
                cancellationToken).ConfigureAwait(false);
            await sourceLibrary.ExtractAsync(
                loadFastFilePath, loadAssetDirectory, loadZoneSourcePath,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            string unlinkerPath = ValidateExistingFile(
                request.UnlinkerPath ?? throw new ArgumentException(
                    "Extraction without a source library requires Unlinker.", nameof(request)),
                nameof(request.UnlinkerPath),
                "OpenAssetTools Unlinker");
            await ExtractZoneAssetsAsync(
                unlinkerPath, nativeMapConverterPath, mapFastFilePath,
                mapAssetDirectory, mapZoneSourcePath, scratchDirectory,
                iwdPath, cancellationToken).ConfigureAwait(false);
            await ExtractZoneAssetsAsync(
                unlinkerPath, nativeMapConverterPath, loadFastFilePath,
                loadAssetDirectory, loadZoneSourcePath, scratchDirectory,
                iwdPath, cancellationToken).ConfigureAwait(false);
        }

        return new Iw3PcExtractionResult(
            sourceLibrary is null ? Description : "Native IW3 conversion with resolved source assets",
            nativeConversionOutput,
            d3dbspPath,
            dynamicEntityPath,
            xmodelCollisionPath,
            mapAssetDirectory,
            mapZoneSourcePath,
            loadAssetDirectory,
            loadZoneSourcePath,
            sourceLibrary);
    }

    private static async Task ExtractZoneAssetsAsync(
        string unlinkerPath,
        string nativeMapConverterPath,
        string fastFilePath,
        string assetDirectory,
        string zoneSourcePath,
        string scratchDirectory,
        string? iwdPath,
        CancellationToken cancellationToken)
    {
        await UnlinkZoneAsync(
            unlinkerPath,
            fastFilePath,
            assetDirectory,
            scratchDirectory,
            iwdPath,
            IncludedAssetTypes,
            "IW3 asset extraction",
            cancellationToken).ConfigureAwait(false);

        ValidateProducedFile(zoneSourcePath, "Unlinker did not produce the expected zone source");

        await ExtractTechniquesAsync(
            nativeMapConverterPath,
            fastFilePath,
            assetDirectory,
            scratchDirectory,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExtractTechniquesAsync(
        string nativeMapConverterPath,
        string fastFilePath,
        string assetDirectory,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        // The native loader preserves inline technique and shader identities.
        // Unlinker's name-only graph export merges shader-model variants.
        await RunToolAsync(
            "IW3 technique and shader extraction",
            nativeMapConverterPath,
            workingDirectory,
            ["--extract-techniques", fastFilePath, assetDirectory],
            cancellationToken).ConfigureAwait(false);

        foreach (string name in new[] { "techsets", "techniques", "shader_bin" })
        {
            ValidateExistingDirectory(
                Path.Combine(assetDirectory, name),
                nameof(assetDirectory),
                $"Extracted {name} directory");
        }
    }

    internal static async Task ExtractWorldAssetsAsync(
        string unlinkerPath,
        string mapFastFilePath,
        string outputDirectory,
        string workingDirectory,
        string? iwdPath,
        CancellationToken cancellationToken)
    {
        unlinkerPath = ValidateExistingFile(
            unlinkerPath,
            nameof(unlinkerPath),
            "OpenAssetTools Unlinker");
        mapFastFilePath = ValidateFastFile(
            mapFastFilePath,
            nameof(mapFastFilePath),
            "Map fastfile");
        iwdPath = iwdPath is null ? null : ValidateIwd(iwdPath, nameof(iwdPath));
        outputDirectory = NormalizePath(outputDirectory, nameof(outputDirectory));
        workingDirectory = ValidateExistingDirectory(
            workingDirectory,
            nameof(workingDirectory),
            "Scratch directory");

        var mapName = GetZoneName(mapFastFilePath, nameof(mapFastFilePath));
        var mapZoneSourcePath = Path.Combine(outputDirectory, "zone_source", $"{mapName}.zone");
        RejectExistingOutput(outputDirectory);
        Directory.CreateDirectory(outputDirectory);

        await UnlinkZoneAsync(
            unlinkerPath,
            mapFastFilePath,
            outputDirectory,
            workingDirectory,
            iwdPath,
            "material,image,xmodel,rawfile",
            "IW3 world asset extraction",
            cancellationToken).ConfigureAwait(false);

        ValidateProducedFile(mapZoneSourcePath, "Unlinker did not produce the expected map zone source");
    }

    private static Task UnlinkZoneAsync(
        string unlinkerPath,
        string fastFilePath,
        string outputDirectory,
        string workingDirectory,
        string? iwdPath,
        string includedAssetTypes,
        string operationName,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "--no-color",
            "--output-folder",
            outputDirectory,
            "--include-assets",
            includedAssetTypes,
            "--image-format",
            "IWI",
            "--model-format",
            "XMODEL_EXPORT",
        };
        string? searchPath = iwdPath is null
            ? null
            : Path.GetDirectoryName(iwdPath);
        if (searchPath is not null)
        {
            arguments.Add("--search-path");
            arguments.Add(searchPath);
        }
        arguments.Add(fastFilePath);
        return RunToolAsync(
            operationName,
            unlinkerPath,
            workingDirectory,
            arguments,
            cancellationToken);
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

    private static void ValidateProducedFile(string path, string message)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length == 0)
        {
            throw new Iw3PcExtractionException($"{message}: '{path}'.");
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
