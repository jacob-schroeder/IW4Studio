using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Menu;

public sealed class ItemFloatExpression
{
    public const int SerializedSize = 0x08;

    public ItemFloatExpressionTarget Target { get; init; }
    public XPointer<Statement> Expression { get; init; }
    public Statement? Statement { get; set; }
}
