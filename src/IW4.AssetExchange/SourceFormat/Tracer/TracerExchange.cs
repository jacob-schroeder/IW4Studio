using IW4.AssetExchange.SourceFormat.InfoString;
using IW4.Assets.Assets.Tracer;

namespace IW4.AssetExchange.SourceFormat.Tracer;

/// <summary>Writes an IW4 tracer in the native TRACER info-string format.</summary>
public sealed class TracerExchange
{
    public IReadOnlyList<string> Unlink(
        string sourceDirectory,
        TracerDefAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        string assetName = SourceOutput.NormalizeOwnedAssetName(
            asset.Name,
            "Tracer");
        if (asset.Colors.Count != TracerDefAsset.ColorCount)
        {
            throw new InvalidDataException(
                $"Tracer '{assetName}' requires {TracerDefAsset.ColorCount} materialized color rows but has {asset.Colors.Count}.");
        }

        var source = new InfoStringSourceWriter("TRACER");
        source.AddString(
            "material",
            InfoStringSourceWriter.ReferencedAssetName(
                asset.MaterialPointer.Raw,
                asset.Material?.SerializedAssetName,
                $"Tracer '{assetName}' material"));
        source.AddInt(
            "drawInterval",
            asset.DrawInterval,
            $"Tracer '{assetName}' draw interval");
        source.AddFloat("speed", asset.Speed);
        source.AddFloat("beamLength", asset.BeamLength);
        source.AddFloat("beamWidth", asset.BeamWidth);
        source.AddFloat("screwRadius", asset.ScrewRadius);
        source.AddFloat("screwDist", asset.ScrewDistance);
        for (int index = 0; index < TracerDefAsset.ColorCount; index++)
        {
            TracerColor color = asset.Colors[index];
            source.AddFloat($"colorR{index}", color.Red);
            source.AddFloat($"colorG{index}", color.Green);
            source.AddFloat($"colorB{index}", color.Blue);
            source.AddFloat($"colorA{index}", color.Alpha);
        }

        return new SourceOutput(sourceDirectory).WriteTextBatch([
            ($"tracer/{assetName}", source.Write)
        ]);
    }
}
