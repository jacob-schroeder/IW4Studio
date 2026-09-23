using IW4.Render.OpenGl.Diagnostics;
using Silk.NET.OpenGL;

namespace IW4.Render.OpenGl;

public sealed unsafe partial class SilkOpenGlMapRenderer :
    IMapRenderOpenGlNormalCameraDiagnosticReplayApi
{
    void IMapRenderOpenGlNormalCameraDiagnosticReplayApi.SetUseInstancing(
        bool enabled) =>
        _state.Uniform1(_solidUseInstancingLocation, enabled ? 1 : 0);

    void IMapRenderOpenGlNormalCameraDiagnosticReplayApi.DrawNonInstanced(
        MapRenderOpenGlNormalCameraDiagnosticDrawCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.IsInstanced)
        {
            throw new ArgumentException(
                "A non-instanced diagnostic replay cannot submit an instanced command.",
                nameof(command));
        }
        Draw(command.Mesh, PrimitiveType.Triangles);
    }

    void IMapRenderOpenGlNormalCameraDiagnosticReplayApi.DrawInstanced(
        MapRenderOpenGlNormalCameraDiagnosticDrawCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!command.IsInstanced)
        {
            throw new ArgumentException(
                "An instanced diagnostic replay cannot submit a non-instanced command.",
                nameof(command));
        }
        Draw(command.InstancedMesh, PrimitiveType.Triangles);
    }
}
