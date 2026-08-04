namespace IW4.Studio.Documents.MenuEditing.Debugging;

internal sealed record MenuDebugSelectedHook(
    MenuDebugEventSet EventSet,
    string Path,
    MenuNodeId? ItemId,
    MenuDebugFocusTransition FocusTransition);

internal enum MenuDebugFocusTransition
{
    None,
    Set,
    Clear
}

internal sealed class MenuDebugDispatchState
{
    private readonly MenuDebugScenario _original;
    private readonly Dictionary<string, MenuDebugValue> _localVariables;
    private bool _changed;

    public MenuDebugDispatchState(MenuDebugScenario original)
    {
        _original = original;
        _localVariables = new Dictionary<string, MenuDebugValue>(
            original.LocalVariables,
            StringComparer.OrdinalIgnoreCase);
        FocusedItemId = original.FocusedItemId;
    }

    public MenuNodeId? FocusedItemId { get; private set; }

    public bool TryGetLocal(string name, out MenuDebugValue value) =>
        _localVariables.TryGetValue(name, out value);

    public void SetLocal(string name, MenuDebugValue value)
    {
        if (!_localVariables.TryGetValue(name, out MenuDebugValue previous) ||
            previous != value)
        {
            _changed = true;
        }
        _localVariables[name] = value;
    }

    public void SetFocus(MenuNodeId? itemId)
    {
        if (FocusedItemId != itemId)
            _changed = true;
        FocusedItemId = itemId;
    }

    public MenuDebugScenario ToScenario() => _changed
        ? new MenuDebugScenario(
            _original.Milliseconds,
            _original.Dvars,
            _localVariables,
            _original.Environment,
            _original.OpenMenus,
            FocusedItemId,
            _original.LocalizationResolver)
        : _original;
}

internal sealed class MenuDebugDispatchTraceBuilder
{
    private readonly List<MenuDebugDispatchTraceEntry> _entries = [];

    public IReadOnlyList<MenuDebugDispatchTraceEntry> Entries => _entries;

    public void AddDecision(
        string path,
        MenuDebugBranchKind kind,
        MenuEvaluation<bool> decision) => _entries.Add(
        new MenuDebugDecisionTraceEntry(
            _entries.Count,
            path,
            kind,
            decision.Status,
            decision.IsKnown ? decision.Value : null,
            decision.Dependencies,
            decision.Trace));

    public void AddLocalVariable(
        string path,
        string name,
        MenuDebugValueKind declaredKind,
        MenuEvaluationStatus status,
        bool isApplied,
        MenuDebugValue? previousValue,
        MenuDebugValue? value,
        IEnumerable<MenuDebugDependency> dependencies,
        IEnumerable<MenuEvaluationTraceEntry> expressionTrace) => _entries.Add(
        new MenuDebugLocalVariableTraceEntry(
            _entries.Count,
            path,
            name,
            declaredKind,
            status,
            isApplied,
            previousValue,
            value,
            dependencies,
            expressionTrace));

    public void AddScript(string path, string script) => _entries.Add(
        new MenuDebugQueuedScriptTraceEntry(
            _entries.Count,
            path,
            script));

    public void AddFocus(
        string path,
        MenuNodeId? previousItemId,
        MenuNodeId? itemId) => _entries.Add(
        new MenuDebugFocusTraceEntry(
            _entries.Count,
            path,
            previousItemId,
            itemId));

    public void AddDiagnostic(
        string path,
        MenuDebugDiagnosticKind kind,
        MenuEvaluationStatus status,
        string code,
        string message) => _entries.Add(
        new MenuDebugDiagnosticTraceEntry(
            _entries.Count,
            path,
            kind,
            status,
            code,
            message));
}
