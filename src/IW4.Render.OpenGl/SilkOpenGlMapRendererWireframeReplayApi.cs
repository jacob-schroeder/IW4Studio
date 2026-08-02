using System.Numerics;

using IW4.Render.OpenGl.Wireframe;
using IW4.Render.Scheduling.FramePlans;

using Silk.NET.OpenGL;

namespace IW4.Render.OpenGl;

public sealed unsafe partial class SilkOpenGlMapRenderer :
    IMapRenderOpenGlWireframeReplayApi
{
    void IMapRenderOpenGlWireframeReplayApi
        .PrepareNonInstancedSolidProgram(
            in Matrix4x4 hostWorldViewProjection)
    {
        _state.UseProgram(_solidProgram);
        _state.UniformMatrix4(
            _solidViewProjectionLocation,
            hostWorldViewProjection);
        // The legacy oracle can inherit one from a previous diagnostics frame.
        // The strict frame-plan draw has no instance slice, so fail closed on
        // that stale-state hazard without changing the historical path.
        _state.Uniform1(_solidUseInstancingLocation, 0);
    }

    void IMapRenderOpenGlWireframeReplayApi.ApplyExactWireframeFixedState(
        RenderFixedStateDescriptor fixedState)
    {
        if (!ReferenceEquals(
                fixedState,
                RenderFixedStatePresets.WireframeV1))
        {
            throw new ArgumentException(
                "Strict OpenGL wireframe replay requires the exact shared WireframeV1 fixed-state descriptor.",
                nameof(fixedState));
        }

        // Own every state represented by WireframeV1 so the overlay cannot
        // inherit blend, masks, culling, polygon, stencil, or depth writes
        // from the final authored material draw.
        _state.FrontFace(FrontFaceDirection.Ccw);
        _state.SetEnabled(EnableCap.CullFace, false);
        _state.PolygonMode(PolygonMode.Fill);
        _state.SetEnabled(EnableCap.PolygonOffsetFill, false);
        _state.SetEnabled(EnableCap.PolygonOffsetLine, false);
        _state.SetEnabled(EnableCap.PolygonOffsetPoint, false);
        _state.SetEnabled(EnableCap.DepthTest, false);
        _state.DepthMask(false);
        _state.SetEnabled(EnableCap.StencilTest, false);
        _state.SetEnabled(EnableCap.Blend, false);
        _state.ColorMask(true, true, true, true);
    }

    void IMapRenderOpenGlWireframeReplayApi.SetLineWidth(float width) =>
        _state.LineWidth(RequireSemanticAndResolveLineWidth(width));

    void IMapRenderOpenGlWireframeReplayApi.DrawLinesUnsignedInt(
        MapRenderOpenGlWireframeDrawCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        Draw(command.Mesh, PrimitiveType.Lines);
    }

    private float RequireSemanticAndResolveLineWidth(float width)
    {
        if (width != 1.25f)
        {
            throw new ArgumentException(
                "The strict wireframe replay accepts only the shared semantic line width 1.25.",
                nameof(width));
        }
        return _wireframeEffectiveLineWidth;
    }

    internal static float ResolveEffectiveLineWidth(
        float requested,
        float minimum,
        float maximum)
    {
        if (!float.IsFinite(requested) || requested <= 0f)
            throw new ArgumentOutOfRangeException(nameof(requested));
        if (!float.IsFinite(minimum) || minimum <= 0f ||
            !float.IsFinite(maximum) || maximum < minimum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimum),
                "The OpenGL aliased line-width range must be finite, positive, and ordered.");
        }
        return Math.Clamp(requested, minimum, maximum);
    }

    private static float ResolveEffectiveLineWidthOrRequested(
        float requested,
        float minimum,
        float maximum) =>
        float.IsFinite(minimum) && minimum > 0f &&
        float.IsFinite(maximum) && maximum >= minimum
            ? ResolveEffectiveLineWidth(requested, minimum, maximum)
            // Dispatch-only characterization fixtures do not own a real GL
            // capability table. Preserve the semantic request there; a real
            // context is required to publish a compatibility clamp.
            : requested;
}
