using System.Globalization;
using IW4.Assets.Assets.Sound;

namespace IW4.AssetExchange.SourceFormat.Sound;

/// <summary>
/// Writes an IW4 sound falloff curve in OpenAssetTools SNDCURVE format.
/// </summary>
public sealed class SndCurveExchange
{
    public IReadOnlyList<string> Unlink(
        string sourceDirectory,
        SndCurve asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        string assetName = SourceOutput.NormalizeOwnedAssetName(
            asset.Filename,
            "SndCurve");
        Validate(asset, assetName);

        return new SourceOutput(sourceDirectory).WriteTextBatch([
            (
                $"soundaliases/{assetName}.vfcurve",
                writer => WriteCurve(writer, asset))
        ]);
    }

    private static void Validate(
        SndCurve asset,
        string assetName)
    {
        if (asset.KnotCount > SndCurve.MaxKnotCount)
        {
            throw new InvalidDataException(
                $"SndCurve '{assetName}' has {asset.KnotCount} active knots; " +
                $"the native maximum is {SndCurve.MaxKnotCount}.");
        }
        if (asset.Knots.Count != SndCurve.MaxKnotCount)
        {
            throw new InvalidDataException(
                $"SndCurve '{assetName}' has {asset.Knots.Count} serialized knot rows; " +
                $"expected {SndCurve.MaxKnotCount}.");
        }

        for (int index = 0; index < asset.KnotCount; index++)
        {
            SndCurveKnot knot = asset.Knots[index];
            if (!float.IsFinite(knot.X) || !float.IsFinite(knot.Y))
            {
                throw new InvalidDataException(
                    $"SndCurve '{assetName}' knot {index} is not finite.");
            }
        }
    }

    private static void WriteCurve(
        TextWriter writer,
        SndCurve asset)
    {
        writer.WriteLine("SNDCURVE");
        writer.WriteLine();
        writer.Write(asset.KnotCount.ToString(CultureInfo.InvariantCulture));
        for (int index = 0; index < asset.KnotCount; index++)
        {
            SndCurveKnot knot = asset.Knots[index];
            writer.WriteLine();
            writer.Write(knot.X.ToString("F4", CultureInfo.InvariantCulture));
            writer.Write(' ');
            writer.Write(knot.Y.ToString("F4", CultureInfo.InvariantCulture));
        }
    }
}
