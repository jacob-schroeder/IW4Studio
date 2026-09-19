using System.Numerics;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.MapEnts;
using IW4.Assets.Assets.Physics;
using Iw4Radiant.MapSource;
using Iw4Radiant.Editing;
using Bounds = IW4.Assets.Math.Bounds;
using Vec3 = IW4.Assets.Math.Vec3;

namespace Iw4Radiant.Compilation;

internal static class BrushCollisionCompiler
{
    internal static ClipMapAsset Compile(
        MapDocument document,
        string assetName,
        IReadOnlyList<ClipMaterial> materials,
        IReadOnlyDictionary<string, ClipMaterial> baseMaterials,
        MapEntsAsset mapEnts)
    {
        MapEntity[] entities = [document.World, .. MapCompiler.BrushEntities(document)];
        var sourceBrushes = new List<MapBrush>(document.World.Brushes);
        var modelBrushRanges = new List<(int First, int Count)> { (0, sourceBrushes.Count) };
        foreach (MapEntity entity in entities.Skip(1))
        {
            int first = sourceBrushes.Count;
            Matrix4x4 local = Matrix4x4.CreateTranslation(-EditorSession.EntityOrigin(entity)) *
                Matrix4x4.Transpose(EntityOrientation.Rotation(entity));
            foreach (MapBrush brush in entity.Brushes)
            {
                MapBrush copy = brush.Clone();
                copy.Transform(local, textureLock: true);
                sourceBrushes.Add(copy);
            }
            modelBrushRanges.Add((first, sourceBrushes.Count - first));
        }
        if (sourceBrushes.Count is 0 or > short.MaxValue)
            throw new NotSupportedException("A single-cell map requires between 1 and 32767 total world/entity brushes.");
        if (materials.Count is 0 or > short.MaxValue)
            throw new NotSupportedException("Brush collision requires between 1 and 32767 materials.");
        var materialIndices = materials.Select((material, index) =>
                (Name: material.Name ?? throw new InvalidDataException("A collision material has no name."), material.Contents, Index: index))
            .ToDictionary(item => (item.Name, item.Contents), item => item.Index);

        var planes = new List<CPlane>();
        var sides = new List<CBrushSide>();
        var edges = new List<byte>();
        var brushes = new CBrush[sourceBrushes.Count];
        var bounds = new Bounds[sourceBrushes.Count];
        var contents = new uint[sourceBrushes.Count];
        Vector3 worldMin = new(float.PositiveInfinity), worldMax = new(float.NegativeInfinity);
        uint combinedContents = 0;
        for (int brushIndex = 0; brushIndex < sourceBrushes.Count; brushIndex++)
        {
            MapBrush source = sourceBrushes[brushIndex];
            BrushGeometry.Validate(source);
            IReadOnlyList<MapPolygon> polygons = source.GetPolygons();
            if (!baseMaterials.TryGetValue(polygons[0].Face.Material, out ClipMaterial? baseMaterial))
                throw new InvalidDataException($"Brush {brushIndex} material '{polygons[0].Face.Material}' is missing.");
            int brushContents = BrushContents.Compile(BrushContents.ReadForCompilation(source), baseMaterial.Contents);
            var faceMaterials = polygons.Select(polygon =>
                materialIndices.TryGetValue((polygon.Face.Material, brushContents), out int index)
                    ? index
                    : throw new InvalidDataException($"Brush {brushIndex} material '{polygon.Face.Material}' is missing."))
                .ToArray();
            contents[brushIndex] = unchecked((uint)brushContents);
            if (brushIndex < document.World.Brushes.Count) combinedContents |= contents[brushIndex];
            (Vector3 min, Vector3 max) = source.GetBounds();
            bounds[brushIndex] = MakeBounds(min, max);
            if (brushIndex < document.World.Brushes.Count)
            {
                worldMin = Vector3.Min(worldMin, min);
                worldMax = Vector3.Max(worldMax, max);
            }

            // Native side numbers interleave min/max by axis. The six material
            // and offset fields are stored separately as min XYZ, then max XYZ.
            var sideNumbers = new int[polygons.Count];
            var axialFaces = Enumerable.Repeat(-1, 6).ToArray();
            var nonAxialFaces = new List<int>();
            for (int faceIndex = 0; faceIndex < polygons.Count; faceIndex++)
            {
                int axialSide = GetAxialSide(polygons[faceIndex].Face.Normal);
                if (axialSide >= 0)
                {
                    if (axialFaces[axialSide] >= 0)
                        throw new InvalidDataException($"Brush {brushIndex} repeats an axial collision face.");
                    axialFaces[axialSide] = faceIndex;
                    sideNumbers[faceIndex] = axialSide;
                }
                else
                {
                    sideNumbers[faceIndex] = checked(6 + nonAxialFaces.Count);
                    nonAxialFaces.Add(faceIndex);
                }
            }
            if (nonAxialFaces.Count > byte.MaxValue - 5)
                throw new NotSupportedException($"Brush {brushIndex} exceeds the byte-sized collision side range.");

            byte[][] adjacency = BuildAdjacency(polygons, sideNumbers, brushIndex);
            var localEdges = new List<byte>();
            var axialMaterials = new short[6];
            var axialOffsets = new byte[6];
            var axialEdgeCounts = new byte[6];
            for (int axialSide = 0; axialSide < 6; axialSide++)
            {
                int field = axialSide / 2 + (axialSide % 2) * 3;
                int faceIndex = axialFaces[axialSide];
                axialMaterials[field] = checked((short)faceMaterials[faceIndex < 0 ? 0 : faceIndex]);
                axialOffsets[field] = GetAdjacencyOffset(localEdges.Count, brushIndex);
                if (faceIndex < 0)
                    continue;
                axialEdgeCounts[field] = checked((byte)adjacency[faceIndex].Length);
                localEdges.AddRange(adjacency[faceIndex]);
            }

            var localSides = new CBrushSide[nonAxialFaces.Count];
            for (int sideIndex = 0; sideIndex < nonAxialFaces.Count; sideIndex++)
            {
                int faceIndex = nonAxialFaces[sideIndex];
                MapFace face = polygons[faceIndex].Face;
                CPlane plane = MakePlane(face.Normal, (float)BrushGeometry.Dot(face.Normal, face.A));
                planes.Add(plane);
                var side = new CBrushSide
                {
                    Plane = plane,
                    MaterialNum = checked((ushort)faceMaterials[faceIndex]),
                    FirstAdjacentSideOffset = GetAdjacencyOffset(localEdges.Count, brushIndex),
                    EdgeCount = checked((byte)adjacency[faceIndex].Length)
                };
                localSides[sideIndex] = side;
                sides.Add(side);
                localEdges.AddRange(adjacency[faceIndex]);
            }
            brushes[brushIndex] = new CBrush
            {
                NumSides = checked((ushort)localSides.Length),
                Sides = localSides,
                BaseAdjacentSide = localEdges.ToArray(),
                AxialMaterialNum = axialMaterials,
                FirstAdjacentSideOffsets = axialOffsets,
                EdgeCount = axialEdgeCounts
            };
            edges.AddRange(localEdges);
        }

        // Everything at or behind this plane traverses the occupied world leaf.
        // The other child is an empty leaf; leaf zero remains the engine sentinel.
        float splitDistance = MathF.BitIncrement(worldMax.X);
        if (!float.IsFinite(splitDistance))
            throw new InvalidDataException("The map bounds leave no finite position for its collision root plane.");
        CPlane rootPlane = MakePlane(Vector3.UnitX, splitDistance);
        planes.Add(rootPlane);
        ushort[] leafBrushes = Enumerable.Range(0, document.World.Brushes.Count).Select(index => (ushort)index).ToArray();
        Bounds leafBounds = MakeBounds(worldMin - new Vector3(0.125f), worldMax + new Vector3(0.125f));
        Bounds modelBounds = MakeBounds(worldMin - Vector3.One, worldMax + Vector3.One);
        Vector3 modelExtent = Vector3.Max(Vector3.Abs(worldMin - Vector3.One), Vector3.Abs(worldMax + Vector3.One));
        var brushNodes = new List<CLeafBrushNode>
        {
            new() { Data = new CLeafBrushNodeData { Children = new CLeafBrushNodeChildren { ChildOffsets = [0, 0] } } },
            new()
            {
                LeafBrushCount = checked((short)leafBrushes.Length), Contents = unchecked((int)combinedContents),
                Data = new CLeafBrushNodeData { Brushes = leafBrushes, LeafUnionPad = new byte[8] }
            }
        };
        var models = new List<CModel>
        {
            new() { Mins = modelBounds.MidPoint, Maxs = modelBounds.HalfSize, Radius = modelExtent.Length() }
        };
        foreach (var (first, count) in modelBrushRanges.Skip(1))
        {
            var vertices = sourceBrushes.Skip(first).Take(count).SelectMany(brush => brush.GetVertices()).ToArray();
            Bounds model = MakeBounds(vertices.Aggregate(Vector3.Min), vertices.Aggregate(Vector3.Max));
            int modelContents = unchecked((int)contents.Skip(first).Take(count).Aggregate(0u, (left, right) => left | right));
            int node = brushNodes.Count;
            brushNodes.Add(new CLeafBrushNode
            {
                LeafBrushCount = checked((short)count), Contents = modelContents,
                Data = new CLeafBrushNodeData
                {
                    Brushes = Enumerable.Range(first, count).Select(index => checked((ushort)index)).ToArray(),
                    LeafUnionPad = new byte[8]
                }
            });
            models.Add(new CModel
            {
                Mins = model.MidPoint, Maxs = model.HalfSize,
                Radius = vertices.Max(point => point.Length()),
                Leaf = new CLeaf { Mins = model.MidPoint, Maxs = model.HalfSize, BrushContents = modelContents, LeafBrushNode = node }
            });
        }
        return new ClipMapAsset
        {
            Name = assetName,
            PlaneCount = planes.Count,
            Planes = planes.ToArray(),
            NumMaterials = materials.Count,
            Materials = materials,
            NumBrushSides = sides.Count,
            BrushSides = sides.ToArray(),
            NumBrushEdges = edges.Count,
            BrushEdges = edges.ToArray(),
            NumNodes = 1,
            Nodes = [new CNode { Plane = rootPlane, Children = [-3, -2] }],
            NumLeafs = 3,
            Leafs =
            [
                new CLeaf(),
                new CLeaf
                {
                    BrushContents = unchecked((int)combinedContents),
                    Mins = leafBounds.MidPoint,
                    Maxs = leafBounds.HalfSize,
                    LeafBrushNode = 1
                },
                new CLeaf()
            ],
            LeafBrushNodesCount = brushNodes.Count,
            LeafBrushNodes = brushNodes,
            NumLeafBrushes = leafBrushes.Length,
            LeafBrushes = leafBrushes,
            NumSubModels = models.Count,
            CModels = models,
            NumBrushes = checked((ushort)brushes.Length),
            Brushes = brushes,
            BrushBounds = bounds,
            BrushContents = contents,
            MapEnts = mapEnts,
            SModelNodeCount = 1,
            SModelNodes = [new SModelAabbNode()],
            DynEntCount = [0, 0]
        };
    }

    private static byte[][] BuildAdjacency(IReadOnlyList<MapPolygon> polygons, int[] sideNumbers, int brushIndex)
    {
        IReadOnlyList<Vector3> vertices = BrushGeometry.Vertices(polygons);
        var owners = new Dictionary<(int, int), List<int>>();
        var faceEdges = new (int, int)[polygons.Count][];
        for (int face = 0; face < polygons.Count; face++)
        {
            Vector3[] points = polygons[face].Vertices;
            if (points.Length > byte.MaxValue)
                throw new NotSupportedException($"Brush {brushIndex} has a face with more than 255 edges.");
            faceEdges[face] = new (int, int)[points.Length];
            for (int edge = 0; edge < points.Length; edge++)
            {
                int first = BrushGeometry.FindVertex(vertices, points[edge]);
                int second = BrushGeometry.FindVertex(vertices, points[(edge + 1) % points.Length]);
                var key = first < second ? (first, second) : (second, first);
                faceEdges[face][edge] = key;
                if (!owners.TryGetValue(key, out List<int>? edgeOwners))
                    owners.Add(key, edgeOwners = []);
                edgeOwners.Add(face);
            }
        }

        var adjacency = new byte[polygons.Count][];
        for (int face = 0; face < polygons.Count; face++)
        {
            adjacency[face] = new byte[faceEdges[face].Length];
            for (int edge = 0; edge < faceEdges[face].Length; edge++)
            {
                List<int> edgeOwners = owners[faceEdges[face][edge]];
                if (edgeOwners.Count != 2)
                    throw new InvalidDataException($"Brush {brushIndex} has an edge without exactly two collision faces.");
                int adjacentFace = edgeOwners[0] == face ? edgeOwners[1] : edgeOwners[0];
                // Native winding reconstruction uses PlaneFromPoints' clockwise
                // order; editor polygons have the opposite orientation.
                adjacency[face][faceEdges[face].Length - 1 - edge] = checked((byte)sideNumbers[adjacentFace]);
            }
        }
        return adjacency;
    }

    private static byte GetAdjacencyOffset(int count, int brushIndex) => count <= byte.MaxValue
        ? (byte)count
        : throw new NotSupportedException($"Brush {brushIndex} exceeds the byte-sized collision adjacency offset range.");

    private static int GetAxialSide(Vector3 normal)
    {
        for (int axis = 0; axis < 3; axis++)
        {
            Vector3 unit = axis == 0 ? Vector3.UnitX : axis == 1 ? Vector3.UnitY : Vector3.UnitZ;
            if (normal == -unit) return axis * 2;
            if (normal == unit) return axis * 2 + 1;
        }
        return -1;
    }

    internal static CPlane MakePlane(Vector3 normal, float distance) => new()
    {
        Normal = ToVec3(normal),
        Dist = distance,
        Type = normal == Vector3.UnitX ? (byte)0 : normal == Vector3.UnitY ? (byte)1 : normal == Vector3.UnitZ ? (byte)2 : (byte)3,
        Pad12 = new byte[2]
    };

    internal static Bounds MakeBounds(Vector3 min, Vector3 max)
    {
        Vector3 midpoint = new(
            (float)(((double)min.X + max.X) * 0.5),
            (float)(((double)min.Y + max.Y) * 0.5),
            (float)(((double)min.Z + max.Z) * 0.5));
        return new Bounds { MidPoint = ToVec3(midpoint), HalfSize = ToVec3(midpoint - min) };
    }

    internal static Vec3 ToVec3(Vector3 value) => new() { X = value.X, Y = value.Y, Z = value.Z };
}
