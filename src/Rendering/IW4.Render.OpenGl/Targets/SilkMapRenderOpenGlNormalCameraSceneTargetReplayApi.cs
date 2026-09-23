using IW4.Render.Scheduling.Lifecycle;
using Silk.NET.OpenGL;

namespace IW4.Render.OpenGl.Targets;

/// <summary>Silk lowering for the bounded scene-target replay surface.</summary>
internal sealed class SilkMapRenderOpenGlNormalCameraSceneTargetReplayApi :
    IMapRenderSilkNormalCameraSceneTargetReplayApi
{
    private readonly GL _gl;
    private readonly SilkOpenGlStateShadow? _state;

    public SilkMapRenderOpenGlNormalCameraSceneTargetReplayApi(
        GL gl,
        string contextIdentity)
        : this(gl, contextIdentity, state: null)
    {
    }

    internal SilkMapRenderOpenGlNormalCameraSceneTargetReplayApi(
        GL gl,
        string contextIdentity,
        SilkOpenGlStateShadow? state)
    {
        ArgumentNullException.ThrowIfNull(gl);
        ArgumentException.ThrowIfNullOrWhiteSpace(contextIdentity);
        _gl = gl;
        _state = state;
        ContextIdentity = contextIdentity;
        MaximumCombinedTextureImageUnits = GetInteger(
            GLEnum.MaxCombinedTextureImageUnits);
        MaximumSampleMaskWords = GetInteger(GLEnum.MaxSampleMaskWords);
    }

    public string ContextIdentity { get; }

    public int MaximumCombinedTextureImageUnits { get; }

    public int MaximumSampleMaskWords { get; }

    public int GetActiveTextureUnit() =>
        _state?.GetActiveTextureUnit() ??
        GetInteger(GLEnum.ActiveTexture) - (int)TextureUnit.Texture0;

    public void CaptureTextureUnitBindings(
        MapRenderOpenGlNormalCameraTextureTarget target,
        Span<uint> destination)
    {
        if (destination.Length > MaximumCombinedTextureImageUnits)
        {
            throw new ArgumentOutOfRangeException(nameof(destination));
        }

        if (_state is not null)
        {
            TextureTarget textureTarget = ToTextureTarget(target);
            for (int textureUnit = 0;
                 textureUnit < destination.Length;
                 textureUnit++)
            {
                destination[textureUnit] = _state.GetTextureBinding(
                    textureUnit,
                    textureTarget);
            }
            return;
        }

        int previousActiveTextureUnit = GetActiveTextureUnit();
        try
        {
            for (int textureUnit = 0;
                 textureUnit < destination.Length;
                 textureUnit++)
            {
                ActiveTexture(textureUnit);
                destination[textureUnit] = checked((uint)GetInteger(
                    target switch
                    {
                        MapRenderOpenGlNormalCameraTextureTarget.Texture2D =>
                            GLEnum.TextureBinding2D,
                        MapRenderOpenGlNormalCameraTextureTarget
                            .Texture2DMultisample =>
                            GLEnum.TextureBinding2DMultisample,
                        _ => throw new ArgumentOutOfRangeException(
                            nameof(target))
                    }));
            }
        }
        finally
        {
            // Active-texture selection is a host query transport detail, not
            // an RSX texture-cache invalidation. Restore it before publishing
            // the immutable capture.
            ActiveTexture(previousActiveTextureUnit);
        }
    }

    public void ActiveTexture(int textureUnit)
    {
        if (_state is not null)
            _state.ActiveTexture(textureUnit);
        else
            _gl.ActiveTexture(
                (TextureUnit)((int)TextureUnit.Texture0 + textureUnit));
    }

    public void BindTexture(
        MapRenderOpenGlNormalCameraTextureTarget target,
        uint textureHandle)
    {
        TextureTarget textureTarget = ToTextureTarget(target);
        if (_state is not null)
            _state.BindTexture(textureTarget, textureHandle);
        else
            _gl.BindTexture(textureTarget, textureHandle);
    }

    public void BindDrawFramebuffer(uint framebufferHandle)
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

    public void SetScissorTestEnabled(bool enabled) =>
        SetEnabled(EnableCap.ScissorTest, enabled);

    public void SetMultisampleEnabled(bool enabled) =>
        SetEnabled(EnableCap.Multisample, enabled);

    public void SetSampleMaskEnabled(bool enabled) =>
        SetEnabled(EnableCap.SampleMask, enabled);

    public void SampleMask(uint wordIndex, uint mask)
    {
        if (_state is not null)
            _state.SampleMask(wordIndex, mask);
        else
            _gl.SampleMask(wordIndex, mask);
    }

    public void SetSampleAlphaToCoverageEnabled(bool enabled) =>
        SetEnabled(EnableCap.SampleAlphaToCoverage, enabled);

    public void SetSampleAlphaToOneEnabled(bool enabled) =>
        SetEnabled(EnableCap.SampleAlphaToOne, enabled);

    public void ColorMask(bool red, bool green, bool blue, bool alpha)
    {
        if (_state is not null)
            _state.ColorMask(red, green, blue, alpha);
        else
            _gl.ColorMask(red, green, blue, alpha);
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

    public void ClearColor(float red, float green, float blue, float alpha) =>
        _gl.ClearColor(red, green, blue, alpha);

    public void ClearDepth(double depth) => _gl.ClearDepth(depth);

    public void ClearStencil(int stencil) => _gl.ClearStencil(stencil);

    public void Clear(MapRenderSceneClearSurfaceMask mask)
    {
        ClearBufferMask glMask = 0;
        if ((mask & MapRenderSceneClearSurfaceMask.Rgba) != 0)
            glMask |= ClearBufferMask.ColorBufferBit;
        if ((mask & MapRenderSceneClearSurfaceMask.Depth) != 0)
            glMask |= ClearBufferMask.DepthBufferBit;
        if ((mask & MapRenderSceneClearSurfaceMask.Stencil) != 0)
            glMask |= ClearBufferMask.StencilBufferBit;
        _gl.Clear(glMask);
    }

    private unsafe int GetInteger(GLEnum name)
    {
        int value = 0;
        _gl.GetInteger(name, &value);
        return value;
    }

    private void SetEnabled(EnableCap cap, bool enabled)
    {
        if (_state is not null)
        {
            _state.SetEnabled(cap, enabled);
            return;
        }
        if (enabled)
            _gl.Enable(cap);
        else
            _gl.Disable(cap);
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
