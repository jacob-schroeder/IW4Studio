using IW4.Gsc.Analysis;
using IW4.Gsc.Semantics;
using IW4.Gsc.BuiltIns;
using IW4.Gsc.Syntax;

namespace IW4.Gsc.Workspace;

/// <summary>Resolves compiler references after every document has been indexed.</summary>
internal static class GscWorkspaceResolver
{
    internal static Dictionary<GscScriptPath, GscIndexedDocument> Resolve(
        IReadOnlyDictionary<GscScriptPath, GscWorkspaceSourceDocument> sources,
        IReadOnlyDictionary<string, GscSourceText> animationTrees,
        CancellationToken cancellationToken)
    {
        var animationReferences = sources.ToDictionary(pair => pair.Key,
            pair => FindAnimationReferences(pair.Value, cancellationToken).ToArray());
        var parsedTrees = new Dictionary<string, GscAnimationTree>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in animationReferences.Values.SelectMany(references => references).GroupBy(reference => reference.Tree))
            if (animationTrees.TryGetValue($"animtrees/{group.Key}.atr", out GscSourceText? treeSource))
                parsedTrees[group.Key] = new GscAnimationTree(treeSource,
                    group.Select(reference => reference.Name).OfType<string>()
                        .ToHashSet(StringComparer.OrdinalIgnoreCase), cancellationToken);
        var result = new Dictionary<GscScriptPath, GscIndexedDocument>();
        foreach (GscWorkspaceSourceDocument source in sources.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var diagnostics = source.Analysis.Diagnostics.ToList();
            var references = new List<GscSymbolReference>(source.References);
            var visible = source.Functions.Where(function => !function.DeveloperOnly).GroupBy(function => function.Name)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
            bool unavailableInclude = false;
            var includes = new HashSet<GscScriptPath>();
            foreach (GscIncludeReference include in source.Includes)
            {
                if (!includes.Add(include.TargetPath)) continue;
                if (!sources.TryGetValue(include.TargetPath, out GscWorkspaceSourceDocument? imported) ||
                    imported.Analysis.SemanticModel is null)
                {
                    unavailableInclude = true;
                    if (imported is null)
                        Incomplete(include.Location.Span, $"Cannot validate include '{include.TargetPath}': its source is not loaded.");
                    continue;
                }
                // TU1.11 imports a file's own function positions. Imported positions
                // live in a temporary link table and are not re-exported. Defines
                // are cleared at 0x20AC3C before dependencies are loaded.
                foreach (GscFunctionDefinition function in imported.Functions.Where(function => !function.DeveloperOnly))
                {
                    if (!visible.TryGetValue(function.Name, out List<GscFunctionDefinition>? definitions))
                        visible.Add(function.Name, [function]);
                    else
                    {
                        Error(GscDiagnosticCodes.FunctionAlreadyDefined, include.Location.Span,
                            $"function '{function.Name}' already defined");
                        definitions.Add(function);
                    }
                }
            }

            foreach (GscPendingFunctionReference pending in source.FunctionReferences)
            {
                cancellationToken.ThrowIfCancellationRequested();
                GscFunctionDefinition[] targets = [];
                bool available = true;
                if (!pending.Native && !pending.Developer)
                {
                    if (pending.QualifiedTarget is { } path)
                    {
                        if (sources.TryGetValue(path, out GscWorkspaceSourceDocument? target) &&
                            target.Analysis.SemanticModel is not null)
                            targets = target.Functions.Where(function => function.Name == pending.Name && !function.DeveloperOnly).ToArray();
                        else
                        {
                            available = false;
                            if (target is null)
                                Incomplete(pending.Location.Span, $"Cannot validate reference to '{path}': its source is not loaded.");
                        }
                    }
                    else
                    {
                        targets = visible.TryGetValue(pending.Name, out List<GscFunctionDefinition>? definitions)
                            ? definitions.ToArray() : [];
                        available = !unavailableInclude;
                    }
                    if (targets.Length == 0 && available)
                        Error(GscDiagnosticCodes.UnknownFunction, pending.Location.Span, "unknown function");
                }
                references.Add(new GscSymbolReference(pending.Location, pending.Name, pending.SourceName,
                    pending.Kind, targets.Select(target => target.Symbol.Id), pending.QualifiedTarget));
            }

            foreach (var reference in animationReferences[source.Snapshot.Path])
            {
                if (!parsedTrees.TryGetValue(reference.Tree, out GscAnimationTree? tree))
                {
                    if (reference.Name is null)
                        Incomplete(reference.Span, $"Cannot validate anim tree '{reference.Tree}': animtrees/{reference.Tree}.atr is not loaded.");
                }
                else if (tree.Error is { } error)
                {
                    if (reference.Name is null)
                    {
                        GscSourceText treeSource = animationTrees[$"animtrees/{reference.Tree}.atr"];
                        int line = treeSource.GetLinePosition(tree.ErrorOffset).Line + 1;
                        Error(GscDiagnosticCodes.AnimationTreeError, reference.Span,
                            $"{error} (animtrees/{reference.Tree}.atr, line {line})");
                    }
                }
                else if (reference.Name is { } name && !tree.Names.Contains(name))
                    Error(GscDiagnosticCodes.AnimationTreeError, reference.Span,
                        $"animation '{name}' not defined in anim tree '{reference.Tree}'");
            }
            CheckPrecache(source, 0, new HashSet<GscScriptPath>());
            var analysis = new GscAnalysisResult(source.Analysis.Tokens,
                diagnostics.DistinctBy(diagnostic => (diagnostic.Code, diagnostic.Span, diagnostic.Message))
                    .OrderBy(diagnostic => diagnostic.Span.Start).ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal),
                source.Analysis.SemanticModel);
            result.Add(source.Snapshot.Path, new GscIndexedDocument(source.Snapshot, analysis,
                source.Definitions, references, source.Includes, source.Functions, source.ObservedFields));

            void Error(string code, GscTextSpan span, string message) => Add(code, span, message, GscDiagnosticSeverity.Error);
            void Incomplete(GscTextSpan span, string message) => Add(GscDiagnosticCodes.ValidationIncomplete,
                span, message, GscDiagnosticSeverity.Warning);
            void Add(string code, GscTextSpan span, string message, GscDiagnosticSeverity severity) =>
                diagnostics.Add(new GscDiagnostic(code, GscDiagnosticStage.Semantic, severity,
                    span, source.Snapshot.Source.GetLinePositionSpan(span), message));

            // Precache entries are reserved for every include/far reference, not
            // unique file names; the reservation persists along recursive loads.
            void CheckPrecache(GscWorkspaceSourceDocument document, int reserved, HashSet<GscScriptPath> loaded)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!loaded.Add(document.Snapshot.Path)) return;
                var dependencies = document.Includes.Select(include => (Path: (GscScriptPath?)include.TargetPath, include.Location.Span, Developer: false))
                    .Concat(document.FunctionReferences
                        .Where(reference => reference.QualifiedTarget is not null)
                        .Select(reference => (Path: reference.QualifiedTarget, reference.Location.Span, reference.Developer))).ToArray();
                if (reserved + dependencies.Length > 1024)
                {
                    GscTextSpan span = document == source && dependencies.Length != 0
                        ? dependencies[Math.Clamp(1024 - reserved, 0, dependencies.Length - 1)].Span
                        : new GscTextSpan(0, Math.Min(1, source.Snapshot.Source.Length));
                    Error(GscDiagnosticCodes.CompilerCapacityExceeded, span, "MAX_PRECACHE_ENTRIES exceeded");
                    return;
                }
                int activeCount = dependencies.Count(dependency => !dependency.Developer);
                foreach (var dependency in dependencies)
                    if (!dependency.Developer && dependency.Path is { } path &&
                        sources.TryGetValue(path, out GscWorkspaceSourceDocument? child))
                        CheckPrecache(child, reserved + activeCount, loaded);
            }
        }
        // A dependency must compile too. Keep its own source locations in its
        // document and attach a summary to the caller's include/reference.
        var resolved = new Dictionary<GscScriptPath, GscIndexedDocument>(result);
        foreach (GscWorkspaceSourceDocument source in sources.Values)
        {
            GscIndexedDocument document = result[source.Snapshot.Path];
            var diagnostics = document.Analysis.Diagnostics.ToList();
            foreach (var dependency in Dependencies(source).Distinct())
            {
                var visited = new HashSet<GscScriptPath> { source.Snapshot.Path };
                var pending = new Stack<GscScriptPath>();
                pending.Push(dependency.Path);
                (GscScriptPath Path, GscDiagnostic Diagnostic)? finding = null;
                while (pending.TryPop(out GscScriptPath? path))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!visited.Add(path) || !result.TryGetValue(path, out GscIndexedDocument? target)) continue;
                    GscDiagnostic? error = target.Analysis.Diagnostics.FirstOrDefault(diagnostic =>
                        diagnostic.Severity == GscDiagnosticSeverity.Error);
                    if (error is not null) { finding = (path, error); break; }
                    GscDiagnostic? incomplete = target.Analysis.Diagnostics.FirstOrDefault(diagnostic =>
                        diagnostic.Code == GscDiagnosticCodes.ValidationIncomplete);
                    if (incomplete is not null) finding ??= (path, incomplete);
                    foreach (var next in Dependencies(sources[path])) pending.Push(next.Path);
                }
                if (finding is not { } issue) continue;
                bool isError = issue.Diagnostic.Severity == GscDiagnosticSeverity.Error;
                diagnostics.Add(new GscDiagnostic(
                    isError ? GscDiagnosticCodes.DependencyCompilationError : GscDiagnosticCodes.ValidationIncomplete,
                    GscDiagnosticStage.Semantic, issue.Diagnostic.Severity, dependency.Span,
                    source.Snapshot.Source.GetLinePositionSpan(dependency.Span),
                    isError ? $"Script '{issue.Path}' has a compilation error: {issue.Diagnostic.Message}"
                        : $"Validation of script '{issue.Path}' is incomplete: {issue.Diagnostic.Message}"));
            }
            if (diagnostics.Count == document.Analysis.Diagnostics.Count) continue;
            resolved[source.Snapshot.Path] = new GscIndexedDocument(source.Snapshot,
                new GscAnalysisResult(document.Analysis.Tokens,
                    diagnostics.OrderBy(diagnostic => diagnostic.Span.Start), document.Analysis.SemanticModel),
                document.Definitions, document.References, document.Includes, document.Functions, document.ObservedFields);
        }
        return resolved;
    }

    private static IEnumerable<(GscScriptPath Path, GscTextSpan Span)> Dependencies(GscWorkspaceSourceDocument source)
    {
        foreach (GscIncludeReference include in source.Includes) yield return (include.TargetPath, include.Location.Span);
        foreach (GscPendingFunctionReference reference in source.FunctionReferences)
            if (!reference.Developer && reference.QualifiedTarget is { } path) yield return (path, reference.Location.Span);
    }

    private static IEnumerable<(string Tree, string? Name, GscTextSpan Span)> FindAnimationReferences(
        GscWorkspaceSourceDocument source, CancellationToken cancellationToken)
    {
        if (source.Analysis.SemanticModel is not { } model) yield break;
        string? tree = null;
        bool developer = false;
        var defines = new Dictionary<string, GscConstant>(StringComparer.OrdinalIgnoreCase);
        var constants = new GscConstantEvaluator(source.Snapshot.Source, defines, (_, _) => { }, cancellationToken);
        foreach (GscSyntaxNode item in GscSemanticSyntax.EnumerateTopLevelItems(GscSemanticSyntax.Node(model.SyntaxTree.Root.Children[2])))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.Production == GscProduction.DeveloperSectionOpen) developer = true;
            if (item.Production == GscProduction.DeveloperSectionClose) developer = false;
            if (item.Production == GscProduction.DefineDeclaration)
            {
                if (constants.Evaluate(GscSemanticSyntax.Node(item.Children[2])) is { } value)
                {
                    defines.TryAdd(source.Snapshot.Source.GetText(item.Children[0].Span), value);
                    constants.DefinesChanged();
                }
                continue;
            }
            if (item.Production == GscProduction.UsingAnimTreeDirective)
            {
                GscSyntaxTokenElement token = item.Children.OfType<GscSyntaxTokenElement>()
                    .First(child => child.Token.Kind == GscTokenKind.String);
                tree = GscConstantEvaluator.DecodeString(source.Snapshot.Source.GetText(token.Span)).ToLowerInvariant();
                if (tree.All(character => char.IsAsciiLetterOrDigit(character) || character == '_'))
                    yield return (tree, null, token.Span);
                else tree = null;
            }
            else if (tree is not null && !developer)
                foreach (var animation in Animations(item))
                    yield return (tree, animation.Name, animation.Span);
        }

        IEnumerable<(string? Name, GscTextSpan Span)> Animations(GscSyntaxNode node)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node.Production == GscProduction.DeveloperBlockStatement ||
                Iw4GscBuiltInCatalog.ResolveCall(source.Snapshot.Source, node) is { DeveloperOnly: true }) yield break;
            if (node.Nonterminal == GscNonterminal.Expression && constants.Evaluate(node) is { Kind: GscConstantKind.PreAnimation } value)
            {
                yield return (value.Text, node.Span);
                yield break;
            }
            foreach (GscSyntaxNode child in node.Children.OfType<GscSyntaxNode>())
                foreach (var animation in Animations(child)) yield return animation;
        }
    }
}
