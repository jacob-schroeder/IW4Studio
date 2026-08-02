using IW4.Render.Execution;

namespace IW4.Render.OpenGl.FloatZ;

/// <summary>
/// Same-revision publication of the native processed-FloatZ target. The
/// texture is owned by the reusable OpenGL backend; this token only proves
/// that target 8 was produced from target 2 for one exact scene frame.
/// </summary>
internal sealed class MapRenderOpenGlProcessedFloatZFrame
{
    internal MapRenderOpenGlProcessedFloatZFrame(
        long frameRevision,
        uint textureHandle,
        uint samplerHandle)
    {
        if (frameRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(frameRevision));
        if (textureHandle == 0)
            throw new ArgumentOutOfRangeException(nameof(textureHandle));
        if (samplerHandle == 0)
            throw new ArgumentOutOfRangeException(nameof(samplerHandle));

        FrameRevision = frameRevision;
        TextureHandle = textureHandle;
        SamplerHandle = samplerHandle;
    }

    public long FrameRevision { get; }

    public uint TextureHandle { get; }

    public uint SamplerHandle { get; }

    public MapRenderShaderRuntimeSamplerBinding CreateBinding(
        ushort destination) =>
        new(
            destination,
            MapRenderShaderRuntimeSamplerResourceKind.ProcessedFloatZ,
            FrameRevision,
            MapRenderShaderRuntimeSamplerBindingStatus.Ready);
}
