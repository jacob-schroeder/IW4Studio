using System.Collections.ObjectModel;
using IW4.Studio.MapEditor.Compilation.Collision;
using IW4.Studio.MapEditor.Editing.Objects;

namespace IW4.Studio.MapEditor.Compilation.RenderWorld.Visibility;

public enum RenderWorldVisibilityIssueKind
{
    SourceCandidateInvalid = 0,
    SourceIdentityMismatch = 1,
    StaticModelDomainNotAdmitted = 2,
    InlineModelDomainNotAdmitted = 3,
    DynamicEntityDomainNotAdmitted = 4,
    ProfileMismatch = 5,
    CellPartitionTopologyMismatch = 6,
    CellTreeTopologyMismatch = 7,
    SurfaceMembershipMismatch = 8,
    SurfaceBoundsMismatch = 9,
    AggregateBoundsMismatch = 10,
    RuntimeAllocationShapeMismatch = 11,
    RuntimeStateAuthorshipViolation = 12,
    CoverageProofMismatch = 13,
    DeferredBlockerContractMismatch = 14
}

public sealed record RenderWorldVisibilityIssue(
    RenderWorldVisibilityIssueKind Kind,
    string Path,
    string Detail);

public sealed class RenderWorldVisibilityAssessment
{
    private readonly IReadOnlyList<RenderWorldVisibilityIssue> _issues;

    internal RenderWorldVisibilityAssessment(
        IEnumerable<RenderWorldVisibilityIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        _issues =
            new ReadOnlyCollection<RenderWorldVisibilityIssue>(
                issues.ToArray());
    }

    public IReadOnlyList<RenderWorldVisibilityIssue> Issues => _issues;
    public bool IsValid => Issues.Count == 0;
}

public sealed class RenderWorldVisibilityCompilationException :
    Exception
{
    public RenderWorldVisibilityCompilationException(
        RenderWorldVisibilityAssessment assessment)
        : base(CreateMessage(assessment))
    {
        Assessment = assessment ??
            throw new ArgumentNullException(nameof(assessment));
    }

    public RenderWorldVisibilityAssessment Assessment { get; }

    private static string CreateMessage(
        RenderWorldVisibilityAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        return
            "The bounded M4 visibility candidate failed closed: " +
            string.Join(
                "; ",
                assessment.Issues.Select(value =>
                    $"{value.Path}: {value.Detail}"));
    }
}

/// <summary>
/// Re-proves admission, exact surface coverage, outward containment, and
/// runtime allocation cardinalities without invoking a target renderer.
/// Target acceptance remains an explicit blocker.
/// </summary>
public static class RenderWorldVisibilityValidator
{
    public static RenderWorldVisibilityAssessment Assess(
        RenderWorldVisibilityCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return Assess(
            candidate.CollisionCandidate,
            candidate.RenderCandidate,
            candidate.CollisionWorldBounds,
            candidate.SortedWorldSurfaceOrdinals,
            candidate.SurfaceMemberships,
            candidate.Cells,
            candidate.StaticDpvsShape,
            candidate.RuntimeAllocationShape,
            candidate.CoverageProof,
            candidate.Blockers,
            candidate.CompilerIdentity,
            candidate.TopologyProfile,
            candidate.CellPartitionPlanes,
            candidate.PackedCellPartitionNodes);
    }

    internal static RenderWorldVisibilityAssessment AssessAdmission(
        CollisionStructuralCandidate collisionCandidate,
        RenderWorldStructuralCandidate renderCandidate)
    {
        ArgumentNullException.ThrowIfNull(collisionCandidate);
        ArgumentNullException.ThrowIfNull(renderCandidate);
        var issues = new List<RenderWorldVisibilityIssue>();
        AssessSourceCandidates(
            collisionCandidate,
            renderCandidate,
            issues);
        return new RenderWorldVisibilityAssessment(issues);
    }

    internal static RenderWorldVisibilityAssessment Assess(
        CollisionStructuralCandidate collisionCandidate,
        RenderWorldStructuralCandidate renderCandidate,
        MapBounds collisionWorldBounds,
        IReadOnlyList<ushort> sortedWorldSurfaceOrdinals,
        IReadOnlyList<RenderWorldVisibilitySurfaceMembership>
            surfaceMemberships,
        IReadOnlyList<RenderWorldVisibilityCell> cells,
        RenderWorldVisibilityStaticDpvsShape staticDpvsShape,
        RenderWorldVisibilityRuntimeAllocationShape runtimeAllocationShape,
        RenderWorldVisibilityCoverageProof coverageProof,
        IReadOnlyList<RenderWorldVisibilityBlocker> blockers,
        string compilerIdentity,
        RenderWorldVisibilityTopologyProfile topologyProfile,
        IReadOnlyList<RenderWorldVisibilityCellPartitionPlane>
            cellPartitionPlanes,
        IReadOnlyList<ushort> packedCellPartitionNodes)
    {
        ArgumentNullException.ThrowIfNull(collisionCandidate);
        ArgumentNullException.ThrowIfNull(renderCandidate);
        ArgumentNullException.ThrowIfNull(sortedWorldSurfaceOrdinals);
        ArgumentNullException.ThrowIfNull(surfaceMemberships);
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(staticDpvsShape);
        ArgumentNullException.ThrowIfNull(runtimeAllocationShape);
        ArgumentNullException.ThrowIfNull(coverageProof);
        ArgumentNullException.ThrowIfNull(blockers);
        ArgumentNullException.ThrowIfNull(compilerIdentity);
        ArgumentNullException.ThrowIfNull(cellPartitionPlanes);
        ArgumentNullException.ThrowIfNull(packedCellPartitionNodes);

        var issues = new List<RenderWorldVisibilityIssue>();
        AssessSourceCandidates(
            collisionCandidate,
            renderCandidate,
            issues);
        AssessProfile(
            compilerIdentity,
            topologyProfile,
            cellPartitionPlanes,
            packedCellPartitionNodes,
            issues);

        MapBounds expectedCollisionWorldBounds = default;
        bool collisionBoundsAvailable = false;
        try
        {
            expectedCollisionWorldBounds =
                RenderWorldVisibilityOutwardBounds.FromCollisionWorld(
                    collisionCandidate);
            collisionBoundsAvailable = true;
            if (collisionWorldBounds != expectedCollisionWorldBounds)
            {
                Add(
                    issues,
                    RenderWorldVisibilityIssueKind.AggregateBoundsMismatch,
                    "collisionWorldBounds",
                    "The retained collision-bound evidence does not match " +
                    "the M3 collision world model.");
            }
        }
        catch (Exception exception) when (
            exception is InvalidDataException or
            ArgumentOutOfRangeException or
            OverflowException)
        {
            Add(
                issues,
                RenderWorldVisibilityIssueKind.AggregateBoundsMismatch,
                "collisionWorldBounds",
                exception.Message);
        }

        MapBounds[] expectedSurfaceBounds =
            CreateExpectedSurfaceBounds(renderCandidate, issues);
        AssessMemberships(
            renderCandidate,
            expectedSurfaceBounds,
            sortedWorldSurfaceOrdinals,
            surfaceMemberships,
            issues);
        AssessCells(
            collisionBoundsAvailable
                ? expectedCollisionWorldBounds
                : collisionWorldBounds,
            expectedSurfaceBounds,
            sortedWorldSurfaceOrdinals.Count,
            cells,
            issues);
        AssessStaticDpvsShape(
            renderCandidate.Surfaces.Count,
            staticDpvsShape,
            issues);
        AssessRuntimeShape(
            renderCandidate.Surfaces.Count,
            runtimeAllocationShape,
            issues);
        AssessCoverageProof(
            renderCandidate,
            collisionBoundsAvailable
                ? expectedCollisionWorldBounds
                : collisionWorldBounds,
            expectedSurfaceBounds,
            surfaceMemberships,
            cells,
            coverageProof,
            issues);
        AssessBlockers(blockers, issues);

        return new RenderWorldVisibilityAssessment(issues);
    }

    private static void AssessSourceCandidates(
        CollisionStructuralCandidate collisionCandidate,
        RenderWorldStructuralCandidate renderCandidate,
        ICollection<RenderWorldVisibilityIssue> issues)
    {
        if (collisionCandidate.Authority !=
                CollisionStructuralCandidateAuthority
                    .OfflineValidationOnly ||
            collisionCandidate.PersistenceAuthorized ||
            !collisionCandidate.ReachabilityAssessment.IsValid ||
            renderCandidate.Authority !=
                RenderWorldStructuralCandidateAuthority
                    .OfflineValidationOnly ||
            renderCandidate.PersistenceAuthorized ||
            !renderCandidate.ValidationAssessment.IsValid)
        {
            Add(
                issues,
                RenderWorldVisibilityIssueKind.SourceCandidateInvalid,
                "sources",
                "M4 requires valid detached M3 collision and render " +
                "candidates with offline-only authority.");
        }

        if (collisionCandidate.DocumentId != renderCandidate.DocumentId ||
            collisionCandidate.DocumentRevision !=
                renderCandidate.DocumentRevision ||
            !string.Equals(
                collisionCandidate.MapAssetName,
                renderCandidate.MapAssetName,
                StringComparison.Ordinal))
        {
            Add(
                issues,
                RenderWorldVisibilityIssueKind.SourceIdentityMismatch,
                "sources.identity",
                "Collision and render candidates must carry the exact same " +
                "document, revision, and normalized map asset name.");
        }

        bool hasEmptyStaticModelAabbRoot =
            collisionCandidate.Definition.SModelNodeCount == 1 &&
            collisionCandidate.Definition.SModelNodes.Count == 1 &&
            collisionCandidate.Definition.SModelNodes[0] is
            {
                FirstChild: 0,
                ChildCount: 0,
                Bounds:
                {
                    MidPoint: { X: 0, Y: 0, Z: 0 },
                    HalfSize: { X: 0, Y: 0, Z: 0 }
                }
            };
        if (collisionCandidate.Definition.NumStaticModels != 0 ||
            collisionCandidate.Definition.StaticModelList.Count != 0 ||
            !hasEmptyStaticModelAabbRoot)
        {
            Add(
                issues,
                RenderWorldVisibilityIssueKind
                    .StaticModelDomainNotAdmitted,
                "collision.staticModels",
                "The single-cell profile initially admits zero static " +
                "models and requires one zero-filled native traversal " +
                "root.");
        }

        bool collisionHasInlineModels =
            collisionCandidate.Definition.NumSubModels != 1 ||
            collisionCandidate.Definition.CModels.Count != 1 ||
            collisionCandidate.InlineModelPlan.ModelCount != 1;
        bool renderHasInlineModels =
            renderCandidate.InlineModelRanges.Count != 0 ||
            renderCandidate.Surfaces.Any(value =>
                value.OwnershipKind !=
                    RenderMeshOwnershipKind.StandaloneWorld);
        if (collisionHasInlineModels || renderHasInlineModels)
        {
            Add(
                issues,
                RenderWorldVisibilityIssueKind
                    .InlineModelDomainNotAdmitted,
                "sources.inlineModels",
                "The single-cell profile initially admits the world model " +
                "only; collision and render inline models are rejected.");
        }

        bool invalidDynamicCounts =
            collisionCandidate.Definition.DynEntCount.Count != 2 ||
            collisionCandidate.Definition.DynEntCount.Any(
                value => value != 0);
        bool materializedDynamics =
            collisionCandidate.Definition.DynEntDefList.Any(
                value => value.Count != 0) ||
            collisionCandidate.Definition.DynEntPoseList.Any(
                value => value.Count != 0) ||
            collisionCandidate.Definition.DynEntClientList.Any(
                value => value.Count != 0) ||
            collisionCandidate.Definition.DynEntCollList.Any(
                value => value.Count != 0);
        if (invalidDynamicCounts || materializedDynamics)
        {
            Add(
                issues,
                RenderWorldVisibilityIssueKind
                    .DynamicEntityDomainNotAdmitted,
                "collision.dynamicEntities",
                "The single-cell profile initially admits zero dynamic " +
                "model and brush entities.");
        }

        int surfaceCount = renderCandidate.Surfaces.Count;
        if (surfaceCount is <= 0 or > ushort.MaxValue ||
            renderCandidate.StandaloneWorldSurfaceRange !=
                new RenderWorldRange(0, surfaceCount))
        {
            Add(
                issues,
                RenderWorldVisibilityIssueKind.SurfaceMembershipMismatch,
                "render.worldSurfaceRange",
                "The admitted world surface domain must be nonempty, fit a " +
                "UInt16 AABB-leaf count, and occupy the complete prefix.");
        }
    }

    private static void AssessProfile(
        string compilerIdentity,
        RenderWorldVisibilityTopologyProfile topologyProfile,
        IReadOnlyList<RenderWorldVisibilityCellPartitionPlane>
            cellPartitionPlanes,
        IReadOnlyList<ushort> packedCellPartitionNodes,
        ICollection<RenderWorldVisibilityIssue> issues)
    {
        if (!string.Equals(
                compilerIdentity,
                RenderWorldVisibilityProfile.CompilerIdentity,
                StringComparison.Ordinal) ||
            topologyProfile !=
                RenderWorldVisibilityTopologyProfile.SingleCellAllStaticV1)
        {
            Add(
                issues,
                RenderWorldVisibilityIssueKind.ProfileMismatch,
                "profile",
                "The candidate does not carry the exact separately " +
                "versioned single-cell/all-static profile.");
        }

        if (cellPartitionPlanes.Count != 0 ||
            packedCellPartitionNodes.Count != 1 ||
            packedCellPartitionNodes[0] !=
                RenderWorldVisibilityProfile.SingleCellLeafNode)
        {
            Add(
                issues,
                RenderWorldVisibilityIssueKind
                    .CellPartitionTopologyMismatch,
                "cellPartition",
                "The bounded camera-cell BSP is exactly planes=[] and " +
                "nodes=[1], resolving every finite origin to cell zero.");
        }
    }

    private static MapBounds[] CreateExpectedSurfaceBounds(
        RenderWorldStructuralCandidate renderCandidate,
        ICollection<RenderWorldVisibilityIssue> issues)
    {
        var result =
            new MapBounds[renderCandidate.Surfaces.Count];
        for (int surfaceIndex = 0;
             surfaceIndex < renderCandidate.Surfaces.Count;
             surfaceIndex++)
        {
            try
            {
                result[surfaceIndex] =
                    RenderWorldVisibilityOutwardBounds.FromSurface(
                        renderCandidate.Geometry,
                        renderCandidate.Surfaces[surfaceIndex]);
            }
            catch (Exception exception) when (
                exception is InvalidDataException or
                ArgumentOutOfRangeException or
                OverflowException)
            {
                Add(
                    issues,
                    RenderWorldVisibilityIssueKind.SurfaceBoundsMismatch,
                    $"surfaceBounds[{surfaceIndex}]",
                    exception.Message);
            }
        }

        return result;
    }

    private static void AssessMemberships(
        RenderWorldStructuralCandidate renderCandidate,
        IReadOnlyList<MapBounds> expectedSurfaceBounds,
        IReadOnlyList<ushort> sortedWorldSurfaceOrdinals,
        IReadOnlyList<RenderWorldVisibilitySurfaceMembership>
            surfaceMemberships,
        ICollection<RenderWorldVisibilityIssue> issues)
    {
        int surfaceCount = renderCandidate.Surfaces.Count;
        bool sortedMatches =
            sortedWorldSurfaceOrdinals.SequenceEqual(
                renderCandidate.SortedWorldSurfaceOrdinals);
        bool exactCover =
            sortedWorldSurfaceOrdinals.Count == surfaceCount &&
            sortedWorldSurfaceOrdinals
                .Select(value => (int)value)
                .Order()
                .SequenceEqual(Enumerable.Range(0, surfaceCount));
        if (!sortedMatches || !exactCover ||
            surfaceMemberships.Count != surfaceCount)
        {
            Add(
                issues,
                RenderWorldVisibilityIssueKind.SurfaceMembershipMismatch,
                "surfaceMemberships",
                "The cell must retain the M3 sorted world-surface array and " +
                "cover every surface ordinal exactly once.");
        }

        int commonCount = Math.Min(
            surfaceMemberships.Count,
            sortedWorldSurfaceOrdinals.Count);
        for (int membershipIndex = 0;
             membershipIndex < commonCount;
             membershipIndex++)
        {
            RenderWorldVisibilitySurfaceMembership membership =
                surfaceMemberships[membershipIndex];
            ushort expectedOrdinal =
                sortedWorldSurfaceOrdinals[membershipIndex];
            if (expectedOrdinal >= renderCandidate.Surfaces.Count)
                continue;

            RenderWorldCompiledSurface surface =
                renderCandidate.Surfaces[expectedOrdinal];
            if (membership.MembershipOrdinal != membershipIndex ||
                membership.SurfaceOrdinal != expectedOrdinal ||
                membership.SourceObjectId != surface.SourceObjectId ||
                membership.SourceSurfaceOrdinal !=
                    surface.SourceSurfaceOrdinal)
            {
                Add(
                    issues,
                    RenderWorldVisibilityIssueKind
                        .SurfaceMembershipMismatch,
                    $"surfaceMemberships[{membershipIndex}]",
                    "Membership slot, surface identity, or source identity " +
                    "does not match the sorted M3 surface row.");
            }

            if (membership.Bounds !=
                    expectedSurfaceBounds[expectedOrdinal] ||
                !RenderWorldVisibilityOutwardBounds
                    .ContainsSurfaceVertices(
                        membership.Bounds,
                        renderCandidate.Geometry,
                        surface))
            {
                Add(
                    issues,
                    RenderWorldVisibilityIssueKind.SurfaceBoundsMismatch,
                    $"surfaceMemberships[{membershipIndex}].bounds",
                    "The surface bound must be the exact outward-rounded " +
                    "envelope of its packed M3 source positions.");
            }
        }
    }

    private static void AssessCells(
        MapBounds collisionWorldBounds,
        IReadOnlyList<MapBounds> expectedSurfaceBounds,
        int surfaceCount,
        IReadOnlyList<RenderWorldVisibilityCell> cells,
        ICollection<RenderWorldVisibilityIssue> issues)
    {
        MapBounds expectedAggregateBounds =
            RenderWorldVisibilityOutwardBounds.Include(
            [
                collisionWorldBounds,
                .. expectedSurfaceBounds
            ]);
        if (cells.Count != RenderWorldVisibilityProfile.CellCount)
        {
            Add(
                issues,
                RenderWorldVisibilityIssueKind.CellTreeTopologyMismatch,
                "cells",
                "The profile requires exactly one materialized cell.");
            return;
        }

        RenderWorldVisibilityCell cell = cells[0];
        bool topologyMatches =
            cell.Ordinal == 0 &&
            cell.PortalCount == RenderWorldVisibilityProfile.PortalCount &&
            cell.Tree.DeclaredAabbTreeCount == 1 &&
            cell.Tree.AabbTrees.Count == 1 &&
            cell.Tree.RootLeaf.ChildCount == 0 &&
            cell.Tree.RootLeaf.ChildrenOffset == 0 &&
            cell.Tree.RootLeaf.SortedSurfaceRange ==
                new RenderWorldRange(0, surfaceCount) &&
            cell.Tree.RootLeaf.StaticModelIndexCount == 0 &&
            cell.Tree.RootLeaf.StaticModelOrdinals.Count == 0;
        if (!topologyMatches)
        {
            Add(
                issues,
                RenderWorldVisibilityIssueKind.CellTreeTopologyMismatch,
                "cells[0].tree",
                "Cell zero requires one root AABB leaf, exact complete " +
                "surface range, and no child or static-model membership.");
        }

        if (cell.Bounds != expectedAggregateBounds ||
            cell.Tree.RootLeaf.Bounds != expectedAggregateBounds)
        {
            Add(
                issues,
                RenderWorldVisibilityIssueKind.AggregateBoundsMismatch,
                "cells[0].bounds",
                "Cell and root-tree bounds must equal the outward union of " +
                "collision world bounds and all render surface bounds.");
        }
    }

    private static void AssessRuntimeShape(
        int surfaceCount,
        RenderWorldVisibilityRuntimeAllocationShape shape,
        ICollection<RenderWorldVisibilityIssue> issues)
    {
        int expectedSurfaceWords =
            RenderWorldVisibilityProfile.AlignedVisibilityWordCount(
                surfaceCount);
        bool shapeMatches =
            shape.SceneEntityCellBitWordCount ==
                RenderWorldVisibilityProfile
                    .SceneEntityCellBitWordCount &&
            shape.StaticVisibilityViewCount ==
                RenderWorldVisibilityProfile.StaticVisibilityViewCount &&
            shape.StaticModelVisibilityWordCount == 0 &&
            shape.SurfaceVisibilityWordCount ==
                expectedSurfaceWords &&
            shape.SurfaceMaterialRowCount == surfaceCount &&
            shape.SurfaceSunShadowWordCount == expectedSurfaceWords &&
            shape.CellCasterMatrixWordCount == 1 &&
            shape.CellCasterAggregateWordCount == 1 &&
            shape.DynamicModelClientCount == 0 &&
            shape.DynamicBrushClientCount == 0 &&
            shape.DynamicModelWordCount == 0 &&
            shape.DynamicBrushWordCount == 0 &&
            shape.DynamicCellBitWordCount == 0 &&
            shape.DynamicVisibilityByteCount == 0;
        if (!shapeMatches)
        {
            Add(
                issues,
                RenderWorldVisibilityIssueKind
                    .RuntimeAllocationShapeMismatch,
                "runtimeAllocationShape",
                "Runtime allocation cardinalities must match one cell, " +
                "zero models/dynamics, and 128-bit-aligned surface " +
                "visibility rows.");
        }
        if (shape.CarriesAuthoredRuntimeState)
        {
            Add(
                issues,
                RenderWorldVisibilityIssueKind
                    .RuntimeStateAuthorshipViolation,
                "runtimeAllocationShape",
                "M4 may describe runtime allocation shapes but cannot " +
                "author runtime-owned visibility state.");
        }
    }

    private static void AssessStaticDpvsShape(
        int surfaceCount,
        RenderWorldVisibilityStaticDpvsShape shape,
        ICollection<RenderWorldVisibilityIssue> issues)
    {
        int expectedSurfaceWords =
            RenderWorldVisibilityProfile.AlignedVisibilityWordCount(
                surfaceCount);
        bool unresolvedPrefix =
            shape.VisibilityWordCounts.Count == 8 &&
            shape.VisibilityWordCounts.Take(6).All(
                value => value is null);
        bool knownSuffix =
            shape.VisibilityWordCounts.Count == 8 &&
            shape.VisibilityWordCounts[6] == 0 &&
            shape.VisibilityWordCounts[7] ==
                checked((uint)expectedSurfaceWords);
        if (shape.StaticModelCount != 0 ||
            shape.StaticSurfaceCount != checked((uint)surfaceCount) ||
            shape.LitSurfacesBegin is not null ||
            shape.LitSurfacesEnd is not null ||
            shape.LightingPartitionResolved ||
            !unresolvedPrefix ||
            !knownSuffix)
        {
            Add(
                issues,
                RenderWorldVisibilityIssueKind
                    .RuntimeAllocationShapeMismatch,
                "staticDpvsShape",
                "M4 must prove zero static models, every admitted surface " +
                "as static, known visibility counts [6]/[7], and leave " +
                "lighting-dependent ranges/counts unresolved.");
        }
    }

    private static void AssessCoverageProof(
        RenderWorldStructuralCandidate renderCandidate,
        MapBounds collisionWorldBounds,
        IReadOnlyList<MapBounds> expectedSurfaceBounds,
        IReadOnlyList<RenderWorldVisibilitySurfaceMembership>
            memberships,
        IReadOnlyList<RenderWorldVisibilityCell> cells,
        RenderWorldVisibilityCoverageProof proof,
        ICollection<RenderWorldVisibilityIssue> issues)
    {
        int surfaceCount = renderCandidate.Surfaces.Count;
        bool exactCover =
            memberships.Count == surfaceCount &&
            memberships
                .Select(value => (int)value.SurfaceOrdinal)
                .Order()
                .SequenceEqual(Enumerable.Range(0, surfaceCount));
        bool surfaceContainment =
            memberships.Count == surfaceCount &&
            memberships.All(value =>
                value.SurfaceOrdinal < renderCandidate.Surfaces.Count &&
                RenderWorldVisibilityOutwardBounds
                    .ContainsSurfaceVertices(
                        value.Bounds,
                        renderCandidate.Geometry,
                        renderCandidate.Surfaces[value.SurfaceOrdinal]));
        bool aggregateContainment = cells.Count == 1 &&
            RenderWorldVisibilityOutwardBounds.Contains(
                cells[0].Bounds,
                collisionWorldBounds) &&
            expectedSurfaceBounds.All(value =>
                RenderWorldVisibilityOutwardBounds.Contains(
                    cells[0].Bounds,
                    value)) &&
            RenderWorldVisibilityOutwardBounds.Contains(
                cells[0].Tree.RootLeaf.Bounds,
                collisionWorldBounds) &&
            expectedSurfaceBounds.All(value =>
                RenderWorldVisibilityOutwardBounds.Contains(
                    cells[0].Tree.RootLeaf.Bounds,
                    value));
        bool proofMatches =
            proof.Policy ==
                RenderWorldVisibilityCoveragePolicy
                    .ExactWorldSurfaceCoverWithOutwardBoundsV1 &&
            proof.CoveredSurfaceCount == surfaceCount &&
            proof.ExactOrdinalCover == exactCover &&
            proof.SurfaceBoundsContainPackedVertices ==
                surfaceContainment &&
            proof.AggregateBoundsContainCollisionAndSurfaces ==
                aggregateContainment &&
            proof.NoFalseNegativeWithinAdmittedGeometry;
        if (!proofMatches)
        {
            Add(
                issues,
                RenderWorldVisibilityIssueKind.CoverageProofMismatch,
                "coverageProof",
                "The no-false-negative proof must establish exact ordinal " +
                "cover and outward containment of collision and render " +
                "source geometry.");
        }
    }

    private static void AssessBlockers(
        IReadOnlyList<RenderWorldVisibilityBlocker> blockers,
        ICollection<RenderWorldVisibilityIssue> issues)
    {
        RenderWorldVisibilityBlockerKind[] expectedKinds =
            Enum.GetValues<RenderWorldVisibilityBlockerKind>();
        bool exactKinds =
            blockers.Count == expectedKinds.Length &&
            blockers
                .Select(value => value.Kind)
                .Order()
                .SequenceEqual(expectedKinds.Order());
        bool milestonesMatch = blockers.All(value =>
            value.Kind switch
            {
                RenderWorldVisibilityBlockerKind
                    .TargetConsumerAcceptanceNotEstablished =>
                    value.Milestone ==
                    RenderWorldVisibilityDeferredMilestone
                        .M4TargetConsumerAcceptance,
                RenderWorldVisibilityBlockerKind
                    .LightingMembershipAndBakeOutputsNotCompiled =>
                    value.Milestone ==
                    RenderWorldVisibilityDeferredMilestone.M5Lighting,
                RenderWorldVisibilityBlockerKind
                    .CompleteGfxWorldAssemblyNotCompiled or
                RenderWorldVisibilityBlockerKind
                    .LinkingEmissionAndPersistenceNotAuthorized =>
                    value.Milestone ==
                    RenderWorldVisibilityDeferredMilestone
                        .M7AssetResolutionAndPersistence,
                _ => false
            });
        if (!exactKinds || !milestonesMatch)
        {
            Add(
                issues,
                RenderWorldVisibilityIssueKind
                    .DeferredBlockerContractMismatch,
                "blockers",
                "The candidate must retain the exact target-consumer, M5, " +
                "and M7 deferred boundary.");
        }
    }

    private static void Add(
        ICollection<RenderWorldVisibilityIssue> issues,
        RenderWorldVisibilityIssueKind kind,
        string path,
        string detail) =>
        issues.Add(new RenderWorldVisibilityIssue(kind, path, detail));
}
