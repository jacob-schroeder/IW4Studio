using IW4.FastFiles.Zone;

namespace IW4.Linker.Contracts;

/// <summary>
/// A logical asset family. Serialized XAsset types remain separate because
/// more than one wire type can belong to the same canonical family.
/// </summary>
public readonly record struct CanonicalAssetFamily
{
    private readonly bool _isConstructed;

    public CanonicalAssetFamily(XAssetType type)
    {
        if (!XAssetTypeFamilyCatalog.IsCanonicalFamily(type))
        {
            throw new ArgumentException(
                $"{type} is a serialized alias, not a canonical asset family.",
                nameof(type));
        }

        Type = type;
        _isConstructed = true;
    }

    public XAssetType Type { get; }

    public static CanonicalAssetFamily FromSerializedType(XAssetType serializedType) =>
        new(XAssetTypeFamilyCatalog.GetCanonicalFamily(serializedType));

    internal bool IsValid =>
        _isConstructed &&
        Enum.IsDefined(Type) &&
        XAssetTypeFamilyCatalog.IsCanonicalFamily(Type);

    public override string ToString() => Type.ToString();
}

public readonly record struct AssetKey
{
    public AssetKey(CanonicalAssetFamily family, string normalizedName)
    {
        if (!family.IsValid)
            throw new ArgumentException("Asset family must be constructed and valid.", nameof(family));
        if (string.IsNullOrWhiteSpace(normalizedName))
            throw new ArgumentException("Asset name cannot be null or whitespace.", nameof(normalizedName));
        if (normalizedName[0] == ',')
        {
            throw new ArgumentException(
                "Asset name cannot include the leading comma used by wire syntax.",
                nameof(normalizedName));
        }
        if (normalizedName.Contains('\0'))
            throw new ArgumentException("Asset name cannot contain NUL.", nameof(normalizedName));
        if (!string.Equals(normalizedName, normalizedName.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Asset name cannot contain leading or trailing whitespace.",
                nameof(normalizedName));
        }

        Family = family;
        NormalizedName = normalizedName.Replace('\\', '/').ToLowerInvariant();
    }

    public CanonicalAssetFamily Family { get; }
    public string NormalizedName { get; }

    public static AssetKey FromWireName(CanonicalAssetFamily family, string wireName)
    {
        ArgumentNullException.ThrowIfNull(wireName);
        string logicalName = wireName.Length > 0 && wireName[0] == ','
            ? wireName[1..]
            : wireName;
        return new AssetKey(family, logicalName);
    }

    internal bool IsValid
    {
        get
        {
            if (!Family.IsValid || NormalizedName is null)
                return false;

            try
            {
                return this == new AssetKey(Family, NormalizedName);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }

    public override string ToString() => $"{Family}:{NormalizedName}";
}
