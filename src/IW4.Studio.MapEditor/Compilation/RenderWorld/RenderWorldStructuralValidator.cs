using System.Buffers.Binary;
using System.Collections.ObjectModel;
using IW4.Studio.MapEditor.Compilation.Collision;
using IW4.Studio.MapEditor.Editing.Identity;

namespace IW4.Studio.MapEditor.Compilation.RenderWorld;

public enum RenderWorldStructuralIssueKind
{
    ProfileMismatch = 0,
    PackedPositionShapeMismatch = 1,
    PackedVertexLayerShapeMismatch = 2,
    VertexStreamCardinalityMismatch = 3,
    SurfaceOrdinalMismatch = 4,
    SurfaceRangeMismatch = 5,
    SurfaceIndexOutOfRange = 6,
    UnreferencedSurfaceVertex = 7,
    SourceMappingMismatch = 8,
    SourceBoundsMismatch = 9,
    StandaloneWorldRangeMismatch = 10,
    SortedWorldSurfaceOrdinalMismatch = 11,
    InlineModelRangeMismatch = 12,
    OwnershipMismatch = 13,
    AggregateCoverageMismatch = 14,
    DeferredBlockerContractMismatch = 15,
    CanonicalSourceVertexMismatch = 16,
    CanonicalSourceTriangleMismatch = 17,
    InlineModelAllocationMismatch = 18,
    MapVertexChecksumPolicyMismatch = 19
}

public sealed record RenderWorldStructuralIssue(
    RenderWorldStructuralIssueKind Kind,
    string Path,
    string Detail);

public sealed class RenderWorldStructuralAssessment
{
    private readonly IReadOnlyList<RenderWorldStructuralIssue> _issues;

    internal RenderWorldStructuralAssessment(
        IEnumerable<RenderWorldStructuralIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        _issues =
            new ReadOnlyCollection<RenderWorldStructuralIssue>(
                issues.ToArray());
    }

    public IReadOnlyList<RenderWorldStructuralIssue> Issues => _issues;
    public bool IsValid => Issues.Count == 0;
}

/// <summary>
/// Validates the complete detached M3 payload/range graph. This proves local
/// structural consistency only; it cannot grant consumer, material, linker,
/// emitter, or persistence acceptance.
/// </summary>
public static class RenderWorldStructuralValidator
{
    public static RenderWorldStructuralAssessment Assess(
        RenderWorldStructuralCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return Assess(candidate.Geometry, candidate.Blockers);
    }

    internal static RenderWorldStructuralAssessment Assess(
        RenderWorldCompiledGeometry geometry,
        IReadOnlyList<RenderWorldStructuralBlocker> blockers)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(blockers);
        var issues = new List<RenderWorldStructuralIssue>();

        AssessProfileAndStreams(geometry, issues);
        AssessSurfaces(geometry, issues);
        AssessSourceMappings(geometry, issues);
        AssessCanonicalSourceEquivalence(geometry, issues);
        AssessOwnershipRanges(geometry, issues);
        AssessBlockers(blockers, issues);

        return new RenderWorldStructuralAssessment(issues);
    }

    private static void AssessProfileAndStreams(
        RenderWorldCompiledGeometry geometry,
        ICollection<RenderWorldStructuralIssue> issues)
    {
        if (geometry.VertexProfile !=
                RenderWorldStructuralVertexProfile
                    .Tex1Nrm1StructuralV1 ||
            geometry.PositionStride !=
                RenderWorldStructuralProfile.PositionStride ||
            geometry.VertexLayerStride !=
                RenderWorldStructuralProfile.VertexLayerStride ||
            !string.Equals(
                geometry.CompilerIdentity,
                RenderWorldStructuralProfile.CompilerIdentity,
                StringComparison.Ordinal) ||
            !string.Equals(
                geometry.SymbolicMaterialSurfaceOrderingPolicyId,
                RenderWorldStructuralProfile
                    .SymbolicMaterialSurfaceOrderingPolicyId,
                StringComparison.Ordinal))
        {
            Add(
                issues,
                RenderWorldStructuralIssueKind.ProfileMismatch,
                "profile",
                "The geometry does not carry the exact bounded structural " +
                "profile identity and strides.");
        }
        if (geometry.MapVertexChecksumAssignment.Kind !=
                GfxMapVertexChecksumAssignmentKind
                    .StudioConstantZeroV1 ||
            geometry.MapVertexChecksumAssignment.ProductionFidelity !=
                GfxMapVertexChecksumProductionFidelity
                    .DeterministicStudioAssignmentRetailParityUnproven ||
            geometry.MapVertexChecksum != 0 ||
            GfxMapVertexChecksumPolicy.CurrentStatus !=
                GfxMapVertexChecksumPolicyStatus
                    .ImportedPreservationAndStudioConstantZeroV1)
        {
            Add(
                issues,
                RenderWorldStructuralIssueKind
                    .MapVertexChecksumPolicyMismatch,
                "mapVertexChecksumAssignment",
                "Greenfield M3 geometry requires the explicit versioned " +
                "StudioConstantZeroV1 assignment with retail parity " +
                "unproven.");
        }

        int positionByteCount = geometry.PackedPositionData.Count;
        if (positionByteCount == 0 ||
            positionByteCount %
                RenderWorldStructuralProfile.PositionStride != 0)
        {
            Add(
                issues,
                RenderWorldStructuralIssueKind
                    .PackedPositionShapeMismatch,
                "packedPositionData",
                "Packed positions must contain nonempty exact 16-byte " +
                "rows.");
        }

        int layerByteCount = geometry.PackedVertexLayerData.Count;
        if (layerByteCount == 0 ||
            layerByteCount %
                RenderWorldStructuralProfile.VertexLayerStride != 0)
        {
            Add(
                issues,
                RenderWorldStructuralIssueKind
                    .PackedVertexLayerShapeMismatch,
                "packedVertexLayerData",
                "Packed vertex layers must contain nonempty exact 28-byte " +
                "rows.");
        }

        int positionCount =
            positionByteCount /
            RenderWorldStructuralProfile.PositionStride;
        int layerCount =
            layerByteCount /
            RenderWorldStructuralProfile.VertexLayerStride;
        if (positionCount != layerCount)
        {
            Add(
                issues,
                RenderWorldStructuralIssueKind
                    .VertexStreamCardinalityMismatch,
                "packedVertexData",
                $"Position and layer streams retain {positionCount} and " +
                $"{layerCount} rows.");
        }

        for (int vertexIndex = 0;
             vertexIndex < positionCount;
             vertexIndex++)
        {
            int offset = checked(
                vertexIndex *
                RenderWorldStructuralProfile.PositionStride);
            float x = ReadSingleBigEndian(
                geometry.PackedPositionData,
                offset);
            float y = ReadSingleBigEndian(
                geometry.PackedPositionData,
                offset + sizeof(float));
            float z = ReadSingleBigEndian(
                geometry.PackedPositionData,
                offset + 2 * sizeof(float));
            float w = ReadSingleBigEndian(
                geometry.PackedPositionData,
                offset + 3 * sizeof(float));
            if (!float.IsFinite(x) ||
                !float.IsFinite(y) ||
                !float.IsFinite(z) ||
                !float.IsFinite(w) ||
                w != 1f)
            {
                Add(
                    issues,
                    RenderWorldStructuralIssueKind
                        .PackedPositionShapeMismatch,
                    $"packedPositionData[{vertexIndex}]",
                    "Every position row must be finite big-endian float4 " +
                    "with homogeneous W exactly 1.");
            }
        }

        for (int vertexIndex = 0;
             vertexIndex < layerCount;
             vertexIndex++)
        {
            int offset = checked(
                vertexIndex *
                RenderWorldStructuralProfile.VertexLayerStride);
            bool finite =
                float.IsFinite(
                    ReadSingleBigEndian(
                        geometry.PackedVertexLayerData,
                        offset + 0x04)) &&
                float.IsFinite(
                    ReadSingleBigEndian(
                        geometry.PackedVertexLayerData,
                        offset + 0x08)) &&
                float.IsFinite(
                    ReadSingleBigEndian(
                        geometry.PackedVertexLayerData,
                        offset + 0x0C)) &&
                float.IsFinite(
                    ReadSingleBigEndian(
                        geometry.PackedVertexLayerData,
                        offset + 0x10));
            if (!finite)
            {
                Add(
                    issues,
                    RenderWorldStructuralIssueKind
                        .PackedVertexLayerShapeMismatch,
                    $"packedVertexLayerData[{vertexIndex}]",
                    "Base and lightmap UV channels must be finite " +
                    "big-endian floats.");
            }
        }
    }

    private static void AssessSurfaces(
        RenderWorldCompiledGeometry geometry,
        ICollection<RenderWorldStructuralIssue> issues)
    {
        if (geometry.Surfaces.Count == 0 ||
            geometry.Surfaces.Count >
                RenderWorldStructuralProfile.MaximumSurfaceCount)
        {
            Add(
                issues,
                RenderWorldStructuralIssueKind
                    .AggregateCoverageMismatch,
                "surfaces",
                "The detached surface domain must be nonempty and " +
                "UInt16-addressable.");
        }

        int positionCount =
            geometry.PackedPositionData.Count /
            RenderWorldStructuralProfile.PositionStride;
        int vertexCursor = 0;
        int layerCursor = 0;
        int indexCursor = 0;
        for (int surfaceIndex = 0;
             surfaceIndex < geometry.Surfaces.Count;
             surfaceIndex++)
        {
            RenderWorldCompiledSurface surface =
                geometry.Surfaces[surfaceIndex];
            string path = $"surfaces[{surfaceIndex}]";
            if (surface.Ordinal != surfaceIndex)
            {
                Add(
                    issues,
                    RenderWorldStructuralIssueKind
                        .SurfaceOrdinalMismatch,
                    $"{path}.ordinal",
                    $"Surface row {surfaceIndex} declares ordinal " +
                    $"{surface.Ordinal}.");
            }

            bool rangesValid =
                surface.VertexRange.Start == vertexCursor &&
                surface.VertexRange.Count is > 0 and <=
                    RenderWorldStructuralProfile
                        .MaximumVerticesPerSurface &&
                surface.VertexRange.EndExclusive <= positionCount &&
                surface.VertexLayerByteRange.Start == layerCursor &&
                surface.VertexLayerByteRange.Count ==
                    surface.VertexRange.Count *
                    RenderWorldStructuralProfile.VertexLayerStride &&
                surface.VertexLayerByteRange.EndExclusive <=
                    geometry.PackedVertexLayerData.Count &&
                surface.IndexRange.Start == indexCursor &&
                surface.TriangleCount is > 0 and <=
                    RenderWorldStructuralProfile
                        .MaximumTrianglesPerSurface &&
                surface.IndexRange.Count ==
                    surface.TriangleCount * 3 &&
                surface.IndexRange.EndExclusive <=
                    geometry.Indices.Count;
            if (!rangesValid)
            {
                Add(
                    issues,
                    RenderWorldStructuralIssueKind
                        .SurfaceRangeMismatch,
                    path,
                    "Surface vertex, layer-byte, or index ranges are not " +
                    "contiguous and exactly covered.");
            }

            if (surface.IndexRange.Start >= 0 &&
                surface.IndexRange.EndExclusive <=
                    geometry.Indices.Count &&
                surface.VertexRange.Count > 0)
            {
                var referenced =
                    new bool[surface.VertexRange.Count];
                for (int index = surface.IndexRange.Start;
                     index < surface.IndexRange.EndExclusive;
                     index++)
                {
                    ushort localIndex = geometry.Indices[index];
                    if (localIndex >= surface.VertexRange.Count)
                    {
                        Add(
                            issues,
                            RenderWorldStructuralIssueKind
                                .SurfaceIndexOutOfRange,
                            $"indices[{index}]",
                            $"Local index {localIndex} is outside surface " +
                            $"{surfaceIndex}'s {surface.VertexRange.Count}" +
                            "-vertex window.");
                    }
                    else
                    {
                        referenced[localIndex] = true;
                    }
                }

                int firstUnreferenced = Array.FindIndex(
                    referenced,
                    value => !value);
                if (firstUnreferenced >= 0)
                {
                    Add(
                        issues,
                        RenderWorldStructuralIssueKind
                            .UnreferencedSurfaceVertex,
                        $"{path}.vertexRange",
                        $"Local vertex {firstUnreferenced} is not reachable " +
                        "from the surface index range.");
                }
            }

            vertexCursor = surface.VertexRange.EndExclusive;
            layerCursor =
                surface.VertexLayerByteRange.EndExclusive;
            indexCursor = surface.IndexRange.EndExclusive;
        }

        if (vertexCursor != positionCount ||
            layerCursor != geometry.PackedVertexLayerData.Count ||
            indexCursor != geometry.Indices.Count)
        {
            Add(
                issues,
                RenderWorldStructuralIssueKind
                    .AggregateCoverageMismatch,
                "packedGeometry",
                "Surface ranges do not cover every packed vertex, layer " +
                "byte, and index exactly once.");
        }
    }

    private static void AssessSourceMappings(
        RenderWorldCompiledGeometry geometry,
        ICollection<RenderWorldStructuralIssue> issues)
    {
        if (geometry.Sources.Count != geometry.SourceMappings.Count)
        {
            Add(
                issues,
                RenderWorldStructuralIssueKind.SourceMappingMismatch,
                "sourceMappings",
                $"The candidate retains {geometry.Sources.Count} sources " +
                $"and {geometry.SourceMappings.Count} mappings.");
        }

        var sourceIds = new HashSet<MapObjectId>();
        int mappingCursor = 0;
        int commonCount = Math.Min(
            geometry.Sources.Count,
            geometry.SourceMappings.Count);
        for (int index = 0; index < commonCount; index++)
        {
            AuthoredIndexedRenderMeshSource source =
                geometry.Sources[index];
            RenderWorldSourceSurfaceMapping mapping =
                geometry.SourceMappings[index];
            string path = $"sourceMappings[{index}]";
            bool sourceMatches =
                source.ObjectId == mapping.SourceObjectId &&
                source.Ownership.Kind == mapping.OwnershipKind &&
                source.Ownership.InlineBrushModelObjectId ==
                    mapping.InlineBrushModelObjectId &&
                mapping.ModelOrdinal ==
                    ExpectedModelOrdinal(source, geometry) &&
                string.Equals(
                    source.SymbolicMaterialName,
                    mapping.SymbolicMaterialName,
                    StringComparison.Ordinal) &&
                source.TriangleWinding == mapping.TriangleWinding &&
                mapping.SurfaceRange.Start == mappingCursor &&
                mapping.SurfaceRange.Count > 0 &&
                mapping.SurfaceRange.EndExclusive <=
                    geometry.Surfaces.Count &&
                sourceIds.Add(mapping.SourceObjectId);
            if (!sourceMatches)
            {
                Add(
                    issues,
                    RenderWorldStructuralIssueKind.SourceMappingMismatch,
                    path,
                    "Source identity, ownership, material, winding, " +
                    "uniqueness, or contiguous surface range is invalid.");
            }

            RenderWorldSourceBounds expectedBounds =
                RenderWorldSourceBounds.From(source.Vertices);
            if (mapping.SourceBounds != expectedBounds)
            {
                Add(
                    issues,
                    RenderWorldStructuralIssueKind.SourceBoundsMismatch,
                    $"{path}.sourceBounds",
                    "The source-bound sidecar does not match canonical " +
                    "source positions.");
            }

            if (mapping.SurfaceRange.Start >= 0 &&
                mapping.SurfaceRange.EndExclusive <=
                    geometry.Surfaces.Count)
            {
                for (int surfaceIndex = mapping.SurfaceRange.Start;
                     surfaceIndex < mapping.SurfaceRange.EndExclusive;
                     surfaceIndex++)
                {
                    RenderWorldCompiledSurface surface =
                        geometry.Surfaces[surfaceIndex];
                    int expectedSourceSurfaceOrdinal =
                        surfaceIndex - mapping.SurfaceRange.Start;
                    if (surface.SourceObjectId !=
                            mapping.SourceObjectId ||
                        surface.SourceSurfaceOrdinal !=
                            expectedSourceSurfaceOrdinal ||
                        surface.OwnershipKind !=
                            mapping.OwnershipKind ||
                        surface.InlineBrushModelObjectId !=
                            mapping.InlineBrushModelObjectId ||
                        surface.ModelOrdinal != mapping.ModelOrdinal ||
                        !string.Equals(
                            surface.SymbolicMaterialName,
                            mapping.SymbolicMaterialName,
                            StringComparison.Ordinal) ||
                        surface.TriangleWinding !=
                            mapping.TriangleWinding)
                    {
                        Add(
                            issues,
                            RenderWorldStructuralIssueKind
                                .OwnershipMismatch,
                            $"surfaces[{surfaceIndex}]",
                            "A surface contradicts its canonical source " +
                            "mapping.");
                    }
                }
            }

            mappingCursor = mapping.SurfaceRange.EndExclusive;
        }

        if (mappingCursor != geometry.Surfaces.Count)
        {
            Add(
                issues,
                RenderWorldStructuralIssueKind
                    .AggregateCoverageMismatch,
                "sourceMappings",
                "Source mappings do not cover the complete surface domain.");
        }
    }

    private static void AssessCanonicalSourceEquivalence(
        RenderWorldCompiledGeometry geometry,
        ICollection<RenderWorldStructuralIssue> issues)
    {
        int commonCount = Math.Min(
            geometry.Sources.Count,
            geometry.SourceMappings.Count);
        for (int mappingIndex = 0;
             mappingIndex < commonCount;
             mappingIndex++)
        {
            AuthoredIndexedRenderMeshSource source =
                geometry.Sources[mappingIndex];
            RenderWorldSourceSurfaceMapping mapping =
                geometry.SourceMappings[mappingIndex];
            if (mapping.SurfaceRange.Start < 0 ||
                mapping.SurfaceRange.EndExclusive >
                    geometry.Surfaces.Count)
            {
                continue;
            }

            int sourceTriangleCursor = 0;
            for (int surfaceIndex = mapping.SurfaceRange.Start;
                 surfaceIndex < mapping.SurfaceRange.EndExclusive;
                 surfaceIndex++)
            {
                RenderWorldCompiledSurface surface =
                    geometry.Surfaces[surfaceIndex];
                string path = $"surfaces[{surfaceIndex}]";
                bool vertexMapShapeValid =
                    surface.SourceVertexIndices.Count ==
                        surface.VertexRange.Count &&
                    surface.SourceVertexIndices
                        .All(value =>
                            (uint)value < (uint)source.Vertices.Count) &&
                    surface.SourceVertexIndices.Distinct().Count() ==
                        surface.SourceVertexIndices.Count;
                if (!vertexMapShapeValid)
                {
                    Add(
                        issues,
                        RenderWorldStructuralIssueKind
                            .CanonicalSourceVertexMismatch,
                        $"{path}.sourceVertexIndices",
                        "The immutable surface-local to canonical-source " +
                        "vertex map is invalid.");
                }
                else
                {
                    for (int localVertex = 0;
                         localVertex <
                            surface.SourceVertexIndices.Count;
                         localVertex++)
                    {
                        AuthoredRenderVertex sourceVertex =
                            source.Vertices[
                                surface.SourceVertexIndices[localVertex]];
                        byte[] expectedPosition =
                            new byte[
                                RenderWorldStructuralProfile.PositionStride];
                        byte[] expectedLayer =
                            new byte[
                                RenderWorldStructuralProfile
                                    .VertexLayerStride];
                        RenderWorldVertexPacker.WritePositionRow(
                            sourceVertex,
                            expectedPosition);
                        RenderWorldVertexPacker.WriteVertexLayerRow(
                            sourceVertex,
                            expectedLayer);
                        int packedVertex =
                            surface.VertexRange.Start + localVertex;
                        int positionOffset = checked(
                            packedVertex *
                            RenderWorldStructuralProfile.PositionStride);
                        int layerOffset =
                            surface.VertexLayerByteRange.Start +
                            checked(
                                localVertex *
                                RenderWorldStructuralProfile
                                    .VertexLayerStride);
                        if (!Matches(
                                geometry.PackedPositionData,
                                positionOffset,
                                expectedPosition) ||
                            !Matches(
                                geometry.PackedVertexLayerData,
                                layerOffset,
                                expectedLayer))
                        {
                            Add(
                                issues,
                                RenderWorldStructuralIssueKind
                                    .CanonicalSourceVertexMismatch,
                                $"{path}.vertices[{localVertex}]",
                                "Packed position/layer rows do not exactly " +
                                "equal the canonical source vertex under " +
                                "the declared profile.");
                        }
                    }
                }

                bool triangleRangeValid =
                    surface.SourceTriangleRange.Start ==
                        sourceTriangleCursor &&
                    surface.SourceTriangleRange.Count ==
                        surface.TriangleCount &&
                    surface.SourceTriangleRange.EndExclusive <=
                        source.Triangles.Count &&
                    surface.IndexRange.EndExclusive <=
                        geometry.Indices.Count;
                if (!triangleRangeValid || !vertexMapShapeValid)
                {
                    Add(
                        issues,
                        RenderWorldStructuralIssueKind
                            .CanonicalSourceTriangleMismatch,
                        $"{path}.sourceTriangleRange",
                        "The immutable canonical triangle window is not " +
                        "contiguous or representable.");
                }
                else
                {
                    for (int localTriangle = 0;
                         localTriangle < surface.TriangleCount;
                         localTriangle++)
                    {
                        AuthoredIndexedRenderTriangle expected =
                            source.Triangles[
                                surface.SourceTriangleRange.Start +
                                localTriangle];
                        int indexOffset =
                            surface.IndexRange.Start +
                            localTriangle * 3;
                        ushort local0 = geometry.Indices[indexOffset];
                        ushort local1 = geometry.Indices[indexOffset + 1];
                        ushort local2 = geometry.Indices[indexOffset + 2];
                        bool localIndicesValid =
                            local0 <
                                surface.SourceVertexIndices.Count &&
                            local1 <
                                surface.SourceVertexIndices.Count &&
                            local2 <
                                surface.SourceVertexIndices.Count;
                        if (!localIndicesValid ||
                            surface.SourceVertexIndices[local0] !=
                                expected.Index0 ||
                            surface.SourceVertexIndices[local1] !=
                                expected.Index1 ||
                            surface.SourceVertexIndices[local2] !=
                                expected.Index2)
                        {
                            Add(
                                issues,
                                RenderWorldStructuralIssueKind
                                    .CanonicalSourceTriangleMismatch,
                                $"{path}.triangles[{localTriangle}]",
                                "The compiled local index triplet does not " +
                                "preserve the exact canonical source " +
                                "triangle sequence.");
                        }
                    }
                }

                sourceTriangleCursor =
                    surface.SourceTriangleRange.EndExclusive;
            }

            if (sourceTriangleCursor != source.Triangles.Count)
            {
                Add(
                    issues,
                    RenderWorldStructuralIssueKind
                        .CanonicalSourceTriangleMismatch,
                    $"sourceMappings[{mappingIndex}]",
                    "Surface windows do not cover every canonical source " +
                    "triangle exactly once and in order.");
            }
        }
    }

    private static void AssessOwnershipRanges(
        RenderWorldCompiledGeometry geometry,
        ICollection<RenderWorldStructuralIssue> issues)
    {
        RenderWorldCompiledSurface[] expectedWorldSurfaces =
            geometry.Surfaces
                .Where(value =>
                    value.OwnershipKind ==
                    RenderMeshOwnershipKind.StandaloneWorld)
                .ToArray();
        int expectedWorldCount = expectedWorldSurfaces.Length;
        if (geometry.StandaloneWorldSurfaceRange.Start != 0 ||
            geometry.StandaloneWorldSurfaceRange.Count !=
                expectedWorldCount ||
            expectedWorldCount > ushort.MaxValue ||
            expectedWorldSurfaces
                .Select((surface, index) => (surface, index))
                .Any(value =>
                    value.surface.Ordinal != value.index))
        {
            Add(
                issues,
                RenderWorldStructuralIssueKind
                    .StandaloneWorldRangeMismatch,
                "standaloneWorldSurfaceRange",
                "Standalone-world surfaces must form the exact model-zero " +
                "prefix.");
        }

        ushort[] expectedSortedWorldOrdinals =
            expectedWorldSurfaces
                .OrderBy(
                    value => value.SymbolicMaterialName,
                    StringComparer.Ordinal)
                .ThenBy(
                    value => StableKey(value.SourceObjectId),
                    StringComparer.Ordinal)
                .ThenBy(value => value.SourceSurfaceOrdinal)
                .Select(value => checked((ushort)value.Ordinal))
                .ToArray();
        if (!geometry.SortedWorldSurfaceOrdinals.SequenceEqual(
                expectedSortedWorldOrdinals))
        {
            Add(
                issues,
                RenderWorldStructuralIssueKind
                    .SortedWorldSurfaceOrdinalMismatch,
                "sortedWorldSurfaceOrdinals",
                "The structural world-surface ordinal list is not the " +
                "symbolic-material/source/window ordering.");
        }

        RenderWorldSourceSurfaceMapping[] worldMappings =
            geometry.SourceMappings
                .Where(value =>
                    value.OwnershipKind ==
                    RenderMeshOwnershipKind.StandaloneWorld)
                .ToArray();
        RenderWorldSourceBounds expectedWorldBounds =
            UnionBoundsOrLocalOrigin(worldMappings);
        if (geometry.WorldModel.ModelOrdinal != 0 ||
            geometry.WorldModel.SurfaceRange !=
                geometry.StandaloneWorldSurfaceRange ||
            geometry.WorldModel.SourceBounds != expectedWorldBounds ||
            geometry.WorldModel.LocalOriginRadius !=
                expectedWorldBounds.LocalOriginRadius)
        {
            Add(
                issues,
                RenderWorldStructuralIssueKind
                    .StandaloneWorldRangeMismatch,
                "worldModel",
                "The detached world GfxBrushModel row does not match model " +
                "ordinal zero, its surface prefix, or outward local-origin " +
                "bounds.");
        }

        CollisionInlineModelAllocation[] allocationRows =
            geometry.InlineModelAllocationPlan.Rows.ToArray();
        bool planShapeValid =
            allocationRows.Length ==
                geometry.InlineModels.Count + 1 &&
            allocationRows.Length != 0 &&
            allocationRows[0].OwnerKind ==
                CollisionInlineModelOwnerKind.World &&
            allocationRows[0].OwnerObjectId is null &&
            allocationRows[0].ModelOrdinal == 0 &&
            allocationRows
                .Skip(1)
                .All(value =>
                    value.OwnerKind ==
                        CollisionInlineModelOwnerKind
                            .MapEntityBrushModel &&
                    value.OwnerObjectId is not null);
        if (!planShapeValid)
        {
            Add(
                issues,
                RenderWorldStructuralIssueKind
                    .InlineModelAllocationMismatch,
                "inlineModelAllocationPlan",
                "The M3 render candidate requires world row zero followed " +
                "only by shared physical-order MapEnt allocations.");
        }

        int inlineCursor =
            geometry.StandaloneWorldSurfaceRange.EndExclusive;
        var seenInlineModels = new HashSet<MapObjectId>();
        for (int modelIndex = 0;
             modelIndex < geometry.InlineModels.Count;
             modelIndex++)
        {
            RenderWorldInlineModelSurfaceRange inlineModel =
                geometry.InlineModels[modelIndex];
            string key = StableKey(
                inlineModel.InlineBrushModelObjectId);
            CollisionInlineModelAllocation? allocation =
                modelIndex + 1 < allocationRows.Length
                    ? allocationRows[modelIndex + 1]
                    : null;
            bool rangeValid =
                inlineModel.SurfaceRange.Start == inlineCursor &&
                inlineModel.SurfaceRange.EndExclusive <=
                    geometry.Surfaces.Count &&
                inlineModel.SurfaceRange.Start <= ushort.MaxValue &&
                inlineModel.SurfaceRange.Count <= ushort.MaxValue &&
                seenInlineModels.Add(
                    inlineModel.InlineBrushModelObjectId) &&
                allocation is not null &&
                allocation.OwnerKind ==
                    CollisionInlineModelOwnerKind.MapEntityBrushModel &&
                allocation.OwnerObjectId ==
                    inlineModel.InlineBrushModelObjectId &&
                allocation.ModelOrdinal == inlineModel.ModelOrdinal;
            if (!rangeValid)
            {
                Add(
                    issues,
                    RenderWorldStructuralIssueKind
                        .InlineModelRangeMismatch,
                    $"inlineModels[{key}]",
                    "Inline models must follow the shared physical-order " +
                    "allocation, remain contiguous (empty is explicit), " +
                    "and be UInt16-representable.");
            }

            if (inlineModel.SurfaceRange.Start >= 0 &&
                inlineModel.SurfaceRange.EndExclusive <=
                    geometry.Surfaces.Count)
            {
                for (int surfaceIndex =
                         inlineModel.SurfaceRange.Start;
                     surfaceIndex <
                         inlineModel.SurfaceRange.EndExclusive;
                     surfaceIndex++)
                {
                    RenderWorldCompiledSurface surface =
                        geometry.Surfaces[surfaceIndex];
                    if (surface.OwnershipKind !=
                            RenderMeshOwnershipKind.InlineBrushModel ||
                        surface.InlineBrushModelObjectId !=
                            inlineModel.InlineBrushModelObjectId ||
                        surface.ModelOrdinal !=
                            inlineModel.ModelOrdinal)
                    {
                        Add(
                            issues,
                            RenderWorldStructuralIssueKind
                                .OwnershipMismatch,
                            $"surfaces[{surfaceIndex}]",
                            "The surface is outside its declared inline " +
                            "brush-model owner.");
                    }
                }
            }

            RenderWorldSourceSurfaceMapping[] expectedMappings =
                geometry.SourceMappings
                    .Where(value =>
                        value.InlineBrushModelObjectId ==
                        inlineModel.InlineBrushModelObjectId)
                    .OrderBy(
                        value => value.SymbolicMaterialName,
                        StringComparer.Ordinal)
                    .ThenBy(
                        value => StableKey(value.SourceObjectId),
                        StringComparer.Ordinal)
                    .ToArray();
            MapObjectId[] expectedSourceIds = expectedMappings
                .Select(value => value.SourceObjectId)
                .ToArray();
            if (!inlineModel.SourceObjectIds.SequenceEqual(
                    expectedSourceIds))
            {
                Add(
                    issues,
                    RenderWorldStructuralIssueKind
                        .InlineModelRangeMismatch,
                    $"inlineModels[{key}].sourceObjectIds",
                    "Inline-model source identities do not match source " +
                    "ownership.");
            }
            RenderWorldSourceBounds expectedBounds =
                UnionBoundsOrLocalOrigin(expectedMappings);
            if (inlineModel.SourceBounds != expectedBounds ||
                inlineModel.LocalOriginRadius !=
                    expectedBounds.LocalOriginRadius ||
                inlineModel.HasSurfaceGeometry !=
                    (expectedMappings.Length != 0))
            {
                Add(
                    issues,
                    RenderWorldStructuralIssueKind
                        .SourceBoundsMismatch,
                    $"inlineModels[{key}].sourceBounds",
                    "Inline-model source bounds/radius do not match the " +
                    "outward local-origin union of canonical mesh sources.");
            }

            inlineCursor = inlineModel.SurfaceRange.EndExclusive;
        }

        if (inlineCursor != geometry.Surfaces.Count)
        {
            Add(
                issues,
                RenderWorldStructuralIssueKind
                    .AggregateCoverageMismatch,
                "inlineModels",
                "World and inline-model ranges do not cover every surface.");
        }
    }

    private static void AssessBlockers(
        IReadOnlyList<RenderWorldStructuralBlocker> blockers,
        ICollection<RenderWorldStructuralIssue> issues)
    {
        var expected =
            new Dictionary<
                RenderWorldStructuralBlockerKind,
                RenderWorldDeferredMilestone>
            {
                [
                    RenderWorldStructuralBlockerKind
                        .CellsPortalsAabbAndVisibilityNotCompiled
                ] = RenderWorldDeferredMilestone.M4SpatialTopology,
                [
                    RenderWorldStructuralBlockerKind
                        .FinalRuntimeBoundsNotCompiled
                ] = RenderWorldDeferredMilestone.M4SpatialTopology,
                [
                    RenderWorldStructuralBlockerKind
                        .LightingAssignmentsNotCompiled
                ] = RenderWorldDeferredMilestone.M5Lighting,
                [
                    RenderWorldStructuralBlockerKind
                        .LightingBakesNotCompiled
                ] = RenderWorldDeferredMilestone.M5Lighting,
                [
                    RenderWorldStructuralBlockerKind
                        .MaterialResolutionNotCompiled
                ] = RenderWorldDeferredMilestone
                    .M7AssetResolutionAndPersistence,
                [
                    RenderWorldStructuralBlockerKind
                        .CompleteGfxWorldAssemblyNotCompiled
                ] = RenderWorldDeferredMilestone
                    .M7AssetResolutionAndPersistence,
                [
                    RenderWorldStructuralBlockerKind
                        .LinkingEmissionAndPersistenceNotAuthorized
                ] = RenderWorldDeferredMilestone
                    .M7AssetResolutionAndPersistence
            };

        bool valid =
            blockers.Count == expected.Count &&
            blockers
                .GroupBy(value => value.Kind)
                .All(group => group.Count() == 1) &&
            expected.All(pair =>
                blockers.Any(value =>
                    value.Kind == pair.Key &&
                    value.Milestone == pair.Value &&
                    !string.IsNullOrWhiteSpace(value.Detail)));
        if (!valid)
        {
            Add(
                issues,
                RenderWorldStructuralIssueKind
                    .DeferredBlockerContractMismatch,
                "blockers",
                "The M3 candidate must retain every explicit M4, M5, and " +
                "M7 authority blocker exactly once.");
        }
    }

    private static float ReadSingleBigEndian(
        IReadOnlyList<byte> source,
        int offset)
    {
        if (offset < 0 || offset + sizeof(float) > source.Count)
            return float.NaN;
        if (source is byte[] array)
        {
            return BinaryPrimitives.ReadSingleBigEndian(
                array.AsSpan(offset, sizeof(float)));
        }

        Span<byte> scratch = stackalloc byte[sizeof(float)];
        for (int index = 0; index < scratch.Length; index++)
            scratch[index] = source[offset + index];
        return BinaryPrimitives.ReadSingleBigEndian(scratch);
    }

    private static ushort? ExpectedModelOrdinal(
        AuthoredIndexedRenderMeshSource source,
        RenderWorldCompiledGeometry geometry)
    {
        if (source.Ownership.Kind ==
            RenderMeshOwnershipKind.StandaloneWorld)
        {
            return 0;
        }

        MapObjectId owner =
            source.Ownership.InlineBrushModelObjectId!.Value;
        CollisionInlineModelAllocation[] rows =
            geometry.InlineModelAllocationPlan.Rows
                .Where(value => value.OwnerObjectId == owner)
                .ToArray();
        return rows.Length == 1 ? rows[0].ModelOrdinal : null;
    }

    private static bool Matches(
        IReadOnlyList<byte> actual,
        int offset,
        ReadOnlySpan<byte> expected)
    {
        if (offset < 0 || offset + expected.Length > actual.Count)
            return false;
        for (int index = 0; index < expected.Length; index++)
        {
            if (actual[offset + index] != expected[index])
                return false;
        }
        return true;
    }

    private static RenderWorldSourceBounds UnionBoundsOrLocalOrigin(
        IReadOnlyList<RenderWorldSourceSurfaceMapping> mappings)
    {
        if (mappings.Count == 0)
            return RenderWorldSourceBounds.EmptyAtLocalOrigin;

        RenderWorldSourceBounds bounds = mappings[0].SourceBounds;
        for (int index = 1; index < mappings.Count; index++)
            bounds = bounds.Include(mappings[index].SourceBounds);
        return bounds;
    }

    private static void Add(
        ICollection<RenderWorldStructuralIssue> issues,
        RenderWorldStructuralIssueKind kind,
        string path,
        string detail) =>
        issues.Add(new RenderWorldStructuralIssue(kind, path, detail));

    private static string StableKey(MapObjectId value) =>
        value.Value.ToString("D");
}
