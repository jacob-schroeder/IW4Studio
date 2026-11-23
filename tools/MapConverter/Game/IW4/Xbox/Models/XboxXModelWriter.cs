using IW4.Game.Assets.XModel;
using IW4.Game.Codecs.XModel;
using IW4.Game.Math;
using MapConverter.Game.IW4.Xbox.FastFiles;
using MapConverter.Game.IW4.Xbox.Geometry;

namespace MapConverter.Game.IW4.Xbox.Models;

/// <summary>Converts materialized PS3 model geometry to the retail Xenon XModel wire layout.</summary>
internal static class XboxXModelWriter
{
    private const int RootSize = 0x120;
    private const int LodSize = 0x28;
    private const int SurfaceSize = 0x7c;

    private static bool CanWrite(XModelAsset model)
    {
        if (model.Name is null || model.Lods.Count != 4 || model.NoScalePartBits.Count != 6 ||
            model.BoneNames.Count != model.NumBones ||
            model.BoneNames.Any(item => item.Text is null) ||
            model.NumRootBones > model.NumBones || model.Materials.Count != (model.SerializedNumSurfs ?? model.NumSurfs) ||
            model.InvHighMipRadius.Count != (model.SerializedNumSurfs ?? model.NumSurfs) ||
            model.BoneInfo.Count != model.NumBones || model.BaseMat.Count != model.NumBones ||
            model.CollSurfs.Count != model.NumCollSurfs)
            return false;
        int childBones = model.NumBones - model.NumRootBones;
        if (model.ParentList.Count != childBones || model.Quats.Count != childBones * 4 ||
            model.Trans.Count != childBones * 3 || model.PartClassification.Count != model.NumBones)
            return false;
        foreach (XModelLodInfo lod in model.Lods)
        {
            IReadOnlyList<uint> bits = lod.SerializedPartBits ?? lod.PartBits;
            if (bits.Count != 6) return false;
            int count = lod.SerializedNumSurfs ?? lod.NumSurfs;
            if (count == 0) continue;
            if (lod.ModelSurfs?.Name is null || lod.ModelSurfs.NumSurfs != count ||
                lod.ModelSurfs.Surfaces.Count != count ||
                lod.ModelSurfs.PartBits.Count != 6)
                return false;
            foreach (XSurface surface in lod.ModelSurfs.Surfaces)
            {
                if (surface.PartBits.Count != 6 ||
                    surface.Verts0.Count != surface.VertCount * 16 ||
                    surface.Verts1.Count != surface.VertCount * 16 ||
                    surface.TriIndices.Count != surface.TriCount * 3 ||
                    surface.VertList.Count != surface.VertListCount ||
                    surface.VertexInfo.VertsBlend.Count != surface.VertexInfo.Blend0 +
                        3 * surface.VertexInfo.Blend1 + 5 * surface.VertexInfo.Blend2 +
                        7 * surface.VertexInfo.Blend3 ||
                    surface.VertList.Any(rigid => rigid.CollisionTree is { } tree &&
                        (tree.Nodes.Count != tree.NodeCount || tree.Leafs.Count != tree.LeafCount)))
                    return false;
            }
        }
        return true;
    }

    internal static void Write(XboxZoneStream zone, XModelAsset model)
    {
        if (!CanWrite(model))
            throw new InvalidDataException($"XModel '{model.Name}' cannot be converted to the retail Xbox layout from its retained graph.");
        byte surfaceCount = model.SerializedNumSurfs ?? model.NumSurfs;
        byte[] root = new byte[RootSize];
        root[4] = model.NumBones;
        root[5] = model.NumRootBones;
        root[6] = surfaceCount;
        // The TU6 XModel names this byte lodRampType. It is padding in the OAT declaration.
        root[7] = (byte)model.LodRampType;
        XboxWire.Float(root, 8, model.Scale);
        for (int i = 0; i < 6; i++) XboxWire.U32(root, 12 + i * 4, model.NoScalePartBits[i]);
        for (int i = 0; i < 4; i++)
        {
            XModelLodInfo lod = model.Lods[i];
            int at = 0x40 + i * LodSize;
            XboxWire.Float(root, at, lod.Dist);
            XboxWire.U16(root, at + 4, lod.SerializedNumSurfs ?? lod.NumSurfs);
            XboxWire.U16(root, at + 6, lod.SerializedSurfIndex ?? lod.SurfIndex);
            IReadOnlyList<uint> bits = lod.SerializedPartBits ?? lod.PartBits;
            for (int bit = 0; bit < 6; bit++) XboxWire.U32(root, at + 12 + bit * 4, bits[bit]);
        }
        root[0xe0] = model.MaxLoadedLod;
        root[0xe1] = model.NumLods;
        root[0xe2] = model.CollLod;
        root[0xe3] = (byte)model.Flags;
        XboxWire.I32(root, 0xe8, model.NumCollSurfs);
        XboxWire.I32(root, 0xec, model.Contents);
        XboxWire.Float(root, 0xf4, model.Radius);
        Bounds(root, 0xf8, model.Bounds);
        XboxWire.I32(root, 0x114, model.MemUsage);
        uint rootAt = zone.Write(root, 4, XboxBlock.Temp);
        zone.Patch(rootAt, 0, XboxWire.Inline);
        zone.String(model.Name);

        Table(zone, rootAt, 0x24, model.BoneNames, 2, 2,
            (bytes, at, item) => XboxWire.U16(bytes, at, zone.ScriptString(item.Text)));
        Bytes(zone, rootAt, 0x28, model.ParentList.ToArray(), 1);
        Table(zone, rootAt, 0x2c, model.Quats, 2, 2,
            (bytes, at, item) => XboxWire.U16(bytes, at, unchecked((ushort)item)));
        Table(zone, rootAt, 0x30, model.Trans, 4, 4,
            (bytes, at, item) => XboxWire.Float(bytes, at, item));
        Bytes(zone, rootAt, 0x34, model.PartClassification.ToArray(), 1);
        Table(zone, rootAt, 0x38, model.BaseMat, DObjAnimMat.SerializedSize, 4,
            (bytes, at, item) =>
            {
                XboxWire.Float(bytes, at, item.Quat.X);
                XboxWire.Float(bytes, at + 4, item.Quat.Y);
                XboxWire.Float(bytes, at + 8, item.Quat.Z);
                XboxWire.Float(bytes, at + 12, item.Quat.W);
                XboxWire.Vector(bytes, at + 16, item.Trans);
                XboxWire.Float(bytes, at + 28, item.TransWeight);
            });
        uint materialsAt = Table(zone, rootAt, 0x3c, model.Materials, 4, 4,
            (_, _, _) => { });
        for (int i = 0; i < model.Materials.Count; i++)
            if (model.Materials[i]?.SerializedAssetName is { } name)
                zone.Patch(materialsAt, i * 4, zone.Asset(XboxAssetType.Material, name));

        for (int i = 0; i < 4; i++)
        {
            XModelLodInfo lod = model.Lods[i];
            if ((lod.SerializedNumSurfs ?? lod.NumSurfs) == 0) continue;
            var surfaces = lod.ModelSurfs ?? throw new InvalidDataException($"Missing LOD {i} in '{model.Name}'.");
            zone.Patch(rootAt, 0x40 + i * LodSize + 8, XboxWire.Inline);
            using (zone.TempScope()) WriteSurfs(zone, surfaces);
        }

        Table(zone, rootAt, 0xe4, model.CollSurfs, XModelCollSurf.SerializedSize, 4,
            (bytes, at, item) =>
            {
                Bounds(bytes, at, item.Bounds);
                XboxWire.I32(bytes, at + 24, item.BoneIndex);
                XboxWire.I32(bytes, at + 28, item.Contents);
                XboxWire.I32(bytes, at + 32, item.SurfaceFlags);
            });
        Table(zone, rootAt, 0xf0, model.BoneInfo, XBoneInfo.SerializedSize, 4,
            (bytes, at, item) =>
            {
                Bounds(bytes, at, item.Bounds);
                XboxWire.Float(bytes, at + 24, item.RadiusSquared);
            });
        Table(zone, rootAt, 0x110, model.InvHighMipRadius, 2, 2,
            (bytes, at, item) => XboxWire.U16(bytes, at, item));
        if (model.PhysPreset?.SerializedAssetName is { } preset)
            zone.Patch(rootAt, 0x118, zone.Asset(XboxAssetType.PhysPreset, preset));
        if (model.PhysCollmap?.SerializedAssetName is { } collmap)
            zone.Patch(rootAt, 0x11c, zone.Asset(XboxAssetType.PhysCollmap, collmap));
    }

    private static void WriteSurfs(XboxZoneStream zone, XModelSurfsAsset asset)
    {
        byte[] root = new byte[0x24];
        XboxWire.U16(root, 8, asset.NumSurfs);
        XboxWire.U16(root, 10, asset.Pad0A);
        for (int i = 0; i < 6; i++) XboxWire.U32(root, 12 + i * 4, asset.PartBits[i]);
        uint rootAt = zone.Write(root, 4, XboxBlock.Temp);
        zone.Patch(rootAt, 0, XboxWire.Inline);
        zone.String(asset.Name);
        uint surfacesAt = Table(zone, rootAt, 4, asset.Surfaces, SurfaceSize, 4,
            (bytes, at, item) =>
            {
                bytes[at] = (byte)item.TileMode;
                bytes[at + 1] = item.DeformedRaw;
                XboxWire.U16(bytes, at + 2, item.VertCount);
                XboxWire.U16(bytes, at + 4, item.TriCount);
                XboxWire.U16(bytes, at + 12, item.VertexInfo.Blend0);
                XboxWire.U16(bytes, at + 14, item.VertexInfo.Blend1);
                XboxWire.U16(bytes, at + 16, item.VertexInfo.Blend2);
                XboxWire.U16(bytes, at + 18, item.VertexInfo.Blend3);
                XboxWire.I32(bytes, at + 60, item.VertListCount);
                for (int bit = 0; bit < 6; bit++)
                    XboxWire.U32(bytes, at + 100 + bit * 4, item.PartBits[bit]);
            });
        for (int i = 0; i < asset.Surfaces.Count; i++)
        {
            XSurface surface = asset.Surfaces[i];
            int at = i * SurfaceSize;
            Table(zone, surfacesAt, at + 20, surface.VertexInfo.VertsBlend, 2, 2,
                (bytes, p, value) => XboxWire.U16(bytes, p, value));
            if (surface.VertCount != 0)
            {
                var packed = new byte[checked(surface.VertCount * 32)];
                for (int vert = 0; vert < surface.VertCount; vert++)
                    PackVertex(surface, vert, packed);
                Bytes(zone, surfacesAt, at + 24, packed, 16);
            }
            uint rigidAt = Table(zone, surfacesAt, at + 64, surface.VertList, XRigidVertList.SerializedSize, 4,
                (bytes, p, item) =>
                {
                    XboxWire.U16(bytes, p, item.BoneOffset);
                    XboxWire.U16(bytes, p + 2, item.VertCount);
                    XboxWire.U16(bytes, p + 4, item.TriOffset);
                    XboxWire.U16(bytes, p + 6, item.TriCount);
                });
            for (int rigid = 0; rigid < surface.VertList.Count; rigid++)
                if (surface.VertList[rigid].CollisionTree is { } tree)
                    WriteCollisionTree(zone, rigidAt, rigid * XRigidVertList.SerializedSize + 8, tree);
            Table(zone, surfacesAt, at + 8, surface.TriIndices, 2, 16,
                (bytes, p, value) => XboxWire.U16(bytes, p, value));
        }
    }

    private static void WriteCollisionTree(XboxZoneStream zone, uint owner, int offset,
        XSurfaceCollisionTree tree)
    {
        byte[] data = new byte[XSurfaceCollisionTree.SerializedSize];
        XboxWire.Vector(data, 0, tree.Trans);
        XboxWire.Vector(data, 12, tree.Scale);
        XboxWire.I32(data, 24, tree.NodeCount);
        XboxWire.I32(data, 32, tree.LeafCount);
        uint treeAt = Bytes(zone, owner, offset, data, 4);
        Table(zone, treeAt, 28, tree.Nodes, XSurfaceCollisionNode.SerializedSize, 16,
            (bytes, at, item) =>
            {
                XboxWire.U16(bytes, at, item.Aabb.MinsX);
                XboxWire.U16(bytes, at + 2, item.Aabb.MinsY);
                XboxWire.U16(bytes, at + 4, item.Aabb.MinsZ);
                XboxWire.U16(bytes, at + 6, item.Aabb.MaxsX);
                XboxWire.U16(bytes, at + 8, item.Aabb.MaxsY);
                XboxWire.U16(bytes, at + 10, item.Aabb.MaxsZ);
                XboxWire.U16(bytes, at + 12, item.ChildBeginIndex);
                XboxWire.U16(bytes, at + 14, item.ChildCount);
            });
        Table(zone, treeAt, 36, tree.Leafs, 2, 2,
            (bytes, at, item) => XboxWire.U16(bytes, at, item.TriangleBeginIndex));
    }

    private static uint Table<T>(XboxZoneStream zone, uint owner, int offset,
        IReadOnlyList<T> items, int stride, int alignment, Action<byte[], int, T> write)
    {
        if (items.Count == 0) return 0;
        var data = new byte[checked(items.Count * stride)];
        for (int i = 0; i < items.Count; i++) write(data, i * stride, items[i]);
        return Bytes(zone, owner, offset, data, alignment);
    }

    private static uint Bytes(XboxZoneStream zone, uint owner, int offset, byte[] data, int alignment)
    {
        if (data.Length == 0) return 0;
        zone.Patch(owner, offset, XboxWire.Inline);
        return zone.Write(data, alignment);
    }

    private static void Bounds(byte[] bytes, int at, Bounds bounds)
    {
        XboxWire.Vector(bytes, at, bounds.MidPoint);
        XboxWire.Vector(bytes, at + 12, bounds.HalfSize);
    }

    private static void PackVertex(XSurface surface, int index, byte[] output)
    {
        int sourceAt = index * 16, destAt = index * 32;
        for (int i = 0; i < 16; i++) output[destAt + i] = surface.Verts0[sourceAt + i];
        // Xenon uses the same VU half word as the PS3 source, but the color
        // word and third-based 10:10:10 directions have different packing.
        XboxWire.U32(output, destAt + 16, XboxPackedUnitCodec.Color(
            surface.Verts1[sourceAt], surface.Verts1[sourceAt + 1],
            surface.Verts1[sourceAt + 2], surface.Verts1[sourceAt + 3]));
        for (int i = 0; i < 4; i++) output[destAt + 20 + i] = surface.Verts1[sourceAt + 4 + i];
        if (!XSurfaceVertexCodec.TryReadNormal(surface.Verts1, index, out var normal) ||
            !XSurfaceVertexCodec.TryReadTangent(surface.Verts1, index, out var tangent))
            throw new InvalidDataException($"XSurface vertex {index} has unrepresentable direction data.");
        XboxWire.U32(output, destAt + 24, XboxPackedUnitCodec.Pack(normal.X, normal.Y, normal.Z));
        XboxWire.U32(output, destAt + 28, XboxPackedUnitCodec.Pack(tangent.X, tangent.Y, tangent.Z));
    }
}
