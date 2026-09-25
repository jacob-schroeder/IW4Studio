using IW4.Game.Assets.XAnim;

namespace IW4.Formats.SourceFormat.XAnim;

/// <summary>
/// Reads compiled XAnim source for editor playback and writes materialized
/// PS3 IW4 animation assets to that source layout.
/// </summary>
public sealed class XAnimExchange
{
    /// <summary>Loads an exported compiled XAnim for editor pose sampling.</summary>
    public XAnimPlaybackClip Read(string sourceDirectory, string assetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        string normalized = SourceOutput.NormalizeOwnedAssetName(assetName, "XAnim");
        string root = Path.GetFullPath(sourceDirectory);
        string path = Path.GetFullPath(Path.Combine(root, "xanim", normalized));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException($"XAnim '{assetName}' resolves outside the source directory.");
        using FileStream stream = File.OpenRead(path);
        return new XAnimPlaybackClip(CompiledXAnimReader.Read(stream, assetName));
    }

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
