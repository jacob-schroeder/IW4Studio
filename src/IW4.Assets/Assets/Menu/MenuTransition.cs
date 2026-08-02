namespace IW4.Assets.Assets.Menu;

public sealed class MenuTransition
{
    public const int SerializedSize = 0x1c;

    public MenuTransitionType TransitionType { get; init; }

    /// <summary>
    /// Serialized target selector. Scale/alpha/x/y transitions are grouped by
    /// their containing arrays.
    /// </summary>
    public int TargetField { get; init; }
    public int StartTime { get; init; }
    public float StartValue { get; init; }
    public float EndValue { get; init; }
    public float Time { get; init; }
    public MenuTransitionEndTrigger EndTriggerType { get; init; }
}
