using System.Buffers.Binary;
using System.Text;
using IW4.Game.Assets.ColMap;
using FxEffectDefAsset = IW4.Game.Assets.Fx.FxEffectDefAsset;
using IW4.Game.Assets.GfxMap;
using IW4.Game.Assets.MapEnts;
using IW4.Game.Assets.Physics;
using IW4.Game.Assets.XModel;
using IW4.Game.Math;
using IW4.Game.Zone;
using IW4.Linker.D3dbsp;

namespace MapConverter.Game.IW3.PS3.Extraction;

internal static partial class Iw3Ps3FastFileCatalog
{
    private sealed partial class Reader
    {
        private Pointer? ClipMap(uint raw, Address cell) => AssetPointer(raw, cell, "clipmap", 0x11c, root =>
        {
            Push(4);
            try
            {
                int start = root.FileOffset;
                ReadOnlySpan<byte> header = Bytes(root).Span;
                string? name = ReadString(U32(start));
                var dependencies = new HashSet<int>();

                int planeCount = Count(Be32(header, 8));
                Pointer? planeTable = ReadArray(Be32(header, 0x0c), 4,
                    Size(planeCount, 0x14), "clip-planes");
                CPlane[] planes = _converting ? ReadPlanes(planeTable, planeCount) : [];

                int staticCount = Count(Be32(header, 0x10));
                Pointer? staticTable = ReadArray(Be32(header, 0x14), 4,
                    Size(staticCount, 0x50), "clip-static-models");
                var staticModels = new List<ClipStaticModel>(staticCount);
                if (staticTable is not null && Be32(header, 0x14) == uint.MaxValue)
                {
                    for (int index = 0; index < staticCount; index++)
                    {
                        int row = staticTable.Region.FileOffset + index * 0x50;
                        Pointer? model = Model(U32(row + 4), staticTable.Region.Address.Add(index * 0x50 + 4));
                        Add(dependencies, model);
                        if (_converting) staticModels.Add(ReadStaticModel(row, model));
                    }
                }
                else if (_converting && staticCount != 0)
                    throw Unsupported("clipmap static-model table has no inline definitions");

                int materialCount = Count(Be32(header, 0x18));
                Pointer? materialTable = ReadArray(Be32(header, 0x1c), 4,
                    Size(materialCount, 0x48), "clip-materials");
                ClipMaterial[] materials = _converting ? ReadClipMaterials(materialTable, materialCount) : [];

                int sideCount = Count(Be32(header, 0x20));
                Pointer? sideTable = ReadArray(Be32(header, 0x24), 4,
                    Size(sideCount, 0x0c), "clip-brush-sides");
                var sides = new List<CBrushSide>(sideCount);
                if (sideTable is not null && Be32(header, 0x24) == uint.MaxValue)
                {
                    for (int index = 0; index < sideCount; index++)
                    {
                        int row = sideTable.Region.FileOffset + index * 0x0c;
                        CPlane? plane = ReadPlaneReference(U32(row),
                            sideTable.Region.Address.Add(index * 0x0c), planeTable, planes);
                        if (_converting)
                        {
                            uint materialNum = U32(row + 4);
                            ushort adjacent = U16(row + 8);
                            if (materialNum > ushort.MaxValue ||
                                adjacent > byte.MaxValue && adjacent != ushort.MaxValue)
                                throw Unsupported($"clipmap brush side {index} exceeds IW4 field widths");
                            sides.Add(new CBrushSide
                            {
                                Plane = plane,
                                MaterialNum = (ushort)materialNum,
                                FirstAdjacentSideOffset = adjacent == ushort.MaxValue
                                    ? byte.MaxValue : (byte)adjacent,
                                EdgeCount = _data[row + 10]
                            });
                        }
                    }
                }
                else if (_converting && sideCount != 0)
                    throw Unsupported("clipmap brush-side table has no inline definitions");

                int edgeCount = Count(Be32(header, 0x28));
                Pointer? edgeTable = ReadArray(Be32(header, 0x2c), 1,
                    edgeCount, "clip-brush-edges");
                byte[] edges = _converting ? Raw(edgeTable, edgeCount, "clipmap brush edges").ToArray() : [];

                int nodeCount = Count(Be32(header, 0x30));
                Pointer? nodeTable = ReadArray(Be32(header, 0x34), 4,
                    Size(nodeCount, 8), "clip-nodes");
                var nodes = new List<CNode>(nodeCount);
                if (nodeTable is not null && Be32(header, 0x34) == uint.MaxValue)
                {
                    for (int index = 0; index < nodeCount; index++)
                    {
                        int row = nodeTable.Region.FileOffset + index * 8;
                        CPlane? plane = ReadPlaneReference(U32(row),
                            nodeTable.Region.Address.Add(index * 8), planeTable, planes);
                        if (_converting) nodes.Add(new CNode
                        {
                            Plane = plane,
                            Children = [unchecked((short)U16(row + 4)), unchecked((short)U16(row + 6))]
                        });
                    }
                }
                else if (_converting && nodeCount != 0)
                    throw Unsupported("clipmap node table has no inline definitions");

                int leafCount = Count(Be32(header, 0x38));
                Pointer? leafTable = ReadArray(Be32(header, 0x3c), 4,
                    Size(leafCount, 0x2c), "clip-leafs");
                CLeaf[] leafs = _converting ? ReadLeafs(leafTable, leafCount) : [];

                // d24e8 loads the shared +0x4c leaf-brush indices before
                // bf700 walks +0x44 leaf-brush nodes and their inline spans.
                int leafBrushCount = Count(Be32(header, 0x48));
                Pointer? leafBrushTable = ReadArray(Be32(header, 0x4c), 2,
                    Size(leafBrushCount, 2), "clip-leaf-brushes");
                ushort[] leafBrushes = _converting ? ReadUshorts(
                    Raw(leafBrushTable, leafBrushCount * 2, "clipmap leaf brushes").Span) : [];

                int leafNodeCount = Count(Be32(header, 0x40));
                Pointer? leafNodeTable = ReadArray(Be32(header, 0x44), 4,
                    Size(leafNodeCount, 0x14), "clip-leaf-brush-nodes");
                var leafNodes = new List<CLeafBrushNode>(leafNodeCount);
                if (leafNodeTable is not null && Be32(header, 0x44) == uint.MaxValue)
                {
                    for (int index = 0; index < leafNodeCount; index++)
                    {
                        int row = leafNodeTable.Region.FileOffset + index * 0x14;
                        short count = unchecked((short)U16(row + 2));
                        Pointer? inlineBrushes = count > 0 && U32(row + 8) == uint.MaxValue
                            ? ReadArray(U32(row + 8), 2, Size(count, 2), "clip-leaf-node-brushes")
                            : null;
                        if (_converting)
                            leafNodes.Add(ReadLeafNode(row, count, inlineBrushes,
                                leafBrushTable, leafBrushes));
                    }
                }
                else if (_converting && leafNodeCount != 0)
                    throw Unsupported("clipmap leaf-brush-node table has no inline definitions");

                int leafSurfaceCount = Count(Be32(header, 0x50));
                Pointer? leafSurfaceTable = ReadArray(Be32(header, 0x54), 4,
                    Size(leafSurfaceCount, 4), "clip-leaf-surfaces");
                uint[] leafSurfaces = _converting ? ReadUInts(
                    Raw(leafSurfaceTable, leafSurfaceCount * 4, "clipmap leaf surfaces").Span) : [];

                int vertCount = Count(Be32(header, 0x58));
                Pointer? vertsTable = ReadArray(Be32(header, 0x5c), 4,
                    Size(vertCount, 12), "clip-verts");
                Vec3[] verts = _converting ? ReadVec3Array(
                    Raw(vertsTable, vertCount * 12, "clipmap vertices").Span) : [];

                int triCount = Count(Be32(header, 0x60));
                Pointer? triTable = ReadArray(Be32(header, 0x64), 2,
                    Size(triCount, 6), "clip-triangles");
                ushort[] triangles = _converting ? ReadUshorts(
                    Raw(triTable, triCount * 6, "clipmap triangles").Span) : [];
                int walkableBytes = checked(((triCount * 3 + 0x1f) >> 5) * 4);
                Pointer? walkableTable = ReadArray(Be32(header, 0x68), 1,
                    walkableBytes, "clip-walkable-edges");
                byte[] walkable = _converting ? Raw(walkableTable, walkableBytes,
                    "clipmap walkable edges").ToArray() : [];

                int borderCount = Count(Be32(header, 0x6c));
                Pointer? borderTable = ReadArray(Be32(header, 0x70), 4,
                    Size(borderCount, 0x1c), "clip-borders");
                CollisionBorder[] borders = _converting ? ReadBorders(borderTable, borderCount) : [];

                int partitionCount = Count(Be32(header, 0x74));
                Pointer? partitionTable = ReadArray(Be32(header, 0x78), 4,
                    Size(partitionCount, 0x0c), "clip-partitions");
                var partitions = new List<CollisionPartition>(partitionCount);
                if (partitionTable is not null && Be32(header, 0x78) == uint.MaxValue)
                {
                    for (int index = 0; index < partitionCount; index++)
                    {
                        int row = partitionTable.Region.FileOffset + index * 0x0c;
                        int count = _data[row + 1];
                        uint childRaw = U32(row + 8);
                        Pointer? child = childRaw == uint.MaxValue
                            ? ReadArray(childRaw, 4, 0x1c, "clip-partition-border")
                            : null;
                        if (_converting) partitions.Add(new CollisionPartition
                        {
                            TriCount = _data[row],
                            BorderCount = (byte)count,
                            FirstVertSegment = _data[row + 2],
                            Pad03 = _data[row + 3],
                            FirstTri = unchecked((int)U32(row + 4)),
                            Borders = ReadBorderView(childRaw, child, count, borderTable, borders)
                        });
                    }
                }
                else if (_converting && partitionCount != 0)
                    throw Unsupported("clipmap partition table has no inline definitions");

                int aabbCount = Count(Be32(header, 0x7c));
                Pointer? aabbTable = ReadArray(Be32(header, 0x80), 4,
                    Size(aabbCount, 0x20), "clip-aabb-trees");
                CollisionAabbTree[] aabbs = _converting
                    ? ReadAabbs(aabbTable, aabbCount, materialCount, partitionCount) : [];

                int modelCount = Count(Be32(header, 0x84));
                Pointer? modelTable = ReadArray(Be32(header, 0x88), 4,
                    Size(modelCount, 0x48), "clip-cmodels");
                CModel[] models = _converting ? ReadCModels(modelTable, modelCount) : [];

                int brushCount = Be16(header, 0x8c);
                // COD4 PS3 ba1c8 uses d9ea8(0xf) for both brush allocations.
                Pointer? brushTable = ReadArray(Be32(header, 0x90), 16,
                    Size(brushCount, 0x50), "clip-brushes");
                var brushes = new List<CBrush>(brushCount);
                var brushBounds = new List<Bounds>(brushCount);
                var brushContents = new List<uint>(brushCount);
                if (brushTable is not null && Be32(header, 0x90) == uint.MaxValue)
                {
                    for (int index = 0; index < brushCount; index++)
                    {
                        int row = brushTable.Region.FileOffset + index * 0x50;
                        uint sideRaw = U32(row + 0x20), edgeRaw = U32(row + 0x30);
                        if (sideRaw == uint.MaxValue)
                            ReadBrushSideChild(row + 0x20, planeTable, planes);
                        if (edgeRaw == uint.MaxValue)
                            ReadBytes(1);
                        if (_converting)
                        {
                            CBrush brush = ReadBrush(row, sideRaw, edgeRaw, sideTable,
                                sides, edgeTable, edges);
                            brushes.Add(brush);
                            brushBounds.Add(ReadMinMaxBounds(_data.AsSpan(row, 0x50), 0, 0x10));
                            brushContents.Add(U32(row + 0x0c));
                        }
                    }
                }
                else if (_converting && brushCount != 0)
                    throw Unsupported("clipmap brush table has no inline definitions");

                int clusters = Count(Be32(header, 0x94));
                int clusterBytes = Count(Be32(header, 0x98));
                ReadArray(Be32(header, 0x9c), 1,
                    Size(clusters, clusterBytes), "clip-visibility");
                Pointer? mapEnts = MapEnts(Be32(header, 0xa4), root.Address.Add(0xa4));
                Add(dependencies, mapEnts);
                if (Be32(header, 0xa8) != 0)
                {
                    if (Be32(header, 0xa8) != uint.MaxValue)
                        throw Unsupported("clipmap box-brush direct pointer");
                    Align(16);
                    Region box = ReadBytes(0x50);
                    if (U32(box.FileOffset + 0x20) != 0)
                        ReadBrushSideChild(box.FileOffset + 0x20, planeTable, planes);
                    if (U32(box.FileOffset + 0x30) != 0)
                        ReadBytes(1);
                }

                int[] dynCounts = [Be16(header, 0xf4), Be16(header, 0xf6)];
                var dynDefs = new IReadOnlyList<DynEntityDef>[2];
                for (int list = 0; list < 2; list++)
                    dynDefs[list] = ReadDynDefinitions(header, list, dynCounts[list], dependencies);
                for (int category = 0; category < 3; category++)
                    for (int list = 0; list < 2; list++)
                    {
                        int offset = 0x100 + category * 8 + list * 4;
                        if (Be32(header, offset) == 0) continue;
                        Push(1);
                        try { Align(4); Reserve(Size(dynCounts[list], category switch
                            { 0 => 0x20, 1 => 0x0c, _ => 0x14 })); }
                        finally { Pop(); }
                    }

                object? value = null;
                if (_converting)
                {
                    uint checksum = Be32(header, 0x118);
                    SModelAabbNode staticModelRoot = CreateClipStaticModelRoot(staticModels);
                    ClipMapAsset Build(MapEntsAsset? finalMapEnts) => new()
                    {
                        SerializedType = XAssetType.ColMapMp,
                        Name = name,
                        PlaneCount = planeCount, Planes = planes,
                        NumStaticModels = staticCount, StaticModelList = staticModels,
                        NumMaterials = materialCount, Materials = materials,
                        NumBrushSides = sideCount, BrushSides = sides,
                        NumBrushEdges = edgeCount, BrushEdges = edges,
                        NumNodes = nodeCount, Nodes = nodes,
                        NumLeafs = leafCount, Leafs = leafs,
                        LeafBrushNodesCount = leafNodeCount, LeafBrushNodes = leafNodes,
                        NumLeafBrushes = leafBrushCount, LeafBrushes = leafBrushes,
                        NumLeafSurfaces = leafSurfaceCount, LeafSurfaces = leafSurfaces,
                        VertCount = vertCount, Verts = verts,
                        TriCount = triCount, TriIndices = triangles,
                        TriEdgeIsWalkable = walkable,
                        BorderCount = borderCount, Borders = borders,
                        PartitionCount = partitionCount, Partitions = partitions,
                        AabbTreeCount = aabbCount, AabbTrees = aabbs,
                        NumSubModels = modelCount, CModels = models,
                        NumBrushes = (ushort)brushCount, Brushes = brushes,
                        BrushBounds = brushBounds, BrushContents = brushContents,
                        MapEnts = finalMapEnts,
                        SModelNodeCount = 1, SModelNodes = [staticModelRoot],
                        DynEntCount = dynCounts.Select(count => (ushort)count).ToArray(),
                        DynEntDefListPointers = [default, default],
                        DynEntDefList = dynDefs,
                        DynEntPoseListPointers = [default, default],
                        DynEntPoseList = [[], []],
                        DynEntClientListPointers = [default, default],
                        DynEntClientList = [[], []],
                        DynEntCollListPointers = [default, default],
                        DynEntCollList = [[], []],
                        Checksum = checksum
                    };
                    var sourceMapEnts = mapEnts?.Value as MapEntsAsset;
                    if (sourceMapEnts is null || string.IsNullOrEmpty(sourceMapEnts.Name))
                        throw Unsupported("clipmap has no resolved map entities for trigger conversion");
                    // Entity model names are strings, so normal asset-pointer
                    // traversal does not discover their stock definitions.
                    foreach (string modelName in D3dbspAssetLinker.ReadNamedEntityModelNames(
                        sourceMapEnts.EntityStringBytes.ToArray()))
                    {
                        string targetName = _ownedNamePrefix + modelName;
                        if (_definitions.OfType<XModelAsset>().Any(model => model.Name == targetName))
                            continue;
                        Iw3Ps3ModelSource sourceModel = (_stockSources ??
                            throw Invalid("conversion has no stock source library")).ResolveModel(modelName);
                        _definitions.Add(CompileModelSource(sourceModel, null));
                    }
                    GfxWorldAsset world = _definitions.OfType<GfxWorldAsset>().SingleOrDefault() ??
                        throw Unsupported("clipmap needs a converted render world for stage conversion");
                    ClipMapAsset geometry = Build(null);
                    MapEntsAsset finalMapEnts = D3dbspAssetLinker.CreateMapEnts(
                        sourceMapEnts.Name, sourceMapEnts.EntityStringBytes.ToArray(), geometry,
                        world.SunPrimaryLightIndex, _ownedNamePrefix);
                    _definitions.Add(finalMapEnts);
                    value = Build(finalMapEnts);
                }
                return Named("clipmap", name, root, dependencies, value);
            }
            finally { Pop(); }
        });

        private CPlane[] ReadPlanes(Pointer? pointer, int count)
        {
            ReadOnlySpan<byte> bytes = Raw(pointer, Size(count, 0x14), "clipmap planes").Span;
            var result = new CPlane[count];
            for (int index = 0; index < count; index++)
                result[index] = ReadPlane(bytes.Slice(index * 0x14, 0x14));
            return result;
        }

        private static CPlane ReadPlane(ReadOnlySpan<byte> row) => new()
        {
            Normal = ReadVec3(row, 0),
            Dist = BeFloat(row, 12),
            Type = row[16],
            SignBits = row[17],
            Pad12 = row[18..20].ToArray()
        };

        private CPlane? ReadPlaneReference(uint raw, Address cell, Pointer? table, IReadOnlyList<CPlane> planes)
        {
            if (raw == 0) return null;
            if (raw == uint.MaxValue)
            {
                Align(4);
                Region inline = ReadBytes(0x14);
                return _converting ? ReadPlane(Bytes(inline).Span) : null;
            }
            if (!_converting) return null;
            if (table is null)
                throw Unsupported("clipmap plane reference has no resolved plane table");
            Address address = Decode(raw);
            int displacement = address.Offset - table.Region.Address.Offset;
            if (address.Block != table.Region.Address.Block || displacement < 0 ||
                displacement % 0x14 != 0 || displacement / 0x14 >= planes.Count)
                throw Unsupported($"clipmap plane reference 0x{raw:X8} is outside the plane table");
            return planes[displacement / 0x14];
        }

        private ClipStaticModel ReadStaticModel(int row, Pointer? model)
        {
            if (model is not null && model.Value is not XModelAsset)
                throw Unsupported("clipmap static model has no converted XModel");
            ReadOnlySpan<byte> bytes = _data.AsSpan(row, 0x50);
            return new ClipStaticModel
            {
                XModel = model?.Value as XModelAsset,
                Origin = ReadVec3(bytes, 8),
                InvScaledAxis = [ReadVec3(bytes, 0x14), ReadVec3(bytes, 0x20),
                    ReadVec3(bytes, 0x2c)],
                Bounds = ReadMinMaxBounds(bytes, 0x38, 0x44)
            };
        }

        private ClipMaterial[] ReadClipMaterials(Pointer? pointer, int count)
        {
            ReadOnlySpan<byte> bytes = Raw(pointer, Size(count, 0x48), "clipmap materials").Span;
            var result = new ClipMaterial[count];
            for (int index = 0; index < count; index++)
            {
                ReadOnlySpan<byte> row = bytes.Slice(index * 0x48, 0x48);
                int end = row[..64].IndexOf((byte)0);
                if (end < 0) throw Invalid($"clipmap material {index} has no NUL-terminated name");
                result[index] = new ClipMaterial
                {
                    Name = Encoding.Latin1.GetString(row[..end]),
                    SurfaceFlags = BinaryPrimitives.ReadInt32BigEndian(row[0x40..]),
                    Contents = BinaryPrimitives.ReadInt32BigEndian(row[0x44..])
                };
            }
            return result;
        }

        private CLeaf[] ReadLeafs(Pointer? pointer, int count)
        {
            ReadOnlySpan<byte> bytes = Raw(pointer, Size(count, 0x2c), "clipmap leafs").Span;
            var result = new CLeaf[count];
            for (int index = 0; index < count; index++)
                result[index] = ReadLeaf(bytes.Slice(index * 0x2c, 0x2c));
            return result;
        }

        private static CLeaf ReadLeaf(ReadOnlySpan<byte> row)
        {
            Bounds bounds = ReadMinMaxBounds(row, 12, 24);
            return new CLeaf
            {
                FirstCollAabbIndex = Be16(row, 0),
                CollAabbCount = Be16(row, 2),
                BrushContents = BinaryPrimitives.ReadInt32BigEndian(row[4..]),
                TerrainContents = BinaryPrimitives.ReadInt32BigEndian(row[8..]),
                // IW4's legacy property names store midpoint and half-size.
                Mins = bounds.MidPoint,
                Maxs = bounds.HalfSize,
                LeafBrushNode = BinaryPrimitives.ReadInt32BigEndian(row[36..])
            };
        }

        private CLeafBrushNode ReadLeafNode(int row, short count, Pointer? brushes,
            Pointer? leafBrushTable, IReadOnlyList<ushort> leafBrushes)
        {
            ReadOnlySpan<byte> bytes = _data.AsSpan(row, 0x14);
            ushort[] selected = count > 0
                ? LeafBrushView(U32(row + 8), brushes, count, leafBrushTable, leafBrushes)
                : [];
            CLeafBrushNodeData data = count > 0
                ? new CLeafBrushNodeData
                {
                    Brushes = selected,
                    LeafUnionPad = bytes[12..20].ToArray()
                }
                : new CLeafBrushNodeData
                {
                    Children = new CLeafBrushNodeChildren
                    {
                        Dist = BeFloat(bytes, 8),
                        Range = BeFloat(bytes, 12),
                        ChildOffsets = [Be16(bytes, 16), Be16(bytes, 18)]
                    }
                };
            return new CLeafBrushNode
            {
                Axis = bytes[0], Pad01 = bytes[1], LeafBrushCount = count,
                Contents = BinaryPrimitives.ReadInt32BigEndian(bytes[4..]), Data = data
            };
        }

        private ushort[] LeafBrushView(uint raw, Pointer? inline, int count,
            Pointer? table, IReadOnlyList<ushort> all)
        {
            if (raw == uint.MaxValue)
                return ReadUshorts(Raw(inline, count * 2,
                    "clipmap leaf-brush-node brushes").Span);
            if (raw == 0 || table is null)
                throw Unsupported("clipmap leaf-brush node has no brush span");
            Address address = Decode(raw);
            int displacement = address.Offset - table.Region.Address.Offset;
            if (address.Block != table.Region.Address.Block || displacement < 0 ||
                displacement % 2 != 0 || displacement / 2 > all.Count - count)
                throw Unsupported("clipmap leaf-brush-node span is outside leaf brushes");
            return all.Skip(displacement / 2).Take(count).ToArray();
        }

        private CollisionBorder[] ReadBorders(Pointer? pointer, int count)
        {
            ReadOnlySpan<byte> bytes = Raw(pointer, Size(count, 0x1c), "clipmap borders").Span;
            var result = new CollisionBorder[count];
            for (int index = 0; index < count; index++)
            {
                ReadOnlySpan<byte> row = bytes.Slice(index * 0x1c, 0x1c);
                result[index] = new CollisionBorder
                {
                    DistEq = [BeFloat(row, 0), BeFloat(row, 4), BeFloat(row, 8)],
                    ZBase = BeFloat(row, 12), ZSlope = BeFloat(row, 16),
                    Start = BeFloat(row, 20), Length = BeFloat(row, 24)
                };
            }
            return result;
        }

        private IReadOnlyList<CollisionBorder> ReadBorderView(uint raw, Pointer? inline,
            int count, Pointer? table, IReadOnlyList<CollisionBorder> all)
        {
            if (count == 0) return [];
            if (raw == uint.MaxValue)
            {
                if (count != 1)
                    throw Unsupported("inline clipmap partition border is not one row");
                return ReadBorders(inline, 1);
            }
            if (raw == 0 || table is null)
                throw Unsupported("clipmap partition has no border span");
            Address address = Decode(raw);
            int displacement = address.Offset - table.Region.Address.Offset;
            if (address.Block != table.Region.Address.Block || displacement < 0 ||
                displacement % 0x1c != 0 || displacement / 0x1c > all.Count - count)
                throw Unsupported("clipmap partition border span is outside border table");
            return all.Skip(displacement / 0x1c).Take(count).ToArray();
        }

        private CollisionAabbTree[] ReadAabbs(Pointer? pointer, int count,
            int materialCount, int partitionCount)
        {
            ReadOnlySpan<byte> bytes = Raw(pointer, Size(count, 0x20), "clipmap AABB trees").Span;
            var result = new CollisionAabbTree[count];
            for (int index = 0; index < count; index++)
            {
                ReadOnlySpan<byte> row = bytes.Slice(index * 0x20, 0x20);
                result[index] = new CollisionAabbTree
                {
                    // IW3 stores both vectors before the material/child fields;
                    // the IW4 wire layout places those fields between vectors.
                    Origin = ReadVec3(row, 0), HalfSize = ReadVec3(row, 12),
                    MaterialIndex = Be16(row, 24), ChildCount = Be16(row, 26),
                    FirstChildOrPartitionIndex = BinaryPrimitives.ReadInt32BigEndian(row[28..])
                };
                CollisionAabbTree tree = result[index];
                if (!float.IsFinite(tree.Origin.X) || !float.IsFinite(tree.Origin.Y) ||
                    !float.IsFinite(tree.Origin.Z) || !float.IsFinite(tree.HalfSize.X) ||
                    !float.IsFinite(tree.HalfSize.Y) || !float.IsFinite(tree.HalfSize.Z) ||
                    tree.HalfSize.X < 0 || tree.HalfSize.Y < 0 || tree.HalfSize.Z < 0 ||
                    tree.MaterialIndex >= materialCount)
                    throw Invalid($"clipmap AABB {index} has invalid bounds or material");
                int first = tree.FirstChildOrPartitionIndex;
                if (first < 0 || (tree.ChildCount == 0
                    ? first >= partitionCount
                    : (long)first + tree.ChildCount > count))
                    throw Invalid($"clipmap AABB {index} has an invalid child or partition range");
            }
            return result;
        }

        private CModel[] ReadCModels(Pointer? pointer, int count)
        {
            ReadOnlySpan<byte> bytes = Raw(pointer, Size(count, 0x48), "clipmap submodels").Span;
            var result = new CModel[count];
            for (int index = 0; index < count; index++)
            {
                ReadOnlySpan<byte> row = bytes.Slice(index * 0x48, 0x48);
                Bounds bounds = ReadMinMaxBounds(row, 0, 12);
                result[index] = new CModel
                {
                    Mins = bounds.MidPoint, Maxs = bounds.HalfSize,
                    Radius = BeFloat(row, 24), Leaf = ReadLeaf(row[28..])
                };
            }
            return result;
        }

        private static SModelAabbNode CreateClipStaticModelRoot(IReadOnlyList<ClipStaticModel> models)
        {
            // IW4 starts point traces at node zero even with no static models.
            if (models.Count == 0) return new SModelAabbNode();
            Vec3 mins = new()
            {
                X = models.Min(model => model.Bounds.MidPoint.X - model.Bounds.HalfSize.X),
                Y = models.Min(model => model.Bounds.MidPoint.Y - model.Bounds.HalfSize.Y),
                Z = models.Min(model => model.Bounds.MidPoint.Z - model.Bounds.HalfSize.Z)
            };
            Vec3 maxs = new()
            {
                X = models.Max(model => model.Bounds.MidPoint.X + model.Bounds.HalfSize.X),
                Y = models.Max(model => model.Bounds.MidPoint.Y + model.Bounds.HalfSize.Y),
                Z = models.Max(model => model.Bounds.MidPoint.Z + model.Bounds.HalfSize.Z)
            };
            return new SModelAabbNode
            {
                Bounds = new Bounds
                {
                    MidPoint = new Vec3 { X = (mins.X + maxs.X) * 0.5f,
                        Y = (mins.Y + maxs.Y) * 0.5f, Z = (mins.Z + maxs.Z) * 0.5f },
                    HalfSize = new Vec3 { X = (maxs.X - mins.X) * 0.5f,
                        Y = (maxs.Y - mins.Y) * 0.5f, Z = (maxs.Z - mins.Z) * 0.5f }
                },
                FirstChild = 0,
                ChildCount = checked((ushort)models.Count)
            };
        }

        private void ReadBrushSideChild(int cell, Pointer? planeTable, IReadOnlyList<CPlane> planes)
        {
            uint raw = U32(cell);
            if (raw == 0) return;
            if (raw != uint.MaxValue) return;
            Align(4);
            Region side = ReadBytes(0x0c);
            ReadPlaneReference(U32(side.FileOffset), side.Address, planeTable, planes);
        }

        private CBrush ReadBrush(int row, uint sideRaw, uint edgeRaw,
            Pointer? sideTable, IReadOnlyList<CBrushSide> sides,
            Pointer? edgeTable, IReadOnlyList<byte> edges)
        {
            ReadOnlySpan<byte> bytes = _data.AsSpan(row, 0x50);
            int sideCount = checked((int)Be32(bytes, 0x1c));
            if (sideCount > ushort.MaxValue)
                throw Unsupported("clipmap brush has too many sides for IW4");
            CBrushSide[] sideView = SideView(sideRaw, sideCount, sideTable, sides);
            var firstAdjacent = new byte[6];
            var edgeCounts = new byte[6];
            var axial = new short[6];
            int edgeByteCount = 0;
            for (int index = 0; index < 6; index++)
            {
                axial[index] = BinaryPrimitives.ReadInt16BigEndian(bytes[(0x24 + index * 2)..]);
                ushort offset = Be16(bytes, 0x34 + index * 2);
                if (offset > byte.MaxValue && offset != ushort.MaxValue)
                    throw Unsupported("clipmap brush adjacency offset exceeds the IW4 byte field");
                firstAdjacent[index] = offset == ushort.MaxValue ? byte.MaxValue : (byte)offset;
                edgeCounts[index] = bytes[0x40 + index];
                if (edgeCounts[index] != 0)
                    edgeByteCount = Math.Max(edgeByteCount,
                        firstAdjacent[index] + edgeCounts[index]);
            }
            foreach (CBrushSide side in sideView)
                if (side.EdgeCount != 0)
                    edgeByteCount = Math.Max(edgeByteCount,
                        side.FirstAdjacentSideOffset + side.EdgeCount);
            byte[] edgeView = EdgeView(edgeRaw, edgeByteCount, edgeTable, edges);
            return new CBrush
            {
                NumSides = (ushort)sideCount,
                Sides = sideView,
                BaseAdjacentSide = edgeView,
                AxialMaterialNum = axial,
                FirstAdjacentSideOffsets = firstAdjacent,
                EdgeCount = edgeCounts
            };
        }

        private CBrushSide[] SideView(uint raw, int count, Pointer? table,
            IReadOnlyList<CBrushSide> sides)
        {
            if (count == 0) return [];
            if (raw is 0 or uint.MaxValue || table is null)
                throw Unsupported("clipmap brush sides do not reference the shared side table");
            Address address = Decode(raw);
            int displacement = address.Offset - table.Region.Address.Offset;
            if (address.Block != table.Region.Address.Block || displacement < 0 ||
                displacement % 12 != 0 || displacement / 12 > sides.Count - count)
                throw Unsupported("clipmap brush side span is outside the shared side table");
            return sides.Skip(displacement / 12).Take(count).ToArray();
        }

        private byte[] EdgeView(uint raw, int count, Pointer? table,
            IReadOnlyList<byte> edges)
        {
            if (count == 0) return [];
            if (raw is 0 or uint.MaxValue || table is null)
                throw Unsupported("clipmap brush edges do not reference the shared edge table");
            Address address = Decode(raw);
            int displacement = address.Offset - table.Region.Address.Offset;
            if (address.Block != table.Region.Address.Block || displacement < 0 ||
                displacement > edges.Count - count)
                throw Unsupported("clipmap brush edge span is outside the shared edge table");
            return edges.Skip(displacement).Take(count).ToArray();
        }

        private IReadOnlyList<DynEntityDef> ReadDynDefinitions(
            ReadOnlySpan<byte> header, int list, int count,
            HashSet<int> dependencies)
        {
            uint raw = Be32(header, 0xf8 + list * 4);
            if (raw == 0)
            {
                if (count != 0) throw Invalid("clipmap dynamic-entity count has no definition table");
                return [];
            }
            if (raw != uint.MaxValue)
                throw Unsupported("clipmap dynamic-entity definition table uses an unresolved direct pointer");
            Align(4);
            Region table = ReadBytes(Size(count, 0x60));
            var definitions = new List<DynEntityDef>(count);
            for (int index = 0; index < count; index++)
            {
                int row = table.FileOffset + index * 0x60;
                Address cell = table.Address.Add(index * 0x60);
                Pointer? model = Model(U32(row + 0x20), cell.Add(0x20));
                Add(dependencies, model);
                Pointer? destruction = Fx(U32(row + 0x28), cell.Add(0x28));
                Add(dependencies, destruction);
                if (U32(row + 0x2c) != 0)
                    throw Unsupported($"clipmap dynamic entity {list}:{index} references model pieces");
                Pointer? preset = PhysPreset(U32(row + 0x30), cell.Add(0x30));
                Add(dependencies, preset);
                if (!_converting) continue;
                if (model is not null && model.Value is not XModelAsset)
                    throw Unsupported($"clipmap dynamic entity {list}:{index} has no converted XModel");
                if (preset is not null && preset.Value is not PhysPresetAsset)
                    throw Unsupported($"clipmap dynamic entity {list}:{index} has no converted physics preset");
                ReadOnlySpan<byte> bytes = _data.AsSpan(row, 0x60);
                definitions.Add(new DynEntityDef
                {
                    Type = BinaryPrimitives.ReadInt32BigEndian(bytes),
                    Pose = new GfxPlacement
                    {
                        Quat = [BeFloat(bytes, 4), BeFloat(bytes, 8),
                            BeFloat(bytes, 12), BeFloat(bytes, 16)],
                        Origin = ReadVec3(bytes, 20)
                    },
                    XModel = model?.Value as XModelAsset,
                    BrushModel = Be16(bytes, 0x24),
                    PhysicsBrushModel = Be16(bytes, 0x26),
                    DestroyFx = destruction?.Value as FxEffectDefAsset,
                    PhysPreset = preset?.Value as PhysPresetAsset,
                    Health = BinaryPrimitives.ReadInt32BigEndian(bytes[0x34..]),
                    Mass = new PhysMass
                    {
                        CenterOfMass = ReadVec3(bytes, 0x38),
                        MomentsOfInertia = ReadVec3(bytes, 0x44),
                        ProductsOfInertia = ReadVec3(bytes, 0x50)
                    },
                    Contents = BinaryPrimitives.ReadInt32BigEndian(bytes[0x5c..])
                });
            }
            return definitions;
        }

        private static uint[] ReadUInts(ReadOnlySpan<byte> bytes)
        {
            var result = new uint[bytes.Length / 4];
            for (int index = 0; index < result.Length; index++)
                result[index] = Be32(bytes, index * 4);
            return result;
        }

        private static Vec3[] ReadVec3Array(ReadOnlySpan<byte> bytes)
        {
            var result = new Vec3[bytes.Length / 12];
            for (int index = 0; index < result.Length; index++)
                result[index] = ReadVec3(bytes, index * 12);
            return result;
        }
    }
}
