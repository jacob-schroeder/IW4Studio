using IW4.Game.Pointers;
using XString = IW4.Game.Pointers.XPointer<string>;

namespace IW4.Loaders.Assets.Weapon;

internal sealed record SoundAliasCellArrayPayload(
    IReadOnlyList<XString> Pointers,
    IReadOnlyList<XString> ValuePointers,
    IReadOnlyList<string?> Values);

internal sealed record SoundAliasCellPayload(
    XString ValuePointer,
    string? Value)
{
    public static SoundAliasCellPayload Empty { get; } = new(default, null);
}
