using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.ComWorld;
using IW4.Assets.Assets.FxMap;
using IW4.Assets.Assets.GameMap;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.MapEnts;
using IW4.Studio.Documents;

namespace IW4Map;

internal static class FastFileD3dbspEncoder
{
    public static D3dbspFile Encode(FastFileWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        GfxWorldAsset gfx = FastFileInspector.GetSingle<GfxWorldAsset>(workspace) ??
            throw new InvalidDataException("The fastfile does not contain exactly one GfxWorld asset.");
        ClipMapAsset clip = FastFileInspector.GetSingle<ClipMapAsset>(workspace) ??
            throw new InvalidDataException("The fastfile does not contain exactly one ClipMap asset.");
        ComWorldAsset com = FastFileInspector.GetSingle<ComWorldAsset>(workspace) ??
            throw new InvalidDataException("The fastfile does not contain exactly one ComWorld asset.");
        MapEntsAsset ents = FastFileInspector.GetSingle<MapEntsAsset>(workspace) ??
            clip.MapEnts ??
            throw new InvalidDataException("The fastfile does not contain a MapEnts asset.");
        FxWorldAsset fx = FastFileInspector.GetSingle<FxWorldAsset>(workspace) ??
            throw new InvalidDataException("The fastfile does not contain exactly one FxWorld asset.");
        GameWorldMpAsset game = FastFileInspector.GetSingle<GameWorldMpAsset>(workspace) ??
            throw new InvalidDataException("The fastfile does not contain exactly one multiplayer GameWorld asset.");

        string assetName = RequireMatchingNames(gfx, clip, com, ents, fx, game);
        ValidateCounts(gfx, clip, com, ents);
        ValidateCanonicalCollisionGraph(clip, ents, com);
        ValidateCanonicalRenderGraph(gfx, clip, com);
        ValidateEmptyDerivedGraphs(fx, game);

        (byte[] brushSides, byte[] brushes) =
            D3dbspCollisionCodec.EncodeBrushGraph(clip);
        (byte[] leafs, byte[] leafBrushes) =
            D3dbspCollisionCodec.EncodeCanonicalLeafGraph(clip);
        byte[] lightGridEntries = D3dbspLightingCodec.EncodeLightGridEntries(gfx.LightGrid);
        byte[] lightGridColors = D3dbspLightingCodec.EncodeLightGridColors(
            gfx.LightGrid,
            omitLinkerGeneratedDefault: true);
        byte[] lightGridRows = D3dbspLightingCodec.EncodeLightGridRows(gfx.LightGrid);
        bool hasLightRegions = gfx.LightGrid.HasLightRegions;
        byte[] lightRegions = D3dbspLightingCodec.EncodeLightRegions(
            gfx.LightRegions,
            hasLightRegions);
        byte[] lightRegionHulls = D3dbspLightingCodec.EncodeLightRegionHulls(
            gfx.LightRegions,
            hasLightRegions);
        byte[] lightRegionAxes = D3dbspLightingCodec.EncodeLightRegionAxes(
            gfx.LightRegions,
            hasLightRegions);

        var lumps = new List<(D3dbspLumpType Type, byte[] Data)>
        {
            (D3dbspLumpType.Materials, D3dbspCollisionCodec.EncodeMaterials(clip.Materials))
        };
        AddIfNotEmpty(lumps, D3dbspLumpType.LightGridEntries, lightGridEntries);
        AddIfNotEmpty(lumps, D3dbspLumpType.LightGridColors, lightGridColors);
        lumps.Add((D3dbspLumpType.Planes, D3dbspCollisionCodec.EncodePlanes(clip.Planes)));
        lumps.Add((D3dbspLumpType.BrushSides, brushSides));
        lumps.Add((
            D3dbspLumpType.BrushSideEdgeCounts,
            D3dbspCollisionCodec.EncodeBrushSideEdgeCounts(clip.Brushes)));
        AddIfNotEmpty(
            lumps,
            D3dbspLumpType.BrushEdges,
            D3dbspCollisionCodec.EncodeBrushEdges(clip.BrushEdges));
        lumps.Add((D3dbspLumpType.Brushes, brushes));
        lumps.Add((
            D3dbspLumpType.UnlayeredAabbTrees,
            D3dbspGfxCodec.EncodeCanonicalUnlayeredAabbTree(gfx)));
        lumps.Add((D3dbspLumpType.Cells, D3dbspGfxCodec.EncodeCanonicalCell(gfx)));
        lumps.Add((D3dbspLumpType.Nodes, D3dbspCollisionCodec.EncodeNodes(clip.Nodes, clip.Planes)));
        lumps.Add((D3dbspLumpType.Leafs, leafs));
        AddIfNotEmpty(lumps, D3dbspLumpType.LeafBrushes, leafBrushes);
        AddIfNotEmpty(
            lumps,
            D3dbspLumpType.LeafSurfaces,
            D3dbspCollisionCodec.EncodeLeafSurfaces(clip.LeafSurfaces));
        AddIfNotEmpty(
            lumps,
            D3dbspLumpType.CollisionVerts,
            D3dbspCollisionCodec.EncodeCollisionVerts(clip.Verts));
        AddIfNotEmpty(
            lumps,
            D3dbspLumpType.CollisionTris,
            D3dbspCollisionCodec.EncodeCollisionTris(clip.TriIndices));
        AddIfNotEmpty(
            lumps,
            D3dbspLumpType.CollisionEdgeWalkable,
            D3dbspCollisionCodec.EncodeCollisionEdgeWalkable(clip.TriEdgeIsWalkable));
        AddIfNotEmpty(
            lumps,
            D3dbspLumpType.CollisionBorders,
            D3dbspCollisionCodec.EncodeCollisionBorders(clip.Borders));
        AddIfNotEmpty(
            lumps,
            D3dbspLumpType.CollisionPartitions,
            D3dbspCollisionCodec.EncodeCollisionPartitions(
                clip.Partitions,
                clip.BorderCount,
                clip.TriCount));
        AddIfNotEmpty(
            lumps,
            D3dbspLumpType.CollisionAabbs,
            D3dbspCollisionCodec.EncodeCollisionAabbs(clip.AabbTrees));
        lumps.Add((D3dbspLumpType.Models, D3dbspGfxCodec.EncodeModels(gfx, clip)));
        lumps.Add((D3dbspLumpType.Entities, ents.EntityStringBytes.ToArray()));
        lumps.Add((D3dbspLumpType.PrimaryLights, D3dbspPrimaryLightCodec.Encode(com.PrimaryLights)));
        lumps.Add((D3dbspLumpType.LightGridHeader, D3dbspLightingCodec.EncodeLightGridHeader(gfx.LightGrid)));
        AddIfNotEmpty(lumps, D3dbspLumpType.LightGridRows, lightGridRows);
        lumps.Add((
            D3dbspLumpType.UnlayeredTriangles,
            D3dbspGfxCodec.EncodeUnlayeredTriangles(gfx, clip)));
        lumps.Add((
            D3dbspLumpType.UnlayeredDrawVerts,
            D3dbspGfxCodec.EncodeUnlayeredDrawVerts(gfx)));
        lumps.Add((
            D3dbspLumpType.UnlayeredDrawIndices,
            D3dbspGfxCodec.EncodeUnlayeredDrawIndices(gfx)));
        if (hasLightRegions)
            lumps.Add((D3dbspLumpType.LightRegions, lightRegions));
        AddIfNotEmpty(lumps, D3dbspLumpType.LightRegionHulls, lightRegionHulls);
        AddIfNotEmpty(lumps, D3dbspLumpType.LightRegionAxes, lightRegionAxes);

        Console.WriteLine($"map-asset: {assetName}");
        Console.WriteLine("encoding-profile: canonical-one-cell-fullbright-v22");
        Console.WriteLine("canonicalized: linked render order, leaf-brush slices, brush material selectors, node tails, partition stamps");
        Console.WriteLine("omitted: light bytes, layered geometry, authored visibility, cull groups, portals, paths, raw reflection probes");
        return D3dbspFile.Create(lumps);
    }

    private static string RequireMatchingNames(
        GfxWorldAsset gfx,
        ClipMapAsset clip,
        ComWorldAsset com,
        MapEntsAsset ents,
        FxWorldAsset fx,
        GameWorldMpAsset game)
    {
        string name = gfx.Name ?? throw new InvalidDataException("The GfxWorld asset has no name.");
        if (name.Length == 0 || name[0] == ',' || name.Contains('\0') ||
            !name.EndsWith(".d3dbsp", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                "The map roots must use one owned .d3dbsp wire name.");
        }

        foreach ((string label, string? candidate) in new[]
                 {
                     ("ClipMap", clip.Name),
                     ("ComWorld", com.Name),
                     ("MapEnts", ents.Name),
                     ("FxWorld", fx.Name),
                     ("GameWorld", game.Name)
                 })
        {
            if (!string.Equals(name, candidate, StringComparison.Ordinal))
            {
                throw new NotSupportedException(
                    $"The {label} root name '{candidate}' does not match GfxWorld name '{name}'.");
            }
        }
        if (clip.MapEnts is null ||
            !string.Equals(clip.MapEnts.Name, name, StringComparison.Ordinal) ||
            !clip.MapEnts.EntityStringBytes.SequenceEqual(ents.EntityStringBytes))
        {
            throw new NotSupportedException(
                "The ClipMap MapEnts reference does not match the loaded MapEnts asset.");
        }
        if (clip.Checksum != gfx.Checksum)
            throw new NotSupportedException("The ClipMap and GfxWorld checksums differ.");
        return name;
    }

    private static void ValidateCounts(
        GfxWorldAsset gfx,
        ClipMapAsset clip,
        ComWorldAsset com,
        MapEntsAsset ents)
    {
        RequireCount(clip.PlaneCount, clip.Planes.Count, "collision planes");
        RequireCount(clip.NumStaticModels, clip.StaticModelList.Count, "collision static models");
        RequireCount(clip.NumMaterials, clip.Materials.Count, "collision materials");
        RequireCount(clip.NumBrushSides, clip.BrushSides.Count, "collision brush sides");
        RequireCount(clip.NumBrushEdges, clip.BrushEdges.Count, "collision brush edges");
        RequireCount(clip.NumNodes, clip.Nodes.Count, "collision nodes");
        RequireCount(clip.NumLeafs, clip.Leafs.Count, "collision leafs");
        RequireCount(clip.LeafBrushNodesCount, clip.LeafBrushNodes.Count, "leaf-brush nodes");
        RequireCount(clip.NumLeafBrushes, clip.LeafBrushes.Count, "leaf brushes");
        RequireCount(clip.NumLeafSurfaces, clip.LeafSurfaces.Count, "leaf surfaces");
        RequireCount(clip.VertCount, clip.Verts.Count, "collision vertices");
        RequireCount(checked(clip.TriCount * 3L), clip.TriIndices.Count, "collision triangle indices");
        RequireCount(clip.BorderCount, clip.Borders.Count, "collision borders");
        RequireCount(clip.PartitionCount, clip.Partitions.Count, "collision partitions");
        RequireCount(clip.AabbTreeCount, clip.AabbTrees.Count, "collision AABBs");
        RequireCount(clip.NumSubModels, clip.CModels.Count, "collision models");
        RequireCount(clip.NumBrushes, clip.Brushes.Count, "collision brushes");
        RequireCount(com.PrimaryLightCount, com.PrimaryLights.Count, "primary lights");
        RequireCount(gfx.PrimaryLightCount, com.PrimaryLights.Count, "render primary lights");
        RequireCount(gfx.SurfaceCount, gfx.Dpvs.Surfaces.Count, "render surfaces");
        RequireCount(gfx.ModelCount, gfx.Models.Count, "render models");
        RequireCount(gfx.WorldDraw.LightmapCount, gfx.WorldDraw.Lightmaps.Count, "render lightmaps");
        RequireCount(gfx.WorldDraw.IndexCount, gfx.WorldDraw.Indices.Count, "render indices");
        RequireCount(ents.NumEntityChars, ents.EntityStringBytes.Count, "entity bytes");
        RequireCount(ents.StageCount, ents.Stages.Count, "map stages");
    }

    private static void ValidateCanonicalCollisionGraph(
        ClipMapAsset clip,
        MapEntsAsset ents,
        ComWorldAsset com)
    {
        bool hasNoStaticModelTree =
            clip.SModelNodeCount == 0 && clip.SModelNodes.Count == 0;
        if (clip.SModelNodeCount == 1 && clip.SModelNodes.Count == 1)
        {
            SModelAabbNode root = clip.SModelNodes[0];
            hasNoStaticModelTree =
                root.FirstChild == 0 && root.ChildCount == 0 &&
                root.Bounds.MidPoint.X == 0.0f &&
                root.Bounds.MidPoint.Y == 0.0f &&
                root.Bounds.MidPoint.Z == 0.0f &&
                root.Bounds.HalfSize.X == 0.0f &&
                root.Bounds.HalfSize.Y == 0.0f &&
                root.Bounds.HalfSize.Z == 0.0f;
        }

        if (clip.NumStaticModels != 0 || clip.StaticModelList.Count != 0 ||
            !hasNoStaticModelTree)
        {
            throw new NotSupportedException(
                "Strict d3dbsp encoding does not support collision static models.");
        }
        if (clip.DynEntCount.Count != 2 || clip.DynEntCount.Any(count => count != 0) ||
            clip.DynEntDefList.Any(list => list.Count != 0) ||
            clip.DynEntPoseList.Any(list => list.Count != 0) ||
            clip.DynEntClientList.Any(list => list.Count != 0) ||
            clip.DynEntCollList.Any(list => list.Count != 0))
        {
            throw new NotSupportedException(
                "Strict d3dbsp encoding does not support dynamic entities.");
        }
        MapTriggers trigger = ents.Trigger;
        if (trigger.Count != 0 || trigger.Models.Count != 0 ||
            trigger.HullCount != 0 || trigger.Hulls.Count != 0 ||
            trigger.SlabCount != 0 || trigger.Slabs.Count != 0)
        {
            throw new NotSupportedException(
                "Strict d3dbsp encoding does not support MapTriggers.");
        }
        int sunPrimaryLightIndex =
            D3dbspPrimaryLightCodec.GetLastSunPrimaryLightIndex(com.PrimaryLights);
        if (!D3dbspMapEntsCodec.IsCanonicalDefaultStage(
                ents.Stages,
                sunPrimaryLightIndex))
        {
            throw new NotSupportedException(
                "Strict d3dbsp encoding supports only the generated default map stage.");
        }

        if (ents.EntityStringBytes.Count == 0 || ents.EntityStringBytes[^1] != 0 ||
            ents.EntityStringBytes.Take(ents.EntityStringBytes.Count - 1).Any(value => value == 0))
        {
            throw new InvalidDataException(
                "The MapEnts entity payload must contain exactly one terminating NUL byte.");
        }
    }

    private static void ValidateCanonicalRenderGraph(
        GfxWorldAsset gfx,
        ClipMapAsset clip,
        ComWorldAsset com)
    {
        if (gfx.DpvsPlanes.CellCount != 1 || gfx.Cells.Count != 1 ||
            gfx.CellTreeCounts.Count != 1 || gfx.CellTrees.Count != 1)
        {
            throw new NotSupportedException(
                "Strict d3dbsp encoding requires exactly one render cell and cell tree.");
        }
        GfxCell cell = gfx.Cells[0];
        if (cell.PortalCount != 0 || cell.Portals.Count != 0)
            throw new NotSupportedException("Strict d3dbsp encoding does not support portals.");
        if (gfx.PlaneCount != 0 || gfx.DpvsPlanes.Planes.Count != 0 ||
            gfx.NodeCount != 1 || gfx.DpvsPlanes.Nodes.Count != 1 ||
            gfx.DpvsPlanes.Nodes[0] != 1)
        {
            throw new NotSupportedException(
                "Strict d3dbsp encoding requires the canonical one-cell DPVS root.");
        }
        if (gfx.SkyCount != 0 || gfx.Skies.Count != 0 ||
            gfx.MaterialMemoryCount != 0 || gfx.MaterialMemory.Count != 0 ||
            gfx.OutdoorImage is not null)
        {
            throw new NotSupportedException(
                "Strict d3dbsp encoding does not support skies, material-memory rows, or outdoor images.");
        }
        if (gfx.Dpvs.SModelCount != 0 || gfx.Dpvs.SModelInsts.Count != 0 ||
            gfx.Dpvs.SModelDrawInsts.Count != 0 ||
            gfx.SceneDynModels.Count != 0 || gfx.SceneDynBrushes.Count != 0)
        {
            throw new NotSupportedException(
                "Strict d3dbsp encoding does not support render static or dynamic models.");
        }
        if (gfx.DpvsDyn.DynEntClientCount.Any(value => value != 0) ||
            gfx.DpvsDyn.DynEntClientWordCount.Any(value => value != 0) ||
            gfx.DpvsDyn.DynEntCellBits.Any(bits => bits.Count != 0) ||
            gfx.DpvsDyn.DynEntVisData.Any(bits => bits.Count != 0))
        {
            throw new NotSupportedException(
                "Strict d3dbsp encoding does not support dynamic-entity visibility data.");
        }
        if (gfx.HeroOnlyLightCount != 0 || gfx.HeroOnlyLights.Count != 0 ||
            gfx.UmbraGateCount != 0 || gfx.UmbraGateData.Any(value => value != 0) ||
            gfx.UmbraGateData2.Any(value => value != 0))
        {
            throw new NotSupportedException(
                "Strict d3dbsp encoding does not support hero-only lights or Umbra gates " +
                $"(hero {gfx.HeroOnlyLightCount}/{gfx.HeroOnlyLights.Count}, " +
                $"Umbra {gfx.UmbraGateCount}/{gfx.UmbraGateData.Count}/{gfx.UmbraGateData2.Count}).");
        }

        GfxWorldDraw draw = gfx.WorldDraw;
        bool hasNoLightmaps = draw.LightmapCount == 0 && draw.Lightmaps.Count == 0;
        bool hasGeneratedFullbrightLightmap =
            draw.LightmapCount == 1 && draw.Lightmaps.Count == 1 &&
            draw.Lightmaps[0].Primary is not null &&
            draw.Lightmaps[0].Secondary is not null &&
            NormalizeReferenceName(draw.Lightmaps[0].Primary!.Name)
                .Equals(
                    D3dbspGfxCodec.FullbrightPrimaryLightmapImageName,
                    StringComparison.Ordinal) &&
            NormalizeReferenceName(draw.Lightmaps[0].Secondary!.Name)
                .Equals(
                    D3dbspGfxCodec.FullbrightSecondaryLightmapImageName,
                    StringComparison.Ordinal);
        if ((!hasNoLightmaps && !hasGeneratedFullbrightLightmap) ||
            draw.LightmapOverridePrimary is not null || draw.LightmapOverrideSecondary is not null)
        {
            throw new NotSupportedException(
                "Strict d3dbsp encoding supports only the generated $white fullbright lightmap.");
        }
        if (gfx.Dpvs.Surfaces.Any(surface =>
                surface.LightmapIndex != 0 && surface.LightmapIndex != 0x1f) ||
            (hasNoLightmaps && gfx.Dpvs.Surfaces.Any(surface => surface.LightmapIndex != 0x1f)))
        {
            throw new NotSupportedException(
                "Strict d3dbsp encoding supports only lightmap index 0 and no-lightmap index 31.");
        }
        if (draw.ReflectionProbeCount != 1 || draw.ReflectionProbeOrigins.Count != 1 ||
            draw.ReflectionProbeImages.Count != 1 || draw.ReflectionProbeImages[0] is null ||
            !NormalizeReferenceName(draw.ReflectionProbeImages[0]!.Name)
                .Equals("*reflection_probe0", StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                "Strict d3dbsp encoding requires the single generated default reflection probe.");
        }
        if (cell.ReflectionProbeCount != 1 || cell.ReflectionProbes.Count != 1 ||
            cell.ReflectionProbes[0] != 0)
        {
            throw new NotSupportedException(
                "Strict d3dbsp encoding requires the cell to reference only the default reflection probe.");
        }
        if (gfx.Models.Count != clip.CModels.Count || gfx.Models.Count == 0)
            throw new NotSupportedException("Render and collision model counts must match and be nonzero.");
        if (gfx.LightRegions.Count != com.PrimaryLights.Count)
            throw new InvalidDataException("The light-region and primary-light counts differ.");
        if (gfx.SunPrimaryLightIndex !=
            D3dbspPrimaryLightCodec.GetLastSunPrimaryLightIndex(com.PrimaryLights))
        {
            throw new NotSupportedException(
                "The render sun-primary-light index is not canonical for the primary-light table.");
        }
    }

    private static void ValidateEmptyDerivedGraphs(FxWorldAsset fx, GameWorldMpAsset game)
    {
        FxGlassSystem glass = fx.GlassSystem;
        if (glass.DefCount != 0 || glass.PieceLimit != 0 || glass.PieceWordCount != 0 ||
            glass.InitPieceCount != 0 || glass.CellCount != 0 || glass.ActivePieceCount != 0 ||
            glass.GeoDataLimit != 0 || glass.GeoDataCount != 0 || glass.InitGeoDataCount != 0 ||
            glass.Defs.Count != 0 || glass.PiecePlaces.Count != 0 || glass.PieceStates.Count != 0 ||
            glass.PieceDynamics.Count != 0 || glass.GeoData.Count != 0 || glass.IsInUse.Count != 0 ||
            glass.CellBits.Count != 0 || glass.VisData.Count != 0 || glass.LinkOrg.Count != 0 ||
            glass.HalfThickness.Count != 0 || glass.LightingHandles.Count != 0 ||
            glass.InitPieceStates.Count != 0 || glass.InitGeoData.Count != 0)
        {
            throw new NotSupportedException(
                "Strict d3dbsp encoding does not support FxWorld glass data.");
        }
        GGlassData gameGlass = game.GlassData ??
            throw new InvalidDataException("The multiplayer GameWorld has no glass-data header.");
        if (gameGlass.PieceCount != 0 || gameGlass.GlassPieces.Count != 0 ||
            gameGlass.GlassNameCount != 0 || gameGlass.GlassNames.Count != 0)
        {
            throw new NotSupportedException(
                "Strict d3dbsp encoding does not support GameWorld glass data.");
        }
    }

    private static void AddIfNotEmpty(
        ICollection<(D3dbspLumpType Type, byte[] Data)> lumps,
        D3dbspLumpType type,
        byte[] data)
    {
        if (data.Length != 0)
            lumps.Add((type, data));
    }

    private static string NormalizeReferenceName(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return string.Empty;
        return name[0] == ',' ? name[1..] : name;
    }

    private static void RequireCount(long declared, int actual, string description)
    {
        if (declared != actual)
        {
            throw new InvalidDataException(
                $"The {description} table declares {declared} rows but materializes {actual}.");
        }
    }
}
