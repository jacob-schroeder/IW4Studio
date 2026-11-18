using System.Globalization;
using IW4.Gsc.Semantics;
using IW4.Gsc.Syntax;

namespace IW4.Gsc.Character;

public sealed partial class CharacterScriptProjection
{
    /// <summary>Reads the ordered model names from a stock-style xmodelalias main() function.</summary>
    public static bool TryReadHeadAlias(string source, out IReadOnlyList<string> models, out string? error)
    {
        models = [];
        error = "The head alias main() does not return a static array of model names.";
        var text = new GscSourceText(source);
        GscSyntaxTree? tree = new GscSyntaxAnalyzer().Analyze(text).SyntaxTree;
        if (tree is null)
        {
            error = "The head alias source contains a syntax error.";
            return false;
        }

        GscSyntaxNode[] main = FindFunctions(text, tree, "main");
        if (main.Length != 1 || !TakesNoParameters(main[0]))
            return false;

        GscSyntaxNode?[] statements = GscSemanticSyntax.EnumerateStatementList(Node(main[0], 5))
            .Where(item => item.Production != GscProduction.EmptyBlockItem)
            .Select(DirectOperation)
            .ToArray();
        if (statements.Length < 2 ||
            statements[^1] is not { Production: GscProduction.ReturnValueStatement } returned ||
            Unwrap(Node(returned, 1)) is not { Production: GscProduction.LocalLValue } array)
        {
            return false;
        }

        string arrayName = text.GetText(array.Span);
        var orderedModels = new string?[statements.Length - 1];
        foreach (GscSyntaxNode? statement in statements.Take(orderedModels.Length))
        {
            if (statement is not { Production: GscProduction.AssignmentStatement } ||
                Node(statement, 0) is not { Production: GscProduction.IndexLValue } target ||
                Unwrap(Node(target, 0)) is not { Production: GscProduction.LocalLValue } variable ||
                !string.Equals(text.GetText(variable.Span), arrayName, StringComparison.OrdinalIgnoreCase) ||
                Unwrap(Node(target, 2)) is not { Production: GscProduction.IntegerLiteral } subscript ||
                !int.TryParse(text.GetText(subscript.Span), NumberStyles.None, CultureInfo.InvariantCulture, out int index) ||
                index < 0 || index >= orderedModels.Length || orderedModels[index] is not null ||
                ReadString(text, Node(statement, 2)) is not { } model)
            {
                return false;
            }

            orderedModels[index] = model;
        }

        models = Array.AsReadOnly(orderedModels.OfType<string>().ToArray());
        error = null;
        return true;
    }

    private static GscSyntaxNode[] FindFunctions(GscSourceText text, GscSyntaxTree tree, string functionName)
    {
        bool developer = false;
        var matches = new List<GscSyntaxNode>();
        foreach (GscSyntaxNode item in GscSemanticSyntax.EnumerateTopLevelItems(Node(tree.Root, 2)))
        {
            if (item.Production == GscProduction.DeveloperSectionOpen)
                developer = true;
            else if (item.Production == GscProduction.DeveloperSectionClose)
                developer = false;
            else if (!developer && item.Production == GscProduction.FunctionDefinition &&
                     string.Equals(text.GetText(item.Children[0].Span), functionName, StringComparison.OrdinalIgnoreCase))
                matches.Add(item);
        }

        return matches.ToArray();
    }

    private static bool TakesNoParameters(GscSyntaxNode function) =>
        Node(function, 2).Production == GscProduction.OptionalParameterListEmpty;

    private static IEnumerable<(GscSyntaxNode Node, bool Direct)> FunctionOperations(GscSyntaxNode function)
    {
        var pending = new Stack<GscSyntaxNode>();
        foreach (GscSyntaxNode item in GscSemanticSyntax.EnumerateStatementList(Node(function, 5)))
        {
            GscSyntaxNode? direct = DirectOperation(item);
            pending.Push(item);
            while (pending.TryPop(out GscSyntaxNode? node))
            {
                yield return (node, node == direct);
                foreach (GscSyntaxNode child in node.Children.OfType<GscSyntaxNode>())
                    pending.Push(child);
            }
        }
    }

    private static GscSyntaxNode? DirectOperation(GscSyntaxNode item)
    {
        if (item.Production != GscProduction.BlockItemStatement ||
            Node(item, 0) is not { Production: GscProduction.TerminatedStatement } statement)
        {
            return null;
        }

        GscSyntaxNode core = Node(statement, 0);
        return core.Production is GscProduction.StatementCoreCall or GscProduction.StatementCoreSimple
            ? Node(core, 0) : null;
    }

    private static bool IsVoiceWrite(GscSourceText text, GscSyntaxNode node) => node.Production switch
    {
        GscProduction.AssignmentStatement or GscProduction.IncrementStatement or GscProduction.DecrementStatement or
            >= GscProduction.OrAssignmentStatement and <= GscProduction.ModuloAssignmentStatement => IsVoiceField(text, Node(node, 0)),
        GscProduction.ValueForeachStatement => IsVoiceField(text, Node(node, 2)),
        GscProduction.KeyValueForeachStatement => IsVoiceField(text, Node(node, 2)) || IsVoiceField(text, Node(node, 4)),
        _ => false
    };

    private static bool IsVoiceField(GscSourceText text, GscSyntaxNode target) =>
        target.Production == GscProduction.FieldLValue &&
        Unwrap(Node(target, 0)).Production == GscProduction.SelfExpression &&
        string.Equals(text.GetText(target.Children[2].Span), "voice", StringComparison.OrdinalIgnoreCase);

    private static NamedCall? ReadCall(GscSourceText text, GscSyntaxNode node)
    {
        bool method = node.Production == GscProduction.MethodCallExpression;
        if (!method && node.Production != GscProduction.CallExpression)
            return null;

        GscSyntaxNode kind = Node(node, method ? 1 : 0);
        GscSyntaxNode callable = kind.Children.OfType<GscSyntaxNode>().First();
        if (callable.Production != GscProduction.CallableNamedFunction)
            return null;

        GscSyntaxNode named = Node(callable, 0);
        bool qualified = named.Production == GscProduction.NamedFunctionQualified;
        GscTextSpan? pathSpan = qualified ? named.Children[0].Span : null;
        GscSyntaxNode optional = Node(node, method ? 3 : 2);
        GscSyntaxNode[] arguments = optional.Production == GscProduction.OptionalExpressionListPresent
            ? GscSemanticSyntax.EnumerateExpressions(Node(optional, 0)).ToArray() : [];

        return new NamedCall(
            text.GetText(named.Children[qualified ? 2 : 0].Span).ToLowerInvariant(),
            pathSpan is { } span ? text.GetText(span) : null,
            pathSpan,
            kind.Production == GscProduction.CallKindDirect,
            method,
            method && Unwrap(Node(node, 0)).Production == GscProduction.SelfExpression,
            arguments);
    }

    private static GscSyntaxNode Unwrap(GscSyntaxNode node)
    {
        while (true)
        {
            switch (node.Production)
            {
                case GscProduction.ExpressionFromPrimary:
                case GscProduction.PrimaryLValueExpression:
                case GscProduction.PrimaryCallExpression:
                    node = Node(node, 0);
                    break;

                case GscProduction.ParenthesizedExpressionList:
                    GscSyntaxNode optional = Node(node, 1);
                    if (optional.Production != GscProduction.OptionalExpressionListPresent ||
                        Node(optional, 0) is not { Production: GscProduction.ExpressionListSingle } list)
                    {
                        return node;
                    }
                    node = Node(list, 0);
                    break;

                default:
                    return node;
            }
        }
    }

    private static GscSyntaxNode Node(GscSyntaxNode parent, int index) => GscSemanticSyntax.Node(parent.Children[index]);

    private readonly record struct NamedCall(string Name, string? Path, GscTextSpan? PathSpan,
        bool IsDirect, bool IsMethod, bool IsSelfMethod, GscSyntaxNode[] Arguments);
}
