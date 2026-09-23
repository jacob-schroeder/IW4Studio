using IW4.Loaders.Database;

namespace IW4.Loaders.Database.Planning;

public sealed record DbZonePlanExecutionResult(
    IReadOnlyList<LoadedXZone> LoadedZones,
    LoadedXZone Target);
