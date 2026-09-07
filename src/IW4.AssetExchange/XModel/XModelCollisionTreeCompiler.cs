using System.Numerics;
using IW4.Assets.Assets.XModel;
using IW4.Assets.Math;

namespace IW4.AssetExchange.XModel;

/// <summary>Builds the conservative native collision-tree form for imported rigid XSurfaces.</summary>
public static class XModelCollisionTreeCompiler
{
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
        Vector3 translation = -mins;
        var scale = new Vector3(Scale(delta.X), Scale(delta.Y), Scale(delta.Z));
        var pairs = new (ushort TriangleBeginIndex, Vector3 Mins, Vector3 Maxs, Vector3 Center)[(surface.TriCount + 1) / 2];
        for (int triangle = 0; triangle < surface.TriCount; triangle += 2)
        {
            bool pair = triangle + 1 < surface.TriCount;
            Vector3 pairMins = positions[surface.TriIndices[triangle * 3]], pairMaxs = pairMins;
            int end = (triangle + (pair ? 2 : 1)) * 3;
            for (int index = triangle * 3 + 1; index < end; index++)
            {
                Vector3 position = positions[surface.TriIndices[index]];
                pairMins = Vector3.Min(pairMins, position);
                pairMaxs = Vector3.Max(pairMaxs, position);
            }
            pairs[triangle / 2] = (
                checked((ushort)(triangle | (pair ? 0x8000 : 0))),
                pairMins,
                pairMaxs,
                pairMins * 0.5f + pairMaxs * 0.5f);
        }

        const int maxPairsPerNode = 8;
        // The native traversal has a 128-entry pending-span queue. This bound permits
        // at most 63 internal nodes and 64 terminal spans, even when every box is hit.
        const int maxDepth = 6;
        var spans = new List<(int Begin, int Count, int Depth)> { (0, pairs.Length, 0) };
        var nodes = new List<XSurfaceCollisionNode>();
        for (int nodeIndex = 0; nodeIndex < spans.Count; nodeIndex++)
        {
            (int begin, int count, int depth) = spans[nodeIndex];
            Vector3 nodeMins = pairs[begin].Mins, nodeMaxs = pairs[begin].Maxs;
            Vector3 centerMins = pairs[begin].Center, centerMaxs = centerMins;
            for (int index = begin + 1; index < begin + count; index++)
            {
                nodeMins = Vector3.Min(nodeMins, pairs[index].Mins);
                nodeMaxs = Vector3.Max(nodeMaxs, pairs[index].Maxs);
                centerMins = Vector3.Min(centerMins, pairs[index].Center);
                centerMaxs = Vector3.Max(centerMaxs, pairs[index].Center);
            }
            var bounds = new XSurfaceCollisionAabb(
                Quantize(nodeMins.X, translation.X, scale.X, upper: false),
                Quantize(nodeMins.Y, translation.Y, scale.Y, upper: false),
                Quantize(nodeMins.Z, translation.Z, scale.Z, upper: false),
                Quantize(nodeMaxs.X, translation.X, scale.X, upper: true),
                Quantize(nodeMaxs.Y, translation.Y, scale.Y, upper: true),
                Quantize(nodeMaxs.Z, translation.Z, scale.Z, upper: true));
            if (count <= maxPairsPerNode || depth == maxDepth)
            {
                nodes.Add(new XSurfaceCollisionNode(bounds, checked((ushort)begin), checked((ushort)(0x8000 | count))));
                continue;
            }

            Vector3 centerExtent = centerMaxs - centerMins;
            int axis = centerExtent.Y > centerExtent.X ? 1 : 0;
            if (centerExtent.Z > centerExtent[axis]) axis = 2;
            pairs.AsSpan(begin, count).Sort((left, right) =>
            {
                int order = left.Center[axis].CompareTo(right.Center[axis]);
                return order != 0 ? order : left.TriangleBeginIndex.CompareTo(right.TriangleBeginIndex);
            });
            int leftCount = count / 2;
            // Breadth-first spans keep each internal node's two children contiguous.
            int childBegin = spans.Count;
            spans.Add((begin, leftCount, depth + 1));
            spans.Add((begin + leftCount, count - leftCount, depth + 1));
            nodes.Add(new XSurfaceCollisionNode(bounds, checked((ushort)childBegin), 2));
        }
        XSurfaceCollisionLeaf[] leaves = Array.ConvertAll(pairs, pair => new XSurfaceCollisionLeaf(pair.TriangleBeginIndex));
        var tree = new XSurfaceCollisionTree
        {
            Trans = new Vec3 { X = translation.X, Y = translation.Y, Z = translation.Z },
            Scale = new Vec3 { X = scale.X, Y = scale.Y, Z = scale.Z },
            NodeCount = nodes.Count,
            Nodes = Array.AsReadOnly(nodes.ToArray()),
            LeafCount = leaves.Length,
            Leafs = Array.AsReadOnly(leaves)
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

    private static ushort Quantize(float value, float translation, float scale, bool upper)
    {
        if (!float.IsFinite(scale) || scale <= 0f)
            return upper ? ushort.MaxValue : (ushort)0;
        double quantized = ((double)value + translation) * scale;
        // Round outwards with one additional quantization unit for native float rounding.
        double bound = upper ? Math.Ceiling(quantized) + 1d : Math.Floor(quantized) - 1d;
        return (ushort)Math.Clamp(bound, 0d, ushort.MaxValue);
    }

    private static float Scale(float extent) => extent == 0f ? float.PositiveInfinity : ushort.MaxValue / extent;
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
