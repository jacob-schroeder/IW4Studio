namespace IW4.Render.Scheduling.Dpvs;

/// <summary>
/// Atomic normal-camera DPVS snapshot. View 0 supplies camera inclusion while
/// views 1 and 2 independently supply Event 0x0E page-zero membership. The
/// projection constants were produced by the same source revision.
/// </summary>
public sealed class MapRenderWorldDpvsThreeViewFrame
{
    internal MapRenderWorldDpvsThreeViewFrame(
        long revision,
        MapRenderWorldDpvsViewVisibility camera,
        MapRenderWorldDpvsViewVisibility sunShadowPartition0,
        MapRenderWorldDpvsViewVisibility sunShadowPartition1,
        MapRenderWorldDpvsSunShadowFullProjectionState projection)
    {
        if (revision < 0)
            throw new ArgumentOutOfRangeException(nameof(revision));
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(sunShadowPartition0);
        ArgumentNullException.ThrowIfNull(sunShadowPartition1);
        ArgumentNullException.ThrowIfNull(projection);
        ValidateRole(camera, MapRenderWorldDpvsViewIndex.Camera);
        ValidateRole(
            sunShadowPartition0,
            MapRenderWorldDpvsViewIndex.SunShadowPartition0);
        ValidateRole(
            sunShadowPartition1,
            MapRenderWorldDpvsViewIndex.SunShadowPartition1);
        ValidateCardinality(camera, sunShadowPartition0);
        ValidateCardinality(camera, sunShadowPartition1);

        Revision = revision;
        Camera = camera;
        SunShadowPartition0 = sunShadowPartition0;
        SunShadowPartition1 = sunShadowPartition1;
        Projection = projection;
        WorldSurfaces = new MapRenderWorldSurfaceVisibilityState(
            camera,
            sunShadowPartition0,
            sunShadowPartition1);
        StaticModelReceivers =
            new MapRenderStaticModelReceiverVisibilityState(
                revision,
                camera,
                sunShadowPartition0,
                sunShadowPartition1);
    }

    public long Revision { get; }

    public MapRenderWorldDpvsViewVisibility Camera { get; }

    public MapRenderWorldDpvsViewVisibility SunShadowPartition0 { get; }

    public MapRenderWorldDpvsViewVisibility SunShadowPartition1 { get; }

    /// <summary>
    /// Exact projection object atomically published with these three DPVS
    /// views under <see cref="Revision"/>.
    /// </summary>
    public MapRenderWorldDpvsSunShadowFullProjectionState Projection { get; }

    public MapRenderWorldDpvsSunShadowProjectionCodeConstants
        ProjectionCodeConstants => Projection.CodeConstants;

    public MapRenderWorldSurfaceVisibilityState WorldSurfaces { get; }

    /// <summary>
    /// Opaque static receiver classification sourced from the same three-view
    /// revision as <see cref="WorldSurfaces"/> and <see cref="Projection"/>.
    /// </summary>
    public MapRenderStaticModelReceiverVisibilityState StaticModelReceivers
        { get; }

    public MapRenderWorldDpvsViewVisibility GetView(
        MapRenderWorldDpvsViewIndex viewIndex) => viewIndex switch
    {
        MapRenderWorldDpvsViewIndex.Camera => Camera,
        MapRenderWorldDpvsViewIndex.SunShadowPartition0 =>
            SunShadowPartition0,
        MapRenderWorldDpvsViewIndex.SunShadowPartition1 =>
            SunShadowPartition1,
        _ => throw new ArgumentOutOfRangeException(nameof(viewIndex))
    };

    private static void ValidateRole(
        MapRenderWorldDpvsViewVisibility view,
        MapRenderWorldDpvsViewIndex expected)
    {
        if (view.ViewIndex != expected)
        {
            throw new ArgumentException(
                $"Expected DPVS view {expected}, received {view.ViewIndex}.");
        }
    }

    private static void ValidateCardinality(
        MapRenderWorldDpvsViewVisibility camera,
        MapRenderWorldDpvsViewVisibility secondary)
    {
        if (camera.SurfaceCount != secondary.SurfaceCount ||
            camera.StaticModelCount != secondary.StaticModelCount)
        {
            throw new ArgumentException(
                "All three DPVS views must describe identical world cardinalities.");
        }
    }
}
