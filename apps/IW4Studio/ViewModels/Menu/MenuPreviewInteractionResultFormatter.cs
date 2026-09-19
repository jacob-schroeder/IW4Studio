using IW4.Studio.Documents.MenuEditing;
using IW4.Studio.Documents.MenuEditing.Debugging;

namespace IW4.Studio.Desktop.ViewModels.Menu;

internal sealed record MenuPreviewInteractionResultPresentation(
    int StateChangeCount,
    int ScriptCount,
    int IssueCount,
    bool HasDetails,
    string Details);

/// <summary>
/// Pure formatter for debugger dispatch output. Keeping this outside the view
/// model makes state orchestration independent from developer-detail copy.
/// </summary>
internal static class MenuPreviewInteractionResultFormatter
{
    public static MenuPreviewInteractionResultPresentation Format(
        MenuDebugDispatchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        string[] trace = result.Trace
            .OrderBy(value => value.Sequence)
            .Select(FormatTrace)
            .ToArray();
        string[] diagnostics = result.Diagnostics
            .Select(value => $"{value.Kind} {value.Code}: {value.Message}")
            .ToArray();
        string[] stateChanges = result.LocalVariableChanges
            .Select(value =>
                $"{value.Name}: {FormatValue(value.PreviousValue)} → " +
                $"{FormatValue(value.Value)} ({value.DeclaredKind})")
            .Concat(result.ItemColorChanges.Select(FormatItemColorChange))
            .ToArray();
        string[] scripts = result.QueuedScripts
            .Select(value => value.Script)
            .ToArray();
        bool hasDetails = trace.Length > 0 ||
            diagnostics.Length > 0 ||
            stateChanges.Length > 0 ||
            scripts.Length > 0;
        return new MenuPreviewInteractionResultPresentation(
            stateChanges.Length,
            scripts.Length,
            diagnostics.Length,
            hasDetails,
            FormatDetails(trace, stateChanges, diagnostics, scripts));
    }

    private static string FormatTrace(MenuDebugDispatchTraceEntry value) =>
        value switch
        {
            MenuDebugDecisionTraceEntry decision =>
                $"#{value.Sequence:N0} {decision.BranchKind}: " +
                $"{(decision.IsSelected is null ? decision.Status : decision.IsSelected)} " +
                $"· {value.HandlerPath}",
            MenuDebugLocalVariableTraceEntry local =>
                $"#{value.Sequence:N0} Local {local.Name}: " +
                $"{FormatValue(local.PreviousValue)} → {FormatValue(local.Value)} " +
                $"({local.Status}, {(local.IsApplied ? "applied" : "not applied")}) " +
                $"· {value.HandlerPath}",
            MenuDebugItemColorTraceEntry color =>
                $"#{value.Sequence:N0} {FormatItemColorChange(color)} " +
                $"· {value.HandlerPath}",
            MenuDebugQueuedScriptTraceEntry =>
                $"#{value.Sequence:N0} Command awaiting game runtime · " +
                value.HandlerPath,
            MenuDebugFocusTraceEntry focus =>
                $"#{value.Sequence:N0} Focus: " +
                $"{FormatNode(focus.PreviousItemId)} → {FormatNode(focus.ItemId)} " +
                $"· {value.HandlerPath}",
            MenuDebugDiagnosticTraceEntry diagnostic =>
                $"#{value.Sequence:N0} {diagnostic.Kind} {diagnostic.Code}: " +
                $"{diagnostic.Message} · {value.HandlerPath}",
            _ => $"#{value.Sequence:N0} {value.GetType().Name} · " +
                value.HandlerPath
        };

    private static string FormatValue(MenuDebugValue? value) =>
        value is { } actual ? actual.AsString() : "<unset>";

    private static string FormatNode(MenuNodeId? value) =>
        value?.ToString() ?? "<none>";

    private static string FormatItemColorChange(
        MenuDebugItemColorTraceEntry value) =>
        $"Item {value.ItemId} {value.Target}: " +
        $"{FormatColor(value.PreviousValue)} → {FormatColor(value.Value)}";

    private static string FormatColor(MenuColorValue? value)
    {
        if (value is not { } color)
            return "<unset>";
        return $"rgba({color.R:0.###}, {color.G:0.###}, " +
            $"{color.B:0.###}, {color.A:0.###})";
    }

    private static string FormatDetails(
        IReadOnlyList<string> trace,
        IReadOnlyList<string> stateChanges,
        IReadOnlyList<string> diagnostics,
        IReadOnlyList<string> queuedScripts)
    {
        var sections = new List<string>();
        AddSection(sections, "Ordered trace", trace);
        AddSection(sections, "Applied state changes", stateChanges);
        AddSection(sections, "Issues", diagnostics);
        AddSection(
            sections,
            "Commands awaiting game runtime (not executed by Studio)",
            queuedScripts,
            Environment.NewLine + "---" + Environment.NewLine);
        return sections.Count == 0
            ? "This action produced no trace entries."
            : string.Join(Environment.NewLine + Environment.NewLine, sections);
    }

    private static void AddSection(
        ICollection<string> sections,
        string heading,
        IReadOnlyList<string> lines,
        string? separator = null)
    {
        if (lines.Count == 0)
            return;
        sections.Add(
            heading + Environment.NewLine +
            string.Join(separator ?? Environment.NewLine, lines));
    }
}
