using IW4.FastFiles.Pointers;
using XString = IW4.FastFiles.Pointers.XPointer<string>;

namespace IW4.FastFiles.Loaders.Assets.Weapon;

internal sealed record SoundAliasCellArrayPayload(
    IReadOnlyList<XString> Pointers,
    IReadOnlyList<string?> Values);
