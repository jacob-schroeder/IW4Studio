using System.Numerics;
using Silk.NET.OpenGL;
using IW4.Render.Diagnostics;
using IW4.Render.EditorPreview;
using IW4.Render.Execution;
using IW4.Render.Geometry;
using IW4.Render.Lighting;
using IW4.Render.Scheduling.Lighting;
using IW4.Render.SceneBuilding;
using IW4.Render.World;
using IW4.Render.Shaders;
using IW4.Render.OpenGl.Diagnostics;
using IW4.Render.OpenGl.FloatZ;
using IW4.Render.OpenGl.Presentation;
using IW4.Render.OpenGl.Programs;
using IW4.Render.OpenGl.Shadows;
using IW4.Render.OpenGl.World;

namespace IW4.Render.OpenGl;

public sealed unsafe partial class SilkOpenGlMapRenderer
{
    private void Draw(GlMesh mesh, PrimitiveType primitiveType)
    {
        if (mesh.IndexCount == 0)
            return;

        _state.BindVertexArray(mesh.VertexArray);
        RecordDraw(mesh.IndexCount, instanceCount: 1, primitiveType);
        _gl.DrawElements(primitiveType, mesh.IndexCount, DrawElementsType.UnsignedInt, null);
    }

    private void DrawSky(GlSkyMesh sky, Matrix4x4 viewProjection)
    {
        if (sky.IndexCount == 0 || sky.Texture == 0)
            return;

        // Sky surfaces are authored masks. Project their XY normally, force them
        // to the far depth in the vertex shader, and establish the background
        // without reserving depth that later world geometry needs.
        _state.SetEnabled(EnableCap.DepthTest, true);
        _state.DepthFunc(DepthFunction.Lequal);
        _state.DepthMask(false);
        _state.SetEnabled(EnableCap.Blend, false);
        _state.SetEnabled(EnableCap.CullFace, false);
        _state.SetEnabled(EnableCap.PolygonOffsetFill, false);
        _state.SetEnabled(EnableCap.StencilTest, false);
        _state.ColorMask(true, true, true, true);
        _state.UseProgram(_skyProgram);
        _state.UniformMatrix4(_skyViewProjectionLocation, viewProjection);
        _state.ActiveTexture(0);
        _state.BindSampler(0, 0);
        _state.BindTexture(TextureTarget.TextureCubeMap, sky.Texture);
        _state.BindVertexArray(sky.VertexArray);
        RecordDraw(
            sky.IndexCount,
            instanceCount: 1,
            PrimitiveType.Triangles);
        _gl.DrawElements(PrimitiveType.Triangles, sky.IndexCount, DrawElementsType.UnsignedInt, null);
        ApplyDefaultRenderState();
    }

    private void Draw(GlInstancedMesh mesh, PrimitiveType primitiveType)
    {
        if (mesh.IndexCount == 0 || mesh.InstanceCount == 0)
            return;

        _state.BindVertexArray(mesh.VertexArray);
        RecordDraw(mesh.IndexCount, mesh.InstanceCount, primitiveType);
        _gl.DrawElementsInstanced(
            primitiveType,
            mesh.IndexCount,
            DrawElementsType.UnsignedInt,
            null,
            mesh.InstanceCount);
    }

    private void DrawVisibleDepthPrepassGroups(
        IReadOnlyList<MapRenderEditorDrawGroup<GlTexturedDrawCommand>> groups,
        Matrix4x4 viewProjection,
        DerivedMatrixState rsxMatrices,
        float editorTimeSeconds)
    {
        using MapRenderOpenGlGpuPhaseScope gpuTiming =
            _gpuTimers.BeginPhase(MapRenderGpuPhase.DepthPrepass);
        using GpuDrawPhaseScope drawWork =
            BeginGpuDrawPhase(MapRenderGpuPhase.DepthPrepass);
        using MapRenderCpuPhaseScope cpuTiming =
            _frameTelemetry.BeginCpuPhase(MapRenderCpuPhase.DepthPrepass);
        for (int groupIndex = 0;
             groupIndex < groups.Count;
             groupIndex++)
        {
            MapRenderEditorDrawGroup<GlTexturedDrawCommand> group =
                groups[groupIndex];
            if (!IsTexturedDrawGroupVisibleForFrame(group))
                continue;
            IReadOnlyList<GlTexturedDrawCommand> commands =
                SelectStandardDepthPrepassCommands(
                    group,
                    out MapRenderEditorDepthPrepassPlan? plan);
            if (commands.Count == 0 || plan is null)
                continue;

            if (commands.Count == 1 &&
                TryGetDepthMultiDrawWorldMesh(
                    group,
                    out GlTexturedMesh firstMesh,
                    out WorldSurfaceBatchRuntime? firstSurfaceBatch))
            {
                GlTexturedMesh firstVisibleMesh =
                    SelectFirstWorldMultiDrawMesh(
                        firstMesh,
                        firstSurfaceBatch);
                int multiDrawCount = AppendWorldMultiDrawRanges(
                    firstMesh,
                    firstSurfaceBatch,
                    destinationIndex: 0);
                int groupedDrawCount = 1;
                while (groupIndex + groupedDrawCount < groups.Count)
                {
                    int nextGroupIndex = groupIndex + groupedDrawCount;
                    MapRenderEditorDrawGroup<GlTexturedDrawCommand> nextGroup =
                        groups[nextGroupIndex];
                    if (!IsTexturedDrawGroupVisibleForFrame(nextGroup))
                        break;
                    IReadOnlyList<GlTexturedDrawCommand> nextCommands =
                        SelectStandardDepthPrepassCommands(
                            nextGroup,
                            out MapRenderEditorDepthPrepassPlan? nextPlan);
                    if (nextPlan is null ||
                        nextCommands.Count != 1 ||
                        !TryGetDepthMultiDrawWorldMesh(
                            nextGroup,
                            out GlTexturedMesh nextMesh,
                            out WorldSurfaceBatchRuntime? nextSurfaceBatch))
                    {
                        break;
                    }

                    int nextRunCount = ResolveWorldMultiDrawRangeCount(
                        nextSurfaceBatch);
                    if (!CanAggregateWorldDepthMultiDrawGroup(
                            firstMesh,
                            nextMesh,
                            nextRunCount))
                    {
                        break;
                    }

                    multiDrawCount = checked(
                        multiDrawCount + AppendWorldMultiDrawRanges(
                            nextMesh,
                            nextSurfaceBatch,
                            multiDrawCount));
                    groupedDrawCount++;
                }

                DrawDepthPrepass(
                    firstVisibleMesh,
                    plan,
                    viewProjection,
                    rsxMatrices,
                    editorTimeSeconds,
                    instanceIndex: null,
                    multiDrawCount);
                groupIndex += groupedDrawCount - 1;
                continue;
            }

            // A draw group preserves authored ordering, but its commands are
            // not guaranteed to share one geometry range. Multi-surface world
            // batches collapse to SurfaceIndex=-1 and can therefore coexist
            // here as disjoint ranges of the same material/technique. Replay
            // every command so each range establishes depth. True multipass
            // duplicates are harmless under the authored LEQUAL state.
            for (int commandIndex = 0;
                 commandIndex < commands.Count;
                 commandIndex++)
            {
                DrawDepthPrepass(
                    commands[commandIndex],
                    plan,
                    viewProjection,
                    rsxMatrices,
                    editorTimeSeconds);
            }
        }
    }

    private void PrepareTexturedDrawGroupVisibility(
        IReadOnlyList<MapRenderEditorDrawGroup<GlTexturedDrawCommand>> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        if (_texturedDrawGroupVisibilityScratch.Length < groups.Count)
        {
            Array.Resize(
                ref _texturedDrawGroupVisibilityScratch,
                groups.Count);
        }
        _texturedDrawGroupVisibilityByIdentity.Clear();
        _texturedDrawGroupVisibilityByIdentity.EnsureCapacity(
            groups.Count);

        for (int groupIndex = 0;
             groupIndex < groups.Count;
             groupIndex++)
        {
            MapRenderEditorDrawGroup<GlTexturedDrawCommand> group =
                groups[groupIndex];
            bool visible = IsTexturedDrawGroupVisible(group);
            _texturedDrawGroupVisibilityScratch[groupIndex] = visible;
            _texturedDrawGroupVisibilityByIdentity[group] = visible;
        }
    }

    private bool IsTexturedDrawGroupVisibleForFrame(
        MapRenderEditorDrawGroup<GlTexturedDrawCommand> group) =>
        _texturedDrawGroupVisibilityByIdentity.TryGetValue(
            group,
            out bool visible)
            ? visible
            : IsTexturedDrawGroupVisible(group);

    private bool IsTexturedDrawGroupVisible(
        MapRenderEditorDrawGroup<GlTexturedDrawCommand> group)
    {
        IReadOnlyList<GlTexturedDrawCommand> commands =
            group.AuthoredPasses;
        for (int commandIndex = 0;
             commandIndex < commands.Count;
             commandIndex++)
        {
            if (IsTexturedDrawCommandVisible(commands[commandIndex]))
                return true;
        }
        return false;
    }

    private bool IsTexturedDrawCommandVisible(
        GlTexturedDrawCommand command)
    {
        GlTexturedMesh mesh = command.Mesh;
        if (mesh.InstanceCount == 0)
        {
            return TryGetWorldSurfaceBatchRuntime(
                    command,
                    out WorldSurfaceBatchRuntime worldBatch)
                ? worldBatch.RunCount != 0
                : IsWorldMeshVisible(mesh);
        }

        if (command.InstanceIndex is int instanceIndex)
            return IsStaticInstanceVisible(mesh, instanceIndex);
        return ResolveVisibleInstanceCount(mesh) != 0;
    }

    internal static IReadOnlyList<GlTexturedDrawCommand>
        SelectStandardDepthPrepassCommands(
        MapRenderEditorDrawGroup<GlTexturedDrawCommand> group,
        out MapRenderEditorDepthPrepassPlan? plan)
    {
        ArgumentNullException.ThrowIfNull(group);
        plan = null;
        if (group.Bucket != MapRenderEditorDrawBucket.Opaque ||
            group.AuthoredPasses.Count == 0 ||
            group.AuthoredPasses[0].Mesh.EditorDepthPrepass is not
                { Program: MapRenderEditorDepthPrepassProgram.TransformOnlyNull }
                candidate)
        {
            return [];
        }

        for (int passIndex = 1;
             passIndex < group.AuthoredPasses.Count;
             passIndex++)
        {
            if (group.AuthoredPasses[passIndex].Mesh.EditorDepthPrepass !=
                candidate)
            {
                return [];
            }
        }

        plan = candidate;
        return group.AuthoredPasses;
    }

    private void DrawDepthPrepass(
        GlTexturedDrawCommand command,
        MapRenderEditorDepthPrepassPlan plan,
        Matrix4x4 viewProjection,
        DerivedMatrixState rsxMatrices,
        float editorTimeSeconds)
    {
        if (command.Mesh.InstanceCount == 0 &&
            TryGetWorldSurfaceBatchRuntime(
                command,
                out WorldSurfaceBatchRuntime worldBatch))
        {
            if (worldBatch.RunCount == 0)
                return;

            GlTexturedMesh visibleMesh = SelectWorldVisibleRun(
                command.Mesh,
                worldBatch.VisibleRuns[0]);
            int multiDrawCount = worldBatch.RunCount;
            if (multiDrawCount > 1)
            {
                EnsureMultiDrawCapacity(multiDrawCount);
                for (int runIndex = 0;
                     runIndex < multiDrawCount;
                     runIndex++)
                {
                    SetMultiDrawRange(
                        runIndex,
                        SelectWorldVisibleRun(
                            command.Mesh,
                            worldBatch.VisibleRuns[runIndex]));
                }
            }

            DrawDepthPrepass(
                visibleMesh,
                plan,
                viewProjection,
                rsxMatrices,
                editorTimeSeconds,
                instanceIndex: null,
                multiDrawCount);
            return;
        }
        if (command.Mesh.InstanceCount == 0 &&
            !IsWorldMeshVisible(command.Mesh))
        {
            return;
        }
        if (command.InstanceIndex is int visibilityInstanceIndex &&
            !IsStaticInstanceVisible(
                command.Mesh,
                visibilityInstanceIndex))
        {
            return;
        }
        if (command.Mesh.InstanceCount != 0 &&
            command.InstanceIndex is null &&
            ResolveVisibleInstanceCount(command.Mesh) == 0)
        {
            return;
        }

        DrawDepthPrepass(
            command.Mesh,
            plan,
            viewProjection,
            rsxMatrices,
            editorTimeSeconds,
            command.InstanceIndex);
    }

    private void DrawDepthPrepass(
        GlTexturedMesh mesh,
        MapRenderEditorDepthPrepassPlan plan,
        Matrix4x4 viewProjection,
        DerivedMatrixState rsxMatrices,
        float editorTimeSeconds,
        int? instanceIndex,
        int multiDrawCount = 0)
    {
        if (mesh.IndexCount == 0)
            return;
        if (instanceIndex is int isolatedInstanceIndex &&
            (isolatedInstanceIndex < 0 ||
             (uint)isolatedInstanceIndex >= mesh.InstanceCount))
        {
            throw new ArgumentOutOfRangeException(
                nameof(instanceIndex),
                isolatedInstanceIndex,
                "Depth-prepass instance index is outside the mesh.");
        }

        if (mesh.RsxProgram.Handle != 0)
        {
            // A translated color mesh is backed by the 16xvec4 RSX input
            // arena. Execute the resolved authored slot-0 shader over that
            // exact slab; the generic host transform consumes a different
            // coordinate convention and is not a valid substitute here.
            if (mesh.DepthPrepassRsxProgram.Handle == 0)
            {
                throw new InvalidOperationException(
                    "Translated world color execution was authorized without its resolved authored depth owner.");
            }

            DerivedMatrixState drawMatrices =
                ResolveTranslatedDrawMatrices(
                    mesh,
                    instanceIndex,
                    rsxMatrices);
            ApplyRenderState(plan.State);
            _state.UseProgram(mesh.DepthPrepassRsxProgram.Handle);
            ApplyStaticModelInstancingFrame(
                mesh.DepthStaticModelProgramUniforms,
                rsxMatrices);
            ApplyTranslatedStaticComposition(
                mesh.DepthStaticModelProgramUniforms,
                mesh,
                editorTimeSeconds);
            ApplyRsxConstantBindings(
                mesh.DepthPrepassRsxConstantBindings,
                drawMatrices,
                editorTimeSeconds);
            _state.BindVertexArray(mesh.VertexArray);
            if (instanceIndex.HasValue)
            {
                ConfigureTexturedInstanceBase(
                    mesh.InstanceBuffer,
                    instanceIndex.Value,
                    MapRenderOpenGlStaticModelInstancedVertexComposer
                        .FirstPlacementAttribute);
            }
            IssueWorldDraws(mesh, multiDrawCount, instanceIndex);
            return;
        }

        ApplyRenderState(plan.State);
        _state.UseProgram(_depthPrepassProgram);
        _state.UniformMatrix4(
            _depthPrepassViewProjectionLocation,
            viewProjection);
        _state.Uniform1(
            _depthPrepassUseInstancingLocation,
            mesh.InstanceCount == 0 ? 0 : 1);
        // The host bridge keeps EditorPreview vegetation deformation in the
        // depth owner so its generic static color pass cannot drift away. The
        // decoded native transform_only program itself has no wind inputs.
        MapRenderEditorVegetationAnimationPlan? vegetation =
            mesh.VegetationAnimation;
        _state.Uniform1(
            _depthPrepassVegetationWindEnabledLocation,
            vegetation?.IsEnabled == true ? 1 : 0);
        _state.Uniform1(
            _depthPrepassVegetationTimeLocation,
            editorTimeSeconds);
        _state.Uniform1(
            _depthPrepassVegetationAmplitudeLocation,
            vegetation?.Amplitude ?? 0f);
        _state.Uniform1(
            _depthPrepassVegetationAngularFrequencyLocation,
            vegetation?.AngularFrequency ?? 0f);
        _state.Uniform1(
            _depthPrepassVegetationSpatialFrequencyLocation,
            vegetation?.SpatialFrequency ?? 0f);
        _state.Uniform1(
            _depthPrepassVegetationLocalMinimumHeightLocation,
            mesh.LocalMinimumHeight);
        _state.Uniform1(
            _depthPrepassVegetationLocalHeightRangeLocation,
            mesh.LocalHeightRange);
        _state.BindVertexArray(mesh.VertexArray);
        if (mesh.InstanceCount == 0)
        {
            IssueWorldDraws(mesh, multiDrawCount);
            return;
        }

        if (instanceIndex.HasValue ||
            (_staticInstanceBuffers.TryGetValue(
                 mesh.InstanceBuffer,
                 out StaticInstanceBufferRuntime? runtime) &&
             runtime.HasIsolatedDraw))
        {
            ConfigureTexturedInstanceBase(
                mesh.InstanceBuffer,
                instanceIndex ?? 0);
        }
        uint drawnInstanceCount = instanceIndex.HasValue
            ? 1u
            : ResolveVisibleInstanceCount(mesh);
        RecordDraw(
            mesh.IndexCount,
            drawnInstanceCount,
            PrimitiveType.Triangles);
        _gl.DrawElementsInstanced(
            PrimitiveType.Triangles,
            mesh.IndexCount,
            DrawElementsType.UnsignedInt,
            null,
            drawnInstanceCount);
    }

    private void DrawVisibleTexturedGroups(
        IReadOnlyList<MapRenderEditorDrawGroup<GlTexturedDrawCommand>> groups,
        Matrix4x4 viewProjection,
        DerivedMatrixState rsxMatrices,
        Vector3 cameraPosition,
        float editorTimeSeconds)
    {
        Span<int> phaseRunCounts = stackalloc int[GpuPhaseCount];
        phaseRunCounts.Clear();
        int previousPhaseIndex = -1;
        for (int index = 0; index < groups.Count; index++)
        {
            if (!_texturedDrawGroupVisibilityScratch[index] ||
                !IsTexturedDrawGroupReadyForColorExecution(groups[index]))
            {
                continue;
            }
            int phaseIndex = (int)ResolveTexturedGpuPhase(groups[index]);
            if (phaseIndex == previousPhaseIndex)
                continue;

            phaseRunCounts[phaseIndex]++;
            previousPhaseIndex = phaseIndex;
        }

        MapRenderOpenGlGpuPhaseScope gpuTimingScope = default;
        GpuDrawPhaseScope drawWorkScope = default;
        int activePhaseIndex = -1;
        try
        {
            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                MapRenderEditorDrawGroup<GlTexturedDrawCommand> group =
                    groups[groupIndex];
                if (!_texturedDrawGroupVisibilityScratch[groupIndex] ||
                    !IsTexturedDrawGroupReadyForColorExecution(group))
                {
                    continue;
                }
                MapRenderGpuPhase gpuPhase = ResolveTexturedGpuPhase(group);
                int gpuPhaseIndex = (int)gpuPhase;
                if (gpuPhaseIndex != activePhaseIndex)
                {
                    gpuTimingScope.Dispose();
                    drawWorkScope.Dispose();
                    gpuTimingScope = default;
                    drawWorkScope = BeginGpuDrawPhase(gpuPhase);
                    activePhaseIndex = gpuPhaseIndex;

                    // One TIME_ELAPSED query cannot span another phase and
                    // OpenGL forbids nesting. If state sorting makes a source
                    // phase non-contiguous, retain exact draw order and omit
                    // that frame's timing sample for the repeated phase.
                    if (phaseRunCounts[gpuPhaseIndex] == 1)
                        gpuTimingScope = _gpuTimers.BeginPhase(gpuPhase);
                }

                using var phaseTiming = _frameTelemetry.BeginCpuPhase(
                    group.AuthoredPasses[0].Mesh.InstanceCount == 0
                        ? MapRenderCpuPhase.WorldGeometry
                        : MapRenderCpuPhase.StaticModels);
                if (TryDrawCompactedStaticReceiverGroup(
                        groupIndex,
                        group,
                        viewProjection,
                        rsxMatrices,
                        cameraPosition,
                        editorTimeSeconds))
                {
                    continue;
                }
                if (!TryGetMultiDrawWorldMesh(
                        group,
                        out GlTexturedMesh firstMesh,
                        out WorldSurfaceBatchRuntime? firstSurfaceBatch))
                {
                    IReadOnlyList<GlTexturedDrawCommand> authoredPasses =
                        group.AuthoredPasses;
                    for (int commandIndex = 0;
                         commandIndex < authoredPasses.Count;
                         commandIndex++)
                    {
                        Draw(
                            authoredPasses[commandIndex],
                            viewProjection,
                            rsxMatrices,
                            cameraPosition,
                            editorTimeSeconds);
                    }
                    continue;
                }

                GlTexturedMesh firstVisibleMesh =
                    SelectFirstWorldMultiDrawMesh(
                        firstMesh,
                        firstSurfaceBatch);
                int multiDrawCount = AppendWorldMultiDrawRanges(
                    firstMesh,
                    firstSurfaceBatch,
                    destinationIndex: 0);
                int groupedDrawCount = 1;
                while (groupIndex + groupedDrawCount < groups.Count)
                {
                    MapRenderEditorDrawGroup<GlTexturedDrawCommand> nextGroup =
                        groups[groupIndex + groupedDrawCount];
                    if (nextGroup.Bucket != group.Bucket ||
                        ResolveTexturedGpuPhase(nextGroup) != gpuPhase ||
                        !TryGetMultiDrawWorldMesh(
                            nextGroup,
                            out GlTexturedMesh nextMesh,
                            out WorldSurfaceBatchRuntime? nextSurfaceBatch))
                    {
                        break;
                    }

                    int nextRunCount = ResolveWorldMultiDrawRangeCount(
                        nextSurfaceBatch);
                    if (!CanAggregateWorldMultiDrawGroup(
                            firstMesh,
                            nextMesh,
                            nextRunCount))
                    {
                        break;
                    }

                    multiDrawCount = checked(
                        multiDrawCount + AppendWorldMultiDrawRanges(
                            nextMesh,
                            nextSurfaceBatch,
                            multiDrawCount));
                    groupedDrawCount++;
                }

                Draw(
                    firstVisibleMesh,
                    viewProjection,
                    rsxMatrices,
                    cameraPosition,
                    editorTimeSeconds,
                    instanceIndex: null,
                    multiDrawCount: multiDrawCount);
                groupIndex += groupedDrawCount - 1;
            }
        }
        finally
        {
            gpuTimingScope.Dispose();
            drawWorkScope.Dispose();
        }
    }

    private bool TryBuildProcessedFloatZ(
        EditorPresentationFrame? frame,
        float zNear)
    {
        if (frame is null ||
            _editorPreviewPresentationSession is null)
        {
            return false;
        }

        using MapRenderOpenGlGpuPhaseScope gpuTiming =
            _gpuTimers.BeginPhase(MapRenderGpuPhase.ProcessedFloatZ);
        using GpuDrawPhaseScope drawWork =
            BeginGpuDrawPhase(MapRenderGpuPhase.ProcessedFloatZ);
        using MapRenderCpuPhaseScope cpuTiming =
            _frameTelemetry.BeginCpuPhase(
                MapRenderCpuPhase.ProcessedFloatZ);
        _currentProcessedFloatZFrame =
            _editorPreviewPresentationSession.ExecuteProcessedFloatZ(
                frame,
                zNear);

        // The backend owns three fullscreen submissions: the host-only
        // D24S8-MS representation adapter followed by the two exact authored
        // PS3 passes ($floatz and $processed_floatz).
        RecordDraw(3, 1, PrimitiveType.Triangles);
        RecordDraw(6, 1, PrimitiveType.Triangles);
        RecordDraw(6, 1, PrimitiveType.Triangles);
        return true;
    }

    private static bool RequiresProcessedFloatZ(
        MapRenderEditorDrawGroup<GlTexturedDrawCommand> group) =>
        group.AuthoredPasses.Any(command =>
            command.Mesh.ShaderExecution?.RuntimeSamplerRequirements.Any(
                requirement =>
                    requirement.ResourceKind ==
                        ShaderRuntimeSamplerResourceKind
                            .ProcessedFloatZ &&
                    requirement.Status ==
                        ShaderRuntimeSamplerRequirementStatus
                            .SameRevisionTextureRequired) == true);

    private bool RequiresVisibleProcessedFloatZ(
        IReadOnlyList<MapRenderEditorDrawGroup<GlTexturedDrawCommand>>
            groups)
    {
        for (int groupIndex = 0;
             groupIndex < groups.Count;
             groupIndex++)
        {
            if (_texturedDrawGroupVisibilityScratch[groupIndex] &&
                IsTexturedDrawGroupReadyForColorExecution(
                    groups[groupIndex]) &&
                RequiresProcessedFloatZ(groups[groupIndex]))
            {
                return true;
            }
        }

        return false;
    }

    private static MapRenderGpuPhase ResolveTexturedGpuPhase(
        MapRenderEditorDrawGroup<GlTexturedDrawCommand> group)
    {
        if (group.Bucket == MapRenderEditorDrawBucket.Translucent)
            return MapRenderGpuPhase.Translucent;

        bool isStatic = group.AuthoredPasses[0].Mesh.InstanceCount != 0;
        return (group.Bucket, isStatic) switch
        {
            (MapRenderEditorDrawBucket.Opaque, false) =>
                MapRenderGpuPhase.WorldOpaque,
            (MapRenderEditorDrawBucket.AlphaTest, false) =>
                MapRenderGpuPhase.WorldCutout,
            (MapRenderEditorDrawBucket.Opaque, true) =>
                MapRenderGpuPhase.StaticOpaque,
            (MapRenderEditorDrawBucket.AlphaTest, true) =>
                MapRenderGpuPhase.StaticCutout,
            _ => throw new ArgumentOutOfRangeException(
                nameof(group),
                group.Bucket,
                "Unknown editor draw bucket.")
        };
    }

    private bool TryGetMultiDrawWorldMesh(
        MapRenderEditorDrawGroup<GlTexturedDrawCommand> group,
        out GlTexturedMesh mesh,
        out WorldSurfaceBatchRuntime? surfaceBatch)
    {
        mesh = default;
        surfaceBatch = null;
        if (group.Bucket == MapRenderEditorDrawBucket.Translucent ||
            group.AuthoredPasses.Count != 1)
        {
            return false;
        }

        GlTexturedDrawCommand command = group.AuthoredPasses[0];
        mesh = command.Mesh;
        if (command.InstanceIndex is not null ||
            mesh.InstanceCount != 0 ||
            mesh.IndexCount == 0 ||
            mesh.VertexArray == 0 ||
            mesh.ElementBuffer == 0 ||
            mesh.MultiDrawBatchGroupId < 0)
        {
            return false;
        }

        if (TryGetWorldSurfaceBatchRuntime(
                command,
                out WorldSurfaceBatchRuntime runtime))
        {
            if (runtime.RunCount == 0)
                return false;
            surfaceBatch = runtime;
            return true;
        }

        return IsWorldMeshVisible(mesh);
    }

    private bool TryGetDepthMultiDrawWorldMesh(
        MapRenderEditorDrawGroup<GlTexturedDrawCommand> group,
        out GlTexturedMesh mesh,
        out WorldSurfaceBatchRuntime? surfaceBatch)
    {
        mesh = default;
        surfaceBatch = null;
        if (group.Bucket != MapRenderEditorDrawBucket.Opaque ||
            group.AuthoredPasses.Count != 1)
        {
            return false;
        }

        GlTexturedDrawCommand command = group.AuthoredPasses[0];
        mesh = command.Mesh;
        if (command.InstanceIndex is not null ||
            mesh.InstanceCount != 0 ||
            mesh.IndexCount == 0 ||
            mesh.VertexArray == 0 ||
            mesh.ElementBuffer == 0 ||
            mesh.DepthMultiDrawBatchGroupId < 0)
        {
            return false;
        }

        if (TryGetWorldSurfaceBatchRuntime(
                command,
                out WorldSurfaceBatchRuntime runtime))
        {
            if (runtime.RunCount == 0)
                return false;
            surfaceBatch = runtime;
            return true;
        }

        return IsWorldMeshVisible(mesh);
    }

    private int AppendWorldMultiDrawRanges(
        GlTexturedMesh mesh,
        WorldSurfaceBatchRuntime? surfaceBatch,
        int destinationIndex)
    {
        int rangeCount = ResolveWorldMultiDrawRangeCount(surfaceBatch);
        EnsureMultiDrawCapacity(checked(destinationIndex + rangeCount));
        if (surfaceBatch is null)
        {
            SetMultiDrawRange(destinationIndex, mesh);
            return 1;
        }

        for (int runIndex = 0;
             runIndex < surfaceBatch.RunCount;
             runIndex++)
        {
            SetMultiDrawRange(
                destinationIndex + runIndex,
                SelectWorldVisibleRun(
                    mesh,
                    surfaceBatch.VisibleRuns[runIndex]));
        }
        return rangeCount;
    }

    private static int ResolveWorldMultiDrawRangeCount(
        WorldSurfaceBatchRuntime? surfaceBatch) =>
        surfaceBatch?.RunCount ?? 1;

    private static GlTexturedMesh SelectFirstWorldMultiDrawMesh(
        GlTexturedMesh mesh,
        WorldSurfaceBatchRuntime? surfaceBatch) =>
        surfaceBatch is null
            ? mesh
            : SelectWorldVisibleRun(mesh, surfaceBatch.VisibleRuns[0]);

    internal static bool CanAggregateWorldMultiDrawGroup(
        GlTexturedMesh first,
        GlTexturedMesh next,
        int visibleRunCount) =>
        visibleRunCount > 0 &&
        first.MultiDrawBatchGroupId >= 0 &&
        first.MultiDrawBatchGroupId == next.MultiDrawBatchGroupId &&
        CanMultiDrawTogether(first, next);

    internal static bool CanAggregateWorldDepthMultiDrawGroup(
        GlTexturedMesh first,
        GlTexturedMesh next,
        int visibleRunCount) =>
        visibleRunCount > 0 &&
        first.DepthMultiDrawBatchGroupId >= 0 &&
        first.DepthMultiDrawBatchGroupId ==
            next.DepthMultiDrawBatchGroupId &&
        CanDepthMultiDrawTogether(first, next);

    private void AssignWorldMultiDrawBatchGroupIds()
    {
        _nextWorldMultiDrawBatchGroupId = 0;
        _nextWorldDepthMultiDrawBatchGroupId = 0;
        AssignWorldMultiDrawBatchGroupIds(_textured);
    }

    private void AssignWorldMultiDrawBatchGroupIds(
        GlTexturedMesh[] meshes)
    {
        var buckets = new Dictionary<
            int,
            List<(int Id, GlTexturedMesh Representative)>>();
        for (int meshIndex = 0; meshIndex < meshes.Length; meshIndex++)
        {
            GlTexturedMesh mesh = meshes[meshIndex];
            if (mesh.InstanceCount != 0 ||
                mesh.IndexCount == 0 ||
                mesh.VertexArray == 0 ||
                mesh.ElementBuffer == 0)
            {
                continue;
            }

            int batchGroupId = -1;
            int hash = ComputeMultiDrawBatchHash(mesh);
            if (buckets.TryGetValue(hash, out var candidates))
            {
                foreach ((int id, GlTexturedMesh representative) in
                         candidates)
                {
                    if (!CanMultiDrawTogether(representative, mesh))
                        continue;
                    batchGroupId = id;
                    break;
                }
            }

            if (batchGroupId < 0)
            {
                batchGroupId = _nextWorldMultiDrawBatchGroupId++;
                (candidates ??= []).Add((batchGroupId, mesh));
                buckets[hash] = candidates;
            }

            meshes[meshIndex] = mesh with
            {
                MultiDrawBatchGroupId = batchGroupId
            };
        }

        AssignWorldDepthMultiDrawBatchGroupIds(meshes);
    }

    private void AssignWorldDepthMultiDrawBatchGroupIds(
        GlTexturedMesh[] meshes)
    {
        var buckets = new Dictionary<
            int,
            List<(int Id, GlTexturedMesh Representative)>>();
        for (int meshIndex = 0; meshIndex < meshes.Length; meshIndex++)
        {
            GlTexturedMesh mesh = meshes[meshIndex];
            if (mesh.InstanceCount != 0 ||
                mesh.IndexCount == 0 ||
                mesh.VertexArray == 0 ||
                mesh.ElementBuffer == 0 ||
                mesh.EditorDepthPrepass?.Program !=
                    MapRenderEditorDepthPrepassProgram.TransformOnlyNull)
            {
                continue;
            }

            int batchGroupId = -1;
            int hash = ComputeDepthMultiDrawBatchHash(mesh);
            if (buckets.TryGetValue(hash, out var candidates))
            {
                foreach ((int id, GlTexturedMesh representative) in
                         candidates)
                {
                    if (!CanDepthMultiDrawTogether(representative, mesh))
                        continue;
                    batchGroupId = id;
                    break;
                }
            }

            if (batchGroupId < 0)
            {
                batchGroupId =
                    _nextWorldDepthMultiDrawBatchGroupId++;
                (candidates ??= []).Add((batchGroupId, mesh));
                buckets[hash] = candidates;
            }

            meshes[meshIndex] = mesh with
            {
                DepthMultiDrawBatchGroupId = batchGroupId
            };
        }
    }

    private static int ComputeDepthMultiDrawBatchHash(
        GlTexturedMesh mesh)
    {
        var hash = new HashCode();
        hash.Add(mesh.VertexArray);
        hash.Add(mesh.ElementBuffer);
        hash.Add(mesh.RsxProgram.Handle != 0);
        hash.Add(mesh.EditorDepthPrepass?.Program);
        hash.Add(mesh.EditorDepthPrepass?.State);
        hash.Add(mesh.DepthPrepassRsxProgram.Handle);
        foreach (GlRsxConstantBinding binding in
                 mesh.DepthPrepassRsxConstantBindings)
        {
            hash.Add(binding);
        }
        if (mesh.RsxProgram.Handle == 0 &&
            mesh.VegetationAnimation is { } vegetation)
        {
            hash.Add(vegetation.Status);
            hash.Add(vegetation.IsEnabled);
            hash.Add(vegetation.Amplitude);
            hash.Add(vegetation.AngularFrequency);
            hash.Add(vegetation.SpatialFrequency);
            if (vegetation.IsEnabled)
            {
                hash.Add(mesh.LocalMinimumHeight);
                hash.Add(mesh.LocalHeightRange);
            }
        }
        return hash.ToHashCode();
    }

    private static int ComputeMultiDrawBatchHash(
        GlTexturedMesh mesh)
    {
        var hash = new HashCode();
        hash.Add(mesh.VertexArray);
        hash.Add(mesh.ElementBuffer);
        hash.Add(mesh.State);
        hash.Add(mesh.RsxProgram.Handle);
        hash.Add(mesh.FragmentProgramControl);
        if (mesh.ShaderExecution is { } execution)
        {
            hash.Add(execution.RuntimeSamplerRequirements.Count);
            foreach (ShaderRuntimeSamplerRequirement requirement in
                     execution.RuntimeSamplerRequirements)
            {
                hash.Add(requirement);
            }
        }
        hash.Add(mesh.EditorDepthPrepass?.Program);
        hash.Add(mesh.EditorDepthPrepass?.State);
        hash.Add(mesh.DepthPrepassRsxProgram.Handle);
        foreach (GlRsxConstantBinding binding in
                 mesh.DepthPrepassRsxConstantBindings)
        {
            hash.Add(binding);
        }
        if (mesh.RsxProgram.Handle != 0)
        {
            foreach (GlRsxSamplerBinding binding in mesh.RsxSamplerBindings)
                hash.Add(binding);
            foreach (GlRsxConstantBinding binding in mesh.RsxConstantBindings)
                hash.Add(binding);
            return hash.ToHashCode();
        }

        foreach (uint texture in mesh.ColorTextures)
            hash.Add(texture);
        foreach (int component in mesh.BlendWeightComponents)
            hash.Add(component);
        hash.Add(mesh.LightmapTexture);
        foreach (uint texture in mesh.NormalTextures)
            hash.Add(texture);
        foreach (uint texture in mesh.SpecularTextures)
            hash.Add(texture);
        if (mesh.VegetationAnimation is { } vegetation)
        {
            hash.Add(vegetation.Status);
            hash.Add(vegetation.IsEnabled);
            hash.Add(vegetation.Amplitude);
            hash.Add(vegetation.AngularFrequency);
            hash.Add(vegetation.SpatialFrequency);
            if (vegetation.IsEnabled)
            {
                hash.Add(mesh.LocalMinimumHeight);
                hash.Add(mesh.LocalHeightRange);
            }
        }
        hash.Add(mesh.ReceivesEditorLighting);
        return hash.ToHashCode();
    }

    internal static bool CanMultiDrawTogether(
        GlTexturedMesh first,
        GlTexturedMesh next)
    {
        if (first.VertexArray != next.VertexArray ||
            first.ElementBuffer != next.ElementBuffer ||
            first.InstanceCount != 0 ||
            next.InstanceCount != 0 ||
            first.State != next.State ||
            first.RsxProgram.Handle != next.RsxProgram.Handle ||
            first.FragmentProgramControl != next.FragmentProgramControl ||
            !RuntimeSamplerRequirementsMatch(first, next) ||
            first.EditorDepthPrepass?.Program !=
                next.EditorDepthPrepass?.Program ||
            first.EditorDepthPrepass?.State !=
                next.EditorDepthPrepass?.State ||
            first.DepthPrepassRsxProgram.Handle !=
                next.DepthPrepassRsxProgram.Handle ||
            !first.DepthPrepassRsxConstantBindings.AsSpan().SequenceEqual(
                next.DepthPrepassRsxConstantBindings))
        {
            return false;
        }

        if (first.RsxProgram.Handle != 0)
        {
            return first.RsxSamplerBindings.AsSpan().SequenceEqual(
                       next.RsxSamplerBindings) &&
                   first.RsxConstantBindings.AsSpan().SequenceEqual(
                       next.RsxConstantBindings);
        }

        return first.ColorTextures.AsSpan().SequenceEqual(next.ColorTextures) &&
               first.BlendWeightComponents.AsSpan().SequenceEqual(
                   next.BlendWeightComponents) &&
               first.LightmapTexture == next.LightmapTexture &&
               first.NormalTextures.AsSpan().SequenceEqual(next.NormalTextures) &&
               first.SpecularTextures.AsSpan().SequenceEqual(
                   next.SpecularTextures) &&
               CompositionPlansMatch(
                   first.VegetationAnimation,
                   next.VegetationAnimation) &&
               (first.VegetationAnimation?.IsEnabled != true ||
                (first.LocalMinimumHeight == next.LocalMinimumHeight &&
                 first.LocalHeightRange == next.LocalHeightRange)) &&
               first.ReceivesEditorLighting == next.ReceivesEditorLighting;
    }

    internal static bool CanDepthMultiDrawTogether(
        GlTexturedMesh first,
        GlTexturedMesh next)
    {
        if (first.VertexArray != next.VertexArray ||
            first.ElementBuffer != next.ElementBuffer ||
            first.InstanceCount != 0 ||
            next.InstanceCount != 0 ||
            (first.RsxProgram.Handle != 0) !=
                (next.RsxProgram.Handle != 0) ||
            first.EditorDepthPrepass?.Program !=
                next.EditorDepthPrepass?.Program ||
            first.EditorDepthPrepass?.State !=
                next.EditorDepthPrepass?.State ||
            first.DepthPrepassRsxProgram.Handle !=
                next.DepthPrepassRsxProgram.Handle ||
            !first.DepthPrepassRsxConstantBindings.AsSpan().SequenceEqual(
                next.DepthPrepassRsxConstantBindings))
        {
            return false;
        }

        if (first.RsxProgram.Handle != 0)
            return true;

        return CompositionPlansMatch(
                   first.VegetationAnimation,
                   next.VegetationAnimation) &&
               (first.VegetationAnimation?.IsEnabled != true ||
                (first.LocalMinimumHeight == next.LocalMinimumHeight &&
                 first.LocalHeightRange == next.LocalHeightRange));
    }

    private static bool RuntimeSamplerRequirementsMatch(
        GlTexturedMesh first,
        GlTexturedMesh next)
    {
        IReadOnlyList<ShaderRuntimeSamplerRequirement> firstValues =
            first.ShaderExecution?.RuntimeSamplerRequirements ?? [];
        IReadOnlyList<ShaderRuntimeSamplerRequirement> nextValues =
            next.ShaderExecution?.RuntimeSamplerRequirements ?? [];
        if (firstValues.Count != nextValues.Count)
            return false;

        for (int index = 0; index < firstValues.Count; index++)
        {
            if (firstValues[index] != nextValues[index])
                return false;
        }
        return true;
    }

    private static bool CompositionPlansMatch(
        MapRenderEditorVegetationAnimationPlan? first,
        MapRenderEditorVegetationAnimationPlan? next) =>
        ReferenceEquals(first, next) ||
        (first is not null &&
         next is not null &&
         first.Status == next.Status &&
         first.IsEnabled == next.IsEnabled &&
         first.Amplitude == next.Amplitude &&
         first.AngularFrequency == next.AngularFrequency &&
         first.SpatialFrequency == next.SpatialFrequency);

    private void EnsureMultiDrawCapacity(int required)
    {
        if (_multiDrawIndexCounts.Length >= required)
            return;

        int capacity = Math.Max(
            required,
            Math.Max(16, _multiDrawIndexCounts.Length * 2));
        Array.Resize(ref _multiDrawIndexCounts, capacity);
        Array.Resize(ref _multiDrawIndexOffsets, capacity);
        Array.Resize(ref _multiDrawBaseVertices, capacity);
    }

    private void SetMultiDrawRange(int index, GlTexturedMesh mesh)
    {
        if ((uint)index >= (uint)_multiDrawIndexCounts.Length)
            throw new ArgumentOutOfRangeException(nameof(index));
        if (mesh.IndexCount == 0 || mesh.IndexCount > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(mesh.IndexCount));
        if ((mesh.IndexOffsetBytes & 3) != 0)
        {
            throw new InvalidOperationException(
                "A multi-draw element-buffer offset must be uint-aligned.");
        }

        _multiDrawIndexCounts[index] = mesh.IndexCount;
        _multiDrawIndexOffsets[index] = checked((nint)mesh.IndexOffsetBytes);
        _multiDrawBaseVertices[index] = mesh.BaseVertex;
    }

    private void Draw(
        GlTexturedDrawCommand command,
        Matrix4x4 viewProjection,
        DerivedMatrixState rsxMatrices,
        Vector3 cameraPosition,
        float editorTimeSeconds)
    {
        if (command.Mesh.InstanceCount == 0 &&
            TryGetWorldSurfaceBatchRuntime(
                command,
                out WorldSurfaceBatchRuntime worldBatch))
        {
            if (worldBatch.RunCount == 0)
                return;

            GlTexturedMesh visibleMesh = SelectWorldVisibleRun(
                command.Mesh,
                worldBatch.VisibleRuns[0]);
            int multiDrawCount = worldBatch.RunCount;
            if (multiDrawCount > 1)
            {
                EnsureMultiDrawCapacity(multiDrawCount);
                for (int runIndex = 0;
                     runIndex < multiDrawCount;
                     runIndex++)
                {
                    SetMultiDrawRange(
                        runIndex,
                        SelectWorldVisibleRun(
                            command.Mesh,
                            worldBatch.VisibleRuns[runIndex]));
                }
            }

            Draw(
                visibleMesh,
                viewProjection,
                rsxMatrices,
                cameraPosition,
                editorTimeSeconds,
                instanceIndex: null,
                multiDrawCount);
            return;
        }
        if (command.Mesh.InstanceCount == 0 &&
            !IsWorldMeshVisible(command.Mesh))
        {
            return;
        }
        if (command.InstanceIndex is int visibilityInstanceIndex &&
            !IsStaticInstanceVisible(
                command.Mesh,
                visibilityInstanceIndex))
        {
            return;
        }
        if (command.Mesh.InstanceCount != 0 &&
            command.InstanceIndex is null &&
            ResolveVisibleInstanceCount(command.Mesh) == 0)
        {
            return;
        }
        if (command.InstanceIndex is int instanceIndex &&
            (instanceIndex < 0 ||
             (uint)instanceIndex >= command.Mesh.InstanceCount))
        {
            throw new ArgumentOutOfRangeException(
                nameof(command),
                instanceIndex,
                "Textured draw command instance index is outside the mesh.");
        }

        Draw(
            command.Mesh,
            viewProjection,
            rsxMatrices,
            cameraPosition,
            editorTimeSeconds,
            command.InstanceIndex);
    }

    private bool TryGetWorldSurfaceBatchRuntime(
        GlTexturedDrawCommand command,
        out WorldSurfaceBatchRuntime runtime)
    {
        int batchIndex = command.WorldBatchIndex;
        int channelIndex = command.WorldReceiverChannelIndex;
        WorldSurfaceBatchRuntime? candidate = null;
        if (channelIndex >= 0 &&
            (uint)channelIndex < (uint)_worldReceiverVariants.Length)
        {
            WorldSurfaceBatchRuntime?[] channelBatches =
                _worldReceiverVariants[channelIndex].SurfaceBatches;
            if ((uint)batchIndex < (uint)channelBatches.Length)
                candidate = channelBatches[batchIndex];
        }
        else if ((uint)batchIndex < (uint)_worldSurfaceBatches.Length)
        {
            candidate = _worldSurfaceBatches[batchIndex];
        }

        if (candidate is not null)
        {
            runtime = candidate;
            return true;
        }

        runtime = null!;
        return false;
    }

    private static GlTexturedMesh SelectWorldVisibleRun(
        GlTexturedMesh mesh,
        MapRenderOpenGlWorldVisibleRun run) =>
        mesh with
        {
            IndexCount = checked((uint)run.IndexCount),
            IndexOffsetBytes = checked(
                mesh.IndexOffsetBytes +
                (nuint)run.FirstIndex * sizeof(uint)),
            WorldSurfaceIndex = -1
        };

    private void Draw(
        GlTexturedMesh mesh,
        Matrix4x4 viewProjection,
        DerivedMatrixState rsxMatrices,
        Vector3 cameraPosition,
        float editorTimeSeconds,
        int? instanceIndex,
        int multiDrawCount = 0)
    {
        if (mesh.IndexCount == 0)
            return;

        bool executeTranslatedAuthored = mesh.RsxProgram.Handle != 0;
        if (executeTranslatedAuthored &&
            !TryBindRuntimeSamplers(mesh))
        {
            // Runtime-authored receivers have no generic substitute. A
            // missing or stale atlas keeps the exact +3 pass out of this
            // frame rather than rendering a semantically different shader.
            return;
        }
        ApplyRenderState(mesh.State);
        if (executeTranslatedAuthored)
        {
            DerivedMatrixState drawMatrices =
                ResolveTranslatedDrawMatrices(
                    mesh,
                    instanceIndex,
                    rsxMatrices);
            _state.UseProgram(mesh.RsxProgram.Handle);
            ApplyStaticModelInstancingFrame(
                mesh.StaticModelProgramUniforms,
                rsxMatrices);
            ApplyTranslatedStaticComposition(
                mesh.StaticModelProgramUniforms,
                mesh,
                editorTimeSeconds);
            ApplyRsxConstantBindings(
                mesh.RsxConstantBindings,
                drawMatrices,
                editorTimeSeconds);
            foreach (GlRsxSamplerBinding binding in mesh.RsxSamplerBindings)
            {
                _state.ActiveTexture(binding.Destination);
                // A code sampler can leave an RSX-equivalent comparison
                // sampler object on this unit. Ordinary material samplers own
                // the texture descriptor state and must explicitly restore
                // texture-owned filtering/wrap behavior.
                _state.BindSampler(checked((uint)binding.Destination), 0);
                _state.BindTexture(binding.Target, binding.Texture);
            }
            BindRuntimeSamplers(mesh);
            _state.BindVertexArray(mesh.VertexArray);
            if (instanceIndex.HasValue)
            {
                ConfigureTexturedInstanceBase(
                    mesh.InstanceBuffer,
                    instanceIndex.Value,
                    MapRenderOpenGlStaticModelInstancedVertexComposer
                        .FirstPlacementAttribute);
            }
            IssueWorldDraws(mesh, multiDrawCount, instanceIndex);
            _state.ActiveTexture(0);
            return;
        }

        _state.UseProgram(_texturedProgram);
        _state.UniformMatrix4(_texturedViewProjectionLocation, viewProjection);
        _state.Uniform1(_texturedUseInstancingLocation, mesh.InstanceCount == 0 ? 0 : 1);
        _state.Uniform1(_texturedAlphaTestEnabledLocation, mesh.State.AlphaTestEnabled ? 1 : 0);
        _state.Uniform1(_texturedAlphaFuncLocation, unchecked((int)mesh.State.AlphaFunc));
        _state.Uniform1(_texturedAlphaRefLocation, mesh.State.AlphaRef / 255f);
        OpenGlRsxShaderPackerMode genericShaderPackerMode =
            OpenGlFixedFunctionEpilogue.ResolveShaderPackerMode(
                mesh.FragmentProgramControl,
                mesh.State,
                suppressForDiagnosticOutput: UseRsxVertexPlacementDiagnostic);
        _state.Uniform1(
            _texturedShaderPackerSrgbEnabledLocation,
            genericShaderPackerMode == OpenGlRsxShaderPackerMode
                .LinearToSrgbProgramEpilogue
                ? 1
                : 0);
        _state.Uniform1(
            _texturedLinearizeColorInputsLocation,
            mesh.ColorInputLinearizationMask);
        _state.Uniform1(
            _texturedPremultiplyAlphaLocation,
            OpenGlFixedFunctionEpilogue
                .RequiresPremultipliedSourceRgb(mesh.State)
                ? 1
                : 0);
        _state.Uniform1(_texturedHasLightmapLocation, mesh.LightmapTexture == 0 ? 0 : 1);
        bool expectsStaticModelLighting =
            mesh.UsesGenericStaticModelLighting;
        if (expectsStaticModelLighting &&
            _staticModelLightingAtlasTexture == 0)
        {
            throw new InvalidOperationException(
                "A generic static-model lighting draw reached execution " +
                "without its required immutable scene atlas.");
        }
        bool hasStaticModelLighting = expectsStaticModelLighting;
        _state.Uniform1(
            _texturedHasStaticModelLightingLocation,
            hasStaticModelLighting ? 1 : 0);
        MapRenderEditorPreviewLightingPlan? lighting =
            mesh.ReceivesEditorLighting
                ? _editorPreviewLighting
                : null;
        _state.Uniform1(_texturedLightingEnabledLocation, lighting is null ? 0 : 1);
        Vector3 ambientColor = lighting?.AmbientColor ?? Vector3.Zero;
        _state.Uniform3(
            _texturedAmbientColorLocation,
            ambientColor.X,
            ambientColor.Y,
            ambientColor.Z);
        bool hasDirectionalSunDiffuse =
            lighting?.HasDirectionalSun == true &&
            (!expectsStaticModelLighting ||
             mesh.GenericStaticModelLightingAddsDirectionalDiffuse);
        _state.Uniform1(
            _texturedHasDirectionalSunDiffuseLocation,
            hasDirectionalSunDiffuse ? 1 : 0);
        bool hasDirectionalSunSpecular =
            lighting?.HasDirectionalSun == true &&
            (!expectsStaticModelLighting ||
             mesh.GenericStaticModelLightingAddsDirectionalSpecular);
        _state.Uniform1(
            _texturedHasDirectionalSunSpecularLocation,
            hasDirectionalSunSpecular ? 1 : 0);
        Vector3 sunDirection =
            lighting?.DirectionalSunDirection ?? Vector3.Zero;
        _state.Uniform3(
            _texturedDirectionalSunDirectionLocation,
            sunDirection.X,
            sunDirection.Y,
            sunDirection.Z);
        Vector3 sunDiffuseColor = hasDirectionalSunDiffuse
            ? _editorPreviewDirectionalSunDiffuseColor
            : Vector3.Zero;
        _state.Uniform3(
            _texturedDirectionalSunDiffuseColorLocation,
            sunDiffuseColor.X,
            sunDiffuseColor.Y,
            sunDiffuseColor.Z);
        Vector3 sunSpecularColor = hasDirectionalSunSpecular
            ? _editorPreviewDirectionalSunSpecularColor
            : Vector3.Zero;
        _state.Uniform3(
            _texturedDirectionalSunSpecularColorLocation,
            sunSpecularColor.X,
            sunSpecularColor.Y,
            sunSpecularColor.Z);
        _state.Uniform3(
            _texturedCameraPositionLocation,
            cameraPosition.X,
            cameraPosition.Y,
            cameraPosition.Z);
        MapRenderGenericFogPlan genericFog =
            MapRenderGenericFogPlanner.Resolve(
                _editorPreviewFogRenderingEnabled,
                _editorPreviewGenericActiveFog,
                _editorPreviewAtmosphereEnabled
                    ? _editorPreviewAtmosphere
                    : null,
                OpenGlFixedFunctionEpilogue
                    .ConsumesLinearFogColor(genericShaderPackerMode));
        _state.Uniform1(
            _texturedFogEnabledLocation,
            genericFog.IsEnabled ? 1 : 0);
        _state.Uniform1(
            _texturedFogUseActiveStateLocation,
            genericFog.UsesActiveFog ? 1 : 0);
        _state.Uniform3(
            _texturedFogColorLocation,
            genericFog.FogColor.X,
            genericFog.FogColor.Y,
            genericFog.FogColor.Z);
        _state.Uniform1(
            _texturedFogStartLocation,
            genericFog.AtmosphereStartDistance);
        _state.Uniform1(
            _texturedFogEndLocation,
            genericFog.AtmosphereEndDistance);
        _state.Uniform1(
            _texturedFogMaxOpacityLocation,
            genericFog.AtmosphereMaxOpacity);
        _state.Uniform1(
            _texturedFogDistanceScaleLocation,
            genericFog.FogDistanceScale);
        _state.Uniform1(
            _texturedFogDistanceBiasLocation,
            genericFog.FogDistanceBias);
        _state.Uniform1(
            _texturedFogMinimumVisibilityLocation,
            genericFog.FogMinimumVisibility);
        _state.Uniform1(
            _texturedSunFogEnabledLocation,
            genericFog.SunFogEnabled ? 1 : 0);
        _state.Uniform3(
            _texturedSunFogColorLocation,
            genericFog.SunFogColor.X,
            genericFog.SunFogColor.Y,
            genericFog.SunFogColor.Z);
        _state.Uniform3(
            _texturedSunFogDirectionLocation,
            genericFog.SunFogDirection.X,
            genericFog.SunFogDirection.Y,
            genericFog.SunFogDirection.Z);
        _state.Uniform1(
            _texturedSunFogDistanceScaleLocation,
            genericFog.SunFogDistanceScale);
        _state.Uniform1(
            _texturedSunFogEndCosineLocation,
            genericFog.SunFogEndCosine);
        _state.Uniform1(
            _texturedSunFogAngularScaleLocation,
            genericFog.SunFogAngularScale);
        MapRenderEditorVegetationAnimationPlan? vegetation =
            mesh.VegetationAnimation;
        _state.Uniform1(
            _texturedVegetationWindEnabledLocation,
            vegetation?.IsEnabled == true ? 1 : 0);
        _state.Uniform1(
            _texturedVegetationTimeLocation,
            editorTimeSeconds);
        _state.Uniform1(
            _texturedVegetationAmplitudeLocation,
            vegetation?.Amplitude ?? 0f);
        _state.Uniform1(
            _texturedVegetationAngularFrequencyLocation,
            vegetation?.AngularFrequency ?? 0f);
        _state.Uniform1(
            _texturedVegetationSpatialFrequencyLocation,
            vegetation?.SpatialFrequency ?? 0f);
        _state.Uniform1(
            _texturedVegetationLocalMinimumHeightLocation,
            mesh.LocalMinimumHeight);
        _state.Uniform1(
            _texturedVegetationLocalHeightRangeLocation,
            mesh.LocalHeightRange);
        int normalTextureUnitStart = MapRenderScene.MaxColorLayerCount + 1;
        for (int index = 0; index < _texturedNormalSamplerLocations.Length; index++)
        {
            _state.Uniform1(
                _texturedHasNormalLocations[index],
                index < mesh.NormalTextures.Length &&
                mesh.NormalTextures[index] != 0 ? 1 : 0);
        }
        int specularTextureUnitStart =
            normalTextureUnitStart + _texturedNormalSamplerLocations.Length;
        for (int index = 0; index < _texturedSpecularSamplerLocations.Length; index++)
        {
            _state.Uniform1(
                _texturedHasSpecularLocations[index],
                index < mesh.SpecularTextures.Length &&
                mesh.SpecularTextures[index] != 0 ? 1 : 0);
        }
        _state.Uniform1(_texturedColorLayerCountLocation, mesh.ColorTextures.Length);
        for (int index = 0; index < _texturedBlendWeightComponentLocations.Length; index++)
        {
            int component = index < mesh.BlendWeightComponents.Length
                ? mesh.BlendWeightComponents[index]
                : -1;
            _state.Uniform1(_texturedBlendWeightComponentLocations[index], component);
        }
        // Every declared sampler must have a complete texture even when the dynamic
        // layer count prevents the shader from reading it. Some OpenGL drivers validate
        // all active sampler uniforms before evaluating that branch.
        for (int layerIndex = 0; layerIndex < MapRenderScene.MaxColorLayerCount; layerIndex++)
        {
            _state.ActiveTexture(layerIndex);
            _state.BindSampler(checked((uint)layerIndex), 0);
            uint texture = layerIndex < mesh.ColorTextures.Length
                ? mesh.ColorTextures[layerIndex]
                : mesh.ColorTextures[0];
            _state.BindTexture(TextureTarget.Texture2D, texture);
        }

        _state.ActiveTexture(MapRenderScene.MaxColorLayerCount);
        _state.BindSampler(
            checked((uint)MapRenderScene.MaxColorLayerCount),
            0);
        _state.BindTexture(
            TextureTarget.Texture2D,
            mesh.LightmapTexture == 0 ? mesh.ColorTextures[0] : mesh.LightmapTexture);
        for (int index = 0; index < _texturedNormalSamplerLocations.Length; index++)
        {
            int textureUnit = normalTextureUnitStart + index;
            _state.ActiveTexture(textureUnit);
            _state.BindSampler(checked((uint)textureUnit), 0);
            uint texture = index < mesh.NormalTextures.Length &&
                mesh.NormalTextures[index] != 0
                    ? mesh.NormalTextures[index]
                    : mesh.ColorTextures[0];
            _state.BindTexture(TextureTarget.Texture2D, texture);
        }
        for (int index = 0; index < _texturedSpecularSamplerLocations.Length; index++)
        {
            int textureUnit = specularTextureUnitStart + index;
            _state.ActiveTexture(textureUnit);
            _state.BindSampler(checked((uint)textureUnit), 0);
            uint texture = index < mesh.SpecularTextures.Length &&
                mesh.SpecularTextures[index] != 0
                    ? mesh.SpecularTextures[index]
                    : mesh.ColorTextures[0];
            _state.BindTexture(TextureTarget.Texture2D, texture);
        }
        _state.ActiveTexture(GenericStaticModelLightingTextureUnit);
        _state.BindSampler(
            GenericStaticModelLightingTextureUnit,
            0);
        _state.BindTexture(
            TextureTarget.Texture3D,
            _staticModelLightingAtlasTexture);
        _state.ActiveTexture(0);
        _state.BindVertexArray(mesh.VertexArray);
        if (mesh.InstanceCount == 0)
        {
            IssueWorldDraws(mesh, multiDrawCount);
        }
        else
        {
            if (instanceIndex.HasValue ||
                (_staticInstanceBuffers.TryGetValue(
                     mesh.InstanceBuffer,
                     out StaticInstanceBufferRuntime? runtime) &&
                 runtime.HasIsolatedDraw))
            {
                ConfigureTexturedInstanceBase(
                    mesh.InstanceBuffer,
                    instanceIndex ?? 0);
            }
            uint drawnInstanceCount = instanceIndex.HasValue
                ? 1u
                : ResolveVisibleInstanceCount(mesh);
            RecordDraw(
                mesh.IndexCount,
                drawnInstanceCount,
                PrimitiveType.Triangles);
            _gl.DrawElementsInstanced(
                PrimitiveType.Triangles,
                mesh.IndexCount,
                DrawElementsType.UnsignedInt,
                null,
                drawnInstanceCount);
        }
    }

    private DerivedMatrixState ResolveTranslatedDrawMatrices(
        GlTexturedMesh mesh,
        int? instanceIndex,
        DerivedMatrixState frameMatrices)
    {
        if (mesh.InstanceCount == 0)
        {
            if (instanceIndex.HasValue)
            {
                throw new InvalidOperationException(
                    "A translated world draw cannot carry a static instance index.");
            }
            return frameMatrices;
        }

        if (instanceIndex is not int index)
        {
            if (mesh.StaticModelProgramUniforms is not null)
                return frameMatrices;
            throw new InvalidOperationException(
                "A translated static-model draw requires one isolated placement.");
        }
        if (!_staticInstanceBuffers.TryGetValue(
                mesh.InstanceBuffer,
                out StaticInstanceBufferRuntime? runtime) ||
            (uint)index >= (uint)runtime.Instances.Length)
        {
            throw new InvalidOperationException(
                "A translated static-model draw lost its exact placement runtime.");
        }

        return MapRenderStaticModelDerivedMatrixResolver.WithPlacement(
            frameMatrices,
            runtime.Instances[index]);
    }

    private void ApplyRsxConstantBindings(
        IReadOnlyList<GlRsxConstantBinding> bindings,
        DerivedMatrixState rsxMatrices,
        float editorTimeSeconds)
    {
        if (!_authoredMaterials.TryApplyConstantBindings(
                bindings,
                rsxMatrices,
                editorTimeSeconds,
                (sourceRow, sceneLightIndex) =>
                    ResolveMapDynamicCodeConstant(
                        sourceRow,
                        sceneLightIndex,
                        rsxMatrices.EyeOffset),
                out string? blocker))
        {
            throw new InvalidOperationException(
                blocker ?? "Authored RSX constant execution failed.");
        }
    }

    private ShaderConstantValue? ResolveMapDynamicCodeConstant(
        ushort sourceRow,
        int? sceneLightIndex,
        Vector3 eyeOffset) => sourceRow switch
    {
        FrameDirectCodeConstants.DirectionalLightDirectionRowIndex
            when sceneLightIndex is int index =>
            ResolveSceneLightPositionCodeConstant(
                index,
                eyeOffset),
        FrameDirectCodeConstants.SunShadowSwitchPartitionRowIndex =>
            ResolveSunShadowProjectionCodeConstant(
                sourceRow,
                switchPartition: true),
        FrameDirectCodeConstants.SunShadowMapScaleRowIndex =>
            ResolveSunShadowProjectionCodeConstant(
                sourceRow,
                switchPartition: false),
        FrameDirectCodeConstants.ClipSpaceLookupScaleRowIndex =>
            _frameClipSpaceLookupScaleCodeConstant,
        FrameDirectCodeConstants.ClipSpaceLookupOffsetRowIndex =>
            _frameClipSpaceLookupOffsetCodeConstant,
        FrameDirectCodeConstants.ZNearRowIndex =>
            _frameZNearCodeConstant,
        _ => null
    };

    private ShaderConstantValue
        ResolveSceneLightPositionCodeConstant(
            int sceneLightIndex,
            Vector3 eyeOffset)
    {
        MapRenderWorldEvent20SceneLightFrameInput frame =
            _editorPreviewSceneLightFrame ??
            throw new InvalidOperationException(
                "A translated Event20 local-light draw has no immutable scene-light frame: " +
                (_editorPreviewSceneLightFrameFailure?.ToString() ??
                 "source unavailable"));
        if (_previewWorldSource is not { } source ||
            !source.AssetLookup.HasCanonicalAssetPoolRevision(
                frame.AssetPoolRevision))
        {
            throw new InvalidOperationException(
                $"A translated Event20 local-light draw retained stale canonical light assets from revision {frame.AssetPoolRevision}.");
        }

        return MapRenderWorldEvent20SceneLightDirectCodeConstantProducer
            .ProducePositionRow(frame, sceneLightIndex, eyeOffset)
            .Value;
    }

    private bool TryBindRuntimeSamplers(GlTexturedMesh mesh)
    {
        ShaderExecutionContract? execution = mesh.ShaderExecution;
        if (execution is null ||
            execution.RuntimeSamplerRequirements.Count == 0)
        {
            return true;
        }

        IReadOnlyList<ShaderRuntimeSamplerRequirement> requirements =
            execution.RuntimeSamplerRequirements;
        for (int requirementIndex = 0;
             requirementIndex < requirements.Count;
             requirementIndex++)
        {
            ShaderRuntimeSamplerRequirement requirement =
                requirements[requirementIndex];
            if (requirement.ResourceKind ==
                    ShaderRuntimeSamplerResourceKind
                        .ModelLightingAtlas &&
                requirement.Status ==
                    ShaderRuntimeSamplerRequirementStatus
                        .ImmutableSceneAtlasRequired)
            {
                if (_staticModelLightingAtlasTexture == 0)
                    return false;
                continue;
            }

            if (requirement.ResourceKind ==
                    ShaderRuntimeSamplerResourceKind
                        .ProcessedFloatZ &&
                requirement.Status ==
                    ShaderRuntimeSamplerRequirementStatus
                        .SameRevisionTextureRequired)
            {
                if (_currentProcessedFloatZFrame is not { } floatZFrame ||
                    LastFramePlan is not { } framePlan ||
                    floatZFrame.FrameRevision != framePlan.FrameRevision)
                {
                    return false;
                }
                continue;
            }

            if (requirement.ResourceKind !=
                    ShaderRuntimeSamplerResourceKind
                        .SunShadowAtlas ||
                requirement.Status !=
                    ShaderRuntimeSamplerRequirementStatus
                        .SameRevisionAtlasRequired ||
                _currentSunShadowReceiverFrame is not { } frame ||
                _sunShadowAtlas is null)
            {
                return false;
            }
            bool bindingReady = false;
            IReadOnlyList<ShaderRuntimeSamplerBinding> bindings =
                frame.RuntimeSamplerBindings;
            for (int bindingIndex = 0;
                 bindingIndex < bindings.Count;
                 bindingIndex++)
            {
                ShaderRuntimeSamplerBinding binding =
                    bindings[bindingIndex];
                if (binding.Destination == requirement.Destination &&
                    binding.ResourceKind == requirement.ResourceKind &&
                    binding.Revision == frame.Revision &&
                    binding.Status ==
                        ShaderRuntimeSamplerBindingStatus.Ready)
                {
                    bindingReady = true;
                    break;
                }
            }
            if (!bindingReady)
                return false;
        }
        return true;
    }

    private void BindRuntimeSamplers(GlTexturedMesh mesh)
    {
        ShaderExecutionContract? execution = mesh.ShaderExecution;
        if (execution is null ||
            execution.RuntimeSamplerRequirements.Count == 0)
        {
            return;
        }

        IReadOnlyList<ShaderRuntimeSamplerRequirement> requirements =
            execution.RuntimeSamplerRequirements;
        for (int requirementIndex = 0;
             requirementIndex < requirements.Count;
             requirementIndex++)
        {
            ShaderRuntimeSamplerRequirement requirement =
                requirements[requirementIndex];
            if (requirement.ResourceKind ==
                    ShaderRuntimeSamplerResourceKind
                        .ModelLightingAtlas &&
                requirement.Status ==
                    ShaderRuntimeSamplerRequirementStatus
                        .ImmutableSceneAtlasRequired)
            {
                if (_staticModelLightingAtlasTexture == 0)
                {
                    throw new InvalidOperationException(
                        "A model-lighting sampler draw reached execution without the immutable scene atlas.");
                }
                _state.ActiveTexture(requirement.Destination);
                // Texture-unit sampler objects override texture parameters.
                // Clear a possible shadow-compare sampler before publishing
                // the immutable linear/clamp 3D atlas on the same destination.
                _state.BindSampler(requirement.Destination, 0);
                _state.BindTexture(
                    TextureTarget.Texture3D,
                    _staticModelLightingAtlasTexture);
                continue;
            }

            if (requirement.ResourceKind ==
                    ShaderRuntimeSamplerResourceKind
                        .ProcessedFloatZ &&
                requirement.Status ==
                    ShaderRuntimeSamplerRequirementStatus
                        .SameRevisionTextureRequired)
            {
                MapRenderOpenGlProcessedFloatZFrame floatZPublication =
                    _currentProcessedFloatZFrame ??
                    throw new InvalidOperationException(
                        "A processed-FloatZ draw reached execution without the current target-8 publication.");
                if (LastFramePlan is not { } framePlan ||
                    floatZPublication.FrameRevision !=
                        framePlan.FrameRevision)
                {
                    throw new InvalidOperationException(
                        "A processed-FloatZ draw reached execution with a stale target-8 publication.");
                }
                _state.ActiveTexture(requirement.Destination);
                _state.BindSampler(
                    requirement.Destination,
                    floatZPublication.SamplerHandle);
                _state.BindTexture(
                    TextureTarget.Texture2D,
                    floatZPublication.TextureHandle);
                continue;
            }

            if (requirement.ResourceKind !=
                    ShaderRuntimeSamplerResourceKind
                        .SunShadowAtlas ||
                requirement.Status !=
                    ShaderRuntimeSamplerRequirementStatus
                        .SameRevisionAtlasRequired)
            {
                throw new InvalidOperationException(
                    $"Unsupported runtime sampler resource {requirement.ResourceKind}/{requirement.Status} reached OpenGL execution.");
            }

            MapRenderOpenGlSunShadowReceiverFrame frame =
                _currentSunShadowReceiverFrame ??
                throw new InvalidOperationException(
                    "A runtime sampler draw reached execution without a current sun-shadow receiver frame.");
            MapRenderOpenGlSunShadowAtlasBackend atlas = _sunShadowAtlas ??
                throw new InvalidOperationException(
                    "A runtime sampler draw reached execution without an OpenGL sun-shadow atlas.");
            atlas.BindReadyReceiver(
                frame.BackendFrame,
                requirement.Destination);
        }
    }

    private ShaderConstantValue
        ResolveSunShadowProjectionCodeConstant(
            ushort sourceRow,
            bool switchPartition)
    {
        MapRenderOpenGlSunShadowReceiverFrame frame =
            _currentSunShadowReceiverFrame ??
            throw new InvalidOperationException(
                $"Selected RSX execution reached sun-shadow row 0x{sourceRow:X2} without a same-revision atlas/projection publication.");
        Vector4 value = switchPartition
            ? frame.Projection.CodeConstants.SwitchPartition
            : frame.Projection.CodeConstants.ShadowMapScale;
        return new ShaderConstantValue(
            value.X,
            value.Y,
            value.Z,
            value.W);
    }

    private void IssueWorldDraws(
        GlTexturedMesh mesh,
        int multiDrawCount,
        int? instanceIndex = null)
    {
        if (mesh.InstanceCount != 0)
        {
            if (multiDrawCount > 1 || mesh.BaseVertex != 0)
            {
                throw new InvalidOperationException(
                    "Instanced translated static geometry cannot use a world multi-draw/base-vertex range.");
            }
            uint drawnInstanceCount = instanceIndex.HasValue
                ? 1u
                : ResolveVisibleInstanceCount(mesh);
            if (drawnInstanceCount == 0)
                return;
            RecordDraw(
                mesh.IndexCount,
                drawnInstanceCount,
                PrimitiveType.Triangles);
            _gl.DrawElementsInstanced(
                PrimitiveType.Triangles,
                mesh.IndexCount,
                DrawElementsType.UnsignedInt,
                (void*)mesh.IndexOffsetBytes,
                drawnInstanceCount);
            return;
        }

        if (multiDrawCount <= 1)
        {
            RecordDraw(
                mesh.IndexCount,
                instanceCount: 1,
                PrimitiveType.Triangles);
            _gl.DrawElementsBaseVertex(
                PrimitiveType.Triangles,
                mesh.IndexCount,
                DrawElementsType.UnsignedInt,
                (void*)mesh.IndexOffsetBytes,
                mesh.BaseVertex);
            return;
        }

        if (multiDrawCount > _multiDrawIndexCounts.Length ||
            multiDrawCount > _multiDrawIndexOffsets.Length ||
            multiDrawCount > _multiDrawBaseVertices.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(multiDrawCount));
        }

        long triangleCount = 0;
        for (int index = 0; index < multiDrawCount; index++)
        {
            triangleCount = checked(
                triangleCount + _multiDrawIndexCounts[index] / 3);
        }

        fixed (uint* indexCounts = _multiDrawIndexCounts)
        fixed (nint* indexOffsets = _multiDrawIndexOffsets)
        fixed (int* baseVertices = _multiDrawBaseVertices)
        {
            _gl.MultiDrawElementsBaseVertex(
                PrimitiveType.Triangles,
                indexCounts,
                DrawElementsType.UnsignedInt,
                (void**)indexOffsets,
                (uint)multiDrawCount,
                baseVertices);
        }

        _frameDrawCalls++;
        _frameLogicalDrawCommands += multiDrawCount;
        _frameMultiDrawApiCalls++;
        RecordPhaseLogicalDrawCommands(multiDrawCount);
        _frameTelemetry.AddCounter(
            MapRenderFrameCounter.MultiDrawCommands,
            multiDrawCount);
        _frameTelemetry.AddCounter(
            MapRenderFrameCounter.Triangles,
            triangleCount);
        if (_activeGpuDrawPhase is MapRenderGpuPhase gpuPhase)
        {
            _frameTelemetry.AddGpuPhaseWork(
                gpuPhase,
                drawCalls: 1,
                triangleCount);
        }
    }

    private void RecordDraw(
        uint indexCount,
        uint instanceCount,
        PrimitiveType primitiveType)
    {
        _frameDrawCalls++;
        _frameLogicalDrawCommands++;
        RecordPhaseLogicalDrawCommands(1);
        if (primitiveType == PrimitiveType.Triangles)
        {
            long triangles = checked(
                (long)(indexCount / 3) * instanceCount);
            _frameTelemetry.AddCounter(
                MapRenderFrameCounter.Triangles,
                triangles);
            if (_activeGpuDrawPhase is MapRenderGpuPhase gpuPhase)
            {
                _frameTelemetry.AddGpuPhaseWork(
                    gpuPhase,
                    drawCalls: 1,
                    triangles);
            }
        }
        else if (_activeGpuDrawPhase is MapRenderGpuPhase gpuPhase)
        {
            _frameTelemetry.AddGpuPhaseWork(
                gpuPhase,
                drawCalls: 1,
                triangles: 0);
        }
    }

    /// <summary>
    /// Attributes semantic draw commands at the same point the GL draw is
    /// issued. This preserves multi-draw command cardinality and records the
    /// depth pass only when its first command actually executes.
    /// </summary>
    private void RecordPhaseLogicalDrawCommands(int logicalDrawCount)
    {
        if (logicalDrawCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(logicalDrawCount));

        switch (_activeGpuDrawPhase)
        {
            case MapRenderGpuPhase.SunShadow:
                _frameTelemetry.AddCounter(
                    MapRenderFrameCounter.SunShadowLogicalDrawCommands,
                    logicalDrawCount);
                break;
            case MapRenderGpuPhase.DepthPrepass:
                if (!_frameDepthPassRecorded)
                {
                    _frameTelemetry.AddCounter(
                        MapRenderFrameCounter.Passes);
                    _frameDepthPassRecorded = true;
                }
                break;
            case MapRenderGpuPhase.Presentation:
                _frameTelemetry.AddCounter(
                    MapRenderFrameCounter.PostLogicalDrawCommands,
                    logicalDrawCount);
                break;
        }
    }

    private GpuDrawPhaseScope BeginGpuDrawPhase(MapRenderGpuPhase phase)
    {
        if (_activeGpuDrawPhase.HasValue)
        {
            throw new InvalidOperationException(
                $"GPU draw phase {_activeGpuDrawPhase.Value} is already active.");
        }

        _activeGpuDrawPhase = phase;
        return new GpuDrawPhaseScope(this, phase);
    }

    private void EndGpuDrawPhase(MapRenderGpuPhase phase)
    {
        if (_activeGpuDrawPhase == phase)
            _activeGpuDrawPhase = null;
    }

    private readonly struct GpuDrawPhaseScope : IDisposable
    {
        private readonly SilkOpenGlMapRenderer? _owner;
        private readonly MapRenderGpuPhase _phase;

        public GpuDrawPhaseScope(
            SilkOpenGlMapRenderer owner,
            MapRenderGpuPhase phase)
        {
            _owner = owner;
            _phase = phase;
        }

        public void Dispose() => _owner?.EndGpuDrawPhase(_phase);
    }





}
