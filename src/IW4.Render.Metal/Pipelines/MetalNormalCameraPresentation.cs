using System.Numerics;
using System.Runtime.Versioning;

using IW4.Assets.Assets.TechniqueSet;
using IW4.Render.EditorPreview;
using IW4.Render.Metal.Targets;
using IW4.Render.SceneBuilding;
using IW4.Render.Scheduling.Lifecycle;
using IW4.Render.Shaders;

using SharpMetal.Metal;

namespace IW4.Render.Metal.Pipelines;

internal readonly record struct
    MetalNormalCameraPresentationExecutionResult(
        bool ExecutedFilmColorManipulation,
        bool ExecutedGlow,
        int GlowGaussianPassCount,
        int FullscreenDrawCount,
        long TelemetryOverlayTriangleCount);

/// <summary>
/// Scene-revision-owned execution of IW4's selected normal-camera post
/// material and optional native glow chain. Hardware resolves target 2 before
/// this executor consumes target 4; all remaining passes preserve the exact
/// authored programs, constants, state words, target order, and blend.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MetalNormalCameraPresentation : IDisposable
{
    internal const MTLPixelFormat HostOutputFormat =
        MTLPixelFormat.BGRA8Unorm;

    private const MTLPixelFormat GlowTargetFormat =
        MTLPixelFormat.RGBA8Unorm;

    private readonly MTLDevice _device;
    private readonly MapRenderEditorPreviewEffectivePostState? _effectivePost;
    private readonly bool _usesFilmColorManipulation;
    private readonly bool _usesGlow;
    private readonly bool _usesGlowSetupColor2;
    private MetalFullscreenProgram? _postFxProgram;
    private MetalFullscreenProgram? _glowSetupProgram;
    private MetalFullscreenProgram? _glowApplyProgram;
    private MetalFullscreenProgram[] _glowFilterPrograms = [];
    private MapRenderEditorPreviewGlowFilterPass[] _glowFilterPasses = [];
    private MTLSamplerState _sampler;
    private MetalFullscreenDraw? _postFxDraw;
    private MetalFullscreenDraw? _glowSetupDraw;
    private MetalFullscreenDraw? _glowApplyDraw;
    private MetalFullscreenDraw[] _glowFilterDraws = [];
    private MTLTexture _glowTarget9;
    private MTLTexture _glowTarget10;
    private MTLTexture _glowTarget11;
    private int _sceneWidth;
    private int _sceneHeight;
    private int _hostWidth;
    private int _hostHeight;
    private int _glowTargetWidth;
    private int _glowTargetHeight;
    private int _glowFilterPassCount;
    private bool _disposed;

    internal MetalNormalCameraPresentation(
        MTLDevice device,
        MapRenderWorldSceneSource source,
        MapRenderEditorPreviewEffectivePostState? effectivePost,
        int sceneWidth,
        int sceneHeight,
        int hostWidth,
        int hostHeight)
    {
        if (device.NativePtr == 0)
            throw new ArgumentException("A Metal device is required.", nameof(device));
        ArgumentNullException.ThrowIfNull(source);
        ValidateEffectivePost(source, effectivePost);

        _device = device;
        _effectivePost = effectivePost;
        _usesFilmColorManipulation =
            effectivePost?.SelectsPostFxColor2 == true;
        _usesGlow = effectivePost?.UsesGlow == true;
        _usesGlowSetupColor2 =
            effectivePost?.UsesGlowSetupColor2 == true;

        try
        {
            CreatePrograms(source);
            _sampler = CreateSampler(device);
            Resize(
                sceneWidth,
                sceneHeight,
                hostWidth,
                hostHeight);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal bool UsesFilmColorManipulation =>
        _usesFilmColorManipulation;

    internal bool UsesGlow => _usesGlow;

    internal void PrepareRenderStates(
        MetalRenderStateCache renderStates)
    {
        ArgumentNullException.ThrowIfNull(renderStates);
        _ = renderStates.GetOrCreate(PostFxProgram.RenderState);
        if (!_usesGlow)
            return;
        _ = renderStates.GetOrCreate(GlowSetupProgram.RenderState);
        _ = renderStates.GetOrCreate(GlowApplyProgram.RenderState);
        foreach (MetalFullscreenProgram program in _glowFilterPrograms)
            _ = renderStates.GetOrCreate(program.RenderState);
    }

    internal void Resize(
        int sceneWidth,
        int sceneHeight,
        int hostWidth,
        int hostHeight)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (sceneWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(sceneWidth));
        if (sceneHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(sceneHeight));
        if (hostWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(hostWidth));
        if (hostHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(hostHeight));
        if (_sceneWidth == sceneWidth &&
            _sceneHeight == sceneHeight &&
            _hostWidth == hostWidth &&
            _hostHeight == hostHeight)
        {
            return;
        }

        MetalFullscreenDraw? postFxDraw = null;
        MetalFullscreenDraw? glowSetupDraw = null;
        MetalFullscreenDraw? glowApplyDraw = null;
        MetalFullscreenDraw[] glowFilterDraws = [];
        MTLTexture glowTarget9 = default;
        MTLTexture glowTarget10 = default;
        MTLTexture glowTarget11 = default;
        int quarterWidth = 0;
        int quarterHeight = 0;
        int filterPassCount = 0;
        try
        {
            postFxDraw = PostFxProgram.CreateDraw(hostWidth, hostHeight);
            InitializeFilmConstants(postFxDraw);
            if (_usesGlow)
            {
                quarterWidth = sceneWidth >> 2;
                quarterHeight = sceneHeight >> 2;
                if (quarterWidth <= 0 || quarterHeight <= 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(sceneWidth),
                        "Native glow targets require a scene extent of at least 4x4.");
                }

                filterPassCount =
                    MapRenderEditorPreviewGlowFilterPlanner.Generate(
                        RequireEffectivePost().Glow.Values.Radius,
                        sceneWidth,
                        sceneHeight,
                        _glowFilterPasses);
                glowTarget9 = CreateGlowTarget(
                    quarterWidth,
                    quarterHeight,
                    targetId: 9);
                glowTarget10 = CreateGlowTarget(
                    quarterWidth,
                    quarterHeight,
                    targetId: 10);
                glowTarget11 = CreateGlowTarget(
                    quarterWidth,
                    quarterHeight,
                    targetId: 11);

                glowSetupDraw = GlowSetupProgram.CreateDraw(
                    quarterWidth,
                    quarterHeight);
                glowApplyDraw = GlowApplyProgram.CreateDraw(
                    hostWidth,
                    hostHeight);
                InitializeGlowConstants(
                    glowSetupDraw,
                    glowApplyDraw);

                glowFilterDraws = new MetalFullscreenDraw[
                    filterPassCount];
                for (int passIndex = 0;
                     passIndex < filterPassCount;
                     passIndex++)
                {
                    ref MapRenderEditorPreviewGlowFilterPass pass =
                        ref _glowFilterPasses[passIndex];
                    if ((uint)(pass.TapHalfCount - 1) >=
                        (uint)_glowFilterPrograms.Length)
                    {
                        throw new InvalidOperationException(
                            $"Native glow produced unsupported symmetric tap count {pass.TapHalfCount}.");
                    }
                    MetalFullscreenDraw draw =
                        _glowFilterPrograms[pass.TapHalfCount - 1]
                            .CreateDraw(
                                quarterWidth,
                                quarterHeight);
                    for (int tapIndex = 0;
                         tapIndex < pass.TapHalfCount;
                         tapIndex++)
                    {
                        Vector4 tap = pass.GetTap(tapIndex);
                        draw.SetVertexConstant(12 + tapIndex, tap);
                        draw.SetCodePixelConstant(
                            checked((ushort)(
                                (int)MaterialConstantSource.FilterTap0 +
                                tapIndex)),
                            tap);
                    }
                    glowFilterDraws[passIndex] = draw;
                }
            }
        }
        catch
        {
            postFxDraw?.Dispose();
            glowSetupDraw?.Dispose();
            glowApplyDraw?.Dispose();
            DisposeDraws(glowFilterDraws);
            Dispose(ref glowTarget11);
            Dispose(ref glowTarget10);
            Dispose(ref glowTarget9);
            throw;
        }

        DeleteDrawsAndTargets();
        _postFxDraw = postFxDraw;
        _glowSetupDraw = glowSetupDraw;
        _glowApplyDraw = glowApplyDraw;
        _glowFilterDraws = glowFilterDraws;
        _glowTarget9 = glowTarget9;
        _glowTarget10 = glowTarget10;
        _glowTarget11 = glowTarget11;
        _sceneWidth = sceneWidth;
        _sceneHeight = sceneHeight;
        _hostWidth = hostWidth;
        _hostHeight = hostHeight;
        _glowTargetWidth = quarterWidth;
        _glowTargetHeight = quarterHeight;
        _glowFilterPassCount = filterPassCount;
    }

    internal MetalNormalCameraPresentationExecutionResult Encode(
        MTLCommandBuffer commandBuffer,
        MTLTexture resolvedSceneColor,
        MTLTexture hostOutput,
        MetalRenderStateCache renderStates,
        MetalMapRenderTelemetryOverlay telemetryOverlay,
        int commandSlot,
        Action<MTLRenderPassDescriptor>? attachPass = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateFrameTargets(
            commandBuffer,
            resolvedSceneColor,
            hostOutput);
        ArgumentNullException.ThrowIfNull(renderStates);
        if (_usesGlow &&
            (!IsGlowTargetReady(_glowTarget9) ||
             !IsGlowTargetReady(_glowTarget10) ||
             !IsGlowTargetReady(_glowTarget11) ||
             _glowFilterDraws.Length != _glowFilterPassCount))
        {
            throw new InvalidOperationException(
                "The native glow target chain does not match this presentation revision.");
        }
        ArgumentNullException.ThrowIfNull(telemetryOverlay);

        long telemetryOverlayTriangleCount = EncodePass(
            commandBuffer,
            hostOutput,
            resolvedSceneColor,
            _postFxDraw ?? throw new InvalidOperationException(
                "The selected postfx draw is unavailable."),
            preserveDestination: false,
            renderStates,
            attachPass,
            telemetryOverlay: _usesGlow ? null : telemetryOverlay,
            commandSlot: commandSlot);

        if (_usesGlow)
        {
            bool hasGaussianPasses = _glowFilterPassCount > 0;
            MTLTexture setupDestination = hasGaussianPasses
                ? _glowTarget9
                : _glowTarget11;
            EncodePass(
                commandBuffer,
                setupDestination,
                resolvedSceneColor,
                _glowSetupDraw ?? throw new InvalidOperationException(
                    "The glow setup draw is unavailable."),
                preserveDestination: false,
                renderStates,
                attachPass);

            MTLTexture input = setupDestination;
            for (int passIndex = 0;
                 passIndex < _glowFilterPassCount;
                 passIndex++)
            {
                bool isFinalPass =
                    passIndex == _glowFilterPassCount - 1;
                MTLTexture output = isFinalPass
                    ? _glowTarget11
                    : (passIndex & 1) == 0
                        ? _glowTarget10
                        : _glowTarget9;
                EncodePass(
                    commandBuffer,
                    output,
                    input,
                    _glowFilterDraws[passIndex],
                    preserveDestination: false,
                    renderStates,
                    attachPass);
                input = output;
            }

            telemetryOverlayTriangleCount = EncodePass(
                commandBuffer,
                hostOutput,
                _glowTarget11,
                _glowApplyDraw ?? throw new InvalidOperationException(
                    "The glow apply draw is unavailable."),
                preserveDestination: true,
                renderStates,
                attachPass,
                telemetryOverlay,
                commandSlot);
        }

        return new MetalNormalCameraPresentationExecutionResult(
            ExecutedFilmColorManipulation:
                _usesFilmColorManipulation,
            ExecutedGlow: _usesGlow,
            GlowGaussianPassCount: _glowFilterPassCount,
            FullscreenDrawCount: 1 +
                (_usesGlow ? 2 + _glowFilterPassCount : 0),
            TelemetryOverlayTriangleCount:
                telemetryOverlayTriangleCount);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        DeleteDrawsAndTargets();
        Dispose(ref _sampler);
        foreach (MetalFullscreenProgram? program in _glowFilterPrograms)
            program?.Dispose();
        _glowFilterPrograms = [];
        _glowApplyProgram?.Dispose();
        _glowApplyProgram = null;
        _glowSetupProgram?.Dispose();
        _glowSetupProgram = null;
        _postFxProgram?.Dispose();
        _postFxProgram = null;
    }

    private void CreatePrograms(MapRenderWorldSceneSource source)
    {
        MapRenderEditorPreviewNormalCameraRecipe recipe =
            MapRenderEditorPreviewNormalCameraRecipe.Current;
        MapRenderNormalCameraMaterialAssetContract activePostFx =
            _usesFilmColorManipulation
                ? recipe.PostFxColor2
                : recipe.PostFx;
        _postFxProgram = ResolveProgram(
            _device,
            source,
            activePostFx,
            _usesFilmColorManipulation
                ?
                [
                    (ushort)MaterialConstantSource.ColorTintBase,
                    (ushort)MaterialConstantSource.ColorTintDelta,
                    (ushort)MaterialConstantSource.ColorTintQuadraticDelta,
                    (ushort)MaterialConstantSource.ColorBias
                ]
                : [],
            [0, 1, 2, 3],
            HostOutputFormat);
        if (!_usesGlow)
            return;

        MapRenderNormalCameraMaterialAssetContract setup =
            _usesGlowSetupColor2
                ? recipe.GlowConsistentSetupColor2
                : recipe.GlowConsistentSetup;
        _glowSetupProgram = ResolveProgram(
            _device,
            source,
            setup,
            _usesGlowSetupColor2
                ?
                [
                    (ushort)MaterialConstantSource.GlowSetup,
                    (ushort)MaterialConstantSource.ColorTintBase,
                    (ushort)MaterialConstantSource.ColorTintDelta,
                    (ushort)MaterialConstantSource.ColorTintQuadraticDelta,
                    (ushort)MaterialConstantSource.ColorBias
                ]
                :
                [
                    (ushort)MaterialConstantSource.GlowSetup,
                    (ushort)MaterialConstantSource.ColorTintBase,
                    (ushort)MaterialConstantSource.ColorTintDelta,
                    (ushort)MaterialConstantSource.ColorBias
                ],
            [0, 1, 2, 3, 16, 467],
            GlowTargetFormat);
        _glowApplyProgram = ResolveProgram(
            _device,
            source,
            recipe.GlowApplyBloom,
            [(ushort)MaterialConstantSource.GlowApply],
            [0, 1, 2, 3],
            HostOutputFormat);

        _glowFilterPrograms = new MetalFullscreenProgram[
            MapRenderEditorPreviewGlowFilterPlanner.MaximumTapHalfCount];
        for (int index = 0;
             index < _glowFilterPrograms.Length;
             index++)
        {
            int tapHalfCount = index + 1;
            _glowFilterPrograms[index] = ResolveProgram(
                _device,
                source,
                recipe.GlowSymmetricFilters[index],
                Enumerable.Range(
                        (int)MaterialConstantSource.FilterTap0,
                        tapHalfCount)
                    .Select(value => checked((ushort)value))
                    .ToArray(),
                Enumerable.Range(12, tapHalfCount)
                    .Prepend(0)
                    .Prepend(3)
                    .Prepend(2)
                    .Prepend(1)
                    .Order()
                    .ToArray(),
                GlowTargetFormat);
        }
        _glowFilterPasses = new MapRenderEditorPreviewGlowFilterPass[
            MapRenderEditorPreviewGlowFilterPlanner
                .MaximumGaussianPassCount];
    }

    private void InitializeFilmConstants(MetalFullscreenDraw draw)
    {
        if (!_usesFilmColorManipulation)
            return;
        MapRenderEditorPreviewEffectivePostState post =
            RequireEffectivePost();
        IReadOnlyList<MapRenderEditorPreviewFilmCodeConstantRow> rows =
            MapRenderEditorPreviewFilmCodeConstantProducer.Produce(
                post.Film.Values,
                post.Film.Mixer);
        foreach (MapRenderEditorPreviewFilmCodeConstantRow row in rows)
            draw.SetCodePixelConstant(row.SourceRowIndex, row.Value);
    }

    private void InitializeGlowConstants(
        MetalFullscreenDraw setup,
        MetalFullscreenDraw apply)
    {
        MapRenderEditorPreviewEffectivePostState post =
            RequireEffectivePost();
        setup.SetVertexConstant(16, Vector4.Zero);
        setup.SetVertexConstant(467, new Vector4(0.5f, 0f, 0f, 0f));

        IReadOnlyList<MapRenderEditorPreviewGlowCodeConstantRow> glowRows =
            MapRenderEditorPreviewGlowCodeConstantProducer.Produce(
                post.Glow.Values);
        setup.SetCodePixelConstant(
            glowRows[0].SourceRowIndex,
            glowRows[0].Value);
        apply.SetCodePixelConstant(
            glowRows[1].SourceRowIndex,
            glowRows[1].Value);

        IReadOnlyList<MapRenderEditorPreviewFilmCodeConstantRow> filmRows =
            MapRenderEditorPreviewFilmCodeConstantProducer.Produce(
                post.Film.Values,
                post.Film.Mixer);
        foreach (MapRenderEditorPreviewFilmCodeConstantRow row in filmRows)
        {
            if (GlowSetupProgram.UsesCodePixelConstant(
                    row.SourceRowIndex))
            {
                setup.SetCodePixelConstant(
                    row.SourceRowIndex,
                    row.Value);
            }
            else if (row.SourceRowIndex !=
                     MapRenderEditorPreviewFilmCodeConstantProducer
                         .ColorTintQuadraticRowIndex)
            {
                throw new InvalidOperationException(
                    $"Glow setup has no active direct row 0x{row.SourceRowIndex:X2} constant.");
            }
        }
    }

    private long EncodePass(
        MTLCommandBuffer commandBuffer,
        MTLTexture target,
        MTLTexture source,
        MetalFullscreenDraw draw,
        bool preserveDestination,
        MetalRenderStateCache renderStates,
        Action<MTLRenderPassDescriptor>? attachPass,
        MetalMapRenderTelemetryOverlay? telemetryOverlay = null,
        int commandSlot = -1)
    {
        using MTLRenderPassDescriptor pass = CreateColorPass(
            target,
            preserveDestination);
        attachPass?.Invoke(pass);
        MTLRenderCommandEncoder encoder =
            commandBuffer.RenderCommandEncoder(pass);
        if (encoder.NativePtr == 0)
        {
            throw new InvalidOperationException(
                $"Metal could not begin the {draw.MaterialName} pass.");
        }
        try
        {
            SetViewport(encoder, target.Width, target.Height);
            renderStates.ResetEncoderInheritance();
            draw.Encode(
                encoder,
                source,
                _sampler,
                renderStates);
            return telemetryOverlay?.EncodeInto(
                encoder,
                target,
                commandSlot) ?? 0;
        }
        finally
        {
            encoder.EndEncoding();
        }
    }

    private void ValidateFrameTargets(
        MTLCommandBuffer commandBuffer,
        MTLTexture resolvedSceneColor,
        MTLTexture hostOutput)
    {
        if (commandBuffer.NativePtr == 0)
        {
            throw new ArgumentException(
                "A Metal command buffer is required.",
                nameof(commandBuffer));
        }
        if (resolvedSceneColor.NativePtr == 0 ||
            resolvedSceneColor.PixelFormat !=
                MetalFrameTargets.SceneColorFormat ||
            resolvedSceneColor.Width != checked((ulong)_sceneWidth) ||
            resolvedSceneColor.Height != checked((ulong)_sceneHeight))
        {
            throw new ArgumentException(
                "The resolved target-4 texture does not match this presentation revision.",
                nameof(resolvedSceneColor));
        }
        if (hostOutput.NativePtr == 0 ||
            hostOutput.PixelFormat != HostOutputFormat ||
            hostOutput.Width != checked((ulong)_hostWidth) ||
            hostOutput.Height != checked((ulong)_hostHeight))
        {
            throw new ArgumentException(
                "The retained host target does not match this presentation revision.",
                nameof(hostOutput));
        }
    }

    private MTLTexture CreateGlowTarget(
        int width,
        int height,
        int targetId)
    {
        using var descriptor = new MTLTextureDescriptor
        {
            TextureType = MTLTextureType.Type2D,
            PixelFormat = GlowTargetFormat,
            Width = checked((ulong)width),
            Height = checked((ulong)height),
            Depth = 1,
            ArrayLength = 1,
            MipmapLevelCount = 1,
            SampleCount = 1,
            StorageMode = MTLStorageMode.Private,
            Usage = MTLTextureUsage.RenderTarget |
                MTLTextureUsage.ShaderRead
        };
        MTLTexture texture = _device.NewTexture(descriptor);
        if (texture.NativePtr == 0)
        {
            throw new InvalidOperationException(
                $"Metal failed to allocate glow target {targetId} at {width}x{height}.");
        }
        return texture;
    }

    private bool IsGlowTargetReady(MTLTexture texture) =>
        texture.NativePtr != 0 &&
        texture.PixelFormat == GlowTargetFormat &&
        texture.Width == checked((ulong)_glowTargetWidth) &&
        texture.Height == checked((ulong)_glowTargetHeight);

    private static MTLRenderPassDescriptor CreateColorPass(
        MTLTexture target,
        bool preserveDestination)
    {
        var descriptor = new MTLRenderPassDescriptor
        {
            RenderTargetWidth = target.Width,
            RenderTargetHeight = target.Height,
            DefaultRasterSampleCount = 1
        };
        MTLRenderPassColorAttachmentDescriptor color =
            descriptor.ColorAttachments.Object(0);
        color.Texture = target;
        color.LoadAction = preserveDestination
            ? MTLLoadAction.Load
            : MTLLoadAction.DontCare;
        color.StoreAction = MTLStoreAction.Store;
        return descriptor;
    }

    private static void SetViewport(
        MTLRenderCommandEncoder encoder,
        ulong width,
        ulong height)
    {
        encoder.SetViewport(new MTLViewport
        {
            originX = 0,
            originY = 0,
            width = width,
            height = height,
            znear = 0,
            zfar = 1
        });
        encoder.SetScissorRect(new MTLScissorRect
        {
            x = 0,
            y = 0,
            width = width,
            height = height
        });
    }

    private void DeleteDrawsAndTargets()
    {
        _postFxDraw?.Dispose();
        _postFxDraw = null;
        _glowSetupDraw?.Dispose();
        _glowSetupDraw = null;
        _glowApplyDraw?.Dispose();
        _glowApplyDraw = null;
        DisposeDraws(_glowFilterDraws);
        _glowFilterDraws = [];
        Dispose(ref _glowTarget11);
        Dispose(ref _glowTarget10);
        Dispose(ref _glowTarget9);
        _sceneWidth = 0;
        _sceneHeight = 0;
        _hostWidth = 0;
        _hostHeight = 0;
        _glowTargetWidth = 0;
        _glowTargetHeight = 0;
        _glowFilterPassCount = 0;
    }

    private static void DisposeDraws(
        IEnumerable<MetalFullscreenDraw> draws)
    {
        foreach (MetalFullscreenDraw? draw in draws)
            draw?.Dispose();
    }

    private static void ValidateEffectivePost(
        MapRenderWorldSceneSource source,
        MapRenderEditorPreviewEffectivePostState? effectivePost)
    {
        if (effectivePost is not { } post)
            return;
        if (post.SourceSnapshot is not { } snapshot ||
            post.Revision.AssetPoolRevision !=
                source.AssetPoolRevisionAtConstruction ||
            post.Revision.RuntimeRevision != snapshot.Revision)
        {
            throw new ArgumentException(
                "Fullscreen effective post state must belong to the canonical scene asset revision and its exact atomic runtime snapshot.",
                nameof(effectivePost));
        }
    }

    private static MetalFullscreenProgram ResolveProgram(
        MTLDevice device,
        MapRenderWorldSceneSource source,
        MapRenderNormalCameraMaterialAssetContract contract,
        IReadOnlyList<ushort> expectedCodePixelSourceRows,
        IReadOnlyList<int> expectedVertexConstantDestinations,
        MTLPixelFormat targetFormat)
    {
        MapRenderNormalCameraMaterialProgramResolution resolution =
            MapRenderNormalCameraMaterialProgramResolver.ResolveExact(
                source.AssetLookup,
                source.AssetPoolRevisionAtConstruction,
                contract,
                expectedVertexInputDestinations: [0, 8],
                expectedCodePixelSourceRows:
                    expectedCodePixelSourceRows,
                expectedVertexConstantDestinations:
                    expectedVertexConstantDestinations);
        return new MetalFullscreenProgram(
            device,
            contract.MaterialName,
            resolution,
            expectedCodePixelSourceRows,
            targetFormat);
    }

    private static MTLSamplerState CreateSampler(MTLDevice device)
    {
        using var descriptor = new MTLSamplerDescriptor
        {
            MinFilter = MTLSamplerMinMagFilter.Linear,
            MagFilter = MTLSamplerMinMagFilter.Linear,
            MipFilter = MTLSamplerMipFilter.NotMipmapped,
            SAddressMode = MTLSamplerAddressMode.ClampToEdge,
            TAddressMode = MTLSamplerAddressMode.ClampToEdge,
            RAddressMode = MTLSamplerAddressMode.ClampToEdge,
            NormalizedCoordinates = true,
            MaxAnisotropy = 1,
            LodMinClamp = 0f,
            LodMaxClamp = 0f
        };
        MTLSamplerState sampler = device.NewSamplerState(descriptor);
        if (sampler.NativePtr == 0)
        {
            throw new InvalidOperationException(
                "Metal failed to create the normal-camera post sampler.");
        }
        return sampler;
    }

    private MetalFullscreenProgram PostFxProgram =>
        _postFxProgram ?? throw new InvalidOperationException(
            "The selected postfx program is unavailable.");

    private MetalFullscreenProgram GlowSetupProgram =>
        _glowSetupProgram ?? throw new InvalidOperationException(
            "The glow setup program is unavailable.");

    private MetalFullscreenProgram GlowApplyProgram =>
        _glowApplyProgram ?? throw new InvalidOperationException(
            "The glow apply program is unavailable.");

    private MapRenderEditorPreviewEffectivePostState RequireEffectivePost() =>
        _effectivePost ?? throw new InvalidOperationException(
            "Renderer-effective post state is unavailable.");

    private static void Dispose(ref MTLTexture texture)
    {
        if (texture.NativePtr == 0)
            return;
        texture.Dispose();
        texture = default;
    }

    private static void Dispose(ref MTLSamplerState sampler)
    {
        if (sampler.NativePtr == 0)
            return;
        sampler.Dispose();
        sampler = default;
    }
}
