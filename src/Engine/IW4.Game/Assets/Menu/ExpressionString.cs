using IW4.Game.Pointers;

namespace IW4.Game.Assets.Menu;

public sealed class ExpressionString
{
    public const int SerializedSize = 0x04;

    public XPointer<string> String { get; init; }
}
