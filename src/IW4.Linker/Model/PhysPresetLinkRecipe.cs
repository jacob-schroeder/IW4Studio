using IW4.Assets.Assets.Physics;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.Model;

/// <summary>
/// Frozen PhysPreset body preserving native scalar bit patterns and XString
/// traversal order.
/// </summary>
internal sealed class PhysPresetLinkRecipe : AssetLinkRecipe
{
    private PhysPresetLinkRecipe(
        AssetKey key,
        string originalSerializedName,
        int type,
        int[] floatWords,
        LinkStorageSymbol? sndAliasPrefix,
        byte tempDefaultToCylinder,
        byte perSurfaceSndAlias,
        ushort pad2A,
        LinkAssetFreezeScope freeze)
        : base(
            key,
            originalSerializedName,
            freeze.FreezeProviderName(originalSerializedName, 0, "Asset.Name"))
    {
        var writer = new LinkTemplateWriter(PhysPresetAsset.SerializedSize);
        writer.Skip(sizeof(int));
        writer.WriteInt32(type);
        for (int index = 0; index < 5; index++)
            writer.WriteInt32(floatWords[index]);
        writer.Skip(sizeof(int));
        writer.WriteInt32(floatWords[5]);
        writer.WriteInt32(floatWords[6]);
        writer.WriteByte(tempDefaultToCylinder);
        writer.WriteByte(perSurfaceSndAlias);
        writer.WriteUInt16(pad2A);
        Root = LinkStorageSymbol.SourceBytes(
            XFileBlockType.TEMP,
            writer.Complete(),
            alignment: 4,
            root => sndAliasPrefix is null
                ? [NameOperation(root, 0)]
                : [
                    NameOperation(root, 0),
                    XStringOperation(root, 0x1c, sndAliasPrefix, "PhysPreset.SndAliasPrefix")
                ]);
    }

    internal override LinkStorageSymbol Root { get; }

    public static AssetLinkRecipe Freeze(
        AssetKey key,
        string originalSerializedName,
        PhysPresetAsset definition,
        LinkAssetFreezeScope freeze)
    {
        ArgumentNullException.ThrowIfNull(definition);
        int[] floatWords = FreezeFloatWords(definition);
        if (originalSerializedName.StartsWith(','))
        {
            if (definition.Type != 0 ||
                floatWords.Any(word => word != 0) ||
                definition.SndAliasPrefix is not null ||
                definition.TempDefaultToCylinder != 0 ||
                definition.PerSurfaceSndAlias != 0 ||
                definition.Pad2A != 0)
            {
                throw new InvalidDataException(
                    "A comma-prefixed PhysPreset provider must have a zeroed reference body.");
            }

            return ExternalAssetLinkRecipe.Create(
                key,
                XAssetType.PhysPreset,
                originalSerializedName,
                freeze);
        }

        return new PhysPresetLinkRecipe(
            key,
            originalSerializedName,
            definition.Type,
            floatWords,
            freeze.FreezeOptionalXString(
                definition.SndAliasPrefix,
                definition.SndAliasPrefixPointer.Untyped,
                "PhysPreset.SndAliasPrefix"),
            definition.TempDefaultToCylinder,
            definition.PerSurfaceSndAlias,
            definition.Pad2A,
            freeze);
    }

    private static int[] FreezeFloatWords(PhysPresetAsset definition) =>
    [
        BitConverter.SingleToInt32Bits(definition.Mass),
        BitConverter.SingleToInt32Bits(definition.Bounce),
        BitConverter.SingleToInt32Bits(definition.Friction),
        BitConverter.SingleToInt32Bits(definition.BulletForceScale),
        BitConverter.SingleToInt32Bits(definition.ExplosiveForceScale),
        BitConverter.SingleToInt32Bits(definition.PiecesSpreadFraction),
        BitConverter.SingleToInt32Bits(definition.PiecesUpwardVelocity)
    ];
}
