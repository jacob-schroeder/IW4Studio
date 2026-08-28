using System.Text;
using IW4.Assets.Assets;
using IW4.Assets.Assets.Fx;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.Physics;
using IW4.Assets.Assets.RawFile;
using IW4.Assets.Assets.StringTable;
using IW4.Assets.Assets.XModel;
using IW4.Assets.D3dbsp;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;
using IW4.Linker.D3dbsp;
using IW4.Linker.Linking;
using IW4.Linker.Packaging;
using IW4.Studio.Documents;
using IW4.Unlinker.D3dbsp;
using D3dbspLinker.Inspection;

namespace D3dbspLinker.Conversion;

internal static class FastFileConverter
{
    // Exact PS3 FFA startup closure for the us_army/opforce_airborne factions.
    private static readonly string[] BootstrapXModelNames =
    [
        "mp_body_army_sniper",
        "head_allies_us_army_sniper",
        "viewhands_sniper_us_army",
        "mp_body_us_army_lmg",
        "head_us_army_a",
        "head_us_army_b",
        "head_us_army_c",
        "head_us_army_d",
        "head_us_army_f",
        "viewhands_us_army",
        "mp_body_us_army_lmg_b",
        "mp_body_us_army_lmg_c",
        "mp_body_us_army_assault_a",
        "mp_body_us_army_assault_b",
        "mp_body_us_army_assault_c",
        "mp_body_us_army_shotgun",
        "mp_body_us_army_shotgun_b",
        "mp_body_us_army_shotgun_c",
        "mp_body_us_army_smg",
        "mp_body_us_army_smg_b",
        "mp_body_us_army_smg_c",
        "mp_body_us_army_riot",
        "head_us_army_e",
        "mp_body_airborne_assault_a",
        "head_airborne_a",
        "head_airborne_b",
        "head_airborne_c",
        "head_airborne_d",
        "head_airborne_e",
        "viewhands_russian_airborne",
        "mp_body_airborne_assault_b",
        "mp_body_airborne_assault_c",
        "mp_body_airborne_lmg",
        "mp_body_airborne_lmg_b",
        "mp_body_airborne_lmg_c",
        "mp_body_airborne_shotgun",
        "mp_body_airborne_shotgun_b",
        "mp_body_airborne_shotgun_c",
        "mp_body_airborne_smg",
        "mp_body_airborne_smg_b",
        "mp_body_airborne_smg_c",
        "mp_body_op_airborne_sniper",
        "head_op_airborne_sniper",
        "viewhands_sniper_op_airborne",
        "mp_body_riot_op_airborne",
        "head_riot_op_airborne",
        "mp_body_ally_sniper_ghillie_urban",
        "head_allies_sniper_ghillie_urban",
        "viewhands_ghillie_urban",
        "mp_body_op_sniper_ghillie_urban",
        "head_op_sniper_ghillie_urban",
        "com_plasticcase_rangers",
        "com_plasticcase_ussr"
    ];

    public static void ToD3dbsp(string input, string output)
    {
        string inputPath = Path.GetFullPath(input);
        string outputPath = Path.GetFullPath(output);
        RequireDifferentPath(inputPath, outputPath, "fastfile input");
        string outputDirectory = Path.GetDirectoryName(outputPath) ??
            throw new InvalidDataException("The output path has no containing directory.");
        if (!Directory.Exists(outputDirectory))
            throw new DirectoryNotFoundException($"Output directory '{outputDirectory}' does not exist.");
        if (File.Exists(outputPath))
            throw new IOException($"Output file '{outputPath}' already exists.");

        using FastFileWorkspace workspace = FastFileInspector.Open(inputPath);
        BaseAsset[] assets = workspace.LoadedZone.LoadedAssets
            .Select(result => result.Asset)
            .OfType<BaseAsset>()
            .ToArray();
        D3dbspFile file = D3dbspUnlinker.Unlink(assets);
        string assetName = assets
            .OfType<GfxWorldAsset>()
            .Single()
            .Name!;
        Console.WriteLine($"map-asset: {assetName}");
        Console.WriteLine("encoding-profile: reconstructed-editable-one-cell-v22");
        Console.WriteLine("preserved: render geometry, collision, static models, entities, stages, baked lighting, reflection probes");
        Console.WriteLine("canonicalized: surface order, vertex streams, lightmap atlases, leaf-brush slices, all-visible PVS, one render cell");
        Console.WriteLine("unrecoverable: original portal/cull/PVS layout, compiler-only entity rows, node tails, lightmap packing metadata, probe color-correction source");
        file.Write(outputPath);
        Console.WriteLine($"wrote: {outputPath}");
        Console.WriteLine($"d3dbsp-chunks: {file.Lumps.Count}");
        Console.WriteLine($"d3dbsp-bytes: {new FileInfo(outputPath).Length}");
    }

    public static void FromD3dbsp(
        string input,
        string templateFastFile,
        string assetName,
        string output,
        bool forceFullbright,
        IReadOnlyList<string> dependencyFastFiles,
        IReadOnlyList<string> providerFastFiles,
        IReadOnlyList<string> additionalXModelNames,
        IReadOnlyList<string> additionalMaterialNames,
        IReadOnlyList<string> additionalFxNames,
        IReadOnlyDictionary<string, string> rawFilePaths)
    {
        ArgumentNullException.ThrowIfNull(dependencyFastFiles);
        ArgumentNullException.ThrowIfNull(providerFastFiles);
        ArgumentNullException.ThrowIfNull(additionalXModelNames);
        ArgumentNullException.ThrowIfNull(additionalMaterialNames);
        ArgumentNullException.ThrowIfNull(additionalFxNames);
        ArgumentNullException.ThrowIfNull(rawFilePaths);
        string inputPath = Path.GetFullPath(input);
        string templatePath = Path.GetFullPath(templateFastFile);
        string outputPath = Path.GetFullPath(output);
        RawFileAsset[] rawFileOverrides = rawFilePaths
            .Select(mapping => CreateRawFile(mapping.Key, mapping.Value))
            .ToArray();
        RequireDifferentPath(inputPath, outputPath, "d3dbsp input");
        RequireDifferentPath(templatePath, outputPath, "template fastfile");
        string[] dependencyPaths = dependencyFastFiles
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        string[] providerPaths = providerFastFiles
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        string? duplicateDependencyPath = dependencyPaths
            .Intersect(providerPaths, StringComparer.Ordinal)
            .FirstOrDefault();
        if (duplicateDependencyPath is not null)
        {
            throw new ArgumentException(
                $"Fastfile '{duplicateDependencyPath}' cannot be both an asset " +
                "dependency and a provider-only fastfile.");
        }
        foreach (string dependencyPath in dependencyPaths)
        {
            RequireDifferentPath(dependencyPath, outputPath, "dependency fastfile");
            if (string.Equals(dependencyPath, templatePath, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"The template fastfile '{templatePath}' cannot also be a dependency fastfile.");
            }
        }
        foreach (string providerPath in providerPaths)
        {
            RequireDifferentPath(providerPath, outputPath, "provider-only fastfile");
            if (string.Equals(providerPath, templatePath, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"The template fastfile '{templatePath}' cannot also be a " +
                    "provider-only fastfile.");
            }
        }
        string outputDirectory = Path.GetDirectoryName(outputPath) ??
            throw new InvalidDataException("The output path has no containing directory.");
        if (!Directory.Exists(outputDirectory))
            throw new DirectoryNotFoundException($"Output directory '{outputDirectory}' does not exist.");
        if (File.Exists(outputPath))
            throw new IOException($"Output file '{outputPath}' already exists.");

        using FastFileWorkspace template = FastFileInspector.Open(templatePath);
        GfxWorldAsset templateWorld =
            FastFileInspector.GetSingle<GfxWorldAsset>(template) ??
            throw new InvalidDataException(
                $"The template fastfile '{templatePath}' does not contain exactly one GfxWorld asset.");
        XModelAsset[] availableXModels = CaptureActiveXModels(template)
            .Concat(dependencyPaths.SelectMany(LoadActiveXModels))
            .DistinctBy(AssetKey.FromDefinition)
            .ToArray();
        D3dbspLinkResult graph = D3dbspAssetLinker.Link(
            new D3dbspLinkRequest(
                inputPath,
                assetName,
                forceFullbright,
                templateWorld.FragmentProgramUploadCapacity,
                availableXModels));
        string mapScriptName = assetName[..^".d3dbsp".Length] + ".gsc";
        RawFileAsset mapScript = rawFileOverrides.FirstOrDefault(rawFile =>
                string.Equals(rawFile.Name, mapScriptName, StringComparison.Ordinal)) ??
            CreateMapScript(assetName);
        BaseAsset[] fastFileMapRoots =
        [
            .. graph.Roots,
            mapScript,
            CreateMapMarker(assetName),
            .. rawFileOverrides.Where(rawFile =>
                !string.Equals(rawFile.Name, mapScriptName, StringComparison.Ordinal))
        ];
        HashSet<AssetKey> mapMaterialKeys = graph.DependencyReferences
            .Where(asset => asset.SerializedAssetType == XAssetType.Material)
            .Select(AssetKey.FromDefinition)
            .ToHashSet();
        var bootstrapXModelGraph = ResolveXModelGraph(
            template,
            templatePath,
            BootstrapXModelNames,
            mapMaterialKeys,
            "required PS3 FFA bootstrap",
            externalizeOnlyDependencyProvidedReferences: false);
        ValidateBootstrapXModelGraph(
            templatePath,
            bootstrapXModelGraph.XModelSurfsCount,
            bootstrapXModelGraph.MaterialReferenceCount,
            bootstrapXModelGraph.PhysPresetReferenceCount);
        string[] staticXModelNames = graph.DependencyReferences
            .Where(asset => asset.SerializedAssetType == XAssetType.XModel)
            .Select(asset => asset.SerializedAssetName ??
                throw new InvalidDataException(
                    "A map static-model dependency has no serialized name."))
            .Select(name => name.StartsWith(',') ? name[1..] : name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var staticXModelGraph = ResolveXModelGraphAcrossFastFiles(
            template,
            templatePath,
            dependencyPaths,
            staticXModelNames,
            mapMaterialKeys,
            "map static");
        var additionalXModelGraph = ResolveXModelGraphAcrossFastFiles(
            template,
            templatePath,
            dependencyPaths,
            additionalXModelNames,
            mapMaterialKeys,
            "requested additional");
        MaterialAsset[] additionalMaterials = ResolveOwnedAssetsAcrossFastFiles<MaterialAsset>(
            template,
            templatePath,
            dependencyPaths,
            additionalMaterialNames,
            XAssetType.Material,
            "requested Material");
        FxEffectDefAsset[] additionalFx = ResolveOwnedAssetsAcrossFastFiles<FxEffectDefAsset>(
            template,
            templatePath,
            dependencyPaths,
            additionalFxNames,
            XAssetType.Fx,
            "requested FxEffectDef");
        BaseAsset[] xModelGraphProviders = bootstrapXModelGraph.Providers
            .Concat(staticXModelGraph.Providers)
            .Concat(additionalXModelGraph.Providers)
            .DistinctBy(AssetKey.FromDefinition)
            .ToArray();
        HashSet<AssetKey> xModelExternalProviderKeys =
        [
            .. bootstrapXModelGraph.ExternalProviderKeys,
            .. staticXModelGraph.ExternalProviderKeys,
            .. additionalXModelGraph.ExternalProviderKeys
        ];
        StringTableAsset bootstrapStringTable =
            D3dbspAssetLinker.CreatePs3DmConfigStringBaseline(
                assetName,
                graph.Checksum);
        string signedChecksum = bootstrapStringTable.Cells[3].String
            ?? throw new InvalidDataException(
                "The generated PS3 deathmatch configstring baseline has no mapcrc value.");

        LinkAssetPool baseAssets = template.InitialLinkRequest.Assets;
        var existingKeys = baseAssets.Providers
            .Select(provider => provider.Key)
            .ToHashSet();
        var existingFullProviderKeys = baseAssets.Providers
            .Where(provider => !provider.IsReferencePlaceholder)
            .Select(provider => provider.Key)
            .ToHashSet();
        foreach (string dependencyPath in dependencyPaths.Concat(providerPaths))
        {
            using FastFileWorkspace dependency = FastFileInspector.Open(dependencyPath);
            LinkAssetPool missingAssets = dependency.InitialLinkRequest.Assets
                .WithoutProviders(existingFullProviderKeys);
            baseAssets = baseAssets.WithHighestPrecedencePool(missingAssets);
            foreach (LinkAssetProvider provider in missingAssets.Providers)
            {
                existingKeys.Add(provider.Key);
                if (!provider.IsReferencePlaceholder)
                    existingFullProviderKeys.Add(provider.Key);
            }
        }
        baseAssets = baseAssets.WithoutProviders(xModelExternalProviderKeys);
        existingKeys.ExceptWith(xModelExternalProviderKeys);
        existingFullProviderKeys.ExceptWith(xModelExternalProviderKeys);

        var newSources = new List<LinkAssetProviderSource>(
            fastFileMapRoots.Length + graph.NestedAssets.Count +
            xModelGraphProviders.Length + 1 +
            additionalMaterials.Length +
            additionalFx.Length);
        var externalFallbackNames = new List<string>();
        foreach (BaseAsset root in fastFileMapRoots)
            newSources.Add(new LinkAssetProviderSource(root).AsAuthoredDetached());
        foreach (BaseAsset nestedAsset in graph.NestedAssets)
        {
            newSources.Add(
                new LinkAssetProviderSource(nestedAsset).AsAuthoredDetached());
        }
        newSources.Add(
            new LinkAssetProviderSource(bootstrapStringTable).AsAuthoredDetached());
        foreach (BaseAsset provider in xModelGraphProviders)
        {
            newSources.Add(
                new LinkAssetProviderSource(provider).AsAuthoredDetached());
        }
        foreach (MaterialAsset material in additionalMaterials)
        {
            newSources.Add(
                new LinkAssetProviderSource(material).AsAuthoredDetached());
        }
        foreach (FxEffectDefAsset effect in additionalFx)
        {
            newSources.Add(
                new LinkAssetProviderSource(effect).AsAuthoredDetached());
        }
        foreach (BaseAsset dependency in graph.DependencyReferences)
        {
            AssetKey key = AssetKey.FromDefinition(dependency);
            if (existingKeys.Contains(key))
                continue;
            newSources.Add(new LinkAssetProviderSource(dependency).AsAuthoredDetached());
            externalFallbackNames.Add(
                dependency.SerializedAssetName ?? key.ToString());
            existingKeys.Add(key);
        }

        LinkAssetPool assets = baseAssets
            .WithHighestPrecedenceProviders(newSources);
        var roots = new List<LinkRoot>(
            fastFileMapRoots.Length + bootstrapXModelGraph.Models.Count +
            additionalXModelGraph.Models.Count + additionalMaterials.Length +
            additionalFx.Length + 1);
        roots.AddRange(fastFileMapRoots.Select(CreateOwnedRoot));
        roots.Add(CreateNamedOwnedRoot(
            "d3dbsplinker:bootstrap:stringtable:dm",
            bootstrapStringTable));
        for (int index = 0; index < additionalMaterials.Length; index++)
        {
            roots.Add(CreateNamedOwnedRoot(
                $"d3dbsplinker:additional:material:{index}:{additionalMaterialNames[index]}",
                additionalMaterials[index]));
        }
        for (int index = 0; index < bootstrapXModelGraph.Models.Count; index++)
        {
            roots.Add(CreateNamedOwnedRoot(
                $"d3dbsplinker:bootstrap:xmodel:{index}:{BootstrapXModelNames[index]}",
                bootstrapXModelGraph.Models[index]));
        }
        for (int index = 0; index < additionalXModelGraph.Models.Count; index++)
        {
            roots.Add(CreateNamedOwnedRoot(
                $"d3dbsplinker:additional:xmodel:{index}:{additionalXModelNames[index]}",
                additionalXModelGraph.Models[index]));
        }
        for (int index = 0; index < additionalFx.Length; index++)
        {
            roots.Add(CreateNamedOwnedRoot(
                $"d3dbsplinker:additional:fx:{index}:{additionalFxNames[index]}",
                additionalFx[index]));
        }
        var request = new ZoneLinkRequest(
            assets,
            roots,
            template.InitialLinkRequest.LanguageMask,
            template.InitialLinkRequest.SelectedLanguageMask,
            template.InitialLinkRequest.ScriptStrings);

        ZoneLinkResult link = new ZoneLinker().Link(request);
        if (!link.Succeeded || link.DecodedBytes is not { } decodedBytes)
        {
            throw new InvalidDataException(
                "Fastfile link failed: " + string.Join("; ", link.Errors));
        }

        FastFilePackagingResult package = new FastFilePackager().PackageGreenfield(
            decodedBytes,
            link.LanguageMask,
            link.SelectedLanguageMask,
            link.ImageStreamLanguageTables);
        if (!package.Succeeded || package.Bytes is not { } packageBytes)
        {
            throw new InvalidDataException(
                "Fastfile packaging failed: " +
                string.Join(
                    "; ",
                    package.Errors.Select(error => $"{error.Code}: {error.Message}")));
        }

        WriteNewFileAtomically(outputPath, packageBytes.Span);

        Console.WriteLine($"wrote: {outputPath}");
        Console.WriteLine($"map-asset: {assetName}");
        Console.WriteLine($"owned-map-roots: {fastFileMapRoots.Length}");
        Console.WriteLine($"nested-map-assets: {graph.NestedAssets.Count}");
        Console.WriteLine($"owned-bootstrap-roots: {bootstrapXModelGraph.Models.Count + 1}");
        Console.WriteLine($"owned-additional-xmodel-roots: {additionalXModelGraph.Models.Count}");
        Console.WriteLine($"owned-additional-material-roots: {additionalMaterials.Length}");
        Console.WriteLine($"owned-additional-fx-roots: {additionalFx.Length}");
        Console.WriteLine($"owned-rawfile-overrides: {rawFileOverrides.Length}");
        Console.WriteLine($"owned-roots: {roots.Count}");
        Console.WriteLine($"bootstrap-stringtable: {bootstrapStringTable.Name}");
        Console.WriteLine($"bootstrap-mapcrc: {signedChecksum}");
        Console.WriteLine($"bootstrap-xmodels: {bootstrapXModelGraph.Models.Count}");
        Console.WriteLine($"bootstrap-xmodelsurfs: {bootstrapXModelGraph.XModelSurfsCount}");
        Console.WriteLine(
            $"bootstrap-material-references: {bootstrapXModelGraph.MaterialReferenceCount}");
        Console.WriteLine(
            $"bootstrap-physpreset-references: {bootstrapXModelGraph.PhysPresetReferenceCount}");
        Console.WriteLine($"map-static-xmodels: {staticXModelGraph.Models.Count}");
        Console.WriteLine($"map-static-xmodelsurfs: {staticXModelGraph.XModelSurfsCount}");
        Console.WriteLine(
            $"map-static-material-references: {staticXModelGraph.MaterialReferenceCount}");
        Console.WriteLine(
            $"map-static-physpreset-references: {staticXModelGraph.PhysPresetReferenceCount}");
        Console.WriteLine($"additional-xmodels: {additionalXModelGraph.Models.Count}");
        Console.WriteLine($"additional-xmodelsurfs: {additionalXModelGraph.XModelSurfsCount}");
        Console.WriteLine($"additional-materials: {additionalMaterials.Length}");
        Console.WriteLine(
            $"additional-material-references: {additionalXModelGraph.MaterialReferenceCount}");
        Console.WriteLine(
            $"additional-physpreset-references: {additionalXModelGraph.PhysPresetReferenceCount}");
        Console.WriteLine($"template-providers: {template.InitialLinkRequest.Assets.Providers.Count}");
        Console.WriteLine($"dependency-fastfiles: {dependencyPaths.Length}");
        Console.WriteLine($"provider-fastfiles: {providerPaths.Length}");
        int linkedLightmapCount = graph.Roots
            .OfType<GfxWorldAsset>()
            .Single()
            .WorldDraw
            .LightmapCount;
        Console.WriteLine(forceFullbright
            ? $"lighting-mode: forced fullbright; discarded {graph.DiscardedLightByteCount} compiled light bytes"
            : $"lighting-mode: authored; linked {linkedLightmapCount} lightmap arrays");
        Console.WriteLine($"available-providers: {baseAssets.Providers.Count}");
        Console.WriteLine($"external-reference-fallbacks: {externalFallbackNames.Count}");
        foreach (string referenceName in externalFallbackNames)
            Console.WriteLine($"external-reference: {referenceName}");
        Console.WriteLine($"decoded-zone-bytes: {decodedBytes.Length}");
        Console.WriteLine($"fastfile-bytes: {packageBytes.Length}");
    }

    private static LinkRoot CreateOwnedRoot(BaseAsset asset, int index)
    {
        string name = asset.SerializedAssetName ??
            throw new InvalidDataException($"{asset.SerializedAssetType} root has no serialized name.");
        return new LinkRoot(
            $"d3dbsplinker:{index}:{asset.SerializedAssetType}",
            asset.SerializedAssetType,
            LinkRootIntent.Owned,
            AssetKey.FromDefinition(asset),
            name,
            opaqueHeader: null);
    }

    private static RawFileAsset CreateMapScript(string assetName)
    {
        string scriptName = assetName[..^".d3dbsp".Length] + ".gsc";
        // These factions own the player-model closure selected below.
        const string script =
            "main()\r\n" +
            "{\r\n" +
            "\tmaps\\mp\\_load::main();\r\n" +
            "\tgame[\"allies\"] = \"us_army\";\r\n" +
            "\tgame[\"axis\"] = \"opforce_airborne\";\r\n" +
            "\tgame[\"attackers\"] = \"allies\";\r\n" +
            "\tgame[\"defenders\"] = \"axis\";\r\n" +
            "}\r\n";
        byte[] content = Encoding.ASCII.GetBytes(script);
        return new RawFileAsset
        {
            Name = scriptName,
            CompressedLen = 0,
            Len = content.Length,
            Buffer = [.. content, 0]
        };
    }

    private static RawFileAsset CreateMapMarker(string assetName) => new()
    {
        Name = Path.GetFileNameWithoutExtension(assetName.Replace('\\', '/')),
        CompressedLen = 0,
        Len = 0,
        Buffer = [0]
    };

    private static RawFileAsset CreateRawFile(string name, string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name[0] == ',')
        {
            throw new ArgumentException(
                $"RawFile override '{name}' cannot use an external-reference name.");
        }
        string path = Path.GetFullPath(sourcePath);
        byte[] content = File.ReadAllBytes(path);
        if (content.AsSpan().IndexOf((byte)0) >= 0)
        {
            throw new InvalidDataException(
                $"RawFile source '{path}' contains an embedded null byte.");
        }
        return new RawFileAsset
        {
            Name = name,
            CompressedLen = 0,
            Len = content.Length,
            Buffer = [.. content, 0]
        };
    }

    private static LinkRoot CreateNamedOwnedRoot(string entryId, BaseAsset asset)
    {
        string name = asset.SerializedAssetName ??
            throw new InvalidDataException(
                $"{asset.SerializedAssetType} owned root has no serialized name.");
        return new LinkRoot(
            entryId,
            asset.SerializedAssetType,
            LinkRootIntent.Owned,
            AssetKey.FromDefinition(asset),
            name,
            opaqueHeader: null);
    }

    private static (
        IReadOnlyList<XModelAsset> Models,
        IReadOnlyList<BaseAsset> Providers,
        IReadOnlySet<AssetKey> ExternalProviderKeys,
        int XModelSurfsCount,
        int MaterialReferenceCount,
        int PhysPresetReferenceCount) ResolveXModelGraph(
        FastFileWorkspace template,
        string templatePath,
        IReadOnlyList<string> modelNames,
        IReadOnlySet<AssetKey> mapMaterialKeys,
        string graphDescription,
        bool externalizeOnlyDependencyProvidedReferences)
    {
        ArgumentNullException.ThrowIfNull(modelNames);
        ArgumentNullException.ThrowIfNull(mapMaterialKeys);
        ArgumentException.ThrowIfNullOrEmpty(graphDescription);
        var models = new XModelAsset[modelNames.Count];
        var missingNames = new List<string>();
        var fullProviders = template.LoadedZone.Context.AssetPool.Slots
            .SelectMany(slot => slot.Providers)
            .Where(provider => !provider.IsReferencePlaceholder)
            .OrderByDescending(provider =>
                provider.Owner == template.LoadedZone.Context.ZoneOwner)
            .ThenBy(provider => provider.RegistrationSequence)
            .ToArray();
        HashSet<AssetKey>? dependencyProviderKeys =
            externalizeOnlyDependencyProvidedReferences
                ? fullProviders
                    .Where(provider =>
                        (provider.AssetType == XAssetType.Material ||
                         provider.AssetType == XAssetType.PhysPreset) &&
                        provider.Owner != template.LoadedZone.Context.ZoneOwner)
                    .Select(provider => provider.Asset)
                    .OfType<BaseAsset>()
                    .Select(AssetKey.FromDefinition)
                    .ToHashSet()
                : null;
        for (int index = 0; index < modelNames.Count; index++)
        {
            string name = modelNames[index];
            XModelAsset? model = fullProviders.FirstOrDefault(
                candidate =>
                    candidate.Owner == template.LoadedZone.Context.ZoneOwner &&
                    candidate.AssetType == XAssetType.XModel &&
                    string.Equals(
                        candidate.Name,
                        name,
                        StringComparison.Ordinal) &&
                    candidate.Asset is XModelAsset)?.Asset as XModelAsset;
            if (model is null)
            {
                missingNames.Add(name);
                continue;
            }

            models[index] = model;
        }

        if (missingNames.Count != 0)
        {
            throw new InvalidDataException(
                $"Template fastfile '{templatePath}' does not contain full XModel " +
                $"providers for the {missingNames.Count} {graphDescription} " +
                $"asset(s): {string.Join(", ", missingNames)}.");
        }

        var authoredProviders = new List<BaseAsset>(models.Length);
        var authoredKeys = new HashSet<AssetKey>();
        foreach (XModelAsset model in models)
        {
            if (authoredKeys.Add(AssetKey.FromDefinition(model)))
                authoredProviders.Add(model);
        }

        int modelSurfsCount = 0;
        var missingModelSurfs = new List<string>();
        foreach (XModelAsset model in models)
        {
            foreach (XModelSurfsAsset retained in model.Lods
                .Select(lod => lod.ModelSurfs)
                .Where(modelSurfs => modelSurfs is not null)
                .Cast<XModelSurfsAsset>())
            {
                AssetKey key = AssetKey.FromDefinition(retained);
                if (authoredKeys.Contains(key))
                    continue;

                XModelSurfsAsset? modelSurfs = fullProviders.FirstOrDefault(
                    candidate =>
                        candidate.AssetType == XAssetType.XModelSurfs &&
                        candidate.Asset is XModelSurfsAsset asset &&
                        AssetKey.FromDefinition(asset) == key)?.Asset as XModelSurfsAsset;
                if (modelSurfs is null)
                {
                    missingModelSurfs.Add(
                        $"{model.SerializedAssetName} -> {retained.SerializedAssetName}");
                    continue;
                }

                authoredKeys.Add(key);
                authoredProviders.Add(modelSurfs);
                modelSurfsCount++;
            }
        }

        if (missingModelSurfs.Count != 0)
        {
            throw new InvalidDataException(
                $"Template workspace for '{templatePath}' does not contain full " +
                $"XModelSurfs providers for the {graphDescription} model graph: " +
                $"{string.Join(", ", missingModelSurfs)}.");
        }

        MaterialAsset[] materialDependencies = models
            .SelectMany(model => model.Materials)
            .OfType<MaterialAsset>()
            .DistinctBy(AssetKey.FromDefinition)
            .Where(material => !mapMaterialKeys.Contains(
                AssetKey.FromDefinition(material)))
            .Where(material => dependencyProviderKeys is null ||
                dependencyProviderKeys.Contains(AssetKey.FromDefinition(material)))
            .ToArray();
        PhysPresetAsset[] physPresetDependencies = models
            .Select(model => model.PhysPreset)
            .OfType<PhysPresetAsset>()
            .DistinctBy(AssetKey.FromDefinition)
            .Where(physPreset => dependencyProviderKeys is null ||
                dependencyProviderKeys.Contains(AssetKey.FromDefinition(physPreset)))
            .ToArray();

        var externalProviderKeys = new HashSet<AssetKey>();
        foreach (MaterialAsset material in materialDependencies)
        {
            MaterialAsset reference = CreateMaterialReference(material);
            AssetKey key = AssetKey.FromDefinition(reference);
            externalProviderKeys.Add(key);
            authoredProviders.Add(reference);
        }
        foreach (PhysPresetAsset physPreset in physPresetDependencies)
        {
            PhysPresetAsset reference = CreatePhysPresetReference(physPreset);
            AssetKey key = AssetKey.FromDefinition(reference);
            externalProviderKeys.Add(key);
            authoredProviders.Add(reference);
        }

        return (
            Array.AsReadOnly(models),
            authoredProviders.AsReadOnly(),
            externalProviderKeys,
            modelSurfsCount,
            materialDependencies.Length,
            physPresetDependencies.Length);
    }

    private static (
        IReadOnlyList<XModelAsset> Models,
        IReadOnlyList<BaseAsset> Providers,
        IReadOnlySet<AssetKey> ExternalProviderKeys,
        int XModelSurfsCount,
        int MaterialReferenceCount,
        int PhysPresetReferenceCount) ResolveXModelGraphAcrossFastFiles(
        FastFileWorkspace template,
        string templatePath,
        IReadOnlyList<string> dependencyPaths,
        IReadOnlyList<string> modelNames,
        IReadOnlySet<AssetKey> mapMaterialKeys,
        string graphDescription)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(dependencyPaths);
        ArgumentNullException.ThrowIfNull(modelNames);
        ArgumentNullException.ThrowIfNull(mapMaterialKeys);
        ArgumentException.ThrowIfNullOrEmpty(graphDescription);
        if (modelNames.Count == 0)
        {
            return (
                Array.Empty<XModelAsset>(),
                Array.Empty<BaseAsset>(),
                new HashSet<AssetKey>(),
                0,
                0,
                0);
        }

        var unresolvedNames = new HashSet<string>(
            modelNames,
            StringComparer.Ordinal);
        var modelsByName = new Dictionary<string, XModelAsset>(
            StringComparer.Ordinal);
        var providers = new List<BaseAsset>();
        var externalProviderKeys = new HashSet<AssetKey>();

        ResolveFromWorkspace(template, templatePath);
        foreach (string dependencyPath in dependencyPaths)
        {
            if (unresolvedNames.Count == 0)
                break;
            using FastFileWorkspace dependency = FastFileInspector.Open(
                dependencyPath);
            ResolveFromWorkspace(dependency, dependencyPath);
        }

        if (unresolvedNames.Count != 0)
        {
            throw new InvalidDataException(
                $"The template and dependency fastfiles do not contain full XModel " +
                $"providers for the {unresolvedNames.Count} {graphDescription} " +
                $"asset(s): {string.Join(", ", unresolvedNames.OrderBy(name => name, StringComparer.Ordinal))}.");
        }

        BaseAsset[] distinctProviders = providers
            .DistinctBy(AssetKey.FromDefinition)
            .ToArray();
        XModelAsset[] orderedModels = modelNames
            .Select(name => modelsByName[name])
            .ToArray();
        return (
            Array.AsReadOnly(orderedModels),
            Array.AsReadOnly(distinctProviders),
            externalProviderKeys,
            distinctProviders.OfType<XModelSurfsAsset>().Count(),
            distinctProviders.OfType<MaterialAsset>().Count(),
            distinctProviders.OfType<PhysPresetAsset>().Count());

        void ResolveFromWorkspace(
            FastFileWorkspace workspace,
            string sourcePath)
        {
            var zoneOwner = workspace.LoadedZone.Context.ZoneOwner;
            string[] ownedNames = workspace.LoadedZone.Context.AssetPool.Slots
                .SelectMany(slot => slot.Providers)
                .Where(provider =>
                    provider.Owner == zoneOwner &&
                    !provider.IsReferencePlaceholder &&
                    provider.AssetType == XAssetType.XModel &&
                    provider.Asset is XModelAsset &&
                    provider.Name is not null &&
                    unresolvedNames.Contains(provider.Name))
                .Select(provider => provider.Name!)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            if (ownedNames.Length == 0)
                return;

            var resolved = ResolveXModelGraph(
                workspace,
                sourcePath,
                ownedNames,
                mapMaterialKeys,
                graphDescription,
                externalizeOnlyDependencyProvidedReferences: true);
            for (int index = 0; index < ownedNames.Length; index++)
            {
                modelsByName.Add(ownedNames[index], resolved.Models[index]);
                unresolvedNames.Remove(ownedNames[index]);
            }
            providers.AddRange(resolved.Providers);
            externalProviderKeys.UnionWith(resolved.ExternalProviderKeys);
        }
    }

    private static TAsset[] ResolveOwnedAssetsAcrossFastFiles<TAsset>(
        FastFileWorkspace template,
        string templatePath,
        IReadOnlyList<string> dependencyPaths,
        IReadOnlyList<string> assetNames,
        XAssetType assetType,
        string assetDescription)
        where TAsset : BaseAsset
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(dependencyPaths);
        ArgumentNullException.ThrowIfNull(assetNames);
        ArgumentException.ThrowIfNullOrEmpty(assetDescription);
        if (assetNames.Count == 0)
            return [];

        var unresolvedNames = new HashSet<string>(assetNames, StringComparer.Ordinal);
        var assetsByName = new Dictionary<string, TAsset>(StringComparer.Ordinal);
        ResolveFromWorkspace(template);
        foreach (string dependencyPath in dependencyPaths)
        {
            if (unresolvedNames.Count == 0)
                break;
            using FastFileWorkspace dependency = FastFileInspector.Open(dependencyPath);
            ResolveFromWorkspace(dependency);
        }
        if (unresolvedNames.Count != 0)
        {
            throw new InvalidDataException(
                $"The template fastfile '{templatePath}' and its dependencies do not contain " +
                $"full providers for the {unresolvedNames.Count} {assetDescription} asset(s): " +
                string.Join(", ", unresolvedNames.OrderBy(name => name, StringComparer.Ordinal)) +
                ".");
        }
        return assetNames.Select(name => assetsByName[name]).ToArray();

        void ResolveFromWorkspace(FastFileWorkspace workspace)
        {
            var zoneOwner = workspace.LoadedZone.Context.ZoneOwner;
            foreach (var provider in workspace.LoadedZone.Context.AssetPool.Slots
                .SelectMany(slot => slot.Providers)
                .Where(provider =>
                    provider.Owner == zoneOwner &&
                    !provider.IsReferencePlaceholder &&
                    provider.AssetType == assetType &&
                    provider.Name is not null &&
                    unresolvedNames.Contains(provider.Name) &&
                    provider.Asset is TAsset)
                .OrderBy(provider => provider.RegistrationSequence))
            {
                string name = provider.Name!;
                if (!assetsByName.TryAdd(name, (TAsset)provider.Asset!))
                    continue;
                unresolvedNames.Remove(name);
            }
        }
    }

    private static void ValidateBootstrapXModelGraph(
        string templatePath,
        int modelSurfsCount,
        int materialReferenceCount,
        int physPresetReferenceCount)
    {
        if (modelSurfsCount != 105)
        {
            throw new InvalidDataException(
                $"Template fastfile '{templatePath}' PS3 FFA bootstrap graph contains " +
                $"{modelSurfsCount} unique full XModelSurfs provider key(s); " +
                "the hardware-proven closure requires exactly 105.");
        }
        if (materialReferenceCount != 98 || physPresetReferenceCount != 1)
        {
            throw new InvalidDataException(
                $"Template fastfile '{templatePath}' PS3 FFA bootstrap dependency " +
                $"closure contains {materialReferenceCount} externalizable Material " +
                $"and {physPresetReferenceCount} PhysPreset provider key(s); " +
                "the hardware-proven closure requires exactly 98 and 1.");
        }
    }

    private static MaterialAsset CreateMaterialReference(MaterialAsset source) =>
        new()
        {
            Info = new MaterialInfo
            {
                Name = CreateExternalReferenceName(source)
            }
        };

    private static PhysPresetAsset CreatePhysPresetReference(
        PhysPresetAsset source) =>
        new()
        {
            Name = CreateExternalReferenceName(source)
        };

    private static string CreateExternalReferenceName(BaseAsset source)
    {
        string name = source.SerializedAssetName ?? throw new InvalidDataException(
            $"{source.SerializedAssetType} XModel graph dependency has no serialized name.");
        if (name.Length == 0 || name.Contains('\0'))
        {
            throw new InvalidDataException(
                $"{source.SerializedAssetType} XModel graph dependency has invalid name '{name}'.");
        }

        return name[0] == ',' ? name : "," + name;
    }

    private static IReadOnlyList<XModelAsset> LoadActiveXModels(string path)
    {
        using FastFileWorkspace workspace = FastFileInspector.Open(path);
        return CaptureActiveXModels(workspace);
    }

    private static IReadOnlyList<XModelAsset> CaptureActiveXModels(
        FastFileWorkspace workspace) =>
        Array.AsReadOnly(workspace.LoadedZone.Context.AssetPool.Slots
            .Where(slot =>
                slot.AssetType == XAssetType.XModel &&
                !slot.ActiveProvider.IsReferencePlaceholder)
            .Select(slot => slot.ActiveProvider.Asset)
            .OfType<XModelAsset>()
            .ToArray());

    private static void RequireDifferentPath(
        string source,
        string destination,
        string sourceDescription)
    {
        if (string.Equals(source, destination, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The output path must be different from the {sourceDescription} path.");
        }
    }

    private static void WriteNewFileAtomically(
        string outputPath,
        ReadOnlySpan<byte> bytes)
    {
        string directory = Path.GetDirectoryName(outputPath) ??
            throw new InvalidDataException("The fastfile output path has no containing directory.");
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 1024,
                FileOptions.SequentialScan))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, outputPath);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
