using System.Numerics;
using Avalonia;
using Vector = Avalonia.Vector;

namespace Iw4Radiant.Viewports.Orthographic;

internal sealed class OrthographicProjection
{
    private Vector2 _center;
    private double _zoom = 0.8;

    internal OrthoPlane Plane { get; private set; }
    internal Size Size { get; set; }
    internal double Zoom => _zoom;

    internal void SetPlane(OrthoPlane plane)
    {
        Plane = plane;
        _center = Vector2.Zero;
    }

    internal void Reset()
    {
        _center = Vector2.Zero;
        _zoom = 0.8;
    }

    internal void Pan(Vector delta) =>
        _center += new Vector2((float)(-delta.X / _zoom), (float)(delta.Y / _zoom));

    internal void ZoomAt(Point position, double wheelDelta)
    {
        Vector2 before = ToWorld(position);
        _zoom = Math.Clamp(_zoom * Math.Exp(wheelDelta * 0.16), 0.015, 12);
        _center += before - ToWorld(position);
    }

    internal void Frame(Vector3 minimum, Vector3 maximum)
    {
        Vector2 min = Project(minimum), max = Project(maximum);
        _center = (min + max) / 2;
        _zoom = Math.Clamp(Math.Min(Math.Max(1, Size.Width - 112) / Math.Max(64, max.X - min.X),
            Math.Max(1, Size.Height - 112) / Math.Max(64, max.Y - min.Y)), 0.015, 12);
    }

    internal Vector2 Project(Vector3 point) => Plane switch
    {
        OrthoPlane.Top => new(point.X, point.Y), OrthoPlane.Front => new(point.X, point.Z), _ => new(point.Y, point.Z)
    };
    internal Vector3 Unproject(Vector2 point, float missing) => Plane switch
    {
        OrthoPlane.Top => new(point.X, point.Y, missing), OrthoPlane.Front => new(point.X, missing, point.Y), _ => new(missing, point.X, point.Y)
    };
    internal float MissingAxis(Vector3 point) => Plane switch
    {
        OrthoPlane.Top => point.Z, OrthoPlane.Front => point.Y, _ => point.X
    };
    internal Point ToScreen(Vector3 point) => ToScreen(Project(point));
    internal Point ToScreen(Vector2 point) => new(Size.Width / 2 + (point.X - _center.X) * _zoom,
        Size.Height / 2 - (point.Y - _center.Y) * _zoom);
    internal Vector2 ToWorld(Point point) => new((float)((point.X - Size.Width / 2) / _zoom) + _center.X,
        (float)((Size.Height / 2 - point.Y) / _zoom) + _center.Y);
    internal Rect ScreenBounds(Vector3 min, Vector3 max) => OrthographicGeometry.Rectangle(ToScreen(min), ToScreen(max));
}
