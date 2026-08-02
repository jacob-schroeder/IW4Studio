using System.Numerics;
using IW4.Assets.Assets.XModel;
using IW4.Render.Geometry.Shadows;
using IW4.Render.Scheduling.Shadows;
using IW4.Render.Scheduling.StaticModels;

namespace IW4.Render.OpenGl.Shadows;

/// <summary>
/// Context-owned geometry for exact slot-2 caster submissions. A world mesh
/// may contain several independently admitted surface spans for one canonical
/// material; a static mesh owns a dynamic instance buffer. CutoutTexture is
/// the authored route-02 texture and is never a backend-selected substitute.
/// </summary>
internal readonly record struct MapRenderOpenGlSunShadowCasterMesh(
    uint VertexArray,
    uint VertexBuffer,
    uint ElementBuffer,
    uint InstanceBuffer,
    uint IndexCount,
    uint CutoutTexture,
    bool IsCutout,
    bool HasVertexColor);

internal sealed class MapRenderOpenGlSunShadowWorldCasterRuntime
{
    internal MapRenderOpenGlSunShadowWorldCasterRuntime(
        MapRenderSunShadowWorldCasterPackedBatch batch,
        MapRenderOpenGlSunShadowCasterMesh mesh)
    {
        Batch = batch ?? throw new ArgumentNullException(nameof(batch));
        Mesh = mesh;
        CompactDrawRuns = new MapRenderSunShadowWorldCasterDrawRun[
            batch.Spans.Count];
    }

    public MapRenderSunShadowWorldCasterPackedBatch Batch { get; }

    public MapRenderOpenGlSunShadowCasterMesh Mesh { get; }

    public MapRenderSunShadowWorldCasterDrawRun[] CompactDrawRuns
    { get; }
}

internal readonly record struct
    MapRenderOpenGlSunShadowWorldCasterSurfaceRuntime(
        MapRenderOpenGlSunShadowWorldCasterRuntime Runtime,
        MapRenderSunShadowWorldCasterSurfaceSpan Span);

internal sealed class MapRenderOpenGlSunShadowStaticCasterRuntime
{
    internal MapRenderOpenGlSunShadowStaticCasterRuntime(
        MapRenderStaticSunShadowCasterBatch batch,
        MapRenderOpenGlSunShadowCasterMesh mesh)
        : this(batch, mesh, mesh)
    {
    }

    internal MapRenderOpenGlSunShadowStaticCasterRuntime(
        MapRenderStaticSunShadowCasterBatch batch,
        MapRenderOpenGlSunShadowCasterMesh partition0Mesh,
        MapRenderOpenGlSunShadowCasterMesh partition1Mesh)
    {
        Batch = batch ?? throw new ArgumentNullException(nameof(batch));
        Partitions =
        [
            new MapRenderOpenGlSunShadowStaticCasterPartitionRuntime(
                partitionIndex: 0,
                partition0Mesh,
                batch.Instances.Count),
            new MapRenderOpenGlSunShadowStaticCasterPartitionRuntime(
                partitionIndex: 1,
                partition1Mesh,
                batch.Instances.Count)
        ];
        CompactTransforms = new float[checked(batch.Instances.Count * 12)];
    }

    public MapRenderStaticSunShadowCasterBatch Batch { get; private set; }

    public MapRenderOpenGlSunShadowCasterMesh Mesh => Partitions[0].Mesh;

    public IReadOnlyList<MapRenderOpenGlSunShadowStaticCasterPartitionRuntime>
        Partitions { get; }

    public float[] CompactTransforms { get; }

    internal MapRenderOpenGlSunShadowStaticCasterPartitionRuntime
        GetPartition(int partitionIndex) =>
        (uint)partitionIndex < (uint)Partitions.Count
            ? Partitions[partitionIndex]
            : throw new ArgumentOutOfRangeException(nameof(partitionIndex));

    internal void ReplaceBatch(
        MapRenderStaticSunShadowCasterBatch batch)
    {
        ValidateReplacementBatch(batch);
        Batch = batch;
        foreach (MapRenderOpenGlSunShadowStaticCasterPartitionRuntime
                 partition in Partitions)
        {
            partition.InvalidateSelection();
        }
    }

    internal void ValidateReplacementBatch(
        MapRenderStaticSunShadowCasterBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (batch.Instances.Count != Batch.Instances.Count ||
            batch.LodIndex != Batch.LodIndex ||
            batch.MaterialSurfaceIndex !=
                Batch.MaterialSurfaceIndex ||
            !ReferenceEquals(batch.Model, Batch.Model) ||
            !ReferenceEquals(batch.Lod, Batch.Lod) ||
            !ReferenceEquals(batch.Surface, Batch.Surface) ||
            !ReferenceEquals(batch.Material, Batch.Material) ||
            !ReferenceEquals(batch.Geometry, Batch.Geometry))
        {
            throw new InvalidOperationException(
                "A live static-model projection cannot change sun-shadow caster topology.");
        }
    }
}

/// <summary>
/// Partition-local static-caster upload state. The selected source-index
/// sequence is the exact identity of the compact instance payload; retaining
/// it independently for each native sun partition lets an unchanged payload
/// reuse its already-uploaded GPU slice without conflating the two views.
/// </summary>
internal sealed class MapRenderOpenGlSunShadowStaticCasterPartitionRuntime
{
    private readonly int[] _uploadedSourceIndices;
    private readonly int[] _candidateSourceIndices;
    private int _uploadedCount = -1;
    private int _candidateCount;

    internal MapRenderOpenGlSunShadowStaticCasterPartitionRuntime(
        int partitionIndex,
        MapRenderOpenGlSunShadowCasterMesh mesh,
        int capacity)
    {
        if ((uint)partitionIndex >= 2u)
            throw new ArgumentOutOfRangeException(nameof(partitionIndex));
        if (capacity < 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        PartitionIndex = partitionIndex;
        Mesh = mesh;
        _uploadedSourceIndices = new int[capacity];
        _candidateSourceIndices = new int[capacity];
    }

    public int PartitionIndex { get; }

    public MapRenderOpenGlSunShadowCasterMesh Mesh { get; }

    internal bool HasCommittedSelection => _uploadedCount >= 0;

    internal int InstanceCount => Math.Max(_uploadedCount, 0);

    internal void BeginSelection() => _candidateCount = 0;

    internal void InvalidateSelection()
    {
        _uploadedCount = -1;
        _candidateCount = 0;
    }

    internal void AddSelectedSourceIndex(int sourceIndex)
    {
        if ((uint)sourceIndex >= (uint)_candidateSourceIndices.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceIndex),
                "A static-caster source index exceeded its immutable batch capacity.");
        }
        if (_candidateCount == _candidateSourceIndices.Length)
        {
            throw new InvalidOperationException(
                "A static-caster partition selected more instances than its immutable batch owns.");
        }

        _candidateSourceIndices[_candidateCount++] = sourceIndex;
    }

    /// <summary>
    /// Commits the exact selected sequence and reports whether the GPU slice
    /// needs replacement. An initial empty selection is a change so its state
    /// is fully initialized, but it requires no zero-byte upload.
    /// </summary>
    internal bool CommitSelection()
    {
        bool changed = _uploadedCount != _candidateCount;
        if (!changed)
        {
            for (int index = 0; index < _candidateCount; index++)
            {
                if (_uploadedSourceIndices[index] ==
                    _candidateSourceIndices[index])
                {
                    continue;
                }

                changed = true;
                break;
            }
        }

        if (changed)
        {
            Array.Copy(
                _candidateSourceIndices,
                _uploadedSourceIndices,
                _candidateCount);
            _uploadedCount = _candidateCount;
        }
        return changed;
    }

    internal int GetSelectedSourceIndex(int compactIndex)
    {
        if ((uint)compactIndex >= (uint)InstanceCount)
            throw new ArgumentOutOfRangeException(nameof(compactIndex));
        return _uploadedSourceIndices[compactIndex];
    }
}

/// <summary>
/// Context-owned lookup for the native static caster path. Scene batches are
/// material-surface shaped, so one object is repeated in every eligible
/// surface and LOD batch. The PS3 selector, however, computes CullDist and LOD
/// once for the object before emitting its material surfaces. Retaining that
/// object-shaped index prevents the host backend from repeating the selector
/// scan for every surface while preserving the exact batch ownership used by
/// coverage validation and drawing.
/// </summary>
internal sealed class MapRenderOpenGlSunShadowStaticCasterIndex
{
    private readonly Dictionary<int, StaticObjectRuntime> _objects = [];
    private readonly Dictionary<int,
        MapRenderSunShadowStaticCasterExpectation[]> _expectationsByObject;
    private readonly HashSet<(
        int ObjectIndex,
        int LodIndex,
        int MaterialSurfaceIndex)> _executableKeys = [];
    private readonly Dictionary<int, int> _selectedLodByObject = [];

    internal MapRenderOpenGlSunShadowStaticCasterIndex(
        IReadOnlyList<MapRenderOpenGlSunShadowStaticCasterRuntime> runtimes,
        IReadOnlyList<MapRenderSunShadowStaticCasterExpectation> expectations)
        : this(
            runtimes?.Select(runtime => runtime.Batch).ToArray() ??
            throw new ArgumentNullException(nameof(runtimes)),
            expectations)
    {
    }

    internal MapRenderOpenGlSunShadowStaticCasterIndex(
        IReadOnlyList<MapRenderStaticSunShadowCasterBatch> batches,
        IReadOnlyList<MapRenderSunShadowStaticCasterExpectation> expectations)
    {
        ArgumentNullException.ThrowIfNull(batches);
        ArgumentNullException.ThrowIfNull(expectations);

        foreach (MapRenderStaticSunShadowCasterBatch batch in batches)
        {
            foreach (MapRenderSunShadowStaticCasterInstance instance in
                     batch.Instances)
            {
                var candidate = new StaticObjectRuntime(
                    batch.Model,
                    instance);
                if (_objects.TryGetValue(
                        instance.ObjectIndex,
                        out StaticObjectRuntime existing))
                {
                    ValidateObjectRuntime(
                        instance.ObjectIndex,
                        existing,
                        candidate);
                }
                else
                {
                    _objects.Add(instance.ObjectIndex, candidate);
                }

                _executableKeys.Add((
                    instance.ObjectIndex,
                    batch.LodIndex,
                    batch.MaterialSurfaceIndex));
            }
        }

        _expectationsByObject = expectations
            .GroupBy(expectation => expectation.ObjectIndex)
            .ToDictionary(
                group => group.Key,
                group => group.ToArray());
        foreach ((int objectIndex,
                     MapRenderSunShadowStaticCasterExpectation[] rows) in
                 _expectationsByObject)
        {
            ValidateExpectationPlacement(objectIndex, rows);
        }
    }

    internal int CountMissingExecutableExpectations(
        IEnumerable<int> admittedObjectIndices,
        Vector3 nativeCameraOrigin)
    {
        ArgumentNullException.ThrowIfNull(admittedObjectIndices);

        int missing = 0;
        foreach (int objectIndex in admittedObjectIndices)
        {
            if (!_expectationsByObject.TryGetValue(
                    objectIndex,
                    out MapRenderSunShadowStaticCasterExpectation[]? rows))
            {
                continue;
            }

            MapRenderSunShadowStaticCasterExpectation representative =
                rows[0];
            float cameraDistance = Vector3.Distance(
                nativeCameraOrigin,
                representative.GameOrigin);
            if (representative.CullDistance != 0 &&
                MapRenderStaticModelLodSelector.IsBeyondCullDistance(
                    representative.CullDistance,
                    cameraDistance,
                    viewDistanceScale: 1f))
            {
                continue;
            }

            if (_objects.TryGetValue(
                    objectIndex,
                    out StaticObjectRuntime objectRuntime))
            {
                if (!MapRenderStaticModelLodSelector
                        .TrySelectForCameraDistance(
                            objectRuntime.Model,
                            cameraDistance,
                            representative.PlacementScale,
                            nearViewScale: 1f,
                            farViewScale: 1f,
                            out int selectedLod))
                {
                    continue;
                }

                foreach (MapRenderSunShadowStaticCasterExpectation row in
                         rows)
                {
                    if (row.LodIndex == selectedLod &&
                        !_executableKeys.Contains((
                            objectIndex,
                            row.LodIndex,
                            row.MaterialSurfaceIndex)))
                    {
                        missing++;
                    }
                }
                continue;
            }

            // Preserve the fail-closed behavior for an object with eligible
            // native expectations but no executable backend model. Without a
            // model there is no basis for rejecting any expected LOD row.
            foreach (MapRenderSunShadowStaticCasterExpectation row in rows)
            {
                if (!_executableKeys.Contains((
                        objectIndex,
                        row.LodIndex,
                        row.MaterialSurfaceIndex)))
                {
                    missing++;
                }
            }
        }

        return missing;
    }

    internal void PreparePartitionSelection(
        IReadOnlyList<MapRenderSunShadowStaticCasterIdentity>
            admittedObjects,
        Vector3 nativeCameraOrigin)
    {
        ArgumentNullException.ThrowIfNull(admittedObjects);
        _selectedLodByObject.Clear();

        foreach (MapRenderSunShadowStaticCasterIdentity identity in
                 admittedObjects)
        {
            int objectIndex = identity.ObjectIndex;
            if (!_objects.TryGetValue(
                    objectIndex,
                    out StaticObjectRuntime objectRuntime))
            {
                continue;
            }

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

    internal bool IsSelected(int objectIndex, int lodIndex) =>
        _selectedLodByObject.TryGetValue(
            objectIndex,
            out int selectedLod) &&
        selectedLod == lodIndex;

    private static void ValidateObjectRuntime(
        int objectIndex,
        StaticObjectRuntime existing,
        StaticObjectRuntime candidate)
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
                $"Static shadow caster object {objectIndex} has divergent model, placement, or transform ownership across material-surface batches.");
        }
    }

    private static void ValidateExpectationPlacement(
        int objectIndex,
        IReadOnlyList<MapRenderSunShadowStaticCasterExpectation> rows)
    {
        MapRenderSunShadowStaticCasterExpectation representative = rows[0];
        if (rows.Skip(1).Any(row =>
                row.GameOrigin != representative.GameOrigin ||
                row.PlacementScale != representative.PlacementScale ||
                row.CullDistance != representative.CullDistance))
        {
            throw new InvalidOperationException(
                $"Static shadow caster object {objectIndex} has divergent placement inputs across native expectation rows.");
        }
    }

    private readonly record struct StaticObjectRuntime(
        XModelAsset Model,
        MapRenderSunShadowStaticCasterInstance Instance);
}
