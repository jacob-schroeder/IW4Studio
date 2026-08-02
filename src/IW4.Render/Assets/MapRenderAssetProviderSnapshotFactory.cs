using IW4.Assets.Assets;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;

namespace IW4.Render.Assets;

internal static class MapRenderAssetProviderSnapshotFactory
{
    internal static bool TryCapture<TAsset>(
        XAssetPool assetPool,
        TAsset seed,
        XAssetType assetType,
        long poolRevision,
        out TAsset? canonical,
        out XAssetActiveProviderSnapshot? identity)
        where TAsset : BaseAsset
    {
        canonical = null;
        identity = null;
        if (seed.RuntimeAddress?.AssetPoolAddress is not { } address ||
            address.AssetType != assetType ||
            !assetPool.TryGetSlot(address, out XAssetSlot? slot) ||
            slot is null ||
            slot.Address != address ||
            slot.AssetType != assetType ||
            slot.CanonicalAsset is not TAsset typed ||
            typed.RuntimeAddress is not { } runtimeAddress)
        {
            return false;
        }

        XAssetProviderContribution provider = slot.ActiveProvider;
        if (provider.IsReferencePlaceholder || provider.AssetType != assetType)
            return false;
        canonical = typed;
        identity = new XAssetActiveProviderSnapshot(
            poolRevision,
            slot.Address,
            provider.Id,
            provider.Owner,
            provider.RegistrationSequence,
            provider.StagingAddress,
            runtimeAddress,
            provider.IsReferencePlaceholder,
            IsActiveCanonicalProvider: true,
            ReferenceEquals(slot.CanonicalAsset, provider.Asset));
        return true;
    }
}
