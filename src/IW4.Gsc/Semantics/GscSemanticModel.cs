using IW4.Gsc.Syntax;

namespace IW4.Gsc.Semantics;

internal enum GscSymbolKind
{
    Function,
    Define,
    Parameter,
    Local
}

internal sealed class GscSymbol
{
    internal GscSymbol(
        GscSymbolKind kind,
        string name,
        GscTextSpan declarationSpan)
    {
        Kind = kind;
        Name = name;
        DeclarationSpan = declarationSpan;
    }

    internal GscSymbolKind Kind { get; }

    internal string Name { get; }

    internal GscTextSpan DeclarationSpan { get; }
}

internal enum GscBoundReferenceKind
{
    Declaration,
    Read,
    Write,
    ReadWrite
}

internal sealed record GscBoundReference(
    GscSymbol Symbol,
    GscBoundReferenceKind Kind,
    GscTextSpan Span);

internal sealed class GscBoundFunction
{
    private readonly Dictionary<string, GscSymbol> _variables =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<GscSymbol> _parameters = [];

    internal GscBoundFunction(GscSymbol symbol, GscSyntaxNode syntax)
    {
        Symbol = symbol;
        Syntax = syntax;
    }

    internal GscSymbol Symbol { get; }

    internal GscSyntaxNode Syntax { get; }

    internal IReadOnlyDictionary<string, GscSymbol> Variables => _variables;

    internal IReadOnlyList<GscSymbol> Parameters => _parameters;

    internal bool TryGetVariable(string name, out GscSymbol symbol) =>
        _variables.TryGetValue(name, out symbol!);

    internal GscSymbol DeclareParameter(string name, GscTextSpan span)
    {
        if (!_variables.TryGetValue(name, out GscSymbol? symbol))
        {
            symbol = new GscSymbol(GscSymbolKind.Parameter, name, span);
            _variables.Add(name, symbol);
        }

        _parameters.Add(symbol);
        return symbol;
    }

    internal GscSymbol DeclareLocal(string name, GscTextSpan span)
    {
        if (!_variables.TryGetValue(name, out GscSymbol? symbol))
        {
            symbol = new GscSymbol(GscSymbolKind.Local, name, span);
            _variables.Add(name, symbol);
        }

        return symbol;
    }
}

internal sealed class GscSemanticModel
{
    private readonly IReadOnlyList<GscBoundFunction> _functions;
    private readonly IReadOnlyList<GscBoundReference> _references;
    private readonly IReadOnlyDictionary<int, GscBoundReference> _referencesByStart;

    internal GscSemanticModel(
        GscSyntaxTree syntaxTree,
        IReadOnlyDictionary<string, GscSymbol> defines,
        IEnumerable<GscBoundFunction> functions,
        IEnumerable<GscBoundReference> references)
    {
        SyntaxTree = syntaxTree;
        Defines = defines;
        _functions = Array.AsReadOnly(functions.ToArray());

        GscBoundReference[] copiedReferences = references.ToArray();
        _references = Array.AsReadOnly(copiedReferences);
        _referencesByStart = copiedReferences.ToDictionary(
            reference => reference.Span.Start);
    }

    internal GscSyntaxTree SyntaxTree { get; }

    internal IReadOnlyDictionary<string, GscSymbol> Defines { get; }

    internal IReadOnlyList<GscBoundFunction> Functions => _functions;

    internal IReadOnlyList<GscBoundReference> References => _references;

    internal bool TryGetReference(
        GscSyntaxTokenElement token,
        out GscBoundReference reference) =>
        _referencesByStart.TryGetValue(token.Token.Span.Start, out reference!);
}

internal sealed record GscBindingResult(
    GscSemanticModel Model,
    IReadOnlyList<GscDiagnostic> Diagnostics);
