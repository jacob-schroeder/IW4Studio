using IW4.Game.ScriptStrings;

namespace IW4.Game.Assets.Weapon;

public sealed class WeaponNoteTrackMapEntry
{
    public ScriptStringReference Key { get; init; } = new(0, null, default, default);
    public ScriptStringReference Value { get; init; } = new(0, null, default, default);
}
