using IW4.Gsc.Syntax;

namespace IW4.Gsc.Semantics;

internal static class GscSemanticAnalyzer
{
    internal static GscSemanticAnalysis Analyze(
        GscSourceText source,
        GscSyntaxTree syntaxTree,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(syntaxTree);
        cancellationToken.ThrowIfCancellationRequested();

        GscBindingResult binding = GscBinder.Bind(
            source,
            syntaxTree,
            cancellationToken);
        IReadOnlyList<GscDiagnostic> contextDiagnostics =
            GscContextAnalyzer.Analyze(
                source,
                binding.Model,
                cancellationToken);
        IReadOnlyList<GscDiagnostic> assignmentDiagnostics =
            GscDefiniteAssignmentAnalyzer.Analyze(
                source,
                binding.Model,
                cancellationToken);

        IReadOnlyList<GscDiagnostic> diagnostics = Array.AsReadOnly(
            binding.Diagnostics
                .Concat(contextDiagnostics)
                .Concat(assignmentDiagnostics)
                .OrderBy(diagnostic => diagnostic.Span.Start)
                .ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
                .ToArray());
        return new GscSemanticAnalysis(binding.Model, diagnostics);
    }
}

internal sealed record GscSemanticAnalysis(
    GscSemanticModel Model,
    IReadOnlyList<GscDiagnostic> Diagnostics);
