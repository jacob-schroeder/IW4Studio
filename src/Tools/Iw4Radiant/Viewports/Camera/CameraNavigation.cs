using System.Numerics;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;
using Iw4Radiant.Rendering;

namespace Iw4Radiant.Viewports.Camera;

internal sealed class CameraNavigation
{
    private const float FieldOfView = MathF.PI / 3;
    private Vector3 _target;
    private float _yaw = -MathF.PI * 0.3f, _pitch = MathF.PI * 0.22f, _distance = 1024;

    internal void FrameAll(MapDocument document, float aspect)
    {
        var vertices = document.Brushes.SelectMany(brush => brush.GetPolygons())
            .SelectMany(polygon => polygon.Vertices)
            .Concat(document.Terrains.SelectMany(terrain => terrain.Vertices))
            .Concat(document.Entities.Where(PointEntityGeometry.IsPointEntity)
                .SelectMany(entity => PointEntityGeometry.CreateBrush(entity).GetPolygons())
                .SelectMany(polygon => polygon.Vertices));
        Frame(vertices, aspect);
    }

    internal void FrameSelection(MapDocument document, object? selection, float aspect)
    {
        if (selection is MapBrush brush)
            Frame(brush.GetPolygons().SelectMany(polygon => polygon.Vertices), aspect);
        else if (selection is MapTerrain terrain)
            Frame(terrain.Vertices, aspect);
        else if (selection is MapEntity entity)
        {
            var bounds = EditorSession.EntityBounds(entity);
            Frame([bounds.Min, bounds.Max], aspect);
        }
        else
            FrameAll(document, aspect);
    }

    private void Frame(IEnumerable<Vector3> vertices, float aspect)
    {
        Vector3 minimum = new(float.PositiveInfinity), maximum = new(float.NegativeInfinity);
        foreach (var vertex in vertices)
        {
            minimum = Vector3.Min(minimum, vertex);
            maximum = Vector3.Max(maximum, vertex);
        }
        if (!float.IsFinite(minimum.X))
        {
            _target = Vector3.Zero;
            _distance = 1024;
        }
        else
        {
            _target = (minimum + maximum) / 2;
            float halfAngle = MathF.Atan(MathF.Tan(FieldOfView / 2) * Math.Clamp(aspect, 0.1f, 1));
            _distance = Math.Clamp(Vector3.Distance(minimum, maximum) * 0.6f / MathF.Sin(halfAngle), 32, 1000000);
        }
    }

    private Vector3 Eye => _target + _distance * new Vector3(MathF.Cos(_pitch) * MathF.Cos(_yaw),
        MathF.Cos(_pitch) * MathF.Sin(_yaw), MathF.Sin(_pitch));

    internal Matrix4x4 ViewProjection(float aspect)
    {
        const float near = 0.5f;
        float far = Math.Max(131072, _distance * 4);
        float y = 1 / MathF.Tan(FieldOfView / 2);
        // System.Numerics' stock perspective uses a 0..1 depth range; OpenGL uses -1..1.
        var projection = new Matrix4x4(y / aspect, 0, 0, 0, 0, y, 0, 0,
            0, 0, -(far + near) / (far - near), -1, 0, 0, -2 * far * near / (far - near), 0);
        return Matrix4x4.CreateLookAt(Eye, _target, Vector3.UnitZ) * projection;
    }

    internal void Pan(float deltaX, float deltaY, float viewportHeight)
    {
        Vector3 forward = Vector3.Normalize(_target - Eye);
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitZ));
        Vector3 up = Vector3.Cross(right, forward);
        float scale = 2 * _distance * MathF.Tan(FieldOfView / 2) / viewportHeight;
        _target += (-right * deltaX + up * deltaY) * scale;
    }

    internal void Orbit(float deltaX, float deltaY)
    {
        _yaw -= deltaX * 0.008f;
        _pitch = Math.Clamp(_pitch + deltaY * 0.008f, -1.5f, 1.5f);
    }

    internal void Zoom(float delta) =>
        _distance = Math.Clamp(_distance * MathF.Exp(-delta * 0.14f), 4, 1000000);

    internal (Vector3 Origin, Vector3 Direction) PickRay(float x, float y, float aspect)
    {
        Vector3 origin = Eye;
        Vector3 forward = Vector3.Normalize(_target - origin);
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitZ));
        Vector3 up = Vector3.Cross(right, forward);
        float tangent = MathF.Tan(FieldOfView / 2);
        Vector3 direction = Vector3.Normalize(forward + right * x * tangent * aspect
            + up * y * tangent);
        return (origin, direction);
    }
}
