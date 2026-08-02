namespace IW4.Render.Scheduling.Dpvs;

/// <summary>
/// One partition of the PS3 GfxSunShadowClip planeCount, frustumPlaneCount, and
/// planes[10] storage installed for a secondary DPVS view.
/// </summary>
public sealed class MapRenderWorldDpvsSunShadowPartitionClipSet
{
    public const int MaximumPlaneCount = 10;

    private readonly MapRenderWorldDpvsCommandPlaneSet _planes;
    private readonly MapRenderWorldDpvsCommandPlaneSet _frustumPlanes;

    public MapRenderWorldDpvsSunShadowPartitionClipSet(
        MapRenderWorldDpvsViewIndex viewIndex,
        IReadOnlyList<MapRenderWorldDpvsClipPlane> planes,
        int frustumPlaneCount)
    {
        if (viewIndex is not (
                MapRenderWorldDpvsViewIndex.SunShadowPartition0 or
                MapRenderWorldDpvsViewIndex.SunShadowPartition1))
        {
            throw new ArgumentOutOfRangeException(nameof(viewIndex));
        }
        ArgumentNullException.ThrowIfNull(planes);
        if (planes.Count > MaximumPlaneCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(planes),
                "PS3 R_SetupSunShadowMaps owns exactly ten local plane slots per partition.");
        }
        if ((uint)frustumPlaneCount > (uint)planes.Count)
            throw new ArgumentOutOfRangeException(nameof(frustumPlaneCount));

        ViewIndex = viewIndex;
        FrustumPlaneCount = frustumPlaneCount;
        _planes = MapRenderWorldDpvsCommandPlaneSet.CopyOf(planes);
        _frustumPlanes = _planes.CopyPrefix(frustumPlaneCount);
        Planes = _planes.Planes;
    }

    public MapRenderWorldDpvsViewIndex ViewIndex { get; }

    public IReadOnlyList<MapRenderWorldDpvsClipPlane> Planes { get; }

    /// <summary>
    /// GfxSunShadowClip planeCount is deliberately independent from
    /// FrustumPlaneCount: traversal consumes only the frustum prefix while the
    /// publication record retains all PlaneCount rows.
    /// </summary>
    public int PlaneCount => _planes.Count;

    public int FrustumPlaneCount { get; }

    internal ReadOnlySpan<MapRenderWorldDpvsClipPlane> FrustumPlaneSpan =>
        _frustumPlanes.Span;

    internal MapRenderWorldDpvsCommandPlaneSet FrustumCommandPlaneSet =>
        _frustumPlanes;
}
