using System.Numerics;
using Iw4Radiant.MapSource;
using Iw4Radiant.Materials;
using Silk.NET.OpenGL;

namespace Iw4Radiant.Rendering;

internal sealed class SceneShadows
{
    private const int PreferredFaceSize = 256;
    private uint _program, _framebuffer, _texture;
    private int _viewProjectionLocation, _lightPositionLocation, _alphaTestLocation;
    private int _atlasLocation, _gridLocation, _tileSizeLocation;
    private int _maximumTextureSize, _width, _height, _columns, _rows, _tileSize;

    internal bool IsAvailable { get; private set; }
    internal string? Notice { get; private set; }

    internal void Initialize(GL gl, uint sceneProgram, string shaderHeader)
    {
        _program = SceneShaderProgram.Create(gl, shaderHeader, "shadow.vert", "shadow.frag");
        _viewProjectionLocation = gl.GetUniformLocation(_program, "uViewProjection");
        _lightPositionLocation = gl.GetUniformLocation(_program, "uLightPositionRadius");
        _alphaTestLocation = gl.GetUniformLocation(_program, "uAlphaTest");
        gl.UseProgram(_program);
        gl.Uniform1(gl.GetUniformLocation(_program, "uTexture"), 0);
        _atlasLocation = gl.GetUniformLocation(sceneProgram, "uShadowAtlas");
        _gridLocation = gl.GetUniformLocation(sceneProgram, "uShadowGrid");
        _tileSizeLocation = gl.GetUniformLocation(sceneProgram, "uShadowTileSize");
        _maximumTextureSize = gl.GetInteger(GetPName.MaxTextureSize);
        _framebuffer = gl.GenFramebuffer();
        uint previousFramebuffer = (uint)gl.GetInteger(GetPName.DrawFramebufferBinding);
        try
        {
            PrepareAtlas(gl, 1, 1);
        }
        finally
        {
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, previousFramebuffer);
            gl.ActiveTexture(TextureUnit.Texture2);
            gl.BindTexture(TextureTarget.Texture2D, 0);
            gl.ActiveTexture(TextureUnit.Texture0);
        }
    }

    internal unsafe void Update(GL gl, IReadOnlyList<MapLight> lights, uint vertexArray,
        IReadOnlyList<(string Material, int Start, int Count, int WireStart, int WireCount)> batches,
        Func<string, MaterialSource?>? resolveMaterial, SceneMaterialTextures textures)
    {
        IsAvailable = false;
        Notice = null;
        int faces = checked(lights.Count * 6);
        _columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(faces)));
        _rows = Math.Max(1, (faces + _columns - 1) / _columns);
        _tileSize = Math.Min(PreferredFaceSize, _maximumTextureSize / Math.Max(_columns, _rows));
        if (_tileSize < 2)
        {
            Notice = "Light preview unavailable: this GPU cannot hold the required shadow views. Turn off Preview lights to view textures.";
            return;
        }

        try
        {
            PrepareAtlas(gl, lights.Count == 0 ? 1 : _columns * _tileSize,
                lights.Count == 0 ? 1 : _rows * _tileSize);
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
            gl.Disable(EnableCap.ScissorTest);
            gl.Disable(EnableCap.Blend);
            gl.Disable(EnableCap.CullFace);
            gl.Disable(EnableCap.PolygonOffsetFill);
            gl.Enable(EnableCap.DepthTest);
            gl.DepthFunc(DepthFunction.Less);
            gl.DepthMask(true);
            gl.ColorMask(false, false, false, false);
            float clearDepth = 1;
            gl.ClearBuffer(GLEnum.Depth, 0, &clearDepth);
            gl.UseProgram(_program);
            gl.BindVertexArray(vertexArray);
            gl.Enable(EnableCap.ScissorTest);
            for (int lightIndex = 0; lightIndex < lights.Count; lightIndex++)
            {
                MapLight light = lights[lightIndex];
                gl.Uniform4(_lightPositionLocation, light.Origin.X, light.Origin.Y, light.Origin.Z, light.Radius);
                for (int face = 0; face < 6; face++)
                {
                    int tile = lightIndex * 6 + face;
                    int x = tile % _columns * _tileSize, y = tile / _columns * _tileSize;
                    gl.Viewport(x, y, (uint)_tileSize, (uint)_tileSize);
                    gl.Scissor(x, y, (uint)_tileSize, (uint)_tileSize);
                    Matrix4x4 viewProjection = ViewProjection(light, face);
                    gl.UniformMatrix4(_viewProjectionLocation, 1, false, (float*)&viewProjection);
                    foreach (var batch in batches)
                        if (SceneMaterialDrawing.BindShadow(gl, _alphaTestLocation, batch.Material, resolveMaterial, textures))
                            gl.DrawArrays(PrimitiveType.Triangles, batch.Start, (uint)batch.Count);
                }
            }
            IsAvailable = true;
        }
        catch (Exception exception) when (SceneRenderer.IsRenderException(exception))
        {
            Notice = $"Light preview unavailable: {exception.Message} Turn off Preview lights to view textures.";
        }
        finally
        {
            gl.Disable(EnableCap.ScissorTest);
            gl.ColorMask(true, true, true, true);
            gl.BindVertexArray(0);
            gl.UseProgram(0);
            gl.ActiveTexture(TextureUnit.Texture2);
            gl.BindTexture(TextureTarget.Texture2D, 0);
            gl.ActiveTexture(TextureUnit.Texture0);
        }
    }

    internal void Bind(GL gl)
    {
        gl.Uniform1(_atlasLocation, 2);
        gl.Uniform2(_gridLocation, _columns, _rows);
        gl.Uniform1(_tileSizeLocation, _tileSize);
        gl.ActiveTexture(TextureUnit.Texture2);
        gl.BindTexture(TextureTarget.Texture2D, _texture);
        gl.ActiveTexture(TextureUnit.Texture0);
    }

    private unsafe void PrepareAtlas(GL gl, int width, int height)
    {
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        if (_width == width && _height == height)
            return;
        // A fresh texture makes allocation failure leave an incomplete framebuffer,
        // rather than accidentally reusing a differently sized previous atlas.
        _width = _height = 0;
        if (_texture != 0)
            gl.DeleteTexture(_texture);
        _texture = gl.GenTexture();
        gl.ActiveTexture(TextureUnit.Texture2);
        gl.BindTexture(TextureTarget.Texture2D, _texture);
        gl.BindBuffer(BufferTargetARB.PixelUnpackBuffer, 0);
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.DepthComponent24,
            (uint)width, (uint)height, 0, PixelFormat.DepthComponent, PixelType.UnsignedInt, null);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
            TextureTarget.Texture2D, _texture, 0);
        GLEnum noColor = GLEnum.None;
        gl.DrawBuffers(1, &noColor);
        gl.ReadBuffer(ReadBufferMode.None);
        if (gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete)
            throw new InvalidOperationException("The point/spot shadow framebuffer is incomplete.");
        _width = width;
        _height = height;
    }

    private static Matrix4x4 ViewProjection(MapLight light, int face)
    {
        float near = Math.Max(float.Epsilon, Math.Min(0.01f, light.Radius * 0.001f));
        float far = Math.Max(near * 2, light.Radius);
        // OpenGL -1..1 depth range, 90-degree square projection.
        float ratio = near / far;
        var projection = new Matrix4x4(1, 0, 0, 0, 0, 1, 0, 0,
            0, 0, -(1 + ratio) / (1 - ratio), -1, 0, 0, -2 * near / (1 - ratio), 0);
        return CubeView(light.Origin, face) * projection;
    }

    internal static Matrix4x4 CubeView(Vector3 origin, int face)
    {
        (Vector3 direction, Vector3 up) = face switch
        {
            0 => (Vector3.UnitX, -Vector3.UnitY),
            1 => (-Vector3.UnitX, -Vector3.UnitY),
            2 => (Vector3.UnitY, Vector3.UnitZ),
            3 => (-Vector3.UnitY, -Vector3.UnitZ),
            4 => (Vector3.UnitZ, -Vector3.UnitY),
            _ => (-Vector3.UnitZ, -Vector3.UnitY)
        };
        return Matrix4x4.CreateTranslation(-origin) * Matrix4x4.CreateLookAt(Vector3.Zero, direction, up);
    }

    internal void Clear(GL gl)
    {
        if (_texture != 0) gl.DeleteTexture(_texture);
        if (_framebuffer != 0) gl.DeleteFramebuffer(_framebuffer);
        if (_program != 0) gl.DeleteProgram(_program);
        ForgetHandles();
    }

    internal void ForgetHandles()
    {
        _texture = _framebuffer = _program = 0;
        _width = _height = _columns = _rows = _tileSize = 0;
        IsAvailable = false;
        Notice = null;
    }
}
