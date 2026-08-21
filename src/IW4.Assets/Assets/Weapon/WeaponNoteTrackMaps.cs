using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Weapon;

public sealed class WeaponNoteTrackMaps
{
    // 0x018 / 0x01C: sound notify remap pair.
    public XPointer<ushort[]> SoundMapKeysPointer { get; init; }
    public XPointer<ushort[]> SoundMapValuesPointer { get; init; }
    public IReadOnlyList<WeaponNoteTrackMapEntry> SoundMappings { get; init; } = [];

    // 0x020 / 0x024: rumble notify remap pair.
    public XPointer<ushort[]> RumbleMapKeysPointer { get; init; }
    public XPointer<ushort[]> RumbleMapValuesPointer { get; init; }
    public IReadOnlyList<WeaponNoteTrackMapEntry> RumbleMappings { get; init; } = [];
}
