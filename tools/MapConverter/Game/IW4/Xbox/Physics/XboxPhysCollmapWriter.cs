using IW4.Game.Assets.Physics;
using MapConverter.Game.IW4.Xbox.FastFiles;
using static MapConverter.Game.IW4.Xbox.FastFiles.XboxWire;

namespace MapConverter.Game.IW4.Xbox.Physics;

internal static class XboxPhysCollmapWriter
{
    internal static void Write(XboxZoneStream zone, PhysCollmapAsset asset)
    {
        if (asset.Name is null || asset.Count != asset.Geoms.Count)
            throw new InvalidDataException("PhysCollmap requires a name and its retained geometry count.");
        foreach (PhysGeomInfo geom in asset.Geoms)
        {
            if (geom.Orientation.Count != 3)
                throw new InvalidDataException($"PhysCollmap '{asset.Name}' geometry requires three orientation rows.");
            if (geom.BrushWrapper is { } wrapper)
            {
                CBrush brush = wrapper.Brush;
                if (brush.NumSides != brush.Sides.Count ||
                    wrapper.TotalEdgeCount != brush.BaseAdjacentSide.Count ||
                    wrapper.Planes.Count != brush.NumSides ||
                    brush.AxialMaterialNum.Count != 6 ||
                    brush.FirstAdjacentSideOffsets.Count != 6 || brush.EdgeCount.Count != 6)
                    throw new InvalidDataException($"PhysCollmap '{asset.Name}' brush payload does not match its wire counts.");
            }
        }
        byte[] root = new byte[72];
        U32(root, 0, Inline); I32(root, 4, asset.Count); U32(root, 8, Present(asset.Count));
        Vector(root, 12, asset.Mass.CenterOfMass); Vector(root, 24, asset.Mass.MomentsOfInertia);
        Vector(root, 36, asset.Mass.ProductsOfInertia);
        Vector(root, 48, asset.Bounds.MidPoint); Vector(root, 60, asset.Bounds.HalfSize);
        zone.Write(root, 4, XboxBlock.Temp); zone.String(asset.Name);
        if (asset.Geoms.Count == 0) return;
        byte[] geoms = new byte[checked(asset.Geoms.Count * 68)];
        for (int i = 0; i < asset.Geoms.Count; i++)
        {
            var g = asset.Geoms[i];
            int at = i * 68;
            if (g.BrushWrapper != null) U32(geoms, at, Inline);
            I32(geoms, at + 4, (int)g.Type);
            for (int j = 0; j < 3; j++) Vector(geoms, at + 8 + j * 12, g.Orientation[j]);
            Vector(geoms, at + 44, g.Bounds.MidPoint); Vector(geoms, at + 56, g.Bounds.HalfSize);
        }
        zone.Write(geoms, 4);
        foreach (var g in asset.Geoms)
            if (g.BrushWrapper is { } wrapper) Brush(zone, wrapper);
    }

    private static void Brush(XboxZoneStream zone, BrushWrapper wrapper)
    {
        var brush = wrapper.Brush;
        byte[] bytes = new byte[68];
        Vector(bytes, 0, wrapper.Bounds.MidPoint); Vector(bytes, 12, wrapper.Bounds.HalfSize);
        U16(bytes, 24, brush.NumSides); U16(bytes, 26, brush.GlassPieceIndex);
        U32(bytes, 28, Present(brush.Sides.Count)); U32(bytes, 32, Present(brush.BaseAdjacentSide.Count));
        for (int i = 0; i < 6; i++)
        {
            U16(bytes, 36 + i * 2, unchecked((ushort)brush.AxialMaterialNum[i]));
            bytes[48 + i] = brush.FirstAdjacentSideOffsets[i]; bytes[54 + i] = brush.EdgeCount[i];
        }
        I32(bytes, 60, wrapper.TotalEdgeCount); U32(bytes, 64, Present(wrapper.Planes.Count));
        zone.Write(bytes, 4);
        var sides = new byte[checked(brush.Sides.Count * 8)];
        for (int i = 0; i < brush.Sides.Count; i++)
        {
            var s = brush.Sides[i];
            if (s.Plane != null) U32(sides, i * 8, Inline);
            U16(sides, i * 8 + 4, s.MaterialNum); sides[i * 8 + 6] = s.FirstAdjacentSideOffset; sides[i * 8 + 7] = s.EdgeCount;
        }
        if (sides.Length > 0) zone.Write(sides, 4);
        foreach (var side in brush.Sides)
            if (side.Plane is { } p)
            {
                byte[] plane = new byte[20]; Plane(plane, 0, p); zone.Write(plane, 4);
            }
        if (brush.BaseAdjacentSide.Count > 0) zone.Write(brush.BaseAdjacentSide.ToArray());
        if (wrapper.Planes.Count == 0) return;
        byte[] planes = new byte[checked(wrapper.Planes.Count * 20)];
        for (int i = 0; i < wrapper.Planes.Count; i++) Plane(planes, i * 20, wrapper.Planes[i]);
        zone.Write(planes, 4);
    }

    private static void Plane(byte[] data, int offset, CPlane plane)
    {
        Vector(data, offset, plane.Normal); Float(data, offset + 12, plane.Dist);
        data[offset + 16] = (byte)plane.Type;
    }
}
