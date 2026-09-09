using System.Globalization;
using System.Text.RegularExpressions;
using IW4.Assets.Assets;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.RawFile;
using IW4.Assets.Assets.StringTable;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;
using IW4.Linker.D3dbsp;
using IW4.Linker.Linking;
using IW4.Linker.Packaging;
using IW4.Studio.Documents;
using MapConverter.CommandLine;
using MapConverter.Game.IW3.PC.Bootstrap;
using MapConverter.Game.IW3.PC.DynamicEntities;
using MapConverter.Game.IW3.PC.Extraction;
using MapConverter.Game.IW3.PC.Images;
using MapConverter.Game.IW3.PC.Materials;
using MapConverter.Game.IW3.PC.Models;
using MapConverter.Game.IW3.PC.Physics;
using MapConverter.Game.IW3.PC.Shaders;
using MapConverter.Game.IW3.PC.Techniques;

namespace MapConverter.Game.IW3.PC.Conversion;

internal sealed record Iw3PcMapConversionResult(
    string BackendDescription,
    string MapFastFilePath,
    string LoadFastFilePath,
    string? ImageFilePath,
    int ImageCount,
    int ExternalImageCount,
    int TechniqueSetCount,
    int ShaderCount,
    int MaterialCount,
    int XModelCount,
    int BootstrapXModelCount,
    string? BootstrapFastFilePath,
    int ExternalXModelCount,
    int DynamicEntityCount,
    int DestroyFxFallbackCount,
    IReadOnlyList<string> DestroyFxFallbackNames,
    int DestroyPiecesFallbackCount,
    IReadOnlyList<string> DestroyPiecesFallbackNames,
    int RawFileCount,
    bool ElectricBoxFallbackApplied,
    IReadOnlyList<string> FallbackImageNames);

/// <summary>
/// Converts one extracted IW3 PC custom-map graph directly into authored IW4
/// PS3 assets. A caller-selected IW4 fastfile may supply an explicitly scoped
/// bootstrap XModel closure when the source map references that stock model.
/// </summary>
internal static class Iw3PcMapConverter
{
    private const uint LanguageMask = 1;
    private const int FragmentProgramUploadCapacity = 0x1a0000;
    private const string BootstrapTntBombModelName = "mil_tntbomb_mp";
    private const string LoadBriefingMaterialName = "$levelbriefing";
    internal const string ElectricBoxRawFileName = "maps/mp/_electricbox.gsc";
    internal const string ElectricBoxFxName = "explosions/tv_explosion_mp";

    // Intact vehicle props retained without enabling the source vehicle scripts.
    private static readonly IReadOnlySet<string> WorldOnlyVehicleModels = new HashSet<string>(
        [
            "t5_veh_bus_zis154",
            "t5_veh_truck_gaz63",
            "t5_veh_gaz66_flatbed",
            "t5_veh_gaz66_canvas",
            "t5_veh_civ_smallwagon",
            "t5_veh_civ_smallwagon_blue",
            "t5_veh_police_cuba"
        ], StringComparer.Ordinal);

    private static ReadOnlySpan<byte> ElectricBoxMainPrefix =>
        "main()\r\n{\r\n"u8;

    private static ReadOnlySpan<byte> ElectricBoxMainSuffix =>
        "}\r\n\r\ndowindow("u8;

    private static ReadOnlySpan<byte> ElectricBoxLoadFxReference =>
        "\twindfx = loadfx (\"explosions/tv_explosion_mp\");"u8;

    private static ReadOnlySpan<byte> BlockCommentStart =>
        "\t/*\r\n"u8;

    private static ReadOnlySpan<byte> BlockCommentEnd =>
        "\t*/\r\n"u8;

    internal static async Task<(string MapPath, string? LoadPath, string? ImagePath, int TexturedMaterialCount, int DamageFxCount,
        IReadOnlyList<string> DefaultMaterialNames, IReadOnlyList<string> FallbackImageNames)> ConvertWorldOnlyAsync(
        MapConverterOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Game != SourceGame.Iw3 || options.Platform != SourcePlatform.Pc)
            throw new NotSupportedException("World-first conversion supports only IW3 PC input.");

        string inputPath = RequireInputFile(options.MapPath, ".ff", "map fastfile");
        string templatePath = RequireInputFile(
            options.WorldTemplatePath ?? throw new ArgumentException("A world template is required."),
            ".ff",
            "IW4 world template fastfile");
        if (PathComparer.Equals(inputPath, templatePath))
            throw new ArgumentException("The IW3 input and IW4 world template must be different files.");
        string? bootstrapPath = options.BootstrapFastFilePath is null
            ? null
            : RequireInputFile(
                options.BootstrapFastFilePath,
                ".ff",
                "IW4 world material bootstrap fastfile");
        if (bootstrapPath is not null &&
            (PathComparer.Equals(bootstrapPath, inputPath) ||
             PathComparer.Equals(bootstrapPath, templatePath)))
        {
            throw new ArgumentException(
                "The world material bootstrap must differ from the IW3 input and IW4 world template.");
        }
        string? iwdPath = options.IwdPath is null
            ? null : RequireInputFile(options.IwdPath, ".iwd", "image archive");
        string? sourceFastFileDirectory = options.SourceFastFileDirectory is null
            ? null : RequireInputDirectory(options.SourceFastFileDirectory, "IW3 PS3 fastfile directory");
        bool textured = iwdPath is not null || sourceFastFileDirectory is not null;
        if (textured != (options.ImageFileIndex is not null) ||
            options.ImageFileIndex is < 1 or > 20)
            throw new ArgumentException("World textures require an image source and an imagefile index from 1 through 20.");
        if (textured && bootstrapPath is null)
            throw new ArgumentException("World texturing requires --bootstrap-fastfile with the proven native world material.");
        if (options.LoadPath is not null && !textured)
            throw new ArgumentException("A world-first loading screen requires an image source and --imagefile-index.");
        string mapName = RequireFileStem(inputPath, "map fastfile");
        string? loadInputPath = options.LoadPath is null
            ? null : RequireLoadInput(options.LoadPath, inputPath);
        string? loadName = loadInputPath is null ? null : RequireFileStem(loadInputPath, "load fastfile");
        string outputDirectory = Path.GetFullPath(options.OutputDirectory);
        if (File.Exists(outputDirectory))
            throw new IOException($"Output directory path is an existing file: '{outputDirectory}'.");
        string outputPath = Path.Combine(outputDirectory, mapName + ".ff");
        if (File.Exists(outputPath))
            throw new IOException($"Output file '{outputPath}' already exists.");
        string? loadOutputPath = loadName is null ? null : Path.Combine(outputDirectory, loadName + ".ff");
        if (loadOutputPath is not null && (File.Exists(loadOutputPath) || Directory.Exists(loadOutputPath)))
            throw new IOException($"Output file '{loadOutputPath}' already exists.");
        string? imagePath = options.ImageFileIndex is { } imageIndex
            ? Path.Combine(outputDirectory, $"imagefile{imageIndex}.pak") : null;
        if (imagePath is not null && (File.Exists(imagePath) || Directory.Exists(imagePath)))
            throw new IOException($"Output file '{imagePath}' already exists.");

        string nativeConverter = Iw3PcExtractionToolLocator.FindNativeMapConverter();
        string worldLinker = Iw3PcExtractionToolLocator.FindWorldLinker();
        string scratchDirectory = Path.Combine(
            Path.GetTempPath(),
            "mapconverter-world-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratchDirectory);
        string? stagedMapPath = null;
        string? stagedLoadPath = null;
        string? stagedImagePath = null;
        try
        {
            string bspPath = Path.Combine(scratchDirectory, mapName + ".d3dbsp");
            string dynamicEntityPath = Path.Combine(scratchDirectory, mapName + ".dynents");
            string modelCollisionPath = Path.Combine(scratchDirectory, mapName + ".modelcoll");
            string worldExtractionOutput = await Iw3PcExtractionBackend.RunToolAsync(
                "Native IW3 world extraction",
                nativeConverter,
                scratchDirectory,
                [
                    inputPath, bspPath,
                    dynamicEntityPath,
                    modelCollisionPath
                ],
                cancellationToken).ConfigureAwait(false);

            int texturedMaterialCount = 0;
            IReadOnlyList<string> defaultMaterialNames = [];
            IReadOnlyList<string> fallbackImageNames = [];
            IReadOnlyList<string> hudMaterialNames = [];
            string? hudScriptPath = null;
            Iw3WorldFxCompilation? damageFx = null;
            Iw3WorldObjectiveCompilation? objectives = null;
            IReadOnlyList<GfxLightmapArray> lightmaps = [];
            GfxImageAsset? outdoorImage = null;
            string? outdoorLookupMatrix = null;
            string? materialProviderPath = bootstrapPath;
            if (textured)
            {
                string assetDirectory = Path.Combine(scratchDirectory, "world-assets");
                await Iw3PcExtractionBackend.ExtractWorldAssetsAsync(
                    Iw3PcExtractionToolLocator.FindUnlinker(), inputPath, assetDirectory,
                    scratchDirectory, iwdPath, cancellationToken).ConfigureAwait(false);
                Iw3DynamicEntitySourceData dynamicEntities = Iw3DynamicEntityReader.Read(dynamicEntityPath);
                ValidateDynamicEntityMapName(dynamicEntities.MapName, mapName);
                damageFx = Iw3WorldFxCompiler.Compile(assetDirectory, dynamicEntities, mapName, scratchDirectory);
                objectives = Iw3WorldObjectiveCompiler.Compile(bspPath, mapName, scratchDirectory);
                (string AssetDirectory, string Name, string OutputPath)? loadZone = null;
                if (loadInputPath is not null && loadName is not null)
                {
                    string loadAssetDirectory = Path.Combine(scratchDirectory, "load-assets");
                    await Iw3PcExtractionBackend.ExtractWorldAssetsAsync(
                        Iw3PcExtractionToolLocator.FindUnlinker(), loadInputPath, loadAssetDirectory,
                        scratchDirectory, iwdPath, cancellationToken).ConfigureAwait(false);
                    stagedLoadPath = Path.Combine(scratchDirectory, loadName + ".ff");
                    loadZone = (loadAssetDirectory, loadName, stagedLoadPath);
                }
                string imageDirectory = Path.Combine(assetDirectory, "images");
                string atlasCountText = ReadWorldExtractionValue(
                    worldExtractionOutput, "source-lightmap-atlases-linker-owned");
                if (!int.TryParse(atlasCountText, NumberStyles.None, CultureInfo.InvariantCulture,
                        out int atlasCount) || atlasCount is < 0 or > 31)
                    throw new InvalidDataException("Native world extraction returned an invalid lightmap atlas count.");
                lightmaps = Iw3Iwi6LightingCompiler.CompileDirectory(imageDirectory, atlasCount);
                string outdoorName = ReadWorldExtractionValue(worldExtractionOutput, "outdoor-image");
                outdoorImage = Iw3Iwi6LightingCompiler.CompileOutdoor(
                    ResolveAssetFile(assetDirectory, "images", outdoorName, ".iwi", "outdoor image"));
                outdoorLookupMatrix = ReadWorldExtractionValue(worldExtractionOutput, "outdoor-lookup-matrix");
                string[] matrixElements = outdoorLookupMatrix.Split(',');
                if (matrixElements.Length != 16 || matrixElements.Any(value =>
                        !float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
                            out float element) || !float.IsFinite(element)))
                    throw new InvalidDataException("Native world extraction returned an invalid outdoor lookup matrix.");
                materialProviderPath = Path.Combine(scratchDirectory, "world-materials.ff");
                stagedImagePath = Path.Combine(scratchDirectory, "world-images.pak");
                (texturedMaterialCount, defaultMaterialNames, fallbackImageNames, hudMaterialNames, hudScriptPath) =
                    BuildWorldTextureProvider(bspPath, modelCollisionPath, assetDirectory, mapName,
                        bootstrapPath ?? throw new InvalidOperationException("Missing material bootstrap."),
                        iwdPath, sourceFastFileDirectory,
                        options.ImageFileIndex ?? throw new InvalidOperationException("Missing imagefile index."),
                        materialProviderPath, stagedImagePath, lightmaps, outdoorImage, loadZone, damageFx, objectives);
            }
            stagedMapPath = Path.Combine(scratchDirectory, mapName + ".ff");
            List<string> linkerArguments =
            [
                "to-fastfile", bspPath, templatePath,
                $"maps/mp/{mapName}.d3dbsp", stagedMapPath, "--world-only",
                "--xmodel", "com_plasticcase_beige_big",
                "--xmodel", "com_laptop_2_open",
                "--xmodel", "com_cellphone_on",
                "--xmodel", "com_bomb_objective",
                "--xmodel", "com_bomb_objective_d",
                "--xmodel", BootstrapTntBombModelName
            ];
            if (textured)
            {
                linkerArguments.Add("--source-materials");
                linkerArguments.AddRange(["--sound", "ui_pulse_text_type", "--sound", "ui_pulse_text_delete"]);
                linkerArguments.Add(bootstrapPath ?? throw new InvalidOperationException("Missing world bootstrap."));
                if (damageFx is not null)
                {
                    foreach (string effectName in damageFx.EffectNames)
                    {
                        linkerArguments.Add("--fx");
                        linkerArguments.Add(effectName);
                    }
                    foreach (string soundName in damageFx.SoundNames)
                    {
                        linkerArguments.Add("--sound");
                        linkerArguments.Add(soundName);
                    }
                    // Models already referenced by the world receive their asset
                    // row there; a later explicit root would duplicate that owner.
                    IReadOnlyList<string> worldModelNames = Iw3StaticModelNameReader
                        .ReadFromConvertedD3dbsp(bspPath, WorldOnlyVehicleModels).StaticModelNames;
                    foreach (string modelName in damageFx.ModelNames.Except(worldModelNames, StringComparer.Ordinal))
                    {
                        linkerArguments.Add("--xmodel");
                        linkerArguments.Add(modelName);
                    }
                    foreach ((string name, string path) in damageFx.RawFilePaths)
                    {
                        linkerArguments.Add("--rawfile");
                        linkerArguments.Add(name + "=" + path);
                    }
                }
                if (objectives is not null)
                {
                    HashSet<string> linkedModelNames = linkerArguments.Zip(linkerArguments.Skip(1))
                        .Where(pair => pair.First == "--xmodel").Select(pair => pair.Second)
                        .Concat(Iw3StaticModelNameReader.ReadFromConvertedD3dbsp(bspPath, WorldOnlyVehicleModels).StaticModelNames)
                        .ToHashSet(StringComparer.Ordinal);
                    foreach (string modelName in objectives.ModelNames.Where(linkedModelNames.Add))
                        linkerArguments.AddRange(["--xmodel", modelName]);
                    foreach (string materialName in objectives.MaterialNames)
                        linkerArguments.AddRange(["--material", materialName]);
                    linkerArguments.AddRange(["--rawfile", objectives.RawFileName + "=" + objectives.Path]);
                }
                foreach (string modelName in WorldOnlyVehicleModels.Order(StringComparer.Ordinal))
                {
                    linkerArguments.Add("--static-script-model");
                    linkerArguments.Add(modelName);
                }
                foreach (string materialName in hudMaterialNames)
                {
                    linkerArguments.Add("--material");
                    linkerArguments.Add(materialName);
                }
                if (hudScriptPath is not null)
                {
                    linkerArguments.Add("--rawfile");
                    linkerArguments.Add($"maps/mp/{mapName}.gsc={hudScriptPath}");
                }
                foreach (GfxLightmapArray lightmap in lightmaps)
                {
                    linkerArguments.Add("--lightmap");
                    linkerArguments.Add(lightmap.Primary?.Name ?? throw new InvalidDataException("Missing primary atlas name."));
                    linkerArguments.Add(lightmap.Secondary?.Name ?? throw new InvalidDataException("Missing secondary atlas name."));
                }
                linkerArguments.Add("--outdoor-image");
                linkerArguments.Add(outdoorImage?.Name ?? throw new InvalidDataException("Missing outdoor image name."));
                linkerArguments.Add("--outdoor-lookup-matrix");
                linkerArguments.Add(outdoorLookupMatrix ?? throw new InvalidDataException("Missing outdoor lookup matrix."));
            }
            // The intermediate provider now owns the pixels; retain only its
            // path and the image names while the separate linker runs.
            lightmaps = [];
            outdoorImage = null;
            if (materialProviderPath is not null)
            {
                linkerArguments.Add("--provider-fastfile");
                linkerArguments.Add(materialProviderPath);
            }
            Directory.CreateDirectory(outputDirectory);
            await Iw3PcExtractionBackend.RunToolAsync(
                "IW4 world-first fastfile linking",
                worldLinker,
                scratchDirectory,
                linkerArguments,
                cancellationToken).ConfigureAwait(false);
            if (!File.Exists(stagedMapPath) || new FileInfo(stagedMapPath).Length == 0)
                throw new InvalidDataException("World-first linking did not produce a fastfile.");
            PublishOutputs((stagedMapPath, outputPath),
                stagedLoadPath is not null && loadOutputPath is not null
                    ? (stagedLoadPath, loadOutputPath) : null,
                stagedImagePath is not null && imagePath is not null
                    ? (stagedImagePath, imagePath) : null);
            stagedMapPath = null;
            stagedLoadPath = null;
            stagedImagePath = null;
            return (outputPath, loadOutputPath, imagePath, texturedMaterialCount, damageFx?.EffectNames.Count ?? 0,
                defaultMaterialNames, fallbackImageNames);
        }
        finally
        {
            TryDeleteDirectory(scratchDirectory);
        }
    }

    private static string ReadWorldExtractionValue(string output, string field)
    {
        string prefix = field + ": ";
        string[] values = output.Split('\n', StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith(prefix, StringComparison.Ordinal))
            .Select(line => line[prefix.Length..]).ToArray();
        if (values.Length != 1 || string.IsNullOrWhiteSpace(values[0]))
            throw new InvalidDataException($"Native world extraction must provide one '{field}' value.");
        return values[0];
    }

    private static (int TexturedMaterialCount, IReadOnlyList<string> DefaultMaterialNames,
        IReadOnlyList<string> FallbackImageNames,
        IReadOnlyList<string> HudMaterialNames,
        string? HudScriptPath) BuildWorldTextureProvider(
        string bspPath, string modelCollisionPath, string assetDirectory, string mapName, string bootstrapPath,
        string? iwdPath, string? sourceFastFileDirectory, int imageFileIndex,
        string providerPath, string imagePath, IReadOnlyList<GfxLightmapArray> lightmaps,
        GfxImageAsset outdoorImage,
        (string AssetDirectory, string Name, string OutputPath)? loadZone,
        Iw3WorldFxCompilation damageFx, Iw3WorldObjectiveCompilation? objectives)
    {
        IReadOnlyList<string> worldNames = D3dbspAssetLinker.ReadWorldMaterialNames(bspPath);
        Iw3ZoneManifest manifest = Iw3ZoneManifest.Read(
            Path.Combine(assetDirectory, "zone_source", mapName + ".zone"));
        IReadOnlyList<string> staticModelNames = Iw3StaticModelNameReader
            .ReadFromConvertedD3dbsp(bspPath, WorldOnlyVehicleModels).StaticModelNames;
        staticModelNames = staticModelNames.Concat(damageFx.ModelNames).Distinct(StringComparer.Ordinal).ToArray();
        HashSet<string> ownedModelNames = OwnedNames(manifest, "xmodel")
            .ToHashSet(StringComparer.Ordinal);
        string[] unownedModelNames = staticModelNames.Where(name => !ownedModelNames.Contains(name)).ToArray();
        if (unownedModelNames.Length != 0)
            throw new InvalidDataException("The converted d3dbsp contains static XModels without owned IW3 definitions: " +
                string.Join(", ", unownedModelNames));
        Iw3XModelCollisionSourceData modelCollisionSource = Iw3XModelCollisionReader.Read(modelCollisionPath);
        ValidateDynamicEntityMapName(modelCollisionSource.MapName, mapName);
        ValidateModelCollisionSource(modelCollisionSource, staticModelNames, manifest);
        IReadOnlyList<string> modelMaterialNames = Iw3XModelExportImporter.ReadReferencedMaterialNames(
            assetDirectory, staticModelNames);
        Dictionary<string, string> modelSourcesByMaterial = modelMaterialNames
            .ToDictionary(ToIw4MaterialName, StringComparer.Ordinal);
        HashSet<string> ownedMaterialNames = OwnedNames(manifest, "material")
            .ToHashSet(StringComparer.Ordinal);
        var ownedSources = ownedMaterialNames.GroupBy(name =>
                D3dbspAssetLinker.GetWorldMaterialName(name.StartsWith("wc/", StringComparison.Ordinal)
                    ? name[3..] : name), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        using FastFileWorkspace bootstrap = new FastFileDocumentService().Open(
            new FastFileDocumentOpenRequest(bootstrapPath, Isolated.Instance));
        MaterialAsset[] nativeTemplates = bootstrap.LoadedZone.Context.AssetPool.Slots
            .Select(slot => slot.CanonicalAsset).OfType<MaterialAsset>()
            .Where(material => !IsExternal(material)).ToArray();
        MaterialAsset defaultTemplate = nativeTemplates.Single(material => material.Info.Name == "w/$default3d");
        MaterialAsset defaultModelTemplate = nativeTemplates.Single(material => material.Info.Name == "mc/lambert1");
        if (bootstrap.LoadedZone.Header.ImageStreamEntries.Any(entry =>
                !entry.IsEmpty && entry.FileIndex == imageFileIndex))
            throw new InvalidDataException("The material bootstrap already references the selected imagefile index.");
        var templatesByMaterial = new Dictionary<string, MaterialAsset>(StringComparer.Ordinal);
        var bindingsByMaterial = new Dictionary<string, Iw3IwdImageRequest[]>(StringComparer.Ordinal);
        var statesByMaterial = new Dictionary<string, GfxStateBits>(StringComparer.Ordinal);
        var modelInspectionsByMaterial = new Dictionary<string, Iw3MaterialSourceInspection>(StringComparer.Ordinal);
        var waterSourcesByMaterial = new Dictionary<string, string>(StringComparer.Ordinal);
        var requirements = new Dictionary<string, ImageRequirement>(StringComparer.OrdinalIgnoreCase);
        var defaultMaterialNames = new List<string>();
        (string? compassMaterialName, float? compassMaxRange) = ReadSourceCompass(assetDirectory, mapName);
        Iw3IwdImageRequest? compassImageRequest = null;
        string[] hudMaterialNames = ReadCallsignMaterialNames(bootstrap, nativeTemplates);
        if (compassMaterialName is not null)
        {
            if (!ownedMaterialNames.Contains(compassMaterialName))
                throw new InvalidDataException($"The source minimap material '{compassMaterialName}' is not owned by the map.");
            string compassPath = ResolveAssetFile(assetDirectory, "materials", compassMaterialName, ".json", "minimap material");
            using FileStream compassStream = File.OpenRead(compassPath);
            Iw3MaterialSourceInspection compass = Iw3MaterialCompiler.InspectSource(compassMaterialName, compassStream);
            if (compass.SourceTechniqueSetName != "2d" || compass.ImageRequirements.Count != 1 ||
                compass.ImageRequirements[0].Semantic != TextureSemantic.TwoDimensional)
                throw new InvalidDataException($"The source minimap '{compassMaterialName}' requires one native-2d-compatible image.");
            compassImageRequest = compass.ImageRequirements[0] with { UseSrgbReads = true };
            AddImageRequirement(requirements, compassImageRequest, null);
            // The generated world-first script retains the existing Rangers/Spetsnaz factions.
            hudMaterialNames = [compassMaterialName, .. hudMaterialNames, "faction_128_rangers", "faction_128_rangers_fade",
                "faction_128_ussr", "faction_128_ussr_fade"];
            foreach (string name in hudMaterialNames.Skip(1))
            {
                if (!nativeTemplates.Any(material => material.Info.Name == name))
                    throw new InvalidDataException($"The material bootstrap does not own the native HUD material '{name}'.");
            }
        }
        string[] materialNames = [.. worldNames, .. modelSourcesByMaterial.Keys];
        foreach (string targetName in materialNames)
        {
            bool isModel = modelSourcesByMaterial.TryGetValue(targetName, out string? modelSourceName);
            MaterialAsset fallbackTemplate = isModel ? defaultModelTemplate : defaultTemplate;
            templatesByMaterial.Add(targetName, fallbackTemplate);
            if (isModel && targetName is "mc/mtl_small_hatchback_wagon_blue" or
                "mc/mtl_small_hatchback_wagon_white" or "mc/mtl_small_hatchback_wagon_rubber" or
                "mc/mtl_small_hatchback_wagon_windows")
            {
                // These source cars share the native hatchback texture layout, but
                // their stock IW3 pixels are absent. Keep the complete MW2 material
                // contract, including its specular variant, on the source geometry.
                string nativeName = "m/" + targetName[3..];
                templatesByMaterial[targetName] = nativeTemplates.SingleOrDefault(
                    material => material.Info.Name == nativeName) ??
                    throw new InvalidDataException($"The bootstrap must own native vehicle material '{nativeName}' and its dependencies.");
                continue;
            }
            string[] sourceNames;
            if (isModel)
                sourceNames = modelSourceName is not null && ownedMaterialNames.Contains(modelSourceName) ? [modelSourceName] : [];
            else
                sourceNames = ownedSources.GetValueOrDefault(targetName) ?? [];
            if (sourceNames.Length == 0)
            {
                defaultMaterialNames.Add(targetName + " (no owned source material)");
                continue;
            }
            if (sourceNames.Length != 1)
                throw new InvalidDataException($"More than one source material maps to '{targetName}'.");
            string sourcePath = ResolveAssetFile(
                assetDirectory, "materials", sourceNames[0], ".json", "material");
            using FileStream stream = File.OpenRead(sourcePath);
            Iw3MaterialSourceInspection inspection = Iw3MaterialCompiler.InspectSource(targetName, stream);
            if (isModel)
                modelInspectionsByMaterial.Add(targetName, inspection);
            stream.Position = 0;
            GfxStateBits sourceState = Iw3MaterialCompiler.ReadWorldState(targetName, stream);
            MaterialAsset? selected = isModel
                ? Iw3MaterialCompiler.SelectModelTemplate(inspection, sourceState, nativeTemplates)
                : Iw3MaterialCompiler.SelectWorldTemplate(inspection, sourceState, nativeTemplates);
            MaterialAsset template = selected ?? fallbackTemplate;
            templatesByMaterial[targetName] = template;
            if (selected is null)
                defaultMaterialNames.Add(targetName + $" (no compatible native {inspection.SourceTechniqueSetName} profile)");
            else
            {
                statesByMaterial.Add(targetName, sourceState);
                if (isModel && (inspection.SourceTechniqueSetName is "mc_l_sm_a0c0" or "m_l_sm_a0c0") &&
                    template.TechniqueSet?.Name == "effect_add")
                {
                    defaultMaterialNames.Add(targetName +
                        $" (native effect_add approximation of {inspection.SourceTechniqueSetName}; source-lit response is not preserved)");
                }
                if (template.Textures.Any(texture => texture.Semantic == TextureSemantic.WaterMap))
                    waterSourcesByMaterial.Add(targetName, sourcePath);
            }
            // A sky's cube image, sampler, and native shaders form one contract.
            // Do not replace it with an unrelated source projection or fallback.
            if ((template.Info.GameFlags & MaterialGameFlags.Sky) != 0)
                continue;
            Iw3IwdImageRequest[] colors = inspection.ImageRequirements
                .Where(image => image.Semantic == TextureSemantic.ColorMap).ToArray();
            if (colors.Length != 1)
            {
                defaultMaterialNames.Add(targetName + " (no unambiguous color image)");
                continue;
            }
            Iw3IwdImageRequest[] bindings = inspection.ImageRequirements
                .Where(image => image.Semantic is TextureSemantic.ColorMap or TextureSemantic.NormalMap or TextureSemantic.SpecularMap or TextureSemantic.WaterMap)
                .Where(image => template.Textures.Count(texture => texture.Semantic == image.Semantic) == 1)
                .Select(image =>
                {
                    if (image.Semantic == TextureSemantic.WaterMap)
                        return image;
                    GfxImageAsset nativeImage = template.Textures.Single(
                        texture => texture.Semantic == image.Semantic).Image ??
                        throw new InvalidDataException($"Native material '{template.Info.Name}' has no '{image.Semantic}' image.");
                    if (IsExternal(nativeImage))
                        return image;
                    // Native shaders may linearize sampled RGB themselves.
                    // Replacement pixels retain the binding's hardware gamma contract.
                    return image with { UseSrgbReads = nativeImage.UsesSrgbReads };
                })
                .ToArray();
            if (bindings.GroupBy(image => image.Semantic).Any(group => group.Count() != 1))
                throw new InvalidDataException($"Material '{targetName}' has ambiguous source texture bindings.");
            bindingsByMaterial.Add(targetName, bindings);
            foreach (Iw3IwdImageRequest binding in bindings)
                AddImageRequirement(requirements, binding, binding.Semantic == TextureSemantic.WaterMap
                    ? inspection.WaterImageRequirements.Single(water =>
                        string.Equals(water.ImageName, binding.ImageName, StringComparison.OrdinalIgnoreCase))
                    : null);
        }

        var imageSources = new List<(Iw3ZoneManifest Manifest, string AssetDirectory)>
        {
            (manifest, assetDirectory)
        };
        var loadImageNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        RawFileAsset[] loadRawFiles = [];
        if (loadZone is { } loadInput)
        {
            Iw3ZoneManifest loadManifest = Iw3ZoneManifest.Read(
                Path.Combine(loadInput.AssetDirectory, "zone_source", loadInput.Name + ".zone"));
            InspectMaterials([CreateLoadMaterialSource(loadInput.AssetDirectory, loadManifest)],
                out Dictionary<string, ImageRequirement> loadRequirements, out _, out loadImageNames);
            foreach (ImageRequirement requirement in loadRequirements.Values)
            {
                if (requirement.Semantic != TextureSemantic.TwoDimensional)
                    throw new InvalidDataException("The loadscreen requires a two-dimensional source image.");
                AddImageRequirement(requirements,
                    new Iw3IwdImageRequest(requirement.Name, requirement.Semantic, requirement.UseSrgbReads), null);
            }
            imageSources.Add((loadManifest, loadInput.AssetDirectory));
            loadRawFiles = CompileRawFiles(loadInput.AssetDirectory, loadManifest, loadInput.Name, out _);
        }
        ImageCompilationGraph images = CompileImages(requirements,
            imageSources, iwdPath, sourceFastFileDirectory, imageFileIndex,
            useFallbackImages: true);
        if (loadZone is { } loadOutput)
            File.WriteAllBytes(loadOutput.OutputPath,
                BuildLoadFastFile(loadOutput.Name, loadImageNames, images, loadRawFiles));
        var materials = new List<MaterialAsset>(materialNames.Length);
        var modelMappings = new Dictionary<string, Iw3XModelMaterialMapping>(StringComparer.Ordinal);
        int texturedMaterialCount = 0;
        foreach (string targetName in materialNames)
        {
            bool isModel = modelSourcesByMaterial.TryGetValue(targetName, out string? modelSourceName);
            MaterialAsset template = templatesByMaterial[targetName];
            var replacements = new Dictionary<TextureSemantic, GfxImageAsset>();
            if (bindingsByMaterial.TryGetValue(targetName, out Iw3IwdImageRequest[]? bindings))
            {
                foreach (Iw3IwdImageRequest binding in bindings)
                {
                    if (binding.Semantic == TextureSemantic.WaterMap)
                        continue;
                    GfxImageAsset image = images.AssetsBySourceName[binding.ImageName];
                    if (IsExternal(image))
                        throw new InvalidDataException($"Material '{targetName}' is missing {binding.Semantic} image '{binding.ImageName}'.");
                    replacements.Add(binding.Semantic, image);
                }
            }
            if (!isModel && (replacements.ContainsKey(TextureSemantic.ColorMap) || (template.Info.GameFlags & MaterialGameFlags.Sky) != 0))
                texturedMaterialCount++;
            MaterialAsset? sourceWaterMaterial = null;
            if (waterSourcesByMaterial.TryGetValue(targetName, out string? waterSourcePath))
            {
                using FileStream stream = File.OpenRead(waterSourcePath);
                sourceWaterMaterial = Iw3MaterialCompiler.Compile(
                    targetName, stream, images.AssetsBySourceName,
                    requiresRuntimeTechniqueState: false).Material;
            }
            GfxStateBits? state = statesByMaterial.GetValueOrDefault(targetName);
            MaterialAsset material = isModel
                ? Iw3MaterialCompiler.CompileModelMaterial(targetName, template, replacements, state,
                    modelInspectionsByMaterial.GetValueOrDefault(targetName))
                : Iw3MaterialCompiler.CompileWorldMaterial(targetName, template, replacements, state, sourceWaterMaterial);
            materials.Add(material);
            if (modelSourceName is not null)
                modelMappings.Add(modelSourceName, new Iw3XModelMaterialMapping(material, InvHighMipRadius: 0));
        }
        string? hudScriptPath = null;
        if (compassMaterialName is not null && compassImageRequest is not null)
        {
            GfxImageAsset compassImage = images.AssetsBySourceName[compassImageRequest.ImageName];
            if (IsExternal(compassImage) || images.FallbackImageNames.Contains(compassImageRequest.ImageName, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException($"The source minimap image '{compassImageRequest.ImageName}' has no source-owned pixels.");
            materials.Add(Iw4LoadZoneBuilder.CreateBackdropMaterial(
                compassMaterialName, 0xbc00180000000000, compassImage,
                MaterialSamplerState.FilterLinear | MaterialSamplerState.MipMapNearest | MaterialSamplerState.ClampMask,
                TextureSemantic.TwoDimensional, 0x19655165,
                new MaterialTechniqueSetAsset { Name = ",2d" }, (MaterialSortKey)47));
        }
        if (compassMaterialName is not null || damageFx.EffectNames.Count != 0 || objectives is not null)
        {
            string providerDirectory = Path.GetDirectoryName(providerPath) ??
                throw new InvalidDataException("The material provider path has no output directory.");
            hudScriptPath = Path.Combine(providerDirectory, mapName + ".hud.gsc");
            string script = "main()\r\n{\r\n" +
                // Register objective model/exploder pairs before native _load
                // initializes them; native gametype callbacks run after main.
                (objectives is not null ? $"\tmaps\\mp\\{mapName}_objectives::main();\r\n" : "") +
                "\tmaps\\mp\\_load::main();\r\n" +
                (compassMaterialName is not null
                    ? $"\tmaps\\mp\\_compass::setupMiniMap(\"{compassMaterialName}\");\r\n" : "") +
                "\tgame[\"allies\"] = \"us_army\";\r\n" +
                "\tgame[\"axis\"] = \"opforce_airborne\";\r\n" +
                "\tgame[\"attackers\"] = \"allies\";\r\n" +
                "\tgame[\"defenders\"] = \"axis\";\r\n" +
                (compassMaxRange is float range
                    ? $"\tsetdvar(\"compassmaxrange\", \"{range.ToString("R", CultureInfo.InvariantCulture)}\");\r\n"
                    : "") +
                (damageFx.EffectNames.Count != 0 ? $"\tmaps\\mp\\{mapName}_fx::main();\r\n" : "") + "}\r\n";
            File.WriteAllText(hudScriptPath, script);
        }
        Iw3XModelExportImportResult models = Iw3XModelExportImporter.Import(
            assetDirectory, staticModelNames, modelMappings, modelCollisionSource.ByName,
            staticModelNames.ToHashSet(StringComparer.Ordinal));
        BaseAsset[] lightingImages = lightmaps
            .SelectMany(lightmap => new[] { lightmap.Primary, lightmap.Secondary })
            .OfType<GfxImageAsset>().Append(outdoorImage).Cast<BaseAsset>().ToArray();
        // The intermediate fastfile must own the static models, named lighting
        // images, and native faction materials consumed by the world linker.
        BaseAsset[] authoredRoots = materials.Cast<BaseAsset>()
            .Concat(models.Models).Concat(lightingImages).ToArray();
        HashSet<AssetKey> authoredKeys = authoredRoots.Select(AssetKey.FromDefinition).ToHashSet();
        MaterialAsset[] nativeMaterialRoots = nativeTemplates
            .Where(material => !authoredKeys.Contains(AssetKey.FromDefinition(material))).ToArray();
        BaseAsset[] providerRoots = [.. authoredRoots, .. nativeMaterialRoots];
        HashSet<AssetKey> nativeRootKeys = nativeMaterialRoots.Select(AssetKey.FromDefinition).ToHashSet();
        LinkAssetPool nativeProviders = bootstrap.InitialLinkRequest.Assets.WithoutProviders(
            authoredRoots.Concat(models.ModelSurfs)
                .Concat(images.AssetsBySourceName.Values.Where(image => !IsExternal(image)))
                .Select(AssetKey.FromDefinition).ToHashSet());
        byte[] providerBytes = LinkAndPackage("world-materials", providerRoots,
            images.AssetsBySourceName.Values.Cast<BaseAsset>().Concat(models.ModelSurfs).ToArray(),
            images.StreamReferencesByAsset, nativeProviders, nativeRootKeys);
        using (FileStream file = new(providerPath, FileMode.CreateNew, FileAccess.Write))
            file.Write(providerBytes);
        using (FileStream file = new(imagePath, FileMode.CreateNew, FileAccess.Write))
            file.Write((images.Package ?? throw new InvalidDataException("Missing world image package.")).Bytes.Span);
        return (texturedMaterialCount, defaultMaterialNames, images.FallbackImageNames, hudMaterialNames, hudScriptPath);
    }

    private static string[] ReadCallsignMaterialNames(
        FastFileWorkspace bootstrap, IReadOnlyList<MaterialAsset> nativeMaterials)
    {
        BaseAsset[] assets = bootstrap.LoadedZone.Context.AssetPool.Slots
            .Select(slot => slot.CanonicalAsset).OfType<BaseAsset>().ToArray();
        var names = new SortedSet<string>(StringComparer.Ordinal);
        ReadTable("mp/cardtitletable.csv", 2);
        ReadTable("mp/cardicontable.csv", 1);

        HashSet<string> ownedNames = nativeMaterials.Select(material =>
            NormalizeTargetAssetName(material.Info.Name, "native callsign material"))
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> globalReferences = assets.OfType<MaterialAsset>().Where(IsExternal)
            .Select(material => NormalizeTargetAssetName(material.Info.Name, "global callsign material"))
            .ToHashSet(StringComparer.Ordinal);
        string[] missing = names.Where(name => !ownedNames.Contains(name) && !globalReferences.Contains(name)).ToArray();
        if (missing.Length != 0)
            throw new InvalidDataException("The material bootstrap does not cover the native callsign tables: " +
                string.Join(", ", missing) + ".");

        // Explicit global references stay with the engine's common zones. Only
        // map-owned artwork needs a material root in the converted map.
        return names.Where(ownedNames.Contains).ToArray();

        void ReadTable(string name, int materialColumn)
        {
            StringTableAsset table = assets.OfType<StringTableAsset>()
                .SingleOrDefault(table => table.Name == name && !IsExternal(table)) ??
                throw new InvalidDataException($"The material bootstrap must own callsign table '{name}'.");
            if (table.ColumnCount <= materialColumn || table.RowCount <= 0 ||
                (long)table.ColumnCount * table.RowCount != table.Cells.Count)
                throw new InvalidDataException($"The native callsign table '{name}' has invalid dimensions.");
            for (int row = 0; row < table.RowCount; row++)
            {
                string? materialName = table.Cells[row * table.ColumnCount + materialColumn].String;
                if (!string.IsNullOrWhiteSpace(materialName))
                    names.Add(NormalizeTargetAssetName(materialName, $"callsign table '{name}' material"));
            }
        }
    }

    private static (string? MaterialName, float? MaxRange) ReadSourceCompass(string assetDirectory, string mapName)
    {
        string scriptPath = ResolveContainedPath(assetDirectory, Path.Combine("maps", "mp", mapName + ".gsc"), "map script");
        if (!File.Exists(scriptPath))
            return (null, null);
        string source = File.ReadAllText(scriptPath);
        // Only literal compass settings are consumed; source effects, fog, vision and other script code are not imported.
        source = Regex.Replace(source, @"/\*.*?\*/|//[^\r\n]*", "", RegexOptions.Singleline);
        MatchCollection calls = Regex.Matches(source,
            "(?m)^[ \\t]*maps\\\\mp\\\\_compass::setupMiniMap\\s*\\(\\s*\"(?<name>[A-Za-z0-9_./-]+)\"\\s*\\)\\s*;");
        if (calls.Count == 0)
            return (null, null);
        if (calls.Count != 1)
            throw new InvalidDataException("The source map script must name one unambiguous minimap material.");
        MatchCollection ranges = Regex.Matches(source,
            "(?mi)^[ \\t]*setdvar\\s*\\(\\s*\"compassmaxrange\"\\s*,\\s*(?:\"(?<range>[0-9]+(?:\\.[0-9]+)?)\"|(?<range>[0-9]+(?:\\.[0-9]+)?))\\s*\\)\\s*;");
        if (ranges.Count > 1)
            throw new InvalidDataException("The source map script has ambiguous compass range settings.");
        float? range = null;
        if (ranges.Count == 1)
        {
            if (!float.TryParse(ranges[0].Groups["range"].Value, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out float value) || !float.IsFinite(value) || value <= 0)
                throw new InvalidDataException("The source compass range must be finite and positive.");
            range = value;
        }
        return (calls[0].Groups["name"].Value, range);
    }

    public static async Task<Iw3PcMapConversionResult> ConvertAsync(
        MapConverterOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Game != SourceGame.Iw3 ||
            options.Platform != SourcePlatform.Pc)
        {
            throw new NotSupportedException(
                "This conversion path supports only IW3 PC input.");
        }

        ConversionPaths paths = ValidatePaths(options);
        Iw3PcExtractionTools tools = Iw3PcExtractionToolLocator.Find();
        string scratchDirectory = Path.Combine(
            Path.GetTempPath(),
            "mapconverter-iw3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratchDirectory);

        string? stagedMapPath = null;
        string? stagedLoadPath = null;
        string? stagedImagePath = null;
        try
        {
            Iw3PcExtractionResult extraction =
                await Iw3PcExtractionBackend.ExtractAsync(
                    new Iw3PcExtractionRequest(
                        tools.NativeMapConverterPath,
                        tools.UnlinkerPath,
                        paths.MapInputPath,
                        paths.LoadInputPath,
                        paths.IwdInputPath,
                        scratchDirectory),
                    cancellationToken).ConfigureAwait(false);

            Iw3ZoneManifest mapManifest = Iw3ZoneManifest.Read(
                extraction.MapZoneSourcePath);
            Iw3ZoneManifest loadManifest = Iw3ZoneManifest.Read(
                extraction.LoadZoneSourcePath);
            Iw3DynamicEntitySourceData dynamicEntitySource =
                Iw3DynamicEntityReader.Read(extraction.DynamicEntityPath);
            ValidateDynamicEntityMapName(dynamicEntitySource.MapName, paths.MapName);
            Iw3XModelCollisionSourceData xmodelCollisionSource =
                Iw3XModelCollisionReader.Read(extraction.XModelCollisionPath);
            ValidateDynamicEntityMapName(xmodelCollisionSource.MapName, paths.MapName);

            Iw3MapModelReferences modelReferences =
                Iw3StaticModelNameReader.ReadFromConvertedD3dbsp(
                    extraction.D3dbspPath);
            string[] dynamicModelNames = dynamicEntitySource.Definitions
                .SelectMany(definitions => definitions)
                .Select(definition => NormalizeSourceReferenceName(
                    definition.XModelName,
                    "dynamic-entity XModel"))
                .Where(name => name is not null)
                .Select(name => name!)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            ValidateModelCollisionSource(xmodelCollisionSource,
                dynamicModelNames.Concat(modelReferences.StaticModelNames), mapManifest);
            HashSet<string> ownedXModelNames = OwnedNames(
                    mapManifest,
                    "xmodel")
                .ToHashSet(StringComparer.Ordinal);
            string[] unownedStaticModelNames = modelReferences.StaticModelNames
                .Where(name => !ownedXModelNames.Contains(name))
                .ToArray();
            if (unownedStaticModelNames.Length != 0)
            {
                throw new InvalidDataException(
                    "The converted d3dbsp contains static XModels without " +
                    "owned IW3 definitions: " +
                    string.Join(", ", unownedStaticModelNames));
            }
            string[] unownedDynamicModelNames = dynamicModelNames
                .Where(name =>
                    !ownedXModelNames.Contains(name) &&
                    !string.Equals(
                        name,
                        BootstrapTntBombModelName,
                        StringComparison.Ordinal))
                .ToArray();
            if (unownedDynamicModelNames.Length != 0)
            {
                throw new InvalidDataException(
                    "The converted map contains dynamic XModels without " +
                    "owned IW3 definitions or a supported bootstrap: " +
                    string.Join(", ", unownedDynamicModelNames));
            }

            bool requiresTntBootstrap = modelReferences.NamedEntityModelNames
                .Concat(dynamicModelNames)
                .Contains(BootstrapTntBombModelName, StringComparer.Ordinal);
            if (requiresTntBootstrap && paths.BootstrapFastFilePath is null)
            {
                throw new InvalidDataException(
                    $"The converted map references stock IW4 XModel " +
                    $"'{BootstrapTntBombModelName}'. Supply mp_rust.ff through " +
                    "'--bootstrap-fastfile' so MapConverter can own its exact closure.");
            }
            if (!requiresTntBootstrap && paths.BootstrapFastFilePath is not null)
            {
                throw new ArgumentException(
                    "Option '--bootstrap-fastfile' was supplied, but this map does " +
                    $"not require '{BootstrapTntBombModelName}'.");
            }
            Iw4BootstrapXModelGraph? bootstrap = requiresTntBootstrap
                ? Iw4BootstrapXModelLoader.Load(
                    paths.BootstrapFastFilePath!,
                    [BootstrapTntBombModelName],
                    scratchDirectory)
                : null;
            if (options.ImageFileIndex is { } outputImageFileIndex &&
                bootstrap?.ReferencedImageFileIndices.Contains(
                    checked((uint)outputImageFileIndex)) == true)
            {
                throw new InvalidDataException(
                    $"The bootstrap XModel closure references stock imagefile" +
                    $"{outputImageFileIndex}.pak, which conflicts with the requested " +
                    "custom imagefile index. Select a different '--imagefile-index'.");
            }

            string[] importedModelNames = modelReferences.StaticModelNames
                .Concat(modelReferences.NamedEntityModelNames)
                .Concat(dynamicModelNames)
                .Where(name =>
                    ownedXModelNames.Contains(name) &&
                    !string.Equals(
                        name,
                        BootstrapTntBombModelName,
                        StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            IReadOnlySet<AssetKey> bootstrapModelKeys = bootstrap?.ModelKeys ??
                new HashSet<AssetKey>();
            XModelAsset[] externalEntityModels = modelReferences
                .NamedEntityModelNames
                .Concat(dynamicModelNames)
                .Where(name =>
                    !ownedXModelNames.Contains(name) &&
                    !bootstrapModelKeys.Contains(XModelKey(name)))
                .Distinct(StringComparer.Ordinal)
                .Select(name => new XModelAsset { Name = "," + name })
                .ToArray();
            IReadOnlyList<string> modelMaterialNames =
                Iw3XModelExportImporter.ReadReferencedMaterialNames(
                    extraction.MapAssetDirectory,
                    importedModelNames);

            MaterialSource[] materialSources = SelectMaterialSources(
                extraction,
                mapManifest,
                loadManifest,
                modelMaterialNames);
            InspectMaterials(
                materialSources,
                out Dictionary<string, ImageRequirement> imageRequirements,
                out HashSet<string> mapImageNames,
                out HashSet<string> loadImageNames);

            ImageCompilationGraph images = CompileImages(
                imageRequirements,
                [(mapManifest, extraction.MapAssetDirectory), (loadManifest, extraction.LoadAssetDirectory)],
                paths.IwdInputPath,
                paths.SourceFastFileDirectory,
                options.ImageFileIndex);

            TechniqueCompilationGraph techniques = CompileTechniques(
                extraction,
                mapManifest,
                materialSources);

            MaterialCompilationGraph materials = CompileMaterials(
                materialSources,
                images.AssetsBySourceName,
                techniques.AssetsByName,
                modelMaterialNames);

            Iw3XModelExportImportResult xmodels =
                Iw3XModelExportImporter.Import(
                    extraction.MapAssetDirectory,
                    importedModelNames,
                    materials.ModelMappingsBySourceName,
                    xmodelCollisionSource.ByName,
                    modelReferences.StaticModelNames.ToHashSet(StringComparer.Ordinal));

            Dictionary<string, XModelAsset> dynamicXModelsByName = xmodels.Models
                .Concat(bootstrap?.Models ?? [])
                .Concat(externalEntityModels)
                .ToDictionary(
                    model => NormalizeTargetAssetName(
                        model.Name,
                        "dynamic-entity XModel provider"),
                    StringComparer.Ordinal);
            string[] dynamicPhysPresetNames = dynamicEntitySource.Definitions
                .SelectMany(definitions => definitions)
                .Select(definition => NormalizeSourceReferenceName(
                    definition.PhysPresetName,
                    "dynamic-entity physics preset"))
                .Where(name => name is not null)
                .Select(name => name!)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            Iw3PhysPresetCompilation physPresets =
                Iw3PhysPresetCompiler.Compile(
                    extraction.MapAssetDirectory,
                    mapManifest,
                    dynamicPhysPresetNames);
            Iw3DynamicEntityCompilation dynamicEntities =
                Iw3DynamicEntityCompiler.Compile(
                    dynamicEntitySource,
                    dynamicXModelsByName,
                    physPresets.ByName);

            string mapAssetName = $"maps/mp/{paths.MapName}.d3dbsp";
            D3dbspLinkResult mapGraph = D3dbspAssetLinker.Link(
                new D3dbspLinkRequest(
                    extraction.D3dbspPath,
                    mapAssetName,
                    ForceFullbright: false,
                    FragmentProgramUploadCapacity,
                    xmodels.Models)
                {
                    DynamicEntityDefinitions = dynamicEntities.Definitions
                });

            RawFileAsset[] mapRawFiles = CompileRawFiles(
                extraction.MapAssetDirectory,
                mapManifest,
                paths.MapName,
                out bool mapElectricBoxFallbackApplied);
            RawFileAsset[] loadRawFiles = CompileRawFiles(
                extraction.LoadAssetDirectory,
                loadManifest,
                paths.LoadName,
                out bool loadElectricBoxFallbackApplied);

            BaseAsset[] mapRoots = BuildMapRoots(
                mapImageNames,
                images.AssetsBySourceName,
                techniques,
                materials,
                xmodels,
                (bootstrap?.Models ?? []).Concat(externalEntityModels).ToArray(),
                mapGraph,
                mapRawFiles,
                mapAssetName);
            BaseAsset[] mapProviderOnly =
            [
                .. xmodels.ModelSurfs,
                .. mapGraph.NestedAssets
            ];
            byte[] mapFastFile = LinkAndPackage(
                "map",
                mapRoots,
                mapProviderOnly,
                images.StreamReferencesByAsset,
                bootstrap?.Providers,
                bootstrapModelKeys);

            byte[] loadFastFile = BuildLoadFastFile(paths.LoadName, loadImageNames, images, loadRawFiles);

            Directory.CreateDirectory(paths.OutputDirectory);
            RejectExistingOutputs(paths);
            string token = Guid.NewGuid().ToString("N");
            stagedMapPath = StageOutput(
                paths.MapOutputPath,
                token,
                mapFastFile);
            stagedLoadPath = StageOutput(
                paths.LoadOutputPath,
                token,
                loadFastFile);
            if (paths.ImageOutputPath is not null)
            {
                if (images.Package is null)
                {
                    throw new InvalidDataException(
                        "Image conversion did not produce an imagefile package.");
                }

                stagedImagePath = StageOutput(
                    paths.ImageOutputPath,
                    token,
                    images.Package.Bytes.Span);
            }

            PublishOutputs(
                (stagedMapPath, paths.MapOutputPath),
                (stagedLoadPath, paths.LoadOutputPath),
                stagedImagePath is null || paths.ImageOutputPath is null
                    ? null
                    : (stagedImagePath, paths.ImageOutputPath));
            stagedMapPath = null;
            stagedLoadPath = null;
            stagedImagePath = null;

            return new Iw3PcMapConversionResult(
                extraction.BackendDescription,
                paths.MapOutputPath,
                paths.LoadOutputPath,
                paths.ImageOutputPath,
                images.OwnedImageCount +
                    (mapImageNames.Contains(loadImageNames.Single()) ? 1 : 0),
                images.AssetsBySourceName.Count - images.OwnedImageCount + 1,
                techniques.OwnedTechniqueSets.Count,
                techniques.Shaders.Count,
                materials.OwnedMaterials.Count + Iw4LoadZoneBuilder.OwnedMaterialCount,
                xmodels.Models.Count,
                bootstrap?.Models.Count ?? 0,
                paths.BootstrapFastFilePath,
                externalEntityModels.Length,
                dynamicEntities.Count,
                dynamicEntities.DestroyFxFallbackCount,
                dynamicEntities.DestroyFxFallbackNames,
                dynamicEntities.DestroyPiecesFallbackCount,
                dynamicEntities.DestroyPiecesFallbackNames,
                mapRawFiles.Length + loadRawFiles.Length,
                mapElectricBoxFallbackApplied || loadElectricBoxFallbackApplied,
                images.FallbackImageNames);
        }
        finally
        {
            DeleteIfExists(stagedMapPath);
            DeleteIfExists(stagedLoadPath);
            DeleteIfExists(stagedImagePath);
            TryDeleteDirectory(scratchDirectory);
        }
    }

    private static ConversionPaths ValidatePaths(MapConverterOptions options)
    {
        string mapPath = RequireInputFile(options.MapPath, ".ff", "map fastfile");
        string loadPath = RequireLoadInput(
            options.LoadPath ?? throw new ArgumentException("A load fastfile is required for full conversion."), mapPath);
        string mapName = RequireFileStem(mapPath, "map fastfile");
        string loadName = RequireFileStem(loadPath, "load fastfile");

        string? iwdPath = options.IwdPath is null
            ? null
            : RequireInputFile(options.IwdPath, ".iwd", "IWD archive");
        string? sourceFastFileDirectory = options.SourceFastFileDirectory is null
            ? null
            : RequireInputDirectory(
                options.SourceFastFileDirectory,
                "IW3 PS3 source fastfile directory");
        string? bootstrapFastFilePath = options.BootstrapFastFilePath is null
            ? null
            : RequireInputFile(
                options.BootstrapFastFilePath,
                ".ff",
                "IW4 bootstrap fastfile");
        if (bootstrapFastFilePath is not null &&
            !string.Equals(
                Path.GetFileName(bootstrapFastFilePath),
                "mp_rust.ff",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The IW4 bootstrap fastfile must be named 'mp_rust.ff'.");
        }
        bool hasImageSource = iwdPath is not null ||
            sourceFastFileDirectory is not null;
        if (hasImageSource != (options.ImageFileIndex is not null))
        {
            throw new ArgumentException(
                "An IWD or IW3 PS3 source fastfile directory and imagefile " +
                "index must be supplied together.");
        }
        if (options.ImageFileIndex is < 1 or > 20)
            throw new ArgumentOutOfRangeException(nameof(options.ImageFileIndex));

        string outputDirectory = Path.GetFullPath(options.OutputDirectory);
        if (File.Exists(outputDirectory))
        {
            throw new IOException(
                $"Output directory path is an existing file: '{outputDirectory}'.");
        }

        string mapOutputPath = Path.Combine(outputDirectory, mapName + ".ff");
        string loadOutputPath = Path.Combine(outputDirectory, loadName + ".ff");
        string? imageOutputPath = options.ImageFileIndex is { } imageIndex
            ? Path.Combine(outputDirectory, $"imagefile{imageIndex}.pak")
            : null;
        var result = new ConversionPaths(
            mapPath,
            loadPath,
            iwdPath,
            sourceFastFileDirectory,
            bootstrapFastFilePath,
            outputDirectory,
            mapName,
            loadName,
            mapOutputPath,
            loadOutputPath,
            imageOutputPath);
        RejectExistingOutputs(result);
        return result;
    }

    private static MaterialSource[] SelectMaterialSources(
        Iw3PcExtractionResult extraction,
        Iw3ZoneManifest mapManifest,
        Iw3ZoneManifest loadManifest,
        IReadOnlyList<string> staticModelMaterialNames)
    {
        string[] ownedMapMaterials = OwnedNames(mapManifest, "material");
        var selectedMapNames = new HashSet<string>(
            ownedMapMaterials.Where(name =>
                name.StartsWith("wc/", StringComparison.Ordinal) ||
                !name.StartsWith("mc/", StringComparison.Ordinal)),
            StringComparer.Ordinal);
        selectedMapNames.UnionWith(staticModelMaterialNames);
        var ownedMapSet = ownedMapMaterials.ToHashSet(StringComparer.Ordinal);

        var result = new List<MaterialSource>();
        foreach (string sourceName in selectedMapNames.Order(StringComparer.Ordinal))
        {
            if (!ownedMapSet.Contains(sourceName))
                continue;
            result.Add(CreateMaterialSource(
                extraction.MapAssetDirectory,
                sourceName,
                ZoneRole.Map));
        }
        result.Add(CreateLoadMaterialSource(extraction.LoadAssetDirectory, loadManifest));

        string? duplicateTarget = result
            .GroupBy(source => source.TargetName, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() != 1)?.Key;
        if (duplicateTarget is not null)
        {
            throw new InvalidDataException(
                $"More than one IW3 material maps to IW4 material '{duplicateTarget}'.");
        }
        return result.ToArray();
    }

    private static MaterialSource CreateLoadMaterialSource(string assetDirectory, Iw3ZoneManifest manifest)
    {
        string[] loadBriefingMaterials = OwnedNames(manifest, "material")
            .Where(name => string.Equals(
                ToIw4MaterialName(name),
                LoadBriefingMaterialName,
                StringComparison.Ordinal))
            .ToArray();
        if (loadBriefingMaterials.Length != 1)
        {
            throw new InvalidDataException(
                $"IW3 load zone must own exactly one '{LoadBriefingMaterialName}' " +
                "material so its custom loadscreen can be identified.");
        }
        return CreateMaterialSource(
            assetDirectory,
            loadBriefingMaterials[0],
            ZoneRole.Load);
    }

    private static MaterialSource CreateMaterialSource(
        string assetDirectory,
        string sourceName,
        ZoneRole zone)
    {
        string targetName = ToIw4MaterialName(sourceName);
        string path = ResolveAssetFile(
            assetDirectory,
            "materials",
            sourceName,
            ".json",
            "material");
        return new MaterialSource(sourceName, targetName, path, zone);
    }

    private static void InspectMaterials(
        IEnumerable<MaterialSource> materialSources,
        out Dictionary<string, ImageRequirement> imageRequirements,
        out HashSet<string> mapImageNames,
        out HashSet<string> loadImageNames)
    {
        imageRequirements = new Dictionary<string, ImageRequirement>(
            StringComparer.OrdinalIgnoreCase);
        mapImageNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        loadImageNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (MaterialSource source in materialSources)
        {
            using FileStream stream = File.OpenRead(source.Path);
            Iw3MaterialSourceInspection inspection =
                Iw3MaterialCompiler.InspectSource(source.TargetName, stream);
            source.Inspection = inspection;
            Dictionary<string, Iw3WaterImageRequest> waterByName =
                inspection.WaterImageRequirements.ToDictionary(
                    request => request.ImageName,
                    StringComparer.OrdinalIgnoreCase);
            HashSet<string> zoneImages = source.Zone == ZoneRole.Map
                ? mapImageNames
                : loadImageNames;
            foreach (Iw3IwdImageRequest request in inspection.ImageRequirements)
            {
                zoneImages.Add(request.ImageName);
                waterByName.TryGetValue(
                    request.ImageName,
                    out Iw3WaterImageRequest? water);
                AddImageRequirement(imageRequirements, request, water);
            }
        }
    }

    private static void AddImageRequirement(
        IDictionary<string, ImageRequirement> requirements,
        Iw3IwdImageRequest request,
        Iw3WaterImageRequest? water)
    {
        if ((request.Semantic == TextureSemantic.WaterMap) != (water is not null))
            throw new InvalidDataException($"Image '{request.ImageName}' has inconsistent water metadata.");
        var requirement = new ImageRequirement(request.ImageName, request.Semantic,
            request.UseSrgbReads, water?.Width, water?.Height);
        if (requirements.TryGetValue(request.ImageName, out ImageRequirement? existing))
        {
            if (existing.Semantic != requirement.Semantic ||
                existing.UseSrgbReads != requirement.UseSrgbReads ||
                existing.WaterWidth != requirement.WaterWidth ||
                existing.WaterHeight != requirement.WaterHeight)
                throw new InvalidDataException($"Image '{request.ImageName}' is used with conflicting texture semantics or sRGB policy.");
            return;
        }
        requirements.Add(request.ImageName, requirement);
    }

    private static ImageCompilationGraph CompileImages(
        IReadOnlyDictionary<string, ImageRequirement> requirements,
        IReadOnlyList<(Iw3ZoneManifest Manifest, string AssetDirectory)> sources,
        string? iwdPath,
        string? sourceFastFileDirectory,
        int? imageFileIndex,
        bool useFallbackImages = false)
    {
        ImageRequirement[] ordered = requirements.Values
            .OrderBy(requirement => requirement.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(requirement => requirement.Name, StringComparer.Ordinal)
            .ToArray();
        HashSet<string> ownedSourceImages = sources.SelectMany(source => OwnedNames(source.Manifest, "image"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> externalSourceImages = sources.SelectMany(source => ReferenceNames(source.Manifest, "image"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        GfxImageAsset[] waterImages = ordered
            .Where(requirement =>
                requirement.Semantic == TextureSemantic.WaterMap)
            .Select(requirement => Iw3WaterImageCompiler.Compile(
                requirement.Name,
                requirement.WaterWidth ?? throw new InvalidDataException(
                    $"Water image '{requirement.Name}' has no width."),
                requirement.WaterHeight ?? throw new InvalidDataException(
                    $"Water image '{requirement.Name}' has no height.")))
            .ToArray();
        Iw3IwdImageRequest[] streamableRequests = ordered
            .Where(requirement =>
                requirement.Semantic != TextureSemantic.WaterMap)
            .Select(requirement => new Iw3IwdImageRequest(
                requirement.Name,
                requirement.Semantic,
                requirement.UseSrgbReads))
            .ToArray();
        var fullImagesByName = new Dictionary<
            string,
            Iw3Iwi6StreamedImageCompilation>(StringComparer.OrdinalIgnoreCase);

        void AddCompilation(Iw3Iwi6StreamedImageCompilation compilation)
        {
            string name = compilation.Image.Name ??
                throw new InvalidDataException("A compiled image has no name.");
            if (!fullImagesByName.TryAdd(name, compilation))
                throw new InvalidDataException($"Image '{name}' was compiled more than once.");
        }

        if (iwdPath is not null)
        {
            IReadOnlyList<Iw3Iwi6StreamedImageCompilation> iwdImages =
                Iw3IwdImageCompiler.Compile(
                iwdPath,
                streamableRequests);
            foreach (Iw3Iwi6StreamedImageCompilation compilation in iwdImages)
                AddCompilation(compilation);
        }

        if (imageFileIndex is not null)
        {
            foreach (Iw3IwdImageRequest request in streamableRequests)
            {
                if (fullImagesByName.ContainsKey(request.ImageName))
                    continue;
                byte[]? iwi = ReadExtractedImage(
                    request.ImageName,
                    sources.Select(source => source.AssetDirectory));
                if (iwi is null)
                    continue;
                AddCompilation(Iw3Iwi6StreamedImageCompiler.Compile(
                    request.ImageName,
                    iwi,
                    request.Semantic,
                    request.UseSrgbReads));
            }
        }

        if (sourceFastFileDirectory is not null)
        {
            Iw3IwdImageRequest[] unresolvedRequests = streamableRequests
                .Where(request =>
                    !fullImagesByName.ContainsKey(request.ImageName))
                .ToArray();
            if (unresolvedRequests.Length != 0)
            {
                IReadOnlyList<Iw3Iwi6StreamedImageCompilation> sourceFastFileImages =
                    Iw3Ps3FastFileImageCompiler.Compile(
                        sourceFastFileDirectory,
                        unresolvedRequests);
                foreach (Iw3Iwi6StreamedImageCompilation compilation in
                         sourceFastFileImages)
                {
                    AddCompilation(compilation);
                }
            }
        }

        var fallbackImageNames = new List<string>();
        if (sourceFastFileDirectory is not null || useFallbackImages)
        {
            foreach (Iw3IwdImageRequest request in streamableRequests)
            {
                if (fullImagesByName.ContainsKey(request.ImageName) ||
                    (!ownedSourceImages.Contains(request.ImageName) &&
                        !(useFallbackImages && externalSourceImages.Contains(request.ImageName))))
                {
                    continue;
                }

                AddCompilation(CompileFallbackImage(request));
                fallbackImageNames.Add(request.ImageName);
            }
        }

        Iw3Iwi6StreamedImageCompilation[] sortedFullImages = fullImagesByName.Values
            .OrderBy(compilation => compilation.Image.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(compilation => compilation.Image.Name, StringComparer.Ordinal)
            .ToArray();

        ImageFilePackage? package = null;
        var referencesByAsset = new Dictionary<AssetKey, ImageFileStreamLanguageReferences>();
        if (imageFileIndex is not null)
        {
            if (sortedFullImages.Length == 0)
            {
                throw new InvalidDataException(
                    "The selected image sources do not supply any material " +
                    "image used by this conversion.");
            }

            ReadOnlyMemory<byte>[] payloads = sortedFullImages
                .SelectMany(compilation => compilation.StreamPartPayloads)
                .ToArray();
            package = new ImageFilePackager().Package(
                checked((uint)imageFileIndex.Value),
                payloads);
            for (int imageIndex = 0; imageIndex < sortedFullImages.Length; imageIndex++)
            {
                ImageFileStreamReference[] references = package.References
                    .Skip(imageIndex * GfxImageStreamData.EntryCount)
                    .Take(GfxImageStreamData.EntryCount)
                    .ToArray();
                referencesByAsset.Add(
                    AssetKey.FromDefinition(sortedFullImages[imageIndex].Image),
                    new ImageFileStreamLanguageReferences(LanguageMask, references));
            }
        }

        var assetsBySourceName = new Dictionary<string, GfxImageAsset>(
            StringComparer.OrdinalIgnoreCase);
        foreach (Iw3Iwi6StreamedImageCompilation compilation in sortedFullImages)
            assetsBySourceName.Add(compilation.Image.Name!, compilation.Image);
        foreach (GfxImageAsset waterImage in waterImages)
            assetsBySourceName.Add(waterImage.Name!, waterImage);
        foreach (ImageRequirement requirement in ordered)
        {
            if (assetsBySourceName.ContainsKey(requirement.Name))
                continue;
            bool isOwnedSource = ownedSourceImages.Contains(requirement.Name);
            if (isOwnedSource)
            {
                throw new InvalidDataException(
                    $"IW3 image '{requirement.Name}' is source-owned, but its " +
                    "payload was not found in the selected custom IWD, " +
                    "extracted fastfiles, or supplied IW3 PS3 fastfiles.");
            }
            if (!isOwnedSource && !externalSourceImages.Contains(requirement.Name))
            {
                throw new InvalidDataException(
                    $"IW3 image '{requirement.Name}' has neither source bytes " +
                    "nor an external-reference manifest entry.");
            }

            assetsBySourceName.Add(
                requirement.Name,
                new GfxImageAsset { Name = "," + requirement.Name });
        }

        return new ImageCompilationGraph(
            assetsBySourceName,
            referencesByAsset,
            package,
            sortedFullImages.Length + waterImages.Length,
            Array.AsReadOnly(fallbackImageNames.ToArray()));
    }

    private static Iw3Iwi6StreamedImageCompilation CompileFallbackImage(
        Iw3IwdImageRequest request)
    {
        const ushort size = 4;
        var payload = new byte[0x80];
        switch (request.Semantic)
        {
            case TextureSemantic.TwoDimensional:
                payload.AsSpan(4, 4).Fill(byte.MaxValue);
                break;
            case TextureSemantic.ColorMap:
            case TextureSemantic.DetailMap:
                payload[0] = payload[2] = 0x10;
                payload[1] = payload[3] = 0x84;
                break;
            case TextureSemantic.NormalMap:
                payload[0] = payload[2] = 0x1f;
                payload[1] = payload[3] = 0x84;
                break;
            case TextureSemantic.Function:
            case TextureSemantic.SpecularMap:
                break;
            default:
                throw new NotSupportedException(
                    $"Image '{request.ImageName}' with semantic " +
                    $"'{request.Semantic}' has no neutral fallback.");
        }

        return Iw3Iwi6StreamedImageCompiler.CompileTopLevelFirstPs3Payload(
            request.ImageName,
            (byte)GfxImageBaseFormat.CompressedDxt1,
            1,
            size,
            size,
            payload,
            request.Semantic,
            request.UseSrgbReads);
    }

    private static byte[]? ReadExtractedImage(
        string imageName,
        IEnumerable<string> assetDirectories)
    {
        byte[]? result = null;
        foreach (string directory in assetDirectories)
        {
            string path = ResolveOptionalImagePath(directory, imageName);
            if (!File.Exists(path))
                continue;
            byte[] bytes = File.ReadAllBytes(path);
            if (result is not null && !result.AsSpan().SequenceEqual(bytes))
                throw new InvalidDataException($"Source fastfiles contain different IWI payloads for '{imageName}'.");
            result = bytes;
        }
        return result;
    }

    private static string ResolveOptionalImagePath(
        string assetDirectory,
        string imageName) => ResolveContainedPath(
            assetDirectory,
            Path.Combine("images", imageName + ".iwi"),
            "image");

    private static TechniqueCompilationGraph CompileTechniques(
        Iw3PcExtractionResult extraction,
        Iw3ZoneManifest mapManifest,
        IReadOnlyList<MaterialSource> materialSources)
    {
        string[] mapNames = OwnedNames(mapManifest, "techniqueset");
        var compiledByName = new Dictionary<string, MaterialTechniqueSetAsset>(
            StringComparer.Ordinal);
        var shadersByKey = new Dictionary<AssetKey, MaterialShaderAsset>();
        CompileTechniqueDirectory(
            extraction.MapAssetDirectory,
            mapNames,
            compiledByName,
            shadersByKey);
        var assetsByName = new Dictionary<string, MaterialTechniqueSetAsset>(
            compiledByName,
            StringComparer.Ordinal);
        foreach (string name in materialSources
                     .Select(source => source.Inspection?.SourceTechniqueSetName ??
                         throw new InvalidOperationException("Material source was not inspected."))
                     .Distinct(StringComparer.Ordinal))
        {
            assetsByName.TryAdd(
                name,
                new MaterialTechniqueSetAsset { Name = "," + name });
        }

        return new TechniqueCompilationGraph(
            assetsByName,
            Array.AsReadOnly(compiledByName.Values
                .OrderBy(asset => asset.Name, StringComparer.Ordinal)
                .ToArray()),
            Array.AsReadOnly(shadersByKey.Values
                .OrderBy(asset => asset.SerializedAssetType)
                .ThenBy(asset => asset.Name, StringComparer.Ordinal)
                .ToArray()),
            mapNames.ToHashSet(StringComparer.Ordinal));
    }

    private static void CompileTechniqueDirectory(
        string assetDirectory,
        IReadOnlyList<string> names,
        IDictionary<string, MaterialTechniqueSetAsset> compiledByName,
        IDictionary<AssetKey, MaterialShaderAsset> shadersByKey)
    {
        if (names.Count == 0)
            return;

        string techniqueDirectory = ResolveRequiredDirectory(
            assetDirectory,
            "techniques",
            "technique directory");
        string shaderDirectory = ResolveRequiredDirectory(
            assetDirectory,
            "shader_bin",
            "shader directory");
        var parser = new Iw3TechniqueFormatParser(techniqueDirectory);
        var compiler = new Iw3TechniqueCompiler(
            shaderDirectory,
            new Iw3PcShaderCompiler());
        foreach (string name in names.Order(StringComparer.Ordinal))
        {
            if (compiledByName.ContainsKey(name))
                continue;
            string path = ResolveAssetFile(
                assetDirectory,
                "techsets",
                name,
                ".techset",
                "technique set");
            Iw3TechniqueSetSource source = parser.ParseTechniqueSet(path, name);
            Iw3TechniqueSetCompilation compilation = compiler.Compile(source);
            compiledByName.Add(name, compilation.TechniqueSet);
            foreach (MaterialShaderAsset shader in compilation.Shaders)
            {
                AssetKey key = AssetKey.FromDefinition(shader);
                shadersByKey.TryAdd(key, shader);
            }
        }
    }

    private static MaterialCompilationGraph CompileMaterials(
        IReadOnlyList<MaterialSource> materialSources,
        IReadOnlyDictionary<string, GfxImageAsset> images,
        IReadOnlyDictionary<string, MaterialTechniqueSetAsset> techniqueSets,
        IReadOnlyList<string> staticModelMaterialNames)
    {
        var bySourceName = new Dictionary<string, MaterialAsset>(
            StringComparer.Ordinal);
        var map = new List<MaterialAsset>();
        foreach (MaterialSource source in materialSources.Where(source =>
                     source.Zone == ZoneRole.Map))
        {
            string techniqueSetName = source.Inspection?.SourceTechniqueSetName ??
                throw new InvalidOperationException("Material source was not inspected.");
            if (!techniqueSets.TryGetValue(
                    techniqueSetName,
                    out MaterialTechniqueSetAsset? techniqueSet))
            {
                throw new InvalidDataException(
                    $"Technique set '{techniqueSetName}' was not resolved.");
            }

            // The PS3 loader fills second-pass state IDs into a source-free
            // RUNTIME table. For an external technique set its pass topology
            // is supplied by the stock zone, so reserving the table is the
            // only safe representation and remains donor-free.
            bool requiresRuntimeTechniqueState =
                techniqueSet.Name?.StartsWith(",", StringComparison.Ordinal) == true ||
                techniqueSet.TechniqueSlots.Any(slot =>
                    slot.Technique?.PassCount == 2);
            using FileStream stream = File.OpenRead(source.Path);
            Iw3MaterialCompilation compilation = Iw3MaterialCompiler.Compile(
                source.TargetName,
                stream,
                images,
                requiresRuntimeTechniqueState);
            bySourceName.Add(source.SourceName, compilation.Material);
            map.Add(compilation.Material);
        }

        var modelMappings = new Dictionary<string, Iw3XModelMaterialMapping>(
            StringComparer.Ordinal);
        var externalModelMaterials = new List<MaterialAsset>();
        foreach (string sourceName in staticModelMaterialNames)
        {
            if (!bySourceName.TryGetValue(sourceName, out MaterialAsset? material))
            {
                material = new MaterialAsset
                {
                    Info = new MaterialInfo
                    {
                        Name = "," + ToIw4MaterialName(sourceName)
                    }
                };
                externalModelMaterials.Add(material);
            }
            modelMappings.Add(
                sourceName,
                new Iw3XModelMaterialMapping(material, InvHighMipRadius: 0));
        }

        return new MaterialCompilationGraph(
            bySourceName,
            Array.AsReadOnly(map.ToArray()),
            Array.AsReadOnly(externalModelMaterials
                .DistinctBy(AssetKey.FromDefinition)
                .ToArray()),
            modelMappings,
            Array.AsReadOnly(map.ToArray()));
    }

    private static RawFileAsset[] CompileRawFiles(
        string assetDirectory,
        Iw3ZoneManifest manifest,
        string zoneName,
        out bool electricBoxFallbackApplied)
    {
        var result = new List<RawFileAsset>();
        string[] ownedRawFileNames = OwnedNames(manifest, "rawfile");
        electricBoxFallbackApplied =
            ownedRawFileNames.Contains(ElectricBoxRawFileName, StringComparer.Ordinal) &&
            OwnedNames(manifest, "fx").Contains(ElectricBoxFxName, StringComparer.Ordinal);
        foreach (string name in ownedRawFileNames)
        {
            string path = ResolveContainedPath(assetDirectory, name, "rawfile");
            if (!File.Exists(path))
                throw new FileNotFoundException($"Extracted rawfile '{name}' was not found.", path);
            byte[] content = File.ReadAllBytes(path);
            if (electricBoxFallbackApplied && string.Equals(
                    name,
                    ElectricBoxRawFileName,
                    StringComparison.Ordinal))
            {
                content = DisableElectricBoxInitialization(content);
            }
            result.Add(new RawFileAsset
            {
                Name = name,
                CompressedLen = 0,
                Len = content.Length,
                Buffer = [.. content, 0]
            });
        }

        if (!result.Any(asset => string.Equals(
                asset.Name,
                zoneName,
                StringComparison.Ordinal)))
        {
            result.Add(new RawFileAsset
            {
                Name = zoneName,
                CompressedLen = 0,
                Len = 0,
                Buffer = [0]
            });
        }
        return result.ToArray();
    }

    private static byte[] DisableElectricBoxInitialization(byte[] content)
    {
        ReadOnlySpan<byte> source = content;
        int suffixOffset = source.IndexOf(ElectricBoxMainSuffix);
        int loadFxOffset = source.IndexOf(ElectricBoxLoadFxReference);
        if (!source.StartsWith(ElectricBoxMainPrefix) ||
            suffixOffset < ElectricBoxMainPrefix.Length ||
            loadFxOffset < ElectricBoxMainPrefix.Length ||
            loadFxOffset >= suffixOffset ||
            source[..suffixOffset].IndexOf("/*"u8) >= 0 ||
            source[..suffixOffset].IndexOf("*/"u8) >= 0)
        {
            throw UnrecognizedElectricBoxRawFile();
        }

        return
        [
            .. source[..ElectricBoxMainPrefix.Length],
            .. BlockCommentStart,
            .. source[ElectricBoxMainPrefix.Length..suffixOffset],
            .. BlockCommentEnd,
            .. source[suffixOffset..]
        ];
    }

    private static InvalidDataException UnrecognizedElectricBoxRawFile() =>
        new(
            $"IW3 rawfile '{ElectricBoxRawFileName}' has an unrecognized " +
            $"runtime FX load. Expected its canonical main() initialization " +
            $"for '{ElectricBoxFxName}'.");

    private static BaseAsset[] BuildMapRoots(
        IReadOnlySet<string> imageNames,
        IReadOnlyDictionary<string, GfxImageAsset> images,
        TechniqueCompilationGraph techniques,
        MaterialCompilationGraph materials,
        Iw3XModelExportImportResult xmodels,
        IReadOnlyList<XModelAsset> externalEntityModels,
        D3dbspLinkResult mapGraph,
        IReadOnlyList<RawFileAsset> rawFiles,
        string mapAssetName)
    {
        var roots = new OrderedAssetSet();
        AddImages(roots, imageNames, images);
        roots.AddRange(techniques.Shaders);
        roots.AddRange(techniques.OwnedTechniqueSets.Where(set =>
            techniques.MapOwnedNames.Contains(set.Name!)));
        AddRequiredTechniqueSets(
            roots,
            materials.MapMaterials,
            materials.BySourceName,
            techniques.AssetsByName);
        roots.AddRange(materials.MapMaterials);
        roots.AddRange(materials.ExternalModelMaterials);
        roots.AddRange(xmodels.Models);
        roots.AddRange(externalEntityModels);

        var preferred = roots.Assets
            .ToDictionary(AssetKey.FromDefinition);
        foreach (BaseAsset dependency in mapGraph.DependencyReferences)
        {
            AssetKey key = AssetKey.FromDefinition(dependency);
            roots.Add(preferred.GetValueOrDefault(key) ?? dependency);
        }
        roots.AddRange(mapGraph.Roots);
        roots.AddRange(rawFiles);
        roots.Add(D3dbspAssetLinker.CreatePs3DmConfigStringBaseline(
            mapAssetName,
            mapGraph.Checksum));
        return roots.Assets.ToArray();
    }

    private static void AddRequiredTechniqueSets(
        OrderedAssetSet roots,
        IReadOnlyList<MaterialAsset> zoneMaterials,
        IReadOnlyDictionary<string, MaterialAsset> materialsBySourceName,
        IReadOnlyDictionary<string, MaterialTechniqueSetAsset> techniquesByName)
    {
        roots.AddRange(ResolveRequiredTechniqueSets(
            zoneMaterials,
            materialsBySourceName,
            techniquesByName));
    }

    private static IEnumerable<MaterialTechniqueSetAsset>
        ResolveRequiredTechniqueSets(
            IReadOnlyList<MaterialAsset> zoneMaterials,
            IReadOnlyDictionary<string, MaterialAsset> materialsBySourceName,
            IReadOnlyDictionary<string, MaterialTechniqueSetAsset> techniquesByName)
    {
        HashSet<AssetKey> materialKeys = zoneMaterials
            .Select(AssetKey.FromDefinition)
            .ToHashSet();
        foreach ((string _, MaterialAsset material) in materialsBySourceName)
        {
            if (!materialKeys.Contains(AssetKey.FromDefinition(material)))
                continue;
            string name = material.TechniqueSet?.Name?.TrimStart(',') ??
                throw new InvalidDataException(
                    $"Material '{material.Info.Name}' has no technique set.");
            if (!techniquesByName.TryGetValue(name, out MaterialTechniqueSetAsset? techniqueSet))
                throw new InvalidDataException($"Technique set '{name}' was not resolved.");
            yield return techniqueSet;
        }
    }

    private static void AddImages(
        OrderedAssetSet roots,
        IEnumerable<string> names,
        IReadOnlyDictionary<string, GfxImageAsset> images)
    {
        foreach (string name in names.Order(StringComparer.OrdinalIgnoreCase))
        {
            if (!images.TryGetValue(name, out GfxImageAsset? image))
                throw new InvalidDataException($"Image '{name}' was not resolved.");
            roots.Add(image);
        }
    }

    private static byte[] BuildLoadFastFile(
        string loadName,
        IReadOnlySet<string> loadImageNames,
        ImageCompilationGraph images,
        IReadOnlyList<RawFileAsset> loadRawFiles)
    {
        GfxImageAsset loadScreenImage = ResolveLoadScreenImage(loadImageNames, images.AssetsBySourceName);
        if (images.FallbackImageNames.Contains(loadScreenImage.Name, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"Loadscreen image '{loadScreenImage.Name}' has no source-owned pixels.");
        if (!images.StreamReferencesByAsset.TryGetValue(AssetKey.FromDefinition(loadScreenImage),
                out ImageFileStreamLanguageReferences? loadScreenReferences))
        {
            throw new InvalidDataException(
                $"Loadscreen image '{loadScreenImage.Name}' is not backed by the generated imagefile package.");
        }
        if (loadRawFiles.Count != 1 || !string.Equals(loadRawFiles[0].Name, loadName, StringComparison.Ordinal))
            throw new InvalidDataException($"IW3 load zone '{loadName}' must contain exactly its matching marker rawfile.");
        return Iw4LoadZoneBuilder.Build(loadName, loadScreenImage, [loadScreenReferences], loadRawFiles[0]);
    }

    private static GfxImageAsset ResolveLoadScreenImage(
        IReadOnlySet<string> loadImageNames,
        IReadOnlyDictionary<string, GfxImageAsset> images)
    {
        if (loadImageNames.Count != 1)
        {
            throw new InvalidDataException(
                $"IW3 '{LoadBriefingMaterialName}' must reference exactly one " +
                $"loadscreen image, but references {loadImageNames.Count}.");
        }

        string imageName = loadImageNames.Single();
        if (!images.TryGetValue(imageName, out GfxImageAsset? image) ||
            image.Name?.StartsWith(",", StringComparison.Ordinal) != false)
        {
            throw new InvalidDataException(
                $"Loadscreen image '{imageName}' has no owned compiled definition.");
        }
        return image;
    }

    private static string RequireLoadInput(string path, string mapPath)
    {
        string loadPath = RequireInputFile(path, ".ff", "load fastfile");
        if (PathComparer.Equals(mapPath, loadPath))
            throw new ArgumentException("Map and load fastfiles must be different files.");
        string expectedLoadName = RequireFileStem(mapPath, "map fastfile") + "_load";
        if (!string.Equals(RequireFileStem(loadPath, "load fastfile"), expectedLoadName, StringComparison.Ordinal))
            throw new ArgumentException($"Load fastfile must be named '{expectedLoadName}.ff'.");
        return loadPath;
    }

    private static byte[] LinkAndPackage(
        string zoneDescription,
        IReadOnlyList<BaseAsset> rootAssets,
        IReadOnlyList<BaseAsset> providerOnlyAssets,
        IReadOnlyDictionary<AssetKey, ImageFileStreamLanguageReferences>
            imageReferences,
        LinkAssetPool? bootstrapProviders = null,
        IReadOnlySet<AssetKey>? bootstrapRootKeys = null)
    {
        if ((bootstrapProviders is null) != (bootstrapRootKeys is null))
        {
            throw new ArgumentException(
                "Bootstrap providers and root keys must be supplied together.");
        }
        if (bootstrapRootKeys is not null)
        {
            foreach (AssetKey key in bootstrapRootKeys)
            {
                if (!rootAssets.Any(asset => AssetKey.FromDefinition(asset) == key))
                {
                    throw new InvalidDataException(
                        $"{zoneDescription} zone is missing bootstrap root {key}.");
                }
                if (!bootstrapProviders!.Providers.Any(provider =>
                        provider.Key == key && !provider.IsReferencePlaceholder))
                {
                    throw new InvalidDataException(
                        $"{zoneDescription} zone has no full bootstrap provider for {key}.");
                }
            }
        }

        var providers = new Dictionary<AssetKey, LinkAssetProviderSource>();
        foreach (BaseAsset asset in rootAssets.Concat(providerOnlyAssets))
        {
            AssetKey key = AssetKey.FromDefinition(asset);
            if (bootstrapRootKeys?.Contains(key) == true)
                continue;
            LinkAssetProviderSource source =
                asset is GfxImageAsset &&
                imageReferences.TryGetValue(key, out ImageFileStreamLanguageReferences? references)
                    ? new LinkAssetProviderSource(
                        asset,
                        imageStreamReferences: [references]).AsAuthoredDetached()
                    : new LinkAssetProviderSource(asset).AsAuthoredDetached();
            if (!providers.TryAdd(key, source) &&
                !ReferenceEquals(providers[key].Definition, asset))
            {
                bool existingExternal = IsExternal(providers[key].Definition);
                bool candidateExternal = IsExternal(asset);
                if (existingExternal && !candidateExternal)
                    providers[key] = source;
                else if (!existingExternal || candidateExternal)
                {
                    throw new InvalidDataException(
                        $"{zoneDescription} zone has conflicting providers for {key}.");
                }
            }
        }

        if (bootstrapProviders is not null)
        {
            HashSet<AssetKey> bootstrapFullKeys = bootstrapProviders.Providers
                .Where(provider => !provider.IsReferencePlaceholder)
                .Select(provider => provider.Key)
                .ToHashSet();
            AssetKey? collision = providers
                .Where(pair =>
                    !IsExternal(pair.Value.Definition) &&
                    bootstrapFullKeys.Contains(pair.Key))
                .Select(pair => (AssetKey?)pair.Key)
                .FirstOrDefault();
            if (collision is not null)
            {
                throw new InvalidDataException(
                    $"{zoneDescription} zone authors {collision.Value}, which " +
                    "conflicts with the scoped bootstrap XModel closure.");
            }
        }

        LinkRoot[] roots = rootAssets
            .Select((asset, index) => CreateRoot(zoneDescription, index, asset))
            .ToArray();
        LinkAssetPool providerPool = bootstrapProviders is null
            ? new LinkAssetPool(providers.Values)
            : bootstrapProviders.WithHighestPrecedenceProviders(providers.Values);
        var request = new ZoneLinkRequest(
            providerPool,
            roots,
            LanguageMask,
            LanguageMask,
            []);
        ZoneLinkResult link = new ZoneLinker().Link(request);
        if (!link.Succeeded || link.DecodedBytes is not { } decodedBytes)
        {
            throw new InvalidDataException(
                $"{zoneDescription} fastfile link failed: " +
                string.Join("; ", link.Errors));
        }

        FastFilePackagingResult package = new FastFilePackager().PackageGreenfield(
            decodedBytes,
            link.LanguageMask,
            link.SelectedLanguageMask,
            link.ImageStreamLanguageTables);
        if (!package.Succeeded || package.Bytes is not { } bytes)
        {
            throw new InvalidDataException(
                $"{zoneDescription} fastfile packaging failed: " +
                string.Join(
                    "; ",
                    package.Errors.Select(error =>
                        $"{error.Code}: {error.Message}")));
        }
        return bytes.ToArray();
    }

    private static LinkRoot CreateRoot(
        string zoneDescription,
        int index,
        BaseAsset asset)
    {
        string name = asset.SerializedAssetName ??
            throw new InvalidDataException(
                $"{asset.SerializedAssetType} root has no serialized name.");
        bool external = name.StartsWith(",", StringComparison.Ordinal);
        return new LinkRoot(
            $"mapconverter:{zoneDescription}:{index}:{asset.SerializedAssetType}",
            asset.SerializedAssetType,
            external ? LinkRootIntent.External : LinkRootIntent.Owned,
            AssetKey.FromDefinition(asset),
            name,
            opaqueHeader: null);
    }

    private static string StageOutput(
        string finalPath,
        string token,
        ReadOnlySpan<byte> bytes)
    {
        string directory = Path.GetDirectoryName(finalPath) ??
            throw new InvalidDataException("Output has no containing directory.");
        string stagePath = Path.Combine(
            directory,
            $".{Path.GetFileName(finalPath)}.{token}.tmp");
        using var stream = new FileStream(
            stagePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
        return stagePath;
    }

    private static void PublishOutputs(
        (string Staged, string Final) map,
        (string Staged, string Final)? load,
        (string Staged, string Final)? image)
    {
        var moved = new List<string>(3);
        try
        {
            IEnumerable<(string Staged, string Final)> outputs = [map];
            if (load is { } loadValue)
                outputs = outputs.Append(loadValue);
            if (image is { } imageValue)
                outputs = outputs.Append(imageValue);
            foreach ((string staged, string final) in outputs)
            {
                File.Move(staged, final, overwrite: false);
                moved.Add(final);
            }
        }
        catch
        {
            foreach (string path in moved)
                DeleteIfExists(path);
            throw;
        }
    }

    private static string RequireInputFile(
        string path,
        string extension,
        string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"The {description} does not exist.", fullPath);
        if (!string.Equals(Path.GetExtension(fullPath), extension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"The {description} must have a {extension} extension.");
        return fullPath;
    }

    private static string RequireFileStem(string path, string description)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(name) ||
            name.Contains('/') || name.Contains('\\') || name.Contains('\0'))
        {
            throw new ArgumentException($"The {description} has an invalid zone name.");
        }
        return name;
    }

    private static void ValidateDynamicEntityMapName(
        string sourceName,
        string expectedMapName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        string expected = $"maps/mp/{expectedMapName}.d3dbsp";
        string normalized = sourceName.Replace('\\', '/');
        if (!string.Equals(normalized, expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"IW3 dynamic-entity sidecar belongs to '{sourceName}', " +
                $"not '{expected}'.");
        }
    }

    private static void ValidateModelCollisionSource(
        Iw3XModelCollisionSourceData source,
        IEnumerable<string> requiredModelNames,
        Iw3ZoneManifest manifest)
    {
        HashSet<string> sourceModelNames = manifest.Entries
            .Where(entry => entry.AssetType == "xmodel")
            .Select(entry => entry.AssetName).ToHashSet(StringComparer.Ordinal);
        string[] missing = requiredModelNames.Distinct(StringComparer.Ordinal)
            .Where(name => !source.ByName.ContainsKey(name)).Order(StringComparer.Ordinal).ToArray();
        string[] unexpected = source.ByName.Keys.Where(name => !sourceModelNames.Contains(name))
            .Order(StringComparer.Ordinal).ToArray();
        if (missing.Length != 0 || unexpected.Length != 0)
        {
            throw new InvalidDataException(
                "The XModel-collision sidecar does not cover the required map models or contains models outside " +
                "the source manifest. Missing: " + string.Join(", ", missing) +
                "; unexpected: " + string.Join(", ", unexpected) + ".");
        }
    }

    private static string? NormalizeSourceReferenceName(
        string? sourceName,
        string description)
    {
        if (sourceName is null)
            return null;
        string name = sourceName.Length > 0 && sourceName[0] == ','
            ? sourceName[1..]
            : sourceName;
        if (string.IsNullOrWhiteSpace(name) ||
            name.Contains(',') || name.Contains('\\') || name.Contains('\0') ||
            !string.Equals(name, name.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"The {description} name '{sourceName}' is invalid.");
        }
        return name;
    }

    private static string NormalizeTargetAssetName(
        string? targetName,
        string description) =>
        NormalizeSourceReferenceName(targetName, description) ??
        throw new InvalidDataException($"The {description} has no name.");

    private static AssetKey XModelKey(string name) => AssetKey.FromWireName(
        CanonicalAssetFamily.FromSerializedType(XAssetType.XModel),
        name);

    private static string RequireInputDirectory(string path, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException(
                $"The {description} does not exist: '{fullPath}'.");
        }
        return fullPath;
    }

    private static string[] OwnedNames(Iw3ZoneManifest manifest, string type) =>
        ManifestNames(manifest, type, isReference: false);

    private static string[] ReferenceNames(Iw3ZoneManifest manifest, string type) =>
        ManifestNames(manifest, type, isReference: true);

    private static string[] ManifestNames(
        Iw3ZoneManifest manifest,
        string type,
        bool isReference)
    {
        string[] names = manifest.Entries
            .Where(entry =>
                entry.IsReference == isReference &&
                string.Equals(entry.AssetType, type, StringComparison.Ordinal))
            .Select(entry => entry.AssetName)
            .ToArray();
        string? duplicate = names
            .GroupBy(name => name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() != 1)?.Key;
        if (duplicate is not null)
        {
            throw new InvalidDataException(
                $"IW3 zone manifest repeats {type} '{duplicate}'.");
        }
        return names;
    }

    private static string ToIw4MaterialName(string sourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        return sourceName.StartsWith("wc/", StringComparison.Ordinal)
            ? "w/" + sourceName[3..]
            : sourceName;
    }

    private static string ResolveAssetFile(
        string assetDirectory,
        string kindDirectory,
        string assetName,
        string extension,
        string description)
    {
        string path = ResolveContainedPath(
            assetDirectory,
            Path.Combine(kindDirectory, assetName + extension),
            description);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Extracted {description} '{assetName}' was not found.", path);
        return path;
    }

    private static string ResolveRequiredDirectory(
        string assetDirectory,
        string relativePath,
        string description)
    {
        string path = ResolveContainedPath(assetDirectory, relativePath, description);
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Extracted {description} was not found: '{path}'.");
        return path;
    }

    private static string ResolveContainedPath(
        string root,
        string relativePath,
        string description)
    {
        if (Path.IsPathRooted(relativePath) ||
            relativePath.Contains('\\') ||
            relativePath.Contains('\0') ||
            relativePath.Split('/', Path.DirectorySeparatorChar)
                .Any(segment => segment is "" or "." or ".."))
        {
            throw new InvalidDataException(
                $"Extracted {description} path '{relativePath}' is unsafe.");
        }

        string fullRoot = Path.GetFullPath(root);
        string path = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        string prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, PathComparison))
        {
            throw new InvalidDataException(
                $"Extracted {description} path '{relativePath}' escapes its asset directory.");
        }
        return path;
    }

    private static void RejectExistingOutputs(ConversionPaths paths)
    {
        foreach (string output in new[]
                 {
                     paths.MapOutputPath,
                     paths.LoadOutputPath,
                     paths.ImageOutputPath
                 }.OfType<string>())
        {
            if (File.Exists(output) || Directory.Exists(output))
            {
                throw new IOException(
                    $"Output already exists and will not be overwritten: '{output}'.");
            }
        }
    }

    private static void DeleteIfExists(string? path)
    {
        if (path is not null && File.Exists(path))
            File.Delete(path);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static bool IsExternal(BaseAsset asset) =>
        asset.SerializedAssetName?.StartsWith(",", StringComparison.Ordinal) == true;

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private enum ZoneRole
    {
        Map,
        Load
    }

    private sealed record ConversionPaths(
        string MapInputPath,
        string LoadInputPath,
        string? IwdInputPath,
        string? SourceFastFileDirectory,
        string? BootstrapFastFilePath,
        string OutputDirectory,
        string MapName,
        string LoadName,
        string MapOutputPath,
        string LoadOutputPath,
        string? ImageOutputPath);

    private sealed record ImageRequirement(
        string Name,
        TextureSemantic Semantic,
        bool UseSrgbReads,
        int? WaterWidth,
        int? WaterHeight);

    private sealed class MaterialSource(
        string sourceName,
        string targetName,
        string path,
        ZoneRole zone)
    {
        internal string SourceName { get; } = sourceName;
        internal string TargetName { get; } = targetName;
        internal string Path { get; } = path;
        internal ZoneRole Zone { get; } = zone;
        internal Iw3MaterialSourceInspection? Inspection { get; set; }
    }

    private sealed record ImageCompilationGraph(
        IReadOnlyDictionary<string, GfxImageAsset> AssetsBySourceName,
        IReadOnlyDictionary<AssetKey, ImageFileStreamLanguageReferences>
            StreamReferencesByAsset,
        ImageFilePackage? Package,
        int OwnedImageCount,
        IReadOnlyList<string> FallbackImageNames);

    private sealed record TechniqueCompilationGraph(
        IReadOnlyDictionary<string, MaterialTechniqueSetAsset> AssetsByName,
        IReadOnlyList<MaterialTechniqueSetAsset> OwnedTechniqueSets,
        IReadOnlyList<MaterialShaderAsset> Shaders,
        IReadOnlySet<string> MapOwnedNames);

    private sealed record MaterialCompilationGraph(
        IReadOnlyDictionary<string, MaterialAsset> BySourceName,
        IReadOnlyList<MaterialAsset> MapMaterials,
        IReadOnlyList<MaterialAsset> ExternalModelMaterials,
        IReadOnlyDictionary<string, Iw3XModelMaterialMapping>
            ModelMappingsBySourceName,
        IReadOnlyList<MaterialAsset> OwnedMaterials);

    private sealed class OrderedAssetSet
    {
        private readonly HashSet<AssetKey> _keys = [];
        private readonly List<BaseAsset> _assets = [];

        internal IReadOnlyList<BaseAsset> Assets => _assets;

        internal void Add(BaseAsset asset)
        {
            ArgumentNullException.ThrowIfNull(asset);
            if (_keys.Add(AssetKey.FromDefinition(asset)))
                _assets.Add(asset);
        }

        internal void AddRange(IEnumerable<BaseAsset> assets)
        {
            foreach (BaseAsset asset in assets)
                Add(asset);
        }
    }
}
