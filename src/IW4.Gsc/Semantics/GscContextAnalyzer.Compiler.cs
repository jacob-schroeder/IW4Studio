using IW4.Gsc.BuiltIns;
using IW4.Gsc.Syntax;

namespace IW4.Gsc.Semantics;

internal sealed partial class GscContextAnalyzer
{
    private readonly Dictionary<string, GscConstant> _defines = new(StringComparer.OrdinalIgnoreCase);
    private readonly GscConstantEvaluator _constants;
    private bool _developer;
    private bool _hasAnimationTree;

    private void AnalyzeCompilerContext()
    {
        GscSyntaxNode[] items = GscSemanticSyntax.EnumerateTopLevelItems(
            GscSemanticSyntax.Node(_model.SyntaxTree.Root.Children[2])).ToArray();
        GscTextSpan? opening = null;
        foreach (GscSyntaxNode item in items)
        {
            ObserveCancellation();
            switch (item.Production)
            {
                case GscProduction.DeveloperSectionOpen:
                    if (_developer)
                        AddDiagnostic(GscDiagnosticCodes.DeveloperSectionError, item.Span, "cannot recurse /#");
                    else
                        opening = item.Span;
                    _developer = true;
                    break;
                case GscProduction.DeveloperSectionClose:
                    if (!_developer)
                        AddDiagnostic(GscDiagnosticCodes.DeveloperSectionError, item.Span, "#/ has no matching /#");
                    _developer = false;
                    opening = null;
                    break;
                case GscProduction.UsingAnimTreeDirective:
                {
                    if (_developer)
                        AddDiagnostic(GscDiagnosticCodes.DeveloperSectionError, item.Span,
                            "cannot put #using_animtree inside /# ... #/ comment");
                    GscSyntaxElement token = item.Children.First(child =>
                        child is GscSyntaxTokenElement { Token.Kind: GscTokenKind.String });
                    string name = GscConstantEvaluator.DecodeString(_source.GetText(token.Span));
                    // Scr_IsIdentifier (0x20C030) calls the ASCII predicate at 0x293330.
                    if (name.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
                        AddDiagnostic(GscDiagnosticCodes.InvalidAnimationTreeName, token.Span, "bad anim tree name");
                    _hasAnimationTree = true;
                    break;
                }
                case GscProduction.DefineDeclaration:
                {
                    GscSyntaxNode expression = GscSemanticSyntax.Node(item.Children[2]);
                    new OperandStack(this).Evaluate(expression);
                    AnalyzeExpressions(expression, valueUsed: true, emitConstants: false);
                    GscConstant? constant = _constants.Evaluate(expression);
                    if (constant is { } value)
                    {
                        _defines.TryAdd(_source.GetText(item.Children[0].Span), value);
                        _constants.DefinesChanged();
                    }
                    else if (!_diagnostics.Any(diagnostic => diagnostic.Span.Start >= expression.Span.Start &&
                                                              diagnostic.Span.End <= expression.Span.End))
                        AddDiagnostic(GscDiagnosticCodes.InvalidDefineExpression, expression.Span,
                            "Expression does not evaluate to a primitive value");
                    break;
                }
                case GscProduction.FunctionDefinition:
                    // developer_script=0: the engine parses but does not emit
                    // developer-only function bodies (unlike inline /# blocks).
                    if (_developer) break;
                    AnalyzeStatementList(GscSemanticSyntax.Node(item.Children[5]),
                        new StatementContext(0, 0), isDirectSwitchBody: false);
                    AnalyzeOperandStack(item);
                    break;
            }
        }
        if (opening is { } unmatched)
            AddDiagnostic(GscDiagnosticCodes.DeveloperSectionError, unmatched, "/# has no matching #/");
    }

    private void AnalyzeCaseLabel(GscSyntaxNode label, HashSet<(GscConstantKind Kind, string Value)> values)
    {
        (GscConstantKind, string) key;
        GscTextSpan span;
        if (label.Production == GscProduction.DefaultLabel)
        {
            key = (GscConstantKind.Undefined, string.Empty);
            span = label.Children[0].Span;
        }
        else
        {
            GscSyntaxNode expression = GscSemanticSyntax.Node(label.Children[1]);
            span = expression.Span;
            while (expression.Production == GscProduction.ExpressionFromPrimary)
                expression = GscSemanticSyntax.Node(expression.Children[0]);
            if (expression.Production is not (GscProduction.IntegerLiteral or GscProduction.StringLiteral))
            {
                AddDiagnostic(GscDiagnosticCodes.InvalidCaseExpression, span, "case expression must be an int or string");
                return;
            }
            if (_constants.Evaluate(expression) is not { } value)
                return;
            if (value.Kind == GscConstantKind.Integer && unchecked((uint)(value.Int + 0x7e7000)) > 0xfe6fff)
            {
                AddDiagnostic(GscDiagnosticCodes.InvalidCaseExpression, span, $"case index {value.Int} out of range");
                return;
            }
            key = (value.Kind, value.Display);
        }
        if (!values.Add(key))
            AddDiagnostic(GscDiagnosticCodes.DuplicateCaseExpression, span, "duplicate case expression");
    }

    private void AnalyzeStatementExpressions(GscSyntaxNode statement)
    {
        switch (statement.Production)
        {
            case GscProduction.IfStatement:
            case GscProduction.IfElseStatement:
            case GscProduction.SwitchStatement:
                AnalyzeExpressions(statement.Children[2], valueUsed: true);
                break;
            case GscProduction.WhileStatement:
                AnalyzeLoopCondition(GscSemanticSyntax.Node(statement.Children[2]));
                break;
            case GscProduction.ForStatement:
            {
                GscSyntaxNode optional = GscSemanticSyntax.Node(statement.Children[3]);
                if (optional.Production == GscProduction.OptionalExpressionPresent)
                    AnalyzeLoopCondition(GscSemanticSyntax.Node(optional.Children[0]));
                break;
            }
            case GscProduction.ValueForeachStatement:
                AnalyzeExpressions(statement.Children[2], valueUsed: true);
                AnalyzeExpressions(statement.Children[4], valueUsed: true);
                break;
            case GscProduction.KeyValueForeachStatement:
                AnalyzeExpressions(statement.Children[2], valueUsed: true);
                AnalyzeExpressions(statement.Children[4], valueUsed: true);
                AnalyzeExpressions(statement.Children[6], valueUsed: true);
                break;
        }
    }

    private void AnalyzeLoopCondition(GscSyntaxNode condition)
    {
        AnalyzeExpressions(condition, valueUsed: true);
        if (_constants.Evaluate(condition) is { IsNumeric: true, Number: 0 })
            AddDiagnostic(GscDiagnosticCodes.ConstantFalseCondition, condition.Span,
                "conditional expression cannot be always false");
    }

    private void AnalyzeExpressions(GscSyntaxElement element, bool valueUsed, bool emitConstants = true)
    {
        ObserveCancellation();
        if (element is not GscSyntaxNode node)
            return;
        bool wasDeveloper = _developer;
        if (_constants.Evaluate(node) is { } constant &&
            node.Nonterminal is GscNonterminal.Expression or GscNonterminal.PrimaryExpression)
        {
            if (emitConstants && !_developer && constant.Kind == GscConstantKind.PreAnimation && !_hasAnimationTree)
                AddDiagnostic(GscDiagnosticCodes.AnimationTreeRequired, node.Span, "#using_animtree was not specified");
            return;
        }
        switch (node.Production)
        {
            case GscProduction.StatementCoreCall:
                AnalyzeExpressions(node.Children[0], valueUsed: false);
                return;
            case GscProduction.DebuggerObjectLValue:
            case GscProduction.DebuggerSelfFieldLValue:
                AddDiagnostic(GscDiagnosticCodes.DebuggerOnlyExpression, node.Span,
                    node.Production == GscProduction.DebuggerObjectLValue
                        ? "$ can only be used in the script debugger"
                        : "self field can only be used in the script debugger");
                return;
            case GscProduction.AnimTreeExpression:
                if (!_hasAnimationTree)
                    AddDiagnostic(GscDiagnosticCodes.AnimationTreeRequired, node.Span, "#using_animtree was not specified");
                return;
            case GscProduction.BreakOnExpression:
                AddDiagnostic(GscDiagnosticCodes.IllegalFunctionName, node.Span, "illegal function name");
                return;
            case GscProduction.FieldLValue:
                if (!IsFieldObject(GscSemanticSyntax.Node(node.Children[0])))
                    AddDiagnostic(GscDiagnosticCodes.InvalidObjectExpression, node.Children[0].Span, "not an object");
                break;
            case GscProduction.CallExpression:
            case GscProduction.MethodCallExpression:
                AnalyzeNativeCall(node, valueUsed);
                if (!valueUsed && Iw4GscBuiltInCatalog.ResolveCall(_source, node) is { DeveloperOnly: true })
                    _developer = true;
                break;
            case GscProduction.FunctionReferenceLocal:
            {
                GscSyntaxElement name = node.Children[1];
                AnalyzeDeveloperReference(_source.GetText(name.Span), name.Span);
                break;
            }
        }
        foreach (GscSyntaxElement child in node.Children)
            AnalyzeExpressions(child, valueUsed: true, emitConstants);
        _developer = wasDeveloper;
    }

    private void AnalyzeNativeCall(GscSyntaxNode call, bool valueUsed)
    {
        GscSyntaxNode? kind = call.Children.OfType<GscSyntaxNode>().FirstOrDefault(child =>
            child.Production is >= GscProduction.CallKindDirect and <= GscProduction.CallKindCallPointer);
        GscSyntaxNode? callable = kind?.Children.OfType<GscSyntaxNode>()
            .FirstOrDefault(child => child.Production == GscProduction.CallableNamedFunction);
        if (callable is null || kind?.Production != GscProduction.CallKindDirect)
            return;
        GscSyntaxNode named = GscSemanticSyntax.Node(callable.Children[0]);
        if (named.Production != GscProduction.NamedFunctionLocal)
            return;
        GscSyntaxElement nameToken = named.Children[0];
        Iw4GscBuiltInDefinition? native = Iw4GscBuiltInCatalog.ResolveCall(_source, call);
        if (native is null)
            return;
        GscSyntaxNode optional = call.Children.OfType<GscSyntaxNode>().First(child =>
            child.Production is GscProduction.OptionalExpressionListPresent or GscProduction.OptionalExpressionListEmpty);
        if (optional.Production == GscProduction.OptionalExpressionListPresent)
        {
            GscSyntaxNode[] arguments = GscSemanticSyntax.EnumerateExpressions(
                GscSemanticSyntax.Node(optional.Children[0])).ToArray();
            if (arguments.Length > 255)
                AddDiagnostic(GscDiagnosticCodes.ParameterCountExceeded, arguments[255].Span, "parameter count exceeds 256");
        }
        if (!_developer && valueUsed && native.DeveloperOnly)
            AddDiagnostic(GscDiagnosticCodes.DeveloperOnlyReference, nameToken.Span,
                "return value of developer command can not be accessed if not in a /# ... #/ comment");
    }

    private void AnalyzeDeveloperReference(string name, GscTextSpan span)
    {
        if (_developer)
            return;
        if (Iw4GscBuiltInCatalog.Multiplayer.FindCallablesByName(name)
                .OrderBy(definition => definition.Kind == Iw4GscBuiltInKind.Function ? 0 : 1)
                .FirstOrDefault() is { DeveloperOnly: true })
            AddDiagnostic(GscDiagnosticCodes.DeveloperOnlyReference, span,
                "developer command can not be accessed if not in a /# ... #/ comment");
    }

    private static bool IsFieldObject(GscSyntaxNode node)
    {
        if (node.Production == GscProduction.ExpressionFromPrimary)
            return IsFieldObject(GscSemanticSyntax.Node(node.Children[0]));
        if (node.Production == GscProduction.ParenthesizedExpressionList)
        {
            GscSyntaxNode optional = GscSemanticSyntax.Node(node.Children[1]);
            if (optional.Production == GscProduction.OptionalExpressionListEmpty)
                return false;
            GscSyntaxNode[] items = GscSemanticSyntax.EnumerateExpressions(
                GscSemanticSyntax.Node(optional.Children[0])).ToArray();
            return items.Length == 1 && IsFieldObject(items[0]);
        }
        return node.Production is GscProduction.PrimaryLValueExpression or GscProduction.PrimaryCallExpression
            or GscProduction.SelfExpression or GscProduction.LevelExpression or GscProduction.AnimExpression;
    }
}
