using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Menu;

public sealed class StringList
{
    public const int SerializedSize = 0x08;

    public int TotalStrings { get; init; }
    public XPointer<XPointer<string>[]> Strings { get; init; }
    public IReadOnlyList<XStringReference> LoadedStrings { get; set; } = [];
}
