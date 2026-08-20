using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using IW4.Assets.Assets.XModel;
using IW4.Render.Geometry.Shadows;
using IW4.Render.Metal.Targets;
using IW4.Render.Resources;
using IW4.Render.Scheduling.FramePlans;
using IW4.Render.Scheduling.Shadows;
using IW4.Render.Scheduling.StaticModels;
using IW4.Render.Textures;

using SharpMetal.Metal;

namespace IW4.Render.Metal.Resources;

/// <summary>
/// Scene-lifetime native caster geometry and cutout bindings plus a rotating
/// frame-lifetime static-instance upload. Geometry packing and admission stay
/// owned by IW4.Render; this type only assigns immutable Metal resources and
/// compacts an already-selected partition into its current ring slice.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe class MetalShadowCasterResources : IDisposable
{
    internal const int SelectionSliceCount =
        MetalShadowAtlases.SunPartitionCount +
        MetalShadowAtlases.SpotTileCount;

    private const int InstanceRowCount = 3;
    private const int InstanceStrideBytes =
        InstanceRowCount * sizeof(float) * 4;

    private readonly MetalResourceCache _cache;
    private readonly MTLBuffer[] _dynamicInstanceBuffers;
    private readonly Dictionary<int, MapRenderSunShadowWorldCasterRejection>
        _worldRejections;
    private readonly HashSet<int> _executableWorldSurfaces;
    private readonly HashSet<int> _admittedWorldSurfaces;
    private readonly StaticShadowObject?[] _staticObjects;
    private readonly bool[] _admittedStaticObjects;
    private readonly int[] _selectedLodByObject;
    private readonly HashSet<StaticCasterKey> _executableStaticCasters;
    private readonly MapRenderSunShadowStaticCasterExpectation[]
        _staticExpectations;
    private readonly int _staticInstanceCapacityPerPartition;
    private bool _disposed;

    private MetalShadowCasterResources(
        MetalResourceCache cache,
        MTLBuffer[] dynamicInstanceBuffers,
        MetalShadowWorldCasterRuntime[] worldCasters,
        MetalShadowStaticCasterRuntime[] staticCasters,
        Dictionary<int, MapRenderSunShadowWorldCasterRejection>
            worldRejections,
        HashSet<int> executableWorldSurfaces,
        StaticShadowObject?[] staticObjects,
        HashSet<StaticCasterKey> executableStaticCasters,
        MapRenderSunShadowStaticCasterExpectation[] staticExpectations,
        int staticInstanceCapacityPerPartition)
    {
        _cache = cache;
        _dynamicInstanceBuffers = dynamicInstanceBuffers;
        WorldCasters = worldCasters;
        StaticCasters = staticCasters;
        _worldRejections = worldRejections;
        _executableWorldSurfaces = executableWorldSurfaces;
        _admittedWorldSurfaces = new HashSet<int>(
            executableWorldSurfaces.Count);
        _staticObjects = staticObjects;
        _admittedStaticObjects = new bool[staticObjects.Length];
        _selectedLodByObject = new int[staticObjects.Length];
        _executableStaticCasters = executableStaticCasters;
        _staticExpectations = staticExpectations;
        _staticInstanceCapacityPerPartition =
            staticInstanceCapacityPerPartition;
    }

    internal IReadOnlyList<MetalShadowWorldCasterRuntime> WorldCasters
        { get; }

    internal IReadOnlyList<MetalShadowStaticCasterRuntime> StaticCasters
        { get; }

    internal bool HasCasters =>
        WorldCasters.Count != 0 || StaticCasters.Count != 0;

    internal int StaticInstanceCapacityPerPartition =>
        _staticInstanceCapacityPerPartition;

    internal static MetalShadowCasterResources Create(
        MTLDevice device,
        MTLCommandQueue commandQueue,
        MapRenderScene scene,
        int frameBufferCount)
    {
        if (device.NativePtr == 0)
            throw new ArgumentException("A Metal device is required.", nameof(device));
        if (commandQueue.NativePtr == 0)
        {
            throw new ArgumentException(
                "A Metal command queue is required.",
                nameof(commandQueue));
        }
        ArgumentNullException.ThrowIfNull(scene);
        if (frameBufferCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(frameBufferCount));

        IReadOnlyList<MapRenderSunShadowWorldCasterPackedBatch> packedWorld =
            MapRenderSunShadowWorldCasterPacker.Pack(
                scene.SunShadowWorldCasterBatches);
        var layoutsByCutout = new Dictionary<
            bool,
            RenderVertexLayoutDescriptor>();
        var layouts = new List<RenderVertexLayoutDescriptor>(2);
        var geometries = new List<RenderGeometryDescriptor>(
            packedWorld.Count + scene.SunShadowStaticCasterBatches.Count);
        var textures = new List<RenderTextureDescriptor>();
        var samplers = new List<RenderSamplerDescriptor>();
        var textureBindings = new Dictionary<Texture, ShadowTextureBinding>(
            ReferenceEqualityComparer.Instance);
        var worldSpecs = new WorldRuntimeSpec[packedWorld.Count];
        var staticSpecs = new StaticRuntimeSpec[
            scene.SunShadowStaticCasterBatches.Count];

        for (int index = 0; index < packedWorld.Count; index++)
        {
            MapRenderSunShadowWorldCasterPackedBatch batch =
                packedWorld[index];
            RenderVertexLayoutDescriptor layout = RequireLayout(
                batch.Geometry.HasCutoutUv,
                layoutsByCutout,
                layouts);
            RenderGeometryDescriptor geometry = CreateGeometry(
                batch.Geometry,
                layout,
                Identity(
                    RenderSemanticResourceKind.Geometry,
                    "world",
                    index));
            geometries.Add(geometry);
            ShadowTextureBinding? cutout = ResolveCutoutBinding(
                batch.CutoutTexture,
                textureBindings,
                textures,
                samplers);
            worldSpecs[index] = new(
                batch,
                geometry.Identity,
                cutout);
        }

        int staticCapacity = 0;
        for (int index = 0;
             index < scene.SunShadowStaticCasterBatches.Count;
             index++)
        {
            MapRenderStaticSunShadowCasterBatch batch =
                scene.SunShadowStaticCasterBatches[index];
            RenderVertexLayoutDescriptor layout = RequireLayout(
                batch.Geometry.HasCutoutUv,
                layoutsByCutout,
                layouts);
            RenderGeometryDescriptor geometry = CreateGeometry(
                batch.Geometry,
                layout,
                Identity(
                    RenderSemanticResourceKind.Geometry,
                    "static",
                    index));
            geometries.Add(geometry);
            ShadowTextureBinding? cutout = ResolveCutoutBinding(
                batch.CutoutTexture,
                textureBindings,
                textures,
                samplers);
            staticSpecs[index] = new(
                batch,
                geometry.Identity,
                cutout,
                staticCapacity);
            staticCapacity = checked(
                staticCapacity + batch.Instances.Count);
        }

        var resourceSnapshot = new RenderResourceSnapshot(
            layouts,
            geometries,
            textures,
            samplers);
        var cache = new MetalResourceCache(device, commandQueue);
        var instanceBuffers = new MTLBuffer[frameBufferCount];
        try
        {
            cache.Load(resourceSnapshot);
            MetalShadowWorldCasterRuntime[] worldRuntimes = worldSpecs
                .Select(spec => CreateWorldRuntime(cache, spec))
                .ToArray();
            MetalShadowStaticCasterRuntime[] staticRuntimes = staticSpecs
                .Select(spec => CreateStaticRuntime(cache, spec))
                .ToArray();
            if (staticCapacity != 0)
            {
                ulong byteCount = checked((ulong)(
                    staticCapacity *
                    SelectionSliceCount *
                    InstanceStrideBytes));
                for (int index = 0;
                     index < instanceBuffers.Length;
                     index++)
                {
                    instanceBuffers[index] = device.NewBuffer(
                        byteCount,
                        MTLResourceOptions.ResourceStorageModeShared |
                        MTLResourceOptions.ResourceCPUCacheModeWriteCombined);
                    if (instanceBuffers[index].NativePtr == 0 ||
                        instanceBuffers[index].Contents == 0)
                    {
                        throw new InvalidOperationException(
                            $"Metal failed to allocate shadow instance ring slot {index}.");
                    }
                }
            }

            BuildStaticIndex(
                scene,
                out StaticShadowObject?[] staticObjects,
                out HashSet<StaticCasterKey> executableStaticCasters);
            var executableWorld = new HashSet<int>();
            foreach (MetalShadowWorldCasterRuntime runtime in worldRuntimes)
            {
                foreach (MapRenderSunShadowWorldCasterSurfaceSpan span in
                         runtime.Batch.Spans)
                {
                    executableWorld.Add(span.SurfaceIndex);
                }
            }

            return new MetalShadowCasterResources(
                cache,
                instanceBuffers,
                worldRuntimes,
                staticRuntimes,
                scene.SunShadowWorldCasterRejections.ToDictionary(
                    rejection => rejection.SurfaceIndex),
                executableWorld,
                staticObjects,
                executableStaticCasters,
                scene.SunShadowStaticCasterExpectations.ToArray(),
                staticCapacity);
        }
        catch
        {
            DisposeBuffers(instanceBuffers);
            cache.Dispose();
            throw;
        }
    }

    internal int PrepareWorldPartition(
        MapRenderSunShadowCasterPartition partition)
    {
        ArgumentNullException.ThrowIfNull(partition);
        return PrepareWorldSelection(partition.WorldSurfaceIndices);
    }

    internal int PrepareWorldSelection(
        IReadOnlyList<int> worldSurfaceIndices)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(worldSurfaceIndices);
        ValidateWorldSelection(worldSurfaceIndices);

        int drawRunCount = 0;
        foreach (MetalShadowWorldCasterRuntime runtime in WorldCasters)
        {
            runtime.DrawRunCount =
                MapRenderSunShadowWorldCasterPacker
                    .CompactAdmittedDrawRuns(
                        runtime.Batch.Spans,
                        _admittedWorldSurfaces,
                        runtime.DrawRuns);
            drawRunCount = checked(
                drawRunCount + runtime.DrawRunCount);
        }
        return drawRunCount;
    }

    internal void ValidateSpotSelection(
        MapRenderSpotShadowPlan plan,
        Vector3 nativeCameraOrigin)
    {
        ThrowIfDisposed();
        ValidateWorldSelection(plan.WorldSurfaceIndices);
        SelectStaticMembership(
            plan.StaticDrawInstances,
            nativeCameraOrigin);
        ValidateStaticCoverage();
    }

    private void ValidateWorldSelection(
        IReadOnlyList<int> worldSurfaceIndices)
    {
        _admittedWorldSurfaces.Clear();
        foreach (int surfaceIndex in worldSurfaceIndices)
        {
            if (_executableWorldSurfaces.Contains(surfaceIndex))
            {
                _admittedWorldSurfaces.Add(surfaceIndex);
                continue;
            }
            if (_worldRejections.TryGetValue(
                    surfaceIndex,
                    out MapRenderSunShadowWorldCasterRejection? rejection) &&
                rejection.IsNativeSelectorRejection)
            {
                continue;
            }

            string detail = rejection is null
                ? "unclassified"
                : $"{rejection.Kind}:{rejection.Detail}";
            throw new InvalidOperationException(
                $"Admitted world caster surface {surfaceIndex} has no exact slot-2 Metal payload ({detail}).");
        }
    }

    internal int PrepareStaticPartition(
        MapRenderSunShadowCasterPartition partition,
        Vector3 nativeCameraOrigin,
        int frameSlot)
    {
        ArgumentNullException.ThrowIfNull(partition);
        if ((uint)partition.PartitionIndex >=
            MetalShadowAtlases.SunPartitionCount)
        {
            throw new ArgumentOutOfRangeException(nameof(partition));
        }
        return PrepareStaticSelection(
            partition.StaticDrawInstances,
            nativeCameraOrigin,
            frameSlot,
            partition.PartitionIndex);
    }

    internal int PrepareStaticSelection(
        IReadOnlyList<MapRenderSunShadowStaticCasterIdentity>
            staticDrawInstances,
        Vector3 nativeCameraOrigin,
        int frameSlot,
        int selectionSlice)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(staticDrawInstances);
        if ((uint)frameSlot >= (uint)_dynamicInstanceBuffers.Length)
            throw new ArgumentOutOfRangeException(nameof(frameSlot));
        if ((uint)selectionSlice >= SelectionSliceCount)
            throw new ArgumentOutOfRangeException(nameof(selectionSlice));

        SelectStaticMembership(staticDrawInstances, nativeCameraOrigin);
        ValidateStaticCoverage();
        if (_staticInstanceCapacityPerPartition == 0)
            return 0;

        MTLBuffer buffer = _dynamicInstanceBuffers[frameSlot];
        int selectedInstanceCount = 0;
        foreach (MetalShadowStaticCasterRuntime runtime in StaticCasters)
        {
            int count = 0;
            ulong sliceOffset = runtime.InstanceOffset(
                selectionSlice,
                _staticInstanceCapacityPerPartition);
            Vector4* destination = (Vector4*)(
                buffer.Contents + checked((nint)sliceOffset));
            foreach (MapRenderSunShadowStaticCasterInstance candidate in
                     runtime.Batch.Instances)
            {
                int objectIndex = candidate.ObjectIndex;
                if ((uint)objectIndex >=
                        (uint)_selectedLodByObject.Length ||
                    _selectedLodByObject[objectIndex] !=
                        runtime.Batch.LodIndex)
                {
                    continue;
                }

                int firstRow = checked(count * InstanceRowCount);
                destination[firstRow] = candidate.Instance.TransformRow0;
                destination[firstRow + 1] =
                    candidate.Instance.TransformRow1;
                destination[firstRow + 2] =
                    candidate.Instance.TransformRow2;
                count++;
            }
            runtime.SetInstanceCount(selectionSlice, count);
            selectedInstanceCount = checked(
                selectedInstanceCount + count);
        }
        return selectedInstanceCount;
    }

    private void SelectStaticMembership(
        IReadOnlyList<MapRenderSunShadowStaticCasterIdentity>
            staticDrawInstances,
        Vector3 nativeCameraOrigin)
    {
        Array.Fill(
            _selectedLodByObject,
            MapRenderStaticModelLodSelector.CulledLodIndex);
        Array.Clear(_admittedStaticObjects);
        foreach (MapRenderSunShadowStaticCasterIdentity identity in
                 staticDrawInstances)
        {
            int objectIndex = identity.ObjectIndex;
            if ((uint)objectIndex >= (uint)_staticObjects.Length)
                continue;
            _admittedStaticObjects[objectIndex] = true;
            StaticShadowObject? objectRuntime =
                _staticObjects[objectIndex];
            if (objectRuntime is null)
                continue;

            MapRenderSunShadowStaticCasterInstance instance =
                objectRuntime.Instance;
            float cameraDistance = Vector3.Distance(
                nativeCameraOrigin,
                instance.GameOrigin);
            if (instance.CullDistance != 0 &&
                MapRenderStaticModelLodSelector.IsBeyondCullDistance(
                    instance.CullDistance,
                    cameraDistance,
                    viewDistanceScale: 1f))
            {
                continue;
            }
            if (MapRenderStaticModelLodSelector.TrySelectForCameraDistance(
                    objectRuntime.Model,
                    cameraDistance,
                    instance.PlacementScale,
                    nearViewScale: 1f,
                    farViewScale: 1f,
                    out int selectedLod))
            {
                _selectedLodByObject[objectIndex] = selectedLod;
            }
        }
    }

    internal MTLBuffer RequireDynamicInstanceBuffer(int frameSlot)
    {
        ThrowIfDisposed();
        if ((uint)frameSlot >= (uint)_dynamicInstanceBuffers.Length)
            throw new ArgumentOutOfRangeException(nameof(frameSlot));
        MTLBuffer buffer = _dynamicInstanceBuffers[frameSlot];
        if (buffer.NativePtr == 0)
        {
            throw new InvalidOperationException(
                "The scene has no static shadow instance storage.");
        }
        return buffer;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        DisposeBuffers(_dynamicInstanceBuffers);
        _cache.Dispose();
    }

    private void ValidateStaticCoverage()
    {
        int missing = 0;
        foreach (MapRenderSunShadowStaticCasterExpectation expectation in
                 _staticExpectations)
        {
            int objectIndex = expectation.ObjectIndex;
            if ((uint)objectIndex >=
                    (uint)_admittedStaticObjects.Length ||
                !_admittedStaticObjects[objectIndex])
            {
                continue;
            }

            StaticShadowObject? objectRuntime =
                _staticObjects[objectIndex];
            bool selectedExpectation = objectRuntime is null ||
                _selectedLodByObject[objectIndex] ==
                    expectation.LodIndex;
            if (selectedExpectation &&
                !_executableStaticCasters.Contains(new(
                    objectIndex,
                    expectation.LodIndex,
                    expectation.MaterialSurfaceIndex)))
            {
                missing++;
            }
        }
        if (missing != 0)
        {
            throw new InvalidOperationException(
                $"Exact slot-2 Metal caster payload is unavailable for {missing} admitted static-model material surface(s).");
        }
    }

    private static void BuildStaticIndex(
        MapRenderScene scene,
        out StaticShadowObject?[] objects,
        out HashSet<StaticCasterKey> executableCasters)
    {
        int maximumObjectIndex = -1;
        foreach (MapRenderStaticSunShadowCasterBatch batch in
                 scene.SunShadowStaticCasterBatches)
        {
            foreach (MapRenderSunShadowStaticCasterInstance instance in
                     batch.Instances)
            {
                maximumObjectIndex = Math.Max(
                    maximumObjectIndex,
                    instance.ObjectIndex);
            }
        }
        foreach (MapRenderSunShadowStaticCasterExpectation expectation in
                 scene.SunShadowStaticCasterExpectations)
        {
            maximumObjectIndex = Math.Max(
                maximumObjectIndex,
                expectation.ObjectIndex);
        }

        objects = new StaticShadowObject?[maximumObjectIndex + 1];
        executableCasters = [];
        foreach (MapRenderStaticSunShadowCasterBatch batch in
                 scene.SunShadowStaticCasterBatches)
        {
            foreach (MapRenderSunShadowStaticCasterInstance instance in
                     batch.Instances)
            {
                int objectIndex = instance.ObjectIndex;
                var candidate = new StaticShadowObject(
                    batch.Model,
                    instance);
                StaticShadowObject? existing = objects[objectIndex];
                if (existing is not null)
                {
                    ValidateStaticObject(
                        objectIndex,
                        existing,
                        candidate);
                }
                else
                {
                    objects[objectIndex] = candidate;
                }
                executableCasters.Add(new(
                    objectIndex,
                    batch.LodIndex,
                    batch.MaterialSurfaceIndex));
            }
        }
    }

    private static void ValidateStaticObject(
        int objectIndex,
        StaticShadowObject existing,
        StaticShadowObject candidate)
    {
        MapRenderSunShadowStaticCasterInstance first = existing.Instance;
        MapRenderSunShadowStaticCasterInstance next = candidate.Instance;
        if (!ReferenceEquals(existing.Model, candidate.Model) ||
            first.GameOrigin != next.GameOrigin ||
            first.PlacementScale != next.PlacementScale ||
            first.CullDistance != next.CullDistance ||
            first.Instance.TransformRow0 != next.Instance.TransformRow0 ||
            first.Instance.TransformRow1 != next.Instance.TransformRow1 ||
            first.Instance.TransformRow2 != next.Instance.TransformRow2)
        {
            throw new InvalidOperationException(
                $"Static shadow caster object {objectIndex} has divergent placement ownership.");
        }
    }

    private static MetalShadowWorldCasterRuntime CreateWorldRuntime(
        MetalResourceCache cache,
        WorldRuntimeSpec spec) => new(
            spec.Batch,
            cache.RequireGeometry(spec.GeometryIdentity),
            ResolveTexture(cache, spec.Cutout));

    private static MetalShadowStaticCasterRuntime CreateStaticRuntime(
        MetalResourceCache cache,
        StaticRuntimeSpec spec) => new(
            spec.Batch,
            cache.RequireGeometry(spec.GeometryIdentity),
            ResolveTexture(cache, spec.Cutout),
            spec.BaseInstanceOrdinal);

    private static MetalShadowTextureRuntime? ResolveTexture(
        MetalResourceCache cache,
        ShadowTextureBinding? binding) => binding is not { } value
            ? null
            : new(
                cache.RequireTexture(value.TextureIdentity),
                cache.RequireSampler(value.SamplerIdentity));

    private static RenderVertexLayoutDescriptor RequireLayout(
        bool cutout,
        IDictionary<bool, RenderVertexLayoutDescriptor> byCutout,
        ICollection<RenderVertexLayoutDescriptor> layouts)
    {
        if (byCutout.TryGetValue(
                cutout,
                out RenderVertexLayoutDescriptor? layout))
        {
            return layout;
        }

        int stride = (cutout
            ? MapRenderSunShadowCasterGeometry.CutoutVertexFloatCount
            : MapRenderSunShadowCasterGeometry.OpaqueVertexFloatCount) *
            sizeof(float);
        var elements = new List<RenderVertexElementDescriptor>
        {
            new(
                RenderVertexSemantic.Position,
                0,
                RenderVertexElementFormat.Float32x3,
                0)
        };
        if (cutout)
        {
            elements.Add(new(
                RenderVertexSemantic.Color,
                0,
                RenderVertexElementFormat.Float32x4,
                MapRenderSunShadowCasterGeometry.CutoutColorOffset *
                    sizeof(float)));
            elements.Add(new(
                RenderVertexSemantic.TextureCoordinate,
                0,
                RenderVertexElementFormat.Float32x2,
                MapRenderSunShadowCasterGeometry.CutoutUvOffset *
                    sizeof(float)));
        }
        layout = new RenderVertexLayoutDescriptor(
            new RenderSemanticIdentity(
                RenderSemanticResourceKind.VertexLayout,
                cutout
                    ? "metal.shadow.cutout.xyz-rgba-uv"
                    : "metal.shadow.opaque.xyz"),
            stride,
            elements);
        byCutout.Add(cutout, layout);
        layouts.Add(layout);
        return layout;
    }

    private static RenderGeometryDescriptor CreateGeometry(
        MapRenderSunShadowCasterGeometry source,
        RenderVertexLayoutDescriptor layout,
        RenderSemanticIdentity identity)
    {
        float[] vertices = source.Vertices.ToArray();
        uint[] indices = source.Indices.ToArray();
        return new RenderGeometryDescriptor(
            identity,
            layout,
            RenderGeometryCoordinateSpace.Render,
            RenderPrimitiveTopology.TriangleList,
            RenderIndexFormat.Unsigned32,
            source.VertexCount,
            indices.Length,
            MemoryMarshal.AsBytes(vertices.AsSpan()).ToArray(),
            MemoryMarshal.AsBytes(indices.AsSpan()).ToArray());
    }

    private static ShadowTextureBinding? ResolveCutoutBinding(
        Texture? source,
        IDictionary<Texture, ShadowTextureBinding> bindings,
        ICollection<RenderTextureDescriptor> textures,
        ICollection<RenderSamplerDescriptor> samplers)
    {
        if (source is null)
            return null;
        if (bindings.TryGetValue(source, out ShadowTextureBinding binding))
            return binding;

        int ordinal = bindings.Count;
        RenderSemanticIdentity textureIdentity = Identity(
            RenderSemanticResourceKind.Texture,
            "cutout-texture",
            ordinal);
        RenderSemanticIdentity samplerIdentity = Identity(
            RenderSemanticResourceKind.Sampler,
            "cutout-sampler",
            ordinal);
        RenderTextureDescriptor texture =
            RenderSceneSnapshotBuilder.CreateTextureDescriptor(
                source,
                textureIdentity,
                preferProvenAuthoredPayload: true);
        var sampler = new RenderSamplerDescriptor(
            samplerIdentity,
            source.DecodedSamplerState);
        binding = new(textureIdentity, samplerIdentity);
        bindings.Add(source, binding);
        textures.Add(texture);
        samplers.Add(sampler);
        return binding;
    }

    private static RenderSemanticIdentity Identity(
        RenderSemanticResourceKind kind,
        string role,
        int ordinal) => new(
            kind,
            $"metal.shadow.{role}.{ordinal}");

    private static void DisposeBuffers(IList<MTLBuffer> buffers)
    {
        for (int index = 0; index < buffers.Count; index++)
        {
            MTLBuffer buffer = buffers[index];
            if (buffer.NativePtr == 0)
                continue;
            buffer.Dispose();
            buffers[index] = default;
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed record StaticShadowObject(
        XModelAsset Model,
        MapRenderSunShadowStaticCasterInstance Instance);

    private readonly record struct StaticCasterKey(
        int ObjectIndex,
        int LodIndex,
        int MaterialSurfaceIndex);

    private readonly record struct ShadowTextureBinding(
        RenderSemanticIdentity TextureIdentity,
        RenderSemanticIdentity SamplerIdentity);

    private readonly record struct WorldRuntimeSpec(
        MapRenderSunShadowWorldCasterPackedBatch Batch,
        RenderSemanticIdentity GeometryIdentity,
        ShadowTextureBinding? Cutout);

    private readonly record struct StaticRuntimeSpec(
        MapRenderStaticSunShadowCasterBatch Batch,
        RenderSemanticIdentity GeometryIdentity,
        ShadowTextureBinding? Cutout,
        int BaseInstanceOrdinal);
}

[SupportedOSPlatform("macos")]
internal sealed class MetalShadowWorldCasterRuntime
{
    internal MetalShadowWorldCasterRuntime(
        MapRenderSunShadowWorldCasterPackedBatch batch,
        MetalGeometryResource geometry,
        MetalShadowTextureRuntime? cutout)
    {
        Batch = batch ?? throw new ArgumentNullException(nameof(batch));
        Geometry = geometry ??
            throw new ArgumentNullException(nameof(geometry));
        if ((batch.CutoutTexture is not null) != (cutout is not null))
        {
            throw new ArgumentException(
                "World cutout texture ownership diverged during Metal materialization.",
                nameof(cutout));
        }
        Cutout = cutout;
        DrawRuns = new MapRenderSunShadowWorldCasterDrawRun[
            batch.Spans.Count];
    }

    internal MapRenderSunShadowWorldCasterPackedBatch Batch { get; }

    internal MetalGeometryResource Geometry { get; }

    internal MetalShadowTextureRuntime? Cutout { get; }

    internal MapRenderSunShadowWorldCasterDrawRun[] DrawRuns { get; }

    internal int DrawRunCount { get; set; }
}

[SupportedOSPlatform("macos")]
internal sealed class MetalShadowStaticCasterRuntime
{
    private readonly int[] _instanceCounts =
        new int[MetalShadowCasterResources.SelectionSliceCount];

    internal MetalShadowStaticCasterRuntime(
        MapRenderStaticSunShadowCasterBatch batch,
        MetalGeometryResource geometry,
        MetalShadowTextureRuntime? cutout,
        int baseInstanceOrdinal)
    {
        Batch = batch ?? throw new ArgumentNullException(nameof(batch));
        Geometry = geometry ??
            throw new ArgumentNullException(nameof(geometry));
        if ((batch.CutoutTexture is not null) != (cutout is not null))
        {
            throw new ArgumentException(
                "Static cutout texture ownership diverged during Metal materialization.",
                nameof(cutout));
        }
        if (baseInstanceOrdinal < 0)
            throw new ArgumentOutOfRangeException(nameof(baseInstanceOrdinal));
        Cutout = cutout;
        BaseInstanceOrdinal = baseInstanceOrdinal;
    }

    internal MapRenderStaticSunShadowCasterBatch Batch { get; }

    internal MetalGeometryResource Geometry { get; }

    internal MetalShadowTextureRuntime? Cutout { get; }

    internal int BaseInstanceOrdinal { get; }

    internal int InstanceCount(int selectionSlice) =>
        _instanceCounts[RequireSelectionSlice(selectionSlice)];

    internal ulong InstanceOffset(
        int selectionSlice,
        int capacityPerPartition)
    {
        if (capacityPerPartition <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacityPerPartition));
        }
        int slice = RequireSelectionSlice(selectionSlice);
        return checked((ulong)(
            (slice * capacityPerPartition + BaseInstanceOrdinal) *
            3 * sizeof(float) * 4));
    }

    internal void SetInstanceCount(int selectionSlice, int count)
    {
        if ((uint)count > (uint)Batch.Instances.Count)
            throw new ArgumentOutOfRangeException(nameof(count));
        _instanceCounts[RequireSelectionSlice(selectionSlice)] = count;
    }

    private static int RequireSelectionSlice(int selectionSlice) =>
        (uint)selectionSlice < MetalShadowCasterResources.SelectionSliceCount
            ? selectionSlice
            : throw new ArgumentOutOfRangeException(nameof(selectionSlice));
}

[SupportedOSPlatform("macos")]
internal sealed record MetalShadowTextureRuntime(
    MetalTextureResource Texture,
    MetalSamplerResource Sampler);
