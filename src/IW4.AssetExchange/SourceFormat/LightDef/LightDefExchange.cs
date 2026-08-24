using System.Text;
using IW4.Assets.Assets.LightDef;

namespace IW4.AssetExchange.SourceFormat.LightDef;

/// <summary>
/// Writes the native IW4 light source tuple: sampler-state byte, attenuation
/// image name, and terminal null.
/// </summary>
public sealed class LightDefExchange
{
    public IReadOnlyList<string> Unlink(
        string sourceDirectory,
        LightDefAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        string assetName = SourceOutput.NormalizeOwnedAssetName(
            asset.Name,
            "LightDef");
        string imageName = SourceOutput.NormalizeReferencedAssetName(
            asset.Image?.Name,
            $"LightDef '{assetName}' attenuation image");
        byte[] encodedImageName = EncodeLatin1(
            imageName,
            $"LightDef '{assetName}' attenuation image");
        var contents = new byte[checked(encodedImageName.Length + 2)];
        contents[0] = (byte)asset.SamplerState;
        encodedImageName.CopyTo(contents, 1);

        return new SourceOutput(sourceDirectory).WriteBinaryBatch([
            (
                $"lights/{assetName}",
                stream => stream.Write(contents))
        ]);
    }

    private static byte[] EncodeLatin1(
        string value,
        string field)
    {
        if (value.Any(character => character > byte.MaxValue))
        {
            throw new InvalidDataException(
                $"{field} cannot be represented as an IW4 Latin-1 string.");
        }

        return Encoding.Latin1.GetBytes(value);
    }
}
