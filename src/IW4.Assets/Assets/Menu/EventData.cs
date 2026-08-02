using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Menu;

public sealed class EventData
{
    public const int SerializedSize = 0x04;

    public EventDataValue Value { get; init; } = new IgnoredEventData { Reserved = 0 };

    public UnconditionalScriptEventData? UnconditionalScript => Value as UnconditionalScriptEventData;
    public ConditionalScriptEventData? ConditionalScript => Value as ConditionalScriptEventData;
    public ElseScriptEventData? ElseScript => Value as ElseScriptEventData;
    public SetLocalVarEventData? SetLocalVarData => Value as SetLocalVarEventData;
}
