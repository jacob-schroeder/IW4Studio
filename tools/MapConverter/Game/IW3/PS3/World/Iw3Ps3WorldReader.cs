using System.Buffers.Binary;
using IW4.Game.Assets.GfxMap;
using IW4.Game.Assets.Image;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.XModel;
using IW4.Game.Math;
using IW4.Game.Pointers;

namespace MapConverter.Game.IW3.PS3.Extraction;

internal static partial class Iw3Ps3FastFileCatalog
{
    private sealed partial class Reader
    {
        // COD4 PS3 cod4_EBOOT_MP.elf: d0680 reads this 0x2e4-byte root. The
        // helpers below follow that function's serialized child order.
        private Pointer? World(uint raw, Address cell) =>
            AssetPointer(raw, cell, "gfxworld", 0x2e4, root =>
            {
                Push(4);
                try
                {
                    int start = root.FileOffset;
                    var dependencies = new HashSet<int>();
                    string? name = ReadString(U32(start));
                    string? baseName = ReadString(U32(start + 4));
                    int planeCount = WorldCount(start + 8, "plane count");
                    int nodeCount = WorldCount(start + 0xc, "node count");
                    int indexCount = WorldCount(start + 0x10, "index count");
                    ushort[] indices = WorldUInt16Array(U32(start + 0x14), 2,
                        indexCount, "world indices");

                    int surfaceCount = WorldCount(start + 0x1c, "surface count");
                    int skySurfCount = WorldCount(start + 0x24, "sky surface count");
                    int[] skyStartSurfs = WorldInt32Array(U32(start + 0x28), 4,
                        skySurfCount, "sky surface starts");
                    Pointer? skyImagePointer = Image(U32(start + 0x2c), root.Address.Add(0x2c));
                    Add(dependencies, skyImagePointer);
                    int skySamplerState = unchecked((int)U32(start + 0x30));

                    // +0xd4 is an inline GfxLight row, not a LightDef root.
                    // c9f40 loads its +0x3c LightDef asset pointer.
                    Pointer? sunLight = ReadArray(U32(start + 0xd4), 4, 0x40,
                        "world-sun-light");
                    if (sunLight is { } light)
                        AddWorldLightDefinition(light, dependencies);

                    int sunPrimaryLightIndex = WorldCount(start + 0xe4, "sun light index");
                    int primaryLightCount = WorldCount(start + 0xe8, "primary light count");
                    int probeCount = WorldCount(start + 0xf0, "reflection probe count");
                    (GfxReflectionProbe[] probes, GfxImageAsset?[] probeImages) =
                        WorldReflectionProbes(U32(start + 0xf4), probeCount, dependencies);

                    GfxWorldDpvsPlanes planes = WorldDpvsPlanes(start + 0xfc,
                        planeCount, nodeCount);
                    (GfxCell[] cells, GfxCellTree[] cellTrees,
                        GfxCellTreeCount[] cellTreeCounts) =
                        WorldCells(U32(start + 0x110), planes.CellCount);
                    int lightmapCount = WorldCount(start + 0x114, "lightmap count");
                    GfxLightmapArray[] lightmaps = WorldLightmaps(U32(start + 0x118),
                        lightmapCount, dependencies);
                    GfxLightGrid grid = WorldLightGrid(start + 0x11c);

                    int modelCount = WorldCount(start + 0x15c, "brush model count");
                    GfxBrushModel[] models = WorldBrushModels(U32(start + 0x160), modelCount);
                    int materialMemoryCount = WorldCount(start + 0x180, "material memory count");
                    MaterialMemory[] materialMemory = WorldMaterialMemory(
                        U32(start + 0x184), materialMemoryCount, dependencies);
                    // d0680 calls c1280/c10c0 after material memory and before
                    // sunflare. These embedded vertex-data headers own the
                    // source streams at +0x38 and +0x48 respectively.
                    int vertexCount = WorldCount(start + 0x34, "vertex count");
                    byte[] packedVertices = WorldBytes(U32(start + 0x38), 16,
                        Size(vertexCount, 16), "world positions");
                    int layerSize = WorldCount(start + 0x44, "vertex layer size");
                    byte[] packedLayers = WorldPushedBytes(6, U32(start + 0x48), 1,
                        layerSize, "world vertex layers");
                    Sunflare sun = WorldSunflare(start + 0x188, root.Address, dependencies);
                    float[] outdoorMatrix = WorldFloats(start + 0x1e8, 16);
                    Pointer? outdoorImagePointer = Image(U32(start + 0x228),
                        root.Address.Add(0x228));
                    Add(dependencies, outdoorImagePointer);

                    // Remaining visibility and static-model tables are loaded
                    // below by the source-specific DPVS converter.
                    GfxWorldAsset? world = WorldVisibilityAndBuild(
                        root, name, baseName, planeCount, nodeCount, surfaceCount,
                        indices, skySurfCount, skyStartSurfs, skyImagePointer,
                        skySamplerState, vertexCount, packedVertices, packedLayers,
                        sunPrimaryLightIndex, primaryLightCount, probes, probeImages,
                        planes, cells, cellTrees, cellTreeCounts,
                        lightmaps, grid, models, materialMemory,
                        sun, outdoorMatrix, outdoorImagePointer, dependencies);
                    return Named("gfxworld", name, root, dependencies, world);
                }
                finally { Pop(); }
            });

        private int WorldCount(int offset, string field)
        {
            uint value = U32(offset);
            if (value > int.MaxValue)
                throw Invalid($"GfxWorld {field} exceeds the supported range");
            return (int)value;
        }

        private byte[] WorldBytes(uint raw, int alignment, int byteCount, string field,
            string? pointerKind = null, bool allowPrimitiveSpan = false)
        {
            string kind = pointerKind ?? field;
            if (allowPrimitiveSpan && raw is not (0 or uint.MaxValue))
                return WorldPrimitiveSpan(raw, alignment, byteCount, kind);
            Pointer? pointer = ReadArray(raw, alignment, byteCount,
                kind);
            if (pointer is null)
            {
                if (byteCount != 0) throw Invalid($"GfxWorld {field} is absent");
                return [];
            }
            Region region = pointer.Region;
            if (region.Length != byteCount)
                throw Invalid($"GfxWorld {field} has an unexpected byte count");
            return _data.AsSpan(region.FileOffset, byteCount).ToArray();
        }

        private byte[] WorldPrimitiveSpan(uint raw, int alignment, int byteCount,
            string kind)
        {
            Address address = Decode(raw);
            Region? found = null;
            int displacement = 0;
            foreach (Pointer candidate in _direct.Values)
            {
                Region region = candidate.Region;
                int offset = address.Offset - region.Address.Offset;
                if (candidate.Kind != kind || candidate.Dependencies.Length != 0 ||
                    address.Block != region.Address.Block || offset < 0 ||
                    offset % alignment != 0 || byteCount > region.Length ||
                    offset > region.Length - byteCount)
                    continue;
                if (found is null || region.Length < found.Value.Length)
                {
                    found = region;
                    displacement = offset;
                }
            }
            if (found is not { } source)
                throw Unsupported($"GfxWorld {kind} pointer 0x{raw:X8} has no bounded source array span");
            return _data.AsSpan(source.FileOffset + displacement, byteCount).ToArray();
        }

        private byte[] WorldPushedBytes(int block, uint raw, int alignment,
            int byteCount, string field)
        {
            Push(block);
            try { return WorldBytes(raw, alignment, byteCount, field); }
            finally { Pop(); }
        }

        private ushort[] WorldUInt16Array(uint raw, int alignment, int count, string field,
            bool allowPrimitiveSpan = false)
        {
            byte[] data = WorldBytes(raw, alignment, Size(count, 2), field,
                allowPrimitiveSpan: allowPrimitiveSpan);
            ushort[] values = new ushort[count];
            for (int index = 0; index < count; index++)
                values[index] = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(index * 2));
            return values;
        }

        private int[] WorldInt32Array(uint raw, int alignment, int count, string field)
        {
            byte[] data = WorldBytes(raw, alignment, Size(count, 4), field);
            int[] values = new int[count];
            for (int index = 0; index < count; index++)
                values[index] = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(index * 4));
            return values;
        }

        private float[] WorldFloats(int offset, int count)
        {
            float[] values = new float[count];
            for (int index = 0; index < count; index++)
                values[index] = BinaryPrimitives.ReadSingleBigEndian(_data.AsSpan(offset + index * 4));
            return values;
        }

        private (GfxReflectionProbe[], GfxImageAsset?[]) WorldReflectionProbes(
            uint raw, int count, HashSet<int> dependencies)
        {
            // c9b98/c9ae0: each 0x10-byte row embeds XYZ and an image asset cell.
            Pointer? table = ReadArray(raw, 4, Size(count, 0x10), "world-reflection-probes");
            if (table is null && count != 0)
                throw Invalid("GfxWorld reflection probes are absent");
            var origins = new GfxReflectionProbe[count];
            var images = new GfxImageAsset?[count];
            if (table is null) return (origins, images);
            for (int index = 0; index < count; index++)
            {
                int row = table.Region.FileOffset + index * 0x10;
                origins[index] = new GfxReflectionProbe(
                    WorldFloat(row), WorldFloat(row + 4), WorldFloat(row + 8));
                Pointer? image = Image(U32(row + 0xc), table.Region.Address.Add(index * 0x10 + 0xc));
                Add(dependencies, image);
                images[index] = WorldAsset<GfxImageAsset>(image, "reflection-probe image");
            }
            return (origins, images);
        }

        private GfxWorldDpvsPlanes WorldDpvsPlanes(int start, int planeCount, int nodeCount)
        {
            // bebf0: the two authored tables load in block 4, followed by a
            // cellCount*0x100-byte runtime visibility table in block 1.
            int cellCount = WorldCount(start, "cell count");
            byte[] planeBytes = WorldBytes(U32(start + 4), 4,
                Size(planeCount, DpvsPlane.SerializedSize), "DPVS planes",
                "clip-planes");
            var planes = new DpvsPlane[planeCount];
            for (int index = 0; index < planeCount; index++)
            {
                int row = index * DpvsPlane.SerializedSize;
                planes[index] = new DpvsPlane(
                    WorldFloat(planeBytes, row), WorldFloat(planeBytes, row + 4),
                    WorldFloat(planeBytes, row + 8), WorldFloat(planeBytes, row + 12),
                    planeBytes[row + 16], planeBytes[row + 17],
                    BinaryPrimitives.ReadUInt16BigEndian(planeBytes.AsSpan(row + 18)));
            }
            ushort[] nodes = WorldUInt16Array(U32(start + 8), 2, nodeCount, "DPVS nodes");
            WorldRuntimeReserve(U32(start + 0xc),
                Size(Size(cellCount, 0x100), sizeof(uint)), 4);
            return new GfxWorldDpvsPlanes
            {
                CellCount = cellCount,
                Planes = planes,
                Nodes = nodes
            };
        }

        private (GfxCell[], GfxCellTree[], GfxCellTreeCount[]) WorldCells(
            uint raw, int count)
        {
            // bF328/befb8: source GfxCell rows are 0x38 bytes, with the AABB
            // arrays embedded per cell. IW4 stores those arrays in CellTrees.
            Pointer? table = ReadArray(raw, 4, Size(count, 0x38), "world-cells");
            if (table is null && count != 0) throw Invalid("GfxWorld cells are absent");
            var cells = new GfxCell[count];
            var trees = new GfxCellTree[count];
            var counts = new GfxCellTreeCount[count];
            if (table is null) return (cells, trees, counts);
            for (int index = 0; index < count; index++)
            {
                int row = table.Region.FileOffset + index * 0x38;
                int treeCount = WorldCount(row + 0x18, $"cell {index} AABB count");
                GfxAabbTree[] aabb = WorldAabbTrees(U32(row + 0x1c), treeCount);
                int portalCount = WorldCount(row + 0x20, $"cell {index} portal count");
                GfxPortal[] portals = WorldPortals(U32(row + 0x24), portalCount,
                    table.Region, count);
                int cullGroupCount = WorldCount(row + 0x28,
                    $"cell {index} cull-group count");
                WorldBytes(U32(row + 0x2c), 4, Size(cullGroupCount, 0x20),
                    $"cell {index} cull groups");
                byte probeCount = _data[row + 0x30];
                byte[] probeIds = WorldBytes(U32(row + 0x34), 1, probeCount,
                    $"cell {index} reflection probes");
                cells[index] = new GfxCell
                {
                    Bounds = WorldEndpointBounds(row),
                    PortalCount = portalCount,
                    Portals = portals,
                    ReflectionProbeCount = probeCount,
                    Pad21 = new byte[3],
                    ReflectionProbes = probeIds
                };
                trees[index] = new GfxCellTree { AabbTrees = aabb };
                counts[index] = new GfxCellTreeCount((uint)treeCount);
            }
            return (cells, trees, counts);
        }

        private GfxAabbTree[] WorldAabbTrees(uint raw, int count)
        {
            // bef28 proves 0x28-byte source rows. Endpoint bounds come from
            // the retained IW3 PC structure; this converter validates each pair.
            Pointer? table = ReadArray(raw, 4, Size(count, 0x28), "world-AABB-trees");
            if (table is null && count != 0) throw Invalid("GfxWorld AABB trees are absent");
            var result = new GfxAabbTree[count];
            if (table is null) return result;
            for (int index = 0; index < count; index++)
            {
                int row = table.Region.FileOffset + index * 0x28;
                int modelIndexCount = U16(row + 0x1e);
                ushort[] modelIndices = WorldUInt16Array(U32(row + 0x20), 2,
                    modelIndexCount, "AABB static-model indices",
                    allowPrimitiveSpan: true);
                result[index] = new GfxAabbTree
                {
                    Bounds = WorldEndpointBounds(row),
                    ChildCount = U16(row + 0x18),
                    SurfaceCount = U16(row + 0x1a),
                    StartSurfIndex = U16(row + 0x1c),
                    SModelIndexCount = (ushort)modelIndexCount,
                    SModelIndexes = modelIndices,
                    ChildrenOffset = unchecked((int)U32(row + 0x24))
                };
            }
            return result;
        }

        private GfxPortal[] WorldPortals(uint raw, int count, Region cells,
            int cellCount)
        {
            // bf228: source portal row 0x44. The extra source plane-side bytes
            // and cell pointer become IW4's compact cell index.
            Pointer? table = ReadArray(raw, 4, Size(count, 0x44), "world-portals");
            if (table is null && count != 0) throw Invalid("GfxWorld portals are absent");
            var result = new GfxPortal[count];
            if (table is null) return result;
            for (int index = 0; index < count; index++)
            {
                int row = table.Region.FileOffset + index * 0x44;
                byte vertexCount = _data[row + 0x28];
                uint rawCell = U32(row + 0x20);
                if (rawCell is 0 or uint.MaxValue or 0xfffffffe)
                    throw Unsupported("portal cell pointer outside the authored cell table");
                Address cellAddress = Decode(rawCell);
                int displacement = cellAddress.Offset - cells.Address.Offset;
                if (cellAddress.Block != cells.Address.Block || displacement < 0 ||
                    displacement % 0x38 != 0 || displacement / 0x38 >= cellCount)
                    throw Invalid("GfxWorld portal points outside its cell table");
                Pointer? vertexTable = ReadArray(U32(row + 0x24), 4,
                    Size(vertexCount, GfxPortalVertex.SerializedSize), "portal-vertices");
                if (vertexTable is null && vertexCount != 0)
                    throw Invalid("GfxWorld portal vertices are absent");
                var vertices = new GfxPortalVertex[vertexCount];
                Region vertexRegion = vertexTable?.Region ?? default;
                for (int vertex = 0; vertex < vertexCount; vertex++)
                {
                    int position = vertexRegion.FileOffset +
                        vertex * GfxPortalVertex.SerializedSize;
                    vertices[vertex] = new GfxPortalVertex(WorldFloat(position),
                        WorldFloat(position + 4), WorldFloat(position + 8));
                }
                result[index] = new GfxPortal
                {
                    IsQueued = _data[row] != 0,
                    IsAncestor = _data[row + 1] != 0,
                    RecursionDepth = _data[row + 2],
                    HullPointCount = _data[row + 3],
                    Plane = new GfxPortalPlane(WorldFloat(row + 0xc),
                        WorldFloat(row + 0x10), WorldFloat(row + 0x14),
                        WorldFloat(row + 0x18)),
                    Vertices = vertices,
                    CellIndex = (ushort)(displacement / 0x38),
                    VertexCount = vertexCount,
                    HullAxis = WorldFloats(row + 0x2c, 6)
                };
            }
            return result;
        }

        private Bounds WorldEndpointBounds(int row)
        {
            return WorldBounds(WorldFloats(row, 3), WorldFloats(row + 12, 3));
        }

        private GfxLightmapArray[] WorldLightmaps(uint raw, int count,
            HashSet<int> dependencies)
        {
            // c9ce8/c9c20: paired image asset cells in 8-byte rows.
            Pointer? table = ReadArray(raw, 4, Size(count, 8), "world-lightmaps");
            if (table is null && count != 0) throw Invalid("GfxWorld lightmaps are absent");
            var result = new GfxLightmapArray[count];
            if (table is null) return result;
            for (int index = 0; index < count; index++)
            {
                int row = table.Region.FileOffset + index * 8;
                Address cell = table.Region.Address.Add(index * 8);
                Pointer? primary = Image(U32(row), cell);
                Pointer? secondary = Image(U32(row + 4), cell.Add(4));
                Add(dependencies, primary);
                Add(dependencies, secondary);
                result[index] = new GfxLightmapArray
                {
                    Primary = WorldAsset<GfxImageAsset>(primary, "primary lightmap"),
                    Secondary = WorldAsset<GfxImageAsset>(secondary, "secondary lightmap")
                };
            }
            return result;
        }

        private GfxLightGrid WorldLightGrid(int start)
        {
            // be3e8: 0x38-byte source row; four payloads load in pointer order.
            ushort[] mins = new ushort[3], maxs = new ushort[3];
            for (int index = 0; index < 3; index++)
            {
                mins[index] = U16(start + 8 + index * 2);
                maxs[index] = U16(start + 0xe + index * 2);
            }
            uint rowAxis = U32(start + 0x14), colAxis = U32(start + 0x18);
            if ((rowAxis, colAxis) is not (0u, 1u) and not (1u, 0u))
                throw Invalid("GfxWorld light-grid axes are invalid");
            int rowCount = checked(maxs[rowAxis] - mins[rowAxis] + 1);
            ushort[] starts = WorldUInt16Array(U32(start + 0x1c), 2,
                rowCount, "light-grid row starts");
            int rawSize = WorldCount(start + 0x20, "light-grid raw byte count");
            byte[] rawRows = WorldBytes(U32(start + 0x24), 1,
                rawSize, "light-grid row data");
            int entryCount = WorldCount(start + 0x28, "light-grid entry count");
            byte[] entryBytes = WorldBytes(U32(start + 0x2c), 4,
                Size(entryCount, 4), "light-grid entries");
            var entries = new GfxLightGridEntry[entryCount];
            for (int index = 0; index < entryCount; index++)
            {
                int row = index * 4;
                entries[index] = new GfxLightGridEntry(
                    BinaryPrimitives.ReadUInt16BigEndian(entryBytes.AsSpan(row)),
                    entryBytes[row + 2], entryBytes[row + 3]);
            }
            int colorCount = WorldCount(start + 0x30, "light-grid color count");
            byte[] colorBytes = WorldBytes(U32(start + 0x34), 4,
                Size(colorCount, GfxLightGridColors.SerializedSize), "light-grid colors");
            var colors = new GfxLightGridColors[colorCount];
            for (int index = 0; index < colorCount; index++)
                colors[index] = new GfxLightGridColors(colorBytes.AsSpan(
                    index * GfxLightGridColors.SerializedSize,
                    GfxLightGridColors.SerializedSize).ToArray());
            return new GfxLightGrid
            {
                // IW3 stores a byte flag followed by three pad bytes; IW4
                // serializes the boolean as an integer word.
                HasLightRegionsRaw = _data[start] == 0 ? 0u : 1u,
                SunPrimaryLightIndex = U32(start + 4),
                Mins = mins, Maxs = maxs,
                RowAxis = (GfxLightGridHorizontalAxis)rowAxis,
                ColAxis = (GfxLightGridHorizontalAxis)colAxis,
                RowDataStart = starts,
                RawRowDataSize = (uint)rawSize,
                RawRowData = rawRows,
                EntryCount = (uint)entryCount,
                Entries = entries,
                ColorCount = (uint)colorCount,
                Colors = colors
            };
        }

        private GfxBrushModel[] WorldBrushModels(uint raw, int count)
        {
            // b4bf8 proves a 0x38-byte source row. Native PS3 rows end in
            // uint32 surfaceCount/start at +0x30/+0x34, unlike the retained
            // PC layout. IW4 stores radius followed by two uint16 fields.
            byte[] data = WorldBytes(raw, 4, Size(count, 0x38), "brush models");
            var result = new GfxBrushModel[count];
            for (int index = 0; index < count; index++)
            {
                int row = index * 0x38;
                uint modelSurfaceCount = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(row + 48));
                uint firstSurface = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(row + 52));
                if (modelSurfaceCount > ushort.MaxValue ||
                    firstSurface > ushort.MaxValue &&
                    !(modelSurfaceCount == 0 && firstSurface == uint.MaxValue))
                    throw Unsupported($"GfxWorld brush model {index} surface range exceeds IW4 field widths");
                Bounds bounds = WorldBounds(WorldFloats(data, row + 24, 3),
                    WorldFloats(data, row + 36, 3));
                Vec3 half = bounds.HalfSize;
                result[index] = new GfxBrushModel
                {
                    BoundsMins = [bounds.MidPoint.X, bounds.MidPoint.Y, bounds.MidPoint.Z],
                    BoundsMaxs = [half.X, half.Y, half.Z],
                    Radius = MathF.Sqrt(half.X * half.X + half.Y * half.Y +
                        half.Z * half.Z),
                    SurfaceCount = (ushort)modelSurfaceCount,
                    StartSurfIndex = firstSurface == uint.MaxValue
                        ? ushort.MaxValue : (ushort)firstSurface
                };
            }
            return result;
        }

        private MaterialMemory[] WorldMaterialMemory(uint raw, int count,
            HashSet<int> dependencies)
        {
            // cb258/cb1a0: each row stores a Material asset cell and byte count.
            Pointer? table = ReadArray(raw, 4, Size(count, 8), "world-material-memory");
            if (table is null && count != 0)
                throw Invalid("GfxWorld material-memory table is absent");
            var result = new MaterialMemory[count];
            if (table is null) return result;
            for (int index = 0; index < count; index++)
            {
                int row = table.Region.FileOffset + index * 8;
                Pointer? material = Material(U32(row), table.Region.Address.Add(index * 8));
                Add(dependencies, material);
                result[index] = new MaterialMemory
                {
                    Material = WorldAsset<MaterialAsset>(material, "material-memory material"),
                    Memory = unchecked((int)U32(row + 4))
                };
            }
            return result;
        }

        private Sunflare WorldSunflare(int start, Address rootAddress,
            HashSet<int> dependencies)
        {
            // cb2e0: 0x60 bytes, with sprite/flare material cells at +4/+8.
            Pointer? sprite = Material(U32(start + 4), rootAddress.Add(0x18c));
            Pointer? flare = Material(U32(start + 8), rootAddress.Add(0x190));
            Add(dependencies, sprite);
            Add(dependencies, flare);
            return new Sunflare
            {
                HasValidDataRaw = _data[start] == 0 ? 0u : 1u,
                SpriteMaterial = WorldAsset<MaterialAsset>(sprite, "sun sprite material"),
                FlareMaterial = WorldAsset<MaterialAsset>(flare, "sun flare material"),
                SpriteSize = WorldFloat(start + 12),
                FlareMinSize = WorldFloat(start + 16),
                FlareMinDot = WorldFloat(start + 20),
                FlareMaxSize = WorldFloat(start + 24),
                FlareMaxDot = WorldFloat(start + 28),
                FlareMaxAlpha = WorldFloat(start + 32),
                FlareFadeInTime = unchecked((int)U32(start + 36)),
                FlareFadeOutTime = unchecked((int)U32(start + 40)),
                BlindMinDot = WorldFloat(start + 44),
                BlindMaxDot = WorldFloat(start + 48),
                BlindMaxDarken = WorldFloat(start + 52),
                BlindFadeInTime = unchecked((int)U32(start + 56)),
                BlindFadeOutTime = unchecked((int)U32(start + 60)),
                GlareMinDot = WorldFloat(start + 64),
                GlareMaxDot = WorldFloat(start + 68),
                GlareMaxLighten = WorldFloat(start + 72),
                GlareFadeInTime = unchecked((int)U32(start + 76)),
                GlareFadeOutTime = unchecked((int)U32(start + 80)),
                SunFxPosition = WorldFloats(start + 84, 3)
            };
        }

        private GfxShadowGeometry[] WorldShadowGeometry(uint raw, int count)
        {
            Pointer? table = ReadArray(raw, 4, Size(count, 0xc), "world-shadow-geometry");
            if (table is null && count != 0)
                throw Invalid("GfxWorld shadow-geometry table is absent");
            var result = new GfxShadowGeometry[count];
            if (table is null) return result;
            for (int index = 0; index < count; index++)
            {
                int row = table.Region.FileOffset + index * 0xc;
                ushort surfaceCount = U16(row);
                ushort modelCount = U16(row + 2);
                result[index] = new GfxShadowGeometry
                {
                    SurfaceCount = surfaceCount,
                    SModelCount = modelCount,
                    SortedSurfIndex = WorldUInt16Array(U32(row + 4), 2,
                        surfaceCount, "shadow surface indices"),
                    SModelIndex = WorldUInt16Array(U32(row + 8), 2,
                        modelCount, "shadow static-model indices")
                };
            }
            return result;
        }

        private GfxLightRegion[] WorldLightRegions(uint raw, int count)
        {
            Pointer? table = ReadArray(raw, 4, Size(count, 8), "world-light-regions");
            if (table is null && count != 0)
                throw Invalid("GfxWorld light-region table is absent");
            var result = new GfxLightRegion[count];
            if (table is null) return result;
            for (int index = 0; index < count; index++)
            {
                int row = table.Region.FileOffset + index * 8;
                int hullCount = WorldCount(row, $"light-region {index} hull count");
                result[index] = new GfxLightRegion
                {
                    HullCount = hullCount,
                    Hulls = WorldLightRegionHulls(U32(row + 4), hullCount)
                };
            }
            return result;
        }

        private GfxLightRegionHull[] WorldLightRegionHulls(uint raw, int count)
        {
            Pointer? table = ReadArray(raw, 4, Size(count, 0x50), "world-light-hulls");
            if (table is null && count != 0)
                throw Invalid("GfxWorld light-region hulls are absent");
            var result = new GfxLightRegionHull[count];
            if (table is null) return result;
            for (int index = 0; index < count; index++)
            {
                int row = table.Region.FileOffset + index * 0x50;
                int axisCount = WorldCount(row + 0x48, "light-region axis count");
                Pointer? axes = ReadArray(U32(row + 0x4c), 4,
                    Size(axisCount, GfxLightRegionAxis.SerializedSize), "world-light-axes");
                if (axes is null && axisCount != 0)
                    throw Invalid("GfxWorld light-region axes are absent");
                var convertedAxes = new GfxLightRegionAxis[axisCount];
                Region axisRegion = axes?.Region ?? default;
                for (int axis = 0; axis < axisCount; axis++)
                {
                    int axisRow = axisRegion.FileOffset + axis * GfxLightRegionAxis.SerializedSize;
                    convertedAxes[axis] = new GfxLightRegionAxis
                    {
                        Dir = WorldFloats(axisRow, 3),
                        MidPoint = WorldFloat(axisRow + 12),
                        HalfSize = WorldFloat(axisRow + 16)
                    };
                }
                result[index] = new GfxLightRegionHull
                {
                    KdopMidPoint = WorldFloats(row, 9),
                    KdopHalfSize = WorldFloats(row + 36, 9),
                    AxisCount = (uint)axisCount,
                    Axes = convertedAxes
                };
            }
            return result;
        }

        private GfxWorldDpvsStatic WorldDpvsStatic(int start, int surfaceCount,
            int cullGroupCount, HashSet<int> dependencies, int[] skyStartSurfs)
        {
            // d0290 proves a 0x64-byte source header. Its source sort ranges
            // differ from IW4; the two visibility table counts are retained.
            int modelCount = WorldCount(start, "static-model count");
            int staticSurfaceCount = WorldCount(start + 4, "static surface count");
            int modelVisCount = WorldCount(start + 0x20, "static-model visibility count");
            int surfaceVisCount = WorldCount(start + 0x24, "surface visibility count");
            for (int index = 0; index < 3; index++)
                WorldRuntimeReserve(U32(start + 0x28 + index * 4),
                    modelVisCount, 4);
            for (int index = 0; index < 3; index++)
                WorldRuntimeReserve(U32(start + 0x34 + index * 4),
                    surfaceVisCount, 4);
            WorldRuntimeReserve(U32(start + 0x40), Size(modelCount, 2), 2);

            ushort[] sorted = WorldUInt16Array(U32(start + 0x44), 2,
                staticSurfaceCount, "sorted surface indices");
            byte[] instanceBytes = WorldBytes(U32(start + 0x48), 4,
                Size(modelCount, 0x1c), "static-model instances");
            GfxStaticModelInst[] instances = new GfxStaticModelInst[modelCount];
            GfxColor[] groundLighting = new GfxColor[modelCount];
            for (int index = 0; index < modelCount; index++)
            {
                int row = index * 0x1c;
                Bounds bounds = WorldEndpointBounds(instanceBytes, row);
                uint sourceGroundLighting = BinaryPrimitives.ReadUInt32BigEndian(
                    instanceBytes.AsSpan(row + 24));
                // IW3 PS3 0x3006fc/0x300714 uploads this ARGB word unchanged.
                // IW4 0x363940 instead packs its authored RGBA bytes into ARGB.
                groundLighting[index] = new GfxColor(
                    (sourceGroundLighting << 8) | (sourceGroundLighting >> 24));
                instances[index] = new GfxStaticModelInst
                {
                    Bounds = bounds,
                    LightingOrigin = bounds.MidPoint
                };
            }

            Pointer? sourceSurfaces = ReadArray(U32(start + 0x4c), 4,
                Size(surfaceCount, 0x34), "world-surfaces");
            if (sourceSurfaces is null && surfaceCount != 0)
                throw Invalid("GfxWorld surface table is absent");
            var surfaces = new GfxSurface[surfaceCount];
            var surfaceBounds = new GfxSurfaceBounds[surfaceCount];
            if (sourceSurfaces is { } table)
            {
                for (int index = 0; index < surfaceCount; index++)
                {
                    int row = table.Region.FileOffset + index * 0x34;
                    Pointer? material = Material(U32(row + 0x14),
                        table.Region.Address.Add(index * 0x34 + 0x14));
                    Add(dependencies, material);
                    // cb9b8 proves Material at +0x14 of a 0x34-byte row.
                    // The 0x14 triangle header follows the IW4 PS3 layout;
                    // range checks in WorldVisibilityAndBuild reject rows
                    // that cannot be represented with those fields.
                    surfaces[index] = new GfxSurface
                    {
                        Triangles = new SrfTriangles
                        {
                            VertexLayerData = unchecked((int)U32(row)),
                            BaseVertex = unchecked((int)U32(row + 4)),
                            MinVertexIndex = U32(row + 8),
                            VertexCount = U16(row + 0xc),
                            TriCount = U16(row + 0xe),
                            BaseIndex = unchecked((int)U32(row + 0x10))
                        },
                        Material = WorldAsset<MaterialAsset>(material, "render-surface material"),
                        LightmapIndex = _data[row + 0x18],
                        ReflectionProbeIndex = _data[row + 0x19],
                        PrimaryLightIndex = _data[row + 0x1a],
                        Flags = (GfxSurfaceFlags)_data[row + 0x1b]
                    };
                    surfaceBounds[index] = new GfxSurfaceBounds
                    {
                        Bounds = WorldEndpointBounds(row + 0x1c),
                        Unknown18To1F = new byte[8]
                    };
                }
            }

            WorldBytes(U32(start + 0x50), 4, Size(cullGroupCount, 0x20),
                "world cull groups");
            Pointer? sourceDraws = ReadArray(U32(start + 0x54), 4,
                Size(modelCount, 0x2c), "static-model draw instances");
            if (sourceDraws is null && modelCount != 0)
                throw Invalid("GfxWorld static-model draw table is absent");
            var draws = new GfxStaticModelDrawInst[modelCount];
            if (sourceDraws is { } drawTable)
            {
                for (int index = 0; index < modelCount; index++)
                {
                    int row = drawTable.Region.FileOffset + index * 0x2c;
                    Pointer? model = Model(U32(row + 0x20),
                        drawTable.Region.Address.Add(index * 0x2c + 0x20));
                    Add(dependencies, model);
                    float sourceCull = WorldFloat(row);
                    if (!float.IsFinite(sourceCull) || sourceCull < 0)
                        throw Invalid("GfxWorld static-model cull distance is invalid");
                    ushort cullDistance = sourceCull >= ushort.MaxValue
                        ? ushort.MaxValue : (ushort)sourceCull;
                    byte sourceFlags = _data[row + 0x28];
                    if ((sourceFlags & ~1) != 0)
                        throw Unsupported("source static-model draw flags");
                    draws[index] = new GfxStaticModelDrawInst
                    {
                        Placement = new GfxPackedPlacement
                        {
                            Origin = WorldFloats(row + 4, 3),
                            PackedAxis = [U32(row + 0x10), U32(row + 0x14), U32(row + 0x18)],
                            Scale = WorldFloat(row + 0x1c)
                        },
                        Model = WorldAsset<XModelAsset>(model, "static-model XModel"),
                        CullDist = cullDistance,
                        LightingHandle = U16(row + 0x26),
                        ReflectionProbeIndex = _data[row + 0x24],
                        PrimaryLightIndex = _data[row + 0x25],
                        Flags = ((sourceFlags & 1) != 0
                                ? GfxStaticModelDrawInstFlags.NoCastShadow
                                : GfxStaticModelDrawInstFlags.None) |
                            (groundLighting[index].Packed != 0
                                ? GfxStaticModelDrawInstFlags.GroundLighting
                                : GfxStaticModelDrawInstFlags.None),
                        GroundLighting = groundLighting[index]
                    };
                }
            }

            WorldRuntimeReserve(U32(start + 0x58), Size(surfaceCount, 8), 4);
            WorldRuntimeReserve(U32(start + 0x5c), surfaceVisCount, 4);
            uint opaqueEnd = U32(start + 0x10);
            uint litEnd = U32(start + 0x1c);
            uint shadowEnd = litEnd;
            if (_converting)
            {
                // These boundaries describe the material-sorted physical
                // surface table produced by IW4's post-load pass. SortedSurfIndex
                // is a separate spatial ordering: cell AABB ranges and sky
                // entries address its positions, which must remain unchanged.
                var rangeCounts = new uint[4];
                var seen = new HashSet<ushort>();
                foreach (ushort surfaceIndex in sorted)
                {
                    if (surfaceIndex >= surfaces.Length || !seen.Add(surfaceIndex))
                        throw Invalid("GfxWorld has an invalid or duplicate sorted surface index");
                    MaterialAsset material = surfaces[surfaceIndex].Material ??
                        throw Invalid($"GfxWorld surface {surfaceIndex} has no converted material");
                    int range = material.CameraRegion switch
                    {
                        GfxCameraRegionType.LitOpaque => 0,
                        GfxCameraRegionType.LitTrans => 1,
                        GfxCameraRegionType.None => 2,
                        GfxCameraRegionType.Emissive => 3,
                        _ => throw Unsupported($"world material '{material.Info.Name}' camera region")
                    };
                    rangeCounts[range]++;
                }
                for (int index = 0; index < skyStartSurfs.Length; index++)
                {
                    int sourcePosition = skyStartSurfs[index];
                    if ((uint)sourcePosition >= (uint)sorted.Length)
                        throw Invalid("GfxWorld sky refers to an invalid sorted surface position");
                }
                opaqueEnd = rangeCounts[0];
                litEnd = opaqueEnd + rangeCounts[1];
                shadowEnd = litEnd + rangeCounts[2];
            }
            return new GfxWorldDpvsStatic
            {
                SModelCount = (uint)modelCount,
                StaticSurfaceCount = (uint)staticSurfaceCount,
                // The IW4 authored-world codec starts its lit sort prefix at
                // zero; the IW3 range start is in a different sort domain.
                LitSurfsBegin = 0,
                LitSurfsEnd = opaqueEnd,
                VisibilityCounts = [opaqueEnd, litEnd, litEnd,
                    shadowEnd, shadowEnd, (uint)staticSurfaceCount,
                    (uint)modelVisCount, (uint)surfaceVisCount],
                SortedSurfIndex = sorted,
                SModelInsts = instances,
                Surfaces = surfaces,
                SurfaceBounds = surfaceBounds,
                SModelDrawInsts = draws,
                UsageCount = U32(start + 0x60)
            };
        }

        private GfxWorldDpvsDynamic WorldDpvsDynamic(int start, int cellCount)
        {
            // be840: all eight child pointers address runtime visibility
            // storage. The only authored values are the two count pairs.
            uint[] wordCounts = [U32(start), U32(start + 4)];
            uint[] clientCounts = [U32(start + 8), U32(start + 12)];
            for (int index = 0; index < 2; index++)
                WorldRuntimeReserve(U32(start + 0x10 + index * 4),
                    Size(Size(Count(wordCounts[index]), cellCount), sizeof(uint)), 4);
            for (int index = 0; index < 6; index++)
            {
                int owner = index < 3 ? 0 : 1;
                WorldRuntimeReserve(U32(start + 0x18 + index * 4),
                    Size(Count(wordCounts[owner]), 32), 4);
            }
            return new GfxWorldDpvsDynamic
            {
                DynEntClientWordCount = wordCounts,
                DynEntClientCount = clientCounts
            };
        }

        private void AddWorldLightDefinition(Pointer light,
            HashSet<int> dependencies)
        {
            int row = light.Region.FileOffset;
            Pointer? definition = LightDef(U32(row + 0x3c),
                light.Region.Address.Add(0x3c));
            Add(dependencies, definition);
        }

        private GfxWorldAsset? WorldVisibilityAndBuild(
            Region root, string? name, string? baseName,
            int planeCount, int nodeCount, int surfaceCount,
            ushort[] indices, int skySurfCount, int[] skyStartSurfs,
            Pointer? skyImagePointer, int skySamplerState,
            int vertexCount, byte[] packedVertices, byte[] packedLayers,
            int sunPrimaryLightIndex, int primaryLightCount,
            GfxReflectionProbe[] probes, GfxImageAsset?[] probeImages,
            GfxWorldDpvsPlanes planes, GfxCell[] cells,
            GfxCellTree[] cellTrees, GfxCellTreeCount[] cellTreeCounts,
            GfxLightmapArray[] lightmaps, GfxLightGrid grid,
            GfxBrushModel[] models, MaterialMemory[] materialMemory,
            Sunflare sun, float[] outdoorMatrix, Pointer? outdoorImagePointer,
            HashSet<int> dependencies)
        {
            int start = root.FileOffset;
            int cullGroupCount = WorldCount(start + 0xec, "cull-group count");
            GfxShadowGeometry[] shadow = WorldShadowGeometry(
                U32(start + 0x248), primaryLightCount);
            GfxLightRegion[] regions = WorldLightRegions(
                U32(start + 0x24c), primaryLightCount);
            GfxWorldDpvsStatic dpvs = WorldDpvsStatic(start + 0x250,
                surfaceCount, cullGroupCount, dependencies, skyStartSurfs);
            GfxWorldDpvsDynamic dynamic = WorldDpvsDynamic(start + 0x2b4,
                planes.CellCount);
            if (!_converting) return null;
            if (models.Length == 0 || models[0].StartSurfIndex != 0 ||
                models[0].SurfaceCount != dpvs.StaticSurfaceCount)
                throw Invalid("GfxWorld brush model zero does not match the static surface prefix");
            for (int index = 0; index < models.Length; index++)
            {
                GfxBrushModel model = models[index];
                if (model.SurfaceCount == 0) continue;
                if ((long)model.StartSurfIndex + model.SurfaceCount > surfaceCount ||
                    index > 0 && model.StartSurfIndex < dpvs.StaticSurfaceCount)
                    throw Invalid($"GfxWorld brush model {index} has an invalid surface range");
            }
            Bounds sourceWorldBounds = WorldEndpointBounds(start + 0x164);
            GfxImageAsset? skyImage = WorldAsset<GfxImageAsset>(skyImagePointer,
                "sky image");
            if (skySurfCount != 0 && skyImage is null)
                throw Invalid("GfxWorld sky surface list has no image");
            GfxSky[] skies = skyImage is null && skySurfCount == 0
                ? []
                : [new GfxSky
                {
                    SkySurfCount = skySurfCount,
                    SkyStartSurfs = skyStartSurfs,
                    SkyImage = skyImage,
                    SkySamplerState = skySamplerState
                }];
            for (int index = 0; index < dpvs.Surfaces.Count; index++)
            {
                SrfTriangles triangles = dpvs.Surfaces[index].Triangles;
                if (triangles.BaseVertex < 0 || triangles.BaseIndex < 0 ||
                    triangles.BaseVertex + (long)triangles.MinVertexIndex + triangles.VertexCount > vertexCount ||
                    triangles.BaseIndex + (long)triangles.TriCount * 3 > indices.Length)
                    throw Invalid($"GfxWorld surface {index} has an invalid vertex or index slice");
                int end = triangles.BaseIndex + triangles.TriCount * 3;
                for (int offset = triangles.BaseIndex; offset < end; offset++)
                {
                    uint vertex = indices[offset];
                    if (vertex < triangles.MinVertexIndex ||
                        vertex - triangles.MinVertexIndex >= triangles.VertexCount)
                        throw Invalid($"GfxWorld surface {index} has an index outside its vertex slice");
                }
            }
            return new GfxWorldAsset
            {
                Name = name,
                BaseName = baseName,
                PlaneCount = planeCount,
                NodeCount = nodeCount,
                SurfaceCount = surfaceCount,
                SkyCount = (uint)skies.Length,
                Skies = skies,
                SunPrimaryLightIndex = sunPrimaryLightIndex,
                PrimaryLightCount = primaryLightCount,
                SortKeyLitDecal = (int)MaterialSortKey.DecalBottom1,
                // IW4 Formats D3dbspGfxCodec's PS3 effect-decal boundary.
                SortKeyEffectDecal = 0x27,
                SortKeyEffectAuto = (int)MaterialSortKey.EffectAutoSort,
                SortKeyDistortion = (int)MaterialSortKey.Distortion,
                DpvsPlanes = planes,
                CellTreeCounts = cellTreeCounts,
                CellTrees = cellTrees,
                Cells = cells,
                WorldDraw = new GfxWorldDraw
                {
                    ReflectionProbeCount = (uint)probes.Length,
                    ReflectionProbeImagePointers = new XPointer<GfxImageAsset>[probes.Length],
                    ReflectionProbeImages = probeImages,
                    ReflectionProbeOrigins = probes,
                    LightmapCount = lightmaps.Length,
                    Lightmaps = lightmaps,
                    VertexCount = (uint)vertexCount,
                    VertexData = new GfxWorldVertexData { PackedVertices = packedVertices },
                    VertexLayerDataSize = (uint)packedLayers.Length,
                    VertexLayerData = new GfxWorldVertexLayerData
                    {
                        PackedLayerData = packedLayers
                    },
                    IndexCount = indices.Length,
                    Indices = indices
                },
                LightGrid = grid,
                ModelCount = models.Length,
                Models = models,
                Mins = [sourceWorldBounds.MidPoint.X,
                    sourceWorldBounds.MidPoint.Y, sourceWorldBounds.MidPoint.Z],
                Maxs = [sourceWorldBounds.HalfSize.X,
                    sourceWorldBounds.HalfSize.Y, sourceWorldBounds.HalfSize.Z],
                Checksum = U32(start + 0x17c),
                MaterialMemoryCount = materialMemory.Length,
                MaterialMemory = materialMemory,
                Sun = sun,
                OutdoorLookupMatrix = outdoorMatrix,
                OutdoorImage = WorldAsset<GfxImageAsset>(outdoorImagePointer,
                    "outdoor image"),
                ShadowGeom = shadow,
                LightRegions = regions,
                Dpvs = dpvs,
                DpvsDyn = dynamic,
                FogTypesAllowed = FogTypesAllowed.Normal,
                Pad279To27B = new byte[3],
                FragmentProgramUploadCapacity =
                    MapConverter.Game.IW3.PC.Conversion.Iw3PcMapConverter.FragmentProgramUploadCapacity
            };
        }

        private Bounds WorldEndpointBounds(byte[] data, int row)
        {
            float[] mins = WorldFloats(data, row, 3);
            float[] maxs = WorldFloats(data, row + 12, 3);
            return WorldBounds(mins, maxs);
        }

        private Bounds WorldBounds(float[] mins, float[] maxs)
        {
            float[] middle = new float[3], half = new float[3];
            for (int axis = 0; axis < 3; axis++)
            {
                if (!float.IsFinite(mins[axis]) || !float.IsFinite(maxs[axis]) ||
                    mins[axis] > maxs[axis])
                    throw Invalid("GfxWorld has invalid source endpoint bounds");
                middle[axis] = (float)(((double)mins[axis] + maxs[axis]) * 0.5);
                half[axis] = middle[axis] - mins[axis];
            }
            return new Bounds
            {
                MidPoint = new Vec3 { X = middle[0], Y = middle[1], Z = middle[2] },
                HalfSize = new Vec3 { X = half[0], Y = half[1], Z = half[2] }
            };
        }

        private T? WorldAsset<T>(Pointer? pointer, string field) where T : class
        {
            if (pointer is null) return null;
            if (!_converting) return null;
            return pointer.Value as T ?? throw new InvalidDataException(
                $"GfxWorld {field} has no converted {typeof(T).Name} asset");
        }

        private float WorldFloat(int offset) =>
            BinaryPrimitives.ReadSingleBigEndian(_data.AsSpan(offset));

        private static float WorldFloat(byte[] data, int offset) =>
            BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(offset));

        private static float[] WorldFloats(byte[] data, int offset, int count)
        {
            var values = new float[count];
            for (int index = 0; index < count; index++)
                values[index] = WorldFloat(data, offset + index * 4);
            return values;
        }

        private void WorldRuntimeReserve(uint raw, int byteCount, int alignment)
        {
            if (raw == 0) return;
            Push(1);
            try { Align(alignment); Reserve(byteCount); }
            finally { Pop(); }
        }
    }
}
