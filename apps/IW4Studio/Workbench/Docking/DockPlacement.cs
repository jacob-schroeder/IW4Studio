namespace IW4.Studio.Desktop.Workbench.Docking;

/// <summary>
/// A constrained pane host around the central Studio workspace.
/// </summary>
public enum DockRegion
{
    Left,
    Bottom,
    Right
}

/// <summary>
/// The rail section that owns a tool's launcher icon.
/// </summary>
public enum DockRailGroup
{
    LeftTop,
    LeftBottom,
    Right
}

public static class DockPlacement
{
    public static DockRegion RegionFor(DockRailGroup railGroup) =>
        railGroup switch
        {
            DockRailGroup.LeftTop => DockRegion.Left,
            DockRailGroup.LeftBottom => DockRegion.Bottom,
            DockRailGroup.Right => DockRegion.Right,
            _ => throw new ArgumentOutOfRangeException(nameof(railGroup), railGroup, null)
        };
}
