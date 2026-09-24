using IW4.Formats.SourceFormat.Image;
using IW4.Formats.SourceFormat.Material;
using IW4.Formats.SourceFormat.Shader;
using IW4.Formats.SourceFormat.Techset;
using IW4.Game.Assets;
using IW4.Game.Assets.Image;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.TechniqueSet;
using IW4.Game.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.Linking;

/// <summary>Loads a material's complete source dependency graph without native providers.</summary>
public sealed class MaterialSourceCompiler
{
    private readonly string _sourceDirectory;
    private readonly string? _bootstrapDirectory;
    private readonly Dictionary<AssetKey, BaseAsset> _assets = [];
    private readonly Dictionary<AssetKey, IReadOnlyList<byte[]>> _imageStreamPayloads = [];

    public MaterialSourceCompiler(string sourceDirectory, string? bootstrapDirectory = null)
    {
        _sourceDirectory = Path.GetFullPath(sourceDirectory);
        _bootstrapDirectory = bootstrapDirectory is null ? null : Path.GetFullPath(bootstrapDirectory);
        if (!Directory.Exists(_sourceDirectory))
            throw new DirectoryNotFoundException($"Asset library '{_sourceDirectory}' does not exist.");
    }

    public IReadOnlyCollection<BaseAsset> Assets => _assets.Values;
    public IReadOnlyDictionary<AssetKey, IReadOnlyList<byte[]>> ImageStreamPayloads => _imageStreamPayloads;

    public MaterialAsset LoadMaterial(string name) => Load(XAssetType.Material, name,
        normalized => new MaterialExchange().Link(SourceRoot($"materials/{normalized}.json"), normalized, LoadTechniqueSet, LoadImage));

    public GfxImageAsset LoadImage(string name) => Load(XAssetType.Image, name, normalized =>
    {
        GfxImageAsset image = new ImageExchange().Link(
            SourceRoot($"images/{normalized.Replace('*', '_')}.image.json"),
            normalized,
            out IReadOnlyList<byte[]> streamParts);
        if (streamParts.Any(part => part.Length != 0))
            _imageStreamPayloads.Add(AssetKey.FromDefinition(image), streamParts);
        return image;
    });

    public MaterialTechniqueSetAsset LoadTechniqueSet(string name) => Load(XAssetType.Techset, name, normalized =>
    {
        MaterialTechniqueSetAsset asset = new TechsetExchange().Link(SourceRoot($"techsets/{normalized}.techset.json"), normalized);
        foreach (MaterialPassAsset pass in asset.TechniqueSlots
                     .SelectMany(slot => slot.Technique?.Passes ?? []))
        {
            if (pass.VertexShader is { } vertex)
                LoadShader(vertex.Name ?? throw new InvalidDataException("Vertex shader has no name."), MaterialShaderKind.Vertex);
            if (pass.PixelShader is { } pixel)
                LoadShader(pixel.Name ?? throw new InvalidDataException("Pixel shader has no name."), MaterialShaderKind.Pixel);
        }
        return asset;
    });

    public MaterialShaderAsset LoadShader(string name, MaterialShaderKind kind) =>
        Load(kind == MaterialShaderKind.Vertex ? XAssetType.VertexShader : XAssetType.PixelShader, name,
            normalized => new ShaderExchange().Link(SourceRoot($"shader_bin_ps3/{(kind == MaterialShaderKind.Vertex ? "vertex" : "pixel")}/{Path.ChangeExtension(normalized, ".cg")}.json"), normalized, kind));

    private string SourceRoot(string relativePath) =>
        !File.Exists(Path.Combine(_sourceDirectory, relativePath)) && _bootstrapDirectory is not null &&
        File.Exists(Path.Combine(_bootstrapDirectory, relativePath)) ? _bootstrapDirectory : _sourceDirectory;

    private T Load<T>(XAssetType type, string name, Func<string, T> read) where T : BaseAsset
    {
        string normalized = name.StartsWith(',') ? name[1..] : name;
        AssetKey key = AssetKey.FromWireName(CanonicalAssetFamily.FromSerializedType(type), normalized);
        if (_assets.TryGetValue(key, out BaseAsset? existing))
            return (T)existing;
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
