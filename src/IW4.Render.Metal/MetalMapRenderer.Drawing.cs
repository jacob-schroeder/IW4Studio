using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.Versioning;

using IW4.Assets.Assets.TechniqueSet;
using IW4.Render.Diagnostics;
using IW4.Render.EditorPreview;
using IW4.Render.Execution;
using IW4.Render.Execution.Fog;
using IW4.Render.Geometry;
using IW4.Render.Lighting;
using IW4.Render.Metal.Pipelines;
using IW4.Render.Metal.Resources;
using IW4.Render.Metal.Shaders;
using IW4.Render.Resources;
using IW4.Render.Scheduling;
using IW4.Render.Scheduling.FramePlans;
using IW4.Render.Scheduling.Lighting;
using IW4.Render.Scheduling.Shadows;
using IW4.Render.SceneBuilding;
using IW4.Render.Shaders;
using IW4.Render.Techniques;
using IW4.Render.Textures;
using IW4.Render.Transforms;

using SharpMetal.Foundation;
using SharpMetal.Metal;

namespace IW4.Render.Metal;

[SupportedOSPlatform("macos")]
public sealed unsafe partial class MetalMapRenderer
{
    private const int FrameBufferCount = 3;
    private const int ConstantBufferAlignment = 256;
    private const int VectorByteCount = sizeof(float) * 4;
    private const int VertexConstantByteCount =
        RsxVertexConstantLayout.Count * VectorByteCount;
    private const int CodePixelConstantByteCount =
        CodeConstantLayout.Float4Count * VectorByteCount;
    private const int FrameVertexConstantByteCount =
        MetalRsxShaderAbi.FrameVertexFloat4Count * VectorByteCount;

    private static readonly CodeMatrixSemantic[] FrameMatrixSemantics =
    [
        CodeMatrixSemantic.View,
        CodeMatrixSemantic.Projection,
        CodeMatrixSemantic.ViewProjection,
        CodeMatrixSemantic.World0,
        CodeMatrixSemantic.WorldView0,
        CodeMatrixSemantic.WorldViewProjection0
    ];
    private static readonly CodeMatrixTransform[] FrameMatrixTransforms =
    [
        CodeMatrixTransform.None,
        CodeMatrixTransform.Inverse,
        CodeMatrixTransform.Transpose,
        CodeMatrixTransform.InverseTranspose
    ];

    private readonly Dictionary<
        RenderNormalCameraPreparedPassSnapshot,
        MetalPreparedNormalCameraPass> _normalCameraPasses = new(
            ReferenceEqualityComparer.Instance);
    private readonly HashSet<MapRenderEditorDrawGroup<
        RenderNormalCameraDrawSubmissionSnapshot>>
        _normalCameraAuthorizedGroups = new(
            ReferenceEqualityComparer.Instance);
    private readonly MTLBuffer[] _normalCameraFrameBuffers =
        new MTLBuffer[FrameBufferCount];
    private MetalGenericMaterialPipelineCache? _normalCameraGenericPipelines;
    private MetalProgramPipelineCache? _normalCameraPipelines;
    private MetalPreparedNormalCameraPass[] _normalCameraPreparedPasses = [];
    private MTLBuffer _normalCameraImmutableBuffer;
    private int _normalCameraFrameBufferByteCount;
    private int _normalCameraFrameConstantsOffset;
    private long _normalCameraAnimationStartTimestamp;
    private long _normalCameraPreparedFrameIndex = -1;
    private long _normalCameraFrameStateRevision = -1;
    private MetalNormalCameraFrameState _normalCameraFrameState;
    private MapRenderWorldEvent20SceneLightFrameInput?
        _normalCameraSceneLightFrame;
    private MapRenderWorldSceneSource? _normalCameraWorldSource;
    private MapRenderActiveFogState? _normalCameraActiveFog;
    private MapRenderActiveFogState? _normalCameraGenericActiveFog;
    private MetalResourceCache? _normalCameraLightAttenuationResources;
    private MetalNormalCameraLightAttenuationBinding?[]
        _normalCameraLightAttenuationBindings = [];
    private MetalNormalCameraAdmissionTelemetry
        _normalCameraAdmissionTelemetry;

    partial void CreateNormalCameraResources(
        MapRenderScene scene,
        RenderSceneSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(snapshot);
        using var pool = new NSAutoreleasePool();

        _normalCameraPipelines = new MetalProgramPipelineCache(
            _surface.Device,
            _depthStencilFormat);
        _normalCameraGenericPipelines =
            new MetalGenericMaterialPipelineCache(
                _surface.Device,
                _depthStencilFormat);
        _normalCameraWorldSource = scene.WorldSource;
        _normalCameraSceneLightFrame = CreateSceneLightFrame(scene);
        CreateNormalCameraLightAttenuationResources(scene);
        MapRenderEditorPreviewLightingPlan previewLighting =
            scene.EditorPreviewLighting ??
            MapRenderEditorPreviewLightingPlanner.Create(comWorld: null);
        MapRenderEditorPreviewAtmospherePlan previewAtmosphere =
            scene.EditorPreviewAtmosphere ??
            MapRenderEditorPreviewAtmospherePlanner.Create(scene.Bounds);
        _normalCameraActiveFog = scene.EditorPreviewActiveFog ??
            (previewAtmosphere.IsEnabled
                ? MapRenderEditorPreviewActiveFogAdapter.Create(
                    previewAtmosphere,
                    previewLighting)
                : null);
        _normalCameraGenericActiveFog = scene.EditorPreviewActiveFog;
        _normalCameraAnimationStartTimestamp = Stopwatch.GetTimestamp();
        _normalCameraPreparedFrameIndex = -1;
        _normalCameraFrameStateRevision = -1;

        var prepared = new List<MetalPreparedNormalCameraPass>(
            snapshot.NormalCameraDraws.PreparedPasses.Length);
        var failedPasses = new Dictionary<
            RenderNormalCameraPreparedPassSnapshot,
            string>(ReferenceEqualityComparer.Instance);
        var authorizedPasses = new HashSet<
            RenderNormalCameraPreparedPassSnapshot>(
                ReferenceEqualityComparer.Instance);
        int snapshotBaseGroups = 0;
        int snapshotReceiverGroups = 0;
        int authorizedBaseGroups = 0;
        int authorizedReceiverGroups = 0;
        int blockedGroups = 0;
        foreach (MapRenderEditorDrawGroup<
                     RenderNormalCameraDrawSubmissionSnapshot> group in
                 snapshot.NormalCameraDraws.DrawGroups)
        {
            ReadOnlySpan<RenderNormalCameraDrawSubmissionSnapshot>
                authoredPasses = group.AuthoredPassSpan;
            bool receiverGroup = IsReceiverPass(
                authoredPasses[0].PreparedPass);
            if (receiverGroup)
                snapshotReceiverGroups++;
            else
                snapshotBaseGroups++;
            bool groupReady = true;
            for (int passIndex = 0;
                 passIndex < authoredPasses.Length;
                 passIndex++)
            {
                RenderNormalCameraPreparedPassSnapshot source =
                    authoredPasses[passIndex].PreparedPass;
                if (IsReceiverPass(source) != receiverGroup)
                {
                    throw new InvalidDataException(
                        "A normal-camera authored group mixed base and receiver passes.");
                }
                if (_normalCameraPasses.TryGetValue(
                        source,
                        out _))
                {
                    continue;
                }
                if (failedPasses.ContainsKey(source))
                {
                    groupReady = false;
                    continue;
                }
                if (!TryPrepareNormalCameraPass(
                        scene,
                        source,
                        out MetalPreparedNormalCameraPass? runtime,
                        out string blocker))
                {
                    failedPasses.Add(source, blocker);
                    groupReady = false;
                    continue;
                }
                _normalCameraPasses.Add(source, runtime!);
                prepared.Add(runtime!);
            }
            if (!groupReady)
            {
                blockedGroups++;
                continue;
            }

            _normalCameraAuthorizedGroups.Add(group);
            if (receiverGroup)
                authorizedReceiverGroups++;
            else
                authorizedBaseGroups++;
            for (int passIndex = 0;
                 passIndex < authoredPasses.Length;
                 passIndex++)
            {
                authorizedPasses.Add(
                    authoredPasses[passIndex].PreparedPass);
            }
        }

        foreach (RenderNormalCameraPreparedPassSnapshot pass in
                 _normalCameraPasses.Keys
                     .Where(pass => !authorizedPasses.Contains(pass))
                     .ToArray())
        {
            _normalCameraPasses.Remove(pass);
        }
        prepared.RemoveAll(runtime =>
            !authorizedPasses.Contains(runtime.Source));

        _normalCameraAdmissionTelemetry =
            CreateNormalCameraAdmissionTelemetry(
                snapshot.NormalCameraDraws.PreparedPasses,
                authorizedPasses,
                failedPasses,
                snapshotBaseGroups,
                snapshotReceiverGroups,
                authorizedBaseGroups,
                authorizedReceiverGroups,
                blockedGroups);

        _normalCameraPreparedPasses = prepared.ToArray();
        _hasNormalCameraGenericMaterials =
            _normalCameraPreparedPasses.Any(pass =>
                pass.GenericMaterial is not null);
        NormalizeInactiveGenericMaterialBindings(
            _normalCameraPreparedPasses);
        AssignFrameConstantOffsets(_normalCameraPreparedPasses);
        CreateImmutableDrawBuffer(_normalCameraPreparedPasses);
        CreateFrameConstantBuffers(_normalCameraPreparedPasses);
    }

    partial void DeleteNormalCameraResources()
    {
        DeleteNormalCameraFloatZResources();
        for (int index = 0;
             index < _normalCameraFrameBuffers.Length;
             index++)
        {
            if (_normalCameraFrameBuffers[index].NativePtr == 0)
                continue;
            _normalCameraFrameBuffers[index].Dispose();
            _normalCameraFrameBuffers[index] = default;
        }
        if (_normalCameraImmutableBuffer.NativePtr != 0)
        {
            _normalCameraImmutableBuffer.Dispose();
            _normalCameraImmutableBuffer = default;
        }
        _normalCameraPipelines?.Dispose();
        _normalCameraPipelines = null;
        _normalCameraGenericPipelines?.Dispose();
        _normalCameraGenericPipelines = null;
        _normalCameraPasses.Clear();
        _normalCameraAuthorizedGroups.Clear();
        _normalCameraPreparedPasses = [];
        _normalCameraFrameBufferByteCount = 0;
        _normalCameraFrameConstantsOffset = 0;
        _normalCameraAnimationStartTimestamp = 0;
        _normalCameraPreparedFrameIndex = -1;
        _normalCameraFrameStateRevision = -1;
        _normalCameraFrameState = default;
        _normalCameraSceneLightFrame = null;
        _normalCameraWorldSource = null;
        _normalCameraActiveFog = null;
        _normalCameraGenericActiveFog = null;
        _normalCameraLightAttenuationResources?.Dispose();
        _normalCameraLightAttenuationResources = null;
        _normalCameraLightAttenuationBindings = [];
        _normalCameraAdmissionTelemetry = default;
        _hasNormalCameraGenericMaterials = false;
        _normalCameraGenericFrameState = default;
    }

    private void CreateNormalCameraLightAttenuationResources(
        MapRenderScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        _normalCameraLightAttenuationResources?.Dispose();
        _normalCameraLightAttenuationResources = null;
        _normalCameraLightAttenuationBindings = [];

        IReadOnlyList<Texture?> sources =
            scene.SceneLightAttenuationTextures;
        if (_normalCameraSceneLightFrame is not { } lightFrame ||
            _normalCameraWorldSource is not { } worldSource ||
            !worldSource.AssetLookup.HasCanonicalAssetPoolRevision(
                lightFrame.AssetPoolRevision) ||
            sources.Count != lightFrame.SceneLightCount)
        {
            return;
        }

        var pending = new MetalNormalCameraLightAttenuationIdentity?[
            sources.Count];
        var textures = new List<RenderTextureDescriptor>();
        var samplers = new List<RenderSamplerDescriptor>();
        var bySource = new Dictionary<
            Texture,
            MetalNormalCameraLightAttenuationIdentity>(
                ReferenceEqualityComparer.Instance);
        for (int sceneLightIndex = 0;
             sceneLightIndex < sources.Count;
             sceneLightIndex++)
        {
            Texture? source = sources[sceneLightIndex];
            if (source is null ||
                lightFrame.GetSceneLight(sceneLightIndex).Type is not (
                    IW4.Assets.Assets.ComWorld.GfxLightType.Spot or
                    IW4.Assets.Assets.ComWorld.GfxLightType.Omni))
            {
                continue;
            }

            if (!bySource.TryGetValue(
                    source,
                    out MetalNormalCameraLightAttenuationIdentity identity))
            {
                string ordinal = bySource.Count.ToString(
                    "D8",
                    System.Globalization.CultureInfo.InvariantCulture);
                identity = new(
                    new RenderSemanticIdentity(
                        RenderSemanticResourceKind.Texture,
                        "metal.normal-camera.light-attenuation.texture." +
                        ordinal),
                    new RenderSemanticIdentity(
                        RenderSemanticResourceKind.Sampler,
                        "metal.normal-camera.light-attenuation.sampler." +
                        ordinal));
                try
                {
                    RenderTextureDescriptor texture =
                        RenderSceneSnapshotBuilder.CreateTextureDescriptor(
                            source,
                            identity.Texture,
                            preferProvenAuthoredPayload: true);
                    _ = MetalTextureUploadPlan.Create(
                        texture,
                        _surface.Device.SupportsBCTextureCompression);
                    var sampler = new RenderSamplerDescriptor(
                        identity.Sampler,
                        source.DecodedSamplerState);
                    textures.Add(texture);
                    samplers.Add(sampler);
                    bySource.Add(source, identity);
                }
                catch (Exception exception) when (
                    exception is ArgumentException or
                    InvalidDataException or
                    InvalidOperationException or
                    NotSupportedException or
                    OverflowException)
                {
                    // Source 13 has no substitute. Only this light remains
                    // unavailable; exact program admission will keep its
                    // receiver group on the base route.
                    continue;
                }
            }
            pending[sceneLightIndex] = identity;
        }

        if (textures.Count == 0)
            return;

        var cache = new MetalResourceCache(
            _surface.Device,
            _surface.CommandQueue);
        try
        {
            cache.Load(new RenderResourceSnapshot(
                Array.Empty<RenderVertexLayoutDescriptor>(),
                Array.Empty<RenderGeometryDescriptor>(),
                textures,
                samplers));
            var bindings = new
                MetalNormalCameraLightAttenuationBinding?[pending.Length];
            for (int sceneLightIndex = 0;
                 sceneLightIndex < pending.Length;
                 sceneLightIndex++)
            {
                if (pending[sceneLightIndex] is not { } identity)
                    continue;
                MetalTextureResource texture =
                    cache.RequireTexture(identity.Texture);
                MetalSamplerResource sampler =
                    cache.RequireSampler(identity.Sampler);
                bindings[sceneLightIndex] = new(
                    texture.ResolveSampledTexture(sampler.UsesSrgbReads),
                    sampler.State);
            }
            _normalCameraLightAttenuationResources = cache;
            _normalCameraLightAttenuationBindings = bindings;
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            InvalidDataException or
            InvalidOperationException or
            NotSupportedException or
            OverflowException or
            AggregateException)
        {
            cache.Dispose();
        }
    }

    private bool TryGetNormalCameraLightAttenuationBinding(
        int sceneLightIndex,
        out MetalNormalCameraLightAttenuationBinding binding)
    {
        if (_normalCameraSceneLightFrame is { } lightFrame &&
            _normalCameraWorldSource is { } worldSource &&
            worldSource.AssetLookup.HasCanonicalAssetPoolRevision(
                lightFrame.AssetPoolRevision) &&
            (uint)sceneLightIndex < (uint)lightFrame.SceneLightCount &&
            (uint)sceneLightIndex <
                (uint)_normalCameraLightAttenuationBindings.Length &&
            lightFrame.GetSceneLight(sceneLightIndex).Type is
                IW4.Assets.Assets.ComWorld.GfxLightType.Spot or
                IW4.Assets.Assets.ComWorld.GfxLightType.Omni &&
            _normalCameraLightAttenuationBindings[sceneLightIndex] is
                { } resolved &&
            resolved.Texture.NativePtr != 0 &&
            resolved.Sampler.NativePtr != 0)
        {
            binding = resolved;
            return true;
        }

        binding = default;
        return false;
    }

    partial void EncodeNormalCameraDraws(
        MTLRenderCommandEncoder encoder,
        RenderCamera camera)
    {
        if (_normalCameraPreparedPasses.Length == 0 ||
            (_normalCameraPreparedPasses.Any(pass =>
                 pass.RequiresImmutableBuffer) &&
             _normalCameraImmutableBuffer.NativePtr == 0) ||
            _drawOrder is null)
        {
            return;
        }
        if (encoder.NativePtr == 0)
            throw new ArgumentException("A Metal render encoder is required.", nameof(encoder));

        MTLBuffer frameBuffer;
        MetalNormalCameraFrameState frameState;
        MetalSunShadowReceiverFrame? sunShadowReceiverFrame;
        IReadOnlyList<MapRenderEditorDrawGroup<
            RenderNormalCameraDrawSubmissionSnapshot>> orderedGroups;
        using (_telemetry.BeginCpuPhase(MapRenderCpuPhase.QueueBuild))
        {
            frameState = PrepareNormalCameraFrameState(camera);
            frameBuffer = frameState.ConstantBuffer;
            sunShadowReceiverFrame =
                TryGetCurrentSunShadowReceiverFrame(
                    out MetalSunShadowReceiverFrame? currentReceiver)
                    ? currentReceiver
                    : null;
            orderedGroups = _drawOrder.Order(
                camera.Position,
                camera.Forward);
        }
        nint currentPipeline = 0;
        RenderState currentState = MetalRenderStateCache.Effective(
            RenderState.Default);
        long worldVisibleCount = 0;
        long worldVisibleRunCount = 0;
        long worldTriangles = 0;
        long issuedDrawCalls = 0;
        MapRenderCpuPhaseScope cpuTimingScope = default;
        MapRenderCpuPhase? activeCpuPhase = null;

        // Establish the native initial polygon-offset value so an authored
        // first-row Inherit action observes the same disabled baseline as the
        // PS3 normal-camera command stream.
        _renderStates.ApplyRasterState(encoder, RenderState.Default);
        _telemetry.AddCounter(MapRenderFrameCounter.RenderStateChanges);
        Span<MetalEncoderBufferBinding> vertexBufferBindings =
            stackalloc MetalEncoderBufferBinding[
                MetalNormalCameraEncoderBindingShadow.VertexBufferSlotCount];
        Span<MetalEncoderBufferBinding> fragmentBufferBindings =
            stackalloc MetalEncoderBufferBinding[
                MetalNormalCameraEncoderBindingShadow.FragmentBufferSlotCount];
        Span<nint> fragmentTextureBindings =
            stackalloc nint[
                MetalNormalCameraEncoderBindingShadow.FragmentTextureSlotCount];
        Span<nint> fragmentSamplerBindings =
            stackalloc nint[
                MetalNormalCameraEncoderBindingShadow.FragmentTextureSlotCount];
        var bindingShadow = new MetalNormalCameraEncoderBindingShadow(
            encoder,
            _telemetry,
            vertexBufferBindings,
            fragmentBufferBindings,
            fragmentTextureBindings,
            fragmentSamplerBindings);
        try
        {
            for (int groupIndex = 0;
                 groupIndex < orderedGroups.Count;
                 groupIndex++)
            {
                MapRenderEditorDrawGroup<
                    RenderNormalCameraDrawSubmissionSnapshot> group =
                        orderedGroups[groupIndex];
                if (!_normalCameraAuthorizedGroups.Contains(group))
                {
                    continue;
                }
                if (!IsNormalCameraGroupSelected(group))
                    continue;
                int visibleRunCount =
                    PrepareNormalCameraVisibleRuns(
                        group,
                        out MetalNormalCameraVisibilityGroupPlan?
                            visibilityPlan,
                        out int visibleInstanceCount);
                if (visibleRunCount == 0)
                    continue;

                ReadOnlySpan<RenderNormalCameraDrawSubmissionSnapshot>
                    authoredPasses = group.AuthoredPassSpan;
                // Publish every authored row before issuing the first row so
                // an unexpected dynamic-constant failure cannot partially
                // execute an otherwise atomic multipass group. Each runtime
                // writes at most once per render attempt and monotonic frame
                // index, which is safe when the three constant-buffer slots
                // rotate back into use and correct when drawable acquisition
                // abandons an attempt without advancing the frame index.
                for (int passIndex = 0;
                     passIndex < authoredPasses.Length;
                     passIndex++)
                {
                    MetalPreparedNormalCameraPass runtime =
                        _normalCameraPasses[
                            authoredPasses[passIndex].PreparedPass];
                    EnsurePassConstantsCurrent(
                        frameBuffer,
                        runtime,
                        frameState,
                        sunShadowReceiverFrame);
                }

                RenderNormalCameraDrawSourceKind sourceKind =
                    authoredPasses[0].PreparedPass.SourceKind;
                MapRenderCpuPhase cpuPhase = sourceKind ==
                    RenderNormalCameraDrawSourceKind.World
                        ? MapRenderCpuPhase.WorldGeometry
                        : MapRenderCpuPhase.StaticModels;
                if (activeCpuPhase != cpuPhase)
                {
                    cpuTimingScope.Dispose();
                    cpuTimingScope = _telemetry.BeginCpuPhase(cpuPhase);
                    activeCpuPhase = cpuPhase;
                }

                if (sourceKind ==
                    RenderNormalCameraDrawSourceKind.World)
                {
                    RenderNormalCameraDrawSubmissionSnapshot firstDraw =
                        authoredPasses[0];
                    MetalPreparedNormalCameraPass firstRuntime =
                        _normalCameraPasses[firstDraw.PreparedPass];
                    worldVisibleCount = checked(
                        worldVisibleCount +
                        (visibilityPlan?.VisibleWorldSurfaceCount ?? 1));
                    worldVisibleRunCount = checked(
                        worldVisibleRunCount + visibleRunCount);
                    worldTriangles = checked(
                        worldTriangles +
                        (visibilityPlan is null
                            ? TriangleCount(
                                firstRuntime.Geometry.PrimitiveType,
                                firstDraw.Range.IndexCount,
                                visibleInstanceCount)
                            : TriangleCount(
                                firstRuntime.Geometry.PrimitiveType,
                                checked((int)visibilityPlan
                                    .VisibleWorldIndexCount),
                                visibleInstanceCount)));
                }
                MapRenderGpuPhase gpuPhase =
                    ResolveNormalCameraGpuPhase(group, sourceKind);
                for (int passIndex = 0;
                     passIndex < authoredPasses.Length;
                     passIndex++)
                {
                    RenderNormalCameraDrawSubmissionSnapshot draw =
                        authoredPasses[passIndex];
                    int passVisibleRunCount = ResolveVisibleRunCount(
                        visibilityPlan,
                        passIndex,
                        visibleRunCount);
                    if (passVisibleRunCount == 0)
                        continue;
                    MetalPreparedNormalCameraPass runtime =
                        _normalCameraPasses[draw.PreparedPass];
                    if (runtime.PipelineState.NativePtr != currentPipeline)
                    {
                        encoder.SetRenderPipelineState(runtime.PipelineState);
                        currentPipeline = runtime.PipelineState.NativePtr;
                        _telemetry.AddCounter(
                            MapRenderFrameCounter.ProgramChanges);
                    }
                    else
                    {
                        _telemetry.AddCounter(
                            MapRenderFrameCounter.StateShadowElidedCalls);
                    }
                    RenderState effectiveState =
                        MetalRenderStateCache.Effective(
                            runtime.Source.SourceState);
                    if (currentState != effectiveState)
                    {
                        _renderStates.ApplyRasterState(
                            encoder,
                            runtime.Source.SourceState);
                        currentState = effectiveState;
                        _telemetry.AddCounter(
                            MapRenderFrameCounter.RenderStateChanges);
                    }
                    else
                    {
                        _telemetry.AddCounter(
                            MapRenderFrameCounter.StateShadowElidedCalls);
                    }

                    BindPassResources(
                        ref bindingShadow,
                        frameBuffer,
                        runtime,
                        sunShadowReceiverFrame);
                    for (int runIndex = 0;
                         runIndex < passVisibleRunCount;
                         runIndex++)
                    {
                        RenderDrawRange visibleRange =
                            ApplyVisibleRun(
                                draw.Range,
                                visibilityPlan,
                                passIndex,
                                runIndex);
                        IssueNormalCameraDraw(
                            encoder,
                            runtime,
                            visibleRange);

                        long triangles = TriangleCount(
                            runtime.Geometry.PrimitiveType,
                            visibleRange.IndexCount,
                            visibleRange.InstanceCount);
                        _telemetry.AddCounter(
                            MapRenderFrameCounter.DrawCalls);
                        _telemetry.AddCounter(
                            MapRenderFrameCounter.LogicalDrawCommands);
                        _telemetry.AddCounter(
                            MapRenderFrameCounter.Triangles,
                            triangles);
                        _telemetry.AddGpuPhaseWork(
                            gpuPhase,
                            drawCalls: 1,
                            triangles: triangles);
                        issuedDrawCalls++;
                    }
                }
            }
        }
        finally
        {
            cpuTimingScope.Dispose();
        }

        _telemetry.SetCounter(
            MapRenderFrameCounter.WorldVisible,
            worldVisibleCount);
        _telemetry.SetCounter(
            MapRenderFrameCounter.WorldVisibleRuns,
            worldVisibleRunCount);
        _telemetry.SetCounter(
            MapRenderFrameCounter.WorldVisibleTriangles,
            worldTriangles);
        _telemetry.SetCounter(
            MapRenderFrameCounter.StaticModelsVisible,
            ResolveStaticModelLightingVisibleObjectCount());
        if (issuedDrawCalls != 0)
            _telemetry.AddCounter(MapRenderFrameCounter.Passes);
    }

    private void PublishNormalCameraInventoryCounters()
    {
        MetalNormalCameraAdmissionTelemetry admission =
            _normalCameraAdmissionTelemetry;
        _telemetry.SetCounter(
            MapRenderFrameCounter.NormalCameraSnapshotBaseGroups,
            admission.SnapshotBaseGroups);
        _telemetry.SetCounter(
            MapRenderFrameCounter.NormalCameraSnapshotReceiverGroups,
            admission.SnapshotReceiverGroups);
        _telemetry.SetCounter(
            MapRenderFrameCounter.NormalCameraSnapshotBasePasses,
            admission.SnapshotBasePasses);
        _telemetry.SetCounter(
            MapRenderFrameCounter.NormalCameraSnapshotReceiverPasses,
            admission.SnapshotReceiverPasses);
        _telemetry.SetCounter(
            MapRenderFrameCounter.NormalCameraAuthorizedBaseGroups,
            admission.AuthorizedBaseGroups);
        _telemetry.SetCounter(
            MapRenderFrameCounter.NormalCameraAuthorizedReceiverGroups,
            admission.AuthorizedReceiverGroups);
        _telemetry.SetCounter(
            MapRenderFrameCounter.NormalCameraAuthorizedBasePasses,
            admission.AuthorizedBasePasses);
        _telemetry.SetCounter(
            MapRenderFrameCounter.NormalCameraAuthorizedReceiverPasses,
            admission.AuthorizedReceiverPasses);
        _telemetry.SetCounter(
            MapRenderFrameCounter.NormalCameraBlockedGroups,
            admission.BlockedGroups);
        _telemetry.SetCounter(
            MapRenderFrameCounter.NormalCameraBlockedPasses,
            admission.BlockedPasses);
        _telemetry.SetCounter(
            MapRenderFrameCounter.NormalCameraBlockedGenericPasses,
            admission.BlockedGenericPasses);
        _telemetry.SetCounter(
            MapRenderFrameCounter.NormalCameraBlockedRuntimeSamplerPasses,
            admission.BlockedRuntimeSamplerPasses);
        _telemetry.SetCounter(
            MapRenderFrameCounter.NormalCameraBlockedUnresolvedSamplerPasses,
            admission.BlockedUnresolvedSamplerPasses);
        _telemetry.SetCounter(
            MapRenderFrameCounter.NormalCameraBlockedSunShadowPasses,
            admission.BlockedSunShadowPasses);
        _telemetry.SetCounter(
            MapRenderFrameCounter.NormalCameraBlockedSpotShadowPasses,
            admission.BlockedSpotShadowPasses);
        _telemetry.SetCounter(
            MapRenderFrameCounter.NormalCameraBlockedModelLightingPasses,
            admission.BlockedModelLightingPasses);
        _telemetry.SetCounter(
            MapRenderFrameCounter.NormalCameraBlockedProcessedFloatZPasses,
            admission.BlockedProcessedFloatZPasses);
        _telemetry.SetCounter(
            MapRenderFrameCounter.NormalCameraBlockedLightAttenuationPasses,
            admission.BlockedLightAttenuationPasses);
        _telemetry.SetCounter(
            MapRenderFrameCounter.NormalCameraBlockedShaderPasses,
            admission.BlockedShaderPasses);
        _telemetry.SetCounter(
            MapRenderFrameCounter.NormalCameraBlockedConstantPasses,
            admission.BlockedConstantPasses);
        _telemetry.SetCounter(
            MapRenderFrameCounter.NormalCameraBlockedRenderStatePasses,
            admission.BlockedRenderStatePasses);
        _telemetry.SetCounter(
            MapRenderFrameCounter.NormalCameraBlockedResourcePasses,
            admission.BlockedResourcePasses);
        _telemetry.SetCounter(
            MapRenderFrameCounter.NormalCameraBlockedOtherPasses,
            admission.BlockedOtherPasses);
        _telemetry.SetCounter(
            MapRenderFrameCounter.WorldCandidates,
            _normalCameraWorldCandidateCount);
        _telemetry.SetCounter(
            MapRenderFrameCounter.StaticModelCandidates,
            _normalCameraStaticCandidateCount);
        _telemetry.SetCounter(
            MapRenderFrameCounter.TextureResidentCount,
            _resources.TextureCount);
        _telemetry.SetCounter(
            MapRenderFrameCounter.TextureResidentBytes,
            _resources.UploadedTextureByteCount);
    }

    private static MetalNormalCameraAdmissionTelemetry
        CreateNormalCameraAdmissionTelemetry(
            ImmutableArray<RenderNormalCameraPreparedPassSnapshot>
                snapshotPasses,
            IReadOnlySet<RenderNormalCameraPreparedPassSnapshot>
                authorizedPasses,
            IReadOnlyDictionary<RenderNormalCameraPreparedPassSnapshot, string>
                failedPasses,
            int snapshotBaseGroups,
            int snapshotReceiverGroups,
            int authorizedBaseGroups,
            int authorizedReceiverGroups,
            int blockedGroups)
    {
        int snapshotBasePasses = 0;
        int snapshotReceiverPasses = 0;
        for (int index = 0; index < snapshotPasses.Length; index++)
        {
            if (IsReceiverPass(snapshotPasses[index]))
                snapshotReceiverPasses++;
            else
                snapshotBasePasses++;
        }

        int authorizedBasePasses = 0;
        int authorizedReceiverPasses = 0;
        foreach (RenderNormalCameraPreparedPassSnapshot pass in
                 authorizedPasses)
        {
            if (IsReceiverPass(pass))
                authorizedReceiverPasses++;
            else
                authorizedBasePasses++;
        }

        int blockedGenericPasses = 0;
        int blockedRuntimeSamplerPasses = 0;
        int blockedUnresolvedSamplerPasses = 0;
        int blockedSunShadowPasses = 0;
        int blockedSpotShadowPasses = 0;
        int blockedModelLightingPasses = 0;
        int blockedProcessedFloatZPasses = 0;
        int blockedLightAttenuationPasses = 0;
        int blockedShaderPasses = 0;
        int blockedConstantPasses = 0;
        int blockedRenderStatePasses = 0;
        int blockedResourcePasses = 0;
        // A complete authored group is rejected atomically. Passes which
        // prepared successfully but share a rejected group are blocked too,
        // even though they have no independent backend failure string.
        int blockedPasses = checked(
            snapshotPasses.Length - authorizedPasses.Count);
        int blockedOtherPasses = checked(
            blockedPasses - failedPasses.Count);
        foreach ((RenderNormalCameraPreparedPassSnapshot pass,
                  string blocker) in failedPasses)
        {
            if (HasGenericMaterialMarker(pass.ShaderProvenance))
            {
                blockedGenericPasses++;
                continue;
            }
            if (blocker.StartsWith("runtime", StringComparison.Ordinal))
            {
                blockedRuntimeSamplerPasses++;
                if (blocker.Contains(
                        "UNRESOLVED_CODE_SAMPLER",
                        StringComparison.Ordinal))
                {
                    blockedUnresolvedSamplerPasses++;
                }
                else if (blocker.Contains(
                             "SPOT_SHADOW",
                             StringComparison.Ordinal))
                {
                    blockedSpotShadowPasses++;
                }
                else if (blocker.Contains(
                             "SUN_SHADOW",
                             StringComparison.Ordinal))
                {
                    blockedSunShadowPasses++;
                }
                else if (blocker.Contains(
                             "MODEL_LIGHTING",
                             StringComparison.Ordinal))
                {
                    blockedModelLightingPasses++;
                }
                else if (blocker.Contains(
                             "PROCESSED_FLOATZ",
                             StringComparison.Ordinal))
                {
                    blockedProcessedFloatZPasses++;
                }
                else if (blocker.Contains(
                             "LIGHT_ATTENUATION",
                             StringComparison.Ordinal))
                {
                    blockedLightAttenuationPasses++;
                }
                continue;
            }
            if (StartsWithAny(
                    blocker,
                    "shaderProgram=",
                    "vertexMsl=",
                    "fragmentMsl=",
                    "fragmentAttachments=",
                    "metalPipeline="))
            {
                blockedShaderPasses++;
            }
            else if (StartsWithAny(
                         blocker,
                         "directConstants=",
                         "vertexConstants=",
                         "vertexConstantC",
                         "codePixelRow"))
            {
                blockedConstantPasses++;
            }
            else if (blocker.StartsWith(
                         "renderState=",
                         StringComparison.Ordinal))
            {
                blockedRenderStatePasses++;
            }
            else if (StartsWithAny(
                         blocker,
                         "rsxVertexInputs=",
                         "samplerDestination",
                         "staticComposition=",
                         "staticInstances=",
                         "staticLightingPayload=",
                         "staticInstanceLayout=",
                         "staticInstanceStride="))
            {
                blockedResourcePasses++;
            }
            else
            {
                blockedOtherPasses++;
            }
        }

        return new MetalNormalCameraAdmissionTelemetry(
            snapshotBaseGroups,
            snapshotReceiverGroups,
            snapshotBasePasses,
            snapshotReceiverPasses,
            authorizedBaseGroups,
            authorizedReceiverGroups,
            authorizedBasePasses,
            authorizedReceiverPasses,
            blockedGroups,
            blockedPasses,
            blockedGenericPasses,
            blockedRuntimeSamplerPasses,
            blockedUnresolvedSamplerPasses,
            blockedSunShadowPasses,
            blockedSpotShadowPasses,
            blockedModelLightingPasses,
            blockedProcessedFloatZPasses,
            blockedLightAttenuationPasses,
            blockedShaderPasses,
            blockedConstantPasses,
            blockedRenderStatePasses,
            blockedResourcePasses,
            blockedOtherPasses);
    }

    private static bool IsReceiverPass(
        RenderNormalCameraPreparedPassSnapshot pass) =>
        pass.WorldReceiverVariant.HasValue ||
        pass.StaticReceiverVariant.HasValue;

    private static bool StartsWithAny(
        string value,
        params ReadOnlySpan<string> prefixes)
    {
        for (int index = 0; index < prefixes.Length; index++)
        {
            if (value.StartsWith(prefixes[index], StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private bool TryPrepareNormalCameraPass(
        MapRenderScene scene,
        RenderNormalCameraPreparedPassSnapshot pass,
        out MetalPreparedNormalCameraPass? runtime,
        out string blocker)
    {
        runtime = null;
        blocker = string.Empty;
        RenderWorldShaderProvenanceSnapshot shader = pass.ShaderProvenance;
        if (HasGenericMaterialMarker(shader))
        {
            return TryPrepareGenericNormalCameraPass(
                scene,
                pass,
                out runtime,
                out blocker);
        }
        int exactRsxInputCount = checked(
            pass.Geometry.VertexCount *
            RenderWorldDrawPacketSnapshot.RsxVertexInputFloatStride);
        if (pass.RsxVertexInputs.Length != exactRsxInputCount)
        {
            blocker = "rsxVertexInputs=EXACT_DIRECT_PAYLOAD_REQUIRED";
            return false;
        }
        if (pass.UnresolvedCodeSamplerCount != 0)
        {
            blocker = "runtimeSamplers=UNRESOLVED_CODE_SAMPLER";
            return false;
        }
        if (!TryPlanRuntimeSamplerBindings(
                shader.RuntimeSamplerRequirements,
                shader.FragmentProgramIr,
                out MetalNormalCameraRuntimeSamplerBinding[]
                    runtimeSamplerBindings,
                out blocker))
        {
            return false;
        }
        if (HasRuntimeSampler(
                runtimeSamplerBindings,
                ShaderRuntimeSamplerResourceKind.LightAttenuation) &&
            !TryGetNormalCameraLightAttenuationBinding(
                pass.SceneLightIndex,
                out _))
        {
            blocker =
                "runtimeSampler=EXACT_LIGHT_ATTENUATION_TEXTURE_REQUIRED";
            return false;
        }

        TranslatedProgramDirectCodeConstantPlanBuildResult directResult =
            TranslatedProgramDirectCodeConstantPlanner.TryPlan(
                shader.ConstantDestinations,
                shader.CodePixelConstantPatchPlans,
                EditorPreviewFogRenderingEnabled,
                _normalCameraActiveFog,
                scene.EditorPreviewLighting,
                MapRenderEditorPreviewPrimaryLightInvocationPolicy.Resolve(
                    scene.EditorPreviewVision?.Vision?.PrimaryLight,
                    useHeroLighting: false),
                pass.SceneLightIndex,
                _normalCameraSceneLightFrame);
        if (!directResult.IsReady || directResult.Plan is null)
        {
            blocker = $"directConstants={string.Join('|', directResult.Blockers)}";
            return false;
        }
        TranslatedProgramVertexConstantBindingPlanBuildResult vertexResult =
            TranslatedProgramVertexConstantBindingPlanner.TryPlan(
                shader.ProgramVertexConstantDestinations,
                shader.ConstantDestinations,
                shader.EmbeddedVertexConstants,
                directResult.Plan);
        if (!vertexResult.IsReady || vertexResult.Plan is null)
        {
            blocker = $"vertexConstants={string.Join('|', vertexResult.Blockers)}";
            return false;
        }
        if (!SupportsDynamicConstants(
                vertexResult.Plan,
                directResult.Plan,
                shader,
                pass.SourceKind,
                HasRuntimeSampler(
                    runtimeSamplerBindings,
                    ShaderRuntimeSamplerResourceKind.SunShadowAtlas),
                HasRuntimeSampler(
                    runtimeSamplerBindings,
                    ShaderRuntimeSamplerResourceKind.SpotShadowAtlas),
                HasRuntimeSampler(
                    runtimeSamplerBindings,
                    ShaderRuntimeSamplerResourceKind.ModelLightingAtlas),
                out blocker))
        {
            return false;
        }

        try
        {
            _ = _renderStates.GetOrCreate(pass.SourceState);
        }
        catch (InvalidOperationException exception)
        {
            blocker = $"renderState={exception.Message}";
            return false;
        }

        if (_normalCameraPipelines is null ||
            !_normalCameraPipelines.TryGetOrCreate(
                pass,
                vertexResult.Plan,
                out MetalProgramPipeline? pipeline,
                out blocker))
        {
            return false;
        }
        if (pipeline!.UsesStaticModelInstancing !=
            (pass.SourceKind == RenderNormalCameraDrawSourceKind.StaticModel))
        {
            blocker = "staticComposition=SOURCE_KIND_MISMATCH";
            return false;
        }
        if (!TryResolveTextureBindings(
                pass,
                pipeline.SampledDestinations,
                runtimeSamplerBindings,
                out MetalNormalCameraTextureBinding[] textureBindings,
                out blocker))
        {
            return false;
        }

        MetalGeometryResource geometry =
            _resources.RequireGeometry(pass.Geometry.Identity);
        MetalInstanceResource? instances = null;
        MapRenderStaticInstanceLightingPayload lightingPayload =
            MapRenderStaticInstanceLightingPayload.None;
        bool needsOwnedInstances = false;
        if (pipeline.UsesStaticModelInstancing)
        {
            if (pass.Instances is null)
            {
                blocker = "staticInstances=RESOURCE_MISSING";
                return false;
            }
            if (!Enum.TryParse(
                    pipeline.StaticLightingPayload,
                    ignoreCase: false,
                    out lightingPayload))
            {
                blocker = "staticLightingPayload=UNRECOGNIZED";
                return false;
            }
            int expectedStride = MapRenderStaticInstanceBufferPacker
                .FloatStride(lightingPayload);
            int expectedPlacementOffset = lightingPayload ==
                MapRenderStaticInstanceLightingPayload.None
                    ? 0
                    : 1;
            if (pipeline.StaticInstanceFloat4Stride != expectedStride / 4 ||
                pipeline.StaticPlacementFloat4Offset !=
                    expectedPlacementOffset)
            {
                blocker = "staticInstanceLayout=LOWERING_PACKER_MISMATCH";
                return false;
            }
            needsOwnedInstances =
                lightingPayload != MapRenderStaticInstanceLightingPayload.None;
            if (!needsOwnedInstances)
            {
                instances = _resources.RequireInstances(
                    pass.Instances.Identity);
                if (instances.StrideBytes != expectedStride * sizeof(float))
                {
                    blocker = "staticInstanceStride=RESOURCE_MISMATCH";
                    return false;
                }
            }
        }

        int staticPixelRowCount = Math.Max(
            1,
            (shader.FragmentProgramIr?.StaticConstantPatches
                .Select(patch => patch.ArgumentOrdinal)
                .DefaultIfEmpty(-1)
                .Max() ?? -1) + 1);
        runtime = new MetalPreparedNormalCameraPass(
            pass,
            pipeline,
            directResult.Plan,
            vertexResult.Plan,
            geometry,
            instances,
            lightingPayload,
            needsOwnedInstances,
            staticPixelRowCount,
            textureBindings,
            runtimeSamplerBindings);
        return true;
    }

    private static bool TryPlanRuntimeSamplerBindings(
        IReadOnlyList<ShaderRuntimeSamplerRequirement> requirements,
        RsxFragmentProgramIr? fragmentProgram,
        out MetalNormalCameraRuntimeSamplerBinding[] bindings,
        out string blocker)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        if (requirements.Count == 0)
        {
            bindings = [];
            blocker = string.Empty;
            return true;
        }

        var destinations = new HashSet<ushort>();
        var result = new List<MetalNormalCameraRuntimeSamplerBinding>(
            requirements.Count);
        foreach (ShaderRuntimeSamplerRequirement requirement in requirements)
        {
            bool hasAbi = CodePixelSamplerAbi.TryResolve(
                requirement.CodeSamplerArgument,
                out CodePixelSamplerAbiEntry abi);
            RsxFragmentSamplerFeatures samplerFeatures =
                fragmentProgram?.SamplerFeatureProfile.FeaturesFor(
                    requirement.Destination) ??
                RsxFragmentSamplerFeatures.None;
            bool exactSun =
                hasAbi &&
                requirement.ResourceKind ==
                    ShaderRuntimeSamplerResourceKind.SunShadowAtlas &&
                requirement.Status ==
                    ShaderRuntimeSamplerRequirementStatus
                        .SameRevisionAtlasRequired &&
                requirement.CodeSamplerArgument ==
                    MaterialTextureSource.ShadowMapSun &&
                string.Equals(
                    abi.TextureTarget,
                    "Texture2DShadow",
                    StringComparison.Ordinal) &&
                samplerFeatures == RsxFragmentSamplerFeatures.Shadow;
            bool exactSpot =
                hasAbi &&
                requirement.ResourceKind ==
                    ShaderRuntimeSamplerResourceKind.SpotShadowAtlas &&
                requirement.Status ==
                    ShaderRuntimeSamplerRequirementStatus
                        .SameRevisionAtlasRequired &&
                requirement.CodeSamplerArgument ==
                    MaterialTextureSource.ShadowMapSpot &&
                string.Equals(
                    abi.TextureTarget,
                    "Texture2DShadow",
                    StringComparison.Ordinal) &&
                samplerFeatures == RsxFragmentSamplerFeatures.Shadow;
            bool exactModelLighting =
                hasAbi &&
                requirement.ResourceKind ==
                    ShaderRuntimeSamplerResourceKind.ModelLightingAtlas &&
                requirement.Status ==
                    ShaderRuntimeSamplerRequirementStatus
                        .ImmutableSceneAtlasRequired &&
                requirement.CodeSamplerArgument ==
                    MaterialTextureSource.ModelLighting &&
                string.Equals(
                    abi.TextureTarget,
                    "Texture3D",
                    StringComparison.Ordinal) &&
                samplerFeatures == RsxFragmentSamplerFeatures.Volume;
            bool exactLightAttenuation =
                hasAbi &&
                requirement.ResourceKind ==
                    ShaderRuntimeSamplerResourceKind.LightAttenuation &&
                requirement.Status ==
                    ShaderRuntimeSamplerRequirementStatus
                        .ImmutableSceneTextureRequired &&
                requirement.CodeSamplerArgument ==
                    MaterialTextureSource.LightAttenuation &&
                string.Equals(
                    abi.TextureTarget,
                    "Texture2D",
                    StringComparison.Ordinal) &&
                samplerFeatures == RsxFragmentSamplerFeatures.None;
            bool exactProcessedFloatZ =
                hasAbi &&
                requirement.ResourceKind ==
                    ShaderRuntimeSamplerResourceKind.ProcessedFloatZ &&
                requirement.Status ==
                    ShaderRuntimeSamplerRequirementStatus
                        .SameRevisionTextureRequired &&
                requirement.CodeSamplerArgument ==
                    MaterialTextureSource.ProcessedFloatZ &&
                string.Equals(
                    abi.TextureTarget,
                    "Texture2D",
                    StringComparison.Ordinal) &&
                samplerFeatures == RsxFragmentSamplerFeatures.None;
            if ((!exactSun && !exactSpot && !exactModelLighting &&
                 !exactLightAttenuation && !exactProcessedFloatZ) ||
                abi.RuntimeResourceKind != requirement.ResourceKind ||
                abi.RuntimeRequirementStatus != requirement.Status ||
                !string.Equals(
                    abi.ResourceIdentity,
                    requirement.ResourceIdentity,
                    StringComparison.Ordinal) ||
                !destinations.Add(requirement.Destination))
            {
                bindings = [];
                blocker =
                    $"runtimeSampler{requirement.Destination}=" +
                    (requirement.ResourceKind switch
                    {
                        ShaderRuntimeSamplerResourceKind.SpotShadowAtlas =>
                            "EXACT_SPOT_SHADOW_PUBLICATION_REQUIRED",
                        ShaderRuntimeSamplerResourceKind.ModelLightingAtlas =>
                            "EXACT_MODEL_LIGHTING_PUBLICATION_REQUIRED",
                        ShaderRuntimeSamplerResourceKind.ProcessedFloatZ =>
                            "EXACT_PROCESSED_FLOATZ_PUBLICATION_REQUIRED",
                        ShaderRuntimeSamplerResourceKind.LightAttenuation =>
                            "EXACT_LIGHT_ATTENUATION_TEXTURE_REQUIRED",
                        _ => "EXACT_SUN_SHADOW_PUBLICATION_REQUIRED"
                    });
                return false;
            }
            result.Add(new MetalNormalCameraRuntimeSamplerBinding(
                requirement.Destination,
                requirement.ResourceKind));
        }

        bindings = result.ToArray();
        blocker = string.Empty;
        return true;
    }

    private static bool HasRuntimeSampler(
        IReadOnlyList<MetalNormalCameraRuntimeSamplerBinding> bindings,
        ShaderRuntimeSamplerResourceKind resourceKind)
    {
        for (int index = 0; index < bindings.Count; index++)
        {
            if (bindings[index].ResourceKind == resourceKind)
                return true;
        }
        return false;
    }

    private bool TryResolveTextureBindings(
        RenderNormalCameraPreparedPassSnapshot pass,
        ImmutableArray<int> destinations,
        IReadOnlyList<MetalNormalCameraRuntimeSamplerBinding>
            runtimeBindings,
        out MetalNormalCameraTextureBinding[] bindings,
        out string blocker)
    {
        var result = new List<MetalNormalCameraTextureBinding>(
            destinations.Length);
        var consumedRuntimeDestinations = new HashSet<ulong>();
        foreach (int destination in destinations)
        {
            var candidates = new HashSet<(
                RenderSemanticIdentity Texture,
                RenderSemanticIdentity Sampler)>();
            foreach (RenderNormalCameraMaterialSamplerSnapshot sampler in
                     pass.MaterialSamplers)
            {
                if (sampler.SamplerDest == destination &&
                    sampler.TextureIdentity is { } texture &&
                    sampler.SamplerIdentity is { } state)
                {
                    candidates.Add((texture, state));
                }
            }
            foreach (RenderNormalCameraColorLayerSnapshot layer in
                     pass.ColorLayers)
            {
                if (layer.SamplerDest == destination)
                {
                    candidates.Add((
                        layer.TextureIdentity,
                        layer.SamplerIdentity));
                }
            }
            if (pass.SourcePass.SamplerDest == destination)
            {
                candidates.Add((
                    pass.BaseTextureIdentity,
                    pass.BaseSamplerIdentity));
            }

            ulong runtimeDestination = checked((ulong)destination);
            int runtimeBindingIndex = -1;
            for (int bindingIndex = 0;
                 bindingIndex < runtimeBindings.Count;
                 bindingIndex++)
            {
                if (runtimeBindings[bindingIndex].Destination ==
                    runtimeDestination)
                {
                    runtimeBindingIndex = bindingIndex;
                    break;
                }
            }
            if (runtimeBindingIndex >= 0)
            {
                MetalNormalCameraRuntimeSamplerBinding runtimeBinding =
                    runtimeBindings[runtimeBindingIndex];
                if (candidates.Count != 0 ||
                    !consumedRuntimeDestinations.Add(
                        runtimeBinding.Destination))
                {
                    bindings = [];
                    blocker = $"samplerDestination{destination}=" +
                        "RUNTIME_RESOURCE_AMBIGUOUS";
                    return false;
                }
                continue;
            }

            if (candidates.Count != 1)
            {
                bindings = [];
                blocker = $"samplerDestination{destination}=" +
                    (candidates.Count == 0
                        ? "RESOURCE_UNAVAILABLE"
                        : "RESOURCE_AMBIGUOUS");
                return false;
            }
            (RenderSemanticIdentity textureIdentity,
             RenderSemanticIdentity samplerIdentity) = candidates.Single();
            MetalSamplerResource samplerResource =
                _resources.RequireSampler(samplerIdentity);
            MetalTextureResource textureResource =
                _resources.RequireTexture(textureIdentity);
            result.Add(new MetalNormalCameraTextureBinding(
                checked((ulong)destination),
                textureResource.ResolveSampledTexture(
                    samplerResource.UsesSrgbReads),
                samplerResource.State));
        }

        if (consumedRuntimeDestinations.Count != runtimeBindings.Count)
        {
            bindings = [];
            blocker = "runtimeSamplers=PROGRAM_DESTINATION_NOT_SAMPLED";
            return false;
        }

        bindings = result.ToArray();
        blocker = string.Empty;
        return true;
    }

    private void AssignFrameConstantOffsets(
        IReadOnlyList<MetalPreparedNormalCameraPass> passes)
    {
        int cursor = 0;
        _normalCameraFrameConstantsOffset = cursor;
        cursor = Align(checked(cursor + FrameVertexConstantByteCount));
        foreach (MetalPreparedNormalCameraPass pass in passes)
        {
            if (pass.GenericMaterial is not null)
            {
                pass.CodePixelConstantsOffset = cursor;
                cursor = Align(checked(
                    cursor +
                    MetalGenericMaterialShaderAbi.ConstantByteCount));
                continue;
            }
            pass.VertexConstantsOffset = cursor;
            cursor = Align(checked(cursor + VertexConstantByteCount));
            pass.CodePixelConstantsOffset = cursor;
            cursor = Align(checked(cursor + CodePixelConstantByteCount));
            pass.StaticPixelConstantsOffset = cursor;
            cursor = Align(checked(
                cursor + pass.StaticPixelRowCount * VectorByteCount));
            if (pass.UsesStaticModelInstancing)
            {
                pass.StaticCompositionOffset = cursor;
                cursor = Align(checked(cursor + 2 * VectorByteCount));
            }
        }
        _normalCameraFrameBufferByteCount = cursor;
    }

    private void CreateImmutableDrawBuffer(
        IReadOnlyList<MetalPreparedNormalCameraPass> passes)
    {
        int cursor = 0;
        foreach (MetalPreparedNormalCameraPass pass in passes)
        {
            pass.RsxVertexInputOffset = Align(cursor);
            cursor = checked(
                pass.RsxVertexInputOffset +
                pass.Source.RsxVertexInputs.Length * sizeof(float));
            if (!pass.NeedsImmutableOwnedInstanceData)
                continue;
            pass.OwnedInstanceOffset = Align(cursor);
            cursor = checked(
                pass.OwnedInstanceOffset +
                pass.Source.StaticInstances.Length *
                MapRenderStaticInstanceBufferPacker.FloatStride(
                    pass.LightingPayload) *
                sizeof(float));
        }
        if (cursor == 0)
            return;

        _normalCameraImmutableBuffer = CreateSharedBuffer(cursor);
        Span<byte> bytes = BufferBytes(
            _normalCameraImmutableBuffer,
            0,
            cursor);
        bytes.Clear();
        foreach (MetalPreparedNormalCameraPass pass in passes)
        {
            pass.Source.RsxVertexInputs.AsSpan().CopyTo(
                BufferFloats(
                    _normalCameraImmutableBuffer,
                    pass.RsxVertexInputOffset,
                    pass.Source.RsxVertexInputs.Length));
            if (!pass.NeedsImmutableOwnedInstanceData)
                continue;
            MapRenderStaticInstanceBufferPacker.PackAll(
                pass.Source.StaticInstances,
                pass.LightingPayload,
                BufferFloats(
                    _normalCameraImmutableBuffer,
                    pass.OwnedInstanceOffset,
                    pass.Source.StaticInstances.Length *
                    MapRenderStaticInstanceBufferPacker.FloatStride(
                        pass.LightingPayload)));
        }
    }

    private void CreateFrameConstantBuffers(
        IReadOnlyList<MetalPreparedNormalCameraPass> passes)
    {
        if (_normalCameraFrameBufferByteCount == 0)
            return;
        for (int index = 0; index < _normalCameraFrameBuffers.Length; index++)
        {
            MTLBuffer buffer = CreateSharedBuffer(
                _normalCameraFrameBufferByteCount);
            _normalCameraFrameBuffers[index] = buffer;
            BufferBytes(
                buffer,
                0,
                _normalCameraFrameBufferByteCount).Clear();
            for (int passIndex = 0;
                 passIndex < passes.Count;
                 passIndex++)
            {
                InitializeStaticPassConstants(
                    buffer,
                    passes[passIndex]);
            }
        }
    }

    private static void InitializeStaticPassConstants(
        MTLBuffer buffer,
        MetalPreparedNormalCameraPass pass)
    {
        if (pass.GenericMaterial is not null)
        {
            InitializeStaticGenericMaterialConstants(buffer, pass);
            return;
        }

        Span<Vector4> vertex = BufferVectors(
            buffer,
            pass.VertexConstantsOffset,
            RsxVertexConstantLayout.Count);
        IReadOnlyList<TranslatedProgramVertexConstantBinding> bindings =
            pass.VertexConstantPlan.Bindings;
        for (int bindingIndex = 0;
             bindingIndex < bindings.Count;
             bindingIndex++)
        {
            TranslatedProgramVertexConstantBinding binding =
                bindings[bindingIndex];
            if (binding.Kind ==
                    TranslatedProgramVertexConstantBindingKind.StaticValue &&
                binding.StaticValue is { } value)
            {
                vertex[binding.Destination] = ToVector4(value);
            }
        }

        Span<Vector4> codePixel = BufferVectors(
            buffer,
            pass.CodePixelConstantsOffset,
            CodeConstantLayout.Float4Count);
        IReadOnlyList<DirectCodeConstantRow> directRows =
            pass.DirectCodePlan.Rows;
        for (int rowIndex = 0;
             rowIndex < directRows.Count;
             rowIndex++)
        {
            DirectCodeConstantRow row = directRows[rowIndex];
            ushort sourceRow = checked((ushort)row.SourceRowIndex);
            if (!pass.DirectCodePlan.IsDynamicSourceRow(sourceRow))
                codePixel[row.SourceRowIndex] = ToVector4(row.Value);
        }

        Span<Vector4> staticPixel = BufferVectors(
            buffer,
            pass.StaticPixelConstantsOffset,
            pass.StaticPixelRowCount);
        if (pass.Source.ShaderProvenance.FragmentProgramIr is { } fragment)
        {
            for (int patchIndex = 0;
                 patchIndex < fragment.StaticConstantPatches.Length;
                 patchIndex++)
            {
                StaticFragmentConstantPatch patch =
                    fragment.StaticConstantPatches[patchIndex];
                staticPixel[patch.ArgumentOrdinal] = ToVector4(patch.Value);
            }
        }

        if (!pass.UsesStaticModelInstancing)
            return;
        MapRenderEditorVegetationAnimationPlan? vegetation =
            pass.Source.VegetationAnimation;
        Span<Vector4> composition = BufferVectors(
            buffer,
            pass.StaticCompositionOffset,
            2);
        composition[0] = new Vector4(
            vegetation?.IsEnabled == true ? 1f : 0f,
            vegetation?.Amplitude ?? 0f,
            vegetation?.AngularFrequency ?? 0f,
            vegetation?.SpatialFrequency ?? 0f);
        composition[1] = new Vector4(
            pass.Source.LocalBounds.Min.Z,
            pass.Source.LocalBounds.Max.Z - pass.Source.LocalBounds.Min.Z,
            0f,
            0f);
    }

    private void WriteFrameConstants(
        MTLBuffer frameBuffer,
        in DerivedMatrixState matrices,
        float animationTimeSeconds,
        ShaderConstantValue clipScale,
        ShaderConstantValue clipOffset,
        ShaderConstantValue zNear)
    {
        Span<Vector4> rows = BufferVectors(
            frameBuffer,
            _normalCameraFrameConstantsOffset,
            MetalRsxShaderAbi.FrameVertexFloat4Count);
        for (int semanticIndex = 0;
             semanticIndex < FrameMatrixSemantics.Length;
             semanticIndex++)
        {
            for (int transformIndex = 0;
                 transformIndex < FrameMatrixTransforms.Length;
                 transformIndex++)
            {
                for (int row = 0; row < 4; row++)
                {
                    if (!DerivedMatrixResolver.TryResolveRow(
                            matrices,
                            FrameMatrixSemantics[semanticIndex],
                            FrameMatrixTransforms[transformIndex],
                            row,
                            out Vector4 value))
                    {
                        throw new InvalidOperationException(
                            $"Metal frame matrix " +
                            $"{FrameMatrixSemantics[semanticIndex]}:" +
                            $"{FrameMatrixTransforms[transformIndex]} " +
                            $"row {row} is unavailable.");
                    }
                    rows[((semanticIndex * 4 + transformIndex) * 4) + row] =
                        value;
                }
            }
        }
        rows[MetalRsxShaderAbi.FrameGameTimeRow] = ToVector4(
            FrameDirectCodeConstants.ProduceGameTimeValue(
                animationTimeSeconds));
        rows[MetalRsxShaderAbi.FrameClipScaleRow] = ToVector4(clipScale);
        rows[MetalRsxShaderAbi.FrameClipOffsetRow] = ToVector4(clipOffset);
        rows[MetalRsxShaderAbi.FrameZNearRow] = ToVector4(zNear);
        rows[MetalRsxShaderAbi.FrameEyeOffsetRow] =
            new Vector4(matrices.EyeOffset, 0f);
        rows[MetalRsxShaderAbi.FrameVegetationTimeRow] =
            new Vector4(animationTimeSeconds, 0f, 0f, 0f);
    }

    private void EnsurePassConstantsCurrent(
        MTLBuffer frameBuffer,
        MetalPreparedNormalCameraPass pass,
        in MetalNormalCameraFrameState frameState,
        MetalSunShadowReceiverFrame? sunShadowReceiverFrame)
    {
        if (pass.LastConstantsFrameIndex == _frameIndex &&
            pass.LastConstantsFrameStateRevision ==
                _normalCameraFrameStateRevision)
        {
            return;
        }

        UpdatePassConstants(
            frameBuffer,
            pass,
            frameState.Matrices,
            frameState.AnimationTimeSeconds,
            frameState.ClipSpaceScale,
            frameState.ClipSpaceOffset,
            frameState.ZNear,
            sunShadowReceiverFrame);
        pass.LastConstantsFrameIndex = _frameIndex;
        pass.LastConstantsFrameStateRevision =
            _normalCameraFrameStateRevision;
    }

    private void UpdatePassConstants(
        MTLBuffer frameBuffer,
        MetalPreparedNormalCameraPass pass,
        in DerivedMatrixState matrices,
        float animationTimeSeconds,
        ShaderConstantValue clipScale,
        ShaderConstantValue clipOffset,
        ShaderConstantValue zNear,
        MetalSunShadowReceiverFrame? sunShadowReceiverFrame)
    {
        if (pass.GenericMaterial is not null)
        {
            WriteGenericMaterialConstants(
                frameBuffer,
                pass);
            return;
        }

        MapRenderSpotShadowAtlasEntry? spotShadowEntry = null;
        sunShadowReceiverFrame?.TryGetSpotEntry(
            pass.Source.SceneLightIndex,
            out spotShadowEntry);
        if (pass.RequiresSpotShadowReceiver && spotShadowEntry is null)
        {
            throw new InvalidOperationException(
                $"Metal spot-shadow constants for scene light {pass.Source.SceneLightIndex} have no same-revision atlas entry.");
        }

        DerivedMatrixState passMatrices =
            pass.RequiresSpotShadowReceiver &&
            spotShadowEntry is not null
                ? DerivedMatrixResolver.WithShadowLookupSource(
                    matrices,
                    spotShadowEntry.ShadowLookupMatrix)
                : pass.RequiresSunShadowReceiver &&
                  sunShadowReceiverFrame is not null
                ? DerivedMatrixResolver.WithShadowLookupSource(
                    matrices,
                    sunShadowReceiverFrame.Projection.ShadowLookupMatrix)
                : matrices;
        Span<Vector4> vertex = BufferVectors(
            frameBuffer,
            pass.VertexConstantsOffset,
            RsxVertexConstantLayout.Count);
        TranslatedProgramVertexConstantBinding[] dynamicBindings =
            pass.DynamicVertexConstantBindings;
        for (int bindingIndex = 0;
             bindingIndex < dynamicBindings.Length;
             bindingIndex++)
        {
            TranslatedProgramVertexConstantBinding binding =
                dynamicBindings[bindingIndex];
            switch (binding.Kind)
            {
                case TranslatedProgramVertexConstantBindingKind
                    .DerivedMatrixRow:
                    if (binding.CodeMatrixSemantic is not { } semantic ||
                        !DerivedMatrixResolver.TryResolveRow(
                            passMatrices,
                            semantic,
                            binding.CodeMatrixTransform,
                            binding.CodeMatrixRow,
                            out Vector4 row))
                    {
                        throw new InvalidOperationException(
                            $"Metal vertex constant c{binding.Destination} matrix is unavailable.");
                    }
                    vertex[binding.Destination] = row;
                    break;
                case TranslatedProgramVertexConstantBindingKind
                    .DynamicGameTime:
                case TranslatedProgramVertexConstantBindingKind
                    .DynamicSceneLightPosition:
                case TranslatedProgramVertexConstantBindingKind
                    .DynamicSceneLightShadow:
                case TranslatedProgramVertexConstantBindingKind
                    .DynamicSunShadowProjection:
                case TranslatedProgramVertexConstantBindingKind
                    .DynamicClipSpaceLookup:
                case TranslatedProgramVertexConstantBindingKind.DynamicZNear:
                    vertex[binding.Destination] = ToVector4(
                        RequireDynamicCodeConstant(
                            binding.DynamicCodeConstantSourceRow!.Value,
                            pass.DirectCodePlan.SceneLightIndex,
                            passMatrices.EyeOffset,
                            animationTimeSeconds,
                            clipScale,
                            clipOffset,
                            zNear,
                            sunShadowReceiverFrame,
                            spotShadowEntry));
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported Metal dynamic vertex constant {binding.Kind}.");
            }
        }

        Span<Vector4> codePixel = BufferVectors(
            frameBuffer,
            pass.CodePixelConstantsOffset,
            CodeConstantLayout.Float4Count);
        CodePixelConstantPatchPlan[] dynamicPatches =
            pass.DynamicCodePixelConstantPatches;
        for (int patchIndex = 0;
             patchIndex < dynamicPatches.Length;
             patchIndex++)
        {
            CodePixelConstantPatchPlan patch =
                dynamicPatches[patchIndex];
            codePixel[patch.CodeIndex] = ToVector4(
                RequireDynamicCodeConstant(
                    patch.CodeIndex,
                    TranslatedProgramDirectCodeConstantPlanner
                        .IsDynamicSceneLightSourceRow(patch.CodeIndex)
                            ? pass.DirectCodePlan.SceneLightIndex
                            : null,
                    passMatrices.EyeOffset,
                    animationTimeSeconds,
                    clipScale,
                    clipOffset,
                    zNear,
                    sunShadowReceiverFrame,
                    spotShadowEntry));
        }
    }

    private ShaderConstantValue RequireDynamicCodeConstant(
        ushort sourceRow,
        int? sceneLightIndex,
        Vector3 eyeOffset,
        float animationTimeSeconds,
        ShaderConstantValue clipScale,
        ShaderConstantValue clipOffset,
        ShaderConstantValue zNear,
        MetalSunShadowReceiverFrame? sunShadowReceiverFrame,
        MapRenderSpotShadowAtlasEntry? spotShadowEntry)
    {
        if (sourceRow == FrameDirectCodeConstants.GameTimeRowIndex)
        {
            return FrameDirectCodeConstants.ProduceGameTimeValue(
                animationTimeSeconds);
        }
        if (sourceRow ==
                FrameDirectCodeConstants.DirectionalLightDirectionRowIndex &&
            sceneLightIndex is { } lightIndex &&
            _normalCameraSceneLightFrame is { } lightFrame &&
            _normalCameraWorldSource is { } source &&
            source.AssetLookup.HasCanonicalAssetPoolRevision(
                lightFrame.AssetPoolRevision))
        {
            return MapRenderWorldEvent20SceneLightDirectCodeConstantProducer
                .ProducePositionValue(lightFrame, lightIndex, eyeOffset);
        }
        if (sourceRow is
                FrameDirectCodeConstants.LightSpotFactorsRowIndex or
                FrameDirectCodeConstants.LightFalloffPlacementRowIndex &&
            sceneLightIndex is { } spotLightIndex &&
            _normalCameraSceneLightFrame is { } spotLightFrame &&
            _normalCameraWorldSource is { } spotSource &&
            spotSource.AssetLookup.HasCanonicalAssetPoolRevision(
                spotLightFrame.AssetPoolRevision))
        {
            return sourceRow ==
                    FrameDirectCodeConstants.LightSpotFactorsRowIndex
                ? MapRenderWorldEvent20SceneLightDirectCodeConstantProducer
                    .ProduceSpotFactorsValue(
                        spotLightFrame,
                        spotLightIndex,
                        spotShadowEntry)
                : MapRenderWorldEvent20SceneLightDirectCodeConstantProducer
                    .ProduceLightFalloffPlacementValue(
                        spotLightFrame,
                        spotLightIndex,
                        spotShadowEntry);
        }
        if (sourceRow ==
            FrameDirectCodeConstants.ClipSpaceLookupScaleRowIndex)
        {
            return clipScale;
        }
        if (sourceRow ==
            FrameDirectCodeConstants.ClipSpaceLookupOffsetRowIndex)
        {
            return clipOffset;
        }
        if (sourceRow == FrameDirectCodeConstants.ZNearRowIndex)
            return zNear;
        if ((sourceRow == FrameDirectCodeConstants
                 .SunShadowSwitchPartitionRowIndex ||
             sourceRow == FrameDirectCodeConstants
                 .SunShadowMapScaleRowIndex) &&
            sunShadowReceiverFrame is { } receiver)
        {
            Vector4 value = sourceRow ==
                    FrameDirectCodeConstants
                        .SunShadowSwitchPartitionRowIndex
                ? receiver.Projection.CodeConstants.SwitchPartition
                : receiver.Projection.CodeConstants.ShadowMapScale;
            return new ShaderConstantValue(
                value.X,
                value.Y,
                value.Z,
                value.W);
        }
        throw new InvalidOperationException(
            $"Metal dynamic code constant row 0x{sourceRow:X2} is unavailable.");
    }

    private void BindPassResources(
        ref MetalNormalCameraEncoderBindingShadow bindings,
        MTLBuffer frameBuffer,
        MetalPreparedNormalCameraPass pass,
        MetalSunShadowReceiverFrame? sunShadowReceiverFrame)
    {
        if (pass.GenericMaterial is not null)
        {
            BindGenericMaterialResources(
                ref bindings,
                frameBuffer,
                pass);
            return;
        }

        bindings.SetVertexBuffer(
            _normalCameraImmutableBuffer,
            checked((ulong)pass.RsxVertexInputOffset),
            MetalRsxShaderAbi.VertexInputBufferIndex);
        int uniformUpdates = bindings.SetVertexBuffer(
            frameBuffer,
            checked((ulong)pass.VertexConstantsOffset),
            MetalRsxShaderAbi.VertexConstantBufferIndex)
                ? 1
                : 0;

        if (pass.UsesStaticModelInstancing)
        {
            MTLBuffer instanceBuffer;
            ulong instanceOffset;
            if (pass.LightingPayload ==
                MapRenderStaticInstanceLightingPayload.BaseLightingCoords)
            {
                RequireStaticModelLightingInstanceBinding(
                    pass,
                    out instanceBuffer,
                    out instanceOffset);
            }
            else if (pass.NeedsOwnedInstanceData)
            {
                instanceBuffer = _normalCameraImmutableBuffer;
                instanceOffset = checked((ulong)pass.OwnedInstanceOffset);
            }
            else
            {
                instanceBuffer = pass.Instances!.Buffer;
                instanceOffset = pass.Instances.Offset;
            }
            bindings.SetVertexBuffer(
                instanceBuffer,
                instanceOffset,
                MetalRsxShaderAbi.StaticInstanceBufferIndex);
            if (bindings.SetVertexBuffer(
                frameBuffer,
                checked((ulong)_normalCameraFrameConstantsOffset),
                MetalRsxShaderAbi.FrameVertexConstantBufferIndex))
            {
                uniformUpdates++;
            }
            if (bindings.SetVertexBuffer(
                frameBuffer,
                checked((ulong)pass.StaticCompositionOffset),
                MetalRsxShaderAbi.StaticCompositionBufferIndex))
            {
                uniformUpdates++;
            }
        }

        if (bindings.SetFragmentBuffer(
            frameBuffer,
            checked((ulong)pass.CodePixelConstantsOffset),
            MetalRsxShaderAbi.FragmentCodeConstantBufferIndex))
        {
            uniformUpdates++;
        }
        if (bindings.SetFragmentBuffer(
            frameBuffer,
            checked((ulong)pass.StaticPixelConstantsOffset),
            MetalRsxShaderAbi.FragmentStaticConstantBufferIndex))
        {
            uniformUpdates++;
        }
        if (_depthStencilFormat.EmulatesDepth24 &&
            bindings.SetFragmentBytes(
                _renderStates.CurrentDepthBias,
                MetalRsxShaderAbi.FragmentDepthBiasBufferIndex))
        {
            uniformUpdates++;
        }
        if (uniformUpdates != 0)
        {
            _telemetry.AddCounter(
                MapRenderFrameCounter.UniformUpdates,
                uniformUpdates);
        }

        for (int bindingIndex = 0;
             bindingIndex < pass.TextureBindings.Length;
             bindingIndex++)
        {
            MetalNormalCameraTextureBinding binding =
                pass.TextureBindings[bindingIndex];
            bindings.SetFragmentTexture(
                binding.Texture,
                binding.Destination);
            bindings.SetFragmentSampler(
                binding.Sampler,
                binding.Destination);
        }
        for (int bindingIndex = 0;
             bindingIndex < pass.RuntimeSamplerBindings.Length;
             bindingIndex++)
        {
            MetalNormalCameraRuntimeSamplerBinding binding =
                pass.RuntimeSamplerBindings[bindingIndex];
            switch (binding.ResourceKind)
            {
                case ShaderRuntimeSamplerResourceKind.SunShadowAtlas:
                    MetalSunShadowReceiverFrame sunReceiver =
                        sunShadowReceiverFrame ??
                        throw new InvalidOperationException(
                            "A Metal sun-shadow receiver reached binding without its same-revision atlas publication.");
                    bindings.SetFragmentTexture(
                        sunReceiver.Texture,
                        binding.Destination);
                    bindings.SetFragmentSampler(
                        sunReceiver.Sampler,
                        binding.Destination);
                    break;
                case ShaderRuntimeSamplerResourceKind.SpotShadowAtlas:
                    MetalSunShadowReceiverFrame spotReceiver =
                        sunShadowReceiverFrame ??
                        throw new InvalidOperationException(
                            "A Metal spot-shadow receiver reached binding without its same-revision atlas publication.");
                    if (!spotReceiver.TryGetSpotEntry(
                            pass.Source.SceneLightIndex,
                            out MapRenderSpotShadowAtlasEntry? spotEntry) ||
                        spotEntry is null)
                    {
                        throw new InvalidOperationException(
                            $"A Metal spot-shadow receiver for scene light {pass.Source.SceneLightIndex} reached binding without its same-revision atlas entry.");
                    }
                    bindings.SetFragmentTexture(
                        spotReceiver.SpotTexture,
                        binding.Destination);
                    bindings.SetFragmentSampler(
                        spotReceiver.SpotSampler,
                        binding.Destination);
                    break;
                case ShaderRuntimeSamplerResourceKind.ModelLightingAtlas:
                    RequireStaticModelLightingSamplerBinding(
                        out MTLTexture modelLightingTexture,
                        out MTLSamplerState modelLightingSampler);
                    bindings.SetFragmentTexture(
                        modelLightingTexture,
                        binding.Destination);
                    bindings.SetFragmentSampler(
                        modelLightingSampler,
                        binding.Destination);
                    break;
                case ShaderRuntimeSamplerResourceKind.ProcessedFloatZ:
                    RequireCurrentProcessedFloatZBinding(
                        out MTLTexture processedFloatZTexture,
                        out MTLSamplerState processedFloatZSampler);
                    bindings.SetFragmentTexture(
                        processedFloatZTexture,
                        binding.Destination);
                    bindings.SetFragmentSampler(
                        processedFloatZSampler,
                        binding.Destination);
                    break;
                case ShaderRuntimeSamplerResourceKind.LightAttenuation:
                    if (!TryGetNormalCameraLightAttenuationBinding(
                            pass.Source.SceneLightIndex,
                            out MetalNormalCameraLightAttenuationBinding
                                attenuation))
                    {
                        throw new InvalidOperationException(
                            $"A Metal light receiver for scene light {pass.Source.SceneLightIndex} reached binding without its immutable attenuation texture.");
                    }
                    bindings.SetFragmentTexture(
                        attenuation.Texture,
                        binding.Destination);
                    bindings.SetFragmentSampler(
                        attenuation.Sampler,
                        binding.Destination);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported Metal runtime sampler resource {binding.ResourceKind} reached normal-camera binding.");
            }
        }
    }

    private static void IssueNormalCameraDraw(
        MTLRenderCommandEncoder encoder,
        MetalPreparedNormalCameraPass pass,
        RenderDrawRange range)
    {
        int indexByteCount = pass.Geometry.IndexType == MTLIndexType.UInt16
            ? sizeof(ushort)
            : sizeof(uint);
        ulong indexOffset = checked(
            pass.Geometry.IndexOffset +
            (ulong)range.FirstIndex * (ulong)indexByteCount);
        encoder.DrawIndexedPrimitives(
            pass.Geometry.PrimitiveType,
            checked((ulong)range.IndexCount),
            pass.Geometry.IndexType,
            pass.Geometry.Buffer,
            indexOffset,
            checked((ulong)range.InstanceCount),
            range.BaseVertex,
            checked((ulong)range.FirstInstance));
    }

    private static MapRenderGpuPhase ResolveNormalCameraGpuPhase(
        MapRenderEditorDrawGroup<
            RenderNormalCameraDrawSubmissionSnapshot> group,
        RenderNormalCameraDrawSourceKind sourceKind)
    {
        if (group.Bucket == MapRenderEditorDrawBucket.Translucent)
            return MapRenderGpuPhase.Translucent;

        return (group.Bucket, sourceKind) switch
        {
            (MapRenderEditorDrawBucket.Opaque,
             RenderNormalCameraDrawSourceKind.World) =>
                MapRenderGpuPhase.WorldOpaque,
            (MapRenderEditorDrawBucket.AlphaTest,
             RenderNormalCameraDrawSourceKind.World) =>
                MapRenderGpuPhase.WorldCutout,
            (MapRenderEditorDrawBucket.Opaque,
             RenderNormalCameraDrawSourceKind.StaticModel) =>
                MapRenderGpuPhase.StaticOpaque,
            (MapRenderEditorDrawBucket.AlphaTest,
             RenderNormalCameraDrawSourceKind.StaticModel) =>
                MapRenderGpuPhase.StaticCutout,
            _ => throw new ArgumentOutOfRangeException(
                nameof(group),
                group.Bucket,
                "Unknown editor draw bucket.")
        };
    }

    private static bool SupportsDynamicConstants(
        TranslatedProgramVertexConstantBindingPlan vertexPlan,
        TranslatedProgramDirectCodeConstantPlan directPlan,
        RenderWorldShaderProvenanceSnapshot shader,
        RenderNormalCameraDrawSourceKind sourceKind,
        bool hasSunShadowRuntimeSampler,
        bool hasSpotShadowRuntimeSampler,
        bool hasModelLightingRuntimeSampler,
        out string blocker)
    {
        IReadOnlyList<TranslatedProgramVertexConstantBinding> bindings =
            vertexPlan.Bindings;
        for (int bindingIndex = 0;
             bindingIndex < bindings.Count;
             bindingIndex++)
        {
            TranslatedProgramVertexConstantBinding binding =
                bindings[bindingIndex];
            if (binding.Kind ==
                    TranslatedProgramVertexConstantBindingKind
                        .DynamicSunShadowProjection &&
                !hasSunShadowRuntimeSampler)
            {
                blocker = $"vertexConstantC{binding.Destination}=" +
                    "SUN_SHADOW_RUNTIME_SAMPLER_REQUIRED";
                return false;
            }
            if (binding.Kind ==
                    TranslatedProgramVertexConstantBindingKind
                        .DerivedMatrixRow &&
                binding.CodeMatrixSemantic ==
                    CodeMatrixSemantic.ShadowLookup &&
                !hasSunShadowRuntimeSampler &&
                !hasSpotShadowRuntimeSampler)
            {
                blocker = $"vertexConstantC{binding.Destination}=" +
                    "SHADOW_RUNTIME_SAMPLER_REQUIRED";
                return false;
            }
            if (binding.Kind ==
                TranslatedProgramVertexConstantBindingKind
                    .PerInstanceStaticModelBaseLightingCoords)
            {
                if (sourceKind !=
                    RenderNormalCameraDrawSourceKind.StaticModel)
                {
                    blocker = $"vertexConstantC{binding.Destination}=" +
                        "PER_INSTANCE_ROW_ON_WORLD_DRAW";
                    return false;
                }
                if (!hasModelLightingRuntimeSampler)
                {
                    blocker = $"vertexConstantC{binding.Destination}=" +
                        "MODEL_LIGHTING_RUNTIME_SAMPLER_REQUIRED";
                    return false;
                }
            }
            if (binding.Kind ==
                    TranslatedProgramVertexConstantBindingKind
                        .PerInstanceStaticModelLightProbeAmbient &&
                sourceKind != RenderNormalCameraDrawSourceKind.StaticModel)
            {
                blocker = $"vertexConstantC{binding.Destination}=" +
                    "PER_INSTANCE_ROW_ON_WORLD_DRAW";
                return false;
            }
        }

        for (int patchIndex = 0;
             patchIndex < shader.CodePixelConstantPatchPlans.Length;
             patchIndex++)
        {
            CodePixelConstantPatchPlan patch =
                shader.CodePixelConstantPatchPlans[patchIndex];
            if (!directPlan.IsDynamicSourceRow(patch.CodeIndex))
                continue;
            if ((patch.CodeIndex == FrameDirectCodeConstants
                     .SunShadowSwitchPartitionRowIndex ||
                 patch.CodeIndex == FrameDirectCodeConstants
                     .SunShadowMapScaleRowIndex) &&
                !hasSunShadowRuntimeSampler)
            {
                blocker = $"codePixelRow0x{patch.CodeIndex:X2}=" +
                    "SUN_SHADOW_RUNTIME_SAMPLER_REQUIRED";
                return false;
            }
        }
        blocker = string.Empty;
        return true;
    }

    private MapRenderWorldEvent20SceneLightFrameInput? CreateSceneLightFrame(
        MapRenderScene scene)
    {
        if (scene.WorldSource is not { } source ||
            source.SceneLights.Source is not { } lightSource)
        {
            return null;
        }
        int lightCount = lightSource.SelectorState.SceneLightCount;
        long revision = source.AssetPoolRevisionAtConstruction;
        var allocation = MapRenderSceneLightShadowAllocationState
            .CreateAllClear(
                lightCount,
                "METAL_NORMAL_CAMERA_EXPLICIT_ALL_CLEAR_ALLOCATION",
                revision);
        var dynamicInput = new MapRenderNormalCameraSceneLightDynamicInput(
            FrameDirectCodeConstants.DefaultDiffuseColorScale,
            FrameDirectCodeConstants.DefaultSpecularColorScale,
            Vector2.One,
            allocation,
            "METAL_NORMAL_CAMERA_DEFAULT_LIGHT_SCALES_HERO_LIGHTING_FALSE",
            revision);
        return MapRenderWorldEvent20SceneLightFrameInputProducer.Build(
            source,
            dynamicInput,
            Vector3.Zero,
            scene.SceneLightAttenuationTextures).Input;
    }

    private float ResolveAnimationTimeSeconds()
    {
        if (_normalCameraAnimationStartTimestamp == 0)
            return 0f;
        return (float)Stopwatch.GetElapsedTime(
            _normalCameraAnimationStartTimestamp,
            Stopwatch.GetTimestamp()).TotalSeconds;
    }

    /// <summary>
    /// Produces the camera-derived values once for the current command-buffer
    /// ring slot. Auxiliary passes call this same seam before color encoding,
    /// ensuring depth and color observe identical vegetation time and matrix
    /// rows rather than sampling the clock independently.
    /// </summary>
    internal MetalNormalCameraFrameState PrepareNormalCameraFrameState(
        RenderCamera camera)
    {
        if (_normalCameraPreparedFrameIndex == _frameIndex)
            return _normalCameraFrameState;
        int frameSlot = checked((int)(_frameIndex % FrameBufferCount));
        MTLBuffer frameBuffer = _normalCameraFrameBuffers[frameSlot];
        if (frameBuffer.NativePtr == 0)
        {
            throw new InvalidOperationException(
                "Metal normal-camera constants are unavailable.");
        }

        float aspectRatio =
            (float)_surfaceExtents.SceneTarget.Width /
            _surfaceExtents.SceneTarget.Height;
        RenderNormalCameraMatrixCalculator.CalculatePs3Native(
            camera,
            aspectRatio,
            out Matrix4x4 view,
            out Matrix4x4 projection,
            out _,
            out Vector3 eyeOffset);
        DerivedMatrixState matrices =
            DerivedMatrixResolver.CreateFromPs3NativeCamera(
                view,
                projection,
                eyeOffset);
        float animationTimeSeconds = ResolveAnimationTimeSeconds();
        ClipSpaceLookupCodeConstants clipSpace =
            FrameDirectCodeConstants.ProduceClipSpaceLookup(
                _surfaceExtents.SceneTarget.Width,
                _surfaceExtents.SceneTarget.Height,
                viewportX: 0,
                viewportY: 0,
                _surfaceExtents.SceneTarget.Width,
                _surfaceExtents.SceneTarget.Height);
        ShaderConstantValue zNear =
            FrameDirectCodeConstants.ProduceZNearValue(camera.NearPlane);
        WriteFrameConstants(
            frameBuffer,
            matrices,
            animationTimeSeconds,
            clipSpace.Scale,
            clipSpace.Offset,
            zNear);
        PrepareGenericMaterialFrameState(
            matrices,
            animationTimeSeconds);

        _normalCameraFrameState = new MetalNormalCameraFrameState(
            frameBuffer,
            matrices,
            animationTimeSeconds,
            clipSpace.Scale,
            clipSpace.Offset,
            zNear,
            checked((ulong)_normalCameraFrameConstantsOffset));
        _normalCameraPreparedFrameIndex = _frameIndex;
        return _normalCameraFrameState;
    }

    internal void ResetNormalCameraFrameState()
    {
        _normalCameraFrameStateRevision = checked(
            _normalCameraFrameStateRevision + 1);
        _normalCameraPreparedFrameIndex = -1;
        _normalCameraFrameState = default;
    }

    internal bool TryGetNormalCameraRsxVertexInput(
        RenderNormalCameraPreparedPassSnapshot pass,
        out MTLBuffer buffer,
        out ulong offset)
    {
        if (_normalCameraPasses.TryGetValue(
                pass,
                out MetalPreparedNormalCameraPass? runtime) &&
            _normalCameraImmutableBuffer.NativePtr != 0)
        {
            buffer = _normalCameraImmutableBuffer;
            offset = checked((ulong)runtime.RsxVertexInputOffset);
            return true;
        }
        buffer = default;
        offset = 0;
        return false;
    }

    private MTLBuffer CreateSharedBuffer(int byteCount)
    {
        if (byteCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(byteCount));
        MTLBuffer buffer = _surface.Device.NewBuffer(
            checked((ulong)byteCount),
            MTLResourceOptions.ResourceStorageModeShared |
            MTLResourceOptions.ResourceCPUCacheModeWriteCombined);
        if (buffer.NativePtr == 0 || buffer.Contents == 0)
        {
            if (buffer.NativePtr != 0)
                buffer.Dispose();
            throw new InvalidOperationException(
                $"Metal failed to allocate a {byteCount}-byte shared normal-camera buffer.");
        }
        return buffer;
    }

    private static Span<byte> BufferBytes(
        MTLBuffer buffer,
        int byteOffset,
        int byteCount)
    {
        if (buffer.NativePtr == 0 || buffer.Contents == 0)
            throw new ArgumentException("A mapped Metal buffer is required.", nameof(buffer));
        if (byteOffset < 0 || byteCount < 0 ||
            (long)byteOffset + byteCount > (long)buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(byteOffset));
        }
        return new Span<byte>(
            (void*)(buffer.Contents + byteOffset),
            byteCount);
    }

    private static Span<float> BufferFloats(
        MTLBuffer buffer,
        int byteOffset,
        int floatCount) => new(
            (void*)(buffer.Contents + byteOffset),
            floatCount);

    private static Span<Vector4> BufferVectors(
        MTLBuffer buffer,
        int byteOffset,
        int vectorCount) => new(
            (void*)(buffer.Contents + byteOffset),
            vectorCount);

    private static int Align(int value) => checked(
        (value + ConstantBufferAlignment - 1) &
        ~(ConstantBufferAlignment - 1));

    private static Vector4 ToVector4(ShaderConstantValue value) =>
        new(value.X, value.Y, value.Z, value.W);

    private static bool IsPlacementMatrix(
        CodeMatrixSemantic? semantic) => semantic is
        CodeMatrixSemantic.World0 or
        CodeMatrixSemantic.WorldView0 or
        CodeMatrixSemantic.WorldViewProjection0;

    private static long TriangleCount(
        MTLPrimitiveType primitiveType,
        int indexCount,
        int instanceCount) => primitiveType switch
    {
        MTLPrimitiveType.Triangle => checked(
            (long)(indexCount / 3) * instanceCount),
        MTLPrimitiveType.TriangleStrip => checked(
            (long)Math.Max(0, indexCount - 2) * instanceCount),
        _ => 0
    };

    private static TranslatedProgramVertexConstantBinding[]
        CacheDynamicVertexConstantBindings(
            TranslatedProgramVertexConstantBindingPlan plan,
            bool usesStaticModelInstancing)
    {
        IReadOnlyList<TranslatedProgramVertexConstantBinding> bindings =
            plan.Bindings;
        var dynamicBindings = new List<
            TranslatedProgramVertexConstantBinding>(bindings.Count);
        for (int bindingIndex = 0;
             bindingIndex < bindings.Count;
             bindingIndex++)
        {
            TranslatedProgramVertexConstantBinding binding =
                bindings[bindingIndex];
            if (binding.Kind is
                    TranslatedProgramVertexConstantBindingKind.StaticValue or
                    TranslatedProgramVertexConstantBindingKind
                        .PerInstanceStaticModelBaseLightingCoords or
                    TranslatedProgramVertexConstantBindingKind
                        .PerInstanceStaticModelLightProbeAmbient ||
                usesStaticModelInstancing &&
                binding.Kind ==
                    TranslatedProgramVertexConstantBindingKind
                        .DerivedMatrixRow &&
                IsPlacementMatrix(binding.CodeMatrixSemantic))
            {
                continue;
            }
            dynamicBindings.Add(binding);
        }
        return dynamicBindings.ToArray();
    }

    private static CodePixelConstantPatchPlan[]
        CacheDynamicCodePixelConstantPatches(
            RenderNormalCameraPreparedPassSnapshot source,
            TranslatedProgramDirectCodeConstantPlan directCodePlan)
    {
        ImmutableArray<CodePixelConstantPatchPlan> patches =
            source.ShaderProvenance.CodePixelConstantPatchPlans;
        var dynamicPatches = new List<CodePixelConstantPatchPlan>(
            patches.Length);
        for (int patchIndex = 0;
             patchIndex < patches.Length;
             patchIndex++)
        {
            CodePixelConstantPatchPlan patch = patches[patchIndex];
            if (directCodePlan.IsDynamicSourceRow(patch.CodeIndex))
                dynamicPatches.Add(patch);
        }
        return dynamicPatches.ToArray();
    }

    private readonly record struct MetalEncoderBufferBinding(
        nint Buffer,
        ulong Offset);

    /// <summary>
    /// Tracks only bindings issued through one normal-camera encoder. A new
    /// instance is stack-allocated for every encoder, because Metal binding
    /// state is not transferable between the depth, color, shadow, and
    /// presentation encoders even when they share one command buffer.
    /// </summary>
    private ref struct MetalNormalCameraEncoderBindingShadow
    {
        internal const int VertexBufferSlotCount =
            MetalRsxShaderAbi.StaticCompositionBufferIndex + 1;
        internal const int FragmentBufferSlotCount =
            MetalRsxShaderAbi.FragmentDepthBiasBufferIndex + 1;
        internal const int FragmentTextureSlotCount =
            MetalRsxShaderAbi.TextureDestinationCount;

        private readonly MTLRenderCommandEncoder _encoder;
        private readonly MapRenderFrameTelemetry _telemetry;
        private readonly Span<MetalEncoderBufferBinding> _vertexBuffers;
        private readonly Span<MetalEncoderBufferBinding> _fragmentBuffers;
        private readonly Span<nint> _fragmentTextures;
        private readonly Span<nint> _fragmentSamplers;
        private bool _hasInlineVertexBytes;
        private int _inlineVertexBytesSlot;
        private Vector4 _inlineVertexBytes0;
        private Vector4 _inlineVertexBytes1;
        private bool _hasInlineFragmentBytes;
        private int _inlineFragmentBytesSlot;
        private Vector2 _inlineFragmentBytes;

        internal MetalNormalCameraEncoderBindingShadow(
            MTLRenderCommandEncoder encoder,
            MapRenderFrameTelemetry telemetry,
            Span<MetalEncoderBufferBinding> vertexBuffers,
            Span<MetalEncoderBufferBinding> fragmentBuffers,
            Span<nint> fragmentTextures,
            Span<nint> fragmentSamplers)
        {
            if (encoder.NativePtr == 0)
            {
                throw new ArgumentException(
                    "A Metal render encoder is required.",
                    nameof(encoder));
            }
            ArgumentNullException.ThrowIfNull(telemetry);
            if (vertexBuffers.Length != VertexBufferSlotCount ||
                fragmentBuffers.Length != FragmentBufferSlotCount ||
                fragmentTextures.Length != FragmentTextureSlotCount ||
                fragmentSamplers.Length != FragmentTextureSlotCount)
            {
                throw new ArgumentException(
                    "Metal encoder binding-shadow storage has the wrong ABI cardinality.");
            }

            _encoder = encoder;
            _telemetry = telemetry;
            _vertexBuffers = vertexBuffers;
            _fragmentBuffers = fragmentBuffers;
            _fragmentTextures = fragmentTextures;
            _fragmentSamplers = fragmentSamplers;
            _hasInlineVertexBytes = false;
            _inlineVertexBytesSlot = -1;
            _inlineVertexBytes0 = default;
            _inlineVertexBytes1 = default;
            _hasInlineFragmentBytes = false;
            _inlineFragmentBytesSlot = -1;
            _inlineFragmentBytes = default;
            _vertexBuffers.Clear();
            _fragmentBuffers.Clear();
            _fragmentTextures.Clear();
            _fragmentSamplers.Clear();
        }

        internal bool SetVertexBuffer(
            MTLBuffer buffer,
            ulong offset,
            ulong slot) => SetBuffer(
                buffer,
                offset,
                slot,
                _vertexBuffers,
                fragmentStage: false);

        internal bool SetFragmentBuffer(
            MTLBuffer buffer,
            ulong offset,
            ulong slot) => SetBuffer(
                buffer,
                offset,
                slot,
                _fragmentBuffers,
                fragmentStage: true);

        internal bool SetFragmentBytes(Vector2 value, ulong slot)
        {
            int index = RequireSlot(slot, _fragmentBuffers.Length);
            if (_hasInlineFragmentBytes &&
                _inlineFragmentBytesSlot == index &&
                _inlineFragmentBytes == value)
            {
                RecordElision();
                return false;
            }

            _encoder.SetFragmentBytes(
                (nint)(&value),
                checked((ulong)sizeof(Vector2)),
                slot);
            _fragmentBuffers[index] = default;
            _hasInlineFragmentBytes = true;
            _inlineFragmentBytesSlot = index;
            _inlineFragmentBytes = value;
            _telemetry.AddCounter(MapRenderFrameCounter.BufferChanges);
            return true;
        }

        internal bool SetVertexBytes(
            Vector4 value0,
            Vector4 value1,
            ulong slot)
        {
            int index = RequireSlot(slot, _vertexBuffers.Length);
            if (_hasInlineVertexBytes &&
                _inlineVertexBytesSlot == index &&
                _inlineVertexBytes0 == value0 &&
                _inlineVertexBytes1 == value1)
            {
                RecordElision();
                return false;
            }

            Vector4* values = stackalloc Vector4[2];
            values[0] = value0;
            values[1] = value1;
            _encoder.SetVertexBytes(
                (nint)values,
                checked((ulong)(2 * sizeof(Vector4))),
                slot);
            _vertexBuffers[index] = default;
            _hasInlineVertexBytes = true;
            _inlineVertexBytesSlot = index;
            _inlineVertexBytes0 = value0;
            _inlineVertexBytes1 = value1;
            _telemetry.AddCounter(MapRenderFrameCounter.BufferChanges);
            return true;
        }

        internal bool SetFragmentTexture(
            MTLTexture texture,
            ulong slot)
        {
            if (texture.NativePtr == 0)
            {
                throw new InvalidOperationException(
                    $"Metal fragment texture slot {slot} cannot bind nil.");
            }
            int index = RequireSlot(slot, _fragmentTextures.Length);
            if (_fragmentTextures[index] == texture.NativePtr)
            {
                RecordElision();
                return false;
            }
            _encoder.SetFragmentTexture(texture, slot);
            _fragmentTextures[index] = texture.NativePtr;
            _telemetry.AddCounter(MapRenderFrameCounter.TextureChanges);
            return true;
        }

        internal bool SetFragmentSampler(
            MTLSamplerState sampler,
            ulong slot)
        {
            if (sampler.NativePtr == 0)
            {
                throw new InvalidOperationException(
                    $"Metal fragment sampler slot {slot} cannot bind nil.");
            }
            int index = RequireSlot(slot, _fragmentSamplers.Length);
            if (_fragmentSamplers[index] == sampler.NativePtr)
            {
                RecordElision();
                return false;
            }
            _encoder.SetFragmentSamplerState(sampler, slot);
            _fragmentSamplers[index] = sampler.NativePtr;
            _telemetry.AddCounter(MapRenderFrameCounter.SamplerChanges);
            return true;
        }

        private bool SetBuffer(
            MTLBuffer buffer,
            ulong offset,
            ulong slot,
            Span<MetalEncoderBufferBinding> currentBindings,
            bool fragmentStage)
        {
            if (buffer.NativePtr == 0)
            {
                throw new InvalidOperationException(
                    $"Metal {(fragmentStage ? "fragment" : "vertex")} buffer slot {slot} cannot bind nil.");
            }
            int index = RequireSlot(slot, currentBindings.Length);
            var requested = new MetalEncoderBufferBinding(
                buffer.NativePtr,
                offset);
            if (currentBindings[index] == requested)
            {
                RecordElision();
                return false;
            }
            if (fragmentStage)
                _encoder.SetFragmentBuffer(buffer, offset, slot);
            else
                _encoder.SetVertexBuffer(buffer, offset, slot);
            currentBindings[index] = requested;
            if (!fragmentStage &&
                _hasInlineVertexBytes &&
                _inlineVertexBytesSlot == index)
            {
                _hasInlineVertexBytes = false;
                _inlineVertexBytesSlot = -1;
            }
            if (fragmentStage &&
                _hasInlineFragmentBytes &&
                _inlineFragmentBytesSlot == index)
            {
                _hasInlineFragmentBytes = false;
                _inlineFragmentBytesSlot = -1;
            }
            _telemetry.AddCounter(MapRenderFrameCounter.BufferChanges);
            return true;
        }

        private void RecordElision() => _telemetry.AddCounter(
            MapRenderFrameCounter.StateShadowElidedCalls);

        private static int RequireSlot(ulong slot, int count)
        {
            if (slot >= checked((ulong)count))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(slot),
                    slot,
                    "Metal binding slot is outside the renderer ABI.");
            }
            return checked((int)slot);
        }
    }

    private sealed class MetalPreparedNormalCameraPass
    {
        private readonly MetalProgramPipeline? _authoredPipeline;
        private readonly TranslatedProgramDirectCodeConstantPlan?
            _directCodePlan;
        private readonly TranslatedProgramVertexConstantBindingPlan?
            _vertexConstantPlan;

        internal MetalPreparedNormalCameraPass(
            RenderNormalCameraPreparedPassSnapshot source,
            MetalProgramPipeline pipeline,
            TranslatedProgramDirectCodeConstantPlan directCodePlan,
            TranslatedProgramVertexConstantBindingPlan vertexConstantPlan,
            MetalGeometryResource geometry,
            MetalInstanceResource? instances,
            MapRenderStaticInstanceLightingPayload lightingPayload,
            bool needsOwnedInstanceData,
            int staticPixelRowCount,
            MetalNormalCameraTextureBinding[] textureBindings,
            MetalNormalCameraRuntimeSamplerBinding[] runtimeSamplerBindings)
        {
            Source = source;
            _authoredPipeline = pipeline;
            _directCodePlan = directCodePlan;
            _vertexConstantPlan = vertexConstantPlan;
            Geometry = geometry;
            Instances = instances;
            LightingPayload = lightingPayload;
            NeedsOwnedInstanceData = needsOwnedInstanceData;
            StaticPixelRowCount = staticPixelRowCount;
            TextureBindings = textureBindings;
            RuntimeSamplerBindings = runtimeSamplerBindings;
            RequiresSunShadowReceiver = HasRuntimeSampler(
                runtimeSamplerBindings,
                ShaderRuntimeSamplerResourceKind.SunShadowAtlas);
            RequiresSpotShadowReceiver = HasRuntimeSampler(
                runtimeSamplerBindings,
                ShaderRuntimeSamplerResourceKind.SpotShadowAtlas);
            DynamicVertexConstantBindings =
                CacheDynamicVertexConstantBindings(
                    vertexConstantPlan,
                    pipeline.UsesStaticModelInstancing);
            DynamicCodePixelConstantPatches =
                CacheDynamicCodePixelConstantPatches(
                    source,
                    directCodePlan);
        }

        internal MetalPreparedNormalCameraPass(
            RenderNormalCameraPreparedPassSnapshot source,
            MetalGenericMaterialDraw genericMaterial,
            MetalGeometryResource geometry,
            MetalInstanceResource? instances,
            MapRenderStaticInstanceLightingPayload lightingPayload,
            bool needsOwnedInstanceData)
        {
            Source = source;
            GenericMaterial = genericMaterial;
            Geometry = geometry;
            Instances = instances;
            LightingPayload = lightingPayload;
            NeedsOwnedInstanceData = needsOwnedInstanceData;
            StaticPixelRowCount = 0;
            TextureBindings = [];
            RuntimeSamplerBindings = [];
            DynamicVertexConstantBindings = [];
            DynamicCodePixelConstantPatches = [];
        }

        internal RenderNormalCameraPreparedPassSnapshot Source { get; }
        internal MetalProgramPipeline Pipeline => _authoredPipeline ??
            throw new InvalidOperationException(
                "A generic material pass has no authored RSX pipeline.");
        internal TranslatedProgramDirectCodeConstantPlan DirectCodePlan
            => _directCodePlan ?? throw new InvalidOperationException(
                "A generic material pass has no authored direct-code plan.");
        internal TranslatedProgramVertexConstantBindingPlan VertexConstantPlan
            => _vertexConstantPlan ?? throw new InvalidOperationException(
                "A generic material pass has no authored vertex-constant plan.");
        internal MetalGenericMaterialDraw? GenericMaterial { get; }
        internal MTLRenderPipelineState PipelineState =>
            GenericMaterial?.Pipeline.State ?? Pipeline.State;
        internal bool UsesStaticModelInstancing =>
            GenericMaterial?.Pipeline.UsesStaticModelInstancing ??
            Pipeline.UsesStaticModelInstancing;
        internal MetalGeometryResource Geometry { get; }
        internal MetalInstanceResource? Instances { get; }
        internal MapRenderStaticInstanceLightingPayload LightingPayload
            { get; }
        internal bool NeedsOwnedInstanceData { get; }
        internal bool NeedsImmutableOwnedInstanceData =>
            NeedsOwnedInstanceData &&
            LightingPayload !=
                MapRenderStaticInstanceLightingPayload.BaseLightingCoords;
        internal int StaticPixelRowCount { get; }
        internal MetalNormalCameraTextureBinding[] TextureBindings { get; }
        internal MetalNormalCameraRuntimeSamplerBinding[]
            RuntimeSamplerBindings { get; }
        internal TranslatedProgramVertexConstantBinding[]
            DynamicVertexConstantBindings { get; }
        internal CodePixelConstantPatchPlan[]
            DynamicCodePixelConstantPatches { get; }
        internal bool RequiresSunShadowReceiver { get; }
        internal bool RequiresSpotShadowReceiver { get; }
        internal bool RequiresImmutableBuffer =>
            NeedsImmutableOwnedInstanceData ||
            Source.RsxVertexInputs.Length != 0;
        internal int RsxVertexInputOffset { get; set; }
        internal int OwnedInstanceOffset { get; set; }
        internal int StaticModelLightingInstanceOffset { get; set; }
        internal int VertexConstantsOffset { get; set; }
        internal int CodePixelConstantsOffset { get; set; }
        internal int StaticPixelConstantsOffset { get; set; }
        internal int StaticCompositionOffset { get; set; }
        internal long LastConstantsFrameIndex { get; set; } = -1;
        internal long LastConstantsFrameStateRevision { get; set; } = -1;
    }

    private readonly record struct MetalNormalCameraTextureBinding(
        ulong Destination,
        MTLTexture Texture,
        MTLSamplerState Sampler);

    private readonly record struct MetalNormalCameraRuntimeSamplerBinding(
        ulong Destination,
        ShaderRuntimeSamplerResourceKind ResourceKind);

    private readonly record struct MetalNormalCameraLightAttenuationIdentity(
        RenderSemanticIdentity Texture,
        RenderSemanticIdentity Sampler);

    private readonly record struct MetalNormalCameraLightAttenuationBinding(
        MTLTexture Texture,
        MTLSamplerState Sampler);

    private readonly record struct MetalNormalCameraAdmissionTelemetry(
        int SnapshotBaseGroups,
        int SnapshotReceiverGroups,
        int SnapshotBasePasses,
        int SnapshotReceiverPasses,
        int AuthorizedBaseGroups,
        int AuthorizedReceiverGroups,
        int AuthorizedBasePasses,
        int AuthorizedReceiverPasses,
        int BlockedGroups,
        int BlockedPasses,
        int BlockedGenericPasses,
        int BlockedRuntimeSamplerPasses,
        int BlockedUnresolvedSamplerPasses,
        int BlockedSunShadowPasses,
        int BlockedSpotShadowPasses,
        int BlockedModelLightingPasses,
        int BlockedProcessedFloatZPasses,
        int BlockedLightAttenuationPasses,
        int BlockedShaderPasses,
        int BlockedConstantPasses,
        int BlockedRenderStatePasses,
        int BlockedResourcePasses,
        int BlockedOtherPasses);
}

internal readonly record struct MetalNormalCameraFrameState(
    MTLBuffer ConstantBuffer,
    DerivedMatrixState Matrices,
    float AnimationTimeSeconds,
    ShaderConstantValue ClipSpaceScale,
    ShaderConstantValue ClipSpaceOffset,
    ShaderConstantValue ZNear,
    ulong FrameConstantOffset);
