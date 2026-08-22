using System.Buffers.Binary;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.Physics;
using IW4.Assets.Math;

namespace IW4Map;

internal static class D3dbspCollisionCodec
{
    private const int DiskPlaneSize = 16;
    private const int DiskLeafBrushSize = 4;
    private const int DiskLeafSurfaceSize = 4;
    private const int DiskCollisionVertSize = 12;
    private const int DiskCollisionIndexSize = 2;
    private const int DiskCollisionBorderSize = 28;
    private const int DiskCollisionAabbSize = 32;

    public static byte[] EncodePlanes(IReadOnlyList<CPlane> planes)
    {
        ArgumentNullException.ThrowIfNull(planes);
        var data = new byte[checked(planes.Count * DiskPlaneSize)];
        for (int index = 0; index < planes.Count; index++)
        {
            CPlane plane = planes[index] ??
                throw new InvalidDataException($"Collision plane row {index} is null.");
            Span<byte> row = data.AsSpan(index * DiskPlaneSize, DiskPlaneSize);
            WriteVec3(row, 0, plane.Normal);
            WriteSingle(row, 12, plane.Dist);
        }

        return data;
    }

    public static byte[] EncodeBrushEdges(IReadOnlyList<byte> brushEdges) =>
        EncodeBytes(brushEdges);

    public static byte[] EncodeBrushSideEdgeCounts(IReadOnlyList<CBrush> brushes)
    {
        ArgumentNullException.ThrowIfNull(brushes);
        int byteCount = 0;
        for (int brushIndex = 0; brushIndex < brushes.Count; brushIndex++)
        {
            CBrush brush = brushes[brushIndex] ??
                throw new InvalidDataException($"Collision brush row {brushIndex} is null.");
            if (brush.Sides.Count != brush.NumSides)
            {
                throw new InvalidDataException(
                    $"Collision brush row {brushIndex} has {brush.Sides.Count} non-axial sides; expected {brush.NumSides}.");
            }
            if (brush.EdgeCount.Count != 6)
            {
                throw new InvalidDataException(
                    $"Collision brush row {brushIndex} has {brush.EdgeCount.Count} axial edge counts; expected 6.");
            }

            byteCount = checked(byteCount + 6 + brush.NumSides);
        }

        var data = new byte[byteCount];
        int offset = 0;
        for (int brushIndex = 0; brushIndex < brushes.Count; brushIndex++)
        {
            CBrush brush = brushes[brushIndex];
            // The fastfile stores edgeCount[2][3] direction-major; the BSP lists both directions per axis.
            for (int axis = 0; axis < 3; axis++)
            {
                data[offset++] = brush.EdgeCount[axis];
                data[offset++] = brush.EdgeCount[axis + 3];
            }
            for (int sideIndex = 0; sideIndex < brush.Sides.Count; sideIndex++)
            {
                CBrushSide side = brush.Sides[sideIndex] ??
                    throw new InvalidDataException(
                        $"Collision brush row {brushIndex} side {sideIndex} is null.");
                data[offset++] = side.EdgeCount;
            }
        }

        return data;
    }

    public static byte[] EncodeLeafBrushes(IReadOnlyList<ushort> leafBrushes)
    {
        ArgumentNullException.ThrowIfNull(leafBrushes);
        var data = new byte[checked(leafBrushes.Count * DiskLeafBrushSize)];
        for (int index = 0; index < leafBrushes.Count; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                data.AsSpan(index * DiskLeafBrushSize, DiskLeafBrushSize),
                leafBrushes[index]);
        }

        return data;
    }

    public static byte[] EncodeLeafSurfaces(IReadOnlyList<uint> leafSurfaces)
    {
        ArgumentNullException.ThrowIfNull(leafSurfaces);
        var data = new byte[checked(leafSurfaces.Count * DiskLeafSurfaceSize)];
        for (int index = 0; index < leafSurfaces.Count; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                data.AsSpan(index * DiskLeafSurfaceSize, DiskLeafSurfaceSize),
                leafSurfaces[index]);
        }

        return data;
    }

    public static byte[] EncodeCollisionVerts(IReadOnlyList<Vec3> verts)
    {
        ArgumentNullException.ThrowIfNull(verts);
        var data = new byte[checked(verts.Count * DiskCollisionVertSize)];
        for (int index = 0; index < verts.Count; index++)
        {
            WriteVec3(
                data.AsSpan(index * DiskCollisionVertSize, DiskCollisionVertSize),
                0,
                verts[index]);
        }

        return data;
    }

    public static byte[] EncodeCollisionTris(IReadOnlyList<ushort> triIndices)
    {
        ArgumentNullException.ThrowIfNull(triIndices);
        if (triIndices.Count % 3 != 0)
        {
            throw new InvalidDataException(
                $"Collision triangle index count {triIndices.Count} is not divisible by 3.");
        }

        var data = new byte[checked(triIndices.Count * DiskCollisionIndexSize)];
        for (int index = 0; index < triIndices.Count; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                data.AsSpan(index * DiskCollisionIndexSize, DiskCollisionIndexSize),
                triIndices[index]);
        }

        return data;
    }

    public static byte[] EncodeCollisionEdgeWalkable(IReadOnlyList<byte> edgeWalkable) =>
        EncodeBytes(edgeWalkable);

    public static byte[] EncodeCollisionBorders(IReadOnlyList<CollisionBorder> borders)
    {
        ArgumentNullException.ThrowIfNull(borders);
        var data = new byte[checked(borders.Count * DiskCollisionBorderSize)];
        for (int index = 0; index < borders.Count; index++)
        {
            CollisionBorder border = borders[index] ??
                throw new InvalidDataException($"Collision border row {index} is null.");
            if (border.DistEq.Count != 3)
            {
                throw new InvalidDataException(
                    $"Collision border row {index} has {border.DistEq.Count} distance-equation values; expected 3.");
            }

            Span<byte> row = data.AsSpan(
                index * DiskCollisionBorderSize,
                DiskCollisionBorderSize);
            WriteSingle(row, 0, border.DistEq[0]);
            WriteSingle(row, 4, border.DistEq[1]);
            WriteSingle(row, 8, border.DistEq[2]);
            WriteSingle(row, 12, border.ZBase);
            WriteSingle(row, 16, border.ZSlope);
            WriteSingle(row, 20, border.Start);
            WriteSingle(row, 24, border.Length);
        }

        return data;
    }

    public static byte[] EncodeCollisionAabbs(IReadOnlyList<CollisionAabbTree> aabbTrees)
    {
        ArgumentNullException.ThrowIfNull(aabbTrees);
        var data = new byte[checked(aabbTrees.Count * DiskCollisionAabbSize)];
        for (int index = 0; index < aabbTrees.Count; index++)
        {
            CollisionAabbTree tree = aabbTrees[index] ??
                throw new InvalidDataException($"Collision AABB row {index} is null.");
            Span<byte> row = data.AsSpan(
                index * DiskCollisionAabbSize,
                DiskCollisionAabbSize);
            WriteVec3(row, 0, tree.Origin);
            WriteVec3(row, 12, tree.HalfSize);
            BinaryPrimitives.WriteUInt16LittleEndian(row[24..], tree.MaterialIndex);
            BinaryPrimitives.WriteUInt16LittleEndian(row[26..], tree.ChildCount);
            BinaryPrimitives.WriteInt32LittleEndian(row[28..], tree.FirstChildOrPartitionIndex);
        }

        return data;
    }

    private static byte[] EncodeBytes(IReadOnlyList<byte> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var data = new byte[values.Count];
        for (int index = 0; index < values.Count; index++)
            data[index] = values[index];

        return data;
    }

    private static void WriteVec3(Span<byte> row, int offset, Vec3 value)
    {
        WriteSingle(row, offset, value.X);
        WriteSingle(row, offset + 4, value.Y);
        WriteSingle(row, offset + 8, value.Z);
    }

    private static void WriteSingle(Span<byte> row, int offset, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(row[offset..], BitConverter.SingleToInt32Bits(value));
}
