using System.Globalization;
using System.Numerics;
using System.Text;
using IW4.Gsc.Syntax;

namespace IW4.Gsc.Semantics;

// PS3 EvalExpression/EmitOrEvalExpression fold primitive and binary expressions.
// Logical operators, unary !/~, calls and field/index reads remain VM operations.
internal sealed class GscConstantEvaluator(
    GscSourceText source,
    IReadOnlyDictionary<string, GscConstant> defines,
    Action<GscTextSpan, string> report,
    CancellationToken cancellationToken)
{
    private readonly Dictionary<GscSyntaxNode, GscConstant?> _values = [];

    internal GscConstant? Evaluate(GscSyntaxNode node)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_values.TryGetValue(node, out GscConstant? cached))
            return cached;
        GscConstant? value = EvaluateCore(node);
        _values.Add(node, value);
        return value;
    }

    internal void DefinesChanged() => _values.Clear();

    private GscConstant? EvaluateCore(GscSyntaxNode node)
    {
        switch (node.Production)
        {
            case GscProduction.ExpressionFromPrimary:
            case GscProduction.PrimaryLValueExpression:
                return Evaluate(GscSemanticSyntax.Node(node.Children[0]));
            case GscProduction.LocalLValue:
                return defines.TryGetValue(source.GetText(node.Span).ToLowerInvariant(), out GscConstant define)
                    ? define : null;
            case GscProduction.IntegerLiteral:
            case GscProduction.NegativeIntegerLiteral:
            {
                int value = 0;
                foreach (char digit in source.GetText(node.Children[^1].Span))
                    value = unchecked(value * 10 + digit - '0');
                return GscConstant.Integer(node.Production == GscProduction.NegativeIntegerLiteral
                    ? unchecked(-value) : value);
            }
            case GscProduction.FloatLiteral:
            case GscProduction.NegativeFloatLiteral:
            {
                float.TryParse(source.GetText(node.Children[^1].Span), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out float value);
                return GscConstant.Float(node.Production == GscProduction.NegativeFloatLiteral ? -value : value);
            }
            case GscProduction.TrueLiteral: return GscConstant.Integer(1);
            case GscProduction.FalseLiteral: return GscConstant.Integer(0);
            case GscProduction.UndefinedLiteral: return new GscConstant(GscConstantKind.Undefined);
            case GscProduction.AnimationExpression:
                return new GscConstant(GscConstantKind.PreAnimation,
                    Text: source.GetText(node.Children[1].Span).ToLowerInvariant());
            case GscProduction.StringLiteral:
            case GscProduction.LocalizedStringLiteral:
                return new GscConstant(node.Production == GscProduction.StringLiteral
                    ? GscConstantKind.String : GscConstantKind.LocalizedString,
                    Text: DecodeString(source.GetText(node.Span)));
            case GscProduction.ParenthesizedExpressionList:
            {
                GscSyntaxNode optional = GscSemanticSyntax.Node(node.Children[1]);
                if (optional.Production == GscProduction.OptionalExpressionListEmpty)
                    return null;
                GscSyntaxNode[] items = GscSemanticSyntax.EnumerateExpressions(
                    GscSemanticSyntax.Node(optional.Children[0])).ToArray();
                if (items.Length == 1)
                    return Evaluate(items[0]);
                if (items.Length != 3)
                    return null;
                GscConstant?[] components = items.Select(Evaluate).ToArray();
                if (components.Any(component => component is null))
                    return null;
                for (int i = 0; i < components.Length; i++)
                {
                    if (components[i] is { IsNumeric: false } component)
                    {
                        report(items[i].Span, $"type {component.TypeName} is not a float");
                        return null;
                    }
                }
                return new GscConstant(GscConstantKind.Vector, Vector: new Vector3(
                    components[0].GetValueOrDefault().Number,
                    components[1].GetValueOrDefault().Number,
                    components[2].GetValueOrDefault().Number));
            }
            case >= GscProduction.BitwiseOrExpression and <= GscProduction.ModuloExpression:
            {
                GscConstant? left = Evaluate(GscSemanticSyntax.Node(node.Children[0]));
                GscConstant? right = Evaluate(GscSemanticSyntax.Node(node.Children[2]));
                return left is { } first && right is { } second
                    ? Binary(node.Production, first, second, node.Children[1].Span)
                    : null;
            }
            default: return null;
        }
    }

    private GscConstant? Binary(GscProduction operation, GscConstant left, GscConstant right, GscTextSpan span)
    {
        if (operation is GscProduction.BitwiseOrExpression or GscProduction.BitwiseXorExpression
            or GscProduction.BitwiseAndExpression or GscProduction.ShiftLeftExpression
            or GscProduction.ShiftRightExpression or GscProduction.ModuloExpression)
        {
            if (left.Kind != GscConstantKind.Integer || right.Kind != GscConstantKind.Integer)
                return Mismatch(left, right, span);
            int a = left.Int, b = right.Int;
            // PPC slw/sraw use six shift-count bits, unlike C#'s five.
            int shift = b & 63;
            if (operation == GscProduction.ModuloExpression && b == 0)
                return Error(span, "divide by 0");
            return GscConstant.Integer(operation switch
            {
                GscProduction.BitwiseOrExpression => a | b,
                GscProduction.BitwiseXorExpression => a ^ b,
                GscProduction.BitwiseAndExpression => a & b,
                GscProduction.ShiftLeftExpression => shift >= 32 ? 0 : a << shift,
                GscProduction.ShiftRightExpression => shift >= 32 ? a >> 31 : a >> shift,
                _ => b == -1 ? 0 : a % b
            });
        }

        if (operation == GscProduction.AddExpression &&
            (left.Kind == GscConstantKind.String || right.Kind == GscConstantKind.String))
        {
            if (!CanConvertToString(left) || !CanConvertToString(right))
                return Mismatch(left, right, span);
            string a = left.Display, b = right.Display;
            string joined = a + b;
            if (source.GetByteCount(joined) >= 8192)
                return Error(span, $"cannot concat \"{a}\" and \"{b}\" - max string length exceeded");
            return new GscConstant(GscConstantKind.String, Text: joined);
        }

        if (left.Kind != right.Kind)
        {
            if (left.IsNumeric && right.IsNumeric)
            {
                left = GscConstant.Float(left.Number);
                right = GscConstant.Float(right.Number);
            }
            else if (operation != GscProduction.AddExpression &&
                     left.Kind == GscConstantKind.Vector && right.IsNumeric)
                right = new GscConstant(GscConstantKind.Vector, Vector: new Vector3(right.Number));
            else if (operation != GscProduction.AddExpression &&
                     right.Kind == GscConstantKind.Vector && left.IsNumeric)
                left = new GscConstant(GscConstantKind.Vector, Vector: new Vector3(left.Number));
            else
                return Mismatch(left, right, span);
        }

        if (operation is GscProduction.EqualsExpression or GscProduction.NotEqualsExpression)
        {
            if (left.Kind == GscConstantKind.PreAnimation) return Mismatch(left, right, span);
            bool equal = left.Kind switch
            {
                GscConstantKind.Undefined => true,
                GscConstantKind.Integer => left.Int == right.Int,
                GscConstantKind.Float => MathF.Abs(left.Number - right.Number) < 0.000001f,
                GscConstantKind.Vector => left.Vector == right.Vector,
                _ => string.Equals(left.Text, right.Text, StringComparison.Ordinal)
            };
            return GscConstant.Integer(equal == (operation == GscProduction.EqualsExpression) ? 1 : 0);
        }
        if (operation is >= GscProduction.LessThanExpression and <= GscProduction.GreaterThanOrEqualExpression)
        {
            if (!left.IsNumeric)
                return Mismatch(left, right, span);
            double a = left.Kind == GscConstantKind.Integer ? left.Int : left.Number;
            double b = right.Kind == GscConstantKind.Integer ? right.Int : right.Number;
            bool result = operation switch
            {
                GscProduction.LessThanExpression => a < b,
                GscProduction.GreaterThanExpression => a > b,
                GscProduction.LessThanOrEqualExpression => !(a > b),
                _ => !(a < b)
            };
            return GscConstant.Integer(result ? 1 : 0);
        }
        if (left.Kind == GscConstantKind.Vector)
        {
            Vector3 a = left.Vector, b = right.Vector;
            if (operation == GscProduction.DivideExpression && (b.X == 0 || b.Y == 0 || b.Z == 0))
                return Error(span, "divide by 0");
            return new GscConstant(GscConstantKind.Vector, Vector: operation switch
            {
                GscProduction.AddExpression => a + b,
                GscProduction.SubtractExpression => a - b,
                GscProduction.MultiplyExpression => a * b,
                _ => a / b
            });
        }
        if (!left.IsNumeric)
            return Mismatch(left, right, span);
        if (operation == GscProduction.DivideExpression)
        {
            if (right.Number == 0)
                return Error(span, "divide by 0");
            // Integer division also produces VAR_FLOAT.
            return GscConstant.Float(left.Kind == GscConstantKind.Integer
                ? (float)((double)left.Int / right.Int) : left.Number / right.Number);
        }
        if (left.Kind == GscConstantKind.Integer)
            return GscConstant.Integer(operation switch
            {
                GscProduction.AddExpression => unchecked(left.Int + right.Int),
                GscProduction.SubtractExpression => unchecked(left.Int - right.Int),
                _ => unchecked(left.Int * right.Int)
            });
        return GscConstant.Float(operation switch
        {
            GscProduction.AddExpression => left.Number + right.Number,
            GscProduction.SubtractExpression => left.Number - right.Number,
            _ => left.Number * right.Number
        });
    }

    private static bool CanConvertToString(GscConstant value) =>
        value.Kind is GscConstantKind.String or GscConstantKind.Vector || value.IsNumeric;

    private GscConstant? Mismatch(GscConstant left, GscConstant right, GscTextSpan span) =>
        Error(span, $"pair '{left.Display}' and '{right.Display}' has unmatching types '{left.TypeName}' and '{right.TypeName}'");

    private GscConstant? Error(GscTextSpan span, string message)
    {
        report(span, message);
        return null;
    }

    internal static string DecodeString(string token)
    {
        int start = token.StartsWith('&') ? 2 : 1;
        var result = new StringBuilder();
        for (int i = start; i < token.Length - 1; i++)
        {
            char value = token[i];
            if (value == '\\' && ++i < token.Length - 1)
                value = token[i] switch { 'n' => '\n', 'r' => '\r', 't' => '\t', var escaped => escaped };
            if (value == '\0')
                break;
            result.Append(value);
        }
        return result.ToString();
    }
}

// These are the primitive VariableValue tags used by the engine evaluator.
internal enum GscConstantKind { Undefined = 0, String = 2, LocalizedString = 3, Vector = 4, Float = 5, Integer = 6, PreAnimation = 15 }

internal readonly record struct GscConstant(GscConstantKind Kind, int Int = 0, float Real = 0,
    string? Text = null, Vector3 Vector = default)
{
    internal static GscConstant Integer(int value) => new(GscConstantKind.Integer, Int: value);
    internal static GscConstant Float(float value) => new(GscConstantKind.Float, Real: value);
    internal bool IsNumeric => Kind is GscConstantKind.Integer or GscConstantKind.Float;
    internal float Number => Kind == GscConstantKind.Integer ? Int : Real;
    internal string TypeName => Kind switch
    {
        GscConstantKind.LocalizedString => "localized string",
        GscConstantKind.Integer => "int",
        GscConstantKind.PreAnimation => "pre animation",
        _ => Kind.ToString().ToLowerInvariant()
    };
    internal string Display => Kind switch
    {
        GscConstantKind.String or GscConstantKind.LocalizedString => Text ?? string.Empty,
        GscConstantKind.Integer => Int.ToString(CultureInfo.InvariantCulture),
        GscConstantKind.Float => Format(Real),
        GscConstantKind.Vector => $"({Format(Vector.X)}, {Format(Vector.Y)}, {Format(Vector.Z)})",
        _ => TypeName
    };
    private static string Format(float value) => value.ToString("g6", CultureInfo.InvariantCulture);
}
