using System.Collections.ObjectModel;
using IW4.Assets.Assets.ColMap;

namespace IW4.Studio.MapEditor.Compilation.Collision;

/// <summary>
/// Graph-level defects in a detached compiled ColMap candidate. Local record
/// shape and range defects remain owned by
/// <see cref="CollisionStructuredRecordValidator"/>.
/// </summary>
public enum CollisionStructuralReachabilityIssueKind
{
    MissingBspRoot = 0,
    BspChildUnavailableForTraversal = 1,
    BspCycle = 2,
    UnreachableBspNodes = 3,
    UnreachableLeaves = 4,
    LeafAabbRootRangeUnavailableForTraversal = 5,
    CollisionAabbTargetUnavailableForTraversal = 6,
    CollisionAabbCycle = 7,
    UnreachableCollisionAabbNodes = 8,
    UnreachablePartitions = 9,
    MissingStaticModelAabbRoot = 10,
    StaticModelAabbTargetUnavailableForTraversal = 11,
    StaticModelAabbCycle = 12,
    UnreachableStaticModelAabbNodes = 13,
    UnreachableStaticModels = 14,
    MissingReservedLeafZero = 15,
    ReservedLeafZeroPayloadMismatch = 16,
    ReservedLeafZeroReferenced = 17
}

public sealed record CollisionStructuralReachabilityIssue(
    CollisionStructuralReachabilityIssueKind Kind,
    string Path,
    string Detail);

/// <summary>
/// Immutable detached-candidate assessment. A valid result proves both the
/// existing local structured-record contracts and graph reachability; it does
/// not grant runtime acceptance, emission, linking, or persistence authority.
/// </summary>
public sealed class CollisionStructuralReachabilityAssessment
{
    internal CollisionStructuralReachabilityAssessment(
        CollisionStructuredRecordAssessment localRecordAssessment,
        IEnumerable<CollisionStructuralReachabilityIssue> issues)
    {
        LocalRecordAssessment = localRecordAssessment ??
            throw new ArgumentNullException(
                nameof(localRecordAssessment));
        ArgumentNullException.ThrowIfNull(issues);
        Issues =
            new ReadOnlyCollection<CollisionStructuralReachabilityIssue>(
                issues.ToArray());
    }

    public CollisionStructuredRecordAssessment LocalRecordAssessment
    {
        get;
    }

    public IReadOnlyList<CollisionStructuralReachabilityIssue> Issues
    {
        get;
    }

    public bool IsValid =>
        LocalRecordAssessment.IsValid &&
        Issues.Count == 0;
}

/// <summary>
/// Validates graph reachability for an in-memory ColMap compiler candidate.
/// The local validator is composed once and remains the sole authority for
/// serialized row shapes and declared-domain ranges.
/// </summary>
public static class CollisionStructuralReachabilityValidator
{
    private const int MaximumReportedOrdinals = 32;

    public static CollisionStructuralReachabilityAssessment Assess(
        ClipMapAsset definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        CollisionStructuredRecordAssessment localAssessment =
            CollisionStructuredRecordValidator.Assess(definition);
        var localIssuePaths = localAssessment.Issues
            .Select(issue => issue.Path)
            .ToHashSet(StringComparer.Ordinal);
        var issues =
            new List<CollisionStructuralReachabilityIssue>();

        AssessBsp(definition, localIssuePaths, issues);
        AssessCollisionAabbGraph(
            definition,
            localIssuePaths,
            issues);
        AssessStaticModelAabbGraph(
            definition,
            localIssuePaths,
            issues);

        return new CollisionStructuralReachabilityAssessment(
            localAssessment,
            issues);
    }

    private static void AssessBsp(
        ClipMapAsset definition,
        IReadOnlySet<string> localIssuePaths,
        ICollection<CollisionStructuralReachabilityIssue> issues)
    {
        int nodeCount = definition.Nodes.Count;
        int leafCount = definition.Leafs.Count;
        var nodeState = new TraversalState[nodeCount];
        var reachableLeaves = new bool[leafCount];

        if (leafCount == 0)
        {
            issues.Add(new CollisionStructuralReachabilityIssue(
                CollisionStructuralReachabilityIssueKind
                    .MissingReservedLeafZero,
                "leafs",
                "Native collision BSP topology requires an all-zero " +
                "reserved leaf row 0."));
        }
        else if (!IsZeroLeaf(definition.Leafs[0]))
        {
            issues.Add(new CollisionStructuralReachabilityIssue(
                CollisionStructuralReachabilityIssueKind
                    .ReservedLeafZeroPayloadMismatch,
                "leafs[0]",
                "Reserved collision leaf row 0 must be exactly " +
                "zero-valued."));
        }

        if (nodeCount == 0)
        {
            issues.Add(new CollisionStructuralReachabilityIssue(
                CollisionStructuralReachabilityIssueKind.MissingBspRoot,
                "nodes",
                "A compiled collision BSP requires node row 0 as its " +
                "traversal root."));
        }
        else
        {
            nodeState[0] = TraversalState.Active;
            var pending = new Stack<TraversalFrame>();
            pending.Push(new TraversalFrame(0, 0));
            while (pending.TryPop(out TraversalFrame frame))
            {
                CNode node = definition.Nodes[frame.Ordinal];
                int traversableChildCount =
                    Math.Min(node.Children.Count, 2);
                if (frame.NextChild >= traversableChildCount)
                {
                    nodeState[frame.Ordinal] =
                        TraversalState.Complete;
                    continue;
                }

                pending.Push(frame.Advance());
                int childIndex = frame.NextChild;
                string path =
                    $"nodes[{frame.Ordinal}].children[{childIndex}]";
                CollisionBspChildReference child =
                    CollisionBspChildReference.Decode(
                        node.Children[childIndex]);
                if (child.TargetKind ==
                    CollisionBspChildTargetKind.Leaf)
                {
                    if ((uint)child.TargetOrdinal <
                        (uint)leafCount)
                    {
                        if (child.TargetOrdinal == 0)
                        {
                            issues.Add(
                                new CollisionStructuralReachabilityIssue(
                                    CollisionStructuralReachabilityIssueKind
                                        .ReservedLeafZeroReferenced,
                                    path,
                                    "Native BSP nodes must not target the " +
                                    "reserved leaf row 0."));
                        }
                        else
                        {
                            reachableLeaves[child.TargetOrdinal] = true;
                        }
                    }
                    else
                    {
                        AddUnavailableWhenNotLocallyReported(
                            localIssuePaths,
                            issues,
                            path,
                            CollisionStructuralReachabilityIssueKind
                                .BspChildUnavailableForTraversal,
                            $"BSP child leaf ordinal " +
                            $"{child.TargetOrdinal} has no loaded row.");
                    }
                    continue;
                }

                if ((uint)child.TargetOrdinal >=
                    (uint)nodeCount)
                {
                    AddUnavailableWhenNotLocallyReported(
                        localIssuePaths,
                        issues,
                        path,
                        CollisionStructuralReachabilityIssueKind
                            .BspChildUnavailableForTraversal,
                        $"BSP child node ordinal " +
                        $"{child.TargetOrdinal} has no loaded row.");
                    continue;
                }

                TraversalState childState =
                    nodeState[child.TargetOrdinal];
                if (childState == TraversalState.Active)
                {
                    issues.Add(
                        new CollisionStructuralReachabilityIssue(
                            CollisionStructuralReachabilityIssueKind
                                .BspCycle,
                            path,
                            $"The edge from BSP node {frame.Ordinal} to " +
                            $"active ancestor node {child.TargetOrdinal} " +
                            "forms a cycle."));
                    continue;
                }
                if (childState == TraversalState.Complete)
                    continue;

                nodeState[child.TargetOrdinal] =
                    TraversalState.Active;
                pending.Push(
                    new TraversalFrame(
                        child.TargetOrdinal,
                        0));
            }
        }

        CollisionStructuralReachabilityValidator.AddUnreachableIssue(
            issues,
            nodeState
                .Select((state, ordinal) => (state, ordinal))
                .Where(value =>
                    value.state == TraversalState.Unvisited)
                .Select(value => value.ordinal),
            CollisionStructuralReachabilityIssueKind
                .UnreachableBspNodes,
            "nodes",
            "BSP node",
            "BSP node 0");
        CollisionStructuralReachabilityValidator.AddUnreachableIssue(
            issues,
            reachableLeaves
                .Select((isReachable, ordinal) =>
                    (isReachable, ordinal))
                .Where(value =>
                    value.ordinal != 0 &&
                    !value.isReachable)
                .Select(value => value.ordinal),
            CollisionStructuralReachabilityIssueKind.UnreachableLeaves,
            "leafs",
            "collision leaf",
            "BSP node 0");
    }

    private static bool IsZeroLeaf(CLeaf leaf) =>
        leaf.FirstCollAabbIndex == 0 &&
        leaf.CollAabbCount == 0 &&
        leaf.BrushContents == 0 &&
        leaf.TerrainContents == 0 &&
        IsPositiveZero(leaf.Mins.X) &&
        IsPositiveZero(leaf.Mins.Y) &&
        IsPositiveZero(leaf.Mins.Z) &&
        IsPositiveZero(leaf.Maxs.X) &&
        IsPositiveZero(leaf.Maxs.Y) &&
        IsPositiveZero(leaf.Maxs.Z) &&
        leaf.LeafBrushNode == 0;

    private static bool IsPositiveZero(float value) =>
        BitConverter.SingleToInt32Bits(value) == 0;

    private static void AssessCollisionAabbGraph(
        ClipMapAsset definition,
        IReadOnlySet<string> localIssuePaths,
        ICollection<CollisionStructuralReachabilityIssue> issues)
    {
        int nodeCount = definition.AabbTrees.Count;
        int partitionCount = definition.Partitions.Count;
        var nodeState = new TraversalState[nodeCount];
        var reachablePartitions = new bool[partitionCount];
        var roots = new List<(int Ordinal, string Path)>();

        for (int leafIndex = 0;
             leafIndex < definition.Leafs.Count;
             leafIndex++)
        {
            AddLeafAabbRoots(
                definition.Leafs[leafIndex],
                $"leafs[{leafIndex}]",
                nodeCount,
                localIssuePaths,
                issues,
                roots);
        }
        for (int modelIndex = 0;
             modelIndex < definition.CModels.Count;
             modelIndex++)
        {
            AddLeafAabbRoots(
                definition.CModels[modelIndex].Leaf,
                $"cmodels[{modelIndex}].leaf",
                nodeCount,
                localIssuePaths,
                issues,
                roots);
        }

        foreach ((int rootOrdinal, _) in roots)
        {
            VisitCollisionAabbRoot(
                rootOrdinal,
                definition,
                localIssuePaths,
                issues,
                nodeState,
                reachablePartitions);
        }

        AddUnreachableIssue(
            issues,
            nodeState
                .Select((state, ordinal) => (state, ordinal))
                .Where(value =>
                    value.state == TraversalState.Unvisited)
                .Select(value => value.ordinal),
            CollisionStructuralReachabilityIssueKind
                .UnreachableCollisionAabbNodes,
            "aabbTrees",
            "collision AABB node",
            "a collision leaf or cmodel leaf");
        AddUnreachableIssue(
            issues,
            reachablePartitions
                .Select((isReachable, ordinal) =>
                    (isReachable, ordinal))
                .Where(value => !value.isReachable)
                .Select(value => value.ordinal),
            CollisionStructuralReachabilityIssueKind
                .UnreachablePartitions,
            "partitions",
            "collision partition",
            "a collision leaf or cmodel leaf");
    }

    private static void AddLeafAabbRoots(
        CLeaf leaf,
        string leafPath,
        int nodeCount,
        IReadOnlySet<string> localIssuePaths,
        ICollection<CollisionStructuralReachabilityIssue> issues,
        ICollection<(int Ordinal, string Path)> roots)
    {
        if (leaf.CollAabbCount == 0)
            return;

        int first = leaf.FirstCollAabbIndex;
        int end = first + leaf.CollAabbCount;
        string path = $"{leafPath}.firstCollAabbIndex";
        if (end > nodeCount)
        {
            AddUnavailableWhenNotLocallyReported(
                localIssuePaths,
                issues,
                path,
                CollisionStructuralReachabilityIssueKind
                    .LeafAabbRootRangeUnavailableForTraversal,
                $"Collision AABB root range [{first}, {end}) exceeds " +
                $"the {nodeCount}-row loaded domain.");
            return;
        }

        for (int rootOrdinal = first;
             rootOrdinal < end;
             rootOrdinal++)
        {
            roots.Add((rootOrdinal, path));
        }
    }

    private static void VisitCollisionAabbRoot(
        int rootOrdinal,
        ClipMapAsset definition,
        IReadOnlySet<string> localIssuePaths,
        ICollection<CollisionStructuralReachabilityIssue> issues,
        TraversalState[] nodeState,
        bool[] reachablePartitions)
    {
        if (nodeState[rootOrdinal] != TraversalState.Unvisited)
            return;

        nodeState[rootOrdinal] = TraversalState.Active;
        var pending = new Stack<TraversalFrame>();
        pending.Push(new TraversalFrame(rootOrdinal, 0));
        while (pending.TryPop(out TraversalFrame frame))
        {
            CollisionAabbTree node =
                definition.AabbTrees[frame.Ordinal];
            var target = new CollisionAabbPayloadReference(
                node.ChildCount,
                node.FirstChildOrPartitionIndex);
            string path =
                $"aabbTrees[{frame.Ordinal}]" +
                ".firstChildOrPartitionIndex";
            if (!target.IsInsideTargetDomains(
                    definition.AabbTrees.Count,
                    definition.Partitions.Count))
            {
                AddUnavailableWhenNotLocallyReported(
                    localIssuePaths,
                    issues,
                    path,
                    CollisionStructuralReachabilityIssueKind
                        .CollisionAabbTargetUnavailableForTraversal,
                    $"The AABB selector cannot be traversed against the " +
                    $"{definition.AabbTrees.Count}-row node and " +
                    $"{definition.Partitions.Count}-row partition " +
                    "payloads.");
                nodeState[frame.Ordinal] =
                    TraversalState.Complete;
                continue;
            }

            if (target.TargetKind ==
                CollisionAabbPayloadTargetKind.Partition)
            {
                reachablePartitions[
                    target.FirstChildOrPartitionIndex] = true;
                nodeState[frame.Ordinal] =
                    TraversalState.Complete;
                continue;
            }

            if (frame.NextChild >= target.ChildCount)
            {
                nodeState[frame.Ordinal] =
                    TraversalState.Complete;
                continue;
            }

            pending.Push(frame.Advance());
            int childOrdinal =
                target.FirstChildOrPartitionIndex +
                frame.NextChild;
            TraversalState childState = nodeState[childOrdinal];
            if (childState == TraversalState.Active)
            {
                issues.Add(new CollisionStructuralReachabilityIssue(
                    CollisionStructuralReachabilityIssueKind
                        .CollisionAabbCycle,
                    path,
                    $"The edge from collision AABB node " +
                    $"{frame.Ordinal} to active ancestor node " +
                    $"{childOrdinal} forms a cycle."));
                continue;
            }
            if (childState == TraversalState.Complete)
                continue;

            nodeState[childOrdinal] = TraversalState.Active;
            pending.Push(new TraversalFrame(childOrdinal, 0));
        }
    }

    private static void AssessStaticModelAabbGraph(
        ClipMapAsset definition,
        IReadOnlySet<string> localIssuePaths,
        ICollection<CollisionStructuralReachabilityIssue> issues)
    {
        int nodeCount = definition.SModelNodes.Count;
        int staticModelCount = definition.StaticModelList.Count;
        var nodeState = new TraversalState[nodeCount];
        var reachableStaticModels = new bool[staticModelCount];

        if (nodeCount == 0)
        {
            issues.Add(
                new CollisionStructuralReachabilityIssue(
                    CollisionStructuralReachabilityIssueKind
                        .MissingStaticModelAabbRoot,
                    "smodelNodes",
                    "Native static-model traversal requires " +
                    "SModelAabbNode row 0 even when the static-model " +
                    "domain is empty."));
        }
        else
        {
            VisitStaticModelAabbRoot(
                definition,
                localIssuePaths,
                issues,
                nodeState,
                reachableStaticModels);
        }

        AddUnreachableIssue(
            issues,
            nodeState
                .Select((state, ordinal) => (state, ordinal))
                .Where(value =>
                    value.state == TraversalState.Unvisited)
                .Select(value => value.ordinal),
            CollisionStructuralReachabilityIssueKind
                .UnreachableStaticModelAabbNodes,
            "smodelNodes",
            "static-model AABB node",
            "static-model AABB node 0");
        AddUnreachableIssue(
            issues,
            reachableStaticModels
                .Select((isReachable, ordinal) =>
                    (isReachable, ordinal))
                .Where(value => !value.isReachable)
                .Select(value => value.ordinal),
            CollisionStructuralReachabilityIssueKind
                .UnreachableStaticModels,
            "staticModelList",
            "static-model row",
            "static-model AABB node 0");
    }

    private static void VisitStaticModelAabbRoot(
        ClipMapAsset definition,
        IReadOnlySet<string> localIssuePaths,
        ICollection<CollisionStructuralReachabilityIssue> issues,
        TraversalState[] nodeState,
        bool[] reachableStaticModels)
    {
        nodeState[0] = TraversalState.Active;
        var pending = new Stack<TraversalFrame>();
        pending.Push(new TraversalFrame(0, 0));
        while (pending.TryPop(out TraversalFrame frame))
        {
            SModelAabbNode node =
                definition.SModelNodes[frame.Ordinal];
            if (node.ChildCount == 0)
            {
                nodeState[frame.Ordinal] =
                    TraversalState.Complete;
                continue;
            }

            int declaredStaticModelCount =
                Math.Max(definition.NumStaticModels, 0);
            var target = new CollisionStaticModelAabbChildRange(
                node.FirstChild,
                node.ChildCount,
                declaredStaticModelCount);
            string path =
                $"smodelNodes[{frame.Ordinal}].firstChild";
            bool targetExists = target.TargetKind switch
            {
                CollisionStaticModelAabbTargetKind
                    .StaticModelRange =>
                    target.TargetEndExclusive <=
                    definition.StaticModelList.Count,
                CollisionStaticModelAabbTargetKind.ChildNodeRange =>
                    target.TargetEndExclusive <=
                    definition.SModelNodes.Count,
                _ => false
            };
            if (!targetExists)
            {
                AddUnavailableWhenNotLocallyReported(
                    localIssuePaths,
                    issues,
                    path,
                    CollisionStructuralReachabilityIssueKind
                        .StaticModelAabbTargetUnavailableForTraversal,
                    $"The static-model AABB selector cannot be traversed " +
                    $"against the {definition.StaticModelList.Count}-row " +
                    $"model and {definition.SModelNodes.Count}-row node " +
                    "payloads.");
                nodeState[frame.Ordinal] =
                    TraversalState.Complete;
                continue;
            }

            if (target.TargetKind ==
                CollisionStaticModelAabbTargetKind.StaticModelRange)
            {
                for (int modelOrdinal =
                         target.TargetStartOrdinal;
                     modelOrdinal <
                         target.TargetEndExclusive;
                     modelOrdinal++)
                {
                    reachableStaticModels[modelOrdinal] = true;
                }
                nodeState[frame.Ordinal] =
                    TraversalState.Complete;
                continue;
            }

            if (frame.NextChild >= target.ChildCount)
            {
                nodeState[frame.Ordinal] =
                    TraversalState.Complete;
                continue;
            }

            pending.Push(frame.Advance());
            int childOrdinal =
                target.TargetStartOrdinal + frame.NextChild;
            TraversalState childState = nodeState[childOrdinal];
            if (childState == TraversalState.Active)
            {
                issues.Add(new CollisionStructuralReachabilityIssue(
                    CollisionStructuralReachabilityIssueKind
                        .StaticModelAabbCycle,
                    path,
                    $"The edge from static-model AABB node " +
                    $"{frame.Ordinal} to active ancestor node " +
                    $"{childOrdinal} forms a cycle."));
                continue;
            }
            if (childState == TraversalState.Complete)
                continue;

            nodeState[childOrdinal] = TraversalState.Active;
            pending.Push(new TraversalFrame(childOrdinal, 0));
        }
    }

    private static void AddUnavailableWhenNotLocallyReported(
        IReadOnlySet<string> localIssuePaths,
        ICollection<CollisionStructuralReachabilityIssue> issues,
        string path,
        CollisionStructuralReachabilityIssueKind kind,
        string detail)
    {
        if (localIssuePaths.Contains(path))
            return;

        issues.Add(new CollisionStructuralReachabilityIssue(
            kind,
            path,
            detail));
    }

    private static void AddUnreachableIssue(
        ICollection<CollisionStructuralReachabilityIssue> issues,
        IEnumerable<int> ordinals,
        CollisionStructuralReachabilityIssueKind kind,
        string path,
        string domainName,
        string rootName)
    {
        int[] values = ordinals.ToArray();
        if (values.Length == 0)
            return;

        string displayedOrdinals = string.Join(
            ", ",
            values.Take(MaximumReportedOrdinals));
        string omitted = values.Length > MaximumReportedOrdinals
            ? $", … ({values.Length - MaximumReportedOrdinals} more)"
            : string.Empty;
        issues.Add(new CollisionStructuralReachabilityIssue(
            kind,
            path,
            $"{values.Length} {domainName} " +
            $"{(values.Length == 1 ? "is" : "are")} unreachable from " +
            $"{rootName}. Ordinals: {displayedOrdinals}{omitted}."));
    }

    private enum TraversalState : byte
    {
        Unvisited = 0,
        Active = 1,
        Complete = 2
    }

    private readonly record struct TraversalFrame(
        int Ordinal,
        int NextChild)
    {
        public TraversalFrame Advance() =>
            this with { NextChild = NextChild + 1 };
    }
}
