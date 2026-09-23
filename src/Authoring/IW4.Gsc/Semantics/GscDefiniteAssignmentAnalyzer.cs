using IW4.Gsc.Syntax;
using System.Text;

namespace IW4.Gsc.Semantics;

// The compiler allocates local slots before emitting statements. Emission still
// visits statements after an abort; missing write slots produce "unreachable code".
internal sealed class GscDefiniteAssignmentAnalyzer
{
    private readonly GscSourceText _source;
    private readonly GscSemanticModel _model;
    private readonly CancellationToken _cancellationToken;
    private readonly List<GscDiagnostic> _diagnostics = [];
    private readonly Dictionary<GscSyntaxNode, LocalBlock> _blocks = [];
    private readonly GscConstantEvaluator _constants;
    private readonly Dictionary<string, GscSymbol> _hiddenLocals = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GscConstant> _defines = new(StringComparer.OrdinalIgnoreCase);
    private readonly GscConstantEvaluator _emittedConstants;
    private List<LocalBlock>? _breaks;
    private List<LocalBlock>? _continues;
    private bool _canBreak;
    private bool _canContinue;
    private bool _developer;

    private GscDefiniteAssignmentAnalyzer(GscSourceText source, GscSemanticModel model,
        CancellationToken cancellationToken)
    {
        _source = source;
        _model = model;
        _cancellationToken = cancellationToken;
        _constants = new GscConstantEvaluator(source, new Dictionary<string, GscConstant>(),
            (_, _) => { }, cancellationToken);
        _emittedConstants = new GscConstantEvaluator(source, _defines, (_, _) => { }, cancellationToken);
    }

    internal static IReadOnlyList<GscDiagnostic> Analyze(GscSourceText source,
        GscSemanticModel model, CancellationToken cancellationToken)
    {
        var analyzer = new GscDefiniteAssignmentAnalyzer(source, model, cancellationToken);
        foreach (GscSyntaxNode item in GscSemanticSyntax.EnumerateTopLevelItems(Node(model.SyntaxTree.Root, 2)))
        {
            if (item.Production == GscProduction.DefineDeclaration)
            {
                if (analyzer._emittedConstants.Evaluate(Node(item, 2)) is { } value)
                {
                    analyzer._defines.TryAdd(source.GetText(item.Children[0].Span), value);
                    analyzer._emittedConstants.DefinesChanged();
                }
                continue;
            }
            if (item.Production != GscProduction.FunctionDefinition) continue;
            GscBoundFunction function = model.Functions.First(function => function.Syntax == item);
            if (function.DeveloperOnly) continue;
            analyzer._hiddenLocals.Clear();
            var block = new LocalBlock();
            foreach (GscSymbol parameter in function.Parameters)
                analyzer.Register(parameter, block);
            GscSyntaxNode body = Node(function.Syntax, 5);
            analyzer.Visit(body, block, allocate: true);
            block.Abort = 0;
            foreach (GscSymbol parameter in function.Parameters)
                analyzer.Access(parameter, parameter.DeclarationSpan, block, create: true);
            analyzer.Visit(body, block, allocate: false);
        }
        return analyzer._diagnostics.Distinct().ToArray();
    }

    private void Visit(GscSyntaxNode node, LocalBlock block, bool allocate)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        switch (node.Production)
        {
            case GscProduction.StatementListAppend:
            case GscProduction.StatementListEmpty:
                foreach (GscSyntaxNode item in GscSemanticSyntax.EnumerateStatementList(node))
                    Visit(item, block, allocate);
                break;
            case GscProduction.BlockItemStatement:
            case GscProduction.TerminatedStatement:
            case GscProduction.StatementCoreSimple:
            case GscProduction.OptionalStatementCorePresent:
                Visit(Node(node, 0), block, allocate);
                break;
            case GscProduction.BlockStatement:
                Visit(Node(node, 1), block, allocate);
                break;
            case GscProduction.AssignmentStatement:
                if (!allocate) Read(node.Children[2], block);
                LValue(Node(node, 0), block, allocate, readWrite: false);
                break;
            case >= GscProduction.OrAssignmentStatement and <= GscProduction.ModuloAssignmentStatement:
                LValue(Node(node, 0), block, allocate, readWrite: true);
                if (!allocate) Read(node.Children[2], block);
                break;
            case GscProduction.IncrementStatement:
            case GscProduction.DecrementStatement:
                LValue(Node(node, 0), block, allocate, readWrite: true);
                break;
            case GscProduction.WaitTillStatement:
            {
                GscSyntaxNode arguments = Node(node, 3);
                var outputs = new Stack<GscSyntaxTokenElement>();
                while (arguments.Production == GscProduction.WaitTillArgumentsAppendOutput)
                {
                    outputs.Push(GscSemanticSyntax.Token(arguments.Children[2]));
                    arguments = Node(arguments, 0);
                }
                if (!allocate)
                {
                    Read(arguments.Children[0], block);
                    Read(node.Children[0], block);
                }
                foreach (GscSyntaxTokenElement output in outputs)
                    Reference(output, block, allocate, create: true);
                break;
            }
            case GscProduction.ReturnValueStatement:
                if (!allocate) Read(node.Children[1], block);
                goto case GscProduction.ReturnStatement;
            case GscProduction.ReturnStatement:
                if (block.Abort == 0) block.Abort = 3;
                break;
            case GscProduction.BreakStatement:
            case GscProduction.ContinueStatement:
            {
                bool isBreak = node.Production == GscProduction.BreakStatement;
                if (!allocate && (block.Abort != 0 || !(isBreak ? _canBreak : _canContinue)))
                    Report(isBreak ? GscDiagnosticCodes.IllegalBreakStatement : GscDiagnosticCodes.IllegalContinueStatement,
                        node.Span, isBreak ? "illegal break statement" : "illegal continue statement");
                else
                {
                    AddChild(isBreak ? _breaks : _continues, block, node.Span, allocate);
                    if (block.Abort == 0) block.Abort = isBreak ? 2 : 1;
                }
                break;
            }
            case GscProduction.IfStatement:
            case GscProduction.IfElseStatement:
                Conditional(node, block, allocate);
                break;
            case GscProduction.WhileStatement:
            case GscProduction.ForStatement:
                Loop(node, block, allocate);
                break;
            case GscProduction.KeyValueForeachStatement:
            case GscProduction.ValueForeachStatement:
                Foreach(node, block, allocate);
                break;
            case GscProduction.SwitchStatement:
                Switch(node, block, allocate);
                break;
            case GscProduction.DeveloperBlockStatement:
            {
                GscSyntaxNode body = Node(node, 1);
                LocalBlock child = Child(body, block, allocate);
                bool previous = _developer;
                if (!allocate) _developer = true;
                Visit(body, child, allocate);
                _developer = previous;
                if (allocate) Merge([child], block);
                break;
            }
            case GscProduction.EmptyBlockItem:
            case GscProduction.OptionalStatementCoreEmpty:
            case GscProduction.CaseLabel:
            case GscProduction.DefaultLabel:
                break;
            default:
                if (!allocate) Read(node, block);
                break;
        }
    }

    private void Conditional(GscSyntaxNode node, LocalBlock block, bool allocate)
    {
        if (!allocate) Read(node.Children[2], block);
        GscSyntaxNode first = Node(node, 4);
        LocalBlock thenBlock = Child(first, block, allocate);
        Visit(first, thenBlock, allocate);
        if (node.Production == GscProduction.IfStatement)
        {
            if (allocate) Merge([thenBlock], block);
            return;
        }
        GscSyntaxNode second = Node(node, 6);
        LocalBlock elseBlock = Child(second, block, allocate);
        Visit(second, elseBlock, allocate);
        LocalBlock[] live = new[] { thenBlock, elseBlock }.Where(child => child.Abort == 0).ToArray();
        if (allocate)
        {
            if (block.Abort == 0) block.Abort = Math.Min(thenBlock.Abort, elseBlock.Abort);
            Append(live, block);
            Merge(live, block);
        }
        else Init(live, block);
    }

    private void Loop(GscSyntaxNode node, LocalBlock block, bool allocate)
    {
        bool isFor = node.Production == GscProduction.ForStatement;
        var oldBreaks = _breaks;
        var oldContinues = _continues;
        bool oldCanBreak = _canBreak, oldCanContinue = _canContinue;
        if (!allocate) _canBreak = _canContinue = false;
        if (isFor) Visit(Node(node, 2), block, allocate);
        GscSyntaxNode condition = Node(node, isFor ? 3 : 2);
        bool always = isFor && condition.Production == GscProduction.OptionalExpressionEmpty;
        if (isFor && !always) condition = Node(condition, 0);
        if (!always) always = (allocate ? _constants : _emittedConstants).Evaluate(condition) is { IsNumeric: true } value && value.Number != 0;
        GscSyntaxNode body = Node(node, isFor ? 7 : 4);
        LocalBlock child = Child(body, block, allocate);
        LocalBlock? post = isFor ? Child(Node(node, 5), block, allocate) : null;
        if (!allocate)
        {
            child.Created = child.Public;
            block.Created = child.Created;
            if (post is not null) Transfer(block, post);
            Read(condition, block);
        }
        _breaks = always ? [] : null;
        _continues = allocate || isFor ? [] : null;
        _canBreak = _canContinue = true;
        Visit(body, child, allocate);
        AddChild(_continues, child, body.Span, allocate);
        _canBreak = _canContinue = false;
        if (allocate)
        {
            foreach (LocalBlock continued in _continues ?? []) Append([continued], block);
            if (post is not null)
            {
                Visit(Node(node, 5), post, allocate);
                Append([post], block);
                Merge([post], block);
            }
            if (always) Append(_breaks ?? [], block);
            Merge([child], block);
        }
        else
        {
            if (post is not null)
            {
                Init(_continues ?? [], post);
                Visit(Node(node, 5), post, allocate);
            }
            if (always) Init(_breaks ?? [], block);
        }
        _breaks = oldBreaks;
        _continues = oldContinues;
        _canBreak = oldCanBreak;
        _canContinue = oldCanContinue;
    }

    private void Foreach(GscSyntaxNode node, LocalBlock block, bool allocate)
    {
        bool keyed = node.Production == GscProduction.KeyValueForeachStatement;
        GscSyntaxNode body = Node(node, keyed ? 8 : 6);
        GscSyntaxNode expression = Node(node, keyed ? 6 : 4);
        // The yacc helper at 0x222E70 lowers foreach to a for loop, with a
        // source-derived array temporary and (for value-only loops) a key.
        int start = node.Children[keyed ? 5 : 3].Span.End;
        int end = node.Children[keyed ? 7 : 5].Span.Start;
        byte[] bytes = _source.GetBytes(new GscTextSpan(start, end - start));
        string suffix = Encoding.Latin1.GetString(bytes).TrimStart(' ', '\t', '\r', '\n');
        suffix = suffix[..Math.Min(62, suffix.Length)].TrimEnd(' ', '\t', '\r', '\n');
        GscSymbol array = Hidden(":" + suffix, expression.Span);
        GscSymbol? key = keyed ? null : Hidden("?" + suffix, expression.Span);
        if (!allocate) Read(expression, block);
        if (allocate) Register(array, block);
        else Access(array, expression.Span, block, create: true);
        if (key is not null)
        {
            if (allocate) Register(key, block);
            else Access(key, expression.Span, block, create: true);
        }
        else LValue(Node(node, 2), block, allocate, readWrite: false);
        LocalBlock child = Child(body, block, allocate);
        if (!allocate) block.Created = child.Created = child.Public;
        LValue(Node(node, keyed ? 4 : 2), child, allocate, readWrite: false);
        var oldBreaks = _breaks;
        var oldContinues = _continues;
        bool oldCanBreak = _canBreak, oldCanContinue = _canContinue;
        _breaks = null;
        _continues = [];
        _canBreak = _canContinue = true;
        Visit(body, child, allocate);
        AddChild(_continues, child, body.Span, allocate);
        if (allocate)
        {
            foreach (LocalBlock continued in _continues) Append([continued], block);
            Merge([child], block);
        }
        _breaks = oldBreaks;
        _continues = oldContinues;
        _canBreak = oldCanBreak;
        _canContinue = oldCanContinue;
    }

    private GscSymbol Hidden(string name, GscTextSpan span)
    {
        if (!_hiddenLocals.TryGetValue(name, out GscSymbol? symbol))
            _hiddenLocals.Add(name, symbol = new GscSymbol(GscSymbolKind.Local, name, span));
        return symbol;
    }

    private void Switch(GscSyntaxNode node, LocalBlock block, bool allocate)
    {
        if (!allocate) Read(node.Children[2], block);
        var oldBreaks = _breaks;
        bool oldCanBreak = _canBreak;
        _breaks = [];
        _canBreak = false;
        LocalBlock? current = null;
        var children = new List<LocalBlock>();
        bool hasDefault = false;
        int abort = 3;
        foreach (GscSyntaxNode item in GscSemanticSyntax.EnumerateStatementList(Node(node, 5)))
        {
            if (item.Production is GscProduction.CaseLabel or GscProduction.DefaultLabel)
            {
                current = Child(item, block, allocate);
                _canBreak = true;
                hasDefault |= item.Production == GscProduction.DefaultLabel;
            }
            else if (current is not null)
            {
                Visit(item, current, allocate);
                if (current.Abort != 0)
                {
                    if (allocate)
                    {
                        if (current.Abort == 2)
                        {
                            current.Abort = 0;
                            abort = 0;
                            AddChild(children, current, item.Span, allocate);
                        }
                        else abort = Math.Min(abort, current.Abort);
                    }
                    current = null;
                    _canBreak = false;
                }
            }
            else if (!allocate && item.Production != GscProduction.EmptyBlockItem)
                Report(GscDiagnosticCodes.MissingCaseStatement, item.Span, "missing case statement");
        }
        if (hasDefault)
        {
            if (current is not null)
            {
                AddChild(_breaks, current, node.Span, allocate);
                if (allocate) AddChild(children, current, node.Span, allocate);
            }
            if (allocate)
            {
                if (block.Abort == 0) block.Abort = abort;
                Append(_breaks, block);
                Merge(children, block);
            }
            else Init(_breaks, block);
        }
        _breaks = oldBreaks;
        _canBreak = oldCanBreak;
    }

    private LocalBlock Child(GscSyntaxNode key, LocalBlock parent, bool allocate)
    {
        if (allocate)
        {
            var child = new LocalBlock { Abort = parent.Abort };
            child.Locals.AddRange(parent.Locals);
            _blocks[key] = child;
            return child;
        }
        LocalBlock result = _blocks[key];
        Transfer(parent, result);
        return result;
    }

    private void Register(GscSymbol symbol, LocalBlock block)
    {
        if (block.Abort != 0 || block.Locals.Contains(symbol)) return;
        if (_defines.ContainsKey(symbol.Name))
            Report(GscDiagnosticCodes.VariableAlreadyDeclaredAsDefine, symbol.DeclarationSpan,
                "Variable is already declared as a define");
        CheckLocals(block.Locals.Count, symbol.DeclarationSpan);
        block.Locals.Add(symbol);
    }

    private void CheckLocals(int count, GscTextSpan span)
    {
        if (count == 64) Report(GscDiagnosticCodes.CompilerCapacityExceeded, span, "LOCAL_VAR_STACK_SIZE exceeded");
    }

    private void Append(IReadOnlyList<LocalBlock> children, LocalBlock parent)
    {
        if (children.Count == 0 || parent.Abort != 0) return;
        foreach (LocalBlock child in children) child.Abort = 0;
        foreach (GscSymbol symbol in children[0].Locals)
            if (children.All(child => child.Locals.Contains(symbol))) Register(symbol, parent);
    }

    private void Merge(IReadOnlyList<LocalBlock> children, LocalBlock parent)
    {
        if (parent.Abort != 0) return;
        foreach (LocalBlock child in children)
        {
            child.Public = parent.Locals.Count;
            Prefix(parent.Locals, child, parent.Locals.Count);
        }
    }

    private void Prefix(List<GscSymbol> locals, LocalBlock child, int count)
    {
        for (int i = 0; i < count; i++)
        {
            GscSymbol symbol = locals[i];
            if (!child.Locals.Remove(symbol)) CheckLocals(child.Locals.Count, symbol.DeclarationSpan);
            child.Locals.Insert(i, symbol);
        }
    }

    private void Transfer(LocalBlock parent, LocalBlock child)
    {
        // Inserting a parent local ahead of a child's public prefix extends
        // that prefix. The loop bound must grow with it (Scr_TransferBlock).
        for (int i = 0; i < child.Public || i < parent.Created; i++)
        {
            GscSymbol symbol = parent.Locals[i];
            int index = child.Locals.IndexOf(symbol);
            if (index < 0)
            {
                index = child.Locals.Count;
                CheckLocals(index, symbol.DeclarationSpan);
            }
            else child.Locals.RemoveAt(index);
            if (index >= child.Public) child.Public++;
            child.Locals.Insert(i, symbol);
        }
        child.Created = parent.Created;
        child.Initialized.UnionWith(parent.Initialized);
        child.Abort = 0;
    }

    private static void Init(IReadOnlyList<LocalBlock> children, LocalBlock parent)
    {
        if (children.Count == 0) return;
        parent.Created = children.Min(child => child.Public);
        foreach (GscSymbol symbol in parent.Locals.Take(parent.Created))
            if (children.All(child => child.Initialized.Contains(symbol))) parent.Initialized.Add(symbol);
    }

    private void AddChild(List<LocalBlock>? children, LocalBlock block, GscTextSpan span, bool allocate)
    {
        if (children is null || block.Abort != 0 || (!allocate && _developer)) return;
        if (children.Count == 1024)
            Report(GscDiagnosticCodes.CompilerCapacityExceeded, span, "MAX_SWITCH_CASES exceeded");
        children.Add(block);
    }

    private void LValue(GscSyntaxNode node, LocalBlock block, bool allocate, bool readWrite)
    {
        switch (node.Production)
        {
            case GscProduction.LocalLValue:
                Reference(GscSemanticSyntax.Token(node.Children[0]), block, allocate, create: !readWrite);
                break;
            case GscProduction.IndexLValue:
                if (!allocate) Read(node.Children[2], block);
                GscSyntaxNode receiver = Node(node, 0);
                if (receiver.Production == GscProduction.PrimaryLValueExpression)
                    LValue(Node(receiver, 0), block, allocate, readWrite);
                else if (!allocate && receiver.Production != GscProduction.GameExpression)
                    Report(GscDiagnosticCodes.InvalidObjectExpression, receiver.Span, "not an lvalue");
                break;
            default:
                if (!allocate) Read(node, block);
                break;
        }
    }

    private void Reference(GscSyntaxTokenElement token, LocalBlock block, bool allocate, bool create)
    {
        if (!_model.TryGetReference(token, out GscBoundReference reference) ||
            reference.Symbol.Kind is not (GscSymbolKind.Local or GscSymbolKind.Parameter)) return;
        if (!allocate && !create && _defines.ContainsKey(reference.Symbol.Name)) return;
        if (allocate) Register(reference.Symbol, block);
        else Access(reference.Symbol, token.Span, block, create);
    }

    private void Access(GscSymbol symbol, GscTextSpan span, LocalBlock block, bool create)
    {
        int index = block.Locals.IndexOf(symbol);
        block.Created = Math.Max(block.Created, index < 0 ? block.Locals.Count : index + 1);
        if (index < 0 && create)
            Report(GscDiagnosticCodes.UnreachableCode, span, "unreachable code");
        else if (!create && !block.Initialized.Contains(symbol))
            Report(GscDiagnosticCodes.UninitialisedVariable, span, $"uninitialised variable '{symbol.Name}'");
        else if (create) block.Initialized.Add(symbol);
    }

    private void Read(GscSyntaxElement element, LocalBlock block)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (element is GscSyntaxTokenElement token)
        {
            if (_model.TryGetReference(token, out GscBoundReference reference) &&
                reference.Kind is GscBoundReferenceKind.Read or GscBoundReferenceKind.ReadWrite)
                Reference(token, block, allocate: false, create: false);
        }
        else
        {
            GscSyntaxNode node = GscSemanticSyntax.Node(element);
            // Index and call arguments are emitted before their receivers.
            if (node.Production == GscProduction.IndexLValue)
            {
                Read(node.Children[2], block);
                Read(node.Children[0], block);
            }
            else foreach (GscSyntaxElement child in node.Children) Read(child, block);
        }
    }

    private void Report(string code, GscTextSpan span, string message) =>
        _diagnostics.Add(new GscDiagnostic(code, GscDiagnosticStage.Semantic,
            GscDiagnosticSeverity.Error, span, _source.GetLinePositionSpan(span), message));

    private static GscSyntaxNode Node(GscSyntaxNode node, int index) => GscSemanticSyntax.Node(node.Children[index]);

    private sealed class LocalBlock
    {
        internal List<GscSymbol> Locals { get; } = [];
        internal HashSet<GscSymbol> Initialized { get; } = [];
        internal int Abort;
        internal int Created;
        internal int Public;
    }
}
