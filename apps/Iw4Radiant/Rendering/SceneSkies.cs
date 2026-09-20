using System.Numerics;
using IW4.Assets.Assets.Material;
using Iw4Radiant.Materials;
using Silk.NET.OpenGL;

namespace Iw4Radiant.Rendering;

internal sealed class SceneSkies
{
    private readonly Dictionary<string, (MaterialSource Source, uint Texture, string? Error)> _textures = new(StringComparer.Ordinal);
    private uint _program;
    private int _viewProjectionLocation, _eyeLocation, _linearCaptureLocation, _maximumFaceSize;
    private float _maximumAnisotropy = 1;
    private bool _desktopSeamlessSampling;

    internal string? Notice { get; private set; }

    internal void Initialize(GL gl, string shaderHeader)
    {
        _desktopSeamlessSampling = !gl.GetStringS(StringName.Version).Contains("OpenGL ES", StringComparison.Ordinal);
        _program = SceneShaderProgram.Create(gl, shaderHeader, "sky.vert", "sky.frag");
        _viewProjectionLocation = gl.GetUniformLocation(_program, "uViewProjection");
        _eyeLocation = gl.GetUniformLocation(_program, "uEye");
        _linearCaptureLocation = gl.GetUniformLocation(_program, "uLinearCapture");
        _maximumFaceSize = gl.GetInteger(GetPName.MaxCubeMapTextureSize);
        if (gl.IsExtensionPresent("GL_EXT_texture_filter_anisotropic") ||
            gl.IsExtensionPresent("GL_ARB_texture_filter_anisotropic"))
            _maximumAnisotropy = gl.GetFloat((GetPName)0x84FF);
        gl.UseProgram(_program);
        gl.Uniform1(gl.GetUniformLocation(_program, "uSkyTexture"), 0);
    }

    internal unsafe void Render(GL gl, Matrix4x4 viewProjection, Vector3 eye, uint vertexArray,
        IReadOnlyList<(string Material, int Start, int Count, int WireStart, int WireCount)> batches,
        Func<string, MaterialSource?>? resolveMaterial, bool linearCapture = false)
    {
        Notice = null;
        if (_program == 0 || resolveMaterial is null) return;
        uint previousProgram = (uint)gl.GetInteger(GetPName.CurrentProgram);
        uint previousVertexArray = (uint)gl.GetInteger(GetPName.VertexArrayBinding);
        bool polygonOffset = gl.IsEnabled(EnableCap.PolygonOffsetFill);
        bool seamless = _desktopSeamlessSampling && gl.IsEnabled(EnableCap.TextureCubeMapSeamless);
        int unavailable = 0;
        try
        {
            gl.UseProgram(_program);
            gl.UniformMatrix4(_viewProjectionLocation, 1, false, (float*)&viewProjection);
            gl.Uniform3(_eyeLocation, eye.X, eye.Y, eye.Z);
            gl.Uniform1(_linearCaptureLocation, linearCapture ? 1 : 0);
            gl.BindVertexArray(vertexArray);
            gl.ActiveTexture(TextureUnit.Texture0);
            gl.Enable(EnableCap.DepthTest);
            gl.DepthFunc(DepthFunction.Lequal);
            gl.DepthMask(true);
            gl.Disable(EnableCap.PolygonOffsetFill);
            if (_desktopSeamlessSampling) gl.Enable(EnableCap.TextureCubeMapSeamless);
            foreach (var batch in batches)
            {
                if (batch.Count == 0 || resolveMaterial(batch.Material) is not { IsSky: true } source) continue;
                var cached = GetTexture(gl, batch.Material, source);
                if (cached.Texture == 0)
                {
                    Notice ??= cached.Error;
                    unavailable++;
                    continue;
                }
                gl.BindTexture(TextureTarget.TextureCubeMap, cached.Texture);
                gl.DrawArrays(PrimitiveType.Triangles, batch.Start, (uint)batch.Count);
            }
            if (unavailable > 1) Notice += $" {unavailable - 1} additional sky materials are unavailable.";
        }
        finally
        {
            gl.BindTexture(TextureTarget.TextureCubeMap, 0);
            if (polygonOffset) gl.Enable(EnableCap.PolygonOffsetFill);
            if (_desktopSeamlessSampling && !seamless) gl.Disable(EnableCap.TextureCubeMapSeamless);
            gl.BindVertexArray(previousVertexArray);
            gl.UseProgram(previousProgram);
        }
    }

    private unsafe (uint Texture, string? Error) GetTexture(GL gl, string name, MaterialSource source)
    {
        if (_textures.TryGetValue(name, out var cached))
        {
            if (cached.Source == source) return (cached.Texture, cached.Error);
            if (cached.Texture != 0) gl.DeleteTexture(cached.Texture);
            _textures.Remove(name);
        }
        uint texture = 0;
        string? error = null;
        try
        {
            if (string.IsNullOrWhiteSpace(source.ImagePath))
                throw new FileNotFoundException("The declared sky image is missing from the asset folder.");
            var image = MaterialImages.LoadCube(source, _maximumFaceSize);
            int faceBytes = checked(image.Width * image.Height * 4);
            texture = gl.GenTexture();
            gl.BindTexture(TextureTarget.TextureCubeMap, texture);
            gl.BindBuffer(BufferTargetARB.PixelUnpackBuffer, 0);
            gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            gl.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
            using (var pixels = image.RgbaBytes.Pin())
            {
                // Image exchange retains DDS +X, -X, +Y, -Y, +Z, -Z face order.
                for (int face = 0; face < 6; face++)
                    gl.TexImage2D((TextureTarget)((int)TextureTarget.TextureCubeMapPositiveX + face),
                        0, InternalFormat.Rgba8, (uint)image.Width, (uint)image.Height, 0,
                        PixelFormat.Rgba, PixelType.UnsignedByte, (byte*)pixels.Pointer + face * faceBytes);
            }
            ApplySampler(gl, source.SamplerState);
        }
        catch (Exception exception) when (SceneRenderer.IsRenderException(exception) || exception is UnauthorizedAccessException)
        {
            if (texture != 0) gl.DeleteTexture(texture);
            texture = 0;
            error = $"Sky '{name}' unavailable: {exception.Message}";
        }
        finally
        {
            gl.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
            gl.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
        }
        _textures.Add(name, (source, texture, error));
        return (texture, error);
    }

    private void ApplySampler(GL gl, MaterialSamplerState sampler)
    {
        MaterialSamplerState filter = sampler & MaterialSamplerState.FilterMask;
        MaterialSamplerState mip = sampler & MaterialSamplerState.MipMapMask;
        bool nearest = filter is MaterialSamplerState.FilterDisabled or MaterialSamplerState.FilterNearest;
        int anisotropy = filter switch
        {
            MaterialSamplerState.FilterDisabled or MaterialSamplerState.FilterNearest or MaterialSamplerState.FilterLinear => 1,
            MaterialSamplerState.FilterAnisotropic2X => 2,
            MaterialSamplerState.FilterAnisotropic4X => 4,
            _ => throw new NotSupportedException($"Sky filter state 0x{(byte)filter:X2} is unsupported.")
        };
        if (anisotropy > _maximumAnisotropy)
            throw new NotSupportedException($"The sky requires {anisotropy}× anisotropic filtering, which this GPU does not support.");
        TextureMinFilter minFilter = mip switch
        {
            MaterialSamplerState.MipMapDisabled => nearest ? TextureMinFilter.Nearest : TextureMinFilter.Linear,
            MaterialSamplerState.MipMapNearest => nearest ? TextureMinFilter.NearestMipmapNearest : TextureMinFilter.LinearMipmapNearest,
            MaterialSamplerState.MipMapLinear => nearest ? TextureMinFilter.NearestMipmapLinear : TextureMinFilter.LinearMipmapLinear,
            _ => throw new NotSupportedException($"Sky mip filter state 0x{(byte)mip:X2} is unsupported.")
        };
        gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMinFilter, (int)minFilter);
        gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMagFilter,
            (int)(nearest ? TextureMagFilter.Nearest : TextureMagFilter.Linear));
        gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapS,
            (int)((sampler & MaterialSamplerState.ClampU) != 0 ? TextureWrapMode.ClampToEdge : TextureWrapMode.Repeat));
        gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapT,
            (int)((sampler & MaterialSamplerState.ClampV) != 0 ? TextureWrapMode.ClampToEdge : TextureWrapMode.Repeat));
        gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapR,
            (int)((sampler & MaterialSamplerState.ClampW) != 0 ? TextureWrapMode.ClampToEdge : TextureWrapMode.Repeat));
        if (anisotropy > 1)
            gl.TexParameter(TextureTarget.TextureCubeMap, (TextureParameterName)0x84FE, (float)anisotropy);
        if (mip != MaterialSamplerState.MipMapDisabled) gl.GenerateMipmap(TextureTarget.TextureCubeMap);
    }

    internal void RemoveUnused(GL gl, IEnumerable<string> materials)
    {
        var used = materials.ToHashSet(StringComparer.Ordinal);
        foreach (string material in _textures.Keys.Where(key => !used.Contains(key)).ToArray())
        {
            if (_textures[material].Texture != 0) gl.DeleteTexture(_textures[material].Texture);
            _textures.Remove(material);
        }
    }

    internal void Reload(GL gl)
    {
        foreach (var cached in _textures.Values)
            if (cached.Texture != 0) gl.DeleteTexture(cached.Texture);
        _textures.Clear();
        Notice = null;
    }

    internal void Clear(GL gl)
    {
        Reload(gl);
        if (_program != 0) gl.DeleteProgram(_program);
        ForgetHandles();
    }

    internal void ForgetHandles()
    {
        _textures.Clear();
        _program = 0;
        _maximumFaceSize = 0;
        _maximumAnisotropy = 1;
        _desktopSeamlessSampling = false;
        Notice = null;
    }
}
