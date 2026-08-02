using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Menu;

public sealed class ConditionalScript
{
    public const int SerializedSize = 0x08;

    public XPointer<MenuEventHandlerSet> EventHandlerSet { get; init; }
    public MenuEventHandlerSet? EventHandlers { get; set; }
    public XPointer<Statement> EventExpression { get; init; }
    public Statement? EventStatement { get; set; }
}
