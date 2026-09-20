using System.Numerics;
using Avalonia;
using Avalonia.OpenGL;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.GfxMap;
using Iw4Radiant.Compilation;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;
using Iw4Radiant.Materials;
using Silk.NET.OpenGL;

namespace Iw4Radiant.Rendering;

internal sealed class SceneRenderer
{
    private GL? _gl;
    private uint _program, _vertexArray, _vertexBuffer;
    private uint _framebuffer, _colorBuffer, _depthBuffer;
    private uint _lineTexture;
    private PixelSize _renderSize;
    private int _viewProjectionLocation, _texturedLocation, _litLocation, _alphaTestLocation, _premultiplyAlphaLocation,
        _ignoreVertexColorLocation, _waterPreviewLocation, _eyeLocation, _linearCaptureLocation, _hasWaterReflectionLocation,
        _cubicClipLocation, _cubicClipCenterLocation, _cubicClipDistanceLocation;
    private readonly List<(string Material, int Start, int Count, int WireStart, int WireCount)> _batches = [];
    private readonly List<(string Material, int Start, int Count, int WireStart, int WireCount)> _surfaceBatches = [];
    private readonly Dictionary<string, MaterialSurfaceState> _surfaceStates = new(StringComparer.Ordinal);
    private readonly List<(string Material, int Start, Vector3 Center)> _transparentTriangles = [];
    private readonly SceneMaterialTextures _materialTextures = new();
    private readonly SceneLighting _lighting = new();
    private readonly SceneShadows _shadows = new();
    private readonly SceneSunlight _sunlight = new();
    private readonly SceneSkies _skies = new();
    private readonly SceneWater _water = new();
    private readonly SceneReflections _reflections = new();
    private readonly List<string> _waterMaterials = [];
    private readonly List<GfxReflectionProbe> _probeOrigins = [];
    private readonly Dictionary<int, byte> _waterProbes = [];
    private bool _reflectionsDirty = true, _reflectionLighting;
    private (Vector3 Min, Vector3 Max)? _surfaceBounds;
    private int _glyphStart, _glyphCount, _gridStart, _gridCount, _outlineStart, _outlineCount, _axesStart, _axesCount;
    private bool _sceneDirty = true, _texturesDirty = true, _shadowsDirty = true;

    internal string? Error { get; private set; } =
        "Camera is waiting for OpenGL. If it remains blank, a compatible OpenGL driver is required.";
    internal bool HasAnimatedWater { get; private set; }
    internal event EventHandler? StatusChanged;

    internal void RefreshScene() => _sceneDirty = true;
    internal void ReloadTextures() => _texturesDirty = _sceneDirty = true;

    internal unsafe void Initialize(GlInterface gl)
    {
        try
        {
            _gl = GL.GetApi(gl.GetProcAddress);
            string header = _gl.GetStringS(StringName.Version).Contains("OpenGL ES", StringComparison.Ordinal)
                ? "#version 300 es\nprecision highp float;\nprecision highp int;\nprecision highp sampler2D;\n" : "#version 150\n";
            _program = SceneShaderProgram.Create(_gl, header, "scene.vert", "scene.frag");
            _viewProjectionLocation = _gl.GetUniformLocation(_program, "uViewProjection");
            _texturedLocation = _gl.GetUniformLocation(_program, "uTextured");
            _litLocation = _gl.GetUniformLocation(_program, "uLit");
            _alphaTestLocation = _gl.GetUniformLocation(_program, "uAlphaTest");
            _premultiplyAlphaLocation = _gl.GetUniformLocation(_program, "uPremultiplyAlpha");
            _ignoreVertexColorLocation = _gl.GetUniformLocation(_program, "uIgnoreVertexColor");
            _waterPreviewLocation = _gl.GetUniformLocation(_program, "uWaterPreview");
            _eyeLocation = _gl.GetUniformLocation(_program, "uEye");
            _linearCaptureLocation = _gl.GetUniformLocation(_program, "uLinearCapture");
            _hasWaterReflectionLocation = _gl.GetUniformLocation(_program, "uHasWaterReflection");
            _cubicClipLocation = _gl.GetUniformLocation(_program, "uCubicClip");
            _cubicClipCenterLocation = _gl.GetUniformLocation(_program, "uCubicClipCenter");
            _cubicClipDistanceLocation = _gl.GetUniformLocation(_program, "uCubicClipDistance");
            _lighting.Initialize(_gl, _program);
            _shadows.Initialize(_gl, _program, header);
            _sunlight.Initialize(_gl, _program, header);
            _skies.Initialize(_gl, header);
            _water.Initialize(_gl, _program, header);
            _reflections.Initialize(_gl, header);
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
            _sceneDirty = _texturesDirty = _shadowsDirty = true;
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

    internal unsafe void Render(PixelSize size, int framebuffer, Matrix4x4 viewProjection, Vector3 eye,
        EditorSession session,
        Func<string, MaterialSource?>? resolveMaterial, bool previewLighting)
    {
        if (_gl is not { } gl || _program == 0)
            return;
        try
        {
            MapDocument document = session.Scene.Document;
            if (_texturesDirty)
            {
                _materialTextures.Reload(gl);
                _skies.Reload(gl);
                _water.Reload(gl);
                _reflectionsDirty = true;
                _texturesDirty = false;
            }
            if (_sceneDirty)
                UploadScene(gl, session, resolveMaterial);
            if (previewLighting && _shadowsDirty)
            {
                _shadows.Update(gl, _lighting.Lights, _vertexArray, _surfaceBatches, resolveMaterial, _materialTextures);
                _sunlight.Update(gl, document.World, _surfaceBounds, _vertexArray, _surfaceBatches,
                    resolveMaterial, _materialTextures);
                _shadowsDirty = false;
            }
            _water.Update(gl, _waterMaterials, resolveMaterial);
            if (HasAnimatedWater && (_reflectionsDirty || _reflectionLighting != previewLighting))
            {
                _reflections.Capture(gl, _probeOrigins, (matrix, origin) =>
                    RenderReflectionFace(gl, matrix, origin, resolveMaterial, previewLighting));
                _reflectionsDirty = false;
                _reflectionLighting = previewLighting;
            }
            PrepareFramebuffer(gl, size);
            gl.Disable(EnableCap.ScissorTest);
            gl.Disable(EnableCap.Blend);
            gl.Disable(EnableCap.CullFace);
            gl.FrontFace(FrontFaceDirection.Ccw);
            gl.ColorMask(true, true, true, true);
            gl.DepthMask(true);
            gl.Viewport(0, 0, (uint)size.Width, (uint)size.Height);
            gl.ClearColor(0.075f, 0.09f, 0.11f, 1);
            gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            // Materials blend inside an opaque viewport, never against the desktop behind it.
            gl.ColorMask(true, true, true, false);
            gl.UseProgram(_program);
            gl.UniformMatrix4(_viewProjectionLocation, 1, false, (float*)&viewProjection);
            gl.Uniform1(_cubicClipLocation, session.CubicClipEnabled ? 1 : 0);
            gl.Uniform3(_cubicClipCenterLocation, eye.X, eye.Y, eye.Z);
            gl.Uniform1(_cubicClipDistanceLocation, session.CubicClipDistance);
            gl.Uniform3(_eyeLocation, eye.X, eye.Y, eye.Z);
            gl.Uniform1(_linearCaptureLocation, 0);
            _lighting.Bind(gl, _shadows.IsAvailable);
            _shadows.Bind(gl);
            _sunlight.Bind(gl);
            gl.BindVertexArray(_vertexArray);
            gl.ActiveTexture(TextureUnit.Texture0);
            gl.BindTexture(TextureTarget.Texture2D, _lineTexture);
            gl.Enable(EnableCap.DepthTest);
            gl.DepthFunc(DepthFunction.Lequal);
            gl.Uniform1(_litLocation, 0);
            gl.Uniform1(_texturedLocation, 0);
            gl.Uniform1(_alphaTestLocation, 0);
            gl.Uniform1(_premultiplyAlphaLocation, 0);
            gl.Uniform1(_waterPreviewLocation, 0);
            gl.DrawArrays(PrimitiveType.Lines, _gridStart, (uint)_gridCount);

            gl.Enable(EnableCap.PolygonOffsetFill);
            gl.PolygonOffset(1, 1);
            RenderSurfaces(gl, resolveMaterial, previewLighting, session.AlphaPreviewEnabled, eye, transparent: false);
            _skies.Render(gl, viewProjection, eye, _vertexArray, _batches, resolveMaterial);
            gl.BindTexture(TextureTarget.Texture2D, _lineTexture);
            gl.Uniform1(_litLocation, 0);
            gl.Uniform1(_texturedLocation, 0);
            gl.DrawArrays(PrimitiveType.Triangles, _glyphStart, (uint)_glyphCount);
            RenderSurfaces(gl, resolveMaterial, previewLighting, session.AlphaPreviewEnabled, eye, transparent: true);
            gl.BindTexture(TextureTarget.Texture2D, _lineTexture);
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
            string?[] notices = [session.Scene.Notice, _materialTextures.Error, _skies.Notice, _water.Notice,
                HasAnimatedWater ? _reflections.Notice : null,
                previewLighting ? _lighting.GetNotice(_sunlight.IsAvailable) : null,
                previewLighting ? _shadows.Notice : null, previewLighting ? _sunlight.Notice : null];
            string notice = string.Join('\n', notices.Where(value => !string.IsNullOrEmpty(value)));
            PublishStatus(notice.Length == 0 ? null : notice);
        }
        catch (Exception exception) when (IsRenderException(exception))
        {
            PublishStatus($"Camera rendering: {exception.Message}");
        }
        finally
        {
            gl.Disable(EnableCap.Blend);
            gl.Disable(EnableCap.CullFace);
            gl.DepthMask(true);
            gl.ColorMask(true, true, true, true);
            gl.Disable(EnableCap.PolygonOffsetFill);
            gl.Disable(EnableCap.DepthTest);
            gl.BindVertexArray(0);
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
            gl.ActiveTexture(TextureUnit.Texture5);
            gl.BindTexture(TextureTarget.TextureCubeMap, 0);
            gl.ActiveTexture(TextureUnit.Texture4);
            gl.BindTexture(TextureTarget.Texture2D, 0);
            gl.ActiveTexture(TextureUnit.Texture3);
            gl.BindTexture(TextureTarget.Texture2D, 0);
            gl.ActiveTexture(TextureUnit.Texture2);
            gl.BindTexture(TextureTarget.Texture2D, 0);
            gl.ActiveTexture(TextureUnit.Texture1);
            gl.BindTexture(TextureTarget.Texture2D, 0);
            gl.ActiveTexture(TextureUnit.Texture0);
            gl.BindTexture(TextureTarget.Texture2D, 0);
            gl.BindTexture(TextureTarget.TextureCubeMap, 0);
            gl.UseProgram(0);
            gl.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
            gl.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)framebuffer);
        }
    }

    private void RenderSurfaces(GL gl, Func<string, MaterialSource?>? resolveMaterial, bool previewLighting,
        bool previewAlpha, Vector3 eye, bool transparent, bool capture = false)
    {
        var textures = new Dictionary<string, uint>(StringComparer.Ordinal);
        foreach (var batch in _surfaceBatches)
        {
            MaterialSurfaceState state = _surfaceStates[batch.Material];
            bool drawTransparent = previewAlpha && (state.IsBlended || !state.DepthWrite);
            if (drawTransparent != transparent) continue;
            MaterialSource? source = resolveMaterial?.Invoke(batch.Material);
            bool water = source?.IsWater == true;
            if (capture && water) continue;
            uint texture = water ? (_water.IsAvailable(batch.Material) ? _lineTexture : 0) : _materialTextures.GetTexture(gl, batch.Material, resolveMaterial);
            if (texture == 0)
            {
                ResetSurfaceState(gl);
                gl.BindTexture(TextureTarget.Texture2D, _lineTexture);
                gl.Uniform1(_litLocation, 0);
                gl.Uniform1(_texturedLocation, 0);
                gl.DrawArrays(PrimitiveType.Lines, batch.WireStart, (uint)batch.WireCount);
                continue;
            }
            if (transparent)
            {
                textures.Add(batch.Material, texture);
                continue;
            }
            SceneMaterialDrawing.Apply(gl, previewAlpha ? state : OpaquePreview(state),
                _alphaTestLocation, _premultiplyAlphaLocation, _ignoreVertexColorLocation);
            gl.BindTexture(TextureTarget.Texture2D, texture);
            gl.Uniform1(_waterPreviewLocation, water ? 1 : 0);
            if (water) _water.Bind(gl, batch.Material);
            gl.Uniform1(_litLocation, previewLighting ? 1 : 0);
            gl.Uniform1(_texturedLocation, 1);
            if (water)
            {
                for (int start = batch.Start; start < batch.Start + batch.Count; start += 3)
                {
                    BindWaterReflection(gl, start);
                    gl.DrawArrays(PrimitiveType.Triangles, start, 3);
                }
            }
            else gl.DrawArrays(PrimitiveType.Triangles, batch.Start, (uint)batch.Count);
        }
        if (transparent)
        {
            string? material = null;
            // Respect material sort keys, then order individual translucent triangles back to front.
            foreach (var triangle in _transparentTriangles.OrderBy(triangle => _surfaceStates[triangle.Material].SortKey)
                         .ThenByDescending(triangle => Vector3.DistanceSquared(eye, triangle.Center)))
            {
                if (!textures.TryGetValue(triangle.Material, out uint texture)) continue;
                if (material != triangle.Material)
                {
                    MaterialSource? source = resolveMaterial?.Invoke(triangle.Material);
                    bool water = source?.IsWater == true;
                    SceneMaterialDrawing.Apply(gl, _surfaceStates[triangle.Material], _alphaTestLocation, _premultiplyAlphaLocation, _ignoreVertexColorLocation);
                    gl.BindTexture(TextureTarget.Texture2D, texture);
                    gl.Uniform1(_waterPreviewLocation, water ? 1 : 0);
                    if (water) _water.Bind(gl, triangle.Material);
                    gl.Uniform1(_litLocation, previewLighting ? 1 : 0);
                    gl.Uniform1(_texturedLocation, 1);
                    material = triangle.Material;
                }
                if (_waterProbes.ContainsKey(triangle.Start)) BindWaterReflection(gl, triangle.Start);
                gl.DrawArrays(PrimitiveType.Triangles, triangle.Start, 3);
            }
        }
        ResetSurfaceState(gl);
    }

    private void BindWaterReflection(GL gl, int start)
    {
        bool available = _reflections.Bind(gl, _waterProbes.GetValueOrDefault(start));
        gl.Uniform1(_hasWaterReflectionLocation, available ? 1 : 0);
    }

    private unsafe void RenderReflectionFace(GL gl, Matrix4x4 matrix, Vector3 origin,
        Func<string, MaterialSource?>? resolveMaterial, bool previewLighting)
    {
        gl.UseProgram(_program);
        gl.UniformMatrix4(_viewProjectionLocation, 1, false, (float*)&matrix);
        gl.Uniform3(_eyeLocation, origin.X, origin.Y, origin.Z);
        gl.Uniform1(_cubicClipLocation, 0);
        gl.Uniform1(_linearCaptureLocation, 1);
        _lighting.Bind(gl, _shadows.IsAvailable);
        _shadows.Bind(gl);
        _sunlight.Bind(gl);
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindVertexArray(_vertexArray);
        gl.FrontFace(FrontFaceDirection.Ccw);
        RenderSurfaces(gl, resolveMaterial, previewLighting, true, origin, false, capture: true);
        _skies.Render(gl, matrix, origin, _vertexArray, _batches, resolveMaterial, linearCapture: true);
        RenderSurfaces(gl, resolveMaterial, previewLighting, true, origin, true, capture: true);
    }

    private static MaterialSurfaceState OpaquePreview(MaterialSurfaceState state) => state with
    {
        BlendOperation = GfxBlendOperation.Disabled,
        Source = GfxBlend.One,
        Destination = GfxBlend.Zero,
        DepthWrite = true
    };

    private void ResetSurfaceState(GL gl)
    {
        gl.Disable(EnableCap.Blend);
        gl.Disable(EnableCap.CullFace);
        gl.Enable(EnableCap.DepthTest);
        gl.DepthFunc(DepthFunction.Lequal);
        gl.DepthMask(true);
        gl.Uniform1(_alphaTestLocation, 0);
        gl.Uniform1(_premultiplyAlphaLocation, 0);
        gl.Uniform1(_waterPreviewLocation, 0);
        gl.Disable(EnableCap.PolygonOffsetFill);
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

    private unsafe void UploadScene(GL gl, EditorSession session, Func<string, MaterialSource?>? resolveMaterial)
    {
        var scene = new SceneGeometry(session.Scene, session.TransformMode, session.Tool);
        _lighting.Update(gl, session.Scene);
        _batches.Clear();
        _batches.AddRange(scene.Batches);
        _surfaceBatches.Clear();
        _surfaceBatches.AddRange(scene.Batches.Where(batch => resolveMaterial?.Invoke(batch.Material)?.IsSky != true));
        _surfaceStates.Clear();
        HasAnimatedWater = false;
        _waterMaterials.Clear();
        foreach (var batch in _surfaceBatches)
        {
            _surfaceStates.Add(batch.Material, resolveMaterial?.Invoke(batch.Material)?.Surface ?? MaterialSurfaceState.Opaque);
            if (resolveMaterial?.Invoke(batch.Material)?.IsWater == true)
            {
                HasAnimatedWater = true;
                _waterMaterials.Add(batch.Material);
            }
        }
        _water.RemoveUnused(gl, _waterMaterials);
        if (!HasAnimatedWater) _reflections.Reload(gl);
        _probeOrigins.Clear();
        _probeOrigins.Add(new GfxReflectionProbe(0, 0, 0));
        foreach (MapEntity entity in session.Scene.Document.Entities)
            if (entity.ClassName == "reflection_probe" && entity.TryGetOrigin(out Vector3 origin))
                _probeOrigins.Add(new GfxReflectionProbe(origin.X, origin.Y, origin.Z));
        _waterProbes.Clear();
        _glyphStart = scene.GlyphStart;
        _glyphCount = scene.GlyphCount;
        _gridStart = scene.GridStart;
        _gridCount = scene.GridCount;
        _outlineStart = scene.OutlineStart;
        _outlineCount = scene.OutlineCount;
        _axesStart = scene.AxesStart;
        _axesCount = scene.AxesCount;
        _materialTextures.RemoveUnused(gl, _surfaceBatches.Select(batch => batch.Material));
        _skies.RemoveUnused(gl, scene.Batches.Where(batch => resolveMaterial?.Invoke(batch.Material)?.IsSky == true)
            .Select(batch => batch.Material));
        gl.BindVertexArray(_vertexArray);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);
        SceneVertex[] data = scene.Vertices;
        _transparentTriangles.Clear();
        foreach (var batch in _surfaceBatches)
            if (_surfaceStates[batch.Material] is { } state && (state.IsBlended || !state.DepthWrite))
                for (int index = batch.Start; index < batch.Start + batch.Count; index += 3)
                    _transparentTriangles.Add((batch.Material, index,
                        data[index].Position / 3 + data[index + 1].Position / 3 + data[index + 2].Position / 3));
        foreach (var batch in _surfaceBatches)
            if (_waterMaterials.Contains(batch.Material))
                for (int start = batch.Start; start < batch.Start + batch.Count; start += 3)
                {
                    Vector3 center = scene.SurfaceCenters.GetValueOrDefault(start,
                        (data[start].Position + data[start + 1].Position + data[start + 2].Position) / 3);
                    _waterProbes.Add(start, BrushRenderCompiler.NearestProbe(center, _probeOrigins));
                }
        _surfaceBounds = null;
        foreach (var batch in _surfaceBatches)
        for (int index = batch.Start; index < batch.Start + batch.Count; index++)
        {
            Vector3 position = data[index].Position;
            _surfaceBounds = _surfaceBounds is { } bounds
                ? (Vector3.Min(bounds.Min, position), Vector3.Max(bounds.Max, position)) : (position, position);
        }
        fixed (SceneVertex* pointer = data)
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(data.Length * sizeof(SceneVertex)), pointer, BufferUsageARB.StaticDraw);
        for (uint attribute = 0; attribute < 4; attribute++)
            gl.EnableVertexAttribArray(attribute);
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, (uint)sizeof(SceneVertex), (void*)0);
        gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, (uint)sizeof(SceneVertex), (void*)12);
        gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, (uint)sizeof(SceneVertex), (void*)24);
        gl.VertexAttribPointer(3, 4, VertexAttribPointerType.Float, false, (uint)sizeof(SceneVertex), (void*)32);
        gl.BindVertexArray(0);
        _sceneDirty = false;
        _shadowsDirty = _reflectionsDirty = true;
    }

    internal void ReleaseResources()
    {
        if (_gl is { } gl)
        {
            _materialTextures.Clear(gl);
            _lighting.Clear(gl);
            _shadows.Clear(gl);
            _sunlight.Clear(gl);
            _skies.Clear(gl);
            _water.Clear(gl);
            _reflections.Clear(gl);
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
        _lighting.ForgetHandle();
        _shadows.ForgetHandles();
        _sunlight.ForgetHandles();
        _skies.ForgetHandles();
        _water.ForgetHandles();
        _reflections.ForgetHandles();
        _waterMaterials.Clear();
        _waterProbes.Clear();
        _probeOrigins.Clear();
        _reflectionsDirty = true;
        _batches.Clear();
        _surfaceBatches.Clear();
        _surfaceStates.Clear();
        _transparentTriangles.Clear();
        HasAnimatedWater = false;
        _surfaceBounds = null;
        _renderSize = default;
        _sceneDirty = _texturesDirty = _shadowsDirty = true;
    }

    private void PublishStatus(string? error)
    {
        if (Error == error)
            return;
        Error = error;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    internal static bool IsRenderException(Exception exception) => exception is IOException or FormatException or
        InvalidOperationException or ArgumentException or NotSupportedException or OverflowException;
}
