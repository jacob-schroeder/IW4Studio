using System.Globalization;
using IW4.Assets.Assets;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.ComWorld;
using IW4.Assets.Assets.FxMap;
using IW4.Assets.Assets.GameMap;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.MapEnts;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.Physics;
using IW4.Assets.Assets.StringTable;
using IW4.Assets.Assets.XModel;
using IW4.Assets.D3dbsp;
using IW4.Assets.Math;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.D3dbsp;

public sealed record D3dbspLinkRequest(
    string InputPath,
    string AssetName,
    bool ForceFullbright,
    int FragmentProgramUploadCapacity,
    IReadOnlyList<XModelAsset> AvailableXModels);

public sealed class D3dbspLinkResult
{
    internal D3dbspLinkResult(
        IReadOnlyList<BaseAsset> roots,
        IReadOnlyList<BaseAsset> nestedAssets,
        IReadOnlyList<BaseAsset> dependencyReferences,
        uint checksum,
        int discardedLightByteCount)
    {
        Roots = roots;
        NestedAssets = nestedAssets;
        DependencyReferences = dependencyReferences;
        Checksum = checksum;
        DiscardedLightByteCount = discardedLightByteCount;
    }

    /// <summary>The five top-level ColMapMp, ComMap, GameMapMp, FxMap, and GfxMap definitions.</summary>
    public IReadOnlyList<BaseAsset> Roots { get; }
    /// <summary>MapEnts and any owned lightmap-image providers.</summary>
    public IReadOnlyList<BaseAsset> NestedAssets { get; }
    public IReadOnlyList<BaseAsset> DependencyReferences { get; }
    public uint Checksum { get; }
    public int DiscardedLightByteCount { get; }
}

public static class D3dbspAssetLinker
{
    public static D3dbspLinkResult Link(D3dbspLinkRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string inputPath = request.InputPath;
        string assetName = request.AssetName;
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ValidateAssetName(assetName);
        ArgumentNullException.ThrowIfNull(request.AvailableXModels);
        if (request.FragmentProgramUploadCapacity <= 0 ||
            request.FragmentProgramUploadCapacity > int.MaxValue - 0x1000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.FragmentProgramUploadCapacity),
                request.FragmentProgramUploadCapacity,
                "Fragment-program upload capacity must be positive and leave room for the additional 0x1000-byte arena reservation.");
        }

        string path = Path.GetFullPath(inputPath);
        D3dbspFile file = D3dbspFile.Read(path);
        uint checksum = CalculateChecksum(path);

        ComWorldAsset comWorld = D3dbspPrimaryLightCodec.DecodeComWorld(
            assetName,
            file.GetRequiredData(D3dbspLumpType.PrimaryLights));
        int sunPrimaryLightIndex = D3dbspPrimaryLightCodec.GetLastSunPrimaryLightIndex(
            comWorld.PrimaryLights);

        byte[] sourceEntities = file.GetRequiredData(D3dbspLumpType.Entities).ToArray();
        int discardedLightByteCount = ValidateSourceProfile(
            file,
            request.ForceFullbright);
        IReadOnlyList<GfxLightmapArray> lightmaps = request.ForceFullbright
            ? []
            : D3dbspImageCodec.DecodeLightBytes(
                file.GetOptionalData(D3dbspLumpType.LightBytes));
        (
            IReadOnlyList<GfxImageAsset?> reflectionProbeImages,
            IReadOnlyList<GfxReflectionProbe> reflectionProbeOrigins) =
                D3dbspImageCodec.DecodeReflectionProbes(
                    file.GetOptionalData(D3dbspLumpType.ReflectionProbes));
        IReadOnlyList<Stage> stages = D3dbspMapEntsCodec.DecodeStages(
            sourceEntities,
            sunPrimaryLightIndex);
        IReadOnlyList<D3dbspStaticModelEntity> staticModelEntities =
            D3dbspMapEntsCodec.DecodeStaticModels(
                sourceEntities,
                sunPrimaryLightIndex);
        (
            IReadOnlyList<GfxStaticModelInst> gfxStaticModelInstances,
            IReadOnlyList<GfxStaticModelDrawInst> gfxStaticModelDrawInstances,
            IReadOnlyList<ClipStaticModel> clipStaticModels,
            SModelAabbNode clipStaticModelRoot,
            IReadOnlyList<XModelAsset> xmodelReferences) =
                BuildStaticModels(
                    staticModelEntities,
                    request.AvailableXModels,
                    reflectionProbeOrigins,
                    comWorld.PrimaryLightCount);

        IReadOnlyList<CPlane> planes = D3dbspCollisionCodec.DecodePlanes(
            file.GetRequiredData(D3dbspLumpType.Planes));
        IReadOnlyList<ClipMaterial> clipMaterials = D3dbspCollisionCodec.DecodeMaterials(
            file.GetRequiredData(D3dbspLumpType.Materials));
        MaterialAsset[] renderMaterials = clipMaterials
            .Select((material, index) => CreateMaterialReference(material.Name, index))
            .ToArray();
        IReadOnlyList<byte> brushEdges = D3dbspCollisionCodec.DecodeBrushEdges(
            file.GetOptionalData(D3dbspLumpType.BrushEdges));
        IReadOnlyList<ushort> leafBrushes = D3dbspCollisionCodec.DecodeLeafBrushes(
            file.GetOptionalData(D3dbspLumpType.LeafBrushes));
        IReadOnlyList<uint> leafSurfaces = D3dbspCollisionCodec.DecodeLeafSurfaces(
            file.GetOptionalData(D3dbspLumpType.LeafSurfaces));
        IReadOnlyList<IW4.Assets.Math.Vec3> collisionVerts =
            D3dbspCollisionCodec.DecodeCollisionVerts(
                file.GetOptionalData(D3dbspLumpType.CollisionVerts));
        IReadOnlyList<ushort> collisionTriIndices =
            D3dbspCollisionCodec.DecodeCollisionTris(
                file.GetOptionalData(D3dbspLumpType.CollisionTris));
        IReadOnlyList<byte> triEdgeIsWalkable =
            D3dbspCollisionCodec.DecodeCollisionEdgeWalkable(
                file.GetOptionalData(D3dbspLumpType.CollisionEdgeWalkable));
        IReadOnlyList<CollisionBorder> borders =
            D3dbspCollisionCodec.DecodeCollisionBorders(
                file.GetOptionalData(D3dbspLumpType.CollisionBorders));
        IReadOnlyList<CollisionAabbTree> collisionAabbs =
            D3dbspCollisionCodec.DecodeCollisionAabbs(
                file.GetOptionalData(D3dbspLumpType.CollisionAabbs));
        IReadOnlyList<CollisionPartition> partitions =
            D3dbspCollisionCodec.DecodeCollisionPartitions(
                file.GetOptionalData(D3dbspLumpType.CollisionPartitions),
                borders,
                collisionTriIndices.Count / 3);

        ReadOnlySpan<byte> diskBrushes = file.GetRequiredData(D3dbspLumpType.Brushes);
        int brushCount = GetElementCount(diskBrushes, 4, "collision brush");
        if (brushCount > ushort.MaxValue)
            throw new InvalidDataException("The collision brush count exceeds the IW4 ushort range.");
        var brushGraph = D3dbspCollisionCodec.DecodeBrushes(
            diskBrushes,
            file.GetRequiredData(D3dbspLumpType.BrushSides),
            file.GetRequiredData(D3dbspLumpType.BrushSideEdgeCounts),
            brushEdges,
            planes,
            clipMaterials,
            new ushort[brushCount]);
        var leafGraph = D3dbspCollisionCodec.DecodeLeafGraph(
            file.GetRequiredData(D3dbspLumpType.Leafs),
            file.GetRequiredData(D3dbspLumpType.Models),
            leafBrushes,
            brushGraph.BrushBounds,
            brushGraph.BrushContents,
            collisionAabbs,
            clipMaterials);

        (byte[] entityString, MapTriggers mapTriggers) =
            D3dbspMapEntsCodec.DecodeMapTriggers(
                sourceEntities,
                leafGraph.CModels,
                leafGraph.LeafBrushNodes,
                brushGraph.Brushes,
                brushGraph.BrushBounds,
                brushGraph.BrushContents);
        var mapEnts = new MapEntsAsset
        {
            Name = assetName,
            EntityStringBytes = entityString,
            NumEntityChars = entityString.Length,
            Trigger = mapTriggers,
            Stages = stages,
            StageCount = checked((byte)stages.Count),
            Pad29To2B = [0, 0, 0]
        };

        var clipMap = new ClipMapAsset
        {
            SerializedType = XAssetType.ColMapMp,
            Name = assetName,
            IsInUse = 0,
            SerializedIsInUse = 0,
            PlaneCount = planes.Count,
            Planes = planes,
            NumStaticModels = clipStaticModels.Count,
            StaticModelList = clipStaticModels,
            NumMaterials = clipMaterials.Count,
            Materials = clipMaterials,
            NumBrushSides = brushGraph.BrushSides.Count,
            BrushSides = brushGraph.BrushSides,
            NumBrushEdges = brushEdges.Count,
            BrushEdges = brushEdges,
            NumNodes = GetElementCount(
                file.GetRequiredData(D3dbspLumpType.Nodes),
                36,
                "collision node"),
            Nodes = D3dbspCollisionCodec.DecodeNodes(
                file.GetRequiredData(D3dbspLumpType.Nodes),
                planes),
            NumLeafs = leafGraph.Leafs.Count,
            Leafs = leafGraph.Leafs,
            LeafBrushNodesCount = leafGraph.LeafBrushNodes.Count,
            LeafBrushNodes = leafGraph.LeafBrushNodes,
            NumLeafBrushes = leafBrushes.Count,
            LeafBrushes = leafBrushes,
            NumLeafSurfaces = leafSurfaces.Count,
            LeafSurfaces = leafSurfaces,
            VertCount = collisionVerts.Count,
            Verts = collisionVerts,
            TriCount = collisionTriIndices.Count / 3,
            TriIndices = collisionTriIndices,
            TriEdgeIsWalkable = triEdgeIsWalkable,
            BorderCount = borders.Count,
            Borders = borders,
            PartitionCount = partitions.Count,
            Partitions = partitions,
            AabbTreeCount = collisionAabbs.Count,
            AabbTrees = collisionAabbs,
            NumSubModels = leafGraph.CModels.Count,
            CModels = leafGraph.CModels,
            NumBrushes = checked((ushort)brushCount),
            Brushes = brushGraph.Brushes,
            BrushBounds = brushGraph.BrushBounds,
            BrushContents = brushGraph.BrushContents,
            MapEnts = mapEnts,
            // IW4's point-trace path always starts at static-model tree node zero,
            // even when the map has no static models.
            SModelNodeCount = 1,
            SModelNodes = [clipStaticModelRoot],
            DynEntCount = [0, 0],
            DynEntDefListPointers = new XPointer<DynEntityDef[]>[2],
            DynEntDefList = EmptyDynamicLists<DynEntityDef>(),
            DynEntPoseListPointers = new XPointer<DynEntityPose[]>[2],
            DynEntPoseList = EmptyDynamicLists<DynEntityPose>(),
            DynEntClientListPointers = new XPointer<DynEntityClient[]>[2],
            DynEntClientList = EmptyDynamicLists<DynEntityClient>(),
            DynEntCollListPointers = new XPointer<DynEntityColl[]>[2],
            DynEntCollList = EmptyDynamicLists<DynEntityColl>(),
            Checksum = checksum,
            PadD0ToFF = new byte[0x30]
        };

        bool hasLightRegions = file.HasLump(D3dbspLumpType.LightRegions);
        GfxLightGrid lightGrid = D3dbspLightingCodec.DecodeLightGrid(
            file.GetRequiredData(D3dbspLumpType.LightGridHeader),
            file.GetOptionalData(D3dbspLumpType.LightGridRows),
            file.GetOptionalData(D3dbspLumpType.LightGridEntries),
            file.GetOptionalData(D3dbspLumpType.LightGridColors),
            checked((uint)sunPrimaryLightIndex),
            hasLightRegions);
        IReadOnlyList<GfxLightRegion> lightRegions =
            D3dbspLightingCodec.DecodeLightRegions(
                file.GetOptionalData(D3dbspLumpType.LightRegions),
                file.GetOptionalData(D3dbspLumpType.LightRegionHulls),
                file.GetOptionalData(D3dbspLumpType.LightRegionAxes),
                comWorld.PrimaryLightCount,
                hasLightRegions);
        GfxWorldAsset gfxWorld = D3dbspGfxCodec.DecodeWorld(
            assetName,
            file,
            renderMaterials,
            comWorld.PrimaryLightCount,
            sunPrimaryLightIndex,
            request.FragmentProgramUploadCapacity,
            checksum,
            lightGrid,
            lightRegions,
            lightmaps,
            reflectionProbeImages,
            reflectionProbeOrigins,
            gfxStaticModelInstances,
            gfxStaticModelDrawInstances);

        var fxWorld = new FxWorldAsset
        {
            Name = assetName,
            GlassSystem = new FxGlassSystem()
        };
        var gameWorld = new GameWorldMpAsset
        {
            Name = assetName,
            GlassData = new GGlassData()
        };

        BaseAsset[] roots =
        [
            clipMap,
            comWorld,
            gameWorld,
            fxWorld,
            gfxWorld
        ];
        BaseAsset[] ownedMapImages = gfxWorld.WorldDraw.Lightmaps
            .SelectMany(lightmap => new[] { lightmap.Primary, lightmap.Secondary })
            .OfType<GfxImageAsset>()
            .Where(image => image.Name is { Length: > 0 } name && name[0] != ',')
            .Cast<BaseAsset>()
            .Concat(gfxWorld.WorldDraw.ReflectionProbeImages
                .OfType<GfxImageAsset>()
                .Where(image => image.Name is { Length: > 0 } name && name[0] != ','))
            .DistinctBy(asset => (asset.SerializedAssetType, asset.SerializedAssetName))
            .ToArray();
        BaseAsset[] dependencies = renderMaterials
            .Cast<BaseAsset>()
            .Concat(gfxWorld.WorldDraw.ReflectionProbeImages
                .OfType<GfxImageAsset>()
                .Where(image => image.Name is { Length: > 0 } name && name[0] == ','))
            .Concat(gfxWorld.WorldDraw.Lightmaps.SelectMany(lightmap =>
                new[] { lightmap.Primary, lightmap.Secondary }
                    .OfType<GfxImageAsset>()
                    .Where(image => image.Name is { Length: > 0 } name && name[0] == ',')))
            .Concat(xmodelReferences)
            .DistinctBy(asset => (asset.SerializedAssetType, asset.SerializedAssetName))
            .ToArray();
        return new D3dbspLinkResult(
            Array.AsReadOnly(roots),
            Array.AsReadOnly<BaseAsset>([mapEnts, .. ownedMapImages]),
            Array.AsReadOnly(dependencies),
            checksum,
            discardedLightByteCount);
    }

    /// <summary>
    /// Creates the sparse PS3 deathmatch configstring baseline that can be
    /// proven from a compiled map alone. Runtime gamestate initialization fills
    /// the remaining configstrings.
    /// </summary>
    public static StringTableAsset CreatePs3DmConfigStringBaseline(
        string assetName,
        uint checksum)
    {
        ValidateAssetName(assetName);
        string mapName = Path.GetFileNameWithoutExtension(
            assetName.Replace('\\', '/'));
        if (mapName.Length == 0)
        {
            throw new InvalidDataException(
                $"Map asset name '{assetName}' has no basename for its PS3 configstring table.");
        }

        string signedChecksum = unchecked((int)checksum).ToString(
            CultureInfo.InvariantCulture);
        return new StringTableAsset
        {
            Name = $"mp/configstrings/configstrings_ps3_{mapName}_dm.csv",
            ColumnCount = 2,
            RowCount = 2,
            Cells =
            [
                CreateStringTableCell("111"),
                CreateStringTableCell("mapcrc"),
                CreateStringTableCell("311"),
                CreateStringTableCell(signedChecksum)
            ]
        };
    }

    /// <summary>
    /// Refreshes only the two map-derived rows in an existing PS3 deathmatch
    /// configstring baseline. All other rows and their stored hashes are kept.
    /// </summary>
    public static StringTableAsset RefreshPs3DmConfigStringBaseline(
        StringTableAsset source,
        string assetName,
        uint checksum)
    {
        ArgumentNullException.ThrowIfNull(source);
        StringTableAsset generated = CreatePs3DmConfigStringBaseline(
            assetName,
            checksum);
        if (AssetKey.FromDefinition(source) != AssetKey.FromDefinition(generated))
        {
            throw new InvalidDataException(
                $"StringTable '{source.Name}' is not the PS3 deathmatch configstring baseline for '{assetName}'.");
        }
        if (source.ColumnCount != 2 || source.RowCount < 0)
        {
            throw new InvalidDataException(
                $"Configstring StringTable '{source.Name}' must have two columns and a non-negative row count.");
        }

        long expectedCellCount = (long)source.RowCount * source.ColumnCount;
        if (expectedCellCount != source.Cells.Count)
        {
            throw new InvalidDataException(
                $"Configstring StringTable '{source.Name}' declares {source.RowCount} rows but contains {source.Cells.Count} cells.");
        }

        var rows = new SortedDictionary<int, (StringTableCell Index, StringTableCell Value)>();
        int previousIndex = -1;
        for (int row = 0; row < source.RowCount; row++)
        {
            int offset = checked(row * source.ColumnCount);
            StringTableCell indexCell = source.Cells[offset]
                ?? throw new InvalidDataException(
                    $"Configstring StringTable '{source.Name}' row {row} has no index cell.");
            StringTableCell valueCell = source.Cells[offset + 1]
                ?? throw new InvalidDataException(
                    $"Configstring StringTable '{source.Name}' row {row} has no value cell.");
            if (!int.TryParse(
                    indexCell.String,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int configStringIndex) ||
                configStringIndex < 0 ||
                configStringIndex <= previousIndex)
            {
                throw new InvalidDataException(
                    $"Configstring StringTable '{source.Name}' row {row} does not contain a strictly increasing non-negative index.");
            }

            rows.Add(configStringIndex, (indexCell, valueCell));
            previousIndex = configStringIndex;
        }

        if (rows.TryGetValue(111, out var mapCrcName) &&
            !string.Equals(
                mapCrcName.Value.String,
                "mapcrc",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Configstring StringTable '{source.Name}' assigns index 111 to '{mapCrcName.Value.String}', not mapcrc.");
        }

        rows[111] = (generated.Cells[0], generated.Cells[1]);
        rows[311] = (generated.Cells[2], generated.Cells[3]);
        StringTableCell[] cells = rows.Values
            .SelectMany(row => new[]
            {
                CloneStringTableCell(row.Index),
                CloneStringTableCell(row.Value)
            })
            .ToArray();
        return new StringTableAsset
        {
            Name = source.Name,
            ColumnCount = 2,
            RowCount = rows.Count,
            Cells = Array.AsReadOnly(cells)
        };
    }

    private static StringTableCell CreateStringTableCell(string value) =>
        new()
        {
            String = value,
            Hash = CalculateStringTableHash(value)
        };

    private static StringTableCell CloneStringTableCell(StringTableCell source) =>
        new()
        {
            String = source.String,
            Hash = source.Hash
        };

    private static int CalculateStringTableHash(string value)
    {
        uint hash = 0;
        foreach (char character in value)
        {
            if (character > 0x7f)
            {
                throw new InvalidDataException(
                    $"PS3 configstring value '{value}' contains a non-ASCII character.");
            }

            byte current = (byte)character;
            if (current is >= (byte)'A' and <= (byte)'Z')
                current += (byte)('a' - 'A');
            hash = unchecked(hash * 31 + current);
        }

        return unchecked((int)hash);
    }

    private static IReadOnlyList<IReadOnlyList<T>> EmptyDynamicLists<T>() =>
        Array.AsReadOnly<IReadOnlyList<T>>(
            [Array.Empty<T>(), Array.Empty<T>()]);

    private static (
        IReadOnlyList<GfxStaticModelInst> GfxInstances,
        IReadOnlyList<GfxStaticModelDrawInst> GfxDrawInstances,
        IReadOnlyList<ClipStaticModel> ClipModels,
        SModelAabbNode ClipRoot,
        IReadOnlyList<XModelAsset> XModelReferences) BuildStaticModels(
            IReadOnlyList<D3dbspStaticModelEntity> sourceModels,
            IReadOnlyList<XModelAsset> availableXModels,
            IReadOnlyList<GfxReflectionProbe> reflectionProbes,
            int primaryLightCount)
    {
        ArgumentNullException.ThrowIfNull(sourceModels);
        ArgumentNullException.ThrowIfNull(availableXModels);
        ArgumentNullException.ThrowIfNull(reflectionProbes);
        if (sourceModels.Count > ushort.MaxValue)
        {
            throw new InvalidDataException(
                $"The d3dbsp contains {sourceModels.Count} render static models; IW4's canonical " +
                $"one-node tree supports at most {ushort.MaxValue}.");
        }
        if (reflectionProbes.Count == 0 || reflectionProbes.Count > byte.MaxValue)
        {
            throw new InvalidDataException(
                "The reflection-probe origin table must contain the default probe and fit in one byte.");
        }

        Dictionary<AssetKey, XModelAsset> xmodelsByKey =
            IndexAvailableXModels(availableXModels);
        var sourceKeys = new AssetKey[sourceModels.Count];
        var missingNames = new Dictionary<AssetKey, string>();
        for (int index = 0; index < sourceModels.Count; index++)
        {
            D3dbspStaticModelEntity source = sourceModels[index] ??
                throw new InvalidDataException($"The d3dbsp static-model entity {index} is null.");
            AssetKey key = GetXModelKey(source.ModelName, $"static-model entity {index}");
            sourceKeys[index] = key;
            if (!xmodelsByKey.ContainsKey(key))
                missingNames.TryAdd(key, source.ModelName);
        }
        if (missingNames.Count != 0)
        {
            string names = string.Join(
                ", ",
                missingNames.Values
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .Select(name => $"'{name}'"));
            throw new InvalidDataException(
                "The d3dbsp references XModels that are not available as full definitions: " +
                names + ".");
        }

        var gfxInstances = new GfxStaticModelInst[sourceModels.Count];
        var gfxDrawInstances = new GfxStaticModelDrawInst[sourceModels.Count];
        var clipModels = new List<ClipStaticModel>(sourceModels.Count);
        var usedXModelKeys = new HashSet<AssetKey>();
        var xmodelReferences = new List<XModelAsset>();
        for (int index = 0; index < sourceModels.Count; index++)
        {
            D3dbspStaticModelEntity source = sourceModels[index];
            AssetKey key = sourceKeys[index];
            XModelAsset xmodel = xmodelsByKey[key];
            ValidateStaticModel(source, xmodel, index, primaryLightCount);

            Bounds renderBounds = TransformBounds(
                xmodel.Bounds,
                source,
                $"Static model {index} render bounds");
            gfxInstances[index] = new GfxStaticModelInst
            {
                Bounds = renderBounds,
                LightingOrigin = renderBounds.MidPoint
            };
            gfxDrawInstances[index] = new GfxStaticModelDrawInst
            {
                Placement = new GfxPackedPlacement
                {
                    Origin = [source.Origin.X, source.Origin.Y, source.Origin.Z],
                    PackedAxis = source.PackedAxis,
                    Scale = source.Scale
                },
                Model = xmodel,
                CullDist = CalculateCullDistance(xmodel, source.Scale, index),
                LightingHandle = 0,
                ReflectionProbeIndex = SelectReflectionProbe(
                    reflectionProbes,
                    renderBounds.MidPoint,
                    index),
                PrimaryLightIndex = source.PrimaryLightIndex,
                Flags = source.Flags,
                FirstMaterialSkinIndex = 0,
                GroundLighting = source.GroundLighting
            };

            if (xmodel.CollLod != byte.MaxValue && xmodel.NumCollSurfs > 0)
                clipModels.Add(CreateClipStaticModel(source, xmodel, index));

            if (usedXModelKeys.Add(key))
            {
                xmodelReferences.Add(new XModelAsset
                {
                    Name = "," + key.NormalizedName
                });
            }
        }

        if (clipModels.Count > ushort.MaxValue)
        {
            throw new InvalidDataException(
                $"The d3dbsp contains {clipModels.Count} collision static models; IW4's canonical " +
                $"one-node tree supports at most {ushort.MaxValue}.");
        }

        SModelAabbNode clipRoot = clipModels.Count == 0
            ? new SModelAabbNode()
            : new SModelAabbNode
            {
                Bounds = UnionClipBounds(clipModels),
                FirstChild = 0,
                ChildCount = checked((ushort)clipModels.Count)
            };
        return (
            Array.AsReadOnly(gfxInstances),
            Array.AsReadOnly(gfxDrawInstances),
            clipModels.AsReadOnly(),
            clipRoot,
            xmodelReferences.AsReadOnly());
    }

    private static Dictionary<AssetKey, XModelAsset> IndexAvailableXModels(
        IReadOnlyList<XModelAsset> availableXModels)
    {
        var result = new Dictionary<AssetKey, XModelAsset>();
        for (int index = 0; index < availableXModels.Count; index++)
        {
            XModelAsset xmodel = availableXModels[index] ??
                throw new InvalidDataException($"Available XModel row {index} is null.");
            string name = xmodel.Name ??
                throw new InvalidDataException($"Available XModel row {index} has no name.");
            if (name.Length == 0 || name[0] == ',')
            {
                throw new InvalidDataException(
                    $"Available XModel row {index} '{name}' is a reference, not a full definition.");
            }

            AssetKey key = GetXModelKey(name, $"available XModel row {index}");
            if (!result.TryAdd(key, xmodel))
            {
                throw new InvalidDataException(
                    $"More than one full XModel definition is available for '{key.NormalizedName}'.");
            }
        }
        return result;
    }

    private static AssetKey GetXModelKey(string name, string description)
    {
        try
        {
            return AssetKey.FromWireName(
                CanonicalAssetFamily.FromSerializedType(XAssetType.XModel),
                name);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                $"The {description} has invalid XModel name '{name}'.",
                exception);
        }
    }

    private static void ValidateStaticModel(
        D3dbspStaticModelEntity source,
        XModelAsset xmodel,
        int index,
        int primaryLightCount)
    {
        if (source.Axis.Count != 3 || source.PackedAxis.Count != 3)
        {
            throw new InvalidDataException(
                $"Static model {index} must retain three axis and three packed-axis rows.");
        }
        RequireFinite(source.Origin, $"Static model {index} origin");
        foreach (Vec3 axis in source.Axis)
            RequireFinite(axis, $"Static model {index} axis");
        if (!float.IsFinite(source.Scale) || source.Scale <= 0.0f)
        {
            throw new InvalidDataException(
                $"Static model {index} has invalid scale {source.Scale}.");
        }
        if (primaryLightCount <= 0
            ? source.PrimaryLightIndex != 0
            : source.PrimaryLightIndex >= primaryLightCount)
        {
            throw new InvalidDataException(
                $"Static model {index} references primary light {source.PrimaryLightIndex}; " +
                $"the table has {primaryLightCount} rows.");
        }

        bool usesGroundLighting =
            (source.Flags & GfxStaticModelDrawInstFlags.GroundLighting) != 0;
        if (usesGroundLighting != (source.GroundLighting.Packed != 0))
        {
            throw new InvalidDataException(
                $"Static model {index} has inconsistent ground-lighting flags and color.");
        }
        if (usesGroundLighting && (xmodel.Flags & XModelFlags.GroundLighting) == 0)
        {
            throw new InvalidDataException(
                $"Static model {index} uses ground lighting, but XModel '{xmodel.Name}' is not ground-lit.");
        }

        Bounds modelBounds = xmodel.Bounds ??
            throw new InvalidDataException($"XModel '{xmodel.Name}' has no render bounds.");
        ValidateBounds(modelBounds, $"XModel '{xmodel.Name}' render bounds");
        if (xmodel.NumLods == 0 || xmodel.NumLods > xmodel.Lods.Count)
        {
            throw new InvalidDataException(
                $"XModel '{xmodel.Name}' declares {xmodel.NumLods} active LODs but retains " +
                $"{xmodel.Lods.Count} rows.");
        }
        XModelLodInfo terminalLod = xmodel.Lods[xmodel.NumLods - 1] ??
            throw new InvalidDataException(
                $"XModel '{xmodel.Name}' terminal active LOD is null.");
        if (!float.IsFinite(terminalLod.Dist) || terminalLod.Dist < 0.0f)
        {
            throw new InvalidDataException(
                $"XModel '{xmodel.Name}' has invalid terminal LOD distance {terminalLod.Dist}.");
        }
        if (xmodel.NumCollSurfs < 0 || xmodel.NumCollSurfs != xmodel.CollSurfs.Count)
        {
            throw new InvalidDataException(
                $"XModel '{xmodel.Name}' declares {xmodel.NumCollSurfs} collision surfaces but " +
                $"retains {xmodel.CollSurfs.Count} rows.");
        }
    }

    private static ushort CalculateCullDistance(
        XModelAsset xmodel,
        float scale,
        int staticModelIndex)
    {
        double distance = (double)xmodel.Lods[xmodel.NumLods - 1].Dist * scale;
        if (!double.IsFinite(distance) || distance < 0.0)
        {
            throw new InvalidDataException(
                $"Static model {staticModelIndex} has invalid scaled terminal LOD distance {distance}.");
        }
        return distance >= ushort.MaxValue
            ? ushort.MaxValue
            : (ushort)distance;
    }

    private static byte SelectReflectionProbe(
        IReadOnlyList<GfxReflectionProbe> probes,
        Vec3 lightingOrigin,
        int staticModelIndex)
    {
        if (probes.Count == 1)
            return 0;

        int nearestIndex = 1;
        double nearestDistance = double.PositiveInfinity;
        for (int index = 1; index < probes.Count; index++)
        {
            GfxReflectionProbe probe = probes[index] ??
                throw new InvalidDataException($"Reflection probe {index} is null.");
            if (!float.IsFinite(probe.OffsetX) ||
                !float.IsFinite(probe.OffsetY) ||
                !float.IsFinite(probe.OffsetZ))
            {
                throw new InvalidDataException(
                    $"Reflection probe {index} has a non-finite origin.");
            }
            double deltaX = (double)lightingOrigin.X - probe.OffsetX;
            double deltaY = (double)lightingOrigin.Y - probe.OffsetY;
            double deltaZ = (double)lightingOrigin.Z - probe.OffsetZ;
            double distance = deltaX * deltaX + deltaY * deltaY + deltaZ * deltaZ;
            if (distance < nearestDistance)
            {
                nearestIndex = index;
                nearestDistance = distance;
            }
        }
        if (!double.IsFinite(nearestDistance))
        {
            throw new InvalidDataException(
                $"Static model {staticModelIndex} has no usable reflection probe.");
        }
        return checked((byte)nearestIndex);
    }

    private static ClipStaticModel CreateClipStaticModel(
        D3dbspStaticModelEntity source,
        XModelAsset xmodel,
        int staticModelIndex)
    {
        XModelCollSurf firstCollisionSurface = xmodel.CollSurfs[0] ??
            throw new InvalidDataException(
                $"XModel '{xmodel.Name}' collision surface 0 is null.");
        Bounds collisionBounds = TransformBounds(
            firstCollisionSurface.Bounds,
            source,
            $"Static model {staticModelIndex} collision surface 0");
        for (int index = 1; index < xmodel.CollSurfs.Count; index++)
        {
            XModelCollSurf collisionSurface = xmodel.CollSurfs[index] ??
                throw new InvalidDataException(
                    $"XModel '{xmodel.Name}' collision surface {index} is null.");
            collisionBounds = UnionBounds(
                collisionBounds,
                TransformBounds(
                    collisionSurface.Bounds,
                    source,
                    $"Static model {staticModelIndex} collision surface {index}"));
        }

        float inverseScale = 1.0f / source.Scale;
        if (!float.IsFinite(inverseScale))
        {
            throw new InvalidDataException(
                $"Static model {staticModelIndex} scale {source.Scale} has no finite inverse.");
        }
        IReadOnlyList<Vec3> axis = source.Axis;
        return new ClipStaticModel
        {
            XModel = xmodel,
            Origin = source.Origin,
            InvScaledAxis =
            [
                new Vec3
                {
                    X = axis[0].X * inverseScale,
                    Y = axis[1].X * inverseScale,
                    Z = axis[2].X * inverseScale
                },
                new Vec3
                {
                    X = axis[0].Y * inverseScale,
                    Y = axis[1].Y * inverseScale,
                    Z = axis[2].Y * inverseScale
                },
                new Vec3
                {
                    X = axis[0].Z * inverseScale,
                    Y = axis[1].Z * inverseScale,
                    Z = axis[2].Z * inverseScale
                }
            ],
            AbsMin = BoundsEndpoint(collisionBounds, maximum: false),
            AbsMax = BoundsEndpoint(collisionBounds, maximum: true)
        };
    }

    private static Bounds TransformBounds(
        Bounds sourceBounds,
        D3dbspStaticModelEntity placement,
        string description)
    {
        ValidateBounds(sourceBounds, description);
        Vec3 midpoint = sourceBounds.MidPoint;
        Vec3 halfSize = sourceBounds.HalfSize;
        Vec3 minimum = new()
        {
            X = float.PositiveInfinity,
            Y = float.PositiveInfinity,
            Z = float.PositiveInfinity
        };
        Vec3 maximum = new()
        {
            X = float.NegativeInfinity,
            Y = float.NegativeInfinity,
            Z = float.NegativeInfinity
        };
        for (int xSign = -1; xSign <= 1; xSign += 2)
        {
            for (int ySign = -1; ySign <= 1; ySign += 2)
            {
                for (int zSign = -1; zSign <= 1; zSign += 2)
                {
                    Vec3 local = new()
                    {
                        X = midpoint.X + xSign * halfSize.X,
                        Y = midpoint.Y + ySign * halfSize.Y,
                        Z = midpoint.Z + zSign * halfSize.Z
                    };
                    Vec3 transformed = TransformPoint(local, placement);
                    minimum.X = MathF.Min(minimum.X, transformed.X);
                    minimum.Y = MathF.Min(minimum.Y, transformed.Y);
                    minimum.Z = MathF.Min(minimum.Z, transformed.Z);
                    maximum.X = MathF.Max(maximum.X, transformed.X);
                    maximum.Y = MathF.Max(maximum.Y, transformed.Y);
                    maximum.Z = MathF.Max(maximum.Z, transformed.Z);
                }
            }
        }
        return BoundsFromEndpoints(minimum, maximum, description);
    }

    private static Vec3 TransformPoint(
        Vec3 local,
        D3dbspStaticModelEntity placement)
    {
        IReadOnlyList<Vec3> axis = placement.Axis;
        return new Vec3
        {
            X = RequireFinite(
                (float)(placement.Origin.X + (double)placement.Scale *
                    (local.X * axis[0].X + local.Y * axis[1].X + local.Z * axis[2].X)),
                "Static-model transformed X"),
            Y = RequireFinite(
                (float)(placement.Origin.Y + (double)placement.Scale *
                    (local.X * axis[0].Y + local.Y * axis[1].Y + local.Z * axis[2].Y)),
                "Static-model transformed Y"),
            Z = RequireFinite(
                (float)(placement.Origin.Z + (double)placement.Scale *
                    (local.X * axis[0].Z + local.Y * axis[1].Z + local.Z * axis[2].Z)),
                "Static-model transformed Z")
        };
    }

    private static Bounds UnionClipBounds(IReadOnlyList<ClipStaticModel> clipModels)
    {
        Bounds result = BoundsFromEndpoints(
            clipModels[0].AbsMin,
            clipModels[0].AbsMax,
            "Collision static-model tree");
        for (int index = 1; index < clipModels.Count; index++)
        {
            result = UnionBounds(
                result,
                BoundsFromEndpoints(
                    clipModels[index].AbsMin,
                    clipModels[index].AbsMax,
                    $"Collision static model {index}"));
        }
        return result;
    }

    private static Bounds UnionBounds(Bounds left, Bounds right)
    {
        Vec3 leftMin = BoundsEndpoint(left, maximum: false);
        Vec3 leftMax = BoundsEndpoint(left, maximum: true);
        Vec3 rightMin = BoundsEndpoint(right, maximum: false);
        Vec3 rightMax = BoundsEndpoint(right, maximum: true);
        return BoundsFromEndpoints(
            new Vec3
            {
                X = MathF.Min(leftMin.X, rightMin.X),
                Y = MathF.Min(leftMin.Y, rightMin.Y),
                Z = MathF.Min(leftMin.Z, rightMin.Z)
            },
            new Vec3
            {
                X = MathF.Max(leftMax.X, rightMax.X),
                Y = MathF.Max(leftMax.Y, rightMax.Y),
                Z = MathF.Max(leftMax.Z, rightMax.Z)
            },
            "Static-model bounds union");
    }

    private static Bounds BoundsFromEndpoints(
        Vec3 minimum,
        Vec3 maximum,
        string description)
    {
        RequireFinite(minimum, description + " minimum");
        RequireFinite(maximum, description + " maximum");
        if (minimum.X > maximum.X || minimum.Y > maximum.Y || minimum.Z > maximum.Z)
            throw new InvalidDataException($"The {description} endpoints are inverted.");
        return new Bounds
        {
            MidPoint = new Vec3
            {
                X = RequireFinite((float)(((double)minimum.X + maximum.X) * 0.5), description),
                Y = RequireFinite((float)(((double)minimum.Y + maximum.Y) * 0.5), description),
                Z = RequireFinite((float)(((double)minimum.Z + maximum.Z) * 0.5), description)
            },
            HalfSize = new Vec3
            {
                X = RequireFinite((float)(((double)maximum.X - minimum.X) * 0.5), description),
                Y = RequireFinite((float)(((double)maximum.Y - minimum.Y) * 0.5), description),
                Z = RequireFinite((float)(((double)maximum.Z - minimum.Z) * 0.5), description)
            }
        };
    }

    private static Vec3 BoundsEndpoint(Bounds bounds, bool maximum)
    {
        ValidateBounds(bounds, "Static-model bounds");
        float direction = maximum ? 1.0f : -1.0f;
        return new Vec3
        {
            X = RequireFinite(bounds.MidPoint.X + direction * bounds.HalfSize.X, "Bounds X"),
            Y = RequireFinite(bounds.MidPoint.Y + direction * bounds.HalfSize.Y, "Bounds Y"),
            Z = RequireFinite(bounds.MidPoint.Z + direction * bounds.HalfSize.Z, "Bounds Z")
        };
    }

    private static void ValidateBounds(Bounds bounds, string description)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        RequireFinite(bounds.MidPoint, description + " midpoint");
        RequireFinite(bounds.HalfSize, description + " half-size");
        if (bounds.HalfSize.X < 0.0f ||
            bounds.HalfSize.Y < 0.0f ||
            bounds.HalfSize.Z < 0.0f)
        {
            throw new InvalidDataException($"The {description} has a negative half-size.");
        }
    }

    private static void RequireFinite(Vec3 value, string description)
    {
        RequireFinite(value.X, description + " X");
        RequireFinite(value.Y, description + " Y");
        RequireFinite(value.Z, description + " Z");
    }

    private static float RequireFinite(float value, string description)
    {
        if (!float.IsFinite(value))
            throw new InvalidDataException($"The {description} is non-finite.");
        return value;
    }

    private static MaterialAsset CreateMaterialReference(string? name, int index)
    {
        if (string.IsNullOrEmpty(name) || name[0] == ',' || name.Contains('\0'))
        {
            throw new InvalidDataException(
                $"Collision material row {index} has invalid asset name '{name}'.");
        }
        string renderName = name switch
        {
            // IW4 world geometry uses the 3D default material; the bare
            // $default asset is the 2D engine/UI fallback.
            "$default" => "w/$default3d",
            _ when name[0] == '$' || name.StartsWith("w/", StringComparison.Ordinal) => name,
            _ => "w/" + name
        };
        return new MaterialAsset
        {
            Info = new MaterialInfo { Name = "," + renderName }
        };
    }

    private static void ValidateAssetName(string assetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetName);
        if (!D3dbspAssetTypeFacts.IsOwnedD3dbspName(assetName))
        {
            throw new ArgumentException(
                "The map asset name must be an owned .d3dbsp wire name without a comma prefix.",
                nameof(assetName));
        }
    }

    private static int ValidateSourceProfile(
        D3dbspFile file,
        bool forceFullbright)
    {
        if (file.GetRequiredData(D3dbspLumpType.Cells).Length != 112)
        {
            throw new NotSupportedException(
                "Strict fastfile conversion currently requires exactly one compiled render cell.");
        }
        D3dbspLumpType selectedAabbType = file.HasLump(D3dbspLumpType.UnlayeredAabbTrees)
            ? D3dbspLumpType.UnlayeredAabbTrees
            : D3dbspLumpType.AabbTrees;
        if (file.GetRequiredData(selectedAabbType).Length != 12)
        {
            throw new NotSupportedException(
                "Strict fastfile conversion requires one terminal render AABB row.");
        }

        foreach (D3dbspLumpType type in new[]
                 {
                     D3dbspLumpType.CullGroups,
                     D3dbspLumpType.CullGroupIndices,
                     D3dbspLumpType.PortalVerts,
                     D3dbspLumpType.Portals,
                     D3dbspLumpType.PathConnections,
                     D3dbspLumpType.UnlayeredCullGroups
                 })
        {
            if (!file.GetOptionalData(type).IsEmpty)
            {
                throw new NotSupportedException(
                    $"Strict fastfile conversion does not yet support nonempty {type} data.");
            }
        }

        int lightByteCount = file.GetOptionalData(D3dbspLumpType.LightBytes).Length;
        if (lightByteCount != 0 && lightByteCount % (3 * 1024 * 1024) != 0)
        {
            throw new InvalidDataException(
                $"The LightBytes lump has noncanonical length {lightByteCount}.");
        }

        return forceFullbright ? lightByteCount : 0;
    }

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

    private static uint CalculateChecksum(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            FileOptions.SequentialScan);
        var buffer = new byte[1024 * 1024];
        uint crc = uint.MaxValue;
        int count;
        while ((count = stream.Read(buffer, 0, buffer.Length)) != 0)
        {
            for (int index = 0; index < count; index++)
            {
                crc ^= buffer[index];
                for (int bit = 0; bit < 8; bit++)
                    crc = (crc >> 1) ^ (0xedb88320u & (uint)-(int)(crc & 1));
            }
        }
        return ~crc;
    }
}
