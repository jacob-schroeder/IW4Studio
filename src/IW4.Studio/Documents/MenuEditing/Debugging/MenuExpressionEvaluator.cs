using IW4.Assets.Assets.Menu;

namespace IW4.Studio.Documents.MenuEditing.Debugging;

/// <summary>Evaluates immutable compiled Menu programs against explicit state.</summary>
public sealed partial class MenuExpressionEvaluator
{
    public static MenuExpressionEvaluator Default { get; } = new();

    public MenuEvaluatedState Evaluate(
        MenuDebugProgram program,
        MenuDebugScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(scenario);
        return new MenuEvaluationSession(this, program, scenario).Evaluate();
    }

    public MenuEvaluation<MenuDebugValue> EvaluateExpression(
        MenuDebugProgram program,
        MenuDebugExpression expression,
        MenuDebugScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(scenario);
        var session = new MenuEvaluationSession(this, program, scenario);
        return session.EvaluateExpression(expression);
    }

    internal MenuEvaluation<MenuDebugValue> EvaluateNode(
        DebugExpressionNode node,
        EvaluationContext context) => node switch
        {
            DebugLiteralExpressionNode literal =>
                MenuEvaluation<MenuDebugValue>.Known(literal.Value),
            DebugInvalidExpressionNode invalid => Error(
                invalid.Message,
                operation: null),
            DebugUnaryExpressionNode unary => EvaluateUnary(unary, context),
            DebugBinaryExpressionNode binary => EvaluateBinary(binary, context),
            DebugCallExpressionNode call => EvaluateCall(call, context),
            _ => Error("Unknown compiled expression node.", operation: null)
        };

    private MenuEvaluation<MenuDebugValue> EvaluateCall(
        DebugCallExpressionNode expression,
        EvaluationContext context)
    {
        if (expression.Operation == OperationEnum.OP_MILLISECONDS)
        {
            if (expression.Arguments.Count != 0)
                return ArityError(expression.Operation, 0, expression.Arguments.Count);
            return MenuEvaluation<MenuDebugValue>.Known(
                MenuDebugValue.FromInt(context.Scenario.Milliseconds),
                [EnvironmentDependency(expression.Operation, "milliseconds", MenuDebugValueKind.Integer)]);
        }

        if (IsDvar(expression.Operation))
        {
            return LookupNamedValue(
                expression,
                context,
                context.Scenario.Dvars,
                MenuDebugDependencyKind.Dvar);
        }
        if (IsLocalVariable(expression.Operation))
        {
            return LookupNamedValue(
                expression,
                context,
                context.Scenario.LocalVariables,
                MenuDebugDependencyKind.LocalVariable);
        }

        switch (expression.Operation)
        {
            case OperationEnum.OP_INT:
                return ConvertSingle(expression, context, MenuDebugValueKind.Integer);
            case OperationEnum.OP_FLOAT:
                return ConvertSingle(expression, context, MenuDebugValueKind.Float);
            case OperationEnum.OP_STRING:
                return ConvertSingle(expression, context, MenuDebugValueKind.String);
            case OperationEnum.OP_SIN:
                return MathSingle(expression, context, MathF.Sin);
            case OperationEnum.OP_COS:
                return MathSingle(expression, context, MathF.Cos);
            case OperationEnum.OP_MIN:
                return MathList(expression, context, minimum: true);
            case OperationEnum.OP_MAX:
                return MathList(expression, context, minimum: false);
            case OperationEnum.OP_MENUISOPEN:
                return MenuIsOpen(expression, context);
            case OperationEnum.OP_LOCALIZESTRING:
                return Localize(expression, context);
            case OperationEnum.OP_GETFOCUSEDITEMNAME:
                if (expression.Arguments.Count != 0)
                    return ArityError(expression.Operation, 0, expression.Arguments.Count);
                return FocusedItemName(expression.Operation, context);
            case OperationEnum.OP_GETFOCUSEDITEMX:
            case OperationEnum.OP_GETFOCUSEDITEMY:
            case OperationEnum.OP_GETFOCUSEDITEMWIDTH:
            case OperationEnum.OP_GETFOCUSEDITEMHEIGHT:
                if (expression.Arguments.Count != 0)
                    return ArityError(expression.Operation, 0, expression.Arguments.Count);
                return FocusedItemGeometry(expression.Operation, context);
            case OperationEnum.OP_GETITEMX:
            case OperationEnum.OP_GETITEMY:
            case OperationEnum.OP_GETITEMWIDTH:
            case OperationEnum.OP_GETITEMHEIGHT:
                return NamedItemGeometry(expression, context);
            default:
                return Environment(expression, context);
        }
    }

    private MenuEvaluation<MenuDebugValue> LookupNamedValue(
        DebugCallExpressionNode expression,
        EvaluationContext context,
        IReadOnlyDictionary<string, MenuDebugValue> values,
        MenuDebugDependencyKind dependencyKind)
    {
        if (expression.Arguments.Count != 1)
            return ArityError(expression.Operation, 1, expression.Arguments.Count);

        MenuEvaluation<MenuDebugValue>? nameResult = null;
        string? name = expression.StaticDvarName;
        if (name is null)
        {
            if (IsStaticDvar(expression.Operation))
            {
                string index = expression.Arguments[0] is DebugLiteralExpressionNode literal
                    ? literal.Value.AsString()
                    : "<dynamic>";
                var unresolved = new MenuDebugDependency(
                    dependencyKind,
                    $"<static:{index}>",
                    RequestedKind(expression.Operation),
                    expression.Operation);
                return Unknown(
                    $"The static dvar index '{index}' could not be resolved from expression supporting data.",
                    expression.Operation,
                    [unresolved]);
            }
            nameResult = EvaluateNode(expression.Arguments[0], context);
            if (!nameResult.IsKnown)
                return nameResult;
            name = nameResult.Value.AsString();
        }

        MenuDebugValueKind expected = RequestedKind(expression.Operation);
        var dependency = new MenuDebugDependency(
            dependencyKind,
            name,
            expected,
            expression.Operation);
        if (!values.TryGetValue(name, out MenuDebugValue value))
        {
            return Unknown(
                $"No {dependencyKind.ToString().ToLowerInvariant()} value was supplied for '{name}'.",
                expression.Operation,
                [dependency],
                nameResult is null ? [] : [nameResult]);
        }

        MenuEvaluation<MenuDebugValue> converted = Convert(
            value,
            expected,
            expression.Operation,
            [dependency],
            nameResult is null ? [] : [nameResult]);
        return converted;
    }

    private MenuEvaluation<MenuDebugValue> ConvertSingle(
        DebugCallExpressionNode expression,
        EvaluationContext context,
        MenuDebugValueKind kind)
    {
        if (expression.Arguments.Count != 1)
            return ArityError(expression.Operation, 1, expression.Arguments.Count);
        MenuEvaluation<MenuDebugValue> value = EvaluateNode(expression.Arguments[0], context);
        return value.IsKnown
            ? Convert(value.Value, kind, expression.Operation, [], [value])
            : value;
    }

    private MenuEvaluation<MenuDebugValue> MathSingle(
        DebugCallExpressionNode expression,
        EvaluationContext context,
        Func<float, float> operation)
    {
        if (expression.Arguments.Count != 1)
            return ArityError(expression.Operation, 1, expression.Arguments.Count);
        MenuEvaluation<MenuDebugValue> value = EvaluateNode(expression.Arguments[0], context);
        if (!value.IsKnown)
            return value;
        if (!value.Value.TryGetFloat(out float numeric))
            return ConversionError(expression.Operation, value);
        return Known(MenuDebugValue.FromFloat(operation(numeric)), value);
    }

    private MenuEvaluation<MenuDebugValue> MathList(
        DebugCallExpressionNode expression,
        EvaluationContext context,
        bool minimum)
    {
        if (expression.Arguments.Count == 0)
            return AtLeastOneArityError(expression.Operation);

        var values = new List<MenuEvaluation<MenuDebugValue>>(expression.Arguments.Count);
        foreach (DebugExpressionNode argument in expression.Arguments)
            values.Add(EvaluateNode(argument, context));
        if (values.Any(value => !value.IsKnown))
            return MergeUnavailable(values.ToArray());
        if (!values[0].Value.TryGetFloat(out float result))
            return ConversionError(expression.Operation, values.ToArray());

        for (int index = 1; index < values.Count; index++)
        {
            if (!values[index].Value.TryGetFloat(out float candidate))
                return ConversionError(expression.Operation, values.ToArray());
            if (minimum ? result > candidate : result < candidate)
                result = candidate;
        }
        return Known(MenuDebugValue.FromFloat(result), values.ToArray());
    }

    private MenuEvaluation<MenuDebugValue> MenuIsOpen(
        DebugCallExpressionNode expression,
        EvaluationContext context)
    {
        if (expression.Arguments.Count != 1)
            return ArityError(expression.Operation, 1, expression.Arguments.Count);
        MenuEvaluation<MenuDebugValue> name = EvaluateArgument(expression, context, 0);
        if (!name.IsKnown)
            return name;
        string menuName = name.Value.AsString();
        var dependency = new MenuDebugDependency(
            MenuDebugDependencyKind.Menu,
            menuName,
            MenuDebugValueKind.Boolean,
            expression.Operation);
        return MenuEvaluation<MenuDebugValue>.Known(
            MenuDebugValue.FromBoolean(context.Scenario.OpenMenus.Contains(menuName)),
            name.Dependencies.Append(dependency),
            name.Trace);
    }

    private MenuEvaluation<MenuDebugValue> Localize(
        DebugCallExpressionNode expression,
        EvaluationContext context)
    {
        var arguments = new List<MenuEvaluation<MenuDebugValue>>(expression.Arguments.Count);
        foreach (DebugExpressionNode argument in expression.Arguments)
            arguments.Add(EvaluateNode(argument, context));
        if (arguments.Any(value => !value.IsKnown))
            return MergeUnavailable(arguments.ToArray());

        if (arguments.Count != 1 ||
            arguments[0].Value.Kind != MenuDebugValueKind.String)
        {
            MenuDebugDependency[] dependencies = arguments
                .Where(value => value.Value.Kind == MenuDebugValueKind.String)
                .Select(value => value.Value.AsString())
                .Where(value => value.StartsWith('@'))
                .Select(value => new MenuDebugDependency(
                    MenuDebugDependencyKind.Localization,
                    value[1..],
                    MenuDebugValueKind.String,
                    expression.Operation))
                .ToArray();
            return Unknown(
                "The engine's multi-value localization formatter is not available in the editor scenario resolver.",
                expression.Operation,
                dependencies,
                arguments);
        }

        MenuEvaluation<MenuDebugValue> reference = arguments[0];
        if (!reference.Value.AsString().StartsWith('@'))
        {
            return Error(
                "A localized expression string must start with '@'.",
                expression.Operation,
                reference);
        }
        return ResolveLocalization(
            reference.Value.AsString(),
            expression.Operation,
            context,
            reference);
    }

    private MenuEvaluation<MenuDebugValue> FocusedItemName(
        OperationEnum operation,
        EvaluationContext context)
    {
        var dependency = new MenuDebugDependency(
            MenuDebugDependencyKind.ItemGeometry,
            "<focused>",
            MenuDebugValueKind.String,
            operation);
        if (context.Scenario.FocusedItemId is not { } focusedId)
            return Unknown("No focused item is configured.", operation, [dependency]);
        MenuDebugItemProgram? item = context.Program.Items.FirstOrDefault(value => value.Id == focusedId);
        if (item is null)
            return Unknown("The focused item is not part of this Menu.", operation, [dependency]);
        return MenuEvaluation<MenuDebugValue>.Known(
            MenuDebugValue.FromString(item.Name ?? string.Empty),
            [dependency]);
    }

    private MenuEvaluation<MenuDebugValue> FocusedItemGeometry(
        OperationEnum operation,
        EvaluationContext context)
    {
        var dependency = new MenuDebugDependency(
            MenuDebugDependencyKind.ItemGeometry,
            "<focused>",
            MenuDebugValueKind.Float,
            operation);
        if (context.Scenario.FocusedItemId is not { } focusedId)
            return Unknown("No focused item is configured.", operation, [dependency]);
        return Geometry(operation, focusedId, dependency, context);
    }

    private MenuEvaluation<MenuDebugValue> NamedItemGeometry(
        DebugCallExpressionNode expression,
        EvaluationContext context)
    {
        if (expression.Arguments.Count != 1)
            return ArityError(expression.Operation, 1, expression.Arguments.Count);
        MenuEvaluation<MenuDebugValue> name = EvaluateArgument(expression, context, 0);
        if (!name.IsKnown)
            return name;
        string itemName = name.Value.AsString();
        var dependency = new MenuDebugDependency(
            MenuDebugDependencyKind.ItemGeometry,
            itemName,
            MenuDebugValueKind.Float,
            expression.Operation);
        MenuDebugItemProgram? item = context.Program.Items.FirstOrDefault(value =>
            string.Equals(value.Name, itemName, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            return Unknown(
                $"Menu item '{itemName}' was not found.",
                expression.Operation,
                name.Dependencies.Append(dependency),
                [name]);
        }
        return Geometry(expression.Operation, item.Id, dependency, context, name);
    }

    private MenuEvaluation<MenuDebugValue> Geometry(
        OperationEnum operation,
        MenuNodeId itemId,
        MenuDebugDependency dependency,
        EvaluationContext context,
        params MenuEvaluation<MenuDebugValue>[] inputs)
    {
        MenuEvaluatedRectangle rectangle = context.ResolveItemRectangle(itemId);
        MenuEvaluation<float> component = operation switch
        {
            OperationEnum.OP_GETFOCUSEDITEMX or OperationEnum.OP_GETITEMX => rectangle.X,
            OperationEnum.OP_GETFOCUSEDITEMY or OperationEnum.OP_GETITEMY => rectangle.Y,
            OperationEnum.OP_GETFOCUSEDITEMWIDTH or OperationEnum.OP_GETITEMWIDTH => rectangle.Width,
            OperationEnum.OP_GETFOCUSEDITEMHEIGHT or OperationEnum.OP_GETITEMHEIGHT => rectangle.Height,
            _ => throw new InvalidOperationException($"'{operation}' is not a geometry operation.")
        };
        IEnumerable<MenuDebugDependency> dependencies = inputs
            .SelectMany(value => value.Dependencies)
            .Concat(component.Dependencies)
            .Append(dependency);
        IEnumerable<MenuEvaluationTraceEntry> trace = inputs
            .SelectMany(value => value.Trace)
            .Concat(component.Trace);
        return component.Status switch
        {
            MenuEvaluationStatus.Known => MenuEvaluation<MenuDebugValue>.Known(
                MenuDebugValue.FromFloat(component.Value),
                dependencies,
                trace),
            MenuEvaluationStatus.Unknown => MenuEvaluation<MenuDebugValue>.Unknown(
                default,
                dependencies,
                trace),
            _ => MenuEvaluation<MenuDebugValue>.Error(default, dependencies, trace)
        };
    }

    private MenuEvaluation<MenuDebugValue> Environment(
        DebugCallExpressionNode expression,
        EvaluationContext context)
    {
        var arguments = new List<MenuEvaluation<MenuDebugValue>>(expression.Arguments.Count);
        foreach (DebugExpressionNode argument in expression.Arguments)
            arguments.Add(EvaluateNode(argument, context));
        if (arguments.Any(value => !value.IsKnown))
            return MergeUnavailable(arguments.ToArray());

        string? qualifier = arguments.Count == 0
            ? null
            : string.Join(",", arguments.Select(value => value.Value.AsString()));
        MenuDebugValueKind? resultKind =
            MenuExpressionOperationMetadata.EnvironmentResultKind(
                expression.Operation);
        var dependency = EnvironmentDependency(
            expression.Operation,
            qualifier,
            resultKind);
        if (!context.Scenario.Environment.TryGetValue(
                new MenuDebugEnvironmentKey(expression.Operation, qualifier),
                out MenuDebugValue value) &&
            !context.Scenario.Environment.TryGetValue(
                new MenuDebugEnvironmentKey(expression.Operation),
                out value))
        {
            return Unknown(
                $"No simulated environment value was supplied for '{new MenuDebugEnvironmentKey(expression.Operation, qualifier)}'.",
                expression.Operation,
                arguments.SelectMany(item => item.Dependencies).Append(dependency),
                arguments);
        }

        if (resultKind is { } expectedKind)
        {
            return Convert(
                value,
                expectedKind,
                expression.Operation,
                [dependency],
                arguments);
        }

        return MenuEvaluation<MenuDebugValue>.Known(
            value,
            arguments.SelectMany(item => item.Dependencies).Append(dependency),
            arguments.SelectMany(item => item.Trace));
    }

    private MenuEvaluation<MenuDebugValue> EvaluateArgument(
        DebugCallExpressionNode expression,
        EvaluationContext context,
        int index)
    {
        if (expression.Arguments.Count <= index)
            return ArityError(expression.Operation, index + 1, expression.Arguments.Count);
        return EvaluateNode(expression.Arguments[index], context);
    }

    internal static MenuEvaluation<MenuDebugValue> ResolveLocalization(
        string reference,
        OperationEnum? operation,
        EvaluationContext context,
        params MenuEvaluation<MenuDebugValue>[] inputs)
    {
        string key = reference.StartsWith('@') ? reference[1..] : reference;
        var dependency = new MenuDebugDependency(
            MenuDebugDependencyKind.Localization,
            key,
            MenuDebugValueKind.String,
            operation);
        if (context.Scenario.LocalizationResolver is null)
        {
            return Unknown(
                $"No localization resolver is configured for '{key}'.",
                operation,
                inputs.SelectMany(value => value.Dependencies).Append(dependency),
                inputs);
        }

        string? localized = context.Scenario.LocalizationResolver(key);
        if (localized is null)
        {
            return Unknown(
                $"Localization reference '{key}' was not resolved.",
                operation,
                inputs.SelectMany(value => value.Dependencies).Append(dependency),
                inputs);
        }

        return MenuEvaluation<MenuDebugValue>.Known(
            MenuDebugValue.FromString(localized),
            inputs.SelectMany(value => value.Dependencies).Append(dependency),
            inputs.SelectMany(value => value.Trace));
    }

    private static MenuEvaluation<MenuDebugValue> Convert(
        MenuDebugValue value,
        MenuDebugValueKind kind,
        OperationEnum operation,
        IEnumerable<MenuDebugDependency> dependencies,
        IEnumerable<MenuEvaluation<MenuDebugValue>> inputs)
    {
        MenuDebugValue converted;
        bool success;
        switch (kind)
        {
            case MenuDebugValueKind.Integer:
                success = value.TryGetInt(out int integer);
                converted = MenuDebugValue.FromInt(integer);
                break;
            case MenuDebugValueKind.Float:
                success = value.TryGetFloat(out float floatingPoint);
                converted = MenuDebugValue.FromFloat(floatingPoint);
                break;
            case MenuDebugValueKind.Boolean:
                success = value.TryGetBoolean(out bool boolean);
                converted = MenuDebugValue.FromBoolean(boolean);
                break;
            case MenuDebugValueKind.String:
                success = true;
                converted = MenuDebugValue.FromString(value.AsString());
                break;
            default:
                success = false;
                converted = default;
                break;
        }

        MenuEvaluation<MenuDebugValue>[] source = inputs.ToArray();
        IEnumerable<MenuDebugDependency> allDependencies = source
            .SelectMany(item => item.Dependencies)
            .Concat(dependencies);
        IEnumerable<MenuEvaluationTraceEntry> trace = source.SelectMany(item => item.Trace);
        return success
            ? MenuEvaluation<MenuDebugValue>.Known(
                converted,
                allDependencies,
                trace)
            : MenuEvaluation<MenuDebugValue>.Error(
                default,
                allDependencies,
                trace.Append(new MenuEvaluationTraceEntry(
                    MenuEvaluationStatus.Error,
                    $"Value '{value}' cannot be converted to {kind}.",
                    operation)));
    }

    private static MenuEvaluation<MenuDebugValue> Known(
        MenuDebugValue value,
        params MenuEvaluation<MenuDebugValue>[] inputs) =>
        MenuEvaluation<MenuDebugValue>.Known(
            value,
            inputs.SelectMany(input => input.Dependencies),
            inputs.SelectMany(input => input.Trace));

    private static MenuEvaluation<MenuDebugValue> MergeUnavailable(
        params MenuEvaluation<MenuDebugValue>[] inputs)
    {
        MenuEvaluationStatus status = inputs.Any(value => value.Status == MenuEvaluationStatus.Error)
            ? MenuEvaluationStatus.Error
            : MenuEvaluationStatus.Unknown;
        IEnumerable<MenuDebugDependency> dependencies = inputs.SelectMany(value => value.Dependencies);
        IEnumerable<MenuEvaluationTraceEntry> trace = inputs.SelectMany(value => value.Trace);
        return status == MenuEvaluationStatus.Error
            ? MenuEvaluation<MenuDebugValue>.Error(default, dependencies, trace)
            : MenuEvaluation<MenuDebugValue>.Unknown(default, dependencies, trace);
    }

    private static MenuEvaluation<MenuDebugValue> ConversionError(
        OperationEnum operation,
        params MenuEvaluation<MenuDebugValue>[] inputs) =>
        Error(
            $"Operation '{operation}' received an incompatible value type.",
            operation,
            inputs);

    private static MenuEvaluation<MenuDebugValue> ArityError(
        OperationEnum operation,
        int expected,
        int actual) =>
        Error(
            $"Operation '{operation}' expected {expected} argument(s), but found {actual}.",
            operation);

    private static MenuEvaluation<MenuDebugValue> AtLeastOneArityError(
        OperationEnum operation) =>
        Error(
            $"Operation '{operation}' expected at least one argument, but found none.",
            operation);

    private static MenuEvaluation<MenuDebugValue> Unknown(
        string message,
        OperationEnum? operation,
        IEnumerable<MenuDebugDependency> dependencies,
        IEnumerable<MenuEvaluation<MenuDebugValue>>? inputs = null)
    {
        MenuEvaluation<MenuDebugValue>[] source = inputs?.ToArray() ?? [];
        return MenuEvaluation<MenuDebugValue>.Unknown(
            default,
            source.SelectMany(value => value.Dependencies).Concat(dependencies),
            source.SelectMany(value => value.Trace).Append(
                new MenuEvaluationTraceEntry(
                    MenuEvaluationStatus.Unknown,
                    message,
                    operation,
                    dependencies.FirstOrDefault())));
    }

    private static MenuEvaluation<MenuDebugValue> Error(
        string message,
        OperationEnum? operation,
        params MenuEvaluation<MenuDebugValue>[] inputs) =>
        MenuEvaluation<MenuDebugValue>.Error(
            default,
            inputs.SelectMany(value => value.Dependencies),
            inputs.SelectMany(value => value.Trace).Append(
                new MenuEvaluationTraceEntry(MenuEvaluationStatus.Error, message, operation)));

    private static MenuDebugDependency EnvironmentDependency(
        OperationEnum operation,
        string? qualifier,
        MenuDebugValueKind? valueKind) =>
        new(
            MenuDebugDependencyKind.Environment,
            qualifier is null ? operation.ToString() : qualifier,
            valueKind,
            operation);

    private static bool IsDvar(OperationEnum operation) => operation is
        OperationEnum.OP_STATICDVARINT or
        OperationEnum.OP_STATICDVARBOOL or
        OperationEnum.OP_STATICDVARFLOAT or
        OperationEnum.OP_STATICDVARSTRING or
        OperationEnum.OP_DVARINT or
        OperationEnum.OP_DVARBOOL or
        OperationEnum.OP_DVARFLOAT or
        OperationEnum.OP_DVARSTRING;

    private static bool IsStaticDvar(OperationEnum operation) => operation is
        OperationEnum.OP_STATICDVARINT or
        OperationEnum.OP_STATICDVARBOOL or
        OperationEnum.OP_STATICDVARFLOAT or
        OperationEnum.OP_STATICDVARSTRING;

    private static bool IsLocalVariable(OperationEnum operation) => operation is
        OperationEnum.OP_LOCALVARINT or
        OperationEnum.OP_LOCALVARBOOL or
        OperationEnum.OP_LOCALVARFLOAT or
        OperationEnum.OP_LOCALVARSTRING;

    private static MenuDebugValueKind RequestedKind(OperationEnum operation) => operation switch
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
        _ => throw new InvalidOperationException($"'{operation}' is not a variable operation.")
    };

}

internal sealed record EvaluationContext(
    MenuDebugProgram Program,
    MenuDebugScenario Scenario,
    Func<MenuNodeId, MenuEvaluatedRectangle> ResolveItemRectangle);
