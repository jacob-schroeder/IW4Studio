namespace IW4.Gsc.Syntax;

/// <summary>A source token or recovered production node in an accepted parse.</summary>
internal abstract class GscSyntaxElement
{
    protected GscSyntaxElement(GscTextSpan span)
    {
        Span = span;
    }

    internal GscTextSpan Span { get; }
}

/// <summary>A shifted scanner token. The program selector is marked synthetic.</summary>
internal sealed class GscSyntaxTokenElement : GscSyntaxElement
{
    internal GscSyntaxTokenElement(GscToken token, bool isSynthetic = false)
        : base(token.Span)
    {
        Token = token;
        IsSynthetic = isSynthetic;
    }

    internal GscToken Token { get; }

    internal bool IsSynthetic { get; }
}

/// <summary>An immutable node produced by one exact yacc reduction.</summary>
internal sealed class GscSyntaxNode : GscSyntaxElement
{
    private readonly IReadOnlyList<GscSyntaxElement> _children;

    internal GscSyntaxNode(
        GscProduction production,
        GscTextSpan span,
        IEnumerable<GscSyntaxElement> children)
        : base(span)
    {
        if (!Enum.IsDefined(production))
            throw new ArgumentOutOfRangeException(nameof(production));
        ArgumentNullException.ThrowIfNull(children);

        GscSyntaxElement[] copiedChildren = children.ToArray();
        if (copiedChildren.Any(child => child is null))
        {
            throw new ArgumentException(
                "A syntax node cannot contain a null child.",
                nameof(children));
        }

        int expectedChildCount = GscProductionFacts.GetRightHandSideLength(production);
        if (copiedChildren.Length != expectedChildCount)
        {
            throw new ArgumentException(
                $"Production {production} requires {expectedChildCount} children.",
                nameof(children));
        }

        Production = production;
        _children = Array.AsReadOnly(copiedChildren);
    }

    internal GscProduction Production { get; }

    internal GscNonterminal Nonterminal =>
        GscProductionFacts.GetLeftHandSide(Production);

    internal IReadOnlyList<GscSyntaxElement> Children => _children;
}

/// <summary>The recovered reduction tree for one accepted full source file.</summary>
internal sealed class GscSyntaxTree
{
    internal GscSyntaxTree(GscSyntaxNode root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (root.Production != GscProduction.Program)
        {
            throw new ArgumentException(
                "A full source tree must have the program production as its root.",
                nameof(root));
        }

        Root = root;
    }

    internal GscSyntaxNode Root { get; }
}
