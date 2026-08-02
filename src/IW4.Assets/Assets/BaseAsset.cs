using IW4.Assets.Zone;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets;

public abstract class BaseAsset
{
    private XRuntimeAddress? _runtimeAddress;

    public int Offset { get; init; }

    // Destination used by Load_Stream before any DB_AddXAsset canonicalization.
    // TEMP staging addresses are intentionally reusable and are not asset identity.
    public XBlockAddress? StagingAddress { get; private set; }

    // Effective runtime identity. This begins as a block address and becomes an
    // asset-pool address after DB_AddXAsset registration.
    public XRuntimeAddress? RuntimeAddress
    {
        get => _runtimeAddress;
        init
        {
            _runtimeAddress = value;
            if (value?.BlockAddress is { } stagingAddress)
                StagingAddress = stagingAddress;
        }
    }

    public void SetCanonicalRuntimeAddress(XAssetPoolAddress address)
    {
        _runtimeAddress = XRuntimeAddress.FromAssetPool(address);
    }

    // Managed XAssetPool failure isolation restores this exact value. Keeping
    // the setter internal prevents application code from manufacturing runtime
    // identity transitions outside the loader/runtime boundary.
    internal void RestoreRuntimeAddress(XRuntimeAddress? address)
    {
        _runtimeAddress = address;
    }
}
