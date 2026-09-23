using IW4.Game.Zone;
namespace IW4.Game.ScriptStrings;

/// <summary>
/// Opaque runtime identity returned by the engine's script-string table.
/// Serialized XZone references contain local indices instead; loaders replace
/// those indices with this global handle.
/// </summary>
public readonly record struct ScriptStringHandle(ushort Value)
{
    public static ScriptStringHandle Null => default;

    public bool IsNull => Value == 0;

    public override string ToString() => $"0x{Value:X4}";
}
