using IW4.Game.Pointers;

namespace IW4.Game.Assets.Menu;

public sealed class StringList
{
    public const int SerializedSize = 0x08;

    public int TotalStrings { get; init; }
    public XPointer<XPointer<string>[]> Strings { get; init; }
    public IReadOnlyList<XStringReference> LoadedStrings { get; set; } = [];
}
