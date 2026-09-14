using IW4.Assets.Assets.Material;
using Iw4Radiant.Materials;
using Silk.NET.OpenGL;

namespace Iw4Radiant.Rendering;

internal static class SceneMaterialDrawing
{
    internal static void Apply(GL gl, MaterialSurfaceState state, int alphaTestLocation)
    {
        gl.DepthMask(state.DepthWrite);
        gl.Uniform1(alphaTestLocation, AlphaTestCode(state));
        if (state.IsBlended)
        {
            gl.Enable(EnableCap.Blend);
            gl.BlendEquationSeparate(Equation(state.BlendOperation), BlendEquationModeEXT.FuncAdd);
            // The viewport is opaque to the desktop compositor; retain coverage alpha while blending native RGB.
            gl.BlendFuncSeparate(Factor(state.Source), Factor(state.Destination), BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
        }
        else gl.Disable(EnableCap.Blend);
        // Editor depth separation keeps coplanar overlays visible without modifying map coordinates.
        gl.PolygonOffset(state.DepthWrite ? 1 : -1, state.DepthWrite ? 1 : -1);
    }

    internal static bool BindShadow(GL gl, int alphaTestLocation, string material,
        Func<string, MaterialSource?>? resolveMaterial, SceneMaterialTextures textures)
    {
        MaterialSurfaceState state = resolveMaterial?.Invoke(material)?.Surface ?? MaterialSurfaceState.Opaque;
        // Blended and non-depth-writing surfaces cannot be represented by an opaque shadow depth.
        if (!state.DepthWrite || state.IsBlended) return false;
        gl.Uniform1(alphaTestLocation, AlphaTestCode(state));
        if (state.AlphaTest is not null)
        {
            gl.ActiveTexture(TextureUnit.Texture0);
            uint texture = textures.GetTexture(gl, material, resolveMaterial);
            if (texture == 0) return false;
            gl.BindTexture(TextureTarget.Texture2D, texture);
        }
        return true;
    }

    private static int AlphaTestCode(MaterialSurfaceState state) => state.AlphaTest switch
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
