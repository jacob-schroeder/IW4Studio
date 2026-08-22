using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.ComWorld;
using IW4.Assets.Assets.FxMap;
using IW4.Assets.Assets.GameMap;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.MapEnts;
using IW4.Studio.Documents;

namespace IW4Map;

internal static class FastFileInspector
{
    public static void Inspect(string input)
    {
        string path = Path.GetFullPath(input);
        using FastFileWorkspace workspace = Open(path);

        Console.WriteLine($"file: {path}");
        Console.WriteLine($"profile: {workspace.ZonePlanProfileName}");
        Console.WriteLine($"target-assets: {workspace.LoadedZone.LoadedAssets.Count}");
        Console.WriteLine($"loaded-zones: {workspace.LoadedZones.Count}");

        foreach (var group in workspace.LoadedZone.LoadedAssets
                     .Select(result => result.Asset)
                     .Where(asset => asset is not null)
                     .GroupBy(asset => asset!.SerializedAssetType)
                     .OrderBy(group => group.Key))
        {
            Console.WriteLine($"asset-count.{group.Key}: {group.Count()}");
        }

        Console.WriteLine();
        WriteGfxWorld(GetSingle<GfxWorldAsset>(workspace));
        ClipMapAsset? clipMap = GetSingle<ClipMapAsset>(workspace);
        WriteClipMap(clipMap);
        WriteComWorld(GetSingle<ComWorldAsset>(workspace));
        WriteMapEnts(GetSingle<MapEntsAsset>(workspace) ?? clipMap?.MapEnts);
        WriteFxWorld(GetSingle<FxWorldAsset>(workspace));
        WriteGameWorld(GetSingle<GameWorldMpAsset>(workspace));
    }

    internal static FastFileWorkspace Open(string input)
    {
        string path = Path.GetFullPath(input);
        string profile = FastFileOpenProfiles.ResolveForTarget(path);
        return new FastFileDocumentService().Open(
            new FastFileDocumentOpenRequest(path, new ZonePlan(profile)));
    }

    internal static TAsset? GetSingle<TAsset>(FastFileWorkspace workspace) where TAsset : class =>
        workspace.LoadedZone.LoadedAssets
            .Select(result => result.Asset)
            .OfType<TAsset>()
            .SingleOrDefault();

    private static void WriteGfxWorld(GfxWorldAsset? world)
    {
        if (world is null)
        {
            Console.WriteLine("gfx-map: missing");
            return;
        }

        Console.WriteLine($"gfx-map.name: {world.Name}");
        Console.WriteLine($"gfx-map.base-name: {world.BaseName}");
        Console.WriteLine($"gfx-map.checksum: 0x{world.Checksum:X8}");
        Console.WriteLine($"gfx-map.map-vertex-checksum: 0x{world.MapVertexChecksum:X8}");
        Console.WriteLine($"gfx-map.planes: {world.PlaneCount} declared, {world.DpvsPlanes.Planes.Count} materialized");
        Console.WriteLine($"gfx-map.nodes: {world.NodeCount} declared, {world.DpvsPlanes.Nodes.Count} materialized");
        Console.WriteLine($"gfx-map.surfaces: {world.SurfaceCount} declared, {world.Dpvs.Surfaces.Count} materialized");
        Console.WriteLine($"gfx-map.cells: {world.DpvsPlanes.CellCount} declared, {world.Cells.Count} materialized");
        Console.WriteLine($"gfx-map.models: {world.ModelCount} declared, {world.Models.Count} materialized");
        Console.WriteLine($"gfx-map.primary-lights: {world.PrimaryLightCount}");
        Console.WriteLine($"gfx-map.light-regions: {world.LightRegions.Count}");
        Console.WriteLine($"gfx-map.reflection-probes: {world.WorldDraw.ReflectionProbeCount} declared, {world.WorldDraw.ReflectionProbeOrigins.Count} origins");
        Console.WriteLine($"gfx-map.lightmaps: {world.WorldDraw.LightmapCount} declared, {world.WorldDraw.Lightmaps.Count} materialized");
        Console.WriteLine($"gfx-map.vertices: {world.WorldDraw.VertexCount} declared, {world.WorldDraw.VertexData.PackedVertices.Count} packed bytes");
        Console.WriteLine($"gfx-map.vertex-layer: {world.WorldDraw.VertexLayerDataSize} declared, {world.WorldDraw.VertexLayerData.PackedLayerData.Count} packed bytes");
        Console.WriteLine($"gfx-map.indices: {world.WorldDraw.IndexCount} declared, {world.WorldDraw.Indices.Count} materialized");
        Console.WriteLine($"gfx-map.light-grid-row-starts: {world.LightGrid.RowDataStart.Count}");
        Console.WriteLine($"gfx-map.light-grid-rows: {world.LightGrid.RawRowDataSize} declared, {world.LightGrid.RawRowData.Count} materialized bytes");
        Console.WriteLine($"gfx-map.light-grid-entries: {world.LightGrid.EntryCount} declared, {world.LightGrid.Entries.Count} materialized");
        Console.WriteLine($"gfx-map.light-grid-colors: {world.LightGrid.ColorCount} declared, {world.LightGrid.Colors.Count} materialized");
    }

    private static void WriteClipMap(ClipMapAsset? clipMap)
    {
        if (clipMap is null)
        {
            Console.WriteLine("col-map: missing");
            return;
        }

        Console.WriteLine($"col-map.name: {clipMap.Name}");
        Console.WriteLine($"col-map.checksum: 0x{clipMap.Checksum:X8}");
        Console.WriteLine($"col-map.planes: {clipMap.PlaneCount} declared, {clipMap.Planes.Count} materialized");
        Console.WriteLine($"col-map.static-models: {clipMap.NumStaticModels} declared, {clipMap.StaticModelList.Count} materialized");
        Console.WriteLine($"col-map.materials: {clipMap.NumMaterials} declared, {clipMap.Materials.Count} materialized");
        Console.WriteLine($"col-map.brush-sides: {clipMap.NumBrushSides} declared, {clipMap.BrushSides.Count} materialized");
        Console.WriteLine($"col-map.brush-edges: {clipMap.NumBrushEdges} declared, {clipMap.BrushEdges.Count} materialized");
        Console.WriteLine($"col-map.nodes: {clipMap.NumNodes} declared, {clipMap.Nodes.Count} materialized");
        Console.WriteLine($"col-map.leafs: {clipMap.NumLeafs} declared, {clipMap.Leafs.Count} materialized");
        Console.WriteLine($"col-map.leaf-brushes: {clipMap.NumLeafBrushes} declared, {clipMap.LeafBrushes.Count} materialized");
        Console.WriteLine($"col-map.leaf-surfaces: {clipMap.NumLeafSurfaces} declared, {clipMap.LeafSurfaces.Count} materialized");
        Console.WriteLine($"col-map.collision-verts: {clipMap.VertCount} declared, {clipMap.Verts.Count} materialized");
        Console.WriteLine($"col-map.collision-tris: {clipMap.TriCount} declared, {clipMap.TriIndices.Count / 3} materialized");
        Console.WriteLine($"col-map.collision-borders: {clipMap.BorderCount} declared, {clipMap.Borders.Count} materialized");
        Console.WriteLine($"col-map.collision-partitions: {clipMap.PartitionCount} declared, {clipMap.Partitions.Count} materialized");
        Console.WriteLine($"col-map.collision-aabbs: {clipMap.AabbTreeCount} declared, {clipMap.AabbTrees.Count} materialized");
        Console.WriteLine($"col-map.models: {clipMap.NumSubModels} declared, {clipMap.CModels.Count} materialized");
        Console.WriteLine($"col-map.brushes: {clipMap.NumBrushes} declared, {clipMap.Brushes.Count} materialized");
        Console.WriteLine($"col-map.dynamic-entities: {string.Join(",", clipMap.DynEntCount)}");
    }

    private static void WriteComWorld(ComWorldAsset? world)
    {
        if (world is null)
        {
            Console.WriteLine("com-map: missing");
            return;
        }

        Console.WriteLine($"com-map.name: {world.Name}");
        Console.WriteLine($"com-map.primary-lights: {world.PrimaryLightCount} declared, {world.PrimaryLights.Count} materialized");
    }

    private static void WriteMapEnts(MapEntsAsset? mapEnts)
    {
        if (mapEnts is null)
        {
            Console.WriteLine("map-ents: missing");
            return;
        }

        Console.WriteLine($"map-ents.name: {mapEnts.Name}");
        Console.WriteLine($"map-ents.entity-bytes: {mapEnts.NumEntityChars} declared, {mapEnts.EntityStringBytes.Count} materialized");
        Console.WriteLine($"map-ents.trigger-models: {mapEnts.Trigger.Count} declared, {mapEnts.Trigger.Models.Count} materialized");
        Console.WriteLine($"map-ents.trigger-hulls: {mapEnts.Trigger.HullCount} declared, {mapEnts.Trigger.Hulls.Count} materialized");
        Console.WriteLine($"map-ents.trigger-slabs: {mapEnts.Trigger.SlabCount} declared, {mapEnts.Trigger.Slabs.Count} materialized");
        Console.WriteLine($"map-ents.stages: {mapEnts.StageCount} declared, {mapEnts.Stages.Count} materialized");
    }

    private static void WriteFxWorld(FxWorldAsset? world)
    {
        if (world is null)
        {
            Console.WriteLine("fx-map: missing");
            return;
        }

        FxGlassSystem glass = world.GlassSystem;
        Console.WriteLine($"fx-map.name: {world.Name}");
        Console.WriteLine($"fx-map.glass-defs: {glass.DefCount} declared, {glass.Defs.Count} materialized");
        Console.WriteLine($"fx-map.glass-init-pieces: {glass.InitPieceCount} declared, {glass.InitPieceStates.Count} materialized");
        Console.WriteLine($"fx-map.glass-init-geometry: {glass.InitGeoDataCount} declared, {glass.InitGeoData.Count} materialized");
        Console.WriteLine($"fx-map.glass-cells: {glass.CellCount}");
    }

    private static void WriteGameWorld(GameWorldMpAsset? world)
    {
        if (world is null)
        {
            Console.WriteLine("game-map-mp: missing");
            return;
        }

        Console.WriteLine($"game-map-mp.name: {world.Name}");
        Console.WriteLine($"game-map-mp.glass-pieces: {world.GlassData?.PieceCount ?? 0} declared, {world.GlassData?.GlassPieces.Count ?? 0} materialized");
        Console.WriteLine($"game-map-mp.glass-names: {world.GlassData?.GlassNameCount ?? 0} declared, {world.GlassData?.GlassNames.Count ?? 0} materialized");
    }
}
