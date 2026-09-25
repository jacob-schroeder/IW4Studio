using IW4.Formats.SourceFormat.Material;
using IW4.Render.OpenGl;
using Iw4Radiant.Materials;
using Silk.NET.OpenGL;

namespace Iw4Radiant.Rendering;

internal static class SceneMaterialDrawing
{
    internal static void Apply(GL gl, MaterialSurfaceState state, int alphaTestLocation,
        int premultiplyAlphaLocation, int ignoreVertexColorLocation) =>
        MaterialPreviewDrawing.Apply(gl, state, alphaTestLocation, premultiplyAlphaLocation, ignoreVertexColorLocation);

    internal static bool BindShadow(GL gl, int alphaTestLocation, string material,
        Func<string, MaterialSource?>? resolveMaterial, SceneMaterialTextures textures)
    {
        MaterialSource? source = resolveMaterial?.Invoke(material);
        if (source?.IsWater == true) return false;
        MaterialSurfaceState? state = source?.Surface;
        if (state is not { HasShadowMapTechnique: true }) return false;
        MaterialPreviewDrawing.ApplyCull(gl, state.ShadowCullFace);
        gl.Uniform1(alphaTestLocation, MaterialPreviewDrawing.AlphaTestCode(state.ShadowAlphaTest));
        if (state.ShadowAlphaTest is not null)
        {
            gl.ActiveTexture(TextureUnit.Texture0);
            uint texture = textures.GetTexture(gl, material, resolveMaterial);
            if (texture == 0) return false;
            gl.BindTexture(TextureTarget.Texture2D, texture);
        }
        return true;
    }

}
