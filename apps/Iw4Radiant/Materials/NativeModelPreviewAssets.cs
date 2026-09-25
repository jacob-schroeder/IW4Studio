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
using IW4.Runtime.Assets.Images;

namespace Iw4Radiant.Materials;

/// <summary>Native bootstrap assets shared by the faction thumbnails and Walk viewmodel.</summary>
internal sealed class NativeModelPreviewAssets
{
    private readonly string _root;
    private readonly Dictionary<string, XModelNativeImport> _models = new(StringComparer.Ordinal);
    private readonly Dictionary<string, XModelSource> _sources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MaterialAsset> _materials = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GfxImageAsset> _images = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<byte[]>> _imageParts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (int Width, int Height, byte[] Pixels, MaterialSurfaceState Surface)> _textures = new(StringComparer.Ordinal);

    internal NativeModelPreviewAssets(string bootstrapRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bootstrapRoot);
        _root = Path.GetFullPath(bootstrapRoot);
        if (!Directory.Exists(Path.Combine(_root, "xmodel_native")))
            throw new DirectoryNotFoundException($"Native XModels were not found under '{_root}'.");
    }

    internal XModelNativeImport LoadModel(string name)
    {
        if (_models.TryGetValue(name, out XModelNativeImport? cached)) return cached;
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Choose a native XModel.", nameof(name));
        cached = new XModelNativeExchange().Link(_root, name,
            material => new MaterialAsset { Info = new MaterialInfo { Name = material } },
            preset => new PhysPresetAsset { Name = preset },
            collmap => new PhysCollmapAsset { Name = collmap });
        _models.Add(name, cached);
        return cached;
    }

    internal XModelSource LoadSource(string name)
    {
        if (_sources.TryGetValue(name, out XModelSource? cached)) return cached;
        XModelNativeImport imported = LoadModel(name);
        if (!XModelExportProjector.TryProjectMaterializedLod(imported.Model, 0,
                out XModelExportDocument? document, out IReadOnlyList<string> blockers) || document is null)
            throw new InvalidDataException($"Native XModel '{name}' has no previewable LOD 0: {string.Join("; ", blockers.Take(3))}");
        cached = new XModelSource(name, document);
        _sources.Add(name, cached);
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
        var resolver = new NativeImageParts(_imageParts);
        if (!GfxImagePreviewDecoder.TryDecodeBestAvailable(image, resolver,
                out GfxImagePreviewSnapshot? decoded, out string reason) || decoded is null)
            throw new NotSupportedException($"Native image '{image.Name}' cannot be previewed: {reason}");
        string materialPath = Path.Combine(_root, "materials", materialName + ".json");
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
        cached = new MaterialExchange().Link(_root, name,
            technique => new MaterialTechniqueSetAsset { Name = technique }, LoadImage);
        _materials.Add(name, cached);
        return cached;
    }

    private GfxImageAsset LoadImage(string name)
    {
        if (_images.TryGetValue(name, out GfxImageAsset? cached)) return cached;
        cached = new ImageExchange().Link(_root, name, out IReadOnlyList<byte[]> parts);
        _images.Add(name, cached);
        _imageParts.Add(name, parts);
        return cached;
    }

    private sealed class NativeImageParts(Dictionary<string, IReadOnlyList<byte[]>> partsByName) : IGfxImagePayloadResolver
    {
        public bool TryResolveBestPayload(GfxImageAsset image, out GfxImagePayload payload, out string reason)
        {
            payload = default;
            if (image.Name is not { } name || !partsByName.TryGetValue(name, out IReadOnlyList<byte[]>? parts))
            {
                reason = $"Image '{image.Name}' has no native stream parts.";
                return false;
            }
            foreach (var item in image.StreamData.Select((data, index) => (data, index))
                         .Where(item => item.data.HasStreamingData)
                         .OrderByDescending(item => (long)item.data.Width * item.data.Height))
            {
                if (item.index >= parts.Count || parts[item.index].Length == 0) continue;
                payload = new GfxImagePayload(item.data.Width, item.data.Height, parts[item.index]);
                reason = string.Empty;
                return true;
            }
            reason = $"Image '{name}' has no populated native stream part.";
            return false;
        }

        public bool TryResolveStreamParts(GfxImageAsset image, out IReadOnlyList<byte[]> parts, out string reason)
        {
            if (image.Name is { } name && partsByName.TryGetValue(name, out IReadOnlyList<byte[]>? found))
            {
                parts = found;
                reason = string.Empty;
                return true;
            }
            parts = [];
            reason = $"Image '{image.Name}' has no native stream parts.";
            return false;
        }

        public bool TryResolveMipPayloads(GfxImageAsset image, out IReadOnlyList<GfxImagePayload> mips, out string reason)
        {
            mips = [];
            reason = "Native model preview requests only the highest available mip.";
            return false;
        }
    }
}
