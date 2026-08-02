namespace IW4.Render.OpenGl.Sky;

/// <summary>
/// Sky-specific concrete OpenGL draw seam. Its implementation owns program,
/// state, telemetry, draw submission, and the established per-draw default
/// state reset; the shared frame plan contains none of those API mechanics.
/// </summary>
internal interface IMapRenderOpenGlNormalCameraSkyReplayApi
{
    void DrawSky(MapRenderOpenGlNormalCameraSkyDrawCommand command);
}
