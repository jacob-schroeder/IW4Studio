using IW4.FastFiles.Strings;

namespace IW4.Assets.Assets.Weapon;

public sealed class WeaponNoteTrackMapEntry
{
    public ScriptStringReference Key { get; init; } = new(0, null, default, default);
    public ScriptStringReference Value { get; init; } = new(0, null, default, default);
}
