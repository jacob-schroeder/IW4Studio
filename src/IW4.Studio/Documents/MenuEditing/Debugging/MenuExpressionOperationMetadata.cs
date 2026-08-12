using IW4.Assets.Assets.Menu;

namespace IW4.Studio.Documents.MenuEditing.Debugging;

/// <summary>
/// Native result contracts for simulated environment operations. Keeping this
/// metadata shared prevents scenario inputs and evaluator coercion from
/// disagreeing about an operation's value type.
/// </summary>
internal static class MenuExpressionOperationMetadata
{
    public static MenuDebugValueKind? EnvironmentResultKind(
        OperationEnum operation) => operation switch
        {
            OperationEnum.OP_ANYNEWMAPPACKS or
            OperationEnum.OP_ISSPLITSCREENONLINEPOSSIBLE =>
                MenuDebugValueKind.Boolean,
            _ => null
        };
}
