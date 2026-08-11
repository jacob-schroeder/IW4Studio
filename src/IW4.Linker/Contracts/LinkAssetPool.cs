using IW4.Assets.Assets;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.LightDef;
using IW4.Assets.Assets.RawFile;
using IW4.FastFiles.Zone;
using IW4.Linker.Model;

namespace IW4.Linker.Contracts;

/// <summary>
/// One immutable provider input. Construction freezes the supported semantic
/// asset data and deliberately discards loader and runtime pointer state.
/// </summary>
public sealed class LinkAssetProvider
{
    public LinkAssetProvider(BaseAsset definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        XAssetType serializedType = definition.SerializedAssetType;
        if (!Enum.IsDefined(serializedType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(definition),
                serializedType,
                "Provider serialized type must be a defined XAssetType.");
        }

        AssetKey key = AssetKey.FromDefinition(definition);
        string serializedName = definition.SerializedAssetName ??
            throw new ArgumentException(
                "Provider definition has no serialized name.",
                nameof(definition));

        Recipe = (serializedType, definition) switch
        {
            (XAssetType.RawFile, RawFileAsset rawFile) =>
                RawFileLinkRecipe.Freeze(key, serializedName, rawFile),
            (XAssetType.LightDef, LightDefAsset lightDef) =>
                LightDefLinkRecipe.Freeze(key, serializedName, lightDef),
            (XAssetType.Image, GfxImageAsset image) =>
                GfxImageReferenceLinkRecipe.Freeze(key, serializedName, image),
            (XAssetType.RawFile, _) => throw new ArgumentException(
                "A RawFile provider requires a RawFileAsset definition.",
                nameof(definition)),
            (XAssetType.LightDef, _) => throw new ArgumentException(
                "A LightDef provider requires a LightDefAsset definition.",
                nameof(definition)),
            (XAssetType.Image, _) => throw new ArgumentException(
                "An Image provider requires a GfxImageAsset definition.",
                nameof(definition)),
            _ => throw new NotSupportedException(
                $"Canonical linking does not yet support {serializedType} providers.")
        };

        Key = key;
        SerializedType = serializedType;
        OriginalSerializedName = Recipe.OriginalSerializedName;
        IsReferencePlaceholder = Recipe.IsReferencePlaceholder;
    }

    public AssetKey Key { get; }
    public XAssetType SerializedType { get; }
    public string OriginalSerializedName { get; }
    public bool IsReferencePlaceholder { get; }

    internal AssetLinkRecipe Recipe { get; }
}

/// <summary>
/// Immutable provider occurrences in highest-precedence-first order.
/// Duplicate logical keys remain distinct provider inputs.
/// </summary>
public sealed class LinkAssetPool
{
    private readonly IReadOnlyList<LinkAssetProvider> _providers;

    public LinkAssetPool(IEnumerable<LinkAssetProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        LinkAssetProvider[] copied = providers
            .Select(provider => provider ?? throw new ArgumentException(
                "Link asset providers cannot contain null.",
                nameof(providers)))
            .ToArray();
        _providers = Array.AsReadOnly(copied);
    }

    public IReadOnlyList<LinkAssetProvider> Providers => _providers;
}
