using IW4.Render.Execution.FixedFunction;
using IW4.Render.Resources;
using IW4.Render.Scheduling.FramePlans;
using IW4.Render.Shaders;
using IW4.Render.Textures;

namespace IW4.Render.Preview;

/// <summary>
/// Shared material draw contracts used by normal-camera frame planning and
/// backend shader lowering.
/// </summary>
public static class RenderMaterialPreviewFramePlanFactory
{
    public static RenderShaderBindingPoint TextureBindingPoint { get; } =
        new(RenderShaderStage.Fragment, destination: 0);

    public static RenderShaderBindingPoint WorldViewProjectionBindingPoint
        { get; } = new(RenderShaderStage.Vertex, destination: 0);

    public static RenderShaderAbiDescriptor ShaderAbi { get; } = new(
        new RenderShaderAbiIdentity("builtin.material-preview.shader-abi.v1"),
        [
            new RenderShaderBindingRequirement(
                TextureBindingPoint,
                RenderTextureDimension.Texture2D),
            new RenderShaderBindingRequirement(
                WorldViewProjectionBindingPoint,
                RenderDynamicConstantEncoding.Matrix4x4Rows,
                RenderShaderCoordinateSpace.Ps3Native,
                expectedVectorCount: 4)
        ]);

    public static RenderShaderProgramDescriptor ShaderProgram { get; } = new(
        new RenderSemanticIdentity(
            RenderSemanticResourceKind.ShaderProgram,
            "builtin.material-preview.shader-program.v1"),
        "builtin.material-preview.vertex.render-position-uv0-ps3-native-wvp.v1",
        "builtin.material-preview.fragment.sample-base-texture2d-rgba.v1",
        ShaderAbi);

    public static RenderFixedStateDescriptor FixedState { get; } = new(
        new RenderSemanticIdentity(
            RenderSemanticResourceKind.FixedState,
            "builtin.material-preview.generic-opaque.fixed-state.v1"),
        new RenderRasterStateDescriptor(
            RenderCullMode.None,
            RenderFrontFace.CounterClockwise,
            RenderPolygonMode.Fill,
            RenderDepthBiasDescriptor.Disabled),
        new RenderDepthStateDescriptor(
            testEnabled: true,
            writeEnabled: true,
            RenderCompareOperation.LessOrEqual),
        RenderStencilStateDescriptor.Disabled,
        RenderBlendStateDescriptor.Disabled,
        RenderColorWriteMask.Rgba);
}
