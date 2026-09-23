namespace IW4.Runtime.Database;

/// <summary>
/// Managed failure-isolation boundary for one DB_LoadXAssets batch. Request
/// ordering is preserved by the caller; this transaction prevents editor
/// failures from publishing half a batch.
/// </summary>
public sealed class DbRuntimeBatchTransaction : IDisposable
{
    private DbRuntime? _runtime;
    private DbRuntimeState? _state;

    internal DbRuntimeBatchTransaction(DbRuntime runtime, DbRuntimeState state)
    {
        _runtime = runtime;
        _state = state;
    }

    public void Commit()
    {
        DbRuntime? runtime = _runtime;
        if (runtime is null)
            return;

        // Keep the rollback snapshot owned until the runtime has committed
        // every deferred lifecycle and zone-memory operation successfully.
        runtime.CommitBatch(this);
        Interlocked.Exchange(ref _runtime, null);
        Interlocked.Exchange(ref _state, null);
    }

    public void Dispose()
    {
        DbRuntime? runtime = Interlocked.Exchange(ref _runtime, null);
        DbRuntimeState? state = Interlocked.Exchange(ref _state, null);
        if (runtime is not null && state is not null)
            runtime.RollbackBatch(this, state);
    }
}
