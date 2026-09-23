using IW4.Gsc.Syntax;

namespace IW4.Gsc.Analysis;

/// <summary>Analyzes one GSC source snapshot through syntax and semantics.</summary>
public interface IGscAnalyzer
{
    GscAnalysisResult Analyze(
        GscSourceText source,
        CancellationToken cancellationToken = default);
}
