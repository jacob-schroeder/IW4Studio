using System.Globalization;

namespace IW4.Studio.Documents.MenuEditing.Debugging;

/// <summary>
/// Applies the bounded setItemColor command to runtime-only Item Window color
/// overrides. Targets resolve as self, exact Item name, or exact Item group.
/// </summary>
internal sealed class MenuDebugItemColorController
{
    private readonly MenuDebugProgram _program;
    private readonly MenuDebugDispatchState _state;
    private readonly MenuDebugDispatchTraceBuilder _trace;

    public MenuDebugItemColorController(
        MenuDebugProgram program,
        MenuDebugDispatchState state,
        MenuDebugDispatchTraceBuilder trace)
    {
        _program = program;
        _state = state;
        _trace = trace;
    }

    public bool Apply(
        IReadOnlyList<string> tokens,
        string path,
        MenuNodeId? contextItemId)
    {
        if (!TryTarget(tokens[2], out MenuDebugItemColorTarget target))
        {
            return Reject(
                path,
                tokens[0],
                $"Color channel '{tokens[2]}' is not supported.");
        }
        if (!TryColor(tokens, out MenuColorValue color))
        {
            return Reject(
                path,
                tokens[0],
                "Color components must be finite invariant-culture numbers.");
        }

        MenuDebugItemProgram[] matches = ResolveTargets(
            tokens[1],
            contextItemId).ToArray();
        if (matches.Length == 0)
        {
            _trace.AddDiagnostic(
                path,
                MenuDebugDiagnosticKind.Blocker,
                MenuEvaluationStatus.Error,
                "item-color-target-not-found",
                $"setItemColor could not resolve target '{tokens[1]}'.");
            return true;
        }

        foreach (MenuDebugItemProgram item in matches)
            Apply(item, target, color, path);
        return true;
    }

    private void Apply(
        MenuDebugItemProgram item,
        MenuDebugItemColorTarget target,
        MenuColorValue color,
        string path)
    {
        MenuDebugItemRuntimeState state = _state.ItemRuntimeState(item.Id);
        MenuColorValue? previous = target switch
        {
            MenuDebugItemColorTarget.ForeColor =>
                state.ForeColor ?? Color(item.Definition.ForeColor),
            MenuDebugItemColorTarget.BackColor =>
                state.BackColor ?? Color(item.Definition.BackColor),
            MenuDebugItemColorTarget.BorderColor =>
                state.BorderColor ?? Color(item.Definition.BorderColor),
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };
        MenuDebugItemRuntimeState updated = target switch
        {
            MenuDebugItemColorTarget.ForeColor => state with { ForeColor = color },
            MenuDebugItemColorTarget.BackColor => state with { BackColor = color },
            MenuDebugItemColorTarget.BorderColor => state with { BorderColor = color },
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };
        _state.SetItemRuntimeState(item.Id, updated);
        _trace.AddItemColor(path, item.Id, target, previous, color);
    }

    private IEnumerable<MenuDebugItemProgram> ResolveTargets(
        string target,
        MenuNodeId? contextItemId)
    {
        if (target.Equals("self", StringComparison.OrdinalIgnoreCase))
        {
            if (contextItemId is { } selfId &&
                _program.Items.FirstOrDefault(item => item.Id == selfId) is { } self)
            {
                yield return self;
            }
            yield break;
        }

        foreach (MenuDebugItemProgram item in _program.Items)
        {
            if (string.Equals(item.Name, target, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.Group, target, StringComparison.OrdinalIgnoreCase))
            {
                yield return item;
            }
        }
    }

    private static bool TryTarget(
        string value,
        out MenuDebugItemColorTarget target)
    {
        if (value.Equals("forecolor", StringComparison.OrdinalIgnoreCase))
        {
            target = MenuDebugItemColorTarget.ForeColor;
            return true;
        }
        if (value.Equals("backcolor", StringComparison.OrdinalIgnoreCase))
        {
            target = MenuDebugItemColorTarget.BackColor;
            return true;
        }
        if (value.Equals("bordercolor", StringComparison.OrdinalIgnoreCase))
        {
            target = MenuDebugItemColorTarget.BorderColor;
            return true;
        }

        target = default;
        return false;
    }

    private static bool TryColor(
        IReadOnlyList<string> tokens,
        out MenuColorValue color)
    {
        var components = new float[4];
        for (int index = 0; index < components.Length; index++)
        {
            if (!float.TryParse(
                    tokens[index + 3],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out components[index]) ||
                !float.IsFinite(components[index]))
            {
                color = default;
                return false;
            }
        }

        color = new MenuColorValue(
            components[3],
            components[0],
            components[1],
            components[2]);
        return true;
    }

    private static MenuColorValue Color(DebugColorDefinition value) =>
        new(value.A, value.R, value.G, value.B);

    private bool Reject(string path, string command, string message)
    {
        _trace.AddDiagnostic(
            path,
            MenuDebugDiagnosticKind.Blocker,
            MenuEvaluationStatus.Error,
            "runtime-command-invalid",
            $"{command}: {message}");
        return false;
    }
}
