using System.Text;
using IW4.Assets.Assets.Menu;

namespace IW4.Studio.Documents.MenuEditing.Debugging;

public sealed partial class MenuExpressionEvaluator
{
    private MenuEvaluation<MenuDebugValue> EvaluateUnary(
        DebugUnaryExpressionNode expression,
        EvaluationContext context)
    {
        MenuEvaluation<MenuDebugValue> operand = EvaluateNode(expression.Operand, context);
        if (!operand.IsKnown)
            return operand;

        if (operand.Value.Kind == MenuDebugValueKind.String)
        {
            return Error(
                $"Operation '{expression.Operation}' cannot be applied to an engine string operand.",
                expression.Operation,
                operand);
        }

        return expression.Operation switch
        {
            OperationEnum.OP_NOT when operand.Value.Kind == MenuDebugValueKind.Float &&
                operand.Value.TryGetFloat(out float value) =>
                Known(MenuDebugValue.FromBoolean(value == 0), operand),
            OperationEnum.OP_NOT when operand.Value.TryGetInt(out int value) =>
                Known(MenuDebugValue.FromBoolean(value == 0), operand),
            OperationEnum.OP_BITWISENOT when operand.Value.TryGetInt(out int value) =>
                Known(MenuDebugValue.FromInt(~value), operand),
            OperationEnum.OP_SUBTRACT when operand.Value.Kind == MenuDebugValueKind.Float &&
                operand.Value.TryGetFloat(out float value) =>
                Known(MenuDebugValue.FromFloat(-value), operand),
            OperationEnum.OP_SUBTRACT when operand.Value.TryGetInt(out int value) =>
                Known(MenuDebugValue.FromInt(unchecked(-value)), operand),
            _ => ConversionError(expression.Operation, operand)
        };
    }

    private MenuEvaluation<MenuDebugValue> EvaluateBinary(
        DebugBinaryExpressionNode expression,
        EvaluationContext context)
    {
        MenuEvaluation<MenuDebugValue> left = EvaluateNode(expression.Left, context);
        MenuEvaluation<MenuDebugValue> right = EvaluateNode(expression.Right, context);
        if (!left.IsKnown || !right.IsKnown)
            return MergeUnavailable(left, right);

        return expression.Operation switch
        {
            OperationEnum.OP_AND or OperationEnum.OP_OR =>
                Logical(expression.Operation, left, right),
            OperationEnum.OP_EQUALS => CompareEquality(left, right, equal: true),
            OperationEnum.OP_NOTEQUAL => CompareEquality(left, right, equal: false),
            OperationEnum.OP_LESSTHAN or
            OperationEnum.OP_LESSTHANEQUALTO or
            OperationEnum.OP_GREATERTHAN or
            OperationEnum.OP_GREATERTHANEQUALTO =>
                CompareNumeric(expression.Operation, left, right),
            OperationEnum.OP_ADD when
                left.Value.Kind == MenuDebugValueKind.String ||
                right.Value.Kind == MenuDebugValueKind.String =>
                Concatenate(left, right),
            OperationEnum.OP_ADD => Arithmetic(
                expression.Operation,
                left,
                right,
                (a, b) => a + b,
                (a, b) => unchecked(a + b)),
            OperationEnum.OP_SUBTRACT => Arithmetic(
                expression.Operation,
                left,
                right,
                (a, b) => a - b,
                (a, b) => unchecked(a - b)),
            OperationEnum.OP_MULTIPLY => Arithmetic(
                expression.Operation,
                left,
                right,
                (a, b) => a * b,
                (a, b) => unchecked(a * b)),
            OperationEnum.OP_DIVIDE => Divide(left, right),
            OperationEnum.OP_MODULUS => Modulus(left, right),
            OperationEnum.OP_BITWISEAND =>
                Bitwise(expression.Operation, left, right, (a, b) => a & b),
            OperationEnum.OP_BITWISEOR =>
                Bitwise(expression.Operation, left, right, (a, b) => a | b),
            OperationEnum.OP_BITSHIFTLEFT =>
                Bitwise(expression.Operation, left, right, (a, b) => a << b),
            OperationEnum.OP_BITSHIFTRIGHT =>
                Bitwise(expression.Operation, left, right, (a, b) => a >> b),
            _ => Error(
                $"Unsupported binary operation '{expression.Operation}'.",
                expression.Operation,
                left,
                right)
        };
    }

    private static MenuEvaluation<MenuDebugValue> Arithmetic(
        OperationEnum operation,
        MenuEvaluation<MenuDebugValue> left,
        MenuEvaluation<MenuDebugValue> right,
        Func<float, float, float> floatOperation,
        Func<int, int, int> integerOperation)
    {
        if (!TryGetEngineNumber(left.Value, out EngineNumber leftNumber) ||
            !TryGetEngineNumber(right.Value, out EngineNumber rightNumber))
        {
            return ConversionError(operation, left, right);
        }
        if (!leftNumber.IsFloat && !rightNumber.IsFloat)
        {
            return Known(
                MenuDebugValue.FromInt(
                    integerOperation(leftNumber.Integer, rightNumber.Integer)),
                left,
                right);
        }
        return Known(
            MenuDebugValue.FromFloat(
                floatOperation(leftNumber.AsFloat, rightNumber.AsFloat)),
            left,
            right);
    }

    private static MenuEvaluation<MenuDebugValue> Concatenate(
        MenuEvaluation<MenuDebugValue> left,
        MenuEvaluation<MenuDebugValue> right)
    {
        string value = left.Value.AsString() + right.Value.AsString();
        if (Encoding.UTF8.GetByteCount(value) >= 256)
        {
            return Unknown(
                "String concatenation exceeds the engine's 256-byte expression buffer.",
                OperationEnum.OP_ADD,
                [],
                [left, right]);
        }
        return Known(MenuDebugValue.FromString(value), left, right);
    }

    private static MenuEvaluation<MenuDebugValue> Divide(
        MenuEvaluation<MenuDebugValue> left,
        MenuEvaluation<MenuDebugValue> right)
    {
        if (!TryGetEngineNumber(left.Value, out EngineNumber leftNumber) ||
            !TryGetEngineNumber(right.Value, out EngineNumber rightNumber))
        {
            return ConversionError(OperationEnum.OP_DIVIDE, left, right);
        }
        float divisor = rightNumber.AsFloat;
        float result = divisor == 0 ? 0 : leftNumber.AsFloat / divisor;
        return Known(MenuDebugValue.FromFloat(result), left, right);
    }

    private static MenuEvaluation<MenuDebugValue> Modulus(
        MenuEvaluation<MenuDebugValue> left,
        MenuEvaluation<MenuDebugValue> right)
    {
        if (!TryGetEngineNumber(left.Value, out EngineNumber leftNumber) ||
            !TryGetEngineNumber(right.Value, out EngineNumber rightNumber))
        {
            return ConversionError(OperationEnum.OP_MODULUS, left, right);
        }
        int dividend = leftNumber.AsSnappedInt;
        int divisor = rightNumber.AsSnappedInt;
        int result = divisor == 0
            ? dividend
            : dividend == int.MinValue && divisor == -1
                ? 0
                : dividend % divisor;
        return Known(MenuDebugValue.FromInt(result), left, right);
    }

    private static MenuEvaluation<MenuDebugValue> Bitwise(
        OperationEnum operation,
        MenuEvaluation<MenuDebugValue> left,
        MenuEvaluation<MenuDebugValue> right,
        Func<int, int, int> function)
    {
        int a;
        int b;
        if ((operation == OperationEnum.OP_BITWISEAND ||
             operation == OperationEnum.OP_BITWISEOR) &&
            left.Value.Kind == MenuDebugValueKind.String &&
            right.Value.Kind == MenuDebugValueKind.String)
        {
            if (!TryGetEngineNumber(left.Value, out EngineNumber leftNumber) ||
                !TryGetEngineNumber(right.Value, out EngineNumber rightNumber) ||
                !leftNumber.TryGetSourceInt(out a) ||
                !rightNumber.TryGetSourceInt(out b))
            {
                return ConversionError(operation, left, right);
            }
        }
        else if (!left.Value.TryGetInt(out a) || !right.Value.TryGetInt(out b))
        {
            return ConversionError(operation, left, right);
        }
        return Known(MenuDebugValue.FromInt(function(a, b)), left, right);
    }

    private static MenuEvaluation<MenuDebugValue> Logical(
        OperationEnum operation,
        MenuEvaluation<MenuDebugValue> left,
        MenuEvaluation<MenuDebugValue> right)
    {
        bool a;
        bool b;
        if (left.Value.Kind == MenuDebugValueKind.String &&
            right.Value.Kind == MenuDebugValueKind.String)
        {
            if (!TryGetEngineNumber(left.Value, out EngineNumber leftNumber) ||
                !TryGetEngineNumber(right.Value, out EngineNumber rightNumber))
            {
                return ConversionError(operation, left, right);
            }
            a = leftNumber.IsTrue;
            b = rightNumber.IsTrue;
        }
        else
        {
            a = IsDirectlyTrue(left.Value);
            b = IsDirectlyTrue(right.Value);
        }

        bool result = operation == OperationEnum.OP_AND ? a && b : a || b;
        return Known(MenuDebugValue.FromBoolean(result), left, right);
    }

    private static MenuEvaluation<MenuDebugValue> CompareEquality(
        MenuEvaluation<MenuDebugValue> left,
        MenuEvaluation<MenuDebugValue> right,
        bool equal)
    {
        bool result;
        if (left.Value.Kind == MenuDebugValueKind.String &&
            right.Value.Kind == MenuDebugValueKind.String)
        {
            result = string.Equals(
                left.Value.AsString(),
                right.Value.AsString(),
                StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            if (!TryGetEngineNumber(left.Value, out EngineNumber leftNumber) ||
                !TryGetEngineNumber(right.Value, out EngineNumber rightNumber))
            {
                return ConversionError(OperationEnum.OP_EQUALS, left, right);
            }
            result = CompareNumbers(
                leftNumber,
                rightNumber,
                (a, b) => a == b,
                (a, b) => a == b);
        }
        return Known(MenuDebugValue.FromBoolean(equal ? result : !result), left, right);
    }

    private static MenuEvaluation<MenuDebugValue> CompareNumeric(
        OperationEnum operation,
        MenuEvaluation<MenuDebugValue> left,
        MenuEvaluation<MenuDebugValue> right)
    {
        if (!TryGetEngineNumber(left.Value, out EngineNumber leftNumber) ||
            !TryGetEngineNumber(right.Value, out EngineNumber rightNumber))
        {
            return ConversionError(operation, left, right);
        }
        bool result = operation switch
        {
            OperationEnum.OP_LESSTHAN => CompareNumbers(
                leftNumber,
                rightNumber,
                (a, b) => a < b,
                (a, b) => a < b),
            OperationEnum.OP_LESSTHANEQUALTO => CompareNumbers(
                leftNumber,
                rightNumber,
                (a, b) => a <= b,
                (a, b) => a <= b),
            OperationEnum.OP_GREATERTHAN => CompareNumbers(
                leftNumber,
                rightNumber,
                (a, b) => a > b,
                (a, b) => a > b),
            OperationEnum.OP_GREATERTHANEQUALTO => CompareNumbers(
                leftNumber,
                rightNumber,
                (a, b) => a >= b,
                (a, b) => a >= b),
            _ => throw new InvalidOperationException(
                $"'{operation}' is not a relational operation.")
        };
        return Known(MenuDebugValue.FromBoolean(result), left, right);
    }

    private static bool CompareNumbers(
        EngineNumber left,
        EngineNumber right,
        Func<float, float, bool> floatFunction,
        Func<int, int, bool> integerFunction) =>
        !left.IsFloat && !right.IsFloat
            ? integerFunction(left.Integer, right.Integer)
            : floatFunction(left.AsFloat, right.AsFloat);

    private static bool TryGetEngineNumber(
        MenuDebugValue value,
        out EngineNumber number)
    {
        if (value.Kind == MenuDebugValueKind.String)
        {
            value.GetEngineStringNumbers(out int integer, out double floatingPoint);
            number = floatingPoint == integer
                ? EngineNumber.FromInteger(integer)
                : EngineNumber.FromFloat((float)floatingPoint);
            return true;
        }
        if (value.Kind == MenuDebugValueKind.Float &&
            value.TryGetFloat(out float floatValue))
        {
            number = EngineNumber.FromFloat(floatValue);
            return true;
        }
        if (value.TryGetInt(out int integerValue))
        {
            number = EngineNumber.FromInteger(integerValue);
            return true;
        }

        number = default;
        return false;
    }

    private static bool IsDirectlyTrue(MenuDebugValue value) => value.Kind switch
    {
        MenuDebugValueKind.String => value.AsString().Length != 0,
        MenuDebugValueKind.Float when value.TryGetFloat(out float floatingPoint) =>
            floatingPoint != 0,
        _ when value.TryGetInt(out int integer) => integer != 0,
        _ => false
    };

    private static int SnapFloatToInt(float value)
    {
        if (!float.IsFinite(value))
            return int.MinValue;
        double rounded = Math.Round(value, MidpointRounding.ToEven);
        return rounded < int.MinValue || rounded > int.MaxValue
            ? int.MinValue
            : (int)rounded;
    }

    private readonly record struct EngineNumber(
        bool IsFloat,
        int Integer,
        float FloatingPoint)
    {
        public float AsFloat => IsFloat ? FloatingPoint : Integer;
        public int AsSnappedInt => IsFloat
            ? SnapFloatToInt(FloatingPoint)
            : Integer;
        public bool IsTrue => IsFloat ? FloatingPoint != 0 : Integer != 0;

        public bool TryGetSourceInt(out int value)
        {
            if (!IsFloat)
            {
                value = Integer;
                return true;
            }
            if (float.IsFinite(FloatingPoint) &&
                FloatingPoint >= int.MinValue &&
                FloatingPoint <= int.MaxValue)
            {
                value = (int)FloatingPoint;
                return true;
            }
            value = default;
            return false;
        }

        public static EngineNumber FromInteger(int value) => new(false, value, value);
        public static EngineNumber FromFloat(float value) => new(true, 0, value);
    }
}
