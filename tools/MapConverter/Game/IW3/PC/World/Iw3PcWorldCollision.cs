using System.Buffers.Binary;
using System.Text;
using IW4.Formats.D3dbsp;
using MapConverter.IW3.PC.Readers;

namespace MapConverter.Game.IW3.PC.World;

/// <summary>Encodes the IW3 PC ClipMap and brush-model portions of a version-22 D3DBSP.</summary>
internal static class Iw3PcWorldCollision
{
    private sealed record MaterialRow(string Name, int SurfaceFlags, int Contents);

    internal static (ushort[] SurfaceMaterials, IReadOnlySet<string> DecalMaterials) AddLumps(Iw3PcZone zone, int clip, int gfx,
        List<(D3dbspLumpType Type, byte[] Data)> lumps, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(lumps);
        int materialCount = Count(zone, clip + 24);
        int materialBase = Required(zone, clip + 28, materialCount, "collision materials");
        int brushCount = U16(zone, clip + 140);
        int brushBase = Required(zone, clip + 144, brushCount, "collision brushes");
        if (materialCount == 0 || brushCount == 0)
            throw new InvalidDataException("ClipMap has no collision materials or brushes.");
        var materials = new List<MaterialRow>(materialCount + 32);
        for (int index = 0; index < materialCount; index++)
        {
            ReadOnlySpan<byte> nameBytes = zone.Bytes(materialBase + index * 72, 64);
            int end = nameBytes.IndexOf((byte)0);
            if (end < 1)
                throw new InvalidDataException($"Collision material {index} has no valid terminated name.");
            materials.Add(new MaterialRow(Encoding.Latin1.GetString(nameBytes[..end]),
                I32(zone, materialBase + index * 72 + 64), I32(zone, materialBase + index * 72 + 68)));
        }

        int planeCount = Count(zone, clip + 8);
        int planeBase = Required(zone, clip + 12, planeCount, "collision planes");
        if (planeCount == 0) throw new InvalidDataException("ClipMap has no collision planes.");
        byte[] planes = new byte[checked(planeCount * 16)];
        for (int index = 0; index < planeCount; index++)
            zone.Bytes(planeBase + index * 20, 16).CopyTo(planes.AsSpan(index * 16));

        var brushSelectors = new short[brushCount];
        for (int index = 0; index < brushCount; index++)
        {
            int contents = I32(zone, brushBase + index * 80 + 12);
            int found = materials.FindIndex(row => row.Contents == contents);
            if (found < 0)
            {
                if (materials.Count >= short.MaxValue)
                    throw new InvalidDataException("Generated collision material table exceeds the signed-short range.");
                found = materials.Count;
                materials.Add(new MaterialRow("$default", 0, contents));
            }
            brushSelectors[index] = checked((short)found);
        }
        int collisionMaterialCount = materials.Count;
        int surfaceCount = Count(zone, gfx + 24);
        int dpvsSurfaces = Required(zone, gfx + 580 + 80, surfaceCount, "render surfaces");
        if (surfaceCount == 0) throw new InvalidDataException("GfxWorld has no render surfaces.");
        var renderMaterialIndices = new ushort[surfaceCount];
        var decalMaterials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < surfaceCount; index++)
        {
            token.ThrowIfCancellationRequested();
            int surface = dpvsSurfaces + index * 48;
            int material = Required(zone, surface + 16, 1, "render surface material");
            string name = zone.String(material);
            if (name.StartsWith(",", StringComparison.Ordinal)) name = name[1..];
            if (name.Length == 0 || name.Contains(','))
                throw new InvalidDataException($"Render surface {index} has an invalid material name.");
            // R_BuildNoDecalSubModels marks a surface whose triangles overlay earlier geometry.
            if ((zone.Bytes(surface + 23, 1)[0] & 2) != 0)
                decalMaterials.Add(name);
            if (name.StartsWith("wc/", StringComparison.Ordinal)) name = name[3..];
            if (name.Length is 0 or >= 64 || Encoding.Latin1.GetByteCount(name) >= 64)
                throw new InvalidDataException($"Render surface {index} has an invalid target material name.");
            int found = materials.FindIndex(row => row.Name == name);
            if (found < 0)
            {
                if (materials.Count > ushort.MaxValue)
                    throw new InvalidDataException("Combined material table exceeds the ushort range.");
                found = materials.Count;
                materials.Add(new MaterialRow(name, 0, 0));
            }
            renderMaterialIndices[index] = checked((ushort)found);
        }
        byte[] materialBytes = new byte[checked(materials.Count * 72)];
        for (int index = 0; index < materials.Count; index++)
        {
            MaterialRow row = materials[index];
            byte[] name = Encoding.Latin1.GetBytes(row.Name);
            if (name.Length is 0 or >= 64 || name[0] == (byte)',')
                throw new InvalidDataException($"Material row {index} has an invalid output name.");
            name.CopyTo(materialBytes.AsSpan(index * 72));
            Put32(materialBytes, index * 72 + 64, row.SurfaceFlags);
            Put32(materialBytes, index * 72 + 68, row.Contents);
        }

        (byte[] sides, byte[] edgeCounts, byte[] edges, byte[] brushes) =
            EncodeBrushes(zone, clip, brushBase, brushCount, planeBase, planeCount,
                materials.Count, brushSelectors, token);
        byte[] nodes = EncodeNodes(zone, clip, planeBase, planeCount);
        (byte[] leafs, byte[] leafBrushes) = EncodeLeafs(zone, clip);
        byte[] leafSurfaces = EncodeLeafSurfaces(zone, clip, surfaceCount);
        byte[] verts = EncodeVerts(zone, clip);
        (byte[] tris, byte[] walkable) = EncodeTriangles(zone, clip);
        byte[] borders = EncodeBorders(zone, clip);
        byte[] partitions = EncodePartitions(zone, clip);
        byte[] aabbs = EncodeAabbs(zone, clip, collisionMaterialCount);
        byte[] models = EncodeModels(zone, clip, gfx, surfaceCount);

        lumps.Add((D3dbspLumpType.Materials, materialBytes));
        lumps.Add((D3dbspLumpType.Planes, planes));
        lumps.Add((D3dbspLumpType.BrushSides, sides));
        lumps.Add((D3dbspLumpType.BrushSideEdgeCounts, edgeCounts));
        AddOptional(lumps, D3dbspLumpType.BrushEdges, edges);
        lumps.Add((D3dbspLumpType.Brushes, brushes));
        lumps.Add((D3dbspLumpType.Nodes, nodes));
        lumps.Add((D3dbspLumpType.Leafs, leafs));
        AddOptional(lumps, D3dbspLumpType.LeafBrushes, leafBrushes);
        AddOptional(lumps, D3dbspLumpType.LeafSurfaces, leafSurfaces);
        AddOptional(lumps, D3dbspLumpType.CollisionVerts, verts);
        AddOptional(lumps, D3dbspLumpType.CollisionTris, tris);
        AddOptional(lumps, D3dbspLumpType.CollisionEdgeWalkable, walkable);
        AddOptional(lumps, D3dbspLumpType.CollisionBorders, borders);
        AddOptional(lumps, D3dbspLumpType.CollisionPartitions, partitions);
        AddOptional(lumps, D3dbspLumpType.CollisionAabbs, aabbs);
        lumps.Add((D3dbspLumpType.Models, models));
        return (renderMaterialIndices, decalMaterials);
    }

    private static (byte[], byte[], byte[], byte[]) EncodeBrushes(Iw3PcZone zone, int clip,
        int brushBase, int brushCount, int planeBase, int planeCount,
        int outputMaterialCount, short[] selectors, CancellationToken token)
    {
        int declaredSides = Count(zone, clip + 32);
        int declaredEdges = Count(zone, clip + 40);
        int edgeBase = Required(zone, clip + 44, declaredEdges, "brush edges");
        int sideCount = 0, edgeCount = 0;
        for (int index = 0; index < brushCount; index++)
        {
            int brush = brushBase + index * 80;
            int count = Count(zone, brush + 28);
            if (count > short.MaxValue - 6)
                throw new InvalidDataException("Collision brush has too many nonaxial sides.");
            Required(zone, brush + 32, count, "collision brush sides");
            sideCount = checked(sideCount + count);
            for (int side = 0; side < 6; side++) edgeCount = checked(edgeCount + U8(zone, brush + 64 + side));
            int sides = zone.Pointer(brush + 32);
            for (int side = 0; side < count; side++) edgeCount = checked(edgeCount + U8(zone, sides + side * 12 + 10));
        }
        if (sideCount != declaredSides || edgeCount != declaredEdges)
            throw new InvalidDataException("ClipMap brush side or edge counts do not cover their tables.");
        int diskSides = checked(brushCount * 6 + sideCount);
        byte[] sidesOut = new byte[checked(diskSides * 8)];
        byte[] edgeCounts = new byte[diskSides];
        byte[] edges = new byte[declaredEdges];
        byte[] brushes = new byte[checked(brushCount * 4)];
        var consumed = new bool[declaredEdges];
        int nextSide = 0, nextEdge = 0;
        for (int index = 0; index < brushCount; index++)
        {
            token.ThrowIfCancellationRequested();
            int brush = brushBase + index * 80;
            int count = Count(zone, brush + 28);
            int brushEdgeBytes = 0;
            for (int axis = 0; axis < 3; axis++)
                for (int direction = 0; direction < 2; direction++)
                {
                    if (brushEdgeBytes > byte.MaxValue || I16(zone, brush + 52 + direction * 6 + axis * 2) != brushEdgeBytes)
                        throw new InvalidDataException($"Collision brush {index} has a noncanonical axial adjacency offset.");
                    int row = nextSide++ * 8;
                    zone.Bytes(brush + (direction == 0 ? axis * 4 : 16 + axis * 4), 4)
                        .CopyTo(sidesOut.AsSpan(row));
                    int material = I16(zone, brush + 36 + direction * 6 + axis * 2);
                    if (material < 0 || material >= outputMaterialCount)
                        throw new InvalidDataException("Collision brush axial material is outside the material table.");
                    Put32(sidesOut, row + 4, material);
                    byte countEdges = U8(zone, brush + 64 + direction * 3 + axis);
                    edgeCounts[nextSide - 1] = countEdges;
                    brushEdgeBytes += countEdges;
                }
            int sideBase = zone.Pointer(brush + 32);
            for (int sideIndex = 0; sideIndex < count; sideIndex++)
            {
                int side = sideBase + sideIndex * 12;
                if (brushEdgeBytes > byte.MaxValue || I16(zone, side + 8) != brushEdgeBytes)
                    throw new InvalidDataException($"Collision brush {index} has a noncanonical side adjacency offset.");
                int plane = PointerIndex(zone.Pointer(side), planeBase, planeCount, 20, "brush side plane");
                uint material = U32(zone, side + 4);
                if (material >= outputMaterialCount)
                    throw new InvalidDataException("Collision brush side material is outside the material table.");
                int row = nextSide++ * 8;
                Put32(sidesOut, row, plane);
                Put32(sidesOut, row + 4, checked((int)material));
                byte countEdges = U8(zone, side + 10);
                edgeCounts[nextSide - 1] = countEdges;
                brushEdgeBytes += countEdges;
            }
            if (brushEdgeBytes > 0)
            {
                int sourceFirst = PointerIndex(zone.Pointer(brush + 48), edgeBase, declaredEdges, 1, "brush edge base");
                if (brushEdgeBytes > declaredEdges - sourceFirst)
                    throw new InvalidDataException("Collision brush adjacency exceeds the edge table.");
                for (int edge = 0; edge < brushEdgeBytes; edge++)
                {
                    int sourceIndex = sourceFirst + edge;
                    byte sideIndex = U8(zone, edgeBase + sourceIndex);
                    if (consumed[sourceIndex] || sideIndex >= count + 6)
                        throw new InvalidDataException("Collision brush adjacency overlaps or references an invalid side.");
                    consumed[sourceIndex] = true;
                    edges[nextEdge++] = sideIndex;
                }
            }
            Put16(brushes, index * 4, count + 6);
            Put16(brushes, index * 4 + 2, selectors[index]);
        }
        if (nextSide != diskSides || nextEdge != declaredEdges || consumed.Any(value => !value))
            throw new InvalidDataException("Collision brush serialization did not cover every side or edge.");
        return (sidesOut, edgeCounts, edges, brushes);
    }

    private static byte[] EncodeNodes(Iw3PcZone zone, int clip, int planeBase, int planeCount)
    {
        int count = Count(zone, clip + 48);
        int source = Required(zone, clip + 52, count, "collision nodes");
        if (count == 0) throw new InvalidDataException("ClipMap has no collision nodes.");
        byte[] output = new byte[checked(count * 36)];
        for (int index = 0; index < count; index++)
        {
            int node = source + index * 8, row = index * 36;
            Put32(output, row, PointerIndex(zone.Pointer(node), planeBase, planeCount, 20, "node plane"));
            Put32(output, row + 4, I16(zone, node + 4));
            Put32(output, row + 8, I16(zone, node + 6));
        }
        return output;
    }

    private static (byte[], byte[]) EncodeLeafs(Iw3PcZone zone, int clip)
    {
        int count = Count(zone, clip + 56), aabbCount = Count(zone, clip + 124);
        int source = Required(zone, clip + 60, count, "collision leafs");
        if (count == 0) throw new InvalidDataException("ClipMap has no collision leafs.");
        byte[] output = new byte[checked(count * 24)];
        var brushes = new List<byte>();
        for (int index = 0; index < count; index++)
        {
            int leaf = source + index * 44, row = index * 24;
            int first = U16(zone, leaf), number = U16(zone, leaf + 2);
            if (first + number > aabbCount) throw new InvalidDataException("Collision leaf AABB slice exceeds its table.");
            ushort[] leafBrushes = CollectLeafBrushes(zone, clip, I32(zone, leaf + 36));
            Put32(output, row + 4, first);
            Put32(output, row + 8, number);
            Put32(output, row + 12, brushes.Count / 4);
            Put32(output, row + 16, leafBrushes.Length);
            foreach (ushort brush in leafBrushes)
            {
                int value = brush;
                var encoded = new byte[4];
                Put32(encoded, 0, value);
                brushes.AddRange(encoded);
            }
        }
        return (output, brushes.ToArray());
    }

    private static ushort[] CollectLeafBrushes(Iw3PcZone zone, int clip, int root)
    {
        if (root == 0) return [];
        int count = Count(zone, clip + 64), source = Required(zone, clip + 68, count, "leaf-brush nodes");
        if (root < 0 || root >= count) throw new InvalidDataException("Invalid leaf-brush root.");
        int brushCount = U16(zone, clip + 140);
        var nodes = new HashSet<int>();
        var seenBrushes = new HashSet<ushort>();
        var result = new List<ushort>();
        Visit(root);
        return result.ToArray();

        void Visit(int index)
        {
            if (index <= 0 || index >= count || !nodes.Add(index))
                throw new InvalidDataException("Invalid or repeated leaf-brush branch.");
            int node = source + index * 20;
            int leafCount = I16(zone, node + 2);
            if (leafCount > 0)
            {
                int list = Required(zone, node + 8, leafCount, "terminal leaf brushes");
                for (int brush = 0; brush < leafCount; brush++)
                {
                    ushort number = U16(zone, list + brush * 2);
                    if (number >= brushCount || !seenBrushes.Add(number))
                        throw new InvalidDataException("Leaf-brush branch repeats or exceeds a brush.");
                    result.Add(number);
                }
                return;
            }
            if (leafCount is not (0 or -1)) throw new InvalidDataException("Unsupported leaf-brush count.");
            if (leafCount == -1) Visit(index + 1);
            for (int child = 0; child < 2; child++)
            {
                int offset = U16(zone, node + 16 + child * 2);
                if (offset == 0) throw new InvalidDataException("Leaf-brush branch has a zero child offset.");
                Visit(checked(index + offset));
            }
        }
    }

    private static byte[] EncodeLeafSurfaces(Iw3PcZone zone, int clip, int surfaceCount)
    {
        int count = Count(zone, clip + 80), source = Required(zone, clip + 84, count, "leaf surfaces");
        byte[] output = new byte[checked(count * 4)];
        for (int index = 0; index < count; index++)
        {
            uint surface = U32(zone, source + index * 4);
            if (surface >= surfaceCount) throw new InvalidDataException("Leaf surface exceeds the render table.");
            Put32(output, index * 4, checked((int)surface));
        }
        return output;
    }

    private static byte[] EncodeVerts(Iw3PcZone zone, int clip)
    {
        int count = Count(zone, clip + 88), source = Required(zone, clip + 92, count, "collision vertices");
        return count == 0 ? [] : zone.Bytes(source, checked(count * 12)).ToArray();
    }

    private static (byte[], byte[]) EncodeTriangles(Iw3PcZone zone, int clip)
    {
        int count = Count(zone, clip + 96), verts = Count(zone, clip + 88);
        int source = Required(zone, clip + 100, count, "collision triangle indices");
        byte[] output = count == 0 ? [] : zone.Bytes(source, checked(count * 6)).ToArray();
        for (int index = 0; index < count * 3; index++)
            if (BinaryPrimitives.ReadUInt16LittleEndian(output.AsSpan(index * 2)) >= verts)
                throw new InvalidDataException("Collision triangle index exceeds the vertex table.");
        int walkBytes = checked(((checked(count * 3) + 31) / 32) * 4);
        int walkSource = Required(zone, clip + 104, walkBytes, "collision walkability");
        return (output, walkBytes == 0 ? [] : zone.Bytes(walkSource, walkBytes).ToArray());
    }

    private static byte[] EncodeBorders(Iw3PcZone zone, int clip)
    {
        int count = Count(zone, clip + 108), source = Required(zone, clip + 112, count, "collision borders");
        return count == 0 ? [] : zone.Bytes(source, checked(count * 28)).ToArray();
    }

    private static byte[] EncodePartitions(Iw3PcZone zone, int clip)
    {
        int count = Count(zone, clip + 116), source = Required(zone, clip + 120, count, "collision partitions");
        int borderCount = Count(zone, clip + 108), borderBase = zone.Pointer(clip + 112), triCount = Count(zone, clip + 96);
        byte[] output = new byte[checked(count * 12)];
        int nextBorder = 0, coveredTris = 0;
        for (int index = 0; index < count; index++)
        {
            int partition = source + index * 12, row = index * 12;
            byte tris = U8(zone, partition), borders = U8(zone, partition + 1);
            int firstTri = I32(zone, partition + 4);
            if (firstTri < 0 || (long)firstTri + tris > triCount)
                throw new InvalidDataException("Collision partition triangle slice exceeds its table.");
            if (borders > 0 && PointerIndex(zone.Pointer(partition + 8), borderBase,
                    borderCount, 28, "partition border") != nextBorder)
                throw new InvalidDataException("Collision partition borders are not contiguous.");
            if (nextBorder + borders > borderCount)
                throw new InvalidDataException("Collision partition border slice exceeds its table.");
            output[row + 2] = tris;
            output[row + 3] = borders;
            Put32(output, row + 4, firstTri);
            Put32(output, row + 8, nextBorder);
            nextBorder += borders;
            coveredTris += tris;
        }
        if (nextBorder != borderCount || coveredTris != triCount)
            throw new InvalidDataException("Collision partitions do not cover every border and triangle.");
        return output;
    }

    private static byte[] EncodeAabbs(Iw3PcZone zone, int clip, int materialCount)
    {
        int count = Count(zone, clip + 124), source = Required(zone, clip + 128, count, "collision AABBs");
        byte[] output = new byte[checked(count * 32)];
        for (int index = 0; index < count; index++)
        {
            int tree = source + index * 32, row = index * 32;
            if (U16(zone, tree + 24) >= materialCount)
                throw new InvalidDataException("Collision AABB material exceeds the table.");
            zone.Bytes(tree, 32).CopyTo(output.AsSpan(row));
        }
        return output;
    }

    private static byte[] EncodeModels(Iw3PcZone zone, int clip, int gfx, int surfaceCount)
    {
        int count = Count(zone, gfx + 336), source = Required(zone, gfx + 340, count, "brush models");
        int collisionCount = Count(zone, clip + 132), collision = Required(zone, clip + 136, collisionCount, "collision models");
        if (count == 0 || count != collisionCount)
            throw new InvalidDataException("Render and collision brush-model counts differ or are empty.");
        int aabbCount = Count(zone, clip + 124);
        byte[] output = new byte[checked(count * 48)];
        for (int index = 0; index < count; index++)
        {
            int model = source + index * 56, row = index * 48;
            for (int axis = 0; axis < 3; axis++)
                if (F32(zone, model + 24 + axis * 4) > F32(zone, model + 36 + axis * 4))
                    throw new InvalidDataException("Brush model has reversed bounds.");
            int number = U16(zone, model + 48), first = U16(zone, model + 50);
            if (number > 0 && first + number > surfaceCount)
                throw new InvalidDataException("Brush model surface slice exceeds the render table.");
            zone.Bytes(model + 24, 24).CopyTo(output.AsSpan(row));
            Put16(output, row + 26, number == 0 ? 0 : first);
            Put16(output, row + 30, number);
            if (index == 0) continue;
            int leaf = collision + index * 72 + 28;
            int aabbFirst = U16(zone, leaf), aabbNumber = U16(zone, leaf + 2);
            if (aabbFirst + aabbNumber > aabbCount)
                throw new InvalidDataException("Collision model AABB slice exceeds its table.");
            ushort[] brushes = CollectLeafBrushes(zone, clip, I32(zone, leaf + 36));
            // IW3 spatially partitions a contiguous disk range, so traversal need not be sorted.
            int firstBrush = brushes.Length == 0 ? 0 : brushes.Min();
            if (brushes.Length > 0 && brushes.Max() - firstBrush + 1 != brushes.Length)
                throw new InvalidDataException(
                    $"Collision model {index} references {brushes.Length} brushes spanning " +
                    $"{firstBrush}..{brushes.Max()}; a brush model requires one contiguous slice.");
            Put32(output, row + 32, aabbFirst);
            Put32(output, row + 36, aabbNumber);
            Put32(output, row + 40, firstBrush);
            Put32(output, row + 44, brushes.Length);
        }
        return output;
    }

    private static void AddOptional(List<(D3dbspLumpType Type, byte[] Data)> lumps,
        D3dbspLumpType type, byte[] data)
    { if (data.Length != 0) lumps.Add((type, data)); }

    private static int PointerIndex(int pointer, int first, int count, int stride, string description)
    {
        long difference = (long)pointer - first;
        if (pointer == 0 || first == 0 || difference < 0 || difference % stride != 0 ||
            difference / stride >= count)
            throw new InvalidDataException($"{description} does not point into its owning table.");
        return checked((int)(difference / stride));
    }
    private static int Required(Iw3PcZone zone, int field, int count, string description)
    {
        int pointer = zone.Pointer(field);
        if (count > 0 && pointer == 0) throw new InvalidDataException($"Missing {description}.");
        return pointer;
    }
    private static int Count(Iw3PcZone zone, int field)
    {
        int value = I32(zone, field);
        if (value < 0) throw new InvalidDataException($"Negative IW3 world count at 0x{field:X}.");
        return value;
    }
    private static byte U8(Iw3PcZone zone, int field) => zone.Bytes(field, 1)[0];
    private static short I16(Iw3PcZone zone, int field) => BinaryPrimitives.ReadInt16LittleEndian(zone.Bytes(field, 2));
    private static ushort U16(Iw3PcZone zone, int field) => BinaryPrimitives.ReadUInt16LittleEndian(zone.Bytes(field, 2));
    private static int I32(Iw3PcZone zone, int field) => BinaryPrimitives.ReadInt32LittleEndian(zone.Bytes(field, 4));
    private static uint U32(Iw3PcZone zone, int field) => BinaryPrimitives.ReadUInt32LittleEndian(zone.Bytes(field, 4));
    private static float F32(Iw3PcZone zone, int field) => BinaryPrimitives.ReadSingleLittleEndian(zone.Bytes(field, 4));
    private static void Put16(byte[] output, int field, int value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(field), checked((ushort)value));
    private static void Put32(byte[] output, int field, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(field), value);
}
