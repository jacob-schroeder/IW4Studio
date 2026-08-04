using IW4.Assets.Assets.Menu;
using IW4.FastFiles.Pointers;

namespace IW4.Studio.Documents.MenuEditing.Behavior.Expressions;

/// <summary>
/// Immutable authoring wrapper for one Statement. Imported statements retain a
/// private source reference and are emitted byte-for-byte at the statement
/// level while untouched. Changing <see cref="Expression"/> intentionally
/// rebuilds the token stream with canonical zero operator tails.
/// </summary>
public sealed class BehaviorExpressionStatement
{
    private readonly IReadOnlyList<BehaviorExpressionDiagnostic> _diagnostics;

    public BehaviorExpressionStatement(
        BehaviorExpression expression,
        BehaviorExpressionSupport? support = null)
        : this(expression, support ?? BehaviorExpressionSupport.Empty, null, false, [])
    {
    }

    internal BehaviorExpressionStatement(
        BehaviorExpression expression,
        BehaviorExpressionSupport support,
        Statement? source,
        bool canReuseSource,
        IEnumerable<BehaviorExpressionDiagnostic> diagnostics)
    {
        Expression = expression ?? throw new ArgumentNullException(nameof(expression));
        Support = support ?? throw new ArgumentNullException(nameof(support));
        Source = source;
        CanReuseSource = canReuseSource;
        _diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }

    public BehaviorExpression Expression { get; }
    public BehaviorExpressionSupport Support { get; }
    public IReadOnlyList<BehaviorExpressionDiagnostic> Diagnostics => _diagnostics;
    public bool IsImported => Source is not null;
    public bool CanReuseSource { get; }
    public bool IsOpaque => BehaviorExpressionValidation.ContainsOpaque(Expression);

    /// <summary>
    /// Returns an immutable replacement. A changed expression cannot reuse the
    /// old statement because source entries and operator-tail cells may differ.
    /// </summary>
    public BehaviorExpressionStatement WithExpression(BehaviorExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return ReferenceEquals(Expression, expression)
            ? this
            : new BehaviorExpressionStatement(expression, Support, Source, false, []);
    }

    internal Statement? Source { get; }
}

public sealed class BehaviorExpressionImportResult
{
    private readonly IReadOnlyList<BehaviorExpressionDiagnostic> _diagnostics;

    internal BehaviorExpressionImportResult(
        BehaviorExpressionStatement statement,
        IEnumerable<BehaviorExpressionDiagnostic> diagnostics)
    {
        Statement = statement;
        _diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }

    public BehaviorExpressionStatement Statement { get; }
    public IReadOnlyList<BehaviorExpressionDiagnostic> Diagnostics => _diagnostics;
    public bool HasErrors => _diagnostics.Any(value => value.Severity == BehaviorExpressionDiagnosticSeverity.Error);
}

/// <summary>
/// Imports native Statements into a semantic tree and lowers semantic trees
/// back to Statements. Desktop consumes only the semantic wrapper/results.
/// </summary>
public static class BehaviorExpressionStatementCodec
{
    public static BehaviorExpressionImportResult Import(
        Statement statement,
        ExpressionSupportingData? fallbackSupportingData = null,
        BehaviorExpressionCatalog? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(statement);
        var diagnostics = new List<BehaviorExpressionDiagnostic>();
        BehaviorExpressionSupport support = BehaviorExpressionSupport.Import(
            statement.SupportingDataValue ?? fallbackSupportingData);
        var compiler = new StatementImporter(
            statement.LoadedEntries,
            support,
            catalog ?? BehaviorExpressionCatalog.Default,
            diagnostics);
        BehaviorExpression expression = compiler.Compile();
        if (statement.NumEntries != statement.LoadedEntries.Count)
        {
            diagnostics.Add(new(
                BehaviorExpressionDiagnosticCode.InvalidStatementShape,
                BehaviorExpressionDiagnosticSeverity.Warning,
                $"Statement declares {statement.NumEntries} entries but loaded {statement.LoadedEntries.Count}."));
        }
        var result = new BehaviorExpressionStatement(
            expression,
            support,
            statement,
            canReuseSource: true,
            diagnostics);
        return new BehaviorExpressionImportResult(result, diagnostics);
    }

    public static BehaviorExpressionResult<Statement> Lower(
        BehaviorExpressionStatement statement,
        BehaviorExpressionCatalog? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(statement);
        if (statement.CanReuseSource && statement.Source is { } source)
            return new(source, statement.Diagnostics);

        var diagnostics = new List<BehaviorExpressionDiagnostic>(statement.Diagnostics);
        BehaviorExpressionValidation.Validate(statement.Expression, statement.Support, catalog ?? BehaviorExpressionCatalog.Default, diagnostics);
        if (diagnostics.Any(value => value.Severity == BehaviorExpressionDiagnosticSeverity.Error))
            return new(null, diagnostics);

        var entries = new List<ExpressionEntry>();
        new StatementLowerer(statement.Support, diagnostics).Append(statement.Expression, entries);
        if (diagnostics.Any(value => value.Severity == BehaviorExpressionDiagnosticSeverity.Error))
            return new(null, diagnostics);

        return new(new Statement
        {
            NumEntries = entries.Count,
            Entries = entries.Count == 0
                ? default
                : new XPointer<ExpressionEntry[]>(-1),
            LoadedEntries = entries.ToArray(),
            SupportingData = statement.Support.Source is null
                ? default
                : new XPointer<ExpressionSupportingData>(-1),
            SupportingDataValue = statement.Support.Source,
            LastExecuteTime = 0,
            LastResult = new Operand
            {
                DataType = ExpDataType.VAL_INT,
                Value = new IntOperandValue(0)
            }
        }, diagnostics);
    }

    private sealed class StatementImporter
    {
        private const int MaximumFunctionArguments = 10;
        private const int MaximumStackDepth = 60;

        private readonly IReadOnlyList<ExpressionEntry> _entries;
        private readonly BehaviorExpressionSupport _support;
        private readonly BehaviorExpressionCatalog _catalog;
        private readonly List<BehaviorExpressionDiagnostic> _diagnostics;
        private readonly List<List<BehaviorExpression>> _operandLists = [];
        private readonly List<OperatorFrame> _operators = [];
        private string? _failure;

        public StatementImporter(
            IReadOnlyList<ExpressionEntry> entries,
            BehaviorExpressionSupport support,
            BehaviorExpressionCatalog catalog,
            List<BehaviorExpressionDiagnostic> diagnostics)
        {
            _entries = entries;
            _support = support;
            _catalog = catalog;
            _diagnostics = diagnostics;
        }

        public BehaviorExpression Compile()
        {
            if (_entries.Count == 0)
                return Fail("The statement contains no expression entries.");

            for (int index = 0; index < _entries.Count && _failure is null; index++)
            {
                ExpressionEntry entry = _entries[index];
                if (entry.IsOperand)
                {
                    BehaviorExpression operand = CompileOperand(entry);
                    if (operand is BehaviorOpaqueExpression)
                        return operand;
                    PushSingle(operand);
                    continue;
                }
                if (!entry.IsOperator || !_catalog.TryGet(entry.Operation, out _))
                    return Fail($"Expression entry {index} has an unknown representation or opcode {entry.OperationCode}.");

                if (entry.Operation != OperationEnum.OP_LEFTPAREN)
                    RunHigherPriorityOperators(entry.Operation);
                if (_failure is null && _operators.Count == MaximumStackDepth)
                    Fail("Expression operators are nested beyond the engine stack limit.");
                else if (_failure is null)
                    _operators.Add(new OperatorFrame(
                        entry.Operation,
                        IsFunction(entry.Operation) ? _operandLists.Count : -1));
            }

            while (_operators.Count > 0 && _failure is null)
                RunOperator();

            if (_failure is not null)
                return Fail(_failure);
            if (_operandLists.Count != 1 || _operandLists[0].Count != 1)
                return Fail("The statement did not reduce to one expression.");
            return _operandLists[0][0];
        }

        private BehaviorExpression CompileOperand(ExpressionEntry entry) => entry.Operand.DataType switch
        {
            ExpDataType.VAL_INT when entry.Operand.Value is IntOperandValue value => new BehaviorIntegerExpression(value.Value),
            ExpDataType.VAL_FLOAT when entry.Operand.Value is FloatOperandValue value => new BehaviorFloatExpression(value.Value),
            ExpDataType.VAL_STRING => new BehaviorStringExpression(entry.StringValue ?? string.Empty),
            ExpDataType.VAL_FUNCTION when entry.FunctionStatement is { } statement && _support.TryGetReusableExpression(statement, out BehaviorReusableExpressionId id) => new BehaviorReusableExpressionReferenceExpression(id),
            ExpDataType.VAL_FUNCTION => Fail("A VAL_FUNCTION operand does not resolve to a reusable expression in the support table."),
            _ => Fail($"Unsupported operand discriminator '{entry.Operand.DataType}'.")
        };

        private void RunHigherPriorityOperators(OperationEnum incoming)
        {
            while (_operators.Count > 0 && _failure is null)
            {
                OperationEnum top = _operators[^1].Operation;
                bool stop =
                    (NativePrecedence(top) >= NativePrecedence(incoming) ||
                     NativePrecedence(top) == 5 && incoming != OperationEnum.OP_RIGHTPAREN) &&
                    (IsAssociative(incoming) || top != incoming);
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
                    Fail("The expression contains OP_NOOP.");
                    return;
                case OperationEnum.OP_RIGHTPAREN:
                    CloseRightParenthesis();
                    return;
                case OperationEnum.OP_LEFTPAREN:
                    return;
                case OperationEnum.OP_COMMA:
                    CompileComma();
                    return;
                case OperationEnum.OP_SUBTRACT:
                    CompileSubtract();
                    return;
                case OperationEnum.OP_NOT:
                case OperationEnum.OP_BITWISENOT:
                    CompileUnary(frame.Operation);
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
                default:
                    if (IsFunction(frame.Operation))
                        CompileFunction(frame);
                    else
                        Fail($"Unsupported expression operation '{frame.Operation}'.");
                    return;
            }
        }

        private void CloseRightParenthesis()
        {
            while (_operators.Count > 0 && _failure is null)
            {
                OperationEnum paired = _operators[^1].Operation;
                RunOperator();
                if (paired == OperationEnum.OP_LEFTPAREN || IsFunction(paired))
                    return;
            }
            Fail("A right parenthesis has no matching group or function.");
        }

        private void CompileBinary(OperationEnum operation)
        {
            if (!TryPopSingle(out BehaviorExpression right) || !TryPopSingle(out BehaviorExpression left))
            {
                Fail($"Binary operator '{operation}' does not have two scalar operands.");
                return;
            }
            PushSingle(new BehaviorBinaryExpression(operation, left, right));
        }

        private void CompileSubtract()
        {
            if (!TryPopSingle(out BehaviorExpression right))
            {
                Fail("Subtraction or negation has no scalar operand.");
                return;
            }
            if (_operandLists.Count == 0)
            {
                PushSingle(new BehaviorUnaryExpression(OperationEnum.OP_SUBTRACT, right));
                return;
            }
            if (!TryPopSingle(out BehaviorExpression left))
            {
                Fail("Subtraction has an invalid left operand.");
                return;
            }
            PushSingle(new BehaviorBinaryExpression(OperationEnum.OP_SUBTRACT, left, right));
        }

        private void CompileUnary(OperationEnum operation)
        {
            if (!TryPopSingle(out BehaviorExpression operand))
            {
                Fail($"Unary operator '{operation}' has no scalar operand.");
                return;
            }
            PushSingle(new BehaviorUnaryExpression(operation, operand));
        }

        private void CompileComma()
        {
            if (!TryPopList(out List<BehaviorExpression> right) || !TryPopList(out List<BehaviorExpression> left))
            {
                Fail("Comma does not have two operand lists to combine.");
                return;
            }
            if (left.Count + right.Count > MaximumFunctionArguments)
            {
                Fail("A function argument list exceeds the engine limit of 10 values.");
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
                Fail($"Function '{frame.Operation}' has an invalid operand-list boundary.");
                return;
            }
            BehaviorExpression[] arguments = availableLists == 0
                ? []
                : TakeArguments();
            if (BehaviorExpressionCatalog.IsStaticDvar(frame.Operation))
            {
                if (arguments.Length != 1 || arguments[0] is not BehaviorIntegerExpression index ||
                    !_support.TryGetStaticDvar(index.Value, out BehaviorStaticDvarReference dvar))
                {
                    Fail($"Static dvar function '{frame.Operation}' has no valid support-table index.");
                    return;
                }
                PushSingle(new BehaviorStaticDvarExpression(frame.Operation, dvar));
                return;
            }
            PushSingle(new BehaviorCallExpression(frame.Operation, arguments));

            BehaviorExpression[] TakeArguments()
            {
                List<BehaviorExpression> values = _operandLists[^1];
                _operandLists.RemoveAt(_operandLists.Count - 1);
                return values.ToArray();
            }
        }

        private bool TryPopSingle(out BehaviorExpression expression)
        {
            expression = null!;
            if (!TryPopList(out List<BehaviorExpression> values) || values.Count != 1)
                return false;
            expression = values[0];
            return true;
        }

        private bool TryPopList(out List<BehaviorExpression> values)
        {
            if (_operandLists.Count == 0)
            {
                values = null!;
                return false;
            }
            values = _operandLists[^1];
            _operandLists.RemoveAt(_operandLists.Count - 1);
            return true;
        }

        private void PushSingle(BehaviorExpression expression)
        {
            if (_operandLists.Count == MaximumStackDepth)
            {
                Fail("Expression contains more operands than the engine stack accepts.");
                return;
            }

            _operandLists.Add([expression]);
        }

        private BehaviorOpaqueExpression Fail(string message)
        {
            if (_failure is null)
            {
                _failure = message;
                _diagnostics.Add(new(
                    BehaviorExpressionDiagnosticCode.UnsupportedRawStatement,
                    BehaviorExpressionDiagnosticSeverity.Warning,
                    message));
            }

            return new BehaviorOpaqueExpression(_failure);
        }

        private static bool IsFunction(OperationEnum operation) =>
            (int)operation >= (int)OperationEnum.OP_STATICDVARINT &&
            (int)operation <= (int)OperationEnum.OP_DOWEHAVEMAPPACK;

        private static bool IsAssociative(OperationEnum operation) =>
            (int)operation < (int)OperationEnum.OP_DIVIDE ||
            (int)operation > (int)OperationEnum.OP_MODULUS &&
            operation != OperationEnum.OP_SUBTRACT;

        private static int NativePrecedence(OperationEnum operation) => operation switch
        {
            OperationEnum.OP_NOOP => int.MaxValue,
            OperationEnum.OP_RIGHTPAREN => 0,
            OperationEnum.OP_MULTIPLY or OperationEnum.OP_DIVIDE or OperationEnum.OP_MODULUS => 11,
            OperationEnum.OP_ADD or OperationEnum.OP_SUBTRACT => 13,
            OperationEnum.OP_NOT => 9,
            OperationEnum.OP_LESSTHAN or OperationEnum.OP_LESSTHANEQUALTO or OperationEnum.OP_GREATERTHAN or OperationEnum.OP_GREATERTHANEQUALTO => 15,
            OperationEnum.OP_EQUALS or OperationEnum.OP_NOTEQUAL => 16,
            OperationEnum.OP_AND or OperationEnum.OP_OR => 25,
            OperationEnum.OP_LEFTPAREN => 99,
            OperationEnum.OP_COMMA => 80,
            OperationEnum.OP_BITWISEAND => 17,
            OperationEnum.OP_BITWISEOR => 18,
            OperationEnum.OP_BITWISENOT => 9,
            OperationEnum.OP_BITSHIFTLEFT or OperationEnum.OP_BITSHIFTRIGHT => 14,
            _ => 5
        };

        private readonly record struct OperatorFrame(OperationEnum Operation, int OperandDepth);
    }

    private sealed class StatementLowerer(
        BehaviorExpressionSupport support,
        List<BehaviorExpressionDiagnostic> diagnostics)
    {
        private readonly BehaviorExpressionSupport _support = support;
        private readonly List<BehaviorExpressionDiagnostic> _diagnostics = diagnostics;

        public void Append(BehaviorExpression expression, List<ExpressionEntry> entries)
        {
            switch (expression)
            {
                case BehaviorIntegerExpression value:
                    entries.Add(Operand(new Operand { DataType = ExpDataType.VAL_INT, Value = new IntOperandValue(value.Value) }));
                    return;
                case BehaviorFloatExpression value:
                    entries.Add(Operand(new Operand { DataType = ExpDataType.VAL_FLOAT, Value = new FloatOperandValue(value.Value, BitConverter.SingleToInt32Bits(value.Value)) }));
                    return;
                case BehaviorStringExpression value:
                    entries.Add(new ExpressionEntry
                    {
                        Kind = ExpressionEntryKind.Operand,
                        Operand = new Operand { DataType = ExpDataType.VAL_STRING, Value = new StringOperandValue(default) },
                        StringValue = value.Value
                    });
                    return;
                case BehaviorReusableExpressionReferenceExpression value:
                    AppendReusableExpression(value, entries);
                    return;
                case BehaviorStaticDvarExpression value:
                    entries.Add(Operator(value.Operation));
                    entries.Add(Operand(new Operand { DataType = ExpDataType.VAL_INT, Value = new IntOperandValue(value.Dvar.Index) }));
                    entries.Add(Operator(OperationEnum.OP_RIGHTPAREN));
                    return;
                case BehaviorUnaryExpression value:
                    entries.Add(Operator(OperationEnum.OP_LEFTPAREN));
                    entries.Add(Operator(value.Operation));
                    Append(value.Operand, entries);
                    entries.Add(Operator(OperationEnum.OP_RIGHTPAREN));
                    return;
                case BehaviorBinaryExpression value:
                    entries.Add(Operator(OperationEnum.OP_LEFTPAREN));
                    Append(value.Left, entries);
                    entries.Add(Operator(value.Operation));
                    Append(value.Right, entries);
                    entries.Add(Operator(OperationEnum.OP_RIGHTPAREN));
                    return;
                case BehaviorCallExpression value:
                    entries.Add(Operator(value.Operation));
                    for (int index = 0; index < value.Arguments.Count; index++)
                    {
                        Append(value.Arguments[index], entries);
                        if (index + 1 < value.Arguments.Count)
                            entries.Add(Operator(OperationEnum.OP_COMMA));
                    }
                    entries.Add(Operator(OperationEnum.OP_RIGHTPAREN));
                    return;
                case BehaviorOpaqueExpression value:
                    Error(BehaviorExpressionDiagnosticCode.UnsupportedOpaqueExpression, value.Reason);
                    return;
                default:
                    Error(BehaviorExpressionDiagnosticCode.UnsupportedOpaqueExpression, "Unknown semantic expression node.");
                    return;
            }
        }

        private void AppendReusableExpression(
            BehaviorReusableExpressionReferenceExpression value,
            List<ExpressionEntry> entries)
        {
            if (!_support.TryResolveReusableExpression(value.ReferenceId, out Statement? statement) || statement is null)
            {
                Error(BehaviorExpressionDiagnosticCode.InvalidReusableExpressionReference, $"Reusable expression {value.ReferenceId} cannot be resolved.");
                return;
            }
            entries.Add(new ExpressionEntry
            {
                Kind = ExpressionEntryKind.Operand,
                Operand = new Operand { DataType = ExpDataType.VAL_FUNCTION, Value = new FunctionOperandValue(default) },
                FunctionStatement = statement
            });
        }

        private void Error(BehaviorExpressionDiagnosticCode code, string message) =>
            _diagnostics.Add(new(code, BehaviorExpressionDiagnosticSeverity.Error, message));

        private static ExpressionEntry Operator(OperationEnum operation) => new()
        {
            Kind = ExpressionEntryKind.Operator,
            OperationCode = (int)operation,
            // New/rebuilt operators have no trustworthy imported tail. Imported
            // statements bypass this lowerer while untouched, preserving tails.
            OperatorTail = 0
        };

        private static ExpressionEntry Operand(Operand operand) => new()
        {
            Kind = ExpressionEntryKind.Operand,
            Operand = operand
        };
    }
}

/// <summary>Validation shared by formula and lowering callers.</summary>
public static class BehaviorExpressionValidation
{
    public static void Validate(
        BehaviorExpression expression,
        BehaviorExpressionSupport support,
        BehaviorExpressionCatalog catalog,
        ICollection<BehaviorExpressionDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(support);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(diagnostics);
        Visit(expression, support, catalog, diagnostics, depth: 0);
    }

    public static bool ContainsOpaque(BehaviorExpression expression) => expression switch
    {
        BehaviorOpaqueExpression => true,
        BehaviorUnaryExpression value => ContainsOpaque(value.Operand),
        BehaviorBinaryExpression value => ContainsOpaque(value.Left) || ContainsOpaque(value.Right),
        BehaviorCallExpression value => value.Arguments.Any(ContainsOpaque),
        _ => false
    };

    private static void Visit(
        BehaviorExpression expression,
        BehaviorExpressionSupport support,
        BehaviorExpressionCatalog catalog,
        ICollection<BehaviorExpressionDiagnostic> diagnostics,
        int depth)
    {
        if (depth >= 60)
        {
            diagnostics.Add(new(BehaviorExpressionDiagnosticCode.InvalidStatementShape, BehaviorExpressionDiagnosticSeverity.Error, "The expression exceeds the engine's 60-level stack limit."));
            return;
        }
        switch (expression)
        {
            case BehaviorOpaqueExpression value:
                diagnostics.Add(new(BehaviorExpressionDiagnosticCode.UnsupportedOpaqueExpression, BehaviorExpressionDiagnosticSeverity.Error, value.Reason));
                return;
            case BehaviorReusableExpressionReferenceExpression value when !support.Contains(value.ReferenceId):
                diagnostics.Add(new(BehaviorExpressionDiagnosticCode.InvalidReusableExpressionReference, BehaviorExpressionDiagnosticSeverity.Error, $"Reusable expression {value.ReferenceId} is not in the support table."));
                return;
            case BehaviorStaticDvarExpression value:
                if (!BehaviorExpressionCatalog.IsStaticDvar(value.Operation) ||
                    !support.TryGetStaticDvar(
                        value.Dvar.Index,
                        out BehaviorStaticDvarReference resolved) ||
                    resolved != value.Dvar)
                    diagnostics.Add(new(BehaviorExpressionDiagnosticCode.InvalidStaticDvarReference, BehaviorExpressionDiagnosticSeverity.Error, "The static-dvar reference is invalid for this support table."));
                return;
            case BehaviorUnaryExpression value:
                // Native OP_SUBTRACT is context-sensitive: it is binary when
                // a left operand is present and unary negation otherwise.
                if (value.Operation != OperationEnum.OP_SUBTRACT)
                {
                    RequireCategory(
                        value.Operation,
                        BehaviorExpressionOperationCategory.Unary,
                        catalog,
                        diagnostics);
                }
                Visit(value.Operand, support, catalog, diagnostics, depth + 1);
                return;
            case BehaviorBinaryExpression value:
                RequireCategory(value.Operation, BehaviorExpressionOperationCategory.Binary, catalog, diagnostics);
                Visit(value.Left, support, catalog, diagnostics, depth + 1);
                Visit(value.Right, support, catalog, diagnostics, depth + 1);
                return;
            case BehaviorCallExpression value:
                if (!catalog.TryGet(value.Operation, out BehaviorExpressionOperationMetadata metadata) || metadata.Category != BehaviorExpressionOperationCategory.Function)
                    diagnostics.Add(new(BehaviorExpressionDiagnosticCode.UnknownOperation, BehaviorExpressionDiagnosticSeverity.Error, $"'{value.Operation}' is not a callable expression operation."));
                else if (value.Arguments.Count > 10 || !metadata.SupportsArgumentCount(value.Arguments.Count))
                    diagnostics.Add(new(BehaviorExpressionDiagnosticCode.InvalidArity, BehaviorExpressionDiagnosticSeverity.Error, $"'{metadata.FormulaName}' does not support {value.Arguments.Count} argument(s)."));
                foreach (BehaviorExpression argument in value.Arguments)
                    Visit(argument, support, catalog, diagnostics, depth + 1);
                return;
        }
    }

    private static void RequireCategory(
        OperationEnum operation,
        BehaviorExpressionOperationCategory category,
        BehaviorExpressionCatalog catalog,
        ICollection<BehaviorExpressionDiagnostic> diagnostics)
    {
        if (!catalog.TryGet(operation, out BehaviorExpressionOperationMetadata metadata) || metadata.Category != category)
            diagnostics.Add(new(BehaviorExpressionDiagnosticCode.UnknownOperation, BehaviorExpressionDiagnosticSeverity.Error, $"'{operation}' cannot be used as a {category.ToString().ToLowerInvariant()} operation."));
    }
}
