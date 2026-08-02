using IW4.FastFiles.Zone;
using IW4.FastFiles.Strings;
using IW4.Runtime.Database;

namespace IW4.Runtime.Strings;

/// <summary>
/// Process-wide semantic equivalent of the engine script-string interner.
/// Handles are stable for this DbRuntime and shared across every loaded zone.
/// Managed handles are deterministic runtime-local opaque identities.
/// </summary>
public sealed class ScriptStringTable
{
    private readonly Dictionary<string, ScriptStringTableEntry> _entriesByText =
        new(StringComparer.Ordinal);
    private readonly Dictionary<ushort, ScriptStringTableEntry> _entriesByHandle = new();
    private readonly Dictionary<DbZoneHandle, HashSet<ushort>> _handlesByZone = new();
    private readonly Dictionary<ushort, HashSet<DbZoneHandle>> _zonesByHandle = new();
    private readonly HashSet<ushort> _persistentHandles = [];
    private ScriptStringTableTransaction? _activeTransaction;
    private int _nextHandle = 1;

    public IReadOnlyCollection<ScriptStringTableEntry> Entries => _entriesByHandle.Values;

    public ScriptStringTableTransaction BeginTransaction()
    {
        if (_activeTransaction is not null)
            throw new InvalidOperationException("The script-string table already has an active load transaction.");

        var transaction = new ScriptStringTableTransaction(this, CaptureState());
        _activeTransaction = transaction;
        return transaction;
    }

    public ScriptStringTableEntry Intern(
        string text,
        ScriptStringUser user)
    {
        ScriptStringTableEntry entry = InternCore(text, user);
        _persistentHandles.Add(entry.Handle.Value);
        return entry;
    }

    public ScriptStringTableEntry Intern(
        string text,
        ScriptStringUser user,
        DbZoneHandle owner)
    {
        if (owner.IsNone)
            throw new ArgumentOutOfRangeException(nameof(owner));

        ScriptStringTableEntry entry = InternCore(text, user);
        AddZoneClaim(owner, entry.Handle);
        return entry;
    }

    private ScriptStringTableEntry InternCore(
        string text,
        ScriptStringUser user)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (_entriesByText.TryGetValue(text, out ScriptStringTableEntry? existing))
            return MergeUsers(existing, user);

        if (_nextHandle > ushort.MaxValue)
        {
            throw new InvalidOperationException(
                "The managed script-string table exhausted its 16-bit handle space.");
        }

        var handle = new ScriptStringHandle(checked((ushort)_nextHandle++));
        var entry = new ScriptStringTableEntry(handle, text, user);
        _entriesByText.Add(text, entry);
        _entriesByHandle.Add(handle.Value, entry);
        return entry;
    }

    public void ReleaseZoneClaims(DbZoneHandle owner)
    {
        if (owner.IsNone)
            throw new ArgumentOutOfRangeException(nameof(owner));
        if (!_handlesByZone.Remove(owner, out HashSet<ushort>? handles))
            return;

        foreach (ushort handle in handles)
        {
            if (_zonesByHandle.TryGetValue(handle, out HashSet<DbZoneHandle>? owners))
            {
                owners.Remove(owner);
                if (owners.Count > 0)
                    continue;

                _zonesByHandle.Remove(handle);
            }

            if (_persistentHandles.Contains(handle) ||
                !_entriesByHandle.TryGetValue(handle, out ScriptStringTableEntry? entry))
            {
                continue;
            }

            ScriptStringUser remainingUsers = entry.Users & ~ScriptStringUser.XZone;
            if (remainingUsers != ScriptStringUser.None)
            {
                ScriptStringTableEntry retained = entry with { Users = remainingUsers };
                _entriesByHandle[handle] = retained;
                _entriesByText[entry.Text] = retained;
                continue;
            }

            _entriesByHandle.Remove(handle);
            _entriesByText.Remove(entry.Text);
        }
    }

    public IReadOnlyCollection<ScriptStringHandle> GetZoneClaims(DbZoneHandle owner)
    {
        if (owner.IsNone)
            throw new ArgumentOutOfRangeException(nameof(owner));
        if (!_handlesByZone.TryGetValue(owner, out HashSet<ushort>? handles))
            return Array.Empty<ScriptStringHandle>();

        return Array.AsReadOnly(handles.Select(handle => new ScriptStringHandle(handle)).ToArray());
    }

    public bool TryGetHandle(string text, out ScriptStringHandle handle)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (_entriesByText.TryGetValue(text, out ScriptStringTableEntry? entry))
        {
            handle = entry.Handle;
            return true;
        }

        handle = ScriptStringHandle.Null;
        return false;
    }

    public bool TryResolve(ScriptStringHandle handle, out string? text)
    {
        if (handle.IsNull)
        {
            text = null;
            return true;
        }

        if (_entriesByHandle.TryGetValue(handle.Value, out ScriptStringTableEntry? entry))
        {
            text = entry.Text;
            return true;
        }

        text = null;
        return false;
    }

    internal ScriptStringTableState CaptureState()
    {
        return new ScriptStringTableState(
            new Dictionary<string, ScriptStringTableEntry>(_entriesByText, StringComparer.Ordinal),
            new Dictionary<ushort, ScriptStringTableEntry>(_entriesByHandle),
            _handlesByZone.ToDictionary(
                pair => pair.Key,
                pair => new HashSet<ushort>(pair.Value)),
            _zonesByHandle.ToDictionary(
                pair => pair.Key,
                pair => new HashSet<DbZoneHandle>(pair.Value)),
            new HashSet<ushort>(_persistentHandles),
            _nextHandle);
    }

    internal void RestoreState(ScriptStringTableState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        _entriesByText.Clear();
        foreach ((string text, ScriptStringTableEntry entry) in state.EntriesByText)
            _entriesByText.Add(text, entry);

        _entriesByHandle.Clear();
        foreach ((ushort handle, ScriptStringTableEntry entry) in state.EntriesByHandle)
            _entriesByHandle.Add(handle, entry);

        _handlesByZone.Clear();
        foreach ((DbZoneHandle owner, HashSet<ushort> handles) in state.HandlesByZone)
            _handlesByZone.Add(owner, new HashSet<ushort>(handles));

        _zonesByHandle.Clear();
        foreach ((ushort handle, HashSet<DbZoneHandle> owners) in state.ZonesByHandle)
            _zonesByHandle.Add(handle, new HashSet<DbZoneHandle>(owners));

        _persistentHandles.Clear();
        _persistentHandles.UnionWith(state.PersistentHandles);

        _nextHandle = state.NextHandle;
    }

    internal void CommitTransaction(ScriptStringTableTransaction transaction)
    {
        EnsureActiveTransaction(transaction);
        _activeTransaction = null;
    }

    internal void RollbackTransaction(
        ScriptStringTableTransaction transaction,
        ScriptStringTableState state)
    {
        EnsureActiveTransaction(transaction);
        try
        {
            RestoreState(state);
        }
        finally
        {
            _activeTransaction = null;
        }
    }

    private ScriptStringTableEntry MergeUsers(
        ScriptStringTableEntry entry,
        ScriptStringUser user)
    {
        ScriptStringUser combinedUsers = entry.Users | user;
        if (combinedUsers == entry.Users)
            return entry;

        ScriptStringTableEntry updated = entry with { Users = combinedUsers };
        _entriesByText[entry.Text] = updated;
        _entriesByHandle[entry.Handle.Value] = updated;
        return updated;
    }

    private void AddZoneClaim(DbZoneHandle owner, ScriptStringHandle handle)
    {
        if (!_handlesByZone.TryGetValue(owner, out HashSet<ushort>? handles))
        {
            handles = [];
            _handlesByZone.Add(owner, handles);
        }

        if (!handles.Add(handle.Value))
            return;

        if (!_zonesByHandle.TryGetValue(handle.Value, out HashSet<DbZoneHandle>? owners))
        {
            owners = [];
            _zonesByHandle.Add(handle.Value, owners);
        }

        owners.Add(owner);
    }

    private void EnsureActiveTransaction(ScriptStringTableTransaction transaction)
    {
        if (!ReferenceEquals(_activeTransaction, transaction))
            throw new InvalidOperationException("Script-string transaction ownership is inconsistent.");
    }
}
