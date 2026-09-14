using IW4.Assets.Assets.Material;
using IW4.Assets.Codecs.Material;
using Iw4Radiant.Materials;
using Silk.NET.OpenGL;

namespace Iw4Radiant.Rendering;

internal static class SceneMaterialDrawing
{
    internal static void Apply(GL gl, MaterialSurfaceState state, int alphaTestLocation, int premultiplyAlphaLocation, int ignoreVertexColorLocation)
    {
        gl.DepthMask(state.DepthWrite);
        gl.Uniform1(ignoreVertexColorLocation, state.IgnoresVertexColor ? 1 : 0);
        gl.Uniform1(alphaTestLocation, AlphaTestCode(state.AlphaTest));
        gl.Uniform1(premultiplyAlphaLocation, state.BlendOperation == GfxBlendOperation.Add &&
            state.Source == GfxBlend.One && state.Destination == GfxBlend.InverseSourceAlpha ? 1 : 0);
        if (state.DepthTest is { } depthTest)
        {
            gl.Enable(EnableCap.DepthTest);
            gl.DepthFunc(depthTest switch
            {
                GfxDepthTest.Always => DepthFunction.Always, GfxDepthTest.Less => DepthFunction.Less,
                GfxDepthTest.Equal => DepthFunction.Equal, GfxDepthTest.LessThanOrEqual => DepthFunction.Lequal,
                _ => throw new NotSupportedException("Unsupported material depth test.")
            });
        }
        else gl.Disable(EnableCap.DepthTest);
        ApplyCull(gl, state.CullFace);
        if (state.IsBlended)
        {
            gl.Enable(EnableCap.Blend);
            gl.BlendEquationSeparate(Equation(state.BlendOperation), BlendEquationModeEXT.FuncAdd);
            // The viewport is opaque to the desktop compositor; retain coverage alpha while blending native RGB.
            gl.BlendFuncSeparate(Factor(state.Source), Factor(state.Destination), BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
        }
        else gl.Disable(EnableCap.Blend);
        if (state.PolygonOffset == GfxPolygonOffset.Disabled) gl.Disable(EnableCap.PolygonOffsetFill);
        else if (state.PolygonOffset != GfxPolygonOffset.Inherit)
        {
            (float factor, float units) = GfxPolygonOffsetCodec.Decode(state.PolygonOffset);
            gl.Enable(EnableCap.PolygonOffsetFill);
            gl.PolygonOffset(factor, units);
        }
    }

    internal static bool BindShadow(GL gl, int alphaTestLocation, string material,
        Func<string, MaterialSource?>? resolveMaterial, SceneMaterialTextures textures)
    {
        MaterialSurfaceState? state = resolveMaterial?.Invoke(material)?.Surface;
        if (state is not { HasShadowMapTechnique: true }) return false;
        ApplyCull(gl, state.ShadowCullFace);
        gl.Uniform1(alphaTestLocation, AlphaTestCode(state.ShadowAlphaTest));
        if (state.ShadowAlphaTest is not null)
        {
            gl.ActiveTexture(TextureUnit.Texture0);
            uint texture = textures.GetTexture(gl, material, resolveMaterial);
            if (texture == 0) return false;
            gl.BindTexture(TextureTarget.Texture2D, texture);
        }
        return true;
    }

    private static void ApplyCull(GL gl, GfxCullFace cull)
    {
        if (cull == GfxCullFace.None) gl.Disable(EnableCap.CullFace);
        else
        {
            gl.Enable(EnableCap.CullFace);
            gl.CullFace(cull switch
            {
                GfxCullFace.Back => TriangleFace.Back, GfxCullFace.Front => TriangleFace.Front,
                _ => throw new NotSupportedException("Unsupported material cull face.")
            });
        }
    }

    private static int AlphaTestCode(GfxAlphaTest? alphaTest) => alphaTest switch
    {
        null => 0, GfxAlphaTest.GreaterThanZero => 1, GfxAlphaTest.LessThan128 => 2,
        GfxAlphaTest.GreaterThanOrEqualTo128 => 3, _ => throw new NotSupportedException("Unsupported material alpha test.")
    };

    private static BlendEquationModeEXT Equation(GfxBlendOperation operation) => operation switch
    {
        GfxBlendOperation.Add => BlendEquationModeEXT.FuncAdd,
        GfxBlendOperation.Subtract => BlendEquationModeEXT.FuncSubtract,
        GfxBlendOperation.ReverseSubtract => BlendEquationModeEXT.FuncReverseSubtract,
        GfxBlendOperation.Minimum => BlendEquationModeEXT.Min,
        GfxBlendOperation.Maximum => BlendEquationModeEXT.Max,
        _ => throw new NotSupportedException("Unsupported material blend operation.")
    };

    private static BlendingFactor Factor(GfxBlend factor) => factor switch
    {
        GfxBlend.Disabled or GfxBlend.Zero => BlendingFactor.Zero, GfxBlend.One => BlendingFactor.One,
        GfxBlend.SourceColor => BlendingFactor.SrcColor, GfxBlend.InverseSourceColor => BlendingFactor.OneMinusSrcColor,
        GfxBlend.SourceAlpha => BlendingFactor.SrcAlpha, GfxBlend.InverseSourceAlpha => BlendingFactor.OneMinusSrcAlpha,
        GfxBlend.DestinationAlpha => BlendingFactor.DstAlpha, GfxBlend.InverseDestinationAlpha => BlendingFactor.OneMinusDstAlpha,
        GfxBlend.DestinationColor => BlendingFactor.DstColor, GfxBlend.InverseDestinationColor => BlendingFactor.OneMinusDstColor,
        _ => throw new NotSupportedException("Unsupported material blend factor.")
    };
}
