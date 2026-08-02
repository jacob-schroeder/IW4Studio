using IW4.Gsc.Syntax;

namespace IW4.Gsc.Semantics;

internal sealed class GscContextAnalyzer
{
    private readonly GscSourceText _source;
    private readonly GscSemanticModel _model;
    private readonly CancellationToken _cancellationToken;
    private readonly List<GscDiagnostic> _diagnostics = [];
    private int _operations;

    private GscContextAnalyzer(
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
        var analyzer = new GscContextAnalyzer(
            source,
            model,
            cancellationToken);
        return analyzer.Analyze();
    }

    private IReadOnlyList<GscDiagnostic> Analyze()
    {
        AnalyzeExpressionListArities();
        foreach (GscBoundFunction function in _model.Functions)
        {
            AnalyzeStatementList(
                GscSemanticSyntax.Node(function.Syntax.Children[5]),
                new StatementContext(LoopDepth: 0, SwitchDepth: 0),
                isDirectSwitchBody: false);
        }

        return Array.AsReadOnly(_diagnostics.ToArray());
    }

    private void AnalyzeExpressionListArities()
    {
        var pending = new Stack<GscSyntaxElement>();
        pending.Push(_model.SyntaxTree.Root);
        while (pending.TryPop(out GscSyntaxElement? element))
        {
            ObserveCancellation();
            if (element is not GscSyntaxNode node)
                continue;

            if (node.Production == GscProduction.ParenthesizedExpressionList)
            {
                GscSyntaxNode optional = GscSemanticSyntax.Node(node.Children[1]);
                int count = optional.Production switch
                {
                    GscProduction.OptionalExpressionListEmpty => 0,
                    GscProduction.OptionalExpressionListPresent =>
                        GscSemanticSyntax.EnumerateExpressions(
                            GscSemanticSyntax.Node(optional.Children[0])).Count(),
                    _ => throw new InvalidOperationException(
                        "Expected an optional expression list.")
                };

                if (count is not (1 or 3))
                {
                    AddDiagnostic(
                        GscDiagnosticCodes.InvalidExpressionListArity,
                        GscSemanticSyntax.Token(node.Children[0]).Token.Span,
                        "expression list must have 1 or 3 parameters");
                }
            }

            for (int index = node.Children.Count - 1; index >= 0; index--)
                pending.Push(node.Children[index]);
        }
    }

    private void AnalyzeStatementList(
        GscSyntaxNode list,
        StatementContext context,
        bool isDirectSwitchBody)
    {
        bool hasSwitchLabel = false;
        bool reportedMissingCase = false;
        foreach (GscSyntaxNode item in GscSemanticSyntax.EnumerateStatementList(list))
        {
            ObserveCancellation();
            if (isDirectSwitchBody &&
                item.Production is GscProduction.CaseLabel or GscProduction.DefaultLabel)
            {
                hasSwitchLabel = true;
                continue;
            }

            if (isDirectSwitchBody && !hasSwitchLabel && !reportedMissingCase)
            {
                AddDiagnostic(
                    GscDiagnosticCodes.MissingCaseStatement,
                    item.Span,
                    "missing case statement");
                reportedMissingCase = true;
            }

            AnalyzeBlockItem(item, context);
        }
    }

    private void AnalyzeBlockItem(
        GscSyntaxNode item,
        StatementContext context)
    {
        switch (item.Production)
        {
            case GscProduction.EmptyBlockItem:
                return;

            case GscProduction.CaseLabel:
                AddDiagnostic(
                    GscDiagnosticCodes.IllegalCaseStatement,
                    GscSemanticSyntax.Token(item.Children[0]).Token.Span,
                    "illegal case statement");
                return;

            case GscProduction.DefaultLabel:
                AddDiagnostic(
                    GscDiagnosticCodes.IllegalDefaultStatement,
                    GscSemanticSyntax.Token(item.Children[0]).Token.Span,
                    "illegal default statement");
                return;

            case GscProduction.BlockItemStatement:
                AnalyzeStatement(GscSemanticSyntax.Node(item.Children[0]), context);
                return;

            default:
                throw new InvalidOperationException(
                    $"Production {item.Production} is not a block item.");
        }
    }

    private void AnalyzeStatement(
        GscSyntaxNode statement,
        StatementContext context)
    {
        switch (statement.Production)
        {
            case GscProduction.TerminatedStatement:
                AnalyzeStatementCore(
                    GscSemanticSyntax.Node(statement.Children[0]),
                    context);
                return;

            case GscProduction.BlockStatement:
                AnalyzeStatementList(
                    GscSemanticSyntax.Node(statement.Children[1]),
                    context,
                    isDirectSwitchBody: false);
                return;

            case GscProduction.IfStatement:
                AnalyzeStatement(
                    GscSemanticSyntax.Node(statement.Children[4]),
                    context);
                return;

            case GscProduction.IfElseStatement:
                AnalyzeStatement(
                    GscSemanticSyntax.Node(statement.Children[4]),
                    context);
                AnalyzeStatement(
                    GscSemanticSyntax.Node(statement.Children[6]),
                    context);
                return;

            case GscProduction.WhileStatement:
                AnalyzeStatement(
                    GscSemanticSyntax.Node(statement.Children[4]),
                    context.EnterLoop());
                return;

            case GscProduction.ForStatement:
                AnalyzeBlockItem(
                    GscSemanticSyntax.Node(statement.Children[2]),
                    context);
                AnalyzeOptionalStatementCore(
                    GscSemanticSyntax.Node(statement.Children[5]),
                    context.EnterLoop());
                AnalyzeStatement(
                    GscSemanticSyntax.Node(statement.Children[7]),
                    context.EnterLoop());
                return;

            case GscProduction.KeyValueForeachStatement:
                AnalyzeStatement(
                    GscSemanticSyntax.Node(statement.Children[8]),
                    context.EnterLoop());
                return;

            case GscProduction.ValueForeachStatement:
                AnalyzeStatement(
                    GscSemanticSyntax.Node(statement.Children[6]),
                    context.EnterLoop());
                return;

            case GscProduction.SwitchStatement:
                AnalyzeStatementList(
                    GscSemanticSyntax.Node(statement.Children[5]),
                    context.EnterSwitch(),
                    isDirectSwitchBody: true);
                return;

            case GscProduction.DeveloperBlockStatement:
                AnalyzeStatementList(
                    GscSemanticSyntax.Node(statement.Children[1]),
                    context,
                    isDirectSwitchBody: false);
                return;

            default:
                throw new InvalidOperationException(
                    $"Production {statement.Production} is not a statement.");
        }
    }

    private void AnalyzeOptionalStatementCore(
        GscSyntaxNode optional,
        StatementContext context)
    {
        if (optional.Production == GscProduction.OptionalStatementCorePresent)
        {
            AnalyzeStatementCore(
                GscSemanticSyntax.Node(optional.Children[0]),
                context);
        }
        else if (optional.Production != GscProduction.OptionalStatementCoreEmpty)
        {
            throw new InvalidOperationException("Expected an optional statement core.");
        }
    }

    private void AnalyzeStatementCore(
        GscSyntaxNode statementCore,
        StatementContext context)
    {
        if (statementCore.Production == GscProduction.StatementCoreCall)
            return;
        if (statementCore.Production != GscProduction.StatementCoreSimple)
            throw new InvalidOperationException("Expected a statement core.");

        GscSyntaxNode statement = GscSemanticSyntax.Node(statementCore.Children[0]);
        if (statement.Production == GscProduction.BreakStatement &&
            context.LoopDepth == 0 &&
            context.SwitchDepth == 0)
        {
            AddDiagnostic(
                GscDiagnosticCodes.IllegalBreakStatement,
                GscSemanticSyntax.Token(statement.Children[0]).Token.Span,
                "illegal break statement");
        }
        else if (statement.Production == GscProduction.ContinueStatement &&
                 context.LoopDepth == 0)
        {
            AddDiagnostic(
                GscDiagnosticCodes.IllegalContinueStatement,
                GscSemanticSyntax.Token(statement.Children[0]).Token.Span,
                "illegal continue statement");
        }
    }

    private void AddDiagnostic(string code, GscTextSpan span, string message) =>
        _diagnostics.Add(new GscDiagnostic(
            code,
            GscDiagnosticStage.Semantic,
            GscDiagnosticSeverity.Error,
            span,
            _source.GetLinePositionSpan(span),
            message));

    private void ObserveCancellation()
    {
        if ((_operations++ & 0xff) == 0)
            _cancellationToken.ThrowIfCancellationRequested();
    }

    private readonly record struct StatementContext(int LoopDepth, int SwitchDepth)
    {
        internal StatementContext EnterLoop() => this with { LoopDepth = LoopDepth + 1 };

        internal StatementContext EnterSwitch() => this with { SwitchDepth = SwitchDepth + 1 };
    }
}
