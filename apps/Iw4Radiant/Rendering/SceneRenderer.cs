using IW4.Render.EditorPreview;
using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Avalonia;
using Avalonia.OpenGL;
using IW4.Formats.SourceFormat.Material;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.GfxMap;
using IW4.Game.Codecs.GfxMap;
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

internal readonly record struct FogPreview(bool Enabled, Vector3 Color, float StartDistance, float HalfDistance,
    float MaxOpacity = 1)
{
    internal static FogPreview Disabled => new(false, MapFogProperties.Default.Color,
        MapFogProperties.Default.StartDistance, MapFogProperties.Default.HalfDistance, MapFogProperties.Default.MaxOpacity);
}

internal sealed class SceneRenderer
{
    private GL? _gl;
    private uint _program, _vertexArray, _vertexBuffer;
    private uint _compiledLightmapUvBuffer;
    private uint[] _compiledDiffuseTextures = [];
    private uint[] _compiledSunVisibilityTextures = [];
    private uint _fxVertexArray, _fxVertexBuffer;
    private uint _framebuffer, _colorTexture, _depthBuffer, _filmProgram, _filmVertexArray;
    private int _filmSizeLocation, _filmBrightnessLocation, _filmContrastLocation, _filmDesaturationLocation,
        _filmLightTintLocation, _filmDarkTintLocation;
    private uint _lineTexture;
    private PixelSize _renderSize;
    private int _viewProjectionLocation, _texturedLocation, _litLocation, _alphaTestLocation, _premultiplyAlphaLocation,
        _ignoreVertexColorLocation, _waterPreviewLocation, _eyeLocation, _linearCaptureLocation, _hasWaterReflectionLocation,
        _cubicClipLocation, _cubicClipCenterLocation, _cubicClipDistanceLocation, _modelLocation, _normalTransformLocation,
        _fogEnabledLocation, _fogColorLocation, _fogStartLocation, _fogDensityLocation, _fogMaxOpacityLocation;
    private int _compiledLightmapModeLocation, _compiledSunDirectionLocation, _compiledSunColorLocation,
        _compiledPrimaryTypeLocation, _compiledLocalPositionRadiusLocation, _compiledLocalColorLocation,
        _compiledSpotDirectionOuterCosLocation, _compiledSpotInnerCosExponentLocation;
    private readonly List<(string Material, int Start, int Count, int WireStart, int WireCount)> _batches = [];
    private readonly List<(string Material, int Start, int Count, int WireStart, int WireCount)> _surfaceBatches = [];
    private readonly List<(MapEntity? Owner, string Material, int Start, int Count, int WireStart, int WireCount)> _drawBatches = [];
    private readonly List<(MapEntity Owner, int Start, int Count)> _physicsOutlines = [];
    private readonly Dictionary<MapEntity, (Vector3 Min, Vector3 Max)> _physicsBounds = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<MapEntity> _physicsVisible = new(ReferenceEqualityComparer.Instance);
    private IReadOnlyDictionary<MapEntity, Matrix4x4>? _physicsTransforms;
    private readonly Dictionary<string, MaterialSurfaceState> _surfaceStates = new(StringComparer.Ordinal);
    private readonly List<(string Material, int Start, Vector3 Center)> _transparentTriangles = [];
    private readonly List<(MapEntity Owner, string Material, int Start, Vector3 Center)> _physicsTransparentTriangles = [];
    private readonly List<(MapEntity? Owner, string Material, int Start, Vector3 Center)> _sortedTransparentTriangles = [];
    private readonly SceneMaterialTextures _materialTextures = new();
    private readonly SceneLighting _lighting = new();
    private readonly SceneShadows _shadows = new();
    private readonly SceneSunlight _sunlight = new();
    private MapStageLighting? _stages;
    private string? _stageLightingNotice;
    private readonly HashSet<byte> _usedSunIndices = [1];
    private readonly List<(int Start, int End, byte SunIndex)> _sourceSunRanges = [];
    private readonly SceneSkies _skies = new();
    private readonly SceneWater _water = new();
    private readonly SceneReflections _reflections = new();
    private FxSpritePreview? _fxPreview;
    private bool _fxPreviewOutlinesMarkers;
    private FxSpritePreview? _fxOutgoingPreview;
    private readonly Stopwatch _fxTransitionClock = new();
    private const double FxTransitionMilliseconds = 350;
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
    private readonly List<(string Material, int Start, int Count, int WireStart, int WireCount)> _compiledWaterDrawBatches = [];
    private bool _reflectionsDirty = true, _reflectionLighting;
    private (Vector3 Min, Vector3 Max)? _surfaceBounds;
    private int _glyphStart, _glyphCount, _gridStart, _gridCount, _highlightStart, _highlightCount,
        _outlineStart, _outlineCount, _axesStart, _axesCount, _leakPathStart, _leakPathCount;
    private bool _sceneDirty = true, _texturesDirty = true, _shadowsDirty = true;
    private SceneVertex[] _movePreviewVertices = [];
    private readonly List<(int Start, int Count)> _movePreviewRanges = [];
    private readonly List<(MapEntity Source, MapEntity Target, int Start, int Count)> _moveConnectionRanges = [];
    private readonly Dictionary<MapEntity, (int Start, int Count)> _lightInfluenceRanges = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<MapEntity> _lightInfluencePreviewSources = new(ReferenceEqualityComparer.Instance);
    private MapEntity? _movePreviewSource;
    private Vector3 _movePreviewOrigin, _movePreviewApplied;
    private bool _movePreviewDirty;
    private readonly Dictionary<XModelSource, PreviewModelMesh> _previewMeshes = new(ReferenceEqualityComparer.Instance);
    private readonly List<(MapEntity Source, string Material, int Start, int Count, int WireStart, int WireCount)> _destructibleRanges = [];
    private readonly List<(MapEntity Source, int Start, int Count)> _destructibleOutlines = [];
    private MapEntity? _activeDestructibleSource;
    private XModelSource? _destructiblePreviewModel;
    private XModelSource? _destructibleWreckModel;
    private IReadOnlyList<XModelSource> _preparedDestructibleModels = [];
    private IReadOnlyDictionary<XModelSource, PreparedPreviewModelMesh> _preparedDestructibleData =
        new Dictionary<XModelSource, PreparedPreviewModelMesh>(ReferenceEqualityComparer.Instance);
    private IReadOnlyList<string> _destructiblePreparationMaterials = [];
    private TaskCompletionSource<string?>? _destructiblePreparation;
    private string? _destructiblePreparationNotice;
    private IReadOnlyList<string> _preparedFxMaterials = [];
    private bool _worldTextureUploaded;
    private readonly Dictionary<string, string> _compiledModelFailures = new(StringComparer.Ordinal);
    private string? _compiledWaterNotice;
    private IReadOnlyList<(MapEntity Entity, XModelSource Model)> _foliagePreview = [];
    private bool _previewMeshesDirty;
    private CompiledBspPreview? _compiledPreview;
    private WalkPlayerPreview? _walkPlayer;
    private WalkPlayerPreview? _uploadedWalkPlayer;
    private WalkPlayerGl? _walkPlayerGl;
    private string? _walkPlayerNotice;
    private bool _walkPlayerFailed;
    internal CompiledBspPreview? CompiledPreview
    {
        get => _compiledPreview;
        set
        {
            _compiledPreview = value;
            _compiledModelFailures.Clear();
            _sceneDirty = _texturesDirty = _previewMeshesDirty = true;
        }
    }
    internal LeakPath? LeakPath { get; set; }
    internal int LeakPointIndex { get; set; }

    internal string? Error { get; private set; } =
        "Camera is waiting for OpenGL. If it remains blank, a compatible OpenGL driver is required.";
    internal bool HasRenderingError { get; private set; }
    internal bool HasAnimatedWater { get; private set; }
    internal bool HasVisibleAnimatedWater { get; private set; }
    internal bool HasPendingTextures => _materialTextures.HasPendingTextures || _destructiblePreparation is not null;
    internal bool HasActiveFxPreview => _fxPreview is not null;
    internal bool HasPlayingFxPreview => _fxPreview?.IsPlaying == true || _fxTransitionClock.IsRunning;
    internal bool IsFxPreviewPaused => _fxPreview?.IsPaused == true;
    internal bool IsFxPreviewFinished => _fxPreview?.IsFinished == true;
    internal bool IsFxPreviewLooping => _fxPreview?.IsLooping == true;
    internal bool IsFxPreviewRepeating => _fxPreview?.Repeat == true;
    internal string? FxPreviewNotice => _fxPreviewNotice;
    internal string? WalkPlayerNotice => _walkPlayerNotice;
    internal (Vector3 Min, Vector3 Max)? FxPreviewBounds => _fxPreview?.PreviewBounds;
    internal bool HasActiveMapFxPreview => _mapFxPreviews.Count != 0;
    internal bool HasPlayingMapFxPreview => _mapFxPreviews.Any(item => item.Preview.IsPlaying);
    internal bool IsMapFxPreviewPaused => HasActiveMapFxPreview && _mapFxPaused;
    internal bool IsMapFxPreviewFinished => HasActiveMapFxPreview &&
        _mapFxPreviews.All(item => item.Preview.IsFinished);
    internal string? MapFxPreviewNotice => _mapFxPreviewNotice;
    internal event EventHandler? StatusChanged;

    internal void RefreshScene() => _sceneDirty = true;
    internal void PreviewLightInfluence(MapEntity source) => _lightInfluencePreviewSources.Add(source);
    internal void RefreshDestructibleAppearance(EditorScene scene)
    {
        _activeDestructibleSource = scene.DestructiblePreviewEntity is null ? null : scene.DestructiblePreviewSource;
        _previewMeshesDirty = true;
    }

    internal void PrepareFxPreviewMaterials(IReadOnlyList<string> materials)
    {
        _preparedFxMaterials = materials.Distinct(StringComparer.Ordinal).ToArray();
    }
    internal XModelSource[] DestructibleModelsNeedingMesh(IReadOnlyList<XModelSource> models) =>
        models.Where(model => !_previewMeshes.ContainsKey(model)).ToArray();

    internal static PreparedPreviewModelMesh BuildDestructibleMesh(XModelSource model) =>
        PreviewModelMesh.Build(model, withOutline: true);

    internal Task<string?> PrepareDestructibleRendering(IReadOnlyList<XModelSource> models,
        IReadOnlyDictionary<XModelSource, PreparedPreviewModelMesh> meshData,
        IReadOnlyList<string> materials, string? notice)
    {
        if (_gl is null || _program == 0)
            throw new InvalidOperationException("The camera OpenGL context is unavailable.");
        _destructiblePreparation?.TrySetCanceled();
        _preparedDestructibleModels = models;
        _preparedDestructibleData = meshData;
        _destructiblePreparationMaterials = materials.Distinct(StringComparer.Ordinal).ToArray();
        _destructiblePreparationNotice = notice;
        _destructiblePreparation = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _previewMeshesDirty = true;
        return _destructiblePreparation.Task;
    }

    internal void CancelDestructiblePreparation(Task<string?>? preparation = null)
    {
        if (preparation is not null && !ReferenceEquals(_destructiblePreparation?.Task, preparation)) return;
        _destructiblePreparation?.TrySetCanceled();
        _destructiblePreparation = null;
        _preparedDestructibleModels = [];
        _preparedDestructibleData = new Dictionary<XModelSource, PreparedPreviewModelMesh>(ReferenceEqualityComparer.Instance);
        _destructiblePreparationMaterials = [];
        _previewMeshesDirty = true;
    }

    internal void SetPhysicsPlacementTransforms(IReadOnlyDictionary<MapEntity, Matrix4x4>? transforms)
    {
        _physicsTransforms = transforms;
        _sceneDirty = true;
    }
    internal void SetWalkPlayer(WalkPlayerPreview? preview)
    {
        if (ReferenceEquals(_walkPlayer, preview)) return;
        _walkPlayer = preview;
        _walkPlayerFailed = false;
        _walkPlayerNotice = null;
        // OpenGL deletion is deferred until Render or ReleaseResources has a current context.
    }
    internal void PreviewPointEntityMove() => _movePreviewDirty = true;
    internal void ReloadTextures()
    {
        _texturesDirty = _sceneDirty = true;
        _worldTextureUploaded = false;
        _compiledModelFailures.Clear();
        _fxMaterials.Clear();
    }
    internal string? SetFxPreview(string? sourceDirectory, string? assetName, Vector3 origin, Matrix4x4 orientation)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory) || string.IsNullOrWhiteSpace(assetName))
        {
            StopFxPreview();
            return null;
        }
        try
        {
            FxSpritePreview preview = FxSpritePreview.Load(sourceDirectory, assetName, origin, orientation);
            if (!preview.HasDrawableElements)
            {
                StopFxPreview();
                return _fxPreviewNotice = $"FX '{assetName}' has no supported visual components. {preview.Notice}";
            }
            return SetFxPreviewInstance(preview, origin, orientation, blend: false,
                outlineFxMarkers: true, inheritPause: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           ArgumentException or NotSupportedException or JsonException or OverflowException)
        {
            StopFxPreview();
            _fxPreviewNotice = $"FX '{assetName}': {exception.Message}";
        }
        return _fxPreviewNotice;
    }

    internal string? SetFxPreview(FxSpritePreview prototype, Vector3 origin, Matrix4x4 orientation,
        bool blend = false) => SetFxPreviewInstance(prototype, origin, orientation, blend,
            outlineFxMarkers: false, inheritPause: true);

    private string? SetFxPreviewInstance(FxSpritePreview prototype, Vector3 origin, Matrix4x4 orientation,
        bool blend, bool outlineFxMarkers, bool inheritPause)
    {
        ArgumentNullException.ThrowIfNull(prototype);
        if (!prototype.HasDrawableElements)
        {
            StopFxPreview();
            return _fxPreviewNotice = $"FX has no supported visual components. {prototype.Notice}";
        }

        bool hadMarkerOutlines = _fxPreviewOutlinesMarkers || _mapFxPreviews.Count != 0;
        bool wasPaused = inheritPause && _fxPreview?.IsPaused == true;
        _fxOutgoingPreview = blend ? _fxPreview : null;
        _fxTransitionClock.Reset();
        if (_fxOutgoingPreview is not null && !wasPaused) _fxTransitionClock.Start();
        _fxPreview = prototype.CreateInstance(origin, orientation);
        _fxPreviewOutlinesMarkers = outlineFxMarkers;
        _fxPreview.SetPaused(wasPaused);
        if (hadMarkerOutlines != (_fxPreviewOutlinesMarkers || _mapFxPreviews.Count != 0))
            _sceneDirty = true;
        return _fxPreviewNotice = prototype.Notice;
    }

    internal void StopFxPreview()
    {
        if (_fxPreviewOutlinesMarkers && _mapFxPreviews.Count == 0) _sceneDirty = true;
        _fxPreview = null;
        _fxPreviewOutlinesMarkers = false;
        _fxOutgoingPreview = null;
        _fxTransitionClock.Reset();
        _fxPreviewNotice = null;
    }

    internal void SetFxPreviewPaused(bool paused)
    {
        _fxPreview?.SetPaused(paused);
        _fxOutgoingPreview?.SetPaused(paused);
        if (paused) _fxTransitionClock.Stop();
        else if (_fxOutgoingPreview is not null) _fxTransitionClock.Start();
    }

    internal void RestartFxPreview()
    {
        _fxPreview?.Restart();
        _fxOutgoingPreview = null;
        _fxTransitionClock.Reset();
    }

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
        _sceneDirty = true;
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
            _compiledLightmapModeLocation = _gl.GetUniformLocation(_program, "uCompiledLightmapMode");
            _compiledSunDirectionLocation = _gl.GetUniformLocation(_program, "uCompiledSunDirection");
            _compiledSunColorLocation = _gl.GetUniformLocation(_program, "uCompiledSunColorLinear");
            _compiledPrimaryTypeLocation = _gl.GetUniformLocation(_program, "uCompiledPrimaryType");
            _compiledLocalPositionRadiusLocation = _gl.GetUniformLocation(_program, "uCompiledLocalPositionRadius");
            _compiledLocalColorLocation = _gl.GetUniformLocation(_program, "uCompiledLocalColorLinear");
            _compiledSpotDirectionOuterCosLocation = _gl.GetUniformLocation(_program, "uCompiledSpotDirectionOuterCos");
            _compiledSpotInnerCosExponentLocation = _gl.GetUniformLocation(_program, "uCompiledSpotInnerCosExponent");
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
            _fogMaxOpacityLocation = _gl.GetUniformLocation(_program, "uFogMaxOpacity");
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
            _gl.Uniform1(_gl.GetUniformLocation(_program, "uCompiledDiffuseLightmap"), 7);
            _gl.Uniform1(_gl.GetUniformLocation(_program, "uCompiledSunVisibility"), 8);
            _compiledLightmapUvBuffer = _gl.GenBuffer();
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
        CancelDestructiblePreparation();
        // Lost-context handles must never be deleted in a replacement context.
        _gl?.Dispose();
        _gl = null;
        ResetResources();
        PublishStatus("OpenGL context lost. Reopen the window to restore the camera viewport.", failure: true);
    }

    internal unsafe void Render(PixelSize size, int framebuffer, Matrix4x4 viewProjection, Vector3 eye,
        EditorSession session,
        Func<string, MaterialSource?>? resolveMaterial, bool previewLighting, FilmPreview filmPreview, FogPreview fogPreview,
        bool showWalkPlayer, double walkPlayerSeconds, Vector3 walkForward,
        float horizontalMotionAmount, bool running)
    {
        if (_gl is not { } gl || _program == 0)
            return;
        try
        {
            SyncWalkPlayer(gl, showWalkPlayer);
            MapDocument document = session.Scene.Document;
            XModelSource? destructibleModel = _compiledPreview is not null || session.Scene.DestructiblePreviewEntity is null
                ? null : session.Scene.DestructiblePreviewModel;
            XModelSource? wreckModel = destructibleModel is null ? null : session.Scene.DestructibleWreckModel;
            _activeDestructibleSource = destructibleModel is null ? null : session.Scene.DestructiblePreviewSource;
            if (!ReferenceEquals(_destructiblePreviewModel, destructibleModel))
            {
                _destructiblePreviewModel = destructibleModel;
                _previewMeshesDirty = true;
            }
            if (!ReferenceEquals(_destructibleWreckModel, wreckModel))
            {
                _destructibleWreckModel = wreckModel;
                _previewMeshesDirty = true;
            }
            RemoveUnusedPreviewMeshes(gl);
            if (_destructibleWreckModel is { } preparedWreck && !_previewMeshes.ContainsKey(preparedWreck))
                _previewMeshes.Add(preparedWreck, PreviewModelMesh.Create(gl, preparedWreck, withOutline: true));
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
            foreach (string material in _preparedFxMaterials)
                if (ResolveFxMaterial(material, resolveMaterial) is not null)
                    _materialTextures.GetTexture(gl, material, resolveMaterial);
            if (_destructiblePreviewModel is { } currentCar)
                foreach (var material in currentCar.Document.Materials)
                    _materialTextures.GetTexture(gl, material.Name, resolveMaterial);
            if (_destructibleWreckModel is { } wreck)
                foreach (var material in wreck.Document.Materials)
                    _materialTextures.GetTexture(gl, material.Name, resolveMaterial);
            if (_materialTextures.UploadReady(gl) is { } uploaded &&
                _surfaceBatches.Any(batch => batch.Material == uploaded))
                _worldTextureUploaded = true;
            ProcessDestructiblePreparation(gl, resolveMaterial);
            if (_worldTextureUploaded && !_materialTextures.HasPendingTextures && _compiledPreview is null)
            {
                _shadowsDirty = _reflectionsDirty = true;
                _worldTextureUploaded = false;
            }
            if ((_movePreviewDirty && !UpdatePointEntityMove(gl, session.Scene)) ||
                (_compiledPreview is null && !UpdateLightInfluence(gl, session.Scene)))
            {
                _sceneDirty = true;
                UploadScene(gl, session, resolveMaterial);
            }
            if (_compiledPreview is null)
                _lighting.UpdateSweep(gl, session.LightSweepPreviewEntity, session.LightSweepPreviewSeconds);
            byte walkSunIndex = showWalkPlayer ? _stages?.SunIndexAt(eye) ?? (byte)1 : (byte)1;
            // A Stage can contain the player without owning any visible geometry.
            // Retain its shadow map after first use instead of rebuilding while walking.
            if (_compiledPreview is null && previewLighting && showWalkPlayer && _usedSunIndices.Add(walkSunIndex))
                _shadowsDirty = true;
            if (_compiledPreview is null && previewLighting && _shadowsDirty &&
                (!session.DeferPreviewLighting || _physicsTransforms is not null))
            {
                _shadows.Update(gl, _lighting.Lights, _vertexArray, _surfaceBatches, resolveMaterial, _materialTextures);
                _sunlight.Update(gl, document.World, _stages, _usedSunIndices, _surfaceBounds, _vertexArray, _surfaceBatches,
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
            if (_compiledPreview is null && HasAnimatedWater && !session.DeferPreviewLighting &&
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
            UpdatePhysicsVisibility(viewProjection);
            gl.Uniform1(_cubicClipLocation, _compiledPreview is null && session.CubicClipEnabled ? 1 : 0);
            gl.Uniform3(_cubicClipCenterLocation, eye.X, eye.Y, eye.Z);
            gl.Uniform1(_cubicClipDistanceLocation, session.CubicClipDistance);
            gl.Uniform3(_eyeLocation, eye.X, eye.Y, eye.Z);
            gl.Uniform1(_linearCaptureLocation, 0);
            gl.Uniform1(_compiledLightmapModeLocation, 0);
            gl.Uniform1(_fogEnabledLocation, 0);
            gl.Uniform3(_fogColorLocation, fogPreview.Color.X, fogPreview.Color.Y, fogPreview.Color.Z);
            gl.Uniform1(_fogStartLocation, fogPreview.StartDistance);
            gl.Uniform1(_fogDensityLocation, MathF.Log(2) / fogPreview.HalfDistance);
            gl.Uniform1(_fogMaxOpacityLocation, fogPreview.MaxOpacity);
            _lighting.Bind(gl, _shadows.IsAvailable);
            _shadows.Bind(gl);
            _sunlight.Bind(gl, enabled: _compiledPreview is null);
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
            {
                RenderFoliagePreview(gl, resolveMaterial, previewLighting, session.AlphaPreviewEnabled, eye, transparent: false);
                RenderDestructiblePreview(gl, session.Scene, resolveMaterial, previewLighting,
                    session.AlphaPreviewEnabled, transparent: false);
            }
            else RenderCompiledModels(gl, resolveMaterial, session.AlphaPreviewEnabled, eye, transparent: false);
            gl.Uniform1(_fogEnabledLocation, 0);
            _skies.Render(gl, viewProjection, eye, _vertexArray, _batches, resolveMaterial);
            gl.BindTexture(TextureTarget.Texture2D, _lineTexture);
            gl.Uniform1(_litLocation, 0);
            gl.Uniform1(_texturedLocation, 0);
            gl.DrawArrays(PrimitiveType.Triangles, _glyphStart, (uint)_glyphCount);
            gl.Uniform1(_fogEnabledLocation, fogPreview.Enabled ? 1 : 0);
            RenderSurfaces(gl, resolveMaterial, previewLighting, session.AlphaPreviewEnabled, eye, transparent: true);
            if (_compiledPreview is null)
            {
                RenderFoliagePreview(gl, resolveMaterial, previewLighting, session.AlphaPreviewEnabled, eye, transparent: true);
                RenderDestructiblePreview(gl, session.Scene, resolveMaterial, previewLighting,
                    session.AlphaPreviewEnabled, transparent: true);
            }
            else RenderCompiledModels(gl, resolveMaterial, session.AlphaPreviewEnabled, eye, transparent: true);
            gl.Uniform1(_fogEnabledLocation, 0);
            RenderFxPreview(gl, resolveMaterial, eye);
            RenderMapFxPreview(gl, resolveMaterial, eye, viewProjection);
            RenderFaceHighlights(gl);
            gl.BindTexture(TextureTarget.Texture2D, _lineTexture);
            gl.Disable(EnableCap.PolygonOffsetFill);
            gl.Uniform1(_litLocation, 0);
            gl.Uniform1(_texturedLocation, 0);
            DrawStaticOutlines(gl);
            if (_compiledPreview is null) RenderDestructibleOutline(gl, session.Scene, session);
            foreach (var outline in _physicsOutlines)
            {
                if (!_physicsVisible.Contains(outline.Owner)) continue;
                SetPhysicsModel(gl, outline.Owner);
                gl.DrawArrays(PrimitiveType.Lines, outline.Start, (uint)outline.Count);
            }
            if (_physicsOutlines.Count != 0) SetPhysicsModel(gl, null);
            gl.Disable(EnableCap.DepthTest);
            gl.DrawArrays(PrimitiveType.Lines, _axesStart, (uint)_axesCount);
            gl.Uniform1(_cubicClipLocation, 0);
            gl.DrawArrays(PrimitiveType.Lines, _leakPathStart, (uint)_leakPathCount);
            if (_compiledPreview is null) _water.RenderUnderwater(gl, eye);
            if (showWalkPlayer && _walkPlayer is not null && !_walkPlayerFailed)
            {
                try { RenderWalkPlayer(gl, size, eye, walkForward, previewLighting, walkSunIndex,
                    walkPlayerSeconds, horizontalMotionAmount, running); }
                catch (Exception exception) when (IsRenderException(exception) || exception is InvalidDataException or IndexOutOfRangeException)
                {
                    _walkPlayerFailed = true;
                    _walkPlayerNotice = $"Walk viewmodel: {exception.Message}";
                }
            }
            gl.BindVertexArray(0);
            gl.BindTexture(TextureTarget.Texture2D, 0);
            gl.ActiveTexture(TextureUnit.Texture8);
            gl.BindTexture(TextureTarget.Texture2D, 0);
            gl.ActiveTexture(TextureUnit.Texture9);
            gl.BindTexture(TextureTarget.Texture2D, 0);
            gl.ActiveTexture(TextureUnit.Texture7);
            gl.BindTexture(TextureTarget.Texture2D, 0);
            gl.ActiveTexture(TextureUnit.Texture0);
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
                ? [_fxPreviewNotice, _mapFxPreviewNotice, _materialTextures.Error, _skies.Notice,
                    _water.Notice, _reflections.Notice, _compiledWaterNotice,
                    _compiledModelFailures.Count == 0 ? null : "Compiled models unavailable: " +
                        string.Join("; ", _compiledModelFailures.Select(item => $"{item.Key}: {item.Value}")),
                    _walkPlayerNotice]
                : [session.Scene.Notice, _fxPreviewNotice, _mapFxPreviewNotice, _materialTextures.Error,
                    _skies.Notice, _water.Notice,
                    HasAnimatedWater ? _reflections.Notice : null,
                    previewLighting ? _lighting.GetNotice(_sunlight.IsAvailable) : null,
                    previewLighting ? _shadows.Notice : null, previewLighting ? _sunlight.Notice : null,
                    previewLighting ? _stageLightingNotice : null,
                    _walkPlayerNotice];
            string notice = string.Join('\n', notices.Where(value => !string.IsNullOrEmpty(value)));
            PublishStatus(notice.Length == 0 ? null : notice);
        }
        catch (Exception exception) when (IsRenderException(exception))
        {
            _destructiblePreparation?.TrySetException(exception);
            CancelDestructiblePreparation();
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
            gl.ActiveTexture(TextureUnit.Texture8);
            gl.BindTexture(TextureTarget.Texture2D, 0);
            gl.ActiveTexture(TextureUnit.Texture7);
            gl.BindTexture(TextureTarget.Texture2D, 0);
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

    private void SyncWalkPlayer(GL gl, bool show)
    {
        if (!ReferenceEquals(_walkPlayer, _uploadedWalkPlayer))
        {
            _walkPlayerGl?.Delete(gl);
            _walkPlayerGl = null;
            _uploadedWalkPlayer = _walkPlayer;
        }
        if (!show || _walkPlayer is null || _walkPlayerGl is not null || _walkPlayerFailed) return;
        try { _walkPlayerGl = WalkPlayerGl.Create(gl, _walkPlayer); }
        catch (Exception exception) when (IsRenderException(exception) || exception is OutOfMemoryException)
        {
            _walkPlayerFailed = true;
            _walkPlayerNotice = $"Walk viewmodel upload: {exception.Message}";
        }
    }

    private unsafe void RenderWalkPlayer(GL gl, PixelSize size, Vector3 eye, Vector3 forward,
        bool previewLighting, byte sunIndex, double seconds, float horizontalMotionAmount, bool running)
    {
        if (_walkPlayer is not { } preview || _walkPlayerGl is not { } gpu) return;
        gpu.UploadFrame(gl, preview.Sample(seconds, horizontalMotionAmount, running, out Vector3 viewOrigin));
        // Preserve the editor's first-person framing and depth range while
        // placing the posed rig in the map's world space for light/shadow lookup.
        forward = Vector3.Normalize(forward);
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitZ));
        Vector3 up = Vector3.Cross(right, forward);
        var rotation = new Matrix4x4(
            forward.X, forward.Y, forward.Z, 0,
            -right.X, -right.Y, -right.Z, 0,
            up.X, up.Y, up.Z, 0,
            0, 0, 0, 1);
        Matrix4x4 model = rotation;
        model.Translation = eye - Vector3.TransformNormal(viewOrigin, rotation);
        const float near = 0.01f, far = 4096f;
        float aspect = size.Width / (float)size.Height;
        float y = 1 / MathF.Tan(MathF.PI / 6);
        var projection = new Matrix4x4(y / aspect, 0, 0, 0, 0, y, 0, 0,
            0, 0, -(far + near) / (far - near), -1, 0, 0, -2 * far * near / (far - near), 0);
        Matrix4x4 viewProjection = Matrix4x4.CreateLookAt(eye, eye + forward, up) * projection;
        gl.UseProgram(_program);
        gl.UniformMatrix4(_viewProjectionLocation, 1, false, (float*)&viewProjection);
        gl.UniformMatrix4(_modelLocation, 1, false, (float*)&model);
        gl.UniformMatrix4(_normalTransformLocation, 1, false, (float*)&rotation);
        gl.Uniform1(_cubicClipLocation, 0);
        gl.Uniform1(_fogEnabledLocation, 0);
        gl.Uniform1(_waterPreviewLocation, 0);
        gl.Uniform1(_hasWaterReflectionLocation, 0);
        gl.Uniform1(_litLocation, previewLighting ? 1 : 0);
        gl.Uniform1(_texturedLocation, 1);
        _sunlight.Bind(gl, sunIndex, enabled: _compiledPreview is null);
        gl.Uniform3(_eyeLocation, eye.X, eye.Y, eye.Z);
        gl.ColorMask(true, true, true, false);
        gl.DepthMask(true);
        gl.Clear(ClearBufferMask.DepthBufferBit);
        gl.Enable(EnableCap.DepthTest);
        gl.DepthFunc(DepthFunction.Lequal);
        gl.Disable(EnableCap.PolygonOffsetFill);
        gl.BindVertexArray(gpu.VertexArray);
        gl.ActiveTexture(TextureUnit.Texture0);
        foreach (var batch in preview.Batches)
        {
            var texture = preview.Texture(batch.Material);
            SceneMaterialDrawing.Apply(gl, texture.Surface, _alphaTestLocation,
                _premultiplyAlphaLocation, _ignoreVertexColorLocation);
            gl.BindTexture(TextureTarget.Texture2D, gpu.Texture(batch.Material));
            gl.DrawArrays(PrimitiveType.Triangles, batch.Start, (uint)batch.Count);
        }
        gl.DepthMask(true);
        gl.Disable(EnableCap.Blend);
        gl.Disable(EnableCap.CullFace);
        gl.Disable(EnableCap.PolygonOffsetFill);
        gl.Disable(EnableCap.DepthTest);
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
        float incomingOpacity = 1;
        float outgoingOpacity = 0;
        if (_fxOutgoingPreview is not null)
        {
            float progress = Math.Clamp((float)(_fxTransitionClock.Elapsed.TotalMilliseconds /
                FxTransitionMilliseconds), 0, 1);
            if (progress >= 1)
            {
                _fxOutgoingPreview = null;
                _fxTransitionClock.Reset();
            }
            else
            {
                incomingOpacity = progress * progress * (3 - 2 * progress);
                outgoingOpacity = 1 - incomingOpacity;
            }
        }
        string? materialNotice = null;
        gl.BindVertexArray(_fxVertexArray);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _fxVertexBuffer);
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.Uniform1(_waterPreviewLocation, 0);
        gl.Uniform1(_litLocation, 0);
        gl.Uniform1(_texturedLocation, 1);
        if (_fxOutgoingPreview is { } outgoing)
            DrawFxPreview(outgoing, outgoingOpacity, false);
        DrawFxPreview(preview, incomingOpacity, true);
        _fxPreviewNotice = string.Join("; ", new[] { preview.Notice, materialNotice }
            .Where(notice => !string.IsNullOrEmpty(notice)));
        if (_fxPreviewNotice.Length == 0) _fxPreviewNotice = null;
        gl.BindVertexArray(_vertexArray);
        ResetSurfaceState(gl);

        void DrawFxPreview(FxSpritePreview effect, float opacity, bool reportNotice)
        {
            if (opacity <= 0) return;
            var available = new Dictionary<string, MaterialSource>(StringComparer.Ordinal);
            foreach (string material in effect.Materials)
            {
                MaterialSource? source = ResolveFxMaterial(material, resolveMaterial);
                if (source is null)
                {
                    if (reportNotice)
                        materialNotice ??= $"FX material '{material}' is unavailable; load its materials/images catalog.";
                }
                else if (source.Surface.SortKey == (int)MaterialSortKey.Distortion)
                {
                    if (reportNotice) materialNotice ??= "Heat distortion is omitted from this preview.";
                }
                else available.Add(material, source);
            }
            foreach (var (material, sampled) in effect.Sample(eye))
            {
                if (!available.TryGetValue(material, out MaterialSource? source)) continue;
                uint texture = _materialTextures.GetFxTexture(gl, material, resolveMaterial, out string? textureNotice);
                if (texture == 0)
                {
                    if (reportNotice) materialNotice ??= textureNotice;
                    continue;
                }
                SceneMaterialDrawing.Apply(gl, source.Surface, _alphaTestLocation, _premultiplyAlphaLocation,
                    _ignoreVertexColorLocation);
                gl.Disable(EnableCap.CullFace);
                gl.Uniform1(_ignoreVertexColorLocation, 0);
                gl.BindTexture(TextureTarget.Texture2D, texture);
                FxPreviewVertex[] vertices = sampled;
                if (opacity < 1)
                {
                    vertices = new FxPreviewVertex[sampled.Length];
                    // Additive One/One materials need their RGB attenuated; premultiplied
                    // One/InverseSourceAlpha and SourceAlpha materials already fade with alpha.
                    bool premultiplied = source.Surface.BlendOperation == GfxBlendOperation.Add &&
                        source.Surface.Source == GfxBlend.One &&
                        source.Surface.Destination == GfxBlend.InverseSourceAlpha;
                    float rgbOpacity = source.Surface.Source == GfxBlend.One && !premultiplied
                        ? opacity : 1;
                    for (int index = 0; index < sampled.Length; index++)
                    {
                        FxPreviewVertex vertex = sampled[index];
                        Vector4 color = vertex.Color;
                        color.X *= rgbOpacity;
                        color.Y *= rgbOpacity;
                        color.Z *= rgbOpacity;
                        color.W *= opacity;
                        vertices[index] = new FxPreviewVertex(vertex.Position, vertex.Normal, vertex.Uv, color);
                    }
                }
                fixed (FxPreviewVertex* pointer = vertices)
                    gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * sizeof(FxPreviewVertex)), pointer,
                        BufferUsageARB.DynamicDraw);
                gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)vertices.Length);
            }
        }
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
                uint texture = _materialTextures.GetFxTexture(gl, material, resolveMaterial, out string? textureNotice);
                if (texture == 0)
                {
                    materialNotice ??= textureNotice;
                    continue;
                }
                SceneMaterialDrawing.Apply(gl, source.Surface, _alphaTestLocation, _premultiplyAlphaLocation,
                    _ignoreVertexColorLocation);
                gl.Disable(EnableCap.CullFace);
                gl.Uniform1(_ignoreVertexColorLocation, 0);
                gl.BindTexture(TextureTarget.Texture2D, texture);
                fixed (FxPreviewVertex* pointer = vertices)
                    gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * sizeof(FxPreviewVertex)), pointer,
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
        if (source is null || string.IsNullOrEmpty(source.TechniqueSet) || !source.HasPreviewImage)
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
            RenderPreviewModel(gl, model, XModelGeometry.Transform(entity), resolveMaterial,
                previewLighting, previewAlpha, transparent);
        }
        RestoreModelDrawState(gl);
    }

    private void RenderDestructiblePreview(GL gl, EditorScene scene,
        Func<string, MaterialSource?>? resolveMaterial, bool previewLighting, bool previewAlpha, bool transparent)
    {
        if (scene.DestructiblePreviewEntity is not { } entity || _destructiblePreviewModel is not { } model) return;
        gl.Uniform1(_waterPreviewLocation, 0);
        RenderPreviewModel(gl, model, XModelGeometry.Transform(entity), resolveMaterial,
            previewLighting, previewAlpha, transparent, withOutline: true);
        RestoreModelDrawState(gl);
    }

    private unsafe void RenderDestructibleOutline(GL gl, EditorScene scene, EditorSession session)
    {
        if (scene.DestructiblePreviewEntity is not { } entity ||
            scene.DestructiblePreviewSource is not { } source || !session.Selection.Contains(source) ||
            _destructiblePreviewModel is not { } model ||
            !_previewMeshes.TryGetValue(model, out PreviewModelMesh? mesh) || mesh.OutlineCount == 0) return;
        Matrix4x4 transform = XModelGeometry.Transform(entity);
        gl.UniformMatrix4(_modelLocation, 1, false, (float*)&transform);
        gl.BindVertexArray(mesh.VertexArray);
        gl.DrawArrays(PrimitiveType.Lines, mesh.OutlineStart, (uint)mesh.OutlineCount);
        RestoreModelDrawState(gl);
    }

    private void RenderCompiledModels(GL gl, Func<string, MaterialSource?>? resolveMaterial,
        bool previewAlpha, Vector3 eye, bool transparent)
    {
        if (_compiledPreview is not { } preview || preview.Models.Count == 0) return;
        gl.Uniform1(_waterPreviewLocation, 0);
        IEnumerable<(string Name, Matrix4x4 Transform, XModelSource Source)> instances = transparent
            ? preview.Models.OrderByDescending(item => Vector3.DistanceSquared(
                new Vector3(item.Transform.M41, item.Transform.M42, item.Transform.M43), eye))
            : preview.Models;
        foreach (var instance in instances)
        {
            if (_compiledModelFailures.ContainsKey(instance.Name)) continue;
            try
            {
                RenderPreviewModel(gl, instance.Source, instance.Transform, resolveMaterial,
                    false, previewAlpha, transparent);
            }
            catch (Exception exception) when (IsRenderException(exception) ||
                                              exception is IndexOutOfRangeException or KeyNotFoundException)
            {
                _compiledModelFailures.TryAdd(instance.Name, exception.Message);
            }
        }
        RestoreModelDrawState(gl);
    }

    private unsafe void RenderPreviewModel(GL gl, XModelSource model, Matrix4x4 transform,
        Func<string, MaterialSource?>? resolveMaterial, bool previewLighting, bool previewAlpha, bool transparent,
        bool withOutline = false)
    {
        if (!_previewMeshes.TryGetValue(model, out PreviewModelMesh? mesh))
        {
            mesh = PreviewModelMesh.Create(gl, model, withOutline);
            _previewMeshes.Add(model, mesh);
        }
        else if (withOutline && mesh.OutlineCount == 0)
        {
            mesh.Delete(gl);
            mesh = PreviewModelMesh.Create(gl, model, withOutline: true);
            _previewMeshes[model] = mesh;
        }
        if (!Matrix4x4.Invert(transform, out Matrix4x4 inverse)) return;
        Matrix4x4 normalTransform = Matrix4x4.Transpose(inverse);
        gl.UniformMatrix4(_modelLocation, 1, false, (float*)&transform);
        gl.UniformMatrix4(_normalTransformLocation, 1, false, (float*)&normalTransform);
        if (previewLighting)
        {
            var (minimum, maximum) = model.Bounds;
            byte sunIndex = _stages?.SunIndexAt(Vector3.Transform(
                minimum * 0.5f + maximum * 0.5f, transform)) ?? (byte)1;
            _sunlight.Bind(gl, sunIndex);
        }
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

    private unsafe void RestoreModelDrawState(GL gl)
    {
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
        if (_destructiblePreviewModel is { } car) used.Add(car);
        if (_destructibleWreckModel is { } wreck) used.Add(wreck);
        used.UnionWith(_preparedDestructibleModels);
        if (_compiledPreview is { } compiled)
            used.UnionWith(compiled.Models.Select(item => item.Source));
        foreach (XModelSource model in _previewMeshes.Keys.Where(model => !used.Contains(model)).ToArray())
        {
            _previewMeshes[model].Delete(gl);
            _previewMeshes.Remove(model);
        }
        _previewMeshesDirty = false;
    }

    private void ProcessDestructiblePreparation(GL gl, Func<string, MaterialSource?>? resolveMaterial)
    {
        if (_destructiblePreparation is not { } preparation) return;
        foreach (XModelSource model in _preparedDestructibleModels)
            if (!_previewMeshes.ContainsKey(model))
            {
                if (!_preparedDestructibleData.TryGetValue(model, out PreparedPreviewModelMesh? data))
                    throw new InvalidOperationException("The prepared destructible mesh was invalidated.");
                _previewMeshes.Add(model, PreviewModelMesh.Upload(gl, data));
                break; // Keep each frame's GL mesh upload bounded.
            }
        foreach (string material in _destructiblePreparationMaterials)
            _materialTextures.GetTexture(gl, material, resolveMaterial);
        if (!_materialTextures.HasPendingTextures && _preparedDestructibleModels.All(_previewMeshes.ContainsKey) &&
            _destructiblePreparationMaterials.All(_materialTextures.IsReady))
        {
            string[] unavailable = _materialTextures.Unavailable(_destructiblePreparationMaterials).ToArray();
            string? fallbackNotice = unavailable.Length == 0 ? null :
                "Model or FX textures unavailable: " + string.Join(", ", unavailable);
            string? notice = string.Join("; ", new[] { _destructiblePreparationNotice, fallbackNotice }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            preparation.TrySetResult(notice.Length == 0 ? null : notice);
            _destructiblePreparation = null;
            _preparedDestructibleData = new Dictionary<XModelSource, PreparedPreviewModelMesh>(ReferenceEqualityComparer.Instance);
        }
    }

    private IEnumerable<string> RetainedTextureMaterials(IEnumerable<string> sceneMaterials)
    {
        foreach (string material in sceneMaterials) yield return material;
        foreach (string material in _preparedFxMaterials) yield return material;
        if (_fxPreview is { } effect)
            foreach (string material in effect.Materials) yield return material;
        if (_fxOutgoingPreview is { } outgoing)
            foreach (string material in outgoing.Materials) yield return material;
        foreach (var (mapEffect, _, _, _) in _mapFxPreviews)
            foreach (string material in mapEffect.Materials) yield return material;
        if (_destructiblePreviewModel is { } car)
            foreach (var material in car.Document.Materials) yield return material.Name;
        if (_destructibleWreckModel is { } wreck)
            foreach (var material in wreck.Document.Materials) yield return material.Name;
        foreach (string material in _destructiblePreparationMaterials) yield return material;
        foreach (XModelSource model in _preparedDestructibleModels)
            foreach (var material in model.Document.Materials) yield return material.Name;
        foreach (var (_, model) in _foliagePreview)
            foreach (var material in model.Document.Materials) yield return material.Name;
        if (_compiledPreview is { } compiled)
            foreach (var model in compiled.Models)
                foreach (var material in model.Source.Document.Materials) yield return material.Name;
    }

    private unsafe void SetPhysicsModel(GL gl, MapEntity? owner)
    {
        Matrix4x4 model = owner is not null && _physicsTransforms?.TryGetValue(owner, out Matrix4x4 pose) == true
            ? pose : Matrix4x4.Identity;
        Matrix4x4 normal = Matrix4x4.Invert(model, out Matrix4x4 inverse)
            ? Matrix4x4.Transpose(inverse) : Matrix4x4.Identity;
        gl.UniformMatrix4(_modelLocation, 1, false, (float*)&model);
        gl.UniformMatrix4(_normalTransformLocation, 1, false, (float*)&normal);
    }

    private void UpdatePhysicsVisibility(Matrix4x4 viewProjection)
    {
        _physicsVisible.Clear();
        if (_physicsTransforms is not { } transforms) return;
        foreach (var (owner, bounds) in _physicsBounds)
            if (transforms.TryGetValue(owner, out Matrix4x4 pose) &&
                IntersectsClip(TransformBounds(bounds, pose), viewProjection))
                _physicsVisible.Add(owner);
    }

    internal static (Vector3 Min, Vector3 Max) TransformBounds((Vector3 Min, Vector3 Max) bounds, Matrix4x4 pose)
    {
        Vector3 min = new(float.PositiveInfinity), max = new(float.NegativeInfinity);
        for (int corner = 0; corner < 8; corner++)
        {
            Vector3 point = new((corner & 1) == 0 ? bounds.Min.X : bounds.Max.X,
                (corner & 2) == 0 ? bounds.Min.Y : bounds.Max.Y,
                (corner & 4) == 0 ? bounds.Min.Z : bounds.Max.Z);
            point = Vector3.Transform(point, pose);
            min = Vector3.Min(min, point);
            max = Vector3.Max(max, point);
        }
        return (min, max);
    }

    private void DrawStaticRange(GL gl, PrimitiveType primitive, int start, int count, bool capture, bool wire = false)
    {
        if (count == 0) return;
        if (capture || _activeDestructibleSource is null)
        {
            gl.DrawArrays(primitive, start, (uint)count);
            return;
        }
        int end = start + count;
        foreach (var range in _destructibleRanges.Where(range => ReferenceEquals(range.Source, _activeDestructibleSource))
                     .OrderBy(range => wire ? range.WireStart : range.Start))
        {
            int hiddenStart = wire ? range.WireStart : range.Start;
            int hiddenEnd = hiddenStart + (wire ? range.WireCount : range.Count);
            if (hiddenEnd <= start || hiddenStart >= end) continue;
            if (hiddenStart > start) gl.DrawArrays(primitive, start, (uint)(hiddenStart - start));
            start = Math.Max(start, hiddenEnd);
            if (start >= end) return;
        }
        gl.DrawArrays(primitive, start, (uint)(end - start));
    }

    private void DrawSourceRange(GL gl, int start, int count, bool capture)
    {
        if (_compiledPreview is not null || _stages?.SunCount is not > 1)
        {
            DrawStaticRange(gl, PrimitiveType.Triangles, start, count, capture);
            return;
        }
        int end = start + count;
        int low = 0, high = _sourceSunRanges.Count;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (_sourceSunRanges[middle].End <= start) low = middle + 1;
            else high = middle;
        }
        for (int index = low; index < _sourceSunRanges.Count && _sourceSunRanges[index].Start < end; index++)
        {
            var range = _sourceSunRanges[index];
            int first = Math.Max(start, range.Start), last = Math.Min(end, range.End);
            _sunlight.Bind(gl, range.SunIndex);
            DrawStaticRange(gl, PrimitiveType.Triangles, first, last - first, capture);
        }
    }

    private bool IsHiddenDestructibleTriangle(int start) => _activeDestructibleSource is not null &&
        _destructibleRanges.Any(range => ReferenceEquals(range.Source, _activeDestructibleSource) &&
            start >= range.Start && start < range.Start + range.Count);

    private void DrawStaticOutlines(GL gl)
    {
        int start = _outlineStart, end = start + _outlineCount;
        if (_activeDestructibleSource is not null)
            foreach (var range in _destructibleOutlines.Where(range => ReferenceEquals(range.Source, _activeDestructibleSource))
                         .OrderBy(range => range.Start))
            {
                if (range.Start > start) gl.DrawArrays(PrimitiveType.Lines, start, (uint)(range.Start - start));
                start = Math.Max(start, range.Start + range.Count);
            }
        if (end > start) gl.DrawArrays(PrimitiveType.Lines, start, (uint)(end - start));
    }

    private void RenderSurfaces(GL gl, Func<string, MaterialSource?>? resolveMaterial, bool previewLighting,
        bool previewAlpha, Vector3 eye, bool transparent, bool capture = false)
    {
        if (_compiledPreview is null) _sunlight.Bind(gl);
        var textures = new Dictionary<string, uint>(StringComparer.Ordinal);
        MapEntity? drawnOwner = null;
        void UseOwner(MapEntity? owner)
        {
            if (_physicsTransforms is null || ReferenceEquals(drawnOwner, owner)) return;
            SetPhysicsModel(gl, owner);
            drawnOwner = owner;
        }
        foreach (var batch in _drawBatches)
        {
            if (batch.Owner is { } owner && (capture || !_physicsVisible.Contains(owner))) continue;
            MaterialSurfaceState state = _surfaceStates[batch.Material];
            MaterialSource? source = resolveMaterial?.Invoke(batch.Material);
            bool water = source?.IsWater == true &&
                (_compiledPreview is null || IsSupportedCompiledWater(source));
            bool drawTransparent = previewAlpha && (state.IsBlended || !state.DepthWrite);
            if (drawTransparent != transparent) continue;
            if (capture && water) continue;
            uint texture = water ? (_water.IsAvailable(batch.Material) ? _lineTexture : 0) : _materialTextures.GetTexture(gl, batch.Material, resolveMaterial);
            if (texture == 0)
            {
                UseOwner(batch.Owner);
                ResetSurfaceState(gl);
                gl.BindTexture(TextureTarget.Texture2D, _lineTexture);
                gl.Uniform1(_litLocation, 0);
                gl.Uniform1(_texturedLocation, 0);
                DrawStaticRange(gl, PrimitiveType.Lines, batch.WireStart, batch.WireCount, capture, wire: true);
                continue;
            }
            if (transparent)
            {
                textures.TryAdd(batch.Material, texture);
                continue;
            }
            SceneMaterialDrawing.Apply(gl, previewAlpha ? state : OpaquePreview(state),
                _alphaTestLocation, _premultiplyAlphaLocation, _ignoreVertexColorLocation);
            if (_compiledPreview is not null) gl.Uniform1(_ignoreVertexColorLocation, 0);
            BindCompiledLightmap(gl, _compiledPreview?.LightingAt(batch.Start) ??
                (CompiledBspPreview.NoLightmap, (byte)0));
            gl.BindTexture(TextureTarget.Texture2D, texture);
            gl.Uniform1(_waterPreviewLocation, water ? 1 : 0);
            if (water) _water.Bind(gl, batch.Material);
            gl.Uniform1(_litLocation, _compiledPreview is null && previewLighting ? 1 : 0);
            gl.Uniform1(_texturedLocation, 1);
            UseOwner(batch.Owner);
            if (water && _compiledPreview is null)
            {
                foreach (var range in _waterDrawRanges[batch.Material])
                {
                    BindWaterReflection(gl, range.Start);
                    DrawSourceRange(gl, range.Start, range.Count, capture);
                }
            }
            else
            {
                if (water) BindWaterReflection(gl, batch.Start);
                DrawSourceRange(gl, batch.Start, batch.Count, capture);
            }
        }
        if (transparent)
        {
            string? material = null;
            bool waterDrawn = false;
            MapEntity? activeOwner = null;
            UseOwner(null);
            _sortedTransparentTriangles.Clear();
            foreach (var triangle in _transparentTriangles)
                if (capture || !IsHiddenDestructibleTriangle(triangle.Start))
                    _sortedTransparentTriangles.Add((null, triangle.Material, triangle.Start, triangle.Center));
            if (!capture && _physicsTransforms is { } transforms)
                foreach (var triangle in _physicsTransparentTriangles)
                    if (_physicsVisible.Contains(triangle.Owner) && transforms.TryGetValue(triangle.Owner, out Matrix4x4 matrix))
                        _sortedTransparentTriangles.Add((triangle.Owner, triangle.Material, triangle.Start,
                            Vector3.Transform(triangle.Center, matrix)));
            _sortedTransparentTriangles.Sort((a, b) =>
            {
                int materialOrder = _surfaceStates[a.Material].SortKey.CompareTo(_surfaceStates[b.Material].SortKey);
                return materialOrder != 0 ? materialOrder :
                    Vector3.DistanceSquared(eye, b.Center).CompareTo(Vector3.DistanceSquared(eye, a.Center));
            });
            // Respect material sort keys, then order individual translucent triangles back to front.
            foreach (var triangle in _sortedTransparentTriangles)
            {
                if (!waterDrawn &&
                    _surfaceStates[triangle.Material].SortKey >= (int)WaterMaterialAuthoring.SurfaceSortKey)
                {
                    DrawWater();
                    material = null;
                }
                if (!textures.TryGetValue(triangle.Material, out uint texture)) continue;
                if (!ReferenceEquals(activeOwner, triangle.Owner))
                {
                    UseOwner(triangle.Owner);
                    activeOwner = triangle.Owner;
                }
                if (material != triangle.Material)
                {
                    MaterialSource? source = resolveMaterial?.Invoke(triangle.Material);
                    bool water = source?.IsWater == true &&
                        (_compiledPreview is null || IsSupportedCompiledWater(source));
                    SceneMaterialDrawing.Apply(gl, _surfaceStates[triangle.Material], _alphaTestLocation, _premultiplyAlphaLocation, _ignoreVertexColorLocation);
                    if (_compiledPreview is not null) gl.Uniform1(_ignoreVertexColorLocation, 0);
                    gl.BindTexture(TextureTarget.Texture2D, texture);
                    gl.Uniform1(_waterPreviewLocation, water ? 1 : 0);
                    if (water) _water.Bind(gl, triangle.Material);
                    gl.Uniform1(_litLocation, _compiledPreview is null && previewLighting ? 1 : 0);
                    gl.Uniform1(_texturedLocation, 1);
                    material = triangle.Material;
                }
                BindCompiledLightmap(gl, _compiledPreview?.LightingAt(triangle.Start) ??
                    (CompiledBspPreview.NoLightmap, (byte)0));
                if (_compiledPreview is null && _stages?.SunCount > 1)
                    _sunlight.Bind(gl, _movePreviewVertices[triangle.Start].SunIndex);
                if ((_compiledPreview is not null && _waterMaterials.Contains(triangle.Material)) ||
                    _waterProbes.ContainsKey(triangle.Start)) BindWaterReflection(gl, triangle.Start);
                gl.DrawArrays(PrimitiveType.Triangles, triangle.Start, 3);
            }
            if (!waterDrawn) DrawWater();

            void DrawWater()
            {
                waterDrawn = true;
                UseOwner(null);
                activeOwner = null;
                // Depth-writing water needs no per-frame CPU triangle sort.
                // Draw cached material/probe ranges after opaque ground and before decals/transparency.
                foreach (var batch in _compiledPreview is null ? _surfaceBatches : _compiledWaterDrawBatches)
                {
                    if (!_waterMaterials.Contains(batch.Material) || !textures.TryGetValue(batch.Material, out uint texture)) continue;
                    SceneMaterialDrawing.Apply(gl, _surfaceStates[batch.Material],
                        _alphaTestLocation, _premultiplyAlphaLocation, _ignoreVertexColorLocation);
                    if (_compiledPreview is not null) gl.Uniform1(_ignoreVertexColorLocation, 0);
                    gl.BindTexture(TextureTarget.Texture2D, texture);
                    gl.Uniform1(_waterPreviewLocation, 1);
                    _water.Bind(gl, batch.Material);
                    gl.Uniform1(_litLocation, _compiledPreview is null && previewLighting ? 1 : 0);
                    gl.Uniform1(_texturedLocation, 1);
                    if (_compiledPreview is not null)
                    {
                        BindCompiledLightmap(gl, _compiledPreview.LightingAt(batch.Start));
                        BindWaterReflection(gl, batch.Start);
                        gl.DrawArrays(PrimitiveType.Triangles, batch.Start, (uint)batch.Count);
                    }
                    else
                    {
                        foreach (var range in _waterDrawRanges[batch.Material])
                        {
                            BindWaterReflection(gl, range.Start);
                            DrawSourceRange(gl, range.Start, range.Count, capture);
                        }
                    }
                }
            }
        }
        UseOwner(null);
        BindCompiledLightmap(gl, (CompiledBspPreview.NoLightmap, (byte)0));
        ResetSurfaceState(gl);
    }

    private void BindCompiledLightmap(GL gl, (int Index, byte PrimaryLightIndex) lighting)
    {
        if (_compiledPreview is null)
        {
            gl.Uniform1(_compiledLightmapModeLocation, 0);
            return;
        }
        int index = lighting.Index;
        uint texture = index >= 0 && index < _compiledDiffuseTextures.Length
            ? _compiledDiffuseTextures[index] : 0;
        uint sunVisibility = lighting.PrimaryLightIndex != 0 && index >= 0 && index < _compiledSunVisibilityTextures.Length
            ? _compiledSunVisibilityTextures[index] : 0;
        int primaryType = 0;
        if (sunVisibility != 0 && _compiledPreview.DirectionalSuns.TryGetValue(lighting.PrimaryLightIndex,
                out var sun))
        {
            primaryType = 1;
            gl.Uniform3(_compiledSunDirectionLocation, sun.Direction.X, sun.Direction.Y, sun.Direction.Z);
            gl.Uniform3(_compiledSunColorLocation, sun.ColorLinear.X, sun.ColorLinear.Y, sun.ColorLinear.Z);
        }
        else if (sunVisibility != 0 && _compiledPreview.PrimaryLocalLights.TryGetValue(lighting.PrimaryLightIndex,
                     out var local))
        {
            primaryType = local.IsSpotlight ? 3 : 2;
            gl.Uniform4(_compiledLocalPositionRadiusLocation, local.Origin.X, local.Origin.Y, local.Origin.Z,
                local.Radius);
            gl.Uniform3(_compiledLocalColorLocation, local.ColorLinear.X, local.ColorLinear.Y, local.ColorLinear.Z);
            if (local.IsSpotlight)
            {
                gl.Uniform4(_compiledSpotDirectionOuterCosLocation, local.Direction.X, local.Direction.Y,
                    local.Direction.Z, local.OuterCos);
                gl.Uniform2(_compiledSpotInnerCosExponentLocation, local.InnerCos, (float)local.Exponent);
            }
            _lighting.BindPrimaryFalloff(gl);
        }
        gl.Uniform1(_compiledPrimaryTypeLocation, primaryType);
        gl.Uniform1(_compiledLightmapModeLocation, index == CompiledBspPreview.NoLightmap
            ? 0 : texture == 0 ? 2 : primaryType != 0 ? 3 : 1);
        gl.ActiveTexture(TextureUnit.Texture7);
        gl.BindTexture(TextureTarget.Texture2D, texture);
        gl.ActiveTexture(TextureUnit.Texture8);
        gl.BindTexture(TextureTarget.Texture2D, sunVisibility);
        gl.ActiveTexture(TextureUnit.Texture0);
    }

    private void BindWaterReflection(GL gl, int start)
    {
        int probe = _compiledPreview?.ReflectionProbeAt(start) ?? _waterProbes.GetValueOrDefault(start);
        bool available = _reflections.Bind(gl, probe);
        gl.Uniform1(_hasWaterReflectionLocation, available ? 1 : 0);
    }

    private bool IsSupportedCompiledWater(MaterialSource source)
    {
        if (!source.IsWater || source.TechniqueSet != "wc_water") return false;
        if (source.Ocean is null) return true;
        return _compiledPreview is { } preview &&
            preview.WaterDefinitions.TryGetValue(source.Name, out WaterMaterialDefinition? definition) &&
            definition.Ocean == source.Ocean;
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
        gl.Uniform1(_compiledLightmapModeLocation, 0);
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
        _compiledWaterNotice = null;
        ClearCompiledLightmaps(gl);
        _stages = null;
        _stageLightingNotice = null;
        if (MapSunProperties.TryRead(session.Scene.Document.World, out MapSunProperties? worldSun, out _) &&
            worldSun is not null)
            try { _stages = MapStageLighting.Read(session.Scene.Document); }
            catch (Exception exception) when (exception is InvalidDataException or NotSupportedException or ArgumentException)
            {
                _stageLightingNotice = $"Stage lighting preview unavailable: {exception.Message}";
            }
        var scene = new SceneGeometry(session.Scene, session.TransformMode, session.Tool, resolveMaterial,
            LeakPath, LeakPointIndex, _fxPreviewOutlinesMarkers || _mapFxPreviews.Count != 0, _stages);
        _destructibleRanges.Clear();
        _destructibleRanges.AddRange(scene.DestructibleRanges);
        _destructibleOutlines.Clear();
        _destructibleOutlines.AddRange(scene.DestructibleOutlines);
        _movePreviewVertices = scene.Vertices;
        _movePreviewRanges.Clear();
        _movePreviewRanges.AddRange(scene.MovePreviewRanges);
        _moveConnectionRanges.Clear();
        _moveConnectionRanges.AddRange(scene.MoveConnectionRanges);
        _lightInfluenceRanges.Clear();
        foreach (var range in scene.LightInfluenceRanges)
            _lightInfluenceRanges.Add(range.Source, (range.Start, range.Count));
        _lightInfluencePreviewSources.Clear();
        _movePreviewSource = session.Selection.Items.OfType<MapEntity>().FirstOrDefault();
        _movePreviewOrigin = _movePreviewSource is null ? Vector3.Zero : EditorSession.EntityOrigin(_movePreviewSource);
        _movePreviewApplied = Vector3.Zero;
        _movePreviewDirty = false;
        if (!session.DeferPreviewLighting) _lighting.Update(gl, session.Scene);
        _batches.Clear();
        _batches.AddRange(scene.Batches);
        _surfaceBatches.Clear();
        _surfaceBatches.AddRange(scene.Batches.Where(batch => resolveMaterial?.Invoke(batch.Material)?.IsSky != true));
        _usedSunIndices.Clear();
        _usedSunIndices.Add(1);
        foreach (var batch in _surfaceBatches)
            for (int index = batch.Start; index < batch.Start + batch.Count; index += 3)
                _usedSunIndices.Add(scene.Vertices[index].SunIndex);
        foreach (var batch in scene.PhysicsBatches)
            for (int index = batch.Start; index < batch.Start + batch.Count; index += 3)
                _usedSunIndices.Add(scene.Vertices[index].SunIndex);
        if (_stages is not null)
            foreach (var (entity, model) in _foliagePreview)
            {
                var (minimum, maximum) = model.Bounds;
                _usedSunIndices.Add(_stages.SunIndexAt(Vector3.Transform(
                    minimum * 0.5f + maximum * 0.5f, XModelGeometry.Transform(entity))));
            }
        _drawBatches.Clear();
        _drawBatches.AddRange(_surfaceBatches.Select(batch => ((MapEntity?)null, batch.Material,
            batch.Start, batch.Count, batch.WireStart, batch.WireCount)));
        _drawBatches.AddRange(scene.PhysicsBatches.Select(batch => ((MapEntity?)batch.Owner, batch.Material,
            batch.Start, batch.Count, batch.WireStart, batch.WireCount)));
        _sourceSunRanges.Clear();
        foreach (var batch in _drawBatches)
        {
            int end = batch.Start + batch.Count;
            for (int start = batch.Start; start < end;)
            {
                byte sunIndex = scene.Vertices[start].SunIndex;
                int next = start + 3;
                while (next < end && scene.Vertices[next].SunIndex == sunIndex) next += 3;
                _sourceSunRanges.Add((start, next, sunIndex));
                start = next;
            }
        }
        _sourceSunRanges.Sort((left, right) => left.Start.CompareTo(right.Start));
        _physicsOutlines.Clear();
        _physicsOutlines.AddRange(scene.PhysicsOutlines);
        _physicsBounds.Clear();
        foreach (var (owner, bounds) in scene.PhysicsBounds) _physicsBounds.Add(owner, bounds);
        _surfaceStates.Clear();
        HasAnimatedWater = false;
        _waterMaterials.Clear();
        foreach (var batch in _drawBatches)
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
            _surfaceStates.TryAdd(batch.Material, state);
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
        _compiledWaterDrawBatches.Clear();
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
        _materialTextures.RemoveUnused(gl, RetainedTextureMaterials(_drawBatches.Select(batch => batch.Material)));
        _skies.RemoveUnused(gl, scene.Batches.Where(batch => resolveMaterial?.Invoke(batch.Material)?.IsSky == true)
            .Select(batch => batch.Material));
        gl.BindVertexArray(_vertexArray);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);
        SceneVertex[] data = scene.Vertices;
        _transparentTriangles.Clear();
        _physicsTransparentTriangles.Clear();
        foreach (var batch in _surfaceBatches)
            if (!_waterMaterials.Contains(batch.Material) && _surfaceStates[batch.Material] is { } state && (state.IsBlended || !state.DepthWrite))
                for (int index = batch.Start; index < batch.Start + batch.Count; index += 3)
                    _transparentTriangles.Add((batch.Material, index,
                        data[index].Position / 3 + data[index + 1].Position / 3 + data[index + 2].Position / 3));
        foreach (var batch in scene.PhysicsBatches)
            if (_surfaceStates[batch.Material] is { } state && (state.IsBlended || !state.DepthWrite))
                for (int index = batch.Start; index < batch.Start + batch.Count; index += 3)
                    _physicsTransparentTriangles.Add((batch.Owner, batch.Material, index,
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
        gl.DisableVertexAttribArray(4);
        gl.BindVertexArray(0);
        _sceneDirty = false;
        _shadowsDirty = _reflectionsDirty = true;
    }

    private bool UpdateLightInfluence(GL gl, EditorScene scene)
    {
        if (_lightInfluencePreviewSources.Count == 0) return true;
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);
        foreach (MapEntity source in _lightInfluencePreviewSources)
        {
            if (!_lightInfluenceRanges.TryGetValue(source, out var range)) return false;
            var vertices = new List<SceneVertex>(range.Count);
            SceneGeometry.AddLightInfluence(vertices, scene, source);
            if (!_lighting.UpdateInfluence(gl, scene, source)) return false;
            if (!UploadPreviewRange(gl, range, vertices)) return false;
        }
        _lightInfluencePreviewSources.Clear();
        return true;
    }

    private unsafe bool UpdatePointEntityMove(GL gl, EditorScene scene)
    {
        _movePreviewDirty = false;
        if (_movePreviewSource is null || _movePreviewRanges.Count == 0) return true;
        Vector3 desired = EditorSession.EntityOrigin(_movePreviewSource) - _movePreviewOrigin;
        Vector3 delta = desired - _movePreviewApplied;
        if (delta == Vector3.Zero) return true;
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);
        foreach (var (start, count) in _movePreviewRanges)
        {
            for (int index = start; index < start + count; index++)
            {
                SceneVertex vertex = _movePreviewVertices[index];
                _movePreviewVertices[index] = new SceneVertex(vertex.Position + delta, vertex.Normal, vertex.Uv,
                    vertex.Color, vertex.SunIndex);
            }
            fixed (SceneVertex* vertices = &_movePreviewVertices[start])
                gl.BufferSubData(BufferTargetARB.ArrayBuffer, (nint)(start * sizeof(SceneVertex)),
                    (nuint)(count * sizeof(SceneVertex)), vertices);
        }
        _movePreviewApplied = desired;
        // Moving a spotlight changes its aim as well as its origin. Rebuild only its guides;
        // lighting/shadows stay at the committed pose until the transform finishes.
        foreach (var (source, range) in _lightInfluenceRanges)
        {
            var vertices = new List<SceneVertex>(range.Count);
            SceneGeometry.AddLightInfluence(vertices, scene, source);
            if (!UploadPreviewRange(gl, range, vertices)) return false;
        }
        foreach (var connection in _moveConnectionRanges)
        {
            var vertices = new List<SceneVertex>(connection.Count);
            SceneGeometry.AddEntityConnection(vertices, scene, connection.Source, connection.Target);
            if (!UploadPreviewRange(gl, (connection.Start, connection.Count), vertices)) return false;
        }
        return true;
    }

    private unsafe bool UploadPreviewRange(GL gl, (int Start, int Count) range, List<SceneVertex> vertices)
    {
        if (vertices.Count > range.Count) return false;
        if (range.Count == 0) return true;
        // A temporarily unresolved cone (for example, at its target) disappears without
        // reallocating scene geometry; the reserved range is reused when it becomes valid.
        var empty = new SceneVertex(Vector3.Zero, Vector3.UnitZ, Vector2.Zero, Vector3.Zero);
        for (int index = 0; index < range.Count; index++)
            _movePreviewVertices[range.Start + index] = index < vertices.Count ? vertices[index] : empty;
        fixed (SceneVertex* pointer = &_movePreviewVertices[range.Start])
            gl.BufferSubData(BufferTargetARB.ArrayBuffer, (nint)(range.Start * sizeof(SceneVertex)),
                (nuint)(range.Count * sizeof(SceneVertex)), pointer);
        return true;
    }

    private unsafe void UploadCompiledScene(GL gl, CompiledBspPreview preview,
        Func<string, MaterialSource?>? resolveMaterial)
    {
        _activeDestructibleSource = null;
        _destructibleRanges.Clear();
        _destructibleOutlines.Clear();
        UploadCompiledLightmaps(gl, preview);
        _movePreviewVertices = [];
        _movePreviewRanges.Clear();
        _moveConnectionRanges.Clear();
        _lightInfluenceRanges.Clear();
        _lightInfluencePreviewSources.Clear();
        _movePreviewSource = null;
        _movePreviewDirty = false;
        _batches.Clear();
        _batches.AddRange(preview.Batches);
        _surfaceBatches.Clear();
        _surfaceBatches.AddRange(preview.Batches.Where(batch =>
            resolveMaterial?.Invoke(batch.Material)?.IsSky != true));
        _drawBatches.Clear();
        _drawBatches.AddRange(_surfaceBatches.Select(batch => ((MapEntity?)null, batch.Material,
            batch.Start, batch.Count, batch.WireStart, batch.WireCount)));
        _physicsOutlines.Clear();
        _physicsBounds.Clear();
        _physicsVisible.Clear();
        _physicsTransparentTriangles.Clear();
        _surfaceStates.Clear();
        _waterMaterials.Clear();
        var unsupportedWater = new HashSet<string>(StringComparer.Ordinal);
        int missingProbeBatches = 0;
        foreach (var batch in _surfaceBatches)
        {
            MaterialSource? source = resolveMaterial?.Invoke(batch.Material);
            _surfaceStates.TryAdd(batch.Material,
                source?.Surface ?? MaterialSurfaceState.Opaque);
            if (source?.IsWater != true) continue;
            if (!IsSupportedCompiledWater(source)) { unsupportedWater.Add(batch.Material); continue; }
            if (!_waterMaterials.Contains(batch.Material, StringComparer.Ordinal))
                _waterMaterials.Add(batch.Material);
            byte probe = preview.ReflectionProbeAt(batch.Start);
            if (probe == 0 || probe >= preview.ReflectionProbeRgbaMips.Count)
                missingProbeBatches++;
        }
        _compiledWaterNotice = string.Join(" ", new[]
        {
            unsupportedWater.Count == 0 ? null :
                $"Unsupported compiled water materials: {string.Join(", ", unsupportedWater)}. Showing material fallback.",
            missingProbeBatches == 0 ? null :
                $"{missingProbeBatches} compiled water batches have no authored baked reflection probe; showing water tint.",
            _waterMaterials.Count == 0 || preview.ReflectionProbeDataError is null ? null :
                $"Compiled reflection-probe data: {preview.ReflectionProbeDataError}"
        }.OfType<string>());
        if (_compiledWaterNotice.Length == 0) _compiledWaterNotice = null;
        _materialTextures.RemoveUnused(gl, RetainedTextureMaterials(_surfaceBatches.Select(batch => batch.Material)));
        _skies.RemoveUnused(gl, preview.Batches.Where(batch =>
            resolveMaterial?.Invoke(batch.Material)?.IsSky == true).Select(batch => batch.Material));
        _waterMaterialBounds.Clear();
        foreach (var batch in _surfaceBatches)
        {
            if (!_waterMaterials.Contains(batch.Material)) continue;
            float height = resolveMaterial?.Invoke(batch.Material)?.Ocean?.Height ?? 0;
            for (int index = batch.Start; index < batch.Start + batch.Count; index++)
            {
                SceneVertex vertex = preview.Vertices[index];
                Vector3 extent = new(height * vertex.Color.X);
                Vector3 minimum = vertex.Position - extent, maximum = vertex.Position + extent;
                _waterMaterialBounds[batch.Material] = _waterMaterialBounds.TryGetValue(batch.Material, out var current)
                    ? (Vector3.Min(current.Min, minimum), Vector3.Max(current.Max, maximum))
                    : (minimum, maximum);
            }
        }
        _water.RemoveUnused(gl, _waterMaterials);
        _water.SetVolumes([]);
        if (_waterMaterials.Count == 0) _reflections.Reload(gl);
        else _reflections.UploadCompiled(gl, preview.ReflectionProbeRgbaMips);
        _probeOrigins.Clear();
        _waterProbes.Clear();
        _waterDrawRanges.Clear();
        _compiledWaterDrawBatches.Clear();
        var batchedWaterMaterials = _waterMaterials.Where(name =>
            preview.WaterDefinitions.ContainsKey(name) &&
            _surfaceStates[name] is { IsBlended: true, DepthWrite: true } state &&
            state.SortKey == (int)WaterMaterialAuthoring.SurfaceSortKey).ToHashSet(StringComparer.Ordinal);
        _compiledWaterDrawBatches.AddRange(_surfaceBatches.Where(batch =>
            batchedWaterMaterials.Contains(batch.Material)));
        _transparentTriangles.Clear();
        foreach (var batch in _surfaceBatches)
            if (!batchedWaterMaterials.Contains(batch.Material) &&
                _surfaceStates[batch.Material] is { } state && (state.IsBlended || !state.DepthWrite))
                for (int index = batch.Start; index < batch.Start + batch.Count; index += 3)
                    _transparentTriangles.Add((batch.Material, index,
                        (preview.Vertices[index].Position + preview.Vertices[index + 1].Position +
                         preview.Vertices[index + 2].Position) / 3));
        _glyphStart = _glyphCount = _gridStart = _gridCount = _highlightStart = _highlightCount =
            _outlineStart = _outlineCount = _axesStart = _axesCount = _leakPathStart = _leakPathCount = 0;
        _surfaceBounds = preview.Bounds;
        HasAnimatedWater = _waterMaterials.Count != 0;
        HasVisibleAnimatedWater = false;
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
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _compiledLightmapUvBuffer);
        Vector2[] lightmapUvs = preview.LightmapUvs;
        fixed (Vector2* pointer = lightmapUvs)
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(lightmapUvs.Length * sizeof(Vector2)), pointer,
                BufferUsageARB.StaticDraw);
        gl.EnableVertexAttribArray(4);
        gl.VertexAttribPointer(4, 2, VertexAttribPointerType.Float, false, (uint)sizeof(Vector2), (void*)0);
        gl.BindVertexArray(0);
        _sceneDirty = false;
        _shadowsDirty = true;
        _reflectionsDirty = false;
    }

    private unsafe void UploadCompiledLightmaps(GL gl, CompiledBspPreview preview)
    {
        ClearCompiledLightmaps(gl);
        _compiledDiffuseTextures = new uint[preview.DiffuseLightmaps.Count];
        _compiledSunVisibilityTextures = new uint[preview.SunVisibilityLightmaps.Count];
        for (int index = 0; index < _compiledDiffuseTextures.Length; index++)
        {
            byte[]? pixels = preview.DiffuseLightmaps[index];
            if (pixels is null) continue;
            uint texture = gl.GenTexture();
            _compiledDiffuseTextures[index] = texture;
            gl.ActiveTexture(TextureUnit.Texture7);
            gl.BindTexture(TextureTarget.Texture2D, texture);
            gl.BindBuffer(BufferTargetARB.PixelUnpackBuffer, 0);
            gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            gl.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
            fixed (byte* pointer = pixels)
                gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8,
                    GfxLightmapCodec.SecondaryWidth, GfxLightmapCodec.SecondaryPlaneHeight, 0,
                    Silk.NET.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, pointer);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            byte[]? primary = preview.SunVisibilityLightmaps[index];
            if (primary is null || preview.SunDataError is not null) continue;
            uint sunTexture = gl.GenTexture();
            _compiledSunVisibilityTextures[index] = sunTexture;
            gl.ActiveTexture(TextureUnit.Texture8);
            gl.BindTexture(TextureTarget.Texture2D, sunTexture);
            gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            fixed (byte* pointer = primary)
                gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.R8,
                    GfxLightmapCodec.PrimaryWidth, GfxLightmapCodec.PrimaryHeight, 0,
                    Silk.NET.OpenGL.PixelFormat.Red, PixelType.UnsignedByte, pointer);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        }
        gl.ActiveTexture(TextureUnit.Texture8);
        gl.BindTexture(TextureTarget.Texture2D, 0);
        gl.ActiveTexture(TextureUnit.Texture7);
        gl.BindTexture(TextureTarget.Texture2D, 0);
        gl.ActiveTexture(TextureUnit.Texture0);
    }

    private void ClearCompiledLightmaps(GL gl)
    {
        foreach (uint texture in _compiledDiffuseTextures)
            if (texture != 0) gl.DeleteTexture(texture);
        foreach (uint texture in _compiledSunVisibilityTextures)
            if (texture != 0) gl.DeleteTexture(texture);
        _compiledDiffuseTextures = [];
        _compiledSunVisibilityTextures = [];
    }

    internal void ReleaseResources()
    {
        CancelDestructiblePreparation();
        if (_gl is { } gl)
        {
            ClearCompiledLightmaps(gl);
            _materialTextures.Clear(gl);
            _lighting.Clear(gl);
            _shadows.Clear(gl);
            _sunlight.Clear(gl);
            _skies.Clear(gl);
            _water.Clear(gl);
            _reflections.Clear(gl);
            foreach (PreviewModelMesh mesh in _previewMeshes.Values) mesh.Delete(gl);
            _walkPlayerGl?.Delete(gl);
            if (_fxVertexBuffer != 0) gl.DeleteBuffer(_fxVertexBuffer);
            if (_fxVertexArray != 0) gl.DeleteVertexArray(_fxVertexArray);
            if (_vertexBuffer != 0) gl.DeleteBuffer(_vertexBuffer);
            if (_compiledLightmapUvBuffer != 0) gl.DeleteBuffer(_compiledLightmapUvBuffer);
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
        _compiledLightmapUvBuffer = 0;
        _compiledDiffuseTextures = [];
        _compiledSunVisibilityTextures = [];
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
        _compiledWaterDrawBatches.Clear();
        _probeOrigins.Clear();
        _reflectionsDirty = true;
        _batches.Clear();
        _surfaceBatches.Clear();
        _drawBatches.Clear();
        _physicsOutlines.Clear();
        _physicsBounds.Clear();
        _physicsVisible.Clear();
        _surfaceStates.Clear();
        _transparentTriangles.Clear();
        _physicsTransparentTriangles.Clear();
        _sortedTransparentTriangles.Clear();
        _movePreviewVertices = [];
        _movePreviewRanges.Clear();
        _moveConnectionRanges.Clear();
        _lightInfluenceRanges.Clear();
        _lightInfluencePreviewSources.Clear();
        _movePreviewSource = null;
        _movePreviewDirty = false;
        _previewMeshes.Clear();
        _activeDestructibleSource = null;
        _destructibleRanges.Clear();
        _destructibleOutlines.Clear();
        _destructiblePreviewModel = null;
        _destructibleWreckModel = null;
        _worldTextureUploaded = false;
        _compiledModelFailures.Clear();
        _compiledWaterNotice = null;
        _stages = null;
        _stageLightingNotice = null;
        _usedSunIndices.Clear();
        _sourceSunRanges.Clear();
        _walkPlayerGl = null;
        _uploadedWalkPlayer = null;
        _walkPlayerFailed = false;
        _walkPlayerNotice = null;
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

    internal sealed record PreparedPreviewModelMesh(SceneVertex[] Vertices,
        IReadOnlyList<(string Material, int Start, int Count)> Batches, int OutlineStart, int OutlineCount);

    private sealed class PreviewModelMesh
    {
        internal required uint VertexArray { get; init; }
        internal required uint VertexBuffer { get; init; }
        internal required IReadOnlyList<(string Material, int Start, int Count)> Batches { get; init; }
        internal required int OutlineStart { get; init; }
        internal required int OutlineCount { get; init; }

        internal static PreviewModelMesh Create(GL gl, XModelSource model, bool withOutline = false) =>
            Upload(gl, Build(model, withOutline));

        internal static PreparedPreviewModelMesh Build(XModelSource model, bool withOutline = false)
        {
            var materials = new Dictionary<string, List<SceneVertex>>(StringComparer.Ordinal);
            var outline = withOutline ? new List<SceneVertex>() : null;
            Vector3 outlineColor = new(1, 0.65f, 0.18f);
            foreach (var triangle in XModelGeometry.GetLocalTriangles(model))
            {
                if (!materials.TryGetValue(triangle.Material, out List<SceneVertex>? vertices))
                    materials.Add(triangle.Material, vertices = []);
                vertices.AddRange([triangle.A, triangle.B, triangle.C]);
                if (outline is not null)
                {
                    AddEdge(triangle.A.Position, triangle.B.Position);
                    AddEdge(triangle.B.Position, triangle.C.Position);
                    AddEdge(triangle.C.Position, triangle.A.Position);
                }
            }
            var data = new List<SceneVertex>();
            var batches = new List<(string Material, int Start, int Count)>();
            foreach (var material in materials)
            {
                int start = data.Count;
                data.AddRange(material.Value);
                batches.Add((material.Key, start, material.Value.Count));
            }
            int outlineStart = data.Count;
            if (outline is not null) data.AddRange(outline);
            return new PreparedPreviewModelMesh(data.ToArray(), batches, outlineStart, outline?.Count ?? 0);

            void AddEdge(Vector3 a, Vector3 b)
            {
                outline!.Add(new SceneVertex(a, Vector3.UnitZ, Vector2.Zero, outlineColor));
                outline.Add(new SceneVertex(b, Vector3.UnitZ, Vector2.Zero, outlineColor));
            }
        }

        internal static unsafe PreviewModelMesh Upload(GL gl, PreparedPreviewModelMesh data)
        {
            uint vertexArray = 0, vertexBuffer = 0;
            try
            {
                vertexArray = gl.GenVertexArray();
                vertexBuffer = gl.GenBuffer();
                gl.BindVertexArray(vertexArray);
                gl.BindBuffer(BufferTargetARB.ArrayBuffer, vertexBuffer);
                fixed (SceneVertex* pointer = data.Vertices)
                    gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(data.Vertices.Length * sizeof(SceneVertex)), pointer,
                        BufferUsageARB.StaticDraw);
                for (uint attribute = 0; attribute < 4; attribute++) gl.EnableVertexAttribArray(attribute);
                gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, (uint)sizeof(SceneVertex), (void*)0);
                gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, (uint)sizeof(SceneVertex), (void*)12);
                gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, (uint)sizeof(SceneVertex), (void*)24);
                gl.VertexAttribPointer(3, 4, VertexAttribPointerType.Float, false, (uint)sizeof(SceneVertex), (void*)32);
                gl.BindVertexArray(0);
                return new PreviewModelMesh { VertexArray = vertexArray, VertexBuffer = vertexBuffer,
                    Batches = data.Batches, OutlineStart = data.OutlineStart, OutlineCount = data.OutlineCount };
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
