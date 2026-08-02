using System.Numerics;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.GfxMap;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;

namespace IW4.Render.Materials;

public readonly record struct MapRenderState(
    bool HasState,
    uint LoadBits0,
    uint LoadBits1,
    uint Tail,
    bool ShaderPackerSrgbEnabled,
    uint ColorMask,
    bool AlphaTestEnabled,
    uint AlphaFunc,
    byte AlphaRef,
    bool CullEnabled,
    uint CullFace,
    uint PolygonMode,
    bool BlendEnabled,
    uint BlendEquationRgb,
    uint BlendEquationAlpha,
    uint BlendSourceRgb,
    uint BlendSourceAlpha,
    uint BlendDestinationRgb,
    uint BlendDestinationAlpha,
    bool DepthTestEnabled,
    bool DepthWriteEnabled,
    uint DepthFunc,
    MapRenderStencilState Stencil,
    bool PolygonOffsetEnabled,
    float PolygonOffsetFactor,
    float PolygonOffsetUnits)
{
    public static MapRenderState Default { get; } = new(
        HasState: false,
        LoadBits0: 0,
        LoadBits1: 0,
        Tail: 0,
        ShaderPackerSrgbEnabled: false,
        ColorMask: 0x01010101,
        AlphaTestEnabled: false,
        AlphaFunc: 0x0207,
        AlphaRef: 0,
        CullEnabled: false,
        CullFace: 0x0404,
        PolygonMode: 0x1B02,
        BlendEnabled: false,
        BlendEquationRgb: 0x8006,
        BlendEquationAlpha: 0x8006,
        BlendSourceRgb: 1,
        BlendSourceAlpha: 1,
        BlendDestinationRgb: 0,
        BlendDestinationAlpha: 0,
        DepthTestEnabled: true,
        DepthWriteEnabled: true,
        DepthFunc: 0x0203,
        Stencil: MapRenderStencilState.Disabled,
        PolygonOffsetEnabled: false,
        PolygonOffsetFactor: 0f,
        PolygonOffsetUnits: 0f);

    // This aggregate covers primary color, depth, and stencil only.
    // MRT color targets have separate RSX state and must be audited through the
    // selected shader execution contract before this can describe all outputs.
    public bool StencilEnabled => Stencil.Enabled;

    // Conservatively counts any enabled stencil state as a possible write.
    public bool FramebufferWriteEnabled =>
        ColorMask != 0 || DepthWriteEnabled || StencilEnabled;
}
