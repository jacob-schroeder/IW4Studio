using IW4.Assets.Assets.Menu;

namespace IW4.Studio.Documents.MenuEditing.Behavior;

/// <summary>
/// The single validation boundary for authored MenuDef and ItemDef behavior.
/// Imported malformed or opaque graph shapes remain representable and are
/// reported as warnings; authoring uses the same rules as errors before a
/// graph is lowered.
/// </summary>
public sealed class MenuItemBehaviorValidator
{
    private readonly IMenuBehaviorExpressionCodec _expressions;

    public MenuItemBehaviorValidator(IMenuBehaviorExpressionCodec? expressions = null)
    {
        _expressions = expressions ?? ImportedMenuBehaviorExpressionCodec.Instance;
    }

    public IReadOnlyList<MenuBehaviorValidationIssue> Validate(
        MenuItemBehaviorBindings value,
        MenuBehaviorValidationMode mode = MenuBehaviorValidationMode.Imported)
    {
        ArgumentNullException.ThrowIfNull(value);

        List<MenuBehaviorValidationIssue> issues = [];
        var activeSets = new HashSet<MenuBehaviorEventHandlerSet>(ReferenceEqualityComparer.Instance);

        ValidateEventBinding(value.MouseEnterText, "item.mouseEnterText", mode, issues, activeSets);
        ValidateEventBinding(value.MouseExitText, "item.mouseExitText", mode, issues, activeSets);
        ValidateEventBinding(value.MouseEnter, "item.mouseEnter", mode, issues, activeSets);
        ValidateEventBinding(value.MouseExit, "item.mouseExit", mode, issues, activeSets);
        ValidateEventBinding(value.Action, "item.action", mode, issues, activeSets);
        ValidateEventBinding(value.Accept, "item.accept", mode, issues, activeSets);
        ValidateEventBinding(value.OnFocus, "item.onFocus", mode, issues, activeSets);
        ValidateEventBinding(value.LeaveFocus, "item.leaveFocus", mode, issues, activeSets);
        ValidateEventBinding(value.ListBoxDoubleClick, "item.listBox.doubleClick", mode, issues, activeSets);
        ValidateKeyHandlers(
            value.KeyHandlers,
            "item.keyHandlers",
            mode,
            issues,
            activeSets);
        ValidateExpressions(value.Expressions, mode, issues);

        return issues.AsReadOnly();
    }

    public IReadOnlyList<MenuBehaviorValidationIssue> Validate(
        MenuDefinitionBehaviorBindings value,
        MenuBehaviorValidationMode mode = MenuBehaviorValidationMode.Imported)
    {
        ArgumentNullException.ThrowIfNull(value);

        List<MenuBehaviorValidationIssue> issues = [];
        var activeSets = new HashSet<MenuBehaviorEventHandlerSet>(
            ReferenceEqualityComparer.Instance);

        ValidateEventBinding(value.OnOpen, "menu.onOpen", mode, issues, activeSets);
        ValidateEventBinding(
            value.OnCloseRequest,
            "menu.onCloseRequest",
            mode,
            issues,
            activeSets);
        ValidateEventBinding(value.OnClose, "menu.onClose", mode, issues, activeSets);
        ValidateEventBinding(value.OnEscape, "menu.onEscape", mode, issues, activeSets);
        ValidateKeyHandlers(
            value.KeyHandlers,
            "menu.keyHandlers",
            mode,
            issues,
            activeSets);

        return issues.AsReadOnly();
    }

    private void ValidateEventBinding(
        MenuBehaviorEventBinding binding,
        string path,
        MenuBehaviorValidationMode mode,
        List<MenuBehaviorValidationIssue> issues,
        HashSet<MenuBehaviorEventHandlerSet> activeSets)
    {
        if (binding is null)
        {
            Add(issues, path, "The event binding is missing.", mode);
            return;
        }

        ValidateEventSet(binding.Handlers, path, mode, issues, activeSets);
    }

    private void ValidateEventSet(
        MenuBehaviorEventHandlerSet? set,
        string path,
        MenuBehaviorValidationMode mode,
        List<MenuBehaviorValidationIssue> issues,
        HashSet<MenuBehaviorEventHandlerSet> activeSets)
    {
        if (set is null)
            return;

        if (!activeSets.Add(set))
        {
            Add(issues, path, "The event-set graph contains a cycle.", mode);
            return;
        }

        try
        {
            MenuBehaviorEventHandler? previous = null;
            for (int index = 0; index < set.Handlers.Length; index++)
            {
                MenuBehaviorEventHandlerEntry entry = set.Handlers[index];
                string handlerPath = $"{path}.handlers[{index}]";
                if (entry.Handler is null)
                {
                    Add(issues, handlerPath, "The handler pointer does not resolve to a handler.", mode);
                    previous = null;
                    continue;
                }

                ValidateEventHandler(
                    entry.Handler,
                    previous,
                    handlerPath,
                    mode,
                    issues,
                    activeSets,
                    entry.ImportedShape?.Matches(entry, index) == true);
                previous = entry.Handler;
            }
        }
        finally
        {
            activeSets.Remove(set);
        }
    }

    private void ValidateEventHandler(
        MenuBehaviorEventHandler handler,
        MenuBehaviorEventHandler? previous,
        string path,
        MenuBehaviorValidationMode mode,
        List<MenuBehaviorValidationIssue> issues,
        HashSet<MenuBehaviorEventHandlerSet> activeSets,
        bool retainsImportedShape)
    {
        switch (handler)
        {
            case MenuBehaviorScriptEventHandler script:
                if (script.Script is null)
                    Add(issues, path, "A script handler is missing its script text.", mode);
                break;

            case MenuBehaviorConditionalEventHandler conditional:
                if (conditional.Condition is null)
                    Add(issues, $"{path}.condition", "A conditional handler requires an expression.", mode);
                else
                    ValidateRequiredExpression(
                        conditional.Condition,
                        new(MenuBehaviorExpressionSiteKind.Conditional),
                        $"{path}.condition",
                        mode,
                        issues);

                if (conditional.Then is null)
                    Add(issues, $"{path}.then", "A conditional handler requires a handler set.", mode);
                else
                    ValidateEventSet(conditional.Then, $"{path}.then", mode, issues, activeSets);
                break;

            case MenuBehaviorElseEventHandler @else:
                if (previous is not MenuBehaviorConditionalEventHandler)
                {
                    Add(
                        issues,
                        path,
                        "An else handler must immediately follow a conditional handler in the same set.",
                        mode,
                        preserveImported: retainsImportedShape);
                }

                if (@else.Handlers is null)
                    Add(issues, $"{path}.handlers", "An else handler requires a handler set.", mode);
                else
                    ValidateEventSet(@else.Handlers, $"{path}.handlers", mode, issues, activeSets);
                break;

            case MenuBehaviorSetLocalVariableEventHandler local:
                if (!Enum.IsDefined(local.ValueType))
                    Add(issues, $"{path}.valueType", "The set-local value type is unknown.", mode);
                if (string.IsNullOrWhiteSpace(local.Name))
                    Add(issues, $"{path}.name", "A set-local handler requires a variable name.", mode);
                if (local.Expression is null)
                    Add(issues, $"{path}.expression", "A set-local handler requires an expression.", mode);
                else
                    ValidateRequiredExpression(
                        local.Expression,
                        MenuBehaviorExpressionSite.Local(local.ValueType),
                        $"{path}.expression",
                        mode,
                        issues);
                break;

            case MenuBehaviorOpaqueEventHandler opaque:
                Add(
                    issues,
                    path,
                    $"Event type 0x{opaque.EventType:X2} has no safe authored representation.",
                    mode,
                    preserveImported: retainsImportedShape);
                break;

            default:
                Add(
                    issues,
                    path,
                    $"Handler type '{handler.GetType().Name}' is not supported.",
                    mode);
                break;
        }
    }

    private void ValidateKeyHandlers(
        MenuBehaviorKeyHandlerBindings bindings,
        string path,
        MenuBehaviorValidationMode mode,
        List<MenuBehaviorValidationIssue> issues,
        HashSet<MenuBehaviorEventHandlerSet> activeSets)
    {
        if (bindings is null)
        {
            Add(issues, path, "The key-handler binding list is missing.", mode);
            return;
        }

        if (bindings.HasTruncatedImportedTail)
        {
            Add(
                issues,
                path,
                "The imported key-handler chain has a cyclic or unresolved tail.",
                mode);
        }

        var seenKeys = new HashSet<int>();
        for (int index = 0; index < bindings.Handlers.Length; index++)
        {
            MenuBehaviorKeyHandlerBinding binding = bindings.Handlers[index];
            if (!seenKeys.Add(binding.Key))
            {
                Add(
                    issues,
                    $"{path}[{index}].key",
                    $"Key code {binding.Key} appears more than once.",
                    mode);
            }

            if (binding.Action is null)
            {
                Add(
                    issues,
                    $"{path}[{index}].action",
                    "A key handler requires an action handler set.",
                    mode);
                continue;
            }

            ValidateEventSet(
                binding.Action,
                $"{path}[{index}].action",
                mode,
                issues,
                activeSets);
        }
    }

    private void ValidateExpressions(
        MenuItemBehaviorExpressionBindings expressions,
        MenuBehaviorValidationMode mode,
        List<MenuBehaviorValidationIssue> issues)
    {
        if (expressions is null)
        {
            Add(issues, "item.expressions", "The expression bindings are missing.", mode);
            return;
        }

        ValidateOptionalExpression(
            expressions.Visible,
            new(MenuBehaviorExpressionSiteKind.ItemVisible),
            "item.expressions.visible",
            mode,
            issues);
        ValidateOptionalExpression(
            expressions.Disabled,
            new(MenuBehaviorExpressionSiteKind.ItemDisabled),
            "item.expressions.disabled",
            mode,
            issues);
        ValidateOptionalExpression(
            expressions.Text,
            new(MenuBehaviorExpressionSiteKind.ItemText),
            "item.expressions.text",
            mode,
            issues);
        ValidateOptionalExpression(
            expressions.Material,
            new(MenuBehaviorExpressionSiteKind.ItemMaterial),
            "item.expressions.material",
            mode,
            issues);

        var seenTargets = new HashSet<ItemFloatExpressionTarget>();
        for (int index = 0; index < expressions.FloatExpressions.Entries.Length; index++)
        {
            MenuBehaviorFloatExpressionBinding binding = expressions.FloatExpressions.Entries[index];
            string path = $"item.expressions.float[{index}]";
            bool retainsImportedShape = expressions.FloatExpressions
                .RetainsImportedInvalidShape(index);
            if (!MenuBehaviorFloatExpressionBindings.AllTargets.Contains(binding.Target))
            {
                Add(
                    issues,
                    $"{path}.target",
                    "The float-expression target is not supported.",
                    mode,
                    preserveImported: retainsImportedShape);
            }
            else if (!seenTargets.Add(binding.Target))
            {
                Add(
                    issues,
                    $"{path}.target",
                    "The float-expression target appears more than once.",
                    mode,
                    preserveImported: retainsImportedShape);
            }

            ValidateRequiredExpression(
                binding.Expression,
                MenuBehaviorExpressionSite.Float(binding.Target),
                $"{path}.expression",
                mode,
                issues);
        }
    }

    private void ValidateOptionalExpression(
        MenuBehaviorExpressionBinding binding,
        MenuBehaviorExpressionSite site,
        string path,
        MenuBehaviorValidationMode mode,
        List<MenuBehaviorValidationIssue> issues)
    {
        if (binding is null)
        {
            Add(issues, path, "The expression binding is missing.", mode);
            return;
        }

        AddImportDiagnostics(binding, path, issues);
        if (binding.Value is not null)
            AddExpressionIssues(
                binding,
                site,
                path,
                mode,
                issues);
    }

    private void ValidateRequiredExpression(
        MenuBehaviorExpressionBinding binding,
        MenuBehaviorExpressionSite site,
        string path,
        MenuBehaviorValidationMode mode,
        List<MenuBehaviorValidationIssue> issues)
    {
        if (binding is null || binding.Value is null)
        {
            Add(issues, path, "An expression is required.", mode);
            return;
        }

        AddImportDiagnostics(binding, path, issues);
        AddExpressionIssues(
            binding,
            site,
            path,
            mode,
            issues);
    }

    private void AddExpressionIssues(
        MenuBehaviorExpressionBinding binding,
        MenuBehaviorExpressionSite site,
        string path,
        MenuBehaviorValidationMode mode,
        List<MenuBehaviorValidationIssue> issues) =>
        issues.AddRange(_expressions.Validate(
            binding,
            site,
            path,
            mode));

    private static void AddImportDiagnostics(
        MenuBehaviorExpressionBinding binding,
        string path,
        List<MenuBehaviorValidationIssue> issues)
    {
        foreach (Expressions.BehaviorExpressionDiagnostic diagnostic in
                 binding.ImportDiagnostics)
        {
            issues.Add(new MenuBehaviorValidationIssue(
                path,
                diagnostic.Message,
                MenuBehaviorValidationSeverity.Warning));
        }
    }

    private static void Add(
        List<MenuBehaviorValidationIssue> issues,
        string path,
        string message,
        MenuBehaviorValidationMode mode,
        bool preserveImported = false) =>
        issues.Add(new MenuBehaviorValidationIssue(
            path,
            message,
            mode == MenuBehaviorValidationMode.Authored && !preserveImported
                ? MenuBehaviorValidationSeverity.Error
                : MenuBehaviorValidationSeverity.Warning));
}
