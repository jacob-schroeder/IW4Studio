using IW4.Formats.SourceFormat.Image;
using IW4.Formats.SourceFormat.Shader;
using IW4.Formats.SourceFormat.Techset;
using IW4.Game.Assets;
using IW4.Game.Assets.Image;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.TechniqueSet;
using IW4.Game.Zone;

namespace IW4.Formats.SourceFormat.Material;

public sealed class MaterialSourceException : IOException
{
    public XAssetType AssetType { get; }
    public string AssetName { get; }
    public string SourcePath { get; }
    public bool IsMissing { get; }

    internal MaterialSourceException(XAssetType type, string name, string path, string library, Exception cause)
        : base($"Cannot compile {type} '{name}' from asset library '{library}': {cause.Message}", cause)
    {
        AssetType = cause is MaterialSourceException nested ? nested.AssetType : type;
        AssetName = cause is MaterialSourceException nestedName ? nestedName.AssetName : name;
        SourcePath = cause switch
        {
            MaterialSourceException nestedPath => nestedPath.SourcePath,
            FileNotFoundException { FileName: { } missingPath } => missingPath,
            _ => path
        };
        IsMissing = cause is MaterialSourceException nestedCause
            ? nestedCause.IsMissing : cause is FileNotFoundException or DirectoryNotFoundException;
    }
}

/// <summary>Loads a material's complete source dependency graph without native providers.</summary>
public sealed class MaterialSourceCompiler
{
    private readonly string _sourceDirectory;
    private readonly string? _bootstrapDirectory;
    private readonly Dictionary<(XAssetType Type, string Name), BaseAsset> _assets = [];
    private readonly Dictionary<GfxImageAsset, IReadOnlyList<byte[]>> _imageStreamPayloads =
        new(ReferenceEqualityComparer.Instance);

    public MaterialSourceCompiler(string sourceDirectory, string? bootstrapDirectory = null)
    {
        _sourceDirectory = Path.GetFullPath(sourceDirectory);
        _bootstrapDirectory = bootstrapDirectory is null ? null : Path.GetFullPath(bootstrapDirectory);
        if (!Directory.Exists(_sourceDirectory))
            throw new DirectoryNotFoundException($"Asset library '{_sourceDirectory}' does not exist.");
    }

    public IReadOnlyCollection<BaseAsset> Assets => _assets.Values;
    public IReadOnlyDictionary<GfxImageAsset, IReadOnlyList<byte[]>> ImageStreamPayloads => _imageStreamPayloads;

    public string ResolveMaterialSourcePath(string name)
    {
        string normalized = SourceOutput.NormalizeOwnedAssetName(name.StartsWith(',') ? name[1..] : name, "Material");
        string relativePath = $"materials/{normalized}.json";
        return Path.Combine(SourceRoot(relativePath), relativePath);
    }

    public MaterialAsset LoadMaterial(string name) => Load(XAssetType.Material, name,
        normalized => $"materials/{normalized}.json",
        (normalized, root) => new MaterialExchange().Link(root, normalized, LoadTechniqueSet, LoadImage));

    public GfxImageAsset LoadImage(string name) => Load(XAssetType.Image, name,
        normalized => $"images/{normalized.Replace('*', '_')}.image.json", (normalized, root) =>
    {
        GfxImageAsset image = new ImageExchange().Link(
            root, normalized, out IReadOnlyList<byte[]> streamParts);
        if (streamParts.Any(part => part.Length != 0))
            _imageStreamPayloads.Add(image, streamParts);
        return image;
    });

    public MaterialTechniqueSetAsset LoadTechniqueSet(string name) => Load(XAssetType.Techset, name,
        normalized => $"techsets/{normalized}.techset.json", (normalized, root) =>
    {
        MaterialTechniqueSetAsset asset = new TechsetExchange().Link(root, normalized);
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
            normalized => $"shader_bin_ps3/{(kind == MaterialShaderKind.Vertex ? "vertex" : "pixel")}/{Path.ChangeExtension(normalized, ".cg")}.json",
            (normalized, root) => new ShaderExchange().Link(root, normalized, kind));

    private string SourceRoot(string relativePath) =>
        !File.Exists(Path.Combine(_sourceDirectory, relativePath)) && _bootstrapDirectory is not null &&
        File.Exists(Path.Combine(_bootstrapDirectory, relativePath)) ? _bootstrapDirectory : _sourceDirectory;

    private T Load<T>(XAssetType type, string name, Func<string, string> relativeSourcePath,
        Func<string, string, T> read) where T : BaseAsset
    {
        string normalized = name.StartsWith(',') ? name[1..] : name;
        (XAssetType Type, string Name) key = (type, normalized.Replace('\\', '/').ToLowerInvariant());
        if (_assets.TryGetValue(key, out BaseAsset? existing))
            return (T)existing;
        string relativePath = relativeSourcePath(normalized);
        string root = SourceRoot(relativePath);
        try
        {
            T asset = read(normalized, root);
            _assets.Add(key, asset);
            return asset;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or System.Text.Json.JsonException or NotSupportedException)
        {
            throw new MaterialSourceException(type, normalized, Path.Combine(root, relativePath),
                _sourceDirectory, exception);
        }
    }
}
