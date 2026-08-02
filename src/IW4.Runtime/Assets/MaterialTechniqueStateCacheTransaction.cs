namespace IW4.Runtime.Assets;

/// <summary>
/// Managed failure isolation for the process-global material technique-state
/// probe table. A failed zone load must not consume IDs that affect later zones.
/// </summary>
public sealed class MaterialTechniqueStateCacheTransaction : IDisposable
{
    private MaterialTechniqueStateCache? _cache;
    private MaterialTechniqueStateCacheState? _state;

    internal MaterialTechniqueStateCacheTransaction(
        MaterialTechniqueStateCache cache,
        MaterialTechniqueStateCacheState state)
    {
        _cache = cache;
        _state = state;
    }

    public void Commit()
    {
        MaterialTechniqueStateCache? cache = Interlocked.Exchange(ref _cache, null);
        Interlocked.Exchange(ref _state, null);
        cache?.CommitTransaction(this);
    }

    public void Dispose()
    {
        MaterialTechniqueStateCache? cache = Interlocked.Exchange(ref _cache, null);
        MaterialTechniqueStateCacheState? state = Interlocked.Exchange(ref _state, null);
        if (cache is not null && state is not null)
            cache.RollbackTransaction(this, state);
    }
}
