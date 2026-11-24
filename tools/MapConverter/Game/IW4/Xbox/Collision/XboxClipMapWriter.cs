using System.Buffers.Binary;
using IW4.Game.Assets.ColMap;
using IW4.Game.Assets.Physics;
using IW4.Game.Math;
using MapConverter.Game.IW4.Xbox.FastFiles;

namespace MapConverter.Game.IW4.Xbox.Collision;

/// <summary>Writes the Xbox 360 clipMap_t stream from the materialized IW4 collision graph.</summary>
internal static class XboxClipMapWriter
{
    private const uint Following = uint.MaxValue;

    internal static void Write(XboxZoneStream zone, ClipMapAsset clip)
    {
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(clip);
        if (clip.Name is null)
            throw new InvalidDataException("Xbox clipMap_t requires a name.");
        Require(clip.Planes, clip.PlaneCount, "planes");
        Require(clip.StaticModelList, clip.NumStaticModels, "staticModelList");
        Require(clip.Materials, clip.NumMaterials, "materials");
        Require(clip.BrushSides, clip.NumBrushSides, "brushsides");
        Require(clip.BrushEdges, clip.NumBrushEdges, "brushEdges");
        Require(clip.Nodes, clip.NumNodes, "nodes");
        Require(clip.Leafs, clip.NumLeafs, "leafs");
        Require(clip.LeafBrushNodes, clip.LeafBrushNodesCount, "leafbrushNodes");
        Require(clip.LeafBrushes, clip.NumLeafBrushes, "leafbrushes");
        Require(clip.LeafSurfaces, clip.NumLeafSurfaces, "leafsurfaces");
        Require(clip.Verts, clip.VertCount, "verts");
        Require(clip.TriIndices, checked(clip.TriCount * 3), "triIndices");
        Require(clip.TriEdgeIsWalkable, checked(((clip.TriCount * 3 + 31) / 32) * 4), "triEdgeIsWalkable");
        Require(clip.Borders, clip.BorderCount, "borders");
        Require(clip.Partitions, clip.PartitionCount, "partitions");
        Require(clip.AabbTrees, clip.AabbTreeCount, "aabbTrees");
        Require(clip.CModels, clip.NumSubModels, "cmodels");
        Require(clip.Brushes, clip.NumBrushes, "brushes");
        Require(clip.BrushBounds, clip.NumBrushes, "brushBounds");
        Require(clip.BrushContents, clip.NumBrushes, "brushContents");
        Require(clip.SModelNodes, clip.SModelNodeCount, "smodelNodes");
        Require(clip.DynEntCount, 2, "dynEntCount");
        Require(clip.DynEntDefList, 2, "dynEntDefList");
        for (int list = 0; list < 2; list++)
            Require(clip.DynEntDefList[list], clip.DynEntCount[list], $"dynEntDefList[{list}]");

        byte[] root = new byte[ClipMapAsset.SerializedSize];
        I32(root, 0x04, clip.SerializedIsInUse ?? 0);
        I32(root, 0x08, clip.PlaneCount);
        I32(root, 0x10, clip.NumStaticModels);
        I32(root, 0x18, clip.NumMaterials);
        I32(root, 0x20, clip.NumBrushSides);
        I32(root, 0x28, clip.NumBrushEdges);
        I32(root, 0x30, clip.NumNodes);
        I32(root, 0x38, clip.NumLeafs);
        I32(root, 0x40, clip.LeafBrushNodesCount);
        I32(root, 0x48, clip.NumLeafBrushes);
        I32(root, 0x50, clip.NumLeafSurfaces);
        I32(root, 0x58, clip.VertCount);
        I32(root, 0x60, clip.TriCount);
        I32(root, 0x6c, clip.BorderCount);
        I32(root, 0x74, clip.PartitionCount);
        I32(root, 0x7c, clip.AabbTreeCount);
        I32(root, 0x84, clip.NumSubModels);
        U16(root, 0x8c, clip.NumBrushes);
        U16(root, 0x8e, clip.Pad8ETo8F);
        U16(root, 0xa0, clip.SModelNodeCount);
        U16(root, 0xa2, clip.PadA2ToA3);
        U16(root, 0xa8, clip.DynEntCount[0]);
        U16(root, 0xaa, clip.DynEntCount[1]);
        U32(root, 0xcc, clip.Checksum);
        if (clip.PadD0ToFF.Count != 0)
        {
            Require(clip.PadD0ToFF, 0x30, "padding");
            clip.PadD0ToFF.ToArray().CopyTo(root, 0xd0);
        }
        uint rootAt = zone.Write(root, 4, XboxBlock.Temp);
        zone.Patch(rootAt, 0, Following);
        zone.String(clip.Name);

        uint planesAt = WriteTable(zone, rootAt, 0x0c, clip.Planes, CPlane.SerializedSize, 4,
            (bytes, at, plane) => Plane(bytes, at, plane));
        var planeAddresses = new Dictionary<(float, float, float, float, CPlaneType, byte), uint>();
        for (int i = 0; i < clip.Planes.Count; i++)
            planeAddresses.TryAdd(PlaneKey(clip.Planes[i]), planesAt + (uint)(i * CPlane.SerializedSize));

        uint staticAt = WriteTable(zone, rootAt, 0x14, clip.StaticModelList, ClipStaticModel.SerializedSize, 4,
            (bytes, at, model) =>
            {
                Vec3(bytes, at + 4, model.Origin);
                Require(model.InvScaledAxis, 3, "static model axis");
                for (int axis = 0; axis < 3; axis++) Vec3(bytes, at + 16 + axis * 12, model.InvScaledAxis[axis]);
                Bounds(bytes, at + 52, model.Bounds);
            });
        for (int i = 0; i < clip.StaticModelList.Count; i++)
            PatchAsset(zone, staticAt, i * ClipStaticModel.SerializedSize,
                XboxAssetType.XModel, clip.StaticModelList[i].XModel?.SerializedAssetName);

        uint materialsAt = WriteTable(zone, rootAt, 0x1c, clip.Materials, ClipMaterial.SerializedSize, 4,
            (bytes, at, material) =>
            {
                I32(bytes, at + 4, material.SurfaceFlags);
                I32(bytes, at + 8, material.Contents);
            });
        for (int i = 0; i < clip.Materials.Count; i++)
            if (clip.Materials[i].Name is { } materialName)
            {
                zone.Patch(materialsAt, i * ClipMaterial.SerializedSize, Following);
                zone.String(materialName);
            }

        uint sidesAt = WriteTable(zone, rootAt, 0x24, clip.BrushSides, CBrushSide.SerializedSize, 4,
            (bytes, at, side) => Side(bytes, at, side));
        for (int i = 0; i < clip.BrushSides.Count; i++)
            PlanePointer(zone, sidesAt, i * CBrushSide.SerializedSize,
                clip.BrushSides[i].Plane, planeAddresses);

        uint edgesAt = WriteBytes(zone, rootAt, 0x2c, clip.BrushEdges.ToArray(), 1);
        uint nodesAt = WriteTable(zone, rootAt, 0x34, clip.Nodes, CNode.SerializedSize, 4,
            (bytes, at, node) =>
            {
                Require(node.Children, 2, "node children");
                U16(bytes, at + 4, unchecked((ushort)node.Children[0]));
                U16(bytes, at + 6, unchecked((ushort)node.Children[1]));
            });
        for (int i = 0; i < clip.Nodes.Count; i++)
            PlanePointer(zone, nodesAt, i * CNode.SerializedSize, clip.Nodes[i].Plane, planeAddresses);

        WriteTable(zone, rootAt, 0x3c, clip.Leafs, CLeaf.SerializedSize, 4,
            (bytes, at, leaf) => Leaf(bytes, at, leaf));
        uint leafBrushesAt = WriteTable(zone, rootAt, 0x4c, clip.LeafBrushes, 2, 2,
            (bytes, at, brush) => U16(bytes, at, brush));
        uint leafBrushNodesAt = WriteTable(zone, rootAt, 0x44, clip.LeafBrushNodes, CLeafBrushNode.SerializedSize, 4,
            (bytes, at, node) =>
            {
                bytes[at] = node.Axis;
                bytes[at + 1] = node.Pad01;
                U16(bytes, at + 2, unchecked((ushort)node.LeafBrushCount));
                I32(bytes, at + 4, node.Contents);
                if (node.LeafBrushCount > 0)
                {
                    if (node.Data.LeafUnionPad.Count != 0)
                    {
                        Require(node.Data.LeafUnionPad, 8, "leaf brush node padding");
                        node.Data.LeafUnionPad.ToArray().CopyTo(bytes, at + 12);
                    }
                }
                else if (node.Data.Children is { } child)
                {
                    F32(bytes, at + 8, child.Dist);
                    F32(bytes, at + 12, child.Range);
                    Require(child.ChildOffsets, 2, "leaf brush node offsets");
                    U16(bytes, at + 16, child.ChildOffsets[0]);
                    U16(bytes, at + 18, child.ChildOffsets[1]);
                }
            });
        foreach ((CLeafBrushNode node, int index) in clip.LeafBrushNodes.Select((item, index) => (item, index)))
        {
            if (node.LeafBrushCount <= 0) continue;
            Require(node.Data.Brushes, node.LeafBrushCount, "leaf brush node brushes");
            int offset = FindSlice(clip.LeafBrushes, node.Data.Brushes);
            if (offset >= 0)
                zone.Patch(leafBrushNodesAt, index * CLeafBrushNode.SerializedSize + 8,
                    leafBrushesAt + (uint)(offset * 2));
            else
            {
                zone.Patch(leafBrushNodesAt, index * CLeafBrushNode.SerializedSize + 8, Following);
                zone.Write(Table(node.Data.Brushes, 2, (bytes, at, value) => U16(bytes, at, value)), 2);
            }
        }

        WriteTable(zone, rootAt, 0x54, clip.LeafSurfaces, 4, 4,
            (bytes, at, value) => U32(bytes, at, value));
        WriteTable(zone, rootAt, 0x5c, clip.Verts, 12, 4,
            (bytes, at, value) => Vec3(bytes, at, value));
        WriteTable(zone, rootAt, 0x64, clip.TriIndices, 2, 2,
            (bytes, at, value) => U16(bytes, at, value));
        WriteBytes(zone, rootAt, 0x68, clip.TriEdgeIsWalkable.ToArray(), 1);
        uint bordersAt = WriteTable(zone, rootAt, 0x70, clip.Borders, CollisionBorder.SerializedSize, 4,
            (bytes, at, border) => Border(bytes, at, border));
        uint partitionsAt = WriteTable(zone, rootAt, 0x78, clip.Partitions, CollisionPartition.SerializedSize, 4,
            (bytes, at, partition) =>
            {
                bytes[at] = partition.TriCount;
                bytes[at + 1] = partition.BorderCount;
                bytes[at + 2] = partition.FirstVertSegment;
                bytes[at + 3] = partition.Pad03;
                I32(bytes, at + 4, partition.FirstTri);
            });
        foreach ((CollisionPartition partition, int index) in clip.Partitions.Select((item, index) => (item, index)))
        {
            Require(partition.Borders, partition.BorderCount, "partition borders");
            if (partition.Borders.Count == 0) continue;
            int offset = FindBorderSlice(clip.Borders, partition.Borders);
            if (offset >= 0)
                zone.Patch(partitionsAt, index * CollisionPartition.SerializedSize + 8,
                    bordersAt + (uint)(offset * CollisionBorder.SerializedSize));
            else
            {
                zone.Patch(partitionsAt, index * CollisionPartition.SerializedSize + 8, Following);
                zone.Write(Table(partition.Borders, CollisionBorder.SerializedSize, Border), 4);
            }
        }

        WriteTable(zone, rootAt, 0x80, clip.AabbTrees, CollisionAabbTree.SerializedSize, 16,
            (bytes, at, tree) =>
            {
                Vec3(bytes, at, tree.Origin);
                U16(bytes, at + 12, tree.MaterialIndex);
                U16(bytes, at + 14, tree.ChildCount);
                Vec3(bytes, at + 16, tree.HalfSize);
                I32(bytes, at + 28, tree.FirstChildOrPartitionIndex);
            });
        WriteTable(zone, rootAt, 0x88, clip.CModels, CModel.SerializedSize, 4,
            (bytes, at, model) =>
            {
                Vec3(bytes, at, model.Mins);
                Vec3(bytes, at + 12, model.Maxs);
                F32(bytes, at + 24, model.Radius);
                Leaf(bytes, at + 28, model.Leaf);
            });

        int sideOffset = 0, edgeOffset = 0;
        uint brushesAt = WriteTable(zone, rootAt, 0x90, clip.Brushes, CBrush.SerializedSize, 128,
            (bytes, at, brush) =>
            {
                U16(bytes, at, brush.NumSides);
                U16(bytes, at + 2, brush.GlassPieceIndex);
                Require(brush.AxialMaterialNum, 6, "brush axial materials");
                Require(brush.FirstAdjacentSideOffsets, 6, "brush side offsets");
                Require(brush.EdgeCount, 6, "brush edge counts");
                for (int i = 0; i < 6; i++)
                {
                    U16(bytes, at + 12 + i * 2, unchecked((ushort)brush.AxialMaterialNum[i]));
                    bytes[at + 24 + i] = brush.FirstAdjacentSideOffsets[i];
                    bytes[at + 30 + i] = brush.EdgeCount[i];
                }
            });
        foreach ((CBrush brush, int index) in clip.Brushes.Select((item, index) => (item, index)))
        {
            Require(brush.Sides, brush.NumSides, "brush sides");
            int adjacency = AdjacencyLength(brush);
            Require(brush.BaseAdjacentSide, adjacency, "brush adjacency");
            if (sideOffset + brush.NumSides > clip.BrushSides.Count ||
                !brush.Sides.Zip(clip.BrushSides.Skip(sideOffset)).All(pair =>
                    SideEqual(pair.First, pair.Second)))
                throw new InvalidDataException($"Brush {index} sides differ from the shared collision side table.");
            if (edgeOffset + adjacency > clip.BrushEdges.Count ||
                !brush.BaseAdjacentSide.SequenceEqual(clip.BrushEdges.Skip(edgeOffset).Take(adjacency)))
                throw new InvalidDataException($"Brush {index} adjacency differs from the shared collision edge table.");
            if (brush.NumSides != 0)
                zone.Patch(brushesAt, index * CBrush.SerializedSize + 4,
                    sidesAt + (uint)(sideOffset * CBrushSide.SerializedSize));
            if (adjacency != 0)
                zone.Patch(brushesAt, index * CBrush.SerializedSize + 8,
                    edgesAt + (uint)edgeOffset);
            sideOffset += brush.NumSides;
            edgeOffset += adjacency;
        }
        WriteTable(zone, rootAt, 0x94, clip.BrushBounds, 24, 128,
            (bytes, at, value) => Bounds(bytes, at, value));
        WriteTable(zone, rootAt, 0x98, clip.BrushContents, 4, 4,
            (bytes, at, value) => U32(bytes, at, value));
        WriteTable(zone, rootAt, 0xa4, clip.SModelNodes, SModelAabbNode.SerializedSize, 4,
            (bytes, at, value) =>
            {
                Bounds(bytes, at, value.Bounds);
                U16(bytes, at + 24, value.FirstChild);
                U16(bytes, at + 26, value.ChildCount);
            });
        PatchAsset(zone, rootAt, 0x9c, XboxAssetType.MapEnts, clip.MapEnts?.SerializedAssetName);

        for (int list = 0; list < 2; list++)
        {
            int listIndex = list;
            IReadOnlyList<DynEntityDef> definitions = clip.DynEntDefList[list];
            uint definitionsAt = WriteTable(zone, rootAt, 0xac + list * 4, definitions,
                DynEntityDef.SerializedSize, 4, (bytes, at, definition) =>
                {
                    I32(bytes, at, (int)definition.Type);
                    Require(definition.Pose.Quat, 4, $"dynEntDefList[{listIndex}] pose quaternion");
                    for (int i = 0; i < 4; i++) F32(bytes, at + 4 + i * 4, definition.Pose.Quat[i]);
                    Vec3(bytes, at + 20, definition.Pose.Origin);
                    U16(bytes, at + 36, definition.BrushModel);
                    U16(bytes, at + 38, definition.PhysicsBrushModel);
                    I32(bytes, at + 48, definition.Health);
                    Vec3(bytes, at + 52, definition.Mass.CenterOfMass);
                    Vec3(bytes, at + 64, definition.Mass.MomentsOfInertia);
                    Vec3(bytes, at + 76, definition.Mass.ProductsOfInertia);
                    I32(bytes, at + 88, definition.Contents);
                });
            for (int i = 0; i < definitions.Count; i++)
            {
                DynEntityDef definition = definitions[i];
                int at = i * DynEntityDef.SerializedSize;
                PatchAsset(zone, definitionsAt, at + 32, XboxAssetType.XModel,
                    definition.XModel?.SerializedAssetName);
                PatchAsset(zone, definitionsAt, at + 40, XboxAssetType.Fx,
                    definition.DestroyFx?.SerializedAssetName);
                PatchAsset(zone, definitionsAt, at + 44, XboxAssetType.PhysPreset,
                    definition.PhysPreset?.SerializedAssetName);
            }
        }
        for (int list = 0; list < 2; list++)
            ReserveRuntime(zone, rootAt, 0xb4 + list * 4, clip.DynEntCount[list], DynEntityPose.SerializedSize);
        for (int list = 0; list < 2; list++)
            ReserveRuntime(zone, rootAt, 0xbc + list * 4, clip.DynEntCount[list], DynEntityClient.SerializedSize);
        for (int list = 0; list < 2; list++)
            ReserveRuntime(zone, rootAt, 0xc4 + list * 4, clip.DynEntCount[list], DynEntityColl.SerializedSize);
    }

    private static void ReserveRuntime(XboxZoneStream zone, uint owner, int offset, int count, int stride)
    {
        if (count == 0) return;
        zone.Patch(owner, offset, Following);
        zone.Reserve(checked(count * stride), 4, XboxBlock.Runtime);
    }

    private static void PatchAsset(XboxZoneStream zone, uint owner, int offset, int type, string? name)
    {
        if (name is not null) zone.Patch(owner, offset, zone.Asset(type, name));
    }

    private static uint WriteBytes(XboxZoneStream zone, uint owner, int offset, byte[] bytes, int alignment)
    {
        if (bytes.Length == 0) return 0;
        zone.Patch(owner, offset, Following);
        return zone.Write(bytes, alignment);
    }

    private static uint WriteTable<T>(XboxZoneStream zone, uint owner, int offset,
        IReadOnlyList<T> values, int stride, int alignment, Action<byte[], int, T> write)
    {
        if (values.Count == 0) return 0;
        return WriteBytes(zone, owner, offset, Table(values, stride, write), alignment);
    }

    private static byte[] Table<T>(IReadOnlyList<T> values, int stride, Action<byte[], int, T> write)
    {
        var bytes = new byte[checked(values.Count * stride)];
        for (int i = 0; i < values.Count; i++) write(bytes, i * stride, values[i]);
        return bytes;
    }

    private static void PlanePointer(XboxZoneStream zone, uint owner, int offset, CPlane? plane,
        IReadOnlyDictionary<(float, float, float, float, CPlaneType, byte), uint> addresses)
    {
        if (plane is null) return;
        if (addresses.TryGetValue(PlaneKey(plane), out uint address))
            zone.Patch(owner, offset, address);
        else
        {
            zone.Patch(owner, offset, Following);
            zone.Write(Table(new[] { plane }, CPlane.SerializedSize, Plane), 4);
        }
    }

    private static (float, float, float, float, CPlaneType, byte) PlaneKey(CPlane value) =>
        (value.Normal.X, value.Normal.Y, value.Normal.Z, value.Dist, value.Type, value.SignBits);

    private static int FindSlice<T>(IReadOnlyList<T> haystack, IReadOnlyList<T> needle)
    {
        for (int at = 0; at <= haystack.Count - needle.Count; at++)
        {
            bool match = true;
            for (int i = 0; i < needle.Count; i++)
                if (!EqualityComparer<T>.Default.Equals(haystack[at + i], needle[i])) { match = false; break; }
            if (match) return at;
        }
        return -1;
    }

    private static int FindBorderSlice(IReadOnlyList<CollisionBorder> source, IReadOnlyList<CollisionBorder> view)
    {
        for (int at = 0; at <= source.Count - view.Count; at++)
        {
            bool match = true;
            for (int i = 0; i < view.Count; i++)
                if (!BorderEqual(source[at + i], view[i])) { match = false; break; }
            if (match) return at;
        }
        return -1;
    }

    private static bool BorderEqual(CollisionBorder left, CollisionBorder right) =>
        left.DistEq.SequenceEqual(right.DistEq) && left.ZBase == right.ZBase &&
        left.ZSlope == right.ZSlope && left.Start == right.Start && left.Length == right.Length;

    private static bool SideEqual(CBrushSide left, CBrushSide right) =>
        left.MaterialNum == right.MaterialNum &&
        left.FirstAdjacentSideOffset == right.FirstAdjacentSideOffset &&
        left.EdgeCount == right.EdgeCount &&
        (left.Plane is null ? right.Plane is null : right.Plane is not null &&
            PlaneKey(left.Plane) == PlaneKey(right.Plane));

    private static int AdjacencyLength(CBrush brush)
    {
        int count = 0;
        for (int i = 0; i < brush.FirstAdjacentSideOffsets.Count; i++)
            count = Math.Max(count, brush.FirstAdjacentSideOffsets[i] + brush.EdgeCount[i]);
        foreach (CBrushSide side in brush.Sides)
            count = Math.Max(count, side.FirstAdjacentSideOffset + side.EdgeCount);
        return count;
    }

    private static void Plane(byte[] bytes, int at, CPlane plane)
    {
        Vec3(bytes, at, plane.Normal);
        F32(bytes, at + 12, plane.Dist);
        bytes[at + 16] = (byte)plane.Type;
        bytes[at + 17] = plane.SignBits;
        if (plane.Pad12.Count != 0)
        {
            Require(plane.Pad12, 2, "plane padding");
            bytes[at + 18] = plane.Pad12[0];
            bytes[at + 19] = plane.Pad12[1];
        }
    }

    private static void Side(byte[] bytes, int at, CBrushSide side)
    {
        U16(bytes, at + 4, side.MaterialNum);
        bytes[at + 6] = side.FirstAdjacentSideOffset;
        bytes[at + 7] = side.EdgeCount;
    }

    private static void Leaf(byte[] bytes, int at, CLeaf leaf)
    {
        U16(bytes, at, leaf.FirstCollAabbIndex);
        U16(bytes, at + 2, leaf.CollAabbCount);
        I32(bytes, at + 4, leaf.BrushContents);
        I32(bytes, at + 8, leaf.TerrainContents);
        Vec3(bytes, at + 12, leaf.Mins);
        Vec3(bytes, at + 24, leaf.Maxs);
        I32(bytes, at + 36, leaf.LeafBrushNode);
    }

    private static void Border(byte[] bytes, int at, CollisionBorder border)
    {
        Require(border.DistEq, 3, "border equation");
        for (int i = 0; i < 3; i++) F32(bytes, at + i * 4, border.DistEq[i]);
        F32(bytes, at + 12, border.ZBase);
        F32(bytes, at + 16, border.ZSlope);
        F32(bytes, at + 20, border.Start);
        F32(bytes, at + 24, border.Length);
    }

    private static void Bounds(byte[] bytes, int at, Bounds bounds)
    {
        Vec3(bytes, at, bounds.MidPoint);
        Vec3(bytes, at + 12, bounds.HalfSize);
    }

    private static void Vec3(byte[] bytes, int at, Vec3 value)
    {
        F32(bytes, at, value.X);
        F32(bytes, at + 4, value.Y);
        F32(bytes, at + 8, value.Z);
    }

    private static void Require<T>(IReadOnlyList<T> values, int count, string field)
    {
        if (values.Count != count)
            throw new InvalidDataException($"Xbox clipMap_t {field} has {values.Count} entries; expected {count}.");
    }

    private static void U16(byte[] bytes, int at, ushort value) =>
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(at, 2), value);
    private static void I32(byte[] bytes, int at, int value) =>
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(at, 4), value);
    private static void U32(byte[] bytes, int at, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(at, 4), value);
    private static void F32(byte[] bytes, int at, float value) =>
        I32(bytes, at, BitConverter.SingleToInt32Bits(value));
}
