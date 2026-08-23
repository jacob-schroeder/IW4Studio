using System.Buffers.Binary;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Math;

namespace IW4Map;

internal static class D3dbspGfxCodec
{
    private const int DiskTriangleSoupSize = 24;
    private const int DiskVertexSize = 68;
    private const int DiskModelSize = 48;
    private const int PositionStride = 16;
    private const int LayerStride = 28;
    private const byte NoLightmapIndex = 0x1f;
    private const int SortKeyLitDecal = 0x06;
    private const int SortKeyEffectDecal = 0x27;
    private const int SortKeyEffectAuto = 0x30;
    private const int SortKeyDistortion = 0x2b;
    internal const string FullbrightPrimaryLightmapImageName = "*lightmap0_primary";
    internal const string FullbrightSecondaryLightmapImageName = "*lightmap0_secondary";

    public static GfxWorldAsset DecodeWorld(
        string assetName,
        D3dbspFile file,
        IReadOnlyList<MaterialAsset> materials,
        int primaryLightCount,
        int sunPrimaryLightIndex,
        int ps3WorldDrawPayloadCapacity,
        uint checksum,
        GfxLightGrid lightGrid,
        IReadOnlyList<GfxLightRegion> lightRegions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetName);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(lightGrid);
        ArgumentNullException.ThrowIfNull(lightRegions);
        if (primaryLightCount < 0 || sunPrimaryLightIndex < 0 ||
            sunPrimaryLightIndex > primaryLightCount)
        {
            throw new InvalidDataException("The primary-light range is invalid.");
        }
        if (lightRegions.Count != primaryLightCount)
        {
            throw new InvalidDataException(
                $"The light-region table has {lightRegions.Count} rows; expected {primaryLightCount}.");
        }
        if (ps3WorldDrawPayloadCapacity <= 0)
        {
            throw new InvalidDataException(
                "The PS3 template GfxWorld has no world-draw payload capacity.");
        }

        ReadOnlySpan<byte> triangleBytes = SelectRequiredLump(
            file,
            D3dbspLumpType.UnlayeredTriangles,
            D3dbspLumpType.Triangles);
        ReadOnlySpan<byte> vertexBytes = SelectRequiredLump(
            file,
            D3dbspLumpType.UnlayeredDrawVerts,
            D3dbspLumpType.DrawVerts);
        ReadOnlySpan<byte> indexBytes = SelectRequiredLump(
            file,
            D3dbspLumpType.UnlayeredDrawIndices,
            D3dbspLumpType.DrawIndices);

        int surfaceCount = GetElementCount(
            triangleBytes,
            DiskTriangleSoupSize,
            "render triangle soup");
        int vertexCount = GetElementCount(vertexBytes, DiskVertexSize, "render vertex");
        int sourceIndexCount = GetElementCount(indexBytes, sizeof(ushort), "render index");
        if (surfaceCount == 0)
            throw new InvalidDataException("The d3dbsp has no render surfaces.");
        if (surfaceCount > ushort.MaxValue)
            throw new InvalidDataException("The render surface count exceeds the IW4 ushort range.");
        if (vertexCount == 0)
            throw new InvalidDataException("The d3dbsp has no render vertices.");

        var positions = new Vec3[vertexCount];
        var packedPositions = new byte[checked(vertexCount * PositionStride)];
        var packedLayers = new byte[checked(vertexCount * LayerStride)];
        for (int index = 0; index < vertexCount; index++)
        {
            ReadOnlySpan<byte> source = vertexBytes.Slice(index * DiskVertexSize, DiskVertexSize);
            Vec3 position = ReadVec3(source, 0);
            Vec3 normal = ReadVec3(source, 12);
            Vec3 tangent = ReadVec3(source, 44);
            Vec3 binormal = ReadVec3(source, 56);
            positions[index] = position;

            Span<byte> positionRow = packedPositions.AsSpan(
                index * PositionStride,
                PositionStride);
            WriteSingleBigEndian(positionRow, 0, position.X);
            WriteSingleBigEndian(positionRow, 4, position.Y);
            WriteSingleBigEndian(positionRow, 8, position.Z);
            WriteSingleBigEndian(
                positionRow,
                12,
                CalculateBinormalSign(normal, tangent, binormal));

            Span<byte> layerRow = packedLayers.AsSpan(index * LayerStride, LayerStride);
            // DiskGfxVertex stores BGRA; the RSX U8N stream consumes RGBA.
            layerRow[0] = source[26];
            layerRow[1] = source[25];
            layerRow[2] = source[24];
            layerRow[3] = source[27];
            WriteSingleBigEndian(layerRow, 4, ReadSingle(source, 28));
            WriteSingleBigEndian(layerRow, 8, ReadSingle(source, 32));
            WriteSingleBigEndian(layerRow, 12, ReadSingle(source, 36));
            WriteSingleBigEndian(layerRow, 16, ReadSingle(source, 40));
            BinaryPrimitives.WriteUInt32BigEndian(layerRow[20..], PackSignedNormal(normal));
            BinaryPrimitives.WriteUInt32BigEndian(layerRow[24..], PackSignedNormal(tangent));
        }

        var surfaces = new GfxSurface[surfaceCount];
        var surfaceBounds = new GfxSurfaceBounds[surfaceCount];
        var outputIndices = new List<ushort>(sourceIndexCount);
        BoundsAccumulator worldBounds = new();
        bool needsFullbrightLightmap = false;
        for (int surfaceIndex = 0; surfaceIndex < surfaceCount; surfaceIndex++)
        {
            ReadOnlySpan<byte> row = triangleBytes.Slice(
                surfaceIndex * DiskTriangleSoupSize,
                DiskTriangleSoupSize);
            int materialIndex = BinaryPrimitives.ReadUInt16LittleEndian(row);
            if ((uint)materialIndex >= (uint)materials.Count)
            {
                throw new InvalidDataException(
                    $"Render surface {surfaceIndex} references material {materialIndex}; the material table has {materials.Count} rows.");
            }

            uint firstVertexRaw = BinaryPrimitives.ReadUInt32LittleEndian(row[12..]);
            if (firstVertexRaw > int.MaxValue)
                throw new InvalidDataException($"Render surface {surfaceIndex} has an invalid first vertex.");
            int firstVertex = (int)firstVertexRaw;
            int localVertexCount = BinaryPrimitives.ReadUInt16LittleEndian(row[16..]);
            int localIndexCount = BinaryPrimitives.ReadUInt16LittleEndian(row[18..]);
            int firstIndex = BinaryPrimitives.ReadInt32LittleEndian(row[20..]);
            if (localIndexCount == 0 || localIndexCount % 3 != 0)
            {
                throw new InvalidDataException(
                    $"Render surface {surfaceIndex} has invalid index count {localIndexCount}.");
            }
            ValidateSlice(firstVertex, localVertexCount, vertexCount, $"Render surface {surfaceIndex} vertex");
            ValidateSlice(firstIndex, localIndexCount, sourceIndexCount, $"Render surface {surfaceIndex} index");

            int baseIndex = outputIndices.Count;
            BoundsAccumulator bounds = new();
            for (int localIndexOffset = 0; localIndexOffset < localIndexCount; localIndexOffset++)
            {
                ushort localIndex = BinaryPrimitives.ReadUInt16LittleEndian(
                    indexBytes.Slice((firstIndex + localIndexOffset) * sizeof(ushort), sizeof(ushort)));
                if (localIndex >= localVertexCount)
                {
                    throw new InvalidDataException(
                        $"Render surface {surfaceIndex} index {localIndexOffset} references local vertex {localIndex}; the surface has {localVertexCount} vertices.");
                }

                outputIndices.Add(localIndex);
                bounds.Add(positions[firstVertex + localIndex]);
            }

            Bounds decodedBounds = bounds.ToBounds($"Render surface {surfaceIndex}");
            worldBounds.Add(decodedBounds);
            byte primaryLightIndex = row[4];
            if (primaryLightIndex >= primaryLightCount)
            {
                throw new InvalidDataException(
                    $"Render surface {surfaceIndex} references primary light {primaryLightIndex}; the table has {primaryLightCount} rows.");
            }
            byte sourceLightmapIndex = row[2];
            if (sourceLightmapIndex > NoLightmapIndex)
            {
                throw new InvalidDataException(
                    $"Render surface {surfaceIndex} has invalid lightmap index {sourceLightmapIndex}.");
            }
            byte outputLightmapIndex = sourceLightmapIndex;
            if (sourceLightmapIndex != NoLightmapIndex)
            {
                // Native BSP loading creates an all-white lightmap when the
                // light-byte lump is absent. --fullbright uses the same graph
                // while deliberately discarding any compiled light bytes.
                outputLightmapIndex = 0;
                needsFullbrightLightmap = true;
            }
            if (row[3] != 0)
            {
                throw new NotSupportedException(
                    $"Render surface {surfaceIndex} references authored reflection probe {row[3]}.");
            }

            surfaces[surfaceIndex] = new GfxSurface
            {
                Triangles = new SrfTriangles
                {
                    VertexLayerData = checked(firstVertex * LayerStride),
                    BaseVertex = firstVertex,
                    MinVertexIndex = 0,
                    VertexCount = checked((ushort)localVertexCount),
                    TriCount = checked((ushort)(localIndexCount / 3)),
                    BaseIndex = baseIndex
                },
                Material = materials[materialIndex],
                LightmapIndex = outputLightmapIndex,
                ReflectionProbeIndex = 0,
                PrimaryLightIndex = primaryLightIndex,
                Flags = row[5] == 0
                    ? GfxSurfaceFlags.None
                    : GfxSurfaceFlags.CastsSunShadow
            };
            surfaceBounds[surfaceIndex] = new GfxSurfaceBounds
            {
                Bounds = decodedBounds,
                Unknown18To1F = new byte[8]
            };
        }

        Bounds world = worldBounds.ToBounds("Render world");
        ValidateCanonicalSourceSpatialRows(file, world, surfaceCount);
        IReadOnlyList<GfxBrushModel> models = DecodeModels(
            file.GetRequiredData(D3dbspLumpType.Models),
            file.HasLump(D3dbspLumpType.UnlayeredTriangles),
            surfaceCount);
        int staticSurfaceCount = models.Count == 0 ? 0 : models[0].SurfaceCount;
        int staticSurfaceStart = models.Count == 0 ? 0 : models[0].StartSurfIndex;
        if (staticSurfaceCount > ushort.MaxValue)
            throw new InvalidDataException("The world-model surface count exceeds the IW4 ushort range.");

        ushort[] sortedSurfaceIndices = Enumerable.Range(
                staticSurfaceStart,
                staticSurfaceCount)
            .Select(value => checked((ushort)value))
            .ToArray();
        var defaultProbe = new GfxImageAsset { Name = ",*reflection_probe0" };
        IReadOnlyList<GfxLightmapArray> lightmaps;
        if (needsFullbrightLightmap)
        {
            lightmaps =
            [
                new GfxLightmapArray
                {
                    Primary = CreateFullbrightLightmapImage(
                        FullbrightPrimaryLightmapImageName,
                        primary: true),
                    Secondary = CreateFullbrightLightmapImage(
                        FullbrightSecondaryLightmapImageName,
                        primary: false)
                }
            ];
        }
        else
        {
            lightmaps = [];
        }
        // Native allocates this runtime bitset in 16-byte groups, then clears
        // one byte per authored surface during DPVS initialization.
        uint surfaceVisibilityWordCount = checked((uint)(
            4 * ((staticSurfaceCount + 127) >> 7)));

        return new GfxWorldAsset
        {
            Name = assetName,
            BaseName = Path.GetFileNameWithoutExtension(assetName),
            PlaneCount = 0,
            NodeCount = 1,
            SurfaceCount = surfaceCount,
            SkyCount = 0,
            SunPrimaryLightIndex = sunPrimaryLightIndex,
            PrimaryLightCount = primaryLightCount,
            SortKeyLitDecal = SortKeyLitDecal,
            SortKeyEffectDecal = SortKeyEffectDecal,
            SortKeyEffectAuto = SortKeyEffectAuto,
            SortKeyDistortion = SortKeyDistortion,
            DpvsPlanes = new GfxWorldDpvsPlanes
            {
                CellCount = 1,
                // Packed DPVS leaves store cellIndex + 1. Native R_CellForPoint
                // always dereferences the root, even for a single-cell world.
                Nodes = [1]
            },
            CellTreeCounts = [new GfxCellTreeCount(1)],
            CellTrees =
            [
                new GfxCellTree
                {
                    AabbTrees =
                    [
                        new GfxAabbTree
                        {
                            Bounds = world,
                            SurfaceCount = checked((ushort)staticSurfaceCount),
                            StartSurfIndex = checked((ushort)staticSurfaceStart)
                        }
                    ]
                }
            ],
            Cells =
            [
                new GfxCell
                {
                    Bounds = world,
                    ReflectionProbeCount = 1,
                    Pad21 = [0, 0, 0],
                    ReflectionProbes = [0]
                }
            ],
            WorldDraw = new GfxWorldDraw
            {
                ReflectionProbeCount = 1,
                ReflectionProbeImagePointers = [default],
                ReflectionProbeImages = [defaultProbe],
                ReflectionProbeOrigins = [new GfxReflectionProbe(0, 0, 0)],
                LightmapCount = lightmaps.Count,
                Lightmaps = lightmaps,
                VertexCount = checked((uint)vertexCount),
                VertexData = new GfxWorldVertexData
                {
                    PackedVertices = packedPositions
                },
                VertexLayerDataSize = checked((uint)packedLayers.Length),
                VertexLayerData = new GfxWorldVertexLayerData
                {
                    PackedLayerData = packedLayers
                },
                IndexCount = outputIndices.Count,
                Indices = outputIndices.AsReadOnly()
            },
            LightGrid = lightGrid,
            ModelCount = models.Count,
            Models = models,
            Mins = [world.MidPoint.X, world.MidPoint.Y, world.MidPoint.Z],
            Maxs = [world.HalfSize.X, world.HalfSize.Y, world.HalfSize.Z],
            Checksum = checksum,
            Sun = new Sunflare { SunFxPosition = [0, 0, 0] },
            OutdoorLookupMatrix = new float[16],
            ShadowGeom = Enumerable.Range(0, primaryLightCount)
                .Select(_ => new GfxShadowGeometry())
                .ToArray(),
            LightRegions = lightRegions,
            Dpvs = new GfxWorldDpvsStatic
            {
                StaticSurfaceCount = checked((uint)staticSurfaceCount),
                LitSurfsBegin = 0,
                LitSurfsEnd = checked((uint)staticSurfaceCount),
                VisibilityCounts =
                [
                    checked((uint)staticSurfaceCount),
                    checked((uint)staticSurfaceCount),
                    checked((uint)staticSurfaceCount),
                    checked((uint)staticSurfaceCount),
                    checked((uint)staticSurfaceCount),
                    checked((uint)staticSurfaceCount),
                    0,
                    surfaceVisibilityWordCount
                ],
                SortedSurfIndex = sortedSurfaceIndices,
                Surfaces = surfaces,
                SurfaceBounds = surfaceBounds
            },
            DpvsDyn = new GfxWorldDpvsDynamic
            {
                DynEntClientWordCount = [0, 0],
                DynEntClientCount = [0, 0]
            },
            MapVertexChecksum = 0,
            FogTypesAllowed = FogTypesAllowed.Normal,
            Pad279To27B = [0, 0, 0],
            // PS3 extends GfxWorld with two alternating fragment-program upload arenas.
            // Native rejects draw-route publication when this byte capacity is zero.
            UmbraGateCount = ps3WorldDrawPayloadCapacity
        };
    }

    private static GfxImageAsset CreateFullbrightLightmapImage(
        string name,
        bool primary)
    {
        ushort width = primary ? (ushort)1024 : (ushort)512;
        const ushort height = 1024;
        int payloadByteCount = primary ? 1024 * 1024 : 512 * 1024 * 4;
        var payload = new byte[payloadByteCount];
        Array.Fill(payload, byte.MaxValue);
        return new GfxImageAsset
        {
            Format = (byte)(primary
                ? GfxImageBaseFormat.B8
                : GfxImageBaseFormat.A8R8G8B8),
            LevelCount = 1,
            DimensionCount = GfxImageDimension.TwoDimensional,
            TextureControl1 = primary ? 0x0001a9ffu : 0x0001aae4u,
            Width = width,
            Height = height,
            Depth = 1,
            MemoryLocation = GfxImageMemoryLocation.Local,
            MapType = MapType.TwoDimensional,
            TextureSemantic = TextureSemantic.Function,
            Category = ImageCategory.Lightmap,
            CardMemory = checked((uint)payload.Length),
            BaseWidth = width,
            BaseHeight = height,
            BaseDepth = 1,
            BaseLevelCount = 1,
            Cached = GfxImageCached.No,
            PayloadByteCount = payload.Length,
            PayloadBytes = payload,
            Name = name
        };
    }

    public static byte[] EncodeUnlayeredTriangles(
        GfxWorldAsset world,
        ClipMapAsset clipMap)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(clipMap);
        ValidateDeclaredCount(world.SurfaceCount, world.Dpvs.Surfaces.Count, "render surfaces");
        var data = new byte[checked(world.Dpvs.Surfaces.Count * DiskTriangleSoupSize)];
        for (int index = 0; index < world.Dpvs.Surfaces.Count; index++)
        {
            GfxSurface surface = world.Dpvs.Surfaces[index] ??
                throw new InvalidDataException($"Render surface row {index} is null.");
            SrfTriangles triangles = surface.Triangles ??
                throw new InvalidDataException($"Render surface row {index} has no triangle range.");
            if (surface.ReflectionProbeIndex != 0 ||
                (surface.LightmapIndex != NoLightmapIndex &&
                 (world.WorldDraw.LightmapCount != 1 || surface.LightmapIndex != 0)))
            {
                throw new NotSupportedException(
                    $"Render surface row {index} uses a noncanonical lightmap or authored reflection probe; strict d3dbsp encoding supports only the generated fullbright lightmap and default probe.");
            }
            if ((surface.Flags & ~GfxSurfaceFlags.CastsSunShadow) != 0)
            {
                throw new NotSupportedException(
                    $"Render surface row {index} contains unsupported flags 0x{(byte)surface.Flags:X2}.");
            }
            if (triangles.MinVertexIndex != 0 ||
                triangles.VertexLayerData != checked(triangles.BaseVertex * LayerStride))
            {
                throw new NotSupportedException(
                    $"Render surface row {index} does not use the canonical local-index/full-layer vertex layout.");
            }

            int indexCount = checked(triangles.TriCount * 3);
            ValidateSlice(
                triangles.BaseVertex,
                triangles.VertexCount,
                checked((int)world.WorldDraw.VertexCount),
                $"Render surface row {index} vertex");
            ValidateSlice(
                triangles.BaseIndex,
                indexCount,
                world.WorldDraw.Indices.Count,
                $"Render surface row {index} index");
            for (int localIndex = 0; localIndex < indexCount; localIndex++)
            {
                ushort value = world.WorldDraw.Indices[triangles.BaseIndex + localIndex];
                if (value >= triangles.VertexCount)
                {
                    throw new InvalidDataException(
                        $"Render surface row {index} index {localIndex} references local vertex {value}; the surface has {triangles.VertexCount} vertices.");
                }
            }

            int materialIndex = FindMaterialIndex(clipMap.Materials, surface.Material, index);
            if (materialIndex > ushort.MaxValue)
                throw new InvalidDataException($"Render surface row {index} material exceeds the v22 ushort range.");
            Span<byte> row = data.AsSpan(index * DiskTriangleSoupSize, DiskTriangleSoupSize);
            BinaryPrimitives.WriteUInt16LittleEndian(row, (ushort)materialIndex);
            row[2] = surface.LightmapIndex;
            row[3] = 0;
            row[4] = surface.PrimaryLightIndex;
            row[5] = (surface.Flags & GfxSurfaceFlags.CastsSunShadow) != 0 ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteUInt32LittleEndian(row[8..], 0);
            BinaryPrimitives.WriteUInt32LittleEndian(row[12..], checked((uint)triangles.BaseVertex));
            BinaryPrimitives.WriteUInt16LittleEndian(row[16..], triangles.VertexCount);
            BinaryPrimitives.WriteUInt16LittleEndian(row[18..], checked((ushort)indexCount));
            BinaryPrimitives.WriteInt32LittleEndian(row[20..], triangles.BaseIndex);
        }

        return data;
    }

    public static byte[] EncodeUnlayeredDrawVerts(GfxWorldAsset world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (world.WorldDraw.VertexCount > int.MaxValue)
            throw new InvalidDataException("The render vertex count exceeds the process range.");
        int vertexCount = (int)world.WorldDraw.VertexCount;
        int expectedPositionBytes = checked(vertexCount * PositionStride);
        int expectedLayerBytes = checked(vertexCount * LayerStride);
        if (world.WorldDraw.VertexData.PackedVertices.Count != expectedPositionBytes ||
            world.WorldDraw.VertexLayerData.PackedLayerData.Count != expectedLayerBytes ||
            world.WorldDraw.VertexLayerDataSize != expectedLayerBytes)
        {
            throw new InvalidDataException(
                "The render vertex counts do not match the packed position and layer payloads.");
        }

        var data = new byte[checked(vertexCount * DiskVertexSize)];
        for (int index = 0; index < vertexCount; index++)
        {
            ReadOnlySpan<byte> position = world.WorldDraw.VertexData.PackedVertices
                .Skip(index * PositionStride)
                .Take(PositionStride)
                .ToArray();
            ReadOnlySpan<byte> layer = world.WorldDraw.VertexLayerData.PackedLayerData
                .Skip(index * LayerStride)
                .Take(LayerStride)
                .ToArray();
            float x = ReadSingleBigEndian(position, 0);
            float y = ReadSingleBigEndian(position, 4);
            float z = ReadSingleBigEndian(position, 8);
            float binormalSign = ReadSingleBigEndian(position, 12);
            if (binormalSign is not (1.0f or -1.0f))
            {
                throw new NotSupportedException(
                    $"Render vertex {index} binormal sign is {binormalSign}; strict d3dbsp encoding requires +1 or -1.");
            }

            Vec3 normal = UnpackSignedNormal(BinaryPrimitives.ReadUInt32BigEndian(layer[20..]));
            Vec3 tangent = UnpackSignedNormal(BinaryPrimitives.ReadUInt32BigEndian(layer[24..]));
            Vec3 binormal = new()
            {
                X = (normal.Y * tangent.Z - normal.Z * tangent.Y) * binormalSign,
                Y = (normal.Z * tangent.X - normal.X * tangent.Z) * binormalSign,
                Z = (normal.X * tangent.Y - normal.Y * tangent.X) * binormalSign
            };

            Span<byte> row = data.AsSpan(index * DiskVertexSize, DiskVertexSize);
            WriteVec3LittleEndian(row, 0, new Vec3 { X = x, Y = y, Z = z });
            WriteVec3LittleEndian(row, 12, normal);
            // Packed RSX color is RGBA; DiskGfxVertex stores BGRA.
            row[24] = layer[2];
            row[25] = layer[1];
            row[26] = layer[0];
            row[27] = layer[3];
            WriteSingleLittleEndian(row, 28, ReadSingleBigEndian(layer, 4));
            WriteSingleLittleEndian(row, 32, ReadSingleBigEndian(layer, 8));
            WriteSingleLittleEndian(row, 36, ReadSingleBigEndian(layer, 12));
            WriteSingleLittleEndian(row, 40, ReadSingleBigEndian(layer, 16));
            WriteVec3LittleEndian(row, 44, tangent);
            WriteVec3LittleEndian(row, 56, binormal);
        }

        return data;
    }

    public static byte[] EncodeUnlayeredDrawIndices(GfxWorldAsset world)
    {
        ArgumentNullException.ThrowIfNull(world);
        ValidateDeclaredCount(world.WorldDraw.IndexCount, world.WorldDraw.Indices.Count, "render indices");
        var data = new byte[checked(world.WorldDraw.Indices.Count * sizeof(ushort))];
        for (int index = 0; index < world.WorldDraw.Indices.Count; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                data.AsSpan(index * sizeof(ushort), sizeof(ushort)),
                world.WorldDraw.Indices[index]);
        }
        return data;
    }

    public static byte[] EncodeCanonicalUnlayeredAabbTree(GfxWorldAsset world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (world.CellTreeCounts.Count != 1 ||
            world.CellTreeCounts[0].AabbTreeCount != 1 ||
            world.CellTrees.Count != 1 ||
            world.CellTrees[0].AabbTrees.Count != 1)
        {
            throw new NotSupportedException(
                "Strict d3dbsp encoding requires one cell tree containing one terminal AABB row.");
        }

        GfxAabbTree tree = world.CellTrees[0].AabbTrees[0] ??
            throw new InvalidDataException("The render AABB row is null.");
        if (tree.ChildCount != 0 || tree.SModelIndexCount != 0 || tree.SModelIndexes.Count != 0)
        {
            throw new NotSupportedException(
                "Strict d3dbsp encoding requires a terminal render AABB with no static-model indices.");
        }
        ValidateSlice(
            tree.StartSurfIndex,
            tree.SurfaceCount,
            world.Dpvs.Surfaces.Count,
            "Render AABB surface");

        var data = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(
            data,
            tree.SurfaceCount == 0 ? 0u : tree.StartSurfIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), tree.SurfaceCount);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 0);
        return data;
    }

    public static byte[] EncodeCanonicalCell(GfxWorldAsset world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (world.Cells.Count != 1)
            throw new NotSupportedException("Strict d3dbsp encoding requires exactly one render cell.");
        GfxCell cell = world.Cells[0] ??
            throw new InvalidDataException("The render cell is null.");
        if (cell.PortalCount != 0 || cell.Portals.Count != 0)
            throw new NotSupportedException("Strict d3dbsp encoding does not support render portals.");

        Vec3 mins = BoundsEndpoint(cell.Bounds, maximum: false, "Render cell");
        Vec3 maxs = BoundsEndpoint(cell.Bounds, maximum: true, "Render cell");
        var data = new byte[112];
        WriteVec3LittleEndian(data, 0, mins);
        WriteVec3LittleEndian(data, 12, maxs);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(24), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(26), 0);
        return data;
    }

    public static byte[] EncodeModels(GfxWorldAsset world, ClipMapAsset clipMap)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(clipMap);
        ValidateDeclaredCount(world.ModelCount, world.Models.Count, "render models");
        ValidateDeclaredCount(clipMap.NumSubModels, clipMap.CModels.Count, "collision models");
        if (world.Models.Count == 0 || world.Models.Count != clipMap.CModels.Count)
        {
            throw new NotSupportedException(
                "Render and collision model tables must have the same nonzero row count.");
        }

        var data = new byte[checked(world.Models.Count * DiskModelSize)];
        for (int index = 0; index < world.Models.Count; index++)
        {
            GfxBrushModel model = world.Models[index] ??
                throw new InvalidDataException($"Render model row {index} is null.");
            Vec3 midpoint = ReadVec3(model.BoundsMins, $"Render model row {index} midpoint");
            Vec3 halfSize = ReadVec3(model.BoundsMaxs, $"Render model row {index} half-size");
            if (halfSize.X < 0 || halfSize.Y < 0 || halfSize.Z < 0)
                throw new InvalidDataException($"Render model row {index} has a negative half-size.");
            ValidateSlice(
                model.SurfaceCount == 0 ? 0 : model.StartSurfIndex,
                model.SurfaceCount,
                world.Dpvs.Surfaces.Count,
                $"Render model row {index} surface");

            Span<byte> row = data.AsSpan(index * DiskModelSize, DiskModelSize);
            WriteVec3LittleEndian(row, 0, Subtract(midpoint, halfSize));
            WriteVec3LittleEndian(row, 12, Add(midpoint, halfSize));
            BinaryPrimitives.WriteUInt16LittleEndian(row[24..], 0);
            BinaryPrimitives.WriteUInt16LittleEndian(row[26..],
                model.SurfaceCount == 0 ? (ushort)0 : model.StartSurfIndex);
            BinaryPrimitives.WriteUInt16LittleEndian(row[28..], 0);
            BinaryPrimitives.WriteUInt16LittleEndian(row[30..], model.SurfaceCount);

            if (index == 0)
                continue;
            CLeaf leaf = clipMap.CModels[index].Leaf;
            ValidateSlice(
                leaf.FirstCollAabbIndex,
                leaf.CollAabbCount,
                clipMap.AabbTrees.Count,
                $"Collision model row {index} AABB");
            IReadOnlyList<ushort> brushes = D3dbspCollisionCodec.GetTerminalBrushesForEncoding(
                clipMap,
                leaf,
                $"Collision model row {index}");
            int firstBrush = brushes.Count == 0 ? 0 : brushes[0];
            for (int brush = 0; brush < brushes.Count; brush++)
            {
                if (brushes[brush] != firstBrush + brush)
                {
                    throw new NotSupportedException(
                        $"Collision model row {index} brush references are not one ascending contiguous slice.");
                }
            }
            BinaryPrimitives.WriteInt32LittleEndian(row[32..], leaf.FirstCollAabbIndex);
            BinaryPrimitives.WriteInt32LittleEndian(row[36..], leaf.CollAabbCount);
            BinaryPrimitives.WriteInt32LittleEndian(row[40..], firstBrush);
            BinaryPrimitives.WriteInt32LittleEndian(row[44..], brushes.Count);
        }

        return data;
    }

    private static IReadOnlyList<GfxBrushModel> DecodeModels(
        ReadOnlySpan<byte> data,
        bool useUnlayeredSurfaceRange,
        int surfaceCount)
    {
        int count = GetElementCount(data, DiskModelSize, "render brush model");
        if (count == 0)
            throw new InvalidDataException("The d3dbsp has no world brush model.");

        int rangeOffset = useUnlayeredSurfaceRange ? 2 : 0;
        var models = new GfxBrushModel[count];
        for (int index = 0; index < models.Length; index++)
        {
            ReadOnlySpan<byte> row = data.Slice(index * DiskModelSize, DiskModelSize);
            Vec3 mins = ReadVec3(row, 0);
            Vec3 maxs = ReadVec3(row, 12);
            ValidateBounds(mins, maxs, $"Render brush model {index}");
            int firstSurface = BinaryPrimitives.ReadUInt16LittleEndian(row[(24 + rangeOffset)..]);
            int modelSurfaceCount = BinaryPrimitives.ReadUInt16LittleEndian(row[(28 + rangeOffset)..]);
            if (modelSurfaceCount == 0)
                firstSurface = ushort.MaxValue;
            else
                ValidateSlice(firstSurface, modelSurfaceCount, surfaceCount, $"Render brush model {index} surface");

            Vec3 midpoint = new()
            {
                X = (float)(((double)mins.X + maxs.X) * 0.5),
                Y = (float)(((double)mins.Y + maxs.Y) * 0.5),
                Z = (float)(((double)mins.Z + maxs.Z) * 0.5)
            };
            Vec3 halfSize = new()
            {
                X = midpoint.X - mins.X,
                Y = midpoint.Y - mins.Y,
                Z = midpoint.Z - mins.Z
            };
            models[index] = new GfxBrushModel
            {
                // The first IW4 Bounds is writable runtime state. Authored
                // d3dbsp input supplies only the immutable second Bounds.
                BoundsMins = [midpoint.X, midpoint.Y, midpoint.Z],
                BoundsMaxs = [halfSize.X, halfSize.Y, halfSize.Z],
                Radius = MathF.Sqrt(
                    halfSize.X * halfSize.X +
                    halfSize.Y * halfSize.Y +
                    halfSize.Z * halfSize.Z),
                SurfaceCount = checked((ushort)modelSurfaceCount),
                StartSurfIndex = checked((ushort)firstSurface)
            };
        }

        return Array.AsReadOnly(models);
    }

    private static ReadOnlySpan<byte> SelectRequiredLump(
        D3dbspFile file,
        D3dbspLumpType preferred,
        D3dbspLumpType fallback) =>
        file.HasLump(preferred)
            ? file.GetRequiredData(preferred)
            : file.GetRequiredData(fallback);

    private static void ValidateCanonicalSourceSpatialRows(
        D3dbspFile file,
        Bounds worldBounds,
        int surfaceCount)
    {
        ReadOnlySpan<byte> aabb = SelectRequiredLump(
            file,
            D3dbspLumpType.UnlayeredAabbTrees,
            D3dbspLumpType.AabbTrees);
        if (aabb.Length != 12 ||
            BinaryPrimitives.ReadUInt32LittleEndian(aabb) != 0 ||
            BinaryPrimitives.ReadUInt32LittleEndian(aabb[4..]) != (uint)surfaceCount ||
            BinaryPrimitives.ReadUInt32LittleEndian(aabb[8..]) != 0)
        {
            throw new NotSupportedException(
                "Strict fastfile conversion requires one terminal render AABB covering every surface and no static models.");
        }

        ReadOnlySpan<byte> cell = file.GetRequiredData(D3dbspLumpType.Cells);
        if (cell.Length != 112 || cell[24..].IndexOfAnyExcept((byte)0) >= 0)
        {
            throw new NotSupportedException(
                "Strict fastfile conversion requires one render cell with no portal or auxiliary metadata.");
        }
        Vec3 sourceMins = ReadVec3(cell, 0);
        Vec3 sourceMaxs = ReadVec3(cell, 12);
        ValidateBounds(sourceMins, sourceMaxs, "Render cell");
        Vec3 decodedMins = BoundsEndpoint(worldBounds, maximum: false, "Render world");
        Vec3 decodedMaxs = BoundsEndpoint(worldBounds, maximum: true, "Render world");
        if (!SameVec3Bits(sourceMins, decodedMins) ||
            !SameVec3Bits(sourceMaxs, decodedMaxs))
        {
            throw new NotSupportedException(
                "The compiled render-cell bounds do not match the canonical all-surface world bounds.");
        }
    }

    private static uint PackSignedNormal(Vec3 value)
    {
        RequireFinite(value, "Packed normal");
        int x = QuantizeNormal(value.X, 1023);
        int y = QuantizeNormal(value.Y, 1023);
        int z = QuantizeNormal(value.Z, 511);
        return (uint)(x & 0x7ff) |
            ((uint)(y & 0x7ff) << 11) |
            ((uint)(z & 0x3ff) << 22);
    }

    private static float CalculateBinormalSign(
        Vec3 normal,
        Vec3 tangent,
        Vec3 binormal)
    {
        RequireFinite(binormal, "Render vertex binormal");
        float crossX = normal.Y * tangent.Z - normal.Z * tangent.Y;
        float crossY = normal.Z * tangent.X - normal.X * tangent.Z;
        float crossZ = normal.X * tangent.Y - normal.Y * tangent.X;
        float dot = crossX * binormal.X + crossY * binormal.Y + crossZ * binormal.Z;
        return dot < 0.0f ? -1.0f : 1.0f;
    }

    private static int QuantizeNormal(float value, int scale) => checked((int)Math.Round(
        Math.Clamp((double)value, -1.0, 1.0) * scale,
        MidpointRounding.AwayFromZero));

    private static int GetElementCount(
        ReadOnlySpan<byte> data,
        int elementSize,
        string description)
    {
        if (data.Length % elementSize != 0)
        {
            throw new InvalidDataException(
                $"The {description} lump length {data.Length} is not divisible by {elementSize}.");
        }
        return data.Length / elementSize;
    }

    private static void ValidateSlice(int first, int count, int total, string description)
    {
        if (first < 0 || count < 0 || first > total - count)
        {
            throw new InvalidDataException(
                $"{description} slice {first}..{first + (long)count} exceeds the {total}-row table.");
        }
    }

    private static void ValidateBounds(Vec3 mins, Vec3 maxs, string description)
    {
        RequireFinite(mins, description);
        RequireFinite(maxs, description);
        if (mins.X > maxs.X || mins.Y > maxs.Y || mins.Z > maxs.Z)
            throw new InvalidDataException($"{description} has reversed bounds.");
    }

    private static void RequireFinite(Vec3 value, string description)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            throw new InvalidDataException($"{description} contains a non-finite component.");
    }

    private static Vec3 ReadVec3(ReadOnlySpan<byte> data, int offset) => new()
    {
        X = ReadSingle(data, offset),
        Y = ReadSingle(data, offset + 4),
        Z = ReadSingle(data, offset + 8)
    };

    private static float ReadSingle(ReadOnlySpan<byte> data, int offset) =>
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data[offset..]));

    private static void WriteSingleBigEndian(Span<byte> data, int offset, float value)
    {
        if (!float.IsFinite(value))
            throw new InvalidDataException("A render vertex contains a non-finite scalar.");
        BinaryPrimitives.WriteSingleBigEndian(data[offset..], value == 0.0f ? 0.0f : value);
    }

    private static int FindMaterialIndex(
        IReadOnlyList<ClipMaterial> materials,
        MaterialAsset? material,
        int surfaceIndex)
    {
        string name = material?.Info?.Name ??
            throw new InvalidDataException($"Render surface row {surfaceIndex} has no material name.");
        if (name.Length != 0 && name[0] == ',')
            name = name[1..];

        if (string.Equals(name, "w/$default3d", StringComparison.Ordinal))
        {
            int defaultMaterial = FindNamedMaterial(
                materials,
                "$default",
                StringComparison.Ordinal);
            if (defaultMaterial >= 0)
                return defaultMaterial;
        }

        int exact = FindNamedMaterial(materials, name, StringComparison.Ordinal);
        if (exact >= 0)
            return exact;
        if (name.StartsWith("w/", StringComparison.Ordinal))
        {
            exact = FindNamedMaterial(materials, name[2..], StringComparison.Ordinal);
            if (exact >= 0)
                return exact;
            name = name[2..];
        }

        int caseInsensitive = FindNamedMaterial(
            materials,
            name,
            StringComparison.OrdinalIgnoreCase);
        if (caseInsensitive >= 0)
            return caseInsensitive;
        throw new NotSupportedException(
            $"Render surface row {surfaceIndex} material '{material.Info.Name}' does not match the collision material table.");
    }

    private static int FindNamedMaterial(
        IReadOnlyList<ClipMaterial> materials,
        string name,
        StringComparison comparison)
    {
        for (int index = 0; index < materials.Count; index++)
        {
            ClipMaterial material = materials[index] ??
                throw new InvalidDataException($"Collision material row {index} is null.");
            if (string.Equals(material.Name, name, comparison))
                return index;
        }
        return -1;
    }

    private static Vec3 UnpackSignedNormal(uint packed) => new()
    {
        X = SignExtend(packed & 0x7ff, 11) / 1023.0f,
        Y = SignExtend((packed >> 11) & 0x7ff, 11) / 1023.0f,
        Z = SignExtend((packed >> 22) & 0x3ff, 10) / 511.0f
    };

    private static int SignExtend(uint value, int bitCount)
    {
        int shift = 32 - bitCount;
        return ((int)value << shift) >> shift;
    }

    private static float ReadSingleBigEndian(ReadOnlySpan<byte> data, int offset)
    {
        float value = BinaryPrimitives.ReadSingleBigEndian(data[offset..]);
        if (!float.IsFinite(value))
            throw new InvalidDataException("A packed render vertex contains a non-finite scalar.");
        return value;
    }

    private static void WriteSingleLittleEndian(Span<byte> data, int offset, float value)
    {
        if (!float.IsFinite(value))
            throw new InvalidDataException("A render vertex contains a non-finite scalar.");
        BinaryPrimitives.WriteSingleLittleEndian(data[offset..], value == 0.0f ? 0.0f : value);
    }

    private static void WriteVec3LittleEndian(Span<byte> data, int offset, Vec3 value)
    {
        WriteSingleLittleEndian(data, offset, value.X);
        WriteSingleLittleEndian(data, offset + 4, value.Y);
        WriteSingleLittleEndian(data, offset + 8, value.Z);
    }

    private static Vec3 BoundsEndpoint(Bounds bounds, bool maximum, string description)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        RequireFinite(bounds.MidPoint, description);
        RequireFinite(bounds.HalfSize, description);
        if (bounds.HalfSize.X < 0 || bounds.HalfSize.Y < 0 || bounds.HalfSize.Z < 0)
            throw new InvalidDataException($"{description} has a negative half-size.");
        return maximum
            ? Add(bounds.MidPoint, bounds.HalfSize)
            : Subtract(bounds.MidPoint, bounds.HalfSize);
    }

    private static Vec3 ReadVec3(IReadOnlyList<float> values, string description)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count != 3)
            throw new InvalidDataException($"{description} has {values.Count} components instead of 3.");
        Vec3 value = new() { X = values[0], Y = values[1], Z = values[2] };
        RequireFinite(value, description);
        return value;
    }

    private static Vec3 Add(Vec3 left, Vec3 right) => new()
    {
        X = left.X + right.X,
        Y = left.Y + right.Y,
        Z = left.Z + right.Z
    };

    private static Vec3 Subtract(Vec3 left, Vec3 right) => new()
    {
        X = left.X - right.X,
        Y = left.Y - right.Y,
        Z = left.Z - right.Z
    };

    private static bool SameVec3Bits(Vec3 left, Vec3 right) =>
        BitConverter.SingleToInt32Bits(left.X) == BitConverter.SingleToInt32Bits(right.X) &&
        BitConverter.SingleToInt32Bits(left.Y) == BitConverter.SingleToInt32Bits(right.Y) &&
        BitConverter.SingleToInt32Bits(left.Z) == BitConverter.SingleToInt32Bits(right.Z);

    private static void ValidateDeclaredCount(long declared, int actual, string description)
    {
        if (declared != actual)
        {
            throw new InvalidDataException(
                $"The {description} table declares {declared} rows but materializes {actual}.");
        }
    }

    private sealed class BoundsAccumulator
    {
        private float _minX = float.MaxValue;
        private float _minY = float.MaxValue;
        private float _minZ = float.MaxValue;
        private float _maxX = -float.MaxValue;
        private float _maxY = -float.MaxValue;
        private float _maxZ = -float.MaxValue;
        private bool _hasPoint;

        public void Add(Vec3 point)
        {
            RequireFinite(point, "Render bounds");
            _minX = MathF.Min(_minX, point.X);
            _minY = MathF.Min(_minY, point.Y);
            _minZ = MathF.Min(_minZ, point.Z);
            _maxX = MathF.Max(_maxX, point.X);
            _maxY = MathF.Max(_maxY, point.Y);
            _maxZ = MathF.Max(_maxZ, point.Z);
            _hasPoint = true;
        }

        public void Add(Bounds bounds)
        {
            ArgumentNullException.ThrowIfNull(bounds);
            Add(new Vec3
            {
                X = bounds.MidPoint.X - bounds.HalfSize.X,
                Y = bounds.MidPoint.Y - bounds.HalfSize.Y,
                Z = bounds.MidPoint.Z - bounds.HalfSize.Z
            });
            Add(new Vec3
            {
                X = bounds.MidPoint.X + bounds.HalfSize.X,
                Y = bounds.MidPoint.Y + bounds.HalfSize.Y,
                Z = bounds.MidPoint.Z + bounds.HalfSize.Z
            });
        }

        public Bounds ToBounds(string description)
        {
            if (!_hasPoint)
                throw new InvalidDataException($"{description} has no referenced vertices.");
            return new Bounds
            {
                MidPoint = new Vec3
                {
                    X = (_minX + _maxX) * 0.5f,
                    Y = (_minY + _maxY) * 0.5f,
                    Z = (_minZ + _maxZ) * 0.5f
                },
                HalfSize = new Vec3
                {
                    X = (_maxX - _minX) * 0.5f,
                    Y = (_maxY - _minY) * 0.5f,
                    Z = (_maxZ - _minZ) * 0.5f
                }
            };
        }
    }
}
