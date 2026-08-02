using IW4.FastFiles.Zone;

namespace IW4.Runtime.Database;

/// <summary>
/// Managed registry bookkeeping that associates an engine-shaped XZone with
/// the loader services and contributions materialized into its memory.
/// </summary>
public sealed record DbLoadedXZone(
    DbZoneHandle Handle,
    XZone Zone,
    IDbZoneLoadRuntimeContext Context,
    DbZoneContributions Contributions);
