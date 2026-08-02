using IW4.Gsc.Semantics;
using IW4.Gsc.Syntax;

namespace IW4.Gsc.Analysis;

/// <summary>The combined lexical, syntactic, and semantic result for one source snapshot.</summary>
public sealed class GscAnalysisResult
{
    private readonly IReadOnlyList<GscToken> _tokens;
    private readonly IReadOnlyList<GscDiagnostic> _diagnostics;

    public GscAnalysisResult(
        IEnumerable<GscToken> tokens,
        IEnumerable<GscDiagnostic> diagnostics)
        : this(tokens, diagnostics, semanticModel: null)
    {
    }

    internal GscAnalysisResult(
        IEnumerable<GscToken> tokens,
        IEnumerable<GscDiagnostic> diagnostics,
        GscSemanticModel? semanticModel)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(diagnostics);

        GscToken[] copiedTokens = tokens.ToArray();
        GscDiagnostic[] copiedDiagnostics = diagnostics.ToArray();
        if (copiedDiagnostics.Any(diagnostic => diagnostic is null))
        {
            throw new ArgumentException(
                "An analysis result cannot contain a null diagnostic.",
                nameof(diagnostics));
        }

        _tokens = Array.AsReadOnly(copiedTokens);
        _diagnostics = Array.AsReadOnly(copiedDiagnostics);
        SemanticModel = semanticModel;
    }

    public IReadOnlyList<GscToken> Tokens => _tokens;

    public IReadOnlyList<GscDiagnostic> Diagnostics => _diagnostics;

    public bool IsValid => !_diagnostics.Any(
        diagnostic => diagnostic.Severity == GscDiagnosticSeverity.Error);

    internal GscSemanticModel? SemanticModel { get; }
}
