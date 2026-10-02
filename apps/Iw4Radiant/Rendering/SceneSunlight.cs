using System.Numerics;
using Iw4Radiant.Compilation;
using Iw4Radiant.MapSource;
using Iw4Radiant.Materials;
using Silk.NET.OpenGL;

namespace Iw4Radiant.Rendering;

internal sealed class SceneSunlight
{
    private const int PreferredShadowSize = 2048;
    private uint _program, _framebuffer;
    private int _shadowProjectionLocation, _enabledLocation, _directionLocation, _colorLocation, _alphaTestLocation;
    private int _sceneProjectionLocation, _textureLocation, _ambientScaleLocation, _maximumTextureSize;
    private readonly Dictionary<byte, SunShadow> _shadows = [];

    private sealed class SunShadow
    {
        internal uint Texture;
        internal int Size;
        internal Matrix4x4 ViewProjection;
        internal Vector3 Direction, Color;
        internal float AmbientScale;
        internal bool Available;
    }

    internal bool IsAvailable { get; private set; }
    internal string? Notice { get; private set; }

    internal void Initialize(GL gl, uint sceneProgram, string shaderHeader)
    {
        _program = SceneShaderProgram.Create(gl, shaderHeader, "shadow.vert", "sun-shadow.frag");
        _shadowProjectionLocation = gl.GetUniformLocation(_program, "uViewProjection");
        _alphaTestLocation = gl.GetUniformLocation(_program, "uAlphaTest");
        gl.UseProgram(_program);
        gl.Uniform1(gl.GetUniformLocation(_program, "uTexture"), 0);
        _enabledLocation = gl.GetUniformLocation(sceneProgram, "uSunEnabled");
        _directionLocation = gl.GetUniformLocation(sceneProgram, "uSunDirection");
        _colorLocation = gl.GetUniformLocation(sceneProgram, "uSunColor");
        _sceneProjectionLocation = gl.GetUniformLocation(sceneProgram, "uSunViewProjection");
        _textureLocation = gl.GetUniformLocation(sceneProgram, "uSunShadow");
        _ambientScaleLocation = gl.GetUniformLocation(sceneProgram, "uSunAmbientScale");
        _maximumTextureSize = gl.GetInteger(GetPName.MaxTextureSize);
        _framebuffer = gl.GenFramebuffer();
    }

    internal unsafe void Update(GL gl, MapEntity world, MapStageLighting? stages,
        IReadOnlyCollection<byte> usedSunIndices, (Vector3 Min, Vector3 Max)? worldBounds, uint vertexArray,
        IReadOnlyList<(string Material, int Start, int Count, int WireStart, int WireCount)> batches,
        Func<string, MaterialSource?>? resolveMaterial, SceneMaterialTextures textures, uint fallbackTexture)
    {
        IsAvailable = false;
        Notice = null;
        foreach (SunShadow shadow in _shadows.Values) shadow.Available = false;
        if (!MapSunProperties.TryRead(world, out MapSunProperties? source, out string? error))
        {
            Notice = $"Sunlight preview unavailable: {error}";
            return;
        }
        if (source is not { }) return;
        if (worldBounds is not { } bounds)
        {
            Notice = "Sunlight preview unavailable: no non-sky geometry to shadow.";
            return;
        }
        int size = Math.Min(_maximumTextureSize,
            (int)(PreferredShadowSize / MathF.Sqrt(Math.Max(usedSunIndices.Count, 1))));
        if (size < 2)
        {
            Notice = "Sunlight preview unavailable: this GPU cannot allocate a sun shadow map.";
            return;
        }

        uint previousDrawFramebuffer = (uint)gl.GetInteger(GetPName.DrawFramebufferBinding);
        uint previousReadFramebuffer = (uint)gl.GetInteger(GetPName.ReadFramebufferBinding);
        try
        {
            gl.Disable(EnableCap.ScissorTest);
            gl.Disable(EnableCap.Blend);
            gl.Disable(EnableCap.CullFace);
            gl.Disable(EnableCap.PolygonOffsetFill);
            gl.Enable(EnableCap.DepthTest);
            gl.DepthFunc(DepthFunction.Less);
            gl.DepthMask(true);
            gl.ColorMask(false, false, false, false);
            gl.UseProgram(_program);
            gl.BindVertexArray(vertexArray);
            foreach (byte index in _shadows.Keys.Where(index => !usedSunIndices.Contains(index)).ToArray())
            {
                if (_shadows[index].Texture != 0) gl.DeleteTexture(_shadows[index].Texture);
                _shadows.Remove(index);
            }
            foreach (byte index in usedSunIndices.Order())
            {
                MapSunProperties sun = stages?.Sun(index) ?? source.Value;
                if (!_shadows.TryGetValue(index, out SunShadow? shadow))
                    _shadows.Add(index, shadow = new SunShadow());
                shadow.Available = false;
                shadow.AmbientScale = stages?.AmbientScale(index) ?? 1;
                shadow.Direction = sun.Direction;
                shadow.Color = sun.Color * sun.Intensity;
                if (!Finite(shadow.Color))
                    throw new InvalidOperationException("The sun color and intensity exceed the preview's numeric range.");
                if (sun.Intensity == 0 || sun.Color == Vector3.Zero) continue;
                shadow.ViewProjection = ViewProjection(bounds, shadow.Direction);
                PrepareShadow(gl, shadow, size);
                gl.Viewport(0, 0, (uint)size, (uint)size);
                float clearDepth = 1;
                gl.ClearBuffer(GLEnum.Depth, 0, &clearDepth);
                Matrix4x4 projection = shadow.ViewProjection;
                gl.UniformMatrix4(_shadowProjectionLocation, 1, false, (float*)&projection);
                foreach (var batch in batches)
                    if (resolveMaterial?.Invoke(batch.Material)?.IsSky != true &&
                        SceneMaterialDrawing.BindShadow(gl, _alphaTestLocation, batch.Material, resolveMaterial, textures, fallbackTexture))
                        gl.DrawArrays(PrimitiveType.Triangles, batch.Start, (uint)batch.Count);
                shadow.Available = true;
                IsAvailable = true;
            }
        }
        catch (Exception exception) when (SceneRenderer.IsRenderException(exception))
        {
            Notice = $"Sunlight preview unavailable: {exception.Message}";
            IsAvailable = false;
            foreach (SunShadow shadow in _shadows.Values) shadow.Available = false;
        }
        finally
        {
            gl.ColorMask(true, true, true, true);
            gl.BindVertexArray(0);
            gl.UseProgram(0);
            gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, previousDrawFramebuffer);
            gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, previousReadFramebuffer);
            gl.ActiveTexture(TextureUnit.Texture3);
            gl.BindTexture(TextureTarget.Texture2D, 0);
            gl.ActiveTexture(TextureUnit.Texture0);
        }
    }

    internal unsafe void Bind(GL gl, uint fallbackTexture, byte sunIndex = 1, bool enabled = true)
    {
        _shadows.TryGetValue(sunIndex, out SunShadow? shadow);
        gl.Uniform1(_enabledLocation, enabled && shadow?.Available == true ? 1 : 0);
        Vector3 direction = shadow?.Direction ?? Vector3.Zero;
        Vector3 color = shadow?.Color ?? Vector3.Zero;
        gl.Uniform3(_directionLocation, direction.X, direction.Y, direction.Z);
        gl.Uniform3(_colorLocation, color.X, color.Y, color.Z);
        gl.Uniform1(_ambientScaleLocation, shadow?.AmbientScale ?? 1);
        Matrix4x4 projection = shadow?.ViewProjection ?? Matrix4x4.Identity;
        gl.UniformMatrix4(_sceneProjectionLocation, 1, false, (float*)&projection);
        gl.Uniform1(_textureLocation, 3);
        gl.ActiveTexture(TextureUnit.Texture3);
        gl.BindTexture(TextureTarget.Texture2D, shadow is { Texture: > 0 } ? shadow.Texture : fallbackTexture);
        gl.ActiveTexture(TextureUnit.Texture0);
    }

    private unsafe void PrepareShadow(GL gl, SunShadow shadow, int size)
    {
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        if (shadow.Size == size)
        {
            gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
                TextureTarget.Texture2D, shadow.Texture, 0);
            return;
        }
        shadow.Size = 0;
        if (shadow.Texture != 0) gl.DeleteTexture(shadow.Texture);
        shadow.Texture = gl.GenTexture();
        gl.ActiveTexture(TextureUnit.Texture3);
        gl.BindTexture(TextureTarget.Texture2D, shadow.Texture);
        gl.BindBuffer(BufferTargetARB.PixelUnpackBuffer, 0);
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.DepthComponent24,
            (uint)size, (uint)size, 0, PixelFormat.DepthComponent, PixelType.UnsignedInt, null);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
            TextureTarget.Texture2D, shadow.Texture, 0);
        GLEnum noColor = GLEnum.None;
        gl.DrawBuffers(1, &noColor);
        gl.ReadBuffer(ReadBufferMode.None);
        if (gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete)
            throw new InvalidOperationException("The sun shadow framebuffer is incomplete.");
        shadow.Size = size;
    }

    private static Matrix4x4 ViewProjection((Vector3 Min, Vector3 Max) bounds, Vector3 toSun)
    {
        if (!Finite(bounds.Min) || !Finite(bounds.Max) || bounds.Min.X > bounds.Max.X ||
            bounds.Min.Y > bounds.Max.Y || bounds.Min.Z > bounds.Max.Z)
            throw new InvalidOperationException("The non-sky geometry bounds are invalid.");
        Vector3 center = bounds.Min * 0.5f + bounds.Max * 0.5f;
        Vector3 up = MathF.Abs(Vector3.Dot(toSun, Vector3.UnitZ)) < 0.95f ? Vector3.UnitZ : Vector3.UnitY;
        Matrix4x4 orientation = Matrix4x4.CreateLookAt(Vector3.Zero, -toSun, up);
        Vector3 minimum = new(float.PositiveInfinity), maximum = new(float.NegativeInfinity);
        for (int corner = 0; corner < 8; corner++)
        {
            Vector3 position = new((corner & 1) == 0 ? bounds.Min.X : bounds.Max.X,
                (corner & 2) == 0 ? bounds.Min.Y : bounds.Max.Y,
                (corner & 4) == 0 ? bounds.Min.Z : bounds.Max.Z);
            Vector3 transformed = Vector3.Transform(position - center, orientation);
            minimum = Vector3.Min(minimum, transformed);
            maximum = Vector3.Max(maximum, transformed);
        }
        float padding = Math.Max(1, (maximum - minimum).Length() * 0.005f);
        minimum -= new Vector3(padding);
        maximum += new Vector3(padding);
        Vector3 span = maximum - minimum;
        if (!Finite(minimum) || !Finite(maximum) || !Finite(span) || span.X <= 0 || span.Y <= 0 || span.Z <= 0)
            throw new InvalidOperationException("The non-sky geometry is too large for a sun shadow projection.");
        Vector3 midpoint = minimum * 0.5f + maximum * 0.5f;
        // Row-vector matrix uploaded without transpose, as in the scene/cube shadows.
        // OpenGL depth is -1 at the toward-sun (+view Z) bound and +1 at the far bound.
        var projection = new Matrix4x4(2 / span.X, 0, 0, 0, 0, 2 / span.Y, 0, 0,
            0, 0, -2 / span.Z, 0, -2 * midpoint.X / span.X, -2 * midpoint.Y / span.Y,
            2 * midpoint.Z / span.Z, 1);
        Matrix4x4 result = Matrix4x4.CreateTranslation(-center) * orientation * projection;
        if (!float.IsFinite(result.M41) || !float.IsFinite(result.M42) || !float.IsFinite(result.M43))
            throw new InvalidOperationException("The non-sky geometry is too far from the origin for a sun shadow projection.");
        return result;
    }

    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    internal void Clear(GL gl)
    {
        foreach (SunShadow shadow in _shadows.Values)
            if (shadow.Texture != 0) gl.DeleteTexture(shadow.Texture);
        if (_framebuffer != 0) gl.DeleteFramebuffer(_framebuffer);
        if (_program != 0) gl.DeleteProgram(_program);
        ForgetHandles();
    }

    internal void ForgetHandles()
    {
        _program = _framebuffer = 0;
        _shadows.Clear();
        IsAvailable = false;
        Notice = null;
    }
}
