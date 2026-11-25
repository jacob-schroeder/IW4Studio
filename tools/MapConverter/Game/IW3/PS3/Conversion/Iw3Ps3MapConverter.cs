using System.Text;
using IW4.Formats.SourceFormat.Material;
using IW4.Formats.SourceFormat.Fx;
using IW4.Formats.SourceFormat.Sound;
using IW4.Formats.SourceFormat.Tracer;
using IW4.Formats.SourceFormat.Weapon;
using IW4.Game.Assets;
using IW4.Game.Assets.ColMap;
using IW4.Game.Assets.FxMap;
using IW4.Game.Assets.Fx;
using IW4.Game.Assets.GfxMap;
using IW4.Game.Assets.Image;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.RawFile;
using IW4.Game.Assets.MapEnts;
using IW4.Game.Assets.Sound;
using IW4.Game.Assets.Tracer;
using IW4.Game.Assets.Weapon;
using IW4.Game.Assets.XModel;
using IW4.Game.Zone;
using IW4.Linker.Contracts;
using IW4.Linker.D3dbsp;
using IW4.Linker.Linking;
using IW4.Linker.Packaging;
using IW4.Runtime.Diagnostics;
using IW4.Studio.Documents;
using MapConverter.CommandLine;
using MapConverter.Game.IW3.PC.Conversion;
using MapConverter.Game.IW3.PS3.Extraction;
using MapConverter.Game.IW3.PS3.Images;
using MapConverter.Game.IW3.PS3.Rules;
using MapConverter.Game.IW3.PS3.Techniques;

namespace MapConverter.Game.IW3.PS3.Conversion;

internal static class Iw3Ps3MapConverter
{
    internal static (string Map, string Load, string Images, string Sounds, string Ui, int AssetCount, bool DynamicShadowMaskDisabled) Convert(
        MapConverterOptions options, CancellationToken token = default)
    {
        if (options.ImageFileIndex != -1)
            throw new ArgumentException("PS3 map conversion requires a named package: '--imagefile-index -1'.");
        string mapPath = Path.GetFullPath(options.MapPath);
        string loadPath = Path.GetFullPath(options.LoadPath ?? throw new ArgumentException("A PS3 loading fastfile is required."));
        string mapName = Path.GetFileNameWithoutExtension(mapPath);
        string output = Path.GetFullPath(options.OutputDirectory);
        string[] names = [mapName + ".ff", mapName + "_load.ff", mapName + ".pak", mapName + "_snd.pak", MapUiRules.ZoneName(mapName) + ".ff"];
        foreach (string name in names)
            if (File.Exists(Path.Combine(output, name)))
                throw new IOException($"Conversion output already exists: '{Path.Combine(output, name)}'.");
        string sourceDirectory = Path.GetFullPath(options.SourceFastFileDirectory ??
            options.SourceLibraryDirectory ?? Path.GetDirectoryName(mapPath) ??
                throw new ArgumentException("The source map has no directory."));
        byte[] ui = Iw3Ps3MapUiCompiler.Build(mapName, sourceDirectory,
            Path.GetFullPath(options.SourceLibraryDirectory ?? sourceDirectory), options.Factions, token);
        string[] sources = new[]
        {
            Path.Combine(sourceDirectory, "code_post_gfx_mp.ff"),
            Path.Combine(sourceDirectory, "common_mp.ff"), mapPath, loadPath
        }.Distinct(StringComparer.Ordinal).ToArray();
        string prefix = mapName + "/";
        var shaders = new Iw3Ps3ShaderLibrary();
        var images = new Iw3Ps3ImageLibrary(prefix);
        foreach (string source in sources)
        {
            shaders.AddFastFile(source, token);
            images.AddFastFile(source, token);
        }
        var techniques = new Iw3Ps3TechniqueCompiler(shaders.Resolve, shaders.ResolveSet, prefix);
        RawFileAsset? sourceVehicleRecipes = Iw3Ps3FastFileCatalog.FindInlineRawFile(
            Path.Combine(sourceDirectory, "common_mp.ff"), "maps/mp/_destructible_types.gsc", token);
        string? vehicleRecipeText = sourceVehicleRecipes is null ? null : Encoding.Latin1.GetString(
            sourceVehicleRecipes.Buffer ?? throw new InvalidDataException("Source vehicle recipe has no content."),
            0, sourceVehicleRecipes.Len);
        var definitions = Iw3Ps3FastFileCatalog.ReadMapAssets(mapPath, techniques, images, shaders, vehicleRecipeText,
            out IReadOnlySet<string> splitStaticModelNames, out var mapSounds, out var vehicleRecipes, token).ToList();
        var convertedModels = definitions.OfType<XModelAsset>().Select(model => model.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] missingVehicleModels = vehicleRecipes.ModelNames
            .Where(name => !convertedModels.Contains(prefix + name)).ToArray();
        if (missingVehicleModels.Length != 0)
            throw new InvalidDataException("Missing source vehicle models: " + string.Join(", ", missingVehicleModels));
        if (vehicleRecipes.Script.Length != 0)
            definitions.Add(CreateRawFile($"maps/mp/{mapName}_vehicles.gsc", vehicleRecipes.Script));
        RawFileAsset[] sourceScripts = definitions.OfType<RawFileAsset>().ToArray();
        var requestedEffects = Iw3Ps3RawFileCompiler.ReadEffectNames(sourceScripts).ToHashSet(StringComparer.Ordinal);
        requestedEffects.UnionWith(vehicleRecipes.EffectNames);
        definitions.AddRange(Iw3Ps3FastFileCatalog.ReadEffectAssets(sources,
            requestedEffects,
            definitions.OfType<FxEffectDefAsset>().ToArray(),
            techniques, images, shaders, token));
        definitions.AddRange(techniques.CompiledShaders);
        RawFileAsset mainScript = definitions.OfType<RawFileAsset>()
            .Single(asset => asset.Name == $"maps/mp/{mapName}.gsc");
        foreach (string compassName in Iw3Ps3RawFileCompiler.ReadMinimapMaterialNames(mainScript))
        {
            if (definitions.Any(asset => asset.SerializedAssetType == XAssetType.Material &&
                    asset.SerializedAssetName == prefix + compassName)) continue;
            // Resolve the source material's actual inline image. Its name can
            // be a packed XString alias, which the image index keeps by location.
            int imageRoot = Iw3Ps3FastFileCatalog.FindInlineUiImageRoot(mapPath, compassName, 43, token) ??
                throw new InvalidDataException($"Source map has no supported compass material '{compassName}'.");
            GfxImageAsset compassImage = images.ResolveAt(mapPath, imageRoot, compassName);
            if (compassImage.TextureSemantic != TextureSemantic.TwoDimensional || compassImage.IsCubemap)
                throw new InvalidDataException($"Source minimap '{compassName}' requires a two-dimensional image.");
            definitions.Add(Ps3LoadZoneBuilder.CreateCompassMaterial(prefix + compassName, compassImage));
        }
        string bootstrapDirectory = Path.Combine(AppContext.BaseDirectory, "bootstrap", "ps3");
        RawFileAsset globalFx = Iw3Ps3FastFileCatalog.FindInlineRawFile(
            Path.Combine(sourceDirectory, "common_mp.ff"), "maps/mp/_global_fx.gsc", token) ??
            throw new InvalidDataException("Missing COD4 shared prefab script 'maps/mp/_global_fx.gsc'.");
        var sharedSoundNames = Iw3Ps3RawFileCompiler.ReadSharedPrefabSoundNames(globalFx,
            D3dbspAssetLinker.ReadEntities(definitions.OfType<MapEntsAsset>().Single()
                .EntityStringBytes.ToArray())).ToHashSet(StringComparer.Ordinal);
        var soundNames = Iw3Ps3RawFileCompiler.ReadSoundNames(sourceScripts).ToHashSet(StringComparer.Ordinal);
        soundNames.UnionWith(vehicleRecipes.SoundNames);
        foreach (FxElemDef element in definitions.OfType<FxEffectDefAsset>().SelectMany(effect => effect.ElemDefs))
            foreach (FxElemDefVisuals visual in element.VisualArray.Prepend(element.Visuals))
                if (visual.Sound?.SoundName is { Length: > 0 } sound)
                    soundNames.Add(sound.StartsWith(prefix, StringComparison.Ordinal) ? sound[prefix.Length..] : sound);
        var audio = Iw3Ps3AudioCompiler.Capture(sourceDirectory, bootstrapDirectory, prefix,
            Iw3Ps3RawFileCompiler.ReadSoundNames(sourceScripts, ambientOnly: true).ToHashSet(StringComparer.Ordinal),
            soundNames, sharedSoundNames, mapSounds, token);
        definitions.AddRange(audio.Assets);
        Iw3Ps3MaterialRoutingCompiler.Apply(definitions, prefix, bootstrapDirectory);
        var bootstrapMaterials = new MaterialSourceCompiler(bootstrapDirectory);
        var bootstrapModels = new ModelSourceCompiler(bootstrapDirectory, bootstrapMaterials);
        foreach (string name in FactionRules.XModelNames(options.Factions, Ps3MapStartupAssets.XModelNames))
        {
            token.ThrowIfCancellationRequested();
            bootstrapModels.LoadModel(name);
        }
        foreach (string name in FactionRules.MaterialNames(options.Factions, Ps3MapStartupAssets.FactionMaterialNames))
            bootstrapMaterials.LoadMaterial(name);
        bool hasNativeTurret = definitions.OfType<MapEntsAsset>().Any(entities =>
            D3dbspAssetLinker.ReadNamedEntityModelNames(entities.EntityStringBytes.ToArray())
                .Contains(EntityRules.NativeTurretModel, StringComparer.OrdinalIgnoreCase));
        if (hasNativeTurret)
            AddNativeTurret(definitions, bootstrapDirectory, bootstrapModels, bootstrapMaterials, token);
        definitions.AddRange(bootstrapModels.Assets);
        definitions.AddRange(bootstrapMaterials.Assets);
        var callsigns = AddCallsigns(definitions, bootstrapDirectory);
        var world = definitions.OfType<GfxWorldAsset>().Single();
        var collision = definitions.OfType<ClipMapAsset>().Single();
        string assetName = world.Name ?? throw new InvalidDataException("The source world has no name.");
        definitions.Add(new FxWorldAsset { Name = assetName });
        definitions.Add(D3dbspAssetLinker.CreatePs3DmConfigStringBaseline(assetName, collision.Checksum));
        if (!definitions.OfType<RawFileAsset>().Any(asset => asset.Name == mapName))
            definitions.Add(CreateRawFile(mapName, ""));
        string visionName = $"vision/{mapName}.vision";
        if (!definitions.OfType<RawFileAsset>().Any(asset => asset.Name == visionName))
        {
            RawFileAsset? vision = null;
            foreach (string source in sources)
            {
                vision = Iw3Ps3FastFileCatalog.FindInlineRawFile(source, visionName, token);
                if (vision is not null) break;
            }
            if (vision is null) throw new InvalidDataException($"Missing COD4 vision file '{visionName}'.");
            definitions.Add(vision);
        }
        string Name(BaseAsset asset) => asset.SerializedAssetName ??
            throw new InvalidDataException("A converted asset has no name.");
        RawFileAsset[] scripts = Iw3Ps3RawFileCompiler.Compile(definitions.OfType<RawFileAsset>().ToArray(), prefix,
            definitions.OfType<FxEffectDefAsset>().Select(Name).ToHashSet(StringComparer.Ordinal),
            definitions.OfType<XModelAsset>().Select(Name).ToHashSet(StringComparer.Ordinal),
            definitions.OfType<MaterialAsset>().Select(Name).ToHashSet(StringComparer.Ordinal),
            definitions.OfType<SoundAliasListAsset>().Select(Name).ToHashSet(StringComparer.Ordinal),
            splitStaticModelNames, options.Factions);
        definitions.RemoveAll(asset => asset is RawFileAsset);
        definitions.AddRange(scripts);
        GfxImageAsset loadingImage = images.Resolve("loadscreen_" + mapName);
        var streamed = images.Compiled.Where(image => image.StreamPartPayloads.Count != 0).ToArray();
        var imageParts = streamed.ToDictionary(
            image => AssetKey.FromDefinition(image.Image),
            image => (IReadOnlyList<byte[]>)image.StreamPartPayloads.Select(part => part.ToArray()).ToArray());
        foreach (var (image, parts) in bootstrapMaterials.ImageStreamPayloads)
            imageParts.Add(AssetKey.FromDefinition(image), parts);
        NamedImageFilePackage package = ImageFilePackager.Package(imageParts, 1);
        var references = package.References.ToDictionary(pair => pair.Key, pair => pair.Value.Single());
        definitions.AddRange(images.Compiled.Select(image => image.Image));
        BaseAsset[] unique = definitions.DistinctBy(AssetKey.FromDefinition).ToArray();
        BaseAsset[] providerOnly = unique.Where(IsProviderOnly).ToArray();
        // As in the PC conversion path, explicit dependency roots must precede
        // their owners so the target fastfile can keep every root inline.
        BaseAsset[] roots = unique.Where(asset => !IsProviderOnly(asset))
            .OrderBy(RootRank).ToArray();
        // Keep the native callsign imagefile references frozen. Map-owned images
        // still use the named package, and authored bootstrap assets take precedence.
        LinkAssetPool callsignProviders = callsigns.Providers.WithoutProviders(unique
            .Where(asset => !Name(asset).StartsWith(',') &&
                !callsigns.RootKeys.Contains(AssetKey.FromDefinition(asset)))
            .Select(AssetKey.FromDefinition));
        byte[] map = Iw3PcMapConverter.LinkAndPackage(mapName, roots, providerOnly, references,
            callsignProviders, callsigns.RootKeys);
        var loadingReferences = references.TryGetValue(AssetKey.FromDefinition(loadingImage), out var loadReference)
            ? new[] { loadReference } : [];
        byte[] load = Ps3LoadZoneBuilder.Build(mapName + "_load", loadingImage, loadingReferences,
            CreateRawFile(mapName + "_load", ""));
        string stage = Path.Combine(Path.GetTempPath(), "iw3ps3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        var published = new List<string>();
        try
        {
            File.WriteAllBytes(Path.Combine(stage, names[0]), map);
            File.WriteAllBytes(Path.Combine(stage, names[1]), load);
            File.WriteAllBytes(Path.Combine(stage, names[2]), package.Bytes);
            File.WriteAllBytes(Path.Combine(stage, names[3]), audio.SoundPackage);
            File.WriteAllBytes(Path.Combine(stage, names[4]), ui);
            Directory.CreateDirectory(output);
            foreach (string name in names)
            {
                string destination = Path.Combine(output, name);
                File.Move(Path.Combine(stage, name), destination);
                published.Add(destination);
            }
        }
        catch
        {
            foreach (string path in published) File.Delete(path);
            throw;
        }
        finally { Directory.Delete(stage, recursive: true); }
        return (Path.Combine(output, names[0]), Path.Combine(output, names[1]),
            Path.Combine(output, names[2]), Path.Combine(output, names[3]), Path.Combine(output, names[4]),
            roots.Length, techniques.DynamicShadowMaskDisabled);
    }

    private static RawFileAsset CreateRawFile(string name, string text)
    {
        byte[] content = Encoding.UTF8.GetBytes(text + '\0');
        return new RawFileAsset { Name = name, Len = content.Length - 1, Buffer = content };
    }

    internal static (LinkAssetPool Providers, IReadOnlySet<AssetKey> RootKeys,
        IReadOnlySet<uint> ReferencedImageFileIndices) AddCallsigns(
        List<BaseAsset> definitions, string bootstrapDirectory)
    {
        using var source = new FastFileDocumentService().Open(new FastFileDocumentOpenRequest(
            Path.Combine(bootstrapDirectory, "callsigns.ff"), Isolated.Instance));
        var assets = source.LoadedZone.Context.AssetPool.Slots
            .Select(slot => slot.CanonicalAsset).ToDictionary(AssetKey.FromDefinition);
        var owned = new HashSet<AssetKey>();
        foreach (LinkRoot root in source.InitialLinkRequest.Roots)
        {
            if (root.SerializedType is not (XAssetType.Material or XAssetType.StringTable) ||
                root.Intent is not (LinkRootIntent.Owned or LinkRootIntent.External) ||
                root.Asset is not { } key || !assets.TryGetValue(key, out BaseAsset? asset))
                throw new InvalidDataException("Invalid native callsign bootstrap root.");
            if (definitions.Any(existing => AssetKey.FromDefinition(existing) == key))
                throw new InvalidDataException($"The map already defines callsign asset {key}.");
            definitions.Add(asset);
            if (root.Intent == LinkRootIntent.Owned) owned.Add(key);
        }
        return (source.InitialLinkRequest.Assets, owned,
            source.LoadedZone.Header.ImageStreamEntries.Where(entry => !entry.IsEmpty)
                .Select(entry => entry.FileIndex).ToHashSet());
    }

    internal static bool IsProviderOnly(BaseAsset asset) => asset.SerializedAssetType is
        XAssetType.XModelSurfs or XAssetType.PhysPreset or XAssetType.PhysCollmap or
        XAssetType.Tracer or XAssetType.SndCurve or XAssetType.LoadedSound;

    internal static int RootRank(BaseAsset asset) => asset.SerializedAssetType switch
    {
        XAssetType.Image => 0,
        XAssetType.PixelShader or XAssetType.VertexShader => 1,
        XAssetType.Techset => 2,
        XAssetType.Material => 3,
        XAssetType.XModel => 4,
        XAssetType.Fx => 5,
        XAssetType.Sound or XAssetType.LightDef => 6,
        XAssetType.MapEnts => 7,
        XAssetType.ComMap => 8,
        XAssetType.GameMapMp => 9,
        XAssetType.FxMap => 10,
        XAssetType.GfxMap => 11,
        XAssetType.ColMapMp => 12,
        XAssetType.StringTable => 13,
        XAssetType.RawFile => 14,
        XAssetType.Weapon => 15,
        _ => throw new InvalidDataException($"Unsupported PS3 output root type {asset.SerializedAssetType}.")
    };

    internal static void AddNativeTurret(List<BaseAsset> definitions, string bootstrapDirectory,
        ModelSourceCompiler models, MaterialSourceCompiler materials, CancellationToken token)
    {
        var linked = new Dictionary<AssetKey, BaseAsset>();
        var traversed = new HashSet<AssetKey>();
        var pending = new Queue<(XAssetType Type, string Name)>();
        var soundPackage = new SoundFilePackager();
        models.LoadModel(EntityRules.NativeTurretModel);
        WeaponAsset weapon = new WeaponNativeExchange().Link(bootstrapDirectory,
            WeaponNativeExchange.SupportedWeaponName, ResolveWeaponDependency);
        foreach (var dependency in WeaponNativeExchange.EnumerateDependencies(weapon))
            pending.Enqueue(dependency);
        while (pending.TryDequeue(out var dependency))
        {
            token.ThrowIfCancellationRequested();
            AssetKey key = AssetKey.FromWireName(
                CanonicalAssetFamily.FromSerializedType(dependency.Type), dependency.Name);
            if (!traversed.Add(key)) continue;
            if (dependency.Type == XAssetType.Sound &&
                SoundRules.IsResidentNativeTurretAlias(dependency.Name)) continue;
            if (dependency.Type is XAssetType.Material or XAssetType.Tracer or XAssetType.RawFile)
            {
                BaseAsset asset = ResolveWeaponDependency(dependency.Type, dependency.Name);
                if (asset is RawFileAsset rawFile)
                {
                    byte[] content = rawFile.Buffer ?? throw new InvalidDataException(
                        $"Turret RawFile '{rawFile.Name}' has no contents.");
                    foreach (string graph in WeaponNativeExchange.EnumerateRumbleGraphFiles(
                        dependency.Name, content.AsSpan(0, rawFile.Len)))
                        pending.Enqueue((XAssetType.RawFile, graph));
                }
                continue;
            }
            BaseAsset loaded = linked.TryGetValue(key, out BaseAsset? existing) ? existing : dependency.Type switch
            {
                XAssetType.Fx => new FxExchange().Link(bootstrapDirectory, dependency.Name),
                XAssetType.Sound => new SoundAliasListExchange().Link(
                    bootstrapDirectory, dependency.Name, soundPackage.AddPayload),
                _ => throw new InvalidDataException(
                    $"Unsupported native turret dependency {dependency.Type} '{dependency.Name}'.")
            };
            linked.TryAdd(key, loaded);
            if (loaded is FxEffectDefAsset effect)
            {
                foreach (FxElemDef element in effect.ElemDefs)
                {
                    Enqueue(XAssetType.Fx, element.EffectOnImpact.Name);
                    Enqueue(XAssetType.Fx, element.EffectOnDeath.Name);
                    Enqueue(XAssetType.Fx, element.EffectEmitted.Name);
                    foreach (FxElemDefVisuals visual in element.VisualArray.Prepend(element.Visuals))
                    {
                        if (visual.Effect is { } runner)
                            Enqueue(XAssetType.Fx, runner.EffectDef.Name);
                        if (visual.Sound is { } sound)
                            Enqueue(XAssetType.Sound, sound.SoundName);
                        if (visual.Model?.Model?.Name is { } model)
                            models.LoadModel(model);
                        if (visual.Material?.Material?.Info.Name is { } material)
                            materials.LoadMaterial(material);
                    }
                    foreach (FxElemMarkVisuals mark in element.MarkVisualArray)
                    {
                        if (mark.Material0?.Info.Name is { } first) materials.LoadMaterial(first);
                        if (mark.Material1?.Info.Name is { } second) materials.LoadMaterial(second);
                    }
                }
            }
            else if (loaded is SoundAliasListAsset sound)
            {
                foreach (SndAlias alias in sound.Aliases)
                {
                    Enqueue(XAssetType.Sound, alias.SecondaryAliasName);
                    Enqueue(XAssetType.Sound, alias.ChainAliasName);
                    if (alias.VolumeFalloffCurve is { } curve)
                        linked.TryAdd(AssetKey.FromDefinition(curve), curve);
                    foreach (LoadedSound sample in alias.SoundFiles
                                 .Select(file => file.Loaded?.LoadedSound).OfType<LoadedSound>())
                        linked.TryAdd(AssetKey.FromDefinition(sample), sample);
                }
            }
        }
        if (soundPackage.PayloadCount != 0)
            throw new InvalidDataException("Native turret requires a streamed sound package, which PS3 map conversion does not publish.");
        definitions.AddRange(linked.Values);
        definitions.Add(weapon);

        void Enqueue(XAssetType type, string? name)
        {
            if (!string.IsNullOrWhiteSpace(name)) pending.Enqueue((type, name));
        }

        BaseAsset ResolveWeaponDependency(XAssetType type, string name)
        {
            AssetKey key = AssetKey.FromWireName(CanonicalAssetFamily.FromSerializedType(type), name);
            if (linked.TryGetValue(key, out BaseAsset? existing)) return existing;
            BaseAsset asset = type switch
            {
                XAssetType.Material => materials.LoadMaterial(name),
                XAssetType.Fx => new FxExchange().Link(bootstrapDirectory, name),
                XAssetType.Tracer => new TracerNativeExchange().Link(
                    bootstrapDirectory, name, materials.LoadMaterial),
                XAssetType.RawFile => ReadTurretRawFile(name),
                _ => throw new InvalidDataException($"Unsupported native turret Weapon dependency {type} '{name}'.")
            };
            linked.Add(key, asset);
            return asset;
        }

        RawFileAsset ReadTurretRawFile(string name)
        {
            string root = Path.GetFullPath(bootstrapDirectory);
            string path = Path.GetFullPath(Path.Combine(root, name));
            string relative = Path.GetRelativePath(root, path);
            if (Path.IsPathRooted(relative) || relative == ".." ||
                relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                throw new InvalidDataException($"Turret RawFile '{name}' escapes bootstrap.");
            byte[] content = File.ReadAllBytes(path);
            if (content.AsSpan().IndexOf((byte)0) >= 0)
                throw new InvalidDataException($"Turret RawFile '{name}' contains a null byte.");
            return new RawFileAsset { Name = name, Len = content.Length, Buffer = [.. content, 0] };
        }
    }
}
