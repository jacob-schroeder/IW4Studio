namespace IW4.Render.Scheduling.Dpvs;

/// <summary>
/// Mutable local equivalent of PS3 GfxSunShadowClip. Mutation is confined to
/// one producer call; published partition records remain immutable.
/// </summary>
internal sealed class MapRenderWorldDpvsSunShadowFullClipBuilder
{
    private readonly List<MapRenderWorldDpvsClipPlane>[] _planes =
        [[], []];
    private readonly int[] _frustumPlaneCounts = [-1, -1];

    public IReadOnlyList<MapRenderWorldDpvsClipPlane> Planes(
        int partitionIndex) => _planes[ValidatePartition(partitionIndex)];

    public void Append(
        int partitionIndex,
        MapRenderWorldDpvsClipPlane plane)
    {
        List<MapRenderWorldDpvsClipPlane> planes =
            _planes[ValidatePartition(partitionIndex)];
        if (planes.Count ==
            MapRenderWorldDpvsSunShadowPartitionClipSet.MaximumPlaneCount)
        {
            throw new InvalidOperationException(
                "PS3 GfxSunShadowClip owns only ten planes per partition.");
        }
        planes.Add(plane);
    }

    public void SnapshotFrustumPlaneCounts()
    {
        _frustumPlaneCounts[0] = _planes[0].Count;
        _frustumPlaneCounts[1] = _planes[1].Count;
    }

    public MapRenderWorldDpvsSunShadowFrame ToFrame(
        string producerIdentity,
        long sourceRevision,
        MapRenderWorldDpvsSunShadowFullProjectionState projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        if (_frustumPlaneCounts[0] < 0 || _frustumPlaneCounts[1] < 0)
        {
            throw new InvalidOperationException(
                "Native frustumPlaneCount must be captured before publication.");
        }

        return new(
            producerIdentity,
            sourceRevision,
            new(
                MapRenderWorldDpvsViewIndex.SunShadowPartition0,
                _planes[0],
                _frustumPlaneCounts[0]),
            new(
                MapRenderWorldDpvsViewIndex.SunShadowPartition1,
                _planes[1],
                _frustumPlaneCounts[1]),
            projection);
    }

    private static int ValidatePartition(int partitionIndex)
    {
        if ((uint)partitionIndex > 1u)
            throw new ArgumentOutOfRangeException(nameof(partitionIndex));
        return partitionIndex;
    }
}
