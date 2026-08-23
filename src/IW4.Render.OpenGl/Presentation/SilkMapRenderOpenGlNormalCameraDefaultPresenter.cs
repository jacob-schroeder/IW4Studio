using System.Numerics;
using System.Runtime.InteropServices;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Render.EditorPreview;
using IW4.Render.OpenGl.Programs;
using IW4.Render.SceneBuilding;
using IW4.Render.Scheduling.Lifecycle;
using Silk.NET.OpenGL;

namespace IW4.Render.OpenGl.Presentation;

public sealed record
    MapRenderOpenGlNormalCameraDefaultPresentationExecutionResult(
        MapRenderOpenGlNormalCameraDefaultPresentationAdapterPlan Plan,
        bool ResolvedStoredSamplePair,
        bool ExecutedFilmColorManipulation,
        bool ExecutedTranslatedPostFx,
        bool ExecutedGlow,
        int GlowGaussianPassCount,
        int FullscreenDrawCount,
        bool WroteCurrentHostBackBuffer,
        MapRenderPixelExtent HostFramebufferExtent)
{
    public MapRenderPixelExtent SceneTargetExtent => new(
        Plan.DisplayWidth,
        Plan.DisplayHeight);

    public bool RequiresLinearHostScale =>
        SceneTargetExtent != HostFramebufferExtent;

    public MapRenderSurfaceExtents SurfaceExtents => new(
        SceneTargetExtent,
        HostFramebufferExtent);

    public bool UsesLinearHostSampling => true;

    public bool IsSuccess =>
        ResolvedStoredSamplePair &&
        (!Plan.UsesFilmColorManipulation || ExecutedFilmColorManipulation) &&
        ExecutedTranslatedPostFx &&
        (!Plan.UsesGlow || ExecutedGlow) &&
        WroteCurrentHostBackBuffer;
}

/// <summary>
/// Context-owned implementation of the explicitly derived default adapter.
/// Program byte identity and translation are resolved from the canonical
/// material graph; only the two-sample host resolve fragment is authored here.
/// </summary>
internal sealed unsafe class
    SilkMapRenderOpenGlNormalCameraDefaultPresenter : IDisposable
{
    internal const FrontFaceDirection FullscreenHostFrontFace =
        FrontFaceDirection.CW;
    internal const TriangleFace FullscreenHostCullFace = TriangleFace.Front;

    internal const string StoredSamplePairResolveFragmentGlsl = """
        #version 330 core
        uniform sampler2DMS rsxSampler0;
        layout(location = 0) out vec4 FragColor;
        void main()
        {
            ivec2 pixel = ivec2(gl_FragCoord.xy);
            FragColor = 0.5 *
                (texelFetch(rsxSampler0, pixel, 0) +
                 texelFetch(rsxSampler0, pixel, 1));
        }
        """;

    private readonly GL _gl;
    private readonly MapRenderWorldSceneSource _source;
    private readonly MapRenderOpenGlProgramCache _programs;
    private readonly string _contextIdentity;
    private readonly MapRenderOpenGlNormalCameraFullscreenProgramSources
        _sources;
    private readonly MapRenderOpenGlProgramResource _resolveProgram;
    private readonly MapRenderOpenGlProgramResource _postFxProgram;
    private readonly MapRenderOpenGlProgramResource? _postFxColor2Program;
    private readonly MapRenderOpenGlProgramResource? _glowSetupProgram;
    private readonly MapRenderOpenGlProgramResource? _glowApplyProgram;
    private readonly MapRenderOpenGlProgramResource[] _glowFilterPrograms;
    private readonly MapRenderEditorPreviewGlowFilterPass[] _glowFilterPasses;
    private readonly MapRenderEditorPreviewEffectivePostState?
        _effectivePost;
    private readonly bool _usesFilmColorManipulation;
    private readonly bool _usesGlow;
    private readonly bool _usesGlowSetupColor2;
    private readonly MapRenderOpenGlPresentationErrorMonitor _errorMonitor;
    private readonly int _ownerThreadId;
    private uint _resolveVertexArray;
    private uint _resolveVertexBuffer;
    private uint _postFxVertexArray;
    private uint _postFxVertexBuffer;
    private uint _glowVertexArray;
    private uint _glowVertexBuffer;
    private uint _indexBuffer;
    private uint _postFxSampler;
    private uint _glowTarget9Texture;
    private uint _glowTarget10Texture;
    private uint _glowTarget11Texture;
    private uint _glowTarget9Framebuffer;
    private uint _glowTarget10Framebuffer;
    private uint _glowTarget11Framebuffer;
    private int _resolveGeometryWidth;
    private int _resolveGeometryHeight;
    private int _postFxGeometryWidth;
    private int _postFxGeometryHeight;
    private int _glowGeometryWidth;
    private int _glowGeometryHeight;
    private int _resolveMatrixWidth;
    private int _resolveMatrixHeight;
    private int _postFxMatrixWidth;
    private int _postFxMatrixHeight;
    private int _glowApplyMatrixWidth;
    private int _glowApplyMatrixHeight;
    private int _glowTargetWidth;
    private int _glowTargetHeight;
    private int _glowFilterPassCount;
    private byte _glowDynamicTapCountMask;
    private bool _disposed;

    internal SilkMapRenderOpenGlNormalCameraDefaultPresenter(
        GL gl,
        MapRenderWorldSceneSource source,
        MapRenderOpenGlProgramCache programs,
        string contextIdentity,
        MapRenderEditorPreviewEffectivePostState? effectivePost)
    {
        ArgumentNullException.ThrowIfNull(gl);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(programs);
        ArgumentException.ThrowIfNullOrWhiteSpace(contextIdentity);
        if (!string.Equals(
                programs.ContextIdentity,
                contextIdentity,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Fullscreen presenter program cache belongs to another OpenGL context.",
                nameof(programs));
        }
        if (effectivePost is { } post &&
            (post.SourceSnapshot is not { } snapshot ||
             post.Revision.AssetPoolRevision !=
                 source.AssetPoolRevisionAtConstruction ||
             post.Revision.RuntimeRevision !=
                 snapshot.Revision))
        {
            throw new ArgumentException(
                "Fullscreen effective post state must belong to the canonical scene asset revision and its exact atomic runtime snapshot.",
                nameof(effectivePost));
        }
        _gl = gl;
        _source = source;
        _programs = programs;
        _contextIdentity = contextIdentity;
        _effectivePost = effectivePost;
        _usesFilmColorManipulation =
            effectivePost?.SelectsPostFxColor2 == true;
        _usesGlow = effectivePost?.UsesGlow == true;
        _usesGlowSetupColor2 =
            effectivePost?.UsesGlowSetupColor2 == true;
        _ownerThreadId = Environment.CurrentManagedThreadId;
        _sources =
            MapRenderOpenGlNormalCameraFullscreenProgramResolver.Resolve(
                gl,
                source,
                _usesFilmColorManipulation,
                _usesGlow,
                _usesGlowSetupColor2);
        _resolveProgram = programs.GetOrCompile(
            _sources.FullscreenVertexGlsl,
            StoredSamplePairResolveFragmentGlsl);
        _postFxProgram = programs.GetOrCompileAuthored(
            _sources.FullscreenVertexGlsl,
            _sources.PostFxPixelSource);
        _postFxColor2Program = _usesFilmColorManipulation
            ? programs.GetOrCompileAuthored(
                _sources.FullscreenVertexGlsl,
                _sources.PostFxColor2PixelSource ??
                throw new InvalidOperationException(
                    "Film-enabled presentation did not resolve postfx_color2."))
            : null;
        if (_usesGlow)
        {
            MapRenderOpenGlNormalCameraGlowProgramSources glowSources =
                _sources.Glow ?? throw new InvalidOperationException(
                    "Glow-enabled presentation did not resolve its native material graph.");
            _glowSetupProgram = programs.GetOrCompileAuthored(
                glowSources.SetupVertexGlsl,
                glowSources.SetupPixelSource);
            _glowApplyProgram = programs.GetOrCompileAuthored(
                glowSources.ApplyVertexGlsl,
                glowSources.ApplyPixelSource);
            _glowFilterPrograms = glowSources.SymmetricFilters
                .Select(filter => programs.GetOrCompileAuthored(
                    filter.VertexGlsl,
                    filter.PixelSource))
                .ToArray();
            _glowFilterPasses = new
                MapRenderEditorPreviewGlowFilterPass[
                    MapRenderEditorPreviewGlowFilterPlanner
                        .MaximumGaussianPassCount];
        }
        else
        {
            _glowFilterPrograms = [];
            _glowFilterPasses = [];
        }
        _errorMonitor = new MapRenderOpenGlPresentationErrorMonitor(
            new SilkMapRenderOpenGlPresentationErrorApi(gl));

        try
        {
            CreateGeometryResources();
            CreatePostFxSampler();
            CreateGlowResources();
            InitializeSamplerUniforms();
            InitializeFilmCodeConstants();
            InitializeGlowConstants();
            RequireNoError(
                "fullscreen presenter resource initialization",
                MapRenderOpenGlPresentationErrorValidationPoint.Forced);
        }
        catch
        {
            DeleteOwnedResources();
            _errorMonitor.Dispose();
            throw;
        }
    }

    internal MapRenderOpenGlPresentationErrorMonitor ErrorMonitor =>
        _errorMonitor;

    public MapRenderOpenGlNormalCameraDefaultPresentationExecutionResult
        Present(
            MapRenderOpenGlNormalCameraDefaultPresentationAdapterPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return Present(
            plan,
            new MapRenderPixelExtent(
                plan.DisplayWidth,
                plan.DisplayHeight));
    }

    public MapRenderOpenGlNormalCameraDefaultPresentationExecutionResult
        Present(
            MapRenderOpenGlNormalCameraDefaultPresentationAdapterPlan plan,
            MapRenderPixelExtent hostFramebufferExtent)
    {
        return Present(plan, hostFramebufferExtent, hostFramebuffer: 0);
    }

    public MapRenderOpenGlNormalCameraDefaultPresentationExecutionResult
        Present(
            MapRenderOpenGlNormalCameraDefaultPresentationAdapterPlan plan,
            MapRenderPixelExtent hostFramebufferExtent,
            uint hostFramebuffer)
    {
        EnsureUsableOnOwnerThread();
        ArgumentNullException.ThrowIfNull(plan);
        if (hostFramebufferExtent.Width <= 0 ||
            hostFramebufferExtent.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(hostFramebufferExtent));
        }
        if (!ReferenceEquals(plan.Source, _source) ||
            !string.Equals(
                plan.ContextIdentity,
                _contextIdentity,
                StringComparison.Ordinal) ||
            plan.AssetPoolRevision != _sources.AssetPoolRevision ||
            !_source.AssetLookup.HasCanonicalAssetPoolRevision(
                plan.AssetPoolRevision))
        {
            throw new ArgumentException(
                "Default presentation plan belongs to another source, context, or canonical asset revision.",
                nameof(plan));
        }
        if (plan.ResolveSourceSampleCount != 2 ||
            plan.ResolveDestinationSampleCount != 1 ||
            plan.FeedbackReplace.MaterialName != "feedbackreplace" ||
            plan.PostFx.MaterialName != "postfx" ||
            plan.PostFx.CodePixelConstants.Count != 0 ||
            !ReferenceEquals(plan.EffectivePost, _effectivePost) ||
            plan.UsesFilmColorManipulation != _usesFilmColorManipulation ||
            plan.UsesGlow != _usesGlow ||
            plan.UsesGlowSetupColor2 != _usesGlowSetupColor2 ||
            (_usesFilmColorManipulation &&
             (plan.ActivePostFx.MaterialName != "postfx_color2" ||
              _postFxColor2Program is null)))
        {
            throw new InvalidOperationException(
                "Default presentation adapter contract changed.");
        }

        BeginDiagnosticStage("entry before default normal-camera presentation");
        RequireNoError(
            "entry before default normal-camera presentation",
            MapRenderOpenGlPresentationErrorValidationPoint.CapturedOnly);
        BeginDiagnosticStage("default fullscreen render-state application");
        ApplyFullscreenState();
        ResolveScene(plan);
        DrawPostFxToHostBackBuffer(
            plan,
            hostFramebufferExtent,
            plan.ResolvedSceneColor.Resource.TextureHandle,
            hostFramebuffer);
        if (_usesGlow)
        {
            DrawGlowToHostBackBuffer(
                plan,
                hostFramebufferExtent,
                plan.ResolvedSceneColor.Resource.TextureHandle,
                hostFramebuffer);
        }
        BeginDiagnosticStage("default back-buffer handoff");
        HandOffHostBackBuffer(
            hostFramebufferExtent.Width,
            hostFramebufferExtent.Height,
            hostFramebuffer);
        RequireNoError(
            "default back-buffer handoff",
            MapRenderOpenGlPresentationErrorValidationPoint.FrameBoundary);

        return new MapRenderOpenGlNormalCameraDefaultPresentationExecutionResult(
            plan,
            ResolvedStoredSamplePair: true,
            ExecutedFilmColorManipulation: _usesFilmColorManipulation,
            ExecutedTranslatedPostFx: true,
            ExecutedGlow: _usesGlow,
            GlowGaussianPassCount: _glowFilterPassCount,
            FullscreenDrawCount: _usesGlow
                ? 4 + _glowFilterPassCount
                : 2,
            WroteCurrentHostBackBuffer: true,
            HostFramebufferExtent: hostFramebufferExtent);
    }

    internal void ResizeSceneTarget(int width, int height)
    {
        EnsureUsableOnOwnerThread();
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));
        if (!_usesGlow)
            return;

        int quarterWidth = width >> 2;
        int quarterHeight = height >> 2;
        if (quarterWidth <= 0 || quarterHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "Native glow targets require a scene extent of at least 4x4.");
        }

        _glowFilterPassCount =
            MapRenderEditorPreviewGlowFilterPlanner.Generate(
                (_effectivePost ?? throw new InvalidOperationException(
                    "Glow requires renderer-effective post state."))
                    .Glow.Values.Radius,
                width,
                height,
                _glowFilterPasses);
        ResizeGlowTargets(quarterWidth, quarterHeight);
        EnsureGeometry(
            _glowVertexBuffer,
            quarterWidth,
            quarterHeight,
            ref _glowGeometryWidth,
            ref _glowGeometryHeight);
        InitializeGlowMatrices(quarterWidth, quarterHeight);
        InitializeGlowFilterTapConstants();
    }

    public void Dispose()
    {
        EnsureOwnerThread();
        if (_disposed)
            return;
        _disposed = true;
        try
        {
            DeleteOwnedResources();
        }
        finally
        {
            _errorMonitor.Dispose();
        }
    }

    private void ResolveScene(
        MapRenderOpenGlNormalCameraDefaultPresentationAdapterPlan plan)
    {
        EnsureGeometry(
            _resolveVertexBuffer,
            plan.DisplayWidth,
            plan.DisplayHeight,
            ref _resolveGeometryWidth,
            ref _resolveGeometryHeight);
        EnsureFullscreenMatrix(
            _resolveProgram,
            plan.DisplayWidth,
            plan.DisplayHeight,
            ref _resolveMatrixWidth,
            ref _resolveMatrixHeight);
        BeginDiagnosticStage(
            "derived target-2 stored-sample-pair resolve into target 4");
        _gl.BindFramebuffer(
            FramebufferTarget.DrawFramebuffer,
            plan.ResolvedSceneColor.Resource.FramebufferHandle);
        _gl.DrawBuffer(DrawBufferMode.ColorAttachment0);
        _gl.Viewport(
            0,
            0,
            checked((uint)plan.DisplayWidth),
            checked((uint)plan.DisplayHeight));
        _gl.UseProgram(_resolveProgram.Handle);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindSampler(0, 0);
        _gl.BindTexture(
            TextureTarget.Texture2DMultisample,
            plan.SceneColor.Resource.TextureHandle);
        DrawFullscreenQuad(_resolveVertexArray);
        RequireNoError(
            "derived target-2 stored-sample-pair resolve into target 4");
    }

    private void DrawPostFxToHostBackBuffer(
        MapRenderOpenGlNormalCameraDefaultPresentationAdapterPlan plan,
        MapRenderPixelExtent hostFramebufferExtent,
        uint inputTexture,
        uint hostFramebuffer)
    {
        MapRenderOpenGlProgramResource activePostFxProgram =
            _usesFilmColorManipulation
                ? _postFxColor2Program ??
                  throw new InvalidOperationException(
                      "The selected postfx_color2 program was not compiled.")
                : _postFxProgram;
        EnsureGeometry(
            _postFxVertexBuffer,
            hostFramebufferExtent.Width,
            hostFramebufferExtent.Height,
            ref _postFxGeometryWidth,
            ref _postFxGeometryHeight);
        EnsureFullscreenMatrix(
            activePostFxProgram,
            hostFramebufferExtent.Width,
            hostFramebufferExtent.Height,
            ref _postFxMatrixWidth,
            ref _postFxMatrixHeight);
        BeginDiagnosticStage(_usesFilmColorManipulation
            ? "translated postfx_color2 target-4 late pass into host back buffer"
            : "translated postfx target-4 copy into host back buffer");
        _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, hostFramebuffer);
        _gl.DrawBuffer(HostDrawBuffer(hostFramebuffer));
        _gl.Viewport(
            0,
            0,
            checked((uint)hostFramebufferExtent.Width),
            checked((uint)hostFramebufferExtent.Height));
        _gl.UseProgram(activePostFxProgram.Handle);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(
            TextureTarget.Texture2D,
            inputTexture);
        _gl.BindSampler(0, _postFxSampler);
        DrawFullscreenQuad(_postFxVertexArray);
        RequireNoError(_usesFilmColorManipulation
            ? "translated postfx_color2 target-4 late pass into host back buffer"
            : "translated postfx target-4 copy into host back buffer");
    }

    private void DrawGlowToHostBackBuffer(
        MapRenderOpenGlNormalCameraDefaultPresentationAdapterPlan plan,
        MapRenderPixelExtent hostFramebufferExtent,
        uint resolvedSceneTexture,
        uint hostFramebuffer)
    {
        MapRenderOpenGlProgramResource setupProgram =
            _glowSetupProgram ?? throw new InvalidOperationException(
                "Glow setup program is unavailable.");
        MapRenderOpenGlProgramResource applyProgram =
            _glowApplyProgram ?? throw new InvalidOperationException(
                "Glow apply program is unavailable.");
        int expectedWidth = plan.DisplayWidth >> 2;
        int expectedHeight = plan.DisplayHeight >> 2;
        if (_glowTargetWidth != expectedWidth ||
            _glowTargetHeight != expectedHeight ||
            _glowTarget9Texture == 0 ||
            _glowTarget10Texture == 0 ||
            _glowTarget11Texture == 0)
        {
            throw new InvalidOperationException(
                "Glow targets must be materialized by ResizeSceneTarget before presentation; frame execution never allocates them.");
        }

        ApplyGlowReplaceState();
        bool hasGaussianPasses = _glowFilterPassCount > 0;
        uint setupDestinationFramebuffer = hasGaussianPasses
            ? _glowTarget9Framebuffer
            : _glowTarget11Framebuffer;
        uint setupDestinationTexture = hasGaussianPasses
            ? _glowTarget9Texture
            : _glowTarget11Texture;
        DrawGlowOffscreenPass(
            setupProgram,
            setupDestinationFramebuffer,
            resolvedSceneTexture,
            "native glow setup/downsample target 4 into quarter target");

        uint inputTexture = setupDestinationTexture;
        for (int passIndex = 0;
             passIndex < _glowFilterPassCount;
             passIndex++)
        {
            ref MapRenderEditorPreviewGlowFilterPass filterPass =
                ref _glowFilterPasses[passIndex];
            if ((uint)(filterPass.TapHalfCount - 1) >=
                (uint)_glowFilterPrograms.Length)
            {
                throw new InvalidOperationException(
                    $"Native glow produced unsupported symmetric tap count {filterPass.TapHalfCount}.");
            }
            MapRenderOpenGlProgramResource filterProgram =
                _glowFilterPrograms[filterPass.TapHalfCount - 1];
            if ((_glowDynamicTapCountMask &
                 (1 << (filterPass.TapHalfCount - 1))) != 0)
            {
                BindGlowFilterTapConstants(filterProgram, in filterPass);
            }

            bool isFinalPass =
                passIndex == _glowFilterPassCount - 1;
            uint outputFramebuffer = isFinalPass
                ? _glowTarget11Framebuffer
                : (passIndex & 1) == 0
                    ? _glowTarget10Framebuffer
                    : _glowTarget9Framebuffer;
            uint outputTexture = isFinalPass
                ? _glowTarget11Texture
                : (passIndex & 1) == 0
                    ? _glowTarget10Texture
                    : _glowTarget9Texture;
            DrawGlowOffscreenPass(
                filterProgram,
                outputFramebuffer,
                inputTexture,
                "native glow symmetric filter pass");
            inputTexture = outputTexture;
        }

        EnsureFullscreenMatrix(
            applyProgram,
            hostFramebufferExtent.Width,
            hostFramebufferExtent.Height,
            ref _glowApplyMatrixWidth,
            ref _glowApplyMatrixHeight);
        BeginDiagnosticStage(
            "native glow target 11 screen-blend apply into host back buffer");
        ApplyGlowScreenBlendState();
        _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, hostFramebuffer);
        _gl.DrawBuffer(HostDrawBuffer(hostFramebuffer));
        _gl.Viewport(
            0,
            0,
            checked((uint)hostFramebufferExtent.Width),
            checked((uint)hostFramebufferExtent.Height));
        _gl.UseProgram(applyProgram.Handle);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _glowTarget11Texture);
        _gl.BindSampler(0, _postFxSampler);
        DrawFullscreenQuad(_postFxVertexArray);
        RequireNoError(
            "native glow target 11 screen-blend apply into host back buffer");
    }

    private void DrawGlowOffscreenPass(
        MapRenderOpenGlProgramResource program,
        uint destinationFramebuffer,
        uint inputTexture,
        string stage)
    {
        BeginDiagnosticStage(stage);
        _gl.BindFramebuffer(
            FramebufferTarget.DrawFramebuffer,
            destinationFramebuffer);
        _gl.DrawBuffer(DrawBufferMode.ColorAttachment0);
        _gl.Viewport(
            0,
            0,
            checked((uint)_glowTargetWidth),
            checked((uint)_glowTargetHeight));
        _gl.UseProgram(program.Handle);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, inputTexture);
        _gl.BindSampler(0, _postFxSampler);
        DrawFullscreenQuad(_glowVertexArray);
        RequireNoError(stage);
    }

    private void BindGlowFilterTapConstants(
        MapRenderOpenGlProgramResource program,
        in MapRenderEditorPreviewGlowFilterPass pass)
    {
        _gl.UseProgram(program.Handle);
        for (int tapIndex = 0;
             tapIndex < pass.TapHalfCount;
             tapIndex++)
        {
            Vector4 tap = pass.GetTap(tapIndex);
            SetVertexConstant(
                program,
                12 + tapIndex,
                tap.X,
                tap.Y,
                tap.Z,
                tap.W);
            int codeRow =
                (int)MaterialConstantSource.FilterTap0 + tapIndex;
            if (!program.TryGetCodePixelConstantUniformLocation(
                    codeRow,
                    out int location))
            {
                throw new InvalidOperationException(
                    $"Symmetric glow program has no active direct row 0x{codeRow:X2} uniform.");
            }
            _gl.Uniform4(location, tap.X, tap.Y, tap.Z, tap.W);
        }
    }

    private void InitializeGlowFilterTapConstants()
    {
        Span<byte> programUseCounts = stackalloc byte[
            MapRenderEditorPreviewGlowFilterPlanner.MaximumTapHalfCount];
        for (int passIndex = 0;
             passIndex < _glowFilterPassCount;
             passIndex++)
        {
            int tapHalfCount = _glowFilterPasses[passIndex].TapHalfCount;
            if ((uint)(tapHalfCount - 1) >=
                (uint)programUseCounts.Length)
            {
                throw new InvalidOperationException(
                    $"Native glow produced unsupported symmetric tap count {tapHalfCount}.");
            }
            programUseCounts[tapHalfCount - 1]++;
        }

        _glowDynamicTapCountMask = 0;
        for (int tapIndex = 0; tapIndex < programUseCounts.Length; tapIndex++)
        {
            if (programUseCounts[tapIndex] > 1)
                _glowDynamicTapCountMask |= checked((byte)(1 << tapIndex));
        }

        // Each unique symmetric program keeps its uniforms until the next
        // resize. Only a pathological radius that reuses the same tap-count
        // program requires per-draw updates. Boneyard's native chain uses
        // distinct programs, so its steady-state glow path uploads no taps.
        for (int passIndex = 0;
             passIndex < _glowFilterPassCount;
             passIndex++)
        {
            ref MapRenderEditorPreviewGlowFilterPass pass =
                ref _glowFilterPasses[passIndex];
            int programIndex = pass.TapHalfCount - 1;
            if ((_glowDynamicTapCountMask & (1 << programIndex)) == 0)
            {
                BindGlowFilterTapConstants(
                    _glowFilterPrograms[programIndex],
                    in pass);
            }
        }
        _gl.UseProgram(0);
    }

    private void ApplyGlowReplaceState()
    {
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.FramebufferSrgb);
    }

    private void ApplyGlowScreenBlendState()
    {
        // Exact loadBits 0x192A892A/0xE00E0002 decode to ADD with
        // ONE_MINUS_DST_COLOR, ONE for both RGB and alpha.
        _gl.Enable(EnableCap.Blend);
        _gl.BlendEquationSeparate(
            BlendEquationModeEXT.FuncAdd,
            BlendEquationModeEXT.FuncAdd);
        _gl.BlendFuncSeparate(
            BlendingFactor.OneMinusDstColor,
            BlendingFactor.One,
            BlendingFactor.OneMinusDstColor,
            BlendingFactor.One);
        _gl.Disable(EnableCap.FramebufferSrgb);
    }

    private void ApplyFullscreenState()
    {
        _gl.Disable(EnableCap.ScissorTest);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.StencilTest);
        _gl.Disable(EnableCap.Blend);
        // feedbackreplace/postfx share official state words
        // 0x18128812/0xE00E0002. They select RSX front-face culling. The same
        // upper-left to lower-left window-origin parity used by world pass
        // state requires CW on OpenGL; omitting it culls both translated quads.
        _gl.FrontFace(FullscreenHostFrontFace);
        _gl.Enable(EnableCap.CullFace);
        _gl.CullFace(FullscreenHostCullFace);
        _gl.Disable(EnableCap.PolygonOffsetFill);
        _gl.Disable(EnableCap.PolygonOffsetLine);
        _gl.Disable(EnableCap.PolygonOffsetPoint);
        _gl.Disable(EnableCap.Multisample);
        _gl.Disable(EnableCap.SampleAlphaToCoverage);
        _gl.Disable(EnableCap.SampleAlphaToOne);
        _gl.Disable(EnableCap.SampleMask);
        _gl.Disable(EnableCap.FramebufferSrgb);
        _gl.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);
        _gl.ColorMask(true, true, true, true);
        _gl.DepthMask(false);
    }

    private void DrawFullscreenQuad(uint vertexArray)
    {
        _gl.BindVertexArray(vertexArray);
        _gl.DrawElements(
            PrimitiveType.Triangles,
            6,
            DrawElementsType.UnsignedShort,
            null);
    }

    private void BindFullscreenMatrix(
        MapRenderOpenGlProgramResource program,
        int width,
        int height)
    {
        SetVertexConstant(program, 0, 2f / width, 0f, 0f, 0f);
        SetVertexConstant(program, 1, 0f, -2f / height, 0f, 0f);
        SetVertexConstant(program, 2, 0f, 0f, 1f, 0f);
        SetVertexConstant(program, 3, -1f, 1f, 0f, 1f);
    }

    private void SetVertexConstant(
        MapRenderOpenGlProgramResource program,
        int destination,
        float x,
        float y,
        float z,
        float w)
    {
        if (!program.TryGetVertexConstantUniformLocation(
                destination,
                out int location))
        {
            throw new InvalidOperationException(
                $"Fullscreen translated program has no active WVP0 row c{destination}.");
        }
        _gl.Uniform4(location, x, y, z, w);
    }

    private void BindSamplerUniformZero(
        MapRenderOpenGlProgramResource program)
    {
        if (!program.TryGetSamplerUniformLocation(0, out int location))
        {
            throw new InvalidOperationException(
                "Fullscreen program has no active rsxSampler0 destination.");
        }
        _gl.Uniform1(location, 0);
    }

    private void InitializeSamplerUniforms()
    {
        _gl.UseProgram(_resolveProgram.Handle);
        BindSamplerUniformZero(_resolveProgram);
        _gl.UseProgram(_postFxProgram.Handle);
        BindSamplerUniformZero(_postFxProgram);
        if (_postFxColor2Program is not null)
        {
            _gl.UseProgram(_postFxColor2Program.Handle);
            BindSamplerUniformZero(_postFxColor2Program);
        }
        if (_glowSetupProgram is not null)
        {
            _gl.UseProgram(_glowSetupProgram.Handle);
            BindSamplerUniformZero(_glowSetupProgram);
        }
        if (_glowApplyProgram is not null)
        {
            _gl.UseProgram(_glowApplyProgram.Handle);
            BindSamplerUniformZero(_glowApplyProgram);
        }
        foreach (MapRenderOpenGlProgramResource filterProgram in
                 _glowFilterPrograms)
        {
            _gl.UseProgram(filterProgram.Handle);
            BindSamplerUniformZero(filterProgram);
        }
        _gl.UseProgram(0);
    }

    private void InitializeFilmCodeConstants()
    {
        if (_postFxColor2Program is null)
            return;
        MapRenderEditorPreviewEffectivePostState post = _effectivePost ??
            throw new InvalidOperationException(
                "A postfx_color2 program requires renderer-effective post state.");
        IReadOnlyList<MapRenderEditorPreviewFilmCodeConstantRow> rows =
            MapRenderEditorPreviewFilmCodeConstantProducer.Produce(
                post.Film.Values,
                post.Film.Mixer);
        _gl.UseProgram(_postFxColor2Program.Handle);
        foreach (MapRenderEditorPreviewFilmCodeConstantRow row in rows)
        {
            if (!_postFxColor2Program
                    .TryGetCodePixelConstantUniformLocation(
                        row.SourceRowIndex,
                        out int location))
            {
                throw new InvalidOperationException(
                    $"postfx_color2 has no active direct row 0x{row.SourceRowIndex:X2} uniform.");
            }
            _gl.Uniform4(
                location,
                row.Value.X,
                row.Value.Y,
                row.Value.Z,
                row.Value.W);
        }
        _gl.UseProgram(0);
    }

    private void InitializeGlowConstants()
    {
        if (_glowSetupProgram is null || _glowApplyProgram is null)
            return;

        MapRenderEditorPreviewEffectivePostState post = _effectivePost ??
            throw new InvalidOperationException(
                "Glow programs require renderer-effective post state.");
        _gl.UseProgram(_glowSetupProgram.Handle);
        // PS3 copies the 0x540-byte GfxCmdBufInput block into
        // GfxViewInfo+0x19A0 and the setup pass has tapHalfCount=0, so it
        // never overwrites row 0x15. IW3 initializes that unused front-end
        // filter row to zero. The official PS3 setup shader consumes c16 and
        // its embedded c467=(0.5,0,0,0), with no contradictory PS3 writer.
        SetVertexConstant(_glowSetupProgram, 16, 0f, 0f, 0f, 0f);
        SetVertexConstant(_glowSetupProgram, 467, 0.5f, 0f, 0f, 0f);

        IReadOnlyList<MapRenderEditorPreviewGlowCodeConstantRow> glowRows =
            MapRenderEditorPreviewGlowCodeConstantProducer.Produce(
                post.Glow.Values);
        BindCodePixelConstant(
            _glowSetupProgram,
            glowRows[0].SourceRowIndex,
            glowRows[0].Value,
            "glow setup");
        _gl.UseProgram(_glowApplyProgram.Handle);
        BindCodePixelConstant(
            _glowApplyProgram,
            glowRows[1].SourceRowIndex,
            glowRows[1].Value,
            "glow apply");

        IReadOnlyList<MapRenderEditorPreviewFilmCodeConstantRow> filmRows =
            MapRenderEditorPreviewFilmCodeConstantProducer.Produce(
                post.Film.Values,
                post.Film.Mixer);
        _gl.UseProgram(_glowSetupProgram.Handle);
        foreach (MapRenderEditorPreviewFilmCodeConstantRow row in filmRows)
        {
            if (!_glowSetupProgram.TryGetCodePixelConstantUniformLocation(
                    row.SourceRowIndex,
                    out int location))
            {
                if (row.SourceRowIndex ==
                        MapRenderEditorPreviewFilmCodeConstantProducer
                            .ColorTintQuadraticRowIndex &&
                    !_usesGlowSetupColor2)
                {
                    continue;
                }
                throw new InvalidOperationException(
                    $"Glow setup has no active direct row 0x{row.SourceRowIndex:X2} uniform.");
            }
            _gl.Uniform4(
                location,
                row.Value.X,
                row.Value.Y,
                row.Value.Z,
                row.Value.W);
        }
        _gl.UseProgram(0);
    }

    private void BindCodePixelConstant(
        MapRenderOpenGlProgramResource program,
        ushort sourceRow,
        Vector4 value,
        string materialIdentity)
    {
        if (!program.TryGetCodePixelConstantUniformLocation(
                sourceRow,
                out int location))
        {
            throw new InvalidOperationException(
                $"{materialIdentity} has no active direct row 0x{sourceRow:X2} uniform.");
        }
        _gl.Uniform4(location, value.X, value.Y, value.Z, value.W);
    }

    private void InitializeGlowMatrices(int width, int height)
    {
        MapRenderOpenGlProgramResource setup = _glowSetupProgram ??
            throw new InvalidOperationException(
                "Glow setup program is unavailable.");
        _gl.UseProgram(setup.Handle);
        BindFullscreenMatrix(setup, width, height);
        foreach (MapRenderOpenGlProgramResource filterProgram in
                 _glowFilterPrograms)
        {
            _gl.UseProgram(filterProgram.Handle);
            BindFullscreenMatrix(filterProgram, width, height);
        }
        _gl.UseProgram(0);
    }

    private void EnsureFullscreenMatrix(
        MapRenderOpenGlProgramResource program,
        int width,
        int height,
        ref int cachedWidth,
        ref int cachedHeight)
    {
        if (cachedWidth == width && cachedHeight == height)
            return;

        _gl.UseProgram(program.Handle);
        BindFullscreenMatrix(program, width, height);
        cachedWidth = width;
        cachedHeight = height;
    }

    private void CreateGeometryResources()
    {
        _resolveVertexArray = _gl.GenVertexArray();
        _resolveVertexBuffer = _gl.GenBuffer();
        _postFxVertexArray = _gl.GenVertexArray();
        _postFxVertexBuffer = _gl.GenBuffer();
        if (_usesGlow)
        {
            _glowVertexArray = _gl.GenVertexArray();
            _glowVertexBuffer = _gl.GenBuffer();
        }
        _indexBuffer = _gl.GenBuffer();
        if (_resolveVertexArray == 0 ||
            _resolveVertexBuffer == 0 ||
            _postFxVertexArray == 0 ||
            _postFxVertexBuffer == 0 ||
            (_usesGlow &&
             (_glowVertexArray == 0 || _glowVertexBuffer == 0)) ||
            _indexBuffer == 0)
        {
            throw new InvalidOperationException(
                "OpenGL did not allocate the fullscreen quad resources.");
        }

        ushort[] indices = [3, 0, 2, 2, 0, 1];
        _gl.BindVertexArray(_resolveVertexArray);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _resolveVertexBuffer);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _indexBuffer);
        fixed (ushort* indexPointer = indices)
        {
            _gl.BufferData(
                BufferTargetARB.ElementArrayBuffer,
                checked((nuint)(indices.Length * sizeof(ushort))),
                indexPointer,
                BufferUsageARB.StaticDraw);
        }
        ConfigureFullscreenVertexAttributes();

        _gl.BindVertexArray(_postFxVertexArray);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _postFxVertexBuffer);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _indexBuffer);
        ConfigureFullscreenVertexAttributes();

        if (_usesGlow)
        {
            _gl.BindVertexArray(_glowVertexArray);
            _gl.BindBuffer(
                BufferTargetARB.ArrayBuffer,
                _glowVertexBuffer);
            _gl.BindBuffer(
                BufferTargetARB.ElementArrayBuffer,
                _indexBuffer);
            ConfigureFullscreenVertexAttributes();
        }
        _gl.BindVertexArray(0);
    }

    private void CreateGlowResources()
    {
        if (!_usesGlow)
            return;

        _glowTarget9Texture = _gl.GenTexture();
        _glowTarget10Texture = _gl.GenTexture();
        _glowTarget11Texture = _gl.GenTexture();
        _glowTarget9Framebuffer = _gl.GenFramebuffer();
        _glowTarget10Framebuffer = _gl.GenFramebuffer();
        _glowTarget11Framebuffer = _gl.GenFramebuffer();
        if (_glowTarget9Texture == 0 ||
            _glowTarget10Texture == 0 ||
            _glowTarget11Texture == 0 ||
            _glowTarget9Framebuffer == 0 ||
            _glowTarget10Framebuffer == 0 ||
            _glowTarget11Framebuffer == 0)
        {
            throw new InvalidOperationException(
                "OpenGL did not allocate the three context-owned native glow target handles.");
        }
    }

    private void ResizeGlowTargets(int width, int height)
    {
        if (_glowTargetWidth == width && _glowTargetHeight == height)
            return;

        BeginDiagnosticStage("native glow quarter-target resize");
        ResizeGlowTarget(
            _glowTarget9Texture,
            _glowTarget9Framebuffer,
            width,
            height,
            targetId: 9);
        ResizeGlowTarget(
            _glowTarget10Texture,
            _glowTarget10Framebuffer,
            width,
            height,
            targetId: 10);
        ResizeGlowTarget(
            _glowTarget11Texture,
            _glowTarget11Framebuffer,
            width,
            height,
            targetId: 11);
        RequireNoError(
            "native glow quarter-target resize",
            MapRenderOpenGlPresentationErrorValidationPoint.Forced);
        _glowTargetWidth = width;
        _glowTargetHeight = height;
    }

    private void ResizeGlowTarget(
        uint texture,
        uint framebuffer,
        int width,
        int height,
        int targetId)
    {
        _gl.BindTexture(TextureTarget.Texture2D, texture);
        _gl.TexImage2D(
            TextureTarget.Texture2D,
            0,
            InternalFormat.Rgba8,
            checked((uint)width),
            checked((uint)height),
            0,
            PixelFormat.Rgba,
            PixelType.UnsignedByte,
            null);
        _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, framebuffer);
        _gl.FramebufferTexture2D(
            FramebufferTarget.DrawFramebuffer,
            FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D,
            texture,
            0);
        _gl.DrawBuffer(DrawBufferMode.ColorAttachment0);
        GLEnum status = _gl.CheckFramebufferStatus(
            FramebufferTarget.DrawFramebuffer);
        if (status != GLEnum.FramebufferComplete)
        {
            throw new InvalidOperationException(
                $"Native glow target {targetId} framebuffer is incomplete: {status}.");
        }
    }

    private void ConfigureFullscreenVertexAttributes()
    {
        const uint stride = 32;
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(
            0,
            4,
            VertexAttribPointerType.Float,
            false,
            stride,
            (void*)0);
        _gl.EnableVertexAttribArray(8);
        _gl.VertexAttribPointer(
            8,
            2,
            VertexAttribPointerType.Float,
            false,
            stride,
            (void*)20);
    }

    private void CreatePostFxSampler()
    {
        _postFxSampler = _gl.GenSampler();
        if (_postFxSampler == 0)
        {
            throw new InvalidOperationException(
                "OpenGL did not allocate the host postfx sampler adapter.");
        }
        _gl.SamplerParameter(
            _postFxSampler,
            GLEnum.TextureMinFilter,
            (int)TextureMinFilter.Linear);
        _gl.SamplerParameter(
            _postFxSampler,
            GLEnum.TextureMagFilter,
            (int)TextureMagFilter.Linear);
        _gl.SamplerParameter(
            _postFxSampler,
            GLEnum.TextureWrapS,
            (int)TextureWrapMode.ClampToEdge);
        _gl.SamplerParameter(
            _postFxSampler,
            GLEnum.TextureWrapT,
            (int)TextureWrapMode.ClampToEdge);
    }

    private void EnsureGeometry(
        uint vertexBuffer,
        int width,
        int height,
        ref int cachedWidth,
        ref int cachedHeight)
    {
        if (vertexBuffer == 0)
            throw new ArgumentOutOfRangeException(nameof(vertexBuffer));
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));
        if (cachedWidth == width && cachedHeight == height)
            return;

        BeginDiagnosticStage("fullscreen quad geometry resize");
        FullscreenVertex[] vertices =
        [
            new(new Vector4(0f, 0f, 0f, 1f), new Vector2(0f, 0f)),
            new(new Vector4(width, 0f, 0f, 1f), new Vector2(1f, 0f)),
            new(new Vector4(width, height, 0f, 1f), new Vector2(1f, 1f)),
            new(new Vector4(0f, height, 0f, 1f), new Vector2(0f, 1f))
        ];
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, vertexBuffer);
        fixed (FullscreenVertex* vertexPointer = vertices)
        {
            _gl.BufferData(
                BufferTargetARB.ArrayBuffer,
                checked((nuint)(vertices.Length * 32)),
                vertexPointer,
                BufferUsageARB.DynamicDraw);
        }
        RequireNoError(
            "fullscreen quad geometry resize",
            MapRenderOpenGlPresentationErrorValidationPoint.Forced);
        // Commit the cache only after the upload is known to be valid. If GL
        // rejects the buffer update, a later presentation attempt must retry
        // instead of treating an unmaterialized extent as current.
        cachedWidth = width;
        cachedHeight = height;
    }

    private void HandOffHostBackBuffer(
        int width,
        int height,
        uint hostFramebuffer)
    {
        _gl.BindVertexArray(0);
        _gl.UseProgram(0);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindSampler(0, 0);
        _gl.BindTexture(TextureTarget.Texture2D, 0);
        _gl.BindTexture(TextureTarget.Texture2DMultisample, 0);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, hostFramebuffer);
        _gl.ReadBuffer(HostReadBuffer(hostFramebuffer));
        _gl.DrawBuffer(HostDrawBuffer(hostFramebuffer));
        _gl.Viewport(0, 0, checked((uint)width), checked((uint)height));
    }

    private static DrawBufferMode HostDrawBuffer(uint hostFramebuffer) =>
        hostFramebuffer == 0
            ? DrawBufferMode.Back
            : DrawBufferMode.ColorAttachment0;

    private static ReadBufferMode HostReadBuffer(uint hostFramebuffer) =>
        hostFramebuffer == 0
            ? ReadBufferMode.Back
            : ReadBufferMode.ColorAttachment0;

    private void DeleteOwnedResources()
    {
        DeleteGlowResources();
        if (_postFxSampler != 0)
        {
            _gl.DeleteSampler(_postFxSampler);
            _postFxSampler = 0;
        }
        if (_indexBuffer != 0)
        {
            _gl.DeleteBuffer(_indexBuffer);
            _indexBuffer = 0;
        }
        if (_postFxVertexBuffer != 0)
        {
            _gl.DeleteBuffer(_postFxVertexBuffer);
            _postFxVertexBuffer = 0;
        }
        if (_resolveVertexBuffer != 0)
        {
            _gl.DeleteBuffer(_resolveVertexBuffer);
            _resolveVertexBuffer = 0;
        }
        if (_glowVertexBuffer != 0)
        {
            _gl.DeleteBuffer(_glowVertexBuffer);
            _glowVertexBuffer = 0;
        }
        if (_postFxVertexArray != 0)
        {
            _gl.DeleteVertexArray(_postFxVertexArray);
            _postFxVertexArray = 0;
        }
        if (_resolveVertexArray != 0)
        {
            _gl.DeleteVertexArray(_resolveVertexArray);
            _resolveVertexArray = 0;
        }
        if (_glowVertexArray != 0)
        {
            _gl.DeleteVertexArray(_glowVertexArray);
            _glowVertexArray = 0;
        }
    }

    private void DeleteGlowResources()
    {
        if (_glowTarget11Framebuffer != 0)
        {
            _gl.DeleteFramebuffer(_glowTarget11Framebuffer);
            _glowTarget11Framebuffer = 0;
        }
        if (_glowTarget10Framebuffer != 0)
        {
            _gl.DeleteFramebuffer(_glowTarget10Framebuffer);
            _glowTarget10Framebuffer = 0;
        }
        if (_glowTarget9Framebuffer != 0)
        {
            _gl.DeleteFramebuffer(_glowTarget9Framebuffer);
            _glowTarget9Framebuffer = 0;
        }
        if (_glowTarget11Texture != 0)
        {
            _gl.DeleteTexture(_glowTarget11Texture);
            _glowTarget11Texture = 0;
        }
        if (_glowTarget10Texture != 0)
        {
            _gl.DeleteTexture(_glowTarget10Texture);
            _glowTarget10Texture = 0;
        }
        if (_glowTarget9Texture != 0)
        {
            _gl.DeleteTexture(_glowTarget9Texture);
            _glowTarget9Texture = 0;
        }
        _glowTargetWidth = 0;
        _glowTargetHeight = 0;
    }

    private void BeginDiagnosticStage(string stage) =>
        _errorMonitor.BeginStage(stage);

    private void RequireNoError(
        string stage,
        MapRenderOpenGlPresentationErrorValidationPoint validationPoint =
            MapRenderOpenGlPresentationErrorValidationPoint.CapturedOnly)
    {
        _errorMonitor.RequireNoErrors(stage, validationPoint);
    }

    private void EnsureUsableOnOwnerThread()
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException(
                "Default normal-camera presenter may only be used and disposed on its owning render thread.");
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private readonly struct FullscreenVertex
    {
        public FullscreenVertex(Vector4 position, Vector2 uv)
        {
            Position = position;
            Color = uint.MaxValue;
            Uv = uv;
            Tail = uint.MaxValue;
        }

        public readonly Vector4 Position;
        public readonly uint Color;
        public readonly Vector2 Uv;
        public readonly uint Tail;
    }
}
