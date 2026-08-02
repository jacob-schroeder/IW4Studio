using IW4.Assets.Zone;
using IW4.FastFiles.Strings;
using IW4.FastFiles.Zone;

namespace IW4.Runtime.Strings;

/// <summary>
/// Per-zone mapping from serialized ushort indices to process-wide script-
/// string handles. This is loader context, not part of XZoneMemory.
/// </summary>
public sealed class ZoneScriptStringTable
{
    private IReadOnlyList<XScriptStringEntry>? _entries;

    public bool IsInitialized => _entries is not null;

    public IReadOnlyList<XScriptStringEntry> Entries => _entries ?? [];

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
    }

    public ScriptStringReference Resolve(
        ushort rawLocalIndex,
        XBlockAddress destinationCellAddress,
        string memberName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);

        if (rawLocalIndex == 0)
        {
            return new ScriptStringReference(
                RawLocalIndex: 0,
                Text: null,
                RuntimeHandle: ScriptStringHandle.Null,
                DestinationCellAddress: destinationCellAddress);
        }

        IReadOnlyList<XScriptStringEntry> entries = _entries
            ?? throw new InvalidOperationException(
                $"Cannot resolve {memberName} before the XZone script-string table is initialized.");
        if (rawLocalIndex >= entries.Count)
        {
            throw new InvalidDataException(
                $"{memberName} local script-string index 0x{rawLocalIndex:X4} is outside the XZone table of 0x{entries.Count:X} entries.");
        }

        XScriptStringEntry entry = entries[rawLocalIndex];
        if (entry.RuntimeHandle.IsNull || entry.Value is null)
        {
            throw new InvalidDataException(
                $"{memberName} local script-string index 0x{rawLocalIndex:X4} resolves to a null table entry.");
        }

        return new ScriptStringReference(
            RawLocalIndex: rawLocalIndex,
            Text: entry.Value,
            RuntimeHandle: entry.RuntimeHandle,
            DestinationCellAddress: destinationCellAddress);
    }
}
