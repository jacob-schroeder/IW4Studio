namespace IW4.Studio.Desktop.Workbench.Docking;

public enum DockActivationResult
{
    Opened,
    Switched,
    Collapsed,
    ToolNotFound,
    ToolNotImplemented
}

public enum DockMoveResult
{
    Moved,
    Unchanged,
    ToolNotFound,
    RegionNotAllowed,
    TargetIndexOutOfRange
}

/// <summary>
/// Owns all mutations of a constrained Studio workbench layout.
/// </summary>
public sealed class DockLayoutController
{
    public DockLayoutController(
        IEnumerable<DockToolDescriptor> descriptors,
        DockLayoutOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        options ??= DockLayoutOptions.Default;

        DockToolDescriptor[] registrations = descriptors.ToArray();
        EnsureUniqueIds(registrations);

        var toolsById = new Dictionary<string, DockToolState>(StringComparer.Ordinal);
        DockRegionState CreateRegion(DockRegion region)
        {
            DockToolState[] tools = registrations
                .Select((descriptor, registrationIndex) => (descriptor, registrationIndex))
                .Where(item => item.descriptor.AllowedRegion == region)
                .OrderBy(item => item.descriptor.Order)
                .ThenBy(item => item.registrationIndex)
                .Select((item, position) => new DockToolState(item.descriptor, position))
                .ToArray();

            foreach (DockToolState tool in tools)
                toolsById.Add(tool.Id, tool);

            return new DockRegionState(region, options.For(region), tools);
        }

        DockRegionState left = CreateRegion(DockRegion.Left);
        DockRegionState bottom = CreateRegion(DockRegion.Bottom);
        DockRegionState right = CreateRegion(DockRegion.Right);
        State = new DockLayoutState(left, bottom, right, toolsById);

        foreach (DockRegionState region in State.Regions)
        {
            DockToolState? defaultTool = region.Tools.FirstOrDefault(
                tool => tool.Descriptor.IsOpenByDefault && tool.IsImplemented);
            region.SetActiveTool(defaultTool);
        }
    }

    public DockLayoutState State { get; }

    public DockActivationResult ActivateTool(string toolId)
    {
        ArgumentNullException.ThrowIfNull(toolId);

        if (!State.TryGetTool(toolId, out DockToolState? tool) || tool is null)
            return DockActivationResult.ToolNotFound;

        if (!tool.IsImplemented)
            return DockActivationResult.ToolNotImplemented;

        DockRegionState region = State.Region(tool.AllowedRegion);
        if (ReferenceEquals(region.ActiveTool, tool))
        {
            region.SetActiveTool(null);
            return DockActivationResult.Collapsed;
        }

        bool wasOpen = region.IsOpen;
        region.SetActiveTool(tool);
        return wasOpen
            ? DockActivationResult.Switched
            : DockActivationResult.Opened;
    }

    public bool CollapseRegion(DockRegion region)
    {
        DockRegionState regionState = State.Region(region);
        if (!regionState.IsOpen)
            return false;

        regionState.SetActiveTool(null);
        return true;
    }

    public double ResizeRegion(DockRegion region, double requestedSize) =>
        State.Region(region).Resize(requestedSize);

    public DockMoveResult MoveTool(string toolId, DockRegion targetRegion, int targetIndex)
    {
        ArgumentNullException.ThrowIfNull(toolId);

        if (!Enum.IsDefined(targetRegion))
            throw new ArgumentOutOfRangeException(nameof(targetRegion), targetRegion, null);

        if (!State.TryGetTool(toolId, out DockToolState? tool) || tool is null)
            return DockMoveResult.ToolNotFound;

        if (tool.AllowedRegion != targetRegion)
            return DockMoveResult.RegionNotAllowed;

        DockRegionState region = State.Region(targetRegion);
        if (targetIndex < 0 || targetIndex >= region.Tools.Count)
            return DockMoveResult.TargetIndexOutOfRange;

        int currentIndex = tool.Position;
        if (currentIndex == targetIndex)
            return DockMoveResult.Unchanged;

        region.Move(currentIndex, targetIndex);
        return DockMoveResult.Moved;
    }

    private static void EnsureUniqueIds(IEnumerable<DockToolDescriptor> descriptors)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (DockToolDescriptor descriptor in descriptors)
        {
            if (descriptor is null)
                throw new ArgumentException("Tool descriptors cannot contain null entries.", nameof(descriptors));

            if (!ids.Add(descriptor.Id))
                throw new ArgumentException($"A tool with the ID '{descriptor.Id}' is already registered.", nameof(descriptors));
        }
    }
}
