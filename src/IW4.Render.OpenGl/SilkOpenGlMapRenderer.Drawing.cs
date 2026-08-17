using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;
using IW4.Render.Diagnostics;
using IW4.Render.EditorPreview;
using IW4.Render.Execution;
using IW4.Render.Geometry;
using IW4.Render.Lighting;
using IW4.Render.Scheduling.Lighting;
using IW4.Render.Scheduling.Shadows;
using IW4.Render.SceneBuilding;
using IW4.Render.World;
using IW4.Render.Shaders;
using IW4.Render.Techniques;
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
        MapRenderOpenGlStencilTargetContract? stencilTargetContract,
        Matrix4x4 viewProjection,
        in DerivedMatrixState rsxMatrices,
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
                    destinationIndex: 0,
                    mergeAdjacentRanges: false);
                long depthSortKey =
                    ResolveWorldDepthMultiDrawSortKey(group) ??
                    throw new InvalidOperationException(
                        "A depth multi-draw candidate has no immutable compatibility key.");
                int groupedDrawCount = 1;
                while (groupIndex + groupedDrawCount < groups.Count)
                {
                    int nextGroupIndex = groupIndex + groupedDrawCount;
                    MapRenderEditorDrawGroup<GlTexturedDrawCommand> nextGroup =
                        groups[nextGroupIndex];
                    IReadOnlyList<GlTexturedDrawCommand> nextCommands =
                        SelectStandardDepthPrepassCommands(
                            nextGroup,
                            out MapRenderEditorDepthPrepassPlan? nextPlan);
                    if (nextPlan is null ||
                        nextCommands.Count != 1 ||
                        ResolveWorldDepthMultiDrawSortKey(nextGroup) !=
                            depthSortKey)
                    {
                        break;
                    }
                    if (!IsTexturedDrawGroupVisibleForFrame(nextGroup))
                    {
                        groupedDrawCount++;
                        continue;
                    }
                    if (!TryGetDepthMultiDrawWorldMesh(
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
                            multiDrawCount,
                            mergeAdjacentRanges: false));
                    groupedDrawCount++;
                }

                DrawDepthPrepass(
                    in firstVisibleMesh,
                    plan,
                    stencilTargetContract,
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
            ReadOnlySpan<GlTexturedDrawCommand> commandSpan =
                group.AuthoredPassSpan;
            for (int commandIndex = 0;
                 commandIndex < commandSpan.Length;
                 commandIndex++)
            {
                ref readonly GlTexturedDrawCommand command =
                    ref commandSpan[commandIndex];
                DrawDepthPrepass(
                    in command,
                    plan,
                    stencilTargetContract,
                    viewProjection,
                    rsxMatrices,
                    editorTimeSeconds);
            }
        }
    }

    /// <summary>
    /// The standard color and depth owners can share depth coverage on Apple
    /// Silicon only when the subsequent opaque color replay is guaranteed to
    /// establish precisely the same final depth buffer.
    ///
    /// This is deliberately a frame-wide all-or-nothing proof. Mixed depth
    /// ownership would make an omitted command interact with an earlier
    /// retained prepass draw, so any unknown command leaves the complete
    /// existing prepass intact.
    /// </summary>
    private bool CanElideAppleSiliconDepthPrepass(
        IReadOnlyList<MapRenderEditorDrawGroup<GlTexturedDrawCommand>>
            colorGroups,
        IReadOnlyList<MapRenderEditorDrawGroup<GlTexturedDrawCommand>>
            depthGroups,
        bool requiresVisibleProcessedFloatZ)
    {
        ArgumentNullException.ThrowIfNull(colorGroups);
        ArgumentNullException.ThrowIfNull(depthGroups);
        _appleDepthFusionOwnerGroups.Clear();
        _appleDepthFusionOwnerGroupScratch.Clear();

        if (!OperatingSystem.IsMacOS() ||
            RuntimeInformation.ProcessArchitecture != Architecture.Arm64 ||
            requiresVisibleProcessedFloatZ)
        {
            return false;
        }

        EnsureAppleDepthFusionOwnerMap(colorGroups, depthGroups);
        bool foundVisibleStandardDepthCommand = false;
        for (int depthGroupIndex = 0;
             depthGroupIndex < depthGroups.Count;
             depthGroupIndex++)
        {
            MapRenderEditorDrawGroup<GlTexturedDrawCommand> depthGroup =
                depthGroups[depthGroupIndex];
            if (!IsTexturedDrawGroupVisibleForFrame(depthGroup))
                continue;

            IReadOnlyList<GlTexturedDrawCommand> depthCommands =
                SelectStandardDepthPrepassCommands(
                    depthGroup,
                    out MapRenderEditorDepthPrepassPlan? plan);
            if (plan is null ||
                depthCommands.Count == 0 ||
                !TryGetCachedDepthColorOwner(
                    depthGroup,
                    colorGroups,
                    out int colorOwnerIndex))
            {
                return false;
            }

            MapRenderEditorDrawGroup<GlTexturedDrawCommand> colorOwner =
                colorGroups[colorOwnerIndex];

            ReadOnlySpan<GlTexturedDrawCommand> commandSpan =
                depthGroup.AuthoredPassSpan;
            for (int commandIndex = 0;
                 commandIndex < commandSpan.Length;
                 commandIndex++)
            {
                ref readonly GlTexturedDrawCommand command =
                    ref commandSpan[commandIndex];
                if (!IsDepthFusionCommandEquivalent(in command, plan))
                    return false;

                foundVisibleStandardDepthCommand = true;
            }

            _appleDepthFusionOwnerGroupScratch.Add(colorOwner);
        }

        if (!foundVisibleStandardDepthCommand ||
            !PublishAppleDepthFusionOwners() ||
            !AreVisibleOpaqueColorCommandsDepthEquivalent(colorGroups))
        {
            _appleDepthFusionOwnerGroups.Clear();
            return false;
        }

        return true;
    }

    private bool AreVisibleOpaqueColorCommandsDepthEquivalent(
        IReadOnlyList<MapRenderEditorDrawGroup<GlTexturedDrawCommand>>
            colorGroups)
    {
        for (int groupIndex = 0;
             groupIndex < colorGroups.Count;
             groupIndex++)
        {
            if (!_texturedDrawGroupVisibilityScratch[groupIndex] ||
                colorGroups[groupIndex].Bucket !=
                    MapRenderEditorDrawBucket.Opaque)
            {
                continue;
            }
            if (!_texturedDrawGroupColorReadinessScratch[groupIndex])
                return false;

            ReadOnlySpan<GlTexturedDrawCommand> commands =
                colorGroups[groupIndex].AuthoredPassSpan;
            for (int commandIndex = 0;
                 commandIndex < commands.Length;
                 commandIndex++)
            {
                ref readonly GlTexturedDrawCommand command =
                    ref commands[commandIndex];
                bool replacesStandardDepthOwner =
                    _appleDepthFusionOwnerGroups.Contains(
                        colorGroups[groupIndex]);
                RenderState effectiveState = command.Mesh.State.HasState
                    ? command.Mesh.State
                    : RenderState.Default;
                if (!HasOpaqueColorDepthEquivalentState(in command) ||
                    (!replacesStandardDepthOwner &&
                     !effectiveState.DepthWriteEnabled))
                    return false;
            }
        }

        return true;
    }

    private bool PublishAppleDepthFusionOwners()
    {
        foreach (MapRenderEditorDrawGroup<GlTexturedDrawCommand> owner in
                 _appleDepthFusionOwnerGroupScratch)
            _appleDepthFusionOwnerGroups.Add(owner);

        return _appleDepthFusionOwnerGroups.Count != 0;
    }

    private void EnsureAppleDepthFusionOwnerMap(
        IReadOnlyList<MapRenderEditorDrawGroup<GlTexturedDrawCommand>>
            colorGroups,
        IReadOnlyList<MapRenderEditorDrawGroup<GlTexturedDrawCommand>>
            depthGroups)
    {
        if (ReferenceEquals(_appleDepthFusionOwnerColorGroups, colorGroups) &&
            ReferenceEquals(_appleDepthFusionOwnerDepthGroups, depthGroups))
        {
            return;
        }

        _appleDepthFusionOwnerColorGroups = colorGroups;
        _appleDepthFusionOwnerDepthGroups = depthGroups;
        _appleDepthFusionColorOwnerIndexByDepthGroup.Clear();
        _appleDepthFusionColorOwnerIndexByDepthGroup.EnsureCapacity(
            depthGroups.Count);
        if (_appleDepthFusionOpaqueColorNextByIndex.Length <
            colorGroups.Count)
        {
            Array.Resize(
                ref _appleDepthFusionOpaqueColorNextByIndex,
                colorGroups.Count);
        }

        _appleDepthFusionOpaqueColorHeadBySourceOrdinal.Clear();
        _appleDepthFusionOpaqueColorHeadBySourceOrdinal.EnsureCapacity(
            colorGroups.Count);
        for (int colorGroupIndex = 0;
             colorGroupIndex < colorGroups.Count;
             colorGroupIndex++)
        {
            _appleDepthFusionOpaqueColorNextByIndex[colorGroupIndex] = -1;
            MapRenderEditorDrawGroup<GlTexturedDrawCommand> candidate =
                colorGroups[colorGroupIndex];
            if (candidate.Bucket != MapRenderEditorDrawBucket.Opaque)
                continue;

            if (_appleDepthFusionOpaqueColorHeadBySourceOrdinal.TryGetValue(
                    candidate.SourceOrdinal,
                    out int headIndex))
            {
                _appleDepthFusionOpaqueColorNextByIndex[colorGroupIndex] =
                    headIndex;
            }

            _appleDepthFusionOpaqueColorHeadBySourceOrdinal[
                candidate.SourceOrdinal] = colorGroupIndex;
        }

        for (int depthGroupIndex = 0;
             depthGroupIndex < depthGroups.Count;
             depthGroupIndex++)
        {
            MapRenderEditorDrawGroup<GlTexturedDrawCommand> depthGroup =
                depthGroups[depthGroupIndex];
            int colorOwnerIndex = -1;
            if (_appleDepthFusionOpaqueColorHeadBySourceOrdinal.TryGetValue(
                    depthGroup.SourceOrdinal,
                    out int colorGroupIndex))
            {
                for (; colorGroupIndex >= 0;
                     colorGroupIndex =
                         _appleDepthFusionOpaqueColorNextByIndex[
                             colorGroupIndex])
                {
                    MapRenderEditorDrawGroup<GlTexturedDrawCommand>
                        candidate = colorGroups[colorGroupIndex];
                    if (!HasUniqueDepthColorGeometryMatch(
                            depthGroup,
                            candidate))
                    {
                        continue;
                    }

                    if (colorOwnerIndex >= 0)
                    {
                        colorOwnerIndex = -1;
                        break;
                    }

                    colorOwnerIndex = colorGroupIndex;
                }
            }

            _appleDepthFusionColorOwnerIndexByDepthGroup[depthGroup] =
                colorOwnerIndex;
        }
    }

    private bool TryGetCachedDepthColorOwner(
        MapRenderEditorDrawGroup<GlTexturedDrawCommand> depthGroup,
        IReadOnlyList<MapRenderEditorDrawGroup<GlTexturedDrawCommand>>
            colorGroups,
        out int colorOwnerIndex)
    {
        if (!_appleDepthFusionColorOwnerIndexByDepthGroup.TryGetValue(
                depthGroup,
                out colorOwnerIndex) ||
            colorOwnerIndex < 0 ||
            colorOwnerIndex >= colorGroups.Count)
        {
            colorOwnerIndex = -1;
            return false;
        }

        if (!_texturedDrawGroupVisibilityScratch[colorOwnerIndex] ||
            !_texturedDrawGroupColorReadinessScratch[colorOwnerIndex])
            return false;

        return true;
    }

    private static bool HasUniqueDepthColorGeometryMatch(
        MapRenderEditorDrawGroup<GlTexturedDrawCommand> depthGroup,
        MapRenderEditorDrawGroup<GlTexturedDrawCommand> colorOwner)
    {
        ReadOnlySpan<GlTexturedDrawCommand> depthCommands =
            depthGroup.AuthoredPassSpan;
        ReadOnlySpan<GlTexturedDrawCommand> colorCommands =
            colorOwner.AuthoredPassSpan;
        for (int depthCommandIndex = 0;
             depthCommandIndex < depthCommands.Length;
             depthCommandIndex++)
        {
            int matches = 0;
            ref readonly GlTexturedDrawCommand depthCommand =
                ref depthCommands[depthCommandIndex];
            for (int colorCommandIndex = 0;
                 colorCommandIndex < colorCommands.Length;
                 colorCommandIndex++)
            {
                if (!depthCommand.Equals(colorCommands[colorCommandIndex]))
                    continue;

                if (++matches != 1)
                    return false;
            }

            if (matches != 1)
                return false;
        }

        return depthCommands.Length != 0;
    }

    private bool IsDepthFusionCommandEquivalent(
        in GlTexturedDrawCommand command,
        MapRenderEditorDepthPrepassPlan plan)
    {
        GlTexturedMesh mesh = command.Mesh;
        if (command.InstanceIndex.HasValue ||
            mesh.IndexCount == 0 ||
            mesh.VertexArray == 0 ||
            !HasOpaqueColorDepthEquivalentState(in command) ||
            !HasMatchingStandardDepthRasterState(mesh.State, plan.State))
        {
            return false;
        }

        if (mesh.RsxProgram.Handle != 0 &&
            (!mesh.HasCertifiedTranslatedDepthFusion ||
             mesh.DepthPrepassRsxProgram.Handle == 0 ||
             !TryBindRuntimeSamplers(in mesh)))
        {
            return false;
        }

        if (mesh.InstanceCount == 0)
            return true;

        // Whole-batch static draws use the same active instance-buffer layout
        // in both generic programs. Isolated and receiver-compacted paths can
        // rewrite attribute bases, so they stay on the explicit depth owner.
        return _staticInstanceBuffers.TryGetValue(
                   mesh.InstanceBuffer,
                   out StaticInstanceBufferRuntime? runtime) &&
            runtime.HasWholeBatchDraw &&
            !runtime.HasIsolatedDraw &&
            !runtime.HasCompactedReceiverSourceLayout &&
            !runtime.HasCommittedReceiverDrawCompaction &&
            !runtime.HasLivePlacementChangePending;
    }

    private static bool HasOpaqueColorDepthEquivalentState(
        in GlTexturedDrawCommand command)
    {
        GlTexturedMesh mesh = command.Mesh;
        RenderState state = mesh.State.HasState
            ? mesh.State
            : RenderState.Default;
        return state.ColorMask == RsxColorMask.Rgba &&
            !state.AlphaTestEnabled &&
            !state.BlendEnabled &&
            !state.StencilEnabled &&
            state.DepthTestEnabled &&
            state.DepthFunc == RsxCompareFunction.LessThanOrEqual &&
            state.PolygonMode == RsxPolygonMode.Fill &&
            state.PolygonOffsetMode == RenderPolygonOffsetMode.Disabled &&
            mesh.ShaderExecution?.FragmentDepthExportEnabled != true &&
            mesh.ShaderExecution?.FragmentProgramIr?.ProgramControl.UsesKill
                != true;
    }

    private static bool HasMatchingStandardDepthRasterState(
        RenderState colorState,
        RenderState depthState)
    {
        RenderState effectiveColorState = colorState.HasState
            ? colorState
            : RenderState.Default;
        RenderState effectiveDepthState = depthState.HasState
            ? depthState
            : RenderState.Default;
        CullMode? colorCull = Cull.Resolve(effectiveColorState);
        CullMode? depthCull = Cull.Resolve(effectiveDepthState);
        return effectiveDepthState.ColorMask == RsxColorMask.None &&
            !effectiveDepthState.AlphaTestEnabled &&
            !effectiveDepthState.BlendEnabled &&
            !effectiveDepthState.StencilEnabled &&
            effectiveDepthState.DepthTestEnabled &&
            effectiveDepthState.DepthWriteEnabled &&
            effectiveDepthState.DepthFunc ==
                RsxCompareFunction.LessThanOrEqual &&
            effectiveDepthState.PolygonMode == RsxPolygonMode.Fill &&
            effectiveDepthState.PolygonOffsetMode ==
                RenderPolygonOffsetMode.Disabled &&
            colorCull is CullMode.Front or CullMode.Back &&
            colorCull == depthCull;
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
        ReadOnlySpan<GlTexturedDrawCommand> commands =
            group.AuthoredPassSpan;
        for (int commandIndex = 0;
             commandIndex < commands.Length;
             commandIndex++)
        {
            ref readonly GlTexturedDrawCommand command =
                ref commands[commandIndex];
            if (IsTexturedDrawCommandVisible(in command))
                return true;
        }
        return false;
    }

    private bool IsTexturedDrawCommandVisible(
        in GlTexturedDrawCommand command)
    {
        GlTexturedMesh mesh = command.Mesh;
        if (mesh.InstanceCount == 0)
        {
            return TryGetWorldSurfaceBatchRuntime(
                    in command,
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
        ReadOnlySpan<GlTexturedDrawCommand> commands =
            group.AuthoredPassSpan;
        if (group.Bucket != MapRenderEditorDrawBucket.Opaque ||
            commands.Length == 0 ||
            commands[0].Mesh.EditorDepthPrepass is not
                { Program: MapRenderEditorDepthPrepassProgram.TransformOnlyNull }
                candidate)
        {
            return [];
        }

        for (int passIndex = 1;
             passIndex < commands.Length;
             passIndex++)
        {
            ref readonly GlTexturedDrawCommand command =
                ref commands[passIndex];
            if (command.Mesh.EditorDepthPrepass !=
                candidate)
            {
                return [];
            }
        }

        plan = candidate;
        return group.AuthoredPasses;
    }

    private void DrawDepthPrepass(
        in GlTexturedDrawCommand command,
        MapRenderEditorDepthPrepassPlan plan,
        MapRenderOpenGlStencilTargetContract? stencilTargetContract,
        Matrix4x4 viewProjection,
        in DerivedMatrixState rsxMatrices,
        float editorTimeSeconds)
    {
        GlTexturedMesh mesh = command.Mesh;
        if (mesh.InstanceCount == 0 &&
            TryGetWorldSurfaceBatchRuntime(
                in command,
                out WorldSurfaceBatchRuntime worldBatch))
        {
            if (worldBatch.RunCount == 0)
                return;

            GlTexturedMesh visibleMesh = SelectWorldVisibleRun(
                in mesh,
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
                            in mesh,
                            worldBatch.VisibleRuns[runIndex]));
                }
            }

            DrawDepthPrepass(
                in visibleMesh,
                plan,
                stencilTargetContract,
                viewProjection,
                in rsxMatrices,
                editorTimeSeconds,
                instanceIndex: null,
                multiDrawCount);
            return;
        }
        if (mesh.InstanceCount == 0 &&
            !IsWorldMeshVisible(mesh))
        {
            return;
        }
        if (command.InstanceIndex is int visibilityInstanceIndex &&
            !IsStaticInstanceVisible(
                mesh,
                visibilityInstanceIndex))
        {
            return;
        }
        if (mesh.InstanceCount != 0 &&
            command.InstanceIndex is null &&
            ResolveVisibleInstanceCount(mesh) == 0)
        {
            return;
        }

        DrawDepthPrepass(
            in mesh,
            plan,
            stencilTargetContract,
            viewProjection,
            in rsxMatrices,
            editorTimeSeconds,
            command.InstanceIndex);
    }

    private void DrawDepthPrepass(
        in GlTexturedMesh mesh,
        MapRenderEditorDepthPrepassPlan plan,
        MapRenderOpenGlStencilTargetContract? stencilTargetContract,
        Matrix4x4 viewProjection,
        in DerivedMatrixState rsxMatrices,
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
                    in mesh,
                    instanceIndex,
                    in rsxMatrices);
            ApplyRenderState(plan.State, stencilTargetContract);
            _state.UseProgram(mesh.DepthPrepassRsxProgram.Handle);
            ApplyTranslatedStaticComposition(
                mesh.DepthStaticModelProgramUniforms,
                mesh,
                editorTimeSeconds);
            ApplyRsxConstantBindings(
                mesh.DepthPrepassRsxConstantBindings,
                in drawMatrices,
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
            IssueWorldDraws(in mesh, multiDrawCount, instanceIndex);
            return;
        }

        ApplyRenderState(plan.State, stencilTargetContract);
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
        _state.Uniform4(
            _depthPrepassVegetationParametersLocation,
            vegetation?.IsEnabled == true ? 1f : 0f,
            vegetation?.Amplitude ?? 0f,
            vegetation?.AngularFrequency ?? 0f,
            vegetation?.SpatialFrequency ?? 0f);
        _state.Uniform1(
            _depthPrepassVegetationTimeLocation,
            editorTimeSeconds);
        _state.Uniform4(
            _depthPrepassVegetationBoundsLocation,
            mesh.LocalMinimumHeight,
            mesh.LocalHeightRange,
            0f,
            0f);
        _state.BindVertexArray(mesh.VertexArray);
        if (mesh.InstanceCount == 0)
        {
            IssueWorldDraws(in mesh, multiDrawCount);
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
            mesh.IndexType,
            null,
            drawnInstanceCount);
    }

    private void DrawVisibleTexturedGroups(
        IReadOnlyList<MapRenderEditorDrawGroup<GlTexturedDrawCommand>> groups,
        MapRenderOpenGlStencilTargetContract? stencilTargetContract,
        Matrix4x4 viewProjection,
        in DerivedMatrixState rsxMatrices,
        Vector3 cameraPosition,
        float editorTimeSeconds)
    {
        Span<int> phaseRunCounts = stackalloc int[GpuPhaseCount];
        phaseRunCounts.Clear();
        int previousPhaseIndex = -1;
        for (int index = 0; index < groups.Count; index++)
        {
            if (!_texturedDrawGroupVisibilityScratch[index] ||
                !_texturedDrawGroupColorReadinessScratch[index])
            {
                continue;
            }
            int phaseIndex =
                (int)_texturedDrawGroupGpuPhaseScratch[index];
            if (phaseIndex == previousPhaseIndex)
                continue;

            phaseRunCounts[phaseIndex]++;
            previousPhaseIndex = phaseIndex;
        }

        MapRenderOpenGlGpuPhaseScope gpuTimingScope = default;
        GpuDrawPhaseScope drawWorkScope = default;
        MapRenderCpuPhaseScope cpuTimingScope = default;
        int activePhaseIndex = -1;
        MapRenderCpuPhase activeCpuPhase = default;
        bool hasActiveCpuPhase = false;
        long? activeStaticExecutionBundleKey = null;
        try
        {
            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                MapRenderEditorDrawGroup<GlTexturedDrawCommand> group =
                    groups[groupIndex];
                if (!_texturedDrawGroupVisibilityScratch[groupIndex] ||
                    !_texturedDrawGroupColorReadinessScratch[groupIndex])
                {
                    activeStaticExecutionBundleKey = null;
                    continue;
                }
                MapRenderGpuPhase gpuPhase =
                    _texturedDrawGroupGpuPhaseScratch[groupIndex];
                int gpuPhaseIndex = (int)gpuPhase;
                if (gpuPhaseIndex != activePhaseIndex)
                {
                    activeStaticExecutionBundleKey = null;
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

                MapRenderCpuPhase cpuPhase =
                    group.AuthoredPassSpan[0].Mesh.InstanceCount == 0
                        ? MapRenderCpuPhase.WorldGeometry
                        : MapRenderCpuPhase.StaticModels;
                if (!hasActiveCpuPhase || cpuPhase != activeCpuPhase)
                {
                    cpuTimingScope.Dispose();
                    cpuTimingScope = _frameTelemetry.BeginCpuPhase(cpuPhase);
                    activeCpuPhase = cpuPhase;
                    hasActiveCpuPhase = true;
                }
                if (TryDrawCompactedStaticReceiverGroup(
                        groupIndex,
                        group,
                        stencilTargetContract,
                        viewProjection,
                        in rsxMatrices,
                        cameraPosition,
                        editorTimeSeconds))
                {
                    activeStaticExecutionBundleKey = null;
                    continue;
                }
                if (!TryGetMultiDrawWorldMesh(
                        group,
                        out GlTexturedMesh firstMesh,
                        out WorldSurfaceBatchRuntime? firstSurfaceBatch))
                {
                    ReadOnlySpan<GlTexturedDrawCommand> authoredPasses =
                        group.AuthoredPassSpan;
                    bool isStaticExecutionBundleCandidate =
                        TryGetStaticExecutionBundleKey(
                            group,
                            out long staticExecutionBundleKey);
                    long drawCallsBeforeGroup = _frameDrawCalls;
                    for (int commandIndex = 0;
                         commandIndex < authoredPasses.Length;
                         commandIndex++)
                    {
                        ref readonly GlTexturedDrawCommand command =
                            ref authoredPasses[commandIndex];
                        Draw(
                            in command,
                            stencilTargetContract,
                            viewProjection,
                            in rsxMatrices,
                            cameraPosition,
                            editorTimeSeconds,
                            forceDepthWrite:
                                _appleDepthFusionOwnerGroups.Contains(group));
                    }
                    if (isStaticExecutionBundleCandidate &&
                        _frameDrawCalls > drawCallsBeforeGroup)
                    {
                        if (activeStaticExecutionBundleKey ==
                            staticExecutionBundleKey)
                        {
                            _frameStaticExecutionBundleReuses++;
                        }
                        else
                        {
                            _frameStaticExecutionBundleBinds++;
                        }
                        activeStaticExecutionBundleKey =
                            staticExecutionBundleKey;
                    }
                    else
                    {
                        activeStaticExecutionBundleKey = null;
                    }
                    continue;
                }

                activeStaticExecutionBundleKey = null;

                GlTexturedMesh firstVisibleMesh =
                    SelectFirstWorldMultiDrawMesh(
                        firstMesh,
                        firstSurfaceBatch);
                int multiDrawCount = AppendWorldMultiDrawRanges(
                    firstMesh,
                    firstSurfaceBatch,
                    destinationIndex: 0,
                    mergeAdjacentRanges: true);
                long colorSortKey = group.CameraIndependentSortKey ??
                    throw new InvalidOperationException(
                        "A color multi-draw candidate has no immutable compatibility key.");
                int consumedGroupCount = 1;
                bool aggregateEstablishesFusedDepth =
                    _appleDepthFusionOwnerGroups.Contains(group);
                while (groupIndex + consumedGroupCount < groups.Count)
                {
                    int nextGroupIndex = groupIndex + consumedGroupCount;
                    MapRenderEditorDrawGroup<GlTexturedDrawCommand> nextGroup =
                        groups[nextGroupIndex];
                    if (nextGroup.CameraIndependentSortKey != colorSortKey ||
                        nextGroup.Bucket != group.Bucket)
                    {
                        break;
                    }
                    if (!_texturedDrawGroupVisibilityScratch[nextGroupIndex] ||
                        !_texturedDrawGroupColorReadinessScratch[nextGroupIndex])
                    {
                        consumedGroupCount++;
                        continue;
                    }
                    if (_texturedDrawGroupGpuPhaseScratch[nextGroupIndex] !=
                            gpuPhase ||
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
                            multiDrawCount,
                            mergeAdjacentRanges: true));
                    aggregateEstablishesFusedDepth |=
                        _appleDepthFusionOwnerGroups.Contains(nextGroup);
                    consumedGroupCount++;
                }

                if (multiDrawCount == 1)
                {
                    firstVisibleMesh = firstVisibleMesh with
                    {
                        IndexCount = _multiDrawIndexCounts[0],
                        IndexOffsetBytes = checked(
                            (nuint)_multiDrawIndexOffsets[0]),
                        BaseVertex = _multiDrawBaseVertices[0]
                    };
                }

                Draw(
                    in firstVisibleMesh,
                    stencilTargetContract,
                    viewProjection,
                    in rsxMatrices,
                    cameraPosition,
                    editorTimeSeconds,
                    instanceIndex: null,
                    multiDrawCount: multiDrawCount,
                    forceDepthWrite: aggregateEstablishesFusedDepth);
                groupIndex += consumedGroupCount - 1;
            }
        }
        finally
        {
            cpuTimingScope.Dispose();
            gpuTimingScope.Dispose();
            drawWorkScope.Dispose();
        }
    }

    private bool TryGetStaticExecutionBundleKey(
        MapRenderEditorDrawGroup<GlTexturedDrawCommand> group,
        out long key)
    {
        key = 0;
        ReadOnlySpan<GlTexturedDrawCommand> commands =
            group.AuthoredPassSpan;
        if (group.Bucket == MapRenderEditorDrawBucket.Translucent ||
            commands.Length != 1 ||
            group.CameraIndependentSortKey is not long candidateKey)
        {
            return false;
        }

        ref readonly GlTexturedDrawCommand command = ref commands[0];
        GlTexturedMesh mesh = command.Mesh;
        if (command.InstanceIndex.HasValue ||
            mesh.InstanceCount == 0 ||
            mesh.IndexCount == 0 ||
            mesh.VertexArray == 0 ||
            mesh.InstanceBuffer == 0 ||
            !_staticInstanceBuffers.TryGetValue(
                mesh.InstanceBuffer,
                out StaticInstanceBufferRuntime? runtime) ||
            runtime.HasIsolatedDraw ||
            runtime.HasCommittedReceiverDrawCompaction)
        {
            return false;
        }

        key = candidateKey;
        return true;
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
        MapRenderEditorDrawGroup<GlTexturedDrawCommand> group)
    {
        ReadOnlySpan<GlTexturedDrawCommand> commands =
            group.AuthoredPassSpan;
        for (int commandIndex = 0;
             commandIndex < commands.Length;
             commandIndex++)
        {
            ref readonly GlTexturedDrawCommand command =
                ref commands[commandIndex];
            ShaderExecutionContract? execution =
                command.Mesh.ShaderExecution;
            if (execution is null)
                continue;

            IReadOnlyList<ShaderRuntimeSamplerRequirement> requirements =
                execution.RuntimeSamplerRequirements;
            for (int requirementIndex = 0;
                 requirementIndex < requirements.Count;
                 requirementIndex++)
            {
                ShaderRuntimeSamplerRequirement requirement =
                    requirements[requirementIndex];
                if (requirement.ResourceKind ==
                        ShaderRuntimeSamplerResourceKind.ProcessedFloatZ &&
                    requirement.Status ==
                        ShaderRuntimeSamplerRequirementStatus
                            .SameRevisionTextureRequired)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool RequiresVisibleProcessedFloatZ(
        IReadOnlyList<MapRenderEditorDrawGroup<GlTexturedDrawCommand>>
            groups)
    {
        for (int groupIndex = 0;
             groupIndex < groups.Count;
             groupIndex++)
        {
            if (_texturedDrawGroupVisibilityScratch[groupIndex] &&
                _texturedDrawGroupColorReadinessScratch[groupIndex] &&
                RequiresProcessedFloatZ(groups[groupIndex]))
            {
                return true;
            }
        }

        return false;
    }

    private bool RequiresAnyVisibleProcessedFloatZ(
        IReadOnlyList<MapRenderEditorDrawGroup<GlTexturedDrawCommand>>
            groups)
    {
        for (int groupIndex = 0;
             groupIndex < groups.Count;
             groupIndex++)
        {
            if (_texturedDrawGroupVisibilityScratch[groupIndex] &&
                RequiresProcessedFloatZ(groups[groupIndex]))
            {
                return true;
            }
        }

        return false;
    }

    private void PrepareTexturedDrawGroupColorExecution(
        IReadOnlyList<MapRenderEditorDrawGroup<GlTexturedDrawCommand>> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        if (CanReuseTexturedDrawGroupColorExecution(groups))
            return;

        if (_texturedDrawGroupColorReadinessScratch.Length < groups.Count)
        {
            Array.Resize(
                ref _texturedDrawGroupColorReadinessScratch,
                groups.Count);
        }
        if (_texturedDrawGroupGpuPhaseScratch.Length < groups.Count)
        {
            Array.Resize(
                ref _texturedDrawGroupGpuPhaseScratch,
                groups.Count);
        }

        for (int groupIndex = 0;
             groupIndex < groups.Count;
             groupIndex++)
        {
            MapRenderEditorDrawGroup<GlTexturedDrawCommand> group =
                groups[groupIndex];
            if (!_texturedDrawGroupVisibilityScratch[groupIndex])
            {
                _texturedDrawGroupColorReadinessScratch[groupIndex] = false;
                _texturedDrawGroupGpuPhaseScratch[groupIndex] = default;
                continue;
            }

            _texturedDrawGroupColorReadinessScratch[groupIndex] =
                IsTexturedDrawGroupReadyForColorExecution(group);
            _texturedDrawGroupGpuPhaseScratch[groupIndex] =
                ResolveTexturedGpuPhase(group);
        }

        CommitTexturedDrawGroupColorExecution(groups);
    }

    private static MapRenderGpuPhase ResolveTexturedGpuPhase(
        MapRenderEditorDrawGroup<GlTexturedDrawCommand> group)
    {
        if (group.Bucket == MapRenderEditorDrawBucket.Translucent)
            return MapRenderGpuPhase.Translucent;

        bool isStatic = group.AuthoredPassSpan[0].Mesh.InstanceCount != 0;
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
        ReadOnlySpan<GlTexturedDrawCommand> commands =
            group.AuthoredPassSpan;
        if (group.Bucket == MapRenderEditorDrawBucket.Translucent ||
            commands.Length != 1)
        {
            return false;
        }

        ref readonly GlTexturedDrawCommand command = ref commands[0];
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
                in command,
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
        ReadOnlySpan<GlTexturedDrawCommand> commands =
            group.AuthoredPassSpan;
        if (group.Bucket != MapRenderEditorDrawBucket.Opaque ||
            commands.Length != 1)
        {
            return false;
        }

        ref readonly GlTexturedDrawCommand command = ref commands[0];
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
                in command,
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
        in GlTexturedMesh mesh,
        WorldSurfaceBatchRuntime? surfaceBatch,
        int destinationIndex,
        bool mergeAdjacentRanges)
    {
        int rangeCount = ResolveWorldMultiDrawRangeCount(surfaceBatch);
        EnsureMultiDrawCapacity(checked(destinationIndex + rangeCount));
        if (surfaceBatch is null)
        {
            if (mergeAdjacentRanges &&
                TryMergeWorldMultiDrawRange(destinationIndex, in mesh))
            {
                return 0;
            }

            SetMultiDrawRange(destinationIndex, in mesh);
            return 1;
        }

        int nextDestinationIndex = destinationIndex;
        for (int runIndex = 0;
             runIndex < surfaceBatch.RunCount;
             runIndex++)
        {
            GlTexturedMesh visibleRun = SelectWorldVisibleRun(
                in mesh,
                surfaceBatch.VisibleRuns[runIndex]);
            if (mergeAdjacentRanges &&
                TryMergeWorldMultiDrawRange(
                    nextDestinationIndex,
                    in visibleRun))
            {
                continue;
            }

            SetMultiDrawRange(nextDestinationIndex, in visibleRun);
            nextDestinationIndex++;
        }
        return nextDestinationIndex - destinationIndex;
    }

    private bool TryMergeWorldMultiDrawRange(
        int destinationIndex,
        in GlTexturedMesh next)
    {
        if (destinationIndex == 0)
            return false;
        if (next.IndexCount == 0 || next.IndexCount > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(next.IndexCount));
        if (next.IndexOffsetBytes % next.IndexElementSizeBytes != 0)
        {
            throw new InvalidOperationException(
                "A multi-draw element-buffer offset must be index-aligned.");
        }

        int previousIndex = destinationIndex - 1;
        if (_multiDrawBaseVertices[previousIndex] != next.BaseVertex)
            return false;

        nint previousOffset = _multiDrawIndexOffsets[previousIndex];
        nint previousByteCount = checked(
            (nint)_multiDrawIndexCounts[previousIndex] *
            (nint)next.IndexElementSizeBytes);
        nint previousByteEnd = checked(previousOffset + previousByteCount);
        nint nextOffset = checked((nint)next.IndexOffsetBytes);
        if (previousByteEnd != nextOffset)
            return false;

        uint combinedIndexCount = checked(
            _multiDrawIndexCounts[previousIndex] + next.IndexCount);
        if (combinedIndexCount > int.MaxValue)
            return false;

        _multiDrawIndexCounts[previousIndex] = combinedIndexCount;
        return true;
    }

    private static int ResolveWorldMultiDrawRangeCount(
        WorldSurfaceBatchRuntime? surfaceBatch) =>
        surfaceBatch?.RunCount ?? 1;

    private static GlTexturedMesh SelectFirstWorldMultiDrawMesh(
        in GlTexturedMesh mesh,
        WorldSurfaceBatchRuntime? surfaceBatch) =>
        surfaceBatch is null
            ? mesh
            : SelectWorldVisibleRun(in mesh, surfaceBatch.VisibleRuns[0]);

    internal static bool CanAggregateWorldMultiDrawGroup(
        in GlTexturedMesh first,
        in GlTexturedMesh next,
        int visibleRunCount) =>
        visibleRunCount > 0 &&
        first.MultiDrawBatchGroupId >= 0 &&
        first.MultiDrawBatchGroupId == next.MultiDrawBatchGroupId &&
        CanMultiDrawTogether(in first, in next);

    internal static bool CanAggregateWorldDepthMultiDrawGroup(
        in GlTexturedMesh first,
        in GlTexturedMesh next,
        int visibleRunCount) =>
        visibleRunCount > 0 &&
        first.DepthMultiDrawBatchGroupId >= 0 &&
        first.DepthMultiDrawBatchGroupId ==
            next.DepthMultiDrawBatchGroupId &&
        CanDepthMultiDrawTogether(in first, in next);

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
        in GlTexturedMesh mesh)
    {
        var hash = new HashCode();
        hash.Add(mesh.VertexArray);
        hash.Add(mesh.ElementBuffer);
        hash.Add(mesh.IndexType);
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
        in GlTexturedMesh mesh)
    {
        var hash = new HashCode();
        hash.Add(mesh.VertexArray);
        hash.Add(mesh.ElementBuffer);
        hash.Add(mesh.IndexType);
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
        if (UsesPerSceneLightRuntimeResource(mesh))
            hash.Add(mesh.SceneLightIndex);
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
        in GlTexturedMesh first,
        in GlTexturedMesh next)
    {
        if (first.VertexArray != next.VertexArray ||
            first.ElementBuffer != next.ElementBuffer ||
            first.IndexType != next.IndexType ||
            first.InstanceCount != 0 ||
            next.InstanceCount != 0 ||
            first.State != next.State ||
            first.RsxProgram.Handle != next.RsxProgram.Handle ||
            first.FragmentProgramControl != next.FragmentProgramControl ||
            (UsesPerSceneLightRuntimeResource(first) &&
             first.SceneLightIndex != next.SceneLightIndex) ||
            !RuntimeSamplerRequirementsMatch(in first, in next))
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
        in GlTexturedMesh first,
        in GlTexturedMesh next)
    {
        if (first.VertexArray != next.VertexArray ||
            first.ElementBuffer != next.ElementBuffer ||
            first.IndexType != next.IndexType ||
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
        in GlTexturedMesh first,
        in GlTexturedMesh next)
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

    private static bool UsesPerSceneLightRuntimeResource(
        in GlTexturedMesh mesh)
    {
        if (mesh.ShaderExecution is not { } execution)
            return false;

        IReadOnlyList<ShaderRuntimeSamplerRequirement> requirements =
            execution.RuntimeSamplerRequirements;
        for (int requirementIndex = 0;
             requirementIndex < requirements.Count;
             requirementIndex++)
        {
            ShaderRuntimeSamplerRequirement requirement =
                requirements[requirementIndex];
            if ((requirement.ResourceKind ==
                    ShaderRuntimeSamplerResourceKind.LightAttenuation &&
                 requirement.Status ==
                    ShaderRuntimeSamplerRequirementStatus
                        .ImmutableSceneTextureRequired) ||
                (requirement.ResourceKind ==
                    ShaderRuntimeSamplerResourceKind.SpotShadowAtlas &&
                 requirement.Status ==
                    ShaderRuntimeSamplerRequirementStatus
                        .SameRevisionAtlasRequired))
            {
                return true;
            }
        }

        return false;
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

    private void SetMultiDrawRange(
        int index,
        in GlTexturedMesh mesh)
    {
        if ((uint)index >= (uint)_multiDrawIndexCounts.Length)
            throw new ArgumentOutOfRangeException(nameof(index));
        if (mesh.IndexCount == 0 || mesh.IndexCount > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(mesh.IndexCount));
        if (mesh.IndexOffsetBytes % mesh.IndexElementSizeBytes != 0)
        {
            throw new InvalidOperationException(
                "A multi-draw element-buffer offset must be index-aligned.");
        }

        _multiDrawIndexCounts[index] = mesh.IndexCount;
        _multiDrawIndexOffsets[index] = checked((nint)mesh.IndexOffsetBytes);
        _multiDrawBaseVertices[index] = mesh.BaseVertex;
    }

    private void Draw(
        in GlTexturedDrawCommand command,
        MapRenderOpenGlStencilTargetContract? stencilTargetContract,
        Matrix4x4 viewProjection,
        in DerivedMatrixState rsxMatrices,
        Vector3 cameraPosition,
        float editorTimeSeconds,
        bool forceDepthWrite = false)
    {
        GlTexturedMesh mesh = command.Mesh;
        if (mesh.InstanceCount == 0 &&
            TryGetWorldSurfaceBatchRuntime(
                in command,
                out WorldSurfaceBatchRuntime worldBatch))
        {
            if (worldBatch.RunCount == 0)
                return;

            GlTexturedMesh visibleMesh = SelectWorldVisibleRun(
                in mesh,
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
                            in mesh,
                            worldBatch.VisibleRuns[runIndex]));
                }
            }

            Draw(
                in visibleMesh,
                stencilTargetContract,
                viewProjection,
                in rsxMatrices,
                cameraPosition,
                editorTimeSeconds,
                instanceIndex: null,
                multiDrawCount,
                forceDepthWrite);
            return;
        }
        if (mesh.InstanceCount == 0 &&
            !IsWorldMeshVisible(mesh))
        {
            return;
        }
        if (command.InstanceIndex is int visibilityInstanceIndex &&
            !IsStaticInstanceVisible(
                mesh,
                visibilityInstanceIndex))
        {
            return;
        }
        if (mesh.InstanceCount != 0 &&
            command.InstanceIndex is null &&
            ResolveVisibleInstanceCount(mesh) == 0)
        {
            return;
        }
        if (command.InstanceIndex is int instanceIndex &&
            (instanceIndex < 0 ||
             (uint)instanceIndex >= mesh.InstanceCount))
        {
            throw new ArgumentOutOfRangeException(
                nameof(command),
                instanceIndex,
                "Textured draw command instance index is outside the mesh.");
        }

        Draw(
            in mesh,
            stencilTargetContract,
            viewProjection,
            in rsxMatrices,
            cameraPosition,
            editorTimeSeconds,
            command.InstanceIndex,
            forceDepthWrite: forceDepthWrite);
    }

    private bool TryGetWorldSurfaceBatchRuntime(
        in GlTexturedDrawCommand command,
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
        in GlTexturedMesh mesh,
        MapRenderOpenGlWorldVisibleRun run) =>
        mesh with
        {
            IndexCount = checked((uint)run.IndexCount),
            IndexOffsetBytes = checked(
                mesh.IndexOffsetBytes +
                (nuint)run.FirstIndex * mesh.IndexElementSizeBytes),
            WorldSurfaceIndex = -1
        };

    private void Draw(
        in GlTexturedMesh mesh,
        MapRenderOpenGlStencilTargetContract? stencilTargetContract,
        Matrix4x4 viewProjection,
        in DerivedMatrixState rsxMatrices,
        Vector3 cameraPosition,
        float editorTimeSeconds,
        int? instanceIndex,
        int multiDrawCount = 0,
        bool forceDepthWrite = false)
    {
        if (mesh.IndexCount == 0)
            return;

        bool executeTranslatedAuthored = mesh.RsxProgram.Handle != 0;
        if (executeTranslatedAuthored &&
            !TryBindRuntimeSamplers(in mesh))
        {
            // Runtime-authored receivers have no generic substitute. A
            // missing or stale atlas keeps the exact +3 pass out of this
            // frame rather than rendering a semantically different shader.
            return;
        }
        ApplyRenderState(
            mesh.State,
            stencilTargetContract,
            forceDepthWrite);
        if (executeTranslatedAuthored)
        {
            DerivedMatrixState drawMatrices =
                ResolveTranslatedDrawMatrices(
                    in mesh,
                    instanceIndex,
                    in rsxMatrices);
            _state.UseProgram(mesh.RsxProgram.Handle);
            ApplyTranslatedStaticComposition(
                mesh.StaticModelProgramUniforms,
                mesh,
                editorTimeSeconds);
            ApplyRsxConstantBindings(
                mesh.RsxConstantBindings,
                in drawMatrices,
                editorTimeSeconds);
            foreach (GlRsxSamplerBinding binding in mesh.RsxSamplerBindings)
            {
                // A code sampler can leave an RSX-equivalent comparison
                // sampler object on this unit. Ordinary material samplers own
                // the texture descriptor state and must explicitly restore
                // texture-owned filtering/wrap behavior.
                _state.BindSampler(checked((uint)binding.Destination), 0);
                _state.EnsureTextureBinding(
                    binding.Destination,
                    binding.Target,
                    binding.Texture);
            }
            BindRuntimeSamplers(in mesh);
            _state.BindVertexArray(mesh.VertexArray);
            if (instanceIndex.HasValue)
            {
                ConfigureTexturedInstanceBase(
                    mesh.InstanceBuffer,
                    instanceIndex.Value,
                    MapRenderOpenGlStaticModelInstancedVertexComposer
                        .FirstPlacementAttribute);
            }
            IssueWorldDraws(in mesh, multiDrawCount, instanceIndex);
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
        _state.Uniform4(
            _texturedVegetationParametersLocation,
            vegetation?.IsEnabled == true ? 1f : 0f,
            vegetation?.Amplitude ?? 0f,
            vegetation?.AngularFrequency ?? 0f,
            vegetation?.SpatialFrequency ?? 0f);
        _state.Uniform1(
            _texturedVegetationTimeLocation,
            editorTimeSeconds);
        _state.Uniform4(
            _texturedVegetationBoundsLocation,
            mesh.LocalMinimumHeight,
            mesh.LocalHeightRange,
            0f,
            0f);
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
            _state.BindSampler(checked((uint)layerIndex), 0);
            uint texture = layerIndex < mesh.ColorTextures.Length
                ? mesh.ColorTextures[layerIndex]
                : _genericInactiveTexture;
            _state.EnsureTextureBinding(
                layerIndex,
                TextureTarget.Texture2D,
                texture);
        }

        _state.BindSampler(
            checked((uint)MapRenderScene.MaxColorLayerCount),
            0);
        _state.EnsureTextureBinding(
            MapRenderScene.MaxColorLayerCount,
            TextureTarget.Texture2D,
            mesh.LightmapTexture == 0
                ? _genericInactiveTexture
                : mesh.LightmapTexture);
        for (int index = 0; index < _texturedNormalSamplerLocations.Length; index++)
        {
            int textureUnit = normalTextureUnitStart + index;
            _state.BindSampler(checked((uint)textureUnit), 0);
            uint texture = index < mesh.NormalTextures.Length &&
                mesh.NormalTextures[index] != 0
                    ? mesh.NormalTextures[index]
                    : _genericInactiveTexture;
            _state.EnsureTextureBinding(
                textureUnit,
                TextureTarget.Texture2D,
                texture);
        }
        for (int index = 0; index < _texturedSpecularSamplerLocations.Length; index++)
        {
            int textureUnit = specularTextureUnitStart + index;
            _state.BindSampler(checked((uint)textureUnit), 0);
            uint texture = index < mesh.SpecularTextures.Length &&
                mesh.SpecularTextures[index] != 0
                    ? mesh.SpecularTextures[index]
                    : _genericInactiveTexture;
            _state.EnsureTextureBinding(
                textureUnit,
                TextureTarget.Texture2D,
                texture);
        }
        _state.BindSampler(
            GenericStaticModelLightingTextureUnit,
            0);
        _state.EnsureTextureBinding(
            checked((int)GenericStaticModelLightingTextureUnit),
            TextureTarget.Texture3D,
            _staticModelLightingAtlasTexture);
        _state.ActiveTexture(0);
        _state.BindVertexArray(mesh.VertexArray);
        if (mesh.InstanceCount == 0)
        {
            IssueWorldDraws(in mesh, multiDrawCount);
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
                mesh.IndexType,
                null,
                drawnInstanceCount);
        }
    }

    private DerivedMatrixState ResolveTranslatedDrawMatrices(
        in GlTexturedMesh mesh,
        int? instanceIndex,
        in DerivedMatrixState frameMatrices)
    {
        DerivedMatrixState drawMatrices = frameMatrices;
        if (UsesSpotShadowAtlas(in mesh))
        {
            MapRenderSpotShadowAtlasEntry entry =
                ResolveCurrentSpotShadowEntry(mesh.SceneLightIndex);
            drawMatrices = DerivedMatrixResolver.WithShadowLookupSource(
                drawMatrices,
                entry.ShadowLookupMatrix);
        }

        if (mesh.InstanceCount == 0)
        {
            if (instanceIndex.HasValue)
            {
                throw new InvalidOperationException(
                    "A translated world draw cannot carry a static instance index.");
            }
            return drawMatrices;
        }

        if (instanceIndex is not int index)
        {
            if (mesh.StaticModelProgramUniforms is not null)
                return drawMatrices;
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
            drawMatrices,
            runtime.Instances[index]);
    }

    private void ApplyRsxConstantBindings(
        IReadOnlyList<GlRsxConstantBinding> bindings,
        in DerivedMatrixState rsxMatrices,
        float editorTimeSeconds)
    {
        Vector3 previousEyeOffset =
            _currentDynamicCodeConstantEyeOffset;
        _currentDynamicCodeConstantEyeOffset = rsxMatrices.EyeOffset;
        try
        {
            if (!_authoredMaterials.TryApplyConstantBindings(
                    bindings,
                    in rsxMatrices,
                    editorTimeSeconds,
                    _dynamicCodeConstantResolver,
                    out string? blocker))
            {
                throw new InvalidOperationException(
                    blocker ?? "Authored RSX constant execution failed.");
            }
        }
        finally
        {
            // Draw execution is single-threaded. Restoring the scoped value
            // also keeps this cached resolver safe if execution is ever
            // re-entered while a binding failure is unwinding.
            _currentDynamicCodeConstantEyeOffset = previousEyeOffset;
        }
    }

    private ShaderConstantValue? ResolveCurrentMapDynamicCodeConstant(
        ushort sourceRow,
        int? sceneLightIndex) =>
        ResolveMapDynamicCodeConstant(
            sourceRow,
            sceneLightIndex,
            _currentDynamicCodeConstantEyeOffset);

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
        FrameDirectCodeConstants.LightSpotFactorsRowIndex
            when sceneLightIndex is int index =>
            ResolveSceneLightShadowCodeConstant(
                sourceRow,
                index,
                spotFactors: true),
        FrameDirectCodeConstants.LightFalloffPlacementRowIndex
            when sceneLightIndex is int index =>
            ResolveSceneLightShadowCodeConstant(
                sourceRow,
                index,
                spotFactors: false),
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
            RequireCurrentSceneLightFrame(
                FrameDirectCodeConstants
                    .DirectionalLightDirectionRowIndex);

        return MapRenderWorldEvent20SceneLightDirectCodeConstantProducer
            .ProducePositionValue(frame, sceneLightIndex, eyeOffset);
    }

    private ShaderConstantValue ResolveSceneLightShadowCodeConstant(
        ushort sourceRow,
        int sceneLightIndex,
        bool spotFactors)
    {
        MapRenderWorldEvent20SceneLightFrameInput frame =
            RequireCurrentSceneLightFrame(sourceRow);
        MapRenderSpotShadowAtlasEntry? entry = null;
        _currentSpotShadowReadyState?.TryGetEntry(
            sceneLightIndex,
            out entry);
        return spotFactors
            ? MapRenderWorldEvent20SceneLightDirectCodeConstantProducer
                .ProduceSpotFactorsValue(
                    frame,
                    sceneLightIndex,
                    entry)
            : MapRenderWorldEvent20SceneLightDirectCodeConstantProducer
                .ProduceLightFalloffPlacementValue(
                    frame,
                    sceneLightIndex,
                    entry);
    }

    private MapRenderWorldEvent20SceneLightFrameInput
        RequireCurrentSceneLightFrame(ushort sourceRow)
    {
        MapRenderWorldEvent20SceneLightFrameInput frame =
            _editorPreviewSceneLightFrame ??
            throw new InvalidOperationException(
                $"A translated Event20 draw reached row 0x{sourceRow:X2} without an immutable scene-light frame: " +
                (_editorPreviewSceneLightFrameFailure?.ToString() ??
                 "source unavailable"));
        if (_previewWorldSource is not { } source ||
            !source.AssetLookup.HasCanonicalAssetPoolRevision(
                frame.AssetPoolRevision))
        {
            throw new InvalidOperationException(
                $"A translated Event20 draw reached row 0x{sourceRow:X2} with stale canonical light assets from revision {frame.AssetPoolRevision}.");
        }
        return frame;
    }

    private static bool UsesSpotShadowAtlas(in GlTexturedMesh mesh)
    {
        if (mesh.ShaderExecution is not { } execution)
            return false;

        IReadOnlyList<ShaderRuntimeSamplerRequirement> requirements =
            execution.RuntimeSamplerRequirements;
        for (int requirementIndex = 0;
             requirementIndex < requirements.Count;
             requirementIndex++)
        {
            ShaderRuntimeSamplerRequirement requirement =
                requirements[requirementIndex];
            if (requirement.ResourceKind ==
                    ShaderRuntimeSamplerResourceKind.SpotShadowAtlas &&
                requirement.Status ==
                    ShaderRuntimeSamplerRequirementStatus
                        .SameRevisionAtlasRequired)
            {
                return true;
            }
        }

        return false;
    }

    private MapRenderSpotShadowAtlasEntry ResolveCurrentSpotShadowEntry(
        int sceneLightIndex)
    {
        if (_currentSpotShadowReadyState is not { } ready ||
            !ready.TryGetEntry(sceneLightIndex, out
                MapRenderSpotShadowAtlasEntry? entry) ||
            entry is null)
        {
            throw new InvalidOperationException(
                $"A translated spot-shadow draw for scene light {sceneLightIndex} has no same-revision lookup entry.");
        }
        return entry;
    }

    private bool TryBindRuntimeSamplers(in GlTexturedMesh mesh)
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
                    ShaderRuntimeSamplerResourceKind.LightAttenuation &&
                requirement.Status ==
                    ShaderRuntimeSamplerRequirementStatus
                        .ImmutableSceneTextureRequired)
            {
                if (!TryGetSceneLightAttenuationTextureHandle(
                        mesh.SceneLightIndex,
                        out _))
                {
                    return false;
                }
                continue;
            }

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

            if (requirement.ResourceKind ==
                    ShaderRuntimeSamplerResourceKind.SpotShadowAtlas &&
                requirement.Status ==
                    ShaderRuntimeSamplerRequirementStatus
                        .SameRevisionAtlasRequired)
            {
                if (_currentSpotShadowReadyState is not { } spotReady ||
                    _currentSpotShadowBackendReadyFrame is not
                        { } spotBackendReady ||
                    _spotShadowAtlas is null ||
                    _currentWorldReceiverTechniqueSelector is not
                        { } receiverSelector ||
                    !ReferenceEquals(
                        receiverSelector.Visibility,
                        spotReady.Frame) ||
                    !ReferenceEquals(
                        receiverSelector.Techniques.SceneLights
                            .SpotShadowAtlasReady,
                        spotReady) ||
                    spotReady.Revision !=
                        spotBackendReady.FrameRevision ||
                    !spotReady.TryGetEntry(
                        mesh.SceneLightIndex,
                        out MapRenderSpotShadowAtlasEntry? spotEntry) ||
                    spotEntry is null ||
                    !spotBackendReady.TryGetEntry(
                        mesh.SceneLightIndex,
                        out MapRenderOpenGlSpotShadowReadyEntry?
                            backendEntry) ||
                    backendEntry.TileIndex != spotEntry.AtlasSlot)
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

    private void BindRuntimeSamplers(in GlTexturedMesh mesh)
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
                    ShaderRuntimeSamplerResourceKind.LightAttenuation &&
                requirement.Status ==
                    ShaderRuntimeSamplerRequirementStatus
                        .ImmutableSceneTextureRequired)
            {
                if (!TryGetSceneLightAttenuationTextureHandle(
                        mesh.SceneLightIndex,
                        out uint attenuationTexture))
                {
                    throw new InvalidOperationException(
                        "A source-13 attenuation draw reached execution without the canonical scene-light image.");
                }
                _state.BindSampler(requirement.Destination, 0);
                _state.EnsureTextureBinding(
                    checked((int)requirement.Destination),
                    TextureTarget.Texture2D,
                    attenuationTexture);
                continue;
            }

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
                // Texture-unit sampler objects override texture parameters.
                // Clear a possible shadow-compare sampler before publishing
                // the immutable linear/clamp 3D atlas on the same destination.
                _state.BindSampler(requirement.Destination, 0);
                _state.EnsureTextureBinding(
                    checked((int)requirement.Destination),
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
                _state.BindSampler(
                    requirement.Destination,
                    floatZPublication.SamplerHandle);
                _state.EnsureTextureBinding(
                    checked((int)requirement.Destination),
                    TextureTarget.Texture2D,
                    floatZPublication.TextureHandle);
                continue;
            }

            if (requirement.ResourceKind ==
                    ShaderRuntimeSamplerResourceKind.SpotShadowAtlas &&
                requirement.Status ==
                    ShaderRuntimeSamplerRequirementStatus
                        .SameRevisionAtlasRequired)
            {
                MapRenderSpotShadowAtlasReadyState spotReady =
                    _currentSpotShadowReadyState ??
                    throw new InvalidOperationException(
                        "A spot-shadow sampler draw reached execution without the current renderer publication.");
                MapRenderOpenGlSpotShadowAtlasReadyFrame backendReady =
                    _currentSpotShadowBackendReadyFrame ??
                    throw new InvalidOperationException(
                        "A spot-shadow sampler draw reached execution without the current OpenGL publication.");
                MapRenderOpenGlSpotShadowAtlasBackend spotAtlas =
                    _spotShadowAtlas ??
                    throw new InvalidOperationException(
                        "A spot-shadow sampler draw reached execution without an OpenGL spot atlas.");
                if (spotReady.Revision != backendReady.FrameRevision ||
                    !spotReady.TryGetEntry(
                        mesh.SceneLightIndex,
                        out MapRenderSpotShadowAtlasEntry? spotEntry) ||
                    spotEntry is null ||
                    !backendReady.TryGetEntry(
                        mesh.SceneLightIndex,
                        out MapRenderOpenGlSpotShadowReadyEntry?
                            backendEntry) ||
                    backendEntry.TileIndex != spotEntry.AtlasSlot)
                {
                    throw new InvalidOperationException(
                        $"A spot-shadow sampler draw for scene light {mesh.SceneLightIndex} reached execution without matching same-revision entries.");
                }
                spotAtlas.BindReadyReceiver(
                    backendReady,
                    mesh.SceneLightIndex,
                    requirement.Destination);
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
        in GlTexturedMesh mesh,
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
                mesh.IndexType,
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
                mesh.IndexType,
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
                mesh.IndexType,
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
