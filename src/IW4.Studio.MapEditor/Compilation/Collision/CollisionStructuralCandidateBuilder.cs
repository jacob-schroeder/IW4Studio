using System.Collections.ObjectModel;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.Physics;
using IW4.FastFiles.Zone;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.Objects;
using IW4.Studio.MapEditor.Editing.SavePlanning;
using AssetBounds = IW4.Assets.Math.Bounds;
using AssetVector3 = IW4.Assets.Math.Vec3;

namespace IW4.Studio.MapEditor.Compilation.Collision;

/// <summary>
/// Authority carried by an M3 collision result. A structural candidate is
/// intentionally detached from <c>IClipMapBuildData</c>, Save As, the asset
/// pool, and every FastFile emitter registration.
/// </summary>
public enum CollisionStructuralCandidateAuthority
{
    OfflineValidationOnly = 0
}

/// <summary>
/// Complete detached ColMap-shaped result for M3 structural validation.
/// Symbolic static-model dependencies remain adjacent to their rows, while
/// actual XModel linking remains M7 work.
/// </summary>
public sealed class CollisionStructuralCandidate
{
    private readonly IReadOnlyList<CollisionCompilationProjectionIssue>
        _closedProjectionIssues;

    internal CollisionStructuralCandidate(
        MapDocumentId documentId,
        long documentRevision,
        string mapAssetName,
        ClipMapAsset definition,
        CollisionCompilationInputProjection sourceProjection,
        CollisionSourceIndexPlan sourceIndexPlan,
        CollisionStructuralIndexPlan structuralIndexPlan,
        CollisionFixedPayloadCapacityPlan fixedCapacityPlan,
        CollisionInlineModelAllocationPlan inlineModelPlan,
        CollisionMapEntBrushModelAllocationAssessment
            mapEntBrushModelAllocationAssessment,
        CollisionCompiledConvexBrushLocalPayload[] brushPayloads,
        CollisionCompiledTriangleMeshAggregatePayload? trianglePayload,
        CollisionCompiledStaticModelAggregatePayload staticModelPayload,
        CollisionPlanePointerOwnershipPlan planePointerPlan,
        CollisionBrushReferencePlan brushReferencePlan,
        CollisionStructuralReachabilityAssessment reachabilityAssessment,
        IEnumerable<CollisionCompilationProjectionIssue>
            closedProjectionIssues)
    {
        DocumentId = documentId;
        DocumentRevision = documentRevision;
        MapAssetName = mapAssetName;
        Definition = definition ??
            throw new ArgumentNullException(nameof(definition));
        SourceProjection = sourceProjection ??
            throw new ArgumentNullException(nameof(sourceProjection));
        SourceIndexPlan = sourceIndexPlan ??
            throw new ArgumentNullException(nameof(sourceIndexPlan));
        StructuralIndexPlan = structuralIndexPlan ??
            throw new ArgumentNullException(nameof(structuralIndexPlan));
        FixedCapacityPlan = fixedCapacityPlan ??
            throw new ArgumentNullException(nameof(fixedCapacityPlan));
        InlineModelPlan = inlineModelPlan ??
            throw new ArgumentNullException(nameof(inlineModelPlan));
        MapEntBrushModelAllocationAssessment =
            mapEntBrushModelAllocationAssessment ??
            throw new ArgumentNullException(
                nameof(mapEntBrushModelAllocationAssessment));
        if (!mapEntBrushModelAllocationAssessment.IsValid)
        {
            throw new ArgumentException(
                "A structural candidate cannot retain an invalid MapEnt " +
                "brush-model allocation assessment.",
                nameof(mapEntBrushModelAllocationAssessment));
        }
        BrushPayloads = new ReadOnlyCollection<
            CollisionCompiledConvexBrushLocalPayload>(
                (brushPayloads ??
                 throw new ArgumentNullException(nameof(brushPayloads)))
                .ToArray());
        TrianglePayload = trianglePayload;
        StaticModelPayload = staticModelPayload ??
            throw new ArgumentNullException(nameof(staticModelPayload));
        PlanePointerPlan = planePointerPlan ??
            throw new ArgumentNullException(nameof(planePointerPlan));
        BrushReferencePlan = brushReferencePlan ??
            throw new ArgumentNullException(nameof(brushReferencePlan));
        ReachabilityAssessment = reachabilityAssessment ??
            throw new ArgumentNullException(
                nameof(reachabilityAssessment));
        ArgumentNullException.ThrowIfNull(closedProjectionIssues);
        _closedProjectionIssues =
            new ReadOnlyCollection<CollisionCompilationProjectionIssue>(
                closedProjectionIssues.ToArray());
    }

    public MapDocumentId DocumentId { get; }
    public long DocumentRevision { get; }
    public string MapAssetName { get; }
    public CollisionStructuralCandidateAuthority Authority =>
        CollisionStructuralCandidateAuthority.OfflineValidationOnly;
    public bool PersistenceAuthorized => false;
    public ClipMapAsset Definition { get; }
    public CollisionCompilationInputProjection SourceProjection { get; }
    public CollisionSourceIndexPlan SourceIndexPlan { get; }
    public CollisionStructuralIndexPlan StructuralIndexPlan { get; }
    public CollisionFixedPayloadCapacityPlan FixedCapacityPlan { get; }
    public CollisionInlineModelAllocationPlan InlineModelPlan { get; }
    public CollisionMapEntBrushModelAllocationAssessment
        MapEntBrushModelAllocationAssessment { get; }
    public IReadOnlyList<CollisionCompiledConvexBrushLocalPayload>
        BrushPayloads { get; }
    public CollisionCompiledTriangleMeshAggregatePayload? TrianglePayload
    {
        get;
    }
    public CollisionCompiledStaticModelAggregatePayload StaticModelPayload
    {
        get;
    }
    public CollisionPlanePointerOwnershipPlan PlanePointerPlan { get; }
    public CollisionBrushReferencePlan BrushReferencePlan { get; }
    public CollisionStructuralReachabilityAssessment ReachabilityAssessment
    {
        get;
    }
    public CollisionStructuredRecordAssessment StructuredRecordAssessment =>
        ReachabilityAssessment.LocalRecordAssessment;
    public IReadOnlyList<CollisionCompilationProjectionIssue>
        ClosedProjectionIssues => _closedProjectionIssues;
}

/// <summary>
/// Composes the detached M3 primitive compilers into one internally
/// referential ColMap-shaped candidate. The conservative spatial policy is
/// sufficient for deterministic offline structure and trace validation only;
/// M4 consumer acceptance is still mandatory before persistence.
/// </summary>
public static class CollisionStructuralCandidateBuilder
{
    private static readonly IReadOnlySet<
        CollisionCompilationProjectionIssueKind>
        IssuesClosedByThisBuilder =
            new HashSet<CollisionCompilationProjectionIssueKind>
            {
                CollisionCompilationProjectionIssueKind
                    .TriangleIndexRebasingNotProjected,
                CollisionCompilationProjectionIssueKind
                    .TriangleMaterialGroupingNotProjected,
                CollisionCompilationProjectionIssueKind
                    .WorldSpatialTopologyNotRetained,
                CollisionCompilationProjectionIssueKind
                    .StaticModelSpatialTopologyNotRetained,
                CollisionCompilationProjectionIssueKind
                    .BrushModelEntityTopologyNotProjected
            };

    public static CollisionStructuralCandidate Compile(
        MapDocumentId documentId,
        long documentRevision,
        string mapAssetName,
        IEnumerable<AuthoredCollisionSource> authoredSources,
        CancellationToken cancellationToken = default) =>
        Compile(
            documentId,
            documentRevision,
            mapAssetName,
            authoredSources,
            Array.Empty<MapEntBrushModelAllocationSource>(),
            cancellationToken);

    public static CollisionStructuralCandidate Compile(
        MapDocumentId documentId,
        long documentRevision,
        string mapAssetName,
        IEnumerable<AuthoredCollisionSource> authoredSources,
        IEnumerable<MapEntBrushModelAllocationSource>
            mapEntBrushModelAllocations,
        CancellationToken cancellationToken = default)
    {
        if (documentId.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(documentId));
        if (documentRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(documentRevision));
        string normalizedMapAssetName =
            MapCompilerContentIdentityInput
                .NormalizeMultiplayerMapAssetName(mapAssetName);
        ArgumentNullException.ThrowIfNull(authoredSources);
        ArgumentNullException.ThrowIfNull(mapEntBrushModelAllocations);
        cancellationToken.ThrowIfCancellationRequested();

        AuthoredCollisionSource[] sourceCopy =
            authoredSources.ToArray();
        if (sourceCopy.Length == 0)
        {
            throw new ArgumentException(
                "A collision structural candidate requires canonical " +
                "authored sources.",
                nameof(authoredSources));
        }
        CollisionMapEntBrushModelAllocationAssessment
            mapEntBrushModelAllocationAssessment =
                CollisionMapEntBrushModelAllocationValidator.Assess(
                    sourceCopy,
                    mapEntBrushModelAllocations);
        if (!mapEntBrushModelAllocationAssessment.IsValid)
        {
            throw new CollisionMapEntBrushModelAllocationException(
                mapEntBrushModelAllocationAssessment);
        }

        CollisionCompilationInputProjection projection =
            CollisionCompilationInputProjector.ProjectCanonicalAuthored(
                documentId,
                documentRevision,
                MapAssetKind.ColMapMp,
                sourceCopy,
                cancellationToken);
        CollisionCompilationProjectionIssue[] unresolved =
            projection.Issues
                .Where(value =>
                    !IssuesClosedByThisBuilder.Contains(value.Kind))
                .ToArray();
        if (unresolved.Length != 0)
        {
            throw new NotSupportedException(
                "The canonical source set exceeds the bounded M3 structural " +
                "candidate: " +
                string.Join(
                    "; ",
                    unresolved.Select(value =>
                        $"{value.Kind}: {value.Detail}")));
        }

        CollisionSourceIndexPlan sourceIndexPlan =
            CollisionSourceIndexPlan.Create(
                projection.Sources,
                projection.Contributions);
        IReadOnlyDictionary<MapObjectId, AuthoredCollisionSource>
            sourceById = sourceCopy.ToDictionary(value => value.ObjectId);
        CollisionCompiledConvexBrushLocalPayload[] brushPayloads =
            CompileBrushes(
                sourceIndexPlan,
                sourceById,
                projection.AuthoredMaterialOrdinals,
                cancellationToken);
        CollisionCompiledTriangleMeshAggregatePayload? trianglePayload =
            CompileTriangles(
                sourceIndexPlan,
                sourceById,
                projection.AuthoredMaterialOrdinals,
                cancellationToken);
        CollisionCompiledStaticModelAggregatePayload staticModelPayload =
            CollisionStaticModelPayloadCompiler.Compile(
                OrderedSources<
                    AuthoredPairedStaticModelCollisionSource>(
                    sourceIndexPlan,
                    sourceById));

        ValidateSourceRanges(
            sourceIndexPlan,
            projection.AuthoredMaterialOrdinals,
            brushPayloads,
            trianglePayload,
            staticModelPayload);

        CollisionWorldBrushSpatialInput[] worldBrushes =
            brushPayloads
                .Select((payload, ordinal) => (payload, ordinal))
                .Where(value =>
                    sourceById[value.payload.SourceObjectId]
                        .Ownership.Category ==
                    CollisionOwnershipCategory.StandaloneWorld)
                .Select(value =>
                    new CollisionWorldBrushSpatialInput(
                        value.payload.SourceObjectId,
                        checked((ushort)value.ordinal),
                        value.payload.Bounds,
                        value.payload.Contents))
                .ToArray();
        CollisionMapEntBrushModelSpatialInput[] submodelBrushes =
            brushPayloads
                .Select((payload, ordinal) => (payload, ordinal))
                .Where(value =>
                    sourceById[value.payload.SourceObjectId]
                        .Ownership.Category ==
                    CollisionOwnershipCategory.BrushModelEntity)
                .Select(value =>
                {
                    AuthoredCollisionSource source =
                        sourceById[value.payload.SourceObjectId];
                    return new CollisionMapEntBrushModelSpatialInput(
                        source.Ownership.Counterpart!.Value.ObjectId,
                        value.payload.SourceObjectId,
                        checked((ushort)value.ordinal),
                        value.payload.Bounds,
                        value.payload.Contents);
                })
                .ToArray();
        CollisionWorldPartitionSpatialInput[] worldPartitions =
            CompilePartitionSpatialInputs(
                trianglePayload,
                projection.AuthoredMaterialOrdinals);
        MapBounds[] emptyWorldFallbackBounds =
        [
            .. staticModelPayload.Sources.Select(value => value.Bounds),
            .. submodelBrushes.Select(value => value.Bounds)
        ];
        CollisionCompiledConservativeWorldSpatialPayload worldSpatialPayload =
            CollisionConservativeWorldSpatialCompiler.Compile(
                worldBrushes,
                worldPartitions,
                projection.AuthoredMaterialOrdinals.Entries.Count,
                emptyWorldFallbackBounds.Length == 0
                    ? null
                    : CollisionOutwardBounds.Include(
                        emptyWorldFallbackBounds));

        CollisionInlineModelAllocationPlan inlineModelPlan =
            CollisionInlineModelAllocationPlan.Create(
                mapEntBrushModelAllocationAssessment.Sources.Select(value =>
                    new MapEntInlineModelAllocationSource(
                        value.MapEntityObjectId,
                        value.PhysicalEntityOrdinal)),
                Array.Empty<DynamicBrushInlineModelAllocationSource>());
        CollisionCompiledConservativeWorldSpatialPayload spatialPayload =
            CollisionMapEntBrushModelSpatialCompiler.Extend(
                worldSpatialPayload,
                inlineModelPlan,
                submodelBrushes,
                brushPayloads.Length);
        if (inlineModelPlan.ModelCount !=
            spatialPayload.CollisionModels.Count)
        {
            throw new InvalidDataException(
                "The conservative cmodel output does not match the shared " +
                "inline-model allocation.");
        }

        int borderCount = trianglePayload?.Borders.Count ?? 0;
        int partitionCount = trianglePayload?.Partitions.Count ?? 0;
        CollisionStructuralIndexPlan structuralIndexPlan =
            CollisionStructuralIndexPlan.Create(
                sourceIndexPlan,
                spatialPayload.CreateAggregateCardinalities(
                    borderCount,
                    partitionCount,
                    staticModelPayload.AabbNodes.Count),
                [
                    new CollisionCompilerCatalogCardinality(
                        CollisionIndexDomain.Plane,
                        spatialPayload.BspPlanes.Count)
                ]);
        CollisionFixedPayloadCapacityPlan capacityPlan =
            CollisionFixedPayloadCapacityPlan.Create(structuralIndexPlan);

        ClipMapAsset definition = AssembleDefinition(
            normalizedMapAssetName,
            projection.AuthoredMaterialOrdinals,
            brushPayloads,
            trianglePayload,
            staticModelPayload,
            spatialPayload);
        ValidateRootCardinalities(definition, structuralIndexPlan);

        CollisionPlanePointerOwnershipPlan planePointerPlan =
            CollisionPlanePointerOwnershipPlan.Create(definition);
        planePointerPlan.RequireAuthoredNonNullBindings();
        CollisionBrushReferencePlan brushReferencePlan =
            CollisionBrushReferencePlan.Create(definition);
        CollisionStructuralReachabilityAssessment reachabilityAssessment =
            CollisionStructuralReachabilityValidator.Assess(definition);
        if (!reachabilityAssessment.IsValid)
        {
            throw new InvalidDataException(
                "The M3 structural candidate violates local records or " +
                "aggregate reachability: " +
                string.Join(
                    "; ",
                    reachabilityAssessment.LocalRecordAssessment.Issues
                        .Select(value =>
                            $"{value.Path}: {value.Detail}")
                        .Concat(
                            reachabilityAssessment.Issues.Select(value =>
                                $"{value.Path}: {value.Detail}"))));
        }

        return new CollisionStructuralCandidate(
            documentId,
            documentRevision,
            normalizedMapAssetName,
            definition,
            projection,
            sourceIndexPlan,
            structuralIndexPlan,
            capacityPlan,
            inlineModelPlan,
            mapEntBrushModelAllocationAssessment,
            brushPayloads,
            trianglePayload,
            staticModelPayload,
            planePointerPlan,
            brushReferencePlan,
            reachabilityAssessment,
            projection.Issues);
    }

    private static CollisionCompiledConvexBrushLocalPayload[]
        CompileBrushes(
            CollisionSourceIndexPlan sourceIndexPlan,
            IReadOnlyDictionary<MapObjectId, AuthoredCollisionSource>
                sourceById,
            CollisionAuthoredMaterialOrdinalPlan materialOrdinals,
            CancellationToken cancellationToken)
    {
        var payloads =
            new List<CollisionCompiledConvexBrushLocalPayload>();
        foreach (AuthoredConvexBrushCollisionSource source in
                 OrderedSources<AuthoredConvexBrushCollisionSource>(
                     sourceIndexPlan,
                     sourceById))
        {
            cancellationToken.ThrowIfCancellationRequested();
            payloads.Add(
                CollisionConvexBrushLocalPayloadCompiler
                    .CompileLocal(source, materialOrdinals));
        }
        return payloads.ToArray();
    }

    private static CollisionCompiledTriangleMeshAggregatePayload?
        CompileTriangles(
            CollisionSourceIndexPlan sourceIndexPlan,
            IReadOnlyDictionary<MapObjectId, AuthoredCollisionSource>
                sourceById,
            CollisionAuthoredMaterialOrdinalPlan materialOrdinals,
            CancellationToken cancellationToken)
    {
        AuthoredIndexedTriangleMeshCollisionSource[] sources =
            OrderedSources<
                    AuthoredIndexedTriangleMeshCollisionSource>(
                    sourceIndexPlan,
                    sourceById)
                .ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        return sources.Length == 0
            ? null
            : CollisionTriangleMeshAggregateCompiler.CompileStandalone(
                sources,
                materialOrdinals);
    }

    private static IEnumerable<TSource> OrderedSources<TSource>(
        CollisionSourceIndexPlan sourceIndexPlan,
        IReadOnlyDictionary<MapObjectId, AuthoredCollisionSource>
            sourceById)
        where TSource : AuthoredCollisionSource
    {
        foreach (CollisionCompilationSource identity in
                 sourceIndexPlan.OrderedSources)
        {
            AuthoredCollisionSource source = sourceById[identity.ObjectId];
            if (source is TSource typed)
                yield return typed;
        }
    }

    private static void ValidateSourceRanges(
        CollisionSourceIndexPlan sourceIndexPlan,
        CollisionAuthoredMaterialOrdinalPlan materialOrdinals,
        IReadOnlyList<CollisionCompiledConvexBrushLocalPayload>
            brushPayloads,
        CollisionCompiledTriangleMeshAggregatePayload? trianglePayload,
        CollisionCompiledStaticModelAggregatePayload staticModelPayload)
    {
        for (int index = 0; index < materialOrdinals.Entries.Count; index++)
        {
            CollisionAuthoredMaterialOrdinal entry =
                materialOrdinals.Entries[index];
            if (entry.Ordinal != index)
            {
                throw new InvalidDataException(
                    "The authored material catalog lost dense ordinal " +
                    "ordering.");
            }
        }
        foreach (IGrouping<MapObjectId, CollisionAuthoredMaterialOrdinal>
                 sourceMaterials in materialOrdinals.Entries
                     .GroupBy(value => value.SourceObjectId))
        {
            CollisionAuthoredMaterialOrdinal[] entries =
                sourceMaterials.OrderBy(value => value.Ordinal).ToArray();
            RequireRange(
                sourceIndexPlan,
                sourceMaterials.Key,
                CollisionIndexDomain.Material,
                entries[0].Ordinal,
                entries.Length);
            if (entries.Where((value, index) =>
                    value.Ordinal != entries[0].Ordinal + index).Any())
            {
                throw new InvalidDataException(
                    $"Source {sourceMaterials.Key} material ordinals are " +
                    "not contiguous.");
            }
        }

        int planeStart = 0;
        int sideStart = 0;
        int edgeStart = 0;
        for (int brushOrdinal = 0;
             brushOrdinal < brushPayloads.Count;
             brushOrdinal++)
        {
            CollisionCompiledConvexBrushLocalPayload payload =
                brushPayloads[brushOrdinal];
            RequireRange(
                sourceIndexPlan,
                payload.SourceObjectId,
                CollisionIndexDomain.Plane,
                planeStart,
                payload.Planes.Count);
            RequireRange(
                sourceIndexPlan,
                payload.SourceObjectId,
                CollisionIndexDomain.BrushSide,
                sideStart,
                payload.BrushSides.Count);
            RequireRange(
                sourceIndexPlan,
                payload.SourceObjectId,
                CollisionIndexDomain.BrushEdge,
                edgeStart,
                payload.BrushEdges.Count);
            RequireRange(
                sourceIndexPlan,
                payload.SourceObjectId,
                CollisionIndexDomain.Brush,
                brushOrdinal,
                1);
            RequireRange(
                sourceIndexPlan,
                payload.SourceObjectId,
                CollisionIndexDomain.BrushBounds,
                brushOrdinal,
                1);
            RequireRange(
                sourceIndexPlan,
                payload.SourceObjectId,
                CollisionIndexDomain.BrushContents,
                brushOrdinal,
                1);
            planeStart = checked(planeStart + payload.Planes.Count);
            sideStart = checked(sideStart + payload.BrushSides.Count);
            edgeStart = checked(edgeStart + payload.BrushEdges.Count);
        }

        if (trianglePayload is not null)
        {
            foreach (CollisionCompiledTriangleSourceRanges ranges in
                     trianglePayload.SourceRanges)
            {
                RequireRange(
                    sourceIndexPlan,
                    ranges.SourceObjectId,
                    CollisionIndexDomain.TriangleVertex,
                    ranges.VertexRange.Start,
                    ranges.VertexRange.Count);
                RequireRange(
                    sourceIndexPlan,
                    ranges.SourceObjectId,
                    CollisionIndexDomain.TriangleIndex,
                    ranges.TriangleIndexRange.Start,
                    ranges.TriangleIndexRange.Count);
            }
        }

        for (int index = 0;
             index < staticModelPayload.Sources.Count;
             index++)
        {
            RequireRange(
                sourceIndexPlan,
                staticModelPayload.Sources[index].SourceObjectId,
                CollisionIndexDomain.StaticModel,
                index,
                1);
        }
    }

    private static void RequireRange(
        CollisionSourceIndexPlan sourceIndexPlan,
        MapObjectId sourceObjectId,
        CollisionIndexDomain domain,
        int expectedStart,
        int expectedCount)
    {
        if (expectedCount == 0)
        {
            if (sourceIndexPlan.TryGetRange(
                    sourceObjectId,
                    domain,
                    out _))
            {
                throw new InvalidDataException(
                    $"Source {sourceObjectId} unexpectedly owns a non-empty " +
                    $"{domain} range.");
            }
            return;
        }

        CollisionEmittedIndexRange range =
            sourceIndexPlan.GetRequiredRange(sourceObjectId, domain);
        if (range.Start != expectedStart ||
            range.Count != expectedCount)
        {
            throw new InvalidDataException(
                $"Source {sourceObjectId} {domain} range " +
                $"[{range.Start}, {range.EndExclusive}) does not match the " +
                $"compiled payload [{expectedStart}, " +
                $"{checked(expectedStart + expectedCount)}).");
        }
    }

    private static CollisionWorldPartitionSpatialInput[]
        CompilePartitionSpatialInputs(
            CollisionCompiledTriangleMeshAggregatePayload? trianglePayload,
            CollisionAuthoredMaterialOrdinalPlan materialOrdinals)
    {
        if (trianglePayload is null)
            return [];

        var result =
            new CollisionWorldPartitionSpatialInput[
                trianglePayload.Partitions.Count];
        for (int partitionOrdinal = 0;
             partitionOrdinal < trianglePayload.Partitions.Count;
             partitionOrdinal++)
        {
            CollisionPartition partition =
                trianglePayload.Partitions[partitionOrdinal];
            CollisionCompiledTrianglePartitionBinding binding =
                trianglePayload.PartitionBindings[partitionOrdinal];
            if (binding.PartitionOrdinal != partitionOrdinal)
            {
                throw new InvalidDataException(
                    "Triangle partition bindings lost dense ordinal order.");
            }

            var vertices = new List<MapVector3>(
                checked(partition.TriCount * 3));
            int triangleEnd = checked(
                partition.FirstTri + partition.TriCount);
            for (int triangleOrdinal = partition.FirstTri;
                 triangleOrdinal < triangleEnd;
                 triangleOrdinal++)
            {
                int firstIndex = checked(triangleOrdinal * 3);
                for (int corner = 0; corner < 3; corner++)
                {
                    int vertexOrdinal =
                        CollisionTrianglePartitionIndexContract
                            .ResolveGlobalVertexOrdinal(
                                partition.FirstVertSegment,
                                trianglePayload.TriangleIndices[
                                    firstIndex + corner],
                                trianglePayload.Vertices.Count);
                    AssetVector3 vertex =
                        trianglePayload.Vertices[vertexOrdinal];
                    vertices.Add(new MapVector3(
                        vertex.X,
                        vertex.Y,
                        vertex.Z));
                }
            }

            CollisionAuthoredMaterialOrdinal material =
                materialOrdinals.Entries[binding.MaterialOrdinal];
            result[partitionOrdinal] =
                new CollisionWorldPartitionSpatialInput(
                    partitionOrdinal,
                    binding.MaterialOrdinal,
                    material.Material.Contents,
                    CollisionGeometryValidation.Bounds(vertices));
        }
        return result;
    }

    private static ClipMapAsset AssembleDefinition(
        string mapAssetName,
        CollisionAuthoredMaterialOrdinalPlan materialOrdinals,
        IReadOnlyList<CollisionCompiledConvexBrushLocalPayload>
            brushPayloads,
        CollisionCompiledTriangleMeshAggregatePayload? trianglePayload,
        CollisionCompiledStaticModelAggregatePayload staticModelPayload,
        CollisionCompiledConservativeWorldSpatialPayload spatialPayload)
    {
        CPlane[] planes =
        [
            .. brushPayloads.SelectMany(value => value.Planes),
            .. spatialPayload.BspPlanes
        ];
        ClipMaterial[] materials = materialOrdinals.Entries
            .OrderBy(value => value.Ordinal)
            .Select(value => new ClipMaterial
            {
                Name = value.Material.ExactName,
                SurfaceFlags = value.Material.SurfaceFlags,
                Contents = value.Material.Contents
            })
            .ToArray();
        CBrushSide[] brushSides =
            brushPayloads.SelectMany(value => value.BrushSides).ToArray();
        byte[] brushEdges =
            brushPayloads.SelectMany(value => value.BrushEdges).ToArray();
        CBrush[] brushes =
            brushPayloads.Select(value => value.Brush).ToArray();
        AssetBounds[] brushBounds = brushPayloads
            .Select(value =>
                CollisionOutwardBounds.ToAsset(value.Bounds))
            .ToArray();
        uint[] brushContents =
            brushPayloads.Select(value => value.Contents).ToArray();

        IReadOnlyList<AssetVector3> vertices =
            trianglePayload?.Vertices ?? [];
        IReadOnlyList<ushort> triangleIndices =
            trianglePayload?.TriangleIndices ?? [];
        IReadOnlyList<byte> triangleWalkability =
            trianglePayload?.TriangleEdgeIsWalkable ?? [];
        IReadOnlyList<CollisionBorder> borders =
            trianglePayload?.Borders ?? [];
        IReadOnlyList<CollisionPartition> partitions =
            trianglePayload?.Partitions ?? [];

        return new ClipMapAsset
        {
            SerializedType = XAssetType.ColMapMp,
            Name = mapAssetName,
            IsInUse = 0,
            SerializedIsInUse = 0,
            PlaneCount = planes.Length,
            Planes = Array.AsReadOnly(planes),
            NumStaticModels = staticModelPayload.StaticModels.Count,
            StaticModelList = staticModelPayload.StaticModels,
            NumMaterials = materials.Length,
            Materials = Array.AsReadOnly(materials),
            NumBrushSides = brushSides.Length,
            BrushSides = Array.AsReadOnly(brushSides),
            NumBrushEdges = brushEdges.Length,
            BrushEdges = Array.AsReadOnly(brushEdges),
            NumNodes = spatialPayload.Nodes.Count,
            Nodes = spatialPayload.Nodes,
            NumLeafs = spatialPayload.Leaves.Count,
            Leafs = spatialPayload.Leaves,
            LeafBrushNodesCount = spatialPayload.LeafBrushNodes.Count,
            LeafBrushNodes = spatialPayload.LeafBrushNodes,
            NumLeafBrushes = spatialPayload.LeafBrushReferences.Count,
            LeafBrushes = spatialPayload.LeafBrushReferences,
            NumLeafSurfaces = 0,
            LeafSurfaces = Array.Empty<uint>(),
            VertCount = vertices.Count,
            Verts = vertices,
            TriCount = triangleIndices.Count / 3,
            TriIndices = triangleIndices,
            TriEdgeIsWalkable = triangleWalkability,
            BorderCount = borders.Count,
            Borders = borders,
            PartitionCount = partitions.Count,
            Partitions = partitions,
            AabbTreeCount = spatialPayload.CollisionAabbNodes.Count,
            AabbTrees = spatialPayload.CollisionAabbNodes,
            NumSubModels = spatialPayload.CollisionModels.Count,
            CModels = spatialPayload.CollisionModels,
            NumBrushes = checked((ushort)brushes.Length),
            Pad8ETo8F = 0,
            Brushes = Array.AsReadOnly(brushes),
            BrushBounds = Array.AsReadOnly(brushBounds),
            BrushContents = Array.AsReadOnly(brushContents),
            MapEnts = null,
            MapEntsIncomingDefinition = null,
            SModelNodeCount =
                checked((ushort)staticModelPayload.AabbNodes.Count),
            PadA2ToA3 = 0,
            SModelNodes = staticModelPayload.AabbNodes,
            DynEntCount = Array.AsReadOnly(new ushort[] { 0, 0 }),
            DynEntDefList =
                EmptyDynamicLists<DynEntityDef>(),
            DynEntPoseList =
                EmptyDynamicLists<DynEntityPose>(),
            DynEntClientList =
                EmptyDynamicLists<DynEntityClient>(),
            DynEntCollList =
                EmptyDynamicLists<DynEntityColl>(),
            // The synchronized map checksum is a whole-map M7 output. M3
            // must not pretend a collision-only candidate has that authority.
            Checksum = 0,
            PadD0ToFF =
                Array.AsReadOnly(new byte[0x30])
        };
    }

    private static IReadOnlyList<IReadOnlyList<T>>
        EmptyDynamicLists<T>() =>
        Array.AsReadOnly<IReadOnlyList<T>>(
            new IReadOnlyList<T>[]
            {
                Array.Empty<T>(),
                Array.Empty<T>()
            });

    private static AssetVector3 ToAsset(MapVector3 value) =>
        new() { X = value.X, Y = value.Y, Z = value.Z };

    private static void ValidateRootCardinalities(
        ClipMapAsset definition,
        CollisionStructuralIndexPlan plan)
    {
        RequireCount(
            CollisionIndexDomain.Plane,
            definition.PlaneCount,
            definition.Planes.Count);
        RequireCount(
            CollisionIndexDomain.StaticModel,
            definition.NumStaticModels,
            definition.StaticModelList.Count);
        RequireCount(
            CollisionIndexDomain.Material,
            definition.NumMaterials,
            definition.Materials.Count);
        RequireCount(
            CollisionIndexDomain.BrushSide,
            definition.NumBrushSides,
            definition.BrushSides.Count);
        RequireCount(
            CollisionIndexDomain.BrushEdge,
            definition.NumBrushEdges,
            definition.BrushEdges.Count);
        RequireCount(
            CollisionIndexDomain.BspNode,
            definition.NumNodes,
            definition.Nodes.Count);
        RequireCount(
            CollisionIndexDomain.Leaf,
            definition.NumLeafs,
            definition.Leafs.Count);
        RequireCount(
            CollisionIndexDomain.LeafBrushNode,
            definition.LeafBrushNodesCount,
            definition.LeafBrushNodes.Count);
        RequireCount(
            CollisionIndexDomain.LeafBrushReference,
            definition.NumLeafBrushes,
            definition.LeafBrushes.Count);
        RequireCount(
            CollisionIndexDomain.LeafSurfaceReference,
            definition.NumLeafSurfaces,
            definition.LeafSurfaces.Count);
        RequireCount(
            CollisionIndexDomain.TriangleVertex,
            definition.VertCount,
            definition.Verts.Count);
        RequireCount(
            CollisionIndexDomain.TriangleIndex,
            checked(definition.TriCount * 3),
            definition.TriIndices.Count);
        RequireCount(
            CollisionIndexDomain.TriangleWalkabilityPackedByte,
            definition.TriEdgeIsWalkable.Count,
            definition.TriEdgeIsWalkable.Count);
        RequireCount(
            CollisionIndexDomain.Border,
            definition.BorderCount,
            definition.Borders.Count);
        RequireCount(
            CollisionIndexDomain.Partition,
            definition.PartitionCount,
            definition.Partitions.Count);
        RequireCount(
            CollisionIndexDomain.AabbTreeNode,
            definition.AabbTreeCount,
            definition.AabbTrees.Count);
        RequireCount(
            CollisionIndexDomain.CollisionModel,
            definition.NumSubModels,
            definition.CModels.Count);
        RequireCount(
            CollisionIndexDomain.Brush,
            definition.NumBrushes,
            definition.Brushes.Count);
        RequireCount(
            CollisionIndexDomain.BrushBounds,
            definition.NumBrushes,
            definition.BrushBounds.Count);
        RequireCount(
            CollisionIndexDomain.BrushContents,
            definition.NumBrushes,
            definition.BrushContents.Count);
        RequireCount(
            CollisionIndexDomain.StaticModelAabbNode,
            definition.SModelNodeCount,
            definition.SModelNodes.Count);

        void RequireCount(
            CollisionIndexDomain domain,
            int rootCount,
            int payloadCount)
        {
            int plannedCount = plan.GetDomainCount(domain);
            if (rootCount != payloadCount ||
                payloadCount != plannedCount)
            {
                throw new InvalidDataException(
                    $"{domain} cardinality mismatch: root {rootCount}, " +
                    $"payload {payloadCount}, structural plan " +
                    $"{plannedCount}.");
            }
        }
    }
}
