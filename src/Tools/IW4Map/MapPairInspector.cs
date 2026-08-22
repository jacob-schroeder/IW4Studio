using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.ComWorld;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.MapEnts;
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
        WriteCount("materials", Count(d3dbsp, D3dbspLumpType.Materials, 72), clipMap.Materials.Count, "count retained; flags transformed");
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
        WriteCount("light-grid colors", Count(d3dbsp, D3dbspLumpType.LightGridColors, 168), gfxWorld.LightGrid.Colors.Count, "linker appends one default");
        WriteCount("light regions", ByteCount(d3dbsp, D3dbspLumpType.LightRegions), gfxWorld.LightRegions.Count, "hull counts retained");
        WriteCount("reflection probes", Count(d3dbsp, D3dbspLumpType.ReflectionProbes, 131_140), checked((int)gfxWorld.WorldDraw.ReflectionProbeCount), "linker prepends one default");
        WriteCount("triangle soups", Count(d3dbsp, D3dbspLumpType.Triangles, 24), gfxWorld.Dpvs.Surfaces.Count, "selected context transformed/sorted");
        WriteCount("draw vertices", Count(d3dbsp, D3dbspLumpType.DrawVerts, 68), checked((int)gfxWorld.WorldDraw.VertexCount), "repacked/deduplicated");
        WriteCount("draw indices", Count(d3dbsp, D3dbspLumpType.DrawIndices, 2), gfxWorld.WorldDraw.Indices.Count, "regrouped by surface");

        Console.WriteLine();
        InspectPrimaryLights(d3dbsp, comWorld);
        InspectForwardLighting(d3dbsp, gfxWorld);
        InspectReversibleLumps(d3dbsp, clipMap, gfxWorld);
    }

    private static void InspectPrimaryLights(
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

        Console.WriteLine($"com-world.asset-name-match: {decodedComWorld.Name == linkedComWorld.Name}");
        Console.WriteLine($"com-world.is-in-use-match: {decodedComWorld.IsInUse == linkedComWorld.IsInUse}");
        Console.WriteLine($"com-world.primary-light-count-match: {decodedComWorld.PrimaryLightCount == linkedComWorld.PrimaryLightCount}");
        Console.WriteLine($"primary-lights.codec-roundtrip-byte-exact: {decodedBytes.AsSpan().SequenceEqual(lump.Data)}");
        Console.WriteLine($"primary-lights.fastfile-to-disk-byte-exact: {fastFileBytes.AsSpan().SequenceEqual(lump.Data)}");
        Console.WriteLine($"primary-lights.expanded-fov-mismatches: {expandedMismatchCount}");
    }

    private static void InspectForwardLighting(
        D3dbspFile d3dbsp,
        GfxWorldAsset linkedGfxWorld)
    {
        GfxLightGrid decodedLightGrid = D3dbspLightingCodec.DecodeLightGrid(
            RequireLump(d3dbsp, D3dbspLumpType.LightGridHeader).Data,
            GetOptionalLumpData(d3dbsp, D3dbspLumpType.LightGridRows),
            GetOptionalLumpData(d3dbsp, D3dbspLumpType.LightGridEntries),
            GetOptionalLumpData(d3dbsp, D3dbspLumpType.LightGridColors),
            linkedGfxWorld.LightGrid.SunPrimaryLightIndex,
            d3dbsp.Lumps.Any(lump => lump.Type == D3dbspLumpType.LightRegions));
        IReadOnlyList<GfxLightRegion> decodedLightRegions =
            D3dbspLightingCodec.DecodeLightRegions(
                RequireLump(d3dbsp, D3dbspLumpType.LightRegions).Data,
                GetOptionalLumpData(d3dbsp, D3dbspLumpType.LightRegionHulls),
                GetOptionalLumpData(d3dbsp, D3dbspLumpType.LightRegionAxes));

        GfxLightGrid linkedLightGrid = linkedGfxWorld.LightGrid;
        bool lightGridMatch =
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
            D3dbspLightingCodec.EncodeLightRegions(decodedLightRegions).AsSpan().SequenceEqual(
                D3dbspLightingCodec.EncodeLightRegions(linkedGfxWorld.LightRegions)) &&
            D3dbspLightingCodec.EncodeLightRegionHulls(decodedLightRegions).AsSpan().SequenceEqual(
                D3dbspLightingCodec.EncodeLightRegionHulls(linkedGfxWorld.LightRegions)) &&
            D3dbspLightingCodec.EncodeLightRegionAxes(decodedLightRegions).AsSpan().SequenceEqual(
                D3dbspLightingCodec.EncodeLightRegionAxes(linkedGfxWorld.LightRegions));

        Console.WriteLine($"gfx-light-grid.asset-graph-match: {lightGridMatch}");
        Console.WriteLine($"gfx-light-regions.asset-graph-match: {lightRegionsMatch}");
    }

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
            D3dbspLightingCodec.EncodeLightRegions(gfxWorld.LightRegions));
        WriteLumpComparison(
            d3dbsp,
            D3dbspLumpType.LightRegionHulls,
            D3dbspLightingCodec.EncodeLightRegionHulls(gfxWorld.LightRegions));
        WriteLumpComparison(
            d3dbsp,
            D3dbspLumpType.LightRegionAxes,
            D3dbspLightingCodec.EncodeLightRegionAxes(gfxWorld.LightRegions));
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
