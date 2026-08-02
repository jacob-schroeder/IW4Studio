using IW4.FastFiles.Zone;
namespace IW4.Runtime.Strings;

/// <summary>
/// Managed failure isolation for script strings interned by one XZone load.
/// This is a managed transaction boundary, not a claim about an IW4 type.
/// </summary>
public sealed class ScriptStringTableTransaction : IDisposable
{
    private ScriptStringTable? _table;
    private ScriptStringTableState? _state;

    internal ScriptStringTableTransaction(
        ScriptStringTable table,
        ScriptStringTableState state)
    {
        _table = table;
        _state = state;
    }

    public void Commit()
    {
        ScriptStringTable? table = Interlocked.Exchange(ref _table, null);
        Interlocked.Exchange(ref _state, null);
        table?.CommitTransaction(this);
    }

    public void Dispose()
    {
        ScriptStringTable? table = Interlocked.Exchange(ref _table, null);
        ScriptStringTableState? state = Interlocked.Exchange(ref _state, null);
        if (table is not null && state is not null)
            table.RollbackTransaction(this, state);
    }
}
