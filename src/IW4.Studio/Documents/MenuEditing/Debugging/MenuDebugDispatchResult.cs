namespace IW4.Studio.Documents.MenuEditing.Debugging;

public abstract class MenuDebugDispatchTraceEntry
{
    protected MenuDebugDispatchTraceEntry(int sequence, string handlerPath)
    {
        Sequence = sequence;
        HandlerPath = handlerPath;
    }

    public int Sequence { get; }
    public string HandlerPath { get; }
}

public enum MenuDebugBranchKind
{
    Conditional,
    Else
}

public sealed class MenuDebugDecisionTraceEntry : MenuDebugDispatchTraceEntry
{
    private readonly IReadOnlyList<MenuDebugDependency> _dependencies;
    private readonly IReadOnlyList<MenuEvaluationTraceEntry> _expressionTrace;

    internal MenuDebugDecisionTraceEntry(
        int sequence,
        string handlerPath,
        MenuDebugBranchKind branchKind,
        MenuEvaluationStatus status,
        bool? isSelected,
        IEnumerable<MenuDebugDependency> dependencies,
        IEnumerable<MenuEvaluationTraceEntry> expressionTrace)
        : base(sequence, handlerPath)
    {
        BranchKind = branchKind;
        Status = status;
        IsSelected = isSelected;
        _dependencies = Array.AsReadOnly(dependencies.Distinct().ToArray());
        _expressionTrace = Array.AsReadOnly(expressionTrace.ToArray());
    }

    public MenuDebugBranchKind BranchKind { get; }
    public MenuEvaluationStatus Status { get; }
    public bool? IsSelected { get; }
    public IReadOnlyList<MenuDebugDependency> Dependencies => _dependencies;
    public IReadOnlyList<MenuEvaluationTraceEntry> ExpressionTrace => _expressionTrace;
}

public sealed class MenuDebugLocalVariableTraceEntry : MenuDebugDispatchTraceEntry
{
    private readonly IReadOnlyList<MenuDebugDependency> _dependencies;
    private readonly IReadOnlyList<MenuEvaluationTraceEntry> _expressionTrace;

    internal MenuDebugLocalVariableTraceEntry(
        int sequence,
        string handlerPath,
        string name,
        MenuDebugValueKind declaredKind,
        MenuEvaluationStatus status,
        bool isApplied,
        MenuDebugValue? previousValue,
        MenuDebugValue? value,
        IEnumerable<MenuDebugDependency> dependencies,
        IEnumerable<MenuEvaluationTraceEntry> expressionTrace)
        : base(sequence, handlerPath)
    {
        Name = name;
        DeclaredKind = declaredKind;
        Status = status;
        IsApplied = isApplied;
        PreviousValue = previousValue;
        Value = value;
        _dependencies = Array.AsReadOnly(dependencies.Distinct().ToArray());
        _expressionTrace = Array.AsReadOnly(expressionTrace.ToArray());
    }

    public string Name { get; }
    public MenuDebugValueKind DeclaredKind { get; }
    public MenuEvaluationStatus Status { get; }
    public bool IsApplied { get; }
    public MenuDebugValue? PreviousValue { get; }
    public MenuDebugValue? Value { get; }
    public IReadOnlyList<MenuDebugDependency> Dependencies => _dependencies;
    public IReadOnlyList<MenuEvaluationTraceEntry> ExpressionTrace => _expressionTrace;
}

public sealed class MenuDebugQueuedScriptTraceEntry : MenuDebugDispatchTraceEntry
{
    internal MenuDebugQueuedScriptTraceEntry(
        int sequence,
        string handlerPath,
        string script)
        : base(sequence, handlerPath) => Script = script;

    /// <summary>
    /// Authored command text that is outside the debugger-safe runtime subset
    /// and remains queued for inspection or an external runtime.
    /// </summary>
    public string Script { get; }
}

public enum MenuDebugItemColorTarget
{
    ForeColor,
    BackColor,
    BorderColor
}

public sealed class MenuDebugItemColorTraceEntry : MenuDebugDispatchTraceEntry
{
    internal MenuDebugItemColorTraceEntry(
        int sequence,
        string handlerPath,
        MenuNodeId itemId,
        MenuDebugItemColorTarget target,
        MenuColorValue? previousValue,
        MenuColorValue value)
        : base(sequence, handlerPath)
    {
        ItemId = itemId;
        Target = target;
        PreviousValue = previousValue;
        Value = value;
    }

    public MenuNodeId ItemId { get; }
    public MenuDebugItemColorTarget Target { get; }
    public MenuColorValue? PreviousValue { get; }
    public MenuColorValue Value { get; }
}

public sealed class MenuDebugFocusTraceEntry : MenuDebugDispatchTraceEntry
{
    internal MenuDebugFocusTraceEntry(
        int sequence,
        string handlerPath,
        MenuNodeId? previousItemId,
        MenuNodeId? itemId)
        : base(sequence, handlerPath)
    {
        PreviousItemId = previousItemId;
        ItemId = itemId;
    }

    public MenuNodeId? PreviousItemId { get; }
    public MenuNodeId? ItemId { get; }
}

public enum MenuDebugDiagnosticKind
{
    Unsupported,
    Blocker
}

public sealed class MenuDebugDiagnosticTraceEntry : MenuDebugDispatchTraceEntry
{
    internal MenuDebugDiagnosticTraceEntry(
        int sequence,
        string handlerPath,
        MenuDebugDiagnosticKind kind,
        MenuEvaluationStatus status,
        string code,
        string message,
        MenuDebugDependency? dependency)
        : base(sequence, handlerPath)
    {
        Kind = kind;
        Status = status;
        Code = code;
        Message = message;
        Dependency = dependency;
    }

    public MenuDebugDiagnosticKind Kind { get; }
    public MenuEvaluationStatus Status { get; }
    public string Code { get; }
    public string Message { get; }
    public MenuDebugDependency? Dependency { get; }
}

/// <summary>
/// Immutable result of applying one explicit input to one debugger scenario.
/// Debugger-safe local-variable, focus, and Window color changes can alter
/// NextScenario.
/// </summary>
public sealed class MenuDebugDispatchResult
{
    private readonly IReadOnlyList<MenuDebugDispatchTraceEntry> _trace;
    private readonly IReadOnlyList<MenuDebugQueuedScriptTraceEntry> _queuedScripts;
    private readonly IReadOnlyList<MenuDebugLocalVariableTraceEntry> _localVariableEvaluations;
    private readonly IReadOnlyList<MenuDebugLocalVariableTraceEntry> _localVariableChanges;
    private readonly IReadOnlyList<MenuDebugItemColorTraceEntry> _itemColorChanges;
    private readonly IReadOnlyList<MenuDebugDiagnosticTraceEntry> _diagnostics;

    internal MenuDebugDispatchResult(
        MenuDebugInput input,
        MenuDebugScenario previousScenario,
        MenuDebugScenario nextScenario,
        IEnumerable<MenuDebugDispatchTraceEntry> trace)
    {
        Input = input;
        PreviousScenario = previousScenario;
        NextScenario = nextScenario;
        MenuDebugDispatchTraceEntry[] entries = trace.ToArray();
        _trace = Array.AsReadOnly(entries);
        _queuedScripts = Array.AsReadOnly(
            entries.OfType<MenuDebugQueuedScriptTraceEntry>().ToArray());
        MenuDebugLocalVariableTraceEntry[] localVariables =
            entries.OfType<MenuDebugLocalVariableTraceEntry>().ToArray();
        _localVariableEvaluations = Array.AsReadOnly(localVariables);
        _localVariableChanges = Array.AsReadOnly(
            localVariables.Where(value => value.IsApplied).ToArray());
        _itemColorChanges = Array.AsReadOnly(
            entries.OfType<MenuDebugItemColorTraceEntry>().ToArray());
        _diagnostics = Array.AsReadOnly(
            entries.OfType<MenuDebugDiagnosticTraceEntry>().ToArray());
    }

    public MenuDebugInput Input { get; }
    public MenuDebugScenario PreviousScenario { get; }
    public MenuDebugScenario NextScenario { get; }
    public IReadOnlyList<MenuDebugDispatchTraceEntry> Trace => _trace;
    public IReadOnlyList<MenuDebugQueuedScriptTraceEntry> QueuedScripts => _queuedScripts;
    public IReadOnlyList<MenuDebugLocalVariableTraceEntry> LocalVariableEvaluations =>
        _localVariableEvaluations;
    public IReadOnlyList<MenuDebugLocalVariableTraceEntry> LocalVariableChanges =>
        _localVariableChanges;
    public IReadOnlyList<MenuDebugItemColorTraceEntry> ItemColorChanges =>
        _itemColorChanges;
    public IReadOnlyList<MenuDebugDiagnosticTraceEntry> Diagnostics => _diagnostics;
    public bool HasBlockers =>
        _diagnostics.Any(value => value.Kind == MenuDebugDiagnosticKind.Blocker);
}
