using IW4.Game.Zone;

namespace IW4.Runtime.Assets;

public sealed record XAssetSlotChange(
    XAssetSlotChangeKind Kind,
    XAssetPoolAddress Address,
    XAssetType AssetType,
    string Name,
    XAssetProviderContribution? PreviousActiveProvider,
    XAssetProviderContribution? ActiveProvider,
    IReadOnlyList<XAssetProviderContribution> RemovedProviders);
