using IW4.FastFiles.Loaders.Database;
using IW4.FastFiles.Loaders.Assets.Image;
using IW4.FastFiles.Loaders.Assets.Material;
using IW4.FastFiles.Loaders.Assets.XModel;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;
using IW4.Runtime.IO;
using ModelBounds = IW4.Assets.Math.Bounds;
using ModelVec3 = IW4.Assets.Math.Vec3;

namespace IW4.FastFiles.Loaders.Assets.GfxMap;

public sealed class GfxWorldLoader
{
    private readonly GfxImageLoader _imageLoader = new();
    private readonly MaterialLoader _materialLoader = new();
    private readonly XModelLoader _xmodelLoader = new();

    public GfxWorldAsset LoadFromAssetPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null)
            throw new InvalidDataException("Top-level GfxWorld pointer is null.");

        if (pointer.Type == PointerType.Offset)
        {
            context.PointerReader.ValidateOffsetPointerRange<GfxWorldAsset>(
                pointer,
                GfxWorldAsset.SerializedSize,
                "GfxWorld");
            GfxWorldAsset canonical = context.ResolveCanonicalAsset<GfxWorldAsset>(
                    pointer,
                    XAssetType.GfxMap)
                ?? throw new InvalidDataException(
                    $"Top-level GfxWorld pointer 0x{unchecked((uint)pointer.Raw):X8} does not resolve to a canonical GfxMap asset.");
            context.PatchCanonicalAssetPointerCellIfPresent(
                pointer,
                canonical,
                "Canonical GfxWorld has no runtime address.");
            return canonical;
        }

        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
        {
            throw new InvalidDataException(
                $"GfxWorld pointer 0x{unchecked((uint)pointer.Raw):X8} has unsupported type {pointer.Type}.");
        }

        ProviderRegistrationOccurrence providerRegistration = context.BeginProviderRegistration(pointer);

        context.Blocks.Push(XFileBlockType.TEMP);
        try
        {
            XBlockAddress rootAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
            GfxWorldAsset gfxWorld = ReadGfxWorld(cursor, rootAddress, context);
            GfxWorldAsset canonical = context.DB_AddXAsset(
                XAssetType.GfxMap,
                gfxWorld.Name,
                gfxWorld,
                providerRegistration);

            return canonical;
        }
        finally
        {
            context.Blocks.Pop();
        }
    }


    private GfxWorldAsset ReadGfxWorld(
        FastFileCursor cursor,
        XBlockAddress expectedRootAddress,
        DbLoadExecutionContext context)
    {
        int sourceOffset = cursor.Offset;
        byte[] rootBytes = context.Blocks.Load(cursor, GfxWorldAsset.SerializedSize, out XBlockAddress rootAddress);
        if (rootAddress != expectedRootAddress)
            throw new InvalidDataException($"GfxWorld pointer patched to {expectedRootAddress}, but root loaded at {rootAddress}.");

        GfxWorldHeader root = ReadGfxWorldHeader(rootBytes, rootAddress, context);

        string? name;
        string? baseName;
        IReadOnlyList<GfxSky> skies;
        GfxWorldDpvsPlanes dpvsPlanes;
        IReadOnlyList<GfxCellTreeCount> cellTreeCounts;
        IReadOnlyList<GfxCellTree> cellTrees;
        IReadOnlyList<GfxCell> cells;
        GfxWorldDraw worldDraw;
        GfxLightGrid lightGrid;
        IReadOnlyList<GfxBrushModel> models;
        IReadOnlyList<MaterialMemory> materialMemory;
        Sunflare sun;
        GfxImageAsset? outdoorImage;
        IReadOnlyList<uint> cellCasterBits;
        IReadOnlyList<uint> cellCasterBits2;
        IReadOnlyList<GfxSceneDynModel> sceneDynModels;
        IReadOnlyList<GfxSceneDynBrush> sceneDynBrushes;
        IReadOnlyList<uint> primaryLightEntityShadowVis;
        IReadOnlyList<uint> primaryLightDynEntShadowVis0;
        IReadOnlyList<uint> primaryLightDynEntShadowVis1;
        IReadOnlyList<byte> primaryLightForModelDynEnt;
        IReadOnlyList<GfxShadowGeometry> shadowGeom;
        IReadOnlyList<GfxLightRegion> lightRegions;
        GfxWorldDpvsStatic dpvs;
        GfxWorldDpvsDynamic dpvsDyn;
        IReadOnlyList<GfxHeroOnlyLight> heroOnlyLights;
        IReadOnlyList<byte> umbraGateData;
        IReadOnlyList<byte> umbraGateData2;

        context.Blocks.Push(XFileBlockType.LARGE);
        try
        {
            name = context.PointerReader.LoadXString(cursor, root.NamePointer);
            baseName = context.PointerReader.LoadXString(cursor, root.BaseNamePointer);
            skies = ReadSkies(cursor, root.SkiesPointer.Untyped, Count(root.SkyCount, "skyCount"), context);
            dpvsPlanes = ReadDpvsPlanesPayloads(cursor, root.DpvsPlanes, root.PlaneCount, root.NodeCount, context);
            cellTreeCounts = ReadCellTreeCounts(cursor, root.CellTreeCountsPointer.Untyped, dpvsPlanes.CellCount, context);
            cellTrees = ReadCellTrees(cursor, root.CellTreesPointer.Untyped, cellTreeCounts, context);
            cells = ReadCells(cursor, root.CellsPointer.Untyped, dpvsPlanes.CellCount, context);
            worldDraw = ReadWorldDrawPayloads(cursor, root.WorldDraw, context);
            lightGrid = ReadLightGridPayloads(cursor, root.LightGrid, context);
            models = ReadBrushModels(cursor, root.ModelsPointer.Untyped, root.ModelCount, context);
            materialMemory = ReadMaterialMemory(cursor, root.MaterialMemoryPointer.Untyped, root.MaterialMemoryCount, context);
            sun = ReadSunflarePayloads(cursor, root.Sun, context);
            outdoorImage = _imageLoader.LoadFromPointer(
                cursor,
                root.OutdoorImagePointer.Untyped,
                context);

            int cellCount = Count(dpvsPlanes.CellCount, "cellCount");
            int cellWordCount = WordCount(cellCount);
            cellCasterBits = ReadPushed(context, XFileBlockType.RUNTIME, () => ReadUInt32Array(cursor, root.CellCasterBitsPointer.Untyped, checked(cellCount * cellWordCount), 4, context, "GfxWorld.cellCasterBits"));
            cellCasterBits2 = ReadPushed(context, XFileBlockType.RUNTIME, () => ReadUInt32Array(cursor, root.CellCasterBits2Pointer.Untyped, cellWordCount, 4, context, "GfxWorld.cellCasterBits2"));

            int dynModelCount = GetDynCount(root.DpvsDyn, 0);
            int dynBrushCount = GetDynCount(root.DpvsDyn, 1);
            sceneDynModels = ReadPushed(context, XFileBlockType.RUNTIME, () => ReadSceneDynModels(cursor, root.SceneDynModelPointer.Untyped, dynModelCount, context));
            sceneDynBrushes = ReadPushed(context, XFileBlockType.RUNTIME, () => ReadSceneDynBrushes(cursor, root.SceneDynBrushPointer.Untyped, dynBrushCount, context));

            int sunInclusiveLightSpan = checked(
                root.PrimaryLightCount - root.SunPrimaryLightIndex);
            if (sunInclusiveLightSpan < 0)
                throw new InvalidDataException(
                    $"GfxWorld primary light range is inverted: " +
                    $"sunPrimaryLightIndex={root.SunPrimaryLightIndex}, " +
                    $"count={root.PrimaryLightCount}.");

            int nonSunPrimaryLightCount = Math.Max(
                0,
                sunInclusiveLightSpan - 1);
            primaryLightEntityShadowVis = ReadPushed(context, XFileBlockType.RUNTIME, () => ReadUInt32Array(cursor, root.PrimaryLightEntityShadowVisPointer.Untyped, checked(nonSunPrimaryLightCount * 0x2000), 4, context, "GfxWorld.primaryLightEntityShadowVis"));
            primaryLightDynEntShadowVis0 = ReadPushed(context, XFileBlockType.RUNTIME, () => ReadUInt32Array(cursor, root.PrimaryLightDynEntShadowVis0Pointer.Untyped, checked(nonSunPrimaryLightCount * dynModelCount), 4, context, "GfxWorld.primaryLightDynEntShadowVis0"));
            primaryLightDynEntShadowVis1 = ReadPushed(context, XFileBlockType.RUNTIME, () => ReadUInt32Array(cursor, root.PrimaryLightDynEntShadowVis1Pointer.Untyped, checked(nonSunPrimaryLightCount * dynBrushCount), 4, context, "GfxWorld.primaryLightDynEntShadowVis1"));
            primaryLightForModelDynEnt = ReadPushed(context, XFileBlockType.RUNTIME, () => ReadByteArray(cursor, root.PrimaryLightForModelDynEntPointer.Untyped, dynModelCount, 1, context, "GfxWorld.primaryLightForModelDynEnt"));

            shadowGeom = ReadShadowGeometry(cursor, root.ShadowGeomPointer.Untyped, root.PrimaryLightCount, context);
            lightRegions = ReadLightRegions(cursor, root.LightRegionPointer.Untyped, root.PrimaryLightCount, context);
            dpvs = ReadDpvsStaticPayloads(cursor, root.Dpvs, root.SurfaceCount, context);
            dpvsDyn = ReadDpvsDynamicPayloads(cursor, root.DpvsDyn, cellCount, context);
            heroOnlyLights = ReadHeroOnlyLights(cursor, root.HeroOnlyLightsPointer.Untyped, Count(root.HeroOnlyLightCount, "heroOnlyLightCount"), context);
            umbraGateData = ReadPushed(context, XFileBlockType.VIRTUAL, () => ReadByteArray(cursor, root.UmbraGateDataPointer.Untyped, checked(root.UmbraGateCount + 0x1000), 4096, context, "GfxWorld.umbraGateData"));
            umbraGateData2 = ReadPushed(context, XFileBlockType.VIRTUAL, () => ReadByteArray(cursor, root.UmbraGateData2Pointer.Untyped, checked(root.UmbraGateCount + 0x1000), 4096, context, "GfxWorld.umbraGateData2"));
        }
        finally
        {
            context.Blocks.Pop();
        }


        return new GfxWorldAsset
        {
            Offset = sourceOffset,
            RuntimeAddress = rootAddress,
            NamePointer = root.NamePointer,
            Name = name,
            BaseNamePointer = root.BaseNamePointer,
            BaseName = baseName,
            PlaneCount = root.PlaneCount,
            NodeCount = root.NodeCount,
            SurfaceCount = root.SurfaceCount,
            SkyCount = root.SkyCount,
            SkiesPointer = root.SkiesPointer,
            Skies = skies,
            SunPrimaryLightIndex = root.SunPrimaryLightIndex,
            PrimaryLightCount = root.PrimaryLightCount,
            SortKeyLitDecal = root.SortKeyLitDecal,
            SortKeyEffectDecal = root.SortKeyEffectDecal,
            SortKeyEffectAuto = root.SortKeyEffectAuto,
            SortKeyDistortion = root.SortKeyDistortion,
            DpvsPlanes = dpvsPlanes,
            CellTreeCountsPointer = root.CellTreeCountsPointer,
            CellTreeCounts = cellTreeCounts,
            CellTreesPointer = root.CellTreesPointer,
            CellTrees = cellTrees,
            CellsPointer = root.CellsPointer,
            Cells = cells,
            WorldDraw = worldDraw,
            LightGrid = lightGrid,
            ModelCount = root.ModelCount,
            ModelsPointer = root.ModelsPointer,
            Models = models,
            Mins = root.Mins,
            Maxs = root.Maxs,
            Checksum = root.Checksum,
            MaterialMemoryCount = root.MaterialMemoryCount,
            MaterialMemoryPointer = root.MaterialMemoryPointer,
            MaterialMemory = materialMemory,
            Sun = sun,
            OutdoorLookupMatrix = root.OutdoorLookupMatrix,
            OutdoorImagePointer = root.OutdoorImagePointer,
            OutdoorImage = outdoorImage,
            CellCasterBitsPointer = root.CellCasterBitsPointer,
            CellCasterBits = cellCasterBits,
            CellCasterBits2Pointer = root.CellCasterBits2Pointer,
            CellCasterBits2 = cellCasterBits2,
            SceneDynModelPointer = root.SceneDynModelPointer,
            SceneDynModels = sceneDynModels,
            SceneDynBrushPointer = root.SceneDynBrushPointer,
            SceneDynBrushes = sceneDynBrushes,
            PrimaryLightEntityShadowVisPointer = root.PrimaryLightEntityShadowVisPointer,
            PrimaryLightEntityShadowVis = primaryLightEntityShadowVis,
            PrimaryLightDynEntShadowVis0Pointer = root.PrimaryLightDynEntShadowVis0Pointer,
            PrimaryLightDynEntShadowVis0 = primaryLightDynEntShadowVis0,
            PrimaryLightDynEntShadowVis1Pointer = root.PrimaryLightDynEntShadowVis1Pointer,
            PrimaryLightDynEntShadowVis1 = primaryLightDynEntShadowVis1,
            PrimaryLightForModelDynEntPointer = root.PrimaryLightForModelDynEntPointer,
            PrimaryLightForModelDynEnt = primaryLightForModelDynEnt,
            ShadowGeomPointer = root.ShadowGeomPointer,
            ShadowGeom = shadowGeom,
            LightRegionPointer = root.LightRegionPointer,
            LightRegions = lightRegions,
            Dpvs = dpvs,
            DpvsDyn = dpvsDyn,
            MapVertexChecksum = root.MapVertexChecksum,
            HeroOnlyLightCount = root.HeroOnlyLightCount,
            HeroOnlyLightsPointer = root.HeroOnlyLightsPointer,
            HeroOnlyLights = heroOnlyLights,
            FogTypesAllowed = root.FogTypesAllowed,
            Pad279To27B = root.Pad279To27B,
            UmbraGateCount = root.UmbraGateCount,
            UmbraGateDataPointer = root.UmbraGateDataPointer,
            UmbraGateData = umbraGateData,
            UmbraGateData2Pointer = root.UmbraGateData2Pointer,
            UmbraGateData2 = umbraGateData2
        };
    }

    private static GfxWorldHeader ReadGfxWorldHeader(
        byte[] rootBytes,
        XBlockAddress rootAddress,
        DbLoadExecutionContext context)
    {
        var cursor = new FastFileCursor(rootBytes, rootAddress);
        var header = new GfxWorldHeader
        {
            NamePointer = context.PointerReader.ReadPointer<string>(cursor, XPointerResolutionMode.Direct),
            BaseNamePointer = context.PointerReader.ReadPointer<string>(cursor, XPointerResolutionMode.Direct),
            PlaneCount = cursor.ReadInt32(),
            NodeCount = cursor.ReadInt32(),
            SurfaceCount = cursor.ReadInt32(),
            SkyCount = cursor.ReadUInt32(),
            SkiesPointer = context.PointerReader.ReadPointer<GfxSky[]>(cursor, XPointerResolutionMode.Direct),
            SunPrimaryLightIndex = cursor.ReadInt32(),
            PrimaryLightCount = cursor.ReadInt32(),
            SortKeyLitDecal = cursor.ReadInt32(),
            SortKeyEffectDecal = cursor.ReadInt32(),
            SortKeyEffectAuto = cursor.ReadInt32(),
            SortKeyDistortion = cursor.ReadInt32(),
            DpvsPlanes = ReadDpvsPlanesHeader(cursor, context),
            CellTreeCountsPointer = context.PointerReader.ReadPointer<GfxCellTreeCount[]>(cursor, XPointerResolutionMode.Direct),
            CellTreesPointer = context.PointerReader.ReadPointer<GfxAabbTree[]>(cursor, XPointerResolutionMode.Direct),
            CellsPointer = context.PointerReader.ReadPointer<GfxCell[]>(cursor, XPointerResolutionMode.Direct),
            WorldDraw = ReadWorldDrawHeader(cursor, context),
            LightGrid = ReadLightGridHeader(cursor, context),
            ModelCount = cursor.ReadInt32(),
            ModelsPointer = context.PointerReader.ReadPointer<GfxBrushModel[]>(cursor, XPointerResolutionMode.Direct),
            Mins = ReadFloatValues(cursor, 3),
            Maxs = ReadFloatValues(cursor, 3),
            Checksum = cursor.ReadUInt32(),
            MaterialMemoryCount = cursor.ReadInt32(),
            MaterialMemoryPointer = context.PointerReader.ReadPointer<MaterialMemory[]>(cursor, XPointerResolutionMode.Direct),
            Sun = ReadSunflareHeader(cursor, context),
            OutdoorLookupMatrix = ReadFloatValues(cursor, 16),
            OutdoorImagePointer = context.PointerReader.ReadPointer<GfxImageAsset>(cursor, XPointerResolutionMode.AliasCell),
            CellCasterBitsPointer = context.PointerReader.ReadPointer<uint[]>(cursor, XPointerResolutionMode.Direct),
            CellCasterBits2Pointer = context.PointerReader.ReadPointer<uint[]>(cursor, XPointerResolutionMode.Direct),
            SceneDynModelPointer = context.PointerReader.ReadPointer<GfxSceneDynModel[]>(cursor, XPointerResolutionMode.Direct),
            SceneDynBrushPointer = context.PointerReader.ReadPointer<GfxSceneDynBrush[]>(cursor, XPointerResolutionMode.Direct),
            PrimaryLightEntityShadowVisPointer = context.PointerReader.ReadPointer<uint[]>(cursor, XPointerResolutionMode.Direct),
            PrimaryLightDynEntShadowVis0Pointer = context.PointerReader.ReadPointer<uint[]>(cursor, XPointerResolutionMode.Direct),
            PrimaryLightDynEntShadowVis1Pointer = context.PointerReader.ReadPointer<uint[]>(cursor, XPointerResolutionMode.Direct),
            PrimaryLightForModelDynEntPointer = context.PointerReader.ReadPointer<byte[]>(cursor, XPointerResolutionMode.Direct),
            ShadowGeomPointer = context.PointerReader.ReadPointer<GfxShadowGeometry[]>(cursor, XPointerResolutionMode.Direct),
            LightRegionPointer = context.PointerReader.ReadPointer<GfxLightRegion[]>(cursor, XPointerResolutionMode.Direct),
            Dpvs = ReadDpvsStaticHeader(cursor, context),
            DpvsDyn = ReadDpvsDynamicHeader(cursor, context),
            MapVertexChecksum = cursor.ReadUInt32(),
            HeroOnlyLightCount = cursor.ReadUInt32(),
            HeroOnlyLightsPointer = context.PointerReader.ReadPointer<GfxHeroOnlyLight[]>(cursor, XPointerResolutionMode.Direct),
            FogTypesAllowed = (FogTypesAllowed)cursor.ReadByte(),
            Pad279To27B = cursor.ReadBytes(3),
            UmbraGateCount = cursor.ReadInt32(),
            UmbraGateDataPointer = context.PointerReader.ReadPointer<byte[]>(cursor, XPointerResolutionMode.Direct),
            UmbraGateData2Pointer = context.PointerReader.ReadPointer<byte[]>(cursor, XPointerResolutionMode.Direct)
        };

        EnsureConsumed(cursor, GfxWorldAsset.SerializedSize, "GfxWorld");
        return header;
    }

    private static GfxWorldDpvsPlanes ReadDpvsPlanesHeader(
        FastFileCursor cursor,
        DbLoadExecutionContext context)
    {
        return new GfxWorldDpvsPlanes
        {
            CellCount = cursor.ReadInt32(),
            PlanesPointer = context.PointerReader.ReadPointer<DpvsPlane[]>(cursor, XPointerResolutionMode.Direct),
            NodesPointer = context.PointerReader.ReadPointer<ushort[]>(cursor, XPointerResolutionMode.Direct),
            SceneEntCellBitsPointer = context.PointerReader.ReadPointer<uint[]>(cursor, XPointerResolutionMode.Direct)
        };
    }

    private static GfxWorldDraw ReadWorldDrawHeader(
        FastFileCursor cursor,
        DbLoadExecutionContext context)
    {
        return new GfxWorldDraw
        {
            StagingAddress = cursor.AddressAt(cursor.Offset),
            ReflectionProbeCount = cursor.ReadUInt32(),
            ReflectionProbeImagesPointer = context.PointerReader.ReadPointer<GfxImageAsset[]>(cursor, XPointerResolutionMode.Direct),
            ReflectionProbeOriginsPointer = context.PointerReader.ReadPointer<GfxReflectionProbe[]>(cursor, XPointerResolutionMode.Direct),
            ReflectionProbeTexturesPointer = context.PointerReader.ReadPointer<GfxTexture[]>(cursor, XPointerResolutionMode.Direct),
            LightmapCount = cursor.ReadInt32(),
            LightmapsPointer = context.PointerReader.ReadPointer<GfxLightmapArray[]>(cursor, XPointerResolutionMode.Direct),
            LightmapPrimaryTexturesPointer = context.PointerReader.ReadPointer<GfxTexture[]>(cursor, XPointerResolutionMode.Direct),
            LightmapSecondaryTexturesPointer = context.PointerReader.ReadPointer<GfxTexture[]>(cursor, XPointerResolutionMode.Direct),
            LightmapOverridePrimaryPointer = context.PointerReader.ReadPointer<GfxImageAsset>(cursor, XPointerResolutionMode.AliasCell),
            LightmapOverrideSecondaryPointer = context.PointerReader.ReadPointer<GfxImageAsset>(cursor, XPointerResolutionMode.AliasCell),
            VertexCount = cursor.ReadUInt32(),
            VertexData = ReadWorldVertexDataHeader(cursor, context),
            VertexLayerDataSize = cursor.ReadUInt32(),
            VertexLayerData = ReadWorldVertexLayerDataHeader(cursor, context),
            IndexCount = cursor.ReadInt32(),
            IndicesPointer = context.PointerReader.ReadPointer<ushort[]>(cursor, XPointerResolutionMode.Direct),
            IndexBufferRaw = cursor.ReadInt32()
        };
    }

    private static GfxWorldVertexData ReadWorldVertexDataHeader(
        FastFileCursor cursor,
        DbLoadExecutionContext context)
    {
        return new GfxWorldVertexData
        {
            VerticesPointer = context.PointerReader.ReadPointer<byte[]>(cursor, XPointerResolutionMode.Direct),
            WorldVbHandle = cursor.ReadInt32(),
            WorldVbOffset = cursor.ReadInt32()
        };
    }

    private static GfxWorldVertexLayerData ReadWorldVertexLayerDataHeader(
        FastFileCursor cursor,
        DbLoadExecutionContext context)
    {
        return new GfxWorldVertexLayerData
        {
            DataPointer = context.PointerReader.ReadPointer<byte[]>(cursor, XPointerResolutionMode.Direct),
            LayerVbHandle = cursor.ReadInt32(),
            LayerVbOffset = cursor.ReadInt32()
        };
    }

    private static GfxLightGrid ReadLightGridHeader(
        FastFileCursor cursor,
        DbLoadExecutionContext context)
    {
        return new GfxLightGrid
        {
            HasLightRegionsRaw = cursor.ReadUInt32(),
            SunPrimaryLightIndex = cursor.ReadUInt32(),
            Mins = ReadUInt16Values(cursor, 3),
            Maxs = ReadUInt16Values(cursor, 3),
            RowAxis = (GfxLightGridHorizontalAxis)cursor.ReadUInt32(),
            ColAxis = (GfxLightGridHorizontalAxis)cursor.ReadUInt32(),
            RowDataStartPointer = context.PointerReader.ReadPointer<ushort[]>(cursor, XPointerResolutionMode.Direct),
            RawRowDataSize = cursor.ReadUInt32(),
            RawRowDataPointer = context.PointerReader.ReadPointer<byte[]>(cursor, XPointerResolutionMode.Direct),
            EntryCount = cursor.ReadUInt32(),
            EntriesPointer = context.PointerReader.ReadPointer<GfxLightGridEntry[]>(cursor, XPointerResolutionMode.Direct),
            ColorCount = cursor.ReadUInt32(),
            ColorsPointer = context.PointerReader.ReadPointer<GfxLightGridColors[]>(cursor, XPointerResolutionMode.Direct)
        };
    }

    private static Sunflare ReadSunflareHeader(
        FastFileCursor cursor,
        DbLoadExecutionContext context)
    {
        return new Sunflare
        {
            HasValidDataRaw = cursor.ReadUInt32(),
            SpriteMaterialPointer = context.PointerReader.ReadPointer<MaterialAsset>(cursor, XPointerResolutionMode.AliasCell),
            FlareMaterialPointer = context.PointerReader.ReadPointer<MaterialAsset>(cursor, XPointerResolutionMode.AliasCell),
            SpriteSize = cursor.ReadSingle(),
            FlareMinSize = cursor.ReadSingle(),
            FlareMinDot = cursor.ReadSingle(),
            FlareMaxSize = cursor.ReadSingle(),
            FlareMaxDot = cursor.ReadSingle(),
            FlareMaxAlpha = cursor.ReadSingle(),
            FlareFadeInTime = cursor.ReadInt32(),
            FlareFadeOutTime = cursor.ReadInt32(),
            BlindMinDot = cursor.ReadSingle(),
            BlindMaxDot = cursor.ReadSingle(),
            BlindMaxDarken = cursor.ReadSingle(),
            BlindFadeInTime = cursor.ReadInt32(),
            BlindFadeOutTime = cursor.ReadInt32(),
            GlareMinDot = cursor.ReadSingle(),
            GlareMaxDot = cursor.ReadSingle(),
            GlareMaxLighten = cursor.ReadSingle(),
            GlareFadeInTime = cursor.ReadInt32(),
            GlareFadeOutTime = cursor.ReadInt32(),
            SunFxPosition = ReadFloatValues(cursor, 3)
        };
    }

    private static GfxWorldDpvsStatic ReadDpvsStaticHeader(
        FastFileCursor cursor,
        DbLoadExecutionContext context)
    {
        uint smodelCount = cursor.ReadUInt32();
        uint staticSurfaceCount = cursor.ReadUInt32();
        uint litSurfsBegin = cursor.ReadUInt32();
        uint litSurfsEnd = cursor.ReadUInt32();
        var visibilityCounts = new uint[8];
        for (int i = 0; i < visibilityCounts.Length; i++)
            visibilityCounts[i] = cursor.ReadUInt32();

        var smodelVisDataPointers = new XPointer<uint[]>[3];
        for (int i = 0; i < smodelVisDataPointers.Length; i++)
            smodelVisDataPointers[i] = context.PointerReader.ReadPointer<uint[]>(cursor, XPointerResolutionMode.Direct);

        var surfaceVisDataPointers = new XPointer<uint[]>[3];
        for (int i = 0; i < surfaceVisDataPointers.Length; i++)
            surfaceVisDataPointers[i] = context.PointerReader.ReadPointer<uint[]>(cursor, XPointerResolutionMode.Direct);

        return new GfxWorldDpvsStatic
        {
            SModelCount = smodelCount,
            StaticSurfaceCount = staticSurfaceCount,
            LitSurfsBegin = litSurfsBegin,
            LitSurfsEnd = litSurfsEnd,
            VisibilityCounts = visibilityCounts,
            SModelVisDataPointers = smodelVisDataPointers,
            SurfaceVisDataPointers = surfaceVisDataPointers,
            SortedSurfIndexPointer = context.PointerReader.ReadPointer<ushort[]>(cursor, XPointerResolutionMode.Direct),
            SModelInstsPointer = context.PointerReader.ReadPointer<GfxStaticModelInst[]>(cursor, XPointerResolutionMode.Direct),
            SurfacesPointer = context.PointerReader.ReadPointer<GfxSurface[]>(cursor, XPointerResolutionMode.Direct),
            SurfaceBoundsPointer = context.PointerReader.ReadPointer<GfxSurfaceBounds[]>(cursor, XPointerResolutionMode.Direct),
            SModelDrawInstsPointer = context.PointerReader.ReadPointer<GfxStaticModelDrawInst[]>(cursor, XPointerResolutionMode.Direct),
            SurfaceMaterialsPointer = context.PointerReader.ReadPointer<GfxMapDrawSurf[]>(cursor, XPointerResolutionMode.Direct),
            SurfaceCastsSunShadowPointer = context.PointerReader.ReadPointer<uint[]>(cursor, XPointerResolutionMode.Direct),
            UsageCount = cursor.ReadUInt32()
        };
    }

    private static GfxWorldDpvsDynamic ReadDpvsDynamicHeader(
        FastFileCursor cursor,
        DbLoadExecutionContext context)
    {
        var wordCounts = new uint[] { cursor.ReadUInt32(), cursor.ReadUInt32() };
        var clientCounts = new uint[] { cursor.ReadUInt32(), cursor.ReadUInt32() };
        var cellBitsPointers = new XPointer<uint[]>[]
        {
            context.PointerReader.ReadPointer<uint[]>(cursor, XPointerResolutionMode.Direct),
            context.PointerReader.ReadPointer<uint[]>(cursor, XPointerResolutionMode.Direct)
        };
        var visDataPointers = new XPointer<byte[]>[6];
        for (int i = 0; i < visDataPointers.Length; i++)
            visDataPointers[i] = context.PointerReader.ReadPointer<byte[]>(cursor, XPointerResolutionMode.Direct);

        return new GfxWorldDpvsDynamic
        {
            DynEntClientWordCount = wordCounts,
            DynEntClientCount = clientCounts,
            DynEntCellBitsPointers = cellBitsPointers,
            DynEntVisDataPointers = visDataPointers
        };
    }

    private IReadOnlyList<GfxSky> ReadSkies(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        byte[] bytes = LoadInlineArray(cursor, pointer, count, GfxSky.SerializedSize, 4, context, "GfxWorld.skies", out XBlockAddress skyAddress);
        var rowCursor = new FastFileCursor(bytes, skyAddress);
        var rows = new GfxSky[count];
        for (int i = 0; i < rows.Length; i++)
        {
            int skySurfCount = rowCursor.ReadInt32();
            XPointer<int[]> skyStartSurfsPointer = context.PointerReader.ReadPointer<int[]>(rowCursor, XPointerResolutionMode.Direct);
            XPointer<GfxImageAsset> skyImagePointer = context.PointerReader.ReadPointer<GfxImageAsset>(rowCursor, XPointerResolutionMode.AliasCell);
            int skySamplerState = rowCursor.ReadInt32();

            IReadOnlyList<int> skyStartSurfs = ReadInt32Array(cursor, skyStartSurfsPointer.Untyped, skySurfCount, 4, context, $"GfxWorld.skies[{i}].skyStartSurfs");
            GfxImageAsset? skyImage = _imageLoader.LoadFromPointer(
                cursor,
                skyImagePointer.Untyped,
                context);
            rows[i] = new GfxSky
            {
                SkySurfCount = skySurfCount,
                SkyStartSurfsPointer = skyStartSurfsPointer,
                SkyStartSurfs = skyStartSurfs,
                SkyImagePointer = skyImagePointer,
                SkyImage = skyImage,
                SkySamplerState = skySamplerState
            };
        }

        EnsureConsumed(rowCursor, bytes.Length, "GfxSky[]");
        return rows;
    }

    private static GfxWorldDpvsPlanes ReadDpvsPlanesPayloads(
        FastFileCursor cursor,
        GfxWorldDpvsPlanes header,
        int planeCount,
        int nodeCount,
        DbLoadExecutionContext context)
    {
        int cellCount = Count(header.CellCount, "cellCount");
        IReadOnlyList<DpvsPlane> planes = ReadDpvsPlaneArray(cursor, header.PlanesPointer.Untyped, planeCount, context, "GfxWorld.dpvsPlanes.planes");
        IReadOnlyList<ushort> nodes = ReadUInt16Array(cursor, header.NodesPointer.Untyped, nodeCount, 4, context, "GfxWorld.dpvsPlanes.nodes");
        IReadOnlyList<uint> sceneEntCellBits = ReadPushed(context, XFileBlockType.RUNTIME, () => ReadUInt32Array(cursor, header.SceneEntCellBitsPointer.Untyped, checked(cellCount << 9), 4, context, "GfxWorld.dpvsPlanes.sceneEntCellBits"));

        return new GfxWorldDpvsPlanes
        {
            CellCount = header.CellCount,
            PlanesPointer = header.PlanesPointer,
            Planes = planes,
            NodesPointer = header.NodesPointer,
            Nodes = nodes,
            SceneEntCellBitsPointer = header.SceneEntCellBitsPointer,
            SceneEntCellBits = sceneEntCellBits
        };
    }

    private static IReadOnlyList<GfxCellTreeCount> ReadCellTreeCounts(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        byte[] bytes = LoadInlineArray(cursor, pointer, count, GfxCellTreeCount.SerializedSize, 4, context, "GfxWorld.cellTreeCounts");
        var c = new FastFileCursor(bytes);
        var rows = new GfxCellTreeCount[count];
        for (int i = 0; i < rows.Length; i++)
            rows[i] = new GfxCellTreeCount(c.ReadUInt32());

        return rows;
    }

    private static IReadOnlyList<GfxCellTree> ReadCellTrees(
        FastFileCursor cursor,
        XPointerReference pointer,
        IReadOnlyList<GfxCellTreeCount> counts,
        DbLoadExecutionContext context)
    {
        byte[] bytes = LoadInlineArray(cursor, pointer, counts.Count, GfxCellTree.SerializedSize, 128, context, "GfxWorld.cellTrees", out XBlockAddress cellTreesAddress);
        var rowCursor = new FastFileCursor(bytes, cellTreesAddress);
        var rows = new GfxCellTree[counts.Count];
        for (int i = 0; i < rows.Length; i++)
        {
            XPointer<GfxAabbTree[]> aabbTreesPointer = context.PointerReader.ReadPointer<GfxAabbTree[]>(rowCursor, XPointerResolutionMode.Direct);
            IReadOnlyList<GfxAabbTree> aabbTrees = ReadAabbTrees(cursor, aabbTreesPointer.Untyped, Count(counts[i].AabbTreeCount, $"cellTreeCounts[{i}].aabbTreeCount"), context, $"GfxWorld.cellTrees[{i}].aabbTrees");
            rows[i] = new GfxCellTree
            {
                AabbTreesPointer = aabbTreesPointer,
                AabbTrees = aabbTrees
            };
        }

        EnsureConsumed(rowCursor, bytes.Length, "GfxCellTree[]");
        return rows;
    }

    private static IReadOnlyList<GfxAabbTree> ReadAabbTrees(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context,
        string memberName)
    {
        byte[] bytes = LoadInlineArray(cursor, pointer, count, GfxAabbTree.SerializedSize, 4, context, memberName, out XBlockAddress aabbTreesAddress);
        var rowCursor = new FastFileCursor(bytes, aabbTreesAddress);
        var rows = new GfxAabbTree[count];
        for (int i = 0; i < rows.Length; i++)
        {
            var bounds = new ModelBounds
            {
                MidPoint = new ModelVec3
                {
                    X = rowCursor.ReadSingle(),
                    Y = rowCursor.ReadSingle(),
                    Z = rowCursor.ReadSingle()
                },
                HalfSize = new ModelVec3
                {
                    X = rowCursor.ReadSingle(),
                    Y = rowCursor.ReadSingle(),
                    Z = rowCursor.ReadSingle()
                }
            };
            ushort childCount = rowCursor.ReadUInt16();
            ushort surfaceCount = rowCursor.ReadUInt16();
            ushort startSurfIndex = rowCursor.ReadUInt16();
            ushort smodelIndexCount = rowCursor.ReadUInt16();
            XPointer<ushort[]> smodelIndexesPointer = context.PointerReader.ReadPointer<ushort[]>(rowCursor, XPointerResolutionMode.Direct);
            int childrenOffset = rowCursor.ReadInt32();
            IReadOnlyList<ushort> smodelIndexes = ReadUInt16ArrayAllowOffset(cursor, smodelIndexesPointer.Untyped, smodelIndexCount, 2, context, $"{memberName}[{i}].smodelIndexes");
            rows[i] = new GfxAabbTree
            {
                Bounds = bounds,
                ChildCount = childCount,
                SurfaceCount = surfaceCount,
                StartSurfIndex = startSurfIndex,
                SModelIndexCount = smodelIndexCount,
                SModelIndexesPointer = smodelIndexesPointer,
                SModelIndexes = smodelIndexes,
                ChildrenOffset = childrenOffset
            };
        }

        EnsureConsumed(rowCursor, bytes.Length, "GfxAabbTree[]");
        return rows;
    }

    private static IReadOnlyList<GfxCell> ReadCells(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        byte[] bytes = LoadInlineArray(cursor, pointer, count, GfxCell.SerializedSize, 4, context, "GfxWorld.cells", out XBlockAddress cellsAddress);
        var rowCursor = new FastFileCursor(bytes, cellsAddress);
        var rows = new GfxCell[count];
        for (int i = 0; i < rows.Length; i++)
        {
            var bounds = new ModelBounds
            {
                MidPoint = new ModelVec3
                {
                    X = rowCursor.ReadSingle(),
                    Y = rowCursor.ReadSingle(),
                    Z = rowCursor.ReadSingle()
                },
                HalfSize = new ModelVec3
                {
                    X = rowCursor.ReadSingle(),
                    Y = rowCursor.ReadSingle(),
                    Z = rowCursor.ReadSingle()
                }
            };
            int portalCount = rowCursor.ReadInt32();
            XPointer<GfxPortal[]> portalsPointer = context.PointerReader.ReadPointer<GfxPortal[]>(rowCursor, XPointerResolutionMode.Direct);
            byte reflectionProbeCount = rowCursor.ReadByte();
            byte[] pad21 = rowCursor.ReadBytes(3);
            XPointer<byte[]> reflectionProbesPointer = context.PointerReader.ReadPointer<byte[]>(rowCursor, XPointerResolutionMode.Direct);
            IReadOnlyList<GfxPortal> portals = ReadPortals(cursor, portalsPointer.Untyped, portalCount, context, $"GfxWorld.cells[{i}].portals");
            IReadOnlyList<byte> reflectionProbes = ReadByteArray(cursor, reflectionProbesPointer.Untyped, reflectionProbeCount, 1, context, $"GfxWorld.cells[{i}].reflectionProbes");
            rows[i] = new GfxCell
            {
                Bounds = bounds,
                PortalCount = portalCount,
                PortalsPointer = portalsPointer,
                Portals = portals,
                ReflectionProbeCount = reflectionProbeCount,
                Pad21 = pad21,
                ReflectionProbesPointer = reflectionProbesPointer,
                ReflectionProbes = reflectionProbes
            };
        }

        EnsureConsumed(rowCursor, bytes.Length, "GfxCell[]");
        return rows;
    }

    private static IReadOnlyList<GfxPortal> ReadPortals(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context,
        string memberName)
    {
        byte[] bytes = LoadInlineArray(cursor, pointer, count, GfxPortal.SerializedSize, 4, context, memberName, out XBlockAddress portalsAddress);
        var rowCursor = new FastFileCursor(bytes, portalsAddress);
        var rows = new GfxPortal[count];
        for (int i = 0; i < rows.Length; i++)
        {
            bool isQueued = rowCursor.ReadByte() != 0;
            bool isAncestor = rowCursor.ReadByte() != 0;
            byte recursionDepth = rowCursor.ReadByte();
            byte hullPointCount = rowCursor.ReadByte();
            int hullPointsRuntimePointer = rowCursor.ReadInt32();
            int queuedParentRuntimePointer = rowCursor.ReadInt32();
            var plane = new GfxPortalPlane(
                rowCursor.ReadSingle(),
                rowCursor.ReadSingle(),
                rowCursor.ReadSingle(),
                rowCursor.ReadSingle());
            XPointer<GfxPortalVertex[]> verticesPointer = context.PointerReader.ReadPointer<GfxPortalVertex[]>(rowCursor, XPointerResolutionMode.Direct);
            ushort cellIndex = rowCursor.ReadUInt16();
            byte vertexCount = rowCursor.ReadByte();
            byte pad23 = rowCursor.ReadByte();
            IReadOnlyList<float> hullAxis = ReadFloatValues(rowCursor, 6);
            IReadOnlyList<GfxPortalVertex> vertices = ReadPortalVertices(cursor, verticesPointer.Untyped, vertexCount, context, $"{memberName}[{i}].vertices");
            rows[i] = new GfxPortal
            {
                IsQueued = isQueued,
                IsAncestor = isAncestor,
                RecursionDepth = recursionDepth,
                HullPointCount = hullPointCount,
                HullPointsRuntimePointer = hullPointsRuntimePointer,
                QueuedParentRuntimePointer = queuedParentRuntimePointer,
                Plane = plane,
                VerticesPointer = verticesPointer,
                Vertices = vertices,
                CellIndex = cellIndex,
                VertexCount = vertexCount,
                Pad23 = pad23,
                HullAxis = hullAxis
            };
        }

        EnsureConsumed(rowCursor, bytes.Length, "GfxPortal[]");
        return rows;
    }

    private static IReadOnlyList<GfxPortalVertex> ReadPortalVertices(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context,
        string memberName)
    {
        byte[] bytes = LoadInlineArray(cursor, pointer, count, GfxPortalVertex.SerializedSize, 4, context, memberName);
        var c = new FastFileCursor(bytes);
        var rows = new GfxPortalVertex[count];
        for (int i = 0; i < rows.Length; i++)
            rows[i] = new GfxPortalVertex(c.ReadSingle(), c.ReadSingle(), c.ReadSingle());

        return rows;
    }

    private GfxWorldDraw ReadWorldDrawPayloads(
        FastFileCursor cursor,
        GfxWorldDraw header,
        DbLoadExecutionContext context)
    {
        int reflectionProbeCount = Count(header.ReflectionProbeCount, "reflectionProbeCount");
        int lightmapCount = header.LightmapCount;
        IReadOnlyList<GfxImageAsset?> reflectionProbeImages = ReadImagePointerArray(
            cursor,
            header.ReflectionProbeImagesPointer.Untyped,
            reflectionProbeCount,
            context,
            "GfxWorldDraw.reflectionProbes",
            out IReadOnlyList<XPointer<GfxImageAsset>> reflectionProbeImagePointers);
        IReadOnlyList<GfxReflectionProbe> reflectionProbeOrigins = ReadReflectionProbes(cursor, header.ReflectionProbeOriginsPointer.Untyped, reflectionProbeCount, context, "GfxWorldDraw.reflectionProbeOrigins");
        (IReadOnlyList<GfxTexture> reflectionProbeTextures, XBlockAddress? reflectionProbeTexturesAddress) = ReadPushed(context, XFileBlockType.RUNTIME, () => ReadTextures(cursor, header.ReflectionProbeTexturesPointer.Untyped, reflectionProbeCount, context, "GfxWorldDraw.reflectionProbeTextures"));
        IReadOnlyList<GfxLightmapArray> lightmaps = ReadLightmaps(cursor, header.LightmapsPointer.Untyped, lightmapCount, context);
        (IReadOnlyList<GfxTexture> lightmapPrimaryTextures, XBlockAddress? lightmapPrimaryTexturesAddress) = ReadPushed(context, XFileBlockType.RUNTIME, () => ReadTextures(cursor, header.LightmapPrimaryTexturesPointer.Untyped, lightmapCount, context, "GfxWorldDraw.lightmapPrimaryTextures"));
        (IReadOnlyList<GfxTexture> lightmapSecondaryTextures, XBlockAddress? lightmapSecondaryTexturesAddress) = ReadPushed(context, XFileBlockType.RUNTIME, () => ReadTextures(cursor, header.LightmapSecondaryTexturesPointer.Untyped, lightmapCount, context, "GfxWorldDraw.lightmapSecondaryTextures"));
        GfxImageAsset? lightmapOverridePrimary = _imageLoader.LoadFromPointer(
            cursor,
            header.LightmapOverridePrimaryPointer.Untyped,
            context);
        GfxImageAsset? lightmapOverrideSecondary = _imageLoader.LoadFromPointer(
            cursor,
            header.LightmapOverrideSecondaryPointer.Untyped,
            context);
        GfxWorldVertexData vertexData = ReadWorldVertexDataPayload(cursor, header.VertexData, Count(header.VertexCount, "vertexCount"), context);
        GfxWorldVertexLayerData vertexLayerData = ReadWorldVertexLayerDataPayload(cursor, header.VertexLayerData, Count(header.VertexLayerDataSize, "vertexLayerDataSize"), context);
        IReadOnlyList<ushort> indices = ReadUInt16Array(
            cursor,
            header.IndicesPointer.Untyped,
            header.IndexCount,
            sizeof(ushort),
            context,
            "GfxWorldDraw.indices");

        return new GfxWorldDraw
        {
            StagingAddress = header.StagingAddress,
            ReflectionProbeCount = header.ReflectionProbeCount,
            ReflectionProbeImagesPointer = header.ReflectionProbeImagesPointer,
            ReflectionProbeImagePointers = reflectionProbeImagePointers,
            ReflectionProbeImages = reflectionProbeImages,
            ReflectionProbeOriginsPointer = header.ReflectionProbeOriginsPointer,
            ReflectionProbeOrigins = reflectionProbeOrigins,
            ReflectionProbeTexturesPointer = header.ReflectionProbeTexturesPointer,
            ReflectionProbeTexturesAddress = reflectionProbeTexturesAddress,
            ReflectionProbeTextures = reflectionProbeTextures,
            LightmapCount = header.LightmapCount,
            LightmapsPointer = header.LightmapsPointer,
            Lightmaps = lightmaps,
            LightmapPrimaryTexturesPointer = header.LightmapPrimaryTexturesPointer,
            LightmapPrimaryTexturesAddress = lightmapPrimaryTexturesAddress,
            LightmapPrimaryTextures = lightmapPrimaryTextures,
            LightmapSecondaryTexturesPointer = header.LightmapSecondaryTexturesPointer,
            LightmapSecondaryTexturesAddress = lightmapSecondaryTexturesAddress,
            LightmapSecondaryTextures = lightmapSecondaryTextures,
            LightmapOverridePrimaryPointer = header.LightmapOverridePrimaryPointer,
            LightmapOverridePrimary = lightmapOverridePrimary,
            LightmapOverrideSecondaryPointer = header.LightmapOverrideSecondaryPointer,
            LightmapOverrideSecondary = lightmapOverrideSecondary,
            VertexCount = header.VertexCount,
            VertexData = vertexData,
            VertexLayerDataSize = header.VertexLayerDataSize,
            VertexLayerData = vertexLayerData,
            IndexCount = header.IndexCount,
            IndicesPointer = header.IndicesPointer,
            Indices = indices,
            IndexBufferRaw = header.IndexBufferRaw
        };
    }

    private GfxWorldVertexData ReadWorldVertexDataPayload(
        FastFileCursor cursor,
        GfxWorldVertexData header,
        int vertexCount,
        DbLoadExecutionContext context)
    {
        int byteCount = checked(vertexCount * 0x10);
        IReadOnlyList<byte> packedVertices = ReadByteArray(
            cursor,
            header.VerticesPointer.Untyped,
            byteCount,
            16,
            context,
            "GfxWorldDraw.vertexData.vertices",
            out XBlockAddress verticesAddress);
        return new GfxWorldVertexData
        {
            VerticesPointer = header.VerticesPointer,
            VerticesAddress = byteCount == 0 ? null : verticesAddress,
            PackedVertices = packedVertices,
            WorldVbHandle = header.WorldVbHandle,
            WorldVbOffset = header.WorldVbOffset
        };
    }

    private GfxWorldVertexLayerData ReadWorldVertexLayerDataPayload(
        FastFileCursor cursor,
        GfxWorldVertexLayerData header,
        int byteCount,
        DbLoadExecutionContext context)
    {
        XBlockAddress layerDataAddress = default;
        IReadOnlyList<byte> packedLayerData = ReadPushed(
            context,
            XFileBlockType.PHYSICAL,
            () => ReadByteArray(
                cursor,
                header.DataPointer.Untyped,
                byteCount,
                1,
                context,
                "GfxWorldDraw.vertexLayerData.data",
                out layerDataAddress));
        return new GfxWorldVertexLayerData
        {
            DataPointer = header.DataPointer,
            DataAddress = byteCount == 0 ? null : layerDataAddress,
            PackedLayerData = packedLayerData,
            LayerVbHandle = header.LayerVbHandle,
            LayerVbOffset = header.LayerVbOffset
        };
    }

    private IReadOnlyList<GfxImageAsset?> ReadImagePointerArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context,
        string memberName,
        out IReadOnlyList<XPointer<GfxImageAsset>> sourcePointers)
    {
        byte[] bytes = LoadInlineArray(cursor, pointer, count, sizeof(int), 4, context, memberName, out XBlockAddress imagesAddress);
        var rowCursor = new FastFileCursor(bytes, imagesAddress);
        var rows = new GfxImageAsset?[count];
        var pointers = new XPointer<GfxImageAsset>[count];
        for (int i = 0; i < rows.Length; i++)
        {
            XPointer<GfxImageAsset> imagePointer = context.PointerReader.ReadPointer<GfxImageAsset>(rowCursor, XPointerResolutionMode.AliasCell);
            pointers[i] = imagePointer;
            rows[i] = _imageLoader.LoadFromPointer(
                cursor,
                imagePointer.Untyped,
                context);
        }

        EnsureConsumed(rowCursor, bytes.Length, $"{memberName}[]");
        sourcePointers = Array.AsReadOnly(pointers);
        return rows;
    }

    private static IReadOnlyList<GfxReflectionProbe> ReadReflectionProbes(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context,
        string memberName)
    {
        byte[] bytes = LoadInlineArray(cursor, pointer, count, GfxReflectionProbe.SerializedSize, 4, context, memberName);
        var c = new FastFileCursor(bytes);
        var rows = new GfxReflectionProbe[count];
        for (int i = 0; i < rows.Length; i++)
            rows[i] = new GfxReflectionProbe(c.ReadSingle(), c.ReadSingle(), c.ReadSingle());

        return rows;
    }

    private static (IReadOnlyList<GfxTexture> Rows, XBlockAddress? Address) ReadTextures(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context,
        string memberName)
    {
        byte[] bytes = LoadInlineArray(
            cursor,
            pointer,
            count,
            GfxTexture.SerializedSize,
            4,
            context,
            memberName,
            out XBlockAddress allocatedAddress);
        var rows = new GfxTexture[count];
        for (int i = 0; i < rows.Length; i++)
            rows[i] = GfxTextureCodec.Decode(bytes.AsSpan(i * GfxTexture.SerializedSize, GfxTexture.SerializedSize));

        return (Array.AsReadOnly(rows), pointer.Type == PointerType.Null ? null : allocatedAddress);
    }

    private IReadOnlyList<GfxLightmapArray> ReadLightmaps(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        byte[] bytes = LoadInlineArray(cursor, pointer, count, GfxLightmapArray.SerializedSize, 4, context, "GfxWorldDraw.lightmaps", out XBlockAddress lightmapsAddress);
        var rowCursor = new FastFileCursor(bytes, lightmapsAddress);
        var rows = new GfxLightmapArray[count];
        for (int i = 0; i < rows.Length; i++)
        {
            XPointer<GfxImageAsset> primaryPointer = context.PointerReader.ReadPointer<GfxImageAsset>(rowCursor, XPointerResolutionMode.AliasCell);
            XPointer<GfxImageAsset> secondaryPointer = context.PointerReader.ReadPointer<GfxImageAsset>(rowCursor, XPointerResolutionMode.AliasCell);
            GfxImageAsset? primary = _imageLoader.LoadFromPointer(
                cursor,
                primaryPointer.Untyped,
                context);
            GfxImageAsset? secondary = _imageLoader.LoadFromPointer(
                cursor,
                secondaryPointer.Untyped,
                context);
            rows[i] = new GfxLightmapArray
            {
                PrimaryPointer = primaryPointer,
                Primary = primary,
                SecondaryPointer = secondaryPointer,
                Secondary = secondary
            };
        }

        EnsureConsumed(rowCursor, bytes.Length, "GfxLightmapArray[]");
        return rows;
    }

    internal static GfxLightGrid ReadLightGridPayloads(
        FastFileCursor cursor,
        GfxLightGrid header,
        DbLoadExecutionContext context)
    {
        int rowDataStartCount = ComputeLightGridRowDataStartCount(header);
        IReadOnlyList<ushort> rowDataStart = ReadUInt16Array(
            cursor,
            header.RowDataStartPointer.Untyped,
            rowDataStartCount,
            sizeof(ushort),
            context,
            "GfxLightGrid.rowDataStart");
        IReadOnlyList<byte> rawRowData = ReadByteArray(cursor, header.RawRowDataPointer.Untyped, Count(header.RawRowDataSize, "rawRowDataSize"), 1, context, "GfxLightGrid.rawRowData");
        IReadOnlyList<GfxLightGridEntry> entries = ReadLightGridEntries(cursor, header.EntriesPointer.Untyped, Count(header.EntryCount, "entryCount"), context);
        IReadOnlyList<GfxLightGridColors> colors = ReadLightGridColors(cursor, header.ColorsPointer.Untyped, Count(header.ColorCount, "colorCount"), context);
        return new GfxLightGrid
        {
            HasLightRegionsRaw = header.HasLightRegionsRaw,
            SunPrimaryLightIndex = header.SunPrimaryLightIndex,
            Mins = header.Mins,
            Maxs = header.Maxs,
            RowAxis = header.RowAxis,
            ColAxis = header.ColAxis,
            RowDataStartPointer = header.RowDataStartPointer,
            RowDataStart = rowDataStart,
            RawRowDataSize = header.RawRowDataSize,
            RawRowDataPointer = header.RawRowDataPointer,
            RawRowData = rawRowData,
            EntryCount = header.EntryCount,
            EntriesPointer = header.EntriesPointer,
            Entries = entries,
            ColorCount = header.ColorCount,
            ColorsPointer = header.ColorsPointer,
            Colors = colors
        };
    }

    private static IReadOnlyList<GfxLightGridEntry> ReadLightGridEntries(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        byte[] bytes = LoadInlineArray(cursor, pointer, count, GfxLightGridEntry.SerializedSize, 4, context, "GfxLightGrid.entries");
        var c = new FastFileCursor(bytes);
        var rows = new GfxLightGridEntry[count];
        for (int i = 0; i < rows.Length; i++)
            rows[i] = new GfxLightGridEntry(c.ReadUInt16(), c.ReadByte(), c.ReadByte());

        return rows;
    }

    private static IReadOnlyList<GfxLightGridColors> ReadLightGridColors(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        byte[] bytes = LoadInlineArray(cursor, pointer, count, GfxLightGridColors.SerializedSize, 4, context, "GfxLightGrid.colors");
        var rows = new GfxLightGridColors[count];
        for (int i = 0; i < rows.Length; i++)
            rows[i] = new GfxLightGridColors(bytes.AsSpan(i * GfxLightGridColors.SerializedSize, GfxLightGridColors.SerializedSize).ToArray());

        return rows;
    }

    private static IReadOnlyList<GfxBrushModel> ReadBrushModels(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        byte[] bytes = LoadInlineArray(cursor, pointer, count, GfxBrushModel.SerializedSize, 4, context, "GfxWorld.models");
        var c = new FastFileCursor(bytes);
        var rows = new GfxBrushModel[count];
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i] = new GfxBrushModel
            {
                WritableMins = ReadFloatValues(c, 3),
                WritableMaxs = ReadFloatValues(c, 3),
                BoundsMins = ReadFloatValues(c, 3),
                BoundsMaxs = ReadFloatValues(c, 3),
                Radius = c.ReadSingle(),
                SurfaceCount = c.ReadUInt16(),
                StartSurfIndex = c.ReadUInt16()
            };
        }

        return rows;
    }

    private IReadOnlyList<MaterialMemory> ReadMaterialMemory(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        byte[] bytes = LoadInlineArray(cursor, pointer, count, MaterialMemory.SerializedSize, 4, context, "GfxWorld.materialMemory", out XBlockAddress materialMemoryAddress);
        var rowCursor = new FastFileCursor(bytes, materialMemoryAddress);
        var rows = new MaterialMemory[count];
        for (int i = 0; i < rows.Length; i++)
        {
            XPointer<MaterialAsset> materialPointer = context.PointerReader.ReadPointer<MaterialAsset>(rowCursor, XPointerResolutionMode.AliasCell);
            int memory = rowCursor.ReadInt32();
            MaterialAsset? material = _materialLoader.LoadFromPointer(
                cursor,
                materialPointer.Untyped,
                context);
            rows[i] = new MaterialMemory
            {
                MaterialPointer = materialPointer,
                Material = material,
                Memory = memory
            };
        }

        EnsureConsumed(rowCursor, bytes.Length, "MaterialMemory[]");
        return rows;
    }

    private Sunflare ReadSunflarePayloads(
        FastFileCursor cursor,
        Sunflare header,
        DbLoadExecutionContext context)
    {
        MaterialAsset? spriteMaterial = _materialLoader.LoadFromPointer(
            cursor,
            header.SpriteMaterialPointer.Untyped,
            context);
        MaterialAsset? flareMaterial = _materialLoader.LoadFromPointer(
            cursor,
            header.FlareMaterialPointer.Untyped,
            context);
        return new Sunflare
        {
            HasValidDataRaw = header.HasValidDataRaw,
            SpriteMaterialPointer = header.SpriteMaterialPointer,
            SpriteMaterial = spriteMaterial,
            FlareMaterialPointer = header.FlareMaterialPointer,
            FlareMaterial = flareMaterial,
            SpriteSize = header.SpriteSize,
            FlareMinSize = header.FlareMinSize,
            FlareMinDot = header.FlareMinDot,
            FlareMaxSize = header.FlareMaxSize,
            FlareMaxDot = header.FlareMaxDot,
            FlareMaxAlpha = header.FlareMaxAlpha,
            FlareFadeInTime = header.FlareFadeInTime,
            FlareFadeOutTime = header.FlareFadeOutTime,
            BlindMinDot = header.BlindMinDot,
            BlindMaxDot = header.BlindMaxDot,
            BlindMaxDarken = header.BlindMaxDarken,
            BlindFadeInTime = header.BlindFadeInTime,
            BlindFadeOutTime = header.BlindFadeOutTime,
            GlareMinDot = header.GlareMinDot,
            GlareMaxDot = header.GlareMaxDot,
            GlareMaxLighten = header.GlareMaxLighten,
            GlareFadeInTime = header.GlareFadeInTime,
            GlareFadeOutTime = header.GlareFadeOutTime,
            SunFxPosition = header.SunFxPosition
        };
    }

    private static IReadOnlyList<GfxSceneDynModel> ReadSceneDynModels(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        byte[] bytes = LoadInlineArray(cursor, pointer, count, GfxSceneDynModel.SerializedSize, 4, context, "GfxWorld.sceneDynModels");
        var c = new FastFileCursor(bytes);
        var rows = new GfxSceneDynModel[count];
        for (int i = 0; i < rows.Length; i++)
            rows[i] = new GfxSceneDynModel(c.ReadUInt16(), c.ReadUInt16(), c.ReadUInt16());

        return rows;
    }

    private static IReadOnlyList<GfxSceneDynBrush> ReadSceneDynBrushes(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        byte[] bytes = LoadInlineArray(cursor, pointer, count, GfxSceneDynBrush.SerializedSize, 4, context, "GfxWorld.sceneDynBrushes");
        var c = new FastFileCursor(bytes);
        var rows = new GfxSceneDynBrush[count];
        for (int i = 0; i < rows.Length; i++)
            rows[i] = new GfxSceneDynBrush(c.ReadUInt16(), c.ReadUInt16());

        return rows;
    }

    private static IReadOnlyList<GfxShadowGeometry> ReadShadowGeometry(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        byte[] bytes = LoadInlineArray(cursor, pointer, count, GfxShadowGeometry.SerializedSize, 4, context, "GfxWorld.shadowGeom", out XBlockAddress shadowGeomAddress);
        var rowCursor = new FastFileCursor(bytes, shadowGeomAddress);
        var rows = new GfxShadowGeometry[count];
        for (int i = 0; i < rows.Length; i++)
        {
            ushort surfaceCount = rowCursor.ReadUInt16();
            ushort smodelCount = rowCursor.ReadUInt16();
            XPointer<ushort[]> sortedSurfIndexPointer = context.PointerReader.ReadPointer<ushort[]>(rowCursor, XPointerResolutionMode.Direct);
            XPointer<ushort[]> smodelIndexPointer = context.PointerReader.ReadPointer<ushort[]>(rowCursor, XPointerResolutionMode.Direct);
            IReadOnlyList<ushort> sortedSurfIndex = ReadUInt16Array(cursor, sortedSurfIndexPointer.Untyped, surfaceCount, 2, context, $"GfxWorld.shadowGeom[{i}].sortedSurfIndex");
            IReadOnlyList<ushort> smodelIndex = ReadUInt16Array(cursor, smodelIndexPointer.Untyped, smodelCount, 2, context, $"GfxWorld.shadowGeom[{i}].smodelIndex");
            rows[i] = new GfxShadowGeometry
            {
                SurfaceCount = surfaceCount,
                SModelCount = smodelCount,
                SortedSurfIndexPointer = sortedSurfIndexPointer,
                SortedSurfIndex = sortedSurfIndex,
                SModelIndexPointer = smodelIndexPointer,
                SModelIndex = smodelIndex
            };
        }

        EnsureConsumed(rowCursor, bytes.Length, "GfxShadowGeometry[]");
        return rows;
    }

    private static IReadOnlyList<GfxLightRegion> ReadLightRegions(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        byte[] bytes = LoadInlineArray(cursor, pointer, count, GfxLightRegion.SerializedSize, 4, context, "GfxWorld.lightRegions", out XBlockAddress lightRegionsAddress);
        var rowCursor = new FastFileCursor(bytes, lightRegionsAddress);
        var rows = new GfxLightRegion[count];
        for (int i = 0; i < rows.Length; i++)
        {
            int hullCount = rowCursor.ReadInt32();
            XPointer<GfxLightRegionHull[]> hullsPointer = context.PointerReader.ReadPointer<GfxLightRegionHull[]>(rowCursor, XPointerResolutionMode.Direct);
            IReadOnlyList<GfxLightRegionHull> hulls = ReadLightRegionHulls(cursor, hullsPointer.Untyped, hullCount, context, $"GfxWorld.lightRegions[{i}].hulls");
            rows[i] = new GfxLightRegion
            {
                HullCount = hullCount,
                HullsPointer = hullsPointer,
                Hulls = hulls
            };
        }

        EnsureConsumed(rowCursor, bytes.Length, "GfxLightRegion[]");
        return rows;
    }

    private static IReadOnlyList<GfxLightRegionHull> ReadLightRegionHulls(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context,
        string memberName)
    {
        byte[] bytes = LoadInlineArray(cursor, pointer, count, GfxLightRegionHull.SerializedSize, 4, context, memberName, out XBlockAddress hullsAddress);
        var rowCursor = new FastFileCursor(bytes, hullsAddress);
        var rows = new GfxLightRegionHull[count];
        for (int i = 0; i < rows.Length; i++)
        {
            IReadOnlyList<float> kdopMidPoint = ReadFloatValues(rowCursor, 9);
            IReadOnlyList<float> kdopHalfSize = ReadFloatValues(rowCursor, 9);
            uint axisCount = rowCursor.ReadUInt32();
            XPointer<GfxLightRegionAxis[]> axesPointer = context.PointerReader.ReadPointer<GfxLightRegionAxis[]>(rowCursor, XPointerResolutionMode.Direct);
            IReadOnlyList<GfxLightRegionAxis> axes = ReadLightRegionAxes(cursor, axesPointer.Untyped, Count(axisCount, $"{memberName}[{i}].axisCount"), context, $"{memberName}[{i}].axes");
            rows[i] = new GfxLightRegionHull
            {
                KdopMidPoint = kdopMidPoint,
                KdopHalfSize = kdopHalfSize,
                AxisCount = axisCount,
                AxesPointer = axesPointer,
                Axes = axes
            };
        }

        EnsureConsumed(rowCursor, bytes.Length, "GfxLightRegionHull[]");
        return rows;
    }

    private static IReadOnlyList<GfxLightRegionAxis> ReadLightRegionAxes(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context,
        string memberName)
    {
        byte[] bytes = LoadInlineArray(cursor, pointer, count, GfxLightRegionAxis.SerializedSize, 4, context, memberName);
        var c = new FastFileCursor(bytes);
        var rows = new GfxLightRegionAxis[count];
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i] = new GfxLightRegionAxis
            {
                Dir = ReadFloatValues(c, 3),
                MidPoint = c.ReadSingle(),
                HalfSize = c.ReadSingle()
            };
        }

        return rows;
    }

    private IReadOnlyList<GfxSurface> ReadSurfaces(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context,
        out XBlockAddress surfacesAddress)
    {
        byte[] bytes = LoadInlineArray(
            cursor,
            pointer,
            count,
            GfxSurface.SerializedSize,
            4,
            context,
            "GfxWorld.dpvs.surfaces",
            out surfacesAddress);
        var rowCursor = new FastFileCursor(bytes, surfacesAddress);
        var rows = new GfxSurface[count];
        for (int i = 0; i < rows.Length; i++)
        {
            SrfTriangles triangles = ReadSrfTriangles(rowCursor);
            XPointer<MaterialAsset> materialPointer = context.PointerReader.ReadPointer<MaterialAsset>(rowCursor, XPointerResolutionMode.AliasCell);
            byte lightmapIndex = rowCursor.ReadByte();
            byte reflectionProbeIndex = rowCursor.ReadByte();
            byte primaryLightIndex = rowCursor.ReadByte();
            GfxSurfaceFlags flags = (GfxSurfaceFlags)rowCursor.ReadByte();
            MaterialAsset? material = _materialLoader.LoadFromPointer(
                cursor,
                materialPointer.Untyped,
                context);
            rows[i] = new GfxSurface
            {
                Triangles = triangles,
                MaterialPointer = materialPointer,
                Material = material,
                LightmapIndex = lightmapIndex,
                ReflectionProbeIndex = reflectionProbeIndex,
                PrimaryLightIndex = primaryLightIndex,
                Flags = flags
            };
        }

        EnsureConsumed(rowCursor, bytes.Length, "GfxSurface[]");
        return rows;
    }

    private IReadOnlyList<GfxStaticModelDrawInst> ReadStaticModelDrawInsts(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        byte[] bytes = LoadInlineArray(cursor, pointer, count, GfxStaticModelDrawInst.SerializedSize, 4, context, "GfxWorld.dpvs.smodelDrawInsts", out XBlockAddress drawInstsAddress);
        var rowCursor = new FastFileCursor(bytes, drawInstsAddress);
        var rows = new GfxStaticModelDrawInst[count];
        for (int i = 0; i < rows.Length; i++)
        {
            GfxPackedPlacement placement = ReadPackedPlacement(rowCursor);
            XPointer<XModelAsset> modelPointer = context.PointerReader.ReadPointer<XModelAsset>(rowCursor, XPointerResolutionMode.AliasCell);
            ushort cullDist = rowCursor.ReadUInt16();
            ushort lightingHandle = rowCursor.ReadUInt16();
            byte reflectionProbeIndex = rowCursor.ReadByte();
            byte primaryLightIndex = rowCursor.ReadByte();
            GfxStaticModelDrawInstFlags flags =
                (GfxStaticModelDrawInstFlags)rowCursor.ReadByte();
            byte firstMaterialSkinIndex = rowCursor.ReadByte();
            var groundLighting = new GfxColor(rowCursor.ReadUInt32());
            XModelAsset? model = _xmodelLoader.LoadFromPointer(
                cursor,
                modelPointer.Untyped,
                context);
            rows[i] = new GfxStaticModelDrawInst
            {
                Placement = placement,
                ModelPointer = modelPointer,
                Model = model,
                CullDist = cullDist,
                LightingHandle = lightingHandle,
                ReflectionProbeIndex = reflectionProbeIndex,
                PrimaryLightIndex = primaryLightIndex,
                Flags = flags,
                FirstMaterialSkinIndex = firstMaterialSkinIndex,
                GroundLighting = groundLighting
            };
        }

        EnsureConsumed(rowCursor, bytes.Length, "GfxStaticModelDrawInst[]");
        return rows;
    }

    private GfxWorldDpvsStatic ReadDpvsStaticPayloads(
        FastFileCursor cursor,
        GfxWorldDpvsStatic header,
        int surfaceCount,
        DbLoadExecutionContext context)
    {
        int smodelCount = Count(header.SModelCount, "smodelCount");
        int staticSurfaceCount = Count(header.StaticSurfaceCount, "staticSurfaceCount");
        int smodelVisCount = Count(header.VisibilityCounts[6], "smodelVisDataCount");
        int surfaceVisCount = Count(header.VisibilityCounts[7], "surfaceVisDataCount");

        var smodelVisData = new IReadOnlyList<uint>[3];
        for (int i = 0; i < smodelVisData.Length; i++)
        {
            int index = i;
            smodelVisData[i] = ReadPushed(context, XFileBlockType.RUNTIME, () => ReadUInt32Array(cursor, header.SModelVisDataPointers[index].Untyped, smodelVisCount, 4, context, $"GfxWorld.dpvs.smodelVisData[{index}]"));
        }

        var surfaceVisData = new IReadOnlyList<uint>[3];
        for (int i = 0; i < surfaceVisData.Length; i++)
        {
            int index = i;
            surfaceVisData[i] = ReadPushed(context, XFileBlockType.RUNTIME, () => ReadUInt32Array(cursor, header.SurfaceVisDataPointers[index].Untyped, surfaceVisCount, 4, context, $"GfxWorld.dpvs.surfaceVisData[{index}]"));
        }

        IReadOnlyList<ushort> sortedSurfIndex = ReadUInt16Array(
            cursor,
            header.SortedSurfIndexPointer.Untyped,
            staticSurfaceCount,
            2,
            context,
            "GfxWorld.dpvs.sortedSurfIndex",
            out XBlockAddress sortedSurfIndexAddress);
        IReadOnlyList<GfxStaticModelInst> smodelInsts = ReadStaticModelInsts(cursor, header.SModelInstsPointer.Untyped, smodelCount, context);
        IReadOnlyList<GfxSurface> surfaces = ReadSurfaces(
            cursor,
            header.SurfacesPointer.Untyped,
            surfaceCount,
            context,
            out XBlockAddress surfacesAddress);
        IReadOnlyList<GfxSurfaceBounds> surfaceBounds = ReadSurfaceBounds(
            cursor,
            header.SurfaceBoundsPointer.Untyped,
            surfaceCount,
            context,
            out XBlockAddress surfaceBoundsAddress);
        IReadOnlyList<GfxStaticModelDrawInst> smodelDrawInsts = ReadStaticModelDrawInsts(cursor, header.SModelDrawInstsPointer.Untyped, smodelCount, context);
        XBlockAddress surfaceMaterialsAddress = default;
        IReadOnlyList<GfxMapDrawSurf> surfaceMaterials = ReadPushed(context, XFileBlockType.RUNTIME, () => ReadDrawSurfArray(cursor, header.SurfaceMaterialsPointer.Untyped, surfaceCount, context, "GfxWorld.dpvs.surfaceMaterials", out surfaceMaterialsAddress));
        XBlockAddress surfaceCastsSunShadowAddress = default;
        IReadOnlyList<uint> surfaceCastsSunShadow = ReadPushed(
            context,
            XFileBlockType.RUNTIME,
            () => ReadUInt32Array(
                cursor,
                header.SurfaceCastsSunShadowPointer.Untyped,
                surfaceVisCount,
                4,
                context,
                "GfxWorld.dpvs.surfaceCastsSunShadow",
                out surfaceCastsSunShadowAddress));

        return new GfxWorldDpvsStatic
        {
            SModelCount = header.SModelCount,
            StaticSurfaceCount = header.StaticSurfaceCount,
            LitSurfsBegin = header.LitSurfsBegin,
            LitSurfsEnd = header.LitSurfsEnd,
            VisibilityCounts = header.VisibilityCounts,
            SModelVisDataPointers = header.SModelVisDataPointers,
            SModelVisData = smodelVisData,
            SurfaceVisDataPointers = header.SurfaceVisDataPointers,
            SurfaceVisData = surfaceVisData,
            SortedSurfIndexPointer = header.SortedSurfIndexPointer,
            SortedSurfIndexAddress = sortedSurfIndexAddress,
            SortedSurfIndex = sortedSurfIndex,
            SModelInstsPointer = header.SModelInstsPointer,
            SModelInsts = smodelInsts,
            SurfacesPointer = header.SurfacesPointer,
            SurfacesAddress = surfacesAddress,
            Surfaces = surfaces,
            AuthoredSurfaceIndexByRuntimeSlot = Enumerable.Range(0, surfaces.Count).ToArray(),
            SurfaceBoundsPointer = header.SurfaceBoundsPointer,
            SurfaceBoundsAddress = surfaceBoundsAddress,
            SurfaceBounds = surfaceBounds,
            SModelDrawInstsPointer = header.SModelDrawInstsPointer,
            SModelDrawInsts = smodelDrawInsts,
            SurfaceMaterialsPointer = header.SurfaceMaterialsPointer,
            SurfaceMaterialsAddress = surfaceMaterialsAddress,
            SurfaceMaterials = surfaceMaterials,
            SurfaceCastsSunShadowPointer = header.SurfaceCastsSunShadowPointer,
            SurfaceCastsSunShadowAddress = surfaceCastsSunShadowAddress,
            SurfaceCastsSunShadow = surfaceCastsSunShadow,
            UsageCount = header.UsageCount
        };
    }

    private static IReadOnlyList<GfxStaticModelInst> ReadStaticModelInsts(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        byte[] bytes = LoadInlineArray(cursor, pointer, count, GfxStaticModelInst.SerializedSize, 4, context, "GfxWorld.dpvs.smodelInsts");
        var c = new FastFileCursor(bytes);
        var rows = new GfxStaticModelInst[count];
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i] = new GfxStaticModelInst
            {
                Bounds = new ModelBounds
                {
                    MidPoint = new ModelVec3
                    {
                        X = c.ReadSingle(),
                        Y = c.ReadSingle(),
                        Z = c.ReadSingle()
                    },
                    HalfSize = new ModelVec3
                    {
                        X = c.ReadSingle(),
                        Y = c.ReadSingle(),
                        Z = c.ReadSingle()
                    }
                },
                LightingOrigin = new ModelVec3
                {
                    X = c.ReadSingle(),
                    Y = c.ReadSingle(),
                    Z = c.ReadSingle()
                }
            };
        }

        return rows;
    }

    private static IReadOnlyList<GfxSurfaceBounds> ReadSurfaceBounds(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context,
        out XBlockAddress surfaceBoundsAddress)
    {
        byte[] bytes = LoadInlineArray(
            cursor,
            pointer,
            count,
            GfxSurfaceBounds.SerializedSize,
            4,
            context,
            "GfxWorld.dpvs.surfaceBounds",
            out surfaceBoundsAddress);
        var c = new FastFileCursor(bytes);
        var rows = new GfxSurfaceBounds[count];
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i] = new GfxSurfaceBounds
            {
                Bounds = new ModelBounds
                {
                    MidPoint = new ModelVec3
                    {
                        X = c.ReadSingle(),
                        Y = c.ReadSingle(),
                        Z = c.ReadSingle()
                    },
                    HalfSize = new ModelVec3
                    {
                        X = c.ReadSingle(),
                        Y = c.ReadSingle(),
                        Z = c.ReadSingle()
                    }
                },
                Unknown18To1F = c.ReadBytes(8)
            };
        }

        return rows;
    }

    private static IReadOnlyList<GfxMapDrawSurf> ReadDrawSurfArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context,
        string memberName,
        out XBlockAddress targetAddress)
    {
        byte[] bytes = LoadInlineArray(cursor, pointer, count, GfxMapDrawSurf.SerializedSize, 4, context, memberName, out targetAddress);
        var c = new FastFileCursor(bytes);
        var rows = new GfxMapDrawSurf[count];
        for (int i = 0; i < rows.Length; i++)
            rows[i] = new GfxMapDrawSurf(c.ReadUInt64());

        return rows;
    }

    private static GfxWorldDpvsDynamic ReadDpvsDynamicPayloads(
        FastFileCursor cursor,
        GfxWorldDpvsDynamic header,
        int cellCount,
        DbLoadExecutionContext context)
    {
        var cellBits = new IReadOnlyList<uint>[2];
        for (int i = 0; i < cellBits.Length; i++)
        {
            int index = i;
            int count = checked(Count(header.DynEntClientWordCount[index], $"dynEntClientWordCount[{index}]") * cellCount);
            cellBits[i] = ReadPushed(context, XFileBlockType.RUNTIME, () => ReadUInt32Array(cursor, header.DynEntCellBitsPointers[index].Untyped, count, 4, context, $"GfxWorld.dpvsDyn.dynEntCellBits[{index}]"));
        }

        var visData = new IReadOnlyList<byte>[6];
        int[] loadOrder = [0, 3, 1, 4, 2, 5];
        foreach (int i in loadOrder)
        {
            int index = i;
            int wordCountIndex = i >= 3 ? 1 : 0;
            int byteCount = checked(Count(header.DynEntClientWordCount[wordCountIndex], $"dynEntClientWordCount[{wordCountIndex}]") << 5);
            visData[i] = ReadPushed(context, XFileBlockType.RUNTIME, () => ReadByteArray(cursor, header.DynEntVisDataPointers[index].Untyped, byteCount, 16, context, $"GfxWorld.dpvsDyn.dynEntVisData[{index}]"));
        }

        return new GfxWorldDpvsDynamic
        {
            DynEntClientWordCount = header.DynEntClientWordCount,
            DynEntClientCount = header.DynEntClientCount,
            DynEntCellBitsPointers = header.DynEntCellBitsPointers,
            DynEntCellBits = cellBits,
            DynEntVisDataPointers = header.DynEntVisDataPointers,
            DynEntVisData = visData
        };
    }

    private static IReadOnlyList<GfxHeroOnlyLight> ReadHeroOnlyLights(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        byte[] bytes = LoadInlineArray(cursor, pointer, count, GfxHeroOnlyLight.SerializedSize, 4, context, "GfxWorld.heroOnlyLights");
        var rows = new GfxHeroOnlyLight[count];
        for (int i = 0; i < rows.Length; i++)
            rows[i] = new GfxHeroOnlyLight(bytes.AsSpan(i * GfxHeroOnlyLight.SerializedSize, GfxHeroOnlyLight.SerializedSize).ToArray());

        return rows;
    }

    private static SrfTriangles ReadSrfTriangles(FastFileCursor cursor)
    {
        return new SrfTriangles
        {
            VertexLayerData = cursor.ReadInt32(),
            BaseVertex = cursor.ReadInt32(),
            MinVertexIndex = cursor.ReadUInt32(),
            VertexCount = cursor.ReadUInt16(),
            TriCount = cursor.ReadUInt16(),
            BaseIndex = cursor.ReadInt32()
        };
    }

    private static GfxPackedPlacement ReadPackedPlacement(FastFileCursor cursor)
    {
        return new GfxPackedPlacement
        {
            Origin = ReadFloatValues(cursor, 3),
            PackedAxis = [cursor.ReadUInt32(), cursor.ReadUInt32(), cursor.ReadUInt32()],
            Scale = cursor.ReadSingle()
        };
    }

    private static IReadOnlyList<DpvsPlane> ReadDpvsPlaneArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context,
        string memberName)
    {
        byte[] bytes = LoadInlineArray(cursor, pointer, count, DpvsPlane.SerializedSize, 4, context, memberName);
        var c = new FastFileCursor(bytes);
        var rows = new DpvsPlane[count];
        for (int i = 0; i < rows.Length; i++)
            rows[i] = ReadDpvsPlane(c);

        return rows;
    }

    private static DpvsPlane ReadDpvsPlane(FastFileCursor cursor)
    {
        return new DpvsPlane(
            cursor.ReadSingle(),
            cursor.ReadSingle(),
            cursor.ReadSingle(),
            cursor.ReadSingle(),
            cursor.ReadByte(),
            cursor.ReadByte(),
            cursor.ReadUInt16());
    }

    private static IReadOnlyList<int> ReadInt32Array(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        int alignment,
        DbLoadExecutionContext context,
        string memberName)
    {
        byte[] bytes = LoadInlineArray(cursor, pointer, count, sizeof(int), alignment, context, memberName);
        var c = new FastFileCursor(bytes);
        var values = new int[count];
        for (int i = 0; i < values.Length; i++)
            values[i] = c.ReadInt32();

        return values;
    }

    private static IReadOnlyList<uint> ReadUInt32Array(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        int alignment,
        DbLoadExecutionContext context,
        string memberName)
    {
        return ReadUInt32Array(
            cursor,
            pointer,
            count,
            alignment,
            context,
            memberName,
            out _);
    }

    private static IReadOnlyList<uint> ReadUInt32Array(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        int alignment,
        DbLoadExecutionContext context,
        string memberName,
        out XBlockAddress targetAddress)
    {
        byte[] bytes = LoadInlineArray(
            cursor,
            pointer,
            count,
            sizeof(uint),
            alignment,
            context,
            memberName,
            out targetAddress);
        var c = new FastFileCursor(bytes);
        var values = new uint[count];
        for (int i = 0; i < values.Length; i++)
            values[i] = c.ReadUInt32();

        return values;
    }

    private static IReadOnlyList<ushort> ReadUInt16Array(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        int alignment,
        DbLoadExecutionContext context,
        string memberName)
    {
        return ReadUInt16Array(
            cursor,
            pointer,
            count,
            alignment,
            context,
            memberName,
            out _);
    }

    private static IReadOnlyList<ushort> ReadUInt16Array(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        int alignment,
        DbLoadExecutionContext context,
        string memberName,
        out XBlockAddress targetAddress)
    {
        byte[] bytes = LoadInlineArray(
            cursor,
            pointer,
            count,
            sizeof(ushort),
            alignment,
            context,
            memberName,
            out targetAddress);
        var c = new FastFileCursor(bytes);
        var values = new ushort[count];
        for (int i = 0; i < values.Length; i++)
            values[i] = c.ReadUInt16();

        return values;
    }

    internal static IReadOnlyList<ushort> ReadUInt16ArrayAllowOffset(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        int alignment,
        DbLoadExecutionContext context,
        string memberName)
    {
        if (pointer.Type == PointerType.Offset)
        {
            int byteCount = checked(count * sizeof(ushort));
            context.PointerReader.ValidateOffsetPointerRange<ushort[]>(
                pointer,
                byteCount,
                memberName);
            XBlockAddress targetAddress = pointer.PackedAddress ??
                throw new InvalidDataException(
                    $"Offset pointer 0x{pointer.Raw:X8} has no packed address for {memberName}.");
            byte[] bytes = context.Blocks.ReadBytes(targetAddress, byteCount);
            var targetCursor = new FastFileCursor(bytes, targetAddress);
            IReadOnlyList<ushort> values = ReadUInt16Values(targetCursor, count);
            return values;
        }

        return ReadUInt16Array(cursor, pointer, count, alignment, context, memberName);
    }

    private static IReadOnlyList<byte> ReadByteArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int byteCount,
        int alignment,
        DbLoadExecutionContext context,
        string memberName)
    {
        return LoadInlineArray(cursor, pointer, byteCount, 1, alignment, context, memberName);
    }

    private static IReadOnlyList<byte> ReadByteArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int byteCount,
        int alignment,
        DbLoadExecutionContext context,
        string memberName,
        out XBlockAddress targetAddress)
    {
        return LoadInlineArray(
            cursor,
            pointer,
            byteCount,
            1,
            alignment,
            context,
            memberName,
            out targetAddress);
    }

    private static byte[] LoadInlineArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        int stride,
        int alignment,
        DbLoadExecutionContext context,
        string memberName)
    {
        return LoadInlineArray(cursor, pointer, count, stride, alignment, context, memberName, out _);
    }

    private static byte[] LoadInlineArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        int stride,
        int alignment,
        DbLoadExecutionContext context,
        string memberName,
        out XBlockAddress targetAddress)
    {
        if (count < 0)
            throw new InvalidDataException($"{memberName} has negative count {count}.");

        if (pointer.Type == PointerType.Null)
        {
            if (count != 0)
                throw new InvalidDataException($"{memberName} is null with non-zero count {count}.");

            targetAddress = context.Blocks.CurrentAddress;
            return [];
        }

        if (pointer.Type == PointerType.Offset)
            throw new InvalidDataException($"{memberName} pointer 0x{pointer.Raw:X8} is packed, but the PS3 GfxWorld body proves inline child-array loading for this field.");

        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
            throw new InvalidDataException($"{memberName} pointer 0x{pointer.Raw:X8} is not inline/insert/null.");

        int byteCount = checked(count * stride);
        int sourceStart = cursor.Offset;
        targetAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment);
        byte[] bytes = context.Blocks.Load(cursor, byteCount, out XBlockAddress loadedAddress);
        if (loadedAddress != targetAddress)
            throw new InvalidDataException($"{memberName} pointer patched to {targetAddress}, but payload loaded at {loadedAddress}.");

        return bytes;
    }

    private static T ReadPushed<T>(
        DbLoadExecutionContext context,
        XFileBlockType block,
        Func<T> read)
    {
        context.Blocks.Push(block);
        try
        {
            return read();
        }
        finally
        {
            context.Blocks.Pop();
        }
    }

    private static IReadOnlyList<float> ReadFloatValues(FastFileCursor cursor, int count)
    {
        var values = new float[count];
        for (int i = 0; i < values.Length; i++)
            values[i] = cursor.ReadSingle();

        return values;
    }

    private static IReadOnlyList<ushort> ReadUInt16Values(FastFileCursor cursor, int count)
    {
        var values = new ushort[count];
        for (int i = 0; i < values.Length; i++)
            values[i] = cursor.ReadUInt16();

        return values;
    }


    private static int Count(uint value, string name)
    {
        if (value > int.MaxValue)
            throw new InvalidDataException($"{name} {value} exceeds supported managed count range.");

        return (int)value;
    }

    private static int Count(int value, string name)
    {
        if (value < 0)
            throw new InvalidDataException($"{name} {value} is negative.");

        return value;
    }

    private static int WordCount(int bitCount)
    {
        return checked((bitCount + 31) >> 5);
    }

    private static int GetDynCount(GfxWorldDpvsDynamic dpvsDyn, int index)
    {
        return Count(dpvsDyn.DynEntClientCount[index], $"dynEntClientCount[{index}]");
    }

    private static int ComputeLightGridRowDataStartCount(GfxLightGrid header)
    {
        if (header.Mins.Count < 3 || header.Maxs.Count < 3)
            throw new InvalidDataException("GfxLightGrid mins/maxs were not parsed.");

        GfxLightGridHorizontalAxis rowAxis = header.RowAxis;
        GfxLightGridHorizontalAxis colAxis = header.ColAxis;
        if ((rowAxis, colAxis) is not
            (GfxLightGridHorizontalAxis.X, GfxLightGridHorizontalAxis.Y) and not
            (GfxLightGridHorizontalAxis.Y, GfxLightGridHorizontalAxis.X))
        {
            throw new InvalidDataException(
                $"GfxLightGrid has invalid horizontal axes {rowAxis}/{colAxis}.");
        }

        return checked(header.Maxs[(int)rowAxis] - header.Mins[(int)rowAxis] + 1);
    }

    private static void EnsureConsumed(FastFileCursor cursor, int expected, string name)
    {
        if (cursor.Offset != expected)
            throw new InvalidDataException($"{name} consumed 0x{cursor.Offset:X} bytes instead of 0x{expected:X}.");
    }

}
