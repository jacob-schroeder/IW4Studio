namespace IW4.Studio.Desktop.Workbench.Docking;

/// <summary>
/// Typed, in-process drag data for one rail tool. The allowed region is
/// duplicated from the descriptor so stale or malformed drag data can be
/// rejected before it mutates the layout.
/// </summary>
public sealed class DockRailDragPayload
{
    public DockRailDragPayload(string toolId, DockRegion allowedRegion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolId);

        if (!Enum.IsDefined(allowedRegion))
            throw new ArgumentOutOfRangeException(nameof(allowedRegion), allowedRegion, null);

        ToolId = toolId;
        AllowedRegion = allowedRegion;
    }

    public string ToolId { get; }

    public DockRegion AllowedRegion { get; }
}
