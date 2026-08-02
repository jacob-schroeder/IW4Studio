using IW4.Runtime.Database;

namespace IW4.Runtime.Assets;

/// <summary>
/// Immutable preview of the canonical-slot changes caused by retiring one
/// zone's provider contributions. Planning does not mutate slot topology.
/// </summary>
public sealed record XAssetProviderRetirementPlan(
    DbZoneHandle Owner,
    long PoolRevision,
    IReadOnlyList<XAssetSlotChange> Changes);
