using IW4.Assets.Math;

namespace IW4.Assets.Assets.XModel;

/// <summary>Validates a materialized native XSurface collision tree.</summary>
public static class XModelCollisionTreeValidator
{
    public static bool TryValidate(
        XSurfaceCollisionTree tree,
        XRigidVertList rigid,
        XSurface surface,
        string fieldPath,
        out string? blocker)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(rigid);
        ArgumentNullException.ThrowIfNull(surface);
        blocker = null;
        if (tree.NodeCount != tree.Nodes.Count || tree.LeafCount != tree.Leafs.Count ||
            tree.NodeCount == 0 || !Finite(tree.Trans) ||
            !ValidScale(tree.Scale.X) || !ValidScale(tree.Scale.Y) || !ValidScale(tree.Scale.Z))
        {
            blocker = $"{fieldPath}: collision-tree counts or transform are invalid.";
            return false;
        }
        var visitedNodes = new bool[tree.NodeCount];
        var visiting = new bool[tree.NodeCount];
        var visitedLeafs = new bool[tree.LeafCount];
        string? failure = null;
        if (!VisitNode(0))
        {
            blocker = failure;
            return false;
        }
        if (visitedNodes.Any(value => !value) || visitedLeafs.Any(value => !value))
        {
            blocker = $"{fieldPath}: collision tree contains unreachable nodes or leaves.";
            return false;
        }
        return true;

        bool VisitNode(int nodeIndex)
        {
            if (visiting[nodeIndex])
            {
                failure = $"{fieldPath}: collision tree contains a node cycle.";
                return false;
            }
            if (visitedNodes[nodeIndex]) return true;
            visiting[nodeIndex] = true;
            XSurfaceCollisionNode node = tree.Nodes[nodeIndex];
            if (node.Aabb.MinsX > node.Aabb.MaxsX || node.Aabb.MinsY > node.Aabb.MaxsY || node.Aabb.MinsZ > node.Aabb.MaxsZ)
            {
                failure = $"{fieldPath}: collision node {nodeIndex} has inverted AABB bounds.";
                return false;
            }
            bool targetsLeafs = (node.ChildCount & 0x8000) != 0;
            int count = node.ChildCount & 0x7fff;
            int available = targetsLeafs ? tree.LeafCount : tree.NodeCount;
            if (count == 0 || (int)node.ChildBeginIndex + count > available)
            {
                failure = $"{fieldPath}: collision node {nodeIndex} has an invalid child span.";
                return false;
            }
            for (int child = node.ChildBeginIndex; child < node.ChildBeginIndex + count; child++)
            {
                if (targetsLeafs)
                {
                    visitedLeafs[child] = true;
                    ushort encoded = tree.Leafs[child].TriangleBeginIndex;
                    int triangle = encoded & 0x7fff;
                    int triangleCount = (encoded & 0x8000) != 0 ? 2 : 1;
                    if (triangle < rigid.TriOffset ||
                        triangle + triangleCount > rigid.TriOffset + rigid.TriCount ||
                        triangle + triangleCount > surface.TriCount)
                    {
                        failure = $"{fieldPath}: collision leaf {child} lies outside its rigid triangle range.";
                        return false;
                    }
                }
                else if (!VisitNode(child)) return false;
            }
            visiting[nodeIndex] = false;
            visitedNodes[nodeIndex] = true;
            return true;
        }
    }

    private static bool ValidScale(float value) =>
        float.IsPositiveInfinity(value) || float.IsFinite(value);

    private static bool Finite(Vec3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
