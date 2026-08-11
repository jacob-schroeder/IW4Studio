namespace IW4.FastFiles.Zone;

/// <summary>
/// Defines the canonical logical family for every serialized XAsset type.
/// </summary>
public static class XAssetTypeFamilyCatalog
{
    public static XAssetType GetCanonicalFamily(XAssetType serializedType)
    {
        ValidateDefined(serializedType, nameof(serializedType));
        return GetCanonicalFamilyCore(serializedType);
    }

    public static bool IsCanonicalFamily(XAssetType candidate)
    {
        ValidateDefined(candidate, nameof(candidate));
        return GetCanonicalFamilyCore(candidate) == candidate;
    }

    private static XAssetType GetCanonicalFamilyCore(XAssetType type) =>
        type == XAssetType.ColMapSp
            ? XAssetType.ColMapMp
            : type;

    private static void ValidateDefined(XAssetType type, string parameterName)
    {
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                type,
                "XAsset type must be defined.");
        }
    }
}
