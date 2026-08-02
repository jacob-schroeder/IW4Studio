namespace IW4.Assets.Assets.Menu;

public sealed class MenuEventHandler
{
    public const int SerializedSize = 0x08;

    public EventData EventData { get; init; } = new();
    public string? UnconditionalScript { get; set; }
    public ConditionalScript? ConditionalScript { get; set; }
    public MenuEventHandlerSet? ElseScriptSet { get; set; }
    public SetLocalVarData? SetLocalVarData { get; set; }
    public MenuEventHandlerType EventType { get; init; }
    public byte Pad05 { get; init; }
    public byte Pad06 { get; init; }
    public byte Pad07 { get; init; }
}
