namespace IW4.Studio.Desktop.Workbench.Docking;

/// <summary>
/// Immutable registration metadata for one Studio workbench tool.
/// </summary>
public sealed class DockToolDescriptor
{
    public DockToolDescriptor(
        string id,
        string title,
        string iconToken,
        int order,
        bool isImplemented,
        bool isOpenByDefault,
        DockRegion allowedRegion,
        DockRailGroup railGroup)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(iconToken);

        if (!Enum.IsDefined(allowedRegion))
            throw new ArgumentOutOfRangeException(nameof(allowedRegion), allowedRegion, null);

        if (!Enum.IsDefined(railGroup))
            throw new ArgumentOutOfRangeException(nameof(railGroup), railGroup, null);

        DockRegion railRegion = DockPlacement.RegionFor(railGroup);
        if (allowedRegion != railRegion)
        {
            throw new ArgumentException(
                $"Rail group '{railGroup}' launches tools in the '{railRegion}' region, not '{allowedRegion}'.",
                nameof(allowedRegion));
        }

        Id = id;
        Title = title;
        IconToken = iconToken;
        Order = order;
        IsImplemented = isImplemented;
        IsOpenByDefault = isOpenByDefault;
        AllowedRegion = allowedRegion;
        RailGroup = railGroup;
    }

    public string Id { get; }

    public string Title { get; }

    public string IconToken { get; }

    /// <summary>
    /// Initial order within the descriptor's constrained region.
    /// </summary>
    public int Order { get; }

    public bool IsImplemented { get; }

    public bool IsOpenByDefault { get; }

    public DockRegion AllowedRegion { get; }

    public DockRailGroup RailGroup { get; }
}
