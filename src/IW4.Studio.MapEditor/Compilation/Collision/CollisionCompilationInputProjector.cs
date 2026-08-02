using System.Collections.ObjectModel;
using IW4.Studio.MapEditor.Compilation.StaticModels;
using IW4.Studio.MapEditor.Editing.Documents;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.Objects;
using IW4.Studio.MapEditor.Editing.SavePlanning;

namespace IW4.Studio.MapEditor.Compilation.Collision;

/// <summary>
/// Fail-closed reason that a retained collision semantic object, or an
/// aggregate ColMap topology domain, could not be represented by the current
/// M0 source/index contract.
/// </summary>
public enum CollisionCompilationProjectionIssueKind
{
    UnresolvedOwnership = 0,
    StaticModelPairInconsistent = 1,
    ConvexBrushTopologyNotRetained = 2,
    TriangleTopologyNotRetained = 3,
    WorldSpatialTopologyNotRetained = 4,
    StaticModelSpatialTopologyNotRetained = 5,
    BrushEdgeEncodingNotProjected = 6,
    BrushModelEntityTopologyNotProjected = 7,
    TriangleIndexRebasingNotProjected = 8,
    StaticModelPlacementNotRetained = 9,
    TriangleMaterialGroupingNotProjected = 10
}

public sealed record CollisionCompilationProjectionIssue(
    MapObjectId? ObjectId,
    CollisionCompilationProjectionIssueKind Kind,
    string Detail);

/// <summary>
/// Immutable, potentially partial source/contribution projection. Blocking
/// issues are intentionally retained beside proven inputs. This type is not
/// a ColMap build plan and never authorizes emission or persistence.
/// </summary>
public sealed class CollisionCompilationInputProjection
{
    private readonly IReadOnlyList<CollisionCompilationSource> _sources;
    private readonly IReadOnlyList<CollisionIndexContribution> _contributions;
    private readonly IReadOnlyList<CollisionCompilationProjectionIssue> _issues;
    private readonly IReadOnlyList<byte> _triangleWalkabilityBytes;

    internal CollisionCompilationInputProjection(
        MapDocumentId documentId,
        long documentRevision,
        IEnumerable<CollisionCompilationSource> sources,
        IEnumerable<CollisionIndexContribution> contributions,
        IEnumerable<CollisionCompilationProjectionIssue> issues,
        CollisionAuthoredMaterialOrdinalPlan? authoredMaterialOrdinals = null,
        IEnumerable<byte>? triangleWalkabilityBytes = null)
    {
        if (documentId.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(documentId));
        if (documentRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(documentRevision));
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(contributions);
        ArgumentNullException.ThrowIfNull(issues);

        DocumentId = documentId;
        DocumentRevision = documentRevision;
        _sources = new ReadOnlyCollection<CollisionCompilationSource>(
            sources.ToArray());
        _contributions =
            new ReadOnlyCollection<CollisionIndexContribution>(
                contributions.ToArray());
        _issues =
            new ReadOnlyCollection<CollisionCompilationProjectionIssue>(
                issues.ToArray());
        AuthoredMaterialOrdinals =
            authoredMaterialOrdinals ??
            CollisionAuthoredMaterialOrdinalPlan.Empty;
        _triangleWalkabilityBytes =
            new ReadOnlyCollection<byte>(
                triangleWalkabilityBytes?.ToArray() ?? []);
    }

    public MapDocumentId DocumentId { get; }
    public long DocumentRevision { get; }
    public IReadOnlyList<CollisionCompilationSource> Sources => _sources;
    public IReadOnlyList<CollisionIndexContribution> Contributions =>
        _contributions;
    public IReadOnlyList<CollisionCompilationProjectionIssue> Issues =>
        _issues;
    public CollisionAuthoredMaterialOrdinalPlan AuthoredMaterialOrdinals
    {
        get;
    }
    public IReadOnlyList<byte> TriangleWalkabilityBytes =>
        _triangleWalkabilityBytes;

    /// <summary>
    /// True means at least one retained semantic or aggregate topology
    /// requirement remains unprojected. False still does not imply that a
    /// complete ColMap compiler or emitter exists.
    /// </summary>
    public bool HasBlockingIssues => _issues.Count != 0;
}

/// <summary>
/// Projects only collision compiler inputs directly proven by the semantic
/// document. Imported brush/triangle rows are deliberately rejected because
/// their current editor objects are views over shared ClipMap tables: they do
/// not retain explicit ownership or enough source-local topology to rebuild
/// those tables. Exact render/collision static-model pairs do retain enough
/// authority to contribute one StaticModel row. The current contract is
/// explicitly ColMapMp; SP and unknown collision authorities are rejected.
/// </summary>
public static class CollisionCompilationInputProjector
{
    /// <summary>
    /// Projects the source identities and domain cardinalities that are
    /// already represented by the M0 contracts. Geometry records remain
    /// available through the canonical source objects supplied by the caller;
    /// this partial projection never emits them. Every unimplemented
    /// structural/material encoding remains an explicit blocker.
    /// </summary>
    public static CollisionCompilationInputProjection
        ProjectCanonicalAuthored(
            MapDocumentId documentId,
            long documentRevision,
            MapAssetKind collisionAssetKind,
            IEnumerable<AuthoredCollisionSource> authoredSources,
            CancellationToken cancellationToken = default)
    {
        if (documentId.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(documentId));
        if (documentRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(documentRevision));
        if (collisionAssetKind != MapAssetKind.ColMapMp)
        {
            throw new ArgumentOutOfRangeException(
                nameof(collisionAssetKind),
                "Canonical authored collision projection requires explicit " +
                "ColMapMp authority.");
        }
        ArgumentNullException.ThrowIfNull(authoredSources);
        cancellationToken.ThrowIfCancellationRequested();

        AuthoredCollisionSource[] sourceCopy =
            authoredSources.ToArray();
        if (sourceCopy.Any(value => value is null))
        {
            throw new ArgumentException(
                "Canonical authored collision sources cannot contain null " +
                "entries.",
                nameof(authoredSources));
        }
        MapObjectId? duplicateId = sourceCopy
            .GroupBy(value => value.ObjectId)
            .FirstOrDefault(value => value.Count() > 1)
            ?.Key;
        if (duplicateId is not null)
        {
            throw new ArgumentException(
                $"Canonical authored collision source {duplicateId} is " +
                "duplicated.",
                nameof(authoredSources));
        }
        MapObjectId? duplicateRenderCounterpart = sourceCopy
            .Where(value =>
                value.Ownership.Category ==
                CollisionOwnershipCategory.PairedStaticModel)
            .GroupBy(value =>
                value.Ownership.Counterpart!.Value.ObjectId)
            .FirstOrDefault(value => value.Count() > 1)
            ?.Key;
        if (duplicateRenderCounterpart is not null)
        {
            throw new ArgumentException(
                $"Render static-model counterpart " +
                $"{duplicateRenderCounterpart} is claimed by multiple " +
                "canonical collision sources.",
                nameof(authoredSources));
        }

        AuthoredCollisionSource[] ordered = sourceCopy
            .OrderBy(value => value.GeometryKind)
            .ThenBy(value => value.Ownership.Category)
            .ThenBy(value =>
                value.ObjectId.Value.ToString("N"),
                StringComparer.Ordinal)
            .ToArray();
        CollisionAuthoredMaterialOrdinalPlan materialOrdinals =
            CollisionAuthoredMaterialOrdinalPlan.Create(ordered);
        IReadOnlyList<byte> triangleWalkabilityBytes =
            CollisionTriangleWalkabilityPacker.Pack(
                ordered
                    .OfType<
                        AuthoredIndexedTriangleMeshCollisionSource>()
                    .SelectMany(value => value.Triangles)
                    .Select(value => value.Walkability));
        var sources =
            new List<CollisionCompilationSource>(ordered.Length);
        var contributions = new List<CollisionIndexContribution>();
        var issues = new List<CollisionCompilationProjectionIssue>();
        bool hasStandaloneWorldGeometry = false;
        bool hasStaticModels = false;

        foreach (AuthoredCollisionSource authored in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CollisionCompilationSource source =
                authored.CreateCompilationIdentity();
            sources.Add(source);

            switch (authored)
            {
                case AuthoredConvexBrushCollisionSource brush:
                    hasStandaloneWorldGeometry =
                        hasStandaloneWorldGeometry ||
                        brush.Ownership.Category ==
                        CollisionOwnershipCategory.StandaloneWorld;
                    CollisionCompiledConvexBrushLocalPayload? brushPayload =
                        null;
                    try
                    {
                        brushPayload =
                            CollisionConvexBrushLocalPayloadCompiler
                                .CompileLocal(
                                    brush,
                                    materialOrdinals);
                    }
                    catch (Exception exception)
                        when (exception is NotSupportedException or
                            OverflowException)
                    {
                        issues.Add(
                            new CollisionCompilationProjectionIssue(
                                brush.ObjectId,
                                CollisionCompilationProjectionIssueKind
                                    .BrushEdgeEncodingNotProjected,
                                exception.Message));
                    }

                    if (brushPayload is not null)
                    {
                        AddContribution(
                            contributions,
                            source,
                            CollisionIndexDomain.Plane,
                            brushPayload.Planes.Count);
                    }
                    AddContribution(
                        contributions,
                        source,
                        CollisionIndexDomain.Material,
                        materialOrdinals.GetSourceMaterialCount(
                            brush.ObjectId));
                    if (brushPayload is not null)
                    {
                        AddContribution(
                            contributions,
                            source,
                            CollisionIndexDomain.BrushSide,
                            brushPayload.BrushSides.Count);
                        AddContribution(
                            contributions,
                            source,
                            CollisionIndexDomain.BrushEdge,
                            brushPayload.BrushEdges.Count);
                    }
                    AddContribution(
                        contributions,
                        source,
                        CollisionIndexDomain.Brush,
                        1);
                    AddContribution(
                        contributions,
                        source,
                        CollisionIndexDomain.BrushBounds,
                        1);
                    AddContribution(
                        contributions,
                        source,
                        CollisionIndexDomain.BrushContents,
                        1);
                    if (brush.Ownership.Category ==
                        CollisionOwnershipCategory.BrushModelEntity)
                    {
                        issues.Add(
                            new CollisionCompilationProjectionIssue(
                                brush.ObjectId,
                                CollisionCompilationProjectionIssueKind
                                    .BrushModelEntityTopologyNotProjected,
                                "The explicit MapEnt counterpart is retained, " +
                                "but cmodel/leaf ownership for brush-model " +
                                "entities is not yet compiled."));
                    }
                    break;
                case AuthoredIndexedTriangleMeshCollisionSource mesh:
                    hasStandaloneWorldGeometry = true;
                    AddContribution(
                        contributions,
                        source,
                        CollisionIndexDomain.Material,
                        materialOrdinals.GetSourceMaterialCount(
                            mesh.ObjectId));
                    AddContribution(
                        contributions,
                        source,
                        CollisionIndexDomain.TriangleVertex,
                        mesh.Vertices.Count);
                    AddContribution(
                        contributions,
                        source,
                        CollisionIndexDomain.TriangleIndex,
                        checked(mesh.Triangles.Count * 3));
                    issues.Add(new CollisionCompilationProjectionIssue(
                        mesh.ObjectId,
                        CollisionCompilationProjectionIssueKind
                            .TriangleIndexRebasingNotProjected,
                        "Canonical triangle indices target the source-local " +
                        "shared vertex table, but IW4 resolves serialized " +
                        "ushort values relative to a partition's 1,024-vertex " +
                        "segment. Partition assignment is required before " +
                        "those values can be encoded."));
                    issues.Add(new CollisionCompilationProjectionIssue(
                        mesh.ObjectId,
                        CollisionCompilationProjectionIssueKind
                            .TriangleMaterialGroupingNotProjected,
                        "ClipMaterial ordinals are assigned, but canonical " +
                        "per-triangle materials still require a proven " +
                        "partition/collision-AABB grouping whose nodes each " +
                        "carry one material ordinal."));
                    break;
                case AuthoredPairedStaticModelCollisionSource:
                    hasStaticModels = true;
                    AddContribution(
                        contributions,
                        source,
                        CollisionIndexDomain.StaticModel,
                        1);
                    break;
                default:
                    throw new InvalidDataException(
                        $"Unsupported canonical authored collision source " +
                        $"{authored.GetType().Name}.");
            }
        }

        if (hasStaticModels)
        {
            issues.Add(new CollisionCompilationProjectionIssue(
                ObjectId: null,
                CollisionCompilationProjectionIssueKind
                    .StaticModelSpatialTopologyNotRetained,
                "StaticModel row contributions are proven, but the shared " +
                "StaticModelAabbNode tree is not compiled."));
        }
        if (hasStandaloneWorldGeometry)
        {
            issues.Add(new CollisionCompilationProjectionIssue(
                ObjectId: null,
                CollisionCompilationProjectionIssueKind
                    .WorldSpatialTopologyNotRetained,
                "Canonical primitive geometry is retained, but BSP nodes, " +
                "leaves, leaf references, collision models, partitions, " +
                "borders, and world AABB nodes remain uncompiled."));
        }

        return new CollisionCompilationInputProjection(
            documentId,
            documentRevision,
            sources,
            contributions,
            issues,
            materialOrdinals,
            triangleWalkabilityBytes);
    }

    public static CollisionCompilationInputProjection Project(
        EditorMapDocument document,
        StaticModelCorrespondenceCatalog staticModelCorrespondences,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(staticModelCorrespondences);
        cancellationToken.ThrowIfCancellationRequested();

        if (staticModelCorrespondences.DocumentId != document.Id ||
            !string.Equals(
                staticModelCorrespondences.MapIdentity,
                document.MapIdentity,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Static-model correspondence authority must belong to the " +
                "same semantic map document.",
                nameof(staticModelCorrespondences));
        }
        if (staticModelCorrespondences.CollisionAssetKind !=
            MapAssetKind.ColMapMp)
        {
            throw new ArgumentException(
                "The M0 collision input projector accepts only explicit " +
                "ColMapMp authority.",
                nameof(staticModelCorrespondences));
        }

        return document.ReadConsistent(documentRevision =>
            ProjectConsistent(
                document,
                documentRevision,
                staticModelCorrespondences,
                cancellationToken));
    }

    private static CollisionCompilationInputProjection ProjectConsistent(
        EditorMapDocument document,
        long documentRevision,
        StaticModelCorrespondenceCatalog staticModelCorrespondences,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sources = new List<CollisionCompilationSource>();
        var contributions = new List<CollisionIndexContribution>();
        var issues = new List<CollisionCompilationProjectionIssue>();

        bool projectedStaticModel = false;
        foreach (EditorStaticModel collision in document.StaticModels
                     .Where(value =>
                         value.Representation ==
                         StaticModelRepresentation.Collision)
                     .OrderBy(value => value.IsImported ? 0 : 1)
                     .ThenBy(value => value.SourceOrdinal.Value)
                     .ThenBy(value =>
                         value.Id.Value.ToString("N"),
                         StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryProjectStaticModel(
                    document,
                    collision,
                    staticModelCorrespondences,
                    issues,
                    out CollisionCompilationSource? source))
            {
                continue;
            }

            // An atomically removed render/collision pair owns no row in the
            // rebuilt StaticModel domain.
            if (source is null)
                continue;

            sources.Add(source);
            contributions.Add(new CollisionIndexContribution(
                source.ObjectId,
                CollisionIndexDomain.StaticModel,
                elementCount: 1));
            projectedStaticModel = true;
        }

        if (projectedStaticModel)
        {
            issues.Add(new CollisionCompilationProjectionIssue(
                ObjectId: null,
                CollisionCompilationProjectionIssueKind
                    .StaticModelPlacementNotRetained,
                "Editor static-model objects retain model, origin, and " +
                "bounds, but not the complete three-row collision " +
                "inverse-scaled-axis input required by the canonical " +
                "authored source taxonomy."));
            issues.Add(new CollisionCompilationProjectionIssue(
                ObjectId: null,
                CollisionCompilationProjectionIssueKind
                    .StaticModelSpatialTopologyNotRetained,
                "StaticModel row contributions are proven, but the shared " +
                "StaticModelAabbNode tree is aggregate compiler output and " +
                "is not retained by individual editor static-model objects."));
        }

        EditorCollisionObject[] primitives = document.Collision
            .OrderBy(value => value.CollisionKind)
            .ThenBy(value => value.SourceOrdinal.Value)
            .ThenBy(value =>
                value.Id.Value.ToString("N"),
                StringComparer.Ordinal)
            .ToArray();
        foreach (EditorCollisionObject primitive in primitives)
        {
            cancellationToken.ThrowIfCancellationRequested();
            issues.Add(new CollisionCompilationProjectionIssue(
                primitive.Id,
                CollisionCompilationProjectionIssueKind.UnresolvedOwnership,
                "The imported collision primitive has no explicit " +
                "StandaloneWorld or BrushModelEntity ownership authority. " +
                "Absence of a graphics counterpart is not ownership proof."));

            switch (primitive.CollisionKind)
            {
                case CollisionObjectKind.Brush:
                    issues.Add(new CollisionCompilationProjectionIssue(
                        primitive.Id,
                        CollisionCompilationProjectionIssueKind
                            .ConvexBrushTopologyNotRetained,
                        "The editor brush view retains bounds, contents, and " +
                        "a side count, but not exact source-local plane, " +
                        "brush-side, or adjacent-edge ownership."));
                    break;
                case CollisionObjectKind.Triangle:
                    issues.Add(new CollisionCompilationProjectionIssue(
                        primitive.Id,
                        CollisionCompilationProjectionIssueKind
                            .TriangleTopologyNotRetained,
                        "The editor triangle view does not retain its exact " +
                        "shared vertex ordinals, index elements, packed " +
                        "walkability bits, partition, or AABB ownership."));
                    break;
                default:
                    throw new InvalidDataException(
                        $"Unsupported collision primitive kind " +
                        $"{primitive.CollisionKind}.");
            }
        }

        if (primitives.Length != 0)
        {
            issues.Add(new CollisionCompilationProjectionIssue(
                ObjectId: null,
                CollisionCompilationProjectionIssueKind
                    .WorldSpatialTopologyNotRetained,
                "BSP nodes, leaves, leaf-brush nodes/references, leaf-surface " +
                "references, collision models, borders, partitions, and " +
                "world AABB nodes remain shared ColMap topology. They cannot " +
                "be assigned to primitive editor objects without a proven " +
                "structural compiler."));
        }

        return new CollisionCompilationInputProjection(
            document.Id,
            documentRevision,
            sources,
            contributions,
            issues);
    }

    private static bool TryProjectStaticModel(
        EditorMapDocument document,
        EditorStaticModel collision,
        StaticModelCorrespondenceCatalog staticModelCorrespondences,
        ICollection<CollisionCompilationProjectionIssue> issues,
        out CollisionCompilationSource? source)
    {
        source = null;

        return collision.IsImported
            ? TryProjectImportedStaticModel(
                document,
                collision,
                staticModelCorrespondences,
                issues,
                out source)
            : TryProjectAuthoredStaticModel(
                document,
                collision,
                issues,
                out source);
    }

    private static bool TryProjectImportedStaticModel(
        EditorMapDocument document,
        EditorStaticModel collision,
        StaticModelCorrespondenceCatalog staticModelCorrespondences,
        ICollection<CollisionCompilationProjectionIssue> issues,
        out CollisionCompilationSource? source)
    {
        source = null;
        if (!staticModelCorrespondences.AuthoritiesValid ||
            !staticModelCorrespondences.TryGetByCollisionObjectId(
                collision.Id,
                out StaticModelCompilationRelationship? relationship) ||
            relationship is null)
        {
            issues.Add(new CollisionCompilationProjectionIssue(
                collision.Id,
                CollisionCompilationProjectionIssueKind.UnresolvedOwnership,
                "No exact, mutual imported-bundle render static-model " +
                "counterpart is proven for this collision row."));
            return false;
        }
        if (relationship.CollisionAssetKind != MapAssetKind.ColMapMp)
        {
            issues.Add(new CollisionCompilationProjectionIssue(
                collision.Id,
                CollisionCompilationProjectionIssueKind
                    .StaticModelPairInconsistent,
                "The imported static-model relationship does not belong to " +
                "the explicit ColMapMp authority."));
            return false;
        }

        if (!TryGetConsistentStaticModelPair(
                document,
                collision,
                relationship.RenderObjectId,
                relationship.GfxSourceOrdinal,
                relationship.ClipSourceOrdinal,
                expectedAuthoredPair: null,
                out EditorStaticModel? render,
                out string? inconsistency))
        {
            issues.Add(new CollisionCompilationProjectionIssue(
                collision.Id,
                CollisionCompilationProjectionIssueKind
                    .StaticModelPairInconsistent,
                inconsistency!));
            return false;
        }

        if (collision.CompiledDisposition ==
            StaticModelCompiledDisposition.Removed)
        {
            return true;
        }

        source = new CollisionCompilationSource(
            collision.Id,
            CollisionGeometryKind.StaticModelHull,
            CollisionOwnershipCategory.PairedStaticModel,
            CollisionSourceProvenance.Imported,
            relationship.ClipSourceOrdinal,
            new CollisionCounterpartIdentity(
                render!.Id,
                CollisionCounterpartKind.RenderStaticModel));
        return true;
    }

    private static bool TryProjectAuthoredStaticModel(
        EditorMapDocument document,
        EditorStaticModel collision,
        ICollection<CollisionCompilationProjectionIssue> issues,
        out CollisionCompilationSource? source)
    {
        source = null;
        AuthoredStaticModelDuplicatePairState? pair =
            collision.AuthoredDuplicatePair;
        if (pair is null)
        {
            issues.Add(new CollisionCompilationProjectionIssue(
                collision.Id,
                CollisionCompilationProjectionIssueKind.UnresolvedOwnership,
                "The authored collision static model has no explicit shared " +
                "render/collision pair authority."));
            return false;
        }
        if (pair.CollisionAssetKind != MapAssetKind.ColMapMp)
        {
            issues.Add(new CollisionCompilationProjectionIssue(
                collision.Id,
                CollisionCompilationProjectionIssueKind
                    .StaticModelPairInconsistent,
                "The authored static-model pair does not belong to the " +
                "explicit ColMapMp authority."));
            return false;
        }

        if (!TryGetConsistentStaticModelPair(
                document,
                collision,
                pair.RenderObjectId,
                pair.GfxProjectedOrdinal,
                pair.ClipProjectedOrdinal,
                pair,
                out EditorStaticModel? render,
                out string? inconsistency))
        {
            issues.Add(new CollisionCompilationProjectionIssue(
                collision.Id,
                CollisionCompilationProjectionIssueKind
                    .StaticModelPairInconsistent,
                inconsistency!));
            return false;
        }

        if (collision.CompiledDisposition ==
            StaticModelCompiledDisposition.Removed)
        {
            return true;
        }

        source = new CollisionCompilationSource(
            collision.Id,
            CollisionGeometryKind.StaticModelHull,
            CollisionOwnershipCategory.PairedStaticModel,
            CollisionSourceProvenance.Authored,
            importedSourceOrdinal: null,
            new CollisionCounterpartIdentity(
                render!.Id,
                CollisionCounterpartKind.RenderStaticModel));
        return true;
    }

    private static void AddContribution(
        ICollection<CollisionIndexContribution> contributions,
        CollisionCompilationSource source,
        CollisionIndexDomain domain,
        int elementCount)
    {
        if (elementCount < 0)
            throw new ArgumentOutOfRangeException(nameof(elementCount));
        if (elementCount == 0)
            return;

        contributions.Add(new CollisionIndexContribution(
            source.ObjectId,
            domain,
            elementCount));
    }

    private static bool TryGetConsistentStaticModelPair(
        EditorMapDocument document,
        EditorStaticModel collision,
        MapObjectId renderObjectId,
        int expectedRenderOrdinal,
        int expectedCollisionOrdinal,
        AuthoredStaticModelDuplicatePairState? expectedAuthoredPair,
        out EditorStaticModel? render,
        out string? inconsistency)
    {
        render = null;
        inconsistency = null;
        if (!document.TryGetObject(
                renderObjectId,
                out EditorMapObject? renderObject) ||
            renderObject is not EditorStaticModel candidate ||
            candidate.Representation != StaticModelRepresentation.Render)
        {
            inconsistency =
                "The explicit render counterpart is not present as a render " +
                "static-model semantic object in this document.";
            return false;
        }

        bool expectsAuthored = expectedAuthoredPair is not null;
        bool candidateIsAuthored = !candidate.IsImported;
        if (candidateIsAuthored != expectsAuthored ||
            collision.SourceOrdinal.Value != expectedCollisionOrdinal ||
            candidate.SourceOrdinal.Value != expectedRenderOrdinal ||
            candidate.CompiledDisposition != collision.CompiledDisposition ||
            candidate.Origin.Value != collision.Origin.Value ||
            !string.Equals(
                candidate.ModelName.Value,
                collision.ModelName.Value,
                StringComparison.Ordinal))
        {
            inconsistency =
                "The explicit render/collision pair no longer has matching " +
                "lineage, source ordinals, compiled disposition, model " +
                "identity, or origin.";
            return false;
        }

        if (expectedAuthoredPair is not null &&
            (!ReferenceEquals(
                collision.AuthoredDuplicatePair,
                expectedAuthoredPair) ||
             !ReferenceEquals(
                 candidate.AuthoredDuplicatePair,
                 expectedAuthoredPair)))
        {
            inconsistency =
                "The authored render/collision objects do not share the same " +
                "duplication-operation authority.";
            return false;
        }

        render = candidate;
        return true;
    }
}
