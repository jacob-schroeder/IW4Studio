namespace IW4.Game.Assets.Menu;

public union EventData(
    UnconditionalScriptEventData,
    ConditionalScriptEventData,
    ElseScriptEventData,
    SetLocalVarEventData,
    IgnoredEventData)
{
    public const int SerializedSize = 0x04;

    public static EventData Ignored { get; } = new IgnoredEventData();

    public UnconditionalScriptEventData? UnconditionalScript => this is UnconditionalScriptEventData value ? value : null;
    public ConditionalScriptEventData? ConditionalScript => this is ConditionalScriptEventData value ? value : null;
    public ElseScriptEventData? ElseScript => this is ElseScriptEventData value ? value : null;
    public SetLocalVarEventData? SetLocalVarData => this is SetLocalVarEventData value ? value : null;
}
