using System.Diagnostics;
using IW4.Game.Assets.Menu;
using IW4.Studio.Desktop.Documents.MenuEditing.Preview;
using IW4.Studio.Desktop.Rendering;
using IW4.Studio.Documents.MenuEditing;
using IW4.Studio.Documents.MenuEditing.Debugging;

namespace IW4.Studio.Desktop.ViewModels.Menu;

public sealed class MenuPlaySessionViewModel : ObservableObject
{
    private const int MaximumMenuTransitionsPerInput = 32;
    private const string WorkspaceConfigName = MenuPreviewConfig.MultiplayerDefaultName;
    private readonly MenuDesignerViewModel _designer;
    private readonly MenuEditorSnapshot _root;
    private readonly Stopwatch _clock = new();
    private readonly List<OpenMenu> _stack = [];
    private readonly Dictionary<string, MenuEditorSnapshot> _resolvedMenus =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _eventIssues = [];
    private readonly List<MenuDebugDependency> _eventDependencies = [];
    private readonly List<MenuDebugQueuedProfileLayout> _queuedProfileLayouts = [];
    private readonly MenuDebugLocalizationResolver? _localization;
    private IReadOnlyDictionary<string, string>? _config;
    private string _configName = WorkspaceConfigName;
    private bool _hasPickedConfig;
    private string? _configError;
    private MenuDebugScenario _overrides = MenuDebugScenario.Empty;
    private MenuDebugScenario _seed = MenuDebugScenario.Empty;
    private MenuDebugScenario _scenario = MenuDebugScenario.Empty;
    private MenuNodeId? _hoveredItem;
    private HashSet<MenuNodeId> _eligibleItems = [];
    private int _remainingTransitions;
    private MenuPreviewScene? _scene;
    private string _navigationPath = string.Empty;
    private string _focusedItem = string.Empty;
    private string _lastAction = string.Empty;
    private string _evaluationIssue = string.Empty;
    private string _eventIssue = string.Empty;
    private string? _selectedRegistration;

    internal MenuPlaySessionViewModel(
        MenuDesignerViewModel designer,
        MenuEditorSnapshot root)
    {
        _designer = designer;
        _root = root;
        Registrations = Array.AsReadOnly(designer.PlayRegistrations
            .Select(registration => registration.Name)
            .OfType<string>()
            .Where(name => !string.IsNullOrWhiteSpace(name) &&
                !string.Equals(name, root.Name, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray());
        _selectedRegistration = Registrations.FirstOrDefault();
        _localization = designer.PreviewDebug.LocalizationResolver;
        Simulation = new MenuPreviewSimulationViewModel(ApplyOverrides, () => { });
        Simulation.ReplaceDependencies(
            root.DebugProgram.Dependencies,
            designer.PreviewDebug.Simulation.Inputs);
        ReloadWorkspaceDefaults();
        Restart();
    }

    public IMenuPreviewMaterialResolver? MaterialResolver => _designer.MaterialResolver;
    public IMenuTextResourceResolver? TextResourceResolver => _designer.TextResourceResolver;
    public MenuPreviewSimulationViewModel Simulation { get; }
    public IReadOnlyList<string> Registrations { get; }
    public bool HasRegistrations => Registrations.Count > 0;
    public string? SelectedRegistration
    {
        get => _selectedRegistration;
        set => SetProperty(ref _selectedRegistration, value);
    }
    public MenuPreviewScene? Scene
    {
        get => _scene;
        private set
        {
            bool wasClosed = IsMenuClosed;
            if (SetProperty(ref _scene, value) && wasClosed != IsMenuClosed)
                OnPropertyChanged(nameof(IsMenuClosed));
        }
    }
    public bool IsMenuClosed => Scene is null;
    public string NavigationPath
    {
        get => _navigationPath;
        private set => SetProperty(ref _navigationPath, value);
    }
    public string FocusedItem
    {
        get => _focusedItem;
        private set => SetProperty(ref _focusedItem, value);
    }
    public string ConfigName => _configName;
    public string ConfigSource => _hasPickedConfig ? "Selected file · direct assignments" : "Workspace RawFile and its includes";
    public string ConfigSummary => _configError ?? (_config switch
    {
        null => "Config unavailable in this workspace",
        { Count: 0 } => "No literal dvar assignments found",
        _ => $"Loaded {_config.Count:N0} game variable defaults"
    });
    public string LastAction
    {
        get => _lastAction;
        private set => SetProperty(ref _lastAction, value);
    }
    public string EvaluationIssue
    {
        get => _evaluationIssue;
        private set
        {
            if (SetProperty(ref _evaluationIssue, value))
            {
                OnPropertyChanged(nameof(HasEvaluationIssue));
                OnPropertyChanged(nameof(HasDiagnostics));
            }
        }
    }
    public bool HasEvaluationIssue => EvaluationIssue.Length > 0;
    public string EventIssue
    {
        get => _eventIssue;
        private set
        {
            if (SetProperty(ref _eventIssue, value))
            {
                OnPropertyChanged(nameof(HasEventIssue));
                OnPropertyChanged(nameof(HasDiagnostics));
            }
        }
    }
    public bool HasEventIssue => EventIssue.Length > 0;
    public bool HasDiagnostics => HasEventIssue || HasEvaluationIssue;

    private OpenMenu? ActiveMenu => _stack.Count == 0 ? null : _stack[^1];

    internal void OpenSelectedRegistration()
    {
        if (SelectedRegistration is not { } name || !Registrations.Contains(name))
            return;

        BeginInput();
        OpenNamedMenu(name);
        Refresh();
    }

    internal void Restart()
    {
        _stack.Clear();
        _hoveredItem = null;
        _overrides = Simulation.BuildScenario(_localization, includeSamplePlayer: false);
        _seed = BuildSeed(_overrides);
        _scenario = _seed;
        _clock.Restart();
        BeginInput();
        Activate(_root);
        Refresh();
    }

    internal void ReloadWorkspaceDefaults()
    {
        try
        {
            ApplyConfig(WorkspaceConfigName, _designer.PlayConfigDefaults, isPicked: false);
        }
        catch (Exception exception) when (exception is
            IOException or InvalidDataException or ArgumentException or InvalidOperationException)
        {
            ReportConfigError(WorkspaceConfigName, exception.Message);
        }
    }

    internal void LoadPickedConfig(string name, string text) =>
        ApplyConfig(name, MenuPreviewConfig.ReadAssignments(text), isPicked: true);

    internal void ReportConfigError(string name, string message)
    {
        _configError = $"Could not read {name}. {message}";
        OnPropertyChanged(nameof(ConfigSummary));
    }

    private void ApplyConfig(
        string name,
        IReadOnlyDictionary<string, string>? values,
        bool isPicked)
    {
        _config = values;
        _configName = name;
        _hasPickedConfig = isPicked;
        _configError = null;
        Simulation.SetConfigDefaultLabels(_config);
        OnPropertyChanged(nameof(ConfigName));
        OnPropertyChanged(nameof(ConfigSource));
        OnPropertyChanged(nameof(ConfigSummary));
        if (!_clock.IsRunning)
            return;

        IReadOnlyDictionary<string, MenuDebugValue> previousDefaults = _seed.Dvars;
        _seed = BuildSeed(_overrides);
        var dvars = new Dictionary<string, MenuDebugValue>(
            _scenario.Dvars, StringComparer.OrdinalIgnoreCase);
        foreach (string previous in previousDefaults.Keys)
            dvars.Remove(previous);
        foreach ((string dvar, MenuDebugValue value) in _seed.Dvars)
            dvars[dvar] = value;

        _scenario = new MenuDebugScenario(
            _scenario.Milliseconds,
            dvars,
            _scenario.LocalVariables,
            _scenario.Environment,
            _scenario.OpenMenus,
            ActiveMenu?.FocusedItem,
            _localization,
            _scenario.ItemRuntimeStates,
            _scenario.Player,
            _scenario.StringTableResolver);
        BeginInput();
        LastAction = "Refreshed config defaults";
        Refresh();
    }

    internal void Advance()
    {
        int milliseconds = (int)Math.Min(int.MaxValue, _clock.ElapsedMilliseconds);
        if (_scenario.Milliseconds == milliseconds || ActiveMenu is null)
            return;

        _scenario = ScenarioFor(ActiveMenu.FocusedItem, milliseconds);
        Refresh();
    }

    internal bool CanTargetItem(MenuNodeId id) => _eligibleItems.Contains(id);

    internal void HandlePointerMove(MenuNodeId? itemId)
    {
        if (_hoveredItem == itemId || ActiveMenu is not { } menu)
            return;

        BeginInput();
        MenuNodeId? previous = _hoveredItem;
        _hoveredItem = itemId;
        if (FindItem(menu, previous) is { } exiting && exiting.Hooks.MouseExit.Handlers.Count > 0)
            Dispatch(menu, new MenuDebugItemHookInput(exiting.Id, MenuDebugItemHook.PointerExit));
        if (ReferenceEquals(ActiveMenu, menu) && FindItem(menu, itemId) is { } entering)
        {
            if (entering.Hooks.MouseEnter.Handlers.Count > 0)
                Dispatch(menu, new MenuDebugItemHookInput(entering.Id, MenuDebugItemHook.PointerEnter));
            if (ReferenceEquals(ActiveMenu, menu))
                Focus(menu, entering.Id);
        }
        Refresh();
    }

    internal void HandlePointerClick(MenuNodeId itemId, bool doubleClick)
    {
        if (ActiveMenu is not { } menu || !CanTargetItem(itemId))
            return;

        BeginInput();
        Focus(menu, itemId);
        if (ReferenceEquals(ActiveMenu, menu) && menu.FocusedItem == itemId)
            Accept(menu, doubleClick);
        Refresh();
    }

    internal void MoveFocus(int direction)
    {
        if (ActiveMenu is not { } menu || _eligibleItems.Count == 0)
            return;

        MenuNodeId[] items = menu.Snapshot.Items
            .Select(item => item.Id)
            .Where(CanTargetItem)
            .ToArray();
        int current = menu.FocusedItem is { } focused ? Array.IndexOf(items, focused) : -1;
        int next = current < 0
            ? (direction > 0 ? 0 : items.Length - 1)
            : (current + direction + items.Length) % items.Length;
        BeginInput();
        Focus(menu, items[next]);
        Refresh();
    }

    internal void HandleButton(int engineKey)
    {
        if (ActiveMenu is not { } menu)
            return;

        BeginInput();
        MenuDebugItemProgram? focused = menu.FocusedItem is { } itemId && CanTargetItem(itemId)
            ? FindItem(menu, itemId)
            : null;
        if (engineKey == 1 && focused?.Hooks.DoubleClick.Handlers.Count > 0)
        {
            Accept(menu, doubleClick: true);
        }
        else if (FindKey(menu.Snapshot.DebugProgram.Hooks.KeyHandlers, engineKey) is { } menuKey)
        {
            LastAction = $"Ran {DisplayName(menu.Snapshot)} key binding";
            Dispatch(menu, new MenuDebugMenuKeyInput(menuKey));
        }
        else if (focused is not null && FindKey(focused.Hooks.KeyHandlers, engineKey) is { } itemKey)
        {
            LastAction = $"Ran {focused.Name ?? "item"} key binding";
            Dispatch(menu, new MenuDebugItemKeyInput(focused.Id, itemKey));
        }
        else if (engineKey == 1)
        {
            Accept(menu, doubleClick: false);
        }
        else if (engineKey == 2)
        {
            LastAction = $"Back from {DisplayName(menu.Snapshot)}";
            if (menu.Snapshot.DebugProgram.Hooks.OnEscape.Handlers.Count > 0)
                Dispatch(menu, new MenuDebugMenuHookInput(MenuDebugMenuHook.Escape));
            else if (_stack.Count > 1)
                CloseActiveMenu();
        }

        Refresh();
    }

    private void Accept(OpenMenu menu, bool doubleClick)
    {
        if (FindItem(menu, menu.FocusedItem) is not { } item ||
            !EligibleItems(menu, menu.Snapshot.DebugProgram.Evaluate(ScenarioFor(item.Id)))
                .Contains(item.Id))
        {
            return;
        }

        LastAction = $"Selected {item.Name ?? "item"}";
        MenuDebugItemHook hook = doubleClick && item.Hooks.DoubleClick.Handlers.Count > 0
            ? MenuDebugItemHook.DoubleClick
            : item.Hooks.Action.Handlers.Count > 0
                ? MenuDebugItemHook.Action
                : MenuDebugItemHook.Accept;
        Dispatch(menu, new MenuDebugItemHookInput(item.Id, hook));
    }

    private void Focus(OpenMenu menu, MenuNodeId itemId)
    {
        if (menu.FocusedItem == itemId)
            return;

        string priorAction = LastAction;
        string focusAction = $"Focused {FindItem(menu, itemId)?.Name ?? "item"}";
        LastAction = focusAction;
        Dispatch(menu, new MenuDebugItemHookInput(itemId, MenuDebugItemHook.Focus));
        if (LastAction == focusAction && menu.FocusedItem != itemId)
            LastAction = priorAction;
    }

    private void Activate(MenuEditorSnapshot snapshot)
    {
        OpenMenu? previouslyOpen = _stack.FirstOrDefault(
            menu => ReferenceEquals(menu.Snapshot, snapshot));
        if (previouslyOpen is not null && ReferenceEquals(ActiveMenu, previouslyOpen))
            return;
        if (!TryBeginTransition())
            return;

        _hoveredItem = null;
        if (previouslyOpen is not null)
            _stack.Remove(previouslyOpen);
        var menu = previouslyOpen ?? new OpenMenu(snapshot);
        _stack.Add(menu);
        LastAction = $"Opened {DisplayName(snapshot)}";
        if (previouslyOpen is not null)
            return;

        ApplyResult(menu, snapshot.DebugProgram.Activate(ScenarioFor(null)));
        if (ReferenceEquals(ActiveMenu, menu) && menu.FocusedItem is null)
        {
            foreach (MenuNodeId first in EligibleItems(
                         menu, snapshot.DebugProgram.Evaluate(ScenarioFor(null))).Take(1))
            {
                Dispatch(menu, new MenuDebugItemHookInput(first, MenuDebugItemHook.Focus));
            }
        }
    }

    private void CloseActiveMenu()
    {
        if (ActiveMenu is not { } menu || !TryBeginTransition())
            return;

        MenuDebugDispatchResult result = menu.Snapshot.DebugProgram.Dispatch(
            new MenuDebugMenuHookInput(MenuDebugMenuHook.Close),
            ScenarioFor(menu.FocusedItem));
        _stack.Remove(menu);
        _hoveredItem = null;
        LastAction = $"Closed {DisplayName(menu.Snapshot)}";
        ApplyResult(menu, result);
    }

    private void Dispatch(OpenMenu menu, MenuDebugInput input) =>
        ApplyResult(menu, menu.Snapshot.DebugProgram.Dispatch(
            input, ScenarioFor(menu.FocusedItem)));

    private void ApplyResult(OpenMenu menu, MenuDebugDispatchResult result)
    {
        _scenario = result.NextScenario;
        menu.FocusedItem = _scenario.FocusedItemId;
        foreach (MenuDebugDispatchTraceEntry entry in result.Trace)
        {
            switch (entry)
            {
                case MenuDebugDiagnosticTraceEntry diagnostic when
                    diagnostic.Status != MenuEvaluationStatus.Known:
                    _eventIssues.Add(diagnostic.Message);
                    if (diagnostic.Dependency is { } dependency)
                        _eventDependencies.Add(dependency);
                    break;
                case MenuDebugDecisionTraceEntry decision when
                    decision.Status != MenuEvaluationStatus.Known:
                    _eventDependencies.AddRange(decision.Dependencies);
                    if (decision.Status == MenuEvaluationStatus.Unknown)
                        _eventIssues.Add("An event condition needs runtime values.");
                    break;
                case MenuDebugLocalVariableTraceEntry local when
                    local.Status != MenuEvaluationStatus.Known:
                    _eventDependencies.AddRange(local.Dependencies);
                    break;
                case MenuDebugDvarTraceEntry dvar:
                    LastAction = $"Set {dvar.Name} to {dvar.Value.AsString()}";
                    break;
                case MenuDebugQueuedScriptTraceEntry script:
                    ApplyQueuedCommands(menu, script);
                    break;
                case MenuDebugQueuedProfileLayout layout:
                    _queuedProfileLayouts.Add(layout);
                    break;
            }
        }
    }

    private void ApplyQueuedCommands(OpenMenu source, MenuDebugQueuedScriptTraceEntry script)
    {
        IReadOnlyList<string> tokens = script.CommandTokens;
        int offset = 0;
        // Stock scripts can chain fixed-arity navigation without a separator.
        // An unsupported command owns its remaining tokens until the next separator.
        while (offset + 1 < tokens.Count && !string.IsNullOrWhiteSpace(tokens[offset + 1]))
        {
            string command = tokens[offset];
            string targetName = tokens[offset + 1];
            if (string.Equals(command, "open", StringComparison.OrdinalIgnoreCase))
            {
                OpenNamedMenu(targetName);
            }
            else if (string.Equals(command, "close", StringComparison.OrdinalIgnoreCase) &&
                     ReferenceEquals(ActiveMenu, source) &&
                     (string.Equals(targetName, "self", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(targetName, source.Snapshot.Name, StringComparison.OrdinalIgnoreCase)))
            {
                CloseActiveMenu();
            }
            else
            {
                break;
            }

            offset += 2;
        }

        if (offset == 0 || offset < tokens.Count)
        {
            string unavailable = offset == 0 ? script.Script : string.Join(' ', tokens.Skip(offset));
            _eventIssues.Add($"Runtime command unavailable: {unavailable}");
        }
    }

    private void OpenNamedMenu(string name)
    {
        if (ResolveMenu(name) is { } target)
            Activate(target);
        else
            _eventIssues.Add($"Menu '{name}' cannot be resolved in this workspace.");
    }

    private MenuEditorSnapshot? ResolveMenu(string name)
    {
        if (string.Equals(_root.Name, name, StringComparison.OrdinalIgnoreCase))
            return _root;
        if (_resolvedMenus.TryGetValue(name, out MenuEditorSnapshot? previous))
            return previous;
        if (_designer.ResolvePlayMenu(name) is not { IsComplete: true } target)
            return null;

        _resolvedMenus.Add(name, target);
        Simulation.ReplaceDependencies(_root.DebugProgram.Dependencies.Concat(
            _resolvedMenus.Values.SelectMany(menu => menu.DebugProgram.Dependencies)));
        Simulation.SetConfigDefaultLabels(_config);
        return target;
    }

    private void BeginInput()
    {
        _eventIssues.Clear();
        _eventDependencies.Clear();
        _remainingTransitions = MaximumMenuTransitionsPerInput;
    }

    private bool TryBeginTransition()
    {
        if (_remainingTransitions == 0)
        {
            _eventIssues.Add("Menu transitions exceeded the preview limit for one input.");
            return false;
        }

        _remainingTransitions--;
        return true;
    }

    private void Refresh()
    {
        if (_queuedProfileLayouts.Count != 0)
        {
            // exec callbacks run after the menu handlers, including navigation.
            _scenario = _scenario.ApplyQueuedProfileLayouts(_queuedProfileLayouts);
            _queuedProfileLayouts.Clear();
        }
        NavigationPath = _stack.Count == 0
            ? "Menu closed"
            : string.Join("  /  ", _stack.Select(menu => DisplayName(menu.Snapshot)));
        EventIssue = string.Join(Environment.NewLine, _eventIssues.Distinct().Take(4));
        if (ActiveMenu is not { } menu)
        {
            _eligibleItems.Clear();
            FocusedItem = string.Empty;
            Scene = null;
            EvaluationIssue = string.Empty;
            Simulation.SetRequiredDependencies(_eventDependencies);
            return;
        }

        var activeScenario = ScenarioFor(menu.FocusedItem);
        MenuEvaluatedState evaluated = menu.Snapshot.DebugProgram.Evaluate(activeScenario);
        _eligibleItems = EligibleItems(menu, evaluated).ToHashSet();
        FocusedItem = menu.FocusedItem is { } focused && CanTargetItem(focused)
            ? FindItem(menu, focused)?.Name ?? string.Empty
            : string.Empty;
        MenuPreviewScene scene = MenuPreviewProjector.Project(menu.Snapshot, evaluated,
            MenuPreviewSettings.ForSimulation(activeScenario, _designer.PreviewDebug.LoadPercent));
        MenuPreviewEvaluationDiagnostics diagnostics =
            MenuPreviewEvaluationRelevance.Classify(evaluated, scene);
        Simulation.SetRequiredDependencies(diagnostics.Active
            .Where(issue => issue.Status == MenuEvaluationStatus.Unknown)
            .Select(issue => issue.Dependency)
            .OfType<MenuDebugDependency>()
            .Concat(_eventDependencies));
        EvaluationIssue = diagnostics.Active
            .FirstOrDefault(issue => issue.Status != MenuEvaluationStatus.Known)?.Message
            ?? string.Empty;
        Scene = AddPopupBackgrounds(scene);
    }

    private MenuPreviewScene AddPopupBackgrounds(MenuPreviewScene foreground)
    {
        int first = _stack.Count - 1;
        while (first > 0 && (_stack[first].Snapshot.Window.Value.StaticFlags &
                WindowStaticFlags.WINDOW_STATIC_POPUP) != 0)
        {
            first--;
        }

        if (first == _stack.Count - 1)
            return foreground;

        var primitives = new List<MenuPreviewPrimitive>();
        int layerOffset = 0;
        for (int index = first; index < _stack.Count; index++)
        {
            OpenMenu menu = _stack[index];
            MenuPreviewScene layer = ReferenceEquals(menu, ActiveMenu)
                ? foreground
                : MenuPreviewProjector.Project(menu.Snapshot,
                    menu.Snapshot.DebugProgram.Evaluate(ScenarioFor(menu.FocusedItem)), foreground.Settings);
            foreach (MenuPreviewPrimitive primitive in layer.Primitives)
                primitives.Add(primitive with { ZIndex = primitive.ZIndex + layerOffset });
            if (layer.Primitives.Count > 0)
                layerOffset += layer.Primitives[^1].ZIndex + 11;
        }

        return new MenuPreviewScene(
            foreground.Settings, primitives, foreground.HitRegions, foreground.FidelityIssues);
    }

    private void ApplyOverrides()
    {
        MenuDebugScenario overrides = Simulation.BuildScenario(_localization, includeSamplePlayer: false);
        MenuDebugScenario next = BuildSeed(overrides);
        _scenario = new MenuDebugScenario(
            _scenario.Milliseconds,
            ReplaceOverrides(_scenario.Dvars, _overrides.Dvars, overrides.Dvars, next.Dvars),
            ReplaceOverrides(_scenario.LocalVariables, _overrides.LocalVariables, overrides.LocalVariables),
            next.Environment,
            next.OpenMenus,
            ActiveMenu?.FocusedItem,
            _localization,
            _scenario.ItemRuntimeStates,
            new MenuDebugPlayer(next.Player.Data, ReplaceOverrides(
                _scenario.Player.Profile, _overrides.Player.Profile, overrides.Player.Profile, next.Player.Profile)),
            next.StringTableResolver);
        _overrides = overrides;
        _seed = next;
        BeginInput();
        LastAction = "Updated session values";
        Refresh();
    }

    private MenuDebugScenario BuildSeed(MenuDebugScenario overrides)
    {
        var dvars = new Dictionary<string, MenuDebugValue>(StringComparer.OrdinalIgnoreCase);
        if (_config is not null)
        {
            foreach ((string name, string value) in _config)
                dvars[name] = MenuDebugValue.FromString(value);
        }
        foreach ((string name, MenuDebugValue value) in overrides.Dvars)
            dvars[name] = value;

        MenuDebugPlayer sample = MenuPreviewSimulationViewModel.SamplePlayer;
        var playerData = new Dictionary<string, MenuDebugValue>(sample.Data, StringComparer.OrdinalIgnoreCase);
        var profile = new Dictionary<string, MenuDebugValue>(sample.Profile, StringComparer.OrdinalIgnoreCase);
        foreach ((string name, MenuDebugValue value) in overrides.Player.Data)
            playerData[name] = value;
        foreach ((string name, MenuDebugValue value) in overrides.Player.Profile)
            profile[name] = value;
        return new MenuDebugScenario(
            dvars: dvars,
            localVariables: overrides.LocalVariables,
            environment: overrides.Environment,
            openMenus: overrides.OpenMenus,
            localizationResolver: _localization,
            player: new MenuDebugPlayer(playerData, profile),
            stringTableResolver: _designer.PreviewDebug.StringTableResolver);
    }

    private MenuDebugScenario ScenarioFor(MenuNodeId? focus, int? milliseconds = null) =>
        new(milliseconds ?? _scenario.Milliseconds,
            _scenario.Dvars,
            _scenario.LocalVariables,
            _scenario.Environment,
            _seed.OpenMenus.Concat(_stack.Select(menu => menu.Snapshot.Name).OfType<string>()),
            focus,
            _localization,
            _scenario.ItemRuntimeStates,
            _scenario.Player,
            _scenario.StringTableResolver);

    private static Dictionary<string, MenuDebugValue> ReplaceOverrides(
        IReadOnlyDictionary<string, MenuDebugValue> runtime,
        IReadOnlyDictionary<string, MenuDebugValue> previous,
        IReadOnlyDictionary<string, MenuDebugValue> next,
        IReadOnlyDictionary<string, MenuDebugValue>? fallback = null)
    {
        var values = new Dictionary<string, MenuDebugValue>(runtime, StringComparer.OrdinalIgnoreCase);
        foreach (string name in previous.Keys)
        {
            if (next.ContainsKey(name))
                continue;
            if (fallback is not null && fallback.TryGetValue(name, out MenuDebugValue defaultValue))
                values[name] = defaultValue;
            else
                values.Remove(name);
        }

        foreach ((string name, MenuDebugValue value) in next)
        {
            if (!previous.TryGetValue(name, out MenuDebugValue oldValue) || value != oldValue)
                values[name] = value;
        }

        return values;
    }

    private static IEnumerable<MenuNodeId> EligibleItems(OpenMenu menu, MenuEvaluatedState evaluated)
    {
        if (!evaluated.Window.IsVisible.IsKnown || !evaluated.Window.IsVisible.Value)
            yield break;

        IReadOnlyList<MenuDebugItemProgram> items = menu.Snapshot.DebugProgram.Items;
        for (int index = 0; index < items.Count; index++)
        {
            MenuDebugItemProgram item = items[index];
            MenuEvaluatedItemState state = evaluated.Items[index];
            if (item.CanAcceptFocus &&
                state.IsVisible.IsKnown && state.IsVisible.Value &&
                state.IsDisabled.IsKnown && !state.IsDisabled.Value)
            {
                yield return item.Id;
            }
        }
    }

    private static MenuDebugItemProgram? FindItem(OpenMenu menu, MenuNodeId? id) =>
        menu.Snapshot.DebugProgram.Items.FirstOrDefault(item => item.Id == id);

    private static MenuDebugKeySelection? FindKey(IReadOnlyList<MenuDebugKeyHandler> handlers, int engineKey)
    {
        for (int index = 0; index < handlers.Count; index++)
        {
            if (handlers[index].Key == engineKey)
                return new MenuDebugKeySelection(engineKey, index);
        }

        return null;
    }

    private static string DisplayName(MenuEditorSnapshot snapshot) =>
        string.IsNullOrWhiteSpace(snapshot.Name) ? "Unnamed menu" : snapshot.Name;

    private sealed class OpenMenu(MenuEditorSnapshot snapshot)
    {
        public MenuEditorSnapshot Snapshot { get; } = snapshot;
        public MenuNodeId? FocusedItem { get; set; }
    }
}
