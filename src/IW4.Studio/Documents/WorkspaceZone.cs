using IW4.FastFiles.Loaders.Database;
using IW4.Runtime.Database;

namespace IW4.Studio.Documents;

/// <summary>
/// Immutable Studio view of one loaded zone.
/// </summary>
public sealed record WorkspaceZone
{
    internal WorkspaceZone(
        LoadedXZone loadResult,
        string physicalPath,
        bool isTarget,
        bool isActive)
    {
        ArgumentNullException.ThrowIfNull(loadResult);
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalPath);

        LoadResult = loadResult;
        PhysicalPath = Path.GetFullPath(physicalPath);
        LogicalZoneName = loadResult.Zone.Name;
        RuntimeZoneHandle = loadResult.Context.ZoneOwner;
        IsTarget = isTarget;
        IsActive = isActive;
    }

    public LoadedXZone LoadResult { get; }

    public string PhysicalPath { get; }

    public string LogicalZoneName { get; }

    public DbZoneHandle RuntimeZoneHandle { get; }

    public bool IsTarget { get; }

    public bool IsActive { get; }
}
