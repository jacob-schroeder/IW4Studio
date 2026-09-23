namespace IW4.Gsc.Syntax;

/// <summary>The exact lexical and grammatical result for one source snapshot.</summary>
public sealed class GscSyntaxResult
{
    private readonly IReadOnlyList<GscToken> _tokens;
    private readonly IReadOnlyList<GscDiagnostic> _diagnostics;

    public GscSyntaxResult(
        IEnumerable<GscToken> tokens,
        IEnumerable<GscDiagnostic> diagnostics)
        : this(tokens, diagnostics, syntaxTree: null)
    {
    }

    internal GscSyntaxResult(
        IEnumerable<GscToken> tokens,
        IEnumerable<GscDiagnostic> diagnostics,
        GscSyntaxTree? syntaxTree)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(diagnostics);

        GscToken[] copiedTokens = tokens.ToArray();
        GscDiagnostic[] copiedDiagnostics = diagnostics.ToArray();
        if (copiedDiagnostics.Any(diagnostic => diagnostic is null))
        {
            throw new ArgumentException(
                "A syntax result cannot contain a null diagnostic.",
                nameof(diagnostics));
        }

        _tokens = Array.AsReadOnly(copiedTokens);
        _diagnostics = Array.AsReadOnly(copiedDiagnostics);
        SyntaxTree = syntaxTree;
    }

    /// <summary>Non-trivia source tokens. Synthetic selector and EOF tokens are omitted.</summary>
    public IReadOnlyList<GscToken> Tokens => _tokens;

    public IReadOnlyList<GscDiagnostic> Diagnostics => _diagnostics;

    /// <summary>The recovered reduction tree when the exact parser accepted.</summary>
    internal GscSyntaxTree? SyntaxTree { get; }

    public bool IsAccepted => !_diagnostics.Any(
        diagnostic => diagnostic.Severity == GscDiagnosticSeverity.Error);
}
