using System.ComponentModel;
using System.Diagnostics;
using IW4.Formats.SourceFormat.Character;
using Iw4Radiant.Editing;
using Iw4Radiant.Materials;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Compilation;

internal static class MapBuildPipeline
{
    internal static Task BuildBspAsync(MapDocument document, string outputPath,
        IReadOnlyDictionary<string, MaterialSource> materials, IReadOnlyDictionary<string, XModelSource> models,
        CancellationToken cancellationToken, string? sourcePath, IProgress<string>? progress = null) => Task.Run(() =>
    {
        string destination = Path.GetFullPath(outputPath);
        string directory = Path.GetDirectoryName(destination) ?? throw new InvalidDataException("Choose a folder for the compiled map.");
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("The selected output folder no longer exists.");
        string assetName = $"maps/mp/{Path.GetFileNameWithoutExtension(destination)}.d3dbsp";
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report("Checking brushes, meshes and entities…");
        MapCompiler.ValidateNavigableSource(document);
        var bsp = MapCompiler.Compile(document, assetName, materials, models, cancellationToken, sourcePath, progress);
        string temporary = Path.Combine(directory,
            $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report("Writing compiled BSP…");
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
        string linkerPath, string emitterAssetDirectory, string outputFolder, IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        sourcePath = Path.GetFullPath(sourcePath);
        linkerPath = RequireFile(linkerPath, "D3dbspLinker");
        outputFolder = Path.GetFullPath(outputFolder);
        if (!Directory.Exists(outputFolder)) throw new DirectoryNotFoundException("Choose an existing output folder.");
        string mapName = Path.GetFileNameWithoutExtension(sourcePath);
        MapEmitterScripts? emitters = MapEmitterScriptAuthoring.Create(document, sourcePath, mapName);
        MapMovingLightScripts? movingLights = MapMovingLightScripts.Create(document, sourcePath, mapName);
        MapFogScripts? fog = MapFogScripts.Create(document, mapName);
        if (string.IsNullOrWhiteSpace(emitterAssetDirectory) ||
            !Directory.Exists(emitterAssetDirectory))
            throw new DirectoryNotFoundException(
                "Choose the raw asset library containing the map's materials, FX and sounds.");
        progress.Report("Checking destructible dependencies before compiling geometry and lighting…");
        await Task.Run(() =>
        {
            var expanded = PrefabLibrary.ExpandForCompilation(document, sourcePath);
            DestructiblePreset[] presets = expanded.Entities.Select(entity =>
            {
                DestructiblePresets.Validate(entity.Properties);
                return DestructiblePresets.Find(entity.Properties);
            }).OfType<DestructiblePreset>().Distinct().ToArray();
            if (presets.Length == 0) return;
            string bootstrap = Path.Combine(Path.GetDirectoryName(linkerPath) ?? AppContext.BaseDirectory, "bootstrap", "ps3");
            var readiness = DestructibleAssets.Check(emitterAssetDirectory, presets, cancellationToken, bootstrap);
            string[] issues = readiness.Where(pair => !pair.Value.IsReady)
                .SelectMany(pair => pair.Value.Issues.Select(issue => $"{pair.Key.Name}: {issue}")).ToArray();
            if (issues.Length != 0)
                throw new InvalidDataException("Destructible assets need attention. Resolve these in the asset library before building:\n" +
                    string.Join('\n', issues));
        }, cancellationToken);
        string assetName = $"maps/mp/{mapName}.d3dbsp";
        string token = Guid.NewGuid().ToString("N")[..8];
        string staging = Path.Combine(outputFolder, $".{mapName}-{token}.building");
        string destination = Path.Combine(outputFolder, $"{mapName}-{DateTime.Now:yyyyMMdd-HHmmss}-{token}");
        Directory.CreateDirectory(staging);
        try
        {
            progress.Report("Preparing build files…");
            string bspPath = Path.Combine(staging, mapName + ".d3dbsp");
            string fastFilePath = Path.Combine(staging, mapName + ".ff");
            IReadOnlyDictionary<string, string> soundVariantPaths = new Dictionary<string, string>(StringComparer.Ordinal);
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                soundVariantPaths = emitters?.WriteSoundVariants(staging, emitterAssetDirectory) ?? soundVariantPaths;
                progress.Report("Checking brushes, meshes and entities…");
                MapCompiler.ValidateNavigableSource(document);
                string savedSource = Path.Combine(staging, mapName + ".map");
                MapFile.Write(document, savedSource);
                var bsp = MapCompiler.Compile(MapFile.Read(savedSource), assetName, materials, models, cancellationToken, sourcePath, progress);
                cancellationToken.ThrowIfCancellationRequested();
                progress.Report("Writing compiled BSP…");
                bsp.Write(bspPath);
                progress.Report("Compiled BSP written.");
            }, cancellationToken);
            var mapRawFiles = new List<(string Name, string Path)>(emitters?.WriteTo(staging) ?? []);
            if (movingLights is not null) mapRawFiles.Add(movingLights.WriteTo(staging));
            if (fog is not null) mapRawFiles.Add(fog.WriteTo(staging));
            if (emitters is not null)
            {
                progress.Report($"Exported {emitters.FxNames.Length} FX references and {emitters.SoundNames.Length} sound aliases to map scripts.");
            }
            progress.Report("Compiling source assets and included startup assets; linking the PS3 fastfile…");
            await RunLinkerAsync(linkerPath, bspPath, assetName, fastFilePath,
                emitters, emitterAssetDirectory, mapRawFiles, soundVariantPaths,
                sourcePath, progress, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(fastFilePath) || new FileInfo(fastFilePath).Length == 0)
                throw new InvalidDataException("D3dbspLinker completed without producing a fastfile.");
            progress.Report("Finalizing build output…");
            Directory.Move(staging, destination);
            string packagedImages = File.Exists(Path.Combine(destination, mapName + ".pak")) ? $", {mapName}.pak" : "";
            string packagedSounds = File.Exists(Path.Combine(destination, mapName + "_snd.pak")) ? $", {mapName}_snd.pak" : "";
            progress.Report($"Built {mapName}.map, {mapName}.d3dbsp, {mapName}.ff{packagedImages}{packagedSounds} in {destination}");
            return destination;
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private static async Task RunLinkerAsync(string linkerPath, string bspPath,
        string assetName, string fastFilePath,
        MapEmitterScripts? emitters, string emitterAssetDirectory,
        IReadOnlyList<(string Name, string Path)> mapRawFiles,
        IReadOnlyDictionary<string, string> soundVariantPaths,
        string sourcePath,
        IProgress<string> progress,
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
        foreach (string value in new[] { "build", bspPath, assetName, fastFilePath, "--compiled-lighting" }) start.ArgumentList.Add(value);
        var entities = IW4.Formats.D3dbsp.D3dbspFile.Read(bspPath).GetEntities();
        DestructiblePreset[] destructibles = entities.Select(DestructiblePresets.Find)
            .OfType<DestructiblePreset>().Distinct().ToArray();
        foreach (string model in entities
                     .Where(entity => entity.GetValueOrDefault("classname") is "script_model" or "misc_turret")
                     .Select(entity => entity["model"]).Concat(destructibles.SelectMany(preset => preset.ModelNames))
                     .Distinct(StringComparer.Ordinal))
        {
            start.ArgumentList.Add("--xmodel");
            start.ArgumentList.Add(model);
        }
        start.ArgumentList.Add("--asset-library");
        start.ArgumentList.Add(Path.GetFullPath(emitterAssetDirectory));
        MapFactionSettings factions = MapFactionAuthoring.Read(
            entities.First(entity => entity.GetValueOrDefault("classname") == "worldspawn"));
        if (factions.AlliesAssaultA?.CustomAssetFolder is not null || factions.AxisAssaultA?.CustomAssetFolder is not null)
        {
            start.ArgumentList.Add("--character-assets");
            start.ArgumentList.Add(MapFactionAuthoring.GetCharacterAssetsDirectory(sourcePath));
        }
        // Destruction-only assets have no placed emitter/model of their own.
        // Package their roots without adding map-start FX or sound playback.
        foreach (string name in (emitters?.FxNames ?? []).Concat(destructibles.SelectMany(preset => preset.FxNames))
                     .Distinct(StringComparer.Ordinal))
        {
            start.ArgumentList.Add("--fx");
            start.ArgumentList.Add(name);
        }
        foreach (string name in destructibles.SelectMany(preset => preset.AnimationNames).Distinct(StringComparer.Ordinal))
        {
            start.ArgumentList.Add("--xanim");
            start.ArgumentList.Add(name);
        }
        string[] precacheRawFiles = destructibles.Select(preset => preset.PrecacheRawFileName)
            .OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
        if (precacheRawFiles.Length != 0)
        {
            string bootstrap = Path.Combine(Path.GetDirectoryName(linkerPath) ?? AppContext.BaseDirectory,
                "bootstrap", "ps3");
            foreach (string name in precacheRawFiles.Append("animtrees/destructibles.atr"))
            {
                string source = Path.Combine(emitterAssetDirectory, name);
                if (!File.Exists(source)) source = Path.Combine(bootstrap, name);
                if (!File.Exists(source))
                    throw new FileNotFoundException($"Destructible RawFile '{name}' is missing from the asset library and bootstrap.", source);
                start.ArgumentList.Add("--rawfile");
                start.ArgumentList.Add($"{name}={Path.GetFullPath(source)}");
            }
        }
        foreach (string name in (emitters?.SoundNames ?? []).Concat(destructibles.SelectMany(preset => preset.SoundNames))
                     .Distinct(StringComparer.Ordinal))
        {
            start.ArgumentList.Add("--sound");
            start.ArgumentList.Add(soundVariantPaths.TryGetValue(name, out string? path)
                ? $"{name}={path}" : name);
        }
        foreach (var rawFile in mapRawFiles)
        {
            start.ArgumentList.Add("--rawfile");
            start.ArgumentList.Add($"{rawFile.Name}={rawFile.Path}");
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
