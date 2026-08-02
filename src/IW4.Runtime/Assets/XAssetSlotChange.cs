using IW4.Assets.Zone;
using IW4.FastFiles.Zone;

namespace IW4.Runtime.Assets;

public sealed record XAssetSlotChange(
    XAssetSlotChangeKind Kind,
    XAssetPoolAddress Address,
    XAssetType AssetType,
    string Name,
    XAssetProviderContribution? PreviousActiveProvider,
    XAssetProviderContribution? ActiveProvider,
    IReadOnlyList<XAssetProviderContribution> RemovedProviders);
