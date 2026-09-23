using IW4.Render.Textures;
using Texture = IW4.Render.Textures.Texture;
using TextureTarget = Silk.NET.OpenGL.TextureTarget;
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
        Texture texture,
        int maxMipLevel,
        TextureTarget textureTarget,
        Action<string>? trace = null)
    {
        ArgumentNullException.ThrowIfNull(texture);
        ApplySwizzle(
            RsxTextureSwizzleDecoder.Decode(
                texture.RsxTextureCommandState),
            textureTarget,
            trace);
        ApplySampler(
            texture.DecodedSamplerState,
            maxMipLevel,
            textureTarget,
            trace);
    }

    internal void ApplySwizzle(
        RsxTextureSwizzle swizzle,
        TextureTarget textureTarget,
        Action<string>? trace = null)
    {
        SetTextureParameter(
            textureTarget,
            TextureParameterName.TextureSwizzleR,
            ToGlTextureSwizzle(swizzle.Red),
            trace);
        SetTextureParameter(
            textureTarget,
            TextureParameterName.TextureSwizzleG,
            ToGlTextureSwizzle(swizzle.Green),
            trace);
        SetTextureParameter(
            textureTarget,
            TextureParameterName.TextureSwizzleB,
            ToGlTextureSwizzle(swizzle.Blue),
            trace);
        SetTextureParameter(
            textureTarget,
            TextureParameterName.TextureSwizzleA,
            ToGlTextureSwizzle(swizzle.Alpha),
            trace);
    }

    internal void ApplySampler(
        RsxSamplerState sampler,
        int maxMipLevel,
        TextureTarget textureTarget,
        Action<string>? trace = null)
    {
        bool useMipChain = maxMipLevel > 0 &&
            sampler.MipFilter != TextureFilter.None;
        SetTextureParameter(
            textureTarget,
            TextureParameterName.TextureMinFilter,
            (int)ToMinFilter(sampler, useMipChain),
            trace);
        SetTextureParameter(
            textureTarget,
            TextureParameterName.TextureMagFilter,
            (int)ToMagFilter(sampler.MagFilter),
            trace);
        SetTextureParameter(
            textureTarget,
            TextureParameterName.TextureBaseLevel,
            0,
            trace);
        SetTextureParameter(
            textureTarget,
            TextureParameterName.TextureMaxLevel,
            useMipChain ? maxMipLevel : 0,
            trace);
        SetTextureParameter(
            textureTarget,
            TextureLodBias,
            sampler.MipLodBias,
            trace);
        SetTextureParameter(
            textureTarget,
            TextureParameterName.TextureWrapS,
            (int)ToWrapMode(sampler.AddressU),
            trace);
        SetTextureParameter(
            textureTarget,
            TextureParameterName.TextureWrapT,
            (int)ToWrapMode(sampler.AddressV),
            trace);
        SetTextureParameter(
            textureTarget,
            TextureParameterName.TextureWrapR,
            (int)ToWrapMode(sampler.AddressW),
            trace);

        if (sampler.MaxAnisotropy > 1 && _anisotropicFilteringSupported)
        {
            SetTextureParameter(
                textureTarget,
                TextureMaxAnisotropyExt,
                Math.Clamp((float)sampler.MaxAnisotropy, 1f, 16f),
                trace);
        }
    }

    private void SetTextureParameter(
        TextureTarget textureTarget,
        TextureParameterName name,
        int value,
        Action<string>? trace)
    {
        trace?.Invoke(
            $"driver glTexParameter started; target={textureTarget}; " +
            $"parameter={name}; value={value}");
        _gl.TexParameter(textureTarget, name, value);
    }

    private void SetTextureParameter(
        TextureTarget textureTarget,
        TextureParameterName name,
        float value,
        Action<string>? trace)
    {
        trace?.Invoke(
            $"driver glTexParameter started; target={textureTarget}; " +
            $"parameter={name}; value={value:R}");
        _gl.TexParameter(textureTarget, name, value);
    }

    private static int ToGlTextureSwizzle(
        RsxTextureSwizzleSource source) => source switch
        {
            RsxTextureSwizzleSource.Zero => GlTextureSwizzleZero,
            RsxTextureSwizzleSource.One => GlTextureSwizzleOne,
            RsxTextureSwizzleSource.Red => GlTextureSwizzleRed,
            RsxTextureSwizzleSource.Green => GlTextureSwizzleGreen,
            RsxTextureSwizzleSource.Blue => GlTextureSwizzleBlue,
            RsxTextureSwizzleSource.Alpha => GlTextureSwizzleAlpha,
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
        };

    private static TextureMinFilter ToMinFilter(
        RsxSamplerState sampler,
        bool useMipChain)
    {
        bool point = sampler.MinFilter == TextureFilter.Point;
        if (!useMipChain)
            return point ? TextureMinFilter.Nearest : TextureMinFilter.Linear;

        return sampler.MipFilter switch
        {
            TextureFilter.Point => point
                ? TextureMinFilter.NearestMipmapNearest
                : TextureMinFilter.LinearMipmapNearest,
            TextureFilter.Linear => point
                ? TextureMinFilter.NearestMipmapLinear
                : TextureMinFilter.LinearMipmapLinear,
            _ => point ? TextureMinFilter.Nearest : TextureMinFilter.Linear
        };
    }

    private static TextureMagFilter ToMagFilter(
        TextureFilter filter) =>
        filter == TextureFilter.Point
            ? TextureMagFilter.Nearest
            : TextureMagFilter.Linear;

    private static TextureWrapMode ToWrapMode(
        TextureAddressMode mode) =>
        mode == TextureAddressMode.Clamp
            ? TextureWrapMode.ClampToEdge
            : TextureWrapMode.Repeat;
}
