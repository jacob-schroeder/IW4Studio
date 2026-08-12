using IW4.Assets.Assets.Menu;
using IW4.Studio.Documents.MenuEditing.Behavior.Expressions;

namespace IW4.Studio.Documents.MenuEditing.Debugging;

/// <summary>
/// Compiles the engine's flat expression stream with the same operator-stack
/// and operand-list rules used by the IW4 runtime.
/// </summary>
internal sealed class DebugExpressionTreeCompiler
{
    private readonly IReadOnlyList<ExpressionEntry> _entries;
    private readonly ExpressionSupportingData? _supportingData;
    private readonly List<List<DebugExpressionNode>> _operandLists = [];
    private readonly List<OperatorFrame> _operators = [];
    private string? _error;

    public DebugExpressionTreeCompiler(
        IReadOnlyList<ExpressionEntry> entries,
        ExpressionSupportingData? supportingData)
    {
        _entries = entries;
        _supportingData = supportingData;
    }

    public DebugExpressionNode Compile()
    {
        if (_entries.Count == 0)
            return Invalid("The statement contains no expression entries.");

        for (int index = 0; index < _entries.Count && _error is null; index++)
        {
            ExpressionEntry entry = _entries[index];
            if (entry.IsOperand)
            {
                DebugExpressionNode operand = CompileOperand(entry);
                if (operand is DebugInvalidExpressionNode invalid)
                    Fail(invalid.Message);
                else
                    PushSingle(operand);
                continue;
            }

            if (!entry.IsOperator)
            {
                Fail($"Expression entry {index} has an unknown representation.");
                continue;
            }

            OperationEnum operation = entry.Operation;
            if (!BehaviorExpressionNativeGrammar.IsKnownOperation(operation))
            {
                Fail($"Expression entry {index} contains unsupported operator code {entry.OperationCode}.");
                continue;
            }

            if (operation != OperationEnum.OP_LEFTPAREN)
                RunHigherPriorityOperators(operation);
            if (_error is null)
            {
                if (_operators.Count == BehaviorExpressionNativeGrammar.MaximumStackDepth)
                {
                    Fail("Expression operators are nested beyond the engine stack limit.");
                }
                else
                {
                    _operators.Add(new OperatorFrame(
                        operation,
                        BehaviorExpressionNativeGrammar.IsFunction(operation)
                            ? _operandLists.Count
                            : -1));
                }
            }
        }

        while (_operators.Count > 0 && _error is null)
            RunOperator();

        if (_error is not null)
            return Invalid(_error);
        if (_operandLists.Count != 1)
        {
            return Invalid(
                $"Expression produced {_operandLists.Count} operand lists instead of one.");
        }
        if (_operandLists[0].Count != 1)
        {
            return Invalid(
                $"Expression produced {_operandLists[0].Count} values instead of one.");
        }

        return _operandLists[0][0];
    }

    private void RunHigherPriorityOperators(OperationEnum incoming)
    {
        while (_operators.Count > 0 && _error is null)
        {
            OperationEnum top = _operators[^1].Operation;
            int topPrecedence = BehaviorExpressionNativeGrammar.Precedence(top);
            int incomingPrecedence = BehaviorExpressionNativeGrammar.Precedence(incoming);
            bool stop =
                (topPrecedence >= incomingPrecedence ||
                 BehaviorExpressionNativeGrammar.HasDefaultPrecedence(top) &&
                 incoming != OperationEnum.OP_RIGHTPAREN) &&
                (BehaviorExpressionNativeGrammar.IsAssociative(incoming) || top != incoming);
            if (stop)
                return;

            RunOperator();
        }
    }

    private void RunOperator()
    {
        OperatorFrame frame = _operators[^1];
        _operators.RemoveAt(_operators.Count - 1);

        switch (frame.Operation)
        {
            case OperationEnum.OP_NOOP:
                Fail("The expression contains the invalid NOOP operator.");
                return;
            case OperationEnum.OP_RIGHTPAREN:
                CloseRightParenthesis();
                return;
            case OperationEnum.OP_MULTIPLY:
            case OperationEnum.OP_DIVIDE:
            case OperationEnum.OP_MODULUS:
            case OperationEnum.OP_ADD:
            case OperationEnum.OP_LESSTHAN:
            case OperationEnum.OP_LESSTHANEQUALTO:
            case OperationEnum.OP_GREATERTHAN:
            case OperationEnum.OP_GREATERTHANEQUALTO:
            case OperationEnum.OP_EQUALS:
            case OperationEnum.OP_NOTEQUAL:
            case OperationEnum.OP_AND:
            case OperationEnum.OP_OR:
            case OperationEnum.OP_BITWISEAND:
            case OperationEnum.OP_BITWISEOR:
            case OperationEnum.OP_BITSHIFTLEFT:
            case OperationEnum.OP_BITSHIFTRIGHT:
                CompileBinary(frame.Operation);
                return;
            case OperationEnum.OP_SUBTRACT:
                CompileSubtract();
                return;
            case OperationEnum.OP_NOT:
            case OperationEnum.OP_BITWISENOT:
                CompileUnary(frame.Operation);
                return;
            case OperationEnum.OP_LEFTPAREN:
                return;
            case OperationEnum.OP_COMMA:
                CompileComma();
                return;
            default:
                if (BehaviorExpressionNativeGrammar.IsFunction(frame.Operation))
                {
                    CompileFunction(frame);
                    return;
                }
                Fail($"Unsupported expression operator '{frame.Operation}'.");
                return;
        }
    }

    private void CloseRightParenthesis()
    {
        while (_operators.Count > 0 && _error is null)
        {
            OperationEnum paired = _operators[^1].Operation;
            RunOperator();
            if (BehaviorExpressionNativeGrammar.PairsWithRightParenthesis(paired))
                return;
        }

        if (_error is null)
            Fail("A right parenthesis has no matching group or function.");
    }

    private void CompileBinary(OperationEnum operation)
    {
        if (!TryPopSingle(out DebugExpressionNode right) ||
            !TryPopSingle(out DebugExpressionNode left))
        {
            Fail($"Binary operator '{operation}' does not have two scalar operands.");
            return;
        }

        PushSingle(new DebugBinaryExpressionNode(operation, left, right));
    }

    private void CompileSubtract()
    {
        if (!TryPopSingle(out DebugExpressionNode right))
        {
            Fail("Subtraction or negation has no scalar operand.");
            return;
        }

        if (_operandLists.Count == 0)
        {
            PushSingle(new DebugUnaryExpressionNode(OperationEnum.OP_SUBTRACT, right));
            return;
        }

        if (!TryPopSingle(out DebugExpressionNode left))
        {
            Fail("Subtraction has an invalid left operand.");
            return;
        }
        PushSingle(new DebugBinaryExpressionNode(OperationEnum.OP_SUBTRACT, left, right));
    }

    private void CompileUnary(OperationEnum operation)
    {
        if (!TryPopSingle(out DebugExpressionNode operand))
        {
            Fail($"Unary operator '{operation}' has no scalar operand.");
            return;
        }
        PushSingle(new DebugUnaryExpressionNode(operation, operand));
    }

    private void CompileComma()
    {
        if (!TryPopList(out List<DebugExpressionNode> right) ||
            !TryPopList(out List<DebugExpressionNode> left))
        {
            Fail("Comma does not have two operand lists to combine.");
            return;
        }
        if (left.Count + right.Count > BehaviorExpressionNativeGrammar.MaximumFunctionArguments)
        {
            Fail(
                $"A function argument list exceeds the engine limit of {BehaviorExpressionNativeGrammar.MaximumFunctionArguments} values.");
            return;
        }

        left.AddRange(right);
        _operandLists.Add(left);
    }

    private void CompileFunction(OperatorFrame frame)
    {
        int availableLists = _operandLists.Count - frame.OperandDepth;
        if (availableLists is < 0 or > 1)
        {
            Fail(
                $"Function '{frame.Operation}' has an invalid operand-list boundary.");
            return;
        }

        DebugExpressionNode[] arguments;
        if (availableLists == 0)
        {
            arguments = [];
        }
        else
        {
            List<DebugExpressionNode> values = _operandLists[^1];
            _operandLists.RemoveAt(_operandLists.Count - 1);
            arguments = values.ToArray();
        }

        string? staticDvarName = IsStaticDvar(frame.Operation)
            ? ResolveStaticDvarName(arguments.FirstOrDefault())
            : null;
        PushSingle(new DebugCallExpressionNode(
            frame.Operation,
            Array.AsReadOnly(arguments),
            staticDvarName));
    }

    private DebugExpressionNode CompileOperand(ExpressionEntry entry) =>
        entry.Operand.DataType switch
        {
            ExpDataType.VAL_INT when entry.Operand.Value is IntOperandValue value =>
                new DebugLiteralExpressionNode(MenuDebugValue.FromInt(value.Value)),
            ExpDataType.VAL_FLOAT when entry.Operand.Value is FloatOperandValue value =>
                new DebugLiteralExpressionNode(MenuDebugValue.FromFloat(value.Value)),
            ExpDataType.VAL_STRING =>
                new DebugLiteralExpressionNode(
                    MenuDebugValue.FromString(entry.StringValue ?? string.Empty)),
            ExpDataType.VAL_FUNCTION when entry.FunctionStatement is not null =>
                new DebugExpressionTreeCompiler(
                    entry.FunctionStatement.LoadedEntries,
                    entry.FunctionStatement.SupportingDataValue ?? _supportingData)
                    .Compile(),
            ExpDataType.VAL_FUNCTION =>
                Invalid("Function operand has no loaded Statement."),
            _ => Invalid(
                $"Unsupported operand representation '{entry.Operand.DataType}'.")
        };

    private string? ResolveStaticDvarName(DebugExpressionNode? argument)
    {
        if (argument is not DebugLiteralExpressionNode literal ||
            !literal.Value.TryGetInt(out int index))
        {
            return null;
        }

        return _supportingData?.StaticDvarList.LoadedStaticDvars
            .FirstOrDefault(reference => reference.Index == index)
            ?.StaticDvar?.DvarNameString;
    }

    private bool TryPopSingle(out DebugExpressionNode node)
    {
        node = null!;
        if (!TryPopList(out List<DebugExpressionNode> list) || list.Count != 1)
            return false;
        node = list[0];
        return true;
    }

    private bool TryPopList(out List<DebugExpressionNode> list)
    {
        if (_operandLists.Count == 0)
        {
            list = null!;
            return false;
        }
        list = _operandLists[^1];
        _operandLists.RemoveAt(_operandLists.Count - 1);
        return true;
    }

    private void PushSingle(DebugExpressionNode node)
    {
        if (_operandLists.Count == BehaviorExpressionNativeGrammar.MaximumStackDepth)
        {
            Fail("Expression contains more operands than the engine stack accepts.");
            return;
        }
        _operandLists.Add([node]);
    }

    private void Fail(string message) => _error ??= message;

    private static DebugInvalidExpressionNode Invalid(string message) => new(message);

    private static bool IsStaticDvar(OperationEnum operation) => operation is
        OperationEnum.OP_STATICDVARINT or
        OperationEnum.OP_STATICDVARBOOL or
        OperationEnum.OP_STATICDVARFLOAT or
        OperationEnum.OP_STATICDVARSTRING;

    private readonly record struct OperatorFrame(
        OperationEnum Operation,
        int OperandDepth);
}
