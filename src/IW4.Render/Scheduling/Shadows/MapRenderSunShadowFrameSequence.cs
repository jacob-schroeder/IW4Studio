using IW4.Render.Scheduling.Dpvs;

namespace IW4.Render.Scheduling.Shadows;

/// <summary>
/// Owns the strictly increasing revision boundary for operational sun-shadow
/// frames. It prevents a completed atlas or selector allocation from being
/// paired with older DPVS/projection state.
/// </summary>
public sealed class MapRenderSunShadowFrameSequence
{
    private readonly object _gate = new();
    private long _latestRevision = -1;

    public long LatestRevision
    {
        get
        {
            lock (_gate)
                return _latestRevision;
        }
    }

    public MapRenderSunShadowFramePublication BeginFrame(
        long revision,
        MapRenderWorldDpvsViewVisibility camera,
        MapRenderWorldDpvsViewVisibility sunShadowPartition0,
        MapRenderWorldDpvsViewVisibility sunShadowPartition1,
        MapRenderWorldDpvsSunShadowFullProjectionState projection)
    {
        lock (_gate)
        {
            if (revision <= _latestRevision)
            {
                throw new InvalidOperationException(
                    $"Sun-shadow frame revision {revision} must be newer than {_latestRevision}.");
            }

            var frame = new MapRenderWorldDpvsThreeViewFrame(
                revision,
                camera,
                sunShadowPartition0,
                sunShadowPartition1,
                projection);
            _latestRevision = revision;
            return new MapRenderSunShadowFramePublication(frame);
        }
    }

    /// <summary>
    /// Publishes one successful full normal-camera visibility result without
    /// allowing its view rows or projection constants to be reassembled by a
    /// caller. The supplied revision remains the renderer-owned monotonic
    /// frame identity; provider SourceRevision is asset/setup provenance.
    /// </summary>
    public MapRenderSunShadowFramePublication BeginFrame(
        long revision,
        MapRenderWorldDpvsVisibilityBuildResult visibility)
    {
        ArgumentNullException.ThrowIfNull(visibility);
        if (!visibility.IsSuccess ||
            visibility.SunShadowProjection is null)
        {
            throw new ArgumentException(
                "A sun-shadow frame requires a successful three-view result with projection rows 0x1E/0x1F.",
                nameof(visibility));
        }

        MapRenderWorldDpvsViewVisibility camera = GetView(
            visibility,
            MapRenderWorldDpvsViewIndex.Camera);
        MapRenderWorldDpvsViewVisibility partition0 = GetView(
            visibility,
            MapRenderWorldDpvsViewIndex.SunShadowPartition0);
        MapRenderWorldDpvsViewVisibility partition1 = GetView(
            visibility,
            MapRenderWorldDpvsViewIndex.SunShadowPartition1);
        return BeginFrame(
            revision,
            camera,
            partition0,
            partition1,
            visibility.SunShadowProjection);
    }

    private static MapRenderWorldDpvsViewVisibility GetView(
        MapRenderWorldDpvsVisibilityBuildResult visibility,
        MapRenderWorldDpvsViewIndex viewIndex) =>
        visibility.CompletedViews.Single(view =>
            view.ViewIndex == viewIndex);
}
