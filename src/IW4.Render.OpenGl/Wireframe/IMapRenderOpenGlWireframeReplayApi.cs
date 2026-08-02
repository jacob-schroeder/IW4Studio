using System.Numerics;

using IW4.Render.Scheduling.FramePlans;

namespace IW4.Render.OpenGl.Wireframe;

/// <summary>
/// Wireframe-specific steady-state OpenGL replay seam. It deliberately has no
/// shader compilation, native-name creation, immutable upload, query creation,
/// or query-result operation.
/// </summary>
internal interface IMapRenderOpenGlWireframeReplayApi
{
    void PrepareNonInstancedSolidProgram(
        in Matrix4x4 hostWorldViewProjection);

    void ApplyExactWireframeFixedState(
        RenderFixedStateDescriptor fixedState);

    void SetLineWidth(float width);

    void DrawLinesUnsignedInt(
        MapRenderOpenGlWireframeDrawCommand command);
}
