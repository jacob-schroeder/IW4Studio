using IW4.Assets.Assets;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.ComWorld;
using IW4.Assets.Assets.FxMap;
using IW4.Assets.Assets.GameMap;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.MapEnts;
using IW4.Assets.D3dbsp;
using IW4.FastFiles.Zone;

namespace IW4.Unlinker.D3dbsp;

public static class D3dbspUnlinker
{
    public static D3dbspFile Unlink(IEnumerable<BaseAsset> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        BaseAsset[] detachedAssets = assets.ToArray();
        GfxWorldAsset gfx = RequireSingleAsset<GfxWorldAsset>(
            detachedAssets,
            XAssetType.GfxMap,
            "GfxWorld");
        ClipMapAsset clip = RequireSingleAsset<ClipMapAsset>(
            detachedAssets,
            XAssetType.ColMapMp,
            "ClipMap");
        ComWorldAsset com = RequireSingleAsset<ComWorldAsset>(
            detachedAssets,
            XAssetType.ComMap,
            "ComWorld");
        MapEntsAsset ents = RequireMapEnts(detachedAssets, clip);
        FxWorldAsset fx = RequireSingleAsset<FxWorldAsset>(
            detachedAssets,
            XAssetType.FxMap,
            "FxWorld");
        GameWorldMpAsset game = RequireSingleAsset<GameWorldMpAsset>(
            detachedAssets,
            XAssetType.GameMapMp,
            "multiplayer GameWorld");

        RequireMatchingNames(gfx, clip, com, ents, fx, game);
        ValidateCounts(gfx, clip, com, ents);
        ValidateCanonicalCollisionGraph(clip, ents);
        ValidateCanonicalRenderGraph(gfx, clip, com);
        ValidateEmptyDerivedGraphs(fx, game);

        D3dbspTriggerCollisionExport collisionExport =
            D3dbspMapEntsCodec.CreateTriggerCollisionExport(ents, clip);
        (byte[] brushSides, byte[] brushes) =
            D3dbspCollisionCodec.EncodeBrushGraph(collisionExport);
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
        (byte[] lightBytes, IReadOnlyList<D3dbspLightmapTile> lightmapTiles) =
            D3dbspImageCodec.EncodeLightBytes(gfx.WorldDraw.Lightmaps);
        byte[] reflectionProbes = D3dbspImageCodec.EncodeReflectionProbes(
            gfx.WorldDraw.ReflectionProbeImages,
            gfx.WorldDraw.ReflectionProbeOrigins);
        (
            byte[] renderTriangles,
            byte[] renderVertices,
            byte[] renderIndices) = D3dbspGfxCodec.EncodeUnlayeredGeometry(
                gfx,
                clip,
                lightmapTiles);

        var lumps = new List<(D3dbspLumpType Type, byte[] Data)>
        {
            (D3dbspLumpType.Materials, D3dbspCollisionCodec.EncodeMaterials(collisionExport.Materials))
        };
        AddIfNotEmpty(lumps, D3dbspLumpType.LightGridEntries, lightGridEntries);
        AddIfNotEmpty(lumps, D3dbspLumpType.LightGridColors, lightGridColors);
        AddIfNotEmpty(lumps, D3dbspLumpType.LightBytes, lightBytes);
        lumps.Add((D3dbspLumpType.Planes, D3dbspCollisionCodec.EncodePlanes(collisionExport.Planes)));
        lumps.Add((D3dbspLumpType.BrushSides, brushSides));
        lumps.Add((
            D3dbspLumpType.BrushSideEdgeCounts,
            D3dbspCollisionCodec.EncodeBrushSideEdgeCounts(collisionExport.Brushes)));
        AddIfNotEmpty(
            lumps,
            D3dbspLumpType.BrushEdges,
            D3dbspCollisionCodec.EncodeBrushEdges(collisionExport.BrushEdges));
        lumps.Add((D3dbspLumpType.Brushes, brushes));
        lumps.Add((
            D3dbspLumpType.UnlayeredAabbTrees,
            D3dbspGfxCodec.EncodeCanonicalUnlayeredAabbTree(gfx)));
        lumps.Add((D3dbspLumpType.Cells, D3dbspGfxCodec.EncodeCanonicalCell(gfx)));
        lumps.Add((D3dbspLumpType.Nodes, D3dbspCollisionCodec.EncodeNodes(clip.Nodes, collisionExport.Planes)));
        lumps.Add((D3dbspLumpType.Leafs, leafs));
        lumps.Add((
            D3dbspLumpType.Visibility,
            D3dbspCollisionCodec.EncodeCanonicalVisibility()));
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
        lumps.Add((D3dbspLumpType.Models, D3dbspGfxCodec.EncodeModels(gfx, clip, collisionExport)));
        lumps.Add((
            D3dbspLumpType.Entities,
            D3dbspMapEntsCodec.EncodeEntityString(
                ents,
                clip,
                gfx,
                collisionExport.CollisionModelsByTrigger)));
        AddIfNotEmpty(
            lumps,
            D3dbspLumpType.ReflectionProbes,
            reflectionProbes);
        lumps.Add((D3dbspLumpType.PrimaryLights, D3dbspPrimaryLightCodec.Encode(com.PrimaryLights)));
        lumps.Add((D3dbspLumpType.LightGridHeader, D3dbspLightingCodec.EncodeLightGridHeader(gfx.LightGrid)));
        AddIfNotEmpty(lumps, D3dbspLumpType.LightGridRows, lightGridRows);
        lumps.Add((
            D3dbspLumpType.UnlayeredTriangles,
            renderTriangles));
        lumps.Add((
            D3dbspLumpType.UnlayeredDrawVerts,
            renderVertices));
        lumps.Add((
            D3dbspLumpType.UnlayeredDrawIndices,
            renderIndices));
        if (hasLightRegions)
            lumps.Add((D3dbspLumpType.LightRegions, lightRegions));
        AddIfNotEmpty(lumps, D3dbspLumpType.LightRegionHulls, lightRegionHulls);
        AddIfNotEmpty(lumps, D3dbspLumpType.LightRegionAxes, lightRegionAxes);

        return D3dbspFile.Create(lumps);
    }

    private static TAsset RequireSingleAsset<TAsset>(
        IReadOnlyList<BaseAsset> assets,
        XAssetType assetType,
        string description)
        where TAsset : BaseAsset
    {
        BaseAsset[] matches = assets
            .Where(asset => asset is not null && asset.SerializedAssetType == assetType)
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidDataException(
                $"The asset collection does not contain exactly one {description} asset.");
        }

        return matches[0] as TAsset ??
            throw new InvalidDataException(
                $"The {assetType} asset is not a {typeof(TAsset).Name}.");
    }

    private static MapEntsAsset RequireMapEnts(
        IReadOnlyList<BaseAsset> assets,
        ClipMapAsset clip)
    {
        BaseAsset[] matches = assets
            .Where(asset => asset is not null && asset.SerializedAssetType == XAssetType.MapEnts)
            .ToArray();
        if (matches.Length > 1)
        {
            throw new InvalidDataException(
                "The asset collection contains more than one MapEnts asset.");
        }
        if (matches.Length == 0)
        {
            return clip.MapEnts ??
                throw new InvalidDataException("The asset collection does not contain a MapEnts asset.");
        }

        return matches[0] as MapEntsAsset ??
            throw new InvalidDataException(
                $"The {XAssetType.MapEnts} asset is not a {nameof(MapEntsAsset)}.");
    }

    private static void RequireMatchingNames(
        GfxWorldAsset gfx,
        ClipMapAsset clip,
        ComWorldAsset com,
        MapEntsAsset ents,
        FxWorldAsset fx,
        GameWorldMpAsset game)
    {
        string name = gfx.Name ?? throw new InvalidDataException("The GfxWorld asset has no name.");
        if (!D3dbspAssetTypeFacts.IsOwnedD3dbspName(name))
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
            !MapEntsHaveSameSerializedState(clip.MapEnts, ents))
        {
            throw new NotSupportedException(
                "The ClipMap MapEnts reference does not match the loaded MapEnts asset.");
        }
        if (clip.Checksum != gfx.Checksum)
            throw new NotSupportedException("The ClipMap and GfxWorld checksums differ.");
    }

    private static bool MapEntsHaveSameSerializedState(
        MapEntsAsset left,
        MapEntsAsset right) =>
        string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
        left.NumEntityChars == right.NumEntityChars &&
        left.EntityStringBytes.SequenceEqual(right.EntityStringBytes) &&
        TriggersHaveSameSerializedState(left.Trigger, right.Trigger) &&
        left.StageCount == right.StageCount &&
        left.Stages.Count == right.Stages.Count &&
        left.Stages.Zip(right.Stages).All(pair =>
            string.Equals(pair.First.StageName, pair.Second.StageName, StringComparison.Ordinal) &&
            SameVec3Bits(pair.First.Origin, pair.Second.Origin) &&
            pair.First.TriggerIndex == pair.Second.TriggerIndex &&
            pair.First.SunPrimaryLightIndex == pair.Second.SunPrimaryLightIndex &&
            pair.First.Pad13 == pair.Second.Pad13) &&
        left.Pad29To2B.SequenceEqual(right.Pad29To2B);

    private static bool TriggersHaveSameSerializedState(
        MapTriggers left,
        MapTriggers right) =>
        left.Count == right.Count &&
        left.Models.Count == right.Models.Count &&
        left.Models.Zip(right.Models).All(pair =>
            pair.First.Contents == pair.Second.Contents &&
            pair.First.HullCount == pair.Second.HullCount &&
            pair.First.FirstHull == pair.Second.FirstHull) &&
        left.HullCount == right.HullCount &&
        left.Hulls.Count == right.Hulls.Count &&
        left.Hulls.Zip(right.Hulls).All(pair =>
            SameBoundsBits(pair.First.Bounds, pair.Second.Bounds) &&
            pair.First.Contents == pair.Second.Contents &&
            pair.First.SlabCount == pair.Second.SlabCount &&
            pair.First.FirstSlab == pair.Second.FirstSlab) &&
        left.SlabCount == right.SlabCount &&
        left.Slabs.Count == right.Slabs.Count &&
        left.Slabs.Zip(right.Slabs).All(pair =>
            SameVec3Bits(pair.First.Dir, pair.Second.Dir) &&
            SameSingleBits(pair.First.MidPoint, pair.Second.MidPoint) &&
            SameSingleBits(pair.First.HalfSize, pair.Second.HalfSize));

    private static bool SameBoundsBits(
        IW4.Assets.Math.Bounds left,
        IW4.Assets.Math.Bounds right) =>
        SameVec3Bits(left.MidPoint, right.MidPoint) &&
        SameVec3Bits(left.HalfSize, right.HalfSize);

    private static bool SameVec3Bits(
        IW4.Assets.Math.Vec3 left,
        IW4.Assets.Math.Vec3 right) =>
        SameSingleBits(left.X, right.X) &&
        SameSingleBits(left.Y, right.Y) &&
        SameSingleBits(left.Z, right.Z);

    private static bool SameSingleBits(float left, float right) =>
        BitConverter.SingleToInt32Bits(left) ==
        BitConverter.SingleToInt32Bits(right);

    private static void ValidateCounts(
        GfxWorldAsset gfx,
        ClipMapAsset clip,
        ComWorldAsset com,
        MapEntsAsset ents)
    {
        RequireCount(clip.PlaneCount, clip.Planes.Count, "collision planes");
        RequireCount(clip.NumStaticModels, clip.StaticModelList.Count, "collision static models");
        RequireCount(clip.SModelNodeCount, clip.SModelNodes.Count, "static-model AABB nodes");
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
        RequireCount(
            gfx.WorldDraw.ReflectionProbeCount,
            gfx.WorldDraw.ReflectionProbeImages.Count,
            "render reflection-probe images");
        RequireCount(
            gfx.WorldDraw.ReflectionProbeCount,
            gfx.WorldDraw.ReflectionProbeOrigins.Count,
            "render reflection-probe origins");
        RequireCount(gfx.Dpvs.SModelCount, gfx.Dpvs.SModelInsts.Count, "render static-model instances");
        RequireCount(gfx.Dpvs.SModelCount, gfx.Dpvs.SModelDrawInsts.Count, "render static-model draw instances");
        RequireCount(gfx.WorldDraw.IndexCount, gfx.WorldDraw.Indices.Count, "render indices");
        RequireCount(ents.NumEntityChars, ents.EntityStringBytes.Count, "entity bytes");
        RequireCount(ents.StageCount, ents.Stages.Count, "map stages");
    }

    private static void ValidateCanonicalCollisionGraph(
        ClipMapAsset clip,
        MapEntsAsset ents)
    {
        if (clip.DynEntCount.Count != 2 || clip.DynEntCount.Any(count => count != 0) ||
            clip.DynEntDefList.Any(list => list.Count != 0) ||
            clip.DynEntPoseList.Any(list => list.Count != 0) ||
            clip.DynEntClientList.Any(list => list.Count != 0) ||
            clip.DynEntCollList.Any(list => list.Count != 0))
        {
            throw new NotSupportedException(
                "Strict d3dbsp encoding does not support dynamic entities.");
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
        if (gfx.DpvsDyn.DynEntClientCount.Any(value => value != 0) ||
            gfx.DpvsDyn.DynEntClientWordCount.Any(value => value != 0) ||
            gfx.DpvsDyn.DynEntCellBits.Any(bits => bits.Count != 0) ||
            gfx.DpvsDyn.DynEntVisData.Any(bits => bits.Count != 0))
        {
            throw new NotSupportedException(
                "Strict d3dbsp encoding does not support dynamic-entity visibility data.");
        }
        GfxWorldDraw draw = gfx.WorldDraw;
        if (draw.ReflectionProbeCount == 0 ||
            draw.ReflectionProbeCount > byte.MaxValue ||
            draw.ReflectionProbeImages.Any(image => image is null))
        {
            throw new InvalidDataException(
                "The render world must contain a byte-sized reflection-probe table including the default probe.");
        }
        for (int index = 0; index < gfx.Dpvs.Surfaces.Count; index++)
        {
            GfxSurface surface = gfx.Dpvs.Surfaces[index] ??
                throw new InvalidDataException($"Render surface row {index} is null.");
            if (surface.LightmapIndex != 0x1f &&
                surface.LightmapIndex >= draw.LightmapCount)
            {
                throw new InvalidDataException(
                    $"Render surface row {index} references lightmap {surface.LightmapIndex}; the table has {draw.LightmapCount} rows.");
            }
            if (surface.ReflectionProbeIndex >= draw.ReflectionProbeCount)
            {
                throw new InvalidDataException(
                    $"Render surface row {index} references reflection probe {surface.ReflectionProbeIndex}; the table has {draw.ReflectionProbeCount} rows.");
            }
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

    private static void RequireCount(long declared, int actual, string description)
    {
        if (declared != actual)
        {
            throw new InvalidDataException(
                $"The {description} table declares {declared} rows but materializes {actual}.");
        }
    }
}
