using IW4.Game.Pointers;

namespace IW4.Loaders.Assets.Weapon;

internal readonly record struct WeaponNoteTrackMapPointers(
    XPointer<ushort[]> SoundMapKeysPointer,
    XPointer<ushort[]> SoundMapValuesPointer,
    XPointer<ushort[]> RumbleMapKeysPointer,
    XPointer<ushort[]> RumbleMapValuesPointer);
