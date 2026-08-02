using IW4.Gsc.Syntax;

namespace IW4.Gsc.Semantics;

internal static class GscSemanticSyntax
{
    internal static GscSyntaxNode Node(
        GscSyntaxElement element,
        string? role = null) =>
        element as GscSyntaxNode
        ?? throw new InvalidOperationException(
            $"Expected a syntax node{FormatRole(role)}.");

    internal static GscSyntaxTokenElement Token(
        GscSyntaxElement element,
        string? role = null) =>
        element as GscSyntaxTokenElement
        ?? throw new InvalidOperationException(
            $"Expected a syntax token{FormatRole(role)}.");

    internal static IEnumerable<GscSyntaxElement> EnumerateLeftRecursiveList(
        GscSyntaxNode list,
        GscProduction appendProduction,
        GscProduction emptyProduction,
        int itemChildIndex)
    {
        var reversed = new List<GscSyntaxElement>();
        GscSyntaxNode current = list;
        while (current.Production == appendProduction)
        {
            reversed.Add(current.Children[itemChildIndex]);
            current = Node(current.Children[0], "as the preceding list");
        }

        if (current.Production != emptyProduction)
        {
            throw new InvalidOperationException(
                $"Expected {emptyProduction} at the start of {appendProduction}.");
        }

        for (int index = reversed.Count - 1; index >= 0; index--)
            yield return reversed[index];
    }

    internal static IEnumerable<GscSyntaxNode> EnumerateStatementList(
        GscSyntaxNode list) =>
        EnumerateLeftRecursiveList(
                list,
                GscProduction.StatementListAppend,
                GscProduction.StatementListEmpty,
                itemChildIndex: 1)
            .Select(element => Node(element, "as a block item"));

    internal static IEnumerable<GscSyntaxNode> EnumerateTopLevelItems(
        GscSyntaxNode list) =>
        EnumerateLeftRecursiveList(
                list,
                GscProduction.TopLevelItemListAppend,
                GscProduction.TopLevelItemListEmpty,
                itemChildIndex: 1)
            .Select(element => Node(element, "as a top-level item"));

    internal static IEnumerable<GscSyntaxNode> EnumerateIncludes(
        GscSyntaxNode list) =>
        EnumerateLeftRecursiveList(
                list,
                GscProduction.IncludeListAppend,
                GscProduction.IncludeListEmpty,
                itemChildIndex: 1)
            .Select(element => Node(element, "as an include"));

    internal static IEnumerable<GscSyntaxNode> EnumerateExpressions(
        GscSyntaxNode list) =>
        EnumerateNonEmptyList(
            list,
            GscProduction.ExpressionListAppend,
            GscProduction.ExpressionListSingle,
            itemChildIndex: 2);

    internal static IEnumerable<GscSyntaxTokenElement> EnumerateParameters(
        GscSyntaxNode optionalParameters)
    {
        if (optionalParameters.Production == GscProduction.OptionalParameterListEmpty)
            yield break;
        if (optionalParameters.Production != GscProduction.OptionalParameterListPresent)
        {
            throw new InvalidOperationException(
                "Expected an optional parameter-list production.");
        }

        var reversed = new List<GscSyntaxTokenElement>();
        GscSyntaxNode current = Node(optionalParameters.Children[0]);
        while (current.Production == GscProduction.ParameterListAppend)
        {
            reversed.Add(Token(current.Children[2], "as a parameter name"));
            current = Node(current.Children[0], "as the preceding parameter list");
        }

        if (current.Production != GscProduction.ParameterListSingle)
            throw new InvalidOperationException("Expected a parameter list.");

        reversed.Add(Token(current.Children[0], "as a parameter name"));
        for (int index = reversed.Count - 1; index >= 0; index--)
            yield return reversed[index];
    }

    internal static IEnumerable<GscSyntaxNode> EnumerateNonEmptyList(
        GscSyntaxNode list,
        GscProduction appendProduction,
        GscProduction singleProduction,
        int itemChildIndex)
    {
        var reversed = new List<GscSyntaxNode>();
        GscSyntaxNode current = list;
        while (current.Production == appendProduction)
        {
            reversed.Add(Node(current.Children[itemChildIndex]));
            current = Node(current.Children[0], "as the preceding list");
        }

        if (current.Production != singleProduction)
        {
            throw new InvalidOperationException(
                $"Expected {singleProduction} at the start of {appendProduction}.");
        }

        reversed.Add(Node(current.Children[0]));
        for (int index = reversed.Count - 1; index >= 0; index--)
            yield return reversed[index];
    }

    internal static string Text(
        GscSourceText source,
        GscSyntaxTokenElement token) =>
        source.GetText(token.Token.Span);

    internal static string IdentifierText(
        GscSourceText source,
        GscSyntaxTokenElement token) =>
        Text(source, token).ToLowerInvariant();

    private static string FormatRole(string? role) =>
        role is null ? string.Empty : $" {role}";
}
