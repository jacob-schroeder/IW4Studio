using IW4.Gsc.BuiltIns;
using IW4.Gsc.Syntax;

namespace IW4.Gsc.Semantics;

internal sealed partial class GscContextAnalyzer
{
    private void AnalyzeOperandStack(GscSyntaxNode function)
    {
        var stack = new OperandStack(this);
        stack.Statement(GscSemanticSyntax.Node(function.Children[5]));
        if (stack.Maximum + 32L * stack.MaximumCall > 2047)
            AddDiagnostic(GscDiagnosticCodes.CompilerCapacityExceeded, function.Children[0].Span,
                "function exceeds operand stack size");
    }

    // Mirrors EmitOpcode's offsets and Scr_PushValue's deferred constants.
    // These are operand counts, independent of the compiler's local slots.
    private sealed class OperandStack(GscContextAnalyzer owner)
    {
        private int _offset;
        private int _pending;
        internal int Maximum { get; private set; }
        internal int MaximumCall { get; private set; }

        private void Op(int offset, bool call = false)
        {
            _offset += _pending;
            _pending = 0;
            Maximum = Math.Max(Maximum, _offset);
            _offset += offset;
            Maximum = Math.Max(Maximum, _offset);
            if (call) MaximumCall = Math.Max(MaximumCall, _offset);
        }

        private void Push(GscTextSpan span)
        {
            if (_pending == 32)
                owner.AddDiagnostic(GscDiagnosticCodes.CompilerCapacityExceeded, span, "VALUE_STACK_SIZE exceeded");
            _pending++;
        }

        private void Expression(GscSyntaxNode node)
        {
            if (Evaluate(node)) Op(1);
        }

        internal bool Evaluate(GscSyntaxNode node)
        {
            owner.ObserveCancellation();
            switch (node.Production)
            {
                case GscProduction.ExpressionFromPrimary:
                case GscProduction.PrimaryLValueExpression:
                case GscProduction.PrimaryCallExpression:
                case GscProduction.FunctionReferenceExpression:
                    return Evaluate(Node(node, 0));
                case >= GscProduction.BitwiseOrExpression and <= GscProduction.ModuloExpression:
                    if (Evaluate(Node(node, 0)))
                    {
                        Push(node.Children[0].Span);
                        if (Evaluate(Node(node, 2)))
                        {
                            _pending--;
                            return true;
                        }
                    }
                    else Expression(Node(node, 2));
                    Op(-1);
                    return false;
                case GscProduction.ParenthesizedExpressionList:
                {
                    GscSyntaxNode[] values = Arguments(Node(node, 1));
                    if (values.Length == 1) return Evaluate(values[0]);
                    if (values.Length != 3) return false;
                    bool folded = true;
                    foreach (GscSyntaxNode value in values.Reverse())
                    {
                        if (folded)
                        {
                            folded = Evaluate(value);
                            if (folded) Push(value.Span);
                        }
                        else Expression(value);
                    }
                    if (folded) _pending -= 3;
                    else Op(-2);
                    return folded;
                }
                case GscProduction.LogicalAndExpression:
                case GscProduction.LogicalOrExpression:
                    Expression(Node(node, 0));
                    Op(-1);
                    Expression(Node(node, 2));
                    Op(0);
                    return false;
                case GscProduction.LogicalNotExpression:
                case GscProduction.BitwiseNotExpression:
                    Expression(Node(node, 1));
                    Op(0);
                    return false;
                case GscProduction.IndexLValue:
                    Expression(Node(node, 2));
                    Expression(Node(node, 0));
                    Op(-1);
                    return false;
                case GscProduction.FieldLValue:
                    FieldObject(Node(node, 0));
                    Op(1);
                    return false;
                case GscProduction.SizeExpression:
                    Expression(Node(node, 0));
                    Op(0);
                    return false;
                case GscProduction.CallExpression:
                case GscProduction.MethodCallExpression:
                    Call(node);
                    return false;
                case GscProduction.FunctionReferenceLocal:
                    Op(1, call: Iw4GscBuiltInCatalog.Multiplayer.FindCallablesByName(
                        owner._source.GetText(node.Children[1].Span)).Count != 0);
                    return false;
                default:
                    if (owner._constants.Evaluate(node) is not null) return true;
                    Op(1);
                    return false;
            }
        }

        private void FieldObject(GscSyntaxNode node)
        {
            if (node.Production == GscProduction.ExpressionFromPrimary)
            {
                FieldObject(Node(node, 0));
                return;
            }
            if (node.Production == GscProduction.ParenthesizedExpressionList &&
                Arguments(Node(node, 1)) is [var single])
            {
                FieldObject(single);
                return;
            }
            if (node.Production is GscProduction.SelfExpression or GscProduction.LevelExpression or GscProduction.AnimExpression)
                Op(0);
            else
            {
                Expression(node);
                Op(-1);
            }
        }

        private void LValue(GscSyntaxNode node)
        {
            switch (node.Production)
            {
                case GscProduction.IndexLValue:
                    Expression(Node(node, 2));
                    GscSyntaxNode receiver = Node(node, 0);
                    if (receiver.Production == GscProduction.PrimaryLValueExpression) LValue(Node(receiver, 0));
                    Op(-1);
                    break;
                case GscProduction.FieldLValue:
                    FieldObject(Node(node, 0));
                    Op(0);
                    break;
                default:
                    Op(0);
                    break;
            }
        }

        private void Call(GscSyntaxNode node)
        {
            bool method = node.Production == GscProduction.MethodCallExpression;
            GscSyntaxNode kind = Node(node, method ? 1 : 0);
            GscSyntaxNode callable = kind.Children.OfType<GscSyntaxNode>().First();
            bool native = Iw4GscBuiltInCatalog.ResolveCall(owner._source, node) is not null;
            bool thread = kind.Production is GscProduction.CallKindThread or GscProduction.CallKindChildThread;
            bool pointer = callable.Production == GscProduction.CallableFunctionPointer;
            if (!native && !thread) Op(1);
            GscSyntaxNode[] arguments = Arguments(Node(node, method ? 3 : 2));
            foreach (GscSyntaxNode argument in arguments.Reverse()) Expression(argument);
            if (method) Expression(Node(node, 0));
            if (pointer)
            {
                GscSyntaxNode expression = callable.Children.OfType<GscSyntaxNode>().First();
                Expression(expression);
            }
            int consumed = arguments.Length + (method ? 1 : 0) + (pointer ? 1 : 0);
            Op((native || thread ? 1 : 0) - consumed, call: true);
        }

        internal void Statement(GscSyntaxNode node)
        {
            owner.ObserveCancellation();
            switch (node.Production)
            {
                case GscProduction.StatementListAppend:
                case GscProduction.StatementListEmpty:
                    foreach (GscSyntaxNode item in GscSemanticSyntax.EnumerateStatementList(node)) Statement(item);
                    break;
                case GscProduction.BlockItemStatement:
                case GscProduction.TerminatedStatement:
                case GscProduction.StatementCoreSimple:
                case GscProduction.OptionalStatementCorePresent:
                    Statement(Node(node, 0));
                    break;
                case GscProduction.BlockStatement:
                case GscProduction.DeveloperBlockStatement:
                    Statement(Node(node, 1));
                    break;
                case GscProduction.StatementCoreCall:
                    Expression(Node(node, 0));
                    Op(-1);
                    break;
                case GscProduction.AssignmentStatement:
                    GscSyntaxNode rhs = Node(node, 2);
                    while (rhs.Production == GscProduction.ExpressionFromPrimary) rhs = Node(rhs, 0);
                    if (rhs.Production == GscProduction.UndefinedLiteral &&
                        Node(node, 0).Production is GscProduction.IndexLValue or GscProduction.FieldLValue)
                    {
                        LValue(Node(node, 0));
                        break;
                    }
                    Expression(Node(node, 2));
                    LValue(Node(node, 0));
                    Op(-1);
                    break;
                case >= GscProduction.OrAssignmentStatement and <= GscProduction.ModuloAssignmentStatement:
                    Expression(Node(node, 0));
                    Expression(Node(node, 2));
                    Op(-1);
                    LValue(Node(node, 0));
                    Op(-1);
                    break;
                case GscProduction.IncrementStatement:
                case GscProduction.DecrementStatement:
                    LValue(Node(node, 0));
                    Op(1);
                    Op(-1);
                    break;
                case GscProduction.ReturnValueStatement:
                case GscProduction.WaitStatement:
                    Expression(Node(node, 1));
                    Op(-1);
                    break;
                case GscProduction.IfStatement:
                case GscProduction.IfElseStatement:
                    Expression(Node(node, 2));
                    Op(-1);
                    Statement(Node(node, 4));
                    if (node.Production == GscProduction.IfElseStatement) Statement(Node(node, 6));
                    break;
                case GscProduction.WhileStatement:
                    Condition(Node(node, 2));
                    Statement(Node(node, 4));
                    break;
                case GscProduction.ForStatement:
                    Statement(Node(node, 2));
                    if (Node(node, 3).Production == GscProduction.OptionalExpressionPresent) Condition(Node(Node(node, 3), 0));
                    Statement(Node(node, 7));
                    Statement(Node(node, 5));
                    break;
                case GscProduction.ValueForeachStatement:
                case GscProduction.KeyValueForeachStatement:
                {
                    bool keyed = node.Production == GscProduction.KeyValueForeachStatement;
                    // The parser lowers foreach to array/key assignments and a
                    // for loop using getfirstarraykey/isdefined/getnextarraykey.
                    Expression(Node(node, keyed ? 6 : 4));
                    Op(-1);
                    Op(1); // array temporary
                    Op(0, call: true); // getfirstarraykey(array)
                    if (keyed) LValue(Node(node, 2));
                    Op(-1);
                    Key();
                    Op(0, call: true); // isdefined(key)
                    Op(-1);
                    Key();
                    Op(1); // array temporary
                    Op(-1); // array[key]
                    LValue(Node(node, keyed ? 4 : 2));
                    Op(-1);
                    Statement(Node(node, keyed ? 8 : 6));
                    Key();
                    Op(1); // array temporary (arguments are reversed)
                    Op(-1, call: true); // getnextarraykey(array, key)
                    if (keyed) LValue(Node(node, 2));
                    Op(-1);
                    break;

                    void Key()
                    {
                        if (keyed) Expression(Node(node, 2));
                        else Op(1);
                    }
                }
                case GscProduction.SwitchStatement:
                    Expression(Node(node, 2));
                    Op(-1);
                    Statement(Node(node, 5));
                    break;
                case GscProduction.WaitTillStatement:
                {
                    GscSyntaxNode arguments = Node(node, 3);
                    while (arguments.Production == GscProduction.WaitTillArgumentsAppendOutput) arguments = Node(arguments, 0);
                    Expression(Node(arguments, 0));
                    Expression(Node(node, 0));
                    Op(-2);
                    break;
                }
                case GscProduction.EndOnStatement:
                    Expression(Node(node, 3));
                    Expression(Node(node, 0));
                    Op(-2);
                    break;
                case GscProduction.NotifyStatement:
                case GscProduction.WaitTillMatchStatement:
                {
                    bool notify = node.Production == GscProduction.NotifyStatement;
                    if (notify) Op(1);
                    var values = new List<GscSyntaxNode>();
                    GscSyntaxNode list = Node(node, 3);
                    while (list.Children.Count == 3)
                    {
                        values.Add(Node(list, 2));
                        list = Node(list, 0);
                    }
                    values.Add(Node(list, 0));
                    foreach (GscSyntaxNode value in values) Expression(value);
                    Expression(Node(node, 0));
                    Op(-values.Count - 1 - (notify ? 1 : 0));
                    break;
                }
                default:
                    Op(0);
                    break;
            }
        }

        private void Condition(GscSyntaxNode node)
        {
            if (Evaluate(node))
            {
                if (owner._constants.Evaluate(node) is { IsNumeric: true }) return;
                Op(1);
            }
            Op(-1);
        }

        private static GscSyntaxNode Node(GscSyntaxNode node, int index) => GscSemanticSyntax.Node(node.Children[index]);
        private static GscSyntaxNode[] Arguments(GscSyntaxNode optional) =>
            optional.Production == GscProduction.OptionalExpressionListPresent
                ? GscSemanticSyntax.EnumerateExpressions(Node(optional, 0)).ToArray() : [];
    }
}
