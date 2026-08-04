namespace IW4.Studio.Documents.MenuEditing.Debugging;

/// <summary>
/// Selects one explicit authored hook set and applies it to an immutable
/// debugger scenario. Native routing is deliberately outside this component.
/// </summary>
public sealed class MenuDebugEventDispatcher
{
    public static MenuDebugEventDispatcher Default { get; } = new();

    public MenuDebugDispatchResult Activate(
        MenuDebugProgram program,
        MenuDebugScenario scenario) =>
        Dispatch(
            program,
            new MenuDebugMenuHookInput(MenuDebugMenuHook.Open),
            scenario,
            opensMenu: true);

    public MenuDebugDispatchResult Dispatch(
        MenuDebugProgram program,
        MenuDebugInput input,
        MenuDebugScenario scenario) =>
        Dispatch(program, input, scenario, opensMenu: false);

    private static MenuDebugDispatchResult Dispatch(
        MenuDebugProgram program,
        MenuDebugInput input,
        MenuDebugScenario scenario,
        bool opensMenu)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(scenario);

        var trace = new MenuDebugDispatchTraceBuilder();
        var state = new MenuDebugDispatchState(scenario);
        if (opensMenu)
            state.OpenMenu(program.Name);
        MenuDebugSelectedHook? selected = SelectHook(program, input, trace);
        if (selected is not null)
        {
            var executor = new MenuDebugEventExecutor(program, state, trace);
            if (selected.FocusTransition == MenuDebugFocusTransition.None)
            {
                executor.Execute(
                    selected.EventSet,
                    selected.Path,
                    selected.ItemId);
            }
            else
            {
                executor.ApplyFocus(selected);
            }
        }

        return new MenuDebugDispatchResult(
            input,
            scenario,
            state.ToScenario(),
            trace.Entries);
    }

    private static MenuDebugSelectedHook? SelectHook(
        MenuDebugProgram program,
        MenuDebugInput input,
        MenuDebugDispatchTraceBuilder trace) => input switch
        {
            MenuDebugMenuHookInput menu => SelectMenuHook(program, menu, trace),
            MenuDebugMenuKeyInput key => SelectKeyHook(
                program.Hooks.KeyHandlers,
                key.Selection,
                "menu.key",
                itemId: null,
                trace),
            MenuDebugItemHookInput item => SelectItemHook(program, item, trace),
            MenuDebugItemKeyInput key => SelectItemKeyHook(program, key, trace),
            _ => UnsupportedInput(input, trace)
        };

    private static MenuDebugSelectedHook? SelectMenuHook(
        MenuDebugProgram program,
        MenuDebugMenuHookInput input,
        MenuDebugDispatchTraceBuilder trace) => input.Hook switch
        {
            MenuDebugMenuHook.Open => Hook(program.Hooks.OnOpen, "menu.onOpen"),
            MenuDebugMenuHook.CloseRequest => Hook(
                program.Hooks.OnCloseRequest,
                "menu.onCloseRequest"),
            MenuDebugMenuHook.Close => Hook(program.Hooks.OnClose, "menu.onClose"),
            MenuDebugMenuHook.Escape => Hook(program.Hooks.OnEscape, "menu.onEscape"),
            _ => UnsupportedHook("menu", input.Hook, trace)
        };

    private static MenuDebugSelectedHook? SelectItemHook(
        MenuDebugProgram program,
        MenuDebugItemHookInput input,
        MenuDebugDispatchTraceBuilder trace)
    {
        MenuDebugItemProgram? item = FindItem(program, input.ItemId, trace);
        if (item is null)
            return null;

        string root = $"item[{input.ItemId}]";
        return input.Hook switch
        {
            MenuDebugItemHook.PointerEnter => Hook(
                item.Hooks.MouseEnter,
                $"{root}.mouseEnter",
                input.ItemId),
            MenuDebugItemHook.PointerExit => Hook(
                item.Hooks.MouseExit,
                $"{root}.mouseExit",
                input.ItemId),
            MenuDebugItemHook.TextPointerEnter => Hook(
                item.Hooks.MouseEnterText,
                $"{root}.mouseEnterText",
                input.ItemId),
            MenuDebugItemHook.TextPointerExit => Hook(
                item.Hooks.MouseExitText,
                $"{root}.mouseExitText",
                input.ItemId),
            MenuDebugItemHook.Focus => Hook(
                item.Hooks.OnFocus,
                $"{root}.onFocus",
                input.ItemId,
                MenuDebugFocusTransition.Set),
            MenuDebugItemHook.LeaveFocus => Hook(
                item.Hooks.LeaveFocus,
                $"{root}.leaveFocus",
                input.ItemId,
                MenuDebugFocusTransition.Clear),
            MenuDebugItemHook.Action => Hook(
                item.Hooks.Action,
                $"{root}.action",
                input.ItemId),
            MenuDebugItemHook.Accept => Hook(
                item.Hooks.Accept,
                $"{root}.accept",
                input.ItemId),
            MenuDebugItemHook.DoubleClick => Hook(
                item.Hooks.DoubleClick,
                $"{root}.doubleClick",
                input.ItemId),
            _ => UnsupportedHook(root, input.Hook, trace)
        };
    }

    private static MenuDebugSelectedHook? SelectItemKeyHook(
        MenuDebugProgram program,
        MenuDebugItemKeyInput input,
        MenuDebugDispatchTraceBuilder trace)
    {
        MenuDebugItemProgram? item = FindItem(program, input.ItemId, trace);
        return item is null
            ? null
            : SelectKeyHook(
                item.Hooks.KeyHandlers,
                input.Selection,
                $"item[{input.ItemId}].key",
                input.ItemId,
                trace);
    }

    private static MenuDebugItemProgram? FindItem(
        MenuDebugProgram program,
        MenuNodeId itemId,
        MenuDebugDispatchTraceBuilder trace)
    {
        MenuDebugItemProgram? item = program.Items.FirstOrDefault(value => value.Id == itemId);
        if (item is not null)
            return item;

        trace.AddDiagnostic(
            $"item[{itemId}]",
            MenuDebugDiagnosticKind.Blocker,
            MenuEvaluationStatus.Error,
            "item-not-found",
            $"Item '{itemId}' is not part of this menu debug program.");
        return null;
    }

    private static MenuDebugSelectedHook? SelectKeyHook(
        IReadOnlyList<MenuDebugKeyHandler> handlers,
        MenuDebugKeySelection selection,
        string root,
        MenuNodeId? itemId,
        MenuDebugDispatchTraceBuilder trace)
    {
        int? handlerIndex = ResolveKeyHandlerIndex(handlers, selection, root, trace);
        return handlerIndex is not { } index
            ? null
            : Hook(handlers[index].Actions, $"{root}[{index}]", itemId);
    }

    private static int? ResolveKeyHandlerIndex(
        IReadOnlyList<MenuDebugKeyHandler> handlers,
        MenuDebugKeySelection selection,
        string path,
        MenuDebugDispatchTraceBuilder trace)
    {
        if (selection.AuthoredHandlerIndex is { } selectedIndex)
        {
            if (selectedIndex < 0 || selectedIndex >= handlers.Count)
            {
                trace.AddDiagnostic(
                    path,
                    MenuDebugDiagnosticKind.Blocker,
                    MenuEvaluationStatus.Error,
                    "key-handler-index-out-of-range",
                    $"Authored key-handler index {selectedIndex} is outside the table of {handlers.Count} handlers.");
                return null;
            }
            if (handlers[selectedIndex].Key == selection.Key)
                return selectedIndex;

            trace.AddDiagnostic(
                $"{path}[{selectedIndex}]",
                MenuDebugDiagnosticKind.Blocker,
                MenuEvaluationStatus.Error,
                "key-handler-mismatch",
                $"Authored handler {selectedIndex} is for key {handlers[selectedIndex].Key}, not key {selection.Key}.");
            return null;
        }

        int[] matches = handlers
            .Select((handler, index) => (handler, index))
            .Where(value => value.handler.Key == selection.Key)
            .Select(value => value.index)
            .ToArray();
        if (matches.Length == 1)
            return matches[0];

        trace.AddDiagnostic(
            path,
            MenuDebugDiagnosticKind.Blocker,
            MenuEvaluationStatus.Error,
            matches.Length == 0 ? "key-handler-not-found" : "key-handler-ambiguous",
            matches.Length == 0
                ? $"No authored hook is registered for key {selection.Key}."
                : $"Key {selection.Key} has {matches.Length} authored hooks. Select an authored handler index explicitly.");
        return null;
    }

    private static MenuDebugSelectedHook Hook(
        MenuDebugEventSet eventSet,
        string path,
        MenuNodeId? itemId = null,
        MenuDebugFocusTransition focusTransition = MenuDebugFocusTransition.None) =>
        new(eventSet, path, itemId, focusTransition);

    private static MenuDebugSelectedHook? UnsupportedInput(
        MenuDebugInput input,
        MenuDebugDispatchTraceBuilder trace)
    {
        trace.AddDiagnostic(
            "input",
            MenuDebugDiagnosticKind.Unsupported,
            MenuEvaluationStatus.Error,
            "unsupported-input",
            $"Input type '{input.GetType().Name}' is not supported.");
        return null;
    }

    private static MenuDebugSelectedHook? UnsupportedHook<T>(
        string path,
        T hook,
        MenuDebugDispatchTraceBuilder trace)
        where T : struct, Enum
    {
        trace.AddDiagnostic(
            path,
            MenuDebugDiagnosticKind.Unsupported,
            MenuEvaluationStatus.Error,
            "unsupported-hook",
            $"Hook value '{hook}' is not supported.");
        return null;
    }
}
