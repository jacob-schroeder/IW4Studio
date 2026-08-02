using IW4.Assets.Assets.GfxMap;
using IW4.FastFiles.Emitters.Assets;
using IW4.FastFiles.Emitters.Emission;
using IW4.Studio.MapEditor.Compilation.RenderWorld;
using IW4.Studio.MapEditor.Compilation.RenderWorld.Visibility;

namespace IW4.Studio.MapEditor.Compilation.TargetAcceptance;

/// <summary>
/// Re-proves the serializer-only M4 projection and managed emitter
/// compatibility. Runtime-safe no-bake lighting has a separate typed policy
/// and validator so its defaults cannot weaken this compatibility boundary.
/// </summary>
internal static class GfxWorldTargetAcceptanceValidator
{
    internal static GfxWorldTargetAcceptanceAssessment Assess(
        RenderWorldVisibilityCandidate source,
        MapPrimaryChecksumAssignment checksumAssignment,
        GfxWorldAsset definition,
        GfxWorldReferenceBuildData references,
        IReadOnlyList<GfxWorldTargetAcceptanceBlocker> blockers)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(checksumAssignment);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(blockers);

        var issues = new List<GfxWorldTargetAcceptanceIssue>();
        ValidateChecksum(checksumAssignment, definition, issues);
        ValidateIdentity(source, definition, issues);
        ValidateGeometry(source.RenderCandidate, definition, issues);
        ValidateVisibility(source, definition, issues);
        ValidateSerializerOnlyState(definition, issues);
        ValidateReferences(source.RenderCandidate, references, issues);
        ValidateBlockers(blockers, issues);
        ValidateEmitter(definition, references, issues);
        return new GfxWorldTargetAcceptanceAssessment(issues);
    }

    private static void ValidateChecksum(
        MapPrimaryChecksumAssignment assignment,
        GfxWorldAsset definition,
        ICollection<GfxWorldTargetAcceptanceIssue> issues)
    {
        bool canonical =
            assignment.Kind ==
                MapPrimaryChecksumAssignmentKind.StudioCanonicalV1 &&
            assignment.ProductionFidelity ==
                MapPrimaryChecksumProductionFidelity
                    .ConsumerCompatibleProductionByteScopeUnknown &&
            assignment.ContentIdentity is not null &&
            assignment.ImportedBaseline is null &&
            MapPrimaryChecksumPolicy.ComputeStudioCanonical(
                assignment.ContentIdentity).Checksum ==
                assignment.Checksum;
        if (!canonical ||
            definition.Checksum != assignment.Checksum.Value)
        {
            Add(
                issues,
                GfxWorldTargetAcceptanceIssueKind
                    .PrimaryChecksumAssignmentInvalid,
                "$.checksum",
                "The GfxWorld root must carry the freshly recomputed " +
                "StudioCanonicalV1 checksum supplied for this probe.");
        }
    }

    private static void ValidateIdentity(
        RenderWorldVisibilityCandidate source,
        GfxWorldAsset definition,
        ICollection<GfxWorldTargetAcceptanceIssue> issues)
    {
        string expectedBaseName = Path.GetFileNameWithoutExtension(
            source.MapAssetName);
        if (!string.Equals(
                definition.Name,
                source.MapAssetName,
                StringComparison.Ordinal) ||
            !string.Equals(
                definition.BaseName,
                expectedBaseName,
                StringComparison.Ordinal))
        {
            Add(
                issues,
                GfxWorldTargetAcceptanceIssueKind
                    .ProjectionIdentityMismatch,
                "$.name",
                "The serialized root did not retain the source map and " +
                "derived base-name identities.");
        }
    }

    private static void ValidateGeometry(
        RenderWorldStructuralCandidate source,
        GfxWorldAsset definition,
        ICollection<GfxWorldTargetAcceptanceIssue> issues)
    {
        RenderWorldCompiledGeometry geometry = source.Geometry;
        bool exact =
            definition.SurfaceCount == source.Surfaces.Count &&
            definition.WorldDraw.VertexCount == geometry.VertexCount &&
            definition.WorldDraw.VertexData.PackedVertices
                .SequenceEqual(geometry.PackedPositionData) &&
            definition.WorldDraw.VertexLayerDataSize ==
                geometry.PackedVertexLayerData.Count &&
            definition.WorldDraw.VertexLayerData.PackedLayerData
                .SequenceEqual(geometry.PackedVertexLayerData) &&
            definition.WorldDraw.IndexCount == geometry.Indices.Count &&
            definition.WorldDraw.Indices
                .SequenceEqual(geometry.Indices) &&
            definition.Dpvs.Surfaces.Count == source.Surfaces.Count &&
            definition.Models.Count == 1 &&
            definition.ModelCount == 1 &&
            definition.MapVertexChecksum == source.MapVertexChecksum;
        if (exact)
        {
            for (int index = 0;
                 index < source.Surfaces.Count;
                 index++)
            {
                RenderWorldCompiledSurface expected =
                    source.Surfaces[index];
                SrfTriangles actual =
                    definition.Dpvs.Surfaces[index].Triangles;
                if (actual.VertexLayerData !=
                        expected.VertexLayerData ||
                    actual.BaseVertex != expected.BaseVertex ||
                    actual.MinVertexIndex !=
                        expected.MinVertexIndex ||
                    actual.VertexCount != expected.VertexCount ||
                    actual.TriCount != expected.TriangleCount ||
                    actual.BaseIndex != expected.BaseIndex)
                {
                    exact = false;
                    break;
                }
            }
        }

        if (!exact)
        {
            Add(
                issues,
                GfxWorldTargetAcceptanceIssueKind
                    .GeometryProjectionMismatch,
                "$.worldDraw",
                "Packed streams, surface windows, world-model cardinality, " +
                "or map-vertex checksum diverged from the M3 candidate.");
        }
    }

    private static void ValidateVisibility(
        RenderWorldVisibilityCandidate source,
        GfxWorldAsset definition,
        ICollection<GfxWorldTargetAcceptanceIssue> issues)
    {
        bool exact =
            definition.PlaneCount ==
                source.CellPartitionPlanes.Count &&
            definition.NodeCount ==
                source.PackedCellPartitionNodes.Count &&
            definition.DpvsPlanes.CellCount == source.Cells.Count &&
            definition.DpvsPlanes.Nodes
                .SequenceEqual(source.PackedCellPartitionNodes) &&
            definition.DpvsPlanes.SceneEntCellBits.Count ==
                source.RuntimeAllocationShape
                    .SceneEntityCellBitWordCount &&
            definition.DpvsPlanes.SceneEntCellBits.All(value =>
                value == 0) &&
            definition.CellTreeCounts.Count == source.Cells.Count &&
            definition.CellTrees.Count == source.Cells.Count &&
            definition.Cells.Count == source.Cells.Count &&
            definition.Dpvs.SortedSurfIndex.SequenceEqual(
                source.SortedWorldSurfaceOrdinals) &&
            definition.Dpvs.StaticSurfaceCount ==
                source.StaticDpvsShape.StaticSurfaceCount &&
            definition.Dpvs.SModelCount ==
                source.StaticDpvsShape.StaticModelCount &&
            definition.Dpvs.VisibilityCounts.Count == 8 &&
            definition.Dpvs.VisibilityCounts[6] == 0 &&
            definition.Dpvs.VisibilityCounts[7] ==
                source.RuntimeAllocationShape
                    .SurfaceVisibilityWordCount &&
            definition.Dpvs.SurfaceBounds.Count ==
                source.SurfaceMemberships.Count;
        if (!exact)
        {
            Add(
                issues,
                GfxWorldTargetAcceptanceIssueKind
                    .VisibilityProjectionMismatch,
                "$.dpvs",
                "Single-cell membership, bounds, sorted surfaces, or " +
                "runtime allocation counts diverged from the M4 candidate.");
        }
    }

    private static void ValidateSerializerOnlyState(
        GfxWorldAsset definition,
        ICollection<GfxWorldTargetAcceptanceIssue> issues)
    {
        bool runtimeEmpty =
            definition.CellCasterBits.Count == 0 &&
            definition.CellCasterBits2.Count == 0 &&
            definition.SceneDynModels.Count == 0 &&
            definition.SceneDynBrushes.Count == 0 &&
            definition.PrimaryLightEntityShadowVis.Count == 0 &&
            definition.PrimaryLightDynEntShadowVis0.Count == 0 &&
            definition.PrimaryLightDynEntShadowVis1.Count == 0 &&
            definition.PrimaryLightForModelDynEnt.Count == 0 &&
            definition.Dpvs.SModelVisData.Count == 0 &&
            definition.Dpvs.SurfaceVisData.Count == 0 &&
            definition.Dpvs.SurfaceMaterials.Count == 0 &&
            definition.Dpvs.SurfaceCastsSunShadow.Count == 0 &&
            definition.DpvsDyn.DynEntCellBits.Count == 0 &&
            definition.DpvsDyn.DynEntVisData.Count == 0;
        if (!runtimeEmpty)
        {
            Add(
                issues,
                GfxWorldTargetAcceptanceIssueKind
                    .RuntimeStateAuthorshipViolation,
                "$.runtime",
                "The managed probe may declare runtime allocation counts but " +
                "must not author runtime-populated visibility state.");
        }

        bool unlit =
            definition.SkyCount == 0 &&
            definition.Skies.Count == 0 &&
            definition.PrimaryLightCount == 0 &&
            definition.WorldDraw.ReflectionProbeCount == 0 &&
            definition.WorldDraw.LightmapCount == 0 &&
            definition.MaterialMemoryCount == 0 &&
            definition.ShadowGeom.Count == 0 &&
            definition.LightRegions.Count == 0 &&
            definition.Dpvs.LitSurfsBegin == 0 &&
            definition.Dpvs.LitSurfsEnd == 0 &&
            definition.Dpvs.Surfaces.All(surface =>
                surface.LightmapIndex == 0 &&
                surface.ReflectionProbeIndex == 0 &&
                surface.PrimaryLightIndex == 0 &&
                surface.CastsSunShadow == 0) &&
            definition.Dpvs.SurfaceBounds.All(value =>
                value.Unknown18To1F.Count ==
                    GfxWorldTargetAcceptanceProfile
                        .SurfaceBoundsTailByteCount &&
                value.Unknown18To1F.All(item => item == 0));
        if (!unlit)
        {
            Add(
                issues,
                GfxWorldTargetAcceptanceIssueKind
                    .UnlitProbePolicyMismatch,
                "$.lighting",
                "The M4 serializer probe must retain the explicit unlit-zero " +
                "and zero surface-bounds-tail policies.");
        }
    }

    private static void ValidateReferences(
        RenderWorldStructuralCandidate source,
        GfxWorldReferenceBuildData references,
        ICollection<GfxWorldTargetAcceptanceIssue> issues)
    {
        string[] expected = source.Surfaces
            .Select(surface =>
                surface.SymbolicMaterialName.StartsWith(
                    ",",
                    StringComparison.Ordinal)
                    ? surface.SymbolicMaterialName
                    : "," + surface.SymbolicMaterialName)
            .ToArray();
        string?[] actual = references.SurfaceMaterials
            .Select(value => value?.OriginalSerializedName)
            .ToArray();
        bool serializerOnlyLightingReferencesEmpty =
            references.ReflectionProbeImages.Count == 0 &&
            references.ReflectionProbeImageDefinitions.Count == 0 &&
            references.ReflectionProbeImageLinks.Count == 0 &&
            references.Lightmaps.Count == 0 &&
            references.LightmapDefinitions.Count == 0 &&
            references.LightmapLinks.Count == 0;
        if (!actual.SequenceEqual(expected) ||
            !serializerOnlyLightingReferencesEmpty)
        {
            Add(
                issues,
                GfxWorldTargetAcceptanceIssueKind
                    .SymbolicReferenceMismatch,
                "$.references.surfaceMaterials",
                "Each surface must retain its ordered external symbolic " +
                "material identity, while the serializer-only M4 projection " +
                "must not acquire target-runtime lighting references.");
        }
    }

    private static void ValidateBlockers(
        IReadOnlyList<GfxWorldTargetAcceptanceBlocker> blockers,
        ICollection<GfxWorldTargetAcceptanceIssue> issues)
    {
        GfxWorldTargetAcceptanceBlockerKind[] expected =
            Enum.GetValues<GfxWorldTargetAcceptanceBlockerKind>();
        GfxWorldTargetAcceptanceBlockerKind[] actual = blockers
            .Select(value => value.Kind)
            .Order()
            .ToArray();
        if (!actual.SequenceEqual(expected.Order()))
        {
            Add(
                issues,
                GfxWorldTargetAcceptanceIssueKind
                    .DeferredBlockerContractMismatch,
                "$.blockers",
                "Every M4/M5/M7 deferred blocker must remain explicit and " +
                "unique on the managed probe.");
        }
    }

    private static void ValidateEmitter(
        GfxWorldAsset definition,
        GfxWorldReferenceBuildData references,
        ICollection<GfxWorldTargetAcceptanceIssue> issues)
    {
        var buildData = new GfxWorldTargetAcceptanceBuildData(
            definition,
            references);
        var emitter = new GfxWorldBodyEmitter();
        IReadOnlyList<EmissionError> errors = emitter.Validate(buildData);
        foreach (EmissionError error in errors)
        {
            Add(
                issues,
                GfxWorldTargetAcceptanceIssueKind
                    .SerializerValidationFailed,
                error.Path,
                error.Message);
        }
        if (errors.Count != 0)
            return;

        try
        {
            _ = emitter.Plan(buildData, new EmissionPlan());
        }
        catch (Exception exception)
        {
            Add(
                issues,
                GfxWorldTargetAcceptanceIssueKind
                    .SerializerPlanningFailed,
                "$",
                exception.Message);
        }
    }

    private static void Add(
        ICollection<GfxWorldTargetAcceptanceIssue> issues,
        GfxWorldTargetAcceptanceIssueKind kind,
        string path,
        string detail) =>
        issues.Add(new GfxWorldTargetAcceptanceIssue(
            kind,
            path,
            detail));
}
