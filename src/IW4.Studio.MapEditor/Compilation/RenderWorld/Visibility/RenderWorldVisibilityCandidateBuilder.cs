using IW4.Studio.MapEditor.Compilation.Collision;
using IW4.Studio.MapEditor.Editing.Objects;

namespace IW4.Studio.MapEditor.Compilation.RenderWorld.Visibility;

/// <summary>
/// Composes coherent M3 collision and render artifacts into the separately
/// versioned one-cell/all-static M4 topology. The result remains detached
/// from target asset construction and persistence.
/// </summary>
public static class RenderWorldVisibilityCandidateBuilder
{
    public static RenderWorldVisibilityCandidate Compile(
        CollisionStructuralCandidate collisionCandidate,
        RenderWorldStructuralCandidate renderCandidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collisionCandidate);
        ArgumentNullException.ThrowIfNull(renderCandidate);
        cancellationToken.ThrowIfCancellationRequested();

        RenderWorldVisibilityAssessment admissionAssessment =
            RenderWorldVisibilityValidator.AssessAdmission(
                collisionCandidate,
                renderCandidate);
        if (!admissionAssessment.IsValid)
        {
            throw new RenderWorldVisibilityCompilationException(
                admissionAssessment);
        }

        MapBounds collisionWorldBounds =
            RenderWorldVisibilityOutwardBounds.FromCollisionWorld(
                collisionCandidate);
        var surfaceBoundsByOrdinal =
            new MapBounds[renderCandidate.Surfaces.Count];
        for (int surfaceOrdinal = 0;
             surfaceOrdinal < renderCandidate.Surfaces.Count;
             surfaceOrdinal++)
        {
            if ((surfaceOrdinal & 0xFFF) == 0)
                cancellationToken.ThrowIfCancellationRequested();

            surfaceBoundsByOrdinal[surfaceOrdinal] =
                RenderWorldVisibilityOutwardBounds.FromSurface(
                    renderCandidate.Geometry,
                    renderCandidate.Surfaces[surfaceOrdinal]);
        }

        ushort[] sortedWorldSurfaceOrdinals =
            renderCandidate.SortedWorldSurfaceOrdinals.ToArray();
        var memberships =
            new RenderWorldVisibilitySurfaceMembership[
                sortedWorldSurfaceOrdinals.Length];
        for (int membershipOrdinal = 0;
             membershipOrdinal < memberships.Length;
             membershipOrdinal++)
        {
            ushort surfaceOrdinal =
                sortedWorldSurfaceOrdinals[membershipOrdinal];
            RenderWorldCompiledSurface surface =
                renderCandidate.Surfaces[surfaceOrdinal];
            memberships[membershipOrdinal] =
                new RenderWorldVisibilitySurfaceMembership(
                    membershipOrdinal,
                    surfaceOrdinal,
                    surface.SourceObjectId,
                    surface.SourceSurfaceOrdinal,
                    surfaceBoundsByOrdinal[surfaceOrdinal]);
        }

        MapBounds aggregateBounds =
            RenderWorldVisibilityOutwardBounds.Include(
            [
                collisionWorldBounds,
                .. surfaceBoundsByOrdinal
            ]);
        var rootLeaf = new RenderWorldVisibilityAabbLeaf(
            aggregateBounds,
            new RenderWorldRange(
                start: 0,
                count: sortedWorldSurfaceOrdinals.Length));
        var cell = new RenderWorldVisibilityCell(
            ordinal: 0,
            aggregateBounds,
            new RenderWorldVisibilityCellTree(rootLeaf));
        var runtimeShape =
            new RenderWorldVisibilityRuntimeAllocationShape(
                renderCandidate.Surfaces.Count);
        var staticDpvsShape =
            new RenderWorldVisibilityStaticDpvsShape(
                renderCandidate.Surfaces.Count);

        bool exactOrdinalCover =
            sortedWorldSurfaceOrdinals
                .Select(value => (int)value)
                .Order()
                .SequenceEqual(
                    Enumerable.Range(
                        0,
                        renderCandidate.Surfaces.Count));
        bool surfaceBoundsContainPackedVertices =
            memberships.All(value =>
                RenderWorldVisibilityOutwardBounds
                    .ContainsSurfaceVertices(
                        value.Bounds,
                        renderCandidate.Geometry,
                        renderCandidate.Surfaces[value.SurfaceOrdinal]));
        bool aggregateContainsCollisionAndSurfaces =
            RenderWorldVisibilityOutwardBounds.Contains(
                aggregateBounds,
                collisionWorldBounds) &&
            surfaceBoundsByOrdinal.All(value =>
                RenderWorldVisibilityOutwardBounds.Contains(
                    aggregateBounds,
                    value));
        var coverageProof = new RenderWorldVisibilityCoverageProof(
            memberships.Length,
            exactOrdinalCover,
            surfaceBoundsContainPackedVertices,
            aggregateContainsCollisionAndSurfaces);
        RenderWorldVisibilityBlocker[] blockers =
            CreateDeferredBlockers();

        RenderWorldVisibilityAssessment validationAssessment =
            RenderWorldVisibilityValidator.Assess(
                collisionCandidate,
                renderCandidate,
                collisionWorldBounds,
                sortedWorldSurfaceOrdinals,
                memberships,
                [cell],
                staticDpvsShape,
                runtimeShape,
                coverageProof,
                blockers,
                RenderWorldVisibilityProfile.CompilerIdentity,
                RenderWorldVisibilityTopologyProfile
                    .SingleCellAllStaticV1,
                Array.Empty<
                    RenderWorldVisibilityCellPartitionPlane>(),
                [RenderWorldVisibilityProfile.SingleCellLeafNode]);
        if (!validationAssessment.IsValid)
        {
            throw new RenderWorldVisibilityCompilationException(
                validationAssessment);
        }

        return new RenderWorldVisibilityCandidate(
            collisionCandidate,
            renderCandidate,
            collisionWorldBounds,
            sortedWorldSurfaceOrdinals,
            memberships,
            cell,
            staticDpvsShape,
            runtimeShape,
            coverageProof,
            blockers,
            validationAssessment);
    }

    private static RenderWorldVisibilityBlocker[]
        CreateDeferredBlockers() =>
    [
        new(
            RenderWorldVisibilityDeferredMilestone
                .M4TargetConsumerAcceptance,
            RenderWorldVisibilityBlockerKind
                .TargetConsumerAcceptanceNotEstablished,
            "The topology has offline exact-cover proof only. Camera-cell, " +
            "static-cull, and platform consumer acceptance remain required " +
            "before this profile can become a target contract."),
        new(
            RenderWorldVisibilityDeferredMilestone.M5Lighting,
            RenderWorldVisibilityBlockerKind
                .LightingMembershipAndBakeOutputsNotCompiled,
            "M5 owns light, reflection-probe, shadow, lightmap, and baked " +
            "visibility assignments. Runtime allocation cardinalities carry " +
            "no authored visibility state."),
        new(
            RenderWorldVisibilityDeferredMilestone
                .M7AssetResolutionAndPersistence,
            RenderWorldVisibilityBlockerKind
                .CompleteGfxWorldAssemblyNotCompiled,
            "This detached topology is not a complete GfxWorldAsset and " +
            "does not implement IGfxWorldBuildData."),
        new(
            RenderWorldVisibilityDeferredMilestone
                .M7AssetResolutionAndPersistence,
            RenderWorldVisibilityBlockerKind
                .LinkingEmissionAndPersistenceNotAuthorized,
            "Material resolution, nested linking, emission, asset-pool " +
            "registration, Save As, and persistence remain unauthorized.")
    ];
}
