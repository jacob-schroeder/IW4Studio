using IW4.Assets.Assets.Menu;

namespace IW4.Studio.Documents.MenuEditing.Debugging;

/// <summary>
/// Executes the debugger-safe subset of a selected event set. This type owns
/// handler semantics, while MenuDebugEventDispatcher owns input selection.
/// </summary>
internal sealed class MenuDebugEventExecutor
{
    private readonly MenuDebugProgram _program;
    private readonly MenuDebugDispatchState _state;
    private readonly MenuDebugDispatchTraceBuilder _trace;

    public MenuDebugEventExecutor(
        MenuDebugProgram program,
        MenuDebugDispatchState state,
        MenuDebugDispatchTraceBuilder trace)
    {
        _program = program;
        _state = state;
        _trace = trace;
    }

    public bool ApplyFocus(MenuDebugSelectedHook hook)
    {
        if (hook.FocusTransition == MenuDebugFocusTransition.None)
            return true;

        MenuNodeId? previous = _state.FocusedItemId;
        if (hook.FocusTransition == MenuDebugFocusTransition.Clear &&
            previous != hook.ItemId)
        {
            _trace.AddDiagnostic(
                hook.Path,
                MenuDebugDiagnosticKind.Blocker,
                MenuEvaluationStatus.Error,
                "leave-focus-target-mismatch",
                previous is null
                    ? $"Item '{hook.ItemId}' cannot leave focus because no item is focused."
                    : $"Item '{hook.ItemId}' cannot leave focus while item '{previous}' is focused.");
            return false;
        }

        MenuNodeId? next = hook.FocusTransition == MenuDebugFocusTransition.Set
            ? hook.ItemId
            : null;
        _state.SetFocus(next);
        _trace.AddFocus(hook.Path, previous, next);
        return true;
    }

    public void Execute(MenuDebugEventSet set, string path)
    {
        MenuEvaluation<bool>? pendingConditional = null;
        for (int index = 0; index < set.Handlers.Count; index++)
        {
            MenuDebugEventHandler handler = set.Handlers[index];
            string handlerPath = $"{path}.handlers[{index}]";
            switch (handler)
            {
                case MenuDebugConditionalEventHandler conditional:
                    pendingConditional = EvaluateCondition(conditional.Condition);
                    _trace.AddDecision(
                        handlerPath,
                        MenuDebugBranchKind.Conditional,
                        pendingConditional);
                    ExecuteSelectedBranch(
                        conditional.Handlers,
                        $"{handlerPath}.then",
                        handlerPath,
                        pendingConditional);
                    break;

                case MenuDebugElseEventHandler @else:
                    MenuEvaluation<bool> elseDecision = EvaluateElse(pendingConditional);
                    _trace.AddDecision(
                        handlerPath,
                        MenuDebugBranchKind.Else,
                        elseDecision);
                    ExecuteSelectedBranch(
                        @else.Handlers,
                        $"{handlerPath}.else",
                        handlerPath,
                        elseDecision);
                    pendingConditional = null;
                    break;

                case MenuDebugScriptEventHandler script:
                    pendingConditional = null;
                    _trace.AddScript(handlerPath, script.Script);
                    break;

                case MenuDebugSetLocalVariableEventHandler setLocal:
                    pendingConditional = null;
                    ApplyLocalVariable(setLocal, handlerPath);
                    break;

                default:
                    pendingConditional = null;
                    _trace.AddDiagnostic(
                        handlerPath,
                        MenuDebugDiagnosticKind.Unsupported,
                        MenuEvaluationStatus.Error,
                        "unsupported-event-handler",
                        $"Event handler type '{handler.GetType().Name}' is not supported.");
                    break;
            }
        }
    }

    private void ExecuteSelectedBranch(
        MenuDebugEventSet handlers,
        string selectedPath,
        string decisionPath,
        MenuEvaluation<bool> decision)
    {
        if (decision.IsKnown)
        {
            if (decision.Value)
                Execute(handlers, selectedPath);
            return;
        }

        _trace.AddDiagnostic(
            decisionPath,
            MenuDebugDiagnosticKind.Blocker,
            decision.Status,
            "branch-decision-unavailable",
            decision.Status == MenuEvaluationStatus.Unknown
                ? "Branch selection depends on scenario state that has not been supplied. Neither branch was dispatched."
                : "Branch selection failed because the authored expression is invalid. Neither branch was dispatched.");
    }

    private MenuEvaluation<bool> EvaluateCondition(MenuDebugExpression? expression)
    {
        if (expression is null)
        {
            return MenuEvaluation<bool>.Error(
                false,
                [],
                [new MenuEvaluationTraceEntry(
                    MenuEvaluationStatus.Error,
                    "Conditional event handler has no expression.")]);
        }

        MenuEvaluation<MenuDebugValue> result = _program.EvaluateExpression(
            expression,
            _state.ToScenario());
        if (!result.IsKnown)
        {
            return result.Status == MenuEvaluationStatus.Error
                ? MenuEvaluation<bool>.Error(false, result.Dependencies, result.Trace)
                : MenuEvaluation<bool>.Unknown(false, result.Dependencies, result.Trace);
        }
        if (result.Value.TryGetBoolean(out bool value))
            return MenuEvaluation<bool>.Known(value, result.Dependencies, result.Trace);

        return MenuEvaluation<bool>.Error(
            false,
            result.Dependencies,
            result.Trace.Append(new MenuEvaluationTraceEntry(
                MenuEvaluationStatus.Error,
                "Conditional event expression cannot be converted to Boolean.")));
    }

    private static MenuEvaluation<bool> EvaluateElse(
        MenuEvaluation<bool>? conditional)
    {
        if (conditional is null)
        {
            return MenuEvaluation<bool>.Error(
                false,
                [],
                [new MenuEvaluationTraceEntry(
                    MenuEvaluationStatus.Error,
                    "Else event handler is not immediately preceded by a conditional handler.")]);
        }

        return conditional.Status switch
        {
            MenuEvaluationStatus.Known => MenuEvaluation<bool>.Known(
                !conditional.Value,
                conditional.Dependencies,
                conditional.Trace),
            MenuEvaluationStatus.Unknown => MenuEvaluation<bool>.Unknown(
                false,
                conditional.Dependencies,
                conditional.Trace),
            _ => MenuEvaluation<bool>.Error(
                false,
                conditional.Dependencies,
                conditional.Trace)
        };
    }

    private void ApplyLocalVariable(
        MenuDebugSetLocalVariableEventHandler handler,
        string path)
    {
        if (string.IsNullOrWhiteSpace(handler.Name))
        {
            _trace.AddDiagnostic(
                path,
                MenuDebugDiagnosticKind.Blocker,
                MenuEvaluationStatus.Error,
                "local-variable-name-missing",
                "Set-local-variable handler has no variable name.");
            return;
        }
        if (handler.ValueExpression is null)
        {
            _trace.AddDiagnostic(
                path,
                MenuDebugDiagnosticKind.Blocker,
                MenuEvaluationStatus.Error,
                "local-variable-expression-missing",
                $"Set-local-variable handler for '{handler.Name}' has no value expression.");
            return;
        }

        MenuEvaluation<MenuDebugValue> evaluated = _program.EvaluateExpression(
            handler.ValueExpression,
            _state.ToScenario());
        MenuEvaluation<MenuDebugValue> converted = ConvertLocalValue(
            handler.ValueType,
            evaluated,
            out MenuDebugValueKind declaredKind);
        if (!converted.IsKnown)
        {
            _trace.AddLocalVariable(
                path,
                handler.Name,
                declaredKind,
                converted.Status,
                false,
                null,
                null,
                converted.Dependencies,
                converted.Trace);
            _trace.AddDiagnostic(
                path,
                MenuDebugDiagnosticKind.Blocker,
                converted.Status,
                "local-variable-value-unavailable",
                converted.Status == MenuEvaluationStatus.Unknown
                    ? $"Value for local variable '{handler.Name}' depends on scenario state that has not been supplied."
                    : $"Value for local variable '{handler.Name}' could not be evaluated or converted to {declaredKind}.");
            return;
        }

        MenuDebugValue? previous = _state.TryGetLocal(
            handler.Name,
            out MenuDebugValue prior)
            ? prior
            : null;
        _state.SetLocal(handler.Name, converted.Value);
        _trace.AddLocalVariable(
            path,
            handler.Name,
            declaredKind,
            MenuEvaluationStatus.Known,
            true,
            previous,
            converted.Value,
            converted.Dependencies,
            converted.Trace);
    }

    private static MenuEvaluation<MenuDebugValue> ConvertLocalValue(
        MenuEventHandlerType type,
        MenuEvaluation<MenuDebugValue> evaluated,
        out MenuDebugValueKind declaredKind)
    {
        declaredKind = type switch
        {
            MenuEventHandlerType.SetLocalVarBool => MenuDebugValueKind.Boolean,
            MenuEventHandlerType.SetLocalVarInt => MenuDebugValueKind.Integer,
            MenuEventHandlerType.SetLocalVarFloat => MenuDebugValueKind.Float,
            MenuEventHandlerType.SetLocalVarString => MenuDebugValueKind.String,
            _ => evaluated.Value.Kind
        };
        if (!evaluated.IsKnown)
            return evaluated;

        MenuDebugValue? value = type switch
        {
            MenuEventHandlerType.SetLocalVarBool when
                evaluated.Value.TryGetBoolean(out bool boolean) =>
                MenuDebugValue.FromBoolean(boolean),
            MenuEventHandlerType.SetLocalVarInt when
                evaluated.Value.TryGetInt(out int integer) =>
                MenuDebugValue.FromInt(integer),
            MenuEventHandlerType.SetLocalVarFloat when
                evaluated.Value.TryGetFloat(out float floatingPoint) =>
                MenuDebugValue.FromFloat(floatingPoint),
            MenuEventHandlerType.SetLocalVarString =>
                MenuDebugValue.FromString(evaluated.Value.AsString()),
            _ => null
        };
        if (value is { } converted)
        {
            return MenuEvaluation<MenuDebugValue>.Known(
                converted,
                evaluated.Dependencies,
                evaluated.Trace);
        }

        return MenuEvaluation<MenuDebugValue>.Error(
            evaluated.Value,
            evaluated.Dependencies,
            evaluated.Trace.Append(new MenuEvaluationTraceEntry(
                MenuEvaluationStatus.Error,
                type is MenuEventHandlerType.SetLocalVarBool or
                    MenuEventHandlerType.SetLocalVarInt or
                    MenuEventHandlerType.SetLocalVarFloat or
                    MenuEventHandlerType.SetLocalVarString
                    ? $"Expression result cannot be converted to {declaredKind}."
                    : $"Set-local-variable type '{type}' is not supported.")));
    }
}
