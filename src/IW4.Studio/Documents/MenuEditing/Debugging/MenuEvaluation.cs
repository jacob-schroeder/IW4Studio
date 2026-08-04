using IW4.Assets.Assets.Menu;

namespace IW4.Studio.Documents.MenuEditing.Debugging;

public enum MenuEvaluationStatus
{
    Known,
    Unknown,
    Error
}

public enum MenuDebugDependencyKind
{
    Dvar,
    LocalVariable,
    Environment,
    Menu,
    ItemGeometry,
    Localization
}

public sealed record MenuDebugDependency(
    MenuDebugDependencyKind Kind,
    string Name,
    MenuDebugValueKind? ValueKind = null,
    OperationEnum? Operation = null);

public sealed record MenuEvaluationTraceEntry(
    MenuEvaluationStatus Status,
    string Message,
    OperationEnum? Operation = null,
    MenuDebugDependency? Dependency = null);

/// <summary>
/// One expression result. Unknown represents missing simulated game state;
/// Error represents malformed authored data or an invalid operation.
/// </summary>
public sealed class MenuEvaluation<T>
{
    private readonly IReadOnlyList<MenuDebugDependency> _dependencies;
    private readonly IReadOnlyList<MenuEvaluationTraceEntry> _trace;

    internal MenuEvaluation(
        MenuEvaluationStatus status,
        T value,
        IEnumerable<MenuDebugDependency>? dependencies = null,
        IEnumerable<MenuEvaluationTraceEntry>? trace = null)
    {
        Status = status;
        Value = value;
        _dependencies = Array.AsReadOnly((dependencies ?? []).Distinct().ToArray());
        _trace = Array.AsReadOnly((trace ?? []).ToArray());
    }

    public MenuEvaluationStatus Status { get; }
    public bool IsKnown => Status == MenuEvaluationStatus.Known;
    public T Value { get; }
    public IReadOnlyList<MenuDebugDependency> Dependencies => _dependencies;
    public IReadOnlyList<MenuEvaluationTraceEntry> Trace => _trace;

    public bool TryGetValue(out T value)
    {
        value = Value;
        return IsKnown;
    }

    internal static MenuEvaluation<T> Known(
        T value,
        IEnumerable<MenuDebugDependency>? dependencies = null,
        IEnumerable<MenuEvaluationTraceEntry>? trace = null) =>
        new(MenuEvaluationStatus.Known, value, dependencies, trace);

    internal static MenuEvaluation<T> Unknown(
        T fallback,
        IEnumerable<MenuDebugDependency> dependencies,
        IEnumerable<MenuEvaluationTraceEntry> trace) =>
        new(MenuEvaluationStatus.Unknown, fallback, dependencies, trace);

    internal static MenuEvaluation<T> Error(
        T fallback,
        IEnumerable<MenuDebugDependency> dependencies,
        IEnumerable<MenuEvaluationTraceEntry> trace) =>
        new(MenuEvaluationStatus.Error, fallback, dependencies, trace);
}
