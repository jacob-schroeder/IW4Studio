using System.Numerics;
using Avalonia;
using Avalonia.OpenGL;
using Iw4Radiant.MapSource;
using Silk.NET.OpenGL;

namespace Iw4Radiant.Rendering;

internal sealed class SceneRenderer
{
    private GL? _gl;
    private uint _program, _vertexArray, _vertexBuffer;
    private uint _framebuffer, _colorBuffer, _depthBuffer;
    private uint _lineTexture;
    private PixelSize _renderSize;
    private int _viewProjectionLocation, _texturedLocation, _litLocation;
    private readonly List<(string Material, int Start, int Count, int WireStart, int WireCount)> _batches = [];
    private readonly SceneMaterialTextures _materialTextures = new();
    private int _glyphStart, _glyphCount, _gridStart, _gridCount, _outlineStart, _outlineCount, _axesStart, _axesCount;
    private bool _sceneDirty = true, _texturesDirty = true;

    internal string? Error { get; private set; } =
        "Camera is waiting for OpenGL. If it remains blank, a compatible OpenGL driver is required.";
    internal event EventHandler? StatusChanged;

    internal void RefreshScene() => _sceneDirty = true;
    internal void ReloadTextures() => _texturesDirty = true;

    internal unsafe void Initialize(GlInterface gl)
    {
        try
        {
            _gl = GL.GetApi(gl.GetProcAddress);
            string header = _gl.GetStringS(StringName.Version).Contains("OpenGL ES", StringComparison.Ordinal)
                ? "#version 300 es\nprecision highp float;\n" : "#version 150\n";
            _program = CreateProgram(_gl, header);
            _viewProjectionLocation = _gl.GetUniformLocation(_program, "uViewProjection");
            _texturedLocation = _gl.GetUniformLocation(_program, "uTextured");
            _litLocation = _gl.GetUniformLocation(_program, "uLit");
            _gl.UseProgram(_program);
            _gl.Uniform1(_gl.GetUniformLocation(_program, "uTexture"), 0);
            _lineTexture = _gl.GenTexture();
            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.BindTexture(TextureTarget.Texture2D, _lineTexture);
            _gl.BindBuffer(BufferTargetARB.PixelUnpackBuffer, 0);
            _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
            _gl.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
            uint white = uint.MaxValue;
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, 1, 1, 0,
                Silk.NET.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, &white);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            _vertexArray = _gl.GenVertexArray();
            _vertexBuffer = _gl.GenBuffer();
            _framebuffer = _gl.GenFramebuffer();
            _colorBuffer = _gl.GenRenderbuffer();
            _depthBuffer = _gl.GenRenderbuffer();
            _sceneDirty = _texturesDirty = true;
            PublishStatus(null);
        }
        catch (Exception exception) when (IsRenderException(exception))
        {
            ReleaseResources();
            PublishStatus($"OpenGL initialization: {exception.Message}");
        }
    }

    internal void ContextLost()
    {
        // Lost-context handles must never be deleted in a replacement context.
        _gl?.Dispose();
        _gl = null;
        ResetResources();
        PublishStatus("OpenGL context lost. Reopen the window to restore the camera viewport.");
    }

    internal unsafe void Render(PixelSize size, int framebuffer, Matrix4x4 viewProjection,
        MapDocument document, object? selection, Func<string, string?>? resolveTexturePath)
    {
        if (_gl is not { } gl || _program == 0)
            return;
        try
        {
            PrepareFramebuffer(gl, size);
            gl.Disable(EnableCap.ScissorTest);
            gl.Disable(EnableCap.Blend);
            gl.Disable(EnableCap.CullFace);
            gl.ColorMask(true, true, true, true);
            gl.DepthMask(true);
            gl.Viewport(0, 0, (uint)size.Width, (uint)size.Height);
            gl.ClearColor(0.075f, 0.09f, 0.11f, 1);
            gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            if (_texturesDirty)
            {
                _materialTextures.Reload(gl);
                _texturesDirty = false;
            }
            if (_sceneDirty)
                UploadScene(gl, document, selection);
            gl.UseProgram(_program);
            gl.UniformMatrix4(_viewProjectionLocation, 1, false, (float*)&viewProjection);
            gl.BindVertexArray(_vertexArray);
            gl.ActiveTexture(TextureUnit.Texture0);
            gl.BindTexture(TextureTarget.Texture2D, _lineTexture);
            gl.Enable(EnableCap.DepthTest);
            gl.DepthFunc(DepthFunction.Lequal);
            gl.Uniform1(_litLocation, 0);
            gl.Uniform1(_texturedLocation, 0);
            gl.DrawArrays(PrimitiveType.Lines, _gridStart, (uint)_gridCount);

            gl.Enable(EnableCap.PolygonOffsetFill);
            gl.PolygonOffset(1, 1);
            foreach (var batch in _batches)
            {
                uint texture = _materialTextures.GetTexture(gl, batch.Material, resolveTexturePath);
                if (texture == 0)
                {
                    gl.BindTexture(TextureTarget.Texture2D, _lineTexture);
                    gl.Uniform1(_litLocation, 0);
                    gl.Uniform1(_texturedLocation, 0);
                    gl.DrawArrays(PrimitiveType.Lines, batch.WireStart, (uint)batch.WireCount);
                    continue;
                }
                gl.BindTexture(TextureTarget.Texture2D, texture);
                gl.Uniform1(_litLocation, 1);
                gl.Uniform1(_texturedLocation, 1);
                gl.DrawArrays(PrimitiveType.Triangles, batch.Start, (uint)batch.Count);
            }
            gl.BindTexture(TextureTarget.Texture2D, _lineTexture);
            gl.Uniform1(_litLocation, 1);
            gl.Uniform1(_texturedLocation, 0);
            gl.DrawArrays(PrimitiveType.Triangles, _glyphStart, (uint)_glyphCount);
            gl.Disable(EnableCap.PolygonOffsetFill);
            gl.Uniform1(_litLocation, 0);
            gl.Uniform1(_texturedLocation, 0);
            gl.DrawArrays(PrimitiveType.Lines, _outlineStart, (uint)_outlineCount);
            gl.Disable(EnableCap.DepthTest);
            gl.DrawArrays(PrimitiveType.Lines, _axesStart, (uint)_axesCount);
            gl.BindVertexArray(0);
            gl.BindTexture(TextureTarget.Texture2D, 0);
            gl.UseProgram(0);
            gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _framebuffer);
            gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, (uint)framebuffer);
            gl.BlitFramebuffer(0, 0, size.Width, size.Height, 0, 0, size.Width, size.Height,
                ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
            PublishStatus(_materialTextures.Error);
        }
        catch (Exception exception) when (IsRenderException(exception))
        {
            PublishStatus($"Camera rendering: {exception.Message}");
        }
        finally
        {
            gl.Disable(EnableCap.PolygonOffsetFill);
            gl.Disable(EnableCap.DepthTest);
            gl.BindVertexArray(0);
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
            gl.BindTexture(TextureTarget.Texture2D, 0);
            gl.UseProgram(0);
            gl.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
            gl.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)framebuffer);
        }
    }

    private void PrepareFramebuffer(GL gl, PixelSize size)
    {
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        if (_renderSize == size)
            return;
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _colorBuffer);
        gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.Rgba8, (uint)size.Width, (uint)size.Height);
        gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            RenderbufferTarget.Renderbuffer, _colorBuffer);
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depthBuffer);
        gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent24, (uint)size.Width, (uint)size.Height);
        gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
            RenderbufferTarget.Renderbuffer, _depthBuffer);
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);
        if (gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete)
            throw new InvalidOperationException("The camera color/depth framebuffer is incomplete.");
        _renderSize = size;
    }

    private unsafe void UploadScene(GL gl, MapDocument document, object? selection)
    {
        var scene = new SceneGeometry(document, selection);
        _batches.Clear();
        _batches.AddRange(scene.Batches);
        _glyphStart = scene.GlyphStart;
        _glyphCount = scene.GlyphCount;
        _gridStart = scene.GridStart;
        _gridCount = scene.GridCount;
        _outlineStart = scene.OutlineStart;
        _outlineCount = scene.OutlineCount;
        _axesStart = scene.AxesStart;
        _axesCount = scene.AxesCount;
        _materialTextures.RemoveUnused(gl, scene.Batches.Select(batch => batch.Material));
        gl.BindVertexArray(_vertexArray);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);
        SceneVertex[] data = scene.Vertices;
        fixed (SceneVertex* pointer = data)
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(data.Length * sizeof(SceneVertex)), pointer, BufferUsageARB.StaticDraw);
        for (uint attribute = 0; attribute < 4; attribute++)
            gl.EnableVertexAttribArray(attribute);
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, (uint)sizeof(SceneVertex), (void*)0);
        gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, (uint)sizeof(SceneVertex), (void*)12);
        gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, (uint)sizeof(SceneVertex), (void*)24);
        gl.VertexAttribPointer(3, 3, VertexAttribPointerType.Float, false, (uint)sizeof(SceneVertex), (void*)32);
        gl.BindVertexArray(0);
        _sceneDirty = false;
    }

    private static uint CreateProgram(GL gl, string header)
    {
        uint vertex = 0, fragment = 0, program = 0;
        try
        {
            vertex = Compile(ShaderType.VertexShader, "scene.vert");
            fragment = Compile(ShaderType.FragmentShader, "scene.frag");
            program = gl.CreateProgram();
            gl.AttachShader(program, vertex);
            gl.AttachShader(program, fragment);
            gl.BindAttribLocation(program, 0, "aPosition");
            gl.BindAttribLocation(program, 1, "aNormal");
            gl.BindAttribLocation(program, 2, "aTexCoord");
            gl.BindAttribLocation(program, 3, "aColor");
            gl.LinkProgram(program);
            gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int linked);
            if (linked == 0)
                throw new InvalidOperationException($"Scene shader link failed: {gl.GetProgramInfoLog(program)}");
            return program;
        }
        catch
        {
            if (program != 0)
                gl.DeleteProgram(program);
            throw;
        }
        finally
        {
            if (vertex != 0)
                gl.DeleteShader(vertex);
            if (fragment != 0)
                gl.DeleteShader(fragment);
        }

        uint Compile(ShaderType type, string filename)
        {
            string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Shaders", filename));
            uint shader = gl.CreateShader(type);
            gl.ShaderSource(shader, header + source);
            gl.CompileShader(shader);
            gl.GetShader(shader, ShaderParameterName.CompileStatus, out int compiled);
            if (compiled != 0)
                return shader;
            string error = gl.GetShaderInfoLog(shader);
            gl.DeleteShader(shader);
            throw new InvalidOperationException($"{filename}: {error}");
        }
    }

    internal void ReleaseResources()
    {
        if (_gl is { } gl)
        {
            _materialTextures.Clear(gl);
            if (_vertexBuffer != 0) gl.DeleteBuffer(_vertexBuffer);
            if (_vertexArray != 0) gl.DeleteVertexArray(_vertexArray);
            if (_program != 0) gl.DeleteProgram(_program);
            if (_framebuffer != 0) gl.DeleteFramebuffer(_framebuffer);
            if (_colorBuffer != 0) gl.DeleteRenderbuffer(_colorBuffer);
            if (_depthBuffer != 0) gl.DeleteRenderbuffer(_depthBuffer);
            if (_lineTexture != 0) gl.DeleteTexture(_lineTexture);
            gl.Dispose();
        }
        _gl = null;
        ResetResources();
    }

    private void ResetResources()
    {
        _program = _vertexArray = _vertexBuffer = _framebuffer = _colorBuffer = _depthBuffer = _lineTexture = 0;
        _materialTextures.ForgetHandles();
        _batches.Clear();
        _renderSize = default;
        _sceneDirty = _texturesDirty = true;
    }

    private void PublishStatus(string? error)
    {
        if (Error == error)
            return;
        Error = error;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    internal static bool IsRenderException(Exception exception) => exception is IOException or
        InvalidOperationException or ArgumentException or NotSupportedException or OverflowException;
}
