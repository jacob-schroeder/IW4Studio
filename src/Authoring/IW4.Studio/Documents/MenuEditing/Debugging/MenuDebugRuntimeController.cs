namespace IW4.Studio.Documents.MenuEditing.Debugging;

/// <summary>
/// Routes parsed script commands into the debugger-safe runtime subset and
/// queues every unsupported command fragment independently.
/// </summary>
internal sealed class MenuDebugRuntimeController
{
    private readonly MenuDebugProgram _program;
    private readonly MenuDebugDispatchState _state;
    private readonly MenuDebugDispatchTraceBuilder _trace;
    private readonly MenuDebugFocusController _focus;
    private readonly MenuDebugItemColorController _colors;

    public MenuDebugRuntimeController(
        MenuDebugProgram program,
        MenuDebugDispatchState state,
        MenuDebugDispatchTraceBuilder trace,
        Action<MenuDebugEventSet, string, MenuNodeId?> executeHandlers)
    {
        _program = program;
        _state = state;
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
                _trace.AddScript(commandPath, command.RawText, command.Tokens);
        }
    }

    private bool TryExecute(
        MenuDebugScriptCommand command,
        string path,
        MenuNodeId? contextItemId)
    {
        string name = command.Tokens[0];
        if (name.Equals("exec", StringComparison.OrdinalIgnoreCase) &&
            command.Tokens is [_, var payload])
            return TryQueueProfileLayout(payload, path);
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
        bool show = name.Equals("show", StringComparison.OrdinalIgnoreCase);
        if (show || name.Equals("hide", StringComparison.OrdinalIgnoreCase))
        {
            if (command.Tokens.Count != 2 || string.IsNullOrEmpty(command.Tokens[1]))
                return Reject(path, name, "The command requires one Item or group selector.");

            foreach (MenuDebugItemProgram item in ResolveTargets(_program, command.Tokens[1], null))
            {
                MenuDebugItemRuntimeState state = _state.ItemRuntimeState(item.Id);
                _state.SetItemRuntimeState(item.Id, state with { Visible = show });
            }
            return true;
        }
        if (name.Equals("setDvar", StringComparison.OrdinalIgnoreCase))
        {
            // PS3 authored setdvar takes a name and one value token (stock
            // menu_xboxlive). The debugger stores the literal scenario value.
            if (command.Tokens.Count != 3 || string.IsNullOrWhiteSpace(command.Tokens[1]))
                return Reject(path, name, "setDvar requires a game variable name and a literal value.");

            string dvarName = command.Tokens[1];
            var value = MenuDebugValue.FromString(command.Tokens[2]);
            MenuDebugValue? previous = _state.SetDvar(dvarName, value);
            _trace.AddDvar(path, dvarName, previous, value);
            return true;
        }

        return false;
    }

    private bool TryQueueProfileLayout(string payload, string path)
    {
        if (payload.Contains('\n') || payload.Contains('\r'))
            return false;
        var parsed = MenuDebugScriptParser.Parse(payload);
        if (parsed.Commands is not [{ Tokens: [var command, var layout] }] ||
            layout.Any(c => !char.IsAscii(c)))
            return false;

        // PS3 handlers 0xbbc90 and 0xbbc88 copy argv[1] into the controller's
        // 32-byte NUL-terminated buttons and sticks profile fields respectively.
        string field;
        if (command.Equals("profile_setButtonsConfig", StringComparison.OrdinalIgnoreCase))
            field = "gpadButtonsConfig";
        else if (command.Equals("profile_setSticksConfig", StringComparison.OrdinalIgnoreCase))
            field = "gpadSticksConfig";
        else
            return false;
        _trace.QueueProfileLayout(path, field, layout.Length <= 31 ? layout : layout[..31]);
        return true;
    }

    internal static IEnumerable<MenuDebugItemProgram> ResolveTargets(
        MenuDebugProgram program,
        string selector,
        MenuNodeId? contextItemId)
    {
        bool self = selector.Equals("self", StringComparison.OrdinalIgnoreCase);
        if (self && contextItemId is { } selfId)
        {
            if (program.Items.FirstOrDefault(item => item.Id == selfId) is { } item)
                yield return item;
            yield break;
        }

        int wildcard = selector.IndexOf('*');
        string prefix = wildcard < 0 ? selector : selector[..wildcard];
        foreach (MenuDebugItemProgram item in program.Items)
        {
            if (wildcard < 0
                ? string.Equals(item.Name, selector, StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(item.Group, selector, StringComparison.OrdinalIgnoreCase)
                : item.Name?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true ||
                  item.Group?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true)
            {
                yield return item;
                if (self)
                    yield break;
            }
        }
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
