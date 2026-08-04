using IW4.Assets.Assets.Menu;
using IW4.Studio.Documents.MenuEditing;
using IW4.Studio.Documents.MenuEditing.Debugging;
using IW4.Studio.Documents.MenuEditing.Preview;
using IW4.Studio.Rendering;

namespace IW4.Studio.Desktop.ViewModels.Menu;

/// <summary>
/// Owns editor-only scenario state and deterministic Menu evaluation. It is
/// deliberately separate from authored document editing and never executes
/// scripts or mutates the serialized Menu graph.
/// </summary>
public sealed class MenuPreviewDebugViewModel : ObservableObject
{
    private static readonly IReadOnlyList<MenuPreviewMode> PreviewModeValues =
        Array.AsReadOnly(Enum.GetValues<MenuPreviewMode>());
    private readonly IMenuTextResourceResolver? _textResourceResolver;
    private MenuDebugScenario? _interactionScenario;
    private MenuEditorSnapshot? _snapshot;
    private MenuPreviewScene? _scene;
    private MenuEvaluatedState? _evaluatedState;
    private IReadOnlyList<MenuEvaluationTraceEntry> _diagnostics = [];
    private IReadOnlyList<MenuPreviewScenarioInputViewModel> _scenarioInputs = [];
    private IReadOnlyList<MenuPreviewFocusOption> _scenarioFocusItems = [];
    private MenuPreviewFocusOption? _selectedScenarioFocus;
    private MenuOutlineNodeKind? _selectedKind;
    private MenuNodeId? _selectedNodeId;
    private MenuPreviewMode _mode;
    private int _milliseconds;

    public MenuPreviewDebugViewModel(
        IMenuTextResourceResolver? textResourceResolver)
    {
        _textResourceResolver = textResourceResolver;
        Interaction = new MenuPreviewInteractionViewModel(
            DispatchInteraction,
            ResetInteractionState);
    }

    public event EventHandler? PreviewChanged;

    public MenuPreviewScene? Scene => _scene;

    public MenuPreviewInteractionViewModel Interaction { get; }

    public IReadOnlyList<MenuPreviewMode> Modes => PreviewModeValues;

    public MenuPreviewMode Mode
    {
        get => _mode;
        set
        {
            if (!SetProperty(ref _mode, value))
                return;
            OnPropertyChanged(nameof(IsScenario));
            OnPropertyChanged(nameof(ModeBadge));
            Refresh();
        }
    }

    public bool IsScenario => Mode == MenuPreviewMode.Scenario;

    public string ModeBadge => IsScenario
        ? "SCENARIO / EVALUATED"
        : "AUTHORED / STATIC";

    public int Milliseconds
    {
        get => _milliseconds;
        set
        {
            if (!SetProperty(ref _milliseconds, value))
                return;
            ClearInteractionState();
            if (IsScenario)
                Refresh();
        }
    }

    public IReadOnlyList<MenuPreviewScenarioInputViewModel> ScenarioInputs =>
        _scenarioInputs;

    public bool HasScenarioInputs => ScenarioInputs.Count > 0;

    public string ScenarioInputHeading =>
        $"Scenario inputs ({ScenarioInputs.Count:N0})";

    public IReadOnlyList<MenuPreviewFocusOption> FocusItems =>
        _scenarioFocusItems;

    public MenuPreviewFocusOption? SelectedFocus
    {
        get => _selectedScenarioFocus;
        set
        {
            if (!SetProperty(ref _selectedScenarioFocus, value))
                return;
            ClearInteractionState();
            if (IsScenario)
                Refresh();
        }
    }

    public int DiagnosticCount => _diagnostics.Count;

    public IReadOnlyList<string> DiagnosticLines => Array.AsReadOnly(
        _diagnostics.Select(FormatDiagnostic).ToArray());

    public string DependencySummary
    {
        get
        {
            int count = _snapshot?.DebugProgram.Dependencies.Count ?? 0;
            return count == 0
                ? "No expression dependencies"
                : $"{count:N0} expression " +
                  $"dependenc{(count == 1 ? "y" : "ies")}";
        }
    }

    public string DependencyDetails
    {
        get
        {
            MenuDebugDependency[] dependencies = _snapshot?.DebugProgram
                .Dependencies
                .OrderBy(value => value.Kind)
                .ThenBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? [];
            return dependencies.Length == 0
                ? DependencySummary
                : string.Join(
                    Environment.NewLine,
                    dependencies.Select(FormatDependency));
        }
    }

    public string EvaluationSummary
    {
        get
        {
            if (!IsScenario)
                return "Expressions use authored values";
            int errors = _diagnostics.Count(value =>
                value.Status == MenuEvaluationStatus.Error);
            int unknown = _diagnostics.Count(value =>
                value.Status == MenuEvaluationStatus.Unknown);
            if (errors == 0 && unknown == 0)
                return "Evaluation complete";
            var parts = new List<string>(2);
            if (errors > 0)
                parts.Add($"{errors:N0} error{(errors == 1 ? string.Empty : "s")}");
            if (unknown > 0)
            {
                parts.Add(
                    $"{unknown:N0} unknown" +
                    (unknown == 1 ? string.Empty : "s"));
            }
            return string.Join(" · ", parts);
        }
    }

    public string EvaluationDetails => _diagnostics.Count == 0
        ? EvaluationSummary
        : string.Join(Environment.NewLine, DiagnosticLines);

    public string SelectedEvaluationSummary
    {
        get
        {
            if (!IsScenario || _evaluatedState is null)
                return string.Empty;
            if (_selectedKind == MenuOutlineNodeKind.Item &&
                _selectedNodeId is { } itemId)
            {
                MenuEvaluatedItemState? item = _evaluatedState.Items
                    .FirstOrDefault(value => value.Id == itemId);
                if (item is null)
                    return string.Empty;
                return $"Selected: {EvaluationValue(item.IsVisible, "visible", "hidden")}" +
                    $" · {EvaluationValue(item.IsDisabled, "disabled", "enabled")}";
            }

            return _selectedKind is MenuOutlineNodeKind.Menu or
                MenuOutlineNodeKind.Window
                    ? $"Selected: {EvaluationValue(
                        _evaluatedState.Window.IsVisible,
                        "visible",
                        "hidden")}"
                    : string.Empty;
        }
    }

    public string SelectedEventSummary
    {
        get
        {
            (int hookCount, int keyCount) = SelectedEventCounts();
            return hookCount == 0 && keyCount == 0
                ? "No authored event hooks"
                : $"Events: {hookCount:N0} hook" +
                  $"{(hookCount == 1 ? string.Empty : "s")} · " +
                  $"{keyCount:N0} key" +
                  $"{(keyCount == 1 ? string.Empty : "s")}";
        }
    }

    public string SelectedEventDetails
    {
        get
        {
            string[] details = SelectedEvents().ToArray();
            return details.Length == 0
                ? "The selected entity has no authored hooks. Explicit " +
                  "focus transitions remain available for Item selections."
                : string.Join(Environment.NewLine, details) +
                  Environment.NewLine +
                  "Use Explicit interactions to dispatch one authored hook. " +
                  "Debugger-safe focus and local-variable changes are applied; " +
                  "authored scripts are queued for inspection only.";
        }
    }

    public void ReplaceDocument(MenuEditorSnapshot? snapshot)
    {
        _snapshot = snapshot;
        ClearInteractionState();
        RebuildScenarioSources(snapshot);
        Interaction.ReplaceOptions(
            snapshot?.DebugProgram,
            _selectedKind,
            _selectedNodeId);
        Refresh();
        OnPropertyChanged(nameof(ScenarioInputs));
        OnPropertyChanged(nameof(HasScenarioInputs));
        OnPropertyChanged(nameof(ScenarioInputHeading));
        OnPropertyChanged(nameof(FocusItems));
        OnPropertyChanged(nameof(SelectedFocus));
        OnPropertyChanged(nameof(DependencySummary));
        OnPropertyChanged(nameof(DependencyDetails));
    }

    public void SelectNode(MenuOutlineNodeKind? kind, MenuNodeId? nodeId)
    {
        if (_selectedKind == kind && _selectedNodeId == nodeId)
            return;
        _selectedKind = kind;
        _selectedNodeId = nodeId;
        Interaction.ReplaceOptions(_snapshot?.DebugProgram, kind, nodeId);
        OnPropertyChanged(nameof(SelectedEvaluationSummary));
        OnPropertyChanged(nameof(SelectedEventSummary));
        OnPropertyChanged(nameof(SelectedEventDetails));
    }

    private void Refresh()
    {
        _evaluatedState = null;
        _diagnostics = [];
        if (_snapshot is not { IsComplete: true } snapshot)
        {
            _scene = null;
            NotifyEvaluationChanged();
            return;
        }

        if (!IsScenario)
        {
            _scene = MenuPreviewProjector.Project(snapshot);
            NotifyEvaluationChanged();
            return;
        }

        MenuDebugScenario scenario = _interactionScenario ?? BuildBaseScenario();
        _evaluatedState = snapshot.DebugProgram.Evaluate(scenario);
        _diagnostics = Array.AsReadOnly(
            _evaluatedState.Trace
                .Concat(ScenarioValidationDiagnostics())
                .Where(value => value.Status != MenuEvaluationStatus.Known)
                .DistinctBy(value => new
                {
                    value.Status,
                    value.Message,
                    value.Operation,
                    value.Dependency
                })
                .ToArray());
        _scene = MenuPreviewProjector.Project(snapshot, _evaluatedState);
        NotifyEvaluationChanged();
    }

    private void NotifyEvaluationChanged()
    {
        OnPropertyChanged(nameof(Scene));
        OnPropertyChanged(nameof(DiagnosticCount));
        OnPropertyChanged(nameof(DiagnosticLines));
        OnPropertyChanged(nameof(EvaluationSummary));
        OnPropertyChanged(nameof(EvaluationDetails));
        OnPropertyChanged(nameof(SelectedEvaluationSummary));
        PreviewChanged?.Invoke(this, EventArgs.Empty);
    }

    private MenuDebugScenario BuildBaseScenario()
    {
        var dvars = new Dictionary<string, MenuDebugValue>(
            StringComparer.OrdinalIgnoreCase);
        var locals = new Dictionary<string, MenuDebugValue>(
            StringComparer.OrdinalIgnoreCase);
        var environment = new Dictionary<
            MenuDebugEnvironmentKey,
            MenuDebugValue>();
        var openMenus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (MenuPreviewScenarioInputViewModel input in ScenarioInputs)
        {
            if (!input.IsSet ||
                !input.TryGetValue(out MenuDebugValue value, out _))
            {
                continue;
            }

            MenuDebugDependency dependency = input.Dependency;
            switch (dependency.Kind)
            {
                case MenuDebugDependencyKind.Dvar:
                    dvars[dependency.Name] = value;
                    break;
                case MenuDebugDependencyKind.LocalVariable:
                    locals[dependency.Name] = value;
                    break;
                case MenuDebugDependencyKind.Environment when
                    dependency.Operation is { } operation:
                    environment[new MenuDebugEnvironmentKey(
                        operation,
                        EnvironmentQualifier(dependency))] = value;
                    break;
                case MenuDebugDependencyKind.Menu when
                    value.TryGetBoolean(out bool isOpen) && isOpen:
                    openMenus.Add(dependency.Name);
                    break;
            }
        }

        return new MenuDebugScenario(
            Milliseconds,
            dvars,
            locals,
            environment,
            openMenus,
            SelectedFocus?.ItemId,
            _textResourceResolver is null ? null : ResolveLocalization);
    }

    private string? ResolveLocalization(string key)
    {
        MenuLocalizedTextResolution resolution = _textResourceResolver!
            .ResolveText($"@{key}");
        return resolution.IsResolved ? resolution.DisplayText : null;
    }

    private IEnumerable<MenuEvaluationTraceEntry> ScenarioValidationDiagnostics() =>
        ScenarioInputs
            .Where(input => input.HasValidationError)
            .Select(input => new MenuEvaluationTraceEntry(
                MenuEvaluationStatus.Error,
                $"Scenario input '{input.KindLabel} {input.Name}': " +
                input.ValidationMessage,
                input.Dependency.Operation,
                input.Dependency));

    private void RebuildScenarioSources(MenuEditorSnapshot? snapshot)
    {
        var previous = ScenarioInputs.ToDictionary(
            value => value.Identity,
            value => (value.IsSet, value.ValueInput),
            StringComparer.OrdinalIgnoreCase);
        MenuDebugDependency[] dependencies = snapshot?.DebugProgram.Dependencies
            .Where(IsScenarioInput)
            .GroupBy(ScenarioStorageKey, StringComparer.OrdinalIgnoreCase)
            .Select(CollapseScenarioDependency)
            .OrderBy(value => value.Kind)
            .ThenBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        _scenarioInputs = Array.AsReadOnly(dependencies.Select(dependency =>
        {
            var probe = new MenuPreviewScenarioInputViewModel(
                dependency,
                ScenarioInputChanged);
            return previous.TryGetValue(probe.Identity, out var state)
                ? new MenuPreviewScenarioInputViewModel(
                    dependency,
                    ScenarioInputChanged,
                    state.IsSet,
                    state.ValueInput)
                : probe;
        }).ToArray());

        MenuNodeId? previousFocus = _selectedScenarioFocus?.ItemId;
        var focusItems = new List<MenuPreviewFocusOption>
        {
            new(null, "No focused item")
        };
        if (snapshot is not null)
        {
            focusItems.AddRange(snapshot.DebugProgram.Items
                .Select((item, index) => new MenuPreviewFocusOption(
                    item.Id,
                    string.IsNullOrWhiteSpace(item.Name)
                        ? $"Item {index + 1:N0}"
                        : item.Name)));
        }
        _scenarioFocusItems = Array.AsReadOnly(focusItems.ToArray());
        _selectedScenarioFocus = _scenarioFocusItems.FirstOrDefault(value =>
                value.ItemId == previousFocus) ??
            _scenarioFocusItems[0];
    }

    private void ScenarioInputChanged()
    {
        ClearInteractionState();
        if (IsScenario)
            Refresh();
    }

    private void DispatchInteraction(MenuDebugInput input)
    {
        if (_snapshot is not { IsComplete: true } snapshot)
            return;
        MenuDebugScenario scenario = _interactionScenario ?? BuildBaseScenario();
        MenuDebugDispatchResult result = snapshot.DebugProgram.Dispatch(
            input,
            scenario);
        _interactionScenario = result.NextScenario;
        Interaction.ShowResult(result);
        Refresh();
    }

    private void ResetInteractionState()
    {
        ClearInteractionState();
        if (IsScenario)
            Refresh();
    }

    private void ClearInteractionState()
    {
        _interactionScenario = null;
        Interaction.ClearInteractionState();
    }

    private static bool IsScenarioInput(MenuDebugDependency dependency) =>
        !string.Equals(
            dependency.Name,
            "<dynamic>",
            StringComparison.OrdinalIgnoreCase) &&
        (dependency.Kind is
            MenuDebugDependencyKind.Dvar or
            MenuDebugDependencyKind.LocalVariable or
            MenuDebugDependencyKind.Menu ||
         dependency.Kind == MenuDebugDependencyKind.Environment &&
         dependency.Operation is not null &&
         dependency.Operation != OperationEnum.OP_MILLISECONDS);

    private static string ScenarioStorageKey(MenuDebugDependency dependency) =>
        dependency.Kind == MenuDebugDependencyKind.Environment
            ? $"{dependency.Kind}:{dependency.Operation}:{dependency.Name}"
            : $"{dependency.Kind}:{dependency.Name}";

    private static MenuDebugDependency CollapseScenarioDependency(
        IGrouping<string, MenuDebugDependency> group)
    {
        MenuDebugDependency first = group.First();
        MenuDebugValueKind?[] kinds = group
            .Select(value => value.ValueKind)
            .Distinct()
            .ToArray();
        return kinds.Length <= 1
            ? first
            : first with { ValueKind = MenuDebugValueKind.String };
    }

    private static string? EnvironmentQualifier(
        MenuDebugDependency dependency) =>
        dependency.Operation is { } operation &&
        !string.Equals(
            dependency.Name,
            operation.ToString(),
            StringComparison.OrdinalIgnoreCase)
                ? dependency.Name
                : null;

    private static string FormatDependency(MenuDebugDependency value)
    {
        string expected = value.ValueKind is { } kind
            ? $" → {kind}"
            : string.Empty;
        return $"{value.Kind}: {value.Name}{expected}";
    }

    private static string FormatDiagnostic(MenuEvaluationTraceEntry value)
    {
        string dependency = value.Dependency is null
            ? string.Empty
            : $" [{value.Dependency.Kind}: {value.Dependency.Name}]";
        return $"{value.Status}: {value.Message}{dependency}";
    }

    private static string EvaluationValue(
        MenuEvaluation<bool> value,
        string whenTrue,
        string whenFalse)
    {
        string result = value.Value ? whenTrue : whenFalse;
        return value.Status == MenuEvaluationStatus.Known
            ? result
            : $"{result} ({value.Status.ToString().ToLowerInvariant()} fallback)";
    }

    private (int HookCount, int KeyCount) SelectedEventCounts()
    {
        if (_snapshot is null)
            return default;
        if (_selectedKind == MenuOutlineNodeKind.Item &&
            _selectedNodeId is { } itemId)
        {
            MenuDebugItemHooks? hooks = _snapshot.DebugProgram.Items
                .FirstOrDefault(value => value.Id == itemId)?.Hooks;
            return hooks is null
                ? default
                : (
                    ItemEventSets(hooks).Sum(value => value.Set.Handlers.Count),
                    hooks.KeyHandlers.Count);
        }
        if (_selectedKind is MenuOutlineNodeKind.Menu or
            MenuOutlineNodeKind.Window)
        {
            MenuDebugMenuHooks hooks = _snapshot.DebugProgram.Hooks;
            return (
                MenuEventSets(hooks).Sum(value => value.Set.Handlers.Count),
                hooks.KeyHandlers.Count);
        }
        return default;
    }

    private IEnumerable<string> SelectedEvents()
    {
        if (_snapshot is null)
            yield break;
        if (_selectedKind == MenuOutlineNodeKind.Item &&
            _selectedNodeId is { } itemId)
        {
            MenuDebugItemHooks? hooks = _snapshot.DebugProgram.Items
                .FirstOrDefault(value => value.Id == itemId)?.Hooks;
            if (hooks is null)
                yield break;
            foreach ((string label, MenuDebugEventSet set) in ItemEventSets(hooks))
            {
                if (set.Handlers.Count > 0)
                    yield return $"{label}: {set.Handlers.Count:N0} handler(s)";
            }
            if (hooks.KeyHandlers.Count > 0)
            {
                yield return "Keys: " + string.Join(
                    ", ",
                    hooks.KeyHandlers.Select(value => value.Key));
            }
            yield break;
        }

        if (_selectedKind is MenuOutlineNodeKind.Menu or
            MenuOutlineNodeKind.Window)
        {
            MenuDebugMenuHooks hooks = _snapshot.DebugProgram.Hooks;
            foreach ((string label, MenuDebugEventSet set) in MenuEventSets(hooks))
            {
                if (set.Handlers.Count > 0)
                    yield return $"{label}: {set.Handlers.Count:N0} handler(s)";
            }
            if (hooks.KeyHandlers.Count > 0)
            {
                yield return "Keys: " + string.Join(
                    ", ",
                    hooks.KeyHandlers.Select(value => value.Key));
            }
        }
    }

    private static IEnumerable<(string Label, MenuDebugEventSet Set)>
        MenuEventSets(MenuDebugMenuHooks hooks)
    {
        yield return ("On open", hooks.OnOpen);
        yield return ("On close request", hooks.OnCloseRequest);
        yield return ("On close", hooks.OnClose);
        yield return ("On escape", hooks.OnEscape);
    }

    private static IEnumerable<(string Label, MenuDebugEventSet Set)>
        ItemEventSets(MenuDebugItemHooks hooks)
    {
        yield return ("Mouse enter text", hooks.MouseEnterText);
        yield return ("Mouse exit text", hooks.MouseExitText);
        yield return ("Mouse enter", hooks.MouseEnter);
        yield return ("Mouse exit", hooks.MouseExit);
        yield return ("Action", hooks.Action);
        yield return ("Accept", hooks.Accept);
        yield return ("On focus", hooks.OnFocus);
        yield return ("Leave focus", hooks.LeaveFocus);
        yield return ("Double click", hooks.DoubleClick);
    }
}
