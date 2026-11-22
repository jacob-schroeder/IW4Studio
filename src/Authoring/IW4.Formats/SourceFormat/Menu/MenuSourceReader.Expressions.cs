using System.Globalization;
using IW4.Game.Assets.Menu;
using IW4.Game.Pointers;

namespace IW4.Formats.SourceFormat.Menu;

internal sealed partial class MenuSourceReader
{
    private static readonly IReadOnlyDictionary<string, OperationEnum> ExpressionOperations =
        Enum.GetValues<OperationEnum>()
            .Where(operation => operation is not (OperationEnum.OP_NOOP or
                OperationEnum.OP_STATICDVARINT or OperationEnum.OP_STATICDVARBOOL or
                OperationEnum.OP_STATICDVARFLOAT or OperationEnum.OP_STATICDVARSTRING))
            .ToDictionary(MenuExpressionOperationNames.Get, StringComparer.OrdinalIgnoreCase);

    private sealed class FunctionScope(FunctionScope? parent)
    {
        public FunctionScope? Parent { get; } = parent;
        public Dictionary<string, Statement> Definitions { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private FunctionScope _scope = new(null);
    private readonly List<Statement> _functions = [];
    private readonly List<(ExpressionEntry Entry, string Name, FunctionScope Scope, string? Path, int Line)>
        _functionCalls = [];
    private readonly List<Statement> _statements = [];
    private readonly List<string> _staticDvarNames = [];

    private void ReadFunction()
    {
        Expect("{");
        Expect("name");
        string name = Text();
        Expect("value");
        Statement body = ReadStatementProperty();
        Expect("}");
        if (!_scope.Definitions.TryAdd(name, body))
            throw Error($"Duplicate menu function '{name}'.");
        _functions.Add(body);
    }

    private Statement ReadStatementProperty(bool when = false)
    {
        if (when)
            Expect("when");
        return ReadStatement(when);
    }

    private Statement ReadStatement(bool parenthesized, bool consumeSemicolon = true)
    {
        if (parenthesized)
            Expect("(");
        var entries = new List<ExpressionEntry>();
        ReadExpression(entries, depth: 0);
        if (parenthesized)
        {
            Expect(")");
            if (consumeSemicolon)
                Take(";");
        }
        else
        {
            Expect(";");
        }

        var statement = new Statement
        {
            NumEntries = entries.Count,
            Entries = new(-1),
            LoadedEntries = entries.ToArray()
        };
        _statements.Add(statement);
        return statement;
    }

    private void ReadExpression(List<ExpressionEntry> entries, int depth)
    {
        if (depth >= MaximumNesting)
            throw Error("Menu expression nesting is too deep.");

        ReadOperand(entries, depth);
        while (Current.Kind == TokenKind.Symbol &&
               ExpressionOperations.TryGetValue(Current.Value, out OperationEnum operation) &&
               IsBinary(operation))
        {
            _position++;
            entries.Add(Operator(operation));
            ReadOperand(entries, depth);
        }
    }

    private void ReadOperand(List<ExpressionEntry> entries, int depth)
    {
        while (At("!") || At("~"))
            entries.Add(Operator(ExpressionOperations[_tokens[_position++].Value]));

        if (Take("("))
        {
            entries.Add(Operator(OperationEnum.OP_LEFTPAREN));
            ReadExpression(entries, depth + 1);
            Expect(")");
            entries.Add(Operator(OperationEnum.OP_RIGHTPAREN));
            return;
        }
        if (Current.Kind == TokenKind.Quoted)
        {
            entries.Add(new ExpressionEntry
            {
                Kind = ExpressionEntryKind.Operand,
                Operand = new Operand { DataType = ExpDataType.VAL_STRING, Value = new StringOperandValue(new(-1)) },
                StringValue = Text()
            });
            return;
        }
        if (AtNumber)
        {
            string value = NumberToken();
            if (TryInteger(value, out int integer))
                entries.Add(Operand(ExpDataType.VAL_INT, new IntOperandValue(integer)));
            else if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number) &&
                     float.IsFinite(number))
                entries.Add(Operand(ExpDataType.VAL_FLOAT,
                    new FloatOperandValue(number, BitConverter.SingleToInt32Bits(number))));
            else
                throw Error($"Invalid expression number '{value}'.");
            return;
        }

        int line = Current.Line;
        string name = Word();
        Expect("(");
        if (!ExpressionOperations.TryGetValue(name, out OperationEnum operation) ||
            operation < OperationEnum.OP_STATICDVARINT)
        {
            Expect(")");
            ExpressionEntry function = Operand(ExpDataType.VAL_FUNCTION, new FunctionOperandValue(new(-1)));
            _functionCalls.Add((function, name, _scope, _sourcePath, line));
            entries.Add(function);
            return;
        }

        if (TryStaticDvar(operation, out OperationEnum staticOperation) &&
            Current.Kind == TokenKind.Atom && !AtNumber &&
            Peek(1) is { Kind: TokenKind.Symbol, Value: ")" })
        {
            string dvar = Word();
            if (dvar.StartsWith('#'))
                throw Error($"Unresolved static dvar '{dvar}' in menu source.");
            int index = _staticDvarNames.FindIndex(value => value.Equals(dvar, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                index = _staticDvarNames.Count;
                _staticDvarNames.Add(dvar);
            }
            entries.Add(Operator(staticOperation));
            entries.Add(Operand(ExpDataType.VAL_INT, new IntOperandValue(index)));
        }
        else
        {
            entries.Add(Operator(operation));
            if (!At(")"))
            {
                ReadExpression(entries, depth + 1);
                while (Take(","))
                {
                    entries.Add(Operator(OperationEnum.OP_COMMA));
                    ReadExpression(entries, depth + 1);
                }
            }
        }
        Expect(")");
        entries.Add(Operator(OperationEnum.OP_RIGHTPAREN));
    }

    private void BindExpressions(IReadOnlyList<MenuDefAsset> menus)
    {
        foreach (var call in _functionCalls)
        {
            Statement? function = null;
            for (FunctionScope? scope = call.Scope; scope is not null; scope = scope.Parent)
            {
                if (scope.Definitions.TryGetValue(call.Name, out function))
                    break;
            }
            if (function is null)
                throw AtSource(call.Path, call.Line, $"Unknown menu function '{call.Name}'.");
            call.Entry.FunctionStatement = function;
        }
        var activeFunctions = new HashSet<Statement>(ReferenceEqualityComparer.Instance);
        var completeFunctions = new HashSet<Statement>(ReferenceEqualityComparer.Instance);
        foreach (Statement function in _functions)
            ValidateFunction(function, activeFunctions, completeFunctions);

        if (_functions.Count == 0 && _staticDvarNames.Count == 0)
            return;
        var supportingData = new ExpressionSupportingData
        {
            UiFunctions = new UIFunctionList
            {
                TotalFunctions = _functions.Count,
                Functions = _functions.Count == 0 ? default : new(-1),
                LoadedFunctions = _functions.Select((value, index) =>
                    new StatementReference(index, new(-1), value)).ToArray()
            },
            StaticDvarList = new StaticDvarList
            {
                NumStaticDvars = _staticDvarNames.Count,
                StaticDvars = _staticDvarNames.Count == 0 ? default : new(-1),
                LoadedStaticDvars = _staticDvarNames.Select((name, index) =>
                    new StaticDvarReference(index, new(-1), new StaticDvar
                    {
                        DvarName = new(-1),
                        DvarNameString = name
                    })).ToArray()
            }
        };
        foreach (Statement statement in _statements)
            statement.SupportingDataValue = supportingData;
        foreach (MenuDefAsset menu in menus)
            menu.ExpressionDataValue = supportingData;
    }

    private void ValidateFunction(Statement statement, HashSet<Statement> active, HashSet<Statement> complete)
    {
        if (complete.Contains(statement))
            return;
        if (active.Count >= MaximumNesting || !active.Add(statement))
            throw Error("Recursive or excessively nested menu function calls.");
        foreach (ExpressionEntry entry in statement.LoadedEntries)
            if (entry.FunctionStatement is { } child)
                ValidateFunction(child, active, complete);
        active.Remove(statement);
        complete.Add(statement);
    }

    private static bool TryStaticDvar(OperationEnum operation, out OperationEnum staticOperation)
    {
        staticOperation = operation switch
        {
            OperationEnum.OP_DVARINT => OperationEnum.OP_STATICDVARINT,
            OperationEnum.OP_DVARFLOAT => OperationEnum.OP_STATICDVARFLOAT,
            OperationEnum.OP_DVARBOOL => OperationEnum.OP_STATICDVARBOOL,
            OperationEnum.OP_DVARSTRING => OperationEnum.OP_STATICDVARSTRING,
            _ => OperationEnum.OP_NOOP
        };
        return staticOperation != OperationEnum.OP_NOOP;
    }

    private static bool IsBinary(OperationEnum operation) => operation is
        >= OperationEnum.OP_MULTIPLY and <= OperationEnum.OP_OR and not OperationEnum.OP_NOT or
        >= OperationEnum.OP_BITWISEAND and <= OperationEnum.OP_BITSHIFTRIGHT and not OperationEnum.OP_BITWISENOT;

    private static ExpressionEntry Operator(OperationEnum operation) =>
        new() { Kind = ExpressionEntryKind.Operator, OperationCode = (int)operation };

    private static ExpressionEntry Operand(ExpDataType type, OperandValue value) =>
        new() { Kind = ExpressionEntryKind.Operand, Operand = new Operand { DataType = type, Value = value } };
}
