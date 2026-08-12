using System.Collections.ObjectModel;
using IW4.Gsc.Analysis;
using IW4.Gsc.Semantics;
using IW4.Gsc.Syntax;

namespace IW4.Gsc.Workspace;

/// <summary>Resolves host-neutral references after every document has been indexed.</summary>
internal static class GscWorkspaceResolver
{
    internal static Dictionary<GscScriptPath, GscIndexedDocument> Resolve(
        IReadOnlyDictionary<GscScriptPath, GscWorkspaceSourceDocument> sources,
        CancellationToken cancellationToken)
    {
        var definitionsByDocumentAndName = sources.Values.ToDictionary(
            source => source.Snapshot.Path,
            source => source.Definitions
                .GroupBy(definition => (definition.Kind, definition.Name))
                .ToDictionary(group => group.Key, group => group.ToArray()));
        var result = new Dictionary<GscScriptPath, GscIndexedDocument>();

        foreach (GscWorkspaceSourceDocument source in sources.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyDictionary<string, GscSymbolDefinition[]> importedDefines =
                CollectImportedDefines(
                    source.Snapshot.Path,
                    sources,
                    cancellationToken);
            List<GscSymbolReference> references = ResolveFunctionReferences(
                source,
                definitionsByDocumentAndName,
                cancellationToken);
            ResolvedImportedDefines imported = ResolveImportedDefines(
                references,
                importedDefines,
                cancellationToken);
            GscAnalysisResult analysis = CreateAnalysis(source, imported);

            result.Add(
                source.Snapshot.Path,
                new GscIndexedDocument(
                    source.Snapshot,
                    analysis,
                    source.Definitions,
                    imported.References,
                    source.Includes,
                    source.Functions,
                    source.ObservedFields));
        }

        return result;
    }

    private static List<GscSymbolReference> ResolveFunctionReferences(
        GscWorkspaceSourceDocument source,
        IReadOnlyDictionary<
            GscScriptPath,
            Dictionary<
                (GscSymbolKind Kind, string Name),
                GscSymbolDefinition[]>> definitions,
        CancellationToken cancellationToken)
    {
        var references = new List<GscSymbolReference>(source.References);
        foreach (GscPendingFunctionReference pending in source.FunctionReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GscScriptPath targetPath = pending.QualifiedTarget ?? source.Snapshot.Path;
            GscSymbolDefinition[] targets =
                definitions.TryGetValue(targetPath, out var byName) &&
                byName.TryGetValue(
                    (GscSymbolKind.Function, pending.Name),
                    out GscSymbolDefinition[]? matching)
                    ? matching
                    : [];
            references.Add(new GscSymbolReference(
                pending.Location,
                pending.Name,
                pending.SourceName,
                pending.Kind,
                targets.Select(target => target.Id),
                pending.QualifiedTarget));
        }

        return references;
    }

    private static ResolvedImportedDefines ResolveImportedDefines(
        IEnumerable<GscSymbolReference> references,
        IReadOnlyDictionary<string, GscSymbolDefinition[]> importedDefines,
        CancellationToken cancellationToken)
    {
        HashSet<GscSymbolId> conflicts = [];
        var resolved = new List<GscSymbolReference>();
        var suppressedUninitialisedSpans = new HashSet<GscTextSpan>();
        foreach (GscSymbolReference reference in references)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reference.Targets.Count == 1 &&
                reference.Targets[0] is
                    { Kind: GscSymbolKind.Local or GscSymbolKind.Parameter } local &&
                importedDefines.TryGetValue(
                    reference.Name,
                    out GscSymbolDefinition[]? imported))
            {
                resolved.Add(new GscSymbolReference(
                    reference.Location,
                    reference.Name,
                    reference.SourceName,
                    reference.Kind,
                    imported.Select(definition => definition.Id),
                    reference.QualifiedTargetPath));
                suppressedUninitialisedSpans.Add(reference.Location.Span);
                if (local.Kind == GscSymbolKind.Parameter ||
                    reference.Kind is GscWorkspaceReferenceKind.Write or
                        GscWorkspaceReferenceKind.ReadWrite)
                {
                    conflicts.Add(local);
                }
            }
            else
            {
                resolved.Add(reference);
            }
        }

        return new ResolvedImportedDefines(
            resolved,
            suppressedUninitialisedSpans,
            conflicts);
    }

    private static GscAnalysisResult CreateAnalysis(
        GscWorkspaceSourceDocument source,
        ResolvedImportedDefines imported)
    {
        var diagnostics = source.Analysis.Diagnostics
            .Where(diagnostic =>
                diagnostic.Code != GscDiagnosticCodes.UninitialisedVariable ||
                !imported.SuppressedUninitialisedSpans.Contains(diagnostic.Span))
            .ToList();
        foreach (GscSymbolId conflict in imported.Conflicts)
        {
            if (diagnostics.Any(diagnostic =>
                    diagnostic.Code == GscDiagnosticCodes.VariableAlreadyDeclaredAsDefine &&
                    diagnostic.Span == conflict.Declaration.Span))
            {
                continue;
            }

            diagnostics.Add(new GscDiagnostic(
                GscDiagnosticCodes.VariableAlreadyDeclaredAsDefine,
                GscDiagnosticStage.Semantic,
                GscDiagnosticSeverity.Error,
                conflict.Declaration.Span,
                source.Snapshot.Source.GetLinePositionSpan(conflict.Declaration.Span),
                "Variable is already declared as a define"));
        }

        return new GscAnalysisResult(
            source.Analysis.Tokens,
            diagnostics.OrderBy(diagnostic => diagnostic.Span.Start)
                .ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal),
            source.Analysis.SemanticModel);
    }

    private static IReadOnlyDictionary<string, GscSymbolDefinition[]> CollectImportedDefines(
        GscScriptPath sourcePath,
        IReadOnlyDictionary<GscScriptPath, GscWorkspaceSourceDocument> sources,
        CancellationToken cancellationToken)
    {
        var collected = new Dictionary<string, List<GscSymbolDefinition>>(
            StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<GscScriptPath> { sourcePath };
        Visit(sourcePath);
        return new ReadOnlyDictionary<string, GscSymbolDefinition[]>(
            collected.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.DistinctBy(definition => definition.Id).ToArray(),
                StringComparer.OrdinalIgnoreCase));

        void Visit(GscScriptPath path)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!sources.TryGetValue(path, out GscWorkspaceSourceDocument? document))
                return;

            foreach (GscIncludeReference include in document.Includes)
            {
                if (!visited.Add(include.TargetPath) ||
                    !sources.TryGetValue(
                        include.TargetPath,
                        out GscWorkspaceSourceDocument? included))
                {
                    continue;
                }

                foreach (GscSymbolDefinition define in included.Definitions.Where(
                             definition => definition.Kind == GscSymbolKind.Define))
                {
                    if (!collected.TryGetValue(
                            define.Name,
                            out List<GscSymbolDefinition>? values))
                    {
                        values = [];
                        collected.Add(define.Name, values);
                    }
                    values.Add(define);
                }

                Visit(include.TargetPath);
            }
        }
    }

    private sealed record ResolvedImportedDefines(
        IReadOnlyList<GscSymbolReference> References,
        IReadOnlySet<GscTextSpan> SuppressedUninitialisedSpans,
        IReadOnlySet<GscSymbolId> Conflicts);
}
