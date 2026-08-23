using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.ComWorld;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.MapEnts;
using IW4.Assets.Assets.Physics;
using IW4.Assets.Math;
using IW4.Studio.Documents;

namespace IW4Map;

internal static class MapPairInspector
{
    public static void Inspect(string d3dbspPath, string fastFilePath)
    {
        D3dbspFile d3dbsp = D3dbspFile.Read(Path.GetFullPath(d3dbspPath));
        using FastFileWorkspace workspace = FastFileInspector.Open(fastFilePath);

        GfxWorldAsset gfxWorld = FastFileInspector.GetSingle<GfxWorldAsset>(workspace) ??
            throw new InvalidDataException("The fastfile has no GfxMap definition.");
        ClipMapAsset clipMap = FastFileInspector.GetSingle<ClipMapAsset>(workspace) ??
            throw new InvalidDataException("The fastfile has no ColMap definition.");
        ComWorldAsset comWorld = FastFileInspector.GetSingle<ComWorldAsset>(workspace) ??
            throw new InvalidDataException("The fastfile has no ComMap definition.");
        MapEntsAsset? mapEnts = FastFileInspector.GetSingle<MapEntsAsset>(workspace) ?? clipMap.MapEnts;

        Console.WriteLine($"d3dbsp: {Path.GetFullPath(d3dbspPath)}");
        Console.WriteLine($"fastfile: {Path.GetFullPath(fastFilePath)}");
        Console.WriteLine($"map-name: {gfxWorld.Name}");
        Console.WriteLine();
        Console.WriteLine("source                         d3dbsp      linked      relation");
        WriteCount("materials", Count(d3dbsp, D3dbspLumpType.Materials, 72), clipMap.Materials.Count, "names and flags retained");
        WriteCount("planes", Count(d3dbsp, D3dbspLumpType.Planes, 16), clipMap.Planes.Count, "float equations retained");
        WriteCount("brush sides", Count(d3dbsp, D3dbspLumpType.BrushSides, 8), clipMap.BrushSides.Count, "axial sides fold into brushes");
        WriteCount("brush edges", ByteCount(d3dbsp, D3dbspLumpType.BrushEdges), clipMap.BrushEdges.Count, "bytes retained");
        WriteCount("brushes", Count(d3dbsp, D3dbspLumpType.Brushes, 4), clipMap.Brushes.Count, "count retained; material index lost");
        WriteCount("collision nodes", Count(d3dbsp, D3dbspLumpType.Nodes, 36), clipMap.Nodes.Count, "tree retained; bounds discarded");
        WriteCount("collision leafs", Count(d3dbsp, D3dbspLumpType.Leafs, 24), clipMap.Leafs.Count, "leaf data transformed");
        WriteCount("leaf brushes", Count(d3dbsp, D3dbspLumpType.LeafBrushes, 4), clipMap.LeafBrushes.Count, "indices narrow to ushort");
        WriteCount("collision verts", Count(d3dbsp, D3dbspLumpType.CollisionVerts, 12), clipMap.Verts.Count, "float positions retained");
        WriteCount("collision tris", Count(d3dbsp, D3dbspLumpType.CollisionTris, 6), clipMap.TriCount, "ushort triples retained");
        WriteCount("collision borders", Count(d3dbsp, D3dbspLumpType.CollisionBorders, 28), clipMap.Borders.Count, "fields retained");
        WriteCount("collision partitions", Count(d3dbsp, D3dbspLumpType.CollisionPartitions, 12), clipMap.Partitions.Count, "check stamp discarded");
        WriteCount("collision AABBs", Count(d3dbsp, D3dbspLumpType.CollisionAabbs, 32), clipMap.AabbTrees.Count, "fields retained");
        WriteCount("brush models", Count(d3dbsp, D3dbspLumpType.Models, 48), clipMap.CModels.Count, "one geometry context retained");
        WriteCount("entity bytes", ByteCount(d3dbsp, D3dbspLumpType.Entities), mapEnts?.EntityStringBytes.Count ?? 0, "purged entities fan out to assets");
        WriteCount("primary lights", Count(d3dbsp, D3dbspLumpType.PrimaryLights, 128), comWorld.PrimaryLights.Count, "unused bytes/FOV canonicalized");
        WriteCount("lightmaps", Count(d3dbsp, D3dbspLumpType.LightBytes, 3 * 1024 * 1024), gfxWorld.WorldDraw.Lightmaps.Count, "images may merge");
        WriteCount("light-grid row bytes", ByteCount(d3dbsp, D3dbspLumpType.LightGridRows), gfxWorld.LightGrid.RawRowData.Count, "size retained; row headers endian-transformed");
        WriteCount("light-grid entries", Count(d3dbsp, D3dbspLumpType.LightGridEntries, 4), gfxWorld.LightGrid.Entries.Count, "fields retained");
        WriteCount(
            "light-grid colors",
            Count(d3dbsp, D3dbspLumpType.LightGridColors, 168),
            gfxWorld.LightGrid.Colors.Count,
            "linker appends a native fallback (two rows for no-bake worlds)");
        WriteCount("light regions", ByteCount(d3dbsp, D3dbspLumpType.LightRegions), gfxWorld.LightRegions.Count, "hull counts retained");
        WriteCount("reflection probes", Count(d3dbsp, D3dbspLumpType.ReflectionProbes, 131_140), checked((int)gfxWorld.WorldDraw.ReflectionProbeCount), "linker prepends one default");
        WriteCount(
            "triangle soups",
            CountSelected(d3dbsp, D3dbspLumpType.UnlayeredTriangles, D3dbspLumpType.Triangles, 24),
            gfxWorld.Dpvs.Surfaces.Count,
            "selected context transformed/sorted");
        WriteCount(
            "draw vertices",
            CountSelected(d3dbsp, D3dbspLumpType.UnlayeredDrawVerts, D3dbspLumpType.DrawVerts, 68),
            checked((int)gfxWorld.WorldDraw.VertexCount),
            "repacked/deduplicated");
        WriteCount(
            "draw indices",
            CountSelected(d3dbsp, D3dbspLumpType.UnlayeredDrawIndices, D3dbspLumpType.DrawIndices, 2),
            gfxWorld.WorldDraw.Indices.Count,
            "regrouped by surface");

        Console.WriteLine();
        ComWorldAsset decodedComWorld = InspectPrimaryLights(d3dbsp, comWorld);
        InspectForwardLighting(d3dbsp, gfxWorld, decodedComWorld);
        InspectForwardCollision(d3dbsp, clipMap);
        InspectMapEnts(d3dbsp, mapEnts);
        InspectReversibleLumps(d3dbsp, clipMap, gfxWorld);
    }

    private static ComWorldAsset InspectPrimaryLights(
        D3dbspFile d3dbsp,
        ComWorldAsset linkedComWorld)
    {
        D3dbspLump lump = RequireLump(d3dbsp, D3dbspLumpType.PrimaryLights);
        string? mapName = linkedComWorld.Name;
        if (string.IsNullOrWhiteSpace(mapName))
            throw new InvalidDataException("The linked ComWorld has no asset name.");

        ComWorldAsset decodedComWorld = D3dbspPrimaryLightCodec.DecodeComWorld(
            mapName,
            lump.Data);
        byte[] decodedBytes = D3dbspPrimaryLightCodec.Encode(decodedComWorld.PrimaryLights);
        byte[] fastFileBytes = D3dbspPrimaryLightCodec.Encode(linkedComWorld.PrimaryLights);
        int expandedMismatchCount = decodedComWorld.PrimaryLights.Count == linkedComWorld.PrimaryLights.Count
            ? decodedComWorld.PrimaryLights.Zip(linkedComWorld.PrimaryLights).Count(pair =>
                BitConverter.SingleToInt32Bits(pair.First.CosHalfFovExpanded) !=
                BitConverter.SingleToInt32Bits(pair.Second.CosHalfFovExpanded))
            : -1;

        Console.WriteLine("com-world.asset-name-source: caller-supplied; not stored in d3dbsp");
        Console.WriteLine($"com-world.is-in-use-match: {decodedComWorld.IsInUse == linkedComWorld.IsInUse}");
        Console.WriteLine($"com-world.primary-light-count-match: {decodedComWorld.PrimaryLightCount == linkedComWorld.PrimaryLightCount}");
        Console.WriteLine($"primary-lights.codec-roundtrip-byte-exact: {decodedBytes.AsSpan().SequenceEqual(lump.Data)}");
        Console.WriteLine($"primary-lights.fastfile-to-disk-byte-exact: {fastFileBytes.AsSpan().SequenceEqual(lump.Data)}");
        Console.WriteLine($"primary-lights.expanded-fov-mismatches: {expandedMismatchCount}");
        return decodedComWorld;
    }

    private static void InspectForwardLighting(
        D3dbspFile d3dbsp,
        GfxWorldAsset linkedGfxWorld,
        ComWorldAsset decodedComWorld)
    {
        uint lastSunPrimaryLightIndex = checked((uint)
            D3dbspPrimaryLightCodec.GetLastSunPrimaryLightIndex(
                decodedComWorld.PrimaryLights));
        bool hasLightRegions = d3dbsp.Lumps.Any(
            lump => lump.Type == D3dbspLumpType.LightRegions);
        GfxLightGrid decodedLightGrid = D3dbspLightingCodec.DecodeLightGrid(
            RequireLump(d3dbsp, D3dbspLumpType.LightGridHeader).Data,
            GetOptionalLumpData(d3dbsp, D3dbspLumpType.LightGridRows),
            GetOptionalLumpData(d3dbsp, D3dbspLumpType.LightGridEntries),
            GetOptionalLumpData(d3dbsp, D3dbspLumpType.LightGridColors),
            lastSunPrimaryLightIndex,
            hasLightRegions);
        IReadOnlyList<GfxLightRegion> decodedLightRegions =
            D3dbspLightingCodec.DecodeLightRegions(
                GetOptionalLumpData(d3dbsp, D3dbspLumpType.LightRegions),
                GetOptionalLumpData(d3dbsp, D3dbspLumpType.LightRegionHulls),
                GetOptionalLumpData(d3dbsp, D3dbspLumpType.LightRegionAxes),
                decodedComWorld.PrimaryLightCount,
                hasLightRegions);

        GfxLightGrid linkedLightGrid = linkedGfxWorld.LightGrid;
        bool lightGridMatch =
            linkedGfxWorld.SunPrimaryLightIndex >= 0 &&
            decodedLightGrid.SunPrimaryLightIndex == (uint)linkedGfxWorld.SunPrimaryLightIndex &&
            decodedComWorld.PrimaryLightCount == linkedGfxWorld.PrimaryLightCount &&
            decodedLightGrid.HasLightRegionsRaw == linkedLightGrid.HasLightRegionsRaw &&
            decodedLightGrid.SunPrimaryLightIndex == linkedLightGrid.SunPrimaryLightIndex &&
            decodedLightGrid.RawRowDataSize == linkedLightGrid.RawRowDataSize &&
            decodedLightGrid.EntryCount == linkedLightGrid.EntryCount &&
            decodedLightGrid.ColorCount == linkedLightGrid.ColorCount &&
            D3dbspLightingCodec.EncodeLightGridHeader(decodedLightGrid).AsSpan().SequenceEqual(
                D3dbspLightingCodec.EncodeLightGridHeader(linkedLightGrid)) &&
            D3dbspLightingCodec.EncodeLightGridRows(decodedLightGrid).AsSpan().SequenceEqual(
                D3dbspLightingCodec.EncodeLightGridRows(linkedLightGrid)) &&
            D3dbspLightingCodec.EncodeLightGridEntries(decodedLightGrid).AsSpan().SequenceEqual(
                D3dbspLightingCodec.EncodeLightGridEntries(linkedLightGrid)) &&
            D3dbspLightingCodec.EncodeLightGridColors(
                decodedLightGrid,
                omitLinkerGeneratedDefault: false).AsSpan().SequenceEqual(
                    D3dbspLightingCodec.EncodeLightGridColors(
                        linkedLightGrid,
                        omitLinkerGeneratedDefault: false));
        bool lightRegionsMatch =
            decodedLightRegions.Count == decodedComWorld.PrimaryLightCount &&
            decodedLightRegions.Count == linkedGfxWorld.LightRegions.Count &&
            D3dbspLightingCodec.EncodeLightRegions(
                decodedLightRegions,
                decodedLightGrid.HasLightRegions).AsSpan().SequenceEqual(
                    D3dbspLightingCodec.EncodeLightRegions(
                        linkedGfxWorld.LightRegions,
                        linkedLightGrid.HasLightRegions)) &&
            D3dbspLightingCodec.EncodeLightRegionHulls(
                decodedLightRegions,
                decodedLightGrid.HasLightRegions).AsSpan().SequenceEqual(
                    D3dbspLightingCodec.EncodeLightRegionHulls(
                        linkedGfxWorld.LightRegions,
                        linkedLightGrid.HasLightRegions)) &&
            D3dbspLightingCodec.EncodeLightRegionAxes(
                decodedLightRegions,
                decodedLightGrid.HasLightRegions).AsSpan().SequenceEqual(
                    D3dbspLightingCodec.EncodeLightRegionAxes(
                        linkedGfxWorld.LightRegions,
                        linkedLightGrid.HasLightRegions));

        Console.WriteLine($"gfx-light-grid.asset-graph-match: {lightGridMatch}");
        Console.WriteLine($"gfx-light-regions.asset-graph-match: {lightRegionsMatch}");
    }

    private static void InspectForwardCollision(D3dbspFile d3dbsp, ClipMapAsset linkedClipMap)
    {
        IReadOnlyList<CPlane> decodedPlanes =
            D3dbspCollisionCodec.DecodePlanes(
                RequireLump(d3dbsp, D3dbspLumpType.Planes).Data);
        IReadOnlyList<ClipMaterial> decodedMaterials =
            D3dbspCollisionCodec.DecodeMaterials(
                RequireLump(d3dbsp, D3dbspLumpType.Materials).Data);
        IReadOnlyList<CNode> decodedNodes = D3dbspCollisionCodec.DecodeNodes(
            RequireLump(d3dbsp, D3dbspLumpType.Nodes).Data,
            decodedPlanes);

        bool planesMatch =
            decodedPlanes.Count == linkedClipMap.Planes.Count &&
            decodedPlanes.Zip(linkedClipMap.Planes).All(pair =>
                PlaneEquals(pair.First, pair.Second));
        bool materialsMatch =
            decodedMaterials.Count == linkedClipMap.Materials.Count &&
            decodedMaterials.Zip(linkedClipMap.Materials).All(pair =>
                string.Equals(pair.First.Name, pair.Second.Name, StringComparison.Ordinal) &&
                pair.First.SurfaceFlags == pair.Second.SurfaceFlags &&
                pair.First.Contents == pair.Second.Contents);
        bool nodesMatch =
            decodedNodes.Count == linkedClipMap.Nodes.Count &&
            decodedNodes.Zip(linkedClipMap.Nodes).All(pair =>
                PlaneEquals(pair.First.Plane, pair.Second.Plane) &&
                pair.First.Children.SequenceEqual(pair.Second.Children));

        IReadOnlyList<byte> decodedBrushEdges = D3dbspCollisionCodec.DecodeBrushEdges(
            GetOptionalLumpData(d3dbsp, D3dbspLumpType.BrushEdges));
        IReadOnlyList<ushort> decodedLeafBrushes = D3dbspCollisionCodec.DecodeLeafBrushes(
            GetOptionalLumpData(d3dbsp, D3dbspLumpType.LeafBrushes));
        IReadOnlyList<uint> decodedLeafSurfaces = D3dbspCollisionCodec.DecodeLeafSurfaces(
            GetOptionalLumpData(d3dbsp, D3dbspLumpType.LeafSurfaces));
        IReadOnlyList<Vec3> decodedVerts =
            D3dbspCollisionCodec.DecodeCollisionVerts(
                GetOptionalLumpData(d3dbsp, D3dbspLumpType.CollisionVerts));
        IReadOnlyList<ushort> decodedTriIndices = D3dbspCollisionCodec.DecodeCollisionTris(
            GetOptionalLumpData(d3dbsp, D3dbspLumpType.CollisionTris));
        IReadOnlyList<byte> decodedEdgeWalkable =
            D3dbspCollisionCodec.DecodeCollisionEdgeWalkable(
                GetOptionalLumpData(d3dbsp, D3dbspLumpType.CollisionEdgeWalkable));
        IReadOnlyList<CollisionBorder> decodedBorders =
            D3dbspCollisionCodec.DecodeCollisionBorders(
                GetOptionalLumpData(d3dbsp, D3dbspLumpType.CollisionBorders));
        IReadOnlyList<CollisionAabbTree> decodedAabbs =
            D3dbspCollisionCodec.DecodeCollisionAabbs(
                GetOptionalLumpData(d3dbsp, D3dbspLumpType.CollisionAabbs));
        IReadOnlyList<CollisionPartition> decodedPartitions =
            D3dbspCollisionCodec.DecodeCollisionPartitions(
                GetOptionalLumpData(d3dbsp, D3dbspLumpType.CollisionPartitions),
                decodedBorders,
                decodedTriIndices.Count / 3);
        var decodedBrushGraph = D3dbspCollisionCodec.DecodeBrushes(
            RequireLump(d3dbsp, D3dbspLumpType.Brushes).Data,
            RequireLump(d3dbsp, D3dbspLumpType.BrushSides).Data,
            RequireLump(d3dbsp, D3dbspLumpType.BrushSideEdgeCounts).Data,
            decodedBrushEdges,
            decodedPlanes,
            decodedMaterials,
            new ushort[RequireLump(d3dbsp, D3dbspLumpType.Brushes).Data.Length / 4]);
        var decodedLeafGraph = D3dbspCollisionCodec.DecodeLeafGraph(
            RequireLump(d3dbsp, D3dbspLumpType.Leafs).Data,
            RequireLump(d3dbsp, D3dbspLumpType.Models).Data,
            decodedLeafBrushes,
            decodedBrushGraph.BrushBounds,
            decodedBrushGraph.BrushContents,
            decodedAabbs,
            decodedMaterials);

        bool directPayloadMatch =
            decodedBrushEdges.SequenceEqual(linkedClipMap.BrushEdges) &&
            decodedLeafBrushes.SequenceEqual(linkedClipMap.LeafBrushes) &&
            decodedLeafSurfaces.SequenceEqual(linkedClipMap.LeafSurfaces) &&
            D3dbspCollisionCodec.EncodeCollisionVerts(decodedVerts).AsSpan().SequenceEqual(
                D3dbspCollisionCodec.EncodeCollisionVerts(linkedClipMap.Verts)) &&
            decodedTriIndices.SequenceEqual(linkedClipMap.TriIndices) &&
            decodedEdgeWalkable.SequenceEqual(linkedClipMap.TriEdgeIsWalkable) &&
            D3dbspCollisionCodec.EncodeCollisionBorders(decodedBorders).AsSpan().SequenceEqual(
                D3dbspCollisionCodec.EncodeCollisionBorders(linkedClipMap.Borders)) &&
            D3dbspCollisionCodec.EncodeCollisionAabbs(decodedAabbs).AsSpan().SequenceEqual(
                D3dbspCollisionCodec.EncodeCollisionAabbs(linkedClipMap.AabbTrees));
        bool partitionsMatch =
            decodedPartitions.Count == linkedClipMap.Partitions.Count &&
            decodedPartitions.Zip(linkedClipMap.Partitions).All(pair =>
                CollisionPartitionEquals(pair.First, pair.Second));
        bool brushSidesMatch =
            decodedBrushGraph.BrushSides.Count == linkedClipMap.BrushSides.Count &&
            decodedBrushGraph.BrushSides.Zip(linkedClipMap.BrushSides).All(pair =>
                BrushSideEquals(pair.First, pair.Second));
        bool brushTopologyMatch =
            decodedBrushGraph.Brushes.Count == linkedClipMap.Brushes.Count &&
            decodedBrushGraph.Brushes.Zip(linkedClipMap.Brushes).All(pair =>
                BrushEqualsExceptGlassPieceIndex(pair.First, pair.Second));
        int brushBoundsMismatchCount = decodedBrushGraph.BrushBounds.Count == linkedClipMap.BrushBounds.Count
            ? decodedBrushGraph.BrushBounds.Zip(linkedClipMap.BrushBounds).Count(pair =>
                !BoundsEquals(pair.First, pair.Second))
            : -1;
        bool brushBoundsMatch = brushBoundsMismatchCount == 0;
        bool brushContentsMatch =
            decodedBrushGraph.BrushContents.SequenceEqual(linkedClipMap.BrushContents);
        bool leafPayloadMatch =
            decodedLeafGraph.Leafs.Count == linkedClipMap.Leafs.Count &&
            decodedLeafGraph.Leafs.Zip(linkedClipMap.Leafs).All(pair =>
                LeafEqualsExceptTreeRoot(pair.First, pair.Second));
        bool modelsMatch =
            decodedLeafGraph.CModels.Count == linkedClipMap.CModels.Count &&
            decodedLeafGraph.CModels.Zip(linkedClipMap.CModels).All(pair =>
                CModelEqualsExceptTreeRoot(pair.First, pair.Second));

        Console.WriteLine($"clip-map.planes-forward-graph-match: {planesMatch}");
        Console.WriteLine($"clip-map.materials-forward-graph-match: {materialsMatch}");
        Console.WriteLine($"clip-map.nodes-forward-graph-match: {nodesMatch}");
        Console.WriteLine($"clip-map.direct-payload-forward-graph-match: {directPayloadMatch}");
        Console.WriteLine($"clip-map.partitions-forward-graph-match: {partitionsMatch}");
        Console.WriteLine($"clip-map.brush-sides-forward-graph-match: {brushSidesMatch}");
        Console.WriteLine($"clip-map.brush-topology-forward-graph-match: {brushTopologyMatch}");
        Console.WriteLine(
            $"clip-map.brush-bounds-forward-graph-match: {brushBoundsMatch} " +
            $"(row mismatches: {brushBoundsMismatchCount})");
        Console.WriteLine($"clip-map.brush-contents-forward-graph-match: {brushContentsMatch}");
        Console.WriteLine($"clip-map.leaf-payload-forward-graph-match: {leafPayloadMatch}");
        Console.WriteLine($"clip-map.models-forward-graph-match: {modelsMatch}");
        Console.WriteLine(
            $"clip-map.leaf-brush-tree-layout: canonical terminal nodes " +
            $"({decodedLeafGraph.LeafBrushNodes.Count} rows; linked {linkedClipMap.LeafBrushNodes.Count})");
        Console.WriteLine("clip-map.brush-glass-index-source: linked glass graph (not stored in brush lumps)");
    }

    private static void InspectMapEnts(D3dbspFile d3dbsp, MapEntsAsset? linkedMapEnts)
    {
        if (linkedMapEnts is null)
        {
            Console.WriteLine("map-ents.asset-graph-match: unavailable (the fastfile has no MapEnts definition)");
            return;
        }

        byte[] entityLump = RequireLump(d3dbsp, D3dbspLumpType.Entities).Data;
        byte[] decodedEntityString = D3dbspMapEntsCodec.DecodeEntityString(entityLump);
        IReadOnlyList<Stage> decodedStages = D3dbspMapEntsCodec.DecodeStages(entityLump);
        bool stagesMatch =
            decodedStages.Count == linkedMapEnts.Stages.Count &&
            decodedStages.Zip(linkedMapEnts.Stages).All(pair => StageEquals(pair.First, pair.Second));

        Console.WriteLine(
            $"map-ents.entity-string-forward-graph-match: " +
            decodedEntityString.SequenceEqual(linkedMapEnts.EntityStringBytes));
        Console.WriteLine($"map-ents.stages-forward-graph-match: {stagesMatch}");
        Console.WriteLine("map-ents.triggers-forward-graph-match: unresolved (no direct d3dbsp lump)");
    }

    private static bool PlaneEquals(
        CPlane? left,
        CPlane? right) =>
        left is not null &&
        right is not null &&
        BitConverter.SingleToInt32Bits(left.Normal.X) ==
            BitConverter.SingleToInt32Bits(right.Normal.X) &&
        BitConverter.SingleToInt32Bits(left.Normal.Y) ==
            BitConverter.SingleToInt32Bits(right.Normal.Y) &&
        BitConverter.SingleToInt32Bits(left.Normal.Z) ==
            BitConverter.SingleToInt32Bits(right.Normal.Z) &&
        BitConverter.SingleToInt32Bits(left.Dist) ==
            BitConverter.SingleToInt32Bits(right.Dist) &&
        left.Type == right.Type &&
        left.SignBits == right.SignBits;

    private static bool BrushSideEquals(
        CBrushSide left,
        CBrushSide right) =>
        PlaneEquals(left.Plane, right.Plane) &&
        left.MaterialNum == right.MaterialNum &&
        left.FirstAdjacentSideOffset == right.FirstAdjacentSideOffset &&
        left.EdgeCount == right.EdgeCount;

    private static bool CollisionPartitionEquals(
        CollisionPartition left,
        CollisionPartition right) =>
        left.TriCount == right.TriCount &&
        left.BorderCount == right.BorderCount &&
        left.FirstVertSegment == right.FirstVertSegment &&
        left.Pad03 == right.Pad03 &&
        left.FirstTri == right.FirstTri &&
        D3dbspCollisionCodec.EncodeCollisionBorders(left.Borders).AsSpan().SequenceEqual(
            D3dbspCollisionCodec.EncodeCollisionBorders(right.Borders));

    private static bool BrushEqualsExceptGlassPieceIndex(
        CBrush left,
        CBrush right) =>
        left.NumSides == right.NumSides &&
        left.Sides.Count == right.Sides.Count &&
        left.Sides.Zip(right.Sides).All(pair => BrushSideEquals(pair.First, pair.Second)) &&
        left.BaseAdjacentSide.SequenceEqual(right.BaseAdjacentSide) &&
        left.AxialMaterialNum.SequenceEqual(right.AxialMaterialNum) &&
        left.FirstAdjacentSideOffsets.SequenceEqual(right.FirstAdjacentSideOffsets) &&
        left.EdgeCount.SequenceEqual(right.EdgeCount);

    private static bool BoundsEquals(
        Bounds left,
        Bounds right) =>
        Vec3Equals(left.MidPoint, right.MidPoint) &&
        Vec3Equals(left.HalfSize, right.HalfSize);

    private static bool LeafEqualsExceptTreeRoot(CLeaf left, CLeaf right) =>
        left.FirstCollAabbIndex == right.FirstCollAabbIndex &&
        left.CollAabbCount == right.CollAabbCount &&
        left.BrushContents == right.BrushContents &&
        left.TerrainContents == right.TerrainContents &&
        Vec3Equals(left.Mins, right.Mins) &&
        Vec3Equals(left.Maxs, right.Maxs);

    private static bool CModelEqualsExceptTreeRoot(CModel left, CModel right) =>
        Vec3Equals(left.Mins, right.Mins) &&
        Vec3Equals(left.Maxs, right.Maxs) &&
        BitConverter.SingleToInt32Bits(left.Radius) ==
            BitConverter.SingleToInt32Bits(right.Radius) &&
        LeafEqualsExceptTreeRoot(left.Leaf, right.Leaf);

    private static bool Vec3Equals(Vec3 left, Vec3 right) =>
        BitConverter.SingleToInt32Bits(left.X) == BitConverter.SingleToInt32Bits(right.X) &&
        BitConverter.SingleToInt32Bits(left.Y) == BitConverter.SingleToInt32Bits(right.Y) &&
        BitConverter.SingleToInt32Bits(left.Z) == BitConverter.SingleToInt32Bits(right.Z);

    private static bool StageEquals(Stage left, Stage right) =>
        string.Equals(left.StageName, right.StageName, StringComparison.Ordinal) &&
        BitConverter.SingleToInt32Bits(left.Origin.X) ==
            BitConverter.SingleToInt32Bits(right.Origin.X) &&
        BitConverter.SingleToInt32Bits(left.Origin.Y) ==
            BitConverter.SingleToInt32Bits(right.Origin.Y) &&
        BitConverter.SingleToInt32Bits(left.Origin.Z) ==
            BitConverter.SingleToInt32Bits(right.Origin.Z) &&
        left.TriggerIndex == right.TriggerIndex &&
        left.SunPrimaryLightIndex == right.SunPrimaryLightIndex &&
        left.Pad13 == right.Pad13;

    private static void InspectReversibleLumps(
        D3dbspFile d3dbsp,
        ClipMapAsset clipMap,
        GfxWorldAsset gfxWorld)
    {
        Console.WriteLine();
        Console.WriteLine("fastfile-to-d3dbsp reversible-lump checks:");
        WriteLumpComparison(
            d3dbsp,
            D3dbspLumpType.Planes,
            D3dbspCollisionCodec.EncodePlanes(clipMap.Planes));
        WriteLumpComparison(
            d3dbsp,
            D3dbspLumpType.BrushEdges,
            D3dbspCollisionCodec.EncodeBrushEdges(clipMap.BrushEdges));
        WriteLumpComparison(
            d3dbsp,
            D3dbspLumpType.BrushSideEdgeCounts,
            D3dbspCollisionCodec.EncodeBrushSideEdgeCounts(clipMap.Brushes));
        WriteLumpComparison(
            d3dbsp,
            D3dbspLumpType.LeafBrushes,
            D3dbspCollisionCodec.EncodeLeafBrushes(clipMap.LeafBrushes));
        WriteLumpComparison(
            d3dbsp,
            D3dbspLumpType.LeafSurfaces,
            D3dbspCollisionCodec.EncodeLeafSurfaces(clipMap.LeafSurfaces));
        WriteLumpComparison(
            d3dbsp,
            D3dbspLumpType.CollisionVerts,
            D3dbspCollisionCodec.EncodeCollisionVerts(clipMap.Verts));
        WriteLumpComparison(
            d3dbsp,
            D3dbspLumpType.CollisionTris,
            D3dbspCollisionCodec.EncodeCollisionTris(clipMap.TriIndices));
        WriteLumpComparison(
            d3dbsp,
            D3dbspLumpType.CollisionEdgeWalkable,
            D3dbspCollisionCodec.EncodeCollisionEdgeWalkable(clipMap.TriEdgeIsWalkable));
        WriteLumpComparison(
            d3dbsp,
            D3dbspLumpType.CollisionBorders,
            D3dbspCollisionCodec.EncodeCollisionBorders(clipMap.Borders));
        WriteLumpComparison(
            d3dbsp,
            D3dbspLumpType.CollisionAabbs,
            D3dbspCollisionCodec.EncodeCollisionAabbs(clipMap.AabbTrees));

        GfxLightGrid lightGrid = gfxWorld.LightGrid;
        WriteLumpComparison(
            d3dbsp,
            D3dbspLumpType.LightGridHeader,
            D3dbspLightingCodec.EncodeLightGridHeader(lightGrid));
        WriteLumpComparison(
            d3dbsp,
            D3dbspLumpType.LightGridRows,
            D3dbspLightingCodec.EncodeLightGridRows(lightGrid));
        WriteLumpComparison(
            d3dbsp,
            D3dbspLumpType.LightGridEntries,
            D3dbspLightingCodec.EncodeLightGridEntries(lightGrid));
        WriteLumpComparison(
            d3dbsp,
            D3dbspLumpType.LightGridColors,
            D3dbspLightingCodec.EncodeLightGridColors(
                lightGrid,
                omitLinkerGeneratedDefault: true));
        WriteLumpComparison(
            d3dbsp,
            D3dbspLumpType.LightRegions,
            D3dbspLightingCodec.EncodeLightRegions(
                gfxWorld.LightRegions,
                lightGrid.HasLightRegions));
        WriteLumpComparison(
            d3dbsp,
            D3dbspLumpType.LightRegionHulls,
            D3dbspLightingCodec.EncodeLightRegionHulls(
                gfxWorld.LightRegions,
                lightGrid.HasLightRegions));
        WriteLumpComparison(
            d3dbsp,
            D3dbspLumpType.LightRegionAxes,
            D3dbspLightingCodec.EncodeLightRegionAxes(
                gfxWorld.LightRegions,
                lightGrid.HasLightRegions));
    }

    private static void WriteLumpComparison(
        D3dbspFile d3dbsp,
        D3dbspLumpType type,
        byte[] encoded)
    {
        D3dbspLump? lump = d3dbsp.Lumps.FirstOrDefault(candidate => candidate.Type == type);
        string result;
        if (lump is null)
        {
            result = encoded.Length == 0
                ? "equivalent empty (compiler omitted the lump)"
                : $"source missing; fastfile encoded {encoded.Length} bytes";
        }
        else if (encoded.AsSpan().SequenceEqual(lump.Data))
        {
            result = $"byte-exact ({encoded.Length} bytes)";
        }
        else
        {
            int sharedLength = Math.Min(encoded.Length, lump.Data.Length);
            int firstDifference = 0;
            while (firstDifference < sharedLength && encoded[firstDifference] == lump.Data[firstDifference])
                firstDifference++;

            string firstDifferenceText = firstDifference < sharedLength
                ? $"first difference 0x{firstDifference:X}"
                : "common prefix matches";
            result =
                $"mismatch (source {lump.Data.Length} bytes, encoded {encoded.Length}; {firstDifferenceText})";
        }

        Console.WriteLine($"  {type,-28} {result}");
    }

    private static int Count(D3dbspFile file, D3dbspLumpType type, int elementSize)
    {
        int length = ByteCount(file, type);
        if (length % elementSize != 0)
        {
            throw new InvalidDataException(
                $"{type} length {length} is not divisible by {elementSize}.");
        }

        return length / elementSize;
    }

    private static int CountSelected(
        D3dbspFile file,
        D3dbspLumpType preferred,
        D3dbspLumpType fallback,
        int elementSize) =>
        Count(file, file.HasLump(preferred) ? preferred : fallback, elementSize);

    private static int ByteCount(D3dbspFile file, D3dbspLumpType type) =>
        file.Lumps.FirstOrDefault(lump => lump.Type == type)?.Data.Length ?? 0;

    private static D3dbspLump RequireLump(D3dbspFile file, D3dbspLumpType type) =>
        file.Lumps.FirstOrDefault(lump => lump.Type == type) ??
        throw new InvalidDataException($"The d3dbsp has no {type} lump.");

    private static byte[] GetOptionalLumpData(D3dbspFile file, D3dbspLumpType type) =>
        file.Lumps.FirstOrDefault(lump => lump.Type == type)?.Data ?? Array.Empty<byte>();

    private static void WriteCount(
        string source,
        int d3dbspCount,
        int linkedCount,
        string relation) =>
        Console.WriteLine($"{source,-28} {d3dbspCount,10}  {linkedCount,10}  {relation}");
}
