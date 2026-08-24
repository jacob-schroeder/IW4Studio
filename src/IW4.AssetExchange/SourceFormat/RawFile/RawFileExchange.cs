using IW4.Assets.Assets.RawFile;

namespace IW4.AssetExchange.SourceFormat.RawFile;

/// <summary>
/// Writes the logical contents of an IW4 RawFile at its source asset path.
/// Compressed fastfile payloads must be decoded by the owning content codec
/// before they cross this source-output boundary.
/// </summary>
public sealed class RawFileExchange
{
    public IReadOnlyList<string> Unlink(
        string sourceDirectory,
        RawFileAsset asset,
        ReadOnlyMemory<byte> logicalContent)
    {
        ArgumentNullException.ThrowIfNull(asset);
        string assetName = SourceOutput.NormalizeOwnedAssetName(
            asset.Name,
            "RawFile");
        if (asset.CompressedLen < 0 || asset.Len < 0)
        {
            throw new InvalidDataException(
                $"RawFile '{assetName}' has negative serialized length metadata.");
        }
        if (logicalContent.Length != asset.Len)
        {
            throw new InvalidDataException(
                $"RawFile '{assetName}' has {logicalContent.Length} logical bytes; " +
                $"expected {asset.Len}.");
        }

        return new SourceOutput(sourceDirectory).WriteBinaryBatch([
            (
                assetName,
                stream => stream.Write(logicalContent.Span))
        ]);
    }
}
