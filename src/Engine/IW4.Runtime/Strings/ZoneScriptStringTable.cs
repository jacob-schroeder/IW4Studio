using IW4.Game.Zone;
using IW4.Game.ScriptStrings;

namespace IW4.Runtime.Strings;

/// <summary>
/// Per-zone mapping from serialized ushort indices to process-wide script-
/// string handles. This is loader context, not part of XZoneMemory.
/// </summary>
public sealed class ZoneScriptStringTable
{
    private IReadOnlyList<XScriptStringEntry>? _entries;
    private bool[] _referenced = [];

    public bool IsInitialized => _entries is not null;

    public IReadOnlyList<XScriptStringEntry> Entries => _entries ?? [];

    // Table entries without asset references are standalone declarations in the editor.
    public IEnumerable<string?> UnreferencedValues =>
        Entries.Where(entry => !_referenced[entry.Index]).Select(entry => entry.Value);

    public void Initialize(IReadOnlyList<XScriptStringEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (_entries is not null)
            throw new InvalidOperationException("The XZone script-string table is already initialized.");

        XScriptStringEntry[] copied = entries.ToArray();
        for (int index = 0; index < copied.Length; index++)
        {
            if (copied[index].Index != index)
            {
                throw new InvalidDataException(
                    $"XZone script-string entry {index} reports local index {copied[index].Index}.");
            }
        }

        _entries = Array.AsReadOnly(copied);
        _referenced = new bool[copied.Length];
    }

    public ScriptStringReference Resolve(
        ushort rawLocalIndex,
        XBlockAddress destinationCellAddress,
        string memberName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);

        IReadOnlyList<XScriptStringEntry> entries = _entries
            ?? throw new InvalidOperationException(
                $"Cannot resolve {memberName} before the XZone script-string table is initialized.");
        if (rawLocalIndex >= entries.Count)
        {
            throw new InvalidDataException(
                $"{memberName} local script-string index 0x{rawLocalIndex:X4} is outside the XZone table of 0x{entries.Count:X} entries.");
        }

        _referenced[rawLocalIndex] = true;
        return new ScriptStringReference(entries[rawLocalIndex], destinationCellAddress);
    }
}
