using System.Globalization;
using System.Text;
using IW4.Assets.Assets;
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
        IReadOnlyList<string> dependencyFastFiles)
    {
        ArgumentNullException.ThrowIfNull(dependencyFastFiles);
        string inputPath = Path.GetFullPath(input);
        string templatePath = Path.GetFullPath(templateFastFile);
        string outputPath = Path.GetFullPath(output);
        RequireDifferentPath(inputPath, outputPath, "d3dbsp input");
        RequireDifferentPath(templatePath, outputPath, "template fastfile");
        string[] dependencyPaths = dependencyFastFiles
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        foreach (string dependencyPath in dependencyPaths)
        {
            RequireDifferentPath(dependencyPath, outputPath, "dependency fastfile");
            if (string.Equals(dependencyPath, templatePath, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"The template fastfile '{templatePath}' cannot also be a dependency fastfile.");
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
                templateWorld.UmbraGateCount,
                availableXModels));
        BaseAsset[] fastFileMapRoots =
        [
            .. graph.Roots,
            CreateMapScript(assetName),
            CreateMapMarker(assetName)
        ];
        HashSet<AssetKey> mapMaterialKeys = graph.DependencyReferences
            .Where(asset => asset.SerializedAssetType == XAssetType.Material)
            .Select(AssetKey.FromDefinition)
            .ToHashSet();
        (
            IReadOnlyList<XModelAsset> bootstrapXModels,
            IReadOnlyList<BaseAsset> bootstrapModelProviders,
            IReadOnlySet<AssetKey> bootstrapExternalProviderKeys,
            int bootstrapXModelSurfsCount,
            int bootstrapMaterialReferenceCount,
            int bootstrapPhysPresetReferenceCount) =
            ResolveBootstrapXModelGraph(
                template,
                templatePath,
                mapMaterialKeys);
        StringTableAsset bootstrapStringTable = CreateBootstrapStringTable(
            assetName,
            graph.Checksum,
            out string signedChecksum);

        LinkAssetPool baseAssets = template.InitialLinkRequest.Assets;
        var existingKeys = baseAssets.Providers
            .Select(provider => provider.Key)
            .ToHashSet();
        foreach (string dependencyPath in dependencyPaths)
        {
            using FastFileWorkspace dependency = FastFileInspector.Open(dependencyPath);
            LinkAssetPool missingAssets = dependency.InitialLinkRequest.Assets
                .WithoutProviders(existingKeys);
            baseAssets = baseAssets.WithHighestPrecedencePool(missingAssets);
            foreach (LinkAssetProvider provider in missingAssets.Providers)
                existingKeys.Add(provider.Key);
        }
        baseAssets = baseAssets.WithoutProviders(bootstrapExternalProviderKeys);
        existingKeys.ExceptWith(bootstrapExternalProviderKeys);

        var newSources = new List<LinkAssetProviderSource>(
            fastFileMapRoots.Length + graph.NestedAssets.Count +
            bootstrapModelProviders.Count + 1);
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
        foreach (BaseAsset provider in bootstrapModelProviders)
        {
            newSources.Add(
                new LinkAssetProviderSource(provider).AsAuthoredDetached());
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
            fastFileMapRoots.Length + bootstrapXModels.Count + 1);
        roots.AddRange(fastFileMapRoots.Select(CreateOwnedRoot));
        roots.Add(CreateBootstrapRoot(
            "d3dbsplinker:bootstrap:stringtable:dm",
            bootstrapStringTable));
        for (int index = 0; index < bootstrapXModels.Count; index++)
        {
            roots.Add(CreateBootstrapRoot(
                $"d3dbsplinker:bootstrap:xmodel:{index}:{BootstrapXModelNames[index]}",
                bootstrapXModels[index]));
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
        Console.WriteLine($"owned-bootstrap-roots: {bootstrapXModels.Count + 1}");
        Console.WriteLine($"owned-roots: {roots.Count}");
        Console.WriteLine($"bootstrap-stringtable: {bootstrapStringTable.Name}");
        Console.WriteLine($"bootstrap-mapcrc: {signedChecksum}");
        Console.WriteLine($"bootstrap-xmodels: {bootstrapXModels.Count}");
        Console.WriteLine($"bootstrap-xmodelsurfs: {bootstrapXModelSurfsCount}");
        Console.WriteLine(
            $"bootstrap-material-references: {bootstrapMaterialReferenceCount}");
        Console.WriteLine(
            $"bootstrap-physpreset-references: {bootstrapPhysPresetReferenceCount}");
        Console.WriteLine($"template-providers: {template.InitialLinkRequest.Assets.Providers.Count}");
        Console.WriteLine($"dependency-fastfiles: {dependencyPaths.Length}");
        Console.WriteLine(
            graph.DiscardedLightByteCount == 0
                ? "lighting-mode: source has no compiled lightmaps"
                : $"lighting-mode: forced fullbright; discarded {graph.DiscardedLightByteCount} compiled light bytes");
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

    private static LinkRoot CreateBootstrapRoot(string entryId, BaseAsset asset)
    {
        string name = asset.SerializedAssetName ??
            throw new InvalidDataException(
                $"{asset.SerializedAssetType} bootstrap root has no serialized name.");
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
        int PhysPresetReferenceCount) ResolveBootstrapXModelGraph(
        FastFileWorkspace template,
        string templatePath,
        IReadOnlySet<AssetKey> mapMaterialKeys)
    {
        ArgumentNullException.ThrowIfNull(mapMaterialKeys);
        var models = new XModelAsset[BootstrapXModelNames.Length];
        var missingNames = new List<string>();
        var fullProviders = template.LoadedZone.Context.AssetPool.Slots
            .SelectMany(slot => slot.Providers)
            .Where(provider => !provider.IsReferencePlaceholder)
            .OrderByDescending(provider =>
                provider.Owner == template.LoadedZone.Context.ZoneOwner)
            .ThenBy(provider => provider.RegistrationSequence)
            .ToArray();
        for (int index = 0; index < BootstrapXModelNames.Length; index++)
        {
            string name = BootstrapXModelNames[index];
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
                $"providers for the {missingNames.Count} required PS3 FFA bootstrap " +
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
                $"XModelSurfs providers for the required PS3 FFA bootstrap model graph: " +
                $"{string.Join(", ", missingModelSurfs)}.");
        }
        if (modelSurfsCount != 105)
        {
            throw new InvalidDataException(
                $"Template fastfile '{templatePath}' PS3 FFA bootstrap graph contains " +
                $"{modelSurfsCount} unique full XModelSurfs provider key(s); " +
                "the hardware-proven closure requires exactly 105.");
        }

        MaterialAsset[] materialDependencies = models
            .SelectMany(model => model.Materials)
            .OfType<MaterialAsset>()
            .DistinctBy(AssetKey.FromDefinition)
            .Where(material => !mapMaterialKeys.Contains(
                AssetKey.FromDefinition(material)))
            .ToArray();
        PhysPresetAsset[] physPresetDependencies = models
            .Select(model => model.PhysPreset)
            .OfType<PhysPresetAsset>()
            .DistinctBy(AssetKey.FromDefinition)
            .ToArray();
        if (materialDependencies.Length != 98 || physPresetDependencies.Length != 1)
        {
            throw new InvalidDataException(
                $"Template fastfile '{templatePath}' PS3 FFA bootstrap dependency " +
                $"closure contains {materialDependencies.Length} externalizable Material " +
                $"and {physPresetDependencies.Length} PhysPreset provider key(s); " +
                "the hardware-proven closure requires exactly 98 and 1.");
        }

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
            $"{source.SerializedAssetType} bootstrap dependency has no serialized name.");
        if (name.Length == 0 || name.Contains('\0'))
        {
            throw new InvalidDataException(
                $"{source.SerializedAssetType} bootstrap dependency has invalid name '{name}'.");
        }

        return name[0] == ',' ? name : "," + name;
    }

    private static StringTableAsset CreateBootstrapStringTable(
        string assetName,
        uint checksum,
        out string signedChecksum)
    {
        string mapName = Path.GetFileNameWithoutExtension(
            assetName.Replace('\\', '/'));
        if (mapName.Length == 0)
        {
            throw new InvalidDataException(
                $"Map asset name '{assetName}' has no basename for its PS3 configstring table.");
        }

        signedChecksum = unchecked((int)checksum).ToString(CultureInfo.InvariantCulture);
        string tableName =
            $"mp/configstrings/configstrings_ps3_{mapName}_dm.csv";
        return new StringTableAsset
        {
            Name = tableName,
            ColumnCount = 2,
            RowCount = 2,
            Cells =
            [
                CreateStringTableCell("111"),
                CreateStringTableCell("mapcrc"),
                CreateStringTableCell("311"),
                CreateStringTableCell(signedChecksum)
            ]
        };
    }

    private static StringTableCell CreateStringTableCell(string value) =>
        new()
        {
            String = value,
            Hash = CalculateStringTableHash(value)
        };

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

    private static int CalculateStringTableHash(string value)
    {
        uint hash = 0;
        foreach (char character in value)
        {
            if (character > 0x7f)
            {
                throw new InvalidDataException(
                    $"PS3 configstring value '{value}' contains a non-ASCII character.");
            }

            byte current = (byte)character;
            if (current is >= (byte)'A' and <= (byte)'Z')
                current += (byte)('a' - 'A');
            hash = unchecked(hash * 31 + current);
        }

        return unchecked((int)hash);
    }

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
