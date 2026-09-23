using IW4.Render.Scheduling.Lifecycle;

namespace IW4.Render.OpenGl.Targets;

/// <summary>Narrow current-context seam for scene target entry and clear.</summary>
internal interface IMapRenderSilkNormalCameraSceneTargetReplayApi
{
    string ContextIdentity { get; }

    int MaximumCombinedTextureImageUnits { get; }

    int MaximumSampleMaskWords { get; }

    int GetActiveTextureUnit();

    void CaptureTextureUnitBindings(
        MapRenderOpenGlNormalCameraTextureTarget target,
        Span<uint> destination);

    void ActiveTexture(int textureUnit);

    void BindTexture(
        MapRenderOpenGlNormalCameraTextureTarget target,
        uint textureHandle);

    void BindDrawFramebuffer(uint framebufferHandle);

    void Viewport(int x, int y, int width, int height);

    void Scissor(int x, int y, int width, int height);

    void SetScissorTestEnabled(bool enabled);

    void SetMultisampleEnabled(bool enabled);

    void SetSampleMaskEnabled(bool enabled);

    void SampleMask(uint wordIndex, uint mask);

    void SetSampleAlphaToCoverageEnabled(bool enabled);

    void SetSampleAlphaToOneEnabled(bool enabled);

    void ColorMask(bool red, bool green, bool blue, bool alpha);

    void DepthMask(bool enabled);

    void StencilMask(uint mask);

    void ClearColor(float red, float green, float blue, float alpha);

    void ClearDepth(double depth);

    void ClearStencil(int stencil);

    void Clear(MapRenderSceneClearSurfaceMask mask);
}
