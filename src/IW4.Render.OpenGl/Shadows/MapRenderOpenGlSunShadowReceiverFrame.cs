using IW4.Render.Execution;
using IW4.Render.Scheduling.Shadows;
using IW4.Render.Scheduling.Dpvs;
using IW4.Render.Shaders;

namespace IW4.Render.OpenGl.Shadows;

/// <summary>
/// Atomic draw-time join between the renderer-agnostic two-partition
/// publication and the OpenGL atlas publication. A receiver may consume raw
/// code sampler argument 6 (authored destination 4), rows 0x1E/0x1F, and the
/// shadow lookup matrix only through this one revision token.
/// </summary>
internal sealed class MapRenderOpenGlSunShadowReceiverFrame
{
    internal MapRenderOpenGlSunShadowReceiverFrame(
        MapRenderSunShadowAtlasReadyState publication,
        MapRenderOpenGlSunShadowAtlasReadyFrame backendFrame)
    {
        Publication = publication ??
            throw new ArgumentNullException(nameof(publication));
        BackendFrame = backendFrame ??
            throw new ArgumentNullException(nameof(backendFrame));
        if (publication.Revision != backendFrame.FrameRevision)
        {
            throw new ArgumentException(
                "Renderer and OpenGL sun-shadow publications must have the same revision.",
                nameof(backendFrame));
        }

        RuntimeSamplerBindings = Array.AsReadOnly(
        [
            new MapRenderShaderRuntimeSamplerBinding(
                Destination: MapRenderSunShadowReceiverShaderProfile
                    .RsxSamplerDestination,
                MapRenderShaderRuntimeSamplerResourceKind.SunShadowAtlas,
                publication.Revision,
                MapRenderShaderRuntimeSamplerBindingStatus.Ready)
        ]);
    }

    public MapRenderSunShadowAtlasReadyState Publication { get; }

    public MapRenderOpenGlSunShadowAtlasReadyFrame BackendFrame { get; }

    public long Revision => Publication.Revision;

    public MapRenderWorldDpvsSunShadowFullProjectionState Projection =>
        Publication.Frame.Projection;

    public IReadOnlyList<MapRenderShaderRuntimeSamplerBinding>
        RuntimeSamplerBindings { get; }
}
