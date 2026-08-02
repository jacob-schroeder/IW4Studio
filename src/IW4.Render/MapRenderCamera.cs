using System.Numerics;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.GfxMap;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;

namespace IW4.Render;

public readonly record struct MapRenderCamera(
    Vector3 Position,
    float YawRadians,
    float PitchRadians,
    float FieldOfViewRadians,
    float NearPlane,
    float FarPlane)
{
    /// <summary>
    /// Host preview policy. Four world units retain usable D24 precision for
    /// closely layered map geometry at normal viewing distances. This remains
    /// an explicit, caller-overridable preview value rather than a hidden
    /// production camera constant.
    /// </summary>
    public const float DefaultPreviewNearPlane = 4f;

    public static MapRenderCamera CreateForBounds(MapRenderBounds bounds)
    {
        float radius = bounds.Radius;
        float targetHeight = bounds.IsValid ? bounds.Center.Y - radius * 0.001f : 0f;
        Vector3 target = new(radius * 0.04f, targetHeight, -radius * 0.074f);
        Vector3 position = target + new Vector3(-radius * 0.14f, radius * 0.11f, radius * 0.14f);
        float farPlane = MathF.Max(250000f, position.Y + radius * 4f);
        return CreateLookAt(
            position,
            target,
            DegreesToRadians(55f),
            DefaultPreviewNearPlane,
            farPlane);
    }

    private static MapRenderCamera CreateLookAt(
        Vector3 position,
        Vector3 target,
        float fieldOfViewRadians,
        float nearPlane,
        float farPlane)
    {
        Vector3 direction = Vector3.Normalize(target - position);
        float yaw = MathF.Atan2(direction.X, -direction.Z);
        float pitch = MathF.Asin(direction.Y);
        return new MapRenderCamera(
            position,
            yaw,
            pitch,
            fieldOfViewRadians,
            nearPlane,
            farPlane);
    }

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

    private static float DegreesToRadians(float degrees) => degrees * MathF.PI / 180f;
}
