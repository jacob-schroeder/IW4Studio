namespace IW4.Assets.Assets.Menu;

public sealed class ExpressionSupportingData
{
    public const int SerializedSize = 0x18;

    public UIFunctionList UiFunctions { get; init; } = new();
    /// <summary>
    /// The document compiler may replace this detached list when an authored
    /// support-table delta appends static dvar rows. The support-data root
    /// itself remains stable so every Statement sharing it remains rebound.
    /// </summary>
    public StaticDvarList StaticDvarList { get; set; } = new();
    public StringList UiStrings { get; init; } = new();
}
