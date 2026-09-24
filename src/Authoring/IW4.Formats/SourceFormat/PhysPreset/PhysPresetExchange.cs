using System.Text.Json;
using IW4.Formats.SourceFormat.InfoString;
using IW4.Formats.SourceFormat.Technique;
using IW4.Game.Assets.Physics;

namespace IW4.Formats.SourceFormat.PhysPreset;

/// <summary>Exchanges exact native preset fields and writes a compatible PHYSIC authoring view.</summary>
public sealed class PhysPresetExchange
{
    private const string NativeFormat = "iw4-ps3-phys-preset";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public IReadOnlyList<string> Unlink(
        string sourceDirectory,
        PhysPresetAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        string assetName = SourceOutput.NormalizeOwnedAssetName(
            asset.Name,
            "PhysPreset");
        if (asset.SndAliasPrefix is null && asset.SndAliasPrefixPointer.Type != IW4.Game.Pointers.PointerType.Null)
            throw new InvalidDataException($"PhysPreset '{assetName}' sound-alias prefix was not materialized.");

        var native = new NativeDocument
        {
            Format = NativeFormat,
            Version = 1,
            Name = assetName,
            Type = asset.Type,
            FloatBits = [
                BitConverter.SingleToUInt32Bits(asset.Mass),
                BitConverter.SingleToUInt32Bits(asset.Bounce),
                BitConverter.SingleToUInt32Bits(asset.Friction),
                BitConverter.SingleToUInt32Bits(asset.BulletForceScale),
                BitConverter.SingleToUInt32Bits(asset.ExplosiveForceScale),
                BitConverter.SingleToUInt32Bits(asset.PiecesSpreadFraction),
                BitConverter.SingleToUInt32Bits(asset.PiecesUpwardVelocity)
            ],
            SndAliasPrefix = asset.SndAliasPrefix,
            TempDefaultToCylinder = asset.TempDefaultToCylinder,
            PerSurfaceSndAlias = asset.PerSurfaceSndAlias,
            Pad2A = asset.Pad2A
        };
        string json = JsonSerializer.Serialize(native, JsonOptions);
        var files = new List<(string RelativePath, Action<TextWriter> Write)>();
        if (CanWriteLegacy(asset))
        {
            InfoStringSourceWriter source = WriteLegacy(asset);
            files.Add(($"physic/{assetName}", source.Write));
        }
        files.Add(($"physic/{assetName}.physic.json", writer => writer.WriteLine(json)));
        return new SourceOutput(sourceDirectory).WriteTextBatch(files);
    }

    public PhysPresetAsset Link(string sourceDirectory, string assetName)
    {
        string name = SourceOutput.NormalizeOwnedAssetName(assetName, "PhysPreset");
        string path = NativeSourcePath.Resolve(sourceDirectory, "physic", name, ".physic.json");
        using FileStream stream = File.OpenRead(path);
        NativeDocument native = JsonSerializer.Deserialize<NativeDocument>(stream, JsonOptions)
            ?? throw new InvalidDataException($"PhysPreset '{name}' has an empty native source document.");
        if (native.Format != NativeFormat || native.Version != 1 || native.Name != name ||
            native.FloatBits is not { Length: 7 })
        {
            throw new InvalidDataException($"PhysPreset '{name}' has invalid native source fields.");
        }

        float[] values = native.FloatBits.Select(BitConverter.UInt32BitsToSingle).ToArray();
        return new PhysPresetAsset
        {
            Name = name,
            Type = native.Type,
            Mass = values[0],
            Bounce = values[1],
            Friction = values[2],
            BulletForceScale = values[3],
            ExplosiveForceScale = values[4],
            SndAliasPrefix = native.SndAliasPrefix,
            PiecesSpreadFraction = values[5],
            PiecesUpwardVelocity = values[6],
            TempDefaultToCylinder = native.TempDefaultToCylinder,
            PerSurfaceSndAlias = native.PerSurfaceSndAlias,
            Pad2A = native.Pad2A
        };
    }

    private static bool CanWriteLegacy(PhysPresetAsset asset) =>
        float.IsFinite(asset.Mass) && float.IsFinite(asset.Bounce) &&
        float.IsFinite(asset.BulletForceScale) && float.IsFinite(asset.ExplosiveForceScale) &&
        float.IsFinite(asset.PiecesSpreadFraction) && float.IsFinite(asset.PiecesUpwardVelocity) &&
        !float.IsNaN(asset.Friction) &&
        (asset.Friction >= float.MaxValue || float.IsFinite(asset.Friction)) &&
        asset.SndAliasPrefix?.Contains('\\') != true;

    private static InfoStringSourceWriter WriteLegacy(PhysPresetAsset asset)
    {
        var source = new InfoStringSourceWriter("PHYSIC");
        bool frictionIsInfinite = asset.Friction >= float.MaxValue;
        source.AddFloat("mass", asset.Mass);
        source.AddFloat("bounce", asset.Bounce);
        source.AddFloat("friction", frictionIsInfinite ? 0.0f : asset.Friction);
        source.AddBoolean("isFrictionInfinity", frictionIsInfinite);
        source.AddFloat("bulletForceScale", asset.BulletForceScale);
        source.AddFloat("explosiveForceScale", asset.ExplosiveForceScale);
        source.AddString("sndAliasPrefix", asset.SndAliasPrefix);
        source.AddFloat("piecesSpreadFraction", asset.PiecesSpreadFraction);
        source.AddFloat("piecesUpwardVelocity", asset.PiecesUpwardVelocity);
        source.AddBoolean("tempDefaultToCylinder", asset.TempDefaultToCylinder);
        source.AddBoolean("perSurfaceSndAlias", asset.PerSurfaceSndAlias);
        return source;
    }

    private sealed class NativeDocument
    {
        public NativeDocument() { }

        public string? Format { get; init; }
        public int Version { get; init; }
        public string? Name { get; init; }
        public int Type { get; init; }
        public uint[]? FloatBits { get; init; }
        public string? SndAliasPrefix { get; init; }
        public byte TempDefaultToCylinder { get; init; }
        public byte PerSurfaceSndAlias { get; init; }
        public ushort Pad2A { get; init; }
    }
}
