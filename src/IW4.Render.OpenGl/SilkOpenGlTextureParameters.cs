using IW4.Render.Textures;
using Silk.NET.OpenGL;

namespace IW4.Render.OpenGl;

/// <summary>
/// Applies the renderer-wide OpenGL projection of decoded IW4 texture swizzle
/// and sampler state to the texture currently bound to a target.
/// </summary>
internal sealed class SilkOpenGlTextureParameters
{
    private const TextureParameterName TextureMaxAnisotropyExt =
        (TextureParameterName)0x84FE;
    private const TextureParameterName TextureLodBias =
        (TextureParameterName)0x8501;
    private const int GlTextureSwizzleZero = 0;
    private const int GlTextureSwizzleOne = 1;
    private const int GlTextureSwizzleRed = 0x1903;
    private const int GlTextureSwizzleGreen = 0x1904;
    private const int GlTextureSwizzleBlue = 0x1905;
    private const int GlTextureSwizzleAlpha = 0x1906;

    private readonly GL _gl;
    private readonly bool _anisotropicFilteringSupported;

    internal SilkOpenGlTextureParameters(GL gl)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        _anisotropicFilteringSupported =
            gl.IsExtensionPresent("GL_EXT_texture_filter_anisotropic") ||
            gl.IsExtensionPresent("GL_ARB_texture_filter_anisotropic");
    }

    internal void Apply(
        MapRenderTexture texture,
        int maxMipLevel,
        TextureTarget textureTarget)
    {
        ArgumentNullException.ThrowIfNull(texture);
        ApplySwizzle(
            MapRenderRsxTextureSwizzleDecoder.Decode(
                texture.RsxTextureCommandState.TexSwizzlePayload),
            textureTarget);
        ApplySampler(texture.DecodedSamplerState, maxMipLevel, textureTarget);
    }

    internal void ApplySwizzle(
        MapRenderRsxTextureSwizzle swizzle,
        TextureTarget textureTarget)
    {
        _gl.TexParameter(
            textureTarget,
            TextureParameterName.TextureSwizzleR,
            ToGlTextureSwizzle(swizzle.Red));
        _gl.TexParameter(
            textureTarget,
            TextureParameterName.TextureSwizzleG,
            ToGlTextureSwizzle(swizzle.Green));
        _gl.TexParameter(
            textureTarget,
            TextureParameterName.TextureSwizzleB,
            ToGlTextureSwizzle(swizzle.Blue));
        _gl.TexParameter(
            textureTarget,
            TextureParameterName.TextureSwizzleA,
            ToGlTextureSwizzle(swizzle.Alpha));
    }

    internal void ApplySampler(
        MapRenderSamplerState sampler,
        int maxMipLevel,
        TextureTarget textureTarget)
    {
        bool useMipChain = maxMipLevel > 0 &&
            sampler.MipFilter != MapRenderTextureFilter.None;
        _gl.TexParameter(
            textureTarget,
            TextureParameterName.TextureMinFilter,
            (int)ToMinFilter(sampler, useMipChain));
        _gl.TexParameter(
            textureTarget,
            TextureParameterName.TextureMagFilter,
            (int)ToMagFilter(sampler.MagFilter));
        _gl.TexParameter(
            textureTarget,
            TextureParameterName.TextureBaseLevel,
            0);
        _gl.TexParameter(
            textureTarget,
            TextureParameterName.TextureMaxLevel,
            useMipChain ? maxMipLevel : 0);
        _gl.TexParameter(textureTarget, TextureLodBias, sampler.MipLodBias);
        _gl.TexParameter(
            textureTarget,
            TextureParameterName.TextureWrapS,
            (int)ToWrapMode(sampler.AddressU));
        _gl.TexParameter(
            textureTarget,
            TextureParameterName.TextureWrapT,
            (int)ToWrapMode(sampler.AddressV));
        _gl.TexParameter(
            textureTarget,
            TextureParameterName.TextureWrapR,
            (int)ToWrapMode(sampler.AddressW));

        if (sampler.MaxAnisotropy > 1 && _anisotropicFilteringSupported)
        {
            _gl.TexParameter(
                textureTarget,
                TextureMaxAnisotropyExt,
                Math.Clamp((float)sampler.MaxAnisotropy, 1f, 16f));
        }
    }

    private static int ToGlTextureSwizzle(
        MapRenderRsxTextureSwizzleSource source) => source switch
        {
            MapRenderRsxTextureSwizzleSource.Zero => GlTextureSwizzleZero,
            MapRenderRsxTextureSwizzleSource.One => GlTextureSwizzleOne,
            MapRenderRsxTextureSwizzleSource.Red => GlTextureSwizzleRed,
            MapRenderRsxTextureSwizzleSource.Green => GlTextureSwizzleGreen,
            MapRenderRsxTextureSwizzleSource.Blue => GlTextureSwizzleBlue,
            MapRenderRsxTextureSwizzleSource.Alpha => GlTextureSwizzleAlpha,
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
        };

    private static TextureMinFilter ToMinFilter(
        MapRenderSamplerState sampler,
        bool useMipChain)
    {
        bool point = sampler.MinFilter == MapRenderTextureFilter.Point;
        if (!useMipChain)
            return point ? TextureMinFilter.Nearest : TextureMinFilter.Linear;

        return sampler.MipFilter switch
        {
            MapRenderTextureFilter.Point => point
                ? TextureMinFilter.NearestMipmapNearest
                : TextureMinFilter.LinearMipmapNearest,
            MapRenderTextureFilter.Linear => point
                ? TextureMinFilter.NearestMipmapLinear
                : TextureMinFilter.LinearMipmapLinear,
            _ => point ? TextureMinFilter.Nearest : TextureMinFilter.Linear
        };
    }

    private static TextureMagFilter ToMagFilter(
        MapRenderTextureFilter filter) =>
        filter == MapRenderTextureFilter.Point
            ? TextureMagFilter.Nearest
            : TextureMagFilter.Linear;

    private static TextureWrapMode ToWrapMode(
        MapRenderTextureAddressMode mode) =>
        mode == MapRenderTextureAddressMode.Clamp
            ? TextureWrapMode.ClampToEdge
            : TextureWrapMode.Repeat;
}
