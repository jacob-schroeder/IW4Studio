using Silk.NET.OpenGL;

namespace IW4.Render.OpenGl.Shadows;

/// <summary>Current-context limits required by the bounded atlas backend.</summary>
public sealed record MapRenderOpenGlSunShadowAtlasCapabilities
{
    public MapRenderOpenGlSunShadowAtlasCapabilities(
        bool supportsDepthComponent24Texture,
        bool supportsFramebufferObjects,
        bool supportsSamplerObjects,
        int maximumTextureSize,
        bool supportsDepth24Stencil8Texture = false)
    {
        if (maximumTextureSize < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumTextureSize));

        SupportsDepthComponent24Texture = supportsDepthComponent24Texture;
        SupportsFramebufferObjects = supportsFramebufferObjects;
        SupportsSamplerObjects = supportsSamplerObjects;
        SupportsDepth24Stencil8Texture = supportsDepth24Stencil8Texture;
        MaximumTextureSize = maximumTextureSize;
    }

    public bool SupportsDepthComponent24Texture { get; }

    public bool SupportsFramebufferObjects { get; }

    public bool SupportsSamplerObjects { get; }

    public bool SupportsDepth24Stencil8Texture { get; }

    public int MaximumTextureSize { get; }

    public bool SupportsComparisonDepthAtlas =>
        SupportsDepthComponent24Texture &&
        SupportsFramebufferObjects &&
        SupportsSamplerObjects &&
        MaximumTextureSize > 0;

    public bool SupportsComparisonDepthStencilAtlas =>
        SupportsDepth24Stencil8Texture &&
        SupportsFramebufferObjects &&
        SupportsSamplerObjects &&
        MaximumTextureSize > 0;

    public bool Supports(MapRenderOpenGlSunShadowAtlasPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return SupportsComparisonDepthAtlas &&
            plan.Width <= MaximumTextureSize &&
            plan.Height <= MaximumTextureSize;
    }
}

/// <summary>
/// Narrow current-context seam used for allocation, depth-target entry, and
/// ready receiver binding. It deliberately exposes no caster bias or material
/// selector operation.
/// </summary>
internal interface IMapRenderOpenGlSunShadowAtlasApi
{
    string ContextIdentity { get; }

    MapRenderOpenGlSunShadowAtlasCapabilities Capabilities { get; }

    uint GetBoundTexture2D();

    uint GetBoundDrawFramebuffer();

    uint GetBoundReadFramebuffer();

    uint CreateTexture();

    void BindTexture2DForAllocation(uint textureHandle);

    void AllocateDepthComponent24LevelZero(int width, int height);

    void AllocateDepth24Stencil8LevelZero(int width, int height);

    void SetTextureMipLevelRange(int baseLevel, int maximumLevel);

    uint CreateFramebuffer();

    void BindDrawFramebufferForAllocation(uint framebufferHandle);

    void BindReadFramebufferForAllocation(uint framebufferHandle);

    void AttachDepthTexture2D(uint textureHandle);

    void AttachDepthStencilTexture2D(uint textureHandle);

    void SelectDrawNone();

    void SelectReadNone();

    bool IsDrawFramebufferComplete();

    uint CreateSampler();

    void ConfigureLinearClampComparisonLessSampler(uint samplerHandle);

    void BindDrawFramebufferForPartition(uint framebufferHandle);

    void Viewport(int x, int y, int width, int height);

    void Scissor(int x, int y, int width, int height);

    void SetScissorTestEnabled(bool enabled);

    void DepthMask(bool enabled);

    void StencilMask(uint mask);

    void ClearDepth(double depth);

    void ClearStencil(int stencil);

    void ClearDepthBuffer();

    void ClearDepthStencilBuffer();

    void BindReadyReceiver(
        int textureUnit,
        uint textureHandle,
        uint samplerHandle);

    void DeleteTexture(uint textureHandle);

    void DeleteFramebuffer(uint framebufferHandle);

    void DeleteSampler(uint samplerHandle);
}

/// <summary>
/// Silk adapter. Temporary allocation bindings use raw GL and are restored by
/// the owner. Draw-time mutations optionally pass through the renderer's
/// authoritative state shadow so later state elision remains valid.
/// </summary>
internal sealed unsafe class SilkMapRenderOpenGlSunShadowAtlasApi :
    IMapRenderOpenGlSunShadowAtlasApi
{
    private readonly GL _gl;
    private readonly SilkOpenGlStateShadow? _state;

    internal SilkMapRenderOpenGlSunShadowAtlasApi(
        GL gl,
        string contextIdentity,
        SilkOpenGlStateShadow? state = null)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        ArgumentException.ThrowIfNullOrWhiteSpace(contextIdentity);
        _state = state;
        ContextIdentity = contextIdentity;

        bool version14 = VersionAtLeast(1, 4);
        bool version30 = VersionAtLeast(3, 0);
        bool version33 = VersionAtLeast(3, 3);
        Capabilities = new MapRenderOpenGlSunShadowAtlasCapabilities(
            supportsDepthComponent24Texture: version14 ||
                _gl.IsExtensionPresent("GL_ARB_depth_texture"),
            supportsFramebufferObjects: version30 ||
                _gl.IsExtensionPresent("GL_ARB_framebuffer_object"),
            supportsSamplerObjects: version33 ||
                _gl.IsExtensionPresent("GL_ARB_sampler_objects"),
            maximumTextureSize: GetInteger(GLEnum.MaxTextureSize),
            supportsDepth24Stencil8Texture: version30);
    }

    public string ContextIdentity { get; }

    public MapRenderOpenGlSunShadowAtlasCapabilities Capabilities { get; }

    public uint GetBoundTexture2D() => checked((uint)GetInteger(
        GLEnum.TextureBinding2D));

    public uint GetBoundDrawFramebuffer() => checked((uint)GetInteger(
        GLEnum.DrawFramebufferBinding));

    public uint GetBoundReadFramebuffer() => checked((uint)GetInteger(
        GLEnum.ReadFramebufferBinding));

    public uint CreateTexture() => _gl.GenTexture();

    public void BindTexture2DForAllocation(uint textureHandle) =>
        _gl.BindTexture(TextureTarget.Texture2D, textureHandle);

    public void AllocateDepthComponent24LevelZero(int width, int height) =>
        _gl.TexImage2D(
            TextureTarget.Texture2D,
            0,
            InternalFormat.DepthComponent24,
            checked((uint)width),
            checked((uint)height),
            0,
            PixelFormat.DepthComponent,
            PixelType.UnsignedInt,
            null);

    public void AllocateDepth24Stencil8LevelZero(int width, int height) =>
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

    public void SetTextureMipLevelRange(int baseLevel, int maximumLevel)
    {
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

    public void BindDrawFramebufferForAllocation(uint framebufferHandle) =>
        _gl.BindFramebuffer(
            FramebufferTarget.DrawFramebuffer,
            framebufferHandle);

    public void BindReadFramebufferForAllocation(uint framebufferHandle) =>
        _gl.BindFramebuffer(
            FramebufferTarget.ReadFramebuffer,
            framebufferHandle);

    public void AttachDepthTexture2D(uint textureHandle) =>
        _gl.FramebufferTexture2D(
            FramebufferTarget.DrawFramebuffer,
            FramebufferAttachment.DepthAttachment,
            TextureTarget.Texture2D,
            textureHandle,
            0);

    public void AttachDepthStencilTexture2D(uint textureHandle) =>
        _gl.FramebufferTexture2D(
            FramebufferTarget.DrawFramebuffer,
            FramebufferAttachment.DepthStencilAttachment,
            TextureTarget.Texture2D,
            textureHandle,
            0);

    public void SelectDrawNone() => _gl.DrawBuffer(DrawBufferMode.None);

    public void SelectReadNone() => _gl.ReadBuffer(ReadBufferMode.None);

    public bool IsDrawFramebufferComplete() =>
        _gl.CheckFramebufferStatus(FramebufferTarget.DrawFramebuffer) ==
        GLEnum.FramebufferComplete;

    public uint CreateSampler() => _gl.GenSampler();

    public void ConfigureLinearClampComparisonLessSampler(uint samplerHandle)
    {
        _gl.SamplerParameter(
            samplerHandle,
            GLEnum.TextureMinFilter,
            (int)TextureMinFilter.Linear);
        _gl.SamplerParameter(
            samplerHandle,
            GLEnum.TextureMagFilter,
            (int)TextureMagFilter.Linear);
        _gl.SamplerParameter(
            samplerHandle,
            GLEnum.TextureWrapS,
            (int)TextureWrapMode.ClampToEdge);
        _gl.SamplerParameter(
            samplerHandle,
            GLEnum.TextureWrapT,
            (int)TextureWrapMode.ClampToEdge);
        _gl.SamplerParameter(
            samplerHandle,
            GLEnum.TextureWrapR,
            (int)TextureWrapMode.ClampToEdge);
        _gl.SamplerParameter(
            samplerHandle,
            GLEnum.TextureCompareMode,
            (int)GLEnum.CompareRefToTexture);
        _gl.SamplerParameter(
            samplerHandle,
            GLEnum.TextureCompareFunc,
            (int)GLEnum.Less);
    }

    public void BindDrawFramebufferForPartition(uint framebufferHandle)
    {
        if (_state is not null)
        {
            _state.BindFramebuffer(
                FramebufferTarget.DrawFramebuffer,
                framebufferHandle);
        }
        else
        {
            _gl.BindFramebuffer(
                FramebufferTarget.DrawFramebuffer,
                framebufferHandle);
        }
    }

    public void Viewport(int x, int y, int width, int height)
    {
        if (_state is not null)
            _state.Viewport(x, y, width, height);
        else
            _gl.Viewport(x, y, checked((uint)width), checked((uint)height));
    }

    public void Scissor(int x, int y, int width, int height)
    {
        if (_state is not null)
            _state.Scissor(x, y, width, height);
        else
            _gl.Scissor(x, y, checked((uint)width), checked((uint)height));
    }

    public void SetScissorTestEnabled(bool enabled)
    {
        if (_state is not null)
            _state.SetEnabled(EnableCap.ScissorTest, enabled);
        else if (enabled)
            _gl.Enable(EnableCap.ScissorTest);
        else
            _gl.Disable(EnableCap.ScissorTest);
    }

    public void DepthMask(bool enabled)
    {
        if (_state is not null)
            _state.DepthMask(enabled);
        else
            _gl.DepthMask(enabled);
    }

    public void StencilMask(uint mask)
    {
        if (_state is not null)
            _state.StencilMask(mask);
        else
            _gl.StencilMask(mask);
    }

    public void ClearDepth(double depth) => _gl.ClearDepth(depth);

    public void ClearStencil(int stencil) => _gl.ClearStencil(stencil);

    public void ClearDepthBuffer() =>
        _gl.Clear(ClearBufferMask.DepthBufferBit);

    public void ClearDepthStencilBuffer() =>
        _gl.Clear(
            ClearBufferMask.DepthBufferBit |
            ClearBufferMask.StencilBufferBit);

    public void BindReadyReceiver(
        int textureUnit,
        uint textureHandle,
        uint samplerHandle)
    {
        if (textureUnit < 0)
            throw new ArgumentOutOfRangeException(nameof(textureUnit));

        if (_state is not null)
        {
            _state.ActiveTexture(textureUnit);
            _state.BindTexture(TextureTarget.Texture2D, textureHandle);
            _state.BindSampler(checked((uint)textureUnit), samplerHandle);
            return;
        }

        _gl.ActiveTexture(
            (TextureUnit)((int)TextureUnit.Texture0 + textureUnit));
        _gl.BindTexture(TextureTarget.Texture2D, textureHandle);
        _gl.BindSampler(checked((uint)textureUnit), samplerHandle);
    }

    public void DeleteTexture(uint textureHandle) =>
        _gl.DeleteTexture(textureHandle);

    public void DeleteFramebuffer(uint framebufferHandle) =>
        _gl.DeleteFramebuffer(framebufferHandle);

    public void DeleteSampler(uint samplerHandle) =>
        _gl.DeleteSampler(samplerHandle);

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
}
