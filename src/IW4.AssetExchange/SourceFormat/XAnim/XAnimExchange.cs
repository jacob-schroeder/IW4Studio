using IW4.Assets.Assets.XAnim;

namespace IW4.AssetExchange.SourceFormat.XAnim;

/// <summary>
/// Writes a materialized PS3 IW4 animation to the OpenAssetTools compiled
/// XAnim source layout.
/// </summary>
public sealed class XAnimExchange
{
    /// <summary>
    /// Decodes the materialized console streams once for frame-accurate
    /// preview sampling. Root-motion delta tracks remain separate from the
    /// per-bone pose and are intentionally not applied by this clip.
    /// </summary>
    public XAnimPlaybackClip Decode(XAnimPartsAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        string assetName = string.IsNullOrWhiteSpace(asset.Name)
            ? "<unnamed XAnim>"
            : asset.Name;
        return new XAnimPlaybackClip(
            ConsoleXAnimReader.Read(asset, assetName));
    }

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
