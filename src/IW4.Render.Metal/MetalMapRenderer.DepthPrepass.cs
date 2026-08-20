using System.Numerics;
using System.Runtime.Versioning;

using IW4.Render.Diagnostics;
using IW4.Render.EditorPreview;
using IW4.Render.Execution;
using IW4.Render.Metal.Pipelines;
using IW4.Render.Metal.Resources;
using IW4.Render.Metal.Shaders;
using IW4.Render.Resources;
using IW4.Render.Scheduling.FramePlans;
using IW4.Render.Shaders;
using IW4.Render.Techniques;

using SharpMetal.Foundation;
using SharpMetal.Metal;

namespace IW4.Render.Metal;

[SupportedOSPlatform("macos")]
public sealed unsafe partial class MetalMapRenderer
{
    private static readonly TranslatedProgramDirectCodeConstantPlan
        EmptyDepthDirectCodePlan = new(
            "METAL_STANDARD_DEPTH_PREPASS_NO_DIRECT_CODE_ROWS",
            Array.Empty<DirectCodeConstantRow>());

    private readonly MTLBuffer[] _depthPrepassConstantBuffers =
        new MTLBuffer[FrameBufferCount];
    private MetalDepthPrepassPipelineCache? _depthPrepassPipelines;
    private MetalDepthPrepassGroup[] _orderedDepthPrepassGroups = [];
    private MetalDepthVertexConstantSlot[] _depthPrepassConstantSlots = [];
    private int _depthPrepassConstantBufferByteCount;

    partial void CreateDepthPrepassResources(
        MapRenderScene scene,
        RenderSceneSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(snapshot);
        using var pool = new NSAutoreleasePool();

        _depthPrepassPipelines = new MetalDepthPrepassPipelineCache(
            _surface.Device,
            _depthStencilFormat);
        try
        {
            var admitted = new List<MetalDepthPrepassGroup>();
            foreach (MapRenderEditorDrawGroup<
                         RenderNormalCameraDrawSubmissionSnapshot> group in
                     snapshot.NormalCameraDraws.DrawGroups)
            {
                if (!_normalCameraAuthorizedGroups.Contains(group) ||
                    !TrySelectStandardDepthPrepass(
                        group,
                        out MapRenderEditorDepthPrepassPlan? plan))
                {
                    continue;
                }

                ReadOnlySpan<RenderNormalCameraDrawSubmissionSnapshot>
                    authoredPasses = group.AuthoredPassSpan;
                var draws = new MetalPreparedDepthDraw[
                    authoredPasses.Length];
                bool groupReady = true;
                for (int passIndex = 0;
                     passIndex < authoredPasses.Length;
                     passIndex++)
                {
                    RenderNormalCameraDrawSubmissionSnapshot submission =
                        authoredPasses[passIndex];
                    if (!TryPrepareDepthDraw(
                            submission,
                            plan!,
                            out MetalPreparedDepthDraw? draw,
                            out _))
                    {
                        groupReady = false;
                        break;
                    }
                    draws[passIndex] = draw!;
                }
                if (!groupReady)
                    continue;

                var runtime = new MetalDepthPrepassGroup(
                    group,
                    plan!,
                    draws);
                admitted.Add(runtime);
            }

            AssignDepthConstantSlots(admitted);
            CreateDepthConstantBuffers();

            // Inherit consumes the polygon offset left by the preceding draw.
            // Once one admitted row uses it, the entire filtered source stream
            // remains unsorted and every authored pass stays contiguous.
            bool preservesSourceOrder = admitted.Any(group =>
                group.Plan.State.PolygonOffsetMode ==
                    RenderPolygonOffsetMode.Inherit);
            _orderedDepthPrepassGroups = preservesSourceOrder
                ? admitted.ToArray()
                : admitted
                    .OrderBy(
                        group => group.SortIdentity,
                        StringComparer.Ordinal)
                    .ThenBy(group => group.Source.SourceOrdinal)
                    .ToArray();
        }
        catch
        {
            DeleteDepthPrepassResources();
            throw;
        }
    }

    partial void DeleteDepthPrepassResources()
    {
        for (int index = 0;
             index < _depthPrepassConstantBuffers.Length;
             index++)
        {
            if (_depthPrepassConstantBuffers[index].NativePtr == 0)
                continue;
            _depthPrepassConstantBuffers[index].Dispose();
            _depthPrepassConstantBuffers[index] = default;
        }
        _depthPrepassPipelines?.Dispose();
        _depthPrepassPipelines = null;
        _orderedDepthPrepassGroups = [];
        _depthPrepassConstantSlots = [];
        _depthPrepassConstantBufferByteCount = 0;
    }

    partial void EncodeNormalCameraDepthPrepass(
        MTLRenderCommandEncoder encoder,
        RenderCamera camera)
    {
        if (_orderedDepthPrepassGroups.Length == 0)
            return;
        if (encoder.NativePtr == 0)
        {
            throw new ArgumentException(
                "A Metal render encoder is required.",
                nameof(encoder));
        }

        using MapRenderCpuPhaseScope cpuPhase =
            _telemetry.BeginCpuPhase(MapRenderCpuPhase.DepthPrepass);
        MetalNormalCameraFrameState frameState =
            PrepareNormalCameraFrameState(camera);
        MTLBuffer depthConstants = PrepareDepthConstants(
            frameState.Matrices);

        nint currentPipeline = 0;
        RenderState currentState = MetalRenderStateCache.Effective(
            RenderState.Default);
        long drawCalls = 0;
        long triangles = 0;

        // The native normal-camera command stream begins with polygon offset
        // disabled. This also gives a first-row Inherit action an exact,
        // deterministic predecessor.
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

        for (int groupIndex = 0;
             groupIndex < _orderedDepthPrepassGroups.Length;
             groupIndex++)
        {
            MetalDepthPrepassGroup group =
                _orderedDepthPrepassGroups[groupIndex];
            if (!IsNormalCameraGroupSelected(group.Source))
                continue;
            int visibleRunCount =
                PrepareNormalCameraVisibleRuns(
                    group.Source,
                    out MetalNormalCameraVisibilityGroupPlan?
                        visibilityPlan,
                    out _);
            if (visibleRunCount == 0)
                continue;

            // Admission guaranteed one runtime for every authored row. Replay
            // the complete array in source order for every selected instance
            // run; a multipass owner is not a license to collapse equivalent
            // depth writes.
            for (int drawIndex = 0;
                 drawIndex < group.Draws.Length;
                 drawIndex++)
            {
                MetalPreparedDepthDraw draw = group.Draws[drawIndex];
                int drawVisibleRunCount = ResolveVisibleRunCount(
                    visibilityPlan,
                    drawIndex,
                    visibleRunCount);
                if (drawVisibleRunCount == 0)
                    continue;
                if (draw.Pipeline.State.NativePtr != currentPipeline)
                {
                    encoder.SetRenderPipelineState(draw.Pipeline.State);
                    currentPipeline = draw.Pipeline.State.NativePtr;
                    _telemetry.AddCounter(
                        MapRenderFrameCounter.ProgramChanges);
                }
                else
                {
                    _telemetry.AddCounter(
                        MapRenderFrameCounter.StateShadowElidedCalls);
                }
                RenderState effectiveState =
                    MetalRenderStateCache.Effective(group.Plan.State);
                if (currentState != effectiveState)
                {
                    _renderStates.ApplyRasterState(
                        encoder,
                        group.Plan.State);
                    currentState = effectiveState;
                    _telemetry.AddCounter(
                        MapRenderFrameCounter.RenderStateChanges);
                }
                else
                {
                    _telemetry.AddCounter(
                        MapRenderFrameCounter.StateShadowElidedCalls);
                }

                BindDepthResources(
                    ref bindingShadow,
                    depthConstants,
                    frameState,
                    draw);
                for (int runIndex = 0;
                     runIndex < drawVisibleRunCount;
                     runIndex++)
                {
                    RenderDrawRange visibleRange =
                        ApplyVisibleRun(
                            draw.Submission.Range,
                            visibilityPlan,
                            drawIndex,
                            runIndex);
                    IssueDepthDraw(encoder, draw, visibleRange);

                    long drawTriangles = TriangleCount(
                        draw.Geometry.PrimitiveType,
                        visibleRange.IndexCount,
                        visibleRange.InstanceCount);
                    drawCalls++;
                    triangles = checked(triangles + drawTriangles);
                    _telemetry.AddCounter(
                        MapRenderFrameCounter.DrawCalls);
                    _telemetry.AddCounter(
                        MapRenderFrameCounter.LogicalDrawCommands);
                    _telemetry.AddCounter(
                        MapRenderFrameCounter.Triangles,
                        drawTriangles);
                }
            }
        }

        if (drawCalls != 0)
            _telemetry.AddCounter(MapRenderFrameCounter.Passes);
        _telemetry.AddGpuPhaseWork(
            MapRenderGpuPhase.DepthPrepass,
            drawCalls,
            triangles);
    }

    private bool TryPrepareDepthDraw(
        RenderNormalCameraDrawSubmissionSnapshot submission,
        MapRenderEditorDepthPrepassPlan plan,
        out MetalPreparedDepthDraw? runtime,
        out string blocker)
    {
        runtime = null;
        blocker = string.Empty;
        RenderNormalCameraPreparedPassSnapshot pass =
            submission.PreparedPass;
        if (pass.DepthPrepassShaderProvenance is not { } shader)
        {
            blocker = "depthShader=PROVENANCE_MISSING";
            return false;
        }
        if (shader.Purpose != ShaderExecutionPurpose.DepthOnly ||
            !HasCompatibleDepthVertexInputs(
                pass.ShaderProvenance.VertexInputs,
                shader.VertexInputs))
        {
            blocker = "depthShader=VERTEX_INPUT_ROUTE_MISMATCH";
            return false;
        }
        if (!TryGetNormalCameraRsxVertexInput(
                pass,
                out MTLBuffer rsxVertexInputs,
                out ulong rsxVertexInputOffset))
        {
            blocker = "depthShader=EXACT_RSX_INPUT_SLAB_UNAVAILABLE";
            return false;
        }

        TranslatedProgramVertexConstantBindingPlanBuildResult
            constantResult =
                TranslatedProgramVertexConstantBindingPlanner.TryPlan(
                    shader.ProgramVertexConstantDestinations,
                    shader.ConstantDestinations,
                    shader.EmbeddedVertexConstants,
                    EmptyDepthDirectCodePlan);
        if (!constantResult.IsReady || constantResult.Plan is null)
        {
            blocker = $"depthConstants={string.Join('|', constantResult.Blockers)}";
            return false;
        }
        if (!SupportsDepthVertexConstants(
                constantResult.Plan,
                out blocker))
        {
            return false;
        }

        try
        {
            _ = _renderStates.GetOrCreate(plan.State);
        }
        catch (InvalidOperationException exception)
        {
            blocker = $"depthState={exception.Message}";
            return false;
        }
        if (_depthPrepassPipelines is null ||
            !_depthPrepassPipelines.TryGetOrCreate(
                pass,
                plan,
                shader,
                constantResult.Plan,
                out MetalDepthPrepassPipeline? pipeline,
                out blocker))
        {
            return false;
        }

        bool isStatic = pass.SourceKind ==
            RenderNormalCameraDrawSourceKind.StaticModel;
        if (pipeline!.UsesStaticModelInstancing != isStatic)
        {
            blocker = "depthStaticComposition=SOURCE_KIND_MISMATCH";
            return false;
        }

        MetalGeometryResource geometry =
            _resources.RequireGeometry(pass.Geometry.Identity);
        if (geometry.PrimitiveType is not
                MTLPrimitiveType.Triangle and not
                MTLPrimitiveType.TriangleStrip)
        {
            blocker = "depthGeometry=TRIANGLES_REQUIRED";
            return false;
        }

        MetalInstanceResource? instances = null;
        if (isStatic)
        {
            if (pass.Instances is null)
            {
                blocker = "depthInstances=RESOURCE_MISSING";
                return false;
            }
            instances = _resources.RequireInstances(pass.Instances.Identity);
            int expectedStride = checked(
                pipeline.StaticInstanceFloat4Stride * sizeof(float) * 4);
            if (pipeline.StaticInstanceFloat4Stride !=
                    MetalRsxShaderAbi.StaticPlacementFloat4Stride ||
                pipeline.StaticPlacementFloat4Offset != 0 ||
                instances.StrideBytes != expectedStride)
            {
                blocker = "depthInstances=PLACEMENT_LAYOUT_MISMATCH";
                return false;
            }
        }

        runtime = new MetalPreparedDepthDraw(
            submission,
            pipeline,
            constantResult.Plan,
            geometry,
            instances,
            rsxVertexInputs,
            rsxVertexInputOffset);
        return true;
    }

    private static bool TrySelectStandardDepthPrepass(
        MapRenderEditorDrawGroup<
            RenderNormalCameraDrawSubmissionSnapshot> group,
        out MapRenderEditorDepthPrepassPlan? plan)
    {
        ArgumentNullException.ThrowIfNull(group);
        plan = null;
        ReadOnlySpan<RenderNormalCameraDrawSubmissionSnapshot>
            authoredPasses = group.AuthoredPassSpan;
        if (group.Bucket != MapRenderEditorDrawBucket.Opaque ||
            authoredPasses.IsEmpty ||
            authoredPasses[0].PreparedPass.DepthPrepass is not
                { Program: MapRenderEditorDepthPrepassProgram.TransformOnlyNull }
                candidate)
        {
            return false;
        }

        for (int passIndex = 1;
             passIndex < authoredPasses.Length;
             passIndex++)
        {
            if (authoredPasses[passIndex]
                    .PreparedPass.DepthPrepass != candidate)
            {
                return false;
            }
        }

        plan = candidate;
        return true;
    }

    private static bool HasCompatibleDepthVertexInputs(
        IReadOnlyList<ShaderVertexInputBinding> colorInputs,
        IReadOnlyList<ShaderVertexInputBinding> depthInputs)
    {
        for (int depthIndex = 0;
             depthIndex < depthInputs.Count;
             depthIndex++)
        {
            ShaderVertexInputBinding depth = depthInputs[depthIndex];
            int matchCount = 0;
            for (int colorIndex = 0;
                 colorIndex < colorInputs.Count;
                 colorIndex++)
            {
                ShaderVertexInputBinding color = colorInputs[colorIndex];
                if (color.Destination != depth.Destination)
                    continue;
                matchCount++;
                if (color != depth)
                    return false;
            }
            if (matchCount != 1)
                return false;
        }
        return true;
    }

    private static bool SupportsDepthVertexConstants(
        TranslatedProgramVertexConstantBindingPlan plan,
        out string blocker)
    {
        IReadOnlyList<TranslatedProgramVertexConstantBinding> bindings =
            plan.Bindings;
        for (int bindingIndex = 0;
             bindingIndex < bindings.Count;
             bindingIndex++)
        {
            TranslatedProgramVertexConstantBinding binding =
                bindings[bindingIndex];
            if (binding.Kind ==
                    TranslatedProgramVertexConstantBindingKind.StaticValue ||
                binding.Kind ==
                    TranslatedProgramVertexConstantBindingKind
                        .DerivedMatrixRow &&
                binding.CodeMatrixSemantic is not
                    CodeMatrixSemantic.ShadowLookup)
            {
                continue;
            }
            blocker = $"depthVertexConstantC{binding.Destination}=" +
                "UNSUPPORTED_DYNAMIC_OWNER";
            return false;
        }
        blocker = string.Empty;
        return true;
    }

    private void AssignDepthConstantSlots(
        IReadOnlyList<MetalDepthPrepassGroup> groups)
    {
        var slotsByPlan = new Dictionary<
            TranslatedProgramVertexConstantBindingPlan,
            MetalDepthVertexConstantSlot>(
                DepthVertexConstantPlanComparer.Instance);
        var slots = new List<MetalDepthVertexConstantSlot>();
        int cursor = 0;
        for (int groupIndex = 0;
             groupIndex < groups.Count;
             groupIndex++)
        {
            MetalDepthPrepassGroup group = groups[groupIndex];
            for (int drawIndex = 0;
                 drawIndex < group.Draws.Length;
                 drawIndex++)
            {
                MetalPreparedDepthDraw draw = group.Draws[drawIndex];
                if (!slotsByPlan.TryGetValue(
                        draw.VertexConstantPlan,
                        out MetalDepthVertexConstantSlot? slot))
                {
                    slot = new MetalDepthVertexConstantSlot(
                        draw.VertexConstantPlan,
                        cursor);
                    slotsByPlan.Add(draw.VertexConstantPlan, slot);
                    slots.Add(slot);
                    cursor = Align(checked(
                        cursor + VertexConstantByteCount));
                }
                draw.VertexConstantsOffset = slot.Offset;
            }
        }
        _depthPrepassConstantSlots = slots.ToArray();
        _depthPrepassConstantBufferByteCount = cursor;
    }

    private void CreateDepthConstantBuffers()
    {
        if (_depthPrepassConstantBufferByteCount == 0)
            return;
        for (int bufferIndex = 0;
             bufferIndex < _depthPrepassConstantBuffers.Length;
             bufferIndex++)
        {
            MTLBuffer buffer = CreateSharedBuffer(
                _depthPrepassConstantBufferByteCount);
            _depthPrepassConstantBuffers[bufferIndex] = buffer;
            BufferBytes(
                buffer,
                0,
                _depthPrepassConstantBufferByteCount).Clear();
            for (int slotIndex = 0;
                 slotIndex < _depthPrepassConstantSlots.Length;
                 slotIndex++)
            {
                MetalDepthVertexConstantSlot slot =
                    _depthPrepassConstantSlots[slotIndex];
                Span<Vector4> values = BufferVectors(
                    buffer,
                    slot.Offset,
                    RsxVertexConstantLayout.Count);
                IReadOnlyList<TranslatedProgramVertexConstantBinding>
                    bindings = slot.Plan.Bindings;
                for (int bindingIndex = 0;
                     bindingIndex < bindings.Count;
                     bindingIndex++)
                {
                    TranslatedProgramVertexConstantBinding binding =
                        bindings[bindingIndex];
                    if (binding.Kind ==
                            TranslatedProgramVertexConstantBindingKind
                                .StaticValue &&
                        binding.StaticValue is { } value)
                    {
                        values[binding.Destination] = ToVector4(value);
                    }
                }
            }
        }
    }

    private MTLBuffer PrepareDepthConstants(
        in DerivedMatrixState matrices)
    {
        int frameSlot = checked((int)(_frameIndex % FrameBufferCount));
        MTLBuffer buffer = _depthPrepassConstantBuffers[frameSlot];
        if (buffer.NativePtr == 0)
        {
            throw new InvalidOperationException(
                "Metal depth-prepass constants are unavailable.");
        }
        for (int slotIndex = 0;
             slotIndex < _depthPrepassConstantSlots.Length;
             slotIndex++)
        {
            MetalDepthVertexConstantSlot slot =
                _depthPrepassConstantSlots[slotIndex];
            Span<Vector4> values = BufferVectors(
                buffer,
                slot.Offset,
                RsxVertexConstantLayout.Count);
            TranslatedProgramVertexConstantBinding[] dynamicBindings =
                slot.DynamicBindings;
            for (int bindingIndex = 0;
                 bindingIndex < dynamicBindings.Length;
                 bindingIndex++)
            {
                TranslatedProgramVertexConstantBinding binding =
                    dynamicBindings[bindingIndex];
                if (binding.CodeMatrixSemantic is not { } semantic ||
                    !DerivedMatrixResolver.TryResolveRow(
                        matrices,
                        semantic,
                        binding.CodeMatrixTransform,
                        binding.CodeMatrixRow,
                        out Vector4 value))
                {
                    throw new InvalidOperationException(
                        $"Metal depth constant c{binding.Destination} matrix is unavailable.");
                }
                values[binding.Destination] = value;
            }
        }
        return buffer;
    }

    private void BindDepthResources(
        ref MetalNormalCameraEncoderBindingShadow bindings,
        MTLBuffer depthConstants,
        in MetalNormalCameraFrameState frameState,
        MetalPreparedDepthDraw draw)
    {
        bindings.SetVertexBuffer(
            draw.RsxVertexInputs,
            draw.RsxVertexInputOffset,
            MetalRsxShaderAbi.VertexInputBufferIndex);
        int uniformUpdates = bindings.SetVertexBuffer(
            depthConstants,
            checked((ulong)draw.VertexConstantsOffset),
            MetalRsxShaderAbi.VertexConstantBufferIndex)
                ? 1
                : 0;
        if (_depthStencilFormat.EmulatesDepth24 &&
            bindings.SetFragmentBytes(
                _renderStates.CurrentDepthBias,
                MetalDepthPrepassShaderAbi.DepthBiasBufferIndex))
        {
            uniformUpdates++;
        }

        if (!draw.Pipeline.UsesStaticModelInstancing)
        {
            if (uniformUpdates != 0)
            {
                _telemetry.AddCounter(
                    MapRenderFrameCounter.UniformUpdates,
                    uniformUpdates);
            }
            return;
        }
        bindings.SetVertexBuffer(
            draw.Instances!.Buffer,
            draw.Instances.Offset,
            MetalRsxShaderAbi.StaticInstanceBufferIndex);
        if (bindings.SetVertexBuffer(
            frameState.ConstantBuffer,
            frameState.FrameConstantOffset,
            MetalRsxShaderAbi.FrameVertexConstantBufferIndex))
        {
            uniformUpdates++;
        }

        MapRenderEditorVegetationAnimationPlan? vegetation =
            draw.Submission.PreparedPass.VegetationAnimation;
        var compositionParameters = new Vector4(
            vegetation?.IsEnabled == true ? 1f : 0f,
            vegetation?.Amplitude ?? 0f,
            vegetation?.AngularFrequency ?? 0f,
            vegetation?.SpatialFrequency ?? 0f);
        RenderBounds bounds = draw.Submission.PreparedPass.LocalBounds;
        var compositionBounds = new Vector4(
            bounds.Min.Z,
            bounds.Max.Z - bounds.Min.Z,
            0f,
            0f);
        if (bindings.SetVertexBytes(
                compositionParameters,
                compositionBounds,
                MetalRsxShaderAbi.StaticCompositionBufferIndex))
        {
            uniformUpdates++;
        }
        if (uniformUpdates != 0)
        {
            _telemetry.AddCounter(
                MapRenderFrameCounter.UniformUpdates,
                uniformUpdates);
        }
    }

    private static void IssueDepthDraw(
        MTLRenderCommandEncoder encoder,
        MetalPreparedDepthDraw draw,
        RenderDrawRange range)
    {
        int indexByteCount = draw.Geometry.IndexType == MTLIndexType.UInt16
            ? sizeof(ushort)
            : sizeof(uint);
        ulong indexOffset = checked(
            draw.Geometry.IndexOffset +
            (ulong)range.FirstIndex * (ulong)indexByteCount);
        encoder.DrawIndexedPrimitives(
            draw.Geometry.PrimitiveType,
            checked((ulong)range.IndexCount),
            draw.Geometry.IndexType,
            draw.Geometry.Buffer,
            indexOffset,
            checked((ulong)range.InstanceCount),
            range.BaseVertex,
            checked((ulong)range.FirstInstance));
    }

    private sealed class MetalDepthPrepassGroup
    {
        internal MetalDepthPrepassGroup(
            MapRenderEditorDrawGroup<
                RenderNormalCameraDrawSubmissionSnapshot> source,
            MapRenderEditorDepthPrepassPlan plan,
            MetalPreparedDepthDraw[] draws)
        {
            Source = source;
            Plan = plan;
            Draws = draws;
            SortIdentity = string.Join(
                ';',
                draws.Select(draw =>
                    $"{draw.Submission.PreparedPass.SourceKind}:" +
                    $"{draw.Submission.PreparedPass.DepthPrepassShaderProvenance!.ProgramCacheKey}:" +
                    draw.Pipeline.StaticCompositionIdentity));
        }

        internal MapRenderEditorDrawGroup<
            RenderNormalCameraDrawSubmissionSnapshot> Source { get; }
        internal MapRenderEditorDepthPrepassPlan Plan { get; }
        internal MetalPreparedDepthDraw[] Draws { get; }
        internal string SortIdentity { get; }
    }

    private sealed class MetalPreparedDepthDraw
    {
        internal MetalPreparedDepthDraw(
            RenderNormalCameraDrawSubmissionSnapshot submission,
            MetalDepthPrepassPipeline pipeline,
            TranslatedProgramVertexConstantBindingPlan vertexConstantPlan,
            MetalGeometryResource geometry,
            MetalInstanceResource? instances,
            MTLBuffer rsxVertexInputs,
            ulong rsxVertexInputOffset)
        {
            Submission = submission;
            Pipeline = pipeline;
            VertexConstantPlan = vertexConstantPlan;
            Geometry = geometry;
            Instances = instances;
            RsxVertexInputs = rsxVertexInputs;
            RsxVertexInputOffset = rsxVertexInputOffset;
        }

        internal RenderNormalCameraDrawSubmissionSnapshot Submission
            { get; }
        internal MetalDepthPrepassPipeline Pipeline { get; }
        internal TranslatedProgramVertexConstantBindingPlan
            VertexConstantPlan { get; }
        internal MetalGeometryResource Geometry { get; }
        internal MetalInstanceResource? Instances { get; }
        internal MTLBuffer RsxVertexInputs { get; }
        internal ulong RsxVertexInputOffset { get; }
        internal int VertexConstantsOffset { get; set; }
    }

    private sealed class MetalDepthVertexConstantSlot
    {
        internal MetalDepthVertexConstantSlot(
            TranslatedProgramVertexConstantBindingPlan plan,
            int offset)
        {
            Plan = plan;
            Offset = offset;
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
                if (binding.Kind ==
                    TranslatedProgramVertexConstantBindingKind
                        .DerivedMatrixRow)
                {
                    dynamicBindings.Add(binding);
                }
            }
            DynamicBindings = dynamicBindings.ToArray();
        }

        internal TranslatedProgramVertexConstantBindingPlan Plan { get; }
        internal int Offset { get; }
        internal TranslatedProgramVertexConstantBinding[] DynamicBindings
            { get; }
    }

    private sealed class DepthVertexConstantPlanComparer :
        IEqualityComparer<TranslatedProgramVertexConstantBindingPlan>
    {
        internal static DepthVertexConstantPlanComparer Instance { get; } =
            new();

        public bool Equals(
            TranslatedProgramVertexConstantBindingPlan? left,
            TranslatedProgramVertexConstantBindingPlan? right) =>
            ReferenceEquals(left, right) ||
            left is not null &&
            right is not null &&
            BindingPlansEqual(left.Bindings, right.Bindings);

        public int GetHashCode(
            TranslatedProgramVertexConstantBindingPlan value)
        {
            var hash = new HashCode();
            IReadOnlyList<TranslatedProgramVertexConstantBinding> bindings =
                value.Bindings;
            for (int bindingIndex = 0;
                 bindingIndex < bindings.Count;
                 bindingIndex++)
            {
                hash.Add(bindings[bindingIndex]);
            }
            return hash.ToHashCode();
        }

        private static bool BindingPlansEqual(
            IReadOnlyList<TranslatedProgramVertexConstantBinding> left,
            IReadOnlyList<TranslatedProgramVertexConstantBinding> right)
        {
            if (left.Count != right.Count)
                return false;
            for (int bindingIndex = 0;
                 bindingIndex < left.Count;
                 bindingIndex++)
            {
                if (left[bindingIndex] != right[bindingIndex])
                    return false;
            }
            return true;
        }
    }
}
