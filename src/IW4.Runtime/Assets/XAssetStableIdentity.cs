using IW4.FastFiles.Zone;

namespace IW4.Runtime.Assets;

/// <summary>
/// Stable lookup identity for one serialized XAsset definition. Original
/// serialized spelling remains outside this value; the identity exists solely
/// for family-aware canonical lookup.
/// </summary>
public readonly record struct XAssetStableIdentity
{
    public XAssetStableIdentity(
        XAssetType serializedType,
        XAssetType canonicalFamily,
        string originalName)
    {
        ArgumentNullException.ThrowIfNull(originalName);

        SerializedType = serializedType;
        CanonicalFamily = canonicalFamily;
        NormalizedName = NormalizeLookupName(originalName);
    }

    public XAssetType SerializedType { get; }

    public XAssetType CanonicalFamily { get; }

    public string NormalizedName { get; }

    public static bool IsReferenceName(string name) =>
        !string.IsNullOrEmpty(name) && name[0] == ',';

    /// <summary>
    /// Removes only the native reference marker. It deliberately preserves
    /// case and slash spelling for callers retaining the serialized name.
    /// </summary>
    public static string GetLookupSpelling(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return IsReferenceName(name) ? name[1..] : name;
    }

    /// <summary>
    /// Applies the DB lookup normalization without discarding a caller's
    /// separately retained serialized spelling.
    /// </summary>
    public static string NormalizeLookupName(string name) =>
        GetLookupSpelling(name).Replace('\\', '/').ToLowerInvariant();
}
