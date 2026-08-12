using System.Globalization;
using System.Text;
using IW4.Assets.Assets.Menu;

namespace IW4.Studio.Documents.MenuEditing.Behavior.Expressions;

/// <summary>Parses the editor formula language into immutable semantic nodes.</summary>
public static class BehaviorExpressionFormulaParser
{
    public static BehaviorExpressionResult<BehaviorExpression> Parse(
        string formula,
        BehaviorExpressionSupport? support = null,
        BehaviorExpressionCatalog? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(formula);
        var parser = new Parser(
            formula,
            support ?? BehaviorExpressionSupport.Empty,
            catalog ?? BehaviorExpressionCatalog.Default);
        return parser.Parse();
    }

    private sealed class Parser
    {
        private readonly BehaviorExpressionSupport _support;
        private readonly BehaviorExpressionCatalog _catalog;
        private readonly List<BehaviorExpressionDiagnostic> _diagnostics = [];
        private readonly List<Token> _tokens;
        private int _position;

        public Parser(
            string source,
            BehaviorExpressionSupport support,
            BehaviorExpressionCatalog catalog)
        {
            _support = support;
            _catalog = catalog;
            _tokens = Lex(source, _diagnostics);
        }

        public BehaviorExpressionResult<BehaviorExpression> Parse()
        {
            if (Current.Kind == TokenKind.End)
            {
                Error(BehaviorExpressionDiagnosticCode.EmptyExpression, "Enter an expression.", 0);
                return new(null, _diagnostics);
            }

            BehaviorExpression? expression = ParseBinary(0);
            if (Current.Kind != TokenKind.End)
                Error(BehaviorExpressionDiagnosticCode.UnexpectedToken, $"Unexpected token '{Current.Text}'.", Current.Position);
            return new(expression, _diagnostics);
        }

        private BehaviorExpression? ParseBinary(int minimumPrecedence)
        {
            BehaviorExpression? left = ParseUnary();
            if (left is null)
                return null;

            while (TryBinary(Current, out OperationEnum operation, out int precedence) && precedence >= minimumPrecedence)
            {
                Token token = Next();
                BehaviorExpression? right = ParseBinary(precedence + 1);
                if (right is null)
                {
                    Error(BehaviorExpressionDiagnosticCode.InvalidOperand, $"'{token.Text}' needs a right operand.", token.Position);
                    return left;
                }

                left = new BehaviorBinaryExpression(operation, left, right);
            }

            return left;
        }

        private BehaviorExpression? ParseUnary()
        {
            if (Current.Kind == TokenKind.Operator && Current.Text is "!" or "~" or "-")
            {
                Token token = Next();
                BehaviorExpression? operand = ParseUnary();
                if (operand is null)
                {
                    Error(BehaviorExpressionDiagnosticCode.InvalidOperand, $"'{token.Text}' needs an operand.", token.Position);
                    return null;
                }

                OperationEnum operation = token.Text switch
                {
                    "!" => OperationEnum.OP_NOT,
                    "~" => OperationEnum.OP_BITWISENOT,
                    _ => OperationEnum.OP_SUBTRACT
                };
                return new BehaviorUnaryExpression(operation, operand);
            }

            return ParsePrimary();
        }

        private BehaviorExpression? ParsePrimary()
        {
            Token token = Current;
            switch (token.Kind)
            {
                case TokenKind.Integer:
                    Next();
                    if (int.TryParse(token.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int integer))
                        return new BehaviorIntegerExpression(integer);
                    Error(BehaviorExpressionDiagnosticCode.InvalidToken, $"'{token.Text}' is outside the supported integer range.", token.Position);
                    return new BehaviorOpaqueExpression("Integer literal is outside the supported range.");
                case TokenKind.Float:
                    Next();
                    if (float.TryParse(token.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float number))
                        return new BehaviorFloatExpression(number);
                    Error(BehaviorExpressionDiagnosticCode.InvalidToken, $"'{token.Text}' is outside the supported float range.", token.Position);
                    return new BehaviorOpaqueExpression("Float literal is outside the supported range.");
                case TokenKind.String:
                    Next();
                    return new BehaviorStringExpression(token.Value ?? string.Empty);
                case TokenKind.LeftParenthesis:
                    Next();
                    BehaviorExpression? grouped = ParseBinary(0);
                    if (!Match(TokenKind.RightParenthesis))
                        Error(BehaviorExpressionDiagnosticCode.MissingClosingParenthesis, "Expected ')'.", Current.Position);
                    return grouped;
                case TokenKind.Identifier:
                    return ParseIdentifierOrCall();
                default:
                    Error(BehaviorExpressionDiagnosticCode.UnexpectedToken, $"Expected a value, but found '{token.Text}'.", token.Position);
                    if (token.Kind != TokenKind.End)
                        Next();
                    return null;
            }
        }

        private BehaviorExpression? ParseIdentifierOrCall()
        {
            Token identifier = Next();
            if (!Match(TokenKind.LeftParenthesis))
            {
                Error(BehaviorExpressionDiagnosticCode.UnknownOperation, $"'{identifier.Text}' is not a literal or function call.", identifier.Position);
                return null;
            }

            var arguments = new List<BehaviorExpression>();
            if (Current.Kind != TokenKind.RightParenthesis)
            {
                while (true)
                {
                    BehaviorExpression? argument = ParseBinary(0);
                    if (argument is not null)
                        arguments.Add(argument);
                    if (!Match(TokenKind.Comma))
                        break;
                }
            }
            if (!Match(TokenKind.RightParenthesis))
                Error(BehaviorExpressionDiagnosticCode.MissingClosingParenthesis, $"Expected ')' after '{identifier.Text}'.", Current.Position);

            if (IsReusableReferenceName(identifier.Text))
                return BuildReusableReference(identifier, arguments);

            if (!_catalog.TryGetFormulaOperation(identifier.Text, out BehaviorExpressionOperationMetadata? metadata))
            {
                Error(BehaviorExpressionDiagnosticCode.UnknownOperation, $"Unknown expression operation '{identifier.Text}'.", identifier.Position);
                return new BehaviorOpaqueExpression($"Unknown operation '{identifier.Text}'.");
            }
            if (!metadata.SupportsArgumentCount(arguments.Count))
                Error(BehaviorExpressionDiagnosticCode.InvalidArity, $"'{metadata.FormulaName}' does not support {arguments.Count} argument(s).", identifier.Position);
            if (BehaviorExpressionCatalog.IsStaticDvar(metadata.Operation))
                return BuildStaticDvar(metadata.Operation, identifier, arguments);
            return new BehaviorCallExpression(metadata.Operation, arguments);
        }

        private BehaviorExpression BuildReusableReference(Token identifier, IReadOnlyList<BehaviorExpression> arguments)
        {
            if (arguments.Count != 1 || arguments[0] is not BehaviorIntegerExpression value)
            {
                Error(BehaviorExpressionDiagnosticCode.InvalidArity, $"'{identifier.Text}' requires one reusable-expression index.", identifier.Position);
                return new BehaviorOpaqueExpression("Invalid reusable-expression reference.");
            }

            var id = new BehaviorReusableExpressionId(value.Value);
            if (!_support.Contains(id))
            {
                Error(BehaviorExpressionDiagnosticCode.InvalidReusableExpressionReference, $"Reusable expression {id} is not present in this Menu's support table.", identifier.Position);
                return new BehaviorOpaqueExpression("Unknown reusable-expression reference.");
            }
            return new BehaviorReusableExpressionReferenceExpression(id);
        }

        private BehaviorExpression BuildStaticDvar(
            OperationEnum operation,
            Token identifier,
            IReadOnlyList<BehaviorExpression> arguments)
        {
            if (arguments.Count != 1)
            {
                Error(BehaviorExpressionDiagnosticCode.InvalidArity, $"'{identifier.Text}' requires one static-dvar name or index.", identifier.Position);
                return new BehaviorOpaqueExpression("Invalid static-dvar call.");
            }

            bool found = arguments[0] switch
            {
                BehaviorIntegerExpression integer => _support.TryGetStaticDvar(integer.Value, out BehaviorStaticDvarReference _),
                BehaviorStringExpression text => _support.TryGetStaticDvar(text.Value, out BehaviorStaticDvarReference _),
                _ => false
            };
            if (!found)
            {
                Error(BehaviorExpressionDiagnosticCode.InvalidStaticDvarReference, "The static dvar must be an existing support-table name or index.", identifier.Position);
                return new BehaviorOpaqueExpression("Unknown static dvar.");
            }

            BehaviorStaticDvarReference dvar = arguments[0] switch
            {
                BehaviorIntegerExpression integer => Resolve(integer.Value),
                BehaviorStringExpression text => ResolveName(text.Value),
                _ => throw new InvalidOperationException("The static-dvar argument was validated before resolution.")
            };
            return new BehaviorStaticDvarExpression(operation, dvar);

            BehaviorStaticDvarReference Resolve(int index)
            {
                _support.TryGetStaticDvar(index, out BehaviorStaticDvarReference value);
                return value;
            }
            BehaviorStaticDvarReference ResolveName(string name)
            {
                _support.TryGetStaticDvar(name, out BehaviorStaticDvarReference value);
                return value;
            }
        }

        private Token Current => _tokens[Math.Min(_position, _tokens.Count - 1)];
        private Token Next() => _tokens[_position++];
        private bool Match(TokenKind kind)
        {
            if (Current.Kind != kind)
                return false;
            _position++;
            return true;
        }
        private void Error(BehaviorExpressionDiagnosticCode code, string message, int position) =>
            _diagnostics.Add(new(code, BehaviorExpressionDiagnosticSeverity.Error, message, position));

        private static bool IsReusableReferenceName(string value) =>
            string.Equals(value, "expressionRef", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "ref", StringComparison.OrdinalIgnoreCase);

        private static bool TryBinary(Token token, out OperationEnum operation, out int precedence)
        {
            operation = token.Text switch
            {
                "||" => OperationEnum.OP_OR,
                "&&" => OperationEnum.OP_AND,
                "|" => OperationEnum.OP_BITWISEOR,
                "&" => OperationEnum.OP_BITWISEAND,
                "==" => OperationEnum.OP_EQUALS,
                "!=" => OperationEnum.OP_NOTEQUAL,
                "<" => OperationEnum.OP_LESSTHAN,
                "<=" => OperationEnum.OP_LESSTHANEQUALTO,
                ">" => OperationEnum.OP_GREATERTHAN,
                ">=" => OperationEnum.OP_GREATERTHANEQUALTO,
                "<<" => OperationEnum.OP_BITSHIFTLEFT,
                ">>" => OperationEnum.OP_BITSHIFTRIGHT,
                "+" => OperationEnum.OP_ADD,
                "-" => OperationEnum.OP_SUBTRACT,
                "*" => OperationEnum.OP_MULTIPLY,
                "/" => OperationEnum.OP_DIVIDE,
                "%" => OperationEnum.OP_MODULUS,
                _ => default
            };
            precedence = operation switch
            {
                OperationEnum.OP_OR => 1,
                OperationEnum.OP_AND => 2,
                OperationEnum.OP_BITWISEOR => 3,
                OperationEnum.OP_BITWISEAND => 4,
                OperationEnum.OP_EQUALS or OperationEnum.OP_NOTEQUAL => 5,
                OperationEnum.OP_LESSTHAN or OperationEnum.OP_LESSTHANEQUALTO or
                OperationEnum.OP_GREATERTHAN or OperationEnum.OP_GREATERTHANEQUALTO => 6,
                OperationEnum.OP_BITSHIFTLEFT or OperationEnum.OP_BITSHIFTRIGHT => 7,
                OperationEnum.OP_ADD or OperationEnum.OP_SUBTRACT => 8,
                OperationEnum.OP_MULTIPLY or OperationEnum.OP_DIVIDE or OperationEnum.OP_MODULUS => 9,
                _ => -1
            };
            return token.Kind == TokenKind.Operator && precedence >= 0;
        }
    }

    private static List<Token> Lex(string source, List<BehaviorExpressionDiagnostic> diagnostics)
    {
        var tokens = new List<Token>();
        for (int index = 0; index < source.Length;)
        {
            char value = source[index];
            if (char.IsWhiteSpace(value)) { index++; continue; }
            if (char.IsLetter(value) || value == '_')
            {
                int start = index++;
                while (index < source.Length && (char.IsLetterOrDigit(source[index]) || source[index] == '_')) index++;
                tokens.Add(new(TokenKind.Identifier, source[start..index], start));
                continue;
            }
            if (char.IsDigit(value) || value == '.' && index + 1 < source.Length && char.IsDigit(source[index + 1]))
            {
                int start = index++;
                while (index < source.Length && char.IsDigit(source[index])) index++;
                bool floating = false;
                if (index < source.Length && source[index] == '.')
                {
                    floating = true; index++;
                    while (index < source.Length && char.IsDigit(source[index])) index++;
                }
                if (index < source.Length && source[index] is 'e' or 'E')
                {
                    floating = true; index++;
                    if (index < source.Length && source[index] is '+' or '-') index++;
                    while (index < source.Length && char.IsDigit(source[index])) index++;
                }
                tokens.Add(new(floating ? TokenKind.Float : TokenKind.Integer, source[start..index], start));
                continue;
            }
            if (value == '"')
            {
                int start = index++;
                var decoded = new StringBuilder();
                bool closed = false;
                while (index < source.Length)
                {
                    char current = source[index++];
                    if (current == '"') { closed = true; break; }
                    if (current != '\\' || index == source.Length) { decoded.Append(current); continue; }
                    decoded.Append(source[index++] switch { 'n' => '\n', 'r' => '\r', 't' => '\t', '"' => '"', '\\' => '\\', var other => other });
                }
                if (!closed)
                    diagnostics.Add(new(BehaviorExpressionDiagnosticCode.InvalidToken, BehaviorExpressionDiagnosticSeverity.Error, "Unterminated string literal.", start));
                tokens.Add(new(TokenKind.String, source[start..index], start, decoded.ToString()));
                continue;
            }
            if (value == '(') { tokens.Add(new(TokenKind.LeftParenthesis, "(", index++)); continue; }
            if (value == ')') { tokens.Add(new(TokenKind.RightParenthesis, ")", index++)); continue; }
            if (value == ',') { tokens.Add(new(TokenKind.Comma, ",", index++)); continue; }
            string? operation = index + 1 < source.Length ? source.Substring(index, 2) : null;
            if (operation is "||" or "&&" or "==" or "!=" or "<=" or ">=" or "<<" or ">>")
            {
                tokens.Add(new(TokenKind.Operator, operation, index)); index += 2; continue;
            }
            if (value is '+' or '-' or '*' or '/' or '%' or '!' or '~' or '<' or '>' or '&' or '|')
            {
                tokens.Add(new(TokenKind.Operator, value.ToString(), index++)); continue;
            }
            diagnostics.Add(new(BehaviorExpressionDiagnosticCode.InvalidToken, BehaviorExpressionDiagnosticSeverity.Error, $"Unexpected character '{value}'.", index));
            index++;
        }
        tokens.Add(new(TokenKind.End, string.Empty, source.Length));
        return tokens;
    }

    private enum TokenKind { End, Identifier, Integer, Float, String, LeftParenthesis, RightParenthesis, Comma, Operator }
    private sealed record Token(TokenKind Kind, string Text, int Position, string? Value = null);
}

/// <summary>Formats semantic expressions without exposing the native token stream.</summary>
public static class BehaviorExpressionFormatter
{
    public static BehaviorExpressionResult<string> Format(
        BehaviorExpression expression,
        BehaviorExpressionCatalog? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var diagnostics = new List<BehaviorExpressionDiagnostic>();
        string value = Format(expression, catalog ?? BehaviorExpressionCatalog.Default, diagnostics, 0);
        return new(value, diagnostics);
    }

    private static string Format(
        BehaviorExpression expression,
        BehaviorExpressionCatalog catalog,
        List<BehaviorExpressionDiagnostic> diagnostics,
        int parentPrecedence) => expression switch
    {
        BehaviorIntegerExpression value => value.Value.ToString(CultureInfo.InvariantCulture),
        BehaviorFloatExpression value => FormatFloat(value.Value),
        BehaviorStringExpression value => Quote(value.Value),
        BehaviorReusableExpressionReferenceExpression value => $"expressionRef({value.ReferenceId})",
        BehaviorStaticDvarExpression value => $"{catalog.Get(value.Operation).FormulaName}({FormatStaticDvarArgument(value.Dvar)})",
        BehaviorUnaryExpression value => FormatUnary(value, catalog, diagnostics, parentPrecedence),
        BehaviorBinaryExpression value => FormatBinary(value, catalog, diagnostics, parentPrecedence),
        BehaviorCallExpression value => $"{catalog.Get(value.Operation).FormulaName}({string.Join(", ", value.Arguments.Select(argument => Format(argument, catalog, diagnostics, 0)))})",
        BehaviorOpaqueExpression value => Opaque(value, diagnostics),
        _ => Opaque(new BehaviorOpaqueExpression("Unknown semantic node."), diagnostics)
    };

    private static string FormatUnary(BehaviorUnaryExpression value, BehaviorExpressionCatalog catalog, List<BehaviorExpressionDiagnostic> diagnostics, int parentPrecedence)
    {
        const int precedence = 10;
        string text = UnarySymbol(value.Operation) + Format(value.Operand, catalog, diagnostics, precedence);
        return precedence < parentPrecedence ? $"({text})" : text;
    }

    private static string FormatBinary(BehaviorBinaryExpression value, BehaviorExpressionCatalog catalog, List<BehaviorExpressionDiagnostic> diagnostics, int parentPrecedence)
    {
        int precedence = Precedence(value.Operation);
        string text = $"{Format(value.Left, catalog, diagnostics, precedence)} {BinarySymbol(value.Operation)} {Format(value.Right, catalog, diagnostics, precedence + 1)}";
        return precedence < parentPrecedence ? $"({text})" : text;
    }

    private static string Opaque(BehaviorOpaqueExpression value, List<BehaviorExpressionDiagnostic> diagnostics)
    {
        diagnostics.Add(new(BehaviorExpressionDiagnosticCode.UnsupportedOpaqueExpression, BehaviorExpressionDiagnosticSeverity.Error, value.Reason));
        return "/* unsupported expression */";
    }

    private static string Quote(string value) => $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal).Replace("\t", "\\t", StringComparison.Ordinal)}\"";

    private static string FormatFloat(float value)
    {
        var formatted = value.ToString("R", CultureInfo.InvariantCulture);
        return formatted.IndexOfAny(['.', 'E', 'e']) >= 0 ? formatted : $"{formatted}.0";
    }

    private static string FormatStaticDvarArgument(BehaviorStaticDvarReference dvar) =>
        dvar.Name is null
            ? dvar.Index.ToString(CultureInfo.InvariantCulture)
            : Quote(dvar.Name);

    private static string UnarySymbol(OperationEnum operation) => operation switch { OperationEnum.OP_NOT => "!", OperationEnum.OP_BITWISENOT => "~", OperationEnum.OP_SUBTRACT => "-", _ => $"{operation} " };
    private static string BinarySymbol(OperationEnum operation) => operation switch { OperationEnum.OP_OR => "||", OperationEnum.OP_AND => "&&", OperationEnum.OP_BITWISEOR => "|", OperationEnum.OP_BITWISEAND => "&", OperationEnum.OP_EQUALS => "==", OperationEnum.OP_NOTEQUAL => "!=", OperationEnum.OP_LESSTHAN => "<", OperationEnum.OP_LESSTHANEQUALTO => "<=", OperationEnum.OP_GREATERTHAN => ">", OperationEnum.OP_GREATERTHANEQUALTO => ">=", OperationEnum.OP_BITSHIFTLEFT => "<<", OperationEnum.OP_BITSHIFTRIGHT => ">>", OperationEnum.OP_ADD => "+", OperationEnum.OP_SUBTRACT => "-", OperationEnum.OP_MULTIPLY => "*", OperationEnum.OP_DIVIDE => "/", OperationEnum.OP_MODULUS => "%", _ => operation.ToString() };
    private static int Precedence(OperationEnum operation) => operation switch { OperationEnum.OP_OR => 1, OperationEnum.OP_AND => 2, OperationEnum.OP_BITWISEOR => 3, OperationEnum.OP_BITWISEAND => 4, OperationEnum.OP_EQUALS or OperationEnum.OP_NOTEQUAL => 5, OperationEnum.OP_LESSTHAN or OperationEnum.OP_LESSTHANEQUALTO or OperationEnum.OP_GREATERTHAN or OperationEnum.OP_GREATERTHANEQUALTO => 6, OperationEnum.OP_BITSHIFTLEFT or OperationEnum.OP_BITSHIFTRIGHT => 7, OperationEnum.OP_ADD or OperationEnum.OP_SUBTRACT => 8, OperationEnum.OP_MULTIPLY or OperationEnum.OP_DIVIDE or OperationEnum.OP_MODULUS => 9, _ => 0 };
}
