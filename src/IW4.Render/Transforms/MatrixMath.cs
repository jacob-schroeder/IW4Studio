using System.Numerics;

namespace IW4.Render.Transforms;

/// <summary>
/// Shared policy-free normal-camera math. Callers provide the complete camera
/// and aspect ratio; this type supplies no captured or preview defaults.
/// </summary>
internal static class MapRenderNormalCameraMatrixCalculator
{
    public static void CalculatePs3Native(
        MapRenderCamera camera,
        float aspectRatio,
        out Matrix4x4 view,
        out Matrix4x4 projection,
        out Matrix4x4 viewProjection,
        out Vector3 eyeOffset)
    {
        if (!(aspectRatio > 0f) || !float.IsFinite(aspectRatio))
        {
            throw new ArgumentOutOfRangeException(
                nameof(aspectRatio),
                "Framebuffer aspect ratio must be finite and positive.");
        }

        eyeOffset = MapRenderCoordinateConverter.RenderToGamePosition(
            camera.Position);
        Vector3 forwardAxis =
            MapRenderCoordinateConverter.RenderToGameUnitDirection(
                camera.Forward);
        Vector3 leftAxis =
            -MapRenderCoordinateConverter.RenderToGameUnitDirection(
                camera.Right);
        Vector3 upAxis =
            MapRenderCoordinateConverter.RenderToGameUnitDirection(camera.Up);

        view = MapRenderViewerMatrixMath.CreateRotationOnlyView(
            forwardAxis,
            leftAxis,
            upAxis);
        float tanHalfFovY = MathF.Tan(
            camera.FieldOfViewRadians * 0.5f);
        float tanHalfFovX = tanHalfFovY * aspectRatio;
        projection = MapRenderViewerMatrixMath.CreateInfiniteProjection(
            tanHalfFovX,
            tanHalfFovY,
            camera.NearPlane);
        viewProjection = MapRenderViewerMatrixMath.CreateViewProjection(
            view,
            projection);
    }
}

/// <summary>
/// Pure matrix operations reproduced from the PS3 viewer/projection builders.
/// Inputs are already expressed in native game coordinates.
/// </summary>
internal static class MapRenderViewerMatrixMath
{
    public const float ClipScale = 0.99951171875f;

    /// <summary>
    /// Output columns are <c>-axis[1], axis[2], axis[0]</c>. Translation is
    /// deliberately omitted because normal-camera backend source state retains
    /// EyeOffset separately.
    /// </summary>
    public static Matrix4x4 CreateRotationOnlyView(
        Vector3 forwardAxis,
        Vector3 leftAxis,
        Vector3 upAxis)
    {
        ValidateFinite(forwardAxis, nameof(forwardAxis));
        ValidateFinite(leftAxis, nameof(leftAxis));
        ValidateFinite(upAxis, nameof(upAxis));

        return new Matrix4x4(
            -leftAxis.X, upAxis.X, forwardAxis.X, 0f,
            -leftAxis.Y, upAxis.Y, forwardAxis.Y, 0f,
            -leftAxis.Z, upAxis.Z, forwardAxis.Z, 0f,
            0f, 0f, 0f, 1f);
    }

    /// <summary>Creates the PS3 infinite perspective projection.</summary>
    public static Matrix4x4 CreateInfiniteProjection(
        float tanHalfFovX,
        float tanHalfFovY,
        float zNear)
    {
        if (!(tanHalfFovX > 0f) ||
            !(tanHalfFovY > 0f) ||
            !(zNear > 0f) ||
            !float.IsFinite(tanHalfFovX) ||
            !float.IsFinite(tanHalfFovY) ||
            !float.IsFinite(zNear))
        {
            throw new ArgumentOutOfRangeException(
                nameof(tanHalfFovX),
                "PS3 projection parameters must be finite and positive.");
        }

        return new Matrix4x4(
            ClipScale / tanHalfFovX, 0f, 0f, 0f,
            0f, ClipScale / tanHalfFovY, 0f, 0f,
            0f, 0f, ClipScale, 1f,
            0f, 0f, -zNear * ClipScale, 0f);
    }

    /// <summary>
    /// Multiplies the row-major matrices with View first and Projection
    /// second.
    /// </summary>
    public static Matrix4x4 CreateViewProjection(
        Matrix4x4 view,
        Matrix4x4 projection) => view * projection;

    private static void ValidateFinite(Vector3 value, string parameterName)
    {
        if (!float.IsFinite(value.X) ||
            !float.IsFinite(value.Y) ||
            !float.IsFinite(value.Z))
        {
            throw new ArgumentException(
                "PS3 viewer axes must be finite.",
                parameterName);
        }
    }
}

internal static class MapRenderMatrixValidation
{
    internal static bool IsFinite(Matrix4x4 value) =>
        float.IsFinite(value.M11) && float.IsFinite(value.M12) &&
        float.IsFinite(value.M13) && float.IsFinite(value.M14) &&
        float.IsFinite(value.M21) && float.IsFinite(value.M22) &&
        float.IsFinite(value.M23) && float.IsFinite(value.M24) &&
        float.IsFinite(value.M31) && float.IsFinite(value.M32) &&
        float.IsFinite(value.M33) && float.IsFinite(value.M34) &&
        float.IsFinite(value.M41) && float.IsFinite(value.M42) &&
        float.IsFinite(value.M43) && float.IsFinite(value.M44);
}
