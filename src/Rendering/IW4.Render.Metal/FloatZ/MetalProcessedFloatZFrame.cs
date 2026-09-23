using IW4.Render.Execution;

using SharpMetal.Metal;

using System.Runtime.Versioning;

namespace IW4.Render.Metal.FloatZ;

/// <summary>
/// One submitted target-8 publication. The reusable target allocation is
/// deliberately not itself a publication: this token proves that it was
/// produced from target 2 for the current normal-camera frame.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MetalProcessedFloatZFrame
{
    internal MetalProcessedFloatZFrame(
        long frameRevision,
        MTLTexture texture,
        MTLSamplerState sampler)
    {
        if (frameRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(frameRevision));
        if (texture.NativePtr == 0)
            throw new ArgumentException("A processed FloatZ texture is required.", nameof(texture));
        if (sampler.NativePtr == 0)
            throw new ArgumentException("A processed FloatZ sampler is required.", nameof(sampler));

        FrameRevision = frameRevision;
        Texture = texture;
        Sampler = sampler;
    }

    internal long FrameRevision { get; }

    internal MTLTexture Texture { get; }

    internal MTLSamplerState Sampler { get; }

    internal ShaderRuntimeSamplerBinding CreateBinding(ushort destination) =>
        new(
            destination,
            ShaderRuntimeSamplerResourceKind.ProcessedFloatZ,
            FrameRevision,
            ShaderRuntimeSamplerBindingStatus.Ready);
}
