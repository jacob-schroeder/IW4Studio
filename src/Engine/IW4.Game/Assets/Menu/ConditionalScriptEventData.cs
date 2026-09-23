using IW4.Game.Pointers;

namespace IW4.Game.Assets.Menu;

public sealed class ConditionalScriptEventData : EventDataValue
{
    public XPointer<ConditionalScript> ConditionalScriptPointer { get; init; }
}
