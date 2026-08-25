using IW4.Assets.Assets.TechniqueSet;

namespace IW4.AssetExchange.SourceFormat.Shader;

/// <summary>
/// Writes the materialized PS3 shader program bytes without converting them
/// to a PC shader representation.
/// </summary>
public sealed class ShaderExchange
{
    public IReadOnlyList<string> Unlink(
        string sourceDirectory,
        MaterialShaderAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);

        (string stage, string assetType) = asset.Kind switch
        {
            MaterialShaderKind.Vertex => ("vertex", "VertexShader"),
            MaterialShaderKind.Pixel => ("pixel", "PixelShader"),
            _ => throw new InvalidDataException(
                $"Shader has unsupported material shader kind {asset.Kind}.")
        };
        string assetName = SourceOutput.NormalizeOwnedAssetName(
            asset.Name,
            assetType);
        byte[]? data = asset.Data?.ToArray();
        if (data is null || data.Length == 0)
        {
            throw new InvalidDataException(
                $"{assetType} '{assetName}' has no materialized PS3 shader program bytes.");
        }
        if (asset.DataSize != (uint)data.Length)
        {
            throw new InvalidDataException(
                $"{assetType} '{assetName}' declares {asset.DataSize} PS3 shader program bytes but retains {data.Length}.");
        }

        return new SourceOutput(sourceDirectory).WriteBinaryBatch([
            (
                $"shader_bin_ps3/{stage}/{assetName}",
                stream => stream.Write(data))
        ]);
    }
}
