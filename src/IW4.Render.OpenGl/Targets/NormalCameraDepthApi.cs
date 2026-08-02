using Silk.NET.OpenGL;

namespace IW4.Render.OpenGl.Targets;

/// <summary>
/// Narrow combined-FBO allocation surface. It exposes no clear, draw,
/// resolve, readback, viewport, sampler, or persistent bind operation.
/// </summary>
internal interface IMapRenderSilkNormalCameraDepthStencilTargetApi
{
    MapRenderSilkNormalCameraDepthStencilTargetCapabilities Capabilities { get; }

    uint GetBoundTexture(MapRenderOpenGlNormalCameraTextureTarget target);

    uint GetBoundDrawFramebuffer();

    uint GetBoundReadFramebuffer();

    uint CreateTexture();

    void BindTexture(
        MapRenderOpenGlNormalCameraTextureTarget target,
        uint handle);

    void AllocateDepth24Stencil8LevelZero(
        MapRenderOpenGlNormalCameraTextureTarget target,
        int width,
        int height,
        int sampleCount,
        bool fixedSampleLocations);

    void SetTextureMipLevelRange(
        MapRenderOpenGlNormalCameraTextureTarget target,
        int baseLevel,
        int maximumLevel);

    uint CreateFramebuffer();

    void BindDrawFramebuffer(uint handle);

    void BindReadFramebuffer(uint handle);

    void AttachTextureToColorZero(
        MapRenderOpenGlNormalCameraTextureTarget target,
        uint textureHandle);

    void AttachTextureToDepthStencil(
        MapRenderOpenGlNormalCameraTextureTarget target,
        uint textureHandle);

    void SelectDrawColorZero();

    bool IsDrawFramebufferComplete();

    void DeleteTexture(uint handle);

    void DeleteFramebuffer(uint handle);
}

/// <summary>Context limits required by the bounded combined-target allocator.</summary>
public sealed record MapRenderSilkNormalCameraDepthStencilTargetCapabilities
{
    public MapRenderSilkNormalCameraDepthStencilTargetCapabilities(
        bool supportsDepth24Stencil8TextureStorage,
        bool supportsFramebufferObjects,
        int maximumTextureSize,
        int maximumColorAttachments,
        bool supportsTexture2DMultisample = false,
        int maximumSamples = 0,
        int maximumDepthTextureSamples = 0)
    {
        if (maximumTextureSize < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumTextureSize));
        if (maximumColorAttachments < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumColorAttachments));
        if (maximumSamples < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumSamples));
        if (maximumDepthTextureSamples < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumDepthTextureSamples));
        }

        SupportsDepth24Stencil8TextureStorage =
            supportsDepth24Stencil8TextureStorage;
        SupportsFramebufferObjects = supportsFramebufferObjects;
        MaximumTextureSize = maximumTextureSize;
        MaximumColorAttachments = maximumColorAttachments;
        SupportsTexture2DMultisample = supportsTexture2DMultisample;
        MaximumSamples = maximumSamples;
        MaximumDepthTextureSamples = maximumDepthTextureSamples;
    }

    public bool SupportsDepth24Stencil8TextureStorage { get; }

    public bool SupportsFramebufferObjects { get; }

    public int MaximumTextureSize { get; }

    public int MaximumColorAttachments { get; }

    public bool SupportsTexture2DMultisample { get; }

    public int MaximumSamples { get; }

    public int MaximumDepthTextureSamples { get; }

    public bool SupportsCombinedRgba8Depth24Stencil8Framebuffer =>
        SupportsDepth24Stencil8TextureStorage &&
        SupportsFramebufferObjects &&
        MaximumTextureSize > 0 &&
        MaximumColorAttachments >= 1;

    public bool SupportsSampleCount(int sampleCount)
    {
        if (sampleCount <= 0)
            return false;
        if (sampleCount == 1)
            return SupportsCombinedRgba8Depth24Stencil8Framebuffer;
        return SupportsCombinedRgba8Depth24Stencil8Framebuffer &&
            SupportsTexture2DMultisample &&
            sampleCount <= MaximumSamples &&
            sampleCount <= MaximumDepthTextureSamples;
    }
}

/// <summary>Direct Silk implementation of the combined-target allocation API.</summary>
internal sealed unsafe class SilkMapRenderOpenGlNormalCameraDepthStencilTargetApi :
    IMapRenderSilkNormalCameraDepthStencilTargetApi
{
    private readonly GL _gl;

    internal SilkMapRenderOpenGlNormalCameraDepthStencilTargetApi(GL gl)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        bool version30 = VersionAtLeast(3, 0);
        bool textureMultisample = VersionAtLeast(3, 2) ||
            _gl.IsExtensionPresent("GL_ARB_texture_multisample");
        Capabilities =
            new MapRenderSilkNormalCameraDepthStencilTargetCapabilities(
                supportsDepth24Stencil8TextureStorage: version30,
                supportsFramebufferObjects: version30 ||
                    _gl.IsExtensionPresent("GL_ARB_framebuffer_object"),
                maximumTextureSize: GetInteger(GLEnum.MaxTextureSize),
                maximumColorAttachments: GetInteger(
                    GLEnum.MaxColorAttachments),
                supportsTexture2DMultisample: textureMultisample,
                maximumSamples: textureMultisample
                    ? GetInteger(GLEnum.MaxSamples)
                    : 0,
                maximumDepthTextureSamples: textureMultisample
                    ? GetInteger(GLEnum.MaxDepthTextureSamples)
                    : 0);
    }

    public MapRenderSilkNormalCameraDepthStencilTargetCapabilities
        Capabilities { get; }

    public uint GetBoundTexture(
        MapRenderOpenGlNormalCameraTextureTarget target) =>
        checked((uint)GetInteger(target switch
        {
            MapRenderOpenGlNormalCameraTextureTarget.Texture2D =>
                GLEnum.TextureBinding2D,
            MapRenderOpenGlNormalCameraTextureTarget.Texture2DMultisample =>
                GLEnum.TextureBinding2DMultisample,
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        }));

    public uint GetBoundDrawFramebuffer() => checked((uint)GetInteger(
        GLEnum.DrawFramebufferBinding));

    public uint GetBoundReadFramebuffer() => checked((uint)GetInteger(
        GLEnum.ReadFramebufferBinding));

    public uint CreateTexture() => _gl.GenTexture();

    public void BindTexture(
        MapRenderOpenGlNormalCameraTextureTarget target,
        uint handle) =>
        _gl.BindTexture(ToTextureTarget(target), handle);

    public void AllocateDepth24Stencil8LevelZero(
        MapRenderOpenGlNormalCameraTextureTarget target,
        int width,
        int height,
        int sampleCount,
        bool fixedSampleLocations)
    {
        switch (target)
        {
            case MapRenderOpenGlNormalCameraTextureTarget.Texture2D:
                if (sampleCount != 1 || fixedSampleLocations)
                {
                    throw new ArgumentException(
                        "A Texture2D allocation must use one sample without multisample location state.");
                }
                _gl.TexImage2D(
                    TextureTarget.Texture2D,
                    0,
                    InternalFormat.Depth24Stencil8,
                    checked((uint)width),
                    checked((uint)height),
                    0,
                    PixelFormat.DepthStencil,
                    PixelType.UnsignedInt248,
                    null);
                break;
            case MapRenderOpenGlNormalCameraTextureTarget.Texture2DMultisample:
                _gl.TexImage2DMultisample(
                    TextureTarget.Texture2DMultisample,
                    checked((uint)sampleCount),
                    InternalFormat.Depth24Stencil8,
                    checked((uint)width),
                    checked((uint)height),
                    fixedSampleLocations);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(target));
        }
    }

    public void SetTextureMipLevelRange(
        MapRenderOpenGlNormalCameraTextureTarget target,
        int baseLevel,
        int maximumLevel)
    {
        if (target != MapRenderOpenGlNormalCameraTextureTarget.Texture2D)
        {
            throw new ArgumentException(
                "Multisample textures do not expose mip-level parameters.",
                nameof(target));
        }
        _gl.TexParameter(
            TextureTarget.Texture2D,
            TextureParameterName.TextureBaseLevel,
            baseLevel);
        _gl.TexParameter(
            TextureTarget.Texture2D,
            TextureParameterName.TextureMaxLevel,
            maximumLevel);
    }

    public uint CreateFramebuffer() => _gl.GenFramebuffer();

    public void BindDrawFramebuffer(uint handle) =>
        _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, handle);

    public void BindReadFramebuffer(uint handle) =>
        _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, handle);

    public void AttachTextureToColorZero(
        MapRenderOpenGlNormalCameraTextureTarget target,
        uint textureHandle) =>
        _gl.FramebufferTexture2D(
            FramebufferTarget.DrawFramebuffer,
            FramebufferAttachment.ColorAttachment0,
            ToTextureTarget(target),
            textureHandle,
            0);

    public void AttachTextureToDepthStencil(
        MapRenderOpenGlNormalCameraTextureTarget target,
        uint textureHandle) =>
        _gl.FramebufferTexture2D(
            FramebufferTarget.DrawFramebuffer,
            FramebufferAttachment.DepthStencilAttachment,
            ToTextureTarget(target),
            textureHandle,
            0);

    public void SelectDrawColorZero() =>
        _gl.DrawBuffer(DrawBufferMode.ColorAttachment0);

    public bool IsDrawFramebufferComplete() =>
        _gl.CheckFramebufferStatus(FramebufferTarget.DrawFramebuffer) ==
        GLEnum.FramebufferComplete;

    public void DeleteTexture(uint handle) => _gl.DeleteTexture(handle);

    public void DeleteFramebuffer(uint handle) => _gl.DeleteFramebuffer(handle);

    private bool VersionAtLeast(int requiredMajor, int requiredMinor)
    {
        int major = GetInteger(GLEnum.MajorVersion);
        int minor = GetInteger(GLEnum.MinorVersion);
        return major > requiredMajor ||
            (major == requiredMajor && minor >= requiredMinor);
    }

    private int GetInteger(GLEnum name)
    {
        int value = 0;
        _gl.GetInteger(name, &value);
        return value;
    }

    private static TextureTarget ToTextureTarget(
        MapRenderOpenGlNormalCameraTextureTarget target) => target switch
        {
            MapRenderOpenGlNormalCameraTextureTarget.Texture2D =>
                TextureTarget.Texture2D,
            MapRenderOpenGlNormalCameraTextureTarget.Texture2DMultisample =>
                TextureTarget.Texture2DMultisample,
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };
}
