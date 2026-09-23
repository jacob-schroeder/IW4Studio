using IW4.Render.Execution.FixedFunction;

namespace IW4.Render.Scheduling.FramePlans;

/// <summary>
/// Immutable backend-neutral fixed-function rendering intent. Backends lower
/// this semantic state independently and retain ownership of API objects.
/// </summary>
public sealed class RenderFixedStateDescriptor
{
    public RenderFixedStateDescriptor(
        RenderSemanticIdentity identity,
        RenderRasterStateDescriptor raster,
        RenderDepthStateDescriptor depth,
        RenderStencilStateDescriptor stencil,
        RenderBlendStateDescriptor blend,
        RenderColorWriteMask colorWriteMask,
        RenderFragmentOutputTransfer fragmentOutputTransfer =
            RenderFragmentOutputTransfer.Linear)
    {
        RenderGeometrySlice.RequireKind(
            identity,
            RenderSemanticResourceKind.FixedState);
        raster.Validate(nameof(raster));
        depth.Validate(nameof(depth));
        stencil.Validate(nameof(stencil));
        blend.Validate(nameof(blend));
        if ((colorWriteMask & ~RenderColorWriteMask.Rgba) != 0)
            throw new ArgumentOutOfRangeException(nameof(colorWriteMask));
        if (!Enum.IsDefined(fragmentOutputTransfer))
        {
            throw new ArgumentOutOfRangeException(
                nameof(fragmentOutputTransfer));
        }

        Identity = identity;
        Raster = raster;
        Depth = depth;
        Stencil = stencil;
        Blend = blend;
        ColorWriteMask = colorWriteMask;
        FragmentOutputTransfer = fragmentOutputTransfer;
    }

    public RenderSemanticIdentity Identity { get; }

    public RenderRasterStateDescriptor Raster { get; }

    public RenderDepthStateDescriptor Depth { get; }

    public RenderStencilStateDescriptor Stencil { get; }

    public RenderBlendStateDescriptor Blend { get; }

    public RenderColorWriteMask ColorWriteMask { get; }

    /// <summary>
    /// Semantic transfer applied by each backend's authored-program lowering
    /// before the blend operation. It does not request an sRGB attachment.
    /// </summary>
    public RenderFragmentOutputTransfer FragmentOutputTransfer { get; }

    internal bool ContentEquals(RenderFixedStateDescriptor? other) =>
        other is not null &&
        Identity == other.Identity &&
        Raster == other.Raster &&
        Depth == other.Depth &&
        Stencil == other.Stencil &&
        Blend == other.Blend &&
        ColorWriteMask == other.ColorWriteMask &&
        FragmentOutputTransfer == other.FragmentOutputTransfer;
}

public static class RenderFixedStatePresets
{
    public const int SkyVersion = 1;
    public const int DiagnosticsVersion = 1;
    public const int WireframeVersion = 1;

    public static RenderFixedStateDescriptor SkyV1 { get; } = new(
        new RenderSemanticIdentity(
            RenderSemanticResourceKind.FixedState,
            "builtin.sky.fixed-state.v1"),
        new RenderRasterStateDescriptor(
            RenderCullMode.None,
            RenderFrontFace.CounterClockwise,
            RenderPolygonMode.Fill,
            RenderDepthBiasDescriptor.Disabled),
        new RenderDepthStateDescriptor(
            testEnabled: true,
            writeEnabled: false,
            RenderCompareOperation.LessOrEqual),
        RenderStencilStateDescriptor.Disabled,
        RenderBlendStateDescriptor.Disabled,
        RenderColorWriteMask.Rgba);

    public static RenderFixedStateDescriptor DiagnosticsV1 { get; } = new(
        new RenderSemanticIdentity(
            RenderSemanticResourceKind.FixedState,
            "builtin.diagnostics.fixed-state.v1"),
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

    /// <summary>
    /// Legacy collision-wireframe intent: vertex-colored indexed lines are
    /// overlaid without reading or updating scene depth at a 1.25-pixel width.
    /// </summary>
    public static RenderFixedStateDescriptor WireframeV1 { get; } = new(
        new RenderSemanticIdentity(
            RenderSemanticResourceKind.FixedState,
            "builtin.wireframe.fixed-state.v1"),
        new RenderRasterStateDescriptor(
            RenderCullMode.None,
            RenderFrontFace.CounterClockwise,
            RenderPolygonMode.Fill,
            RenderDepthBiasDescriptor.Disabled,
            lineWidth: 1.25f),
        RenderDepthStateDescriptor.Disabled,
        RenderStencilStateDescriptor.Disabled,
        RenderBlendStateDescriptor.Disabled,
        RenderColorWriteMask.Rgba);
}
