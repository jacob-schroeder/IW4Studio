using System.Numerics;

namespace Iw4Radiant.Viewports.Camera;

internal sealed class CameraNavigation
{
    private const float FieldOfView = MathF.PI / 3;
    private const float MinimumOrbitDistance = 0.05f;
    private Vector3 _target;
    private float _yaw = -MathF.PI * 0.3f, _pitch = MathF.PI * 0.22f, _distance = 1024;

    internal void FrameBounds((Vector3 Min, Vector3 Max)? bounds, float aspect)
    {
        if (bounds is not { } finite || !float.IsFinite(finite.Min.X) || !float.IsFinite(finite.Max.X))
        {
            _target = Vector3.Zero;
            _distance = 1024;
        }
        else
        {
            _target = (finite.Min + finite.Max) / 2;
            float halfAngle = MathF.Atan(MathF.Tan(FieldOfView / 2) * Math.Clamp(aspect, 0.1f, 1));
            _distance = Math.Clamp(Vector3.Distance(finite.Min, finite.Max) * 0.6f / MathF.Sin(halfAngle), 32, 1000000);
        }
    }

    internal Vector3 Eye => _target + EyeOffset;
    internal Vector3 Right => ViewAxes().Right;
    internal Vector3 Target => _target;
    internal float NearPlane => Math.Clamp(_distance * 0.01f, 0.001f, 0.5f);

    private Vector3 EyeOffset => _distance * new Vector3(MathF.Cos(_pitch) * MathF.Cos(_yaw),
        MathF.Cos(_pitch) * MathF.Sin(_yaw), MathF.Sin(_pitch));

    internal Matrix4x4 ViewProjection(float aspect)
    {
        // Keep the near plane in front of the camera even at close inspection distances.
        // Scaling it with orbit distance also avoids throwing away geometry when the eye
        // is moved closer than the old fixed half-unit near plane.
        float near = NearPlane;
        float far = Math.Max(131072, _distance * 4);
        float y = 1 / MathF.Tan(FieldOfView / 2);
        // System.Numerics' stock perspective uses a 0..1 depth range; OpenGL uses -1..1.
        var projection = new Matrix4x4(y / aspect, 0, 0, 0, 0, y, 0, 0,
            0, 0, -(far + near) / (far - near), -1, 0, 0, -2 * far * near / (far - near), 0);
        return Matrix4x4.CreateLookAt(Eye, _target, Vector3.UnitZ) * projection;
    }

    internal void Pan(float deltaX, float deltaY, float viewportHeight)
    {
        var (_, right, up) = ViewAxes();
        float scale = 2 * _distance * MathF.Tan(FieldOfView / 2) / viewportHeight;
        _target += (-right * deltaX + up * deltaY) * scale;
    }

    internal void Orbit(float deltaX, float deltaY)
    {
        _yaw -= deltaX * 0.008f;
        _pitch = Math.Clamp(_pitch + deltaY * 0.008f, -1.5f, 1.5f);
    }

    internal void Look(float deltaX, float deltaY)
    {
        Vector3 eye = Eye;
        Orbit(deltaX, deltaY);
        _target = eye - EyeOffset;
    }

    internal void MoveLocal(float right, float forward, float up)
    {
        var (viewForward, viewRight, _) = ViewAxes();
        _target += viewRight * right + viewForward * forward + Vector3.UnitZ * up;
    }

    internal void Zoom(float delta) =>
        _distance = Math.Clamp(_distance * MathF.Exp(-delta * 0.14f), MinimumOrbitDistance, 1000000);

    internal (Vector3 Origin, Vector3 Direction) PickRay(float x, float y, float aspect)
    {
        Vector3 origin = Eye;
        var (forward, right, up) = ViewAxes();
        float tangent = MathF.Tan(FieldOfView / 2);
        Vector3 direction = Vector3.Normalize(forward + right * x * tangent * aspect
            + up * y * tangent);
        return (origin, direction);
    }

    private (Vector3 Forward, Vector3 Right, Vector3 Up) ViewAxes()
    {
        Vector3 forward = Vector3.Normalize(_target - Eye);
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitZ));
        return (forward, right, Vector3.Cross(right, forward));
    }
}
