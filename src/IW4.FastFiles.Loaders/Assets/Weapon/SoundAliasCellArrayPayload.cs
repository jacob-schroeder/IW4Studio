using IW4.FastFiles.Pointers;
using XString = IW4.FastFiles.Pointers.XPointer<string>;

namespace IW4.FastFiles.Loaders.Assets.Weapon;

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
