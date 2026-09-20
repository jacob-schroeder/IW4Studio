using System.Diagnostics;
using System.Numerics;
using IW4.Assets.Assets.Material;
using Iw4Radiant.Materials;
using Silk.NET.OpenGL;

namespace Iw4Radiant.Rendering;

// GL-context-owned simulation textures. Immutable spectra are uploaded once;
// frequency evolution, both Fourier axes and luminance conversion are GPU passes.
internal sealed class SceneWater
{
    private sealed class Wave(MaterialSource source)
    {
        internal readonly MaterialSource Source = source;
        internal uint Spectrum, First, Second, Height;
        internal string? Error;
    }

    private readonly Dictionary<string, Wave> _waves = new(StringComparer.Ordinal);
    private readonly long _started = Stopwatch.GetTimestamp();
    private uint _program, _framebuffer, _vertexArray;
    private int _pass, _time, _axis, _span, _size;
    private int _enabled, _color, _environment;
    private int _maximumSize;
    private bool _floatTargets;

    internal string? Notice { get; private set; }

    internal void Initialize(GL gl, uint sceneProgram, string header)
    {
        _floatTargets = !gl.GetStringS(StringName.Version).Contains("OpenGL ES", StringComparison.Ordinal) ||
            gl.IsExtensionPresent("GL_EXT_color_buffer_float");
        _maximumSize = gl.GetInteger(GetPName.MaxTextureSize);
        _program = SceneShaderProgram.Create(gl, header, "water-pass.vert", "water-spectrum.frag");
        _pass = gl.GetUniformLocation(_program, "uPass");
        _time = gl.GetUniformLocation(_program, "uTime");
        _axis = gl.GetUniformLocation(_program, "uAxis");
        _span = gl.GetUniformLocation(_program, "uSpan");
        _size = gl.GetUniformLocation(_program, "uSize");
        gl.UseProgram(_program);
        gl.Uniform1(gl.GetUniformLocation(_program, "uSource"), 0);
        _enabled = gl.GetUniformLocation(sceneProgram, "uWaterPreview");
        _color = gl.GetUniformLocation(sceneProgram, "uWaterColor");
        _environment = gl.GetUniformLocation(sceneProgram, "uEnvMapParms");
        gl.UseProgram(sceneProgram);
        gl.Uniform1(gl.GetUniformLocation(sceneProgram, "uWaterHeight"), 4);
        gl.Uniform1(gl.GetUniformLocation(sceneProgram, "uWaterReflection"), 5);
        _framebuffer = gl.GenFramebuffer();
        _vertexArray = gl.GenVertexArray();
    }

    internal void Update(GL gl, IReadOnlyList<string> materials, Func<string, MaterialSource?>? resolve)
    {
        Notice = null;
        if (materials.Count == 0 || resolve is null) return;
        gl.UseProgram(_program);
        gl.BindVertexArray(_vertexArray);
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.Disable(EnableCap.ScissorTest);
        gl.Disable(EnableCap.DepthTest);
        gl.Disable(EnableCap.CullFace);
        gl.Disable(EnableCap.Blend);
        gl.Disable(EnableCap.PolygonOffsetFill);
        gl.ColorMask(true, true, true, true);
        gl.Uniform1(_time, (float)Stopwatch.GetElapsedTime(_started).TotalSeconds);
        foreach (string name in materials)
        {
            if (resolve(name) is not { Water: { } water } source) continue;
            if (_waves.TryGetValue(name, out Wave? cached) && cached.Source != source)
            {
                Delete(gl, cached);
                _waves.Remove(name);
            }
            if (!_waves.TryGetValue(name, out Wave? wave))
            {
                wave = Create(gl, source);
                _waves.Add(name, wave);
            }
            if (wave.Height == 0)
            {
                Notice ??= wave.Error;
                continue;
            }
            gl.Viewport(0, 0, (uint)water.M, (uint)water.N);
            gl.Uniform1(_size, water.M);
            gl.Uniform1(_pass, 0);
            Draw(gl, wave.Spectrum, wave.First);
            uint current = wave.First, next = wave.Second;
            gl.Uniform1(_pass, 1);
            for (int axis = 0; axis < 2; axis++)
            {
                gl.Uniform1(_axis, axis);
                for (int span = 2; span <= water.M; span *= 2)
                {
                    gl.Uniform1(_span, span);
                    Draw(gl, current, next);
                    (current, next) = (next, current);
                }
            }
            gl.Uniform1(_pass, 2);
            Draw(gl, current, wave.Height);
            gl.BindTexture(TextureTarget.Texture2D, wave.Height);
            if ((source.SamplerState & MaterialSamplerState.MipMapMask) != MaterialSamplerState.MipMapDisabled)
                gl.GenerateMipmap(TextureTarget.Texture2D);
        }
        gl.BindVertexArray(0);
    }

    internal bool IsAvailable(string name) => _waves.TryGetValue(name, out Wave? wave) && wave.Height != 0;

    internal bool Bind(GL gl, string name)
    {
        if (!_waves.TryGetValue(name, out Wave? wave) || wave.Height == 0) return false;
        gl.Uniform1(_enabled, 1);
        Vector4 color = wave.Source.WaterColor, environment = wave.Source.EnvMapParms;
        gl.Uniform4(_color, color.X, color.Y, color.Z, color.W);
        gl.Uniform4(_environment, environment.X, environment.Y, environment.Z, environment.W);
        gl.ActiveTexture(TextureUnit.Texture4);
        gl.BindTexture(TextureTarget.Texture2D, wave.Height);
        gl.ActiveTexture(TextureUnit.Texture0);
        return true;
    }

    private void Draw(GL gl, uint source, uint target)
    {
        gl.BindTexture(TextureTarget.Texture2D, source);
        gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, target, 0);
        gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
    }

    private unsafe Wave Create(GL gl, MaterialSource source)
    {
        var wave = new Wave(source);
        try
        {
            MaterialWater water = source.Water ?? throw new InvalidOperationException("Missing water simulation.");
            if (!_floatTargets)
                throw new NotSupportedException("GPU water requires renderable float textures (EXT_color_buffer_float on OpenGL ES).");
            if (water.M != water.N || water.M < 4 || water.M > _maximumSize || !BitOperations.IsPow2((uint)water.M))
                throw new NotSupportedException("GPU water requires a square power-of-two spectrum supported by the GPU.");
            var spectrum = new float[checked(water.M * water.N * 4)];
            for (int index = 0; index < water.H0X.Count; index++)
            {
                spectrum[index * 4] = water.H0X[index];
                spectrum[index * 4 + 1] = water.H0Y[index];
                spectrum[index * 4 + 2] = water.WTerm[index];
            }
            wave.Spectrum = Allocate(InternalFormat.Rgba32f, PixelFormat.Rgba, PixelType.Float);
            fixed (float* data = spectrum)
                gl.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, (uint)water.M, (uint)water.N,
                    PixelFormat.Rgba, PixelType.Float, data);
            wave.First = Allocate(InternalFormat.Rgba32f, PixelFormat.Rgba, PixelType.Float);
            wave.Second = Allocate(InternalFormat.Rgba32f, PixelFormat.Rgba, PixelType.Float);
            CheckTarget(wave.First);
            wave.Height = Allocate(InternalFormat.R8, PixelFormat.Red, PixelType.UnsignedByte);
            CheckTarget(wave.Height);
            MaterialSamplerState sampler = source.SamplerState;
            bool nearest = (sampler & MaterialSamplerState.FilterMask) is MaterialSamplerState.FilterDisabled or MaterialSamplerState.FilterNearest;
            TextureMinFilter min = (sampler & MaterialSamplerState.MipMapMask) switch
            {
                MaterialSamplerState.MipMapDisabled => nearest ? TextureMinFilter.Nearest : TextureMinFilter.Linear,
                MaterialSamplerState.MipMapNearest => nearest ? TextureMinFilter.NearestMipmapNearest : TextureMinFilter.LinearMipmapNearest,
                _ => nearest ? TextureMinFilter.NearestMipmapLinear : TextureMinFilter.LinearMipmapLinear
            };
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)min);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)(nearest ? TextureMagFilter.Nearest : TextureMagFilter.Linear));
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,
                (int)((sampler & MaterialSamplerState.ClampU) != 0 ? TextureWrapMode.ClampToEdge : TextureWrapMode.Repeat));
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT,
                (int)((sampler & MaterialSamplerState.ClampV) != 0 ? TextureWrapMode.ClampToEdge : TextureWrapMode.Repeat));

            uint Allocate(InternalFormat format, PixelFormat pixelFormat, PixelType type)
            {
                uint texture = gl.GenTexture();
                gl.BindTexture(TextureTarget.Texture2D, texture);
                gl.BindBuffer(BufferTargetARB.PixelUnpackBuffer, 0);
                gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
                gl.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
                gl.TexImage2D(TextureTarget.Texture2D, 0, format, (uint)water.M, (uint)water.N, 0, pixelFormat, type, null);
                gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
                gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
                return texture;
            }
            void CheckTarget(uint texture)
            {
                gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, texture, 0);
                if (gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete)
                    throw new NotSupportedException("GPU water floating-point framebuffer is incomplete.");
            }
        }
        catch (Exception exception) when (SceneRenderer.IsRenderException(exception))
        {
            Delete(gl, wave);
            wave.Error = $"Water '{source.Name}' unavailable: {exception.Message}";
        }
        return wave;
    }

    internal void RemoveUnused(GL gl, IReadOnlyCollection<string> materials)
    {
        foreach (string name in _waves.Keys.Where(name => !materials.Contains(name)).ToArray())
        {
            Delete(gl, _waves[name]);
            _waves.Remove(name);
        }
    }

    internal void Reload(GL gl)
    {
        foreach (Wave wave in _waves.Values) Delete(gl, wave);
        _waves.Clear();
        Notice = null;
    }

    private static void Delete(GL gl, Wave wave)
    {
        if (wave.Spectrum != 0) gl.DeleteTexture(wave.Spectrum);
        if (wave.First != 0) gl.DeleteTexture(wave.First);
        if (wave.Second != 0) gl.DeleteTexture(wave.Second);
        if (wave.Height != 0) gl.DeleteTexture(wave.Height);
        wave.Spectrum = wave.First = wave.Second = wave.Height = 0;
    }

    internal void Clear(GL gl)
    {
        Reload(gl);
        if (_program != 0) gl.DeleteProgram(_program);
        if (_framebuffer != 0) gl.DeleteFramebuffer(_framebuffer);
        if (_vertexArray != 0) gl.DeleteVertexArray(_vertexArray);
        ForgetHandles();
    }

    internal void ForgetHandles()
    {
        _waves.Clear();
        _program = _framebuffer = _vertexArray = 0;
        Notice = null;
    }
}
