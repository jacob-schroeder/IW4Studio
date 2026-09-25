using System.Numerics;
using System.Text.Json;
using Avalonia;
using Avalonia.OpenGL;
using IW4.Formats.SourceFormat.Material;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.GfxMap;
using Iw4Radiant.Compilation;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;
using Iw4Radiant.Materials;
using Silk.NET.OpenGL;

namespace Iw4Radiant.Rendering;

internal readonly record struct FilmPreview(float Brightness, float Contrast, float Desaturation,
    Vector3 LightTint, Vector3 DarkTint)
{
    internal static FilmPreview Neutral => new(0, 1, 0, Vector3.One, Vector3.One);
    internal bool IsNeutral => this == Neutral;
}

internal readonly record struct FogPreview(bool Enabled, Vector3 Color, float StartDistance, float HalfDistance)
{
    internal static FogPreview Disabled => new(false, new Vector3(0.55f, 0.62f, 0.68f), 512, 1024);
}

internal sealed class SceneRenderer
{
    private GL? _gl;
    private uint _program, _vertexArray, _vertexBuffer;
    private uint _fxVertexArray, _fxVertexBuffer;
    private uint _framebuffer, _colorTexture, _depthBuffer, _filmProgram, _filmVertexArray;
    private int _filmSizeLocation, _filmBrightnessLocation, _filmContrastLocation, _filmDesaturationLocation,
        _filmLightTintLocation, _filmDarkTintLocation;
    private uint _lineTexture;
    private PixelSize _renderSize;
    private int _viewProjectionLocation, _texturedLocation, _litLocation, _alphaTestLocation, _premultiplyAlphaLocation,
        _ignoreVertexColorLocation, _waterPreviewLocation, _eyeLocation, _linearCaptureLocation, _hasWaterReflectionLocation,
        _cubicClipLocation, _cubicClipCenterLocation, _cubicClipDistanceLocation, _modelLocation, _normalTransformLocation,
        _fogEnabledLocation, _fogColorLocation, _fogStartLocation, _fogDensityLocation;
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
    private FxSpritePreview? _fxPreview;
    private string? _fxPreviewNotice;
    private readonly List<(FxSpritePreview Preview, MapEntity Owner, Vector3 Origin, Matrix4x4 Orientation)> _mapFxPreviews = [];
    private readonly Dictionary<MapEntity, (string Name, FxSpritePreview Preview)> _mapFxInstances =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, (FxSpritePreview? Preview, string? Notice)> _mapFxAssets =
        new(StringComparer.OrdinalIgnoreCase);
    private string? _mapFxAssetRoot;
    private string? _mapFxSourceNotice, _mapFxPreviewNotice;
    private readonly Dictionary<string, MaterialSource?> _fxMaterials = new(StringComparer.Ordinal);
    private bool _mapFxPaused;
    private readonly List<string> _waterMaterials = [];
    private readonly Dictionary<string, (Vector3 Min, Vector3 Max)> _waterMaterialBounds = new(StringComparer.Ordinal);
    private readonly List<GfxReflectionProbe> _probeOrigins = [];
    private readonly Dictionary<int, byte> _waterProbes = [];
    private readonly Dictionary<string, List<(int Start, int Count)>> _waterDrawRanges = new(StringComparer.Ordinal);
    private bool _reflectionsDirty = true, _reflectionLighting;
    private (Vector3 Min, Vector3 Max)? _surfaceBounds;
    private int _glyphStart, _glyphCount, _gridStart, _gridCount, _highlightStart, _highlightCount,
        _outlineStart, _outlineCount, _axesStart, _axesCount, _leakPathStart, _leakPathCount;
    private bool _sceneDirty = true, _texturesDirty = true, _shadowsDirty = true;
    private SceneVertex[] _movePreviewVertices = [];
    private readonly List<(int Start, int Count)> _movePreviewRanges = [];
    private MapEntity? _movePreviewSource;
    private Vector3 _movePreviewOrigin, _movePreviewApplied;
    private bool _movePreviewDirty;
    private readonly Dictionary<XModelSource, PreviewModelMesh> _previewMeshes = new(ReferenceEqualityComparer.Instance);
    private IReadOnlyList<(MapEntity Entity, XModelSource Model)> _foliagePreview = [];
    private bool _previewMeshesDirty;
    private CompiledBspPreview? _compiledPreview;
    internal CompiledBspPreview? CompiledPreview
    {
        get => _compiledPreview;
        set { _compiledPreview = value; _sceneDirty = _texturesDirty = true; }
    }
    internal LeakPath? LeakPath { get; set; }
    internal int LeakPointIndex { get; set; }

    internal string? Error { get; private set; } =
        "Camera is waiting for OpenGL. If it remains blank, a compatible OpenGL driver is required.";
    internal bool HasRenderingError { get; private set; }
    internal bool HasAnimatedWater { get; private set; }
    internal bool HasVisibleAnimatedWater { get; private set; }
    internal bool HasActiveFxPreview => _fxPreview is not null;
    internal bool HasPlayingFxPreview => _fxPreview?.IsPlaying == true;
    internal bool IsFxPreviewPaused => _fxPreview?.IsPaused == true;
    internal bool IsFxPreviewFinished => _fxPreview?.IsFinished == true;
    internal bool IsFxPreviewLooping => _fxPreview?.IsLooping == true;
    internal bool IsFxPreviewRepeating => _fxPreview?.Repeat == true;
    internal string? FxPreviewNotice => _fxPreviewNotice;
    internal (Vector3 Min, Vector3 Max)? FxPreviewBounds => _fxPreview?.PreviewBounds;
    internal bool HasActiveMapFxPreview => _mapFxPreviews.Count != 0;
    internal bool HasPlayingMapFxPreview => _mapFxPreviews.Any(item => item.Preview.IsPlaying);
    internal bool IsMapFxPreviewPaused => HasActiveMapFxPreview && _mapFxPaused;
    internal bool IsMapFxPreviewFinished => HasActiveMapFxPreview &&
        _mapFxPreviews.All(item => item.Preview.IsFinished);
    internal string? MapFxPreviewNotice => _mapFxPreviewNotice;
    internal event EventHandler? StatusChanged;

    internal void RefreshScene() => _sceneDirty = true;
    internal void PreviewPointEntityMove() => _movePreviewDirty = true;
    internal void ReloadTextures()
    {
        _texturesDirty = _sceneDirty = true;
        _fxMaterials.Clear();
    }
    internal string? SetFxPreview(string? sourceDirectory, string? assetName, Vector3 origin, Matrix4x4 orientation)
    {
        StopFxPreview();
        _fxMaterials.Clear();
        if (string.IsNullOrWhiteSpace(sourceDirectory) || string.IsNullOrWhiteSpace(assetName))
            return null;
        try
        {
            FxSpritePreview preview = FxSpritePreview.Load(sourceDirectory, assetName, origin, orientation);
            if (preview.HasDrawableElements)
            {
                _fxPreview = preview;
                _sceneDirty = true;
                _fxPreviewNotice = preview.Notice;
            }
            else
                _fxPreviewNotice = $"FX '{assetName}' has no supported visual components. {preview.Notice}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           ArgumentException or NotSupportedException or JsonException or OverflowException)
        {
            _fxPreviewNotice = $"FX '{assetName}': {exception.Message}";
        }
        return _fxPreviewNotice;
    }

    internal void StopFxPreview()
    {
        if (_fxPreview is not null) _sceneDirty = true;
        _fxPreview = null;
        _fxPreviewNotice = null;
    }

    internal void SetFxPreviewPaused(bool paused) => _fxPreview?.SetPaused(paused);

    internal void RestartFxPreview() => _fxPreview?.Restart();

    internal void SetFxPreviewRepeat(bool repeat)
    {
        if (_fxPreview is not null) _fxPreview.Repeat = repeat;
    }

    internal void SetMapFxPreviewPaused(bool paused)
    {
        if (!HasActiveMapFxPreview) return;
        _mapFxPaused = paused;
        foreach (var (_, preview) in _mapFxInstances.Values)
            preview.SetPaused(paused);
    }

    internal void RestartMapFxPreview()
    {
        foreach (var (_, preview) in _mapFxInstances.Values)
            preview.Restart();
    }

    internal void UpdateMapFxPreviewTransforms()
    {
        for (int index = 0; index < _mapFxPreviews.Count; index++)
        {
            var (preview, owner, origin, orientation) = _mapFxPreviews[index];
            if (!owner.TryGetOrigin(out Vector3 currentOrigin)) continue;
            Matrix4x4 currentOrientation = EntityOrientation.Rotation(owner);
            if (origin != currentOrigin || orientation != currentOrientation)
                _mapFxPreviews[index] = (preview, owner, currentOrigin, currentOrientation);
        }
    }

    internal string? SetMapFxPreview(string? sourceDirectory,
        IReadOnlyList<(MapEntity Owner, string Name, Vector3 Origin, Matrix4x4 Orientation)> emitters)
    {
        _sceneDirty = true;
        _mapFxPreviews.Clear();
        _fxMaterials.Clear();
        _mapFxSourceNotice = _mapFxPreviewNotice = null;
        if (_mapFxAssetRoot != sourceDirectory || emitters.Count == 0)
        {
            _mapFxAssets.Clear();
            _mapFxInstances.Clear();
            _mapFxAssetRoot = sourceDirectory;
        }
        if (emitters.Count == 0)
        {
            _mapFxPaused = false;
            return null;
        }
        if (string.IsNullOrWhiteSpace(sourceDirectory))
        {
            _mapFxPaused = false;
            return _mapFxPreviewNotice = "Choose a raw library in the FX tab to preview placed effects.";
        }

        var described = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var notices = new List<string>();
        foreach (var (owner, name, origin, orientation) in emitters)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                if (!notices.Contains("A placed FX has no asset name."))
                    notices.Add("A placed FX has no asset name.");
                continue;
            }
            if (!_mapFxAssets.TryGetValue(name, out var asset))
            {
                FxSpritePreview? loaded;
                string? notice = null;
                try
                {
                    loaded = FxSpritePreview.Load(sourceDirectory, name, Vector3.Zero, Matrix4x4.Identity);
                    if (!loaded.HasDrawableElements)
                        notice = $"FX '{name}' has no supported visual components. {loaded.Notice}";
                    else
                    {
                        loaded.SetPaused(_mapFxPaused);
                        if (!string.IsNullOrEmpty(loaded.Notice))
                            notice = $"FX '{name}': {loaded.Notice}";
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                                   ArgumentException or NotSupportedException or JsonException or OverflowException)
                {
                    loaded = null;
                    notice = $"FX '{name}': {exception.Message}";
                }
                asset = (loaded, notice);
                _mapFxAssets.Add(name, asset);
            }
            if (described.Add(name) && asset.Notice is { } message) notices.Add(message);
            if (asset.Preview is { HasDrawableElements: true } preview)
            {
                if (!_mapFxInstances.TryGetValue(owner, out var instance) || instance.Name != name)
                {
                    instance = (name, preview.CreateInstance());
                    instance.Preview.SetPaused(_mapFxPaused);
                    _mapFxInstances[owner] = instance;
                }
                _mapFxPreviews.Add((instance.Preview, owner, origin, orientation));
            }
        }
        var activeOwners = new HashSet<MapEntity>(_mapFxPreviews.Select(item => item.Owner),
            ReferenceEqualityComparer.Instance);
        foreach (MapEntity obsolete in _mapFxInstances.Keys.Where(owner => !activeOwners.Contains(owner)).ToArray())
            _mapFxInstances.Remove(obsolete);
        foreach (string obsolete in _mapFxAssets.Keys.Where(name => !described.Contains(name)).ToArray())
            _mapFxAssets.Remove(obsolete);
        _mapFxSourceNotice = string.Join("; ", notices.Take(4));
        if (notices.Count > 4) _mapFxSourceNotice += $"; {notices.Count - 4} more FX notices";
        _mapFxPreviewNotice = _mapFxSourceNotice;
        if (!HasActiveMapFxPreview) _mapFxPaused = false;
        return _mapFxPreviewNotice;
    }
    internal void SetFoliagePreview(IReadOnlyList<(MapEntity Entity, XModelSource Model)> preview)
    {
        _foliagePreview = preview;
        _previewMeshesDirty = true;
    }

    internal unsafe void Initialize(GlInterface gl)
    {
        try
        {
            _gl = GL.GetApi(gl.GetProcAddress);
            string header = _gl.GetStringS(StringName.Version).Contains("OpenGL ES", StringComparison.Ordinal)
                ? "#version 300 es\nprecision highp float;\nprecision highp int;\nprecision highp sampler2D;\n" : "#version 150\n";
            _program = SceneShaderProgram.Create(_gl, header, "scene.vert", "scene.frag");
            _filmProgram = SceneShaderProgram.Create(_gl, header, "water-pass.vert", "film-preview.frag");
            _filmSizeLocation = _gl.GetUniformLocation(_filmProgram, "uSceneSize");
            _filmBrightnessLocation = _gl.GetUniformLocation(_filmProgram, "uBrightness");
            _filmContrastLocation = _gl.GetUniformLocation(_filmProgram, "uContrast");
            _filmDesaturationLocation = _gl.GetUniformLocation(_filmProgram, "uDesaturation");
            _filmLightTintLocation = _gl.GetUniformLocation(_filmProgram, "uLightTint");
            _filmDarkTintLocation = _gl.GetUniformLocation(_filmProgram, "uDarkTint");
            _gl.UseProgram(_filmProgram);
            _gl.Uniform1(_gl.GetUniformLocation(_filmProgram, "uScene"), 0);
            _filmVertexArray = _gl.GenVertexArray();
            _viewProjectionLocation = _gl.GetUniformLocation(_program, "uViewProjection");
            _modelLocation = _gl.GetUniformLocation(_program, "uModel");
            _normalTransformLocation = _gl.GetUniformLocation(_program, "uNormalTransform");
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
            _fogEnabledLocation = _gl.GetUniformLocation(_program, "uFogEnabled");
            _fogColorLocation = _gl.GetUniformLocation(_program, "uFogColor");
            _fogStartLocation = _gl.GetUniformLocation(_program, "uFogStart");
            _fogDensityLocation = _gl.GetUniformLocation(_program, "uFogDensity");
            _lighting.Initialize(_gl, _program);
            _shadows.Initialize(_gl, _program, header);
            _sunlight.Initialize(_gl, _program, header);
            _skies.Initialize(_gl, header);
            _water.Initialize(_gl, _program, header);
            _reflections.Initialize(_gl, header);
            _gl.UseProgram(_program);
            Matrix4x4 identity = Matrix4x4.Identity;
            _gl.UniformMatrix4(_modelLocation, 1, false, (float*)&identity);
            _gl.UniformMatrix4(_normalTransformLocation, 1, false, (float*)&identity);
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
            _fxVertexArray = _gl.GenVertexArray();
            _fxVertexBuffer = _gl.GenBuffer();
            _gl.BindVertexArray(_fxVertexArray);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _fxVertexBuffer);
            for (uint attribute = 0; attribute < 4; attribute++) _gl.EnableVertexAttribArray(attribute);
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, (uint)sizeof(SceneVertex), (void*)0);
            _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, (uint)sizeof(SceneVertex), (void*)12);
            _gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, (uint)sizeof(SceneVertex), (void*)24);
            _gl.VertexAttribPointer(3, 4, VertexAttribPointerType.Float, false, (uint)sizeof(SceneVertex), (void*)32);
            _gl.BindVertexArray(0);
            _framebuffer = _gl.GenFramebuffer();
            _colorTexture = _gl.GenTexture();
            _depthBuffer = _gl.GenRenderbuffer();
            _sceneDirty = _texturesDirty = _shadowsDirty = true;
            PublishStatus(null);
        }
        catch (Exception exception) when (IsRenderException(exception))
        {
            ReleaseResources();
            PublishStatus($"OpenGL initialization: {exception.Message}", failure: true);
        }
    }

    internal void ContextLost()
    {
        // Lost-context handles must never be deleted in a replacement context.
        _gl?.Dispose();
        _gl = null;
        ResetResources();
        PublishStatus("OpenGL context lost. Reopen the window to restore the camera viewport.", failure: true);
    }

    internal unsafe void Render(PixelSize size, int framebuffer, Matrix4x4 viewProjection, Vector3 eye,
        EditorSession session,
        Func<string, MaterialSource?>? resolveMaterial, bool previewLighting, FilmPreview filmPreview, FogPreview fogPreview)
    {
        if (_gl is not { } gl || _program == 0)
            return;
        try
        {
            MapDocument document = session.Scene.Document;
            RemoveUnusedPreviewMeshes(gl);
            if (_texturesDirty)
            {
                _materialTextures.Reload(gl);
                _skies.Reload(gl);
                _water.Reload(gl);
                _reflectionsDirty = true;
                _texturesDirty = false;
            }
            if (_sceneDirty)
            {
                if (_compiledPreview is { } compiled) UploadCompiledScene(gl, compiled, resolveMaterial);
                else UploadScene(gl, session, resolveMaterial);
            }
            if (_movePreviewDirty) UpdatePointEntityMove(gl);
            if (previewLighting && _shadowsDirty && !session.DeferPreviewLighting)
            {
                _shadows.Update(gl, _lighting.Lights, _vertexArray, _surfaceBatches, resolveMaterial, _materialTextures);
                _sunlight.Update(gl, document.World, _surfaceBounds, _vertexArray, _surfaceBatches,
                    resolveMaterial, _materialTextures);
                _shadowsDirty = false;
            }
            var visibleWater = new List<string>();
            foreach (string material in _waterMaterials)
                if (!_waterMaterialBounds.TryGetValue(material, out var bounds) ||
                    Contains(bounds, eye) || IntersectsClip(bounds, viewProjection))
                    visibleWater.Add(material);
            HasVisibleAnimatedWater = visibleWater.Count != 0;
            _water.Update(gl, _reflectionsDirty && !session.DeferPreviewLighting ? _waterMaterials : visibleWater,
                resolveMaterial);
            if (HasAnimatedWater && !session.DeferPreviewLighting &&
                (_reflectionsDirty || _reflectionLighting != previewLighting))
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
            Matrix4x4 identity = Matrix4x4.Identity;
            gl.UniformMatrix4(_modelLocation, 1, false, (float*)&identity);
            gl.UniformMatrix4(_normalTransformLocation, 1, false, (float*)&identity);
            gl.Uniform1(_cubicClipLocation, _compiledPreview is null && session.CubicClipEnabled ? 1 : 0);
            gl.Uniform3(_cubicClipCenterLocation, eye.X, eye.Y, eye.Z);
            gl.Uniform1(_cubicClipDistanceLocation, session.CubicClipDistance);
            gl.Uniform3(_eyeLocation, eye.X, eye.Y, eye.Z);
            gl.Uniform1(_linearCaptureLocation, 0);
            gl.Uniform1(_fogEnabledLocation, 0);
            gl.Uniform3(_fogColorLocation, fogPreview.Color.X, fogPreview.Color.Y, fogPreview.Color.Z);
            gl.Uniform1(_fogStartLocation, fogPreview.StartDistance);
            gl.Uniform1(_fogDensityLocation, MathF.Log(2) / fogPreview.HalfDistance);
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
            gl.Uniform1(_fogEnabledLocation, fogPreview.Enabled ? 1 : 0);
            RenderSurfaces(gl, resolveMaterial, previewLighting, session.AlphaPreviewEnabled, eye, transparent: false);
            if (_compiledPreview is null)
                RenderFoliagePreview(gl, resolveMaterial, previewLighting, session.AlphaPreviewEnabled, eye, transparent: false);
            gl.Uniform1(_fogEnabledLocation, 0);
            _skies.Render(gl, viewProjection, eye, _vertexArray, _batches, resolveMaterial);
            gl.BindTexture(TextureTarget.Texture2D, _lineTexture);
            gl.Uniform1(_litLocation, 0);
            gl.Uniform1(_texturedLocation, 0);
            gl.DrawArrays(PrimitiveType.Triangles, _glyphStart, (uint)_glyphCount);
            gl.Uniform1(_fogEnabledLocation, fogPreview.Enabled ? 1 : 0);
            RenderSurfaces(gl, resolveMaterial, previewLighting, session.AlphaPreviewEnabled, eye, transparent: true);
            if (_compiledPreview is null)
                RenderFoliagePreview(gl, resolveMaterial, previewLighting, session.AlphaPreviewEnabled, eye, transparent: true);
            gl.Uniform1(_fogEnabledLocation, 0);
            RenderFxPreview(gl, resolveMaterial, eye);
            RenderMapFxPreview(gl, resolveMaterial, eye, viewProjection);
            RenderFaceHighlights(gl);
            gl.BindTexture(TextureTarget.Texture2D, _lineTexture);
            gl.Disable(EnableCap.PolygonOffsetFill);
            gl.Uniform1(_litLocation, 0);
            gl.Uniform1(_texturedLocation, 0);
            gl.DrawArrays(PrimitiveType.Lines, _outlineStart, (uint)_outlineCount);
            gl.Disable(EnableCap.DepthTest);
            gl.DrawArrays(PrimitiveType.Lines, _axesStart, (uint)_axesCount);
            gl.Uniform1(_cubicClipLocation, 0);
            gl.DrawArrays(PrimitiveType.Lines, _leakPathStart, (uint)_leakPathCount);
            if (_compiledPreview is null) _water.RenderUnderwater(gl, eye);
            gl.BindVertexArray(0);
            gl.BindTexture(TextureTarget.Texture2D, 0);
            gl.UseProgram(0);
            if (filmPreview.IsNeutral)
            {
                gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _framebuffer);
                gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, (uint)framebuffer);
                gl.BlitFramebuffer(0, 0, size.Width, size.Height, 0, 0, size.Width, size.Height,
                    ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
            }
            else
                DrawFilmPreview(gl, size, framebuffer, filmPreview);
            string?[] notices = _compiledPreview is not null
                ? [_fxPreviewNotice, _mapFxPreviewNotice, _materialTextures.Error]
                : [session.Scene.Notice, _fxPreviewNotice, _mapFxPreviewNotice, _materialTextures.Error,
                    _skies.Notice, _water.Notice,
                    HasAnimatedWater ? _reflections.Notice : null,
                    previewLighting ? _lighting.GetNotice(_sunlight.IsAvailable) : null,
                    previewLighting ? _shadows.Notice : null, previewLighting ? _sunlight.Notice : null];
            string notice = string.Join('\n', notices.Where(value => !string.IsNullOrEmpty(value)));
            PublishStatus(notice.Length == 0 ? null : notice);
        }
        catch (Exception exception) when (IsRenderException(exception))
        {
            PublishStatus($"Camera rendering: {exception.Message}", failure: true);
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

    private static bool Contains((Vector3 Min, Vector3 Max) bounds, Vector3 point) =>
        point.X >= bounds.Min.X && point.X <= bounds.Max.X &&
        point.Y >= bounds.Min.Y && point.Y <= bounds.Max.Y &&
        point.Z >= bounds.Min.Z && point.Z <= bounds.Max.Z;

    private static bool IntersectsClip((Vector3 Min, Vector3 Max) bounds, Matrix4x4 viewProjection)
    {
        // Reject only when all eight corners lie outside the same clip plane.
        int outsideEveryCorner = 0b11_1111;
        for (int corner = 0; corner < 8; corner++)
        {
            Vector3 point = new(corner % 2 == 0 ? bounds.Min.X : bounds.Max.X,
                (corner & 2) == 0 ? bounds.Min.Y : bounds.Max.Y,
                (corner & 4) == 0 ? bounds.Min.Z : bounds.Max.Z);
            Vector4 clip = Vector4.Transform(new Vector4(point, 1), viewProjection);
            int outside = 0;
            if (clip.X < -clip.W) outside |= 1;
            if (clip.X > clip.W) outside |= 2;
            if (clip.Y < -clip.W) outside |= 4;
            if (clip.Y > clip.W) outside |= 8;
            if (clip.Z < -clip.W) outside |= 16;
            if (clip.Z > clip.W) outside |= 32;
            outsideEveryCorner &= outside;
            if (outsideEveryCorner == 0) return true;
        }
        return false;
    }

    private unsafe void RenderFxPreview(GL gl, Func<string, MaterialSource?>? resolveMaterial, Vector3 eye)
    {
        if (_fxPreview is not { HasDrawableElements: true } preview) return;
        string? materialNotice = null;
        var available = new Dictionary<string, MaterialSource>(StringComparer.Ordinal);
        foreach (string material in preview.Materials)
        {
            MaterialSource? source = ResolveFxMaterial(material, resolveMaterial);
            if (source is null)
                materialNotice ??= $"FX material '{material}' is unavailable; load its materials/images catalog.";
            else if (source.Surface.SortKey == (int)MaterialSortKey.Distortion)
                materialNotice ??= "Heat distortion is omitted from this preview.";
            else available.Add(material, source);
        }
        gl.BindVertexArray(_fxVertexArray);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _fxVertexBuffer);
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.Uniform1(_waterPreviewLocation, 0);
        gl.Uniform1(_litLocation, 0);
        gl.Uniform1(_texturedLocation, 1);
        foreach (var (material, vertices) in preview.Sample(eye))
        {
            if (!available.TryGetValue(material, out MaterialSource? source)) continue;
            uint texture = _materialTextures.GetTexture(gl, material, resolveMaterial);
            if (texture == 0) continue;
            SceneMaterialDrawing.Apply(gl, source.Surface, _alphaTestLocation, _premultiplyAlphaLocation,
                _ignoreVertexColorLocation);
            gl.Disable(EnableCap.CullFace);
            gl.Uniform1(_ignoreVertexColorLocation, 0);
            gl.BindTexture(TextureTarget.Texture2D, texture);
            fixed (SceneVertex* pointer = vertices)
                gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * sizeof(SceneVertex)), pointer,
                    BufferUsageARB.DynamicDraw);
            gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)vertices.Length);
        }
        _fxPreviewNotice = string.Join("; ", new[] { preview.Notice, materialNotice }
            .Where(notice => !string.IsNullOrEmpty(notice)));
        if (_fxPreviewNotice.Length == 0) _fxPreviewNotice = null;
        gl.BindVertexArray(_vertexArray);
        ResetSurfaceState(gl);
    }

    private unsafe void RenderMapFxPreview(GL gl, Func<string, MaterialSource?>? resolveMaterial,
        Vector3 eye, Matrix4x4 viewProjection)
    {
        if (_mapFxPreviews.Count == 0) return;
        const int maximumSprites = 512;
        int remaining = maximumSprites;
        bool capped = false;
        string? materialNotice = null;
        var materials = new Dictionary<string, MaterialSource?>(StringComparer.Ordinal);
        var ordered = _mapFxPreviews
            .OrderByDescending(item => IsNearView(item.Origin, viewProjection))
            .ThenBy(item => Vector3.DistanceSquared(item.Origin, eye)).ToArray();
        int emittersRemaining = ordered.Length;
        gl.BindVertexArray(_fxVertexArray);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _fxVertexBuffer);
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.Uniform1(_waterPreviewLocation, 0);
        gl.Uniform1(_litLocation, 0);
        gl.Uniform1(_texturedLocation, 1);
        foreach (var (preview, owner, origin, orientation) in ordered)
        {
            if (remaining == 0)
            {
                capped = true;
                break;
            }
            // Share the frame budget so one dense fire does not hide every other emitter.
            int budget = Math.Min(128, Math.Max(1, remaining / emittersRemaining--));
            foreach (var (material, vertices) in preview.Sample(eye, origin, orientation, budget,
                         applyDistanceFade: true,
                         variationSeed: (uint)System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(owner)))
            {
                remaining = Math.Max(0, remaining - (vertices.Length + 5) / 6);
                if (!materials.TryGetValue(material, out MaterialSource? source))
                {
                    source = ResolveFxMaterial(material, resolveMaterial);
                    if (source is null)
                    {
                        source = null;
                        materialNotice ??= $"FX material '{material}' is unavailable; load its materials/images catalog.";
                    }
                    materials.Add(material, source);
                }
                if (source is null) continue;
                // Distortion colorMap pixels encode offsets into the resolved scene,
                // not visible color. Drawing them as RGBA paints red/green over the FX.
                if (source.Surface.SortKey == (int)MaterialSortKey.Distortion)
                {
                    materialNotice ??= "Heat distortion is omitted from this preview.";
                    continue;
                }
                uint texture = _materialTextures.GetTexture(gl, material, resolveMaterial);
                if (texture == 0)
                {
                    materialNotice ??= $"FX material '{material}' could not be loaded.";
                    continue;
                }
                SceneMaterialDrawing.Apply(gl, source.Surface, _alphaTestLocation, _premultiplyAlphaLocation,
                    _ignoreVertexColorLocation);
                gl.Disable(EnableCap.CullFace);
                gl.Uniform1(_ignoreVertexColorLocation, 0);
                gl.BindTexture(TextureTarget.Texture2D, texture);
                fixed (SceneVertex* pointer = vertices)
                    gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * sizeof(SceneVertex)), pointer,
                        BufferUsageARB.DynamicDraw);
                gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)vertices.Length);
            }
        }
        _mapFxPreviewNotice = string.Join("; ", new[]
        {
            _mapFxSourceNotice, materialNotice,
            capped ? $"Map FX preview is limited to {maximumSprites} sprites per frame." : null
        }.Where(notice => !string.IsNullOrEmpty(notice)));
        if (_mapFxPreviewNotice.Length == 0) _mapFxPreviewNotice = null;
        gl.BindVertexArray(_vertexArray);
        ResetSurfaceState(gl);
    }

    private MaterialSource? ResolveFxMaterial(string name, Func<string, MaterialSource?>? resolveMaterial)
    {
        if (_fxMaterials.TryGetValue(name, out var cached)) return cached;
        MaterialSource? source = resolveMaterial?.Invoke(name);
        if (source is null || string.IsNullOrEmpty(source.TechniqueSet) ||
            string.IsNullOrEmpty(source.ImagePath) || !File.Exists(source.ImagePath))
            source = null;
        _fxMaterials.Add(name, source);
        return source;
    }

    private static bool IsNearView(Vector3 origin, Matrix4x4 viewProjection)
    {
        Vector4 clip = Vector4.Transform(new Vector4(origin, 1), viewProjection);
        return clip.W > 0 && MathF.Abs(clip.X) <= clip.W * 2 &&
               MathF.Abs(clip.Y) <= clip.W * 2 && MathF.Abs(clip.Z) <= clip.W * 2;
    }

    private unsafe void RenderFoliagePreview(GL gl, Func<string, MaterialSource?>? resolveMaterial,
        bool previewLighting, bool previewAlpha, Vector3 eye, bool transparent)
    {
        if (_foliagePreview.Count == 0) return;
        gl.Uniform1(_waterPreviewLocation, 0);
        IEnumerable<(MapEntity Entity, XModelSource Model)> instances = transparent
            ? _foliagePreview.OrderByDescending(item => Vector3.DistanceSquared(
                EditorSession.EntityOrigin(item.Entity), eye)) : _foliagePreview;
        foreach (var (entity, model) in instances)
        {
            if (!_previewMeshes.TryGetValue(model, out PreviewModelMesh? mesh))
            {
                mesh = PreviewModelMesh.Create(gl, model);
                _previewMeshes.Add(model, mesh);
            }
            Matrix4x4 transform = XModelGeometry.Transform(entity);
            if (!Matrix4x4.Invert(transform, out Matrix4x4 inverse)) continue;
            Matrix4x4 normalTransform = Matrix4x4.Transpose(inverse);
            gl.UniformMatrix4(_modelLocation, 1, false, (float*)&transform);
            gl.UniformMatrix4(_normalTransformLocation, 1, false, (float*)&normalTransform);
            gl.BindVertexArray(mesh.VertexArray);
            foreach (var batch in mesh.Batches)
            {
                MaterialSource? source = resolveMaterial?.Invoke(batch.Material);
                MaterialSurfaceState state = source?.Surface ?? MaterialSurfaceState.Opaque;
                bool drawTransparent = previewAlpha && (state.IsBlended || !state.DepthWrite);
                if (drawTransparent != transparent) continue;
                uint texture = _materialTextures.GetTexture(gl, batch.Material, resolveMaterial);
                if (texture == 0) continue;
                SceneMaterialDrawing.Apply(gl, previewAlpha ? state : OpaquePreview(state), _alphaTestLocation,
                    _premultiplyAlphaLocation, _ignoreVertexColorLocation);
                gl.BindTexture(TextureTarget.Texture2D, texture);
                gl.Uniform1(_litLocation, previewLighting ? 1 : 0);
                gl.Uniform1(_texturedLocation, 1);
                gl.DrawArrays(PrimitiveType.Triangles, batch.Start, (uint)batch.Count);
            }
        }
        Matrix4x4 identity = Matrix4x4.Identity;
        gl.UniformMatrix4(_modelLocation, 1, false, (float*)&identity);
        gl.UniformMatrix4(_normalTransformLocation, 1, false, (float*)&identity);
        gl.BindVertexArray(_vertexArray);
        ResetSurfaceState(gl);
    }

    private void RemoveUnusedPreviewMeshes(GL gl)
    {
        if (!_previewMeshesDirty) return;
        var used = _foliagePreview.Select(item => item.Model).ToHashSet(ReferenceEqualityComparer.Instance);
        foreach (XModelSource model in _previewMeshes.Keys.Where(model => !used.Contains(model)).ToArray())
        {
            _previewMeshes[model].Delete(gl);
            _previewMeshes.Remove(model);
        }
        _previewMeshesDirty = false;
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
            bool water = _compiledPreview is null && source?.IsWater == true;
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
            if (_compiledPreview is not null) gl.Uniform1(_ignoreVertexColorLocation, 0);
            gl.BindTexture(TextureTarget.Texture2D, texture);
            gl.Uniform1(_waterPreviewLocation, water ? 1 : 0);
            if (water) _water.Bind(gl, batch.Material);
            gl.Uniform1(_litLocation, previewLighting ? 1 : 0);
            gl.Uniform1(_texturedLocation, 1);
            if (water)
            {
                foreach (var range in _waterDrawRanges[batch.Material])
                {
                    BindWaterReflection(gl, range.Start);
                    gl.DrawArrays(PrimitiveType.Triangles, range.Start, (uint)range.Count);
                }
            }
            else gl.DrawArrays(PrimitiveType.Triangles, batch.Start, (uint)batch.Count);
        }
        if (transparent)
        {
            string? material = null;
            bool waterDrawn = false;
            // Respect material sort keys, then order individual translucent triangles back to front.
            foreach (var triangle in _transparentTriangles.OrderBy(triangle => _surfaceStates[triangle.Material].SortKey)
                         .ThenByDescending(triangle => Vector3.DistanceSquared(eye, triangle.Center)))
            {
                if (!waterDrawn && _surfaceStates[triangle.Material].SortKey >= (int)WaterMaterialAuthoring.SurfaceSortKey)
                {
                    DrawWater();
                    material = null;
                }
                if (!textures.TryGetValue(triangle.Material, out uint texture)) continue;
                if (material != triangle.Material)
                {
                    MaterialSource? source = resolveMaterial?.Invoke(triangle.Material);
                    bool water = _compiledPreview is null && source?.IsWater == true;
                    SceneMaterialDrawing.Apply(gl, _surfaceStates[triangle.Material], _alphaTestLocation, _premultiplyAlphaLocation, _ignoreVertexColorLocation);
                    if (_compiledPreview is not null) gl.Uniform1(_ignoreVertexColorLocation, 0);
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
            if (!waterDrawn) DrawWater();

            void DrawWater()
            {
                waterDrawn = true;
                // Depth-writing water needs no per-frame CPU triangle sort.
                // Draw cached material/probe ranges after opaque ground and before decals/transparency.
                foreach (var batch in _surfaceBatches)
                {
                    if (!_waterMaterials.Contains(batch.Material) || !textures.TryGetValue(batch.Material, out uint texture)) continue;
                    SceneMaterialDrawing.Apply(gl, _surfaceStates[batch.Material],
                        _alphaTestLocation, _premultiplyAlphaLocation, _ignoreVertexColorLocation);
                    gl.BindTexture(TextureTarget.Texture2D, texture);
                    gl.Uniform1(_waterPreviewLocation, 1);
                    _water.Bind(gl, batch.Material);
                    gl.Uniform1(_litLocation, previewLighting ? 1 : 0);
                    gl.Uniform1(_texturedLocation, 1);
                    foreach (var range in _waterDrawRanges[batch.Material])
                    {
                        BindWaterReflection(gl, range.Start);
                        gl.DrawArrays(PrimitiveType.Triangles, range.Start, (uint)range.Count);
                    }
                }
            }
        }
        ResetSurfaceState(gl);
    }

    private void BindWaterReflection(GL gl, int start)
    {
        bool available = _reflections.Bind(gl, _waterProbes.GetValueOrDefault(start));
        gl.Uniform1(_hasWaterReflectionLocation, available ? 1 : 0);
    }

    private void RenderFaceHighlights(GL gl)
    {
        if (_highlightCount == 0) return;
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindTexture(TextureTarget.Texture2D, _lineTexture);
        gl.Uniform1(_litLocation, 0);
        gl.Uniform1(_texturedLocation, 0);
        gl.Uniform1(_alphaTestLocation, 0);
        gl.Uniform1(_premultiplyAlphaLocation, 0);
        gl.Uniform1(_ignoreVertexColorLocation, 0);
        gl.Uniform1(_waterPreviewLocation, 0);
        gl.Enable(EnableCap.DepthTest);
        gl.DepthFunc(DepthFunction.Lequal);
        gl.DepthMask(false);
        gl.Enable(EnableCap.Blend);
        gl.BlendEquation(BlendEquationModeEXT.FuncAdd);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        gl.Enable(EnableCap.PolygonOffsetFill);
        gl.PolygonOffset(-1, -1);
        gl.DrawArrays(PrimitiveType.Triangles, _highlightStart, (uint)_highlightCount);
        gl.DepthMask(true);
        gl.Disable(EnableCap.Blend);
        gl.Disable(EnableCap.PolygonOffsetFill);
    }

    private unsafe void RenderReflectionFace(GL gl, Matrix4x4 matrix, Vector3 origin,
        Func<string, MaterialSource?>? resolveMaterial, bool previewLighting)
    {
        gl.UseProgram(_program);
        Matrix4x4 identity = Matrix4x4.Identity;
        gl.UniformMatrix4(_modelLocation, 1, false, (float*)&identity);
        gl.UniformMatrix4(_normalTransformLocation, 1, false, (float*)&identity);
        gl.UniformMatrix4(_viewProjectionLocation, 1, false, (float*)&matrix);
        gl.Uniform3(_eyeLocation, origin.X, origin.Y, origin.Z);
        gl.Uniform1(_cubicClipLocation, 0);
        gl.Uniform1(_linearCaptureLocation, 1);
        gl.Uniform1(_fogEnabledLocation, 0);
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

    private unsafe void PrepareFramebuffer(GL gl, PixelSize size)
    {
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        if (_renderSize == size)
            return;
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindBuffer(BufferTargetARB.PixelUnpackBuffer, 0);
        gl.BindTexture(TextureTarget.Texture2D, _colorTexture);
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)size.Width, (uint)size.Height, 0,
            Silk.NET.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, null);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, _colorTexture, 0);
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depthBuffer);
        gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent24, (uint)size.Width, (uint)size.Height);
        gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
            RenderbufferTarget.Renderbuffer, _depthBuffer);
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);
        if (gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete)
            throw new InvalidOperationException("The camera color/depth framebuffer is incomplete.");
        _renderSize = size;
    }

    private void DrawFilmPreview(GL gl, PixelSize size, int framebuffer, FilmPreview preview)
    {
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)framebuffer);
        gl.Viewport(0, 0, (uint)size.Width, (uint)size.Height);
        gl.Disable(EnableCap.DepthTest);
        gl.Disable(EnableCap.Blend);
        gl.Disable(EnableCap.CullFace);
        gl.ColorMask(true, true, true, true);
        gl.UseProgram(_filmProgram);
        gl.Uniform2(_filmSizeLocation, (float)size.Width, (float)size.Height);
        gl.Uniform1(_filmBrightnessLocation, preview.Brightness);
        gl.Uniform1(_filmContrastLocation, preview.Contrast);
        gl.Uniform1(_filmDesaturationLocation, preview.Desaturation);
        gl.Uniform3(_filmLightTintLocation, preview.LightTint.X, preview.LightTint.Y, preview.LightTint.Z);
        gl.Uniform3(_filmDarkTintLocation, preview.DarkTint.X, preview.DarkTint.Y, preview.DarkTint.Z);
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindTexture(TextureTarget.Texture2D, _colorTexture);
        gl.BindVertexArray(_filmVertexArray);
        gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        gl.BindVertexArray(0);
        gl.BindTexture(TextureTarget.Texture2D, 0);
        gl.UseProgram(0);
    }

    private unsafe void UploadScene(GL gl, EditorSession session, Func<string, MaterialSource?>? resolveMaterial)
    {
        var scene = new SceneGeometry(session.Scene, session.TransformMode, session.Tool, resolveMaterial,
            LeakPath, LeakPointIndex, _fxPreview is not null || _mapFxPreviews.Count != 0);
        _movePreviewVertices = scene.Vertices;
        _movePreviewRanges.Clear();
        _movePreviewRanges.AddRange(scene.MovePreviewRanges);
        _movePreviewSource = session.Selection.Items.OfType<MapEntity>().FirstOrDefault();
        _movePreviewOrigin = _movePreviewSource is null ? Vector3.Zero : EditorSession.EntityOrigin(_movePreviewSource);
        _movePreviewApplied = Vector3.Zero;
        _movePreviewDirty = false;
        if (!session.DeferPreviewLighting) _lighting.Update(gl, session.Scene);
        _batches.Clear();
        _batches.AddRange(scene.Batches);
        _surfaceBatches.Clear();
        _surfaceBatches.AddRange(scene.Batches.Where(batch => resolveMaterial?.Invoke(batch.Material)?.IsSky != true));
        _surfaceStates.Clear();
        HasAnimatedWater = false;
        _waterMaterials.Clear();
        foreach (var batch in _surfaceBatches)
        {
            MaterialSource? source = resolveMaterial?.Invoke(batch.Material);
            MaterialSurfaceState state = source?.Surface ?? MaterialSurfaceState.Opaque;
            if (source?.IsWater == true)
            {
                HasAnimatedWater = true;
                _waterMaterials.Add(batch.Material);
                state = state with { BlendOperation = GfxBlendOperation.Add,
                    Source = GfxBlend.SourceAlpha, Destination = GfxBlend.InverseSourceAlpha, DepthWrite = true,
                    CullFace = GfxCullFace.None, AlphaTest = null, SortKey = (int)WaterMaterialAuthoring.SurfaceSortKey };
            }
            _surfaceStates.Add(batch.Material, state);
        }
        _water.RemoveUnused(gl, _waterMaterials);
        _water.SetVolumes(session.Scene.Document.World.Brushes.Where(brush =>
            brush.Faces.Count != 0 && brush.Faces.All(face => resolveMaterial?.Invoke(face.Material)?.IsWater == true))
            .Select(brush => (brush, resolveMaterial?.Invoke(brush.Faces
                .OrderByDescending(face => face.Normal.Z)
                .ThenBy(face => face.Material, StringComparer.Ordinal)
                .First().Material))));
        if (!HasAnimatedWater) _reflections.Reload(gl);
        _probeOrigins.Clear();
        _probeOrigins.Add(new GfxReflectionProbe(0, 0, 0));
        foreach (MapEntity entity in session.Scene.Document.Entities)
            if (entity.ClassName == "reflection_probe" && entity.TryGetOrigin(out Vector3 origin))
                _probeOrigins.Add(new GfxReflectionProbe(origin.X, origin.Y, origin.Z));
        _waterProbes.Clear();
        _waterDrawRanges.Clear();
        _glyphStart = scene.GlyphStart;
        _glyphCount = scene.GlyphCount;
        _gridStart = scene.GridStart;
        _gridCount = scene.GridCount;
        _highlightStart = scene.HighlightStart;
        _highlightCount = scene.HighlightCount;
        _outlineStart = scene.OutlineStart;
        _outlineCount = scene.OutlineCount;
        _axesStart = scene.AxesStart;
        _axesCount = scene.AxesCount;
        _leakPathStart = scene.LeakPathStart;
        _leakPathCount = scene.LeakPathCount;
        _materialTextures.RemoveUnused(gl, _surfaceBatches.Select(batch => batch.Material));
        _skies.RemoveUnused(gl, scene.Batches.Where(batch => resolveMaterial?.Invoke(batch.Material)?.IsSky == true)
            .Select(batch => batch.Material));
        gl.BindVertexArray(_vertexArray);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);
        SceneVertex[] data = scene.Vertices;
        _transparentTriangles.Clear();
        foreach (var batch in _surfaceBatches)
            if (!_waterMaterials.Contains(batch.Material) && _surfaceStates[batch.Material] is { } state && (state.IsBlended || !state.DepthWrite))
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
        // Ocean subdivision can create thousands of triangles. Batch contiguous
        // triangles sharing a reflection probe once, instead of issuing one draw each frame.
        foreach (var batch in _surfaceBatches.Where(batch => _waterMaterials.Contains(batch.Material)))
        {
            var ranges = new List<(int Start, int Count)>();
            for (int start = batch.Start; start < batch.Start + batch.Count;)
            {
                int end = start + 3;
                while (end < batch.Start + batch.Count && _waterProbes[end] == _waterProbes[start]) end += 3;
                ranges.Add((start, end - start));
                start = end;
            }
            _waterDrawRanges.Add(batch.Material, ranges);
        }
        _surfaceBounds = null;
        _waterMaterialBounds.Clear();
        foreach (var batch in _surfaceBatches)
        {
            float height = resolveMaterial?.Invoke(batch.Material)?.Ocean?.Height ?? 0;
            bool water = _waterMaterials.Contains(batch.Material);
            for (int index = batch.Start; index < batch.Start + batch.Count; index++)
            {
                Vector3 position = data[index].Position;
                Vector3 extent = new Vector3(height * data[index].Color.X);
                _surfaceBounds = _surfaceBounds is { } bounds
                    ? (Vector3.Min(bounds.Min, position - extent), Vector3.Max(bounds.Max, position + extent))
                    : (position - extent, position + extent);
                if (water)
                    _waterMaterialBounds[batch.Material] = _waterMaterialBounds.TryGetValue(batch.Material, out var current)
                        ? (Vector3.Min(current.Min, position - extent), Vector3.Max(current.Max, position + extent))
                        : (position - extent, position + extent);
            }
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

    private unsafe void UpdatePointEntityMove(GL gl)
    {
        _movePreviewDirty = false;
        if (_movePreviewSource is null || _movePreviewRanges.Count == 0) return;
        Vector3 desired = EditorSession.EntityOrigin(_movePreviewSource) - _movePreviewOrigin;
        Vector3 delta = desired - _movePreviewApplied;
        if (delta == Vector3.Zero) return;
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);
        foreach (var (start, count) in _movePreviewRanges)
        {
            for (int index = start; index < start + count; index++)
            {
                SceneVertex vertex = _movePreviewVertices[index];
                _movePreviewVertices[index] = new SceneVertex(vertex.Position + delta, vertex.Normal, vertex.Uv,
                    vertex.Color);
            }
            fixed (SceneVertex* vertices = &_movePreviewVertices[start])
                gl.BufferSubData(BufferTargetARB.ArrayBuffer, (nint)(start * sizeof(SceneVertex)),
                    (nuint)(count * sizeof(SceneVertex)), vertices);
        }
        _movePreviewApplied = desired;
    }

    private unsafe void UploadCompiledScene(GL gl, CompiledBspPreview preview,
        Func<string, MaterialSource?>? resolveMaterial)
    {
        _movePreviewVertices = [];
        _movePreviewRanges.Clear();
        _movePreviewSource = null;
        _movePreviewDirty = false;
        _batches.Clear();
        _surfaceBatches.Clear();
        _surfaceBatches.AddRange(preview.Batches);
        _surfaceStates.Clear();
        foreach (var batch in _surfaceBatches)
            _surfaceStates.TryAdd(batch.Material,
                resolveMaterial?.Invoke(batch.Material)?.Surface ?? MaterialSurfaceState.Opaque);
        _materialTextures.RemoveUnused(gl, _surfaceBatches.Select(batch => batch.Material));
        _skies.RemoveUnused(gl, []);
        _waterMaterials.Clear();
        _waterMaterialBounds.Clear();
        _water.RemoveUnused(gl, _waterMaterials);
        _water.SetVolumes([]);
        _probeOrigins.Clear();
        _waterProbes.Clear();
        _waterDrawRanges.Clear();
        _transparentTriangles.Clear();
        foreach (var batch in _surfaceBatches)
            if (_surfaceStates[batch.Material] is { } state && (state.IsBlended || !state.DepthWrite))
                for (int index = batch.Start; index < batch.Start + batch.Count; index += 3)
                    _transparentTriangles.Add((batch.Material, index,
                        (preview.Vertices[index].Position + preview.Vertices[index + 1].Position +
                         preview.Vertices[index + 2].Position) / 3));
        _glyphStart = _glyphCount = _gridStart = _gridCount = _highlightStart = _highlightCount =
            _outlineStart = _outlineCount = _axesStart = _axesCount = _leakPathStart = _leakPathCount = 0;
        _surfaceBounds = preview.Bounds;
        HasAnimatedWater = HasVisibleAnimatedWater = false;
        gl.BindVertexArray(_vertexArray);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);
        SceneVertex[] data = preview.Vertices;
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
            foreach (PreviewModelMesh mesh in _previewMeshes.Values) mesh.Delete(gl);
            if (_fxVertexBuffer != 0) gl.DeleteBuffer(_fxVertexBuffer);
            if (_fxVertexArray != 0) gl.DeleteVertexArray(_fxVertexArray);
            if (_vertexBuffer != 0) gl.DeleteBuffer(_vertexBuffer);
            if (_vertexArray != 0) gl.DeleteVertexArray(_vertexArray);
            if (_program != 0) gl.DeleteProgram(_program);
            if (_filmProgram != 0) gl.DeleteProgram(_filmProgram);
            if (_filmVertexArray != 0) gl.DeleteVertexArray(_filmVertexArray);
            if (_framebuffer != 0) gl.DeleteFramebuffer(_framebuffer);
            if (_colorTexture != 0) gl.DeleteTexture(_colorTexture);
            if (_depthBuffer != 0) gl.DeleteRenderbuffer(_depthBuffer);
            if (_lineTexture != 0) gl.DeleteTexture(_lineTexture);
            gl.Dispose();
        }
        _gl = null;
        ResetResources();
    }

    private void ResetResources()
    {
        _program = _vertexArray = _vertexBuffer = _framebuffer = _colorTexture = _depthBuffer = _lineTexture =
            _filmProgram = _filmVertexArray = 0;
        _fxVertexArray = _fxVertexBuffer = 0;
        _materialTextures.ForgetHandles();
        _lighting.ForgetHandle();
        _shadows.ForgetHandles();
        _sunlight.ForgetHandles();
        _skies.ForgetHandles();
        _water.ForgetHandles();
        _reflections.ForgetHandles();
        _waterMaterials.Clear();
        _waterMaterialBounds.Clear();
        _waterProbes.Clear();
        _waterDrawRanges.Clear();
        _probeOrigins.Clear();
        _reflectionsDirty = true;
        _batches.Clear();
        _surfaceBatches.Clear();
        _surfaceStates.Clear();
        _transparentTriangles.Clear();
        _movePreviewVertices = [];
        _movePreviewRanges.Clear();
        _movePreviewSource = null;
        _movePreviewDirty = false;
        _previewMeshes.Clear();
        _foliagePreview = [];
        _previewMeshesDirty = false;
        HasAnimatedWater = HasVisibleAnimatedWater = false;
        _surfaceBounds = null;
        _renderSize = default;
        _sceneDirty = _texturesDirty = _shadowsDirty = true;
    }

    private void PublishStatus(string? error, bool failure = false)
    {
        if (Error == error && HasRenderingError == failure)
            return;
        Error = error;
        HasRenderingError = failure;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    internal static bool IsRenderException(Exception exception) => exception is IOException or FormatException or
        InvalidOperationException or ArgumentException or NotSupportedException or OverflowException;

    private sealed class PreviewModelMesh
    {
        internal required uint VertexArray { get; init; }
        internal required uint VertexBuffer { get; init; }
        internal required IReadOnlyList<(string Material, int Start, int Count)> Batches { get; init; }

        internal static unsafe PreviewModelMesh Create(GL gl, XModelSource model)
        {
            var materials = new Dictionary<string, List<SceneVertex>>(StringComparer.Ordinal);
            foreach (var triangle in XModelGeometry.GetLocalTriangles(model))
            {
                if (!materials.TryGetValue(triangle.Material, out List<SceneVertex>? vertices))
                    materials.Add(triangle.Material, vertices = []);
                vertices.AddRange([triangle.A, triangle.B, triangle.C]);
            }
            var data = new List<SceneVertex>();
            var batches = new List<(string Material, int Start, int Count)>();
            foreach (var material in materials)
            {
                int start = data.Count;
                data.AddRange(material.Value);
                batches.Add((material.Key, start, material.Value.Count));
            }
            uint vertexArray = 0, vertexBuffer = 0;
            try
            {
                vertexArray = gl.GenVertexArray();
                vertexBuffer = gl.GenBuffer();
                gl.BindVertexArray(vertexArray);
                gl.BindBuffer(BufferTargetARB.ArrayBuffer, vertexBuffer);
                SceneVertex[] verticesArray = data.ToArray();
                fixed (SceneVertex* pointer = verticesArray)
                    gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(verticesArray.Length * sizeof(SceneVertex)), pointer,
                        BufferUsageARB.StaticDraw);
                for (uint attribute = 0; attribute < 4; attribute++) gl.EnableVertexAttribArray(attribute);
                gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, (uint)sizeof(SceneVertex), (void*)0);
                gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, (uint)sizeof(SceneVertex), (void*)12);
                gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, (uint)sizeof(SceneVertex), (void*)24);
                gl.VertexAttribPointer(3, 4, VertexAttribPointerType.Float, false, (uint)sizeof(SceneVertex), (void*)32);
                gl.BindVertexArray(0);
                return new PreviewModelMesh { VertexArray = vertexArray, VertexBuffer = vertexBuffer, Batches = batches };
            }
            catch
            {
                if (vertexBuffer != 0) gl.DeleteBuffer(vertexBuffer);
                if (vertexArray != 0) gl.DeleteVertexArray(vertexArray);
                throw;
            }
        }

        internal void Delete(GL gl)
        {
            gl.DeleteBuffer(VertexBuffer);
            gl.DeleteVertexArray(VertexArray);
        }
    }
}
