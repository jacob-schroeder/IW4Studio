using System.Numerics;

using IW4.Render.Shaders;
using IW4.Render.Transforms;

namespace IW4.Render.OpenGl;

/// <summary>
/// OpenGL-only direct-framebuffer matrix policy retained from the historical
/// preview renderer.
/// </summary>
internal static class MapRenderOpenGlDerivedMatrixPolicy
{
    internal static MapRenderDerivedMatrixState CreatePreviewFromCamera(
        MapRenderCamera camera,
        float aspectRatio)
    {
        MapRenderNormalCameraMatrixCalculator.CalculatePs3Native(
            camera,
            aspectRatio,
            out Matrix4x4 view,
            out Matrix4x4 projection,
            out _,
            out Vector3 eyeOffset);

        // The translated RSX vertex export applies the native upper-left
        // viewport Y lowering, while this direct OpenGL framebuffer uses a
        // lower-left origin. Pre-negation makes those operations cancel.
        projection.M22 = -projection.M22;
        return MapRenderDerivedMatrixResolver.CreateFromPs3NativeCamera(
            view,
            projection,
            eyeOffset);
    }
}
