namespace IW4.Runtime.Database;

public sealed record DbZoneRetirementResult(
    IReadOnlyList<DbLoadedXZone> RetiredZones);
