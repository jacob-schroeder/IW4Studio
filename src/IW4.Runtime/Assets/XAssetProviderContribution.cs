using IW4.Assets.Assets;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;

namespace IW4.Runtime.Assets;

/// <summary>
/// One serialized asset definition owned by a zone and linked behind a stable
/// canonical slot. Provider order is load order; the first full definition is
/// active, matching the native head-node/provider-link behavior.
/// </summary>
public sealed record XAssetProviderContribution(
    XAssetProviderId Id,
    DbZoneHandle Owner,
    long RegistrationSequence,
    XAssetType AssetType,
    string Name,
    BaseAsset Asset,
    XBlockAddress StagingAddress,
    byte[] HeaderBytes,
    byte[] NativePoolCopyBytes,
    int NativePoolCopyCapturedLength,
    bool IsReferencePlaceholder,
    IXAssetSourceMemory? SourceBlocks);
