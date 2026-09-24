using IW4.Formats.SourceFormat.PhysCollmap;
using IW4.Formats.SourceFormat.PhysPreset;
using IW4.Formats.SourceFormat.XModel;
using IW4.Game.Assets;
using IW4.Game.Assets.Physics;
using IW4.Game.Assets.XModel;
using IW4.Game.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.Linking;

/// <summary>Loads native model geometry and physics from the same library as its materials.</summary>
public sealed class ModelSourceCompiler(string sourceDirectory, MaterialSourceCompiler materials, string? bootstrapDirectory = null)
{
    private readonly string _sourceDirectory = Path.GetFullPath(sourceDirectory);
    private readonly string? _bootstrapDirectory = bootstrapDirectory is null ? null : Path.GetFullPath(bootstrapDirectory);
    private readonly Dictionary<AssetKey, BaseAsset> _assets = [];

    public IReadOnlyCollection<BaseAsset> Assets => _assets.Values;

    public XModelAsset LoadModel(string name) => Load(XAssetType.XModel, name, normalized =>
    {
        XModelNativeImport imported = new XModelNativeExchange().Link(
            SourceRoot($"xmodel_native/{normalized}.json"), normalized, materials.LoadMaterial, LoadPreset, LoadCollmap);
        foreach (XModelSurfsAsset surfaces in imported.ModelSurfs)
            _assets.TryAdd(AssetKey.FromDefinition(surfaces), surfaces);
        return imported.Model;
    });

    private PhysPresetAsset LoadPreset(string name) => Load(XAssetType.PhysPreset, name,
        normalized => new PhysPresetExchange().Link(SourceRoot($"physic/{normalized}.physic.json"), normalized));

    private PhysCollmapAsset LoadCollmap(string name) => Load(XAssetType.PhysCollmap, name,
        normalized => new PhysCollmapExchange().Link(SourceRoot($"phys_collmaps/{normalized}.phys_collmap.json"), normalized));

    private string SourceRoot(string relativePath) =>
        !File.Exists(Path.Combine(_sourceDirectory, relativePath)) && _bootstrapDirectory is not null &&
        File.Exists(Path.Combine(_bootstrapDirectory, relativePath)) ? _bootstrapDirectory : _sourceDirectory;

    private T Load<T>(XAssetType type, string name, Func<string, T> read) where T : BaseAsset
    {
        string normalized = name.StartsWith(',') ? name[1..] : name;
        AssetKey key = AssetKey.FromWireName(CanonicalAssetFamily.FromSerializedType(type), normalized);
        if (_assets.TryGetValue(key, out BaseAsset? existing)) return (T)existing;
        try
        {
            T asset = read(normalized);
            _assets.Add(key, asset);
            return asset;
        }
        catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException or NotSupportedException)
        {
            throw new InvalidDataException(
                $"Cannot compile {type} '{normalized}' from asset library '{_sourceDirectory}': {exception.Message}", exception);
        }
    }
}
