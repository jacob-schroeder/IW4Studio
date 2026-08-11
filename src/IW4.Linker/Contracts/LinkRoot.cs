using IW4.FastFiles.Zone;

namespace IW4.Linker.Contracts;

public enum LinkRootIntent
{
    Owned,
    External,
    Null,
    OpaqueNative
}

/// <summary>
/// One ordered root occurrence, preserving its serialized wire identity apart
/// from the canonical asset identity used for provider resolution.
/// </summary>
public sealed record LinkRoot
{
    public LinkRoot(
        string entryId,
        XAssetType serializedType,
        LinkRootIntent intent,
        AssetKey? asset,
        string? originalSerializedName,
        int? opaqueHeader)
    {
        if (string.IsNullOrEmpty(entryId))
            throw new ArgumentException("Link root entry ID cannot be null or empty.", nameof(entryId));
        if (entryId.Contains('\0'))
            throw new ArgumentException("Link root entry ID cannot contain NUL.", nameof(entryId));
        if (!Enum.IsDefined(serializedType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(serializedType),
                serializedType,
                "Link root serialized type must be a defined XAssetType.");
        }
        if (!Enum.IsDefined(intent))
            throw new ArgumentOutOfRangeException(nameof(intent), intent, "Unknown link root intent.");

        switch (intent)
        {
            case LinkRootIntent.Owned:
                ValidateAssetRoot(
                    serializedType,
                    asset,
                    originalSerializedName,
                    opaqueHeader,
                    external: false);
                break;
            case LinkRootIntent.External:
                ValidateAssetRoot(
                    serializedType,
                    asset,
                    originalSerializedName,
                    opaqueHeader,
                    external: true);
                break;
            case LinkRootIntent.Null:
                if (asset is not null || originalSerializedName is not null || opaqueHeader is not null)
                {
                    throw new ArgumentException(
                        "A null root cannot have an asset, serialized name, or opaque header.",
                        nameof(intent));
                }
                break;
            case LinkRootIntent.OpaqueNative:
                if (asset is not null || originalSerializedName is not null || opaqueHeader is null)
                {
                    throw new ArgumentException(
                        "An opaque native root requires only an opaque header.",
                        nameof(intent));
                }
                break;
        }

        EntryId = entryId;
        SerializedType = serializedType;
        Intent = intent;
        Asset = asset;
        OriginalSerializedName = originalSerializedName;
        OpaqueHeader = opaqueHeader;
    }

    public string EntryId { get; }
    public XAssetType SerializedType { get; }
    public LinkRootIntent Intent { get; }
    public AssetKey? Asset { get; }
    public string? OriginalSerializedName { get; }
    public int? OpaqueHeader { get; }

    private static void ValidateAssetRoot(
        XAssetType serializedType,
        AssetKey? asset,
        string? originalSerializedName,
        int? opaqueHeader,
        bool external)
    {
        if (asset is not { } assetKey || !assetKey.IsValid)
            throw new ArgumentException("An asset root requires a constructed, valid asset key.", nameof(asset));
        CanonicalAssetFamily expectedFamily =
            CanonicalAssetFamily.FromSerializedType(serializedType);
        if (assetKey.Family != expectedFamily)
        {
            throw new ArgumentException(
                $"Asset family {assetKey.Family} does not match canonical family " +
                $"{expectedFamily} for serialized type {serializedType}.",
                nameof(asset));
        }
        if (originalSerializedName is null ||
            (originalSerializedName.Length == 0 &&
             !AssetKey.AllowsEmptyWireName(assetKey.Family)))
        {
            throw new ArgumentException(
                "An asset root requires its exact serialized wire name.",
                nameof(originalSerializedName));
        }
        if (originalSerializedName.Contains('\0'))
        {
            throw new ArgumentException(
                "Serialized wire name cannot contain NUL.",
                nameof(originalSerializedName));
        }
        if (opaqueHeader is not null)
            throw new ArgumentException("An asset root cannot have an opaque header.", nameof(opaqueHeader));

        bool commaPrefixed = originalSerializedName.StartsWith(',');
        if (external != commaPrefixed)
        {
            throw new ArgumentException(
                external
                    ? "An external root wire name must begin with one comma."
                    : "An owned root wire name cannot begin with a comma.",
                nameof(originalSerializedName));
        }

        AssetKey normalizedWireName;
        try
        {
            normalizedWireName = AssetKey.FromWireName(assetKey.Family, originalSerializedName);
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException(
                "Serialized wire name is not a valid asset name.",
                nameof(originalSerializedName),
                exception);
        }

        if (normalizedWireName != assetKey)
        {
            throw new ArgumentException(
                "Serialized wire name does not normalize to the root asset key.",
                nameof(originalSerializedName));
        }
    }
}
