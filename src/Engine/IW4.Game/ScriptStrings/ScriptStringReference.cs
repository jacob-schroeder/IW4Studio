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
    XBlockAddress DestinationCellAddress);
