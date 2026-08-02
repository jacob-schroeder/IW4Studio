using System.Numerics;
using IW4.Render.Scheduling.Clear;
using IW4.Render.Transforms;

namespace IW4.Render.Scheduling.Dpvs;

/// <summary>
/// Reconstructs PS3 default_mp.elf 0x0034C3C8 and 0x00349E90 for the normal
/// camera. This is operational camera state, not capture or preview metadata.
/// </summary>
public static class MapRenderWorldDpvsNormalCameraFrameProducer
{
    private const float PortalNearDistance = 0.1f;

    private static readonly Vector4[] StandardSidePlanes =
    [
        new(-1f, 0f, 0f, 1f),
        new(1f, 0f, 0f, 1f),
        new(0f, -1f, 0f, 1f),
        new(0f, 1f, 0f, 1f)
    ];

    public static MapRenderWorldDpvsNormalCameraFrameBuildResult Build(
        MapRenderCamera camera,
        MapRenderNormalCameraFramebufferExtent framebuffer,
        MapRenderNormalCameraFarPlaneState farPlaneState)
    {
        ArgumentNullException.ThrowIfNull(farPlaneState);
        if (!IsValid(camera))
        {
            return Failed(
                MapRenderWorldDpvsNormalCameraFrameFailureKind.InvalidCamera,
                "Normal camera position, angles, field of view, or projection near plane is invalid.");
        }
        float aspectRatio = framebuffer.AspectRatio;
        if (!(aspectRatio > 0f) || !float.IsFinite(aspectRatio))
        {
            return Failed(
                MapRenderWorldDpvsNormalCameraFrameFailureKind.InvalidFramebufferAspectRatio,
                "Logical framebuffer aspect ratio must be finite and positive.");
        }
        float effectiveFar = farPlaneState.EffectiveDistance;
        if (!float.IsFinite(effectiveFar))
        {
            return Failed(
                MapRenderWorldDpvsNormalCameraFrameFailureKind.InvalidFarPlaneState,
                "Effective PS3 far-plane distance is not finite.");
        }

        MapRenderNormalCameraMatrixCalculator.CalculatePs3Native(
            camera,
            aspectRatio,
            out _,
            out _,
            out Matrix4x4 rotationViewProjection,
            out Vector3 origin);
        Vector3 forward = MapRenderCoordinateConverter
            .RenderToGameUnitDirection(camera.Forward);
        Matrix4x4 viewProjection =
            Matrix4x4.CreateTranslation(-origin) * rotationViewProjection;
        if (!Matrix4x4.Invert(viewProjection, out Matrix4x4 inverseViewProjection))
        {
            return Failed(
                MapRenderWorldDpvsNormalCameraFrameFailureKind.SingularViewProjection,
                "Normal-camera native view-projection matrix is singular.");
        }

        var planes = new List<MapRenderWorldDpvsClipPlane>(6);
        for (int planeIndex = 0; planeIndex < StandardSidePlanes.Length; planeIndex++)
        {
            if (!TryTransformPlane(
                    StandardSidePlanes[planeIndex],
                    viewProjection,
                    out MapRenderWorldDpvsClipPlane plane))
            {
                return Failed(
                    MapRenderWorldDpvsNormalCameraFrameFailureKind.InvalidFrustumPlane,
                    $"PS3 side-frustum plane {planeIndex} cannot be normalized.",
                    planeIndex);
            }
            planes.Add(plane);
        }

        var nearPlane = new MapRenderWorldDpvsClipPlane(
            forward.X,
            forward.Y,
            forward.Z,
            PortalNearDistance - Vector3.Dot(forward, origin));
        planes.Add(nearPlane);

        MapRenderWorldDpvsClipPlane? farPlane = null;
        if (effectiveFar > 0f)
        {
            Vector3 farNormal = -forward;
            farPlane = new(
                farNormal.X,
                farNormal.Y,
                farNormal.Z,
                effectiveFar - Vector3.Dot(farNormal, origin));
            planes.Add(farPlane.Value);
        }

        return MapRenderWorldDpvsNormalCameraFrameBuildResult.Succeeded(
            new(
                origin,
                forward,
                viewProjection,
                inverseViewProjection,
                nearPlane,
                farPlane,
                planes));
    }

    private static bool TryTransformPlane(
        Vector4 clipPlane,
        Matrix4x4 matrix,
        out MapRenderWorldDpvsClipPlane plane)
    {
        float normalX = Vector4.Dot(
            clipPlane,
            new(matrix.M11, matrix.M12, matrix.M13, matrix.M14));
        float normalY = Vector4.Dot(
            clipPlane,
            new(matrix.M21, matrix.M22, matrix.M23, matrix.M24));
        float normalZ = Vector4.Dot(
            clipPlane,
            new(matrix.M31, matrix.M32, matrix.M33, matrix.M34));
        float coefficientW = Vector4.Dot(
            clipPlane,
            new(matrix.M41, matrix.M42, matrix.M43, matrix.M44));
        float length = MathF.Sqrt(
            normalX * normalX +
            normalY * normalY +
            normalZ * normalZ);
        if (!(length > 0f) || !float.IsFinite(length))
        {
            plane = default;
            return false;
        }

        float scale = 1f / length;
        plane = new(
            normalX * scale,
            normalY * scale,
            normalZ * scale,
            coefficientW * scale);
        return IsFinite(plane);
    }

    private static bool IsValid(MapRenderCamera camera) =>
        IsFinite(camera.Position) &&
        float.IsFinite(camera.YawRadians) &&
        float.IsFinite(camera.PitchRadians) &&
        camera.FieldOfViewRadians > 0f &&
        camera.FieldOfViewRadians < MathF.PI &&
        float.IsFinite(camera.FieldOfViewRadians) &&
        camera.NearPlane > 0f &&
        float.IsFinite(camera.NearPlane);

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);

    private static bool IsFinite(MapRenderWorldDpvsClipPlane value) =>
        float.IsFinite(value.NormalX) &&
        float.IsFinite(value.NormalY) &&
        float.IsFinite(value.NormalZ) &&
        float.IsFinite(value.CoefficientW);

    private static MapRenderWorldDpvsNormalCameraFrameBuildResult Failed(
        MapRenderWorldDpvsNormalCameraFrameFailureKind kind,
        string detail,
        int? planeIndex = null) =>
        MapRenderWorldDpvsNormalCameraFrameBuildResult.Failed(
            new(kind, detail, planeIndex));
}
