using IW4.FastFiles.Pointers;

namespace IW4.FastFiles.Loaders.Assets.Weapon;

internal readonly record struct WeaponNoteTrackMapPointers(
    XPointer<ushort[]> SoundMapKeysPointer,
    XPointer<ushort[]> SoundMapValuesPointer,
    XPointer<ushort[]> RumbleMapKeysPointer,
    XPointer<ushort[]> RumbleMapValuesPointer);
