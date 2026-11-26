using IW4.Formats.Codecs.D3dbsp;
using IW4.Game.Assets.GfxMap;
using IW4.Game.Math;

namespace MapConverter.Game.IW3.PC.World;

internal static partial class Iw3PcWorldGraphics
{
    private sealed partial class Graphics
    {
        internal (byte[] Cells, byte[] Spatial) EncodeSpatial(int[] modelIndexBySource,
            Render render, int probeCount)
        {
            int cellCount = Count(gfx + 240, "render cell");
            int planeCount = Count(gfx + 8, "render plane");
            int nodeCount = Count(gfx + 12, "render node word");
            int staticSurfaceCount = Count(gfx + 584, "static world surface");
            int sourceModelCount = Count(gfx + 580, "static model");
            if (cellCount is < 1 or >= ushort.MaxValue || planeCount > ushort.MaxValue - cellCount ||
                nodeCount == 0 || staticSurfaceCount == 0 ||
                staticSurfaceCount > render.Triangles.Length / 24 ||
                sourceModelCount != modelIndexBySource.Length)
                throw new InvalidDataException("IW3 PC render spatial counts are inconsistent.");
            int brushModelCount = Count(gfx + 336, "render brush model");
            if (brushModelCount == 0)
                throw new InvalidDataException("IW3 PC render world has no world brush model.");
            int brushModels = Required(gfx + 340, "render brush model table");
            if (U16(brushModels + 50) != 0 || U16(brushModels + 48) != staticSurfaceCount)
                throw new InvalidDataException("IW3 PC world brush model disagrees with the static world surface prefix.");

            int planeTable = planeCount == 0 ? 0 : Required(gfx + 244, "render plane table");
            int nodeTable = Required(gfx + 248, "render node table");
            int cellTable = Required(gfx + 260, "render cell table");
            int sortedTable = Required(gfx + 652, "sorted world surface table");
            if (planeCount != 0) zone.Bytes(planeTable, checked(planeCount * DpvsPlane.SerializedSize));
            zone.Bytes(nodeTable, checked(nodeCount * 2));
            zone.Bytes(cellTable, checked(cellCount * 56));
            zone.Bytes(sortedTable, checked(staticSurfaceCount * 2));
            var planes = new DpvsPlane[planeCount];
            for (int index = 0; index < planes.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                int row = checked(planeTable + index * DpvsPlane.SerializedSize);
                planes[index] = new DpvsPlane(
                    Coordinate(row), Coordinate(row + 4), Coordinate(row + 8), Coordinate(row + 12),
                    checked((byte)B(row + 16)), checked((byte)B(row + 17)), checked((ushort)U16(row + 18)));
            }
            var nodes = new ushort[nodeCount];
            for (int index = 0; index < nodes.Length; index++)
                nodes[index] = checked((ushort)U16(checked(nodeTable + index * 2)));

            var sorted = new ushort[staticSurfaceCount];
            for (int index = 0; index < sorted.Length; index++)
                sorted[index] = checked((ushort)U16(checked(sortedTable + index * 2)));

            int retainedModelCount = 0;
            foreach (int destination in modelIndexBySource)
                retainedModelCount = Math.Max(retainedModelCount, destination + 1);

            var cells = new GfxCell[cellCount];
            var cellTrees = new GfxCellTree[cellCount];
            for (int index = 0; index < cellCount; index++)
            {
                token.ThrowIfCancellationRequested();
                int row = checked(cellTable + index * 56);
                int cellProbeCount = B(row + 48);
                if (cellProbeCount > 67)
                    throw new InvalidDataException($"IW3 PC render cell {index} has invalid probe count.");
                int probeTable = cellProbeCount == 0 ? 0 : Required(row + 52, $"cell {index} reflection probes");
                byte[] probes = cellProbeCount == 0 ? [] : zone.Bytes(probeTable, cellProbeCount).ToArray();
                if (probes.Any(probe => probe >= probeCount))
                    throw new InvalidDataException($"IW3 PC render cell {index} references an absent reflection probe.");
                GfxAabbTree[] trees = ReadCellTrees(row, index, staticSurfaceCount, modelIndexBySource);
                GfxPortal[] portals = ReadCellPortals(row, index, cellTable, cellCount);
                cells[index] = new GfxCell
                {
                    Bounds = Endpoints(row, $"cell {index}"),
                    PortalCount = portals.Length,
                    Portals = portals,
                    ReflectionProbeCount = checked((byte)cellProbeCount),
                    Pad21 = [0, 0, 0],
                    ReflectionProbes = probes
                };
                cellTrees[index] = new GfxCellTree { AabbTrees = trees };
            }

            return D3dbspGfxCodec.EncodeRenderSpatial(
                new GfxWorldDpvsPlanes { CellCount = cellCount, Planes = planes, Nodes = nodes },
                cells, cellTrees, sorted, retainedModelCount);
        }

        private GfxAabbTree[] ReadCellTrees(int cell, int cellIndex, int staticSurfaceCount,
            int[] modelIndexBySource)
        {
            int count = Count(cell + 24, $"cell {cellIndex} AABB");
            if (count == 0)
                throw new InvalidDataException($"IW3 PC render cell {cellIndex} has no root AABB.");
            int table = Required(cell + 28, $"cell {cellIndex} AABB table");
            zone.Bytes(table, checked(count * 44));
            var result = new GfxAabbTree[count];
            for (int index = 0; index < count; index++)
            {
                int row = checked(table + index * 44);
                int childCount = U16(row + 24), surfaceCount = U16(row + 26);
                int surfaceStart = U16(row + 28), sourceModelCount = U16(row + 34);
                if (surfaceCount != 0 && surfaceStart + surfaceCount > staticSurfaceCount)
                    throw new InvalidDataException($"IW3 PC cell {cellIndex} AABB {index} exceeds the world surface prefix.");
                int modelTable = sourceModelCount == 0 ? 0 :
                    Required(row + 36, $"cell {cellIndex} AABB {index} static model indices");
                var models = new List<ushort>(sourceModelCount);
                if (sourceModelCount != 0) zone.Bytes(modelTable, checked(sourceModelCount * 2));
                for (int model = 0; model < sourceModelCount; model++)
                {
                    int sourceId = U16(checked(modelTable + model * 2));
                    if (sourceId >= modelIndexBySource.Length)
                        throw new InvalidDataException($"IW3 PC cell {cellIndex} AABB {index} references an absent static model.");
                    int destination = modelIndexBySource[sourceId];
                    if (destination >= 0) models.Add(checked((ushort)destination));
                }
                int childOffset = I32(row + 40);
                if (childCount != 0 &&
                    (childOffset <= 0 || childOffset % 44 != 0 || index + childOffset / 44 + childCount > count))
                    throw new InvalidDataException($"IW3 PC cell {cellIndex} AABB {index} has an invalid child span.");
                result[index] = new GfxAabbTree
                {
                    Bounds = surfaceCount == 0 && sourceModelCount == 0 && IsEmptyTreeBounds(row, childCount != 0)
                        ? new Bounds()
                        : Endpoints(row, $"cell {cellIndex} AABB {index}"),
                    ChildCount = checked((ushort)childCount),
                    SurfaceCount = checked((ushort)surfaceCount),
                    StartSurfIndex = checked((ushort)(surfaceCount == 0 ? 0 : surfaceStart)),
                    SModelIndexCount = checked((ushort)models.Count),
                    SModelIndexes = models,
                    ChildrenOffset = childCount == 0 ? 0 :
                        checked(childOffset / 44 * GfxAabbTree.SerializedSize)
                };
            }
            return result;
        }

        private GfxPortal[] ReadCellPortals(int cell, int cellIndex, int cellTable, int cellCount)
        {
            int count = Count(cell + 32, $"cell {cellIndex} portal");
            if (count == 0) return [];
            int table = Required(cell + 36, $"cell {cellIndex} portal table");
            zone.Bytes(table, checked(count * 68));
            var result = new GfxPortal[count];
            for (int index = 0; index < count; index++)
            {
                int row = checked(table + index * 68);
                int adjacent = Required(row + 32, $"cell {cellIndex} portal {index} adjacent cell");
                int delta = adjacent - cellTable;
                if (delta < 0 || delta % 56 != 0 || delta / 56 >= cellCount || delta / 56 == cellIndex)
                    throw new InvalidDataException($"IW3 PC cell {cellIndex} portal {index} points outside the other cells.");
                int vertexCount = B(row + 40);
                if (vertexCount < 3)
                    throw new InvalidDataException($"IW3 PC cell {cellIndex} portal {index} has fewer than three vertices.");
                int verticesTable = Required(row + 36, $"cell {cellIndex} portal {index} vertices");
                zone.Bytes(verticesTable, checked(vertexCount * GfxPortalVertex.SerializedSize));
                var vertices = new GfxPortalVertex[vertexCount];
                for (int vertex = 0; vertex < vertexCount; vertex++)
                {
                    int position = checked(verticesTable + vertex * GfxPortalVertex.SerializedSize);
                    vertices[vertex] = new GfxPortalVertex(
                        Coordinate(position), Coordinate(position + 4), Coordinate(position + 8));
                }
                var hullAxis = new float[6];
                for (int axis = 0; axis < hullAxis.Length; axis++)
                    hullAxis[axis] = Coordinate(row + 44 + axis * 4);
                result[index] = new GfxPortal
                {
                    Plane = new GfxPortalPlane(Coordinate(row + 12), Coordinate(row + 16),
                        Coordinate(row + 20), Coordinate(row + 24)),
                    CellIndex = checked((ushort)(delta / 56)),
                    VertexCount = checked((byte)vertexCount),
                    Vertices = vertices,
                    HullAxis = hullAxis
                };
            }
            return result;
        }

        private float Coordinate(int field)
        {
            float value = F32(field);
            Finite(value, "render spatial coordinate");
            return value;
        }

        private bool IsEmptyTreeBounds(int field, bool internalNode)
        {
            float minimum = internalNode ? 131072f : float.MaxValue;
            float maximum = internalNode ? -131072f : -float.MaxValue;
            for (int axis = 0; axis < 3; axis++)
                if (F32(field + axis * 4) != minimum ||
                    F32(field + 12 + axis * 4) != maximum)
                    return false;
            return true;
        }

        private Bounds Endpoints(int field, string description)
        {
            var bounds = new Bounds();
            float[] mins = new float[3], maxs = new float[3];
            for (int axis = 0; axis < 3; axis++)
            {
                mins[axis] = Coordinate(field + axis * 4);
                maxs[axis] = Coordinate(field + 12 + axis * 4);
                if (mins[axis] > maxs[axis])
                    throw new InvalidDataException($"IW3 PC {description} has reversed bounds.");
            }
            bounds.MidPoint = new Vec3
            {
                X = mins[0] * 0.5f + maxs[0] * 0.5f,
                Y = mins[1] * 0.5f + maxs[1] * 0.5f,
                Z = mins[2] * 0.5f + maxs[2] * 0.5f
            };
            bounds.HalfSize = new Vec3
            {
                X = maxs[0] * 0.5f - mins[0] * 0.5f,
                Y = maxs[1] * 0.5f - mins[1] * 0.5f,
                Z = maxs[2] * 0.5f - mins[2] * 0.5f
            };
            Finite(bounds.MidPoint.X, description);
            Finite(bounds.MidPoint.Y, description);
            Finite(bounds.MidPoint.Z, description);
            Finite(bounds.HalfSize.X, description);
            Finite(bounds.HalfSize.Y, description);
            Finite(bounds.HalfSize.Z, description);
            return bounds;
        }
    }
}
