using System.Numerics;
using System.Runtime.Versioning;

using IW4.Render.Diagnostics;
using IW4.Render.EditorPreview;
using IW4.Render.Execution;
using IW4.Render.Geometry;
using IW4.Render.Lighting;
using IW4.Render.Metal.Resources;
using IW4.Render.Resources;
using IW4.Render.Scheduling;
using IW4.Render.SceneBuilding;

using SharpMetal.Metal;

namespace IW4.Render.Metal;

[SupportedOSPlatform("macos")]
public sealed unsafe partial class MetalMapRenderer
{
    private readonly MTLBuffer[] _staticModelLightingInstanceBuffers =
        new MTLBuffer[MetalStaticModelLightingResources.FrameSlotCount];
    private readonly ulong[] _staticModelLightingInstanceGenerations =
        new ulong[MetalStaticModelLightingResources.FrameSlotCount];
    private MapRenderStaticModelLightingAtlas? _staticModelLightingAtlas;
    private MapRenderStaticModelLightingWorkingSet?
        _staticModelLightingWorkingSet;
    private MetalStaticModelLightingResources?
        _staticModelLightingResources;
    private MetalPreparedNormalCameraPass[]
        _staticModelLightingPreparedPasses = [];
    private int[] _staticModelLightingObjectIndices = [];
    private bool[] _staticModelLightingAdmittedByObject = [];
    private int _staticModelLightingInstanceBufferByteCount;
    private int _staticModelLightingAdmittedVisibleObjectCount;
    private long _staticModelLightingAdmissionFrameStateRevision = -1;
    private MTLTexture _currentStaticModelLightingTexture;

    private void CreateStaticModelLightingResources(
        MapRenderScene scene,
        RenderSceneSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(snapshot);
        DeleteStaticModelLightingResources();

        MetalPreparedNormalCameraPass[] consumers =
            _normalCameraPreparedPasses
                .Where(pass =>
                    pass.LightingPayload ==
                        MapRenderStaticInstanceLightingPayload
                            .BaseLightingCoords)
                .ToArray();
        foreach (MetalPreparedNormalCameraPass pass in
                 _normalCameraPreparedPasses)
        {
            bool consumesAtlas = pass.RuntimeSamplerBindings.Any(binding =>
                binding.ResourceKind ==
                    ShaderRuntimeSamplerResourceKind.ModelLightingAtlas);
            bool consumesBaseCoordinates = pass.LightingPayload ==
                MapRenderStaticInstanceLightingPayload.BaseLightingCoords;
            bool exactContract =
                MapRenderStaticModelLightingContract.TryCreate(
                    pass.Source.ShaderProvenance,
                    out _);
            if (consumesAtlas != consumesBaseCoordinates ||
                consumesAtlas != exactContract)
            {
                throw new InvalidDataException(
                    "A Metal static-model lighting pass must consume both " +
                    "the row-0x39 tile center and the model-lighting atlas.");
            }
        }
        if (consumers.Length == 0)
            return;

        MapRenderStaticModelLightingAtlas atlas =
            scene.StaticModelLightingAtlas ??
            throw new InvalidDataException(
                "An authored Metal static-model lighting pass has no scene atlas.");
        var objectSeen = new bool[atlas.EntryCount];
        var objectIndices = new List<int>();
        foreach (MapRenderEditorDrawGroup<
                     RenderNormalCameraDrawSubmissionSnapshot> group in
                 snapshot.NormalCameraDraws.DrawGroups)
        {
            if (!_normalCameraAuthorizedGroups.Contains(group))
                continue;
            RenderNormalCameraDrawSubmissionSnapshot first =
                group.AuthoredPasses[0];
            RenderNormalCameraPreparedPassSnapshot pass = first.PreparedPass;
            if (pass.SourceKind !=
                    RenderNormalCameraDrawSourceKind.StaticModel ||
                pass.StaticReceiverVariant is not null)
            {
                continue;
            }

            int firstInstance = first.Range.FirstInstance;
            int endInstance = checked(
                firstInstance + first.Range.InstanceCount);
            if (firstInstance < 0 ||
                endInstance > pass.StaticInstances.Length)
            {
                throw new InvalidDataException(
                    "A Metal static draw range is outside its object-indexed instance source.");
            }
            for (int instanceIndex = firstInstance;
                 instanceIndex < endInstance;
                 instanceIndex++)
            {
                int objectIndex =
                    pass.StaticInstances[instanceIndex].ObjectIndex;
                if ((uint)objectIndex >= (uint)atlas.EntryCount)
                {
                    throw new InvalidDataException(
                        "A renderable static object is outside the model-lighting source atlas.");
                }
                if (objectSeen[objectIndex])
                    continue;
                objectSeen[objectIndex] = true;
                objectIndices.Add(objectIndex);
            }
        }

        var workingSet = new MapRenderStaticModelLightingWorkingSet(
            atlas.EntryCount);
        var resources = new MetalStaticModelLightingResources(
            _surface.Device,
            atlas);
        MTLBuffer[] instanceBuffers = new MTLBuffer[
            _staticModelLightingInstanceBuffers.Length];
        try
        {
            int cursor = 0;
            for (int passIndex = 0;
                 passIndex < consumers.Length;
                 passIndex++)
            {
                MetalPreparedNormalCameraPass pass = consumers[passIndex];
                pass.StaticModelLightingInstanceOffset = Align(cursor);
                cursor = checked(
                    pass.StaticModelLightingInstanceOffset +
                    pass.Source.StaticInstances.Length *
                    MapRenderStaticInstanceBufferPacker.FloatStride(
                        MapRenderStaticInstanceLightingPayload
                            .BaseLightingCoords) *
                    sizeof(float));
            }
            for (int frameSlot = 0;
                 frameSlot < instanceBuffers.Length;
                 frameSlot++)
            {
                instanceBuffers[frameSlot] = CreateSharedBuffer(cursor);
                BufferBytes(
                    instanceBuffers[frameSlot],
                    0,
                    cursor).Clear();
                PackStaticModelLightingInstances(
                    instanceBuffers[frameSlot],
                    consumers,
                    workingSet.CoordinatesByObject);
            }

            _staticModelLightingAtlas = atlas;
            _staticModelLightingWorkingSet = workingSet;
            _staticModelLightingResources = resources;
            _staticModelLightingPreparedPasses = consumers;
            _staticModelLightingObjectIndices = objectIndices.ToArray();
            _staticModelLightingAdmittedByObject = new bool[
                _normalCameraStaticVisible.Length];
            _staticModelLightingInstanceBufferByteCount = cursor;
            Array.Copy(
                instanceBuffers,
                _staticModelLightingInstanceBuffers,
                instanceBuffers.Length);
        }
        catch
        {
            for (int index = 0; index < instanceBuffers.Length; index++)
            {
                if (instanceBuffers[index].NativePtr != 0)
                    instanceBuffers[index].Dispose();
            }
            resources.Dispose();
            throw;
        }
    }

    private void DeleteStaticModelLightingResources()
    {
        for (int index = 0;
             index < _staticModelLightingInstanceBuffers.Length;
             index++)
        {
            if (_staticModelLightingInstanceBuffers[index].NativePtr != 0)
                _staticModelLightingInstanceBuffers[index].Dispose();
            _staticModelLightingInstanceBuffers[index] = default;
            _staticModelLightingInstanceGenerations[index] = 0;
        }
        _staticModelLightingResources?.Dispose();
        _staticModelLightingResources = null;
        _staticModelLightingAtlas = null;
        _staticModelLightingWorkingSet = null;
        _staticModelLightingPreparedPasses = [];
        _staticModelLightingObjectIndices = [];
        _staticModelLightingAdmittedByObject = [];
        _staticModelLightingInstanceBufferByteCount = 0;
        _staticModelLightingAdmittedVisibleObjectCount = 0;
        _staticModelLightingAdmissionFrameStateRevision = -1;
        _currentStaticModelLightingTexture = default;
    }

    private void ResetStaticModelLightingFrameState()
    {
        _staticModelLightingAdmissionFrameStateRevision = -1;
        _currentStaticModelLightingTexture = default;
        RecordStaticModelLightingTelemetry(null);
    }

    private void PrepareStaticModelLighting()
    {
        MapRenderStaticModelLightingWorkingSet? workingSet =
            _staticModelLightingWorkingSet;
        MetalStaticModelLightingResources? resources =
            _staticModelLightingResources;
        if (workingSet is null || resources is null)
            return;
        if (_staticModelLightingAdmittedByObject.Length !=
            _normalCameraStaticVisible.Length)
        {
            throw new InvalidOperationException(
                "Metal static-model lighting visibility lost its object-indexed storage.");
        }

        using MapRenderCpuPhaseScope cpuPhase =
            _telemetry.BeginCpuPhase(
                MapRenderCpuPhase.StaticResourceAdmission);
        Array.Copy(
            _normalCameraStaticVisible,
            _staticModelLightingAdmittedByObject,
            _normalCameraStaticVisible.Length);
        workingSet.UpdateFrame(
            _staticModelLightingAdmittedByObject,
            _staticModelLightingObjectIndices);
        resources.ApplyAssignments(workingSet.DirtyAssignments);

        int frameSlot = checked((int)(
            _frameIndex %
            MetalStaticModelLightingResources.FrameSlotCount));
        MTLTexture texture = resources.PrepareFrameSlot(frameSlot);
        MTLBuffer instanceBuffer =
            _staticModelLightingInstanceBuffers[frameSlot];
        if (instanceBuffer.NativePtr == 0 ||
            instanceBuffer.Contents == 0 ||
            _staticModelLightingInstanceBufferByteCount == 0)
        {
            throw new InvalidOperationException(
                "Metal static-model lighting has no frame-safe instance buffer.");
        }
        if (_staticModelLightingInstanceGenerations[frameSlot] !=
            workingSet.AssignmentGeneration)
        {
            PackStaticModelLightingInstances(
                instanceBuffer,
                _staticModelLightingPreparedPasses,
                workingSet.CoordinatesByObject);
            _staticModelLightingInstanceGenerations[frameSlot] =
                workingSet.AssignmentGeneration;
        }

        _currentStaticModelLightingTexture = texture;
        _staticModelLightingAdmissionFrameStateRevision =
            _normalCameraFrameStateRevision;
        _staticModelLightingAdmittedVisibleObjectCount = Math.Max(
            0,
            _normalCameraStaticVisibleObjectCount -
                workingSet.AllocationMissCount);
        RecordStaticModelLightingTelemetry(workingSet);
    }

    private bool IsStaticModelLightingObjectAdmitted(int objectIndex)
    {
        if (_staticModelLightingWorkingSet is null)
            return true;
        if (_staticModelLightingAdmissionFrameStateRevision !=
            _normalCameraFrameStateRevision)
        {
            return false;
        }
        return (uint)objectIndex <
                   (uint)_staticModelLightingAdmittedByObject.Length &&
               _staticModelLightingAdmittedByObject[objectIndex];
    }

    private int ResolveStaticModelLightingVisibleObjectCount() =>
        _staticModelLightingAdmissionFrameStateRevision ==
            _normalCameraFrameStateRevision
            ? _staticModelLightingAdmittedVisibleObjectCount
            : _normalCameraStaticVisibleObjectCount;

    private void RequireStaticModelLightingInstanceBinding(
        MetalPreparedNormalCameraPass pass,
        out MTLBuffer buffer,
        out ulong offset)
    {
        ArgumentNullException.ThrowIfNull(pass);
        if (pass.LightingPayload !=
            MapRenderStaticInstanceLightingPayload.BaseLightingCoords ||
            _staticModelLightingAdmissionFrameStateRevision !=
                _normalCameraFrameStateRevision)
        {
            throw new InvalidOperationException(
                "A row-0x39 Metal draw reached binding without current working-set admission.");
        }
        int frameSlot = checked((int)(
            _frameIndex %
            MetalStaticModelLightingResources.FrameSlotCount));
        buffer = _staticModelLightingInstanceBuffers[frameSlot];
        if (buffer.NativePtr == 0)
        {
            throw new InvalidOperationException(
                "A row-0x39 Metal draw has no frame-safe instance buffer.");
        }
        offset = checked((ulong)pass.StaticModelLightingInstanceOffset);
    }

    private void RequireStaticModelLightingSamplerBinding(
        out MTLTexture texture,
        out MTLSamplerState sampler)
    {
        if (_staticModelLightingAdmissionFrameStateRevision !=
                _normalCameraFrameStateRevision ||
            _currentStaticModelLightingTexture.NativePtr == 0 ||
            _staticModelLightingResources is not { } resources)
        {
            throw new InvalidOperationException(
                "A model-lighting sampler draw reached binding without the current atlas publication.");
        }
        texture = _currentStaticModelLightingTexture;
        sampler = resources.Sampler;
    }

    private static void PackStaticModelLightingInstances(
        MTLBuffer buffer,
        IReadOnlyList<MetalPreparedNormalCameraPass> passes,
        Vector4[] coordinatesByObject)
    {
        for (int passIndex = 0;
             passIndex < passes.Count;
             passIndex++)
        {
            MetalPreparedNormalCameraPass pass = passes[passIndex];
            int floatCount = checked(
                pass.Source.StaticInstances.Length *
                MapRenderStaticInstanceBufferPacker.FloatStride(
                    MapRenderStaticInstanceLightingPayload
                        .BaseLightingCoords));
            MapRenderStaticInstanceBufferPacker.PackAll(
                pass.Source.StaticInstances,
                MapRenderStaticInstanceLightingPayload.BaseLightingCoords,
                BufferFloats(
                    buffer,
                    pass.StaticModelLightingInstanceOffset,
                    floatCount),
                coordinatesByObject);
        }
    }

    private void RecordStaticModelLightingTelemetry(
        MapRenderStaticModelLightingWorkingSet? workingSet)
    {
        _telemetry.SetCounter(
            MapRenderFrameCounter.StaticModelLightingResidentEntries,
            workingSet?.ResidentEntryCount ?? 0);
        _telemetry.SetCounter(
            MapRenderFrameCounter.StaticModelLightingAllocationMisses,
            workingSet?.AllocationMissCount ?? 0);
        _telemetry.SetCounter(
            MapRenderFrameCounter.StaticModelLightingNewAssignments,
            workingSet?.NewAssignmentCount ?? 0);
        _telemetry.SetCounter(
            MapRenderFrameCounter.StaticModelLightingRecycledAssignments,
            workingSet?.RecycledAssignmentCount ?? 0);
    }
}
