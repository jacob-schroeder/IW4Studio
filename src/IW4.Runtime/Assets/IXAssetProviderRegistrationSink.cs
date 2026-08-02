using IW4.Assets.Zone;
using IW4.FastFiles.Zone;

namespace IW4.Runtime.Assets;

/// <summary>
/// Receives a provider only after the canonical pool and zone contribution
/// ledger contain it. Runtime-side lifecycle services use this seam without
/// making the load session depend on a loader or renderer implementation.
/// </summary>
public interface IXAssetProviderRegistrationSink
{
    void RegisterProvider(
        XAssetPool pool,
        XAssetPoolAddress slotAddress,
        XAssetProviderId providerId);
}
