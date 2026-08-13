
namespace IW4.Render.Techniques;

/// <summary>
/// Action emitted by one PS3 material state row for polygon-offset state.
/// Inherit deliberately leaves the current graphics state untouched.
/// </summary>
public enum RenderPolygonOffsetMode : byte
{
    Disabled = 0,
    Explicit = 1,
    Inherit = 2
}

public readonly record struct RenderState(
    bool HasState,
    uint LoadBits0,
    uint LoadBits1,
    uint CommandWordCount,
    bool ShaderPackerSrgbEnabled,
    RsxColorMask ColorMask,
    bool AlphaTestEnabled,
    RsxCompareFunction AlphaFunc,
    byte AlphaRef,
    bool CullEnabled,
    RsxCullFace CullFace,
    RsxPolygonMode PolygonMode,
    bool BlendEnabled,
    RsxBlendEquation BlendEquationRgb,
    RsxBlendEquation BlendEquationAlpha,
    RsxBlendFactor BlendSourceRgb,
    RsxBlendFactor BlendSourceAlpha,
    RsxBlendFactor BlendDestinationRgb,
    RsxBlendFactor BlendDestinationAlpha,
    bool DepthTestEnabled,
    bool DepthWriteEnabled,
    RsxCompareFunction DepthFunc,
    StencilState Stencil,
    RenderPolygonOffsetMode PolygonOffsetMode,
    float PolygonOffsetFactor,
    float PolygonOffsetUnits)
{
    public static RenderState Default { get; } = new(
        HasState: false,
        LoadBits0: 0,
        LoadBits1: 0,
        CommandWordCount: 0,
        ShaderPackerSrgbEnabled: false,
        ColorMask: RsxColorMask.Rgba,
        AlphaTestEnabled: false,
        AlphaFunc: RsxCompareFunction.Always,
        AlphaRef: 0,
        CullEnabled: false,
        CullFace: RsxCullFace.Front,
        PolygonMode: RsxPolygonMode.Fill,
        BlendEnabled: false,
        BlendEquationRgb: RsxBlendEquation.Add,
        BlendEquationAlpha: RsxBlendEquation.Add,
        BlendSourceRgb: RsxBlendFactor.One,
        BlendSourceAlpha: RsxBlendFactor.One,
        BlendDestinationRgb: RsxBlendFactor.Zero,
        BlendDestinationAlpha: RsxBlendFactor.Zero,
        DepthTestEnabled: true,
        DepthWriteEnabled: true,
        DepthFunc: RsxCompareFunction.LessThanOrEqual,
        Stencil: StencilState.Disabled,
        PolygonOffsetMode: RenderPolygonOffsetMode.Disabled,
        PolygonOffsetFactor: 0f,
        PolygonOffsetUnits: 0f);

    // This aggregate covers primary color, depth, and stencil only.
    // MRT color targets have separate RSX state and must be audited through the
    // selected shader execution contract before this can describe all outputs.
    public bool StencilEnabled => Stencil.Enabled;

    // Conservatively counts any enabled stencil state as a possible write.
    public bool FramebufferWriteEnabled =>
        ColorMask != RsxColorMask.None || DepthWriteEnabled || StencilEnabled;
}
