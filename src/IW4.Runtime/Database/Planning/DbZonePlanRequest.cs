using IW4.FastFiles.Zone;

namespace IW4.Runtime.Database.Planning;

public sealed record DbZonePlanRequest(
    XZoneInfo ZoneInfo,
    string? FastFilePath,
    bool FileExists,
    bool MissingIsNonFatal,
    DbZonePlanPosition Position)
{
    public bool IsLoad => ZoneInfo.Name is not null && ZoneInfo.AllocFlags != XZoneFlags.None;

    public bool IsFreeOnly => ZoneInfo.Name is null && ZoneInfo.FreeFlags != XZoneFlags.None;

    public bool IsTarget => Position == DbZonePlanPosition.Target;
}
