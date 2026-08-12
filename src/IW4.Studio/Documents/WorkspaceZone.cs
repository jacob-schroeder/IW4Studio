using IW4.FastFiles.Loaders.Database;

namespace IW4.Studio.Documents;

/// <summary>Immutable Studio view of one successfully loaded fastfile zone.</summary>
public sealed record WorkspaceZone
{
    internal WorkspaceZone(
        LoadedXZone loadResult,
        string physicalPath,
        bool isTarget,
        bool isActive)
    {
        LoadResult = loadResult ?? throw new ArgumentNullException(nameof(loadResult));
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalPath);
        PhysicalPath = Path.GetFullPath(physicalPath);
        LogicalZoneName = loadResult.Zone.Name;
        IsTarget = isTarget;
        IsActive = isActive;
    }

    public LoadedXZone LoadResult { get; }
    public string PhysicalPath { get; }
    public string LogicalZoneName { get; }
    public bool IsTarget { get; }
    public bool IsActive { get; }
}
