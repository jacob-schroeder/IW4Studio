using System.Diagnostics;
using System.Numerics;
using IW4.AssetExchange.SourceFormat.Material;
using IW4.Assets.Assets.Material;
using Iw4Radiant.Materials;
using Iw4Radiant.MapSource;
using Silk.NET.OpenGL;

namespace Iw4Radiant.Rendering;

// GL-context-owned simulation textures. Immutable spectra are uploaded once;
// frequency evolution, both Fourier axes and luminance conversion are GPU passes.
internal sealed class SceneWater
{
    private sealed class Wave(MaterialSource source)
    {
        internal readonly MaterialSource Source = source;
        internal readonly (Vector4 First, Vector4 Second) OceanWaves = source.Ocean?.GetWaves() ?? (Vector4.Zero, Vector4.Zero);
        internal readonly float InverseFadeWidth = source.Ocean is { } ocean ? 1 / ocean.FadeWidth : 0;
        internal uint Spectrum, First, Second, Height;
        internal string? Error;
    }

    private readonly Dictionary<string, Wave> _waves = new(StringComparer.Ordinal);
    private readonly long _started = Stopwatch.GetTimestamp();
    private uint _program, _framebuffer, _vertexArray, _underwaterProgram;
    private int _underwaterTintLocation;
    private readonly List<(Vector3 Minimum, Vector3 Maximum, Vector4[] Planes, MaterialVec4 Tint)> _volumes = [];
    private bool _submerged;
    private float _underwaterMix;
    private double _lastUnderwaterTime;
    private MaterialVec4 _underwaterTint;
    private int _pass, _time, _axis, _span, _size;
    private int _enabled, _color, _environment, _oceanTime, _oceanFirst, _oceanSecond, _oceanFade;
    private int _maximumSize;
    private bool _floatTargets;
    private float _oceanSeconds;

    internal string? Notice { get; private set; }

    internal void Initialize(GL gl, uint sceneProgram, string header)
    {
        _floatTargets = !gl.GetStringS(StringName.Version).Contains("OpenGL ES", StringComparison.Ordinal) ||
            gl.IsExtensionPresent("GL_EXT_color_buffer_float");
        _maximumSize = gl.GetInteger(GetPName.MaxTextureSize);
        _program = SceneShaderProgram.Create(gl, header, "water-pass.vert", "water-spectrum.frag");
        _underwaterProgram = SceneShaderProgram.Create(gl, header, "water-pass.vert", "underwater.frag");
        _underwaterTintLocation = gl.GetUniformLocation(_underwaterProgram, "uTint");
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
        _oceanTime = gl.GetUniformLocation(sceneProgram, "uOceanTime");
        _oceanFirst = gl.GetUniformLocation(sceneProgram, "uOceanFirst");
        _oceanSecond = gl.GetUniformLocation(sceneProgram, "uOceanSecond");
        _oceanFade = gl.GetUniformLocation(sceneProgram, "uOceanInverseFade");
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
        double seconds = Stopwatch.GetElapsedTime(_started).TotalSeconds;
        _oceanSeconds = (float)(seconds % 43200);
        gl.Uniform1(_time, (float)seconds);
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
        var (first, second) = wave.OceanWaves;
        gl.Uniform4(_oceanFirst, first.X, first.Y, first.Z, first.W);
        gl.Uniform4(_oceanSecond, second.X, second.Y, second.Z, second.W);
        gl.Uniform1(_oceanTime, _oceanSeconds);
        gl.Uniform1(_oceanFade, wave.InverseFadeWidth);
        gl.ActiveTexture(TextureUnit.Texture4);
        gl.BindTexture(TextureTarget.Texture2D, wave.Height);
        gl.ActiveTexture(TextureUnit.Texture0);
        return true;
    }

    internal void SetVolumes(IEnumerable<(MapBrush Brush, MaterialSource? Material)> brushes)
    {
        _volumes.Clear();
        foreach ((MapBrush brush, MaterialSource? material) in brushes)
        {
            if (material is null) continue;
            var (minimum, maximum) = brush.GetBounds();
            _volumes.Add((minimum, maximum, brush.Faces.Select(face =>
                new Vector4(face.Normal, (float)BrushGeometry.Dot(face.Normal, face.A))).ToArray(),
                WaterMaterialAuthoring.CreateUnderwaterTint(material.WaterColor.X, material.WaterColor.Y,
                    material.WaterColor.Z)));
        }
        if (_volumes.Count == 0) { _submerged = false; _underwaterMix = 0; }
    }

    internal void RenderUnderwater(GL gl, Vector3 eye)
    {
        float margin = (_submerged ? 1 : -1) * WaterMaterialAuthoring.UnderwaterBoundaryInset;
        _submerged = false;
        foreach (var volume in _volumes)
        {
            if (eye.X < volume.Minimum.X - margin || eye.X > volume.Maximum.X + margin ||
                eye.Y < volume.Minimum.Y - margin || eye.Y > volume.Maximum.Y + margin ||
                eye.Z < volume.Minimum.Z - margin || eye.Z > volume.Maximum.Z + margin) continue;
            bool inside = true;
            foreach (Vector4 plane in volume.Planes)
                if (eye.X * plane.X + eye.Y * plane.Y + eye.Z * plane.Z > plane.W + margin)
                { inside = false; break; }
            if (inside)
            {
                _submerged = true;
                _underwaterTint = volume.Tint;
                break;
            }
        }
        double seconds = Stopwatch.GetElapsedTime(_started).TotalSeconds;
        float step = (float)Math.Clamp(seconds - _lastUnderwaterTime, 0, 0.1) / WaterMaterialAuthoring.UnderwaterFadeSeconds;
        _lastUnderwaterTime = seconds;
        _underwaterMix = Math.Clamp(_underwaterMix + (_submerged ? step : -step), 0, 1);
        if (_underwaterMix == 0) return;
        gl.UseProgram(_underwaterProgram);
        gl.Uniform4(_underwaterTintLocation, _underwaterTint.X, _underwaterTint.Y, _underwaterTint.Z,
            _underwaterTint.W * _underwaterMix);
        gl.Disable(EnableCap.DepthTest);
        gl.Disable(EnableCap.CullFace);
        gl.Enable(EnableCap.Blend);
        gl.BlendEquation(BlendEquationModeEXT.FuncAdd);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        gl.BindVertexArray(_vertexArray);
        gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        gl.BindVertexArray(0);
        gl.Disable(EnableCap.Blend);
        gl.UseProgram(0);
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
        if (_underwaterProgram != 0) gl.DeleteProgram(_underwaterProgram);
        if (_framebuffer != 0) gl.DeleteFramebuffer(_framebuffer);
        if (_vertexArray != 0) gl.DeleteVertexArray(_vertexArray);
        ForgetHandles();
    }

    internal void ForgetHandles()
    {
        _waves.Clear();
        _program = _framebuffer = _vertexArray = _underwaterProgram = 0;
        _volumes.Clear();
        _submerged = false;
        _underwaterMix = 0;
        _lastUnderwaterTime = 0;
        Notice = null;
    }
}
