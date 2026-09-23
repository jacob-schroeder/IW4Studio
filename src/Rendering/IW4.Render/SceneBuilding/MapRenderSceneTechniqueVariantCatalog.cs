using IW4.Render.Scheduling;

namespace IW4.Render.SceneBuilding;

[Flags]
internal enum MapRenderWorldReceiverVariantRequirement : byte
{
    None = 0,
    PageZeroUnshadowed = 1 << 0,
    PageZeroShadowMapAllocated = 1 << 1,
    PageOneUnshadowed = 1 << 2,
    PageOneShadowMapAllocated = 1 << 3,
    All = PageZeroUnshadowed |
          PageZeroShadowMapAllocated |
          PageOneUnshadowed |
          PageOneShadowMapAllocated
}

/// <summary>
/// Scene-owned immutable slot catalog. Entries are indexed by GfxSurface or
/// GfxStaticModelDrawInst so a renderer can resolve one exact prepared variant
/// without rescanning ComWorld light metadata.
/// </summary>
public sealed class MapRenderSceneTechniqueVariantCatalog
{
    private readonly MapRenderTechniqueVariantSet?[] _worldSurfaces;
    private readonly MapRenderTechniqueVariantSet?[] _staticModelDrawInstances;
    private readonly MapRenderWorldReceiverVariantRequirement[]
        _worldReceiverRequirements;

    internal MapRenderSceneTechniqueVariantCatalog(
        MapRenderDrawMethod drawMethod,
        IReadOnlyList<MapRenderTechniqueVariantSet?> worldSurfaces,
        IReadOnlyList<MapRenderTechniqueVariantSet?> staticModelDrawInstances,
        IReadOnlyList<MapRenderWorldReceiverVariantRequirement>?
            worldReceiverRequirements = null)
    {
        DrawMethod = drawMethod ??
            throw new ArgumentNullException(nameof(drawMethod));
        ArgumentNullException.ThrowIfNull(worldSurfaces);
        ArgumentNullException.ThrowIfNull(staticModelDrawInstances);
        if (worldReceiverRequirements is not null &&
            worldReceiverRequirements.Count != worldSurfaces.Count)
        {
            throw new ArgumentException(
                "World receiver requirements must remain index-parallel with world surfaces.",
                nameof(worldReceiverRequirements));
        }

        _worldSurfaces = worldSurfaces.ToArray();
        _staticModelDrawInstances = staticModelDrawInstances.ToArray();
        _worldReceiverRequirements = Enumerable.Range(
                0,
                _worldSurfaces.Length)
            .Select(index =>
                _worldSurfaces[index] is null
                    ? MapRenderWorldReceiverVariantRequirement.None
                    : worldReceiverRequirements?[index] ??
                      MapRenderWorldReceiverVariantRequirement.All)
            .ToArray();
        WorldSurfaces = Array.AsReadOnly(_worldSurfaces);
        StaticModelDrawInstances =
            Array.AsReadOnly(_staticModelDrawInstances);
    }

    public MapRenderDrawMethod DrawMethod { get; }

    public IReadOnlyList<MapRenderTechniqueVariantSet?> WorldSurfaces
        { get; }

    public IReadOnlyList<MapRenderTechniqueVariantSet?> StaticModelDrawInstances
        { get; }

    /// <summary>
    /// Returns whether the exact native camera-color receiver phase exists (or
    /// could not be resolved and must therefore fail closed) for this surface
    /// axis. A false result is authoritative phase absence, such as a surface
    /// owned by GfxSky or a material with no technique in the selected slot.
    /// </summary>
    public bool RequiresWorldReceiverVariant(
        int surfaceIndex,
        MapRenderWorldSurfacePageMembership page,
        MapRenderTechniqueVariantAllocation allocation)
    {
        if ((uint)surfaceIndex >= (uint)_worldReceiverRequirements.Length)
            throw new ArgumentOutOfRangeException(nameof(surfaceIndex));

        MapRenderWorldReceiverVariantRequirement requirement = page switch
        {
            MapRenderWorldSurfacePageMembership.PageZero => allocation switch
            {
                MapRenderTechniqueVariantAllocation.Unshadowed =>
                    MapRenderWorldReceiverVariantRequirement
                        .PageZeroUnshadowed,
                MapRenderTechniqueVariantAllocation.ShadowMapAllocated =>
                    MapRenderWorldReceiverVariantRequirement
                        .PageZeroShadowMapAllocated,
                _ => throw new ArgumentOutOfRangeException(nameof(allocation))
            },
            MapRenderWorldSurfacePageMembership.PageOne => allocation switch
            {
                MapRenderTechniqueVariantAllocation.Unshadowed =>
                    MapRenderWorldReceiverVariantRequirement
                        .PageOneUnshadowed,
                MapRenderTechniqueVariantAllocation.ShadowMapAllocated =>
                    MapRenderWorldReceiverVariantRequirement
                        .PageOneShadowMapAllocated,
                _ => throw new ArgumentOutOfRangeException(nameof(allocation))
            },
            _ => throw new ArgumentOutOfRangeException(nameof(page))
        };
        return (_worldReceiverRequirements[surfaceIndex] & requirement) != 0;
    }

    internal MapRenderSceneTechniqueVariantCatalog
        WithWorldReceiverRequirements(
            IReadOnlyList<MapRenderWorldReceiverVariantRequirement>
                worldReceiverRequirements)
    {
        ArgumentNullException.ThrowIfNull(worldReceiverRequirements);
        return new(
            DrawMethod,
            _worldSurfaces,
            _staticModelDrawInstances,
            worldReceiverRequirements);
    }
}
