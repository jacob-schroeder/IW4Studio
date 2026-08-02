using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Menu;

public sealed class MenuEventHandlerSet
{
    public const int SerializedSize = 0x08;

    public int EventHandlerCount { get; init; }
    public XPointer<XPointer<MenuEventHandler>[]> EventHandlers { get; init; }
    public IReadOnlyList<MenuEventHandlerReference> Handlers { get; set; } = [];
}
