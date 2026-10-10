using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IW4.Formats.SourceFormat.Image;
using IW4.Formats.SourceFormat.Material;
using IW4.Game.Assets.Image;
using IW4.Render.Geometry;
using IW4.Render.Metal;
using IW4.Render.WebGpu;
using Iw4Radiant.MapSource;
using Iw4Radiant.Materials;

namespace Iw4Radiant.Compilation;

internal static class MapBspBuildCache
{
    private const long MaximumEntryBytes = 512L * 1024 * 1024;
    private const long MaximumCacheBytes = 2L * 1024 * 1024 * 1024;
    private static readonly JsonSerializerOptions KeyJsonOptions = new() { IncludeFields = true };
    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IW4Studio", "map-bsp-cache", "v1");

    internal static string? CreateKey(MapDocument expanded, string assetName,
        IReadOnlyDictionary<string, MaterialSource> materials,
        IReadOnlyDictionary<string, XModelSource> models, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AddText(hash, typeof(MapCompiler).Assembly.ManifestModule.ModuleVersionId.ToString("N"));
        AddText(hash, typeof(GpuRayTraversal).Assembly.ManifestModule.ModuleVersionId.ToString("N"));
        AddText(hash, typeof(MetalRayTraversal).Assembly.ManifestModule.ModuleVersionId.ToString("N"));
        AddText(hash, typeof(WebGpuRayTraversal).Assembly.ManifestModule.ModuleVersionId.ToString("N"));
        AddText(hash, typeof(ImageExchange).Assembly.ManifestModule.ModuleVersionId.ToString("N"));
        AddText(hash, typeof(GfxImageAsset).Assembly.ManifestModule.ModuleVersionId.ToString("N"));
        AddText(hash, assetName);
        AddText(hash, MapWriter.Serialize(expanded));
        var materialNames = expanded.Brushes.SelectMany(brush => brush.Faces.Select(face => face.Material))
            .Concat(expanded.Terrains.Select(terrain => terrain.Material)).ToHashSet(StringComparer.Ordinal);
        IReadOnlyDictionary<string, WaterMaterialDefinition> authoredWater =
            WaterMaterialAuthoring.ReadDefinitions(expanded.World.Properties);
        foreach (string name in expanded.Entities.Where(entity => entity.ClassName == "misc_model")
                     .Select(entity => entity.Properties.GetValueOrDefault("model"))
                     .OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!models.TryGetValue(name, out XModelSource? model)) return null;
            AddText(hash, name);
            var source = model.Document;
            AddText(hash, JsonSerializer.Serialize(source, KeyJsonOptions));
            foreach (var material in source.Materials) materialNames.Add(material.Name);
            if (model.SourcePath.Length != 0 && !AddFile(hash, model.SourcePath, cancellationToken)) return null;
            if (model.SourcePath.Length != 0)
            {
                string? root = Directory.GetParent(Path.GetDirectoryName(model.SourcePath)!)?.FullName;
                if (root is null) return null;
                string native = Path.Combine(root, "xmodel_native", name + ".json");
                if (File.Exists(native))
                {
                    if (!AddFile(hash, native, cancellationToken)) return null;
                    using JsonDocument modelMetadata = JsonDocument.Parse(File.ReadAllBytes(native));
                    if (!modelMetadata.RootElement.TryGetProperty("lods", out JsonElement lods) ||
                        lods.ValueKind != JsonValueKind.Array) return null;
                    foreach (JsonElement lod in lods.EnumerateArray())
                    {
                        if (!lod.TryGetProperty("modelSurfs", out JsonElement reference) ||
                            reference.ValueKind == JsonValueKind.Null) continue;
                        if (reference.ValueKind != JsonValueKind.String || reference.GetString() is not { } surfs)
                            return null;
                        if (!AddFile(hash, Path.Combine(root, "xmodelsurfs_native", surfs + ".json"),
                                cancellationToken)) return null;
                    }
                }
            }
        }
        foreach (string name in materialNames.Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!materials.TryGetValue(name, out MaterialSource? material))
            {
                if (authoredWater.TryGetValue(name, out WaterMaterialDefinition? definition) &&
                    materials.TryGetValue(definition.SourceMaterial, out material))
                {
                    AddText(hash, name);
                    AddText(hash, JsonSerializer.Serialize(definition, KeyJsonOptions));
                    if (!AddMaterial(hash, material, cancellationToken)) return null;
                    continue;
                }
                // Caulk and clip materials are intrinsic and have no source asset.
                if (CaulkMaterial.IsCaulk(name) || ClipBrushMaterial.IsCollisionOnly(name)) continue;
                return null;
            }
            if (!AddMaterial(hash, material, cancellationToken)) return null;
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static bool AddMaterial(IncrementalHash hash, MaterialSource material, CancellationToken token)
    {
        AddText(hash, JsonSerializer.Serialize(new
        {
            material.Name, material.IsSky, material.SamplerState, material.TechniqueSet,
            material.ColorTint, material.DetailScale, material.Surface,
            material.GameFlags, material.SurfaceTypeBits, material.WaterColor,
            material.EnvMapParms, material.OceanFoamImagePath, material.Ocean,
            HasWater = material.IsWater
        }, KeyJsonOptions));
        if (!material.IsSky && !material.IsWater && material.UsesVertexColor &&
            material.ColorTint != Vector3.One && !AddTintTechniques(hash, material, token)) return false;
        if (material.ImagePath.Length != 0 && !AddFile(hash, material.ImagePath, token)) return false;
        if (material.OceanFoamImagePath.Length != 0 &&
            !AddFile(hash, material.OceanFoamImagePath, token)) return false;
        foreach (MaterialSource? layer in new[] { material.NormalMap, material.SpecularMap, material.DetailMap })
        {
            if (layer is null) { AddText(hash, "no-layer"); continue; }
            if (!AddMaterial(hash, layer, token)) return false;
        }
        return true;
    }

    private static bool AddTintTechniques(IncrementalHash hash, MaterialSource material, CancellationToken token)
    {
        if (material.SourceDirectory is not { } root) return false;
        string path = Path.Combine(root, "techsets", material.TechniqueSet + ".techset.json");
        if (!AddFile(hash, path, token)) return false;
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
            if (!document.RootElement.TryGetProperty("techniques", out JsonElement techniques) ||
                techniques.ValueKind != JsonValueKind.Array) return false;
            var names = new SortedSet<string>(StringComparer.Ordinal);
            foreach (JsonElement technique in techniques.EnumerateArray())
            {
                if (technique.ValueKind == JsonValueKind.Null) continue;
                if (technique.ValueKind != JsonValueKind.String || technique.GetString() is not { } name)
                    return false;
                names.Add(name);
            }
            foreach (string name in names)
                if (!AddFile(hash, Path.Combine(root, "techniques", name + ".tech.json"), token)) return false;
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static bool AddFile(IncrementalHash hash, string path, CancellationToken token)
    {
        if (!File.Exists(path)) return false;
        AddText(hash, Path.GetFullPath(path));
        using FileStream stream = File.OpenRead(path);
        byte[] buffer = new byte[128 * 1024];
        int length;
        while ((length = stream.Read(buffer)) != 0)
        {
            token.ThrowIfCancellationRequested();
            hash.AppendData(buffer.AsSpan(0, length));
        }
        return true;
    }

    private static void AddText(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        hash.AppendData(BitConverter.GetBytes(bytes.Length));
        hash.AppendData(bytes);
    }

    internal static bool TryRestore(string key, string destination, CancellationToken token)
    {
        string entry = Path.Combine(Root, key + ".d3dbsp");
        string digest = Path.Combine(Root, key + ".sha256");
        try
        {
            if (!File.Exists(entry) || !File.Exists(digest)) return false;
            string expected = File.ReadAllText(digest);
            using FileStream cached = File.OpenRead(entry);
            if (expected.Length != 64 || !string.Equals(expected,
                    Convert.ToHexString(SHA256.HashData(cached)), StringComparison.Ordinal))
                return false;
            token.ThrowIfCancellationRequested();
            File.Copy(entry, destination, overwrite: true);
            token.ThrowIfCancellationRequested();
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    internal static void Store(string key, string source, CancellationToken token)
    {
        try
        {
            long length = new FileInfo(source).Length;
            if (length == 0 || length > MaximumEntryBytes) return;
            Directory.CreateDirectory(Root);
            string entry = Path.Combine(Root, key + ".d3dbsp");
            string digest = Path.Combine(Root, key + ".sha256");
            string temporary = Path.Combine(Root, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.Copy(source, temporary);
                token.ThrowIfCancellationRequested();
                string checksum;
                using (FileStream written = File.OpenRead(temporary))
                    checksum = Convert.ToHexString(SHA256.HashData(written));
                File.Move(temporary, entry, overwrite: true);
                File.WriteAllText(temporary, checksum);
                File.Move(temporary, digest, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            long size = 0;
            foreach (FileInfo file in new DirectoryInfo(Root).GetFiles("*.d3dbsp")
                         .OrderByDescending(file => file.LastWriteTimeUtc))
            {
                size += file.Length;
                if (size <= MaximumCacheBytes) continue;
                file.Delete();
                string oldDigest = Path.ChangeExtension(file.FullName, ".sha256");
                if (File.Exists(oldDigest)) File.Delete(oldDigest);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
