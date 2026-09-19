namespace IW4.Studio.Desktop.Workbench.Docking;

/// <summary>
/// Sizing policy for the three constrained workbench regions.
/// </summary>
public sealed class DockLayoutOptions
{
    public DockLayoutOptions(
        DockRegionSizeLimits left,
        DockRegionSizeLimits bottom,
        DockRegionSizeLimits right)
    {
        Left = left ?? throw new ArgumentNullException(nameof(left));
        Bottom = bottom ?? throw new ArgumentNullException(nameof(bottom));
        Right = right ?? throw new ArgumentNullException(nameof(right));
    }

    public static DockLayoutOptions Default { get; } = new(
        new DockRegionSizeLimits(minimum: 220, maximum: 640, initial: 340),
        new DockRegionSizeLimits(minimum: 120, maximum: 520, initial: 250),
        new DockRegionSizeLimits(minimum: 240, maximum: 680, initial: 360));

    public DockRegionSizeLimits Left { get; }

    public DockRegionSizeLimits Bottom { get; }

    public DockRegionSizeLimits Right { get; }

    public DockRegionSizeLimits For(DockRegion region) =>
        region switch
        {
            DockRegion.Left => Left,
            DockRegion.Bottom => Bottom,
            DockRegion.Right => Right,
            _ => throw new ArgumentOutOfRangeException(nameof(region), region, null)
        };
}
