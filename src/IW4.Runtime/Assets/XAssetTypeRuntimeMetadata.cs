using IW4.FastFiles.Zone;

namespace IW4.Runtime.Assets;

/// <summary>
/// Runtime behavior for one XAssetType registration family.
/// </summary>
public sealed record XAssetTypeRuntimeMetadata(
    XAssetType SerializedType,
    XAssetType CanonicalType,
    XAssetRuntimeDisposition Disposition,
    int SerializedRootSize,
    int NativePoolCopySize,
    bool HasReleaseLifecycle,
    bool AllowsFallbackPromotion)
{
    public bool HasCanonicalRegistration =>
        Disposition is XAssetRuntimeDisposition.Canonical or XAssetRuntimeDisposition.CanonicalAlias;
}
