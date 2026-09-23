using IW4.Gsc.Semantics;
using IW4.Gsc.Syntax;

namespace IW4.Gsc.Analysis;

/// <summary>Runs the recovered grammar followed by high-confidence compiler checks.</summary>
public sealed class GscAnalyzer : IGscAnalyzer
{
    private readonly GscSyntaxAnalyzer _syntaxAnalyzer = new();

    public GscAnalysisResult Analyze(
        string source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Analyze(new GscSourceText(source), cancellationToken);
    }

    public GscAnalysisResult Analyze(
        GscSourceText source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        GscSyntaxResult syntax = _syntaxAnalyzer.Analyze(source, cancellationToken);
        if (!syntax.IsAccepted || syntax.SyntaxTree is null)
            return new GscAnalysisResult(syntax.Tokens, syntax.Diagnostics);

        GscSemanticAnalysis semantic =
            GscSemanticAnalyzer.Analyze(
                source,
                syntax.SyntaxTree,
                cancellationToken);
        return new GscAnalysisResult(
            syntax.Tokens,
            syntax.Diagnostics.Concat(semantic.Diagnostics),
            semantic.Model);
    }
}
