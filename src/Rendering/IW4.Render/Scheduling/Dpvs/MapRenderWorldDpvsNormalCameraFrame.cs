using System.Numerics;

namespace IW4.Render.Scheduling.Dpvs;

/// <summary>
/// Front-end camera state consumed by PS3 world DPVS setup and portal
/// traversal. Coordinates and planes are in native game space.
/// </summary>
public sealed class MapRenderWorldDpvsNormalCameraFrame
{
    private readonly MapRenderWorldDpvsCommandPlaneSet _frustumPlanes;

    internal MapRenderWorldDpvsNormalCameraFrame(
        Vector3 origin,
        Vector3 forward,
        Matrix4x4 viewProjection,
        Matrix4x4 inverseViewProjection,
        MapRenderWorldDpvsClipPlane viewPlane,
        MapRenderWorldDpvsClipPlane? farPlane,
        IReadOnlyList<MapRenderWorldDpvsClipPlane> frustumPlanes)
    {
        Origin = origin;
        Forward = forward;
        ViewProjection = viewProjection;
        InverseViewProjection = inverseViewProjection;
        ViewPlane = viewPlane;
        FarPlane = farPlane;
        _frustumPlanes =
            MapRenderWorldDpvsCommandPlaneSet.CopyOf(frustumPlanes);
        FrustumPlanes = _frustumPlanes.Planes;
        SkyCullInput = farPlane is null
            ? MapRenderWorldDpvsCameraSkyCullInput.Disabled
            : MapRenderWorldDpvsCameraSkyCullInput.EnabledOwned(
                _frustumPlanes.CopyPrefix(_frustumPlanes.Count - 1));
    }

    public Vector3 Origin { get; }

    public Vector3 Forward { get; }

    public Matrix4x4 ViewProjection { get; }

    public Matrix4x4 InverseViewProjection { get; }

    public MapRenderWorldDpvsClipPlane ViewPlane { get; }

    public MapRenderWorldDpvsClipPlane NearPlane => ViewPlane;

    public MapRenderWorldDpvsClipPlane? FarPlane { get; }

    public IReadOnlyList<MapRenderWorldDpvsClipPlane> FrustumPlanes { get; }

    public MapRenderWorldDpvsCameraSkyCullInput SkyCullInput { get; }

    internal ReadOnlySpan<MapRenderWorldDpvsClipPlane> FrustumPlaneSpan =>
        _frustumPlanes.Span;

    internal MapRenderWorldDpvsCommandPlaneSet CommandPlaneSet =>
        _frustumPlanes;
}
