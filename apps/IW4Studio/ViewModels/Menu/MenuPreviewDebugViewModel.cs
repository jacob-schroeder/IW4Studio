using IW4.Game.Assets.Menu;
using IW4.Studio.Documents.MenuEditing;
using IW4.Studio.Documents.MenuEditing.Debugging;
using IW4.Studio.Desktop.Documents.MenuEditing.Preview;
using IW4.Studio.Desktop.Rendering;

namespace IW4.Studio.Desktop.ViewModels.Menu;

/// <summary>
/// Owns editor-only simulation state and deterministic Menu evaluation. It is
/// deliberately separate from authored document editing, invokes no game
/// runtime, and never mutates the serialized Menu graph.
/// </summary>
public sealed class MenuPreviewDebugViewModel : ObservableObject
{
    private readonly IMenuTextResourceResolver? _textResourceResolver;
    private readonly Func<IReadOnlyDictionary<string, string>?>? _readConfigDefaults;
    private MenuDebugScenario? _interactionScenario;
    private MenuEditorSnapshot? _snapshot;
    private MenuPreviewScene? _scene;
    private MenuEvaluatedState? _evaluatedState;
    private IReadOnlyList<MenuEvaluationTraceEntry> _diagnostics = [];
    private IReadOnlyList<MenuEvaluationTraceEntry> _dormantDiagnostics = [];
    private IReadOnlyList<MenuDebugDependency> _activationRequirements = [];
    private IReadOnlyList<MenuDebugDependency> _eventRequirements = [];
    private MenuOutlineNodeKind? _selectedKind;
    private MenuNodeId? _selectedNodeId;
    private MenuPreviewMode _mode;
    private bool _runClock = true;
    private int _loadPercent;
    private string? _configError;

    public MenuPreviewDebugViewModel(
        IMenuTextResourceResolver? textResourceResolver,
        MenuDebugStringTableResolver? stringTableResolver = null,
        Func<IReadOnlyDictionary<string, string>?>? readConfigDefaults = null)
    {
        StringTableResolver = stringTableResolver;
        _textResourceResolver = textResourceResolver;
        _readConfigDefaults = readConfigDefaults;
        Simulation = new MenuPreviewSimulationViewModel(
            BaseSimulationChanged,
            SimulationTimeChanged);
        Interaction = new MenuPreviewInteractionViewModel(DispatchInteraction);
        Comparison = new MenuPreviewStateComparisonViewModel(textResourceResolver);
        ResetSimulatedStateCommand = new ViewModelCommand(
            RestartSimulation,
            () => IsSimulating && _snapshot?.IsComplete == true);
    }

    public event EventHandler? PreviewChanged;

    public MenuPreviewScene? Scene => _scene;

    public MenuPreviewInteractionViewModel Interaction { get; }

    public MenuPreviewSimulationViewModel Simulation { get; }

    public MenuPreviewStateComparisonViewModel Comparison { get; }

    public ViewModelCommand ResetSimulatedStateCommand { get; }

    public MenuPreviewMode Mode
    {
        get => _mode;
        set
        {
            if (!SetProperty(ref _mode, value))
                return;
            OnPropertyChanged(nameof(IsSimulating));
            OnPropertyChanged(nameof(IsAuthored));
            OnPropertyChanged(nameof(HasSelectedAuthoredVisibilityExpression));
            ResetSimulatedStateCommand.RaiseCanExecuteChanged();
            ClearInteractionState();
            Refresh();
        }
    }

    public bool RunClock
    {
        get => _runClock;
        set => SetProperty(ref _runClock, value);
    }

    public bool IsSimulating => Mode == MenuPreviewMode.Simulate;

    public bool IsAuthored => !IsSimulating;

    public bool HasLoadBar =>
        _snapshot?.Window.Value.Style == WindowStyle.WINDOW_STYLE_LOADBAR ||
        _snapshot?.Items.Any(item => item.Value.Window.Style == WindowStyle.WINDOW_STYLE_LOADBAR) == true;

    public int LoadPercent
    {
        get => _loadPercent;
        set
        {
            if (SetProperty(ref _loadPercent, Math.Clamp(value, 0, 100)))
                Refresh();
        }
    }

    public bool HasSelectedAuthoredVisibilityExpression =>
        IsAuthored && SelectedHasVisibilityExpression();

    public int DiagnosticCount => _diagnostics.Count;

    public int DormantDiagnosticCount => _dormantDiagnostics.Count;

    public IReadOnlyList<string> DiagnosticLines => Array.AsReadOnly(
        _diagnostics.Select(FormatDiagnostic).ToArray());

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
                ? "No expression dependencies"
                : string.Join(
                    Environment.NewLine,
                    dependencies.Select(FormatDependency));
        }
    }

    public string EvaluationSummary
    {
        get
        {
            if (!IsSimulating)
                return "Expressions use authored values";
            int errors = _diagnostics.Count(value =>
                value.Status == MenuEvaluationStatus.Error);
            int unknown = _diagnostics.Count(value =>
                value.Status == MenuEvaluationStatus.Unknown);
            int missingInputs = Simulation.MissingInputCount;
            int runtimeIssues = Interaction.ActivationIssueCount +
                Interaction.ResultIssueCount;
            if (errors == 0 && unknown == 0 && runtimeIssues == 0)
                return "Simulation ready";
            var parts = new List<string>(3);
            if (errors > 0)
                parts.Add($"{errors:N0} error{(errors == 1 ? string.Empty : "s")}");
            if (missingInputs > 0)
            {
                parts.Add(
                    $"Needs {missingInputs:N0} value" +
                    (missingInputs == 1 ? string.Empty : "s"));
            }
            else if (unknown > 0)
            {
                parts.Add(
                    $"{unknown:N0} unresolved runtime result" +
                    (unknown == 1 ? string.Empty : "s"));
            }
            if (runtimeIssues > 0)
            {
                parts.Add(
                    $"{runtimeIssues:N0} event issue" +
                    (runtimeIssues == 1 ? string.Empty : "s"));
            }
            return string.Join(" · ", parts);
        }
    }

    public string EvaluationDetails => _diagnostics.Count == 0
        ? EvaluationSummary
        : string.Join(Environment.NewLine, DiagnosticLines);

    public string AdvancedEvaluationHeading => DormantDiagnosticCount == 0
        ? "Advanced evaluation details"
        : $"Advanced evaluation details · {DormantDiagnosticCount:N0} dormant";

    public string AdvancedEvaluationDetails
    {
        get
        {
            if (_dormantDiagnostics.Count == 0)
                return EvaluationDetails;

            string active = _diagnostics.Count == 0
                ? EvaluationSummary
                : string.Join(Environment.NewLine, DiagnosticLines);
            string dormant = string.Join(
                Environment.NewLine,
                _dormantDiagnostics.Select(FormatDiagnostic));
            return "Current rendered path" + Environment.NewLine +
                active + Environment.NewLine + Environment.NewLine +
                "Dormant/non-rendered expressions (not required)" +
                Environment.NewLine + dormant;
        }
    }

    public bool HasEvaluationIssues => IsSimulating &&
        (DiagnosticCount > 0 ||
         Interaction.ActivationIssueCount > 0 ||
         Interaction.ResultIssueCount > 0);

    public bool IsEvaluationReady => IsSimulating && !HasEvaluationIssues;

    public string FocusedItemSummary
    {
        get
        {
            MenuNodeId? focusedItemId = _interactionScenario?.FocusedItemId;
            if (focusedItemId is null)
                return "Focused: none";
            MenuDebugItemProgram? item = _snapshot?.DebugProgram.Items
                .FirstOrDefault(value => value.Id == focusedItemId);
            return "Focused: " + (string.IsNullOrWhiteSpace(item?.Name)
                ? focusedItemId.Value.ToString()
                : item.Name);
        }
    }

    public string SelectedEvaluationSummary
    {
        get
        {
            if (!IsSimulating || _evaluatedState is null)
                return string.Empty;
            if (_selectedKind == MenuOutlineNodeKind.Item &&
                _selectedNodeId is { } itemId)
            {
                MenuEvaluatedItemState? item = _evaluatedState.Items
                    .FirstOrDefault(value => value.Id == itemId);
                if (item is null)
                    return string.Empty;
                return $"State: {EvaluationValue(item.IsVisible, "visible", "hidden")}" +
                    $" · {EvaluationValue(item.IsDisabled, "disabled", "enabled")}";
            }

            return _selectedKind is MenuOutlineNodeKind.Menu or
                MenuOutlineNodeKind.Window
                    ? $"State: {EvaluationValue(
                        _evaluatedState.Window.IsVisible,
                        "visible",
                        "hidden")}"
                    : string.Empty;
        }
    }

    public void ReplaceDocument(MenuEditorSnapshot? snapshot)
    {
        _snapshot = snapshot;
        OnPropertyChanged(nameof(HasLoadBar));
        Comparison.ReplaceDocument(snapshot);
        ClearInteractionState();
        Simulation.ReplaceDocument(snapshot);
        Interaction.ReplaceOptions(
            snapshot?.DebugProgram,
            _selectedKind,
            _selectedNodeId);
        Refresh();
        OnPropertyChanged(nameof(DependencyDetails));
        ResetSimulatedStateCommand.RaiseCanExecuteChanged();
    }

    public void SelectNode(MenuOutlineNodeKind? kind, MenuNodeId? nodeId)
    {
        if (_selectedKind == kind && _selectedNodeId == nodeId)
            return;
        _selectedKind = kind;
        _selectedNodeId = nodeId;
        _eventRequirements = [];
        Interaction.ReplaceOptions(_snapshot?.DebugProgram, kind, nodeId);
        UpdateRequiredInputs();
        OnPropertyChanged(nameof(EvaluationSummary));
        OnPropertyChanged(nameof(HasEvaluationIssues));
        OnPropertyChanged(nameof(IsEvaluationReady));
        OnPropertyChanged(nameof(SelectedEvaluationSummary));
        OnPropertyChanged(nameof(HasSelectedAuthoredVisibilityExpression));
    }

    /// <summary>
    /// Reevaluates the current authored or simulated scene after an external
    /// text resource changes. Interaction state remains intact; only values
    /// resolved from localization are read again.
    /// </summary>
    internal void RefreshTextResources() => Refresh();

    private void Refresh()
    {
        _evaluatedState = null;
        _diagnostics = [];
        _dormantDiagnostics = [];
        if (_snapshot is not { IsComplete: true } snapshot)
        {
            _scene = null;
            NotifyEvaluationChanged();
            return;
        }

        if (!IsSimulating)
        {
            Simulation.SetRequiredDependencies([]);
            _scene = MenuPreviewProjector.Project(snapshot);
            NotifyEvaluationChanged();
            return;
        }

        MenuDebugScenario scenario = EnsureSimulatedScenario(snapshot);
        _evaluatedState = snapshot.DebugProgram.Evaluate(scenario);
        _scene = MenuPreviewProjector.Project(snapshot, _evaluatedState,
            MenuPreviewSettings.ForSimulation(scenario, LoadPercent));
        MenuPreviewEvaluationDiagnostics relevance =
            MenuPreviewEvaluationRelevance.Classify(_evaluatedState, _scene);
        _diagnostics = Array.AsReadOnly(
            relevance.Active
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
        _dormantDiagnostics = relevance.Dormant;
        UpdateRequiredInputs();
        NotifyEvaluationChanged();
    }

    private void UpdateRequiredInputs() =>
        Simulation.SetRequiredDependencies(_diagnostics
            .Where(value => value.Status == MenuEvaluationStatus.Unknown)
            .Select(value => value.Dependency)
            .OfType<MenuDebugDependency>()
            .Concat(_activationRequirements)
            .Concat(_eventRequirements));

    private void NotifyEvaluationChanged()
    {
        Comparison.Update(_evaluatedState, _scene, Simulation.Milliseconds);
        OnPropertyChanged(nameof(Scene));
        OnPropertyChanged(nameof(DiagnosticCount));
        OnPropertyChanged(nameof(DormantDiagnosticCount));
        OnPropertyChanged(nameof(DiagnosticLines));
        OnPropertyChanged(nameof(EvaluationSummary));
        OnPropertyChanged(nameof(EvaluationDetails));
        OnPropertyChanged(nameof(AdvancedEvaluationHeading));
        OnPropertyChanged(nameof(AdvancedEvaluationDetails));
        OnPropertyChanged(nameof(HasEvaluationIssues));
        OnPropertyChanged(nameof(IsEvaluationReady));
        OnPropertyChanged(nameof(FocusedItemSummary));
        OnPropertyChanged(nameof(SelectedEvaluationSummary));
        OnPropertyChanged(nameof(HasSelectedAuthoredVisibilityExpression));
        PreviewChanged?.Invoke(this, EventArgs.Empty);
    }

    internal MenuDebugLocalizationResolver? LocalizationResolver =>
        _textResourceResolver is null ? null : ResolveLocalization;

    internal MenuDebugStringTableResolver? StringTableResolver { get; }

    private MenuDebugScenario BuildBaseScenario()
    {
        IReadOnlyDictionary<string, string>? config = null;
        try
        {
            config = _readConfigDefaults?.Invoke();
        }
        catch (Exception exception) when (exception is
            IOException or InvalidDataException or ArgumentException or InvalidOperationException)
        {
            _configError = $"Could not read {MenuPreviewConfig.MultiplayerDefaultName}. {exception.Message}";
        }

        Simulation.SetConfigDefaultLabels(config);
        return Simulation.BuildScenario(LocalizationResolver, StringTableResolver, dvarDefaults: config);
    }

    private MenuDebugScenario EnsureSimulatedScenario(
        MenuEditorSnapshot snapshot)
    {
        if (_interactionScenario is not null)
            return _interactionScenario;

        MenuDebugDispatchResult activation = snapshot.DebugProgram.Activate(
            BuildBaseScenario());
        _interactionScenario = activation.NextScenario.ApplyQueuedProfileLayouts(
            activation.Trace.OfType<MenuDebugQueuedProfileLayout>());
        _activationRequirements = MissingRuntimeDependencies(activation);
        _eventRequirements = [];
        Interaction.ShowActivation(activation);
        return _interactionScenario;
    }

    private string? ResolveLocalization(string key)
    {
        MenuLocalizedTextResolution resolution = _textResourceResolver!
            .ResolveText($"@{key}");
        return resolution.IsResolved ? resolution.DisplayText : null;
    }

    private IEnumerable<MenuEvaluationTraceEntry> ScenarioValidationDiagnostics()
    {
        if (_configError is { } configError)
            yield return new MenuEvaluationTraceEntry(MenuEvaluationStatus.Error, configError, null, null);

        foreach (MenuPreviewScenarioInputViewModel input in Simulation.Inputs.Where(input => input.HasValidationError))
        {
            yield return new MenuEvaluationTraceEntry(
                MenuEvaluationStatus.Error,
                $"Simulation value '{input.KindLabel} {input.Name}': " +
                input.ValidationMessage,
                input.Dependency.Operation,
                input.Dependency);
        }
    }

    private void BaseSimulationChanged()
    {
        ClearInteractionState();
        if (IsSimulating)
            Refresh();
    }

    private void SimulationTimeChanged()
    {
        if (!IsSimulating)
            return;

        if (_interactionScenario is { } scenario)
        {
            _interactionScenario = new MenuDebugScenario(
                Simulation.Milliseconds,
                scenario.Dvars,
                scenario.LocalVariables,
                scenario.Environment,
                scenario.OpenMenus,
                scenario.FocusedItemId,
                scenario.LocalizationResolver,
                scenario.ItemRuntimeStates,
                scenario.Player,
                scenario.StringTableResolver);
        }

        Refresh();
    }

    private void DispatchInteraction(MenuDebugInput input)
    {
        if (_snapshot is not { IsComplete: true } snapshot)
            return;
        MenuDebugScenario scenario = _interactionScenario ??
            EnsureSimulatedScenario(snapshot);
        MenuDebugDispatchResult result = snapshot.DebugProgram.Dispatch(
            input,
            scenario);
        _interactionScenario = result.NextScenario.ApplyQueuedProfileLayouts(
            result.Trace.OfType<MenuDebugQueuedProfileLayout>());
        _eventRequirements = MissingRuntimeDependencies(result);
        Interaction.ShowResult(result);
        Refresh();
    }

    private void RestartSimulation()
    {
        ClearInteractionState();
        if (!IsSimulating)
            return;

        if (Simulation.Milliseconds == 0)
        {
            Refresh();
            return;
        }

        Simulation.Milliseconds = 0;
    }

    private void ClearInteractionState()
    {
        _interactionScenario = null;
        _configError = null;
        _activationRequirements = [];
        _eventRequirements = [];
        Interaction.ClearInteractionState();
    }

    private static IReadOnlyList<MenuDebugDependency> MissingRuntimeDependencies(
        MenuDebugDispatchResult result) => Array.AsReadOnly(result.Trace
        .SelectMany(value => value switch
        {
            MenuDebugDecisionTraceEntry decision when
                decision.Status == MenuEvaluationStatus.Unknown =>
                decision.Dependencies,
            MenuDebugLocalVariableTraceEntry local when
                local.Status == MenuEvaluationStatus.Unknown =>
                local.Dependencies,
            MenuDebugDiagnosticTraceEntry diagnostic when
                diagnostic.Status == MenuEvaluationStatus.Unknown &&
                diagnostic.Dependency is not null =>
                [diagnostic.Dependency],
            _ => []
        })
        .Distinct()
        .ToArray());

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

    private bool SelectedHasVisibilityExpression()
    {
        if (_snapshot is not { } snapshot)
            return false;
        if (_selectedKind is MenuOutlineNodeKind.Menu or
            MenuOutlineNodeKind.Window)
        {
            return snapshot.Behavior.HasVisibleExpression;
        }
        if (_selectedKind != MenuOutlineNodeKind.Item ||
            _selectedNodeId is not { } itemId)
        {
            return false;
        }

        MenuItemSnapshot? item = snapshot.Items
            .FirstOrDefault(value => value.Id == itemId);
        return item?.Value.Behavior.HasVisibleExpression == true;
    }
}
