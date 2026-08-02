namespace IW4.Render.OpenGl.Diagnostics;

/// <summary>
/// Diagnostic-specific concrete OpenGL replay seam. The implementation owns
/// uniform transitions, U32 draw submission, and draw telemetry. No default
/// state reset occurs after this pass.
/// </summary>
internal interface IMapRenderOpenGlNormalCameraDiagnosticReplayApi
{
    void SetUseInstancing(bool enabled);

    void DrawNonInstanced(
        MapRenderOpenGlNormalCameraDiagnosticDrawCommand command);

    void DrawInstanced(
        MapRenderOpenGlNormalCameraDiagnosticDrawCommand command);
}
