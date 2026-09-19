using IW4.Studio.Documents.MenuEditing;
using IW4.Studio.Documents.MenuEditing.Debugging;
using IW4.Studio.Desktop.Documents.MenuEditing.Preview;

namespace IW4.Studio.Desktop.ViewModels.Menu;

internal sealed record MenuPreviewEvaluationDiagnostics(
    IReadOnlyList<MenuEvaluationTraceEntry> Active,
    IReadOnlyList<MenuEvaluationTraceEntry> Dormant);

/// <summary>
/// Separates diagnostics belonging to nodes in the current projected scene
/// from diagnostics produced while evaluating non-rendered authored branches.
/// The evaluator deliberately inspects the complete Menu graph; that complete
/// trace remains useful for advanced debugging, but it must not make dormant
/// expressions look like required scenario setup.
/// </summary>
internal static class MenuPreviewEvaluationRelevance
{
    public static MenuPreviewEvaluationDiagnostics Classify(
        MenuEvaluatedState state,
        MenuPreviewScene scene)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(scene);

        HashSet<MenuNodeId> projectedNodes = scene.Primitives
            .Select(value => value.NodeId)
            .Concat(scene.HitRegions
                .Where(value => HasArea(value.Bounds))
                .Select(value => value.NodeId))
            .ToHashSet();

        var active = new List<MenuEvaluationTraceEntry>();
        Add(active, state.Window.IsVisible);
        Add(active, state.Window.Rectangle);
        foreach (MenuEvaluatedItemState item in state.Items.Where(value =>
            projectedNodes.Contains(value.Id)))
        {
            Add(active, item.IsVisible);
            Add(active, item.IsDisabled);
            Add(active, item.Rectangle);
            Add(active, item.ForeColor);
            Add(active, item.GlowColor);
            Add(active, item.BackColor);
            Add(active, item.BorderColor);
            Add(active, item.Text);
            Add(active, item.MaterialName);
            foreach (MenuEvaluation<float> value in
                item.FloatExpressions.Values)
            {
                Add(active, value);
            }
        }

        MenuEvaluationTraceEntry[] activeDiagnostics = Diagnostics(active);
        var activeSet = activeDiagnostics.ToHashSet();
        MenuEvaluationTraceEntry[] dormantDiagnostics = Diagnostics(state.Trace)
            .Where(value => !activeSet.Contains(value))
            .ToArray();
        return new MenuPreviewEvaluationDiagnostics(
            Array.AsReadOnly(activeDiagnostics),
            Array.AsReadOnly(dormantDiagnostics));
    }

    private static bool HasArea(MenuPreviewRect bounds) =>
        float.IsFinite(bounds.Width) &&
        float.IsFinite(bounds.Height) &&
        Math.Abs(bounds.Width) > float.Epsilon &&
        Math.Abs(bounds.Height) > float.Epsilon;

    private static MenuEvaluationTraceEntry[] Diagnostics(
        IEnumerable<MenuEvaluationTraceEntry> trace) => trace
        .Where(value => value.Status != MenuEvaluationStatus.Known)
        .Distinct()
        .ToArray();

    private static void Add<T>(
        List<MenuEvaluationTraceEntry> target,
        MenuEvaluation<T> evaluation) => target.AddRange(evaluation.Trace);

    private static void Add(
        List<MenuEvaluationTraceEntry> target,
        MenuEvaluatedRectangle rectangle)
    {
        Add(target, rectangle.X);
        Add(target, rectangle.Y);
        Add(target, rectangle.Width);
        Add(target, rectangle.Height);
    }

    private static void Add(
        List<MenuEvaluationTraceEntry> target,
        MenuEvaluatedColor color)
    {
        Add(target, color.A);
        Add(target, color.R);
        Add(target, color.G);
        Add(target, color.B);
    }
}
