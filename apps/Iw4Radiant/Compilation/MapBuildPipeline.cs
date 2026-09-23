using System.ComponentModel;
using System.Diagnostics;
using Iw4Radiant.Materials;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Compilation;

internal static class MapBuildPipeline
{
    internal static Task BuildBspAsync(MapDocument document, string outputPath,
        IReadOnlyDictionary<string, MaterialSource> materials, IReadOnlyDictionary<string, XModelSource> models,
        CancellationToken cancellationToken, string? sourcePath) => Task.Run(() =>
    {
        string destination = Path.GetFullPath(outputPath);
        string directory = Path.GetDirectoryName(destination) ?? throw new InvalidDataException("Choose a folder for the compiled map.");
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("The selected output folder no longer exists.");
        string assetName = $"maps/mp/{Path.GetFileNameWithoutExtension(destination)}.d3dbsp";
        cancellationToken.ThrowIfCancellationRequested();
        var bsp = MapCompiler.Compile(document, assetName, materials, models, cancellationToken, sourcePath);
        string temporary = Path.Combine(directory,
            $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            bsp.Write(temporary);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }, cancellationToken);

    internal static async Task<string> BuildAsync(MapDocument document, string sourcePath,
        IReadOnlyDictionary<string, MaterialSource> materials, IReadOnlyDictionary<string, XModelSource> models,
        string linkerPath, string templatePath,
        IReadOnlyList<string> providerPaths, string outputFolder, IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        sourcePath = Path.GetFullPath(sourcePath);
        linkerPath = RequireFile(linkerPath, "D3dbspLinker");
        templatePath = RequireFile(templatePath, "Bootstrap map fastfile");
        string[] providers = providerPaths.Select(path => RequireFile(path, "Asset provider fastfile"))
            .Distinct(StringComparer.Ordinal).ToArray();
        outputFolder = Path.GetFullPath(outputFolder);
        if (!Directory.Exists(outputFolder)) throw new DirectoryNotFoundException("Choose an existing output folder.");
        string mapName = Path.GetFileNameWithoutExtension(sourcePath);
        string assetName = $"maps/mp/{mapName}.d3dbsp";
        string token = Guid.NewGuid().ToString("N")[..8];
        string staging = Path.Combine(outputFolder, $".{mapName}-{token}.building");
        string destination = Path.Combine(outputFolder, $"{mapName}-{DateTime.Now:yyyyMMdd-HHmmss}-{token}");
        Directory.CreateDirectory(staging);
        try
        {
            progress.Report("Compiling geometry and collision; baking sunlight, local lights and reflections…");
            string bspPath = Path.Combine(staging, mapName + ".d3dbsp");
            string fastFilePath = Path.Combine(staging, mapName + ".ff");
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                string savedSource = Path.Combine(staging, mapName + ".map");
                MapFile.Write(document, savedSource);
                var bsp = MapCompiler.Compile(MapFile.Read(savedSource), assetName, materials, models, cancellationToken, sourcePath);
                cancellationToken.ThrowIfCancellationRequested();
                bsp.Write(bspPath);
            }, cancellationToken);
            progress.Report("Linking the PS3 fastfile with the selected asset providers…");
            await RunLinkerAsync(linkerPath, bspPath, templatePath, assetName, fastFilePath,
                providers, progress, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(fastFilePath) || new FileInfo(fastFilePath).Length == 0)
                throw new InvalidDataException("D3dbspLinker completed without producing a fastfile.");
            Directory.Move(staging, destination);
            progress.Report($"Built {mapName}.map, {mapName}.d3dbsp and {mapName}.ff in {destination}");
            return destination;
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private static async Task RunLinkerAsync(string linkerPath, string bspPath, string templatePath,
        string assetName, string fastFilePath, IReadOnlyList<string> providers, IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        bool managed = Path.GetExtension(linkerPath).Equals(".dll", StringComparison.OrdinalIgnoreCase);
        var start = new ProcessStartInfo
        {
            FileName = managed ? "dotnet" : linkerPath,
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(linkerPath) ?? "."
        };
        if (managed) start.ArgumentList.Add(linkerPath);
        foreach (string value in new[] { "to-fastfile", bspPath, templatePath, assetName, fastFilePath,
                     "--source-materials", "--compiled-lighting", "--stock-bootstrap" }) start.ArgumentList.Add(value);
        foreach (string model in IW4.Formats.D3dbsp.D3dbspFile.Read(bspPath).GetEntities()
                     .Where(entity => entity.GetValueOrDefault("classname") is "script_model" or "misc_turret")
                     .Select(entity => entity["model"]).Distinct(StringComparer.Ordinal))
        {
            start.ArgumentList.Add("--xmodel");
            start.ArgumentList.Add(model);
        }
        foreach (string provider in providers)
        {
            start.ArgumentList.Add("--provider-fastfile");
            start.ArgumentList.Add(provider);
        }
        // These match the Rangers/OpFor startup profile already emitted by D3dbspLinker.
        foreach (string faction in new[] { "rangers", "ussr" })
        foreach (string suffix in new[] { "", "_fade" })
        {
            start.ArgumentList.Add("--material");
            start.ArgumentList.Add($"faction_128_{faction}{suffix}");
        }
        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start()) throw new InvalidOperationException("D3dbspLinker could not be started.");
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException(managed
                ? "Cannot start dotnet. Install the .NET runtime or select the D3dbspLinker executable instead of its DLL."
                : "Cannot start D3dbspLinker. Select its executable for this operating system.", exception);
        }
        var errors = new Queue<string>();
        Task output = ReadLinesAsync(process.StandardOutput, false);
        Task error = ReadLinesAsync(process.StandardError, true);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(output, error);
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
            await Task.WhenAll(output, error);
            throw;
        }
        if (process.ExitCode != 0)
            throw new InvalidDataException($"D3dbspLinker exited with code {process.ExitCode}." +
                (errors.Count == 0 ? " See the build output for details." : "\n" + string.Join('\n', errors)));

        async Task ReadLinesAsync(StreamReader reader, bool isError)
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                progress.Report(line);
                if (!isError) continue;
                if (errors.Count == 12) errors.Dequeue();
                errors.Enqueue(line);
            }
        }
    }

    private static string RequireFile(string path, string label)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException($"Choose the {label} file.");
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException($"{label} was not found: {fullPath}", fullPath);
        return fullPath;
    }
}
