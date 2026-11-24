using System.Buffers.Binary;
using IW4.Game.Assets.GfxMap;
using IW4.Game.Assets.Material;
using IW4.Game.Math;
using MapConverter.Game.IW4.Xbox.FastFiles;
using MapConverter.Game.IW4.Xbox.Geometry;

namespace MapConverter.Game.IW4.Xbox.World;

/// <summary>Writes the retail Xenon GfxWorld layout and its native loader stream.</summary>
internal static class XboxGfxWorldWriter
{
    private const uint Inline = uint.MaxValue;
    private const int Image = XboxAssetType.Image;
    private const int Material = XboxAssetType.Material;
    private const int XModel = XboxAssetType.XModel;

    internal static void Write(XboxZoneStream zone, GfxWorldAsset world)
    {
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(world);
        if (string.IsNullOrEmpty(world.Name))
            throw new InvalidDataException("GfxWorld requires a native name.");
        Require(world.Skies.Count, checked((int)world.SkyCount), "skies");
        Require(world.CellTrees.Count, world.DpvsPlanes.CellCount, "cell trees");
        Require(world.CellTreeCounts.Count, world.DpvsPlanes.CellCount, "cell tree counts");
        Require(world.Cells.Count, world.DpvsPlanes.CellCount, "cells");
        Require(world.Dpvs.Surfaces.Count, world.SurfaceCount, "surfaces");
        Require(world.Dpvs.SurfaceBounds.Count, world.SurfaceCount, "surface bounds");
        if (world.Dpvs.SortedSurfIndex.Count < world.Dpvs.StaticSurfaceCount)
            throw new InvalidDataException("GfxWorld sorted surface list is shorter than its static surface count.");
        Require(world.Dpvs.SModelInsts.Count, checked((int)world.Dpvs.SModelCount), "static model instances");
        Require(world.Dpvs.SModelDrawInsts.Count, checked((int)world.Dpvs.SModelCount), "static model draws");
        Require(world.Dpvs.VisibilityCounts.Count, 8, "static visibility counts");
        Require(world.DpvsDyn.DynEntClientWordCount.Count, 2, "dynamic entity word counts");
        Require(world.DpvsDyn.DynEntClientCount.Count, 2, "dynamic entity counts");
        Require(world.WorldDraw.Indices.Count, world.WorldDraw.IndexCount, "indices");
        Require(world.WorldDraw.ReflectionProbeImages.Count, checked((int)world.WorldDraw.ReflectionProbeCount), "reflection probes");
        Require(world.WorldDraw.ReflectionProbeOrigins.Count, checked((int)world.WorldDraw.ReflectionProbeCount), "reflection probe origins");
        Require(world.WorldDraw.Lightmaps.Count, world.WorldDraw.LightmapCount, "lightmaps");
        Require(world.DpvsPlanes.Planes.Count, world.PlaneCount, "planes");
        Require(world.DpvsPlanes.Nodes.Count, world.NodeCount, "nodes");
        Require(world.Models.Count, world.ModelCount, "models");
        Require(world.MaterialMemory.Count, world.MaterialMemoryCount, "material memory");
        if (world.ShadowGeomPointer.Raw != 0 || world.ShadowGeom.Count != 0)
            Require(world.ShadowGeom.Count, world.PrimaryLightCount, "shadow geometry");
        if (world.LightRegionPointer.Raw != 0 || world.LightRegions.Count != 0)
            Require(world.LightRegions.Count, world.PrimaryLightCount, "light regions");
        Require(world.HeroOnlyLights.Count, checked((int)world.HeroOnlyLightCount), "hero-only lights");
        Require(world.LightGrid.RawRowData.Count, checked((int)world.LightGrid.RawRowDataSize), "light grid raw rows");
        Require(world.LightGrid.Entries.Count, checked((int)world.LightGrid.EntryCount), "light grid entries");
        Require(world.LightGrid.Colors.Count, checked((int)world.LightGrid.ColorCount), "light grid colors");
        Require(world.LightGrid.Mins.Count, 3, "light grid mins");
        Require(world.LightGrid.Maxs.Count, 3, "light grid maxs");
        if ((uint)world.LightGrid.RowAxis > 1 || (uint)world.LightGrid.ColAxis > 1 ||
            world.LightGrid.Maxs[(int)world.LightGrid.RowAxis] < world.LightGrid.Mins[(int)world.LightGrid.RowAxis])
            throw new InvalidDataException("GfxWorld light grid row axis or range is invalid.");
        if (world.LightGrid.RowDataStartPointer.Raw != 0 || world.LightGrid.RowDataStart.Count != 0)
            Require(world.LightGrid.RowDataStart.Count,
                world.LightGrid.Maxs[(int)world.LightGrid.RowAxis] - world.LightGrid.Mins[(int)world.LightGrid.RowAxis] + 1,
                "light grid row starts");
        WorldGeometry geometry = ConvertGeometry(world);

        // Retail IW4Xenon GfxWorld is 0x2c8; its draw header embeds three
        // 32-byte D3D resource headers.
        byte[] root = new byte[0x2c8];
        P(root, 0x00, true);
        P(root, 0x04, world.BaseName is not null);
        I(root, 0x08, world.PlaneCount);
        I(root, 0x0c, world.NodeCount);
        I(root, 0x10, world.SurfaceCount);
        I(root, 0x14, world.Skies.Count);
        P(root, 0x18, world.Skies.Count != 0);
        I(root, 0x1c, world.SunPrimaryLightIndex);
        I(root, 0x20, world.PrimaryLightCount);
        I(root, 0x24, world.SortKeyLitDecal);
        I(root, 0x28, world.SortKeyEffectDecal);
        I(root, 0x2c, world.SortKeyEffectAuto);
        I(root, 0x30, world.SortKeyDistortion);
        I(root, 0x34, world.DpvsPlanes.CellCount);
        P(root, 0x38, world.DpvsPlanes.Planes.Count != 0);
        P(root, 0x3c, world.DpvsPlanes.Nodes.Count != 0);
        P(root, 0x40, world.DpvsPlanes.CellCount != 0);
        P(root, 0x44, world.CellTreeCounts.Count != 0);
        P(root, 0x48, world.CellTrees.Count != 0);
        P(root, 0x4c, world.Cells.Count != 0);
        WriteDrawHeader(root, 0x50, world.WorldDraw, geometry);
        WriteGridHeader(root, 0xf0, world.LightGrid);
        I(root, 0x128, world.Models.Count);
        P(root, 0x12c, world.Models.Count != 0);
        F3(root, 0x130, world.Mins);
        F3(root, 0x13c, world.Maxs);
        U(root, 0x148, world.Checksum);
        I(root, 0x14c, world.MaterialMemory.Count);
        P(root, 0x150, world.MaterialMemory.Count != 0);
        WriteSun(root, 0x154, world.Sun);
        Fn(root, 0x1b4, world.OutdoorLookupMatrix, 16);
        int cellCount = world.DpvsPlanes.CellCount;
        int casterWords = checked(cellCount * ((cellCount + 31) / 32));
        P(root, 0x1f8, casterWords != 0);
        P(root, 0x1fc, cellCount != 0);
        P(root, 0x200, At(world.DpvsDyn.DynEntClientCount, 0) != 0);
        P(root, 0x204, At(world.DpvsDyn.DynEntClientCount, 1) != 0);
        int nonSunLights = System.Math.Max(0, world.PrimaryLightCount - world.SunPrimaryLightIndex - 1);
        P(root, 0x208, nonSunLights != 0);
        P(root, 0x20c, nonSunLights != 0 && At(world.DpvsDyn.DynEntClientCount, 0) != 0);
        P(root, 0x210, nonSunLights != 0 && At(world.DpvsDyn.DynEntClientCount, 1) != 0);
        P(root, 0x214, At(world.DpvsDyn.DynEntClientCount, 0) != 0);
        P(root, 0x218, world.ShadowGeom.Count != 0);
        P(root, 0x21c, world.LightRegions.Count != 0);
        WriteStaticHeader(root, 0x220, world);
        WriteDynamicHeader(root, 0x288, world);
        U(root, 0x2b8, world.MapVertexChecksum);
        I(root, 0x2bc, world.HeroOnlyLights.Count);
        P(root, 0x2c0, world.HeroOnlyLights.Count != 0);
        root[0x2c4] = (byte)world.FogTypesAllowed;
        uint rootAt = zone.Write(root, 4, XboxBlock.Temp);
        PatchAsset(zone, rootAt, 0x158, Material, world.Sun.SpriteMaterial?.SerializedAssetName);
        PatchAsset(zone, rootAt, 0x15c, Material, world.Sun.FlareMaterial?.SerializedAssetName);
        PatchAsset(zone, rootAt, 0x1f4, Image, world.OutdoorImage?.SerializedAssetName);

        zone.String(world.Name);
        if (world.BaseName is not null) zone.String(world.BaseName);
        WriteSkies(zone, world.Skies);
        WritePlanes(zone, world);
        WriteTrees(zone, world);
        WriteCells(zone, world.Cells);
        WriteDraw(zone, world.WorldDraw, geometry);
        WriteGrid(zone, world.LightGrid);
        WriteModels(zone, world.Models);
        WriteMaterialMemory(zone, world.MaterialMemory);
        ReserveRuntime(zone, checked(casterWords * 4), 4);
        ReserveRuntime(zone, checked(((cellCount + 31) / 32) * 4), 4);
        // Retail GfxSceneDynModel is XModelDrawInfo (4) plus dynEntId (2).
        ReserveRuntime(zone, checked((int)At(world.DpvsDyn.DynEntClientCount, 0) * 6), 4);
        ReserveRuntime(zone, checked((int)At(world.DpvsDyn.DynEntClientCount, 1) * 4), 4);
        ReserveRuntime(zone, checked(nonSunLights * 0x2000 * 4), 4);
        ReserveRuntime(zone, checked(nonSunLights * (int)At(world.DpvsDyn.DynEntClientCount, 0) * 4), 4);
        ReserveRuntime(zone, checked(nonSunLights * (int)At(world.DpvsDyn.DynEntClientCount, 1) * 4), 4);
        ReserveRuntime(zone, (int)At(world.DpvsDyn.DynEntClientCount, 0), 1);
        WriteShadows(zone, world.ShadowGeom);
        WriteRegions(zone, world.LightRegions);
        WriteStatic(zone, world, geometry);
        WriteDynamic(zone, world);
        WriteHeroLights(zone, world.HeroOnlyLights);
    }

    private static void WriteDrawHeader(byte[] b, int o, GfxWorldDraw d, WorldGeometry geometry)
    {
        U(b, o, d.ReflectionProbeCount);
        P(b, o + 4, d.ReflectionProbeImages.Count != 0);
        P(b, o + 8, d.ReflectionProbeOrigins.Count != 0);
        P(b, o + 12, d.ReflectionProbeCount != 0);
        I(b, o + 16, d.Lightmaps.Count);
        P(b, o + 20, d.Lightmaps.Count != 0);
        P(b, o + 24, d.LightmapCount != 0);
        P(b, o + 28, d.LightmapCount != 0);
        // Retail Xbox worlds leave both lightmap overrides empty.
        U(b, o + 32, 0);
        U(b, o + 36, 0);
        I(b, o + 40, geometry.Vertices.Length / 44);
        P(b, o + 44, geometry.Vertices.Length != 0);
        I(b, o + 80, geometry.Layers.Length);
        P(b, o + 84, geometry.Layers.Length != 0);
        I(b, o + 120, geometry.Indices.Length);
        P(b, o + 124, geometry.Indices.Length != 0);
    }

    private static void WriteGridHeader(byte[] b, int o, GfxLightGrid g)
    {
        b[o] = g.HasLightRegions ? (byte)1 : (byte)0;
        U(b, o + 4, g.SunPrimaryLightIndex);
        for (int i = 0; i < 3; i++) { H(b, o + 8 + i * 2, At(g.Mins, i)); H(b, o + 14 + i * 2, At(g.Maxs, i)); }
        I(b, o + 20, (int)g.RowAxis);
        I(b, o + 24, (int)g.ColAxis);
        P(b, o + 28, g.RowDataStart.Count != 0);
        U(b, o + 32, g.RawRowDataSize);
        P(b, o + 36, g.RawRowData.Count != 0);
        U(b, o + 40, g.EntryCount);
        P(b, o + 44, g.Entries.Count != 0);
        U(b, o + 48, g.ColorCount);
        P(b, o + 52, g.Colors.Count != 0);
    }

    private static void WriteStaticHeader(byte[] b, int o, GfxWorldAsset world)
    {
        GfxWorldDpvsStatic d = world.Dpvs;
        U(b, o, d.SModelCount);
        U(b, o + 4, d.StaticSurfaceCount);
        U(b, o + 8, d.LitSurfsBegin);
        U(b, o + 12, d.LitSurfsEnd);
        // The six later range boundaries and two vis-data counts are already
        // present in the PS3 header in the same order.
        for (int i = 0; i < 8; i++) U(b, o + 16 + i * 4, At(d.VisibilityCounts, i));
        for (int i = 0; i < 3; i++)
        {
            P(b, o + 48 + i * 4, d.SModelCount != 0);
            P(b, o + 60 + i * 4, d.StaticSurfaceCount != 0);
        }
        P(b, o + 72, d.SortedSurfIndex.Count != 0);
        P(b, o + 76, d.SModelInsts.Count != 0);
        P(b, o + 80, d.Surfaces.Count != 0);
        P(b, o + 84, d.SurfaceBounds.Count != 0);
        P(b, o + 88, d.SModelDrawInsts.Count != 0);
        P(b, o + 92, world.SurfaceCount != 0);
        P(b, o + 96, At(d.VisibilityCounts, 7) != 0);
        U(b, o + 100, d.UsageCount);
    }

    private static void WriteDynamicHeader(byte[] b, int o, GfxWorldAsset world)
    {
        GfxWorldDpvsDynamic d = world.DpvsDyn;
        for (int i = 0; i < 2; i++)
        {
            U(b, o + i * 4, At(d.DynEntClientWordCount, i));
            U(b, o + 8 + i * 4, At(d.DynEntClientCount, i));
            P(b, o + 16 + i * 4, At(d.DynEntClientWordCount, i) != 0 && world.DpvsPlanes.CellCount != 0);
        }
        for (int i = 0; i < 6; i++)
            P(b, o + 24 + i * 4, At(d.DynEntClientWordCount, i / 3) != 0);
    }

    private static void WriteSun(byte[] b, int o, Sunflare s)
    {
        b[o] = s.HasValidData ? (byte)1 : (byte)0;
        float[] floats = [s.SpriteSize, s.FlareMinSize, s.FlareMinDot, s.FlareMaxSize, s.FlareMaxDot, s.FlareMaxAlpha];
        Fn(b, o + 12, floats, floats.Length);
        I(b, o + 36, s.FlareFadeInTime);
        I(b, o + 40, s.FlareFadeOutTime);
        Fn(b, o + 44, [s.BlindMinDot, s.BlindMaxDot, s.BlindMaxDarken], 3);
        I(b, o + 56, s.BlindFadeInTime);
        I(b, o + 60, s.BlindFadeOutTime);
        Fn(b, o + 64, [s.GlareMinDot, s.GlareMaxDot, s.GlareMaxLighten], 3);
        I(b, o + 76, s.GlareFadeInTime);
        I(b, o + 80, s.GlareFadeOutTime);
        Fn(b, o + 84, s.SunFxPosition, 3);
    }

    private static uint AssetPointer(XboxZoneStream zone, int type, string? name) =>
        string.IsNullOrEmpty(name) ? 0 : zone.Asset(type, name);

    private static void PatchAsset(XboxZoneStream zone, uint owner, int offset, int type, string? name)
    {
        if (!string.IsNullOrEmpty(name)) zone.Patch(owner, offset, zone.Asset(type, name));
    }

    private static T? At<T>(IReadOnlyList<T> list, int i) => i < list.Count ? list[i] : default;
    private static void U(byte[] b, int o, uint v) => BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(o, 4), v);
    private static void I(byte[] b, int o, int v) => U(b, o, unchecked((uint)v));
    private static void H(byte[] b, int o, ushort v) => BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(o, 2), v);
    private static void F(byte[] b, int o, float v) => BinaryPrimitives.WriteSingleBigEndian(b.AsSpan(o, 4), v);
    private static void P(byte[] b, int o, bool present) => U(b, o, present ? Inline : 0);
    private static void F3(byte[] b, int o, IReadOnlyList<float> v) => Fn(b, o, v, 3);
    private static void Fn(byte[] b, int o, IReadOnlyList<float> v, int n)
    {
        for (int i = 0; i < n; i++) F(b, o + i * 4, At(v, i));
    }
    private static void Vec(byte[] b, int o, Vec3 v) { F(b, o, v.X); F(b, o + 4, v.Y); F(b, o + 8, v.Z); }
    private static void Bounds(byte[] b, int o, Bounds v) { Vec(b, o, v.MidPoint); Vec(b, o + 12, v.HalfSize); }
    private static void ReserveRuntime(XboxZoneStream zone, int size, int alignment)
    {
        if (size != 0) zone.Reserve(size, alignment, XboxBlock.Runtime);
    }

    private static void Require(int actual, int declared, string name)
    {
        if (actual != declared) throw new InvalidDataException($"GfxWorld {name}: {actual} rows for declared count {declared}.");
    }

    private static void WriteSkies(XboxZoneStream zone, IReadOnlyList<GfxSky> skies)
    {
        if (skies.Count == 0) return;
        byte[] rows = new byte[checked(skies.Count * 0x10)];
        for (int i = 0; i < skies.Count; i++)
        {
            GfxSky sky = skies[i];
            int o = i * 0x10;
            I(rows, o, sky.SkySurfCount);
            P(rows, o + 4, sky.SkyStartSurfs.Count != 0);
            U(rows, o + 8, AssetPointer(zone, Image, sky.SkyImage?.SerializedAssetName));
            rows[o + 12] = (byte)sky.SamplerState;
        }
        zone.Write(rows, 4);
        foreach (GfxSky sky in skies)
        {
            Require(sky.SkyStartSurfs.Count, sky.SkySurfCount, "sky surfaces");
            WriteInts(zone, sky.SkyStartSurfs);
        }
    }

    private static void WritePlanes(XboxZoneStream zone, GfxWorldAsset world)
    {
        IReadOnlyList<DpvsPlane> planes = world.DpvsPlanes.Planes;
        if (planes.Count != 0)
        {
            byte[] rows = new byte[checked(planes.Count * 0x14)];
            for (int i = 0; i < planes.Count; i++)
            {
                DpvsPlane p = planes[i];
                int o = i * 0x14;
                F(rows, o, p.NormalX); F(rows, o + 4, p.NormalY);
                F(rows, o + 8, p.NormalZ); F(rows, o + 12, p.Distance);
                rows[o + 16] = p.Type;
                rows[o + 17] = p.SignBits;
                H(rows, o + 18, p.Pad12);
            }
            zone.Write(rows, 4);
        }
        WriteUShorts(zone, world.DpvsPlanes.Nodes);
        ReserveRuntime(zone, checked(world.DpvsPlanes.CellCount * 0x200 * 4), 4);
    }

    private static void WriteTrees(XboxZoneStream zone, GfxWorldAsset world)
    {
        if (world.CellTreeCounts.Count != 0)
        {
            byte[] counts = new byte[checked(world.CellTreeCounts.Count * 4)];
            for (int i = 0; i < world.CellTreeCounts.Count; i++)
                U(counts, i * 4, world.CellTreeCounts[i].AabbTreeCount);
            zone.Write(counts, 4);
        }
        if (world.CellTrees.Count == 0) return;
        byte[] headers = new byte[checked(world.CellTrees.Count * 4)];
        for (int i = 0; i < world.CellTrees.Count; i++)
        {
            Require(world.CellTrees[i].AabbTrees.Count, checked((int)world.CellTreeCounts[i].AabbTreeCount),
                $"cell tree {i} nodes");
            P(headers, i * 4, world.CellTrees[i].AabbTrees.Count != 0);
        }
        zone.Write(headers, 128);
        foreach (GfxCellTree cellTree in world.CellTrees)
        {
            IReadOnlyList<GfxAabbTree> trees = cellTree.AabbTrees;
            if (trees.Count == 0) continue;
            byte[] rows = new byte[checked(trees.Count * 0x28)];
            for (int i = 0; i < trees.Count; i++)
            {
                GfxAabbTree t = trees[i];
                int o = i * 0x28;
                Bounds(rows, o, t.Bounds);
                H(rows, o + 24, t.ChildCount);
                H(rows, o + 26, t.SurfaceCount);
                H(rows, o + 28, t.StartSurfIndex);
                H(rows, o + 30, t.SModelIndexCount);
                P(rows, o + 32, t.SModelIndexes.Count != 0);
                I(rows, o + 36, t.ChildrenOffset);
            }
            uint rowsAt = zone.Write(rows, 4);
            ArraySegment<ushort> rootSlice = trees[0].SModelIndexes is ArraySegment<ushort> segment
                ? segment : default;
            uint rootListAt = 0;
            for (int i = 0; i < trees.Count; i++)
            {
                GfxAabbTree tree = trees[i];
                if (i > 0 && rootListAt != 0 && tree.SModelIndexesPointer.Raw == 0 &&
                    tree.SModelIndexes is ArraySegment<ushort> child &&
                    ReferenceEquals(rootSlice.Array, child.Array))
                {
                    int offset = child.Offset - rootSlice.Offset;
                    if (offset < 0 || offset > rootSlice.Count - child.Count)
                        throw new InvalidDataException("A child culling-tree model list lies outside its root list.");
                    zone.Patch(rowsAt, i * 0x28 + 32, checked(rootListAt + (uint)(offset * 2)));
                    continue;
                }
                uint at = WriteUShorts(zone, tree.SModelIndexes);
                if (i == 0) rootListAt = at;
            }
        }
    }

    private static void WriteCells(XboxZoneStream zone, IReadOnlyList<GfxCell> cells)
    {
        if (cells.Count == 0) return;
        byte[] rows = new byte[checked(cells.Count * 0x28)];
        for (int i = 0; i < cells.Count; i++)
        {
            GfxCell c = cells[i];
            int o = i * 0x28;
            Bounds(rows, o, c.Bounds);
            I(rows, o + 24, c.Portals.Count);
            P(rows, o + 28, c.Portals.Count != 0);
            rows[o + 32] = c.ReflectionProbeCount;
            P(rows, o + 36, c.ReflectionProbes.Count != 0);
        }
        zone.Write(rows, 4);
        foreach (GfxCell cell in cells)
        {
            Require(cell.ReflectionProbes.Count, cell.ReflectionProbeCount, "cell reflection probes");
            if (cell.Portals.Count != 0)
            {
                byte[] portals = new byte[checked(cell.Portals.Count * 0x3c)];
                for (int i = 0; i < cell.Portals.Count; i++)
                {
                    GfxPortal p = cell.Portals[i];
                    int o = i * 0x3c;
                    // The first 12 bytes are writable portal queue state.
                    F(portals, o + 12, p.Plane.NormalX);
                    F(portals, o + 16, p.Plane.NormalY);
                    F(portals, o + 20, p.Plane.NormalZ);
                    F(portals, o + 24, p.Plane.Distance);
                    P(portals, o + 28, p.Vertices.Count != 0);
                    H(portals, o + 32, p.CellIndex);
                    portals[o + 34] = p.VertexCount;
                    Fn(portals, o + 36, p.HullAxis, 6);
                }
                zone.Write(portals, 4);
                foreach (GfxPortal portal in cell.Portals)
                {
                    Require(portal.Vertices.Count, portal.VertexCount, "portal vertices");
                    if (portal.Vertices.Count == 0) continue;
                    byte[] vertices = new byte[checked(portal.Vertices.Count * 12)];
                    for (int i = 0; i < portal.Vertices.Count; i++)
                    {
                        GfxPortalVertex v = portal.Vertices[i];
                        F(vertices, i * 12, v.X);
                        F(vertices, i * 12 + 4, v.Y);
                        F(vertices, i * 12 + 8, v.Z);
                    }
                    zone.Write(vertices, 4);
                }
            }
            if (cell.ReflectionProbes.Count != 0)
                zone.Write(cell.ReflectionProbes.ToArray());
        }
    }

    private static void WriteDraw(XboxZoneStream zone, GfxWorldDraw draw, WorldGeometry geometry)
    {
        if (draw.ReflectionProbeImages.Count != 0)
        {
            byte[] images = new byte[checked(draw.ReflectionProbeImages.Count * 4)];
            for (int i = 0; i < draw.ReflectionProbeImages.Count; i++)
                U(images, i * 4, AssetPointer(zone, Image, draw.ReflectionProbeImages[i]?.SerializedAssetName));
            zone.Write(images, 4);
        }
        if (draw.ReflectionProbeOrigins.Count != 0)
        {
            byte[] origins = new byte[checked(draw.ReflectionProbeOrigins.Count * 12)];
            for (int i = 0; i < draw.ReflectionProbeOrigins.Count; i++)
            {
                GfxReflectionProbe p = draw.ReflectionProbeOrigins[i];
                F(origins, i * 12, p.OffsetX);
                F(origins, i * 12 + 4, p.OffsetY);
                F(origins, i * 12 + 8, p.OffsetZ);
            }
            zone.Write(origins, 4);
        }
        ReserveRuntime(zone, checked((int)draw.ReflectionProbeCount * 0x34), 4);
        if (draw.Lightmaps.Count != 0)
        {
            byte[] lightmaps = new byte[checked(draw.Lightmaps.Count * 8)];
            for (int i = 0; i < draw.Lightmaps.Count; i++)
            {
                U(lightmaps, i * 8, AssetPointer(zone, Image, draw.Lightmaps[i].Primary?.SerializedAssetName));
                U(lightmaps, i * 8 + 4, AssetPointer(zone, Image, draw.Lightmaps[i].Secondary?.SerializedAssetName));
            }
            zone.Write(lightmaps, 4);
        }
        ReserveRuntime(zone, checked(draw.LightmapCount * 0x34), 4);
        ReserveRuntime(zone, checked(draw.LightmapCount * 0x34), 4);
        if (geometry.Vertices.Length != 0) zone.Write(geometry.Vertices, 4);
        if (geometry.Layers.Length != 0) zone.Write(geometry.Layers, 4);
        WriteUShorts(zone, geometry.Indices);
    }

    private sealed record WorldGeometry(
        byte[] Vertices, byte[] Layers, ushort[] Indices,
        int[] FirstVertices, int[] LayerOffsets, int[] BaseIndices, ushort[] VertexCounts);

    private static WorldGeometry ConvertGeometry(GfxWorldAsset world)
    {
        GfxWorldDraw draw = world.WorldDraw;
        int sourceVertexCount = checked((int)draw.VertexCount);
        IReadOnlyList<byte> positions = draw.VertexData.PackedVertices;
        IReadOnlyList<byte> layers = draw.VertexLayerData.PackedLayerData;
        if (positions.Count != checked(sourceVertexCount * 16) || layers.Count != draw.VertexLayerDataSize)
            throw new InvalidDataException("The PS3 world position stream has an invalid length.");
        var vertices = new List<byte>();
        var auxiliary = new List<byte>();
        var indices = new List<ushort>();
        int surfaceCount = world.Dpvs.Surfaces.Count;
        var firstVertices = new int[surfaceCount];
        var layerOffsets = new int[surfaceCount];
        var baseIndices = new int[surfaceCount];
        var vertexCounts = new ushort[surfaceCount];
        for (int surfaceIndex = 0; surfaceIndex < surfaceCount; surfaceIndex++)
        {
            GfxSurface surface = world.Dpvs.Surfaces[surfaceIndex];
            SrfTriangles tris = surface.Triangles;
            int stride = LayerStride(surface.Material, layers.Count, sourceVertexCount);
            int extraNormalCount = ExtraNormalCount(surface.Material);
            int extraNormalBytes = checked(extraNormalCount * 4);
            int extraTexBytes = stride - 28 - extraNormalBytes;
            if (extraTexBytes < 0 || extraTexBytes % 8 != 0)
                throw new InvalidDataException("A world material has an unsupported auxiliary layer layout.");
            firstVertices[surfaceIndex] = vertices.Count / 44;
            layerOffsets[surfaceIndex] = auxiliary.Count;
            baseIndices[surfaceIndex] = indices.Count;
            vertexCounts[surfaceIndex] = tris.VertexCount;
            for (int i = 0; i < tris.VertexCount; i++)
            {
                int vertex = checked(tris.BaseVertex + (int)tris.MinVertexIndex + i);
                int source = checked(tris.VertexLayerData + ((int)tris.MinVertexIndex + i) * stride);
                if ((uint)vertex >= (uint)sourceVertexCount || source < 0 || source > layers.Count - stride)
                    throw new InvalidDataException("A world surface references an invalid PS3 vertex-layer slice.");
                byte[] row = new byte[44];
                for (int k = 0; k < 16; k++) row[k] = positions[vertex * 16 + k];
                U(row, 16, XboxPackedUnitCodec.Color(layers[source], layers[source + 1],
                    layers[source + 2], layers[source + 3]));
                for (int k = 0; k < 16; k++) row[20 + k] = layers[source + 4 + k];
                U(row, 36, XenonNormal(layers, source + 20));
                U(row, 40, XenonNormal(layers, source + 24));
                vertices.AddRange(row);
                for (int k = 0; k < extraTexBytes; k++) auxiliary.Add(layers[source + 28 + k]);
                for (int k = 0; k < extraNormalCount; k++)
                {
                    uint packed = XenonNormal(layers, source + 28 + extraTexBytes + k * 4);
                    auxiliary.Add((byte)(packed >> 24));
                    auxiliary.Add((byte)(packed >> 16));
                    auxiliary.Add((byte)(packed >> 8));
                    auxiliary.Add((byte)packed);
                }
            }
            int firstIndex = tris.BaseIndex;
            int indexCount = checked(tris.TriCount * 3);
            if (firstIndex < 0 || firstIndex > draw.Indices.Count - indexCount)
                throw new InvalidDataException("A world surface references an invalid PS3 index range.");
            for (int i = 0; i < indexCount; i++)
            {
                int local = draw.Indices[firstIndex + i] - (int)tris.MinVertexIndex;
                if ((uint)local >= tris.VertexCount)
                    throw new InvalidDataException("A world surface index lies outside its Xenon vertex window.");
                indices.Add(checked((ushort)local));
            }
        }
        int staticCount = checked((int)world.Dpvs.StaticSurfaceCount);
        for (int first = 0; first < staticCount;)
        {
            int end = first + 1;
            while (end < staticCount && SameDrawRun(world.Dpvs.Surfaces[first], world.Dpvs.Surfaces[end]))
                end++;
            int runVertexCount = 0;
            for (int i = first; i < end; i++) runVertexCount = checked(runVertexCount + vertexCounts[i]);
            if (runVertexCount > ushort.MaxValue)
                throw new InvalidDataException("An Xbox static-surface draw run exceeds 65535 vertices.");
            int runFirstVertex = firstVertices[first];
            int runLayerOffset = layerOffsets[first];
            for (int i = first; i < end; i++)
            {
                int vertexOffset = firstVertices[i] - runFirstVertex;
                int count = checked(world.Dpvs.Surfaces[i].Triangles.TriCount * 3);
                for (int j = 0; j < count; j++)
                    indices[baseIndices[i] + j] = checked((ushort)(indices[baseIndices[i] + j] + vertexOffset));
                firstVertices[i] = runFirstVertex;
                layerOffsets[i] = runLayerOffset;
                vertexCounts[i] = checked((ushort)runVertexCount);
            }
            first = end;
        }
        return new WorldGeometry(vertices.ToArray(), auxiliary.ToArray(), indices.ToArray(),
            firstVertices, layerOffsets, baseIndices, vertexCounts);
    }

    private static bool SameDrawRun(GfxSurface left, GfxSurface right) =>
        string.Equals(left.Material?.SerializedAssetName, right.Material?.SerializedAssetName,
            StringComparison.Ordinal) &&
        left.LightmapIndex == right.LightmapIndex &&
        left.ReflectionProbeIndex == right.ReflectionProbeIndex &&
        left.PrimaryLightIndex == right.PrimaryLightIndex &&
        left.Flags == right.Flags;

    private static int LayerStride(MaterialAsset? material, int layerBytes, int vertexCount)
    {
        // IW4 PS3 event20 world rows 5..16, second stream stride.
        ReadOnlySpan<byte> strides = [28, 36, 40, 44, 48, 52, 52, 56, 60, 60, 64, 68];
        int format = material?.TechniqueSet is { } techset ? (int)techset.WorldVertexFormat : -1;
        if ((uint)format < (uint)strides.Length) return strides[format];
        if (layerBytes == checked(vertexCount * 28)) return 28;
        throw new InvalidDataException("A world surface has no resolved PS3 vertex-layer stride.");
    }

    private static int ExtraNormalCount(MaterialAsset? material)
    {
        int format = material?.TechniqueSet is { } techset ? (int)techset.WorldVertexFormat : -1;
        if (format < 0) return 0;
        if (format > 11) throw new InvalidDataException("A world material has an unsupported vertex format.");
        return format switch
        {
            2 or 4 or 7 or 10 => 1,
            5 or 8 or 11 => 2,
            _ => 0
        };
    }

    private static uint XenonNormal(IReadOnlyList<byte> bytes, int offset)
    {
        uint packed = ((uint)bytes[offset] << 24) | ((uint)bytes[offset + 1] << 16) |
                      ((uint)bytes[offset + 2] << 8) | bytes[offset + 3];
        return XenonPackedUnit(packed, rsxNormalized: true);
    }

    private static uint XenonPackedUnit(uint packed, bool rsxNormalized = false)
    {
        PackedSigned11_11_10 source = new(packed);
        System.Numerics.Vector3 value = rsxNormalized
            ? source.DecodeRsxNormalized()
            : source.DecodePlacement();
        return XboxPackedUnitCodec.Pack(value.X, value.Y, value.Z);
    }

    private static void WriteGrid(XboxZoneStream zone, GfxLightGrid grid)
    {
        WriteUShorts(zone, grid.RowDataStart);
        if (grid.RawRowData.Count != 0) zone.Write(grid.RawRowData.ToArray());
        if (grid.Entries.Count != 0)
        {
            byte[] entries = new byte[checked(grid.Entries.Count * 4)];
            for (int i = 0; i < grid.Entries.Count; i++)
            {
                GfxLightGridEntry e = grid.Entries[i];
                H(entries, i * 4, e.ColorsIndex);
                entries[i * 4 + 2] = e.PrimaryLightIndex;
                entries[i * 4 + 3] = e.NeedsTrace;
            }
            zone.Write(entries, 4);
        }
        if (grid.Colors.Count != 0)
        {
            byte[] colors = new byte[checked(grid.Colors.Count * 0xa8)];
            for (int i = 0; i < grid.Colors.Count; i++)
            {
                IReadOnlyList<byte> src = grid.Colors[i].RgbBytes;
                if (src.Count != 0xa8) throw new InvalidDataException("A light grid color row is not 168 bytes.");
                for (int j = 0; j < src.Count; j++) colors[i * 0xa8 + j] = src[j];
            }
            zone.Write(colors, 4);
        }
    }

    private static void WriteModels(XboxZoneStream zone, IReadOnlyList<GfxBrushModel> models)
    {
        if (models.Count == 0) return;
        byte[] rows = new byte[checked(models.Count * 0x38)];
        for (int i = 0; i < models.Count; i++)
        {
            GfxBrushModel m = models[i];
            int o = i * 0x38;
            F3(rows, o, m.WritableMins);
            F3(rows, o + 12, m.WritableMaxs);
            F3(rows, o + 24, m.BoundsMins);
            F3(rows, o + 36, m.BoundsMaxs);
            F(rows, o + 48, m.Radius);
            H(rows, o + 52, m.SurfaceCount);
            H(rows, o + 54, m.StartSurfIndex);
        }
        zone.Write(rows, 4);
    }

    private static void WriteMaterialMemory(XboxZoneStream zone, IReadOnlyList<MaterialMemory> memory)
    {
        if (memory.Count == 0) return;
        byte[] rows = new byte[checked(memory.Count * 8)];
        for (int i = 0; i < memory.Count; i++)
        {
            U(rows, i * 8, AssetPointer(zone, Material, memory[i].Material?.SerializedAssetName));
            I(rows, i * 8 + 4, memory[i].Memory);
        }
        zone.Write(rows, 4);
    }

    private static void WriteShadows(XboxZoneStream zone, IReadOnlyList<GfxShadowGeometry> shadows)
    {
        if (shadows.Count == 0) return;
        byte[] rows = new byte[checked(shadows.Count * 12)];
        for (int i = 0; i < shadows.Count; i++)
        {
            GfxShadowGeometry s = shadows[i];
            int o = i * 12;
            H(rows, o, s.SurfaceCount);
            H(rows, o + 2, s.SModelCount);
            P(rows, o + 4, s.SortedSurfIndex.Count != 0);
            P(rows, o + 8, s.SModelIndex.Count != 0);
        }
        zone.Write(rows, 4);
        foreach (GfxShadowGeometry s in shadows)
        {
            Require(s.SortedSurfIndex.Count, s.SurfaceCount, "shadow surfaces");
            Require(s.SModelIndex.Count, s.SModelCount, "shadow static models");
            WriteUShorts(zone, s.SortedSurfIndex);
            WriteUShorts(zone, s.SModelIndex);
        }
    }

    private static void WriteRegions(XboxZoneStream zone, IReadOnlyList<GfxLightRegion> regions)
    {
        if (regions.Count == 0) return;
        byte[] rows = new byte[checked(regions.Count * 8)];
        for (int i = 0; i < regions.Count; i++)
        {
            I(rows, i * 8, regions[i].Hulls.Count);
            P(rows, i * 8 + 4, regions[i].Hulls.Count != 0);
        }
        zone.Write(rows, 4);
        foreach (GfxLightRegion region in regions)
        {
            if (region.Hulls.Count == 0) continue;
            byte[] hulls = new byte[checked(region.Hulls.Count * 0x50)];
            for (int i = 0; i < region.Hulls.Count; i++)
            {
                GfxLightRegionHull h = region.Hulls[i];
                int o = i * 0x50;
                Fn(hulls, o, h.KdopMidPoint, 9);
                Fn(hulls, o + 36, h.KdopHalfSize, 9);
                U(hulls, o + 72, h.AxisCount);
                P(hulls, o + 76, h.Axes.Count != 0);
            }
            zone.Write(hulls, 4);
            foreach (GfxLightRegionHull hull in region.Hulls)
            {
                Require(hull.Axes.Count, checked((int)hull.AxisCount), "light region axes");
                if (hull.Axes.Count == 0) continue;
                byte[] axes = new byte[checked(hull.Axes.Count * 0x14)];
                for (int i = 0; i < hull.Axes.Count; i++)
                {
                    GfxLightRegionAxis a = hull.Axes[i];
                    int o = i * 0x14;
                    Fn(axes, o, a.Dir, 3);
                    F(axes, o + 12, a.MidPoint);
                    F(axes, o + 16, a.HalfSize);
                }
                zone.Write(axes, 4);
            }
        }
    }

    private static void WriteStatic(XboxZoneStream zone, GfxWorldAsset world, WorldGeometry geometry)
    {
        GfxWorldDpvsStatic dpvs = world.Dpvs;
        for (int i = 0; i < 3; i++)
            ReserveRuntime(zone, checked((int)dpvs.SModelCount), 1);
        for (int i = 0; i < 3; i++)
            ReserveRuntime(zone, checked((int)dpvs.StaticSurfaceCount), 1);
        WriteUShorts(zone, dpvs.SortedSurfIndex.Take(checked((int)dpvs.StaticSurfaceCount)).ToArray());
        WriteStaticInstances(zone, dpvs.SModelInsts);
        WriteSurfaces(zone, dpvs.Surfaces, geometry);
        WriteSurfaceBounds(zone, dpvs.SurfaceBounds);
        WriteStaticDraws(zone, dpvs.SModelDrawInsts);
        ReserveRuntime(zone, checked(world.SurfaceCount * 8), 8);
        ReserveRuntime(zone, checked((int)At(dpvs.VisibilityCounts, 7) * 4), 128);
    }

    private static void WriteStaticInstances(XboxZoneStream zone, IReadOnlyList<GfxStaticModelInst> instances)
    {
        if (instances.Count == 0) return;
        byte[] rows = new byte[checked(instances.Count * 0x24)];
        for (int i = 0; i < instances.Count; i++)
        {
            GfxStaticModelInst inst = instances[i];
            int o = i * 0x24;
            Bounds(rows, o, inst.Bounds);
            Vec(rows, o + 24, inst.LightingOrigin);
        }
        zone.Write(rows, 4);
    }

    private static void WriteSurfaces(XboxZoneStream zone, IReadOnlyList<GfxSurface> surfaces,
        WorldGeometry geometry)
    {
        if (surfaces.Count == 0) return;
        byte[] rows = new byte[checked(surfaces.Count * 0x18)];
        for (int i = 0; i < surfaces.Count; i++)
        {
            GfxSurface s = surfaces[i];
            SrfTriangles t = s.Triangles;
            int o = i * 0x18;
            I(rows, o, geometry.LayerOffsets[i]);
            I(rows, o + 4, geometry.FirstVertices[i]);
            H(rows, o + 8, geometry.VertexCounts[i]);
            H(rows, o + 10, t.TriCount);
            I(rows, o + 12, geometry.BaseIndices[i]);
            U(rows, o + 16, AssetPointer(zone, Material, s.Material?.SerializedAssetName));
            rows[o + 20] = s.LightmapIndex;
            rows[o + 21] = s.ReflectionProbeIndex;
            rows[o + 22] = s.PrimaryLightIndex;
            rows[o + 23] = (byte)s.Flags;
        }
        zone.Write(rows, 4);
    }

    private static void WriteSurfaceBounds(XboxZoneStream zone, IReadOnlyList<GfxSurfaceBounds> bounds)
    {
        if (bounds.Count == 0) return;
        byte[] rows = new byte[checked(bounds.Count * 0x20)];
        for (int i = 0; i < bounds.Count; i++)
        {
            Bounds(rows, i * 0x20, bounds[i].Bounds);
            H(rows, i * 0x20 + 24, bounds[i].MipRadius);
            IReadOnlyList<byte> weights = bounds[i].InvHighMipRadius;
            if (weights.Count != GfxSurfaceBounds.LayerSlotCount) throw new InvalidDataException("A world surface bounds row has an invalid layer-weight count.");
            for (int j = 0; j < GfxSurfaceBounds.LayerSlotCount; j++) rows[i * 0x20 + 26 + j] = weights[j];
            rows[i * 0x20 + 31] = bounds[i].TailPadding;
        }
        zone.Write(rows, 4);
    }

    private static void WriteStaticDraws(XboxZoneStream zone, IReadOnlyList<GfxStaticModelDrawInst> draws)
    {
        if (draws.Count == 0) return;
        byte[] rows = new byte[checked(draws.Count * 0x2c)];
        for (int i = 0; i < draws.Count; i++)
        {
            GfxStaticModelDrawInst d = draws[i];
            int o = i * 0x2c;
            F3(rows, o, d.Placement.Origin);
            for (int k = 0; k < 3; k++)
                U(rows, o + 12 + k * 4, XenonPackedUnit(At(d.Placement.PackedAxis, k)));
            F(rows, o + 24, d.Placement.Scale);
            U(rows, o + 28, AssetPointer(zone, XModel, d.Model?.SerializedAssetName));
            H(rows, o + 32, d.CullDist);
            H(rows, o + 34, d.LightingHandle);
            rows[o + 36] = d.ReflectionProbeIndex;
            rows[o + 37] = d.PrimaryLightIndex;
            rows[o + 38] = (byte)d.Flags;
            rows[o + 39] = d.FirstMaterialSkinIndex;
            U(rows, o + 40, XboxPackedUnitCodec.Color(d.GroundLighting.Red, d.GroundLighting.Green,
                d.GroundLighting.Blue, d.GroundLighting.Alpha));
        }
        zone.Write(rows, 4);
    }

    private static void WriteDynamic(XboxZoneStream zone, GfxWorldAsset world)
    {
        GfxWorldDpvsDynamic d = world.DpvsDyn;
        int cells = world.DpvsPlanes.CellCount;
        for (int i = 0; i < 2; i++)
            ReserveRuntime(zone, checked((int)At(d.DynEntClientWordCount, i) * cells * 4), 4);
        // Native loader consumes the six visibility pointers column by column.
        for (int column = 0; column < 3; column++)
            for (int kind = 0; kind < 2; kind++)
                ReserveRuntime(zone, checked((int)At(d.DynEntClientWordCount, kind) * 32), 16);
    }

    private static void WriteHeroLights(XboxZoneStream zone, IReadOnlyList<GfxHeroOnlyLight> lights)
    {
        if (lights.Count == 0) return;
        byte[] rows = new byte[checked(lights.Count * GfxHeroOnlyLight.SerializedSize)];
        for (int i = 0; i < lights.Count; i++)
        {
            GfxHeroOnlyLight light = lights[i];
            int offset = i * GfxHeroOnlyLight.SerializedSize;
            rows[offset] = (byte)light.Type;
            if (light.Reserved01To03.Count != 3)
                throw new InvalidDataException("A hero-only light has an invalid spare-byte count.");
            for (int j = 0; j < 3; j++) rows[offset + 1 + j] = light.Reserved01To03[j];
            Vec(rows, offset + 4, light.Color);
            Vec(rows, offset + 16, light.Direction);
            Vec(rows, offset + 28, light.Origin);
            F(rows, offset + 40, light.Radius);
            F(rows, offset + 44, light.CosHalfFovOuter);
            F(rows, offset + 48, light.CosHalfFovInner);
            I(rows, offset + 52, light.Exponent);
        }
        zone.Write(rows, 4);
    }

    private static void WriteInts(XboxZoneStream zone, IReadOnlyList<int> values)
    {
        if (values.Count == 0) return;
        byte[] bytes = new byte[checked(values.Count * 4)];
        for (int i = 0; i < values.Count; i++) I(bytes, i * 4, values[i]);
        zone.Write(bytes, 4);
    }

    private static uint WriteUShorts(XboxZoneStream zone, IReadOnlyList<ushort> values)
    {
        if (values.Count == 0) return 0;
        byte[] bytes = new byte[checked(values.Count * 2)];
        for (int i = 0; i < values.Count; i++) H(bytes, i * 2, values[i]);
        return zone.Write(bytes, 2);
    }
}
