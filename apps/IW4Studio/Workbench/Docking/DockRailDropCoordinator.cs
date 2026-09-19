namespace IW4.Studio.Desktop.Workbench.Docking;

/// <summary>
/// Translates a pointer drop between rail items into the final-index move
/// consumed by <see cref="DockLayoutController"/>.
/// </summary>
public sealed class DockRailDropCoordinator
{
    private readonly DockLayoutController _controller;

    public DockRailDropCoordinator(DockLayoutController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
    }

    /// <summary>
    /// Predicts the result without mutating the rail. This is suitable for an
    /// Avalonia DragOver handler deciding whether to advertise a move effect.
    /// </summary>
    public DockMoveResult EvaluateDrop(
        string toolId,
        DockRegion allowedRegion,
        DockRegion pointerDropRegion,
        int pointerInsertionIndex) =>
        EvaluateDrop(
            new DockRailDragPayload(toolId, allowedRegion),
            pointerDropRegion,
            pointerInsertionIndex);

    /// <summary>
    /// Predicts the result without mutating the rail. This is suitable for an
    /// Avalonia DragOver handler deciding whether to advertise a move effect.
    /// </summary>
    public DockMoveResult EvaluateDrop(
        DockRailDragPayload payload,
        DockRegion pointerDropRegion,
        int pointerInsertionIndex) =>
        EvaluateDrop(
            payload,
            pointerDropRegion,
            pointerInsertionIndex,
            out _);

    /// <param name="pointerInsertionIndex">
    /// The insertion slot under the pointer in the unmodified rail. Valid
    /// values are zero through the rail's tool count, inclusive.
    /// </param>
    public DockMoveResult DropTool(
        string toolId,
        DockRegion allowedRegion,
        DockRegion pointerDropRegion,
        int pointerInsertionIndex) =>
        DropTool(
            new DockRailDragPayload(toolId, allowedRegion),
            pointerDropRegion,
            pointerInsertionIndex);

    /// <param name="pointerInsertionIndex">
    /// The insertion slot under the pointer in the unmodified rail. Valid
    /// values are zero through the rail's tool count, inclusive.
    /// </param>
    public DockMoveResult DropTool(
        DockRailDragPayload payload,
        DockRegion pointerDropRegion,
        int pointerInsertionIndex)
    {
        ArgumentNullException.ThrowIfNull(payload);

        DockMoveResult evaluation = EvaluateDrop(
            payload,
            pointerDropRegion,
            pointerInsertionIndex,
            out int targetIndex);
        if (evaluation != DockMoveResult.Moved)
            return evaluation;

        return _controller.MoveTool(payload.ToolId, pointerDropRegion, targetIndex);
    }

    private DockMoveResult EvaluateDrop(
        DockRailDragPayload payload,
        DockRegion pointerDropRegion,
        int pointerInsertionIndex,
        out int targetIndex)
    {
        ArgumentNullException.ThrowIfNull(payload);
        targetIndex = -1;

        DockToolState? tool = _controller.State.FindTool(payload.ToolId);
        if (tool is null)
            return DockMoveResult.ToolNotFound;

        if (!Enum.IsDefined(pointerDropRegion) ||
            tool.AllowedRegion != payload.AllowedRegion ||
            pointerDropRegion != payload.AllowedRegion)
        {
            return DockMoveResult.RegionNotAllowed;
        }

        DockRegionState region = _controller.State.Region(pointerDropRegion);
        if (pointerInsertionIndex < 0 || pointerInsertionIndex > region.Tools.Count)
            return DockMoveResult.TargetIndexOutOfRange;

        // The pointer index addresses a slot while the dragged item is still
        // present. Removing an item before that slot shifts the final index by
        // one. Both slots touching the dragged item therefore mean no change.
        targetIndex = pointerInsertionIndex > tool.Position
            ? pointerInsertionIndex - 1
            : pointerInsertionIndex;

        return targetIndex == tool.Position
            ? DockMoveResult.Unchanged
            : DockMoveResult.Moved;
    }
}
