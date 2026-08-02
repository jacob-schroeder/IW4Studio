using IW4.Assets.Zone;
using IW4.Assets.Assets;
using IW4.FastFiles.Zone;

namespace IW4.Runtime.Assets;

/// <summary>
/// Managed failure isolation for one XZone load. This is not claimed as an
/// original IW4 structure or API.
/// </summary>
public sealed class XAssetPoolTransaction : IDisposable
{
    private XAssetPool? _pool;
    private XAssetPoolState? _state;
    private readonly Dictionary<BaseAsset, XRuntimeAddress?> _originalRuntimeAddresses =
        new(ReferenceEqualityComparer.Instance);

    internal XAssetPoolTransaction(XAssetPool pool, XAssetPoolState state)
    {
        _pool = pool;
        _state = state;
    }

    public void Commit()
    {
        XAssetPool? pool = Interlocked.Exchange(ref _pool, null);
        Interlocked.Exchange(ref _state, null);
        pool?.CommitTransaction(this);
    }

    public void Dispose()
    {
        XAssetPool? pool = Interlocked.Exchange(ref _pool, null);
        XAssetPoolState? state = Interlocked.Exchange(ref _state, null);
        if (pool is not null && state is not null)
            pool.RollbackTransaction(this, state, _originalRuntimeAddresses);
    }

    internal void TrackRuntimeAddress(
        BaseAsset asset,
        XRuntimeAddress? originalAddress)
    {
        _originalRuntimeAddresses.TryAdd(asset, originalAddress);
    }
}
