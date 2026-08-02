using IW4.Assets.Zone;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;

namespace IW4.Runtime.Assets;

/// <summary>
/// Immutable identity of one active provider and its canonical projection at
/// an exact XAssetPool revision.
/// </summary>
public sealed record XAssetActiveProviderSnapshot(
    long PoolRevision,
    XAssetPoolAddress SlotAddress,
    XAssetProviderId ProviderId,
    DbZoneHandle Owner,
    long RegistrationSequence,
    XBlockAddress StagingAddress,
    XRuntimeAddress RuntimeAddress,
    bool IsReferencePlaceholder,
    bool IsActiveCanonicalProvider,
    bool CanonicalProjectionMatchesProviderAsset);
