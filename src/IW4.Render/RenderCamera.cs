using System.Numerics;

namespace IW4.Render;

public readonly record struct RenderCamera(
    Vector3 Position,
    float YawRadians,
    float PitchRadians,
    float FieldOfViewRadians,
    float NearPlane,
    float FarPlane)
{
    public Vector3 Forward
    {
        get
        {
            float cosPitch = MathF.Cos(PitchRadians);
            return Vector3.Normalize(new Vector3(
                MathF.Sin(YawRadians) * cosPitch,
                MathF.Sin(PitchRadians),
                -MathF.Cos(YawRadians) * cosPitch));
        }
    }

    public Vector3 Right => Vector3.Normalize(Vector3.Cross(Forward, UpHint));
    public Vector3 Up => Vector3.Normalize(Vector3.Cross(Right, Forward));

    public Matrix4x4 ViewMatrix() => Matrix4x4.CreateLookAt(Position, Position + Forward, Up);

    public Matrix4x4 ProjectionMatrix(float aspectRatio)
    {
        float nearPlane = ValidateNearPlane();
        return Matrix4x4.CreatePerspectiveFieldOfView(
            FieldOfViewRadians,
            MathF.Max(0.01f, aspectRatio),
            nearPlane,
            MathF.Max(MathF.Max(1024f, FarPlane), nearPlane + 1f));
    }

    public Matrix4x4 ViewProjection(float aspectRatio)
    {
        return ViewMatrix() * ProjectionMatrix(aspectRatio);
    }

    private Vector3 UpHint => MathF.Abs(Vector3.Dot(Forward, Vector3.UnitY)) > 0.95f
        ? Vector3.UnitZ
        : Vector3.UnitY;

    private float ValidateNearPlane()
    {
        if (!(NearPlane > 0f) || !float.IsFinite(NearPlane))
        {
            throw new InvalidOperationException(
                "Camera NearPlane must be finite and positive.");
        }
        return NearPlane;
    }
}
