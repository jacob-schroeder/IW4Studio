namespace IW4.Render.Scheduling.Dpvs;

/// <summary>
/// Immutable operational secondary-view state produced for one source
/// revision. The native numeric ownership is view 1 followed by view 2;
/// sun-shadow partition names follow IW3/IW4 terminology.
/// </summary>
public sealed class MapRenderWorldDpvsSunShadowFrame
{
    public MapRenderWorldDpvsSunShadowFrame(
        string producerIdentity,
        long sourceRevision,
        MapRenderWorldDpvsSunShadowPartitionClipSet partition0,
        MapRenderWorldDpvsSunShadowPartitionClipSet partition1,
        MapRenderWorldDpvsSunShadowFullProjectionState projection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(producerIdentity);
        if (sourceRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceRevision));
        ArgumentNullException.ThrowIfNull(partition0);
        ArgumentNullException.ThrowIfNull(partition1);
        ArgumentNullException.ThrowIfNull(projection);
        if (partition0.ViewIndex !=
            MapRenderWorldDpvsViewIndex.SunShadowPartition0)
        {
            throw new ArgumentException(
                "Partition zero must own native DPVS view index 1.",
                nameof(partition0));
        }
        if (partition1.ViewIndex !=
            MapRenderWorldDpvsViewIndex.SunShadowPartition1)
        {
            throw new ArgumentException(
                "Partition one must own native DPVS view index 2.",
                nameof(partition1));
        }

        ProducerIdentity = producerIdentity;
        SourceRevision = sourceRevision;
        Partition0 = partition0;
        Partition1 = partition1;
        Projection = projection;
    }

    public string ProducerIdentity { get; }

    public long SourceRevision { get; }

    public MapRenderWorldDpvsSunShadowPartitionClipSet Partition0 { get; }

    public MapRenderWorldDpvsSunShadowPartitionClipSet Partition1 { get; }

    /// <summary>
    /// Exact immutable projection payload produced by the same operation as
    /// the two partition clip sets.
    /// </summary>
    public MapRenderWorldDpvsSunShadowFullProjectionState Projection { get; }

    /// <summary>Projection-owned PS3 direct rows 0x1E/0x1F.</summary>
    public MapRenderWorldDpvsSunShadowProjectionCodeConstants
        ProjectionCodeConstants => Projection.CodeConstants;
}
