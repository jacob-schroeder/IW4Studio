using IW4.Assets.Zone;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets.Lifecycle;

namespace IW4.Runtime.Assets;

/// <summary>
/// Ordered semantic operation produced while retiring an XAsset provider.
/// </summary>
public sealed record XAssetRetirementOperation(
    int Sequence,
    XAssetRetirementOperationKind Kind,
    XAssetPoolAddress Address,
    XAssetType AssetType,
    string Name,
    XAssetProviderContribution? OutgoingProvider,
    XAssetProviderContribution? IncomingProvider,
    XAssetProviderContribution? PoolAllocationProvider,
    XAssetRuntimeAllocationKind? AllocationKind);
