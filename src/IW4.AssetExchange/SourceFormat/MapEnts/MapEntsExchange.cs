using IW4.Assets.Assets.MapEnts;

namespace IW4.AssetExchange.SourceFormat.MapEnts;

/// <summary>
/// Writes MapEnts and AddonMapEnts entity text using their retained serialized
/// bytes, excluding the native terminal null.
/// </summary>
public sealed class MapEntsExchange
{
    public IReadOnlyList<string> Unlink(
        string sourceDirectory,
        MapEntsAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        string assetName = SourceOutput.NormalizeOwnedAssetName(
            asset.Name,
            "MapEnts");
        return Write(
            sourceDirectory,
            $"{assetName}.ents",
            assetName,
            "MapEnts",
            asset.NumEntityChars,
            asset.EntityStringBytes);
    }

    public IReadOnlyList<string> Unlink(
        string sourceDirectory,
        AddonMapEntsAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        string assetName = SourceOutput.NormalizeOwnedAssetName(
            asset.Name,
            "AddonMapEnts");
        return Write(
            sourceDirectory,
            assetName,
            assetName,
            "AddonMapEnts",
            asset.NumEntityChars,
            asset.EntityStringBytes);
    }

    private static IReadOnlyList<string> Write(
        string sourceDirectory,
        string relativePath,
        string assetName,
        string assetType,
        int serializedCharacterCount,
        IReadOnlyList<byte> serializedBytes)
    {
        ArgumentNullException.ThrowIfNull(serializedBytes);
        if (serializedCharacterCount < 0)
        {
            throw new InvalidDataException(
                $"{assetType} '{assetName}' has a negative entity-string count.");
        }
        if (serializedBytes.Count != serializedCharacterCount)
        {
            throw new InvalidDataException(
                $"{assetType} '{assetName}' has {serializedBytes.Count} entity-string " +
                $"bytes; expected {serializedCharacterCount}.");
        }
        if (serializedCharacterCount != 0 && serializedBytes[^1] != 0)
        {
            throw new InvalidDataException(
                $"{assetType} '{assetName}' entity string has no terminal null.");
        }

        byte[] sourceBytes = serializedCharacterCount == 0
            ? []
            : serializedBytes.Take(serializedCharacterCount - 1).ToArray();
        return new SourceOutput(sourceDirectory).WriteBinaryBatch([
            (
                relativePath,
                stream => stream.Write(sourceBytes))
        ]);
    }
}
