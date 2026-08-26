using System.Globalization;
using IW4.Assets.Assets.Menu;

namespace IW4.Studio.Documents.MenuEditing.Debugging;

/// <summary>
/// Applies focus transitions in native lifecycle order: the previous Item's
/// leave-focus handlers run before the target Item receives focus and runs its
/// on-focus handlers.
/// </summary>
internal sealed class MenuDebugFocusController
{
    private const int MaximumNestedTransitions = 32;
    private readonly MenuDebugProgram _program;
    private readonly MenuDebugDispatchState _state;
    private readonly MenuDebugDispatchTraceBuilder _trace;
    private readonly Action<MenuDebugEventSet, string, MenuNodeId?>
        _executeHandlers;
    private int _transitionDepth;

    public MenuDebugFocusController(
        MenuDebugProgram program,
        MenuDebugDispatchState state,
        MenuDebugDispatchTraceBuilder trace,
        Action<MenuDebugEventSet, string, MenuNodeId?> executeHandlers)
    {
        _program = program;
        _state = state;
        _trace = trace;
        _executeHandlers = executeHandlers;
    }

    public bool Apply(MenuDebugSelectedHook hook)
    {
        if (hook.FocusTransition == MenuDebugFocusTransition.None)
            return true;
        if (hook.FocusTransition == MenuDebugFocusTransition.Clear &&
            _state.FocusedItemId != hook.ItemId)
        {
            _trace.AddDiagnostic(
                hook.Path,
                MenuDebugDiagnosticKind.Blocker,
                MenuEvaluationStatus.Error,
                "leave-focus-target-mismatch",
                _state.FocusedItemId is null
                    ? $"Item '{hook.ItemId}' cannot leave focus because no item is focused."
                    : $"Item '{hook.ItemId}' cannot leave focus while Item '{_state.FocusedItemId}' is focused.");
            return false;
        }

        MenuNodeId? next = hook.FocusTransition == MenuDebugFocusTransition.Set
            ? hook.ItemId
            : null;
        return Transition(next, hook.Path, validateTarget: next is not null);
    }

    public bool FocusFirst(string path)
    {
        MenuEvaluatedState evaluated = _program.Evaluate(_state.ToScenario());
        IReadOnlyDictionary<MenuNodeId, MenuEvaluatedItemState> states =
            evaluated.Items.ToDictionary(item => item.Id);
        foreach (MenuDebugItemProgram item in _program.Items)
        {
            if (!item.Definition.IsResolved || !item.Definition.CanAcceptFocus)
                continue;
            if (!TryResolveEligibility(
                    item,
                    states[item.Id],
                    path,
                    out bool eligible))
            {
                return true;
            }
            if (eligible)
                return Transition(item.Id, path, validateTarget: false);
        }

        _trace.AddDiagnostic(
            path,
            MenuDebugDiagnosticKind.Blocker,
            MenuEvaluationStatus.Error,
            "focus-first-no-selectable-item",
            "focusFirst found no visible, enabled, non-decoration Item.");
        return true;
    }

    public bool FocusNamed(string name, string path)
    {
        MenuDebugItemProgram? item = _program.Items.FirstOrDefault(value =>
            string.Equals(
                value.Name,
                name,
                StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            _trace.AddDiagnostic(
                path,
                MenuDebugDiagnosticKind.Blocker,
                MenuEvaluationStatus.Error,
                "focus-item-not-found",
                $"setFocus could not find Item '{name}'.");
            return true;
        }

        return Transition(item.Id, path, validateTarget: true);
    }

    public bool FocusByDvar(string name, string path)
    {
        MenuDebugScenario scenario = _state.ToScenario();
        string? dvarValue = null;
        IReadOnlyDictionary<MenuNodeId, MenuEvaluatedItemState>? states = null;
        foreach (MenuDebugItemProgram item in _program.Items)
        {
            DebugItemDefinition definition = item.Definition;
            // PS3 Script_SetFocusByDvar filters the Focus bit before it
            // compares dvarTest or invokes Item_EnableShowViaDvar.
            if ((definition.DvarFlags & ItemDvarFlags.Focus) == 0 ||
                !string.Equals(
                    definition.DvarTest,
                    name,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(definition.DvarTest) &&
                !string.IsNullOrEmpty(definition.EnableDvar) &&
                dvarValue is null)
            {
                if (!scenario.Dvars.TryGetValue(name, out MenuDebugValue value))
                {
                    var dependency = new MenuDebugDependency(
                        MenuDebugDependencyKind.Dvar,
                        name,
                        MenuDebugValueKind.String);
                    _trace.AddDiagnostic(
                        path,
                        MenuDebugDiagnosticKind.Blocker,
                        MenuEvaluationStatus.Unknown,
                        "focus-dvar-value-unavailable",
                        $"setFocusByDvar requires scenario dvar '{name}', but no value was supplied.",
                        dependency);
                    return true;
                }
                dvarValue = DvarVariantString(value);
            }

            string currentValue = dvarValue ?? string.Empty;
            if (!TryMatchDvarValue(
                    definition,
                    scenario,
                    currentValue,
                    path,
                    out bool bypassPredicate,
                    out bool matches))
            {
                return true;
            }
            bool enabledViaDvar =
                bypassPredicate ||
                (definition.DvarFlags &
                 (ItemDvarFlags.Enable | ItemDvarFlags.Disable)) == 0 ||
                SelectDvarFlag(definition, ItemDvarFlags.Enable, matches);
            bool visibleViaDvar =
                bypassPredicate ||
                (definition.DvarFlags &
                 (ItemDvarFlags.Show | ItemDvarFlags.Hide)) == 0 ||
                SelectDvarFlag(definition, ItemDvarFlags.Show, matches);
            bool focusViaDvar = bypassPredicate ||
                SelectDvarFlag(definition, ItemDvarFlags.Focus, matches);
            if (!focusViaDvar ||
                !enabledViaDvar ||
                !visibleViaDvar ||
                !definition.IsResolved ||
                !definition.CanAcceptFocus)
            {
                continue;
            }

            states ??= _program.Evaluate(scenario).Items
                .ToDictionary(value => value.Id);
            if (!TryResolveEligibility(
                    item,
                    states[item.Id],
                    path,
                    out bool eligible))
            {
                return true;
            }
            if (eligible)
                return Transition(item.Id, path, validateTarget: false);
        }

        return true;
    }

    private bool Transition(
        MenuNodeId? next,
        string path,
        bool validateTarget)
    {
        MenuNodeId? previous = _state.FocusedItemId;
        if (previous == next)
            return true;
        if (_transitionDepth >= MaximumNestedTransitions)
        {
            _trace.AddDiagnostic(
                path,
                MenuDebugDiagnosticKind.Blocker,
                MenuEvaluationStatus.Error,
                "focus-transition-depth-exceeded",
                $"Nested focus transitions exceed the limit of {MaximumNestedTransitions:N0}.");
            return false;
        }

        MenuDebugItemProgram? nextItem = next is { } nextId
            ? FindItem(nextId)
            : null;
        if (next is not null && nextItem is null)
        {
            _trace.AddDiagnostic(
                path,
                MenuDebugDiagnosticKind.Blocker,
                MenuEvaluationStatus.Error,
                "focus-item-not-found",
                $"Item '{next}' is not part of this Menu.");
            return false;
        }
        if (validateTarget && nextItem is not null &&
            !ValidateFocusable(nextItem, path))
        {
            return false;
        }

        _transitionDepth++;
        try
        {
            if (previous is { } previousId)
            {
                _state.SetFocus(null);
                _trace.AddFocus($"{path}.leaveFocus", previousId, null);
                if (FindItem(previousId) is { } previousItem)
                {
                    _executeHandlers(
                        previousItem.Hooks.LeaveFocus,
                        $"{path}.leaveFocus",
                        previousId);
                }
            }

            if (nextItem is not null)
            {
                MenuNodeId? afterLeave = _state.FocusedItemId;
                if (afterLeave == nextItem.Id)
                    return true;
                if (afterLeave is not null)
                {
                    return Transition(
                        nextItem.Id,
                        $"{path}.afterLeaveFocus",
                        validateTarget: false);
                }
                _state.SetFocus(nextItem.Id);
                _trace.AddFocus($"{path}.onFocus", afterLeave, nextItem.Id);
                _executeHandlers(
                    nextItem.Hooks.OnFocus,
                    $"{path}.onFocus",
                    nextItem.Id);
            }
            return true;
        }
        finally
        {
            _transitionDepth--;
        }
    }

    private bool ValidateFocusable(MenuDebugItemProgram item, string path)
    {
        if (!item.Definition.IsResolved || !item.Definition.CanAcceptFocus)
        {
            _trace.AddDiagnostic(
                path,
                MenuDebugDiagnosticKind.Blocker,
                MenuEvaluationStatus.Error,
                "item-does-not-accept-focus",
                $"Item '{DisplayName(item)}' is unresolved or decorative.");
            return false;
        }

        MenuEvaluatedItemState state = _program.Evaluate(_state.ToScenario())
            .Items.First(value => value.Id == item.Id);
        if (!TryResolveEligibility(item, state, path, out bool eligible))
            return false;
        if (eligible)
            return true;

        _trace.AddDiagnostic(
            path,
            MenuDebugDiagnosticKind.Blocker,
            MenuEvaluationStatus.Error,
            "item-does-not-accept-focus",
            $"Item '{DisplayName(item)}' is hidden or disabled in the current scenario.");
        return false;
    }

    private bool TryResolveEligibility(
        MenuDebugItemProgram item,
        MenuEvaluatedItemState state,
        string path,
        out bool eligible)
    {
        eligible = false;
        if (!state.IsVisible.IsKnown || !state.IsDisabled.IsKnown)
        {
            MenuEvaluationStatus status = state.IsVisible.Status !=
                MenuEvaluationStatus.Known
                    ? state.IsVisible.Status
                    : state.IsDisabled.Status;
            _trace.AddDiagnostic(
                path,
                MenuDebugDiagnosticKind.Blocker,
                status,
                "focus-eligibility-unavailable",
                $"Focus eligibility for Item '{DisplayName(item)}' depends on scenario state that is unavailable.");
            return false;
        }

        eligible = state.IsVisible.Value && !state.IsDisabled.Value;
        return true;
    }

    private MenuDebugItemProgram? FindItem(MenuNodeId id) =>
        _program.Items.FirstOrDefault(value => value.Id == id);

    private bool TryMatchDvarValue(
        DebugItemDefinition definition,
        MenuDebugScenario scenario,
        string dvarValue,
        string path,
        out bool bypassPredicate,
        out bool matches)
    {
        bypassPredicate = false;
        matches = false;
        if (string.IsNullOrEmpty(definition.EnableDvar) ||
            string.IsNullOrEmpty(definition.DvarTest))
        {
            bypassPredicate = true;
            return true;
        }

        MenuDebugScriptParseResult parsed = MenuDebugScriptParser.Parse(
            FirstLine(definition.EnableDvar));
        if (!parsed.IsValid)
            return true;

        var unresolvedLocalizations = new List<(
            MenuDebugDependency Dependency,
            string Message)>();
        foreach (string parsedToken in parsed.Commands
                     .SelectMany(command => command.Tokens))
        {
            if (parsedToken == ";")
                continue;

            string token = parsedToken;
            if (token.StartsWith('@'))
            {
                string key = token[1..];
                var dependency = new MenuDebugDependency(
                    MenuDebugDependencyKind.Localization,
                    key,
                    MenuDebugValueKind.String);
                string? localized = scenario.LocalizationResolver?.Invoke(key);
                if (localized is null)
                {
                    unresolvedLocalizations.Add((
                        dependency,
                        scenario.LocalizationResolver is null
                            ? $"setFocusByDvar cannot localize '@{key}' because no localization resolver is configured."
                            : $"setFocusByDvar could not resolve localization reference '@{key}'."));
                    continue;
                }

                token = localized;
            }

            if (string.Equals(
                    token,
                    dvarValue,
                    StringComparison.OrdinalIgnoreCase))
            {
                matches = true;
                break;
            }
        }

        if (matches)
            return true;

        foreach ((MenuDebugDependency dependency, string message) in
                 unresolvedLocalizations)
        {
            _trace.AddDiagnostic(
                path,
                MenuDebugDiagnosticKind.Blocker,
                MenuEvaluationStatus.Unknown,
                "focus-dvar-localization-unavailable",
                message,
                dependency);
        }

        return unresolvedLocalizations.Count == 0;
    }

    private static bool SelectDvarFlag(
        DebugItemDefinition definition,
        ItemDvarFlags requestedFlag,
        bool matches)
    {
        bool positive = (definition.DvarFlags & requestedFlag) != 0;
        return matches ? positive : !positive;
    }

    private static string FirstLine(string value)
    {
        int carriageReturn = value.IndexOf('\r');
        int lineFeed = value.IndexOf('\n');
        int lineEnd = carriageReturn < 0
            ? lineFeed
            : lineFeed < 0
                ? carriageReturn
                : Math.Min(carriageReturn, lineFeed);
        return lineEnd < 0 ? value : value[..lineEnd];
    }

    private static string DvarVariantString(MenuDebugValue value)
    {
        if (value.Kind == MenuDebugValueKind.Float &&
            value.TryGetFloat(out float floatingPoint))
        {
            return floatingPoint.ToString("G6", CultureInfo.InvariantCulture);
        }

        return value.AsString();
    }

    private static string DisplayName(MenuDebugItemProgram item) =>
        string.IsNullOrWhiteSpace(item.Name) ? item.Id.ToString() : item.Name;
}
