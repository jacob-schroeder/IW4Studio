using IW4.Runtime.Database;

namespace IW4.Runtime.Strings;

internal sealed record ScriptStringTableState(
    Dictionary<string, ScriptStringTableEntry> EntriesByText,
    Dictionary<ushort, ScriptStringTableEntry> EntriesByHandle,
    Dictionary<DbZoneHandle, HashSet<ushort>> HandlesByZone,
    Dictionary<ushort, HashSet<DbZoneHandle>> ZonesByHandle,
    HashSet<ushort> PersistentHandles,
    int NextHandle);
