using System.Collections.ObjectModel;
using IW4.Assets.Assets.Menu;

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
    private readonly List<MenuEvaluationTraceEntry> _trace = [];
    private readonly EvaluationContext _context;

    public MenuEvaluationSession(
        MenuExpressionEvaluator evaluator,
        MenuDebugProgram program,
        MenuDebugScenario scenario)
    {
        _evaluator = evaluator;
        _program = program;
        _context = new EvaluationContext(program, scenario, ResolveItemRectangle);
    }

    public MenuEvaluatedState Evaluate()
    {
        DebugMenuDefinition root = _program.Definition;
        MenuEvaluatedRectangle rootRectangle = EvaluateRectangle(
            root.Rectangle,
            root.RectX,
            root.RectY,
            root.RectWidth,
            root.RectHeight);
        MenuEvaluation<bool> rootVisible = EvaluateVisibility(
            root.AuthoredVisible,
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
        MenuEvaluation<bool> visible = definition.IsResolved
            ? EvaluateVisibility(definition.AuthoredVisible, definition.Visible)
            : MenuEvaluation<bool>.Known(false);
        MenuEvaluation<bool> disabled = EvaluateBoolean(definition.Disabled, false);
        MenuEvaluation<string?> text = EvaluateString(
            definition.Text,
            definition.AuthoredText,
            localize: true);
        MenuEvaluation<string?> material = EvaluateString(
            definition.Material,
            definition.AuthoredMaterial,
            localize: false);

        var floatResults = new Dictionary<ItemFloatExpressionTarget, MenuEvaluation<float>>();
        MenuEvaluatedColor foreColor = Color(definition.ForeColor);
        MenuEvaluatedColor glowColor = Color(definition.GlowColor);
        MenuEvaluatedColor backColor = Color(definition.BackColor);
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
        bool localize)
    {
        MenuEvaluation<MenuDebugValue> result = expression is null
            ? MenuEvaluation<MenuDebugValue>.Known(
                MenuDebugValue.FromString(fallback ?? string.Empty))
            : EvaluateExpression(expression);
        if (result.IsKnown && localize && result.Value.AsString().StartsWith('@'))
        {
            result = MenuExpressionEvaluator.ResolveLocalization(
                result.Value.AsString(),
                operation: null,
                _context,
                result);
        }

        MenuEvaluation<string?> converted = result.Status switch
        {
            MenuEvaluationStatus.Known => MenuEvaluation<string?>.Known(
                result.Value.AsString(),
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
