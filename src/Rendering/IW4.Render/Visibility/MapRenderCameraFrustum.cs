using System.Numerics;

namespace IW4.Render.Visibility;

/// <summary>
/// Conservative host-camera frustum for preview draw scheduling. Bounds are
/// rejected only when their complete AABB is outside one normalized plane;
/// malformed bounds remain visible so this optimization cannot hide geometry
/// whose bounds were not reconstructed.
/// </summary>
public sealed class MapRenderCameraFrustum
{
    private const float RelativePlaneTolerance = 0.0001f;

    public const int PlaneCount = 6;

    private readonly Vector4[] _planes;

    private MapRenderCameraFrustum(Vector4[] planes)
    {
        _planes = planes;
    }

    public static MapRenderCameraFrustum Create(
        RenderCamera camera,
        float aspectRatio)
    {
        if (!(aspectRatio > 0f) || !float.IsFinite(aspectRatio))
            throw new ArgumentOutOfRangeException(nameof(aspectRatio));

        var planes = new Vector4[PlaneCount];
        BuildPlanes(camera, aspectRatio, planes);
        return new MapRenderCameraFrustum(planes);
    }

    /// <summary>
    /// Rebuilds the six normalized host-camera planes into caller-owned
    /// storage. This is the allocation-free moving-camera path used by backend
    /// frame workspaces.
    /// </summary>
    public static void BuildPlanes(
        RenderCamera camera,
        float aspectRatio,
        Span<Vector4> destination)
    {
        if (!(aspectRatio > 0f) || !float.IsFinite(aspectRatio))
            throw new ArgumentOutOfRangeException(nameof(aspectRatio));
        if (destination.Length < PlaneCount)
        {
            throw new ArgumentException(
                $"Camera frustum storage requires {PlaneCount} planes.",
                nameof(destination));
        }

        Matrix4x4 viewProjection = camera.ViewProjection(aspectRatio);
        destination[0] =
            // System.Numerics transforms row vectors. Each clip inequality is
            // therefore a sum or difference of matrix columns.
            Normalize(new(
                viewProjection.M11 + viewProjection.M14,
                viewProjection.M21 + viewProjection.M24,
                viewProjection.M31 + viewProjection.M34,
                viewProjection.M41 + viewProjection.M44));
        destination[1] = Normalize(new(
                viewProjection.M14 - viewProjection.M11,
                viewProjection.M24 - viewProjection.M21,
                viewProjection.M34 - viewProjection.M31,
                viewProjection.M44 - viewProjection.M41));
        destination[2] = Normalize(new(
                viewProjection.M12 + viewProjection.M14,
                viewProjection.M22 + viewProjection.M24,
                viewProjection.M32 + viewProjection.M34,
                viewProjection.M42 + viewProjection.M44));
        destination[3] = Normalize(new(
                viewProjection.M14 - viewProjection.M12,
                viewProjection.M24 - viewProjection.M22,
                viewProjection.M34 - viewProjection.M32,
                viewProjection.M44 - viewProjection.M42));
            // Matrix4x4.CreatePerspectiveFieldOfView uses a zero-to-one depth
            // interval: z >= 0 and w - z >= 0.
        destination[4] = Normalize(new(
                viewProjection.M13,
                viewProjection.M23,
                viewProjection.M33,
                viewProjection.M43));
        destination[5] = Normalize(new(
                viewProjection.M14 - viewProjection.M13,
                viewProjection.M24 - viewProjection.M23,
                viewProjection.M34 - viewProjection.M33,
                viewProjection.M44 - viewProjection.M43));
    }

    public bool Intersects(RenderBounds bounds) =>
        Intersects(bounds, _planes);

    /// <summary>
    /// Tests bounds against caller-owned normalized planes produced by
    /// <see cref="BuildPlanes"/>.
    /// </summary>
    public static bool Intersects(
        RenderBounds bounds,
        ReadOnlySpan<Vector4> normalizedPlanes)
    {
        if (normalizedPlanes.Length < PlaneCount)
        {
            throw new ArgumentException(
                $"Camera frustum storage requires {PlaneCount} planes.",
                nameof(normalizedPlanes));
        }
        if (!bounds.IsValid ||
            !IsFinite(bounds.Min) ||
            !IsFinite(bounds.Max))
        {
            return true;
        }

        Vector3 center = bounds.Center;
        Vector3 halfSize = (bounds.Max - bounds.Min) * 0.5f;
        float tolerance = RelativePlaneTolerance *
            MathF.Max(1f, halfSize.Length());
        for (int planeIndex = 0;
             planeIndex < PlaneCount;
             planeIndex++)
        {
            Vector4 plane = normalizedPlanes[planeIndex];
            float signedDistance =
                plane.X * center.X +
                plane.Y * center.Y +
                plane.Z * center.Z +
                plane.W;
            float projectedRadius =
                MathF.Abs(plane.X) * halfSize.X +
                MathF.Abs(plane.Y) * halfSize.Y +
                MathF.Abs(plane.Z) * halfSize.Z;
            if (signedDistance + projectedRadius < -tolerance)
                return false;
        }

        return true;
    }

    public ReadOnlySpan<Vector4> NormalizedPlanes => _planes;

    private static Vector4 Normalize(Vector4 plane)
    {
        float length = MathF.Sqrt(
            plane.X * plane.X +
            plane.Y * plane.Y +
            plane.Z * plane.Z);
        if (!(length > 0f) || !float.IsFinite(length))
        {
            throw new InvalidOperationException(
                "The camera produced a non-normalizable frustum plane.");
        }

        Vector4 result = plane / length;
        if (!IsFinite(result))
        {
            throw new InvalidOperationException(
                "The camera produced a non-finite frustum plane.");
        }

        return result;
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);

    private static bool IsFinite(Vector4 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) &&
        float.IsFinite(value.W);
}
