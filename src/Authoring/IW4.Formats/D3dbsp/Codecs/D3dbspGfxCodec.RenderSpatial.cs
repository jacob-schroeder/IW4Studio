using System.Buffers.Binary;
using IW4.Formats.D3dbsp;
using IW4.Game.Assets.GfxMap;
using IW4.Game.Math;

namespace IW4.Formats.Codecs.D3dbsp;

internal static partial class D3dbspGfxCodec
{
    private const int DiskCellSize = 112;
    private const int DiskSpatialTreeSize = 40;
    private const int DiskSpatialPortalSize = 44;

    public static (byte[] Cells, byte[] RenderSpatial) EncodeRenderSpatial(GfxWorldAsset world)
    {
        ArgumentNullException.ThrowIfNull(world);
        int cellCount = world.DpvsPlanes.CellCount;
        if (cellCount <= 1 || cellCount >= ushort.MaxValue ||
            world.Cells.Count != cellCount || world.CellTrees.Count != cellCount)
            throw new InvalidDataException("Multi-cell render spatial rows must agree with DpvsPlanes.CellCount.");
        if (world.Dpvs.SModelCount != 0)
            throw new NotSupportedException("Multi-cell d3dbsp encoding currently supports no authored static models.");
        int staticCount = checked((int)world.Dpvs.StaticSurfaceCount);
        if (world.Models[0].StartSurfIndex != 0 || world.Models[0].SurfaceCount != staticCount ||
            world.Dpvs.SortedSurfIndex.Count != staticCount ||
            world.Dpvs.SortedSurfIndex.Any(index => index >= staticCount) ||
            world.Dpvs.SortedSurfIndex.Distinct().Count() != world.Dpvs.SortedSurfIndex.Count)
            throw new InvalidDataException("Multi-cell d3dbsp encoding requires a complete permutation of world surface indices.");
        if (world.DpvsPlanes.Nodes.Count == 0)
            throw new InvalidDataException("The multi-cell render camera partition has no root node.");
        ValidateRenderPartition(world.DpvsPlanes.Nodes.ToArray(), cellCount, world.DpvsPlanes.Planes.Count);

        var cellData = new byte[checked(cellCount * DiskCellSize)];
        using var spatialStream = new MemoryStream();
        using var writer = new BinaryWriter(spatialStream);
        writer.Write("IW4PS3S\u0001"u8);
        writer.Write(checked((uint)world.DpvsPlanes.Planes.Count));
        writer.Write(checked((uint)world.DpvsPlanes.Nodes.Count));
        writer.Write(0u); // Authored static-model prefix; MapModelCompiler appends models later.
        foreach (DpvsPlane plane in world.DpvsPlanes.Planes)
        {
            WriteFinite(writer, plane.NormalX, "Render plane normal");
            WriteFinite(writer, plane.NormalY, "Render plane normal");
            WriteFinite(writer, plane.NormalZ, "Render plane normal");
            WriteFinite(writer, plane.Distance, "Render plane distance");
            writer.Write(plane.Type);
            writer.Write(plane.SignBits);
            writer.Write(plane.Pad12);
        }
        foreach (ushort node in world.DpvsPlanes.Nodes)
            writer.Write(node);
        var authoredIndices = world.Dpvs.AuthoredSurfaceIndexByRuntimeSlot;
        foreach (ushort surface in world.Dpvs.SortedSurfIndex)
            writer.Write(authoredIndices.Count == 0 ? surface : checked((ushort)authoredIndices[surface]));

        for (int index = 0; index < cellCount; index++)
        {
            GfxCell cell = world.Cells[index] ?? throw new InvalidDataException($"Render cell {index} is null.");
            if (cell.PortalCount != cell.Portals.Count || cell.ReflectionProbeCount != cell.ReflectionProbes.Count ||
                cell.Pad21.Count is not (0 or 3) || cell.Pad21.Any(value => value != 0) || cell.ReflectionProbes.Count > DiskCellSize - 45)
                throw new InvalidDataException($"Render cell {index} has inconsistent or unsupported metadata.");
            Vec3 mins = BoundsEndpoint(cell.Bounds, maximum: false, $"Render cell {index}");
            Vec3 maxs = BoundsEndpoint(cell.Bounds, maximum: true, $"Render cell {index}");
            Span<byte> row = cellData.AsSpan(index * DiskCellSize, DiskCellSize);
            WriteVec3LittleEndian(row, 0, mins);
            WriteVec3LittleEndian(row, 12, maxs);
            row[44] = cell.ReflectionProbeCount;
            for (int probe = 0; probe < cell.ReflectionProbes.Count; probe++)
                row[45 + probe] = cell.ReflectionProbes[probe];

            IReadOnlyList<GfxAabbTree> trees = world.CellTrees[index].AabbTrees;
            if (trees.Count != 1)
                throw new NotSupportedException($"Render cell {index} must contain exactly one terminal AABB tree.");
            GfxAabbTree tree = trees[0];
            if (tree.ChildCount != 0 || tree.ChildrenOffset != 0 || tree.SModelIndexCount != 0 || tree.SModelIndexes.Count != 0)
                throw new NotSupportedException($"Render cell {index} AABB must be a terminal tree with no static models.");
            if (tree.SurfaceCount > GfxAabbTree.MaxIndexCount)
                throw new InvalidDataException($"Render cell {index} exceeds the PS3 AABB surface-list limit.");
            ValidateSlice(tree.StartSurfIndex, tree.SurfaceCount, world.Dpvs.SortedSurfIndex.Count,
                $"Render cell {index} AABB surface");
            writer.Write(1u); // tree count
            writer.Write(checked((uint)cell.Portals.Count));
            writer.Write(0u); // root model-index count
            WriteBounds(writer, tree.Bounds, $"Render cell {index} AABB");
            writer.Write((ushort)0);
            writer.Write(tree.SurfaceCount);
            writer.Write(tree.StartSurfIndex);
            writer.Write((ushort)0);
            writer.Write(0u); // model-list offset
            writer.Write(0u); // child offset
            foreach (GfxPortal portal in cell.Portals)
                WriteSpatialPortal(writer, portal, index, cellCount);
        }
        return (cellData, spatialStream.ToArray());
    }

    private static void WriteSpatialPortal(BinaryWriter writer, GfxPortal portal, int cellIndex, int cellCount)
    {
        if (portal.CellIndex >= cellCount || portal.CellIndex == cellIndex || portal.VertexCount != portal.Vertices.Count ||
            portal.VertexCount < 3 || portal.HullAxis.Count != 6 || portal.Pad23 != 0)
            throw new InvalidDataException($"Render cell {cellIndex} has an invalid portal.");
        WriteFinite(writer, portal.Plane.NormalX, "Render portal plane");
        WriteFinite(writer, portal.Plane.NormalY, "Render portal plane");
        WriteFinite(writer, portal.Plane.NormalZ, "Render portal plane");
        WriteFinite(writer, portal.Plane.Distance, "Render portal plane");
        writer.Write(portal.CellIndex);
        writer.Write(portal.VertexCount);
        writer.Write((byte)0);
        foreach (float axis in portal.HullAxis)
            WriteFinite(writer, axis, "Render portal hull axis");
        foreach (GfxPortalVertex vertex in portal.Vertices)
        {
            WriteFinite(writer, vertex.X, "Render portal vertex");
            WriteFinite(writer, vertex.Y, "Render portal vertex");
            WriteFinite(writer, vertex.Z, "Render portal vertex");
        }
    }

    private static void WriteBounds(BinaryWriter writer, Bounds bounds, string description)
    {
        Vec3 midpoint = bounds.MidPoint;
        Vec3 halfSize = bounds.HalfSize;
        if (halfSize.X < 0 || halfSize.Y < 0 || halfSize.Z < 0)
            throw new InvalidDataException($"{description} has a negative half-size.");
        WriteFinite(writer, midpoint.X, description);
        WriteFinite(writer, midpoint.Y, description);
        WriteFinite(writer, midpoint.Z, description);
        WriteFinite(writer, halfSize.X, description);
        WriteFinite(writer, halfSize.Y, description);
        WriteFinite(writer, halfSize.Z, description);
    }

    private static void WriteFinite(BinaryWriter writer, float value, string description)
    {
        if (!float.IsFinite(value))
            throw new InvalidDataException($"{description} contains a non-finite value.");
        writer.Write(value == 0f ? 0f : value);
    }

    private static (GfxWorldDpvsPlanes Planes, GfxCell[] Cells, GfxCellTree[] CellTrees, ushort[] SortedSurfaceIndices)
        DecodeRenderSpatial(
            D3dbspFile file,
            ReadOnlySpan<int> sourceSurfaceIndices,
            IReadOnlyList<GfxSurfaceBounds> surfaceBounds,
            int reflectionProbeCount,
            IReadOnlyList<GfxStaticModelInst> staticModels,
            int[]? drawStarts)
    {
        ReadOnlySpan<byte> cellsData = file.GetRequiredData(D3dbspLumpType.Cells);
        int cellCount = GetElementCount(cellsData, DiskCellSize, "render cell");
        if (cellCount == 0 || cellCount >= ushort.MaxValue)
            throw new InvalidDataException($"Packed render nodes cannot represent {cellCount} cells.");

        var reader = new RenderSpatialReader(file.GetRequiredData(D3dbspLumpType.RenderSpatial));
        if (!reader.Take(8).SequenceEqual("IW4PS3S\u0001"u8))
            throw new InvalidDataException("The render spatial lump has an unknown header.");

        ReadOnlySpan<byte> header = reader.Take(12);
        int planeCount = ReadSpatialCount(header, ushort.MaxValue - cellCount, "render plane");
        int nodeCount = ReadSpatialCount(header[4..], reader.Remaining / sizeof(ushort), "render node word");
        int authoredModelCount = ReadSpatialCount(header[8..], drawStarts?.Length - 1 ?? staticModels.Count, "authored static model");
        ReadOnlySpan<byte> planeData = reader.Take(planeCount * DpvsPlane.SerializedSize);
        var planes = new DpvsPlane[planeCount];
        for (int index = 0; index < planes.Length; index++)
        {
            ReadOnlySpan<byte> row = planeData.Slice(index * DpvsPlane.SerializedSize, DpvsPlane.SerializedSize);
            Vec3 normal = ReadVec3(row, 0);
            RequireFinite(normal, $"Render plane {index}");
            planes[index] = new DpvsPlane(normal.X, normal.Y, normal.Z,
                ReadSpatialFloat(row, 12), row[16], row[17], BinaryPrimitives.ReadUInt16LittleEndian(row[18..]));
        }

        ushort[] nodes = ReadSpatialUshorts(ref reader, nodeCount);
        ValidateRenderPartition(nodes, cellCount, planeCount);

        // Slots retain donor ordering. The slot values follow the physical sort,
        // preserving the native draw bases and the brush-model surface ranges.
        var surfaceIndexBySource = new int[sourceSurfaceIndices.Length];
        for (int index = 0; index < sourceSurfaceIndices.Length; index++)
            surfaceIndexBySource[sourceSurfaceIndices[index]] = index;
        ushort[] sortedSurfaceIndices = ReadSpatialUshorts(ref reader, sourceSurfaceIndices.Length);
        var usedSurfaces = new bool[sortedSurfaceIndices.Length];
        for (int slot = 0; slot < sortedSurfaceIndices.Length; slot++)
        {
            int index = sortedSurfaceIndices[slot];
            if (index >= usedSurfaces.Length || usedSurfaces[index])
                throw new InvalidDataException($"Render spatial surface slot {slot} has an invalid or repeated surface ID {index}.");
            usedSurfaces[index] = true;
            sortedSurfaceIndices[slot] = checked((ushort)surfaceIndexBySource[index]);
        }

        int authoredDrawCount = drawStarts?[authoredModelCount] ?? authoredModelCount;
        List<ushort>[] addedModels = AssignAddedRenderModels(nodes, planes, cellCount, authoredDrawCount, staticModels);
        var cells = new GfxCell[cellCount];
        var cellTrees = new GfxCellTree[cellCount];
        for (int index = 0; index < cellCount; index++)
        {
            ReadOnlySpan<byte> cellData = cellsData.Slice(index * DiskCellSize, DiskCellSize);
            if (cellData.Slice(24, 20).IndexOfAnyExcept((byte)0) >= 0)
                throw new InvalidDataException($"Render cell {index} contains unsupported stock portal metadata.");
            Vec3 mins = ReadVec3(cellData, 0);
            Vec3 maxs = ReadVec3(cellData, 12);
            ValidateBounds(mins, maxs, $"Render cell {index}");
            byte[] probes = DecodeCellProbeIndices(cellData, reflectionProbeCount);

            ReadOnlySpan<byte> counts = reader.Take(12);
            int treeCount = ReadSpatialCount(counts, reader.Remaining / DiskSpatialTreeSize, "render AABB");
            int portalCount = ReadSpatialCount(counts[4..], reader.Remaining / DiskSpatialPortalSize, "render portal");
            int modelCount = ReadSpatialCount(counts[8..], GfxAabbTree.MaxIndexCount, "cell root model");
            ushort[] modelIndices = ReadSpatialUshorts(ref reader, modelCount);
            if (modelIndices.Any(modelIndex => modelIndex >= authoredModelCount))
                throw new InvalidDataException($"Render cell {index} references a model outside the authored placement prefix.");

            var cellBounds = new BoundsAccumulator();
            cellBounds.Add(mins);
            cellBounds.Add(maxs);
            foreach (ushort modelIndex in addedModels[index])
                cellBounds.Add(staticModels[modelIndex].Bounds);

            cellTrees[index] = new GfxCellTree
            {
                AabbTrees = ReadSpatialCellTrees(reader.Take(treeCount * DiskSpatialTreeSize), treeCount,
                    modelIndices, addedModels[index], sortedSurfaceIndices, surfaceBounds, staticModels, index, drawStarts)
            };
            var portals = new GfxPortal[portalCount];
            for (int portalIndex = 0; portalIndex < portals.Length; portalIndex++)
                portals[portalIndex] = ReadSpatialPortal(ref reader, index, cellCount);
            cells[index] = new GfxCell
            {
                Bounds = cellBounds.ToBounds($"Render cell {index}"),
                PortalCount = portalCount,
                Portals = portals,
                ReflectionProbeCount = checked((byte)probes.Length),
                Pad21 = [0, 0, 0],
                ReflectionProbes = probes
            };
        }
        if (reader.Remaining != 0)
            throw new InvalidDataException("The render spatial lump contains trailing bytes.");

        return (new GfxWorldDpvsPlanes { CellCount = cellCount, Planes = planes, Nodes = nodes },
            cells, cellTrees, sortedSurfaceIndices);
    }

    private static void ValidateRenderPartition(ushort[] nodes, int cellCount, int planeCount)
    {
        var visited = new bool[nodes.Length];
        var pending = new Stack<int>();
        pending.Push(0);
        while (pending.TryPop(out int index))
        {
            if (index >= nodes.Length || visited[index])
                throw new InvalidDataException($"The camera partition repeats or escapes its node table at word {index}.");
            visited[index] = true;
            int plane = nodes[index] - (cellCount + 1);
            if (plane < 0)
                continue;
            if (plane >= planeCount || index + 1 >= nodes.Length || visited[index + 1] || nodes[index + 1] < 2)
                throw new InvalidDataException($"The camera partition has an invalid plane or branch at word {index}.");
            visited[index + 1] = true;
            pending.Push(index + 2);
            pending.Push(index + nodes[index + 1]);
        }
        if (visited.Any(word => !word))
            throw new InvalidDataException("The camera partition contains unreferenced node words.");
    }

    private static List<ushort>[] AssignAddedRenderModels(
        ushort[] nodes,
        DpvsPlane[] planes,
        int cellCount,
        int authoredModelCount,
        IReadOnlyList<GfxStaticModelInst> models)
    {
        var perCell = Enumerable.Range(0, cellCount).Select(_ => new List<ushort>()).ToArray();
        var pending = new Stack<int>();
        var matchingCells = new HashSet<int>();
        for (int modelIndex = authoredModelCount; modelIndex < models.Count; modelIndex++)
        {
            Bounds bounds = models[modelIndex].Bounds;
            pending.Push(0);
            while (pending.TryPop(out int index))
            {
                int node = nodes[index];
                if (node <= cellCount)
                {
                    // Packed leaf zero is outside the map; leaf one is cell zero.
                    if (node != 0)
                        matchingCells.Add(node - 1);
                    continue;
                }

                DpvsPlane plane = planes[node - cellCount - 1];
                Vec3 center = bounds.MidPoint;
                Vec3 extent = bounds.HalfSize;
                float distance = plane.NormalX * center.X + plane.NormalY * center.Y +
                    plane.NormalZ * center.Z - plane.Distance;
                float radius = MathF.Abs(plane.NormalX) * extent.X + MathF.Abs(plane.NormalY) * extent.Y +
                    MathF.Abs(plane.NormalZ) * extent.Z;
                if (distance + radius >= 0)
                    pending.Push(index + 2);
                if (distance - radius <= 0)
                    pending.Push(index + nodes[index + 1]);
            }

            if (matchingCells.Count == 0)
                throw new InvalidDataException($"Added static model {modelIndex} lies outside every render cell.");
            foreach (int cell in matchingCells)
                perCell[cell].Add(checked((ushort)modelIndex));
            matchingCells.Clear();
        }
        return perCell;
    }

    private static GfxAabbTree[] ReadSpatialCellTrees(
        ReadOnlySpan<byte> data,
        int treeCount,
        ushort[] authoredModels,
        List<ushort> addedModels,
        ushort[] sortedSurfaceIndices,
        IReadOnlyList<GfxSurfaceBounds> surfaceBounds,
        IReadOnlyList<GfxStaticModelInst> staticModels,
        int cellIndex,
        int[]? drawStarts)
    {
        if (treeCount == 0)
            throw new InvalidDataException($"Render cell {cellIndex} has no root AABB.");

        // Disk nodes share slices of the cell's original model list. Expand each slice
        // through the same prefix table so every part retains its authored visibility.
        int[]? expandedOffsets = null;
        if (drawStarts is not null)
        {
            expandedOffsets = new int[authoredModels.Length + 1];
            for (int i = 0; i < authoredModels.Length; i++)
            {
                int source = authoredModels[i];
                expandedOffsets[i + 1] = checked(expandedOffsets[i] + (drawStarts[source + 1] - drawStarts[source]));
            }
        }
        int authoredDrawCount = expandedOffsets?[^1] ?? authoredModels.Length;
        var rootModels = new ushort[checked(authoredDrawCount + addedModels.Count)];
        if (rootModels.Length > GfxAabbTree.MaxIndexCount)
            throw new InvalidDataException($"Render cell {cellIndex} exceeds the PS3 AABB model-list limit.");
        if (drawStarts is null)
            authoredModels.CopyTo(rootModels, 0);
        else
        {
            int output = 0;
            foreach (int source in authoredModels)
                for (int draw = drawStarts[source]; draw < drawStarts[source + 1]; draw++)
                    rootModels[output++] = checked((ushort)draw);
        }
        addedModels.CopyTo(rootModels, authoredDrawCount);

        int rootChildren = BinaryPrimitives.ReadUInt16LittleEndian(data[24..]);
        int insertion = treeCount;
        bool appendChild = rootChildren > 0 && addedModels.Count > 0;
        if (appendChild)
        {
            int firstChild = ReadSpatialCount(data[36..], treeCount - 1, "root first child offset");
            ValidateSlice(firstChild, rootChildren, treeCount, "Root AABB children");
            insertion = firstChild + rootChildren;
        }

        var trees = new GfxAabbTree[treeCount + (appendChild ? 1 : 0)];
        var hasParent = new bool[treeCount];
        for (int index = 0; index < treeCount; index++)
        {
            ReadOnlySpan<byte> row = data.Slice(index * DiskSpatialTreeSize, DiskSpatialTreeSize);
            string path = $"Render cell {cellIndex} AABB {index}";
            Vec3 midpoint = ReadVec3(row, 0);
            Vec3 halfSize = ReadVec3(row, 12);
            if (halfSize.X < 0 || halfSize.Y < 0 || halfSize.Z < 0)
                throw new InvalidDataException($"{path} has a negative half-size.");

            int childCount = BinaryPrimitives.ReadUInt16LittleEndian(row[24..]);
            ushort surfaceCount = BinaryPrimitives.ReadUInt16LittleEndian(row[26..]);
            ushort surfaceStart = BinaryPrimitives.ReadUInt16LittleEndian(row[28..]);
            int modelCount = BinaryPrimitives.ReadUInt16LittleEndian(row[30..]);
            int modelStart = ReadSpatialCount(row[32..], authoredModels.Length, "AABB model-list offset");
            int childOffset = ReadSpatialCount(row[36..], treeCount - 1, "AABB first child offset");
            ValidateSlice(surfaceStart, surfaceCount, sortedSurfaceIndices.Length, $"{path} surfaces");
            ValidateSlice(modelStart, modelCount, authoredModels.Length, $"{path} models");
            if (surfaceCount > GfxAabbTree.MaxIndexCount)
                throw new InvalidDataException($"{path} exceeds the PS3 AABB surface-list limit.");
            if (index == 0 && (modelStart != 0 || modelCount != authoredModels.Length))
                throw new InvalidDataException($"{path} does not reference the whole root model list.");
            if (expandedOffsets is not null)
            {
                modelCount = expandedOffsets[modelStart + modelCount] - expandedOffsets[modelStart];
                modelStart = expandedOffsets[modelStart];
            }
            if (childCount == 0 ? childOffset != 0 : childOffset == 0)
                throw new InvalidDataException($"{path} has an invalid child offset.");

            int outputIndex = appendChild && index >= insertion ? index + 1 : index;
            int outputChildOffset = 0;
            if (childCount > 0)
            {
                int firstChild = index + childOffset;
                ValidateSlice(firstChild, childCount, treeCount, $"{path} children");
                for (int child = firstChild; child < firstChild + childCount; child++)
                {
                    if (hasParent[child])
                        throw new InvalidDataException($"{path} shares a child with another AABB node.");
                    hasParent[child] = true;
                }
                if (appendChild && firstChild >= insertion)
                    firstChild++;
                outputChildOffset = firstChild - outputIndex;
            }

            if (index == 0)
            {
                modelCount = rootModels.Length;
                if (appendChild)
                    childCount++;
            }
            var bounds = new BoundsAccumulator();
            bounds.Add(new Bounds { MidPoint = midpoint, HalfSize = halfSize });
            for (int slot = surfaceStart; slot < surfaceStart + surfaceCount; slot++)
                bounds.Add(surfaceBounds[sortedSurfaceIndices[slot]].Bounds);
            var models = new ArraySegment<ushort>(rootModels, modelStart, modelCount);
            foreach (ushort modelIndex in models)
                bounds.Add(staticModels[modelIndex].Bounds);

            trees[outputIndex] = new GfxAabbTree
            {
                Bounds = bounds.ToBounds(path),
                ChildCount = checked((ushort)childCount),
                SurfaceCount = surfaceCount,
                StartSurfIndex = surfaceStart,
                SModelIndexCount = checked((ushort)modelCount),
                SModelIndexes = models,
                ChildrenOffset = checked(outputChildOffset * GfxAabbTree.SerializedSize)
            };
        }
        if (hasParent.AsSpan(1).Contains(false))
            throw new InvalidDataException($"Render cell {cellIndex} contains an unreachable AABB node.");

        if (appendChild)
        {
            var bounds = new BoundsAccumulator();
            foreach (ushort modelIndex in addedModels)
                bounds.Add(staticModels[modelIndex].Bounds);
            trees[insertion] = new GfxAabbTree
            {
                Bounds = bounds.ToBounds($"Render cell {cellIndex} added models"),
                SModelIndexCount = checked((ushort)addedModels.Count),
                SModelIndexes = new ArraySegment<ushort>(rootModels, authoredDrawCount, addedModels.Count)
            };
        }
        return trees;
    }

    private static GfxPortal ReadSpatialPortal(ref RenderSpatialReader reader, int cellIndex, int cellCount)
    {
        ReadOnlySpan<byte> row = reader.Take(DiskSpatialPortalSize);
        ushort destination = BinaryPrimitives.ReadUInt16LittleEndian(row[16..]);
        byte vertexCount = row[18];
        if (destination >= cellCount || destination == cellIndex || vertexCount < 3 || row[19] != 0)
            throw new InvalidDataException($"Render cell {cellIndex} contains a portal with invalid metadata.");

        var hullAxis = new float[6];
        for (int index = 0; index < hullAxis.Length; index++)
            hullAxis[index] = ReadSpatialFloat(row, 20 + index * sizeof(float));
        var vertices = new GfxPortalVertex[vertexCount];
        for (int index = 0; index < vertexCount; index++)
        {
            ReadOnlySpan<byte> vertex = reader.Take(GfxPortalVertex.SerializedSize);
            vertices[index] = new GfxPortalVertex(
                ReadSpatialFloat(vertex, 0), ReadSpatialFloat(vertex, 4), ReadSpatialFloat(vertex, 8));
        }
        return new GfxPortal
        {
            Plane = new GfxPortalPlane(ReadSpatialFloat(row, 0), ReadSpatialFloat(row, 4),
                ReadSpatialFloat(row, 8), ReadSpatialFloat(row, 12)),
            CellIndex = destination,
            VertexCount = vertexCount,
            Vertices = vertices,
            HullAxis = hullAxis
        };
    }

    private static ushort[] ReadSpatialUshorts(ref RenderSpatialReader reader, int count)
    {
        ReadOnlySpan<byte> data = reader.Take(checked(count * sizeof(ushort)));
        var result = new ushort[count];
        for (int index = 0; index < count; index++)
            result[index] = BinaryPrimitives.ReadUInt16LittleEndian(data[(index * sizeof(ushort))..]);
        return result;
    }

    private static int ReadSpatialCount(ReadOnlySpan<byte> data, int maximum, string description)
    {
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (value > (uint)maximum)
            throw new InvalidDataException($"The render spatial lump has an invalid {description} count or offset ({value}).");
        return (int)value;
    }

    private static float ReadSpatialFloat(ReadOnlySpan<byte> data, int offset)
    {
        float value = ReadSingle(data, offset);
        if (!float.IsFinite(value))
            throw new InvalidDataException("The render spatial lump contains a non-finite coordinate.");
        return value;
    }

    private ref struct RenderSpatialReader(ReadOnlySpan<byte> data)
    {
        private ReadOnlySpan<byte> _data = data;

        public readonly int Remaining => _data.Length;

        public ReadOnlySpan<byte> Take(int length)
        {
            if ((uint)length > (uint)_data.Length)
                throw new InvalidDataException("The render spatial lump ends inside a record.");
            ReadOnlySpan<byte> values = _data[..length];
            _data = _data[length..];
            return values;
        }
    }
}
