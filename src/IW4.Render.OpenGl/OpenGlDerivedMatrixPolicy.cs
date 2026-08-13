using System.Numerics;

using IW4.Render.Execution;
using IW4.Render.Shaders;
using IW4.Render.Transforms;

namespace IW4.Render.OpenGl;

/// <summary>
/// OpenGL-only direct-framebuffer matrix policy retained from the historical
/// preview renderer.
/// </summary>
internal static class OpenGlDerivedMatrixPolicy
{
    internal static DerivedMatrixState CreatePreviewFromCamera(
        RenderCamera camera,
        float aspectRatio)
    {
        RenderNormalCameraMatrixCalculator.CalculatePs3Native(
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
        return DerivedMatrixResolver.CreateFromPs3NativeCamera(
            view,
            projection,
            eyeOffset);
    }
}
