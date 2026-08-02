using IW4.Render.Scheduling.Dpvs;

namespace IW4.Render.Scheduling.Shadows;

/// <summary>
/// Same-revision publication gate for a sun-shadow frame. A renderer records
/// completion only after each partition target write is complete. AtlasReady
/// is published atomically when, and only when, both native secondary views
/// have completed for this frame.
/// </summary>
public sealed class MapRenderSunShadowFramePublication
{
    private const int Partition0Mask = 1 << 0;
    private const int Partition1Mask = 1 << 1;
    private const int CompleteMask = Partition0Mask | Partition1Mask;

    private readonly object _gate = new();
    private int _completedPartitionMask;
    private MapRenderSunShadowAtlasReadyState? _atlasReady;

    internal MapRenderSunShadowFramePublication(
        MapRenderWorldDpvsThreeViewFrame frame)
    {
        Frame = frame ?? throw new ArgumentNullException(nameof(frame));
    }

    public MapRenderWorldDpvsThreeViewFrame Frame { get; }

    public long Revision => Frame.Revision;

    public bool RecordPartitionDrawCompleted(
        long revision,
        MapRenderWorldDpvsViewIndex partition)
    {
        if (revision != Revision)
        {
            throw new InvalidOperationException(
                $"Partition completion revision {revision} does not match sun-shadow frame {Revision}.");
        }

        int mask = partition switch
        {
            MapRenderWorldDpvsViewIndex.SunShadowPartition0 => Partition0Mask,
            MapRenderWorldDpvsViewIndex.SunShadowPartition1 => Partition1Mask,
            _ => throw new ArgumentOutOfRangeException(
                nameof(partition),
                "Only native DPVS views 1 and 2 write the sun-shadow atlas.")
        };

        lock (_gate)
        {
            if ((_completedPartitionMask & mask) != 0)
                return false;

            _completedPartitionMask |= mask;
            if (_completedPartitionMask == CompleteMask)
                _atlasReady = new MapRenderSunShadowAtlasReadyState(Frame);
            return true;
        }
    }

    public bool TryGetAtlasReady(
        out MapRenderSunShadowAtlasReadyState? atlasReady)
    {
        lock (_gate)
        {
            atlasReady = _atlasReady;
            return atlasReady is not null;
        }
    }
}
