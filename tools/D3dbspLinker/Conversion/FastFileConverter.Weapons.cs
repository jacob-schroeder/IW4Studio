using System.Text.Json;
using IW4.Formats.SourceFormat.Fx;
using IW4.Formats.SourceFormat.Material;
using IW4.Formats.SourceFormat.Tracer;
using IW4.Formats.SourceFormat.Weapon;
using IW4.Game.Assets;
using IW4.Game.Assets.RawFile;
using IW4.Game.Assets.Tracer;
using IW4.Game.Assets.Weapon;
using IW4.Game.Zone;
using IW4.Linker.Contracts;

namespace D3dbspLinker.Conversion;

internal static partial class FastFileConverter
{
    private static WeaponAsset[] LoadDiskTurretWeapons(
        IReadOnlyList<string> names, string library, string bootstrap,
        MaterialSourceCompiler materials, out BaseAsset[] linkedDependencies,
        out string[] effects, out string[] sounds)
    {
        var references = new Dictionary<AssetKey, BaseAsset>();
        var weapons = new List<WeaponAsset>();
        foreach (string name in names)
        {
            try
            {
                weapons.Add(new WeaponNativeExchange().Link(
                    SourceRoot($"weapon_native/{name}.json"), name, Resolve));
            }
            catch (Exception exception) when (exception is IOException or JsonException or NotSupportedException)
            {
                throw new InvalidDataException(
                    $"Cannot compile turret Weapon '{name}' from disk: {exception.Message}", exception);
            }
        }
        var dependencies = weapons.SelectMany(WeaponNativeExchange.EnumerateDependencies).ToArray();
        var pendingRawFiles = new Queue<string>(dependencies.Where(value => value.Type == XAssetType.RawFile)
            .Select(value => value.Name));
        var includedRawFiles = new HashSet<string>(StringComparer.Ordinal);
        while (pendingRawFiles.TryDequeue(out string? name))
        {
            if (!includedRawFiles.Add(name)) continue;
            var rawFile = (RawFileAsset)Resolve(XAssetType.RawFile, name);
            byte[] content = rawFile.Buffer ?? throw new InvalidDataException($"Turret RawFile '{name}' has no contents.");
            foreach (string graph in WeaponNativeExchange.EnumerateRumbleGraphFiles(name, content.AsSpan(0, rawFile.Len)))
                pendingRawFiles.Enqueue(graph);
        }
        linkedDependencies = references.Values.Where(asset => asset is TracerDefAsset or RawFileAsset).ToArray();
        effects = dependencies.Where(value => value.Type == XAssetType.Fx)
            .Select(value => value.Name).Distinct(StringComparer.Ordinal).ToArray();
        sounds = dependencies.Where(value => value.Type == XAssetType.Sound)
            .Select(value => value.Name).Distinct(StringComparer.Ordinal).ToArray();
        return weapons.ToArray();

        string SourceRoot(string relativePath) =>
            !File.Exists(Path.Combine(library, relativePath)) && File.Exists(Path.Combine(bootstrap, relativePath))
                ? bootstrap : library;

        BaseAsset Resolve(XAssetType type, string name)
        {
            AssetKey key = AssetKey.FromWireName(CanonicalAssetFamily.FromSerializedType(type), name);
            if (references.TryGetValue(key, out BaseAsset? existing)) return existing;
            BaseAsset asset = type switch
            {
                XAssetType.Material => materials.LoadMaterial(name),
                XAssetType.Fx => new FxExchange().Link(SourceRoot($"fx/{name}.json"), name),
                XAssetType.Tracer => new TracerNativeExchange().Link(
                    SourceRoot($"tracer_native/{name}.json"), name, materials.LoadMaterial),
                XAssetType.RawFile => CreateRawFile(name, RawFilePath(name)),
                _ => throw new NotSupportedException($"Turret Weapon dependency {type} '{name}' is unsupported.")
            };
            references.Add(key, asset);
            return asset;
        }

        string RawFilePath(string name)
        {
            string root = Path.GetFullPath(SourceRoot(name));
            string path = Path.GetFullPath(Path.Combine(root, name));
            string relative = Path.GetRelativePath(root, path);
            if (Path.IsPathRooted(relative) || relative == ".." ||
                relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                throw new InvalidDataException($"Turret RawFile '{name}' escapes the asset library.");
            return path;
        }
    }
}
