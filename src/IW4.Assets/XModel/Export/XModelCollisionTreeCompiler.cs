using System.Numerics;
using IW4.Assets.Assets.XModel;
using IW4.Assets.Math;

namespace IW4.Assets.XModel.Export;

/// <summary>Builds the conservative native collision-tree form for imported rigid XSurfaces.</summary>
public static class XModelCollisionTreeCompiler
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

    public static bool TryAttach(
        XSurface surface,
        string fieldPath,
        out XSurface result,
        out string? blocker)
    {
        ArgumentNullException.ThrowIfNull(surface);
        result = surface;
        blocker = null;
        if (surface.TriCount == 0 || surface.TriCount > 0x8000)
        {
            blocker = $"{fieldPath}: collision surface triangle count must be in [1, 32768].";
            return false;
        }
        if (surface.VertListCount != 1 || surface.VertList.Count != 1)
        {
            blocker = $"{fieldPath}: collision surfaces must be exactly rigid to one bone.";
            return false;
        }
        XRigidVertList rigid = surface.VertList[0];
        if (rigid.VertCount != surface.VertCount || rigid.TriOffset != 0 || rigid.TriCount != surface.TriCount)
        {
            blocker = $"{fieldPath}: the sole rigid list must cover every emitted vertex and triangle.";
            return false;
        }
        if (surface.TriIndices.Count != surface.TriCount * 3)
        {
            blocker = $"{fieldPath}: collision topology has an incomplete triangle index stream.";
            return false;
        }

        var positions = new Vector3[surface.VertCount];
        Vector3 mins = default, maxs = default;
        for (int index = 0; index < positions.Length; index++)
        {
            if (!XSurfaceVertexCodec.TryReadPosition(surface.Verts0, index, out Vector3 position) || !Finite(position))
            {
                blocker = $"{fieldPath}: collision vertex {index} has an invalid position.";
                return false;
            }
            positions[index] = position;
            if (index == 0) mins = maxs = position;
            else { mins = Vector3.Min(mins, position); maxs = Vector3.Max(maxs, position); }
        }
        for (int triangle = 0; triangle < surface.TriCount; triangle++)
        {
            int offset = triangle * 3;
            int a = surface.TriIndices[offset], b = surface.TriIndices[offset + 1], c = surface.TriIndices[offset + 2];
            if ((uint)a >= (uint)positions.Length || (uint)b >= (uint)positions.Length || (uint)c >= (uint)positions.Length ||
                a == b || b == c || c == a ||
                !Finite(Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a])) ||
                Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]).LengthSquared() <= 0.0000000001f)
            {
                blocker = $"{fieldPath}: collision triangle {triangle} is invalid or degenerate.";
                return false;
            }
        }

        Vector3 delta = maxs - mins;
        var leaves = new List<XSurfaceCollisionLeaf>((surface.TriCount + 1) / 2);
        for (int triangle = 0; triangle < surface.TriCount; triangle += 2)
        {
            bool pair = triangle + 1 < surface.TriCount;
            leaves.Add(new XSurfaceCollisionLeaf(checked((ushort)(triangle | (pair ? 0x8000 : 0)))));
        }
        var tree = new XSurfaceCollisionTree
        {
            Trans = new Vec3 { X = -mins.X, Y = -mins.Y, Z = -mins.Z },
            Scale = new Vec3 { X = Scale(delta.X), Y = Scale(delta.Y), Z = Scale(delta.Z) },
            NodeCount = 1,
            Nodes = [new XSurfaceCollisionNode(
                new XSurfaceCollisionAabb(0, 0, 0, ushort.MaxValue, ushort.MaxValue, ushort.MaxValue),
                0,
                checked((ushort)(0x8000 | leaves.Count)))],
            LeafCount = leaves.Count,
            Leafs = Array.AsReadOnly(leaves.ToArray())
        };
        result = new XSurface
        {
            TileMode = surface.TileMode, DeformedRaw = surface.DeformedRaw, StreamFlags = surface.StreamFlags, Pad03 = surface.Pad03,
            VertCount = surface.VertCount, TriCount = surface.TriCount, TriIndicesPointer = surface.TriIndicesPointer,
            TriIndices = surface.TriIndices, VertexInfo = surface.VertexInfo, Verts0Pointer = surface.Verts0Pointer,
            Verts0 = surface.Verts0, Vb0 = surface.Vb0, Verts1Pointer = surface.Verts1Pointer, Verts1 = surface.Verts1,
            Vb1 = surface.Vb1, VertListCount = 1, VertListPointer = surface.VertListPointer,
            VertList = [new XRigidVertList { BoneOffset = rigid.BoneOffset, VertCount = rigid.VertCount, TriOffset = rigid.TriOffset, TriCount = rigid.TriCount, CollisionTree = tree }],
            IndexBuffer = surface.IndexBuffer, PartBits = surface.PartBits
        };
        return true;
    }

    private static float Scale(float extent) => extent == 0f ? float.PositiveInfinity : ushort.MaxValue / extent;
    private static bool ValidScale(float value) => float.IsPositiveInfinity(value) || float.IsFinite(value);
    private static bool Finite(Vec3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
