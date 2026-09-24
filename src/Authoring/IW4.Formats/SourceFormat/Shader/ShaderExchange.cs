using System.Text;
using System.Text.Json;
using IW4.Formats.SourceFormat.Technique;
using IW4.Game.Assets.TechniqueSet;

namespace IW4.Formats.SourceFormat.Shader;

/// <summary>Exchanges PS3 shader bytecode and native root program bytes.</summary>
public sealed class ShaderExchange
{
    private const string Format = "iw4-ps3-shader";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public IReadOnlyList<string> Unlink(string sourceDirectory, MaterialShaderAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        (string stage, string assetType) = Stage(asset.Kind);
        string name = SourceOutput.NormalizeOwnedAssetName(asset.Name, assetType);
        byte[]? data = asset.Data?.ToArray();
        if (data is not { Length: > 0 } || asset.DataSize != data.Length)
            throw new InvalidDataException($"{assetType} '{name}' has incomplete PS3 shader bytecode.");
        byte[] programBytes = asset.ProgramBytes?.ToArray()
            ?? throw new InvalidDataException($"{assetType} '{name}' has no native program bytes.");
        if (programBytes.Length != MaterialShaderAsset.GetProgramByteCount(asset.Kind))
            throw new InvalidDataException($"{assetType} '{name}' has an invalid native program-byte count.");

        string programName = Path.ChangeExtension(name, ".cg");
        string directory = $"shader_bin_ps3/{stage}";
        var metadata = new Document
        {
            Format = Format,
            Version = 1,
            Name = name,
            Kind = (byte)asset.Kind,
            ProgramBytes = programBytes
        };
        byte[] json = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(metadata, JsonOptions) + "\n");
        return new SourceOutput(sourceDirectory).WriteBinaryBatch([
            ($"{directory}/{programName}", stream => stream.Write(data)),
            ($"{directory}/{programName}.json", stream => stream.Write(json))
        ]);
    }

    public MaterialShaderAsset Link(
        string sourceDirectory, string assetName, MaterialShaderKind kind)
    {
        (string stage, string assetType) = Stage(kind);
        string name = SourceOutput.NormalizeOwnedAssetName(assetName, assetType);
        string programName = Path.ChangeExtension(name, ".cg");
        string directory = $"shader_bin_ps3/{stage}";
        string dataPath = NativeSourcePath.Resolve(
            sourceDirectory, directory, programName, string.Empty);
        string metadataPath = NativeSourcePath.Resolve(
            sourceDirectory, directory, programName, ".json");
        using FileStream stream = File.OpenRead(metadataPath);
        Document metadata = JsonSerializer.Deserialize<Document>(stream, JsonOptions)
            ?? throw new InvalidDataException($"{assetType} '{name}' has empty metadata.");
        if (metadata.Format != Format || metadata.Version != 1 ||
            metadata.Name != name || metadata.Kind != (byte)kind)
            throw new InvalidDataException($"{assetType} '{name}' has an unsupported metadata format, version, name, or kind.");
        if (metadata.ProgramBytes is null ||
            metadata.ProgramBytes.Length != MaterialShaderAsset.GetProgramByteCount(kind))
            throw new InvalidDataException($"{assetType} '{name}' has an invalid native program-byte count.");
        byte[] data = File.ReadAllBytes(dataPath);
        if (data.Length == 0)
            throw new InvalidDataException($"{assetType} '{name}' has no PS3 shader bytecode.");
        return new MaterialShaderAsset
        {
            Name = name,
            Kind = kind,
            Data = data,
            DataSize = checked((uint)data.Length),
            ProgramBytes = metadata.ProgramBytes
        };
    }

    private static (string Stage, string AssetType) Stage(MaterialShaderKind kind) => kind switch
    {
        MaterialShaderKind.Vertex => ("vertex", "VertexShader"),
        MaterialShaderKind.Pixel => ("pixel", "PixelShader"),
        _ => throw new InvalidDataException($"Shader has unsupported material shader kind {kind}.")
    };

    private sealed class Document
    {
        public Document() { }

        public required string Format { get; init; }
        public required int Version { get; init; }
        public required string Name { get; init; }
        public required byte Kind { get; init; }
        public required byte[] ProgramBytes { get; init; }
    }
}
