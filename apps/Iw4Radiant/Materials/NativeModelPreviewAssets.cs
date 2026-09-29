using System.Text.Json;
using IW4.Formats.SourceFormat.Image;
using IW4.Formats.SourceFormat.Material;
using IW4.Formats.SourceFormat.XModel;
using IW4.Formats.XModel;
using IW4.Game.Assets.Image;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.Physics;
using IW4.Game.Assets.TechniqueSet;
using IW4.Render.Textures;

namespace Iw4Radiant.Materials;

/// <summary>Native bootstrap assets for model, faction, weapon and Walk viewmodel previews.</summary>
internal sealed class NativeModelPreviewAssets
{
    private readonly string _root;
    private readonly string? _customRoot;
    private readonly Dictionary<string, XModelNativeImport> _models = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Name, int Lod), XModelSource> _sources = new();
    private readonly Dictionary<string, IReadOnlyList<int>> _availableLods = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MaterialAsset> _materials = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GfxImageAsset> _images = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<byte[]>> _imageParts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (int Width, int Height, byte[] Pixels, MaterialSurfaceState Surface)> _textures = new(StringComparer.Ordinal);

    internal NativeModelPreviewAssets(string bootstrapRoot, string? customRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bootstrapRoot);
        _root = Path.GetFullPath(bootstrapRoot);
        _customRoot = customRoot is null ? null : Path.GetFullPath(customRoot);
        if (!Directory.Exists(Path.Combine(_root, "xmodel_native")))
            throw new DirectoryNotFoundException($"Native XModels were not found under '{_root}'.");
        if (_customRoot is not null && !Directory.Exists(Path.Combine(_customRoot, "xmodel_native")))
            throw new DirectoryNotFoundException($"Imported native XModels were not found under '{_customRoot}'.");
    }

    private string AssetRoot(string folder, string name) =>
        _customRoot is not null && File.Exists(Path.Combine(_customRoot, folder, name + ".json"))
            ? _customRoot : _root;

    private string ImageRoot(string name) =>
        _customRoot is not null && File.Exists(Path.Combine(_customRoot, "images", name.Replace('*', '_') + ".image.json"))
            ? _customRoot : _root;

    internal XModelNativeImport LoadModel(string name)
    {
        if (_models.TryGetValue(name, out XModelNativeImport? cached)) return cached;
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Choose a native XModel.", nameof(name));
        cached = new XModelNativeExchange().Link(AssetRoot("xmodel_native", name), name,
            material => new MaterialAsset { Info = new MaterialInfo { Name = material } },
            preset => new PhysPresetAsset { Name = preset },
            collmap => new PhysCollmapAsset { Name = collmap });
        _models.Add(name, cached);
        return cached;
    }

    internal IReadOnlyList<int> AvailableLods(string name)
    {
        if (_availableLods.TryGetValue(name, out IReadOnlyList<int>? cached)) return cached;
        var model = LoadModel(name).Model;
        int count = model.NumLods == 0 ? model.Lods.Count : model.NumLods;
        cached = Enumerable.Range(0, Math.Min(count, model.Lods.Count))
            .Where(index => model.Lods[index].ModelSurfs is { } surfaces &&
                model.Lods[index].NumSurfs > 0 && model.Lods[index].NumSurfs <= surfaces.Surfaces.Count)
            .ToArray();
        _availableLods.Add(name, cached);
        return cached;
    }

    internal XModelSource LoadSource(string name, int lodIndex = 0)
    {
        if (_sources.TryGetValue((name, lodIndex), out XModelSource? cached)) return cached;
        XModelNativeImport imported = LoadModel(name);
        if (!XModelExportProjector.TryProjectMaterializedLod(imported.Model, lodIndex,
                out XModelExportDocument? document, out IReadOnlyList<string> blockers) || document is null)
            throw new InvalidDataException($"Native XModel '{name}' has no previewable LOD {lodIndex}: {string.Join("; ", blockers.Take(3))}");
        cached = new XModelSource(name, document);
        _sources.Add((name, lodIndex), cached);
        return cached;
    }

    internal (int Width, int Height, byte[] Pixels, MaterialSurfaceState Surface) ResolveTexture(string materialName)
    {
        if (_textures.TryGetValue(materialName, out var cached)) return cached;
        MaterialAsset material = LoadMaterial(materialName);
        MaterialTextureDef[] colorMaps = material.Textures
            .Where(row => row.Semantic == TextureSemantic.ColorMap && row.Image is not null).ToArray();
        if (colorMaps.Length != 1)
            throw new InvalidDataException($"Native material '{materialName}' needs one resolved color-map image; found {colorMaps.Length}.");
        GfxImageAsset image = colorMaps[0].Image ??
            throw new InvalidDataException($"Native material '{materialName}' has an unresolved color-map image.");
        var resolver = new NativeImageParts(image.Name,
            image.Name is { } name ? _imageParts.GetValueOrDefault(name) : null);
        if (!GfxImagePreviewDecoder.TryDecodeBestAvailable(image, resolver,
                out GfxImagePreviewSnapshot? decoded, out string reason) || decoded is null)
            throw new NotSupportedException($"Native image '{image.Name}' cannot be previewed: {reason}");
        string materialPath = Path.Combine(AssetRoot("materials", materialName), "materials", materialName + ".json");
        using var stream = File.OpenRead(materialPath);
        using var json = JsonDocument.Parse(stream);
        MaterialSurfaceState surface = MaterialSurfaceState.Read(json.RootElement);
        cached = (decoded.Width, decoded.Height, decoded.GetRgbaBytesCopy(), surface);
        _textures.Add(materialName, cached);
        return cached;
    }

    private MaterialAsset LoadMaterial(string name)
    {
        if (_materials.TryGetValue(name, out MaterialAsset? cached)) return cached;
        cached = new MaterialExchange().Link(AssetRoot("materials", name), name,
            technique => new MaterialTechniqueSetAsset { Name = technique }, LoadImage);
        _materials.Add(name, cached);
        return cached;
    }

    private GfxImageAsset LoadImage(string name)
    {
        if (_images.TryGetValue(name, out GfxImageAsset? cached)) return cached;
        cached = new ImageExchange().Link(ImageRoot(name), name, out IReadOnlyList<byte[]> parts);
        _images.Add(name, cached);
        _imageParts.Add(name, parts);
        return cached;
    }
}
