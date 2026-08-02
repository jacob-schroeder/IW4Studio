using IW4.Gsc.Analysis;
using IW4.Gsc.Semantics;
using IW4.Gsc.Syntax;

namespace IW4.Gsc.Workspace;

/// <summary>Projects one exact syntax/binding result into immutable workspace facts.</summary>
internal static class GscDocumentIndexer
{
    internal static GscWorkspaceSourceDocument Build(
        GscDocumentSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        GscAnalysisResult analysis = new GscAnalyzer().Analyze(
            snapshot.Source,
            cancellationToken);
        if (analysis.SemanticModel is not { } model)
        {
            return new GscWorkspaceSourceDocument(
                snapshot,
                analysis,
                [],
                [],
                [],
                [],
                []);
        }

        var definitions = new List<GscSymbolDefinition>();
        var references = new List<GscSymbolReference>();
        var functions = new List<GscFunctionDefinition>();
        var definitionBySymbol = new Dictionary<GscSymbol, GscSymbolDefinition>();
        var parameterDefinitionBySpan = new Dictionary<GscTextSpan, GscSymbolDefinition>();

        foreach (GscSymbol define in model.Defines.Values)
            AddDefinition(define, containingFunction: null, parameterOrdinal: null);

        foreach (GscBoundFunction function in model.Functions)
        {
            GscSymbolDefinition functionDefinition = AddDefinition(
                function.Symbol,
                containingFunction: null,
                parameterOrdinal: null);
            var parameters = new List<GscSymbolDefinition>();
            GscSyntaxTokenElement[] parameterTokens = GscSemanticSyntax
                .EnumerateParameters(GscSemanticSyntax.Node(function.Syntax.Children[2]))
                .ToArray();
            if (parameterTokens.Length != function.Parameters.Count)
            {
                throw new InvalidOperationException(
                    "The bound parameter list does not match the recovered syntax tree.");
            }

            for (int ordinal = 0; ordinal < function.Parameters.Count; ordinal++)
            {
                GscSymbol parameter = function.Parameters[ordinal];
                GscTextSpan declarationSpan = parameterTokens[ordinal].Token.Span;
                GscSymbolDefinition definition;
                if (!definitionBySymbol.TryGetValue(parameter, out definition!))
                {
                    definition = AddDefinition(
                        parameter,
                        functionDefinition.Id,
                        ordinal);
                }
                else if (definition.Location.Span != declarationSpan)
                {
                    definition = CreateParameterOccurrence(
                        parameter,
                        functionDefinition.Id,
                        ordinal,
                        declarationSpan);
                }

                parameterDefinitionBySpan.Add(declarationSpan, definition);
                parameters.Add(definition);
            }

            foreach (GscSymbol variable in function.Variables.Values)
            {
                _ = AddDefinition(
                    variable,
                    functionDefinition.Id,
                    parameterOrdinal: null);
            }

            int signatureStart = function.Syntax.Children[0].Span.Start;
            int signatureEnd = function.Syntax.Children[3].Span.End;
            string declarationSignature = snapshot.Source.Text.Substring(
                signatureStart,
                signatureEnd - signatureStart);
            functions.Add(new GscFunctionDefinition(
                functionDefinition,
                parameters,
                declarationSignature));
        }

        foreach (GscBoundReference reference in model.References)
        {
            if (reference.Symbol.Kind == GscSymbolKind.Function &&
                reference.Kind != GscBoundReferenceKind.Declaration)
            {
                continue;
            }

            GscSymbolDefinition target =
                reference.Kind == GscBoundReferenceKind.Declaration &&
                reference.Symbol.Kind == GscSymbolKind.Parameter &&
                parameterDefinitionBySpan.TryGetValue(reference.Span, out var parameter)
                    ? parameter
                    : definitionBySymbol[reference.Symbol];
            references.Add(new GscSymbolReference(
                new GscSourceLocation(snapshot.Path, reference.Span),
                reference.Symbol.Name,
                ReadSource(reference.Span),
                ConvertReferenceKind(reference.Kind),
                [target.Id]));
        }

        return new GscWorkspaceSourceDocument(
            snapshot,
            analysis,
            definitions,
            references,
            ExtractIncludes(snapshot, model.SyntaxTree).ToArray(),
            functions,
            ExtractFunctionReferences(snapshot, model.SyntaxTree).ToArray());

        GscSymbolDefinition AddDefinition(
            GscSymbol symbol,
            GscSymbolId? containingFunction,
            int? parameterOrdinal)
        {
            if (definitionBySymbol.TryGetValue(symbol, out GscSymbolDefinition? existing))
                return existing;

            var location = new GscSourceLocation(snapshot.Path, symbol.DeclarationSpan);
            var id = new GscSymbolId(location, ConvertSymbolKind(symbol.Kind));
            var definition = new GscSymbolDefinition(
                id,
                symbol.Name,
                ReadSource(symbol.DeclarationSpan),
                containingFunction,
                parameterOrdinal);
            definitionBySymbol.Add(symbol, definition);
            definitions.Add(definition);
            return definition;
        }

        GscSymbolDefinition CreateParameterOccurrence(
            GscSymbol symbol,
            GscSymbolId containingFunction,
            int parameterOrdinal,
            GscTextSpan declarationSpan)
        {
            var location = new GscSourceLocation(snapshot.Path, declarationSpan);
            var id = new GscSymbolId(location, GscWorkspaceSymbolKind.Parameter);
            var definition = new GscSymbolDefinition(
                id,
                symbol.Name,
                ReadSource(declarationSpan),
                containingFunction,
                parameterOrdinal);
            definitions.Add(definition);
            return definition;
        }

        string ReadSource(GscTextSpan span) => snapshot.Source.Text.Substring(
            span.Start,
            span.Length);
    }

    private static IEnumerable<GscIncludeReference> ExtractIncludes(
        GscDocumentSnapshot snapshot,
        GscSyntaxTree tree)
    {
        GscSyntaxNode includeList = GscSemanticSyntax.Node(tree.Root.Children[1]);
        foreach (GscSyntaxNode include in GscSemanticSyntax.EnumerateIncludes(includeList))
        {
            GscSyntaxNode path = GscSemanticSyntax.Node(include.Children[1]);
            GscSyntaxTokenElement token = GscSemanticSyntax.Token(path.Children[0]);
            string text = GscSemanticSyntax.Text(snapshot.Source, token);
            yield return new GscIncludeReference(
                new GscSourceLocation(snapshot.Path, token.Token.Span),
                snapshot.Path.ResolveReference(text));
        }
    }

    private static IEnumerable<GscPendingFunctionReference> ExtractFunctionReferences(
        GscDocumentSnapshot snapshot,
        GscSyntaxTree tree)
    {
        foreach (GscSyntaxNode node in DescendantNodes(tree.Root))
        {
            GscSyntaxTokenElement nameToken;
            GscScriptPath? target = null;
            GscWorkspaceReferenceKind kind;
            switch (node.Production)
            {
                case GscProduction.NamedFunctionLocal:
                    nameToken = GscSemanticSyntax.Token(node.Children[0]);
                    kind = GscWorkspaceReferenceKind.Call;
                    break;

                case GscProduction.NamedFunctionQualified:
                    target = ResolveQualifiedTarget(snapshot, node.Children[0]);
                    nameToken = GscSemanticSyntax.Token(node.Children[2]);
                    kind = GscWorkspaceReferenceKind.Call;
                    break;

                case GscProduction.FunctionReferenceLocal:
                    nameToken = GscSemanticSyntax.Token(node.Children[1]);
                    kind = GscWorkspaceReferenceKind.FunctionReference;
                    break;

                case GscProduction.FunctionReferenceQualified:
                    target = ResolveQualifiedTarget(snapshot, node.Children[0]);
                    nameToken = GscSemanticSyntax.Token(node.Children[2]);
                    kind = GscWorkspaceReferenceKind.FunctionReference;
                    break;

                default:
                    continue;
            }

            yield return new GscPendingFunctionReference(
                new GscSourceLocation(snapshot.Path, nameToken.Token.Span),
                GscSemanticSyntax.IdentifierText(snapshot.Source, nameToken),
                GscSemanticSyntax.Text(snapshot.Source, nameToken),
                kind,
                target);
        }
    }

    private static GscScriptPath ResolveQualifiedTarget(
        GscDocumentSnapshot snapshot,
        GscSyntaxElement pathElement)
    {
        GscSyntaxNode path = GscSemanticSyntax.Node(pathElement);
        GscSyntaxTokenElement token = GscSemanticSyntax.Token(path.Children[0]);
        return snapshot.Path.ResolveReference(GscSemanticSyntax.Text(snapshot.Source, token));
    }

    private static IEnumerable<GscSyntaxNode> DescendantNodes(GscSyntaxElement element)
    {
        if (element is not GscSyntaxNode node)
            yield break;

        yield return node;
        foreach (GscSyntaxElement child in node.Children)
        {
            foreach (GscSyntaxNode descendant in DescendantNodes(child))
                yield return descendant;
        }
    }

    private static GscWorkspaceSymbolKind ConvertSymbolKind(GscSymbolKind kind) => kind switch
    {
        GscSymbolKind.Function => GscWorkspaceSymbolKind.Function,
        GscSymbolKind.Define => GscWorkspaceSymbolKind.Define,
        GscSymbolKind.Parameter => GscWorkspaceSymbolKind.Parameter,
        GscSymbolKind.Local => GscWorkspaceSymbolKind.Local,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static GscWorkspaceReferenceKind ConvertReferenceKind(
        GscBoundReferenceKind kind) => kind switch
    {
        GscBoundReferenceKind.Declaration => GscWorkspaceReferenceKind.Declaration,
        GscBoundReferenceKind.Read => GscWorkspaceReferenceKind.Read,
        GscBoundReferenceKind.Write => GscWorkspaceReferenceKind.Write,
        GscBoundReferenceKind.ReadWrite => GscWorkspaceReferenceKind.ReadWrite,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
