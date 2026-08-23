using System.Text;
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
using IW4.Assets.Assets.RawFile;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4Map;

internal sealed record D3dbspAssetGraph(
    IReadOnlyList<BaseAsset> Roots,
    IReadOnlyList<BaseAsset> NestedAssets,
    IReadOnlyList<BaseAsset> DependencyReferences,
    uint Checksum,
    int DiscardedLightByteCount);

internal static class D3dbspAssetGraphBuilder
{
    public static D3dbspAssetGraph Build(
        string inputPath,
        string assetName,
        bool forceFullbright,
        int ps3WorldDrawPayloadCapacity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ValidateAssetName(assetName);

        string path = Path.GetFullPath(inputPath);
        D3dbspFile file = D3dbspFile.Read(path);
        uint checksum = CalculateChecksum(path);

        ComWorldAsset comWorld = D3dbspPrimaryLightCodec.DecodeComWorld(
            assetName,
            file.GetRequiredData(D3dbspLumpType.PrimaryLights));
        int sunPrimaryLightIndex = D3dbspPrimaryLightCodec.GetLastSunPrimaryLightIndex(
            comWorld.PrimaryLights);

        byte[] sourceEntities = file.GetRequiredData(D3dbspLumpType.Entities).ToArray();
        int discardedLightByteCount = ValidateStrictSourceProfile(
            file,
            sourceEntities,
            forceFullbright);
        byte[] entityString = D3dbspMapEntsCodec.DecodeEntityString(sourceEntities);
        IReadOnlyList<Stage> stages = D3dbspMapEntsCodec.DecodeStages(
            sourceEntities,
            sunPrimaryLightIndex);
        if (!D3dbspMapEntsCodec.IsCanonicalDefaultStage(
                stages,
                sunPrimaryLightIndex))
        {
            throw new NotSupportedException(
                "The d3dbsp contains runtime stages, but its MapTriggers graph has not been recovered yet.");
        }

        var mapEnts = new MapEntsAsset
        {
            Name = assetName,
            EntityStringBytes = entityString,
            NumEntityChars = entityString.Length,
            Trigger = new MapTriggers(),
            Stages = stages,
            StageCount = checked((byte)stages.Count),
            Pad29To2B = [0, 0, 0]
        };

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

        var clipMap = new ClipMapAsset
        {
            SerializedType = XAssetType.ColMapMp,
            Name = assetName,
            IsInUse = 0,
            SerializedIsInUse = 0,
            PlaneCount = planes.Count,
            Planes = planes,
            NumStaticModels = 0,
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
            SModelNodes = [new SModelAabbNode()],
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
            ps3WorldDrawPayloadCapacity,
            checksum,
            lightGrid,
            lightRegions);

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
            gfxWorld,
            CreateMapScript(assetName),
            CreateMapMarker(assetName)
        ];
        BaseAsset[] ownedLightmapImages = gfxWorld.WorldDraw.Lightmaps
            .SelectMany(lightmap => new[] { lightmap.Primary, lightmap.Secondary })
            .OfType<GfxImageAsset>()
            .Where(image => image.Name is { Length: > 0 } name && name[0] != ',')
            .Cast<BaseAsset>()
            .DistinctBy(asset => (asset.SerializedAssetType, asset.SerializedAssetName))
            .ToArray();
        BaseAsset[] dependencies = renderMaterials
            .Cast<BaseAsset>()
            .Concat(gfxWorld.WorldDraw.ReflectionProbeImages.OfType<GfxImageAsset>())
            .Concat(gfxWorld.WorldDraw.Lightmaps.SelectMany(lightmap =>
                new[] { lightmap.Primary, lightmap.Secondary }
                    .OfType<GfxImageAsset>()
                    .Where(image => image.Name is { Length: > 0 } name && name[0] == ',')))
            .DistinctBy(asset => (asset.SerializedAssetType, asset.SerializedAssetName))
            .ToArray();
        return new D3dbspAssetGraph(
            Array.AsReadOnly(roots),
            Array.AsReadOnly<BaseAsset>([mapEnts, .. ownedLightmapImages]),
            Array.AsReadOnly(dependencies),
            checksum,
            discardedLightByteCount);
    }

    private static IReadOnlyList<IReadOnlyList<T>> EmptyDynamicLists<T>() =>
        Array.AsReadOnly<IReadOnlyList<T>>(
            [Array.Empty<T>(), Array.Empty<T>()]);

    private static RawFileAsset CreateMapScript(string assetName)
    {
        string scriptName = assetName[..^".d3dbsp".Length] + ".gsc";
        // These factions own the player-model closure selected by FastFileConverter.
        const string script =
            "main()\r\n" +
            "{\r\n" +
            "\tmaps\\mp\\_load::main();\r\n" +
            "\tgame[\"allies\"] = \"us_army\";\r\n" +
            "\tgame[\"axis\"] = \"opforce_airborne\";\r\n" +
            "\tgame[\"attackers\"] = \"allies\";\r\n" +
            "\tgame[\"defenders\"] = \"axis\";\r\n" +
            "}\r\n";
        byte[] content = Encoding.ASCII.GetBytes(script);
        return new RawFileAsset
        {
            Name = scriptName,
            CompressedLen = 0,
            Len = content.Length,
            Buffer = [.. content, 0]
        };
    }

    private static RawFileAsset CreateMapMarker(string assetName) => new()
    {
        Name = Path.GetFileNameWithoutExtension(assetName.Replace('\\', '/')),
        CompressedLen = 0,
        Len = 0,
        Buffer = [0]
    };

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
        if (assetName[0] == ',' || assetName.Contains('\0') ||
            !assetName.EndsWith(".d3dbsp", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The map asset name must be an owned .d3dbsp wire name without a comma prefix.",
                nameof(assetName));
        }
    }

    private static int ValidateStrictSourceProfile(
        D3dbspFile file,
        ReadOnlySpan<byte> sourceEntities,
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
                     D3dbspLumpType.Visibility,
                     D3dbspLumpType.PathConnections,
                     D3dbspLumpType.ReflectionProbes,
                     D3dbspLumpType.UnlayeredCullGroups
                 })
        {
            if (!file.GetOptionalData(type).IsEmpty)
            {
                throw new NotSupportedException(
                    $"Strict fastfile conversion does not yet support nonempty {type} data.");
            }
        }

        IReadOnlyList<string> unsupportedFeatures =
            D3dbspMapEntsCodec.FindUnsupportedAssetGraphFeatures(sourceEntities);
        if (unsupportedFeatures.Count != 0)
        {
            throw new NotSupportedException(
                "Strict fastfile conversion does not yet reconstruct " +
                string.Join(", ", unsupportedFeatures) + ".");
        }

        int lightByteCount = file.GetOptionalData(D3dbspLumpType.LightBytes).Length;
        if (lightByteCount != 0 && !forceFullbright)
        {
            throw new NotSupportedException(
                $"The d3dbsp contains {lightByteCount} compiled light bytes. " +
                "Pass --fullbright to explicitly discard its lightmaps.");
        }
        if (lightByteCount != 0 && lightByteCount % (3 * 1024 * 1024) != 0)
        {
            throw new InvalidDataException(
                $"The LightBytes lump has noncanonical length {lightByteCount}.");
        }

        return lightByteCount;
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
