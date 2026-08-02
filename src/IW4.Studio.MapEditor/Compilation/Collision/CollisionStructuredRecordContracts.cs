using System.Collections.ObjectModel;
using IW4.Assets.Assets.ColMap;

namespace IW4.Studio.MapEditor.Compilation.Collision;

/// <summary>
/// Target selected by the signed Int16 child stored in an IW4 collision BSP
/// node. Non-negative values address the node table; negative values address
/// leaf <c>-1 - value</c>.
/// </summary>
public enum CollisionBspChildTargetKind
{
    Node = 0,
    Leaf = 1
}

/// <summary>
/// Decoded form of one <see cref="CNode.Children"/> value.
/// </summary>
public readonly record struct CollisionBspChildReference
{
    private CollisionBspChildReference(
        short serializedValue,
        CollisionBspChildTargetKind targetKind,
        int targetOrdinal)
    {
        SerializedValue = serializedValue;
        TargetKind = targetKind;
        TargetOrdinal = targetOrdinal;
    }

    public short SerializedValue { get; }
    public CollisionBspChildTargetKind TargetKind { get; }
    public int TargetOrdinal { get; }

    public static CollisionBspChildReference Decode(short serializedValue) =>
        serializedValue >= 0
            ? new(
                serializedValue,
                CollisionBspChildTargetKind.Node,
                serializedValue)
            : new(
                serializedValue,
                CollisionBspChildTargetKind.Leaf,
                -1 - serializedValue);

    public static CollisionBspChildReference Encode(
        CollisionBspChildTargetKind targetKind,
        int targetOrdinal)
    {
        if (!Enum.IsDefined(targetKind))
            throw new ArgumentOutOfRangeException(nameof(targetKind));
        if ((uint)targetOrdinal > short.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetOrdinal),
                "IW4 signed collision BSP children address at most 32,768 " +
                "nodes or leaves in their selected target domain.");
        }

        short serializedValue = targetKind switch
        {
            CollisionBspChildTargetKind.Node =>
                checked((short)targetOrdinal),
            CollisionBspChildTargetKind.Leaf =>
                checked((short)(-1 - targetOrdinal)),
            _ => throw new ArgumentOutOfRangeException(nameof(targetKind))
        };
        return new(serializedValue, targetKind, targetOrdinal);
    }

    public bool IsInsideTargetDomains(int nodeCount, int leafCount)
    {
        if (nodeCount < 0)
            throw new ArgumentOutOfRangeException(nameof(nodeCount));
        if (leafCount < 0)
            throw new ArgumentOutOfRangeException(nameof(leafCount));

        int targetCount = TargetKind switch
        {
            CollisionBspChildTargetKind.Node => nodeCount,
            CollisionBspChildTargetKind.Leaf => leafCount,
            _ => throw new InvalidOperationException(
                $"Unsupported BSP child target {TargetKind}.")
        };
        return TargetOrdinal < targetCount;
    }
}

/// <summary>
/// Proven local fields in one IW4 <see cref="CollisionPartition"/> row.
/// Triangle and border counts are byte-sized. FirstTriangle is a signed
/// Int32 ordinal into the aggregate triangle domain.
/// </summary>
public readonly record struct CollisionPartitionLocalContract
{
    public CollisionPartitionLocalContract(
        int firstTriangle,
        int triangleCount,
        int borderCount)
    {
        if (firstTriangle < 0)
            throw new ArgumentOutOfRangeException(nameof(firstTriangle));
        if ((uint)triangleCount > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(triangleCount),
                "IW4 CollisionPartition.triCount is an unsigned byte.");
        }
        if ((uint)borderCount > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(borderCount),
                "IW4 CollisionPartition.borderCount is an unsigned byte.");
        }

        FirstTriangle = firstTriangle;
        TriangleCount = checked((byte)triangleCount);
        BorderCount = checked((byte)borderCount);
        TriangleEndExclusive = checked(firstTriangle + triangleCount);
    }

    public int FirstTriangle { get; }
    public byte TriangleCount { get; }
    public byte BorderCount { get; }
    public int TriangleEndExclusive { get; }

    public bool IsInsideTriangleDomain(int triangleCount)
    {
        if (triangleCount < 0)
            throw new ArgumentOutOfRangeException(nameof(triangleCount));

        return TriangleEndExclusive <= triangleCount;
    }

    public bool HasExactBorderPayloadCount(int nestedBorderCount)
    {
        if (nestedBorderCount < 0)
            throw new ArgumentOutOfRangeException(nameof(nestedBorderCount));

        return BorderCount == nestedBorderCount;
    }
}

/// <summary>
/// IW4 triangle indices are not global vertex ordinals. A partition's
/// FirstVertSegment selects a 0x3000-byte vertex segment; with 0x0C-byte
/// Vec3 rows, each segment advances the effective base by 1,024 vertices.
/// </summary>
public readonly record struct CollisionPartitionVertexSegmentContract
{
    public const int SerializedSegmentByteStride = 0x3000;
    public const int SerializedVertexStride = 0x0C;
    public const int VertexOrdinalStride =
        SerializedSegmentByteStride / SerializedVertexStride;
    public const int MaximumEffectiveVertexOrdinal =
        byte.MaxValue * VertexOrdinalStride + ushort.MaxValue;
    public const int MaximumAddressableVertexCount =
        MaximumEffectiveVertexOrdinal + 1;

    public CollisionPartitionVertexSegmentContract(byte firstVertSegment)
    {
        FirstVertSegment = firstVertSegment;
        VertexBaseOrdinal =
            checked(firstVertSegment * VertexOrdinalStride);
    }

    public byte FirstVertSegment { get; }
    public int VertexBaseOrdinal { get; }

    public int ResolveVertexOrdinal(ushort partitionRelativeOrdinal) =>
        checked(VertexBaseOrdinal + partitionRelativeOrdinal);

    public bool IsInsideVertexDomain(
        ushort partitionRelativeOrdinal,
        int vertexCount)
    {
        if (vertexCount < 0)
            throw new ArgumentOutOfRangeException(nameof(vertexCount));

        return ResolveVertexOrdinal(partitionRelativeOrdinal) < vertexCount;
    }
}

public enum CollisionAabbPayloadTargetKind
{
    Partition = 0,
    ChildAabbRange = 1
}

/// <summary>
/// The AABB selector at row +0x1C is discriminated by ChildCount: zero
/// addresses one CollisionPartition; nonzero addresses a contiguous range of
/// CollisionAabbTree rows.
/// </summary>
public readonly record struct CollisionAabbPayloadReference
{
    public CollisionAabbPayloadReference(
        ushort childCount,
        int firstChildOrPartitionIndex)
    {
        ChildCount = childCount;
        FirstChildOrPartitionIndex = firstChildOrPartitionIndex;
        TargetKind = childCount == 0
            ? CollisionAabbPayloadTargetKind.Partition
            : CollisionAabbPayloadTargetKind.ChildAabbRange;
    }

    public ushort ChildCount { get; }
    public int FirstChildOrPartitionIndex { get; }
    public CollisionAabbPayloadTargetKind TargetKind { get; }

    public bool IsInsideTargetDomains(
        int aabbTreeCount,
        int partitionCount)
    {
        if (aabbTreeCount < 0)
            throw new ArgumentOutOfRangeException(nameof(aabbTreeCount));
        if (partitionCount < 0)
            throw new ArgumentOutOfRangeException(nameof(partitionCount));
        if (FirstChildOrPartitionIndex < 0)
            return false;

        return TargetKind switch
        {
            CollisionAabbPayloadTargetKind.Partition =>
                FirstChildOrPartitionIndex < partitionCount,
            CollisionAabbPayloadTargetKind.ChildAabbRange =>
                FirstChildOrPartitionIndex <=
                    aabbTreeCount - ChildCount,
            _ => throw new InvalidOperationException(
                $"Unsupported collision AABB target {TargetKind}.")
        };
    }
}

public enum CollisionStaticModelAabbTargetKind
{
    StaticModelRange = 0,
    ChildNodeRange = 1
}

/// <summary>
/// Static-model AABB FirstChild addresses one virtual ordinal space:
/// [0, NumStaticModels) contains model rows and ordinals at or above
/// NumStaticModels address child nodes after subtracting NumStaticModels.
/// </summary>
public readonly record struct CollisionStaticModelAabbChildRange
{
    public CollisionStaticModelAabbChildRange(
        ushort firstChild,
        ushort childCount,
        int staticModelCount)
    {
        if (staticModelCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(staticModelCount));
        }

        FirstChild = firstChild;
        ChildCount = childCount;
        TargetKind = firstChild < staticModelCount
            ? CollisionStaticModelAabbTargetKind.StaticModelRange
            : CollisionStaticModelAabbTargetKind.ChildNodeRange;
        TargetStartOrdinal = TargetKind switch
        {
            CollisionStaticModelAabbTargetKind.StaticModelRange =>
                firstChild,
            CollisionStaticModelAabbTargetKind.ChildNodeRange =>
                firstChild - staticModelCount,
            _ => throw new InvalidOperationException(
                $"Unsupported static-model AABB target {TargetKind}.")
        };
    }

    public ushort FirstChild { get; }
    public ushort ChildCount { get; }
    public CollisionStaticModelAabbTargetKind TargetKind { get; }
    public int TargetStartOrdinal { get; }
    public int TargetEndExclusive =>
        checked(TargetStartOrdinal + ChildCount);

    public bool IsInsideTargetDomains(
        int staticModelCount,
        int nodeCount)
    {
        if (staticModelCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(staticModelCount));
        }
        if (nodeCount < 0)
            throw new ArgumentOutOfRangeException(nameof(nodeCount));

        int targetCount = TargetKind switch
        {
            CollisionStaticModelAabbTargetKind.StaticModelRange =>
                staticModelCount,
            CollisionStaticModelAabbTargetKind.ChildNodeRange => nodeCount,
            _ => throw new InvalidOperationException(
                $"Unsupported static-model AABB target {TargetKind}.")
        };
        return TargetEndExclusive <= targetCount;
    }
}

/// <summary>
/// Signed selector stored in a leaf-brush-node row. Positive values select a
/// ushort brush-reference payload. Zero and negative values select the typed
/// in-row dist/range/two-relative-child-offset union. A negative value also
/// visits the immediately following node before the selected continuation.
/// </summary>
public readonly record struct CollisionLeafBrushPayloadSelector
{
    private CollisionLeafBrushPayloadSelector(short serializedValue) =>
        SerializedValue = serializedValue;

    public short SerializedValue { get; }
    public bool SelectsBrushReferences => SerializedValue > 0;
    public bool SelectsChildUnion => SerializedValue <= 0;
    public int BrushReferenceCount =>
        SelectsBrushReferences ? SerializedValue : 0;

    public static CollisionLeafBrushPayloadSelector Decode(
        short serializedValue) =>
        new(serializedValue);

    /// <summary>
    /// Creates only the proven positive brush-list arm. Child-union
    /// construction needs the still-open spatial partition algorithm.
    /// </summary>
    public static CollisionLeafBrushPayloadSelector ForBrushReferences(
        int brushReferenceCount)
    {
        if (brushReferenceCount <= 0 ||
            brushReferenceCount > short.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(brushReferenceCount),
                "An authored leaf-brush payload requires between 1 and " +
                "32,767 ushort brush references.");
        }

        return new(checked((short)brushReferenceCount));
    }
}

public enum CollisionStructuredRecordIssueKind
{
    BspChildPayloadShapeMismatch = 0,
    BspChildTargetOutOfRange = 1,
    LeafBrushPayloadShapeMismatch = 2,
    LeafBrushOrdinalOutOfRange = 3,
    PartitionTriangleRangeOutOfRange = 4,
    PartitionBorderPayloadShapeMismatch = 5,
    LeafBrushChildTargetOutOfRange = 6,
    LeafBrushImplicitChildOutOfRange = 7,
    LeafBrushRootOutOfRange = 8,
    PartitionTriangleIndexPayloadOutOfRange = 9,
    PartitionVertexReferenceOutOfRange = 10,
    LeafAabbRangeOutOfRange = 11,
    CollisionAabbMaterialOutOfRange = 12,
    CollisionAabbTargetOutOfRange = 13,
    StaticModelAabbTargetOutOfRange = 14,
    StaticModelAabbVirtualNamespaceOverflow = 15
}

public sealed record CollisionStructuredRecordIssue(
    CollisionStructuredRecordIssueKind Kind,
    string Path,
    string Detail);

/// <summary>
/// Read-only validation result for the consumer-proven local relationships.
/// It grants no emission or persistence authority.
/// </summary>
public sealed class CollisionStructuredRecordAssessment
{
    internal CollisionStructuredRecordAssessment(
        IEnumerable<CollisionStructuredRecordIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        Issues = new ReadOnlyCollection<CollisionStructuredRecordIssue>(
            issues.ToArray());
    }

    public IReadOnlyList<CollisionStructuredRecordIssue> Issues { get; }
    public bool IsValid => Issues.Count == 0;
}

/// <summary>
/// Validates only local ColMap relationships established by the IW4 loader,
/// emitter shape, and audited IW4 Xbox/PS3 MP consumers:
/// <c>CM_PointLeafnum</c> at 0x823523D0,
/// <c>CM_PointContentsLeafBrushNode_r</c> at 0x82352670,
/// <c>CM_TraceThroughAabbTree_Hit</c> at 0x823500A0, and the corresponding
/// default_mp collision traversal paths. These contracts validate retained
/// topology; they do not construct planes, BSPs, partitions, or trees.
/// </summary>
public static class CollisionStructuredRecordValidator
{
    public static CollisionStructuredRecordAssessment Assess(
        ClipMapAsset definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var issues = new List<CollisionStructuredRecordIssue>();

        AssessBspChildren(definition, issues);
        AssessLeafBrushReferences(definition, issues);
        AssessPartitions(definition, issues);
        AssessLeafAabbRanges(definition, issues);
        AssessCollisionAabbTrees(definition, issues);
        AssessStaticModelAabbTree(definition, issues);

        return new CollisionStructuredRecordAssessment(issues);
    }

    private static void AssessBspChildren(
        ClipMapAsset definition,
        ICollection<CollisionStructuredRecordIssue> issues)
    {
        int nodeCount = Math.Max(definition.NumNodes, 0);
        int leafCount = Math.Max(definition.NumLeafs, 0);
        for (int nodeIndex = 0;
             nodeIndex < definition.Nodes.Count;
             nodeIndex++)
        {
            CNode node = definition.Nodes[nodeIndex];
            if (node.Children.Count != 2)
            {
                issues.Add(new CollisionStructuredRecordIssue(
                    CollisionStructuredRecordIssueKind
                        .BspChildPayloadShapeMismatch,
                    $"nodes[{nodeIndex}].children",
                    "An IW4 collision BSP node requires exactly two signed " +
                    $"Int16 children, but the row contains " +
                    $"{node.Children.Count}."));
            }
            for (int childIndex = 0;
                 childIndex < node.Children.Count;
                 childIndex++)
            {
                CollisionBspChildReference child =
                    CollisionBspChildReference.Decode(
                        node.Children[childIndex]);
                if (child.IsInsideTargetDomains(nodeCount, leafCount))
                    continue;

                string targetName =
                    child.TargetKind == CollisionBspChildTargetKind.Node
                        ? "node"
                        : "leaf";
                int targetCount =
                    child.TargetKind == CollisionBspChildTargetKind.Node
                        ? nodeCount
                        : leafCount;
                issues.Add(new CollisionStructuredRecordIssue(
                    CollisionStructuredRecordIssueKind
                        .BspChildTargetOutOfRange,
                    $"nodes[{nodeIndex}].children[{childIndex}]",
                    $"Signed child value {child.SerializedValue} decodes to " +
                    $"{targetName} ordinal {child.TargetOrdinal}, outside " +
                    $"the {targetCount}-row target domain."));
            }
        }
    }

    private static void AssessLeafBrushReferences(
        ClipMapAsset definition,
        ICollection<CollisionStructuredRecordIssue> issues)
    {
        int brushCount = definition.NumBrushes;
        for (int index = 0;
             index < definition.LeafBrushes.Count;
             index++)
        {
            AssessLeafBrushOrdinal(
                definition.LeafBrushes[index],
                brushCount,
                $"leafBrushes[{index}]",
                issues);
        }

        for (int nodeIndex = 0;
             nodeIndex < definition.LeafBrushNodes.Count;
             nodeIndex++)
        {
            CLeafBrushNode node = definition.LeafBrushNodes[nodeIndex];
            CollisionLeafBrushPayloadSelector selector =
                CollisionLeafBrushPayloadSelector.Decode(
                    node.LeafBrushCount);
            if (!selector.SelectsBrushReferences)
            {
                if (node.Data.Children is not { } children ||
                    children.ChildOffsets.Count != 2 ||
                    node.Data.Brushes.Count != 0 ||
                    node.Data.LeafUnionPad.Count != 0)
                {
                    issues.Add(new CollisionStructuredRecordIssue(
                        CollisionStructuredRecordIssueKind
                            .LeafBrushPayloadShapeMismatch,
                        $"leafBrushNodes[{nodeIndex}].children",
                        "A non-positive signed leaf-brush count selects the " +
                        "12-byte in-row union: float dist, float range, and " +
                        "exactly two relative ushort child offsets, with no " +
                        "brush-list arm."));
                    continue;
                }

                continue;
            }

            if (node.Data.Children is not null ||
                node.Data.LeafUnionPad.Count != 8)
            {
                issues.Add(new CollisionStructuredRecordIssue(
                    CollisionStructuredRecordIssueKind
                        .LeafBrushPayloadShapeMismatch,
                    $"leafBrushNodes[{nodeIndex}].brushes",
                    "A positive signed leaf-brush count selects only the " +
                    "brush-list arm and requires its exact eight preserved " +
                    "union-tail bytes."));
            }
            if (node.Data.Brushes.Count !=
                selector.BrushReferenceCount)
            {
                issues.Add(new CollisionStructuredRecordIssue(
                    CollisionStructuredRecordIssueKind
                        .LeafBrushPayloadShapeMismatch,
                    $"leafBrushNodes[{nodeIndex}].brushes",
                    $"Positive signed count {selector.SerializedValue} " +
                    $"requires exactly {selector.BrushReferenceCount} " +
                    $"ushort brush references, but the payload contains " +
                    $"{node.Data.Brushes.Count}."));
            }

            for (int referenceIndex = 0;
                 referenceIndex < node.Data.Brushes.Count;
                 referenceIndex++)
            {
                AssessLeafBrushOrdinal(
                    node.Data.Brushes[referenceIndex],
                    brushCount,
                    $"leafBrushNodes[{nodeIndex}].brushes[{referenceIndex}]",
                    issues);
            }
        }

        var roots = new List<(int Ordinal, string Path)>();
        for (int leafIndex = 0;
             leafIndex < definition.Leafs.Count;
             leafIndex++)
        {
            CLeaf leaf = definition.Leafs[leafIndex];
            if (leaf.BrushContents != 0)
            {
                roots.Add((
                    leaf.LeafBrushNode,
                    $"leafs[{leafIndex}].leafBrushNode"));
            }
        }
        for (int modelIndex = 0;
             modelIndex < definition.CModels.Count;
             modelIndex++)
        {
            CLeaf leaf = definition.CModels[modelIndex].Leaf;
            if (leaf.BrushContents != 0)
            {
                roots.Add((
                    leaf.LeafBrushNode,
                    $"cmodels[{modelIndex}].leaf.leafBrushNode"));
            }
        }

        foreach ((int rootOrdinal, string path) in roots)
        {
            AssessReachableLeafBrushGraph(
                definition.LeafBrushNodes,
                rootOrdinal,
                path,
                issues);
        }
    }

    private static void AssessReachableLeafBrushGraph(
        IReadOnlyList<CLeafBrushNode> nodes,
        int rootOrdinal,
        string path,
        ICollection<CollisionStructuredRecordIssue> issues)
    {
        // Retail row zero is an empty, zero-filled dummy and is not traversed.
        if (rootOrdinal == 0)
            return;
        if (rootOrdinal < 0 || rootOrdinal >= nodes.Count)
        {
            issues.Add(new CollisionStructuredRecordIssue(
                CollisionStructuredRecordIssueKind.LeafBrushRootOutOfRange,
                path,
                $"Leaf-brush root ordinal {rootOrdinal} is neither the zero " +
                $"dummy nor a row inside the {nodes.Count}-row node domain."));
            return;
        }

        var pending = new Stack<int>();
        var visited = new HashSet<int>();
        pending.Push(rootOrdinal);
        while (pending.TryPop(out int nodeIndex))
        {
            if (!visited.Add(nodeIndex))
                continue;

            CLeafBrushNode node = nodes[nodeIndex];
            if (node.LeafBrushCount > 0)
                continue;
            if (node.Data.Children is not { ChildOffsets.Count: 2 } children)
                continue;

            if (node.LeafBrushCount < 0)
            {
                int implicitTarget = nodeIndex + 1;
                if (implicitTarget >= nodes.Count)
                {
                    issues.Add(new CollisionStructuredRecordIssue(
                        CollisionStructuredRecordIssueKind
                            .LeafBrushImplicitChildOutOfRange,
                        $"leafBrushNodes[{nodeIndex}].leafBrushCount",
                        "A reachable negative selector implicitly visits the " +
                        "immediately following row, but no such row exists."));
                }
                else
                {
                    pending.Push(implicitTarget);
                }
            }

            for (int childIndex = 0;
                 childIndex < children.ChildOffsets.Count;
                 childIndex++)
            {
                ushort relativeOffset = children.ChildOffsets[childIndex];
                long target = (long)nodeIndex + relativeOffset;
                if (relativeOffset != 0 &&
                    target > nodeIndex &&
                    target < nodes.Count)
                {
                    pending.Push((int)target);
                    continue;
                }

                issues.Add(new CollisionStructuredRecordIssue(
                    CollisionStructuredRecordIssueKind
                        .LeafBrushChildTargetOutOfRange,
                    $"leafBrushNodes[{nodeIndex}].children" +
                    $".childOffsets[{childIndex}]",
                    $"Reachable relative child offset {relativeOffset} " +
                    $"resolves to row {target}; consumer-relevant edges must " +
                    $"be strictly forward and inside the {nodes.Count}-row " +
                    "domain."));
            }
        }
    }

    private static void AssessLeafBrushOrdinal(
        ushort ordinal,
        int brushCount,
        string path,
        ICollection<CollisionStructuredRecordIssue> issues)
    {
        if (ordinal < brushCount)
            return;

        issues.Add(new CollisionStructuredRecordIssue(
            CollisionStructuredRecordIssueKind
                .LeafBrushOrdinalOutOfRange,
            path,
            $"Brush ordinal {ordinal} is outside the {brushCount}-row " +
            "Brush domain."));
    }

    private static void AssessPartitions(
        ClipMapAsset definition,
        ICollection<CollisionStructuredRecordIssue> issues)
    {
        int triangleCount = Math.Max(definition.TriCount, 0);
        for (int partitionIndex = 0;
             partitionIndex < definition.Partitions.Count;
             partitionIndex++)
        {
            CollisionPartition partition =
                definition.Partitions[partitionIndex];
            if (partition.FirstTri < 0 ||
                partition.FirstTri > int.MaxValue - partition.TriCount)
            {
                issues.Add(new CollisionStructuredRecordIssue(
                    CollisionStructuredRecordIssueKind
                        .PartitionTriangleRangeOutOfRange,
                    $"partitions[{partitionIndex}].firstTri",
                    $"Signed first-triangle ordinal {partition.FirstTri} " +
                    "cannot form a non-negative Int32 triangle range."));
                AssessPartitionBorders(
                    partition,
                    partitionIndex,
                    issues);
                continue;
            }

            var contract = new CollisionPartitionLocalContract(
                partition.FirstTri,
                partition.TriCount,
                partition.BorderCount);
            if (!contract.IsInsideTriangleDomain(triangleCount))
            {
                issues.Add(new CollisionStructuredRecordIssue(
                    CollisionStructuredRecordIssueKind
                        .PartitionTriangleRangeOutOfRange,
                    $"partitions[{partitionIndex}].firstTri",
                    $"Triangle range [{contract.FirstTriangle}, " +
                    $"{contract.TriangleEndExclusive}) is outside the " +
                    $"{triangleCount}-triangle domain."));
            }
            else
            {
                AssessPartitionVertexReferences(
                    definition,
                    partition,
                    partitionIndex,
                    contract,
                    issues);
            }
            AssessPartitionBorders(
                partition,
                partitionIndex,
                issues);
        }
    }

    private static void AssessPartitionVertexReferences(
        ClipMapAsset definition,
        CollisionPartition partition,
        int partitionIndex,
        CollisionPartitionLocalContract triangleRange,
        ICollection<CollisionStructuredRecordIssue> issues)
    {
        var segment =
            new CollisionPartitionVertexSegmentContract(
                partition.FirstVertSegment);
        int vertexCount = Math.Max(definition.VertCount, 0);
        for (int triangleOrdinal = triangleRange.FirstTriangle;
             triangleOrdinal < triangleRange.TriangleEndExclusive;
             triangleOrdinal++)
        {
            long firstElement = (long)triangleOrdinal * 3;
            if (firstElement + 3 > definition.TriIndices.Count)
            {
                issues.Add(new CollisionStructuredRecordIssue(
                    CollisionStructuredRecordIssueKind
                        .PartitionTriangleIndexPayloadOutOfRange,
                    $"partitions[{partitionIndex}].firstTri",
                    $"Triangle {triangleOrdinal} requires index elements " +
                    $"[{firstElement}, {firstElement + 3}), outside the " +
                    $"{definition.TriIndices.Count}-element payload."));
                return;
            }

            for (int corner = 0; corner < 3; corner++)
            {
                int elementOrdinal = checked((int)firstElement + corner);
                ushort localOrdinal =
                    definition.TriIndices[elementOrdinal];
                int effectiveOrdinal =
                    segment.ResolveVertexOrdinal(localOrdinal);
                if (effectiveOrdinal < vertexCount)
                    continue;

                issues.Add(new CollisionStructuredRecordIssue(
                    CollisionStructuredRecordIssueKind
                        .PartitionVertexReferenceOutOfRange,
                    $"partitions[{partitionIndex}].triangles" +
                    $"[{triangleOrdinal}].indices[{corner}]",
                    $"Segment {partition.FirstVertSegment} selects vertex " +
                    $"base {segment.VertexBaseOrdinal}; local ushort " +
                    $"{localOrdinal} resolves to {effectiveOrdinal}, outside " +
                    $"the {vertexCount}-row vertex domain."));
            }
        }
    }

    private static void AssessPartitionBorders(
        CollisionPartition partition,
        int partitionIndex,
        ICollection<CollisionStructuredRecordIssue> issues)
    {
        if (partition.BorderCount == partition.Borders.Count)
            return;

        issues.Add(new CollisionStructuredRecordIssue(
            CollisionStructuredRecordIssueKind
                .PartitionBorderPayloadShapeMismatch,
            $"partitions[{partitionIndex}].borders",
            $"Unsigned-byte border count {partition.BorderCount} requires " +
            $"exactly that many nested border rows, but the payload " +
            $"contains {partition.Borders.Count}."));
    }

    private static void AssessLeafAabbRanges(
        ClipMapAsset definition,
        ICollection<CollisionStructuredRecordIssue> issues)
    {
        int aabbTreeCount = Math.Max(definition.AabbTreeCount, 0);
        for (int leafIndex = 0;
             leafIndex < definition.Leafs.Count;
             leafIndex++)
        {
            AssessLeafAabbRange(
                definition.Leafs[leafIndex],
                aabbTreeCount,
                $"leafs[{leafIndex}]",
                issues);
        }
        for (int modelIndex = 0;
             modelIndex < definition.CModels.Count;
             modelIndex++)
        {
            AssessLeafAabbRange(
                definition.CModels[modelIndex].Leaf,
                aabbTreeCount,
                $"cmodels[{modelIndex}].leaf",
                issues);
        }
    }

    private static void AssessLeafAabbRange(
        CLeaf leaf,
        int aabbTreeCount,
        string path,
        ICollection<CollisionStructuredRecordIssue> issues)
    {
        // The consumer exits before reading FirstCollAabbIndex when count is
        // zero. Retail maps commonly retain a nonzero opaque first value.
        if (leaf.CollAabbCount == 0)
            return;

        int end = leaf.FirstCollAabbIndex + leaf.CollAabbCount;
        if (end <= aabbTreeCount)
            return;

        issues.Add(new CollisionStructuredRecordIssue(
            CollisionStructuredRecordIssueKind.LeafAabbRangeOutOfRange,
            $"{path}.firstCollAabbIndex",
            $"Collision AABB range [{leaf.FirstCollAabbIndex}, {end}) is " +
            $"outside the {aabbTreeCount}-row AABB domain."));
    }

    private static void AssessCollisionAabbTrees(
        ClipMapAsset definition,
        ICollection<CollisionStructuredRecordIssue> issues)
    {
        int materialCount = Math.Max(definition.NumMaterials, 0);
        int aabbTreeCount = Math.Max(definition.AabbTreeCount, 0);
        int partitionCount = Math.Max(definition.PartitionCount, 0);
        for (int nodeIndex = 0;
             nodeIndex < definition.AabbTrees.Count;
             nodeIndex++)
        {
            CollisionAabbTree node = definition.AabbTrees[nodeIndex];
            if (node.MaterialIndex >= materialCount)
            {
                issues.Add(new CollisionStructuredRecordIssue(
                    CollisionStructuredRecordIssueKind
                        .CollisionAabbMaterialOutOfRange,
                    $"aabbTrees[{nodeIndex}].materialIndex",
                    $"Material ordinal {node.MaterialIndex} is outside the " +
                    $"{materialCount}-row collision-material domain."));
            }

            var reference = new CollisionAabbPayloadReference(
                node.ChildCount,
                node.FirstChildOrPartitionIndex);
            if (reference.IsInsideTargetDomains(
                    aabbTreeCount,
                    partitionCount))
            {
                continue;
            }

            string target = reference.TargetKind switch
            {
                CollisionAabbPayloadTargetKind.Partition =>
                    "partition ordinal",
                CollisionAabbPayloadTargetKind.ChildAabbRange =>
                    "child AABB range",
                _ => "target"
            };
            issues.Add(new CollisionStructuredRecordIssue(
                CollisionStructuredRecordIssueKind
                    .CollisionAabbTargetOutOfRange,
                $"aabbTrees[{nodeIndex}].firstChildOrPartitionIndex",
                $"Selector {node.FirstChildOrPartitionIndex} with child " +
                $"count {node.ChildCount} forms an invalid {target}."));
        }
    }

    private static void AssessStaticModelAabbTree(
        ClipMapAsset definition,
        ICollection<CollisionStructuredRecordIssue> issues)
    {
        int staticModelCount = Math.Max(definition.NumStaticModels, 0);
        int nodeCount = definition.SModelNodes.Count;
        try
        {
            CollisionCompositeOrdinalNamespaceContracts
                .ValidateStaticModelAabbVirtualNamespace(
                    staticModelCount,
                    nodeCount);
        }
        catch (OverflowException exception)
        {
            issues.Add(new CollisionStructuredRecordIssue(
                CollisionStructuredRecordIssueKind
                    .StaticModelAabbVirtualNamespaceOverflow,
                "smodelNodes",
                exception.Message));
        }

        for (int nodeIndex = 0;
             nodeIndex < nodeCount;
             nodeIndex++)
        {
            SModelAabbNode node = definition.SModelNodes[nodeIndex];
            // The consumer does not dereference FirstChild for an empty row.
            if (node.ChildCount == 0)
                continue;

            var range = new CollisionStaticModelAabbChildRange(
                node.FirstChild,
                node.ChildCount,
                staticModelCount);
            if (range.IsInsideTargetDomains(
                    staticModelCount,
                    nodeCount))
            {
                continue;
            }

            string target = range.TargetKind switch
            {
                CollisionStaticModelAabbTargetKind.StaticModelRange =>
                    "static-model",
                CollisionStaticModelAabbTargetKind.ChildNodeRange =>
                    "child-node",
                _ => "unknown"
            };
            issues.Add(new CollisionStructuredRecordIssue(
                CollisionStructuredRecordIssueKind
                    .StaticModelAabbTargetOutOfRange,
                $"smodelNodes[{nodeIndex}].firstChild",
                $"Virtual range [{node.FirstChild}, " +
                $"{node.FirstChild + node.ChildCount}) selects an invalid " +
                $"{target} range for {staticModelCount} static models and " +
                $"{nodeCount} AABB nodes."));
        }
    }
}
