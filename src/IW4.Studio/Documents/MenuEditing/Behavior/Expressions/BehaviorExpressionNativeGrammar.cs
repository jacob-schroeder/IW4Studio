using IW4.Assets.Assets.Menu;

namespace IW4.Studio.Documents.MenuEditing.Behavior.Expressions;

/// <summary>Exact immutable expression grammar facts observed in the native runtime.</summary>
internal static class BehaviorExpressionNativeGrammar
{
    internal const int MaximumFunctionArguments = 10;
    internal const int MaximumStackDepth = 60;

    internal static bool IsKnownOperation(OperationEnum operation) =>
        (int)operation >= (int)OperationEnum.OP_NOOP &&
        (int)operation <= (int)OperationEnum.OP_DOWEHAVEMAPPACK;

    internal static bool IsFunction(OperationEnum operation) =>
        (int)operation >= (int)OperationEnum.OP_STATICDVARINT &&
        (int)operation <= (int)OperationEnum.OP_DOWEHAVEMAPPACK;

    internal static bool PairsWithRightParenthesis(OperationEnum operation) =>
        operation == OperationEnum.OP_LEFTPAREN || IsFunction(operation);

    internal static bool IsAssociative(OperationEnum operation) =>
        (int)operation < (int)OperationEnum.OP_DIVIDE ||
        (int)operation > (int)OperationEnum.OP_MODULUS &&
        operation != OperationEnum.OP_SUBTRACT;

    internal static bool HasDefaultPrecedence(OperationEnum operation) =>
        Precedence(operation) == 5;

    internal static int Precedence(OperationEnum operation) => operation switch
    {
        OperationEnum.OP_NOOP => int.MaxValue,
        OperationEnum.OP_RIGHTPAREN => 0,
        OperationEnum.OP_MULTIPLY or
        OperationEnum.OP_DIVIDE or
        OperationEnum.OP_MODULUS => 11,
        OperationEnum.OP_ADD or
        OperationEnum.OP_SUBTRACT => 13,
        OperationEnum.OP_NOT => 9,
        OperationEnum.OP_LESSTHAN or
        OperationEnum.OP_LESSTHANEQUALTO or
        OperationEnum.OP_GREATERTHAN or
        OperationEnum.OP_GREATERTHANEQUALTO => 15,
        OperationEnum.OP_EQUALS or
        OperationEnum.OP_NOTEQUAL => 16,
        OperationEnum.OP_AND or
        OperationEnum.OP_OR => 25,
        OperationEnum.OP_LEFTPAREN => 99,
        OperationEnum.OP_COMMA => 80,
        OperationEnum.OP_BITWISEAND => 17,
        OperationEnum.OP_BITWISEOR => 18,
        OperationEnum.OP_BITWISENOT => 9,
        OperationEnum.OP_BITSHIFTLEFT or
        OperationEnum.OP_BITSHIFTRIGHT => 14,
        _ => 5
    };
}
