namespace IW4.Render.Scheduling.Dpvs;

/// <summary>
/// Explicit per-frame state for PS3 R_AddSkySurfacesDpvs. Disabled means the
/// native far-plane pointer was null; enabled planes are the camera frustum
/// with that far plane already removed.
/// </summary>
public sealed class MapRenderWorldDpvsCameraSkyCullInput
{
    private readonly MapRenderWorldDpvsCommandPlaneSet _planes;

    private MapRenderWorldDpvsCameraSkyCullInput(
        bool isEnabled,
        MapRenderWorldDpvsCommandPlaneSet planes)
    {
        IsEnabled = isEnabled;
        _planes = planes;
        Planes = _planes.Planes;
    }

    public bool IsEnabled { get; }

    public IReadOnlyList<MapRenderWorldDpvsClipPlane> Planes { get; }

    public static MapRenderWorldDpvsCameraSkyCullInput Disabled { get; } =
        new(
            false,
            MapRenderWorldDpvsCommandPlaneSet.TakeOwnership([]));

    public static MapRenderWorldDpvsCameraSkyCullInput Enabled(
        IReadOnlyList<MapRenderWorldDpvsClipPlane> planes)
    {
        ArgumentNullException.ThrowIfNull(planes);
        if (planes.Count > 16)
            throw new ArgumentOutOfRangeException(nameof(planes));
        return new(
            true,
            MapRenderWorldDpvsCommandPlaneSet.CopyOf(planes));
    }

    internal static MapRenderWorldDpvsCameraSkyCullInput EnabledOwned(
        MapRenderWorldDpvsCommandPlaneSet planes)
    {
        ArgumentNullException.ThrowIfNull(planes);
        if (planes.Count > 16)
            throw new ArgumentOutOfRangeException(nameof(planes));
        return new(true, planes);
    }

    internal ReadOnlySpan<MapRenderWorldDpvsClipPlane> PlaneSpan =>
        _planes.Span;
}
