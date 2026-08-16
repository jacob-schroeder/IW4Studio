using System.Numerics;
using Silk.NET.OpenGL;

using IW4.Assets.Assets.Material;
using IW4.Render.Diagnostics;
using IW4.Render.EditorPreview;
using IW4.Render.Execution;
using IW4.Render.Geometry;
using IW4.Render.OpenGl.StaticModels;
using IW4.Render.Shaders;

namespace IW4.Render.OpenGl;

public sealed unsafe partial class SilkOpenGlMapRenderer
{
    /// <summary>
    /// Collapses only exact receiver draws that are already one contiguous,
    /// single-pass run in the sorted visible queue. Interleaved translucent
    /// instances and authored multipass groups keep their isolated commands.
    /// </summary>
    private void PrepareStaticReceiverDrawCompaction(
        IReadOnlyList<
            MapRenderEditorDrawGroup<GlTexturedDrawCommand>> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        foreach (StaticInstanceBufferRuntime runtime in
                 _staticInstanceBuffers.Values)
        {
            if (runtime.IsReceiverVariant && runtime.HasIsolatedDraw)
                runtime.BeginReceiverDrawCompactionFrame();
        }

        int visibleOrdinal = 0;
        for (int groupIndex = 0;
             groupIndex < groups.Count;
             groupIndex++)
        {
            if (!_texturedDrawGroupVisibilityScratch[groupIndex])
                continue;

            MapRenderEditorDrawGroup<GlTexturedDrawCommand> group =
                groups[groupIndex];
            ReadOnlySpan<GlTexturedDrawCommand> commands =
                group.AuthoredPassSpan;
            bool singlePassCandidate =
                group.Bucket == MapRenderEditorDrawBucket.Translucent &&
                commands.Length == 1;
            for (int commandIndex = 0;
                 commandIndex < commands.Length;
                 commandIndex++)
            {
                ref readonly GlTexturedDrawCommand command =
                    ref commands[commandIndex];
                GlTexturedMesh mesh = command.Mesh;
                if (!TryGetIsolatedStaticReceiverRuntime(
                        in mesh,
                        out StaticInstanceBufferRuntime? runtime))
                {
                    continue;
                }

                if (singlePassCandidate &&
                    command.InstanceIndex is int sourceIndex &&
                    mesh.StaticCameraRegion is
                        GfxCameraRegionType.LitOpaque or
                        GfxCameraRegionType.LightMapOpaque &&
                    (mesh.RsxProgram.Handle == 0 ||
                     mesh.StaticModelProgramUniforms is not null))
                {
                    runtime.GetReceiverDrawCompactionPlan()
                        .ObserveCandidate(
                            groupIndex,
                            visibleOrdinal,
                            sourceIndex);
                }
                else
                {
                    runtime.GetReceiverDrawCompactionPlan()
                        .Disqualify();
                }
            }
            visibleOrdinal++;
        }

        foreach ((uint instanceBuffer, StaticInstanceBufferRuntime runtime)
                 in _staticInstanceBuffers)
        {
            if (!runtime.IsReceiverVariant ||
                !runtime.HasIsolatedDraw ||
                !runtime.TryGetReceiverDrawCompactionPlan(
                    out MapRenderOpenGlStaticReceiverDrawCompactionPlan
                        plan))
            {
                continue;
            }

            if (plan.CanCompact)
            {
                CompactStaticReceiverDrawSources(
                    instanceBuffer,
                    runtime,
                    plan.SourceIndices);
                runtime.VisibleCount = checked((uint)plan.SourceCount);
                runtime.HasCommittedReceiverDrawCompaction = true;
                continue;
            }

            if (plan.HasObservation)
            {
                RestoreStaticReceiverSourceLayout(
                    instanceBuffer,
                    runtime);
                runtime.VisibleCount =
                    checked((uint)runtime.Instances.Length);
            }
        }
    }

    private bool TryDrawCompactedStaticReceiverGroup(
        int groupIndex,
        MapRenderEditorDrawGroup<GlTexturedDrawCommand> group,
        MapRenderOpenGlStencilTargetContract? stencilTargetContract,
        Matrix4x4 viewProjection,
        in DerivedMatrixState rsxMatrices,
        Vector3 cameraPosition,
        float editorTimeSeconds)
    {
        ReadOnlySpan<GlTexturedDrawCommand> commands =
            group.AuthoredPassSpan;
        if (commands.Length != 1)
            return false;

        ref readonly GlTexturedDrawCommand command = ref commands[0];
        GlTexturedMesh mesh = command.Mesh;
        if (!TryGetIsolatedStaticReceiverRuntime(
                in mesh,
                out StaticInstanceBufferRuntime? runtime) ||
            !runtime.HasCommittedReceiverDrawCompaction ||
            !runtime.TryGetReceiverDrawCompactionPlan(
                out MapRenderOpenGlStaticReceiverDrawCompactionPlan plan))
        {
            return false;
        }

        if (groupIndex == plan.FirstGroupIndex)
        {
            GlTexturedDrawCommand compactedCommand =
                command with { InstanceIndex = null };
            Draw(
                in compactedCommand,
                stencilTargetContract,
                viewProjection,
                in rsxMatrices,
                cameraPosition,
                editorTimeSeconds);
        }
        return true;
    }

    private bool TryGetIsolatedStaticReceiverRuntime(
        in GlTexturedMesh mesh,
        out StaticInstanceBufferRuntime runtime)
    {
        if (mesh.InstanceBuffer != 0 &&
            _staticInstanceBuffers.TryGetValue(
                mesh.InstanceBuffer,
                out StaticInstanceBufferRuntime? candidate) &&
            candidate.IsReceiverVariant &&
            candidate.HasIsolatedDraw)
        {
            runtime = candidate;
            return true;
        }

        runtime = null!;
        return false;
    }

    private void CompactStaticReceiverDrawSources(
        uint instanceBuffer,
        StaticInstanceBufferRuntime runtime,
        ReadOnlySpan<int> sourceIndices)
    {
        bool sourceSelectionChanged =
            MapRenderStaticInstanceSubset.HasChanged(
                runtime.CurrentSourceIndices,
                runtime.CurrentSourceCount,
                sourceIndices,
                sourceIndices.Length);
        StaticModelLightingEntryStage lightingStage =
            StageSelectedStaticModelLightingEntries(
                runtime,
                sourceIndices,
                force: sourceSelectionChanged);
        bool placementChanged =
            runtime.HasLivePlacementChangePending;
        bool changed =
            sourceSelectionChanged ||
            lightingStage.Changed ||
            placementChanged;
        if (changed)
        {
            float[] transforms = runtime.CompactTransforms;
            MapRenderStaticInstanceBufferPacker.PackSelected(
                runtime.Instances,
                sourceIndices,
                runtime.LightingPayload,
                transforms,
                ResolveStaticModelLightingCoordinates(runtime));
            UploadCompactedStaticInstanceTransforms(
                instanceBuffer,
                runtime,
                transforms,
                sourceIndices.Length);
            runtime.HasLivePlacementChangePending = false;
            sourceIndices.CopyTo(runtime.CurrentSourceIndices);
            runtime.CurrentSourceCount = sourceIndices.Length;
            CommitSelectedStaticModelLightingEntries(
                runtime,
                sourceIndices.Length);
        }
        if (lightingStage.Evaluated)
        {
            CommitStaticModelLightingAssignmentGeneration(
                runtime);
        }

        runtime.HasCompactedReceiverSourceLayout =
            !IsIdentityStaticInstanceSelection(
                sourceIndices,
                runtime.Instances.Length);
    }

    private void RestoreStaticReceiverSourceLayout(
        uint instanceBuffer,
        StaticInstanceBufferRuntime runtime)
    {
        bool sourceLayoutChanged =
            runtime.HasCompactedReceiverSourceLayout;
        bool placementChanged =
            runtime.HasLivePlacementChangePending;
        StaticModelLightingEntryStage lightingStage =
            StageFullStaticModelLightingEntries(
                runtime,
                force: sourceLayoutChanged);
        if (!sourceLayoutChanged &&
            !lightingStage.Changed &&
            !placementChanged)
        {
            if (lightingStage.Evaluated)
            {
                CommitStaticModelLightingAssignmentGeneration(
                    runtime);
            }
            return;
        }

        float[] transforms = runtime.CompactTransforms;
        MapRenderStaticInstanceBufferPacker.PackAll(
            runtime.Instances,
            runtime.LightingPayload,
            transforms,
            ResolveStaticModelLightingCoordinates(runtime));
        UploadCompactedStaticInstanceTransforms(
            instanceBuffer,
            runtime,
            transforms,
            runtime.Instances.Length);
        runtime.HasLivePlacementChangePending = false;
        for (int index = 0; index < runtime.Instances.Length; index++)
            runtime.CurrentSourceIndices[index] = index;
        runtime.CurrentSourceCount = runtime.Instances.Length;
        CommitFullStaticModelLightingEntries(runtime);
        if (lightingStage.Evaluated)
        {
            CommitStaticModelLightingAssignmentGeneration(
                runtime);
        }
        runtime.HasCompactedReceiverSourceLayout = false;
    }

    private void UploadCompactedStaticInstanceTransforms(
        uint instanceBuffer,
        StaticInstanceBufferRuntime runtime,
        float[] transforms,
        int instanceCount)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(transforms);
        if (instanceBuffer != runtime.OriginalInstanceBuffer)
        {
            throw new ArgumentException(
                "The upload target does not own the supplied static-instance runtime.",
                nameof(instanceBuffer));
        }
        if (instanceCount <= 0 ||
            instanceCount > runtime.Instances.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(instanceCount));
        }
        int requiredFloatCount = checked(
            instanceCount * runtime.InstanceFloatStride);
        if (transforms.Length < requiredFloatCount)
        {
            throw new ArgumentException(
                "The compact transform payload is smaller than the requested upload.",
                nameof(transforms));
        }
        if (_activeRenderFrameIndex < 0)
        {
            throw new InvalidOperationException(
                "Dynamic static-instance uploads require an active render frame.");
        }

        EnsureStaticInstanceUploadBufferRing(runtime);
        uint uploadBuffer = runtime.AcquireUploadBuffer(
            _activeRenderFrameIndex,
            out bool advanced);
        nuint uploadByteCount =
            checked((nuint)(requiredFloatCount * sizeof(float)));
        _state.BindArrayBuffer(uploadBuffer);
        fixed (float* transformPointer = transforms)
        {
            _gl.BufferSubData(
                BufferTargetARB.ArrayBuffer,
                0,
                uploadByteCount,
                transformPointer);
        }
        _frameTelemetry.AddCounter(
            MapRenderFrameCounter.StaticInstanceUploadCalls);
        _frameTelemetry.AddCounter(
            MapRenderFrameCounter.StaticInstanceUploadBytes,
            checked((long)uploadByteCount));
        if (advanced)
        {
            _frameTelemetry.AddCounter(
                MapRenderFrameCounter.StaticInstanceUploadRingAdvances);
        }

        // VertexAttribPointer captures the currently bound array buffer in
        // the VAO. Rebase the one owning VAO once per changed upload so whole
        // batch draws and later isolated-instance offset changes both consume
        // the newly selected ring slot.
        _state.BindVertexArray(runtime.VertexArray);
        ConfigureTexturedInstanceBase(
            instanceBuffer,
            instanceIndex: 0,
            firstAttribute: runtime.FirstPlacementAttribute);
    }

    private void EnsureStaticInstanceUploadBufferRing(
        StaticInstanceBufferRuntime runtime)
    {
        if (runtime.HasUploadBufferRing)
            return;

        var buffers = new uint[
            StaticInstanceBufferRuntime.UploadBufferRingCapacity];
        buffers[0] = runtime.OriginalInstanceBuffer;
        int createdCount = 0;
        try
        {
            for (int index = 1; index < buffers.Length; index++)
            {
                uint buffer = _gl.GenBuffer();
                if (buffer == 0)
                {
                    throw new InvalidOperationException(
                        "OpenGL returned buffer object zero for the static-instance upload ring.");
                }
                buffers[index] = buffer;
                createdCount++;
                _state.BindArrayBuffer(buffer);
                _gl.BufferData(
                    BufferTargetARB.ArrayBuffer,
                    runtime.InstanceBufferCapacityBytes,
                    null,
                    BufferUsageARB.DynamicDraw);
            }
            runtime.InstallUploadBufferRing(buffers);
        }
        catch
        {
            for (int index = 1; index <= createdCount; index++)
            {
                uint buffer = buffers[index];
                _state.ForgetArrayBufferBinding(buffer);
                _gl.DeleteBuffer(buffer);
            }
            throw;
        }
    }

    private void RegisterStaticInstanceBufferRuntime(
        uint instanceBuffer,
        StaticInstanceBufferRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (instanceBuffer == 0 ||
            instanceBuffer != runtime.OriginalInstanceBuffer)
        {
            throw new ArgumentException(
                "A static-instance runtime must be registered under its owning buffer.",
                nameof(instanceBuffer));
        }

        _staticInstanceBuffers.Add(instanceBuffer, runtime);
        try
        {
            foreach (int objectIndex in runtime.ObjectIndices)
            {
                if (!_staticInstanceRuntimesByObjectIndex.TryGetValue(
                        objectIndex,
                        out List<StaticInstanceBufferRuntime>? runtimes))
                {
                    runtimes = [];
                    _staticInstanceRuntimesByObjectIndex.Add(
                        objectIndex,
                        runtimes);
                }
                runtimes.Add(runtime);
            }
        }
        catch
        {
            UnregisterStaticInstanceRuntimeAdjacency(runtime);
            _staticInstanceBuffers.Remove(instanceBuffer);
            throw;
        }

        _staticInstanceCompactionFullInvalidationPending = true;
    }

    private void RemoveStaticInstanceBufferRuntime(uint instanceBuffer)
    {
        if (!_staticInstanceBuffers.Remove(
                instanceBuffer,
                out StaticInstanceBufferRuntime? runtime))
        {
            return;
        }
        UnregisterStaticInstanceRuntimeAdjacency(runtime);
        _selectedStaticReceiverOccurrences.RemoveWhere(
            occurrence => ReferenceEquals(
                occurrence.Runtime,
                runtime));
        _previousSelectedStaticReceiverOccurrences.RemoveWhere(
            occurrence => ReferenceEquals(
                occurrence.Runtime,
                runtime));
        _staticInstanceCompactionFullInvalidationPending = true;
        _state.ForgetArrayBufferBinding(runtime.OriginalInstanceBuffer);
        _state.ForgetVertexArrayBinding(runtime.VertexArray);
        DeleteStaticInstanceUploadBufferRing(runtime);
    }

    private void UnregisterStaticInstanceRuntimeAdjacency(
        StaticInstanceBufferRuntime runtime)
    {
        foreach (int objectIndex in runtime.ObjectIndices)
        {
            if (!_staticInstanceRuntimesByObjectIndex.TryGetValue(
                    objectIndex,
                    out List<StaticInstanceBufferRuntime>? runtimes))
            {
                continue;
            }
            runtimes.Remove(runtime);
            if (runtimes.Count == 0)
                _staticInstanceRuntimesByObjectIndex.Remove(objectIndex);
        }
    }

    private void DeleteStaticInstanceUploadBufferRing(
        StaticInstanceBufferRuntime runtime)
    {
        ReadOnlySpan<uint> buffers = runtime.AuxiliaryInstanceBuffers;
        for (int index = 0; index < buffers.Length; index++)
        {
            uint buffer = buffers[index];
            _state.ForgetArrayBufferBinding(buffer);
            _gl.DeleteBuffer(buffer);
        }
        runtime.ForgetUploadBufferRing();
    }

    private static bool IsIdentityStaticInstanceSelection(
        ReadOnlySpan<int> sourceIndices,
        int sourceCount)
    {
        if (sourceIndices.Length != sourceCount)
            return false;
        for (int index = 0; index < sourceIndices.Length; index++)
        {
            if (sourceIndices[index] != index)
                return false;
        }
        return true;
    }
}
