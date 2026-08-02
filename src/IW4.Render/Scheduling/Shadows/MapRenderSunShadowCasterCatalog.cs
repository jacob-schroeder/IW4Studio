namespace IW4.Render.Scheduling.Shadows;

/// <summary>
/// Exact caster admission retained with the three-view frame revision that
/// produced it. <see cref="WorldAdmissionPath"/> makes the PS3 worker
/// branch explicit; this catalog does not silently substitute one worker's
/// gate for another.
/// </summary>
public sealed class MapRenderSunShadowCasterCatalog
{
    internal MapRenderSunShadowCasterCatalog(
        long revision,
        MapRenderSunShadowCasterPartition partition0,
        MapRenderSunShadowCasterPartition partition1)
    {
        if (revision < 0)
            throw new ArgumentOutOfRangeException(nameof(revision));
        ArgumentNullException.ThrowIfNull(partition0);
        ArgumentNullException.ThrowIfNull(partition1);
        if (partition0.PartitionIndex != 0 || partition1.PartitionIndex != 1)
        {
            throw new ArgumentException(
                "Sun-shadow caster partitions must retain native partition ownership.");
        }

        Revision = revision;
        WorldAdmissionPath = MapRenderSunShadowWorldCasterAdmissionPath
            .FastWorkerCachedCasterMask;
        Partition0 = partition0;
        Partition1 = partition1;
    }

    public long Revision { get; }

    public MapRenderSunShadowWorldCasterAdmissionPath WorldAdmissionPath
    {
        get;
    }

    public MapRenderSunShadowCasterPartition Partition0 { get; }

    public MapRenderSunShadowCasterPartition Partition1 { get; }

    public MapRenderSunShadowCasterPartition GetPartition(
        int partitionIndex) => partitionIndex switch
    {
        0 => Partition0,
        1 => Partition1,
        _ => throw new ArgumentOutOfRangeException(nameof(partitionIndex))
    };
}
