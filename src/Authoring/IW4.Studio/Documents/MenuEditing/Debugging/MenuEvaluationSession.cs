using System.Collections.ObjectModel;
using IW4.Game.Assets.Menu;

namespace IW4.Studio.Documents.MenuEditing.Debugging;

/// <summary>
/// Per-evaluation caches and scene projection. Kept separate from expression
/// semantics so evaluating a token tree never owns authored or session state.
/// </summary>
internal sealed class MenuEvaluationSession
{
    private readonly MenuExpressionEvaluator _evaluator;
    private readonly MenuDebugProgram _program;
    private readonly Dictionary<MenuNodeId, MenuEvaluatedRectangle> _rectangles = [];
    private readonly HashSet<MenuNodeId> _rectangleStack = [];
    private readonly Dictionary<ItemFloatExpressionTarget, MenuEvaluation<float>> _menuOrigins = [];
    private readonly HashSet<ItemFloatExpressionTarget> _menuOriginStack = [];
    private readonly List<MenuEvaluationTraceEntry> _trace = [];
    private readonly EvaluationContext _context;

    public MenuEvaluationSession(
        MenuExpressionEvaluator evaluator,
        MenuDebugProgram program,
        MenuDebugScenario scenario)
    {
        _evaluator = evaluator;
        _program = program;
        _context = new EvaluationContext(program, scenario, ResolveItemWindowCoordinate);
    }

    public MenuEvaluatedState Evaluate()
    {
        DebugMenuDefinition root = _program.Definition;
        var rootRectangle = new MenuEvaluatedRectangle(
            ResolveMenuOrigin(ItemFloatExpressionTarget.RectX),
            ResolveMenuOrigin(ItemFloatExpressionTarget.RectY),
            EvaluateFloat(root.RectWidth, root.Rectangle.Width),
            EvaluateFloat(root.RectHeight, root.Rectangle.Height),
            root.Rectangle.HorizontalAlignment,
            root.Rectangle.VerticalAlignment);
        bool isCurrentMenuOpen =
            !string.IsNullOrWhiteSpace(_program.Name) &&
            _context.Scenario.OpenMenus.Contains(_program.Name);
        MenuEvaluation<bool> rootVisible = EvaluateVisibility(
            root.AuthoredVisible || isCurrentMenuOpen,
            root.Visible);
        var window = new MenuEvaluatedWindowState(
            root.WindowId,
            rootVisible,
            rootRectangle);
        MenuEvaluatedItemState[] items = _program.Items.Select(EvaluateItem).ToArray();
        return new MenuEvaluatedState(
            _program.Id,
            _program.RevisionToken,
            window,
            items,
            _trace);
    }

    public MenuEvaluation<MenuDebugValue> EvaluateExpression(MenuDebugExpression expression)
    {
        MenuEvaluation<MenuDebugValue> result =
            _evaluator.EvaluateNode(expression.Root, _context);
        return WithDependencies(result, expression.Dependencies);
    }

    private MenuEvaluatedItemState EvaluateItem(MenuDebugItemProgram item)
    {
        DebugItemDefinition definition = item.Definition;
        MenuEvaluatedRectangle rectangle = ResolveItemRectangle(item.Id);
        MenuDebugItemRuntimeState? runtime = _context.Scenario.ItemRuntimeStates
            .GetValueOrDefault(item.Id);
        MenuEvaluation<bool> visible = EvaluateItemVisibility(definition, runtime);
        MenuEvaluation<bool> disabled = EvaluateBoolean(definition.Disabled, false);
        if ((definition.DvarFlags & (ItemDvarFlags.Enable | ItemDvarFlags.Disable)) != 0 &&
            !(disabled.IsKnown && disabled.Value))
            disabled = CombineDvarGate(disabled, EvaluateDvarGate(definition, ItemDvarFlags.Enable), blocked: true);
        MenuEvaluation<string?> text = EvaluateString(
            definition.Text,
            definition.AuthoredText,
            itemText: true);
        MenuEvaluation<string?> material = EvaluateString(
            definition.Material,
            definition.AuthoredMaterial,
            itemText: false);

        var floatResults = new Dictionary<ItemFloatExpressionTarget, MenuEvaluation<float>>();
        MenuEvaluatedColor foreColor = runtime?.ForeColor is { } runtimeFore
            ? Color(runtimeFore)
            : Color(definition.ForeColor);
        MenuEvaluatedColor glowColor = Color(definition.GlowColor);
        MenuEvaluatedColor backColor = runtime?.BackColor is { } runtimeBack
            ? Color(runtimeBack)
            : Color(definition.BackColor);
        MenuEvaluatedColor borderColor = runtime?.BorderColor is { } runtimeBorder
            ? Color(runtimeBorder)
            : Color(definition.BorderColor);
        foreach (DebugFloatExpression expression in definition.FloatExpressions)
        {
            MenuEvaluation<float> value = expression.Target switch
            {
                ItemFloatExpressionTarget.RectX => rectangle.X,
                ItemFloatExpressionTarget.RectY => rectangle.Y,
                ItemFloatExpressionTarget.RectW => rectangle.Width,
                ItemFloatExpressionTarget.RectH => rectangle.Height,
                _ => EvaluateFloat(expression.Expression, 0)
            };
            floatResults[expression.Target] = value;
            ApplyColor(expression.Target, value, ref foreColor, ref glowColor, ref backColor);
        }

        return new MenuEvaluatedItemState(
            item.Id,
            item.WindowId,
            item.Name,
            visible,
            disabled,
            rectangle,
            foreColor,
            glowColor,
            backColor,
            borderColor,
            text,
            material,
            new ReadOnlyDictionary<ItemFloatExpressionTarget, MenuEvaluation<float>>(floatResults));
    }

    private static void ApplyColor(
        ItemFloatExpressionTarget target,
        MenuEvaluation<float> value,
        ref MenuEvaluatedColor foreColor,
        ref MenuEvaluatedColor glowColor,
        ref MenuEvaluatedColor backColor)
    {
        switch (target)
        {
            case ItemFloatExpressionTarget.ForeColorR:
                foreColor = foreColor with { R = value };
                break;
            case ItemFloatExpressionTarget.ForeColorG:
                foreColor = foreColor with { G = value };
                break;
            case ItemFloatExpressionTarget.ForeColorB:
                foreColor = foreColor with { B = value };
                break;
            case ItemFloatExpressionTarget.ForeColorRgb:
                foreColor = foreColor with { R = value, G = value, B = value };
                break;
            case ItemFloatExpressionTarget.ForeColorA:
                foreColor = foreColor with { A = value };
                break;
            case ItemFloatExpressionTarget.GlowColorR:
                glowColor = glowColor with { R = value };
                break;
            case ItemFloatExpressionTarget.GlowColorG:
                glowColor = glowColor with { G = value };
                break;
            case ItemFloatExpressionTarget.GlowColorB:
                glowColor = glowColor with { B = value };
                break;
            case ItemFloatExpressionTarget.GlowColorRgb:
                glowColor = glowColor with { R = value, G = value, B = value };
                break;
            case ItemFloatExpressionTarget.GlowColorA:
                glowColor = glowColor with { A = value };
                break;
            case ItemFloatExpressionTarget.BackColorR:
                backColor = backColor with { R = value };
                break;
            case ItemFloatExpressionTarget.BackColorG:
                backColor = backColor with { G = value };
                break;
            case ItemFloatExpressionTarget.BackColorB:
                backColor = backColor with { B = value };
                break;
            case ItemFloatExpressionTarget.BackColorRgb:
                backColor = backColor with { R = value, G = value, B = value };
                break;
            case ItemFloatExpressionTarget.BackColorA:
                backColor = backColor with { A = value };
                break;
        }
    }

    private MenuEvaluation<float> ResolveItemWindowCoordinate(
        MenuNodeId id,
        ItemFloatExpressionTarget target)
    {
        MenuEvaluatedRectangle rect = ResolveItemRectangle(id);
        MenuEvaluation<float> component = target switch
        {
            ItemFloatExpressionTarget.RectX => rect.X,
            ItemFloatExpressionTarget.RectY => rect.Y,
            ItemFloatExpressionTarget.RectW => rect.Width,
            ItemFloatExpressionTarget.RectH => rect.Height,
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };
        if (!component.IsKnown || target is not (ItemFloatExpressionTarget.RectX or ItemFloatExpressionTarget.RectY))
            return component;

        // Item_SetScreenCoords composes Window.Rect in virtual coordinates.
        // ScreenPlacement is applied separately when the menu is drawn.
        MenuEvaluation<float> origin = ResolveMenuOrigin(target);
        var dependencies = component.Dependencies.Concat(origin.Dependencies);
        var trace = component.Trace.Concat(origin.Trace);
        float border = _program.Items.First(item => item.Id == id).Definition.BorderInset;
        return origin.Status switch
        {
            MenuEvaluationStatus.Known => MenuEvaluation<float>.Known(
                component.Value + origin.Value + _program.Definition.BorderInset + border,
                dependencies,
                trace),
            MenuEvaluationStatus.Unknown => MenuEvaluation<float>.Unknown(default, dependencies, trace),
            _ => MenuEvaluation<float>.Error(default, dependencies, trace)
        };
    }

    private MenuEvaluation<float> ResolveMenuOrigin(ItemFloatExpressionTarget target)
    {
        if (_menuOrigins.TryGetValue(target, out MenuEvaluation<float>? existing))
            return existing;

        DebugMenuDefinition root = _program.Definition;
        bool isX = target == ItemFloatExpressionTarget.RectX;
        float fallback = isX ? root.Rectangle.X : root.Rectangle.Y;
        if (!_menuOriginStack.Add(target))
        {
            return MenuEvaluation<float>.Error(fallback, [],
                [new MenuEvaluationTraceEntry(MenuEvaluationStatus.Error,
                    $"Menu {target} depends on its own origin through item geometry.")]);
        }

        MenuEvaluation<float> origin = EvaluateFloat(isX ? root.RectX : root.RectY, fallback);
        _menuOriginStack.Remove(target);
        _menuOrigins.Add(target, origin);
        return origin;
    }

    private MenuEvaluatedRectangle ResolveItemRectangle(MenuNodeId id)
    {
        if (_rectangles.TryGetValue(id, out MenuEvaluatedRectangle? existing))
            return existing;
        MenuDebugItemProgram? item = _program.Items.FirstOrDefault(value => value.Id == id);
        if (item is null)
            return ErrorRectangle($"Menu item '{id}' is not part of this program.");
        if (!_rectangleStack.Add(id))
        {
            return ErrorRectangle(
                $"Item geometry dependency cycle includes '{item.Name ?? id.ToString()}'.");
        }

        DebugItemDefinition definition = item.Definition;
        var expressions = definition.FloatExpressions
            .GroupBy(value => value.Target)
            .ToDictionary(group => group.Key, group => group.Last().Expression);
        MenuEvaluatedRectangle rectangle = EvaluateRectangle(
            definition.Rectangle,
            expressions.GetValueOrDefault(ItemFloatExpressionTarget.RectX),
            expressions.GetValueOrDefault(ItemFloatExpressionTarget.RectY),
            expressions.GetValueOrDefault(ItemFloatExpressionTarget.RectW),
            expressions.GetValueOrDefault(ItemFloatExpressionTarget.RectH));
        _rectangleStack.Remove(id);
        _rectangles.Add(id, rectangle);
        return rectangle;
    }

    private MenuEvaluatedRectangle EvaluateRectangle(
        DebugRectangleDefinition authored,
        MenuDebugExpression? x,
        MenuDebugExpression? y,
        MenuDebugExpression? width,
        MenuDebugExpression? height) =>
        new(
            EvaluateFloat(x, authored.X),
            EvaluateFloat(y, authored.Y),
            EvaluateFloat(width, authored.Width),
            EvaluateFloat(height, authored.Height),
            authored.HorizontalAlignment,
            authored.VerticalAlignment);

    private MenuEvaluation<bool> EvaluateItemVisibility(
        DebugItemDefinition item, MenuDebugItemRuntimeState? runtime)
    {
        // PS3 0x254ef8 checks the Window bit and dvar gate before the expression.
        if (!item.IsResolved || !(runtime?.Visible ?? item.AuthoredVisible))
            return MenuEvaluation<bool>.Known(false);
        if ((item.DvarFlags & (ItemDvarFlags.Show | ItemDvarFlags.Hide)) == 0)
            return EvaluateBoolean(item.Visible, item.Visible is null);

        var gate = EvaluateDvarGate(item, ItemDvarFlags.Show);
        if (!gate.Value || item.Visible is null)
            return gate;
        return CombineDvarGate(EvaluateBoolean(item.Visible, false), gate, blocked: false);
    }

    private MenuEvaluation<bool> EvaluateDvarGate(DebugItemDefinition item, ItemDvarFlags requested)
    {
        var gate = MenuDvarPredicate.Allows(item, _context.Scenario, requested);
        Capture(gate);
        return gate;
    }

    private static MenuEvaluation<bool> CombineDvarGate(
        MenuEvaluation<bool> expression, MenuEvaluation<bool> gate, bool blocked)
    {
        var dependencies = expression.Dependencies.Concat(gate.Dependencies);
        var trace = expression.Trace.Concat(gate.Trace);
        if ((gate.IsKnown && !gate.Value) || (expression.IsKnown && expression.Value == blocked))
            return MenuEvaluation<bool>.Known(blocked, dependencies, trace);
        bool result = gate.Value ? expression.Value : blocked;
        if (gate.Status == MenuEvaluationStatus.Error || expression.Status == MenuEvaluationStatus.Error)
            return MenuEvaluation<bool>.Error(result, dependencies, trace);
        return gate.IsKnown && expression.IsKnown
            ? MenuEvaluation<bool>.Known(result, dependencies, trace)
            : MenuEvaluation<bool>.Unknown(result, dependencies, trace);
    }

    private MenuEvaluation<bool> EvaluateVisibility(
        bool authoredVisible,
        MenuDebugExpression? expression) =>
        authoredVisible
            ? EvaluateBoolean(expression, true)
            : MenuEvaluation<bool>.Known(false);

    private MenuEvaluation<bool> EvaluateBoolean(
        MenuDebugExpression? expression,
        bool fallback)
    {
        if (expression is null)
            return MenuEvaluation<bool>.Known(fallback);
        MenuEvaluation<MenuDebugValue> result = EvaluateExpression(expression);
        MenuEvaluation<bool> converted;
        if (!result.IsKnown)
        {
            converted = result.Status == MenuEvaluationStatus.Error
                ? MenuEvaluation<bool>.Error(fallback, result.Dependencies, result.Trace)
                : MenuEvaluation<bool>.Unknown(fallback, result.Dependencies, result.Trace);
        }
        else if (result.Value.TryGetBoolean(out bool value))
        {
            converted = MenuEvaluation<bool>.Known(value, result.Dependencies, result.Trace);
        }
        else
        {
            converted = MenuEvaluation<bool>.Error(
                fallback,
                result.Dependencies,
                result.Trace.Append(new MenuEvaluationTraceEntry(
                    MenuEvaluationStatus.Error,
                    "Expression result cannot be converted to Boolean.")));
        }
        Capture(converted);
        return converted;
    }

    private MenuEvaluation<float> EvaluateFloat(
        MenuDebugExpression? expression,
        float fallback)
    {
        if (expression is null)
            return MenuEvaluation<float>.Known(fallback);
        MenuEvaluation<MenuDebugValue> result = EvaluateExpression(expression);
        MenuEvaluation<float> converted;
        if (!result.IsKnown)
        {
            converted = result.Status == MenuEvaluationStatus.Error
                ? MenuEvaluation<float>.Error(fallback, result.Dependencies, result.Trace)
                : MenuEvaluation<float>.Unknown(fallback, result.Dependencies, result.Trace);
        }
        else if (result.Value.TryGetFloat(out float value))
        {
            converted = MenuEvaluation<float>.Known(value, result.Dependencies, result.Trace);
        }
        else
        {
            converted = MenuEvaluation<float>.Error(
                fallback,
                result.Dependencies,
                result.Trace.Append(new MenuEvaluationTraceEntry(
                    MenuEvaluationStatus.Error,
                    "Expression result cannot be converted to Float.")));
        }
        Capture(converted);
        return converted;
    }

    private MenuEvaluation<string?> EvaluateString(
        MenuDebugExpression? expression,
        string? fallback,
        bool itemText)
    {
        MenuEvaluation<MenuDebugValue> result = expression is null
            ? MenuEvaluation<MenuDebugValue>.Known(
                MenuDebugValue.FromString(fallback ?? string.Empty))
            : EvaluateExpression(expression);
        string? value = result.IsKnown
            ? itemText && expression is not null
                ? result.Value.AsFormattedExpressionString()
                : result.Value.AsString()
            : null;
        if (itemText && value is not null && value.StartsWith('@'))
        {
            result = MenuExpressionEvaluator.ResolveLocalization(
                value,
                operation: null,
                _context,
                result);
            value = result.IsKnown ? result.Value.AsString() : null;
        }

        MenuEvaluation<string?> converted = result.Status switch
        {
            MenuEvaluationStatus.Known => MenuEvaluation<string?>.Known(
                value,
                result.Dependencies,
                result.Trace),
            MenuEvaluationStatus.Unknown => MenuEvaluation<string?>.Unknown(
                fallback,
                result.Dependencies,
                result.Trace),
            _ => MenuEvaluation<string?>.Error(
                fallback,
                result.Dependencies,
                result.Trace)
        };
        Capture(converted);
        return converted;
    }

    private static MenuEvaluatedColor Color(DebugColorDefinition value) =>
        new(
            MenuEvaluation<float>.Known(value.A),
            MenuEvaluation<float>.Known(value.R),
            MenuEvaluation<float>.Known(value.G),
            MenuEvaluation<float>.Known(value.B));

    private static MenuEvaluatedColor Color(MenuColorValue value) =>
        new(
            MenuEvaluation<float>.Known(value.A),
            MenuEvaluation<float>.Known(value.R),
            MenuEvaluation<float>.Known(value.G),
            MenuEvaluation<float>.Known(value.B));

    private static MenuEvaluatedRectangle ErrorRectangle(string message)
    {
        MenuEvaluation<float> error = MenuEvaluation<float>.Error(
            0,
            [],
            [new MenuEvaluationTraceEntry(MenuEvaluationStatus.Error, message)]);
        return new MenuEvaluatedRectangle(
            error,
            error,
            error,
            error,
            HorizontalAlign.HORIZONTAL_ALIGN_SUBLEFT,
            VerticalAlign.VERTICAL_ALIGN_SUBTOP);
    }

    private static MenuEvaluation<MenuDebugValue> WithDependencies(
        MenuEvaluation<MenuDebugValue> result,
        IEnumerable<MenuDebugDependency> dependencies) => result.Status switch
        {
            MenuEvaluationStatus.Known => MenuEvaluation<MenuDebugValue>.Known(
                result.Value,
                result.Dependencies.Concat(dependencies),
                result.Trace),
            MenuEvaluationStatus.Unknown => MenuEvaluation<MenuDebugValue>.Unknown(
                result.Value,
                result.Dependencies.Concat(dependencies),
                result.Trace),
            _ => MenuEvaluation<MenuDebugValue>.Error(
                result.Value,
                result.Dependencies.Concat(dependencies),
                result.Trace)
        };

    private void Capture<T>(MenuEvaluation<T> evaluation) =>
        _trace.AddRange(evaluation.Trace);
}
