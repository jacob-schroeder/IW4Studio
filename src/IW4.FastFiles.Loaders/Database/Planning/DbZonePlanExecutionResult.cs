using IW4.FastFiles.Loaders.Database;

namespace IW4.FastFiles.Loaders.Database.Planning;

public sealed record DbZonePlanExecutionResult(
    IReadOnlyList<LoadedXZone> LoadedZones,
    LoadedXZone Target);
