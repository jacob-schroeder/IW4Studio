using System.Text.Json;
using IW4.Formats.SourceFormat.Material;
using IW4.Formats.SourceFormat.Fx;
using IW4.Formats.SourceFormat.Image;
using IW4.Formats.SourceFormat.PhysCollmap;
using IW4.Formats.SourceFormat.PhysPreset;
using IW4.Formats.SourceFormat.Shader;
using IW4.Formats.SourceFormat.Sound;
using IW4.Formats.SourceFormat.Technique;
using IW4.Formats.SourceFormat.Techset;
using IW4.Formats.SourceFormat.XModel;
using IW4.Game.Assets;
using IW4.Game.Assets.Fx;
using IW4.Game.Assets.GfxMap;
using IW4.Game.Assets.Image;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.Physics;
using IW4.Game.Assets.Sound;
using IW4.Game.Assets.TechniqueSet;
using IW4.Game.Assets.XModel;
using IW4.Game.Zone;
using IW4.Linker.Contracts;
using IW4.Linker.Linking;
using IW4.Studio.Documents;
using IW4.Runtime.Assets.Images;
using IW4.Runtime.Assets.Sound;
using D3dbspLinker.Inspection;

namespace D3dbspLinker.Conversion;

internal static partial class FastFileConverter
{
    /// <summary>Offline extraction only. Normal builds never open the original fastfile.</summary>
    internal static void ExportBootstrap(string input, string imageLibrary, string output)
        => ExportSourceAssets(input, imageLibrary, output, BootstrapXModelNames, Ps3MapBootstrap.FactionMaterials, bootstrap: true);

    internal static void ExportSourceAssets(string input, string imageLibrary, string output,
        IReadOnlyList<string> modelNames, IReadOnlyList<string> materialNames, bool bootstrap = false,
        string? dependencyFastFile = null, IReadOnlyList<string>? fxNames = null)
    {
        string source = Path.GetFullPath(input);
        string destination = Path.GetFullPath(output);
        if (Directory.Exists(destination)) throw new IOException($"Output directory '{destination}' already exists.");
        using FastFileWorkspace workspace = bootstrap
            ? FastFileInspector.Open(source)
            : new FastFileDocumentService().Open(new FastFileDocumentOpenRequest(source, Isolated.Instance));

        using FastFileWorkspace? dependency = dependencyFastFile is null ? null : FastFileInspector.Open(dependencyFastFile);
        FastFileWorkspace[] inputs = dependency is null ? [workspace] : [workspace, dependency];
        var providers = inputs.SelectMany(inputWorkspace => inputWorkspace.LoadedZone.Context.AssetPool.Slots
            .SelectMany(slot => slot.Providers)
            .Where(provider => !provider.IsReferencePlaceholder && provider.Asset is BaseAsset)
            .OrderByDescending(provider => provider.Owner == inputWorkspace.LoadedZone.Context.ZoneOwner)
            .ThenBy(provider => provider.RegistrationSequence)
            .Select(provider => (BaseAsset)provider.Asset))
            .DistinctBy(AssetKey.FromDefinition).ToDictionary(AssetKey.FromDefinition);
        var sourceMaterials = new MaterialSourceCompiler(imageLibrary, Path.Combine(AppContext.BaseDirectory, "bootstrap", "ps3"));
        var sourceImageParts = sourceMaterials.ImageStreamPayloads;
        var imageResolvers = new Dictionary<GfxImageAsset, IGfxImagePayloadResolver>(ReferenceEqualityComparer.Instance);
        var soundResolvers = new Dictionary<SoundAliasListAsset, ISoundPayloadResolver>(ReferenceEqualityComparer.Instance);
        foreach (FastFileWorkspace inputWorkspace in inputs)
        foreach (var provider in inputWorkspace.LoadedZone.Context.AssetPool.Slots.SelectMany(slot => slot.Providers))
        {
            if (provider.IsReferencePlaceholder || provider.Asset is not (GfxImageAsset or SoundAliasListAsset)) continue;
            WorkspaceZone? zone = inputWorkspace.LoadedZones.FirstOrDefault(candidate =>
                candidate.LoadResult.Context.ZoneOwner == provider.Owner);
            if (zone is null) continue;
            if (provider.Asset is GfxImageAsset image)
                imageResolvers.TryAdd(image, zone.LoadResult.ImagePayloadResolver);
            else if (provider.Asset is SoundAliasListAsset sound)
                soundResolvers.TryAdd(sound, zone.LoadResult.SoundPayloadResolver);
        }
        BaseAsset Resolve(XAssetType type, string name)
        {
            AssetKey key = AssetKey.FromWireName(CanonicalAssetFamily.FromSerializedType(type), name);
            if (providers.TryGetValue(key, out BaseAsset? value)) return value;
            return type switch
            {
                XAssetType.Image => sourceMaterials.LoadImage(name),
                XAssetType.Material => sourceMaterials.LoadMaterial(name),
                XAssetType.Techset => sourceMaterials.LoadTechniqueSet(name),
                XAssetType.VertexShader => sourceMaterials.LoadShader(name, MaterialShaderKind.Vertex),
                XAssetType.PixelShader => sourceMaterials.LoadShader(name, MaterialShaderKind.Pixel),
                _ => throw new InvalidDataException($"Source fastfile and disk library are missing full {type} '{name}'.")
            };
        }
        var pending = new Queue<BaseAsset>();
        var seen = new HashSet<AssetKey>();
        void Include(BaseAsset? asset)
        {
            if (asset is null) return;
            string name = asset.SerializedAssetName ?? throw new InvalidDataException("Bootstrap dependency has no name.");
            BaseAsset full = Resolve(asset.SerializedAssetType, name);
            if (seen.Add(AssetKey.FromDefinition(full))) pending.Enqueue(full);
        }
        foreach (string name in modelNames) Include(Resolve(XAssetType.XModel, name));
        foreach (string name in materialNames) Include(Resolve(XAssetType.Material, name));
        foreach (string name in fxNames ?? []) Include(Resolve(XAssetType.Fx, name));
        string staging = destination + "." + Guid.NewGuid().ToString("N") + ".extracting";
        Directory.CreateDirectory(staging);
        try
        {
            while (pending.TryDequeue(out BaseAsset? asset))
            {
                try
                {
                    switch (asset)
                    {
                        case XModelAsset model:
                            new XModelNativeExchange().Unlink(staging, model,
                                name => (XModelSurfsAsset)Resolve(XAssetType.XModelSurfs, name));
                            foreach (MaterialAsset? material in model.Materials) Include(material);
                            Include(model.PhysPreset);
                            Include(model.PhysCollmap);
                            break;
                        case FxEffectDefAsset effect:
                            new FxExchange().Unlink(staging, effect);
                            foreach (FxElemDef element in effect.ElemDefs)
                            {
                                IncludeFx(element.EffectOnImpact.Name);
                                IncludeFx(element.EffectOnDeath.Name);
                                IncludeFx(element.EffectEmitted.Name);
                                foreach (FxElemDefVisuals visuals in element.VisualArray.Prepend(element.Visuals))
                                {
                                    Include(visuals.Material?.Material);
                                    Include(visuals.Model?.Model);
                                    if (visuals.Effect is { } runner) IncludeFx(runner.EffectDef.Name);
                                    if (visuals.Sound is { SoundName: { } soundName })
                                        Include(Resolve(XAssetType.Sound, soundName));
                                }
                                foreach (FxElemMarkVisuals mark in element.MarkVisualArray)
                                {
                                    Include(mark.Material0);
                                    Include(mark.Material1);
                                }
                            }
                            break;
                        case SoundAliasListAsset sound:
                            soundResolvers.TryGetValue(sound, out ISoundPayloadResolver? soundResolver);
                            new SoundAliasListExchange().Unlink(staging, sound, streamed =>
                            {
                                if (soundResolver is null)
                                    throw new InvalidDataException(
                                        $"Sound '{sound.AliasName}' needs streamed audio but has no provider payload resolver.");
                                if (!soundResolver.TryResolvePayload(streamed, out byte[] payload, out string reason))
                                    throw new InvalidDataException(
                                        $"Sound '{sound.AliasName}' needs streamed audio: {reason}");
                                return payload;
                            });
                            foreach (SndAlias alias in sound.Aliases)
                            {
                                if (!string.IsNullOrWhiteSpace(alias.SecondaryAliasName))
                                    Include(Resolve(XAssetType.Sound, alias.SecondaryAliasName));
                                if (!string.IsNullOrWhiteSpace(alias.ChainAliasName))
                                    Include(Resolve(XAssetType.Sound, alias.ChainAliasName));
                            }
                            break;
                        case PhysPresetAsset preset:
                            new PhysPresetExchange().Unlink(staging, preset);
                            break;
                        case PhysCollmapAsset collision:
                            new PhysCollmapExchange().Unlink(staging, collision);
                            break;
                        case MaterialAsset material:
                            new MaterialExchange().Unlink(staging, material);
                            Include(material.TechniqueSet);
                            foreach (var texture in material.Textures)
                            {
                                Include(texture.Image);
                                Include(texture.Water?.Image);
                            }
                            break;
                        case MaterialTechniqueSetAsset techniques:
                            new TechsetExchange().Unlink(staging, techniques);
                            foreach (MaterialTechniqueAsset technique in techniques.TechniqueSlots
                                         .Select(slot => slot.Technique).OfType<MaterialTechniqueAsset>())
                            {
                                new TechniqueExchange().Unlink(staging, technique);
                                foreach (MaterialPassAsset pass in technique.Passes)
                                {
                                    Include(pass.VertexShader);
                                    Include(pass.PixelShader);
                                }
                            }
                            break;
                        case MaterialShaderAsset shader:
                            new ShaderExchange().Unlink(staging, shader);
                            break;
                        case GfxImageAsset image:
                            if (imageResolvers.TryGetValue(image, out IGfxImagePayloadResolver? resolver))
                                NativeImageSourceExport.Unlink(staging, image, resolver);
                            else
                                new ImageExchange().UnlinkNative(staging, image,
                                    sourceImageParts.FirstOrDefault(pair =>
                                        AssetKey.FromDefinition(pair.Key).Equals(AssetKey.FromDefinition(image))).Value);
                            break;
                        default:
                            throw new NotSupportedException($"Bootstrap source export does not support {asset.SerializedAssetType}.");
                    }
                }
                catch (Exception exception) when (exception is IOException or NotSupportedException or JsonException)
                {
                    throw new InvalidDataException($"Cannot extract {asset.SerializedAssetType} '{asset.SerializedAssetName}': {exception.Message}", exception);
                }
            }
            if (bootstrap)
            {
                GfxWorldAsset world = FastFileInspector.GetSingle<GfxWorldAsset>(workspace)
                    ?? throw new InvalidDataException("Bootstrap source must contain one GfxWorld.");
                var settings = new Ps3MapBootstrap
                {
                    Format = "iw4-ps3-map-bootstrap", Version = 1,
                    FragmentProgramUploadCapacity = world.FragmentProgramUploadCapacity,
                    LanguageMask = workspace.InitialLinkRequest.LanguageMask,
                    SelectedLanguageMask = workspace.InitialLinkRequest.SelectedLanguageMask,
                    ScriptStrings = workspace.InitialLinkRequest.ScriptStrings.ToArray()
                };
                File.WriteAllText(Path.Combine(staging, Ps3MapBootstrap.FileName), JsonSerializer.Serialize(settings, Ps3MapBootstrap.JsonOptions));
            }
            Directory.Move(staging, destination);
            Console.WriteLine($"Exported {seen.Count} source asset definitions to {destination}");
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }

        void IncludeFx(string? name)
        {
            if (!string.IsNullOrWhiteSpace(name)) Include(Resolve(XAssetType.Fx, name));
        }
    }
}
