using System.Text.Json;
using System.Collections.Concurrent;
using IW4.Assets.Assets.Material;

namespace Iw4Radiant.Materials;

internal sealed class XModelCatalog
{
    private readonly Dictionary<string, XModelSource> _models;
    private readonly ConcurrentDictionary<string, MaterialSource> _materials;

    private XModelCatalog(Dictionary<string, XModelSource> models, ConcurrentDictionary<string, MaterialSource> materials)
    {
        _models = models;
        _materials = materials;
    }

    internal IReadOnlyCollection<XModelSource> Models => _models.Values;
    internal XModelSource? Resolve(string name) => _models.GetValueOrDefault(name);
    internal MaterialSource? ResolveMaterial(string name) => _materials.GetValueOrDefault(name);

    internal static XModelCatalog Read(string root)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Asset folder '{root}' does not exist.");
        if (Path.GetFileName(root) is "xmodel" or "model_export" or "materials" or "images")
            root = Path.GetDirectoryName(root) ?? root;
        string modelRoot = Path.Combine(root, "xmodel");
        if (!Directory.Exists(modelRoot))
            throw new DirectoryNotFoundException("Choose an extracted raw asset folder containing xmodel metadata and model_export geometry.");
        var models = new Dictionary<string, XModelSource>(StringComparer.Ordinal);
        var (catalogMaterials, _) = MaterialCatalog.Read(root);
        var materials = new ConcurrentDictionary<string, MaterialSource>(catalogMaterials, StringComparer.Ordinal);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true, IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System
        };
        foreach (string path in Directory.EnumerateFiles(modelRoot, "*.json", options).Order(StringComparer.Ordinal))
        {
            string name = Path.ChangeExtension(Path.GetRelativePath(modelRoot, path), null).Replace('\\', '/');
            try
            {
                using var stream = File.OpenRead(path);
                using var json = JsonDocument.Parse(stream);
                var metadata = json.RootElement;
                if (metadata.ValueKind != JsonValueKind.Object ||
                    !metadata.TryGetProperty("_type", out var type) || type.GetString() != "xmodel" ||
                    !metadata.TryGetProperty("_game", out var game) || game.GetString() != "iw4" ||
                    !metadata.TryGetProperty("_version", out var version) || !version.TryGetInt32(out int number) || number != 2 ||
                    !metadata.TryGetProperty("lods", out var lods) || lods.ValueKind != JsonValueKind.Array || lods.GetArrayLength() == 0 ||
                    !lods[0].TryGetProperty("file", out var file) || file.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(file.GetString()))
                    throw new InvalidDataException("Expected IW4 XModel version-2 metadata with a first LOD source file.");
                string relative = (file.GetString() ?? "").Replace('\\', '/');
                string source = Path.GetFullPath(Path.Combine(root, relative));
                var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (Path.IsPathRooted(relative) || !source.StartsWith(root + Path.DirectorySeparatorChar, comparison))
                    throw new InvalidDataException("The LOD source must be inside the selected raw asset folder.");
                if (!Path.GetExtension(source).Equals(".xmodel_export", StringComparison.OrdinalIgnoreCase))
                    throw new NotSupportedException("The model LOD must use XMODEL_EXPORT geometry.");
                models.Add(name, new XModelSource(name, source, document =>
                {
                    foreach (var material in document.Materials)
                    {
                        if (string.IsNullOrWhiteSpace(material.ColorMapPath) || materials.ContainsKey(material.Name)) continue;
                        // The owning IW4 exporter writes image references relative to model_export.
                        string image = Path.GetFullPath(Path.Combine(root, "model_export", material.ColorMapPath.Replace('\\', '/')));
                        if (!image.StartsWith(root + Path.DirectorySeparatorChar, comparison))
                            throw new InvalidDataException($"Model material '{material.Name}' image must remain inside the raw asset folder.");
                        if (File.Exists(image))
                            materials.TryAdd(material.Name, new MaterialSource(material.Name, image, false,
                                MaterialSamplerState.FilterLinear | MaterialSamplerState.MipMapLinear));
                    }
                }));
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or InvalidDataException)
            {
                throw new InvalidDataException($"XModel metadata '{path}': {exception.Message}", exception);
            }
        }
        return new XModelCatalog(models, materials);
    }
}
