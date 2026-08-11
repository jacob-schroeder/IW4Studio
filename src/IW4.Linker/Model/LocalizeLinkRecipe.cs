using IW4.Assets.Assets.Localize;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.Model;

/// <summary>
/// Frozen Localize value/name recipe. Each semantic XString occurrence owns a
/// canonical inline body; equal text is not treated as storage identity.
/// </summary>
internal sealed class LocalizeLinkRecipe : AssetLinkRecipe
{
    private LocalizeLinkRecipe(
        AssetKey key,
        string originalSerializedName,
        LinkStorageSymbol? value,
        LinkAssetFreezeScope freeze)
        : base(
            key,
            originalSerializedName,
            freeze.FreezeProviderName(
                originalSerializedName,
                sizeof(int),
                "Asset.Name"))
    {
        Root = LinkStorageSymbol.SourceBytes(
            XFileBlockType.TEMP,
            new byte[LocalizeAsset.SerializedSize],
            alignment: 4,
            root => value is null
                ? [NameOperation(root, sizeof(int))]
                : [
                    XStringOperation(root, 0, value, "Localize.Value"),
                    NameOperation(root, sizeof(int))
                ]);
    }

    internal override LinkStorageSymbol Root { get; }

    public static AssetLinkRecipe Freeze(
        AssetKey key,
        string originalSerializedName,
        LocalizeAsset definition,
        LinkAssetFreezeScope freeze)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (originalSerializedName.StartsWith(','))
        {
            if (definition.Value is not null)
            {
                throw new InvalidDataException(
                    "A comma-prefixed Localize provider must have a null value.");
            }

            return ExternalAssetLinkRecipe.Create(
                key,
                XAssetType.Localize,
                originalSerializedName,
                freeze);
        }

        return new LocalizeLinkRecipe(
            key,
            originalSerializedName,
            freeze.FreezeOptionalXString(
                definition.Value,
                definition.ValuePointer.Untyped,
                "Localize.Value"),
            freeze);
    }
}
