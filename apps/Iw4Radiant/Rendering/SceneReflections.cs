using System.Numerics;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Codecs.GfxMap;
using Silk.NET.OpenGL;

namespace Iw4Radiant.Rendering;

// Cached editor captures at authored probe origins, rendered only after a scene change.
internal sealed class SceneReflections
{
    private readonly List<uint> _textures = [];
    private uint _framebuffer, _linearTexture, _depth, _encodeProgram, _vertexArray;
    internal string? Notice { get; private set; }

    internal void Initialize(GL gl, string header)
    {
        _encodeProgram = SceneShaderProgram.Create(gl, header, "water-pass.vert", "reflection-encode.frag");
        gl.UseProgram(_encodeProgram);
        gl.Uniform1(gl.GetUniformLocation(_encodeProgram, "uSource"), 0);
        _framebuffer = gl.GenFramebuffer();
        _vertexArray = gl.GenVertexArray();
    }

    internal unsafe void Capture(GL gl, IReadOnlyList<GfxReflectionProbe> probes,
        Action<Matrix4x4, Vector3> drawScene)
    {
        Reload(gl);
        if (probes.Count <= 1)
        {
            Notice = "Add a reflection_probe to preview the water's surroundings.";
            return;
        }
        const int size = GfxReflectionProbeCodec.ReflectionProbeEdgeLength;
        try
        {
            gl.ActiveTexture(TextureUnit.Texture0);
            gl.BindBuffer(BufferTargetARB.PixelUnpackBuffer, 0);
            gl.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
            gl.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
            if (_linearTexture == 0)
            {
                _linearTexture = gl.GenTexture();
                gl.BindTexture(TextureTarget.Texture2D, _linearTexture);
                gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba16f, size, size, 0, PixelFormat.Rgba, PixelType.Float, null);
                gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
                gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
                _depth = gl.GenRenderbuffer();
                gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depth);
                gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent24, size, size);
                gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);
            }
            _textures.Add(0); // Native index zero is reserved for the default probe.
            for (int index = 1; index < probes.Count; index++)
            {
                GfxReflectionProbe probe = probes[index];
                var origin = new Vector3(probe.OffsetX, probe.OffsetY, probe.OffsetZ);
                uint cube = gl.GenTexture();
                _textures.Add(cube);
                gl.ActiveTexture(TextureUnit.Texture5);
                gl.BindTexture(TextureTarget.TextureCubeMap, cube);
                for (int face = 0; face < GfxReflectionProbeCodec.ReflectionProbeFaceCount; face++)
                    gl.TexImage2D((TextureTarget)((int)TextureTarget.TextureCubeMapPositiveX + face), 0,
                        InternalFormat.Rgba8, size, size, 0, PixelFormat.Rgba, PixelType.UnsignedByte, null);
                gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
                gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
                gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
                gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
                gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapR, (int)TextureWrapMode.ClampToEdge);
                // Preview uses the base capture; native convolved probe mip levels are not reproduced.
                gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMaxLevel, 0);
                gl.BindTexture(TextureTarget.TextureCubeMap, 0);
                for (int face = 0; face < GfxReflectionProbeCodec.ReflectionProbeFaceCount; face++)
                {
                    gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
                    gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                        TextureTarget.Texture2D, _linearTexture, 0);
                    gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
                        RenderbufferTarget.Renderbuffer, _depth);
                    RequireFramebuffer(gl);
                    gl.Viewport(0, 0, size, size);
                    gl.Disable(EnableCap.ScissorTest);
                    gl.Disable(EnableCap.Blend);
                    gl.ColorMask(true, true, true, true);
                    gl.DepthMask(true);
                    gl.ClearColor(0, 0, 0, 1);
                    gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                    gl.ColorMask(true, true, true, false);
                    drawScene(ViewProjection(origin, face), origin);
                    gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                        (TextureTarget)((int)TextureTarget.TextureCubeMapPositiveX + face), cube, 0);
                    gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
                        RenderbufferTarget.Renderbuffer, 0);
                    gl.Disable(EnableCap.DepthTest);
                    gl.Disable(EnableCap.Blend);
                    gl.Disable(EnableCap.CullFace);
                    gl.Disable(EnableCap.PolygonOffsetFill);
                    gl.ColorMask(true, true, true, true);
                    gl.UseProgram(_encodeProgram);
                    gl.BindVertexArray(_vertexArray);
                    gl.ActiveTexture(TextureUnit.Texture0);
                    gl.BindTexture(TextureTarget.Texture2D, _linearTexture);
                    gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
                }
            }
        }
        catch (Exception exception) when (SceneRenderer.IsRenderException(exception))
        {
            Reload(gl);
            Notice = $"Water reflection preview unavailable: {exception.Message}";
        }
        finally
        {
            gl.BindVertexArray(0);
            gl.ActiveTexture(TextureUnit.Texture0);
        }
    }

    internal bool Bind(GL gl, int probe)
    {
        uint texture = probe > 0 && probe < _textures.Count ? _textures[probe] : 0;
        gl.ActiveTexture(TextureUnit.Texture5);
        gl.BindTexture(TextureTarget.TextureCubeMap, texture);
        gl.ActiveTexture(TextureUnit.Texture0);
        return texture != 0;
    }

    private static void RequireFramebuffer(GL gl)
    {
        if (gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete)
            throw new NotSupportedException("The GPU reflection capture framebuffer is incomplete.");
    }

    private static Matrix4x4 ViewProjection(Vector3 origin, int face)
    {
        // BrushLightingScene.CubeDirection uses the standard cube axes, sampling face edges.
        // The 63/64 projection scale places GPU texel centers on those same directions.
        const float scale = (GfxReflectionProbeCodec.ReflectionProbeEdgeLength - 1f) / GfxReflectionProbeCodec.ReflectionProbeEdgeLength;
        // Infinite far plane avoids dropping distant authored geometry from captures.
        var projection = new Matrix4x4(scale, 0, 0, 0, 0, scale, 0, 0,
            0, 0, -1, -1, 0, 0, -0.02f, 0);
        return SceneShadows.CubeView(origin, face) * projection;
    }

    internal void Reload(GL gl)
    {
        foreach (uint texture in _textures)
            if (texture != 0) gl.DeleteTexture(texture);
        _textures.Clear();
        Notice = null;
    }

    internal void Clear(GL gl)
    {
        Reload(gl);
        if (_linearTexture != 0) gl.DeleteTexture(_linearTexture);
        if (_depth != 0) gl.DeleteRenderbuffer(_depth);
        if (_framebuffer != 0) gl.DeleteFramebuffer(_framebuffer);
        if (_encodeProgram != 0) gl.DeleteProgram(_encodeProgram);
        if (_vertexArray != 0) gl.DeleteVertexArray(_vertexArray);
        ForgetHandles();
    }

    internal void ForgetHandles()
    {
        _textures.Clear();
        _framebuffer = _linearTexture = _depth = _encodeProgram = _vertexArray = 0;
        Notice = null;
    }
}
