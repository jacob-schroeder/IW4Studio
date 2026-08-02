using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Sound;

public sealed class SoundAliasListAsset : BaseAsset
{
    public const int SerializedSize = 0x0C;

    public XPointer<string> AliasNamePointer { get; init; }
    public string? AliasName { get; init; }
    public XPointer<SndAlias[]> AliasesPointer { get; init; }
    public int Count { get; init; }
    public IReadOnlyList<SndAlias> Aliases { get; init; } = [];
}
