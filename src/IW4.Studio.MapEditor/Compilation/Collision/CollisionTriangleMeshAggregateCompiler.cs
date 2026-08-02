using System.Collections.ObjectModel;
using IW4.Assets.Assets.ColMap;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.Objects;
using AssetVector3 = IW4.Assets.Math.Vec3;

namespace IW4.Studio.MapEditor.Compilation.Collision;

/// <summary>
/// Versioned policy used to synthesize collision borders for Studio-authored
/// triangle meshes. This is a deterministic Studio contract, not a recovered
/// retail <c>codmw2map</c> policy.
/// </summary>
public enum CollisionTriangleBorderPolicyVersion
{
    /// <summary>
    /// Emits only an aggregate mesh edge owned by exactly one triangle.
    /// The border is oriented away from that triangle in XY. Edges with no
    /// XY length, and edges whose opposite vertex is collinear in XY, are
    /// omitted because the consumer representation cannot derive an
    /// unambiguous horizontal outward side from them. This bounded Studio
    /// policy is not retail capsule-border equivalence.
    /// </summary>
    StudioV1ExposedBoundary = 1
}

/// <summary>
/// Contiguous source-owned ranges plus the compiler-aggregate rows associated
/// with one canonical triangle source. Partition and border rows remain
/// compiler-owned; their ranges here are diagnostic bindings, not a transfer
/// of index ownership to the source.
/// </summary>
public sealed record CollisionCompiledTriangleSourceRanges(
    MapObjectId SourceObjectId,
    CollisionEmittedIndexRange VertexRange,
    CollisionEmittedIndexRange TriangleIndexRange,
    CollisionEmittedIndexRange TriangleRange,
    CollisionEmittedIndexRange PartitionRange,
    int FirstBorder,
    int BorderCount);

/// <summary>
/// Maps one reordered aggregate triangle back to its canonical source row.
/// </summary>
public sealed record CollisionCompiledTriangleBinding(
    MapObjectId SourceObjectId,
    int SourceTriangleOrdinal,
    int CompiledTriangleOrdinal,
    int PartitionOrdinal,
    ushort MaterialOrdinal);

/// <summary>
/// Semantic identity retained beside one detached CollisionPartition row.
/// A later AABB compiler uses the material ordinal without reverse-inferring
/// it from triangle geometry.
/// </summary>
public sealed record CollisionCompiledTrianglePartitionBinding(
    int PartitionOrdinal,
    MapObjectId SourceObjectId,
    ushort MaterialOrdinal,
    CollisionEmittedIndexRange TriangleRange,
    int FirstBorder,
    int BorderCount);

/// <summary>
/// Detached M3 triangle payload. It contains root-ready vertex, index,
/// walkability, border, and partition rows, but no BSP, AABB, ClipMapAsset,
/// packed pointer, checksum, or persistence authority.
/// </summary>
public sealed class CollisionCompiledTriangleMeshAggregatePayload
{
    private readonly IReadOnlyList<AssetVector3> _vertices;
    private readonly IReadOnlyList<ushort> _triangleIndices;
    private readonly IReadOnlyList<byte> _triangleEdgeIsWalkable;
    private readonly IReadOnlyList<CollisionBorder> _borders;
    private readonly IReadOnlyList<CollisionPartition> _partitions;
    private readonly IReadOnlyList<CollisionCompiledTriangleSourceRanges>
        _sourceRanges;
    private readonly IReadOnlyList<CollisionCompiledTriangleBinding>
        _triangleBindings;
    private readonly IReadOnlyList<CollisionCompiledTrianglePartitionBinding>
        _partitionBindings;
    private readonly IReadOnlyDictionary<
        MapObjectId,
        CollisionCompiledTriangleSourceRanges> _sourceRangesById;

    internal CollisionCompiledTriangleMeshAggregatePayload(
        CollisionTriangleBorderPolicyVersion borderPolicy,
        AssetVector3[] vertices,
        ushort[] triangleIndices,
        byte[] triangleEdgeIsWalkable,
        CollisionBorder[] borders,
        CollisionPartition[] partitions,
        CollisionCompiledTriangleSourceRanges[] sourceRanges,
        CollisionCompiledTriangleBinding[] triangleBindings,
        CollisionCompiledTrianglePartitionBinding[] partitionBindings)
    {
        BorderPolicy = borderPolicy;
        _vertices = new ReadOnlyCollection<AssetVector3>(vertices);
        _triangleIndices =
            new ReadOnlyCollection<ushort>(triangleIndices);
        _triangleEdgeIsWalkable =
            new ReadOnlyCollection<byte>(triangleEdgeIsWalkable);
        _borders = new ReadOnlyCollection<CollisionBorder>(borders);
        _partitions =
            new ReadOnlyCollection<CollisionPartition>(partitions);
        _sourceRanges =
            new ReadOnlyCollection<CollisionCompiledTriangleSourceRanges>(
                sourceRanges);
        _triangleBindings =
            new ReadOnlyCollection<CollisionCompiledTriangleBinding>(
                triangleBindings);
        _partitionBindings =
            new ReadOnlyCollection<
                CollisionCompiledTrianglePartitionBinding>(
                partitionBindings);
        _sourceRangesById =
            new ReadOnlyDictionary<
                MapObjectId,
                CollisionCompiledTriangleSourceRanges>(
                sourceRanges.ToDictionary(
                    value => value.SourceObjectId));
    }

    public CollisionTriangleBorderPolicyVersion BorderPolicy { get; }
    public IReadOnlyList<AssetVector3> Vertices => _vertices;
    public IReadOnlyList<ushort> TriangleIndices => _triangleIndices;
    public IReadOnlyList<byte> TriangleEdgeIsWalkable =>
        _triangleEdgeIsWalkable;
    public IReadOnlyList<CollisionBorder> Borders => _borders;
    public IReadOnlyList<CollisionPartition> Partitions => _partitions;
    public IReadOnlyList<CollisionCompiledTriangleSourceRanges>
        SourceRanges => _sourceRanges;
    public IReadOnlyList<CollisionCompiledTriangleBinding>
        TriangleBindings => _triangleBindings;
    public IReadOnlyList<CollisionCompiledTrianglePartitionBinding>
        PartitionBindings => _partitionBindings;

    public CollisionCompiledTriangleSourceRanges GetRequiredSourceRanges(
        MapObjectId sourceObjectId)
    {
        if (sourceObjectId.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(sourceObjectId));

        return _sourceRangesById.TryGetValue(
            sourceObjectId,
            out CollisionCompiledTriangleSourceRanges? ranges)
            ? ranges
            : throw new KeyNotFoundException(
                $"Triangle collision source {sourceObjectId} was not " +
                "compiled into this aggregate payload.");
    }
}

/// <summary>
/// Deterministic detached compiler for canonical standalone triangle meshes.
/// Sources are ordered by stable object identity. Their shared vertex tables
/// are appended without global deduplication, retaining explicit source
/// ownership. Triangles are then grouped by source and authored material.
///
/// StudioV1 borders intentionally cover exposed topology only. A zero-border
/// partition remains valid for bounded point/sphere triangle tests.
/// Projected-degenerate and vertical boundary cases intentionally emit no
/// border, so this policy is not capsule-equivalent to a partition produced
/// by the retail map compiler.
/// </summary>
public static class CollisionTriangleMeshAggregateCompiler
{
    private const int MaximumPartitionTriangleCount = byte.MaxValue;
    private const int MaximumPartitionBorderCount = byte.MaxValue;
    private const int TriangleCornerCount = 3;

    public static CollisionCompiledTriangleMeshAggregatePayload
        CompileStandalone(
            IEnumerable<AuthoredIndexedTriangleMeshCollisionSource> sources,
            CollisionAuthoredMaterialOrdinalPlan materialOrdinals,
            CollisionTriangleBorderPolicyVersion borderPolicy =
                CollisionTriangleBorderPolicyVersion
                    .StudioV1ExposedBoundary)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(materialOrdinals);
        if (borderPolicy !=
            CollisionTriangleBorderPolicyVersion
                .StudioV1ExposedBoundary)
        {
            throw new ArgumentOutOfRangeException(nameof(borderPolicy));
        }

        AuthoredIndexedTriangleMeshCollisionSource[] sourceCopy =
            sources.ToArray();
        if (sourceCopy.Length == 0)
        {
            throw new ArgumentException(
                "Triangle aggregate compilation requires at least one " +
                "canonical source.",
                nameof(sources));
        }
        if (sourceCopy.Any(value => value is null))
        {
            throw new ArgumentException(
                "Triangle aggregate sources cannot contain null entries.",
                nameof(sources));
        }
        MapObjectId? duplicateSourceId = sourceCopy
            .GroupBy(value => value.ObjectId)
            .FirstOrDefault(value => value.Count() > 1)
            ?.Key;
        if (duplicateSourceId is not null)
        {
            throw new ArgumentException(
                $"Triangle collision source {duplicateSourceId} is " +
                "duplicated.",
                nameof(sources));
        }
        if (sourceCopy.Any(value =>
                value.Ownership.Category !=
                CollisionOwnershipCategory.StandaloneWorld))
        {
            throw new ArgumentException(
                "The detached triangle compiler accepts only explicit " +
                "standalone-world collision ownership.",
                nameof(sources));
        }

        AuthoredIndexedTriangleMeshCollisionSource[] orderedSources =
            sourceCopy
                .OrderBy(
                    value => value.ObjectId.Value.ToString("N"),
                    StringComparer.Ordinal)
                .ToArray();

        var vertices = new List<AssetVector3>();
        var sourceWork = new List<SourceWork>(orderedSources.Length);
        foreach (AuthoredIndexedTriangleMeshCollisionSource source in
                 orderedSources)
        {
            int firstVertex = vertices.Count;
            foreach (MapVector3 vertex in source.Vertices)
            {
                vertices.Add(new AssetVector3
                {
                    X = CanonicalizeZero(vertex.X),
                    Y = CanonicalizeZero(vertex.Y),
                    Z = CanonicalizeZero(vertex.Z)
                });
            }

            var triangles =
                new TriangleWork[source.Triangles.Count];
            for (int sourceTriangleOrdinal = 0;
                 sourceTriangleOrdinal < source.Triangles.Count;
                 sourceTriangleOrdinal++)
            {
                AuthoredIndexedCollisionTriangle triangle =
                    source.Triangles[sourceTriangleOrdinal];
                ushort materialOrdinal =
                    materialOrdinals.GetRequiredOrdinal(
                        source.ObjectId,
                        triangle.Material);
                triangles[sourceTriangleOrdinal] = new TriangleWork(
                    source.ObjectId,
                    sourceTriangleOrdinal,
                    checked(firstVertex + triangle.Vertex0),
                    checked(firstVertex + triangle.Vertex1),
                    checked(firstVertex + triangle.Vertex2),
                    triangle.Walkability,
                    materialOrdinal);
            }

            sourceWork.Add(new SourceWork(
                source.ObjectId,
                firstVertex,
                source.Vertices.Count,
                triangles));
        }

        IReadOnlyDictionary<AggregateEdge, int> edgeOwnerCounts =
            CountAggregateEdgeOwners(sourceWork);
        var partitionWork = new List<PartitionWork>();
        foreach (SourceWork source in sourceWork)
        {
            foreach (IGrouping<ushort, TriangleWork> materialGroup in
                     source.Triangles
                         .OrderBy(value => value.MaterialOrdinal)
                         .ThenBy(value => value.SourceTriangleOrdinal)
                         .GroupBy(value => value.MaterialOrdinal))
            {
                PartitionMaterialGroup(
                    source.SourceObjectId,
                    materialGroup.Key,
                    materialGroup,
                    vertices,
                    edgeOwnerCounts,
                    partitionWork);
            }
        }

        var triangleIndices =
            new List<ushort>(checked(
                sourceWork.Sum(value => value.Triangles.Count) *
                TriangleCornerCount));
        var finalWalkability =
            new List<AuthoredTriangleEdgeWalkability>();
        var borders = new List<CollisionBorder>();
        var partitionEmissions =
            new List<PartitionEmission>(partitionWork.Count);
        var triangleBindings =
            new List<CollisionCompiledTriangleBinding>();

        for (int partitionOrdinal = 0;
             partitionOrdinal < partitionWork.Count;
             partitionOrdinal++)
        {
            PartitionWork work = partitionWork[partitionOrdinal];
            int firstTriangle = finalWalkability.Count;
            int firstBorder = borders.Count;
            foreach (TriangleWork triangle in work.Triangles)
            {
                triangleIndices.Add(
                    CollisionTrianglePartitionIndexContract
                        .EncodePartitionRelativeOrdinal(
                            work.FirstVertSegment,
                            triangle.Vertex0,
                            vertices.Count));
                triangleIndices.Add(
                    CollisionTrianglePartitionIndexContract
                        .EncodePartitionRelativeOrdinal(
                            work.FirstVertSegment,
                            triangle.Vertex1,
                            vertices.Count));
                triangleIndices.Add(
                    CollisionTrianglePartitionIndexContract
                        .EncodePartitionRelativeOrdinal(
                            work.FirstVertSegment,
                            triangle.Vertex2,
                            vertices.Count));
                finalWalkability.Add(triangle.Walkability);
                int compiledTriangleOrdinal =
                    finalWalkability.Count - 1;
                triangleBindings.Add(
                    new CollisionCompiledTriangleBinding(
                        triangle.SourceObjectId,
                        triangle.SourceTriangleOrdinal,
                        compiledTriangleOrdinal,
                        partitionOrdinal,
                        triangle.MaterialOrdinal));

                AppendStudioV1Borders(
                    triangle,
                    vertices,
                    edgeOwnerCounts,
                    borders);
            }

            int triangleCount =
                finalWalkability.Count - firstTriangle;
            int borderCount = borders.Count - firstBorder;
            if (triangleCount > MaximumPartitionTriangleCount ||
                borderCount > MaximumPartitionBorderCount)
            {
                throw new InvalidDataException(
                    "Triangle partition splitting produced a payload wider " +
                    "than its unsigned-byte consumer fields.");
            }

            partitionEmissions.Add(new PartitionEmission(
                work.SourceObjectId,
                work.MaterialOrdinal,
                work.FirstVertSegment,
                firstTriangle,
                triangleCount,
                firstBorder,
                borderCount));
        }

        CollisionBorder[] borderArray = borders.ToArray();
        var partitions =
            new CollisionPartition[partitionEmissions.Count];
        var partitionBindings =
            new CollisionCompiledTrianglePartitionBinding[
                partitionEmissions.Count];
        for (int partitionOrdinal = 0;
             partitionOrdinal < partitionEmissions.Count;
             partitionOrdinal++)
        {
            PartitionEmission emission =
                partitionEmissions[partitionOrdinal];
            IReadOnlyList<CollisionBorder> borderSlice =
                new ReadOnlyCollection<CollisionBorder>(
                    new ArraySegment<CollisionBorder>(
                        borderArray,
                        emission.FirstBorder,
                        emission.BorderCount));
            partitions[partitionOrdinal] = new CollisionPartition
            {
                TriCount = checked((byte)emission.TriangleCount),
                BorderCount = checked((byte)emission.BorderCount),
                FirstVertSegment = emission.FirstVertSegment,
                Pad03 = 0,
                FirstTri = emission.FirstTriangle,
                Borders = borderSlice
            };
            partitionBindings[partitionOrdinal] =
                new CollisionCompiledTrianglePartitionBinding(
                    partitionOrdinal,
                    emission.SourceObjectId,
                    emission.MaterialOrdinal,
                    new CollisionEmittedIndexRange(
                        emission.FirstTriangle,
                        emission.TriangleCount),
                    emission.FirstBorder,
                    emission.BorderCount);
        }

        CollisionCompiledTriangleSourceRanges[] sourceRanges =
            CompileSourceRanges(
                sourceWork,
                triangleBindings,
                partitionBindings);
        IReadOnlyList<byte> packedWalkability =
            CollisionTriangleWalkabilityPacker.Pack(finalWalkability);

        return new CollisionCompiledTriangleMeshAggregatePayload(
            borderPolicy,
            vertices.ToArray(),
            triangleIndices.ToArray(),
            packedWalkability.ToArray(),
            borderArray,
            partitions,
            sourceRanges,
            triangleBindings.ToArray(),
            partitionBindings);
    }

    private static IReadOnlyDictionary<AggregateEdge, int>
        CountAggregateEdgeOwners(IReadOnlyList<SourceWork> sources)
    {
        var owners = new Dictionary<AggregateEdge, int>();
        foreach (TriangleWork triangle in
                 sources.SelectMany(value => value.Triangles))
        {
            foreach (AggregateEdge edge in triangle.Edges)
            {
                owners[edge] = checked(
                    owners.GetValueOrDefault(edge) + 1);
            }
        }

        return new ReadOnlyDictionary<AggregateEdge, int>(owners);
    }

    private static void PartitionMaterialGroup(
        MapObjectId sourceObjectId,
        ushort materialOrdinal,
        IEnumerable<TriangleWork> triangles,
        IReadOnlyList<AssetVector3> vertices,
        IReadOnlyDictionary<AggregateEdge, int> edgeOwnerCounts,
        ICollection<PartitionWork> destination)
    {
        var current = new List<TriangleWork>();
        int currentMinimumVertex = 0;
        int currentMaximumVertex = 0;
        int currentBorderCount = 0;

        foreach (TriangleWork triangle in triangles)
        {
            int triangleMinimumVertex = triangle.MinimumVertexOrdinal;
            int triangleMaximumVertex = triangle.MaximumVertexOrdinal;
            int triangleBorderCount =
                CountStudioV1Borders(
                    triangle,
                    vertices,
                    edgeOwnerCounts);
            int nextMinimumVertex = current.Count == 0
                ? triangleMinimumVertex
                : Math.Min(
                    currentMinimumVertex,
                    triangleMinimumVertex);
            int nextMaximumVertex = current.Count == 0
                ? triangleMaximumVertex
                : Math.Max(
                    currentMaximumVertex,
                    triangleMaximumVertex);
            int nextBorderCount = checked(
                currentBorderCount + triangleBorderCount);
            bool segmentCompatible = TrySelectVertexSegment(
                nextMinimumVertex,
                nextMaximumVertex,
                out _);
            bool mustSplit =
                current.Count != 0 &&
                (current.Count == MaximumPartitionTriangleCount ||
                 nextBorderCount > MaximumPartitionBorderCount ||
                 !segmentCompatible);

            if (mustSplit)
            {
                AddPartition(
                    sourceObjectId,
                    materialOrdinal,
                    current,
                    currentMinimumVertex,
                    currentMaximumVertex,
                    destination);
                current.Clear();
                currentBorderCount = 0;
                nextMinimumVertex = triangleMinimumVertex;
                nextMaximumVertex = triangleMaximumVertex;
                nextBorderCount = triangleBorderCount;
                segmentCompatible = TrySelectVertexSegment(
                    nextMinimumVertex,
                    nextMaximumVertex,
                    out _);
            }

            if (!segmentCompatible)
            {
                throw new OverflowException(
                    $"Triangle {triangle.SourceTriangleOrdinal} from " +
                    $"collision source {triangle.SourceObjectId} cannot fit " +
                    "inside any IW4 FirstVertSegment + UInt16 vertex window.");
            }

            current.Add(triangle);
            currentMinimumVertex = nextMinimumVertex;
            currentMaximumVertex = nextMaximumVertex;
            currentBorderCount = nextBorderCount;
        }

        if (current.Count != 0)
        {
            AddPartition(
                sourceObjectId,
                materialOrdinal,
                current,
                currentMinimumVertex,
                currentMaximumVertex,
                destination);
        }
    }

    private static void AddPartition(
        MapObjectId sourceObjectId,
        ushort materialOrdinal,
        IReadOnlyList<TriangleWork> triangles,
        int minimumVertexOrdinal,
        int maximumVertexOrdinal,
        ICollection<PartitionWork> destination)
    {
        if (!TrySelectVertexSegment(
                minimumVertexOrdinal,
                maximumVertexOrdinal,
                out byte firstVertSegment))
        {
            throw new InvalidDataException(
                "A validated triangle partition lost its vertex-segment " +
                "window.");
        }

        destination.Add(new PartitionWork(
            sourceObjectId,
            materialOrdinal,
            firstVertSegment,
            triangles.ToArray()));
    }

    private static bool TrySelectVertexSegment(
        int minimumVertexOrdinal,
        int maximumVertexOrdinal,
        out byte firstVertSegment)
    {
        if (minimumVertexOrdinal < 0 ||
            maximumVertexOrdinal < minimumVertexOrdinal)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumVertexOrdinal));
        }

        int firstAllowedSegment =
            maximumVertexOrdinal <= ushort.MaxValue
                ? 0
                : checked(
                    (maximumVertexOrdinal - ushort.MaxValue +
                     CollisionTrianglePartitionIndexContract
                         .VerticesPerSegment -
                     1) /
                    CollisionTrianglePartitionIndexContract
                        .VerticesPerSegment);
        int lastAllowedSegment = Math.Min(
            byte.MaxValue,
            minimumVertexOrdinal /
            CollisionTrianglePartitionIndexContract.VerticesPerSegment);
        if (firstAllowedSegment > lastAllowedSegment)
        {
            firstVertSegment = 0;
            return false;
        }

        // Choosing the highest compatible base minimizes local ushort values
        // while remaining deterministic for the complete partition.
        firstVertSegment = checked((byte)lastAllowedSegment);
        return true;
    }

    private static int CountStudioV1Borders(
        TriangleWork triangle,
        IReadOnlyList<AssetVector3> vertices,
        IReadOnlyDictionary<AggregateEdge, int> edgeOwnerCounts)
    {
        int count = 0;
        for (int edgeOrdinal = 0;
             edgeOrdinal < TriangleCornerCount;
             edgeOrdinal++)
        {
            if (edgeOwnerCounts[triangle.Edges[edgeOrdinal]] != 1)
                continue;

            if (TryResolveStudioV1BorderFrame(
                    vertices[triangle.EdgeStart(edgeOrdinal)],
                    vertices[triangle.EdgeEnd(edgeOrdinal)],
                    vertices[triangle.OppositeVertex(edgeOrdinal)],
                    out _))
                count++;
        }

        return count;
    }

    private static void AppendStudioV1Borders(
        TriangleWork triangle,
        IReadOnlyList<AssetVector3> vertices,
        IReadOnlyDictionary<AggregateEdge, int> edgeOwnerCounts,
        ICollection<CollisionBorder> destination)
    {
        for (int edgeOrdinal = 0;
             edgeOrdinal < TriangleCornerCount;
             edgeOrdinal++)
        {
            if (edgeOwnerCounts[triangle.Edges[edgeOrdinal]] != 1)
                continue;

            AssetVector3 start =
                vertices[triangle.EdgeStart(edgeOrdinal)];
            AssetVector3 end =
                vertices[triangle.EdgeEnd(edgeOrdinal)];
            AssetVector3 interior =
                vertices[triangle.OppositeVertex(edgeOrdinal)];
            if (!TryResolveStudioV1BorderFrame(
                    start,
                    end,
                    interior,
                    out StudioV1BorderFrame frame))
            {
                continue;
            }

            destination.Add(CompileStudioV1Border(frame));
        }
    }

    private static bool TryResolveStudioV1BorderFrame(
        AssetVector3 first,
        AssetVector3 second,
        AssetVector3 interior,
        out StudioV1BorderFrame frame)
    {
        double deltaX = (double)second.X - first.X;
        double deltaY = (double)second.Y - first.Y;
        double horizontalLength = Math.Sqrt(
            deltaX * deltaX + deltaY * deltaY);
        if (!double.IsFinite(horizontalLength) ||
            horizontalLength <=
            CollisionGeometryValidation.DegenerateAreaTolerance)
        {
            frame = default;
            return false;
        }

        double tangentX = deltaX / horizontalLength;
        double tangentY = deltaY / horizontalLength;
        double normalX = -tangentY;
        double normalY = tangentX;
        double interiorSide =
            normalX * ((double)interior.X - first.X) +
            normalY * ((double)interior.Y - first.Y);
        if (!double.IsFinite(interiorSide) ||
            Math.Abs(interiorSide) <=
            CollisionGeometryValidation.DegenerateAreaTolerance)
        {
            // A vertical/projected-degenerate triangle does not identify an
            // outward XY side. Emitting a border using authored edge winding
            // would make one-sided capsule behavior arbitrary.
            frame = default;
            return false;
        }

        if (interiorSide > 0d)
        {
            (first, second) = (second, first);
            tangentX = -tangentX;
            tangentY = -tangentY;
            normalX = -normalX;
            normalY = -normalY;
        }

        // Consumer form: n=(a,b), t=(b,-a), with p0->p1 selected so
        // dot(t,p1-p0) is positive.
        double length =
            tangentX * ((double)second.X - first.X) +
            tangentY * ((double)second.Y - first.Y);
        double distance =
            normalX * first.X + normalY * first.Y;
        double start =
            tangentX * first.X + tangentY * first.Y;
        double zSlope =
            ((double)second.Z - first.Z) / length;
        if (!double.IsFinite(length) || length <= 0d)
        {
            throw new InvalidDataException(
                "StudioV1 border orientation did not produce a positive " +
                "consumer tangent length.");
        }

        frame = new StudioV1BorderFrame(
            first,
            normalX,
            normalY,
            length,
            distance,
            start,
            zSlope);
        return true;
    }

    private static CollisionBorder CompileStudioV1Border(
        StudioV1BorderFrame frame) =>
        new()
        {
            DistEq = Array.AsReadOnly(
                new[]
                {
                    ToFiniteSingle(frame.NormalX, "border normal X"),
                    ToFiniteSingle(frame.NormalY, "border normal Y"),
                    ToFiniteSingle(
                        frame.PlaneDistance,
                        "border plane distance")
                }),
            ZBase = CanonicalizeZero(frame.First.Z),
            ZSlope = ToFiniteSingle(frame.ZSlope, "border Z slope"),
            Start = ToFiniteSingle(
                frame.TangentStart,
                "border tangent start"),
            Length = ToFiniteSingle(frame.Length, "border tangent length")
        };

    private static CollisionCompiledTriangleSourceRanges[]
        CompileSourceRanges(
            IReadOnlyList<SourceWork> sources,
            IReadOnlyList<CollisionCompiledTriangleBinding>
                triangleBindings,
            IReadOnlyList<CollisionCompiledTrianglePartitionBinding>
                partitionBindings)
    {
        var ranges =
            new CollisionCompiledTriangleSourceRanges[sources.Count];
        for (int sourceOrdinal = 0;
             sourceOrdinal < sources.Count;
             sourceOrdinal++)
        {
            SourceWork source = sources[sourceOrdinal];
            CollisionCompiledTriangleBinding[] sourceTriangles =
                triangleBindings
                    .Where(value =>
                        value.SourceObjectId == source.SourceObjectId)
                    .ToArray();
            CollisionCompiledTrianglePartitionBinding[] sourcePartitions =
                partitionBindings
                    .Where(value =>
                        value.SourceObjectId == source.SourceObjectId)
                    .ToArray();
            int firstTriangle =
                sourceTriangles.Min(value =>
                    value.CompiledTriangleOrdinal);
            int triangleCount = sourceTriangles.Length;
            int firstPartition =
                sourcePartitions.Min(value => value.PartitionOrdinal);
            int firstBorder =
                sourcePartitions.Min(value => value.FirstBorder);
            int borderCount =
                sourcePartitions.Sum(value => value.BorderCount);

            if (sourceTriangles
                    .Select(value => value.CompiledTriangleOrdinal)
                    .Order()
                    .Where((value, index) =>
                        value != firstTriangle + index)
                    .Any() ||
                sourcePartitions
                    .Select(value => value.PartitionOrdinal)
                    .Order()
                    .Where((value, index) =>
                        value != firstPartition + index)
                    .Any())
            {
                throw new InvalidDataException(
                    $"Triangle source {source.SourceObjectId} did not retain " +
                    "contiguous aggregate bindings.");
            }

            ranges[sourceOrdinal] =
                new CollisionCompiledTriangleSourceRanges(
                    source.SourceObjectId,
                    new CollisionEmittedIndexRange(
                        source.FirstVertex,
                        source.VertexCount),
                    new CollisionEmittedIndexRange(
                        checked(firstTriangle * TriangleCornerCount),
                        checked(triangleCount * TriangleCornerCount)),
                    new CollisionEmittedIndexRange(
                        firstTriangle,
                        triangleCount),
                    new CollisionEmittedIndexRange(
                        firstPartition,
                        sourcePartitions.Length),
                    firstBorder,
                    borderCount);
        }

        return ranges;
    }

    private static float ToFiniteSingle(
        double value,
        string fieldName)
    {
        float result = (float)value;
        if (!float.IsFinite(result))
        {
            throw new OverflowException(
                $"StudioV1 {fieldName} is outside the finite serialized " +
                "single-precision range.");
        }

        return CanonicalizeZero(result);
    }

    private static float CanonicalizeZero(float value) =>
        value == 0f ? 0f : value;

    private readonly record struct StudioV1BorderFrame(
        AssetVector3 First,
        double NormalX,
        double NormalY,
        double Length,
        double PlaneDistance,
        double TangentStart,
        double ZSlope);

    private readonly record struct AggregateEdge(int First, int Second)
    {
        public static AggregateEdge Create(int first, int second) =>
            first < second
                ? new AggregateEdge(first, second)
                : new AggregateEdge(second, first);
    }

    private sealed record SourceWork(
        MapObjectId SourceObjectId,
        int FirstVertex,
        int VertexCount,
        IReadOnlyList<TriangleWork> Triangles);

    private sealed record TriangleWork(
        MapObjectId SourceObjectId,
        int SourceTriangleOrdinal,
        int Vertex0,
        int Vertex1,
        int Vertex2,
        AuthoredTriangleEdgeWalkability Walkability,
        ushort MaterialOrdinal)
    {
        public int MinimumVertexOrdinal =>
            Math.Min(Vertex0, Math.Min(Vertex1, Vertex2));

        public int MaximumVertexOrdinal =>
            Math.Max(Vertex0, Math.Max(Vertex1, Vertex2));

        public IReadOnlyList<AggregateEdge> Edges =>
        [
            AggregateEdge.Create(Vertex0, Vertex1),
            AggregateEdge.Create(Vertex1, Vertex2),
            AggregateEdge.Create(Vertex2, Vertex0)
        ];

        public int EdgeStart(int edgeOrdinal) =>
            edgeOrdinal switch
            {
                0 => Vertex0,
                1 => Vertex1,
                2 => Vertex2,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(edgeOrdinal))
            };

        public int EdgeEnd(int edgeOrdinal) =>
            edgeOrdinal switch
            {
                0 => Vertex1,
                1 => Vertex2,
                2 => Vertex0,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(edgeOrdinal))
            };

        public int OppositeVertex(int edgeOrdinal) =>
            edgeOrdinal switch
            {
                0 => Vertex2,
                1 => Vertex0,
                2 => Vertex1,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(edgeOrdinal))
            };
    }

    private sealed record PartitionWork(
        MapObjectId SourceObjectId,
        ushort MaterialOrdinal,
        byte FirstVertSegment,
        IReadOnlyList<TriangleWork> Triangles);

    private sealed record PartitionEmission(
        MapObjectId SourceObjectId,
        ushort MaterialOrdinal,
        byte FirstVertSegment,
        int FirstTriangle,
        int TriangleCount,
        int FirstBorder,
        int BorderCount);
}
