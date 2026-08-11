using IW4.Assets.Assets;
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
    public LinkAssetProvider(
        AssetKey key,
        XAssetType serializedType,
        BaseAsset definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!key.IsValid)
            throw new ArgumentException("Provider asset key must be constructed and valid.", nameof(key));
        if (!Enum.IsDefined(serializedType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(serializedType),
                serializedType,
                "Provider serialized type must be a defined XAssetType.");
        }

        CanonicalAssetFamily expectedFamily =
            CanonicalAssetFamily.FromSerializedType(serializedType);
        if (key.Family != expectedFamily)
        {
            throw new ArgumentException(
                $"Provider key family {key.Family} does not match canonical family " +
                $"{expectedFamily} for serialized type {serializedType}.",
                nameof(key));
        }

        Recipe = (serializedType, definition) switch
        {
            (XAssetType.RawFile, RawFileAsset rawFile) =>
                RawFileLinkRecipe.Freeze(key, rawFile),
            (XAssetType.RawFile, _) => throw new ArgumentException(
                "A RawFile provider requires a RawFileAsset definition.",
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

    internal RawFileLinkRecipe Recipe { get; }
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
