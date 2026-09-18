using System.Numerics;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.Physics;
using Iw4Radiant.MapSource;
using static Iw4Radiant.Compilation.BrushCollisionCompiler;
using Vec3 = IW4.Assets.Math.Vec3;

namespace Iw4Radiant.Compilation;

internal static class TerrainCollisionCompiler
{
    internal static ClipMapAsset Append(ClipMapAsset source, IReadOnlyList<MapTerrain> terrains)
    {
        if (terrains.Count == 0)
            return source;
        if (source.NumNodes != 1 || source.NumLeafs != 3 || source.NumSubModels != 1 || source.TriCount != 0)
            throw new InvalidOperationException("Terrain compilation requires the single-cell brush collision graph.");

        // A material name can have brush-content variants. MapCompiler orders
        // the base row first, which is the row used by solid terrain.
        var materialIndices = source.Materials.Select((material, index) => (material.Name, Index: index))
            .GroupBy(item => item.Name ?? throw new InvalidDataException("A collision material has no name."), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => checked((ushort)group.First().Index), StringComparer.Ordinal);
        var vertices = new List<Vector3>();
        var vertexIndices = new Dictionary<Vector3, ushort>();
        var indices = new List<ushort>();
        var normals = new List<Vector3>();
        var triangleMaterials = new List<ushort>();
        var edges = new Dictionary<(ushort, ushort), List<(int Triangle, int Edge)>>();
        Vector3 worldMin = new(float.PositiveInfinity), worldMax = new(float.NegativeInfinity);
        foreach (var bounds in source.BrushBounds)
        {
            Vector3 midpoint = ToVector3(bounds.MidPoint), halfSize = ToVector3(bounds.HalfSize);
            worldMin = Vector3.Min(worldMin, midpoint - halfSize);
            worldMax = Vector3.Max(worldMax, midpoint + halfSize);
        }

        int terrainContents = 0;
        foreach (MapTerrain terrain in terrains)
        {
            if (terrain.IsCurve)
                throw new NotSupportedException("Curve collision is not supported by compilation.");
            if (terrain.Width < 2 || terrain.Height < 2 ||
                terrain.Vertices.Length != checked(terrain.Width * terrain.Height) ||
                terrain.EdgeFlags.Length != terrain.Vertices.Length)
                throw new InvalidDataException($"Terrain '{terrain.Material}' has an invalid mesh grid.");
            if (!materialIndices.TryGetValue(terrain.Material, out ushort materialIndex))
                throw new InvalidDataException($"Terrain collision material '{terrain.Material}' is missing.");
            terrainContents |= source.Materials[materialIndex].Contents;
            var localIndices = new ushort[terrain.Vertices.Length];
            for (int i = 0; i < localIndices.Length; i++)
            {
                Vector3 position = terrain.Vertices[i];
                if (!IsFinite(position))
                    throw new InvalidDataException($"Terrain '{terrain.Material}' has a nonfinite collision vertex.");
                if (!vertexIndices.TryGetValue(position, out ushort index))
                {
                    // PS3 adds firstVertSegment * 4096 to each ushort index. The
                    // current BSP codec authors segment zero, so retain its range.
                    if (vertices.Count > ushort.MaxValue)
                        throw new NotSupportedException("Terrain collision supports at most 65536 unique vertices.");
                    index = checked((ushort)vertices.Count);
                    vertexIndices.Add(position, index);
                    vertices.Add(position);
                    worldMin = Vector3.Min(worldMin, position);
                    worldMax = Vector3.Max(worldMax, position);
                }
                localIndices[i] = index;
            }

            foreach ((int a, int b, int c) in terrain.GetTriangles())
            {
                int triangle = normals.Count;
                // CM_TraceCapsuleThroughTriangle computes cross(v2-v0,v1-v0).
                // Reverse only the winding, retaining the authored grid diagonal.
                indices.Add(localIndices[a]);
                indices.Add(localIndices[c]);
                indices.Add(localIndices[b]);
                Vector3 normal = Vector3.Cross(terrain.Vertices[b] - terrain.Vertices[a],
                    terrain.Vertices[c] - terrain.Vertices[a]);
                float length = normal.Length();
                if (!float.IsFinite(length) || length == 0)
                    throw new InvalidDataException($"Terrain '{terrain.Material}' has a degenerate collision triangle.");
                normals.Add(normal / length);
                triangleMaterials.Add(materialIndex);
                for (int edge = 0; edge < 3; edge++)
                {
                    (ushort first, ushort second) = GetEdge(indices, triangle, edge);
                    var key = first < second ? (first, second) : (second, first);
                    if (!edges.TryGetValue(key, out var owners))
                        edges.Add(key, owners = []);
                    owners.Add((triangle, edge));
                    if (owners.Count > 2)
                        throw new InvalidDataException("Solid terrain meshes share a nonmanifold edge. Remove overlapping collision surfaces.");
                    if (owners.Count == 2 && GetEdge(indices, owners[0].Triangle, owners[0].Edge).First == first)
                        throw new InvalidDataException("Solid terrain meshes share an inconsistently wound edge. Align their front faces or mark an overlay nonColliding.");
                }
            }
        }

        // Native edge bits are indexed opposite the corresponding triangle vertex
        // and packed least-significant bit first, padded to a 32-bit word.
        var walkable = new byte[checked(((indices.Count + 31) >> 5) << 2)];
        var triangleBorders = new Dictionary<int, List<CollisionBorder>>();
        foreach (var owners in edges.Values)
        {
            // The official PS3 collision graph marks both owners when either
            // incident triangle meets the engine's 0.7 walkable-normal threshold.
            if (owners.Any(owner => normals[owner.Triangle].Z >= 0.7f))
                foreach ((int triangle, int edge) in owners)
                {
                    int bit = triangle * 3 + edge;
                    walkable[bit >> 3] |= (byte)(1 << (bit & 7));
                }
            var owner = owners[0];
            Vector3 normal = normals[owner.Triangle];
            (ushort first, ushort second) = GetEdge(indices, owner.Triangle, owner.Edge);
            Vector3 start = vertices[first], end = vertices[second];
            Vector3 delta = end - start;
            float horizontalLength = new Vector2(delta.X, delta.Y).Length();
            if (horizontalLength == 0)
                continue;
            Vector2 outward = new(-delta.Y / horizontalLength, delta.X / horizontalLength);
            if (owners.Count == 2)
            {
                Vector3 other = normals[owners[1].Triangle];
                if (normal.Z * other.Z > 0)
                    continue;
                // Only a convex vertical silhouette needs the capsule's cylindrical
                // side test. Shared grid diagonals and ordinary slope seams do not.
                Vector3 silhouette = normal * MathF.Abs(other.Z) + other * MathF.Abs(normal.Z);
                if (normal.Z == 0 && other.Z == 0)
                    continue;
                if (normal.Z < 0 || normal.Z == 0 && other.Z > 0)
                    outward = -outward;
                if (Vector2.Dot(outward, new Vector2(silhouette.X, silhouette.Y)) <= 0)
                    continue;
            }
            else if (normal.Z < 0 || normal.Z == 0 && Vector2.Dot(outward, new Vector2(normal.X, normal.Y)) < 0)
                outward = -outward;

            if (Vector2.Dot(new Vector2(delta.X, delta.Y), new Vector2(outward.Y, -outward.X)) < 0)
                (start, end) = (end, start);
            var border = new CollisionBorder
            {
                DistEq = [outward.X, outward.Y, outward.X * start.X + outward.Y * start.Y],
                ZBase = start.Z,
                ZSlope = (end.Z - start.Z) / horizontalLength,
                Start = outward.Y * start.X - outward.X * start.Y,
                Length = horizontalLength
            };
            if (!triangleBorders.TryGetValue(owner.Triangle, out var borders))
                triangleBorders.Add(owner.Triangle, borders = []);
            borders.Add(border);
        }

        var allBorders = new List<CollisionBorder>();
        var partitions = new List<CollisionPartition>();
        var trees = new List<CollisionAabbTree>();
        for (int firstTriangle = 0; firstTriangle < normals.Count;)
        {
            ushort material = triangleMaterials[firstTriangle];
            int count = 1;
            // At most three owned borders per triangle; both native counts are bytes.
            while (count < byte.MaxValue / 3 && firstTriangle + count < normals.Count &&
                triangleMaterials[firstTriangle + count] == material)
                count++;
            var borders = new List<CollisionBorder>();
            Vector3 min = new(float.PositiveInfinity), max = new(float.NegativeInfinity);
            for (int triangle = firstTriangle; triangle < firstTriangle + count; triangle++)
            {
                if (triangleBorders.TryGetValue(triangle, out var ownedBorders))
                    borders.AddRange(ownedBorders);
                for (int vertex = 0; vertex < 3; vertex++)
                {
                    Vector3 position = vertices[indices[triangle * 3 + vertex]];
                    min = Vector3.Min(min, position);
                    max = Vector3.Max(max, position);
                }
            }
            if (trees.Count == ushort.MaxValue)
                throw new NotSupportedException("Terrain collision exceeds the single leaf's 65535 AABB limit.");
            var bounds = MakeBounds(min - new Vector3(0.125f), max + new Vector3(0.125f));
            trees.Add(new CollisionAabbTree
            {
                Origin = bounds.MidPoint,
                HalfSize = bounds.HalfSize,
                MaterialIndex = material,
                FirstChildOrPartitionIndex = partitions.Count
            });
            partitions.Add(new CollisionPartition
            {
                FirstTri = firstTriangle,
                TriCount = checked((byte)count),
                BorderCount = checked((byte)borders.Count),
                Borders = borders.ToArray()
            });
            allBorders.AddRange(borders);
            firstTriangle += count;
        }

        float splitDistance = MathF.BitIncrement(worldMax.X);
        if (!float.IsFinite(splitDistance))
            throw new InvalidDataException("The terrain bounds leave no finite position for the collision root plane.");
        CPlane oldPlane = source.Nodes[0].Plane ?? throw new InvalidDataException("The collision root has no plane.");
        CPlane rootPlane = MakePlane(Vector3.UnitX, splitDistance);
        CPlane[] planes = source.Planes.Select(plane => ReferenceEquals(plane, oldPlane) ? rootPlane : plane).ToArray();
        var leafBounds = MakeBounds(worldMin - new Vector3(0.125f), worldMax + new Vector3(0.125f));
        CLeaf occupied = source.Leafs[1];
        var modelBounds = MakeBounds(worldMin - Vector3.One, worldMax + Vector3.One);
        Vector3 modelExtent = Vector3.Max(Vector3.Abs(worldMin - Vector3.One), Vector3.Abs(worldMax + Vector3.One));
        return new ClipMapAsset
        {
            Name = source.Name,
            SerializedType = source.SerializedType,
            PlaneCount = planes.Length,
            Planes = planes,
            NumMaterials = source.NumMaterials,
            Materials = source.Materials,
            NumBrushSides = source.NumBrushSides,
            BrushSides = source.BrushSides,
            NumBrushEdges = source.NumBrushEdges,
            BrushEdges = source.BrushEdges,
            NumNodes = 1,
            Nodes = [new CNode { Plane = rootPlane, Children = source.Nodes[0].Children }],
            NumLeafs = 3,
            Leafs = [source.Leafs[0], new CLeaf
            {
                CollAabbCount = checked((ushort)trees.Count),
                BrushContents = occupied.BrushContents,
                TerrainContents = terrainContents,
                Mins = leafBounds.MidPoint,
                Maxs = leafBounds.HalfSize,
                LeafBrushNode = occupied.LeafBrushNode
            }, source.Leafs[2]],
            LeafBrushNodesCount = source.LeafBrushNodesCount,
            LeafBrushNodes = source.LeafBrushNodes,
            NumLeafBrushes = source.NumLeafBrushes,
            LeafBrushes = source.LeafBrushes,
            NumLeafSurfaces = source.NumLeafSurfaces,
            LeafSurfaces = source.LeafSurfaces,
            VertCount = vertices.Count,
            Verts = vertices.Select(ToVec3).ToArray(),
            TriCount = normals.Count,
            TriIndices = indices.ToArray(),
            TriEdgeIsWalkable = walkable,
            BorderCount = allBorders.Count,
            Borders = allBorders.ToArray(),
            PartitionCount = partitions.Count,
            Partitions = partitions.ToArray(),
            AabbTreeCount = trees.Count,
            AabbTrees = trees.ToArray(),
            NumSubModels = 1,
            CModels = [new CModel { Mins = modelBounds.MidPoint, Maxs = modelBounds.HalfSize,
                Radius = modelExtent.Length(), Leaf = source.CModels[0].Leaf }],
            NumBrushes = source.NumBrushes,
            Brushes = source.Brushes,
            BrushBounds = source.BrushBounds,
            BrushContents = source.BrushContents,
            MapEnts = source.MapEnts,
            NumStaticModels = source.NumStaticModels,
            StaticModelList = source.StaticModelList,
            SModelNodeCount = source.SModelNodeCount,
            SModelNodes = source.SModelNodes,
            DynEntCount = source.DynEntCount,
            DynEntDefList = source.DynEntDefList,
            DynEntPoseList = source.DynEntPoseList,
            DynEntClientList = source.DynEntClientList,
            DynEntCollList = source.DynEntCollList
        };
    }

    private static (ushort First, ushort Second) GetEdge(IReadOnlyList<ushort> indices, int triangle, int edge) =>
        (indices[triangle * 3 + (edge + 1) % 3], indices[triangle * 3 + (edge + 2) % 3]);

    private static Vector3 ToVector3(Vec3 value) => new(value.X, value.Y, value.Z);
    private static bool IsFinite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
