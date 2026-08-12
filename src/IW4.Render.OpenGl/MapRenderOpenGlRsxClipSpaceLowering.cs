using System.Numerics;
using IW4.Render.Scheduling;
using IW4.Render.Shaders;
using IW4.Render.Transforms;

namespace IW4.Render.OpenGl;

/// <summary>
/// PS3 RSX to desktop OpenGL 3.3 clip/window-depth conversion. It never
/// rewrites native camera matrices.
/// </summary>
public static class MapRenderOpenGlRsxClipSpaceLowering
{
    public const double DepthPartition = 1d / 64d;

    private static Matrix4x4 VertexExportMatrix { get; } = new(
        1f, 0f, 0f, 0f,
        0f, -1f, 0f, 0f,
        0f, 0f, 2f, 0f,
        0f, 0f, -1f, 1f);

    public static MapRenderOpenGlDepthRange SceneDepthRange { get; } =
        new(DepthPartition, 1d);

    public static MapRenderOpenGlDepthRange DepthHackDepthRange { get; } =
        new(0d, DepthPartition);

    public static MapRenderOpenGlDepthRange ForPhase(
        MapRenderNormalCameraPhase phase) => phase switch
    {
        MapRenderNormalCameraPhase.DepthHack => DepthHackDepthRange,
        MapRenderNormalCameraPhase.LitOpaque or
        MapRenderNormalCameraPhase.LightMapOpaque or
        MapRenderNormalCameraPhase.LitTrans or
        MapRenderNormalCameraPhase.Emissive => SceneDepthRange,
        _ => throw new ArgumentOutOfRangeException(nameof(phase))
    };

    /// <summary>
    /// EditorPreview mixes generic host geometry and translated native
    /// geometry in one default framebuffer. Route render-space host positions
    /// through the same native camera and clip-lowering chain so both paths
    /// agree on screen orientation and window depth.
    /// </summary>
    internal static Matrix4x4 CreateDirectEditorPreviewHostViewProjection(
        MapRenderDerivedMatrixState matrices) =>
        MapRenderCoordinateConverter.RenderToGameMatrix *
        matrices.WorldViewProjection0 *
        VertexExportMatrix;

    /// <summary>
    /// Lowers an unmodified PS3-native WorldViewProjection0 value for the
    /// OpenGL EditorPreview path. The column-two sign change is the existing
    /// lower-left framebuffer compensation formerly applied while producing
    /// the shared camera state; it belongs here so Vulkan never inherits it.
    /// </summary>
    internal static Matrix4x4
        CreateDirectEditorPreviewHostViewProjectionFromPs3Native(
            Matrix4x4 nativeWorldViewProjection0)
    {
        if (!MapRenderMatrixValidation.IsFinite(nativeWorldViewProjection0))
        {
            throw new ArgumentException(
                "The native world-view-projection matrix must be finite.",
                nameof(nativeWorldViewProjection0));
        }

        nativeWorldViewProjection0.M12 =
            -nativeWorldViewProjection0.M12;
        nativeWorldViewProjection0.M22 =
            -nativeWorldViewProjection0.M22;
        nativeWorldViewProjection0.M32 =
            -nativeWorldViewProjection0.M32;
        nativeWorldViewProjection0.M42 =
            -nativeWorldViewProjection0.M42;
        return MapRenderCoordinateConverter.RenderToGameMatrix *
               nativeWorldViewProjection0 *
               VertexExportMatrix;
    }

    /// <summary>
    /// Lowers one native sun-shadow partition projection for render-space
    /// host geometry. The projection remains the exact immutable matrix
    /// produced with DPVS views 1/2; only the established coordinate-basis and
    /// RSX vertex-export boundaries are applied here.
    /// </summary>
    internal static Matrix4x4 CreateSunShadowCasterHostViewProjection(
        Matrix4x4 nativePartitionWorldToClip) =>
        MapRenderCoordinateConverter.RenderToGameMatrix *
        nativePartitionWorldToClip *
        VertexExportMatrix;

}
