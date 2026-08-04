using IW4.Assets.Assets.Menu;

namespace IW4.Studio.Documents.MenuEditing.Debugging;

public sealed record MenuEvaluatedRectangle(
    MenuEvaluation<float> X,
    MenuEvaluation<float> Y,
    MenuEvaluation<float> Width,
    MenuEvaluation<float> Height,
    HorizontalAlign HorizontalAlignment,
    VerticalAlign VerticalAlignment);

public sealed record MenuEvaluatedColor(
    MenuEvaluation<float> A,
    MenuEvaluation<float> R,
    MenuEvaluation<float> G,
    MenuEvaluation<float> B);

public sealed record MenuEvaluatedWindowState(
    MenuNodeId Id,
    MenuEvaluation<bool> IsVisible,
    MenuEvaluatedRectangle Rectangle);

public sealed record MenuEvaluatedItemState(
    MenuNodeId Id,
    MenuNodeId WindowId,
    string? Name,
    MenuEvaluation<bool> IsVisible,
    MenuEvaluation<bool> IsDisabled,
    MenuEvaluatedRectangle Rectangle,
    MenuEvaluatedColor ForeColor,
    MenuEvaluatedColor GlowColor,
    MenuEvaluatedColor BackColor,
    MenuEvaluatedColor BorderColor,
    MenuEvaluation<string?> Text,
    MenuEvaluation<string?> MaterialName,
    IReadOnlyDictionary<ItemFloatExpressionTarget, MenuEvaluation<float>> FloatExpressions);

public sealed class MenuEvaluatedState
{
    private readonly IReadOnlyList<MenuEvaluatedItemState> _items;
    private readonly IReadOnlyList<MenuEvaluationTraceEntry> _trace;

    internal MenuEvaluatedState(
        MenuNodeId menuId,
        Guid programRevisionToken,
        MenuEvaluatedWindowState window,
        IEnumerable<MenuEvaluatedItemState> items,
        IEnumerable<MenuEvaluationTraceEntry> trace)
    {
        MenuId = menuId;
        ProgramRevisionToken = programRevisionToken;
        Window = window;
        _items = Array.AsReadOnly(items.ToArray());
        _trace = Array.AsReadOnly(trace.ToArray());
    }

    public MenuNodeId MenuId { get; }
    public MenuEvaluatedWindowState Window { get; }
    public IReadOnlyList<MenuEvaluatedItemState> Items => _items;
    public IReadOnlyList<MenuEvaluationTraceEntry> Trace => _trace;
    internal Guid ProgramRevisionToken { get; }
}
