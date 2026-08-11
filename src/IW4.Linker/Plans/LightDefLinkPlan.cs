using IW4.Assets.Assets.LightDef;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.Plans;

/// <summary>
/// Frozen GfxLightDef schema data. The image reference is reduced to logical
/// identity at the request boundary rather than retaining a mutable asset.
/// </summary>
internal sealed class LightDefLinkPlan : AssetLinkPlan
{
    private LightDefLinkPlan(
        AssetKey key,
        string name,
        AssetDependency? image,
        byte samplerState,
        byte[] padding,
        uint lmapLookupStart,
        LinkAssetFreezeScope freeze)
        : base(
            key,
            name,
            freeze.FreezeProviderName(name, 0, "Asset.Name"))
    {
        var writer = new LinkTemplateWriter(LightDefAsset.SerializedSize);
        writer.Skip(sizeof(int));
        writer.Skip(sizeof(int));
        writer.WriteByte(samplerState);
        writer.WriteBytes(padding);
        writer.WriteUInt32(lmapLookupStart);
        Root = LinkStorageSymbol.SourceBytes(
            XFileBlockType.TEMP,
            writer.Complete(),
            alignment: 4,
            root => image is { } dependency
                ? [NameOperation(root, 0), ProviderOperation(root, sizeof(int), dependency)]
                : [NameOperation(root, 0)]);
    }

    internal override LinkStorageSymbol Root { get; }

    public static AssetLinkPlan Freeze(
        AssetKey key,
        string originalSerializedName,
        LightDefAsset definition,
        LinkAssetFreezeScope freeze)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (originalSerializedName.StartsWith(','))
        {
            if (definition.Image is not null ||
                definition.ImagePointer.Raw != 0 ||
                definition.SamplerState != 0 ||
                definition.Pad09To0B is null ||
                definition.Pad09To0B.Length is not (0 or 3) ||
                definition.Pad09To0B.Any(value => value != 0) ||
                definition.LmapLookupStart != 0)
            {
                throw new InvalidDataException(
                    "A comma-prefixed LightDef provider must have a zeroed reference body.");
            }

            return ExternalAssetLinkPlan.Create(
                key,
                XAssetType.LightDef,
                originalSerializedName,
                freeze);
        }

        byte[] sourcePadding = definition.Pad09To0B ??
            throw new InvalidDataException(
                "LightDef padding at 0x09..0x0B cannot be null.");
        byte[] padding = sourcePadding.Length == 0
            ? new byte[3]
            : sourcePadding.ToArray();
        if (padding.Length != 3)
        {
            throw new InvalidDataException(
                "LightDef padding at 0x09..0x0B must contain exactly three bytes.");
        }

        AssetDependency? image = FreezeProviderDependency(
            definition.ImagePointer.Untyped,
            definition.Image,
            XAssetType.Image,
            "LightDef.Image");

        return new LightDefLinkPlan(
            key,
            originalSerializedName,
            image,
            definition.SamplerState,
            padding,
            definition.LmapLookupStart,
            freeze);
    }

}
