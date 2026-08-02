using IW4.Assets.Zone;
using IW4.Assets.Assets;
using IW4.FastFiles.Zone;

namespace IW4.Runtime.Assets;

/// <summary>
/// Lightweight mutation journal used by an outer DB runtime batch. Unlike an
/// <see cref="XAssetPoolTransaction"/>, it does not clone the complete pool;
/// it retains only the first runtime address observed for each mutated object.
/// </summary>
internal sealed class XAssetRuntimeAddressJournal : IDisposable
{
    private XAssetPool? _pool;
    private readonly Dictionary<BaseAsset, XRuntimeAddress?> _originalAddresses =
        new(ReferenceEqualityComparer.Instance);

    internal XAssetRuntimeAddressJournal(XAssetPool pool)
    {
        _pool = pool;
    }

    internal void Track(BaseAsset asset, XRuntimeAddress? originalAddress)
    {
        _originalAddresses.TryAdd(asset, originalAddress);
    }

    internal void Commit()
    {
        XAssetPool? pool = Interlocked.Exchange(ref _pool, null);
        pool?.CommitRuntimeAddressJournal(this);
    }

    public void Dispose()
    {
        XAssetPool? pool = Interlocked.Exchange(ref _pool, null);
        if (pool is not null)
            pool.RollbackRuntimeAddressJournal(this, _originalAddresses);
    }
}
