namespace IW4.Render.Scheduling.Shadows;

/// <summary>
/// Immutable caster admission for one of the two sun-shadow DPVS views.
/// </summary>
public sealed class MapRenderSunShadowCasterPartition
{
    private readonly int[] _worldSurfaceIndices;
    private readonly MapRenderSunShadowStaticCasterIdentity[]
        _staticDrawInstances;

    internal MapRenderSunShadowCasterPartition(
        int partitionIndex,
        IReadOnlyList<int> worldSurfaceIndices,
        IReadOnlyList<MapRenderSunShadowStaticCasterIdentity>
            staticDrawInstances)
        : this(
            partitionIndex,
            new ReadOnlySpan<int>(
                worldSurfaceIndices?.ToArray() ??
                throw new ArgumentNullException(
                    nameof(worldSurfaceIndices))),
            new ReadOnlySpan<MapRenderSunShadowStaticCasterIdentity>(
                staticDrawInstances?.ToArray() ??
                throw new ArgumentNullException(
                    nameof(staticDrawInstances))))
    {
    }

    internal MapRenderSunShadowCasterPartition(
        int partitionIndex,
        ReadOnlySpan<int> worldSurfaceIndices,
        ReadOnlySpan<MapRenderSunShadowStaticCasterIdentity>
            staticDrawInstances)
    {
        if ((uint)partitionIndex >= 2u)
            throw new ArgumentOutOfRangeException(nameof(partitionIndex));

        PartitionIndex = partitionIndex;
        _worldSurfaceIndices = worldSurfaceIndices.ToArray();
        _staticDrawInstances = staticDrawInstances.ToArray();
        WorldSurfaceIndices = Array.AsReadOnly(_worldSurfaceIndices);
        StaticDrawInstances = Array.AsReadOnly(_staticDrawInstances);
    }

    public int PartitionIndex { get; }

    public IReadOnlyList<int> WorldSurfaceIndices { get; }

    public IReadOnlyList<MapRenderSunShadowStaticCasterIdentity>
        StaticDrawInstances { get; }
}
