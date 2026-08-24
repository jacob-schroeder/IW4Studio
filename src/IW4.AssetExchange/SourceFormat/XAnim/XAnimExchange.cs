using IW4.Assets.Assets.XAnim;

namespace IW4.AssetExchange.SourceFormat.XAnim;

/// <summary>
/// Writes a materialized PS3 IW4 animation to the OpenAssetTools compiled
/// XAnim source layout.
/// </summary>
public sealed class XAnimExchange
{
    public IReadOnlyList<string> Unlink(
        string sourceDirectory,
        XAnimPartsAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        string assetName = SourceOutput.NormalizeOwnedAssetName(
            asset.Name,
            "XAnim");
        XAnimSourceParts parts = ConsoleXAnimReader.Read(asset, assetName);

        return new SourceOutput(sourceDirectory).WriteBinaryBatch([
            (
                $"xanim/{assetName}",
                stream => CompiledXAnimWriter.Write(stream, parts))
        ]);
    }
}
