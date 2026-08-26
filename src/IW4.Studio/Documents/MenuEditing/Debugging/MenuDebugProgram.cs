using IW4.Assets.Assets.Menu;

namespace IW4.Studio.Documents.MenuEditing.Debugging;

public sealed class MenuDebugItemProgram
{
    internal MenuDebugItemProgram(
        MenuNodeId id,
        MenuNodeId windowId,
        string? name,
        string? group,
        MenuDebugItemHooks hooks,
        DebugItemDefinition definition)
    {
        Id = id;
        WindowId = windowId;
        Name = name;
        Group = group;
        Hooks = hooks;
        Definition = definition;
    }

    public MenuNodeId Id { get; }
    public MenuNodeId WindowId { get; }
    public string? Name { get; }
    public string? Group { get; }
    public MenuDebugItemHooks Hooks { get; }
    internal DebugItemDefinition Definition { get; }
}

/// <summary>
/// Immutable executable projection of one detached compiled Menu revision.
/// It contains editor identities but no references to the mutable asset graph.
/// </summary>
public sealed class MenuDebugProgram
{
    private readonly IReadOnlyList<MenuDebugItemProgram> _items;
    private readonly IReadOnlyList<MenuDebugDependency> _dependencies;

    internal MenuDebugProgram(
        MenuNodeId id,
        string? name,
        MenuDebugMenuHooks hooks,
        DebugMenuDefinition definition,
        IEnumerable<MenuDebugItemProgram> items,
        IEnumerable<MenuDebugDependency> dependencies)
    {
        Id = id;
        Name = name;
        Hooks = hooks;
        Definition = definition;
        _items = Array.AsReadOnly(items.ToArray());
        _dependencies = Array.AsReadOnly(dependencies.Distinct().ToArray());
    }

    public MenuNodeId Id { get; }
    public string? Name { get; }
    public MenuDebugMenuHooks Hooks { get; }
    public IReadOnlyList<MenuDebugItemProgram> Items => _items;
    public IReadOnlyList<MenuDebugDependency> Dependencies => _dependencies;
    internal DebugMenuDefinition Definition { get; }
    internal Guid RevisionToken { get; } = Guid.NewGuid();

    public MenuEvaluatedState Evaluate(MenuDebugScenario scenario) =>
        MenuExpressionEvaluator.Default.Evaluate(this, scenario);

    public MenuEvaluation<MenuDebugValue> EvaluateExpression(
        MenuDebugExpression expression,
        MenuDebugScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return MenuExpressionEvaluator.Default.EvaluateExpression(this, expression, scenario);
    }

    public MenuDebugDispatchResult Dispatch(
        MenuDebugInput input,
        MenuDebugScenario scenario) =>
        MenuDebugEventDispatcher.Default.Dispatch(this, input, scenario);

    /// <summary>
    /// Applies the authored Menu open lifecycle to an immutable scenario.
    /// Only debugger-safe event and script behavior is executed; unsupported
    /// commands remain queued in the returned trace.
    /// </summary>
    public MenuDebugDispatchResult Activate(MenuDebugScenario scenario) =>
        MenuDebugEventDispatcher.Default.Activate(this, scenario);
}

internal sealed record DebugMenuDefinition(
    MenuNodeId WindowId,
    DebugRectangleDefinition Rectangle,
    bool AuthoredVisible,
    MenuDebugExpression? Visible,
    MenuDebugExpression? RectX,
    MenuDebugExpression? RectY,
    MenuDebugExpression? RectWidth,
    MenuDebugExpression? RectHeight);

internal sealed record DebugItemDefinition(
    bool IsResolved,
    bool CanAcceptFocus,
    string? DvarTest,
    string? EnableDvar,
    ItemDvarFlags DvarFlags,
    DebugRectangleDefinition Rectangle,
    bool AuthoredVisible,
    DebugColorDefinition ForeColor,
    DebugColorDefinition GlowColor,
    DebugColorDefinition BackColor,
    DebugColorDefinition BorderColor,
    string? AuthoredText,
    string? AuthoredMaterial,
    MenuDebugExpression? Visible,
    MenuDebugExpression? Disabled,
    MenuDebugExpression? Text,
    MenuDebugExpression? Material,
    IReadOnlyList<DebugFloatExpression> FloatExpressions);

internal sealed record DebugRectangleDefinition(
    float X,
    float Y,
    float Width,
    float Height,
    HorizontalAlign HorizontalAlignment,
    VerticalAlign VerticalAlignment);

internal sealed record DebugColorDefinition(float A, float R, float G, float B);

internal sealed record DebugFloatExpression(
    ItemFloatExpressionTarget Target,
    MenuDebugExpression Expression);

internal abstract record DebugExpressionNode;
internal sealed record DebugLiteralExpressionNode(MenuDebugValue Value) : DebugExpressionNode;
internal sealed record DebugUnaryExpressionNode(
    OperationEnum Operation,
    DebugExpressionNode Operand) : DebugExpressionNode;
internal sealed record DebugBinaryExpressionNode(
    OperationEnum Operation,
    DebugExpressionNode Left,
    DebugExpressionNode Right) : DebugExpressionNode;
internal sealed record DebugCallExpressionNode(
    OperationEnum Operation,
    IReadOnlyList<DebugExpressionNode> Arguments,
    string? StaticDvarName = null) : DebugExpressionNode;
internal sealed record DebugInvalidExpressionNode(string Message) : DebugExpressionNode;

internal static class MenuDebugProgramFactory
{
    public static MenuDebugProgram Create(
        MenuDefAsset definition,
        MenuDocumentIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(identity);
        if (definition.Items.Count != identity.Items.Count)
        {
            throw new InvalidDataException(
                "Menu debug-program item identity count does not match the detached item table.");
        }

        var compiler = new DebugExpressionCompiler(definition.ExpressionDataValue);
        DebugMenuDefinition menu = new(
            identity.WindowId,
            Rectangle(definition.Window.Rect),
            IsAuthoredVisible(definition.Window),
            compiler.Compile(definition.VisibleStatement),
            compiler.Compile(definition.RectXStatement),
            compiler.Compile(definition.RectYStatement),
            compiler.Compile(definition.RectWStatement),
            compiler.Compile(definition.RectHStatement));

        MenuDebugMenuHooks menuHooks = new(
            CompileEventSet(definition.OnOpenSet, compiler),
            CompileEventSet(definition.OnCloseRequestSet, compiler),
            CompileEventSet(definition.OnCloseSet, compiler),
            CompileEventSet(definition.OnEscSet, compiler),
            CompileKeyHandlers(definition.ExecKeyHandler, compiler));

        var items = new MenuDebugItemProgram[definition.Items.Count];
        for (int index = 0; index < items.Length; index++)
        {
            ItemDefAsset? item = definition.Items[index].Item;
            MenuItemIdentity itemIdentity = identity.Items[index];
            items[index] = item is null
                ? MissingItem(itemIdentity)
                : CompileItem(itemIdentity, item, compiler);
        }

        IEnumerable<MenuDebugDependency> dependencies = Expressions(menu, items, menuHooks)
            .SelectMany(expression => expression.Dependencies)
            .Concat(items
                .Where(item =>
                    (item.Definition.DvarFlags & ItemDvarFlags.Focus) != 0 &&
                    !string.IsNullOrWhiteSpace(item.Definition.DvarTest) &&
                    !string.IsNullOrEmpty(item.Definition.EnableDvar))
                .Select(item => new MenuDebugDependency(
                    MenuDebugDependencyKind.Dvar,
                    item.Definition.DvarTest!,
                    MenuDebugValueKind.String)))
            .Concat(items
                .Select(item => item.Definition.AuthoredText)
                .Where(text => text?.StartsWith('@') == true)
                .Select(text => new MenuDebugDependency(
                    MenuDebugDependencyKind.Localization,
                    text![1..],
                    MenuDebugValueKind.String,
                    OperationEnum.OP_LOCALIZESTRING)))
            .Distinct();
        return new MenuDebugProgram(
            identity.Id,
            definition.Window.Name,
            menuHooks,
            menu,
            items,
            dependencies);
    }

    private static MenuDebugItemProgram CompileItem(
        MenuItemIdentity identity,
        ItemDefAsset item,
        DebugExpressionCompiler compiler)
    {
        MenuDebugExpression? visible = compiler.Compile(item.VisibleStatement);
        MenuDebugExpression? disabled = compiler.Compile(item.DisabledStatement);
        MenuDebugExpression? text = compiler.Compile(item.TextStatement);
        MenuDebugExpression? material = compiler.Compile(item.MaterialStatement);
        DebugFloatExpression[] floats = item.LoadedFloatExpressions
            .Select(value => (value.Target, Expression: compiler.Compile(value.Statement)))
            .Where(value => value.Expression is not null)
            .Select(value => new DebugFloatExpression(value.Target, value.Expression!))
            .ToArray();

        var definition = new DebugItemDefinition(
            true,
            (item.Window.StaticFlags &
             WindowStaticFlags.WINDOW_STATIC_DECORATION) == 0,
            item.DvarTestString,
            item.EnableDvarString,
            item.DvarFlags,
            // Item_SetScreenCoords starts with the client rectangle and
            // composes the Menu origin exactly once at projection time.
            Rectangle(item.Window.RectClient),
            IsAuthoredVisible(item.Window),
            Color(item.Window.ForeColor),
            Color(item.GlowColor),
            Color(item.Window.BackColor),
            Color(item.Window.BorderColor),
            item.TextString,
            LogicalReferenceName(item.Window.BackgroundMaterialName),
            visible,
            disabled,
            text,
            material,
            Array.AsReadOnly(floats));
        var hooks = new MenuDebugItemHooks(
            CompileEventSet(item.MouseEnterTextSet, compiler),
            CompileEventSet(item.MouseExitTextSet, compiler),
            CompileEventSet(item.MouseEnterSet, compiler),
            CompileEventSet(item.MouseExitSet, compiler),
            CompileEventSet(item.ActionSet, compiler),
            CompileEventSet(item.AcceptSet, compiler),
            CompileEventSet(item.OnFocusSet, compiler),
            CompileEventSet(item.LeaveFocusSet, compiler),
            CompileEventSet(item.ListBox?.DoubleClickSet, compiler),
            CompileKeyHandlers(item.OnKeyHandler, compiler));
        return new MenuDebugItemProgram(
            identity.Id,
            identity.WindowId,
            item.Window.Name,
            item.Window.Group,
            hooks,
            definition);
    }

    private static MenuDebugItemProgram MissingItem(MenuItemIdentity identity) =>
        new(
            identity.Id,
            identity.WindowId,
            null,
            null,
            new MenuDebugItemHooks(
                MenuDebugEventSet.Empty,
                MenuDebugEventSet.Empty,
                MenuDebugEventSet.Empty,
                MenuDebugEventSet.Empty,
                MenuDebugEventSet.Empty,
                MenuDebugEventSet.Empty,
                MenuDebugEventSet.Empty,
                MenuDebugEventSet.Empty,
                MenuDebugEventSet.Empty,
                []),
            new DebugItemDefinition(
                false,
                false,
                null,
                null,
                ItemDvarFlags.None,
                new DebugRectangleDefinition(
                    0,
                    0,
                    0,
                    0,
                    HorizontalAlign.HORIZONTAL_ALIGN_SUBLEFT,
                    VerticalAlign.VERTICAL_ALIGN_SUBTOP),
                false,
                new DebugColorDefinition(1, 1, 1, 1),
                new DebugColorDefinition(0, 0, 0, 0),
                new DebugColorDefinition(0, 0, 0, 0),
                new DebugColorDefinition(0, 0, 0, 0),
                null,
                null,
                null,
                null,
                null,
                null,
                []));

    private static MenuDebugEventSet CompileEventSet(
        MenuEventHandlerSet? source,
        DebugExpressionCompiler compiler,
        int depth = 0)
    {
        if (source is null)
            return MenuDebugEventSet.Empty;
        if (depth >= 64)
        {
            throw new InvalidDataException(
                "Menu event-handler nesting exceeds the supported graph depth.");
        }

        var handlers = new List<MenuDebugEventHandler>(source.Handlers.Count);
        foreach (MenuEventHandlerReference reference in source.Handlers)
        {
            if (reference.Handler is not { } handler)
                continue;

            MenuDebugEventHandler compiled = handler.EventType switch
            {
                MenuEventHandlerType.UnconditionalScript =>
                    new MenuDebugScriptEventHandler(handler.UnconditionalScript ?? string.Empty),
                MenuEventHandlerType.ConditionalScript =>
                    new MenuDebugConditionalEventHandler(
                        compiler.Compile(handler.ConditionalScript?.EventStatement),
                        CompileEventSet(
                            handler.ConditionalScript?.EventHandlers,
                            compiler,
                            depth + 1)),
                MenuEventHandlerType.ElseScript =>
                    new MenuDebugElseEventHandler(
                        CompileEventSet(handler.ElseScriptSet, compiler, depth + 1)),
                MenuEventHandlerType.SetLocalVarBool or
                MenuEventHandlerType.SetLocalVarInt or
                MenuEventHandlerType.SetLocalVarFloat or
                MenuEventHandlerType.SetLocalVarString =>
                    new MenuDebugSetLocalVariableEventHandler(
                        handler.EventType,
                        handler.SetLocalVarData?.LocalVarNameString,
                        compiler.Compile(handler.SetLocalVarData?.ExpressionStatement)),
                _ => throw new InvalidDataException(
                    $"Unsupported Menu event-handler type '{handler.EventType}'.")
            };
            handlers.Add(compiled);
        }

        return new MenuDebugEventSet(handlers);
    }

    private static IReadOnlyList<MenuDebugKeyHandler> CompileKeyHandlers(
        ItemKeyHandler? source,
        DebugExpressionCompiler compiler)
    {
        var handlers = new List<MenuDebugKeyHandler>();
        var seen = new HashSet<ItemKeyHandler>(ReferenceEqualityComparer.Instance);
        for (ItemKeyHandler? current = source;
             current is not null && seen.Add(current);
             current = current.NextHandler)
        {
            handlers.Add(new MenuDebugKeyHandler(
                current.Key,
                CompileEventSet(current.ActionSet, compiler)));
        }

        return Array.AsReadOnly(handlers.ToArray());
    }

    private static IEnumerable<MenuDebugExpression> Expressions(
        DebugMenuDefinition menu,
        IEnumerable<MenuDebugItemProgram> items,
        MenuDebugMenuHooks hooks)
    {
        foreach (MenuDebugExpression? expression in new[]
                 {
                     menu.Visible,
                     menu.RectX,
                     menu.RectY,
                     menu.RectWidth,
                     menu.RectHeight
                 })
        {
            if (expression is not null)
                yield return expression;
        }

        foreach (MenuDebugExpression expression in EventExpressions(hooks))
            yield return expression;

        foreach (MenuDebugItemProgram item in items)
        {
            DebugItemDefinition value = item.Definition;
            foreach (MenuDebugExpression? expression in new[]
                     {
                         value.Visible,
                         value.Disabled,
                         value.Text,
                         value.Material
                     })
            {
                if (expression is not null)
                    yield return expression;
            }
            foreach (DebugFloatExpression expression in value.FloatExpressions)
                yield return expression.Expression;
            foreach (MenuDebugExpression expression in EventExpressions(item.Hooks))
                yield return expression;
        }
    }

    private static IEnumerable<MenuDebugExpression> EventExpressions(
        MenuDebugMenuHooks hooks) =>
        EventExpressions(
            [hooks.OnOpen, hooks.OnCloseRequest, hooks.OnClose, hooks.OnEscape],
            hooks.KeyHandlers);

    private static IEnumerable<MenuDebugExpression> EventExpressions(
        MenuDebugItemHooks hooks) =>
        EventExpressions(
            [
                hooks.MouseEnterText,
                hooks.MouseExitText,
                hooks.MouseEnter,
                hooks.MouseExit,
                hooks.Action,
                hooks.Accept,
                hooks.OnFocus,
                hooks.LeaveFocus,
                hooks.DoubleClick
            ],
            hooks.KeyHandlers);

    private static IEnumerable<MenuDebugExpression> EventExpressions(
        IEnumerable<MenuDebugEventSet> sets,
        IEnumerable<MenuDebugKeyHandler> keys)
    {
        foreach (MenuDebugEventSet set in sets.Concat(keys.Select(key => key.Actions)))
        {
            foreach (MenuDebugExpression expression in EventExpressions(set))
                yield return expression;
        }
    }

    private static IEnumerable<MenuDebugExpression> EventExpressions(MenuDebugEventSet set)
    {
        foreach (MenuDebugEventHandler handler in set.Handlers)
        {
            switch (handler)
            {
                case MenuDebugConditionalEventHandler conditional:
                    if (conditional.Condition is not null)
                        yield return conditional.Condition;
                    foreach (MenuDebugExpression expression in EventExpressions(conditional.Handlers))
                        yield return expression;
                    break;
                case MenuDebugElseEventHandler @else:
                    foreach (MenuDebugExpression expression in EventExpressions(@else.Handlers))
                        yield return expression;
                    break;
                case MenuDebugSetLocalVariableEventHandler setLocal when
                    setLocal.ValueExpression is not null:
                    yield return setLocal.ValueExpression;
                    break;
            }
        }
    }

    private static bool IsAuthoredVisible(WindowDef window) =>
        window.DynamicFlags.Count == 0 ||
        (window.DynamicFlags[0] & WindowDynamicFlags.WINDOW_DYNAMIC_VISIBLE) != 0;

    private static DebugRectangleDefinition Rectangle(RectangleDef value) =>
        new(value.X, value.Y, value.W, value.H, value.HorzAlign, value.VertAlign);

    // Vec4 properties retain serialized slot names rather than semantic RGBA
    // order: A/R/G/B slots contain R/G/B/A respectively.
    private static DebugColorDefinition Color(IW4.Assets.Math.Vec4 value) =>
        new(value.B, value.A, value.R, value.G);

    private static string? LogicalReferenceName(string? value) =>
        string.IsNullOrEmpty(value) ? value : value.TrimStart(',');
}
