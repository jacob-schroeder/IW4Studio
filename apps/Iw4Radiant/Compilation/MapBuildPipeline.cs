using System.ComponentModel;
using System.Diagnostics;
using D3dbspLinker.Conversion;
using IW4.Formats.SourceFormat.Character;
using IW4.Formats.SourceFormat.Material;
using IW4.Formats.SourceFormat.Physics;
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
        string emitterAssetDirectory, string outputFolder, IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        sourcePath = Path.GetFullPath(sourcePath);
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
        string assetName = $"maps/mp/{mapName}.d3dbsp";
        string token = Guid.NewGuid().ToString("N")[..8];
        string staging = Path.Combine(outputFolder, $".{mapName}-{token}.building");
        string destination = Path.Combine(outputFolder, $"{mapName}-{DateTime.Now:yyyyMMdd-HHmmss}-{token}");
        Directory.CreateDirectory(staging);
        bool compiledBspWritten = false;
        try
        {
            progress.Report("Preparing build files…");
            string bspPath = Path.Combine(staging, mapName + ".d3dbsp");
            string fastFilePath = Path.Combine(staging, mapName + ".ff");
            IReadOnlyDictionary<string, string> soundVariantPaths = emitters?.WriteSoundVariants(staging, emitterAssetDirectory)
                ?? new Dictionary<string, string>(StringComparer.Ordinal);
            var mapRawFiles = new List<(string Name, string Path)>(emitters?.WriteTo(staging) ?? []);
            if (movingLights is not null) mapRawFiles.Add(movingLights.WriteTo(staging));
            if (fog is not null) mapRawFiles.Add(fog.WriteTo(staging));
            var expanded = PrefabLibrary.ExpandForCompilation(document, sourcePath);
            DestructiblePreset[] presets = expanded.Entities.Select(entity =>
            {
                DestructiblePresets.Validate(entity.Properties);
                return DestructiblePresets.Find(entity.Properties);
            }).OfType<DestructiblePreset>().Distinct().ToArray();
            string bootstrap = Path.Combine(AppContext.BaseDirectory, "bootstrap", "ps3");
            string[] precacheScripts = presets.Select(preset => preset.PrecacheRawFileName)
                .OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
            if (precacheScripts.Length != 0)
                foreach (string name in precacheScripts.Append("animtrees/destructibles.atr"))
                {
                    string path = Path.Combine(emitterAssetDirectory, name);
                    if (!File.Exists(path)) path = Path.Combine(bootstrap, name);
                    mapRawFiles.Add((name, path));
                }
            string[] additionalModels = expanded.Entities
                .Where(entity => entity.ClassName is "script_model" or "misc_turret")
                .Select(entity => entity.Properties["model"]).Concat(presets.SelectMany(preset => preset.ModelNames))
                .Distinct(StringComparer.Ordinal).ToArray();
            var dependencies = CreateDependencyRequest(expanded, sourcePath, emitterAssetDirectory, bootstrap,
                emitters, presets, additionalModels, mapRawFiles, soundVariantPaths, cancellationToken);
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress.Report("Checking brushes, meshes and entities…");
                MapCompiler.ValidateNavigableSource(document);
                string savedSource = Path.Combine(staging, mapName + ".map");
                MapFile.Write(document, savedSource);
                progress.Report("Checking all source asset dependencies before geometry and lighting…");
                FastFileDependencyPreflight.Validate(dependencies);
                progress.Report("Asset dependencies ready. Compiling geometry and lighting…");
                var bsp = MapCompiler.Compile(MapFile.Read(savedSource), assetName, materials, models, cancellationToken, sourcePath, progress);
                cancellationToken.ThrowIfCancellationRequested();
                progress.Report("Writing compiled BSP…");
                bsp.Write(bspPath);
                compiledBspWritten = true;
                progress.Report("Compiled BSP written.");
            }, cancellationToken);
            if (emitters is not null)
            {
                progress.Report($"Exported {emitters.FxNames.Length} FX references and {emitters.SoundNames.Length} sound aliases to map scripts.");
            }
            progress.Report("Compiling source assets and included startup assets; linking the PS3 fastfile…");
            await RunLinkerAsync(bspPath, assetName, fastFilePath,
                dependencies, additionalModels, progress, cancellationToken);
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
            if (Directory.Exists(staging))
            {
                if (compiledBspWritten)
                {
                    string retained = destination + "-unpackaged";
                    Directory.Move(staging, retained);
                    progress.Report($"Packaging did not finish. Compiled BSP and build inputs retained in {retained}");
                }
                else Directory.Delete(staging, recursive: true);
            }
        }
    }

    private static FastFileDependencyPreflightRequest CreateDependencyRequest(MapDocument document,
        string sourcePath, string assetDirectory, string bootstrapDirectory, MapEmitterScripts? emitters,
        IReadOnlyList<DestructiblePreset> presets, IReadOnlyCollection<string> additionalModels,
        IReadOnlyList<(string Name, string Path)> rawFiles, IReadOnlyDictionary<string, string> soundVariants,
        CancellationToken cancellationToken)
    {
        MapBrush[] glass = document.Brushes.Where(BrushGlass.IsGlass).ToArray();
        string[] materialNames = document.Brushes.Where(brush => !BrushGlass.IsGlass(brush))
            .SelectMany(brush => brush.Faces).Select(face => face.Material)
            .Concat(document.Terrains.Select(terrain => terrain.Material))
            .Concat(glass.SelectMany(BrushGlassCompiler.RequiredMaterialNames))
            .Where(name => !CaulkMaterial.IsCaulk(name) && !ClipBrushMaterial.IsCollisionOnly(name))
            .Distinct(StringComparer.Ordinal).ToArray();
        MapFactionSettings factions = MapFactionAuthoring.Read(document.World.Properties);
        return new FastFileDependencyPreflightRequest
        {
            AssetLibraryDirectory = Path.GetFullPath(assetDirectory),
            BootstrapDirectory = bootstrapDirectory,
            MaterialNames = materialNames,
            ModelNames = document.Entities.Where(entity => entity.ClassName is "misc_model" or RuntimePhysicsAuthoring.ClassName)
                .Select(entity => entity.Properties["model"]).Concat(additionalModels)
                .Distinct(StringComparer.Ordinal).ToArray(),
            PhysPresetNames = glass.Select(brush => (BrushGlass.Read(brush) ??
                    throw new InvalidDataException("Configure glass settings before building.")).PhysPreset)
                .Distinct(StringComparer.Ordinal).ToArray(),
            FxNames = (emitters?.FxNames ?? []).Concat(presets.SelectMany(preset => preset.FxNames))
                .Distinct(StringComparer.Ordinal).ToArray(),
            SoundNames = (emitters?.SoundNames ?? []).Concat(presets.SelectMany(preset => preset.SoundNames))
                .Distinct(StringComparer.Ordinal).ToArray(),
            XAnimNames = presets.SelectMany(preset => preset.AnimationNames).Distinct(StringComparer.Ordinal).ToArray(),
            RawFilePaths = rawFiles.ToDictionary(file => file.Name, file => Path.GetFullPath(file.Path), StringComparer.Ordinal),
            SoundDefinitionPaths = soundVariants,
            WorldProperties = document.World.Properties,
            AuthoredWaterNames = materialNames.Where(WaterMaterialAuthoring.IsAuthoredMaterialName).ToArray(),
            TurretWeaponNames = document.Entities.Where(entity => entity.ClassName == "misc_turret")
                .Select(entity => entity.Properties["weaponinfo"]).Distinct(StringComparer.Ordinal).ToArray(),
            CharacterAssetsDirectory = factions.AlliesAssaultA?.CustomAssetFolder is not null ||
                factions.AxisAssaultA?.CustomAssetFolder is not null
                ? MapFactionAuthoring.GetCharacterAssetsDirectory(sourcePath) : null,
            CancellationToken = cancellationToken
        };
    }

    private static async Task RunLinkerAsync(string bspPath,
        string assetName, string fastFilePath,
        FastFileDependencyPreflightRequest dependencies, IReadOnlyCollection<string> additionalModels,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        var start = BundledLinker.CreateStartInfo();
        foreach (string value in new[] { "build", bspPath, assetName, fastFilePath, "--compiled-lighting" }) start.ArgumentList.Add(value);
        foreach (string model in additionalModels)
        {
            start.ArgumentList.Add("--xmodel");
            start.ArgumentList.Add(model);
        }
        start.ArgumentList.Add("--asset-library");
        start.ArgumentList.Add(dependencies.AssetLibraryDirectory);
        if (dependencies.CharacterAssetsDirectory is { } characters)
        {
            start.ArgumentList.Add("--character-assets");
            start.ArgumentList.Add(characters);
        }
        // Destruction-only assets have no placed emitter/model of their own.
        // Package their roots without adding map-start FX or sound playback.
        foreach (string name in dependencies.FxNames)
        {
            start.ArgumentList.Add("--fx");
            start.ArgumentList.Add(name);
        }
        foreach (string name in dependencies.XAnimNames)
        {
            start.ArgumentList.Add("--xanim");
            start.ArgumentList.Add(name);
        }
        foreach (string name in dependencies.SoundNames)
        {
            start.ArgumentList.Add("--sound");
            start.ArgumentList.Add(dependencies.SoundDefinitionPaths.TryGetValue(name, out string? path)
                ? $"{name}={path}" : name);
        }
        foreach (var rawFile in dependencies.RawFilePaths)
        {
            start.ArgumentList.Add("--rawfile");
            start.ArgumentList.Add($"{rawFile.Key}={rawFile.Value}");
        }
        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start()) throw new InvalidOperationException("D3dbspLinker could not be started.");
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException("Cannot start the bundled D3dbspLinker.", exception);
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
}
