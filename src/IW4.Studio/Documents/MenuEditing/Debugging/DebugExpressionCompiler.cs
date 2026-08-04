using IW4.Assets.Assets.Menu;

namespace IW4.Studio.Documents.MenuEditing.Debugging;

internal sealed class DebugExpressionCompiler
{
    private readonly ExpressionSupportingData? _supportingData;

    public DebugExpressionCompiler(ExpressionSupportingData? supportingData) =>
        _supportingData = supportingData;

    public MenuDebugExpression? Compile(Statement? statement)
    {
        if (statement is null)
            return null;

        var compiler = new DebugExpressionTreeCompiler(
            statement.LoadedEntries,
            statement.SupportingDataValue ?? _supportingData);
        DebugExpressionNode root = compiler.Compile();
        MenuDebugDependency[] dependencies = Discover(root).Distinct().ToArray();
        return new MenuDebugExpression(root, dependencies);
    }

    private static IEnumerable<MenuDebugDependency> Discover(DebugExpressionNode node)
    {
        switch (node)
        {
            case DebugUnaryExpressionNode unary:
                foreach (MenuDebugDependency dependency in Discover(unary.Operand))
                    yield return dependency;
                break;
            case DebugBinaryExpressionNode binary:
                foreach (MenuDebugDependency dependency in Discover(binary.Left))
                    yield return dependency;
                foreach (MenuDebugDependency dependency in Discover(binary.Right))
                    yield return dependency;
                break;
            case DebugCallExpressionNode call:
                if (call.Operation == OperationEnum.OP_LOCALIZESTRING)
                {
                    foreach (MenuDebugDependency dependency in
                             LocalizationDependencies(call))
                    {
                        yield return dependency;
                    }
                }
                else
                {
                    MenuDebugDependency? own = Dependency(call);
                    if (own is not null)
                        yield return own;
                }
                foreach (DebugExpressionNode argument in call.Arguments)
                {
                    foreach (MenuDebugDependency dependency in Discover(argument))
                        yield return dependency;
                }
                break;
        }
    }

    private static IEnumerable<MenuDebugDependency> LocalizationDependencies(
        DebugCallExpressionNode call)
    {
        bool foundLiteral = false;
        foreach (DebugExpressionNode argument in call.Arguments)
        {
            string? reference = LiteralString(argument);
            if (reference?.StartsWith('@') != true)
                continue;
            foundLiteral = true;
            yield return new MenuDebugDependency(
                MenuDebugDependencyKind.Localization,
                reference[1..],
                MenuDebugValueKind.String,
                call.Operation);
        }

        if (!foundLiteral)
        {
            yield return new MenuDebugDependency(
                MenuDebugDependencyKind.Localization,
                "<dynamic>",
                MenuDebugValueKind.String,
                call.Operation);
        }
    }

    private static MenuDebugDependency? Dependency(DebugCallExpressionNode call)
    {
        string? argument = call.StaticDvarName ?? LiteralString(call.Arguments.FirstOrDefault());
        string? qualifier = call.Arguments.Count == 0
            ? null
            : LiteralQualifier(call.Arguments) ?? "<dynamic>";
        MenuDebugValueKind? kind = RequestedKind(call.Operation);
        if (IsDvar(call.Operation))
        {
            return new MenuDebugDependency(
                MenuDebugDependencyKind.Dvar,
                argument ?? "<dynamic>",
                kind,
                call.Operation);
        }
        if (IsLocalVariable(call.Operation))
        {
            return new MenuDebugDependency(
                MenuDebugDependencyKind.LocalVariable,
                argument ?? "<dynamic>",
                kind,
                call.Operation);
        }

        return call.Operation switch
        {
            OperationEnum.OP_MILLISECONDS => new MenuDebugDependency(
                MenuDebugDependencyKind.Environment,
                "milliseconds",
                MenuDebugValueKind.Integer,
                call.Operation),
            OperationEnum.OP_MENUISOPEN => new MenuDebugDependency(
                MenuDebugDependencyKind.Menu,
                argument ?? "<dynamic>",
                MenuDebugValueKind.Boolean,
                call.Operation),
            OperationEnum.OP_GETFOCUSEDITEMNAME or
            OperationEnum.OP_GETFOCUSEDITEMX or
            OperationEnum.OP_GETFOCUSEDITEMY or
            OperationEnum.OP_GETFOCUSEDITEMWIDTH or
            OperationEnum.OP_GETFOCUSEDITEMHEIGHT => new MenuDebugDependency(
                MenuDebugDependencyKind.ItemGeometry,
                "<focused>",
                call.Operation == OperationEnum.OP_GETFOCUSEDITEMNAME
                    ? MenuDebugValueKind.String
                    : MenuDebugValueKind.Float,
                call.Operation),
            OperationEnum.OP_GETITEMX or
            OperationEnum.OP_GETITEMY or
            OperationEnum.OP_GETITEMWIDTH or
            OperationEnum.OP_GETITEMHEIGHT => new MenuDebugDependency(
                MenuDebugDependencyKind.ItemGeometry,
                argument ?? "<dynamic>",
                MenuDebugValueKind.Float,
                call.Operation),
            OperationEnum.OP_INT or
            OperationEnum.OP_FLOAT or
            OperationEnum.OP_STRING or
            OperationEnum.OP_SIN or
            OperationEnum.OP_COS or
            OperationEnum.OP_MIN or
            OperationEnum.OP_MAX => null,
            _ => new MenuDebugDependency(
                MenuDebugDependencyKind.Environment,
                qualifier ?? call.Operation.ToString(),
                null,
                call.Operation)
        };
    }

    private static string? LiteralQualifier(IReadOnlyList<DebugExpressionNode> arguments)
    {
        var values = new string[arguments.Count];
        for (int index = 0; index < arguments.Count; index++)
        {
            string? value = LiteralString(arguments[index]);
            if (value is null)
                return null;
            values[index] = value;
        }

        return string.Join(",", values);
    }

    private static string? LiteralString(DebugExpressionNode? node) => node switch
    {
        DebugLiteralExpressionNode literal => literal.Value.AsString(),
        _ => null
    };

    private static bool IsDvar(OperationEnum operation) => operation is
        OperationEnum.OP_STATICDVARINT or
        OperationEnum.OP_STATICDVARBOOL or
        OperationEnum.OP_STATICDVARFLOAT or
        OperationEnum.OP_STATICDVARSTRING or
        OperationEnum.OP_DVARINT or
        OperationEnum.OP_DVARBOOL or
        OperationEnum.OP_DVARFLOAT or
        OperationEnum.OP_DVARSTRING;

    private static bool IsLocalVariable(OperationEnum operation) => operation is
        OperationEnum.OP_LOCALVARINT or
        OperationEnum.OP_LOCALVARBOOL or
        OperationEnum.OP_LOCALVARFLOAT or
        OperationEnum.OP_LOCALVARSTRING;

    private static MenuDebugValueKind? RequestedKind(OperationEnum operation) => operation switch
    {
        OperationEnum.OP_STATICDVARINT or
        OperationEnum.OP_DVARINT or
        OperationEnum.OP_LOCALVARINT => MenuDebugValueKind.Integer,
        OperationEnum.OP_STATICDVARBOOL or
        OperationEnum.OP_DVARBOOL or
        OperationEnum.OP_LOCALVARBOOL => MenuDebugValueKind.Boolean,
        OperationEnum.OP_STATICDVARFLOAT or
        OperationEnum.OP_DVARFLOAT or
        OperationEnum.OP_LOCALVARFLOAT => MenuDebugValueKind.Float,
        OperationEnum.OP_STATICDVARSTRING or
        OperationEnum.OP_DVARSTRING or
        OperationEnum.OP_LOCALVARSTRING => MenuDebugValueKind.String,
        _ => null
    };
}
