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
            pair => FindAnimationReferences(
                pair.Value.Snapshot.Source,
                pair.Value.Analysis.SemanticModel,
                cancellationToken).ToArray());
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
            var visible = source.Functions.GroupBy(function => function.Name)
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
                foreach (GscFunctionDefinition function in imported.Functions)
                {
                    if (!visible.TryGetValue(function.Name, out List<GscFunctionDefinition>? definitions))
                        visible.Add(function.Name, [function]);
                    else
                    {
                        if (!function.DeveloperOnly && definitions.Any(existing => !existing.DeveloperOnly))
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
                if (pending.BuiltInTargets.Count == 0)
                {
                    IEnumerable<GscFunctionDefinition> candidates = [];
                    if (pending.QualifiedTarget is { } path)
                    {
                        if (sources.TryGetValue(path, out GscWorkspaceSourceDocument? target) &&
                            target.Analysis.SemanticModel is not null)
                            candidates = target.Functions.Where(function => function.Name == pending.Name);
                        else
                        {
                            available = false;
                            if (!pending.Developer && target is null)
                                Incomplete(pending.Location.Span, $"Cannot validate reference to '{path}': its source is not loaded.");
                        }
                    }
                    else
                    {
                        if (visible.TryGetValue(pending.Name, out List<GscFunctionDefinition>? definitions))
                            candidates = definitions;
                        available = !unavailableInclude;
                    }
                    targets = candidates.Where(function => pending.Developer || !function.DeveloperOnly).ToArray();
                    if (!pending.Developer && targets.Length == 0 && available)
                        Error(GscDiagnosticCodes.UnknownFunction, pending.Location.Span,
                            $"unknown function '{pending.SourceName}'");
                }
                references.Add(new GscSymbolReference(pending.Location, pending.Name, pending.SourceName,
                    pending.Kind, targets.Select(target => target.Symbol.Id), pending.QualifiedTarget, pending.BuiltInTargets));
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
        }
        return result;
    }

    internal static IEnumerable<(string Tree, string? Name, GscTextSpan Span)> FindAnimationReferences(
        GscSourceText sourceText, GscSemanticModel? model, CancellationToken cancellationToken)
    {
        if (model is null) yield break;
        string? tree = null;
        bool developer = false;
        var defines = new Dictionary<string, GscConstant>(StringComparer.OrdinalIgnoreCase);
        var constants = new GscConstantEvaluator(sourceText, defines, (_, _) => { }, cancellationToken);
        foreach (GscSyntaxNode item in GscSemanticSyntax.EnumerateTopLevelItems(GscSemanticSyntax.Node(model.SyntaxTree.Root.Children[2])))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.Production == GscProduction.DeveloperSectionOpen) developer = true;
            if (item.Production == GscProduction.DeveloperSectionClose) developer = false;
            if (item.Production == GscProduction.DefineDeclaration)
            {
                if (constants.Evaluate(GscSemanticSyntax.Node(item.Children[2])) is { } value)
                {
                    defines.TryAdd(sourceText.GetText(item.Children[0].Span), value);
                    constants.DefinesChanged();
                }
                continue;
            }
            if (item.Production == GscProduction.UsingAnimTreeDirective)
            {
                GscSyntaxTokenElement token = item.Children.OfType<GscSyntaxTokenElement>()
                    .First(child => child.Token.Kind == GscTokenKind.String);
                tree = GscConstantEvaluator.DecodeString(sourceText.GetText(token.Span)).ToLowerInvariant();
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
                Iw4GscBuiltInCatalog.ResolveCall(sourceText, node) is { DeveloperOnly: true }) yield break;
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
