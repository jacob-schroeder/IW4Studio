using IW4.Assets.Math;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Strings;

namespace IW4.Assets.Assets.Weapon;

public sealed class WeaponNoteTrackMaps
{
    // 0x018 / 0x01C: sound notify remap pair.
    public XPointer<ushort[]> SoundMapKeysPointer { get; init; }
    public IReadOnlyList<ScriptStringReference> SoundMapKeys { get; init; } = [];
    public XPointer<ushort[]> SoundMapValuesPointer { get; init; }
    public IReadOnlyList<ScriptStringReference> SoundMapValues { get; init; } = [];

    // 0x020 / 0x024: rumble notify remap pair.
    public XPointer<ushort[]> RumbleMapKeysPointer { get; init; }
    public IReadOnlyList<ScriptStringReference> RumbleMapKeys { get; init; } = [];
    public XPointer<ushort[]> RumbleMapValuesPointer { get; init; }
    public IReadOnlyList<ScriptStringReference> RumbleMapValues { get; init; } = [];
}
