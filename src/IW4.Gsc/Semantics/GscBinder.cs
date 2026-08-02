using IW4.Gsc.Syntax;

namespace IW4.Gsc.Semantics;

internal sealed class GscBinder
{
    private readonly GscSourceText _source;
    private readonly GscSyntaxTree _syntaxTree;
    private readonly CancellationToken _cancellationToken;
    private readonly Dictionary<string, GscSymbol> _defines =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GscSymbol> _functions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GscSyntaxTokenElement> _includes =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(GscSyntaxNode Syntax, GscSymbol Symbol)> _functionDeclarations = [];
    private readonly List<GscBoundFunction> _boundFunctions = [];
    private readonly List<GscBoundReference> _references = [];
    private readonly Dictionary<int, GscBoundReference> _referencesByStart = [];
    private readonly List<GscDiagnostic> _diagnostics = [];
    private readonly Dictionary<GscBoundFunction, HashSet<string>> _defineConflicts = [];
    private int _operations;

    private GscBinder(
        GscSourceText source,
        GscSyntaxTree syntaxTree,
        CancellationToken cancellationToken)
    {
        _source = source;
        _syntaxTree = syntaxTree;
        _cancellationToken = cancellationToken;
    }

    internal static GscBindingResult Bind(
        GscSourceText source,
        GscSyntaxTree syntaxTree,
        CancellationToken cancellationToken)
    {
        var binder = new GscBinder(source, syntaxTree, cancellationToken);
        return binder.Bind();
    }

    private GscBindingResult Bind()
    {
        _cancellationToken.ThrowIfCancellationRequested();
        GscSyntaxNode root = _syntaxTree.Root;
        BindIncludes(GscSemanticSyntax.Node(root.Children[1]));
        BindTopLevelDeclarations(GscSemanticSyntax.Node(root.Children[2]));

        foreach ((GscSyntaxNode syntax, GscSymbol symbol) in _functionDeclarations)
        {
            ObserveCancellation();
            BindFunction(syntax, symbol);
        }

        var model = new GscSemanticModel(
            _syntaxTree,
            _defines,
            _boundFunctions,
            _references);
        return new GscBindingResult(
            model,
            Array.AsReadOnly(_diagnostics.ToArray()));
    }

    private void BindIncludes(GscSyntaxNode includeList)
    {
        foreach (GscSyntaxNode include in GscSemanticSyntax.EnumerateIncludes(includeList))
        {
            ObserveCancellation();
            GscSyntaxNode path = GscSemanticSyntax.Node(include.Children[1]);
            GscSyntaxTokenElement pathToken = GscSemanticSyntax.Token(path.Children[0]);
            string name = GscSemanticSyntax.Text(_source, pathToken);
            if (!_includes.TryAdd(name, pathToken))
            {
                AddDiagnostic(
                    GscDiagnosticCodes.DuplicateInclude,
                    pathToken.Token.Span,
                    "Duplicate #include");
            }
        }
    }

    private void BindTopLevelDeclarations(GscSyntaxNode topLevelList)
    {
        foreach (GscSyntaxNode item in GscSemanticSyntax.EnumerateTopLevelItems(topLevelList))
        {
            ObserveCancellation();
            switch (item.Production)
            {
                case GscProduction.FunctionDefinition:
                    DeclareFunction(item);
                    break;

                case GscProduction.DefineDeclaration:
                    DeclareDefine(item);
                    break;
            }
        }
    }

    private void DeclareFunction(GscSyntaxNode declaration)
    {
        GscSyntaxTokenElement nameToken = GscSemanticSyntax.Token(declaration.Children[0]);
        string name = GscSemanticSyntax.IdentifierText(_source, nameToken);
        var symbol = new GscSymbol(
            GscSymbolKind.Function,
            name,
            nameToken.Token.Span);
        if (!_functions.TryAdd(name, symbol))
        {
            AddDiagnostic(
                GscDiagnosticCodes.FunctionAlreadyDefined,
                nameToken.Token.Span,
                $"function '{name}' already defined");
        }

        AddReference(symbol, GscBoundReferenceKind.Declaration, nameToken);
        _functionDeclarations.Add((declaration, symbol));
    }

    private void DeclareDefine(GscSyntaxNode declaration)
    {
        GscSyntaxTokenElement nameToken = GscSemanticSyntax.Token(declaration.Children[0]);
        string name = GscSemanticSyntax.IdentifierText(_source, nameToken);
        var symbol = new GscSymbol(
            GscSymbolKind.Define,
            name,
            nameToken.Token.Span);
        if (!_defines.TryAdd(name, symbol))
        {
            AddDiagnostic(
                GscDiagnosticCodes.DuplicateDefine,
                nameToken.Token.Span,
                "Duplicate define");
            return;
        }

        AddReference(symbol, GscBoundReferenceKind.Declaration, nameToken);
    }

    private void BindFunction(GscSyntaxNode syntax, GscSymbol symbol)
    {
        var function = new GscBoundFunction(symbol, syntax);
        _boundFunctions.Add(function);
        _defineConflicts.Add(function, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        GscSyntaxNode optionalParameters = GscSemanticSyntax.Node(syntax.Children[2]);
        GscSyntaxTokenElement[] parameters =
            GscSemanticSyntax.EnumerateParameters(optionalParameters).ToArray();
        if (parameters.Length > 256)
        {
            AddDiagnostic(
                GscDiagnosticCodes.ParameterCountExceeded,
                parameters[256].Token.Span,
                "parameter count exceeds 256");
        }

        foreach (GscSyntaxTokenElement parameter in parameters)
        {
            string name = GscSemanticSyntax.IdentifierText(_source, parameter);
            ReportDefineConflict(function, name, parameter.Token.Span);
            GscSymbol parameterSymbol = function.DeclareParameter(
                name,
                parameter.Token.Span);
            AddReference(
                parameterSymbol,
                GscBoundReferenceKind.Declaration,
                parameter);
        }

        GscSyntaxNode body = GscSemanticSyntax.Node(syntax.Children[5]);
        CollectBindingSites(body, function);
        BindElement(body, function);
    }

    private void CollectBindingSites(
        GscSyntaxElement element,
        GscBoundFunction function)
    {
        ObserveCancellation();
        if (element is not GscSyntaxNode node)
            return;

        switch (node.Production)
        {
            case GscProduction.AssignmentStatement:
            case >= GscProduction.OrAssignmentStatement and
                <= GscProduction.ModuloAssignmentStatement:
            case GscProduction.IncrementStatement:
            case GscProduction.DecrementStatement:
                DeclareLValueRoot(GscSemanticSyntax.Node(node.Children[0]), function);
                break;

            case GscProduction.WaitTillStatement:
                foreach (GscSyntaxTokenElement output in EnumerateWaitTillOutputs(
                             GscSemanticSyntax.Node(node.Children[3])))
                {
                    DeclareLocal(output, function);
                }
                break;

            case GscProduction.KeyValueForeachStatement:
                DeclareLValueRoot(GscSemanticSyntax.Node(node.Children[2]), function);
                DeclareLValueRoot(GscSemanticSyntax.Node(node.Children[4]), function);
                break;

            case GscProduction.ValueForeachStatement:
                DeclareLValueRoot(GscSemanticSyntax.Node(node.Children[2]), function);
                break;

            case GscProduction.StatementListAppend:
            case GscProduction.StatementListEmpty:
                foreach (GscSyntaxNode item in GscSemanticSyntax.EnumerateStatementList(node))
                    CollectBindingSites(item, function);
                return;
        }

        foreach (GscSyntaxElement child in node.Children)
            CollectBindingSites(child, function);
    }

    private void DeclareLValueRoot(
        GscSyntaxNode lvalue,
        GscBoundFunction function)
    {
        if (TryGetAssignableRoot(lvalue, out GscSyntaxTokenElement root))
            DeclareLocal(root, function);
    }

    private void DeclareLocal(
        GscSyntaxTokenElement token,
        GscBoundFunction function)
    {
        string name = GscSemanticSyntax.IdentifierText(_source, token);
        if (ReportDefineConflict(function, name, token.Token.Span))
            return;

        function.DeclareLocal(name, token.Token.Span);
    }

    private bool ReportDefineConflict(
        GscBoundFunction function,
        string name,
        GscTextSpan span)
    {
        if (!_defines.ContainsKey(name) || !_defineConflicts[function].Add(name))
            return _defines.ContainsKey(name);

        AddDiagnostic(
            GscDiagnosticCodes.VariableAlreadyDeclaredAsDefine,
            span,
            "Variable is already declared as a define");
        return true;
    }

    private void BindElement(
        GscSyntaxElement element,
        GscBoundFunction function)
    {
        ObserveCancellation();
        if (element is not GscSyntaxNode node)
            return;

        switch (node.Production)
        {
            case GscProduction.AssignmentStatement:
                BindElement(node.Children[2], function);
                BindLValue(
                    GscSemanticSyntax.Node(node.Children[0]),
                    function,
                    GscBoundReferenceKind.Write);
                return;

            case >= GscProduction.OrAssignmentStatement and
                <= GscProduction.ModuloAssignmentStatement:
                BindElement(node.Children[2], function);
                BindLValue(
                    GscSemanticSyntax.Node(node.Children[0]),
                    function,
                    GscBoundReferenceKind.ReadWrite);
                return;

            case GscProduction.IncrementStatement:
            case GscProduction.DecrementStatement:
                BindLValue(
                    GscSemanticSyntax.Node(node.Children[0]),
                    function,
                    GscBoundReferenceKind.ReadWrite);
                return;

            case GscProduction.WaitTillStatement:
                BindElement(node.Children[0], function);
                GscSyntaxNode waitArguments = GscSemanticSyntax.Node(node.Children[3]);
                BindElement(GetWaitTillEventExpression(waitArguments), function);
                foreach (GscSyntaxTokenElement output in EnumerateWaitTillOutputs(waitArguments))
                {
                    BindIdentifier(output, function, GscBoundReferenceKind.Write);
                }
                return;

            case GscProduction.KeyValueForeachStatement:
                BindElement(node.Children[6], function);
                BindLValue(
                    GscSemanticSyntax.Node(node.Children[2]),
                    function,
                    GscBoundReferenceKind.Write);
                BindLValue(
                    GscSemanticSyntax.Node(node.Children[4]),
                    function,
                    GscBoundReferenceKind.Write);
                BindElement(node.Children[8], function);
                return;

            case GscProduction.ValueForeachStatement:
                BindElement(node.Children[4], function);
                BindLValue(
                    GscSemanticSyntax.Node(node.Children[2]),
                    function,
                    GscBoundReferenceKind.Write);
                BindElement(node.Children[6], function);
                return;

            case GscProduction.LocalLValue:
                BindIdentifier(
                    GscSemanticSyntax.Token(node.Children[0]),
                    function,
                    GscBoundReferenceKind.Read);
                return;

            case GscProduction.FieldLValue:
            case GscProduction.DebuggerSelfFieldLValue:
                BindElement(node.Children[0], function);
                return;

            case GscProduction.IndexLValue:
                BindElement(node.Children[0], function);
                BindElement(node.Children[2], function);
                return;

            case GscProduction.DebuggerObjectLValue:
            case GscProduction.AnimationExpression:
            case GscProduction.ScriptPathIdentifier:
            case GscProduction.ScriptPathPath:
                return;

            case GscProduction.NamedFunctionLocal:
                BindFunctionReference(GscSemanticSyntax.Token(node.Children[0]));
                return;

            case GscProduction.FunctionReferenceLocal:
                BindFunctionReference(GscSemanticSyntax.Token(node.Children[1]));
                return;

            case GscProduction.NamedFunctionQualified:
            case GscProduction.FunctionReferenceQualified:
                return;

            case GscProduction.StatementListAppend:
            case GscProduction.StatementListEmpty:
                foreach (GscSyntaxNode item in GscSemanticSyntax.EnumerateStatementList(node))
                    BindElement(item, function);
                return;

            case GscProduction.ExpressionListAppend:
            case GscProduction.ExpressionListSingle:
                foreach (GscSyntaxNode expression in GscSemanticSyntax.EnumerateExpressions(node))
                    BindElement(expression, function);
                return;
        }

        foreach (GscSyntaxElement child in node.Children)
            BindElement(child, function);
    }

    private void BindLValue(
        GscSyntaxNode lvalue,
        GscBoundFunction function,
        GscBoundReferenceKind rootKind)
    {
        switch (lvalue.Production)
        {
            case GscProduction.LocalLValue:
                BindIdentifier(
                    GscSemanticSyntax.Token(lvalue.Children[0]),
                    function,
                    rootKind);
                return;

            case GscProduction.IndexLValue:
                BindElement(lvalue.Children[2], function);
                GscSyntaxNode receiver = GscSemanticSyntax.Node(lvalue.Children[0]);
                if (receiver.Production == GscProduction.PrimaryLValueExpression)
                {
                    BindLValue(
                        GscSemanticSyntax.Node(receiver.Children[0]),
                        function,
                        rootKind);
                }
                else
                {
                    BindElement(receiver, function);
                }
                return;

            case GscProduction.FieldLValue:
            case GscProduction.DebuggerSelfFieldLValue:
                BindElement(lvalue.Children[0], function);
                return;

            case GscProduction.DebuggerObjectLValue:
                return;

            default:
                throw new InvalidOperationException(
                    $"Production {lvalue.Production} is not an lvalue.");
        }
    }

    private void BindIdentifier(
        GscSyntaxTokenElement token,
        GscBoundFunction function,
        GscBoundReferenceKind kind)
    {
        string name = GscSemanticSyntax.IdentifierText(_source, token);
        GscSymbol symbol;
        if (function.TryGetVariable(name, out GscSymbol variable))
        {
            symbol = variable;
        }
        else if (_defines.TryGetValue(name, out GscSymbol? define))
        {
            symbol = define;
        }
        else
        {
            symbol = function.DeclareLocal(name, token.Token.Span);
        }

        AddReference(symbol, kind, token);
    }

    private void BindFunctionReference(GscSyntaxTokenElement token)
    {
        string name = GscSemanticSyntax.IdentifierText(_source, token);
        if (_functions.TryGetValue(name, out GscSymbol? function))
            AddReference(function, GscBoundReferenceKind.Read, token);
    }

    private void AddReference(
        GscSymbol symbol,
        GscBoundReferenceKind kind,
        GscSyntaxTokenElement token)
    {
        int start = token.Token.Span.Start;
        if (_referencesByStart.TryGetValue(start, out GscBoundReference? existing))
        {
            if (!ReferenceEquals(existing.Symbol, symbol) || existing.Kind != kind)
            {
                throw new InvalidOperationException(
                    "A GSC token received inconsistent semantic bindings.");
            }
            return;
        }

        var reference = new GscBoundReference(symbol, kind, token.Token.Span);
        _referencesByStart.Add(start, reference);
        _references.Add(reference);
    }

    private static bool TryGetAssignableRoot(
        GscSyntaxNode lvalue,
        out GscSyntaxTokenElement root)
    {
        switch (lvalue.Production)
        {
            case GscProduction.LocalLValue:
                root = GscSemanticSyntax.Token(lvalue.Children[0]);
                return true;

            case GscProduction.IndexLValue:
                GscSyntaxNode receiver = GscSemanticSyntax.Node(lvalue.Children[0]);
                if (receiver.Production == GscProduction.PrimaryLValueExpression)
                {
                    return TryGetAssignableRoot(
                        GscSemanticSyntax.Node(receiver.Children[0]),
                        out root);
                }
                break;
        }

        root = null!;
        return false;
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
}
