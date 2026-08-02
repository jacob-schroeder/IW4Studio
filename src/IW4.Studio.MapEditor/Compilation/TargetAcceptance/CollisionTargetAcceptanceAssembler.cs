using IW4.Assets.Assets.ColMap;
using IW4.FastFiles.Emitters.Assets;
using IW4.FastFiles.Emitters.Emission;
using IW4.FastFiles.Zone;
using IW4.Studio.MapEditor.Compilation.Collision;
using IW4.Studio.MapEditor.Compilation.RenderWorld.Visibility;

namespace IW4.Studio.MapEditor.Compilation.TargetAcceptance;

/// <summary>
/// Projects the validated, bounded M4 visibility input into a detached
/// serializer-valid ColMap root. The result remains limited to managed
/// emission/fresh-load verification and is not registered with Save As.
/// </summary>
public static class CollisionTargetAcceptanceAssembler
{
    public static CollisionTargetAcceptanceCandidate Assemble(
        RenderWorldVisibilityCandidate sourceCandidate,
        MapPrimaryChecksumAssignment checksumAssignment)
    {
        ArgumentNullException.ThrowIfNull(sourceCandidate);
        ArgumentNullException.ThrowIfNull(checksumAssignment);

        RenderWorldVisibilityAssessment visibilityAssessment =
            RenderWorldVisibilityValidator.Assess(sourceCandidate);
        if (!visibilityAssessment.IsValid)
        {
            throw new InvalidDataException(
                "The ColMap target-acceptance projection requires a valid " +
                "bounded M4 visibility candidate: " +
                string.Join(
                    "; ",
                    visibilityAssessment.Issues.Select(value =>
                        $"{value.Path}: {value.Detail}")));
        }

        RequireCanonicalStudioChecksum(checksumAssignment);

        CollisionStructuralCandidate collision =
            sourceCandidate.CollisionCandidate;
        ClipMapAsset definition = ProjectDefinition(
            collision.Definition,
            checksumAssignment.Checksum.Value);
        var references = new ClipMapReferenceBuildData(
            staticModels: [],
            dynamicEntities:
            [
                Array.Empty<ClipMapDynEntityReferenceBuildData>(),
                Array.Empty<ClipMapDynEntityReferenceBuildData>()
            ],
            mapEnts: null);

        CollisionStructuralReachabilityAssessment structuralAssessment =
            CollisionStructuralReachabilityValidator.Assess(definition);
        if (!structuralAssessment.IsValid)
        {
            throw new InvalidDataException(
                "The checksum-projected ColMap no longer satisfies the M3 " +
                "structural contract: " +
                string.Join(
                    "; ",
                    structuralAssessment.LocalRecordAssessment.Issues
                        .Select(value =>
                            $"{value.Path}: {value.Detail}")
                        .Concat(
                            structuralAssessment.Issues.Select(value =>
                                $"{value.Path}: {value.Detail}"))));
        }

        CollisionPlanePointerOwnershipPlan planePlan =
            CollisionPlanePointerOwnershipPlan.Create(definition);
        planePlan.RequireAuthoredNonNullBindings();
        _ = CollisionBrushReferencePlan.Create(definition);

        var result = new CollisionTargetAcceptanceCandidate(
            sourceCandidate,
            checksumAssignment,
            definition,
            references,
            structuralAssessment);
        IReadOnlyList<EmissionError> errors =
            new ClipMapBodyEmitter(XAssetType.ColMapMp)
                .Validate(result.BuildDataAdapter);
        if (errors.Count != 0)
        {
            throw new InvalidDataException(
                "The M4 ColMap projection is not serializer-valid: " +
                string.Join(
                    "; ",
                    errors.Select(value =>
                        $"{value.Path}: {value.Message}")));
        }

        return result;
    }

    private static void RequireCanonicalStudioChecksum(
        MapPrimaryChecksumAssignment assignment)
    {
        if (assignment.Kind !=
                MapPrimaryChecksumAssignmentKind.StudioCanonicalV1 ||
            assignment.ProductionFidelity !=
                MapPrimaryChecksumProductionFidelity
                    .ConsumerCompatibleProductionByteScopeUnknown ||
            assignment.ContentIdentity is null ||
            assignment.ImportedBaseline is not null)
        {
            throw new ArgumentException(
                "The authored M4 target-acceptance profile requires one " +
                "StudioCanonicalV1 primary checksum assignment with exact " +
                "whole-map content-identity provenance.",
                nameof(assignment));
        }

        MapPrimaryChecksumAssignment recomputed =
            MapPrimaryChecksumPolicy.ComputeStudioCanonical(
                assignment.ContentIdentity);
        if (recomputed.Checksum != assignment.Checksum)
        {
            throw new ArgumentException(
                "The supplied primary checksum does not match a fresh " +
                "StudioCanonicalV1 calculation over its retained whole-map " +
                "content identity.",
                nameof(assignment));
        }
    }

    /// <summary>
    /// Creates a pointer-free root while retaining the exact table objects
    /// from the immutable M3 candidate. Retaining those objects preserves the
    /// plane/side, brush/side, and global-border/partition aliases consumed by
    /// the ColMap emitter.
    /// </summary>
    private static ClipMapAsset ProjectDefinition(
        ClipMapAsset source,
        uint checksum)
    {
        if (source.SerializedType != XAssetType.ColMapMp)
        {
            throw new ArgumentException(
                "The initial M4 target-acceptance profile supports only " +
                "multiplayer ColMap candidates.",
                nameof(source));
        }

        return new ClipMapAsset
        {
            SerializedType = XAssetType.ColMapMp,
            Name = source.Name,
            IsInUse = 0,
            SerializedIsInUse = 0,
            PlaneCount = source.PlaneCount,
            Planes = source.Planes,
            NumStaticModels = source.NumStaticModels,
            StaticModelList = source.StaticModelList,
            NumMaterials = source.NumMaterials,
            Materials = source.Materials,
            NumBrushSides = source.NumBrushSides,
            BrushSides = source.BrushSides,
            NumBrushEdges = source.NumBrushEdges,
            BrushEdges = source.BrushEdges,
            NumNodes = source.NumNodes,
            Nodes = source.Nodes,
            NumLeafs = source.NumLeafs,
            Leafs = source.Leafs,
            LeafBrushNodesCount = source.LeafBrushNodesCount,
            LeafBrushNodes = source.LeafBrushNodes,
            NumLeafBrushes = source.NumLeafBrushes,
            LeafBrushes = source.LeafBrushes,
            NumLeafSurfaces = source.NumLeafSurfaces,
            LeafSurfaces = source.LeafSurfaces,
            VertCount = source.VertCount,
            Verts = source.Verts,
            TriCount = source.TriCount,
            TriIndices = source.TriIndices,
            TriEdgeIsWalkable = source.TriEdgeIsWalkable,
            BorderCount = source.BorderCount,
            Borders = source.Borders,
            PartitionCount = source.PartitionCount,
            Partitions = source.Partitions,
            AabbTreeCount = source.AabbTreeCount,
            AabbTrees = source.AabbTrees,
            NumSubModels = source.NumSubModels,
            CModels = source.CModels,
            NumBrushes = source.NumBrushes,
            Pad8ETo8F = source.Pad8ETo8F,
            Brushes = source.Brushes,
            BrushBounds = source.BrushBounds,
            BrushContents = source.BrushContents,
            MapEnts = null,
            MapEntsIncomingDefinition = null,
            SModelNodeCount = source.SModelNodeCount,
            PadA2ToA3 = source.PadA2ToA3,
            SModelNodes = source.SModelNodes,
            DynEntCount = source.DynEntCount,
            DynEntDefList = source.DynEntDefList,
            DynEntPoseList = source.DynEntPoseList,
            DynEntClientList = source.DynEntClientList,
            DynEntCollList = source.DynEntCollList,
            Checksum = checksum,
            PadD0ToFF = source.PadD0ToFF
        };
    }
}
