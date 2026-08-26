using System.Buffers.Binary;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Assets.Math;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.D3dbsp;

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
        int ps3FragmentProgramUploadCapacity,
        uint checksum,
        GfxLightGrid lightGrid,
        IReadOnlyList<GfxLightRegion> lightRegions,
        IReadOnlyList<GfxLightmapArray> lightmaps,
        IReadOnlyList<GfxImageAsset?> reflectionProbeImages,
        IReadOnlyList<GfxReflectionProbe> reflectionProbeOrigins,
        IReadOnlyList<GfxStaticModelInst> staticModelInstances,
        IReadOnlyList<GfxStaticModelDrawInst> staticModelDrawInstances)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetName);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(lightGrid);
        ArgumentNullException.ThrowIfNull(lightRegions);
        ArgumentNullException.ThrowIfNull(lightmaps);
        ArgumentNullException.ThrowIfNull(reflectionProbeImages);
        ArgumentNullException.ThrowIfNull(reflectionProbeOrigins);
        ArgumentNullException.ThrowIfNull(staticModelInstances);
        ArgumentNullException.ThrowIfNull(staticModelDrawInstances);
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
        if (ps3FragmentProgramUploadCapacity <= 0)
        {
            throw new InvalidDataException(
                "The PS3 template GfxWorld has no fragment-program upload capacity.");
        }
        if (reflectionProbeImages.Count == 0 ||
            reflectionProbeImages.Count != reflectionProbeOrigins.Count ||
            reflectionProbeImages.Count > byte.MaxValue)
        {
            throw new InvalidDataException(
                "The reflection-probe image and origin tables must contain the default probe and share one byte-sized count.");
        }

        bool useUnlayeredGeometry = SelectUnlayeredGeometryFamily(file);
        ReadOnlySpan<byte> triangleBytes = file.GetRequiredData(
            useUnlayeredGeometry
                ? D3dbspLumpType.UnlayeredTriangles
                : D3dbspLumpType.Triangles);
        ReadOnlySpan<byte> vertexBytes = file.GetRequiredData(
            useUnlayeredGeometry
                ? D3dbspLumpType.UnlayeredDrawVerts
                : D3dbspLumpType.DrawVerts);
        ReadOnlySpan<byte> indexBytes = file.GetRequiredData(
            useUnlayeredGeometry
                ? D3dbspLumpType.UnlayeredDrawIndices
                : D3dbspLumpType.DrawIndices);

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
                if (lightmaps.Count == 0)
                {
                    // Native BSP loading creates an all-white lightmap when the
                    // light-byte lump is absent. --fullbright uses the same graph
                    // while deliberately discarding any compiled light bytes.
                    outputLightmapIndex = 0;
                    needsFullbrightLightmap = true;
                }
                else if (sourceLightmapIndex >= lightmaps.Count)
                {
                    throw new InvalidDataException(
                        $"Render surface {surfaceIndex} references lightmap {sourceLightmapIndex}; the table has {lightmaps.Count} rows.");
                }
            }
            byte reflectionProbeIndex = row[3];
            if (reflectionProbeIndex >= reflectionProbeImages.Count)
            {
                throw new InvalidDataException(
                    $"Render surface {surfaceIndex} references reflection probe {reflectionProbeIndex}; the table has {reflectionProbeImages.Count} rows.");
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
                ReflectionProbeIndex = reflectionProbeIndex,
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

        int staticModelCount = staticModelInstances.Count;
        if (staticModelCount != staticModelDrawInstances.Count)
        {
            throw new InvalidDataException(
                $"The render static-model graph has {staticModelCount} instance rows and " +
                $"{staticModelDrawInstances.Count} draw rows.");
        }
        if (staticModelCount > ushort.MaxValue)
        {
            throw new InvalidDataException(
                $"The render static-model count exceeds the IW4 ushort range of {ushort.MaxValue}.");
        }

        var staticModelIndices = new ushort[staticModelCount];
        var shadowStaticModelIndices = Enumerable.Range(0, primaryLightCount)
            .Select(_ => new List<ushort>())
            .ToArray();
        for (int index = 0; index < staticModelCount; index++)
        {
            GfxStaticModelInst instance = staticModelInstances[index] ??
                throw new InvalidDataException($"Render static-model instance {index} is null.");
            GfxStaticModelDrawInst draw = staticModelDrawInstances[index] ??
                throw new InvalidDataException($"Render static-model draw instance {index} is null.");
            if (draw.Model is null)
            {
                throw new InvalidDataException(
                    $"Render static-model draw instance {index} has no XModel definition.");
            }

            worldBounds.Add(BoundsEndpoint(
                instance.Bounds,
                maximum: false,
                $"Render static-model instance {index} bounds"));
            worldBounds.Add(BoundsEndpoint(
                instance.Bounds,
                maximum: true,
                $"Render static-model instance {index} bounds"));
            staticModelIndices[index] = checked((ushort)index);
            if (primaryLightCount == 0)
            {
                if (draw.PrimaryLightIndex != 0)
                {
                    throw new InvalidDataException(
                        $"Render static-model draw instance {index} references primary light " +
                        $"{draw.PrimaryLightIndex}, but the world has no primary lights.");
                }
            }
            else
            {
                if (draw.PrimaryLightIndex >= primaryLightCount)
                {
                    throw new InvalidDataException(
                        $"Render static-model draw instance {index} references primary light " +
                        $"{draw.PrimaryLightIndex}; the table has {primaryLightCount} rows.");
                }
                shadowStaticModelIndices[draw.PrimaryLightIndex].Add(checked((ushort)index));
            }
        }

        Bounds world = worldBounds.ToBounds("Render world");
        GfxCell canonicalCell =
            ValidateCanonicalSourceSpatialRows(
                file,
                world,
                surfaceCount,
                reflectionProbeImages.Count);
        IReadOnlyList<GfxBrushModel> models = DecodeModels(
            file.GetRequiredData(D3dbspLumpType.Models),
            useUnlayeredGeometry,
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
        IReadOnlyList<GfxLightmapArray> outputLightmaps;
        if (needsFullbrightLightmap)
        {
            outputLightmaps =
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
            outputLightmaps = lightmaps;
        }
        // Native allocates this runtime bitset in 16-byte groups, then clears
        // one byte per authored surface during DPVS initialization.
        uint surfaceVisibilityWordCount = checked((uint)(
            4 * ((staticSurfaceCount + 127) >> 7)));
        uint staticModelVisibilityWordCount = checked((uint)(
            4 * ((staticModelCount + 127) >> 7)));
        var shadowSurfaceIndices = Enumerable.Range(0, primaryLightCount)
            .Select(_ => new List<ushort>())
            .ToArray();
        foreach (ushort surfaceIndex in sortedSurfaceIndices)
        {
            GfxSurface surface = surfaces[surfaceIndex];
            if ((surface.Flags & GfxSurfaceFlags.CastsSunShadow) != 0)
                shadowSurfaceIndices[surface.PrimaryLightIndex].Add(surfaceIndex);
        }
        GfxShadowGeometry[] shadowGeometry = Enumerable.Range(0, primaryLightCount)
            .Select(index => new GfxShadowGeometry
            {
                SurfaceCount = checked((ushort)shadowSurfaceIndices[index].Count),
                SModelCount = checked((ushort)shadowStaticModelIndices[index].Count),
                SortedSurfIndex = shadowSurfaceIndices[index].AsReadOnly(),
                SModelIndex = shadowStaticModelIndices[index].AsReadOnly()
            })
            .ToArray();

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
                            StartSurfIndex = checked((ushort)staticSurfaceStart),
                            SModelIndexCount = checked((ushort)staticModelCount),
                            SModelIndexes = Array.AsReadOnly(staticModelIndices)
                        }
                    ]
                }
            ],
            Cells =
            [
                canonicalCell
            ],
            WorldDraw = new GfxWorldDraw
            {
                ReflectionProbeCount = checked((uint)reflectionProbeImages.Count),
                ReflectionProbeImagePointers =
                    new XPointer<GfxImageAsset>[reflectionProbeImages.Count],
                ReflectionProbeImages = reflectionProbeImages,
                ReflectionProbeOrigins = reflectionProbeOrigins,
                LightmapCount = outputLightmaps.Count,
                Lightmaps = outputLightmaps,
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
            ShadowGeom = shadowGeometry,
            LightRegions = lightRegions,
            Dpvs = new GfxWorldDpvsStatic
            {
                SModelCount = checked((uint)staticModelCount),
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
                    staticModelVisibilityWordCount,
                    surfaceVisibilityWordCount
                ],
                SortedSurfIndex = sortedSurfaceIndices,
                SModelInsts = staticModelInstances,
                Surfaces = surfaces,
                SurfaceBounds = surfaceBounds,
                SModelDrawInsts = staticModelDrawInstances
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
            FragmentProgramUploadCapacity = ps3FragmentProgramUploadCapacity
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

    public static (
        byte[] Triangles,
        byte[] DrawVerts,
        byte[] DrawIndices) EncodeUnlayeredGeometry(
        GfxWorldAsset world,
        ClipMapAsset clipMap,
        IReadOnlyList<D3dbspLightmapTile> lightmapTiles)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(clipMap);
        ArgumentNullException.ThrowIfNull(lightmapTiles);
        ValidateDeclaredCount(world.SurfaceCount, world.Dpvs.Surfaces.Count, "render surfaces");
        ValidateDeclaredCount(world.WorldDraw.IndexCount, world.WorldDraw.Indices.Count, "render indices");
        if (world.WorldDraw.VertexCount > int.MaxValue)
            throw new InvalidDataException("The render vertex count exceeds the process range.");
        int sourceVertexCount = (int)world.WorldDraw.VertexCount;
        int expectedPositionBytes = checked(sourceVertexCount * PositionStride);
        if (world.WorldDraw.VertexData.PackedVertices.Count != expectedPositionBytes ||
            world.WorldDraw.VertexLayerData.PackedLayerData.Count !=
                world.WorldDraw.VertexLayerDataSize)
        {
            throw new InvalidDataException(
                "The render vertex counts do not match the packed position and layer payloads.");
        }

        int[] runtimeSlotByAuthoredIndex =
            world.Dpvs.GetRuntimeSlotByAuthoredIndex();
        int outputVertexCount = 0;
        int outputIndexCount = 0;
        foreach (int runtimeSlot in runtimeSlotByAuthoredIndex)
        {
            GfxSurface surface = world.Dpvs.Surfaces[runtimeSlot] ??
                throw new InvalidDataException(
                    $"Render surface runtime row {runtimeSlot} is null.");
            outputVertexCount = checked(
                outputVertexCount + surface.Triangles.VertexCount);
            outputIndexCount = checked(
                outputIndexCount + surface.Triangles.TriCount * 3);
        }

        byte[] packedPositions = world.WorldDraw.VertexData.PackedVertices.ToArray();
        byte[] packedLayers = world.WorldDraw.VertexLayerData.PackedLayerData.ToArray();
        var trianglesData = new byte[checked(
            runtimeSlotByAuthoredIndex.Length * DiskTriangleSoupSize)];
        var vertexData = new byte[checked(outputVertexCount * DiskVertexSize)];
        var indexData = new byte[checked(outputIndexCount * sizeof(ushort))];
        int outputFirstVertex = 0;
        int outputFirstIndex = 0;
        for (int authoredIndex = 0;
             authoredIndex < runtimeSlotByAuthoredIndex.Length;
             authoredIndex++)
        {
            int runtimeSlot = runtimeSlotByAuthoredIndex[authoredIndex];
            GfxSurface surface = world.Dpvs.Surfaces[runtimeSlot] ??
                throw new InvalidDataException(
                    $"Render surface runtime row {runtimeSlot} is null.");
            SrfTriangles triangles = surface.Triangles ??
                throw new InvalidDataException(
                    $"Render surface row {authoredIndex} has no triangle range.");
            if ((surface.Flags & ~GfxSurfaceFlags.CastsSunShadow) != 0)
            {
                throw new NotSupportedException(
                    $"Render surface row {authoredIndex} contains unsupported flags 0x{(byte)surface.Flags:X2}.");
            }
            if (surface.PrimaryLightIndex >= world.PrimaryLightCount)
            {
                throw new InvalidDataException(
                    $"Render surface row {authoredIndex} references primary light {surface.PrimaryLightIndex}; the table has {world.PrimaryLightCount} rows.");
            }
            if (surface.ReflectionProbeIndex >= world.WorldDraw.ReflectionProbeCount)
            {
                throw new InvalidDataException(
                    $"Render surface row {authoredIndex} references reflection probe {surface.ReflectionProbeIndex}; the table has {world.WorldDraw.ReflectionProbeCount} rows.");
            }

            int indexCount = checked(triangles.TriCount * 3);
            ValidateSlice(
                triangles.BaseIndex,
                indexCount,
                world.WorldDraw.Indices.Count,
                $"Render surface row {authoredIndex} index");
            if (triangles.MinVertexIndex > int.MaxValue)
            {
                throw new InvalidDataException(
                    $"Render surface row {authoredIndex} has an invalid minimum vertex index.");
            }
            int firstSourceIndex = (int)triangles.MinVertexIndex;
            int firstWorldVertex = checked(triangles.BaseVertex + firstSourceIndex);
            ValidateSlice(
                firstWorldVertex,
                triangles.VertexCount,
                sourceVertexCount,
                $"Render surface row {authoredIndex} vertex");

            int layerStride = ResolveLayerStride(
                world,
                surface,
                authoredIndex);
            ValidateLayerSlice(
                triangles,
                layerStride,
                packedLayers.Length,
                authoredIndex);
            D3dbspLightmapTile? lightmapTile = ResolveLightmapTile(
                surface,
                firstSourceIndex,
                layerStride,
                packedLayers,
                lightmapTiles,
                authoredIndex);

            int materialIndex = FindMaterialIndex(
                clipMap.Materials,
                surface.Material,
                authoredIndex);
            if (materialIndex > ushort.MaxValue)
            {
                throw new InvalidDataException(
                    $"Render surface row {authoredIndex} material exceeds the v22 ushort range.");
            }

            Span<byte> row = trianglesData.AsSpan(
                authoredIndex * DiskTriangleSoupSize,
                DiskTriangleSoupSize);
            BinaryPrimitives.WriteUInt16LittleEndian(row, (ushort)materialIndex);
            row[2] = lightmapTile?.D3dbspLightmapIndex ?? NoLightmapIndex;
            row[3] = surface.ReflectionProbeIndex;
            row[4] = surface.PrimaryLightIndex;
            row[5] = (surface.Flags & GfxSurfaceFlags.CastsSunShadow) != 0
                ? (byte)1
                : (byte)0;
            BinaryPrimitives.WriteUInt32LittleEndian(row[8..], 0);
            BinaryPrimitives.WriteUInt32LittleEndian(
                row[12..],
                checked((uint)outputFirstVertex));
            BinaryPrimitives.WriteUInt16LittleEndian(row[16..], triangles.VertexCount);
            BinaryPrimitives.WriteUInt16LittleEndian(row[18..], checked((ushort)indexCount));
            BinaryPrimitives.WriteInt32LittleEndian(row[20..], outputFirstIndex);

            for (int vertexSlot = 0;
                 vertexSlot < triangles.VertexCount;
                 vertexSlot++)
            {
                int sourceIndex = checked(firstSourceIndex + vertexSlot);
                int worldVertexIndex = checked(triangles.BaseVertex + sourceIndex);
                int layerOffset = checked(
                    triangles.VertexLayerData + sourceIndex * layerStride);
                WriteDiskVertex(
                    vertexData.AsSpan(
                        checked((outputFirstVertex + vertexSlot) * DiskVertexSize),
                        DiskVertexSize),
                    packedPositions.AsSpan(
                        checked(worldVertexIndex * PositionStride),
                        PositionStride),
                    packedLayers.AsSpan(layerOffset, LayerStride),
                    lightmapTile,
                    authoredIndex,
                    vertexSlot);
            }

            for (int indexOffset = 0; indexOffset < indexCount; indexOffset++)
            {
                ushort sourceIndex = world.WorldDraw.Indices[
                    triangles.BaseIndex + indexOffset];
                int localIndex = sourceIndex - firstSourceIndex;
                if ((uint)localIndex >= triangles.VertexCount)
                {
                    throw new InvalidDataException(
                        $"Render surface row {authoredIndex} index {indexOffset} references source vertex {sourceIndex}; expected {firstSourceIndex}..{firstSourceIndex + triangles.VertexCount - 1}.");
                }
                BinaryPrimitives.WriteUInt16LittleEndian(
                    indexData.AsSpan(
                        checked((outputFirstIndex + indexOffset) * sizeof(ushort)),
                        sizeof(ushort)),
                    checked((ushort)localIndex));
            }

            outputFirstVertex = checked(
                outputFirstVertex + triangles.VertexCount);
            outputFirstIndex = checked(outputFirstIndex + indexCount);
        }

        return (trianglesData, vertexData, indexData);
    }

    private static int ResolveLayerStride(
        GfxWorldAsset world,
        GfxSurface surface,
        int surfaceIndex)
    {
        MaterialWorldVertexFormat? format = surface.Material?
            .TechniqueSet?
            .WorldVertexFormat;
        if (format.HasValue && Enum.IsDefined(format.Value))
        {
            int backendRow = WorldVertexLayout.ResolveGenericFallbackBackendRow(
                format.Value);
            if (WorldVertexLayout.TryGetStreamStride(
                    backendRow,
                    streamIndex: 1,
                    out byte stride) &&
                stride >= LayerStride)
            {
                return stride;
            }
        }

        if (world.WorldDraw.VertexLayerDataSize ==
            checked(world.WorldDraw.VertexCount * LayerStride))
        {
            return LayerStride;
        }

        throw new NotSupportedException(
            $"Render surface row {surfaceIndex} has no resolved PS3 world-vertex layout.");
    }

    private static void ValidateLayerSlice(
        SrfTriangles triangles,
        int layerStride,
        int layerByteCount,
        int surfaceIndex)
    {
        if (triangles.VertexCount == 0)
        {
            throw new InvalidDataException(
                $"Render surface row {surfaceIndex} has no vertices.");
        }
        long firstOffset = triangles.VertexLayerData +
            (long)triangles.MinVertexIndex * layerStride;
        long lastEnd = firstOffset +
            (long)(triangles.VertexCount - 1) * layerStride +
            LayerStride;
        if (firstOffset < 0 || lastEnd > layerByteCount)
        {
            throw new InvalidDataException(
                $"Render surface row {surfaceIndex} vertex-layer slice {firstOffset}..{lastEnd} exceeds the {layerByteCount}-byte payload.");
        }
    }

    private static D3dbspLightmapTile? ResolveLightmapTile(
        GfxSurface surface,
        int firstSourceIndex,
        int layerStride,
        ReadOnlySpan<byte> packedLayers,
        IReadOnlyList<D3dbspLightmapTile> lightmapTiles,
        int surfaceIndex)
    {
        if (surface.LightmapIndex == NoLightmapIndex)
            return null;

        D3dbspLightmapTile[] candidates = lightmapTiles
            .Where(tile => tile.RuntimeLightmapIndex == surface.LightmapIndex)
            .ToArray();
        if (candidates.Length == 0)
        {
            throw new InvalidDataException(
                $"Render surface row {surfaceIndex} references runtime lightmap {surface.LightmapIndex}, which has no d3dbsp tiles.");
        }

        double sumU = 0;
        double sumV = 0;
        for (int vertexSlot = 0;
             vertexSlot < surface.Triangles.VertexCount;
             vertexSlot++)
        {
            int sourceIndex = checked(firstSourceIndex + vertexSlot);
            int layerOffset = checked(
                surface.Triangles.VertexLayerData + sourceIndex * layerStride);
            sumU += ReadSingleBigEndian(packedLayers[layerOffset..], 12);
            sumV += ReadSingleBigEndian(packedLayers[layerOffset..], 16);
        }

        float meanU = checked((float)(sumU / surface.Triangles.VertexCount));
        float meanV = checked((float)(sumV / surface.Triangles.VertexCount));
        if (!float.IsFinite(meanU) || !float.IsFinite(meanV))
        {
            throw new InvalidDataException(
                $"Render surface row {surfaceIndex} has non-finite lightmap coordinates.");
        }
        D3dbspLightmapTile layout = candidates[0];
        int tileX = System.Math.Clamp(
            (int)MathF.Floor(meanU * layout.TilesWide),
            0,
            layout.TilesWide - 1);
        int tileY = System.Math.Clamp(
            (int)MathF.Floor(meanV * layout.TilesHigh),
            0,
            layout.TilesHigh - 1);
        D3dbspLightmapTile? selected = candidates
            .Cast<D3dbspLightmapTile?>()
            .SingleOrDefault(tile =>
                tile!.Value.TileX == tileX && tile.Value.TileY == tileY);
        if (!selected.HasValue)
        {
            throw new InvalidDataException(
                $"Render surface row {surfaceIndex} lightmap coordinates resolve to missing tile ({tileX}, {tileY}).");
        }

        const float tolerance = 1.0f / 4096.0f;
        D3dbspLightmapTile result = selected.Value;
        for (int vertexSlot = 0;
             vertexSlot < surface.Triangles.VertexCount;
             vertexSlot++)
        {
            int sourceIndex = checked(firstSourceIndex + vertexSlot);
            int layerOffset = checked(
                surface.Triangles.VertexLayerData + sourceIndex * layerStride);
            float sourceU = result.ToD3dbspU(
                ReadSingleBigEndian(packedLayers[layerOffset..], 12));
            float sourceV = result.ToD3dbspV(
                ReadSingleBigEndian(packedLayers[layerOffset..], 16));
            if (!float.IsFinite(sourceU) || !float.IsFinite(sourceV) ||
                sourceU < -tolerance || sourceU > 1.0f + tolerance ||
                sourceV < -tolerance || sourceV > 1.0f + tolerance)
            {
                throw new NotSupportedException(
                    $"Render surface row {surfaceIndex} crosses reconstructed lightmap tile {result.D3dbspLightmapIndex}.");
            }
        }

        return result;
    }

    private static void WriteDiskVertex(
        Span<byte> row,
        ReadOnlySpan<byte> position,
        ReadOnlySpan<byte> layer,
        D3dbspLightmapTile? lightmapTile,
        int surfaceIndex,
        int vertexSlot)
    {
        float x = ReadSingleBigEndian(position, 0);
        float y = ReadSingleBigEndian(position, 4);
        float z = ReadSingleBigEndian(position, 8);
        float binormalSign = ReadSingleBigEndian(position, 12);
        if (binormalSign is not (1.0f or -1.0f))
        {
            throw new NotSupportedException(
                $"Render surface row {surfaceIndex} vertex {vertexSlot} has binormal sign {binormalSign}; expected +1 or -1.");
        }

        Vec3 normal = UnpackSignedNormal(
            BinaryPrimitives.ReadUInt32BigEndian(layer[20..]));
        Vec3 tangent = UnpackSignedNormal(
            BinaryPrimitives.ReadUInt32BigEndian(layer[24..]));
        Vec3 binormal = new()
        {
            X = (normal.Y * tangent.Z - normal.Z * tangent.Y) * binormalSign,
            Y = (normal.Z * tangent.X - normal.X * tangent.Z) * binormalSign,
            Z = (normal.X * tangent.Y - normal.Y * tangent.X) * binormalSign
        };

        WriteVec3LittleEndian(row, 0, new Vec3 { X = x, Y = y, Z = z });
        WriteVec3LittleEndian(row, 12, normal);
        // Packed RSX color is RGBA; DiskGfxVertex stores BGRA.
        row[24] = layer[2];
        row[25] = layer[1];
        row[26] = layer[0];
        row[27] = layer[3];
        WriteSingleLittleEndian(row, 28, ReadSingleBigEndian(layer, 4));
        WriteSingleLittleEndian(row, 32, ReadSingleBigEndian(layer, 8));
        float lightmapU = ReadSingleBigEndian(layer, 12);
        float lightmapV = ReadSingleBigEndian(layer, 16);
        if (lightmapTile is { } tile)
        {
            lightmapU = tile.ToD3dbspU(lightmapU);
            lightmapV = tile.ToD3dbspV(lightmapV);
        }
        WriteSingleLittleEndian(row, 36, lightmapU);
        WriteSingleLittleEndian(row, 40, lightmapV);
        WriteVec3LittleEndian(row, 44, tangent);
        WriteVec3LittleEndian(row, 56, binormal);
    }

    public static byte[] EncodeCanonicalUnlayeredAabbTree(GfxWorldAsset world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var data = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(data, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(
            data.AsSpan(4),
            checked((uint)world.Dpvs.Surfaces.Count));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 0);
        return data;
    }

    public static byte[] EncodeCanonicalCell(GfxWorldAsset world)
    {
        ArgumentNullException.ThrowIfNull(world);
        Bounds bounds = ReadWorldBounds(world);
        Vec3 mins = BoundsEndpoint(bounds, maximum: false, "Render world");
        Vec3 maxs = BoundsEndpoint(bounds, maximum: true, "Render world");
        if (world.WorldDraw.ReflectionProbeCount == 0 ||
            world.WorldDraw.ReflectionProbeCount > byte.MaxValue ||
            world.WorldDraw.ReflectionProbeImages.Count !=
                world.WorldDraw.ReflectionProbeCount ||
            world.WorldDraw.ReflectionProbeOrigins.Count !=
                world.WorldDraw.ReflectionProbeCount)
        {
            throw new InvalidDataException(
                "The render reflection-probe tables must contain the default probe and share one byte-sized count.");
        }
        int cellProbeCount = world.WorldDraw.ReflectionProbeCount == 1
            ? 1
            : checked((int)world.WorldDraw.ReflectionProbeCount - 1);
        if (cellProbeCount > 112 - 45)
        {
            throw new NotSupportedException(
                $"The canonical v22 cell can hold at most {112 - 45} reflection-probe indices.");
        }

        var data = new byte[112];
        WriteVec3LittleEndian(data, 0, mins);
        WriteVec3LittleEndian(data, 12, maxs);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(24), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(26), 0);
        data[44] = checked((byte)cellProbeCount);
        if (world.WorldDraw.ReflectionProbeCount == 1)
        {
            data[45] = 0;
        }
        else
        {
            for (int index = 0; index < cellProbeCount; index++)
                data[45 + index] = checked((byte)(index + 1));
        }
        return data;
    }

    public static byte[] EncodeModels(
        GfxWorldAsset world,
        ClipMapAsset clipMap,
        D3dbspTriggerCollisionExport collisionExport)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(clipMap);
        ArgumentNullException.ThrowIfNull(collisionExport);
        ValidateDeclaredCount(world.ModelCount, world.Models.Count, "render models");
        ValidateDeclaredCount(clipMap.NumSubModels, clipMap.CModels.Count, "collision models");
        if (world.Models.Count == 0 || world.Models.Count != clipMap.CModels.Count)
        {
            throw new NotSupportedException(
                "Render and collision model tables must have the same nonzero row count.");
        }

        int modelCount = checked(world.Models.Count + collisionExport.SyntheticModels.Count);
        var data = new byte[checked(modelCount * DiskModelSize)];
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

        for (int syntheticIndex = 0;
             syntheticIndex < collisionExport.SyntheticModels.Count;
             syntheticIndex++)
        {
            D3dbspSyntheticBrushModel model =
                collisionExport.SyntheticModels[syntheticIndex] ??
                throw new InvalidDataException(
                    $"Synthetic brush model row {syntheticIndex} is null.");
            ValidateSlice(
                model.FirstBrush,
                model.BrushCount,
                collisionExport.Brushes.Count,
                $"Synthetic brush model row {syntheticIndex} brush");
            if (model.BrushCount == 0)
            {
                throw new InvalidDataException(
                    $"Synthetic brush model row {syntheticIndex} has no collision brushes.");
            }

            Vec3 mins = BoundsEndpoint(
                model.Bounds,
                maximum: false,
                $"Synthetic brush model row {syntheticIndex} bounds");
            Vec3 maxs = BoundsEndpoint(
                model.Bounds,
                maximum: true,
                $"Synthetic brush model row {syntheticIndex} bounds");
            Span<byte> row = data.AsSpan(
                checked((world.Models.Count + syntheticIndex) * DiskModelSize),
                DiskModelSize);
            WriteVec3LittleEndian(row, 0, mins);
            WriteVec3LittleEndian(row, 12, maxs);
            BinaryPrimitives.WriteInt32LittleEndian(row[32..], 0);
            BinaryPrimitives.WriteInt32LittleEndian(row[36..], 0);
            BinaryPrimitives.WriteInt32LittleEndian(row[40..], model.FirstBrush);
            BinaryPrimitives.WriteInt32LittleEndian(row[44..], model.BrushCount);
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

    private static bool SelectUnlayeredGeometryFamily(D3dbspFile file)
    {
        int unlayeredCount = new[]
        {
            D3dbspLumpType.UnlayeredTriangles,
            D3dbspLumpType.UnlayeredDrawVerts,
            D3dbspLumpType.UnlayeredDrawIndices
        }.Count(file.HasLump);
        int layeredCount = new[]
        {
            D3dbspLumpType.Triangles,
            D3dbspLumpType.DrawVerts,
            D3dbspLumpType.DrawIndices
        }.Count(file.HasLump);
        if (unlayeredCount is 1 or 2 || layeredCount is 1 or 2)
        {
            throw new InvalidDataException(
                "The d3dbsp contains an incomplete layered or unlayered render-geometry lump family.");
        }
        if (unlayeredCount == 0 && layeredCount == 0)
        {
            throw new InvalidDataException(
                "The d3dbsp contains no complete render-geometry lump family.");
        }

        return unlayeredCount == 3;
    }

    private static GfxCell ValidateCanonicalSourceSpatialRows(
        D3dbspFile file,
        Bounds worldBounds,
        int surfaceCount,
        int reflectionProbeCount)
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
        if (cell.Length != 112 ||
            BinaryPrimitives.ReadUInt16LittleEndian(cell[24..]) != 0 ||
            BinaryPrimitives.ReadUInt16LittleEndian(cell[26..]) != 0 ||
            cell.Slice(28, 16).IndexOfAnyExcept((byte)0) >= 0)
        {
            throw new NotSupportedException(
                "Strict fastfile conversion requires one render cell with no portal or auxiliary metadata.");
        }
        int cellProbeCount = cell[44];
        if (cellProbeCount > cell.Length - 45 ||
            cell[(45 + cellProbeCount)..].IndexOfAnyExcept((byte)0) >= 0)
        {
            throw new InvalidDataException(
                "The canonical render cell has an invalid reflection-probe list.");
        }
        // A compiler-produced map with no authored probes leaves the cell list
        // empty. The fastfile graph still receives native probe zero.
        byte[] cellProbes = cellProbeCount == 0
            ? [0]
            : cell.Slice(45, cellProbeCount).ToArray();
        if (cellProbes.Any(index => index >= reflectionProbeCount))
        {
            throw new InvalidDataException(
                "The canonical render cell references a reflection probe outside the world table.");
        }
        Vec3 sourceMins = ReadVec3(cell, 0);
        Vec3 sourceMaxs = ReadVec3(cell, 12);
        ValidateBounds(sourceMins, sourceMaxs, "Render cell");
        Vec3 decodedMins = BoundsEndpoint(worldBounds, maximum: false, "Render world");
        Vec3 decodedMaxs = BoundsEndpoint(worldBounds, maximum: true, "Render world");
        if (sourceMins.X > decodedMins.X ||
            sourceMins.Y > decodedMins.Y ||
            sourceMins.Z > decodedMins.Z ||
            sourceMaxs.X < decodedMaxs.X ||
            sourceMaxs.Y < decodedMaxs.Y ||
            sourceMaxs.Z < decodedMaxs.Z)
        {
            throw new NotSupportedException(
                "The compiled render-cell bounds do not contain the canonical all-surface world bounds.");
        }

        return new GfxCell
        {
            Bounds = new Bounds
            {
                MidPoint = new Vec3
                {
                    X = (float)(((double)sourceMins.X + sourceMaxs.X) * 0.5),
                    Y = (float)(((double)sourceMins.Y + sourceMaxs.Y) * 0.5),
                    Z = (float)(((double)sourceMins.Z + sourceMaxs.Z) * 0.5)
                },
                HalfSize = new Vec3
                {
                    X = (float)(((double)sourceMaxs.X - sourceMins.X) * 0.5),
                    Y = (float)(((double)sourceMaxs.Y - sourceMins.Y) * 0.5),
                    Z = (float)(((double)sourceMaxs.Z - sourceMins.Z) * 0.5)
                }
            },
            ReflectionProbeCount = checked((byte)cellProbes.Length),
            Pad21 = [0, 0, 0],
            ReflectionProbes = Array.AsReadOnly(cellProbes)
        };
    }

    private static Bounds ReadWorldBounds(GfxWorldAsset world)
    {
        Vec3 midpoint = ReadVec3(world.Mins, "Render world midpoint");
        Vec3 halfSize = ReadVec3(world.Maxs, "Render world half-size");
        if (halfSize.X < 0 || halfSize.Y < 0 || halfSize.Z < 0)
            throw new InvalidDataException("The render world has a negative half-size.");
        return new Bounds { MidPoint = midpoint, HalfSize = halfSize };
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

    private static int QuantizeNormal(float value, int scale) => checked((int)System.Math.Round(
        System.Math.Clamp((double)value, -1.0, 1.0) * scale,
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
        int worldPrefixLength = name.StartsWith("wc/", StringComparison.Ordinal)
            ? 3
            : name.StartsWith("w/", StringComparison.Ordinal)
                ? 2
                : 0;
        if (worldPrefixLength != 0)
        {
            exact = FindNamedMaterial(
                materials,
                name[worldPrefixLength..],
                StringComparison.Ordinal);
            if (exact >= 0)
                return exact;
            name = name[worldPrefixLength..];
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
