using System.Text;
using IW4.Formats.SourceFormat.Material;
using IW4.Game.Assets;
using IW4.Game.Assets.ColMap;
using IW4.Game.Assets.FxMap;
using IW4.Game.Assets.Fx;
using IW4.Game.Assets.GfxMap;
using IW4.Game.Assets.Image;
using IW4.Game.Assets.RawFile;
using IW4.Game.Assets.XModel;
using IW4.Game.Zone;
using IW4.Linker.Contracts;
using IW4.Linker.D3dbsp;
using IW4.Linker.Linking;
using IW4.Linker.Packaging;
using MapConverter.CommandLine;
using MapConverter.Game.IW3.PC.Conversion;
using MapConverter.Game.IW3.PS3.Extraction;
using MapConverter.Game.IW3.PS3.Images;
using MapConverter.Game.IW3.PS3.Techniques;

namespace MapConverter.Game.IW3.PS3.Conversion;

internal static class Iw3Ps3MapConverter
{
    internal static (string Map, string Load, string Images, int AssetCount, bool DynamicShadowMaskDisabled) Convert(
        MapConverterOptions options, CancellationToken token = default)
    {
        if (options.ImageFileIndex != -1)
            throw new ArgumentException("PS3 map conversion requires a named package: '--imagefile-index -1'.");
        string mapPath = Path.GetFullPath(options.MapPath);
        string loadPath = Path.GetFullPath(options.LoadPath ?? throw new ArgumentException("A PS3 loading fastfile is required."));
        string mapName = Path.GetFileNameWithoutExtension(mapPath);
        string output = Path.GetFullPath(options.OutputDirectory);
        string[] names = [mapName + ".ff", mapName + "_load.ff", mapName + ".pak"];
        foreach (string name in names)
            if (File.Exists(Path.Combine(output, name)))
                throw new IOException($"Conversion output already exists: '{Path.Combine(output, name)}'.");
        string sourceDirectory = Path.GetFullPath(options.SourceFastFileDirectory ??
            options.SourceLibraryDirectory ?? Path.GetDirectoryName(mapPath) ??
                throw new ArgumentException("The source map has no directory."));
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
        var definitions = Iw3Ps3FastFileCatalog.ReadMapAssets(mapPath, techniques, images, shaders, token).ToList();
        string bootstrapDirectory = Path.Combine(AppContext.BaseDirectory, "bootstrap", "ps3");
        var bootstrapMaterials = new MaterialSourceCompiler(bootstrapDirectory);
        var bootstrapModels = new ModelSourceCompiler(bootstrapDirectory, bootstrapMaterials);
        foreach (string name in Ps3MapStartupAssets.XModelNames)
        {
            token.ThrowIfCancellationRequested();
            bootstrapModels.LoadModel(name);
        }
        foreach (string name in Ps3MapStartupAssets.FactionMaterialNames)
            bootstrapMaterials.LoadMaterial(name);
        definitions.AddRange(bootstrapModels.Assets);
        definitions.AddRange(bootstrapMaterials.Assets);
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
            definitions.OfType<IW4.Game.Assets.Material.MaterialAsset>().Select(Name).ToHashSet(StringComparer.Ordinal));
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
        bool ProviderOnly(BaseAsset asset) => asset.SerializedAssetType is
            XAssetType.XModelSurfs or XAssetType.PhysPreset or XAssetType.PhysCollmap;
        BaseAsset[] providerOnly = unique.Where(ProviderOnly).ToArray();
        // As in the PC conversion path, explicit dependency roots must precede
        // their owners so the target fastfile can keep every root inline.
        BaseAsset[] roots = unique.Where(asset => !ProviderOnly(asset))
            .OrderBy(asset => asset.SerializedAssetType switch
            {
                XAssetType.Image => 0,
                XAssetType.PixelShader or XAssetType.VertexShader => 1,
                XAssetType.Techset => 2,
                XAssetType.Material => 3,
                XAssetType.XModel => 4,
                XAssetType.Fx => 5,
                XAssetType.LightDef => 6,
                XAssetType.MapEnts => 7,
                XAssetType.ComMap => 8,
                XAssetType.GameMapMp => 9,
                XAssetType.FxMap => 10,
                XAssetType.GfxMap => 11,
                XAssetType.ColMapMp => 12,
                XAssetType.StringTable => 13,
                XAssetType.RawFile => 14,
                _ => throw new InvalidDataException($"Unsupported PS3 output root type {asset.SerializedAssetType}.")
            }).ToArray();
        byte[] map = Iw3PcMapConverter.LinkAndPackage(mapName, roots, providerOnly, references);
        var loadingReferences = references.TryGetValue(AssetKey.FromDefinition(loadingImage), out var loadReference)
            ? new[] { loadReference } : [];
        byte[] load = Iw4LoadZoneBuilder.Build(mapName + "_load", loadingImage, loadingReferences,
            CreateRawFile(mapName + "_load", ""));
        string stage = Path.Combine(Path.GetTempPath(), "iw3ps3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        var published = new List<string>();
        try
        {
            File.WriteAllBytes(Path.Combine(stage, names[0]), map);
            File.WriteAllBytes(Path.Combine(stage, names[1]), load);
            File.WriteAllBytes(Path.Combine(stage, names[2]), package.Bytes);
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
            Path.Combine(output, names[2]), roots.Length, techniques.DynamicShadowMaskDisabled);
    }

    private static RawFileAsset CreateRawFile(string name, string text)
    {
        byte[] content = Encoding.UTF8.GetBytes(text + '\0');
        return new RawFileAsset { Name = name, Len = content.Length - 1, Buffer = content };
    }
}
