using IW4.Render.OpenGl.Sky;

namespace IW4.Render.OpenGl;

public sealed unsafe partial class SilkOpenGlMapRenderer :
    IMapRenderOpenGlNormalCameraSkyReplayApi
{
    void IMapRenderOpenGlNormalCameraSkyReplayApi.DrawSky(
        MapRenderOpenGlNormalCameraSkyDrawCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        DrawSky(command.Mesh, command.HostViewProjection);
    }
}
