namespace IW4.Assets.Assets.Menu;

public sealed class ExpressionSupportingData
{
    public const int SerializedSize = 0x18;

    public UIFunctionList UiFunctions { get; init; } = new();
    public StaticDvarList StaticDvarList { get; init; } = new();
    public StringList UiStrings { get; init; } = new();
}
