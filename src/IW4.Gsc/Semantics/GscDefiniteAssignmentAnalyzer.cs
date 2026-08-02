using System.Globalization;
using IW4.Gsc.Syntax;

namespace IW4.Gsc.Semantics;

internal sealed class GscDefiniteAssignmentAnalyzer
{
    private readonly GscSourceText _source;
    private readonly GscSemanticModel _model;
    private readonly CancellationToken _cancellationToken;
    private readonly List<GscDiagnostic> _diagnostics = [];
    private int _operations;

    private GscDefiniteAssignmentAnalyzer(
        GscSourceText source,
        GscSemanticModel model,
        CancellationToken cancellationToken)
    {
        _source = source;
        _model = model;
        _cancellationToken = cancellationToken;
    }

    internal static IReadOnlyList<GscDiagnostic> Analyze(
        GscSourceText source,
        GscSemanticModel model,
        CancellationToken cancellationToken)
    {
        var analyzer = new GscDefiniteAssignmentAnalyzer(
            source,
            model,
            cancellationToken);
        return analyzer.Analyze();
    }

    private IReadOnlyList<GscDiagnostic> Analyze()
    {
        foreach (GscBoundFunction function in _model.Functions)
        {
            ObserveCancellation();
            var assigned = new HashSet<GscSymbol>(
                function.Parameters.Where(
                    symbol => symbol.Kind == GscSymbolKind.Parameter));
            _ = AnalyzeStatementList(
                GscSemanticSyntax.Node(function.Syntax.Children[5]),
                assigned);
        }

        return Array.AsReadOnly(_diagnostics.ToArray());
    }

    private ControlFlowOutcome AnalyzeStatementList(
        GscSyntaxNode list,
        HashSet<GscSymbol> assigned)
    {
        ControlFlowOutcome outcome = ControlFlowOutcome.Fallthrough(assigned);
        foreach (GscSyntaxNode item in GscSemanticSyntax.EnumerateStatementList(list))
        {
            outcome = outcome.Then(
                next => AnalyzeBlockItem(item, next));
        }

        return outcome;
    }

    private ControlFlowOutcome AnalyzeBlockItem(
        GscSyntaxNode item,
        HashSet<GscSymbol> assigned)
    {
        ObserveCancellation();
        return item.Production switch
        {
            GscProduction.EmptyBlockItem or
            GscProduction.CaseLabel or
            GscProduction.DefaultLabel =>
                ControlFlowOutcome.Fallthrough(assigned),
            GscProduction.BlockItemStatement =>
                AnalyzeStatement(GscSemanticSyntax.Node(item.Children[0]), assigned),
            _ => throw new InvalidOperationException(
                $"Production {item.Production} is not a block item.")
        };
    }

    private ControlFlowOutcome AnalyzeStatement(
        GscSyntaxNode statement,
        HashSet<GscSymbol> assigned)
    {
        ObserveCancellation();
        switch (statement.Production)
        {
            case GscProduction.TerminatedStatement:
                return AnalyzeStatementCore(
                    GscSemanticSyntax.Node(statement.Children[0]),
                    assigned);

            case GscProduction.BlockStatement:
                return AnalyzeStatementList(
                    GscSemanticSyntax.Node(statement.Children[1]),
                    assigned);

            case GscProduction.IfStatement:
                return AnalyzeIf(statement, assigned, hasElse: false);

            case GscProduction.IfElseStatement:
                return AnalyzeIf(statement, assigned, hasElse: true);

            case GscProduction.WhileStatement:
                return AnalyzeWhile(statement, assigned);

            case GscProduction.ForStatement:
                return AnalyzeFor(statement, assigned);

            case GscProduction.KeyValueForeachStatement:
                return AnalyzeForeach(
                    statement,
                    assigned,
                    targetChildIndexes: [2, 4],
                    expressionChildIndex: 6,
                    bodyChildIndex: 8);

            case GscProduction.ValueForeachStatement:
                return AnalyzeForeach(
                    statement,
                    assigned,
                    targetChildIndexes: [2],
                    expressionChildIndex: 4,
                    bodyChildIndex: 6);

            case GscProduction.SwitchStatement:
                return AnalyzeSwitch(statement, assigned);

            case GscProduction.DeveloperBlockStatement:
                return AnalyzeDeveloperBlock(statement, assigned);

            default:
                throw new InvalidOperationException(
                    $"Production {statement.Production} is not a statement.");
        }
    }

    private ControlFlowOutcome AnalyzeStatementCore(
        GscSyntaxNode statementCore,
        HashSet<GscSymbol> assigned)
    {
        GscSyntaxNode body = GscSemanticSyntax.Node(statementCore.Children[0]);
        return statementCore.Production switch
        {
            GscProduction.StatementCoreCall => AnalyzeCall(body, assigned),
            GscProduction.StatementCoreSimple => AnalyzeSimpleStatement(body, assigned),
            _ => throw new InvalidOperationException(
                $"Production {statementCore.Production} is not a statement core.")
        };
    }

    private ControlFlowOutcome AnalyzeCall(
        GscSyntaxNode call,
        HashSet<GscSymbol> assigned)
    {
        AnalyzeExpression(call, assigned);
        return ControlFlowOutcome.Fallthrough(assigned);
    }

    private ControlFlowOutcome AnalyzeSimpleStatement(
        GscSyntaxNode statement,
        HashSet<GscSymbol> assigned)
    {
        switch (statement.Production)
        {
            case GscProduction.AssignmentStatement:
                AnalyzeExpression(statement.Children[2], assigned);
                ApplyLValue(
                    GscSemanticSyntax.Node(statement.Children[0]),
                    assigned,
                    isReadWrite: false);
                return ControlFlowOutcome.Fallthrough(assigned);

            case >= GscProduction.OrAssignmentStatement and
                <= GscProduction.ModuloAssignmentStatement:
                AnalyzeExpression(statement.Children[2], assigned);
                ApplyLValue(
                    GscSemanticSyntax.Node(statement.Children[0]),
                    assigned,
                    isReadWrite: true);
                return ControlFlowOutcome.Fallthrough(assigned);

            case GscProduction.IncrementStatement:
            case GscProduction.DecrementStatement:
                ApplyLValue(
                    GscSemanticSyntax.Node(statement.Children[0]),
                    assigned,
                    isReadWrite: true);
                return ControlFlowOutcome.Fallthrough(assigned);

            case GscProduction.ReturnValueStatement:
                AnalyzeExpression(statement.Children[1], assigned);
                return ControlFlowOutcome.Exit(ControlFlowExit.Return, assigned);

            case GscProduction.ReturnStatement:
                return ControlFlowOutcome.Exit(ControlFlowExit.Return, assigned);

            case GscProduction.WaitStatement:
                AnalyzeExpression(statement.Children[1], assigned);
                return ControlFlowOutcome.Fallthrough(assigned);

            case GscProduction.WaitTillStatement:
                AnalyzeExpression(statement.Children[0], assigned);
                GscSyntaxNode waitArguments =
                    GscSemanticSyntax.Node(statement.Children[3]);
                AnalyzeExpression(GetWaitTillEventExpression(waitArguments), assigned);
                foreach (GscSyntaxTokenElement output in EnumerateWaitTillOutputs(waitArguments))
                    ApplyReference(output, assigned, writeAfterRead: false);
                return ControlFlowOutcome.Fallthrough(assigned);

            case GscProduction.WaitTillMatchStatement:
            case GscProduction.NotifyStatement:
                AnalyzeExpression(statement.Children[0], assigned);
                AnalyzeExpression(statement.Children[3], assigned);
                return ControlFlowOutcome.Fallthrough(assigned);

            case GscProduction.EndOnStatement:
                AnalyzeExpression(statement.Children[0], assigned);
                AnalyzeExpression(statement.Children[3], assigned);
                return ControlFlowOutcome.Fallthrough(assigned);

            case GscProduction.BreakStatement:
                return ControlFlowOutcome.Exit(ControlFlowExit.Break, assigned);

            case GscProduction.ContinueStatement:
                return ControlFlowOutcome.Exit(ControlFlowExit.Continue, assigned);

            case GscProduction.WaitTillFrameEndStatement:
            case GscProduction.BreakpointStatement:
            case GscProduction.ProfileBeginStatement:
            case GscProduction.ProfileEndStatement:
                return ControlFlowOutcome.Fallthrough(assigned);

            default:
                throw new InvalidOperationException(
                    $"Production {statement.Production} is not a simple statement.");
        }
    }

    private ControlFlowOutcome AnalyzeIf(
        GscSyntaxNode statement,
        HashSet<GscSymbol> assigned,
        bool hasElse)
    {
        AnalyzeExpression(statement.Children[2], assigned);
        ControlFlowOutcome thenOutcome = AnalyzeStatement(
            GscSemanticSyntax.Node(statement.Children[4]),
            Clone(assigned));
        ControlFlowOutcome elseOutcome = hasElse
            ? AnalyzeStatement(
                GscSemanticSyntax.Node(statement.Children[6]),
                Clone(assigned))
            : ControlFlowOutcome.Fallthrough(assigned);
        return ControlFlowOutcome.Merge(thenOutcome, elseOutcome);
    }

    private ControlFlowOutcome AnalyzeWhile(
        GscSyntaxNode statement,
        HashSet<GscSymbol> assigned)
    {
        GscSyntaxNode condition = GscSemanticSyntax.Node(statement.Children[2]);
        AnalyzeExpression(condition, assigned);
        bool isConditionDefinitelyTruthy = IsDefinitelyTruthy(condition);
        ControlFlowOutcome body = AnalyzeStatement(
            GscSemanticSyntax.Node(statement.Children[4]),
            Clone(assigned));

        var result = new ControlFlowOutcome();
        result.CopyExit(body, ControlFlowExit.Return);
        result.CopyExit(
            body,
            ControlFlowExit.Break,
            ControlFlowExit.Fallthrough);

        if (!isConditionDefinitelyTruthy)
        {
            result.AddPath(ControlFlowExit.Fallthrough, assigned);
            result.CopyExit(
                body,
                ControlFlowExit.Fallthrough,
                ControlFlowExit.Fallthrough);
            result.CopyExit(
                body,
                ControlFlowExit.Continue,
                ControlFlowExit.Fallthrough);
        }

        return result;
    }

    private ControlFlowOutcome AnalyzeFor(
        GscSyntaxNode statement,
        HashSet<GscSymbol> assigned)
    {
        ControlFlowOutcome initializer = AnalyzeBlockItem(
            GscSemanticSyntax.Node(statement.Children[2]),
            Clone(assigned));
        return initializer.Then(
            initialized => AnalyzeForLoop(statement, initialized));
    }

    private ControlFlowOutcome AnalyzeForLoop(
        GscSyntaxNode statement,
        HashSet<GscSymbol> assigned)
    {
        GscSyntaxNode condition = GscSemanticSyntax.Node(statement.Children[3]);
        AnalyzeOptionalExpression(condition, assigned);
        bool isConditionDefinitelyTruthy =
            condition.Production == GscProduction.OptionalExpressionEmpty ||
            IsDefinitelyTruthy(GscSemanticSyntax.Node(condition.Children[0]));

        ControlFlowOutcome body = AnalyzeStatement(
            GscSemanticSyntax.Node(statement.Children[7]),
            Clone(assigned));
        var result = new ControlFlowOutcome();
        result.CopyExit(body, ControlFlowExit.Return);
        result.CopyExit(
            body,
            ControlFlowExit.Break,
            ControlFlowExit.Fallthrough);

        var incrementInput = new ControlFlowOutcome();
        incrementInput.CopyExit(
            body,
            ControlFlowExit.Fallthrough,
            ControlFlowExit.Fallthrough);
        incrementInput.CopyExit(
            body,
            ControlFlowExit.Continue,
            ControlFlowExit.Fallthrough);
        ControlFlowOutcome increment = incrementInput.Then(
            next => AnalyzeOptionalStatementCore(
                GscSemanticSyntax.Node(statement.Children[5]),
                next));

        result.CopyExit(increment, ControlFlowExit.Return);
        result.CopyExit(
            increment,
            ControlFlowExit.Break,
            ControlFlowExit.Fallthrough);
        if (!isConditionDefinitelyTruthy)
        {
            result.AddPath(ControlFlowExit.Fallthrough, assigned);
            result.CopyExit(
                increment,
                ControlFlowExit.Fallthrough,
                ControlFlowExit.Fallthrough);
            result.CopyExit(
                increment,
                ControlFlowExit.Continue,
                ControlFlowExit.Fallthrough);
        }

        return result;
    }

    private ControlFlowOutcome AnalyzeForeach(
        GscSyntaxNode statement,
        HashSet<GscSymbol> assigned,
        IReadOnlyList<int> targetChildIndexes,
        int expressionChildIndex,
        int bodyChildIndex)
    {
        AnalyzeExpression(statement.Children[expressionChildIndex], assigned);
        HashSet<GscSymbol> bodyAssigned = Clone(assigned);
        foreach (int targetIndex in targetChildIndexes)
        {
            ApplyLValue(
                GscSemanticSyntax.Node(statement.Children[targetIndex]),
                bodyAssigned,
                isReadWrite: false);
        }

        ControlFlowOutcome body = AnalyzeStatement(
            GscSemanticSyntax.Node(statement.Children[bodyChildIndex]),
            bodyAssigned);
        var result = ControlFlowOutcome.Fallthrough(assigned);
        result.CopyExit(body, ControlFlowExit.Return);
        result.CopyExit(
            body,
            ControlFlowExit.Break,
            ControlFlowExit.Fallthrough);
        result.CopyExit(
            body,
            ControlFlowExit.Fallthrough,
            ControlFlowExit.Fallthrough);
        result.CopyExit(
            body,
            ControlFlowExit.Continue,
            ControlFlowExit.Fallthrough);
        return result;
    }

    private ControlFlowOutcome AnalyzeSwitch(
        GscSyntaxNode statement,
        HashSet<GscSymbol> assigned)
    {
        AnalyzeExpression(statement.Children[2], assigned);
        GscSyntaxNode[] items = GscSemanticSyntax.EnumerateStatementList(
                GscSemanticSyntax.Node(statement.Children[5]))
            .ToArray();
        var result = new ControlFlowOutcome();
        ControlFlowOutcome? section = null;
        HashSet<GscSymbol>? previousFallthrough = null;
        bool hasDefault = false;

        foreach (GscSyntaxNode item in items)
        {
            bool isLabel = item.Production is
                GscProduction.CaseLabel or GscProduction.DefaultLabel;
            if (isLabel)
            {
                if (section is not null)
                {
                    previousFallthrough = CompleteSwitchSection(
                        section,
                        result,
                        isLast: false);
                }

                section = ControlFlowOutcome.Fallthrough(assigned);
                if (previousFallthrough is not null)
                    section.AddPath(ControlFlowExit.Fallthrough, previousFallthrough);
                hasDefault |= item.Production == GscProduction.DefaultLabel;
                continue;
            }

            if (section is not null)
            {
                section = section.Then(
                    next => AnalyzeBlockItem(item, next));
            }
        }

        if (section is not null)
            _ = CompleteSwitchSection(section, result, isLast: true);
        if (!hasDefault)
            result.AddPath(ControlFlowExit.Fallthrough, assigned);
        return result;
    }

    private static HashSet<GscSymbol>? CompleteSwitchSection(
        ControlFlowOutcome section,
        ControlFlowOutcome result,
        bool isLast)
    {
        result.CopyExit(section, ControlFlowExit.Return);
        result.CopyExit(section, ControlFlowExit.Continue);
        result.CopyExit(
            section,
            ControlFlowExit.Break,
            ControlFlowExit.Fallthrough);

        if (!section.TryGetPath(
                ControlFlowExit.Fallthrough,
                out HashSet<GscSymbol> fallthrough))
        {
            return null;
        }

        if (isLast)
        {
            result.AddPath(ControlFlowExit.Fallthrough, fallthrough);
            return null;
        }

        return Clone(fallthrough);
    }

    private ControlFlowOutcome AnalyzeDeveloperBlock(
        GscSyntaxNode statement,
        HashSet<GscSymbol> assigned)
    {
        ControlFlowOutcome skipped = ControlFlowOutcome.Fallthrough(assigned);
        ControlFlowOutcome included = AnalyzeStatementList(
            GscSemanticSyntax.Node(statement.Children[1]),
            Clone(assigned));
        return ControlFlowOutcome.Merge(skipped, included);
    }

    private void AnalyzeOptionalExpression(
        GscSyntaxNode optional,
        HashSet<GscSymbol> assigned)
    {
        if (optional.Production == GscProduction.OptionalExpressionPresent)
            AnalyzeExpression(optional.Children[0], assigned);
        else if (optional.Production != GscProduction.OptionalExpressionEmpty)
            throw new InvalidOperationException("Expected an optional expression.");
    }

    private ControlFlowOutcome AnalyzeOptionalStatementCore(
        GscSyntaxNode optional,
        HashSet<GscSymbol> assigned)
    {
        return optional.Production switch
        {
            GscProduction.OptionalStatementCorePresent => AnalyzeStatementCore(
                GscSemanticSyntax.Node(optional.Children[0]),
                assigned),
            GscProduction.OptionalStatementCoreEmpty =>
                ControlFlowOutcome.Fallthrough(assigned),
            _ => throw new InvalidOperationException(
                "Expected an optional statement core.")
        };
    }

    private void ApplyLValue(
        GscSyntaxNode lvalue,
        HashSet<GscSymbol> assigned,
        bool isReadWrite)
    {
        switch (lvalue.Production)
        {
            case GscProduction.LocalLValue:
                ApplyReference(
                    GscSemanticSyntax.Token(lvalue.Children[0]),
                    assigned,
                    writeAfterRead: isReadWrite);
                return;

            case GscProduction.IndexLValue:
                AnalyzeExpression(lvalue.Children[2], assigned);
                GscSyntaxNode receiver = GscSemanticSyntax.Node(lvalue.Children[0]);
                if (receiver.Production == GscProduction.PrimaryLValueExpression)
                {
                    ApplyLValue(
                        GscSemanticSyntax.Node(receiver.Children[0]),
                        assigned,
                        isReadWrite);
                }
                else
                {
                    AnalyzeExpression(receiver, assigned);
                }
                return;

            case GscProduction.FieldLValue:
            case GscProduction.DebuggerSelfFieldLValue:
                AnalyzeExpression(lvalue.Children[0], assigned);
                return;

            case GscProduction.DebuggerObjectLValue:
                return;

            default:
                throw new InvalidOperationException(
                    $"Production {lvalue.Production} is not an lvalue.");
        }
    }

    private void AnalyzeExpression(
        GscSyntaxElement element,
        HashSet<GscSymbol> assigned)
    {
        ObserveCancellation();
        if (element is GscSyntaxTokenElement token)
        {
            if (_model.TryGetReference(token, out GscBoundReference reference) &&
                reference.Kind is GscBoundReferenceKind.Read or
                    GscBoundReferenceKind.ReadWrite)
            {
                Read(reference, assigned);
            }
            return;
        }

        GscSyntaxNode node = GscSemanticSyntax.Node(element);
        if (node.Production is GscProduction.StatementListAppend or
            GscProduction.StatementListEmpty)
        {
            throw new InvalidOperationException(
                "A statement list cannot be analyzed as an expression.");
        }

        foreach (GscSyntaxElement child in node.Children)
            AnalyzeExpression(child, assigned);
    }

    private void ApplyReference(
        GscSyntaxTokenElement token,
        HashSet<GscSymbol> assigned,
        bool writeAfterRead)
    {
        if (!_model.TryGetReference(token, out GscBoundReference reference))
            return;
        if (reference.Symbol.Kind is not (GscSymbolKind.Local or GscSymbolKind.Parameter))
            return;

        if (writeAfterRead)
            Read(reference, assigned);
        assigned.Add(reference.Symbol);
    }

    private void Read(
        GscBoundReference reference,
        HashSet<GscSymbol> assigned)
    {
        if (reference.Symbol.Kind is not (GscSymbolKind.Local or GscSymbolKind.Parameter) ||
            assigned.Contains(reference.Symbol))
        {
            return;
        }

        _diagnostics.Add(new GscDiagnostic(
            GscDiagnosticCodes.UninitialisedVariable,
            GscDiagnosticStage.Semantic,
            GscDiagnosticSeverity.Error,
            reference.Span,
            _source.GetLinePositionSpan(reference.Span),
            $"uninitialised variable '{reference.Symbol.Name}'"));
    }

    private bool IsDefinitelyTruthy(GscSyntaxNode expression)
    {
        switch (expression.Production)
        {
            case GscProduction.ExpressionFromPrimary:
                return IsDefinitelyTruthy(
                    GscSemanticSyntax.Node(expression.Children[0]));

            case GscProduction.TrueLiteral:
                return true;

            case GscProduction.IntegerLiteral:
                return IsNonzeroInteger(
                    GscSemanticSyntax.Token(expression.Children[0]));

            case GscProduction.NegativeIntegerLiteral:
                return IsNonzeroInteger(
                    GscSemanticSyntax.Token(expression.Children[1]));

            case GscProduction.FloatLiteral:
                return IsNonzeroFloat(
                    GscSemanticSyntax.Token(expression.Children[0]));

            case GscProduction.NegativeFloatLiteral:
                return IsNonzeroFloat(
                    GscSemanticSyntax.Token(expression.Children[1]));

            case GscProduction.ParenthesizedExpressionList:
                GscSyntaxNode optional = GscSemanticSyntax.Node(expression.Children[1]);
                if (optional.Production != GscProduction.OptionalExpressionListPresent)
                    return false;
                GscSyntaxNode[] items = GscSemanticSyntax.EnumerateExpressions(
                        GscSemanticSyntax.Node(optional.Children[0]))
                    .ToArray();
                return items.Length == 1 && IsDefinitelyTruthy(items[0]);

            default:
                return false;
        }
    }

    private bool IsNonzeroInteger(GscSyntaxTokenElement token)
    {
        string text = GscSemanticSyntax.Text(_source, token);
        return text.Any(character => character != '0');
    }

    private bool IsNonzeroFloat(GscSyntaxTokenElement token)
    {
        string text = GscSemanticSyntax.Text(_source, token);
        return double.TryParse(
                   text,
                   NumberStyles.Float,
                   CultureInfo.InvariantCulture,
                   out double value) &&
               value != 0;
    }

    private static GscSyntaxNode GetWaitTillEventExpression(GscSyntaxNode arguments)
    {
        GscSyntaxNode current = arguments;
        while (current.Production == GscProduction.WaitTillArgumentsAppendOutput)
            current = GscSemanticSyntax.Node(current.Children[0]);
        if (current.Production != GscProduction.WaitTillArgumentsInitialExpression)
            throw new InvalidOperationException("Expected waittill arguments.");
        return GscSemanticSyntax.Node(current.Children[0]);
    }

    private static IEnumerable<GscSyntaxTokenElement> EnumerateWaitTillOutputs(
        GscSyntaxNode arguments)
    {
        var reversed = new List<GscSyntaxTokenElement>();
        GscSyntaxNode current = arguments;
        while (current.Production == GscProduction.WaitTillArgumentsAppendOutput)
        {
            reversed.Add(GscSemanticSyntax.Token(current.Children[2]));
            current = GscSemanticSyntax.Node(current.Children[0]);
        }

        if (current.Production != GscProduction.WaitTillArgumentsInitialExpression)
            throw new InvalidOperationException("Expected waittill arguments.");
        for (int index = reversed.Count - 1; index >= 0; index--)
            yield return reversed[index];
    }

    private void ObserveCancellation()
    {
        if ((_operations++ & 0xff) == 0)
            _cancellationToken.ThrowIfCancellationRequested();
    }

    private static HashSet<GscSymbol> Clone(IEnumerable<GscSymbol> assigned) =>
        [.. assigned];

    private enum ControlFlowExit
    {
        Fallthrough,
        Break,
        Continue,
        Return
    }

    private sealed class ControlFlowOutcome
    {
        private readonly Dictionary<ControlFlowExit, HashSet<GscSymbol>> _paths = [];

        internal static ControlFlowOutcome Fallthrough(IEnumerable<GscSymbol> assigned) =>
            Exit(ControlFlowExit.Fallthrough, assigned);

        internal static ControlFlowOutcome Exit(
            ControlFlowExit exit,
            IEnumerable<GscSymbol> assigned)
        {
            var outcome = new ControlFlowOutcome();
            outcome.AddPath(exit, assigned);
            return outcome;
        }

        internal static ControlFlowOutcome Merge(params ControlFlowOutcome[] alternatives)
        {
            var result = new ControlFlowOutcome();
            foreach (ControlFlowOutcome alternative in alternatives)
                result.MergeFrom(alternative);
            return result;
        }

        internal void AddPath(
            ControlFlowExit exit,
            IEnumerable<GscSymbol> assigned)
        {
            if (_paths.TryGetValue(exit, out HashSet<GscSymbol>? existing))
            {
                existing.IntersectWith(assigned);
            }
            else
            {
                _paths.Add(exit, Clone(assigned));
            }
        }

        internal bool TryGetPath(
            ControlFlowExit exit,
            out HashSet<GscSymbol> assigned) =>
            _paths.TryGetValue(exit, out assigned!);

        internal void CopyExit(
            ControlFlowOutcome source,
            ControlFlowExit sourceExit,
            ControlFlowExit? targetExit = null)
        {
            if (source.TryGetPath(sourceExit, out HashSet<GscSymbol> assigned))
                AddPath(targetExit ?? sourceExit, assigned);
        }

        internal ControlFlowOutcome Then(
            Func<HashSet<GscSymbol>, ControlFlowOutcome> next)
        {
            var result = new ControlFlowOutcome();
            foreach ((ControlFlowExit exit, HashSet<GscSymbol> assigned) in _paths)
            {
                if (exit != ControlFlowExit.Fallthrough)
                    result.AddPath(exit, assigned);
            }

            if (TryGetPath(ControlFlowExit.Fallthrough, out HashSet<GscSymbol> fallthrough))
                result.MergeFrom(next(Clone(fallthrough)));
            return result;
        }

        private void MergeFrom(ControlFlowOutcome other)
        {
            foreach ((ControlFlowExit exit, HashSet<GscSymbol> assigned) in other._paths)
                AddPath(exit, assigned);
        }
    }
}
