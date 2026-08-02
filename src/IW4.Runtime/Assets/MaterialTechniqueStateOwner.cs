namespace IW4.Runtime.Assets;

/// <summary>
/// Immutable collision/equality witness retained by the process-global cache.
/// It deliberately does not retain the zone-owned Material provider object,
/// so fallback promotion or zone retirement cannot leave a dangling owner.
/// </summary>
internal sealed record MaterialTechniqueStateOwner(
    IReadOnlyList<MaterialTechniqueStateDescription> Descriptions);
