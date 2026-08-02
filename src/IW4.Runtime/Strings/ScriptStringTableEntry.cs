using IW4.FastFiles.Strings;

namespace IW4.Runtime.Strings;

/// <summary>
/// One canonical string identity in the process-wide script-string table.
/// </summary>
public sealed record ScriptStringTableEntry(
    ScriptStringHandle Handle,
    string Text,
    ScriptStringUser Users);
