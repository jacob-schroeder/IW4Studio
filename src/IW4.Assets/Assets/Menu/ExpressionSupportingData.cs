namespace IW4.Assets.Assets.Menu;

public sealed class ExpressionSupportingData
{
    public const int SerializedSize = 0x18;

    public UIFunctionList UiFunctions { get; init; } = new();
    /// <summary>Static dvar rows associated with this supporting-data root.</summary>
    public StaticDvarList StaticDvarList { get; set; } = new();
    public StringList UiStrings { get; init; } = new();
}
