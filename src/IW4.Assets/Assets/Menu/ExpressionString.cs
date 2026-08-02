using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Menu;

public sealed class ExpressionString
{
    public const int SerializedSize = 0x04;

    public XPointer<string> String { get; init; }
}
