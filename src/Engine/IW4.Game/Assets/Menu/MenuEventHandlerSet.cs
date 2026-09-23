using IW4.Game.Pointers;

namespace IW4.Game.Assets.Menu;

public sealed class MenuEventHandlerSet
{
    public const int SerializedSize = 0x08;

    public int EventHandlerCount { get; init; }
    public XPointer<XPointer<MenuEventHandler>[]> EventHandlers { get; init; }
    public IReadOnlyList<MenuEventHandlerReference> Handlers { get; set; } = [];
}
