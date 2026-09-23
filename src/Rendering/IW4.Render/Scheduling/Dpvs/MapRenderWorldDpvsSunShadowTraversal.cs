namespace IW4.Render.Scheduling.Dpvs;

/// <summary>
/// Both secondary command sets in native partition order.
/// </summary>
public sealed class MapRenderWorldDpvsSunShadowTraversal
{
    internal MapRenderWorldDpvsSunShadowTraversal(
        string producerIdentity,
        long sourceRevision,
        MapRenderWorldDpvsViewCommandSet partition0Commands,
        MapRenderWorldDpvsViewCommandSet partition1Commands)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(producerIdentity);
        if (sourceRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceRevision));
        ArgumentNullException.ThrowIfNull(partition0Commands);
        ArgumentNullException.ThrowIfNull(partition1Commands);
        if (partition0Commands.ViewIndex !=
                MapRenderWorldDpvsViewIndex.SunShadowPartition0 ||
            partition0Commands.Origin !=
                MapRenderWorldDpvsCommandOrigin.SunShadowFrustumTraversal)
        {
            throw new ArgumentException(
                "Partition zero commands do not own native DPVS view index 1.",
                nameof(partition0Commands));
        }
        if (partition1Commands.ViewIndex !=
                MapRenderWorldDpvsViewIndex.SunShadowPartition1 ||
            partition1Commands.Origin !=
                MapRenderWorldDpvsCommandOrigin.SunShadowFrustumTraversal)
        {
            throw new ArgumentException(
                "Partition one commands do not own native DPVS view index 2.",
                nameof(partition1Commands));
        }

        ProducerIdentity = producerIdentity;
        SourceRevision = sourceRevision;
        Partition0Commands = partition0Commands;
        Partition1Commands = partition1Commands;
    }

    public string ProducerIdentity { get; }

    public long SourceRevision { get; }

    public MapRenderWorldDpvsViewCommandSet Partition0Commands { get; }

    public MapRenderWorldDpvsViewCommandSet Partition1Commands { get; }
}
