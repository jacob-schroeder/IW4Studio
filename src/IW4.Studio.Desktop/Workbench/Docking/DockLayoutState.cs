namespace IW4.Studio.Desktop.Workbench.Docking;

/// <summary>
/// Read-only entry point for the mutable state owned by <see cref="DockLayoutController"/>.
/// </summary>
public sealed class DockLayoutState
{
    private readonly IReadOnlyDictionary<string, DockToolState> _toolsById;

    internal DockLayoutState(
        DockRegionState left,
        DockRegionState bottom,
        DockRegionState right,
        IReadOnlyDictionary<string, DockToolState> toolsById)
    {
        Left = left ?? throw new ArgumentNullException(nameof(left));
        Bottom = bottom ?? throw new ArgumentNullException(nameof(bottom));
        Right = right ?? throw new ArgumentNullException(nameof(right));
        _toolsById = toolsById ?? throw new ArgumentNullException(nameof(toolsById));
        Regions = Array.AsReadOnly([Left, Bottom, Right]);
    }

    public DockRegionState Left { get; }

    public DockRegionState Bottom { get; }

    public DockRegionState Right { get; }

    public IReadOnlyList<DockRegionState> Regions { get; }

    public DockRegionState Region(DockRegion region) =>
        region switch
        {
            DockRegion.Left => Left,
            DockRegion.Bottom => Bottom,
            DockRegion.Right => Right,
            _ => throw new ArgumentOutOfRangeException(nameof(region), region, null)
        };

    public IReadOnlyList<DockToolState> Rail(DockRailGroup railGroup) =>
        Region(DockPlacement.RegionFor(railGroup)).Tools;

    public DockToolState? FindTool(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _toolsById.GetValueOrDefault(id);
    }

    internal bool TryGetTool(string id, out DockToolState? tool) =>
        _toolsById.TryGetValue(id, out tool);
}
