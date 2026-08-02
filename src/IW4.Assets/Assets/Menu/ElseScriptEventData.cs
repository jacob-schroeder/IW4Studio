using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Menu;

public sealed class ElseScriptEventData : EventDataValue
{
    public XPointer<MenuEventHandlerSet> EventHandlerSetPointer { get; init; }
}
