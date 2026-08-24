using IW4.AssetExchange.SourceFormat.InfoString;
using IW4.Assets.Assets.Physics;

namespace IW4.AssetExchange.SourceFormat.PhysPreset;

/// <summary>Writes an IW4 physics preset in the native PHYSIC info-string format.</summary>
public sealed class PhysPresetExchange
{
    public IReadOnlyList<string> Unlink(
        string sourceDirectory,
        PhysPresetAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        string assetName = SourceOutput.NormalizeOwnedAssetName(
            asset.Name,
            "PhysPreset");
        var source = new InfoStringSourceWriter("PHYSIC");
        bool frictionIsInfinite = asset.Friction >= float.MaxValue;

        source.AddFloat("mass", asset.Mass);
        source.AddFloat("bounce", asset.Bounce);
        source.AddFloat("friction", frictionIsInfinite ? 0.0f : asset.Friction);
        source.AddBoolean("isFrictionInfinity", frictionIsInfinite);
        source.AddFloat("bulletForceScale", asset.BulletForceScale);
        source.AddFloat("explosiveForceScale", asset.ExplosiveForceScale);
        source.AddString(
            "sndAliasPrefix",
            InfoStringSourceWriter.MaterializedString(
                asset.SndAliasPrefixPointer.Raw,
                asset.SndAliasPrefix,
                $"PhysPreset '{assetName}' sound-alias prefix"));
        source.AddFloat("piecesSpreadFraction", asset.PiecesSpreadFraction);
        source.AddFloat("piecesUpwardVelocity", asset.PiecesUpwardVelocity);
        source.AddBoolean("tempDefaultToCylinder", asset.TempDefaultToCylinder);
        source.AddBoolean("perSurfaceSndAlias", asset.PerSurfaceSndAlias);

        return new SourceOutput(sourceDirectory).WriteTextBatch([
            ($"physic/{assetName}", source.Write)
        ]);
    }
}
