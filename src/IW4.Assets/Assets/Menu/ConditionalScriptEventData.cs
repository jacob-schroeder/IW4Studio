using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Menu;

public sealed class ConditionalScriptEventData : EventDataValue
{
    public XPointer<ConditionalScript> ConditionalScriptPointer { get; init; }
}
