using IW4.Game.Zone;

namespace IW4.Game.ScriptStrings;

/// <summary>
/// One materialized ScriptString field. RawLocalIndex preserves the serialized
/// zone-local value while RuntimeHandle and Text describe its global identity.
/// </summary>
public sealed record ScriptStringReference(
    ushort RawLocalIndex,
    string? Text,
    ScriptStringHandle RuntimeHandle,
    XBlockAddress DestinationCellAddress)
{
    private readonly bool _resolvedNullEntry;

    public ScriptStringReference(XScriptStringEntry entry, XBlockAddress destination)
        : this(checked((ushort)entry.Index), entry.Value, entry.RuntimeHandle, destination)
    {
        _resolvedNullEntry = RawLocalIndex != 0 && Text is null;
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasResolvedValue => Text is not null || RawLocalIndex == 0 || _resolvedNullEntry;
}
