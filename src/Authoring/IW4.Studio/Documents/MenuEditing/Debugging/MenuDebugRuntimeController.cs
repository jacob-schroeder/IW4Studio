namespace IW4.Studio.Documents.MenuEditing.Debugging;

/// <summary>
/// Routes parsed script commands into the debugger-safe runtime subset and
/// queues every unsupported command fragment independently.
/// </summary>
internal sealed class MenuDebugRuntimeController
{
    private readonly MenuDebugDispatchTraceBuilder _trace;
    private readonly MenuDebugFocusController _focus;
    private readonly MenuDebugItemColorController _colors;

    public MenuDebugRuntimeController(
        MenuDebugProgram program,
        MenuDebugDispatchState state,
        MenuDebugDispatchTraceBuilder trace,
        Action<MenuDebugEventSet, string, MenuNodeId?> executeHandlers)
    {
        _trace = trace;
        _focus = new MenuDebugFocusController(
            program,
            state,
            trace,
            executeHandlers);
        _colors = new MenuDebugItemColorController(program, state, trace);
    }

    public bool ApplyFocus(MenuDebugSelectedHook hook) =>
        _focus.Apply(hook);

    public void ExecuteScript(
        string script,
        string path,
        MenuNodeId? contextItemId)
    {
        MenuDebugScriptParseResult parsed = MenuDebugScriptParser.Parse(script);
        if (!parsed.IsValid)
        {
            _trace.AddDiagnostic(
                path,
                MenuDebugDiagnosticKind.Blocker,
                MenuEvaluationStatus.Error,
                "script-parse-failed",
                parsed.Failure!);
            _trace.AddScript(path, script);
            return;
        }

        for (int index = 0; index < parsed.Commands.Count; index++)
        {
            MenuDebugScriptCommand command = parsed.Commands[index];
            string commandPath = $"{path}.commands[{index}]";
            if (!TryExecute(command, commandPath, contextItemId))
                _trace.AddScript(commandPath, command.RawText);
        }
    }

    private bool TryExecute(
        MenuDebugScriptCommand command,
        string path,
        MenuNodeId? contextItemId)
    {
        string name = command.Tokens[0];
        if (name.Equals("focusFirst", StringComparison.OrdinalIgnoreCase))
        {
            return command.Tokens.Count == 1
                ? _focus.FocusFirst(path)
                : Reject(path, name, "focusFirst accepts no arguments.");
        }
        if (name.Equals("setFocus", StringComparison.OrdinalIgnoreCase))
        {
            return command.Tokens.Count == 2
                ? _focus.FocusNamed(command.Tokens[1], path)
                : Reject(path, name, "setFocus requires one Item name.");
        }
        if (name.Equals("setFocusByDvar", StringComparison.OrdinalIgnoreCase))
        {
            return command.Tokens.Count == 2
                ? _focus.FocusByDvar(command.Tokens[1], path)
                : Reject(path, name, "setFocusByDvar requires one dvar name.");
        }
        if (name.Equals("setItemColor", StringComparison.OrdinalIgnoreCase))
        {
            return command.Tokens.Count == 7
                ? _colors.Apply(command.Tokens, path, contextItemId)
                : Reject(
                    path,
                    name,
                    "setItemColor requires a target, color channel, and four components.");
        }

        return false;
    }

    private bool Reject(string path, string command, string message)
    {
        _trace.AddDiagnostic(
            path,
            MenuDebugDiagnosticKind.Blocker,
            MenuEvaluationStatus.Error,
            "runtime-command-invalid",
            $"{command}: {message}");
        return false;
    }
}
