using IW4.Assets.Assets;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Math;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.Model;

/// <summary>
/// Frozen PS3 GfxWorld provider graph. Authored source storage is rebuilt in
/// native load order; post-load visibility, texture, and GPU state is reserved
/// source-free and never copied from the mutable runtime asset.
/// </summary>
internal sealed class GfxWorldLinkRecipe : AssetLinkRecipe
{
    private GfxWorldLinkRecipe(
        AssetKey key,
        string originalSerializedName,
        GfxWorldAsset definition,
        LinkAssetFreezeScope freeze)
        : base(
            key,
            originalSerializedName,
            freeze.FreezeProviderName(originalSerializedName, 0, "Asset.Name"))
    {
        Root = new StorageFreezer(freeze).FreezeWorld(definition, NameStorage);
    }

    internal override LinkStorageSymbol Root { get; }

    public static AssetLinkRecipe Freeze(
        AssetKey key,
        string originalSerializedName,
        GfxWorldAsset definition,
        LinkAssetFreezeScope freeze)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(freeze);
        if (originalSerializedName.StartsWith(','))
        {
            ValidateReferenceShape(definition, originalSerializedName);
            return ExternalAssetLinkRecipe.Create(
                key,
                XAssetType.GfxMap,
                originalSerializedName,
                freeze);
        }

        if (!string.Equals(
                definition.Name,
                originalSerializedName,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "GfxWorld.Name must equal the provider's exact serialized name.");
        }

        return new GfxWorldLinkRecipe(
            key,
            originalSerializedName,
            definition,
            freeze);
    }

    private static void ValidateReferenceShape(
        GfxWorldAsset definition,
        string originalSerializedName)
    {
        if (!string.Equals(
                definition.Name,
                originalSerializedName,
                StringComparison.Ordinal) ||
            definition.BaseName is not null ||
            definition.PlaneCount != 0 ||
            definition.NodeCount != 0 ||
            definition.SurfaceCount != 0 ||
            definition.SkyCount != 0 ||
            definition.Skies.Count != 0 ||
            definition.SunPrimaryLightIndex != 0 ||
            definition.PrimaryLightCount != 0 ||
            definition.SortKeyLitDecal != 0 ||
            definition.SortKeyEffectDecal != 0 ||
            definition.SortKeyEffectAuto != 0 ||
            definition.SortKeyDistortion != 0 ||
            definition.DpvsPlanes.CellCount != 0 ||
            definition.DpvsPlanes.Planes.Count != 0 ||
            definition.DpvsPlanes.Nodes.Count != 0 ||
            definition.CellTreeCounts.Count != 0 ||
            definition.CellTrees.Count != 0 ||
            definition.Cells.Count != 0 ||
            definition.WorldDraw.ReflectionProbeCount != 0 ||
            definition.WorldDraw.LightmapCount != 0 ||
            definition.WorldDraw.VertexCount != 0 ||
            definition.WorldDraw.VertexLayerDataSize != 0 ||
            definition.WorldDraw.IndexCount != 0 ||
            definition.Models.Count != 0 ||
            definition.MaterialMemory.Count != 0 ||
            definition.ShadowGeom.Count != 0 ||
            definition.LightRegions.Count != 0 ||
            definition.Dpvs.SModelCount != 0 ||
            definition.Dpvs.StaticSurfaceCount != 0 ||
            definition.Dpvs.Surfaces.Count != 0 ||
            definition.Dpvs.SModelDrawInsts.Count != 0 ||
            definition.DpvsDyn.DynEntClientCount.Any(value => value != 0) ||
            definition.HeroOnlyLightCount != 0 ||
            definition.HeroOnlyLights.Count != 0 ||
            definition.FogTypesAllowed != 0 ||
            definition.UmbraGateCount != 0 ||
            HasNonzeroValues(definition.Mins) ||
            HasNonzeroValues(definition.Maxs) ||
            definition.Checksum != 0 ||
            HasSunPayload(definition.Sun) ||
            HasNonzeroValues(definition.OutdoorLookupMatrix) ||
            definition.OutdoorImagePointer.Raw != 0 ||
            definition.OutdoorImage is not null ||
            HasWorldDrawPayload(definition.WorldDraw) ||
            HasLightGridPayload(definition.LightGrid) ||
            HasDpvsPayload(definition.Dpvs, definition.DpvsDyn) ||
            definition.MapVertexChecksum != 0 ||
            definition.Pad279To27B.Any(value => value != 0))
        {
            throw new InvalidDataException(
                "A comma-prefixed GfxWorld provider must have a zeroed reference body.");
        }

        static bool HasWorldDrawPayload(GfxWorldDraw value) =>
            value.ReflectionProbeImages.Count != 0 ||
            value.ReflectionProbeImagePointers.Any(pointer => pointer.Raw != 0) ||
            value.ReflectionProbeOrigins.Count != 0 ||
            value.ReflectionProbeTextures.Count != 0 ||
            value.Lightmaps.Count != 0 ||
            value.LightmapPrimaryTextures.Count != 0 ||
            value.LightmapSecondaryTextures.Count != 0 ||
            value.LightmapOverridePrimaryPointer.Raw != 0 ||
            value.LightmapOverridePrimary is not null ||
            value.LightmapOverrideSecondaryPointer.Raw != 0 ||
            value.LightmapOverrideSecondary is not null ||
            value.VertexData.PackedVertices.Count != 0 ||
            value.VertexData.WorldVbHandle != 0 ||
            value.VertexData.WorldVbOffset != 0 ||
            value.VertexLayerData.PackedLayerData.Count != 0 ||
            value.VertexLayerData.LayerVbHandle != 0 ||
            value.VertexLayerData.LayerVbOffset != 0 ||
            value.Indices.Count != 0 ||
            value.IndexBufferRaw != 0;

        static bool HasLightGridPayload(GfxLightGrid value) =>
            value.HasLightRegions != 0 ||
            value.SunPrimaryLightIndex != 0 ||
            value.Mins.Any(item => item != 0) ||
            value.Maxs.Any(item => item != 0) ||
            value.RowAxis != 0 ||
            value.ColAxis != 0 ||
            value.RowDataStart.Any(item => item != 0) ||
            value.RawRowDataSize != 0 ||
            value.RawRowData.Any(item => item != 0) ||
            value.EntryCount != 0 ||
            value.Entries.Count != 0 ||
            value.ColorCount != 0 ||
            value.Colors.Count != 0;

        static bool HasSunPayload(Sunflare value) =>
            value.HasValidData != 0 ||
            value.SpriteMaterialPointer.Raw != 0 ||
            value.SpriteMaterial is not null ||
            value.FlareMaterialPointer.Raw != 0 ||
            value.FlareMaterial is not null ||
            value.SpriteSize != 0 ||
            value.FlareMinSize != 0 ||
            value.FlareMinDot != 0 ||
            value.FlareMaxSize != 0 ||
            value.FlareMaxDot != 0 ||
            value.FlareMaxAlpha != 0 ||
            value.FlareFadeInTime != 0 ||
            value.FlareFadeOutTime != 0 ||
            value.BlindMinDot != 0 ||
            value.BlindMaxDot != 0 ||
            value.BlindMaxDarken != 0 ||
            value.BlindFadeInTime != 0 ||
            value.BlindFadeOutTime != 0 ||
            value.GlareMinDot != 0 ||
            value.GlareMaxDot != 0 ||
            value.GlareMaxLighten != 0 ||
            value.GlareFadeInTime != 0 ||
            value.GlareFadeOutTime != 0 ||
            HasNonzeroValues(value.SunFxPosition);

        static bool HasDpvsPayload(
            GfxWorldDpvsStatic value,
            GfxWorldDpvsDynamic dynamic) =>
            value.SModelCount != 0 ||
            value.StaticSurfaceCount != 0 ||
            value.LitSurfsBegin != 0 ||
            value.LitSurfsEnd != 0 ||
            value.VisibilityCounts.Any(item => item != 0) ||
            value.SModelVisData.Count != 0 ||
            value.SurfaceVisData.Count != 0 ||
            value.SortedSurfIndex.Count != 0 ||
            value.SModelInsts.Count != 0 ||
            value.Surfaces.Count != 0 ||
            value.SurfaceBounds.Count != 0 ||
            value.SModelDrawInsts.Count != 0 ||
            value.SurfaceMaterials.Count != 0 ||
            value.SurfaceCastsSunShadow.Count != 0 ||
            value.UsageCount != 0 ||
            dynamic.DynEntClientWordCount.Any(item => item != 0) ||
            dynamic.DynEntClientCount.Any(item => item != 0) ||
            dynamic.DynEntCellBits.Count != 0 ||
            dynamic.DynEntVisData.Count != 0;

        static bool HasNonzeroValues(IReadOnlyList<float> values) =>
            values.Any(value => BitConverter.SingleToInt32Bits(value) != 0);
    }

    private sealed class StorageFreezer
    {
        private readonly LinkAssetFreezeScope _freeze;

        public StorageFreezer(LinkAssetFreezeScope freeze) =>
            _freeze = freeze ?? throw new ArgumentNullException(nameof(freeze));

        public LinkStorageSymbol FreezeWorld(
            GfxWorldAsset value,
            LinkStorageSymbol nameStorage)
        {
            ValidateWorld(value);

            LinkStorageSymbol? baseName = value.BaseName is null
                ? null
                : _freeze.FreezeRequiredXString(
                    value.BaseName,
                    value.BaseNamePointer.Untyped,
                    "GfxWorld.BaseName");
            LinkStorageTarget? skies = FreezeSkies(value.SkiesPointer.Untyped, value.Skies);
            LinkStorageTarget? planes = FreezeArray(
                value.DpvsPlanes.PlanesPointer.Untyped,
                value.DpvsPlanes.Planes,
                DpvsPlane.SerializedSize,
                4,
                XFileBlockType.LARGE,
                WritePlane,
                "GfxWorld.DpvsPlanes.Planes");
            LinkStorageTarget? nodes = FreezeUInt16Array(
                value.DpvsPlanes.NodesPointer.Untyped,
                value.DpvsPlanes.Nodes,
                4,
                XFileBlockType.LARGE,
                "GfxWorld.DpvsPlanes.Nodes");
            LinkStorageSymbol? sceneEntCellBits = Runtime(
                checked(value.DpvsPlanes.CellCount * 0x200 * sizeof(uint)),
                4);
            LinkStorageTarget? cellTreeCounts = FreezeArray(
                value.CellTreeCountsPointer.Untyped,
                value.CellTreeCounts,
                GfxCellTreeCount.SerializedSize,
                4,
                XFileBlockType.LARGE,
                static (writer, item, _) => writer.WriteUInt32(item.AabbTreeCount),
                "GfxWorld.CellTreeCounts");
            LinkStorageTarget? cellTrees = FreezeCellTrees(
                value.CellTreesPointer.Untyped,
                value.CellTrees);
            LinkStorageTarget? cells = FreezeCells(
                value.CellsPointer.Untyped,
                value.Cells);
            WorldDrawTargets worldDraw = FreezeWorldDraw(value.WorldDraw);
            LightGridTargets lightGrid = FreezeLightGrid(value.LightGrid);
            LinkStorageTarget? models = FreezeArray(
                value.ModelsPointer.Untyped,
                value.Models,
                GfxBrushModel.SerializedSize,
                4,
                XFileBlockType.LARGE,
                WriteBrushModel,
                "GfxWorld.Models");
            LinkStorageTarget? materialMemory = FreezeMaterialMemory(
                value.MaterialMemoryPointer.Untyped,
                value.MaterialMemory);
            AssetDependency? sunSprite = FreezeProviderDependency(
                value.Sun.SpriteMaterialPointer.Untyped,
                value.Sun.SpriteMaterial,
                XAssetType.Material,
                "GfxWorld.Sun.SpriteMaterial");
            AssetDependency? sunFlare = FreezeProviderDependency(
                value.Sun.FlareMaterialPointer.Untyped,
                value.Sun.FlareMaterial,
                XAssetType.Material,
                "GfxWorld.Sun.FlareMaterial");
            AssetDependency? outdoorImage = FreezeProviderDependency(
                value.OutdoorImagePointer.Untyped,
                value.OutdoorImage,
                XAssetType.Image,
                "GfxWorld.OutdoorImage");

            int cellWordCount = WordCount(value.DpvsPlanes.CellCount);
            LinkStorageSymbol? cellCasterBits = Runtime(
                checked(value.DpvsPlanes.CellCount * cellWordCount * sizeof(uint)),
                4);
            LinkStorageSymbol? cellCasterBits2 = Runtime(
                checked(cellWordCount * sizeof(uint)),
                4);
            int dynModelCount = CountAt(value.DpvsDyn.DynEntClientCount, 0);
            int dynBrushCount = CountAt(value.DpvsDyn.DynEntClientCount, 1);
            int nonSunPrimaryLightCount = Math.Max(
                0,
                checked(value.PrimaryLightCount - value.SunPrimaryLightIndex - 1));
            LinkStorageSymbol? sceneDynModels = Runtime(
                checked(dynModelCount * GfxSceneDynModel.SerializedSize),
                4);
            LinkStorageSymbol? sceneDynBrushes = Runtime(
                checked(dynBrushCount * GfxSceneDynBrush.SerializedSize),
                4);
            LinkStorageSymbol? primaryLightEntityShadowVis = Runtime(
                checked(nonSunPrimaryLightCount * 0x2000 * sizeof(uint)),
                4);
            LinkStorageSymbol? primaryLightDynEntShadowVis0 = Runtime(
                checked(nonSunPrimaryLightCount * dynModelCount * sizeof(uint)),
                4);
            LinkStorageSymbol? primaryLightDynEntShadowVis1 = Runtime(
                checked(nonSunPrimaryLightCount * dynBrushCount * sizeof(uint)),
                4);
            LinkStorageSymbol? primaryLightForModelDynEnt = Runtime(
                dynModelCount,
                1);
            LinkStorageTarget? shadowGeom = FreezeShadowGeometry(
                value.ShadowGeomPointer.Untyped,
                value.ShadowGeom);
            LinkStorageTarget? lightRegions = FreezeLightRegions(
                value.LightRegionPointer.Untyped,
                value.LightRegions);
            DpvsStaticTargets dpvs = FreezeDpvsStatic(value.Dpvs);
            DpvsDynamicTargets dpvsDyn = FreezeDpvsDynamic(
                value.DpvsDyn,
                value.DpvsPlanes.CellCount);
            LinkStorageTarget? heroOnlyLights = FreezeArray(
                value.HeroOnlyLightsPointer.Untyped,
                value.HeroOnlyLights,
                GfxHeroOnlyLight.SerializedSize,
                4,
                XFileBlockType.LARGE,
                WriteHeroOnlyLight,
                "GfxWorld.HeroOnlyLights");
            LinkStorageSymbol umbraGateData = LinkStorageSymbol.SourceFree(
                XFileBlockType.VIRTUAL,
                checked(value.UmbraGateCount + 0x1000),
                0x1000,
                LinkMaterializationKind.VirtualReservation);
            LinkStorageSymbol umbraGateData2 = LinkStorageSymbol.SourceFree(
                XFileBlockType.VIRTUAL,
                checked(value.UmbraGateCount + 0x1000),
                0x1000,
                LinkMaterializationKind.VirtualReservation);

            var writer = new LinkTemplateWriter(GfxWorldAsset.SerializedSize);
            writer.Skip(2 * sizeof(int));
            writer.WriteInt32(value.PlaneCount);
            writer.WriteInt32(value.NodeCount);
            writer.WriteInt32(value.SurfaceCount);
            writer.WriteUInt32(value.SkyCount);
            writer.Skip(sizeof(int));
            writer.WriteInt32(value.SunPrimaryLightIndex);
            writer.WriteInt32(value.PrimaryLightCount);
            writer.WriteInt32(value.SortKeyLitDecal);
            writer.WriteInt32(value.SortKeyEffectDecal);
            writer.WriteInt32(value.SortKeyEffectAuto);
            writer.WriteInt32(value.SortKeyDistortion);
            writer.WriteInt32(value.DpvsPlanes.CellCount);
            writer.Skip(6 * sizeof(int));
            WriteWorldDraw(writer, value.WorldDraw);
            WriteLightGrid(writer, value.LightGrid);
            writer.WriteInt32(value.ModelCount);
            writer.Skip(sizeof(int));
            WriteFloats(writer, value.Mins, 3, "GfxWorld.Mins");
            WriteFloats(writer, value.Maxs, 3, "GfxWorld.Maxs");
            writer.WriteUInt32(value.Checksum);
            writer.WriteInt32(value.MaterialMemoryCount);
            writer.Skip(sizeof(int));
            WriteSunflare(writer, value.Sun);
            WriteFloats(
                writer,
                value.OutdoorLookupMatrix,
                16,
                "GfxWorld.OutdoorLookupMatrix");
            writer.Skip(11 * sizeof(int));
            WriteDpvsStatic(writer, value.Dpvs);
            WriteDpvsDynamic(writer, value.DpvsDyn);
            writer.WriteUInt32(value.MapVertexChecksum);
            writer.WriteUInt32(value.HeroOnlyLightCount);
            writer.Skip(sizeof(int));
            writer.WriteByte(value.FogTypesAllowed);
            writer.WriteBytes(value.Pad279To27B.Count == 0
                ? new byte[3]
                : value.Pad279To27B.ToArray());
            writer.WriteInt32(value.UmbraGateCount);
            writer.Skip(2 * sizeof(int));

            LinkStorageSymbol root = LinkStorageSymbol.CreateSourceBytes(
                XFileBlockType.TEMP,
                writer.Complete(),
                alignment: 4);
            root.FreezeOperations(CreateRootOperations(
                root,
                nameStorage,
                baseName,
                skies,
                planes,
                nodes,
                sceneEntCellBits,
                cellTreeCounts,
                cellTrees,
                cells,
                worldDraw,
                lightGrid,
                models,
                materialMemory,
                sunSprite,
                sunFlare,
                outdoorImage,
                cellCasterBits,
                cellCasterBits2,
                sceneDynModels,
                sceneDynBrushes,
                primaryLightEntityShadowVis,
                primaryLightDynEntShadowVis0,
                primaryLightDynEntShadowVis1,
                primaryLightForModelDynEnt,
                shadowGeom,
                lightRegions,
                dpvs,
                dpvsDyn,
                heroOnlyLights,
                umbraGateData,
                umbraGateData2));
            return root;
        }

        private static IEnumerable<LinkOperation> CreateRootOperations(
            LinkStorageSymbol root,
            LinkStorageSymbol name,
            LinkStorageSymbol? baseName,
            LinkStorageTarget? skies,
            LinkStorageTarget? planes,
            LinkStorageTarget? nodes,
            LinkStorageSymbol? sceneEntCellBits,
            LinkStorageTarget? cellTreeCounts,
            LinkStorageTarget? cellTrees,
            LinkStorageTarget? cells,
            WorldDrawTargets draw,
            LightGridTargets lightGrid,
            LinkStorageTarget? models,
            LinkStorageTarget? materialMemory,
            AssetDependency? sunSprite,
            AssetDependency? sunFlare,
            AssetDependency? outdoorImage,
            LinkStorageSymbol? cellCasterBits,
            LinkStorageSymbol? cellCasterBits2,
            LinkStorageSymbol? sceneDynModels,
            LinkStorageSymbol? sceneDynBrushes,
            LinkStorageSymbol? primaryLightEntityShadowVis,
            LinkStorageSymbol? primaryLightDynEntShadowVis0,
            LinkStorageSymbol? primaryLightDynEntShadowVis1,
            LinkStorageSymbol? primaryLightForModelDynEnt,
            LinkStorageTarget? shadowGeom,
            LinkStorageTarget? lightRegions,
            DpvsStaticTargets dpvs,
            DpvsDynamicTargets dpvsDyn,
            LinkStorageTarget? heroOnlyLights,
            LinkStorageSymbol umbraGateData,
            LinkStorageSymbol umbraGateData2)
        {
            yield return XString(root, 0x00, name, "Asset.Name");
            if (baseName is not null)
                yield return XString(root, 0x04, baseName, "GfxWorld.BaseName");
            if (skies is { } skiesValue)
                yield return Direct(root, 0x18, skiesValue, "GfxWorld.Skies");
            if (planes is { } planesValue)
                yield return Direct(root, 0x38, planesValue, "GfxWorld.DpvsPlanes.Planes");
            if (nodes is { } nodesValue)
                yield return Direct(root, 0x3c, nodesValue, "GfxWorld.DpvsPlanes.Nodes");
            if (sceneEntCellBits is not null)
                yield return Presence(root, 0x40, sceneEntCellBits, "GfxWorld.DpvsPlanes.SceneEntCellBits");
            if (cellTreeCounts is { } countsValue)
                yield return Direct(root, 0x44, countsValue, "GfxWorld.CellTreeCounts");
            if (cellTrees is { } treesValue)
                yield return Direct(root, 0x48, treesValue, "GfxWorld.CellTrees");
            if (cells is { } cellsValue)
                yield return Direct(root, 0x4c, cellsValue, "GfxWorld.Cells");

            foreach (LinkOperation operation in WorldDrawOperations(root, draw))
                yield return operation;
            foreach (LinkOperation operation in LightGridOperations(root, lightGrid))
                yield return operation;
            if (models is { } modelsValue)
                yield return Direct(root, 0xe0, modelsValue, "GfxWorld.Models");
            if (materialMemory is { } memoryValue)
                yield return Direct(root, 0x104, memoryValue, "GfxWorld.MaterialMemory");
            if (sunSprite is { } spriteValue)
                yield return Provider(root, 0x10c, spriteValue);
            if (sunFlare is { } flareValue)
                yield return Provider(root, 0x110, flareValue);
            if (outdoorImage is { } outdoorValue)
                yield return Provider(root, 0x1a8, outdoorValue);
            if (cellCasterBits is not null)
                yield return Presence(root, 0x1ac, cellCasterBits, "GfxWorld.CellCasterBits");
            if (cellCasterBits2 is not null)
                yield return Presence(root, 0x1b0, cellCasterBits2, "GfxWorld.CellCasterBits2");
            if (sceneDynModels is not null)
                yield return Presence(root, 0x1b4, sceneDynModels, "GfxWorld.SceneDynModels");
            if (sceneDynBrushes is not null)
                yield return Presence(root, 0x1b8, sceneDynBrushes, "GfxWorld.SceneDynBrushes");
            if (primaryLightEntityShadowVis is not null)
                yield return Presence(root, 0x1bc, primaryLightEntityShadowVis, "GfxWorld.PrimaryLightEntityShadowVis");
            if (primaryLightDynEntShadowVis0 is not null)
                yield return Presence(root, 0x1c0, primaryLightDynEntShadowVis0, "GfxWorld.PrimaryLightDynEntShadowVis0");
            if (primaryLightDynEntShadowVis1 is not null)
                yield return Presence(root, 0x1c4, primaryLightDynEntShadowVis1, "GfxWorld.PrimaryLightDynEntShadowVis1");
            if (primaryLightForModelDynEnt is not null)
                yield return Presence(root, 0x1c8, primaryLightForModelDynEnt, "GfxWorld.PrimaryLightForModelDynEnt");
            if (shadowGeom is { } shadowValue)
                yield return Direct(root, 0x1cc, shadowValue, "GfxWorld.ShadowGeom");
            if (lightRegions is { } regionsValue)
                yield return Direct(root, 0x1d0, regionsValue, "GfxWorld.LightRegions");

            foreach (LinkOperation operation in DpvsStaticOperations(root, dpvs))
                yield return operation;
            foreach (LinkOperation operation in DpvsDynamicOperations(root, dpvsDyn))
                yield return operation;
            if (heroOnlyLights is { } lightsValue)
                yield return Direct(root, 0x274, lightsValue, "GfxWorld.HeroOnlyLights");
            yield return Presence(root, 0x280, umbraGateData, "GfxWorld.UmbraGateData");
            yield return Presence(root, 0x284, umbraGateData2, "GfxWorld.UmbraGateData2");
        }

        private WorldDrawTargets FreezeWorldDraw(GfxWorldDraw value)
        {
            LinkStorageTarget? reflectionImages = FreezeProviderTable(
                value.ReflectionProbeImagesPointer.Untyped,
                value.ReflectionProbeImagePointers,
                value.ReflectionProbeImages,
                XAssetType.Image,
                "GfxWorld.WorldDraw.ReflectionProbeImages");
            LinkStorageTarget? reflectionOrigins = FreezeArray(
                value.ReflectionProbeOriginsPointer.Untyped,
                value.ReflectionProbeOrigins,
                GfxReflectionProbe.SerializedSize,
                4,
                XFileBlockType.LARGE,
                static (writer, item, _) =>
                {
                    WriteSingle(writer, item.OffsetX);
                    WriteSingle(writer, item.OffsetY);
                    WriteSingle(writer, item.OffsetZ);
                },
                "GfxWorld.WorldDraw.ReflectionProbeOrigins");
            LinkStorageSymbol? reflectionTextures = Runtime(
                checked((int)value.ReflectionProbeCount * GfxTexture.SerializedSize),
                4);
            LinkStorageTarget? lightmaps = FreezeLightmaps(
                value.LightmapsPointer.Untyped,
                value.Lightmaps);
            LinkStorageSymbol? lightmapPrimaryTextures = Runtime(
                checked(value.LightmapCount * GfxTexture.SerializedSize),
                4);
            LinkStorageSymbol? lightmapSecondaryTextures = Runtime(
                checked(value.LightmapCount * GfxTexture.SerializedSize),
                4);
            AssetDependency? lightmapOverridePrimary = FreezeProviderDependency(
                value.LightmapOverridePrimaryPointer.Untyped,
                value.LightmapOverridePrimary,
                XAssetType.Image,
                "GfxWorld.WorldDraw.LightmapOverridePrimary");
            AssetDependency? lightmapOverrideSecondary = FreezeProviderDependency(
                value.LightmapOverrideSecondaryPointer.Untyped,
                value.LightmapOverrideSecondary,
                XAssetType.Image,
                "GfxWorld.WorldDraw.LightmapOverrideSecondary");
            LinkStorageTarget? vertices = FreezeByteArray(
                value.VertexData.VerticesPointer.Untyped,
                value.VertexData.PackedVertices,
                16,
                XFileBlockType.LARGE,
                "GfxWorld.WorldDraw.VertexData.PackedVertices");
            LinkStorageTarget? vertexLayerData = FreezeByteArray(
                value.VertexLayerData.DataPointer.Untyped,
                value.VertexLayerData.PackedLayerData,
                1,
                XFileBlockType.PHYSICAL,
                "GfxWorld.WorldDraw.VertexLayerData.PackedLayerData");
            LinkStorageTarget? indices = FreezeUInt16Array(
                value.IndicesPointer.Untyped,
                value.Indices,
                2,
                XFileBlockType.LARGE,
                "GfxWorld.WorldDraw.Indices");
            return new WorldDrawTargets(
                reflectionImages,
                reflectionOrigins,
                reflectionTextures,
                lightmaps,
                lightmapPrimaryTextures,
                lightmapSecondaryTextures,
                lightmapOverridePrimary,
                lightmapOverrideSecondary,
                vertices,
                vertexLayerData,
                indices);
        }

        private static IEnumerable<LinkOperation> WorldDrawOperations(
            LinkStorageSymbol root,
            WorldDrawTargets value)
        {
            if (value.ReflectionImages is { } reflectionImages)
                yield return Direct(root, 0x54, reflectionImages, "GfxWorld.WorldDraw.ReflectionProbeImages");
            if (value.ReflectionOrigins is { } reflectionOrigins)
                yield return Direct(root, 0x58, reflectionOrigins, "GfxWorld.WorldDraw.ReflectionProbeOrigins");
            if (value.ReflectionTextures is not null)
                yield return Presence(root, 0x5c, value.ReflectionTextures, "GfxWorld.WorldDraw.ReflectionProbeTextures");
            if (value.Lightmaps is { } lightmaps)
                yield return Direct(root, 0x64, lightmaps, "GfxWorld.WorldDraw.Lightmaps");
            if (value.LightmapPrimaryTextures is not null)
                yield return Presence(root, 0x68, value.LightmapPrimaryTextures, "GfxWorld.WorldDraw.LightmapPrimaryTextures");
            if (value.LightmapSecondaryTextures is not null)
                yield return Presence(root, 0x6c, value.LightmapSecondaryTextures, "GfxWorld.WorldDraw.LightmapSecondaryTextures");
            if (value.LightmapOverridePrimary is { } primary)
                yield return Provider(root, 0x70, primary);
            if (value.LightmapOverrideSecondary is { } secondary)
                yield return Provider(root, 0x74, secondary);
            if (value.Vertices is { } vertices)
                yield return Direct(root, 0x7c, vertices, "GfxWorld.WorldDraw.VertexData.PackedVertices");
            if (value.VertexLayerData is { } layers)
                yield return Direct(root, 0x8c, layers, "GfxWorld.WorldDraw.VertexLayerData.PackedLayerData");
            if (value.Indices is { } indices)
                yield return Direct(root, 0x9c, indices, "GfxWorld.WorldDraw.Indices");
        }

        private LightGridTargets FreezeLightGrid(GfxLightGrid value) => new(
            FreezeUInt16Array(
                value.RowDataStartPointer.Untyped,
                value.RowDataStart,
                2,
                XFileBlockType.LARGE,
                "GfxWorld.LightGrid.RowDataStart"),
            FreezeByteArray(
                value.RawRowDataPointer.Untyped,
                value.RawRowData,
                1,
                XFileBlockType.LARGE,
                "GfxWorld.LightGrid.RawRowData"),
            FreezeArray(
                value.EntriesPointer.Untyped,
                value.Entries,
                GfxLightGridEntry.SerializedSize,
                4,
                XFileBlockType.LARGE,
                static (writer, item, _) =>
                {
                    writer.WriteUInt16(item.ColorsIndex);
                    writer.WriteByte(item.PrimaryLightIndex);
                    writer.WriteByte(item.NeedsTrace);
                },
                "GfxWorld.LightGrid.Entries"),
            FreezeArray(
                value.ColorsPointer.Untyped,
                value.Colors,
                GfxLightGridColors.SerializedSize,
                4,
                XFileBlockType.LARGE,
                static (writer, item, path) =>
                {
                    RequireCount(item.RgbBytes, GfxLightGridColors.SerializedSize, $"{path}.RgbBytes");
                    writer.WriteBytes(item.RgbBytes.ToArray());
                },
                "GfxWorld.LightGrid.Colors"));

        private static IEnumerable<LinkOperation> LightGridOperations(
            LinkStorageSymbol root,
            LightGridTargets value)
        {
            if (value.RowDataStart is { } rows)
                yield return Direct(root, 0xc0, rows, "GfxWorld.LightGrid.RowDataStart");
            if (value.RawRowData is { } raw)
                yield return Direct(root, 0xc8, raw, "GfxWorld.LightGrid.RawRowData");
            if (value.Entries is { } entries)
                yield return Direct(root, 0xd0, entries, "GfxWorld.LightGrid.Entries");
            if (value.Colors is { } colors)
                yield return Direct(root, 0xd8, colors, "GfxWorld.LightGrid.Colors");
        }

        private LinkStorageTarget? FreezeSkies(
            XPointerReference pointer,
            IReadOnlyList<GfxSky> values)
        {
            var starts = new LinkStorageTarget?[values.Count];
            var images = new AssetDependency?[values.Count];
            var writer = new LinkTemplateWriter(
                checked(values.Count * GfxSky.SerializedSize));
            for (int index = 0; index < values.Count; index++)
            {
                GfxSky value = values[index] ?? throw NullRow("GfxWorld.Skies", index);
                string path = $"GfxWorld.Skies[{index}]";
                starts[index] = FreezeInt32Array(
                    value.SkyStartSurfsPointer.Untyped,
                    value.SkyStartSurfs,
                    4,
                    XFileBlockType.LARGE,
                    $"{path}.SkyStartSurfs");
                images[index] = FreezeProviderDependency(
                    value.SkyImagePointer.Untyped,
                    value.SkyImage,
                    XAssetType.Image,
                    $"{path}.SkyImage");
                writer.WriteInt32(value.SkySurfCount);
                writer.Skip(2 * sizeof(int));
                writer.WriteInt32(value.SkySamplerState);
            }
            return FreezeTable(
                pointer,
                values.Count,
                writer,
                4,
                (owner, addend) => SkyOperations(owner, addend, starts, images),
                "GfxWorld.Skies");
        }

        private static IEnumerable<LinkOperation> SkyOperations(
            LinkStorageSymbol owner,
            int addend,
            IReadOnlyList<LinkStorageTarget?> starts,
            IReadOnlyList<AssetDependency?> images)
        {
            for (int index = 0; index < starts.Count; index++)
            {
                int row = checked(addend + index * GfxSky.SerializedSize);
                if (starts[index] is { } start)
                    yield return Direct(owner, row + 0x04, start, $"GfxWorld.Skies[{index}].SkyStartSurfs");
                if (images[index] is { } image)
                    yield return Provider(owner, row + 0x08, image);
            }
        }

        private LinkStorageTarget? FreezeCellTrees(
            XPointerReference pointer,
            IReadOnlyList<GfxCellTree> values)
        {
            var trees = new LinkStorageTarget?[values.Count];
            var writer = new LinkTemplateWriter(
                checked(values.Count * GfxCellTree.SerializedSize));
            for (int index = 0; index < values.Count; index++)
            {
                GfxCellTree value = values[index] ?? throw NullRow("GfxWorld.CellTrees", index);
                trees[index] = FreezeAabbTrees(
                    value.AabbTreesPointer.Untyped,
                    value.AabbTrees,
                    $"GfxWorld.CellTrees[{index}].AabbTrees");
                writer.Skip(sizeof(int));
            }
            return FreezeTable(
                pointer,
                values.Count,
                writer,
                128,
                (owner, addend) => trees
                    .Select((target, index) => (target, index))
                    .Where(item => item.target is not null)
                    .Select(item => Direct(
                        owner,
                        checked(addend + item.index * GfxCellTree.SerializedSize),
                        item.target!.Value,
                        $"GfxWorld.CellTrees[{item.index}].AabbTrees")),
                "GfxWorld.CellTrees");
        }

        private LinkStorageTarget? FreezeAabbTrees(
            XPointerReference pointer,
            IReadOnlyList<GfxAabbTree> values,
            string fieldPath)
        {
            var indexes = new LinkStorageTarget?[values.Count];
            var writer = new LinkTemplateWriter(
                checked(values.Count * GfxAabbTree.SerializedSize));
            for (int index = 0; index < values.Count; index++)
            {
                GfxAabbTree value = values[index] ?? throw NullRow(fieldPath, index);
                string path = $"{fieldPath}[{index}]";
                indexes[index] = FreezeUInt16View(
                    value.SModelIndexesPointer.Untyped,
                    value.SModelIndexes,
                    2,
                    XFileBlockType.LARGE,
                    $"{path}.SModelIndexes");
                WriteBounds(writer, value.Bounds, $"{path}.Bounds");
                writer.WriteUInt16(value.ChildCount);
                writer.WriteUInt16(value.SurfaceCount);
                writer.WriteUInt16(value.StartSurfIndex);
                writer.WriteUInt16(value.SModelIndexCount);
                writer.Skip(sizeof(int));
                writer.WriteInt32(value.ChildrenOffset);
            }
            return FreezeTable(
                pointer,
                values.Count,
                writer,
                4,
                (owner, addend) => indexes
                    .Select((target, index) => (target, index))
                    .Where(item => item.target is not null)
                    .Select(item => Direct(
                        owner,
                        checked(addend + item.index * GfxAabbTree.SerializedSize + 0x20),
                        item.target!.Value,
                        $"{fieldPath}[{item.index}].SModelIndexes")),
                fieldPath);
        }

        private LinkStorageTarget? FreezeCells(
            XPointerReference pointer,
            IReadOnlyList<GfxCell> values)
        {
            var portals = new LinkStorageTarget?[values.Count];
            var probes = new LinkStorageTarget?[values.Count];
            var writer = new LinkTemplateWriter(
                checked(values.Count * GfxCell.SerializedSize));
            for (int index = 0; index < values.Count; index++)
            {
                GfxCell value = values[index] ?? throw NullRow("GfxWorld.Cells", index);
                string path = $"GfxWorld.Cells[{index}]";
                portals[index] = FreezePortals(
                    value.PortalsPointer.Untyped,
                    value.Portals,
                    $"{path}.Portals");
                probes[index] = FreezeByteArray(
                    value.ReflectionProbesPointer.Untyped,
                    value.ReflectionProbes,
                    1,
                    XFileBlockType.LARGE,
                    $"{path}.ReflectionProbes");
                WriteBounds(writer, value.Bounds, $"{path}.Bounds");
                writer.WriteInt32(value.PortalCount);
                writer.Skip(sizeof(int));
                writer.WriteByte(value.ReflectionProbeCount);
                writer.WriteBytes(value.Pad21.ToArray());
                writer.Skip(sizeof(int));
            }
            return FreezeTable(
                pointer,
                values.Count,
                writer,
                4,
                (owner, addend) => CellOperations(owner, addend, portals, probes),
                "GfxWorld.Cells");
        }

        private static IEnumerable<LinkOperation> CellOperations(
            LinkStorageSymbol owner,
            int addend,
            IReadOnlyList<LinkStorageTarget?> portals,
            IReadOnlyList<LinkStorageTarget?> probes)
        {
            for (int index = 0; index < portals.Count; index++)
            {
                int row = checked(addend + index * GfxCell.SerializedSize);
                if (portals[index] is { } portal)
                    yield return Direct(owner, row + 0x1c, portal, $"GfxWorld.Cells[{index}].Portals");
                if (probes[index] is { } probe)
                    yield return Direct(owner, row + 0x24, probe, $"GfxWorld.Cells[{index}].ReflectionProbes");
            }
        }

        private LinkStorageTarget? FreezePortals(
            XPointerReference pointer,
            IReadOnlyList<GfxPortal> values,
            string fieldPath)
        {
            var vertices = new LinkStorageTarget?[values.Count];
            var writer = new LinkTemplateWriter(
                checked(values.Count * GfxPortal.SerializedSize));
            for (int index = 0; index < values.Count; index++)
            {
                GfxPortal value = values[index] ?? throw NullRow(fieldPath, index);
                string path = $"{fieldPath}[{index}]";
                vertices[index] = FreezeArray(
                    value.VerticesPointer.Untyped,
                    value.Vertices,
                    GfxPortalVertex.SerializedSize,
                    4,
                    XFileBlockType.LARGE,
                    static (childWriter, item, _) =>
                    {
                        WriteSingle(childWriter, item.X);
                        WriteSingle(childWriter, item.Y);
                        WriteSingle(childWriter, item.Z);
                    },
                    $"{path}.Vertices");
                writer.Skip(3 * sizeof(int));
                WriteSingle(writer, value.Plane.NormalX);
                WriteSingle(writer, value.Plane.NormalY);
                WriteSingle(writer, value.Plane.NormalZ);
                WriteSingle(writer, value.Plane.Distance);
                writer.Skip(sizeof(int));
                writer.WriteUInt16(value.CellIndex);
                writer.WriteByte(value.VertexCount);
                writer.WriteByte(value.Pad23);
                WriteFloats(writer, value.HullAxis, 6, $"{path}.HullAxis");
            }
            return FreezeTable(
                pointer,
                values.Count,
                writer,
                4,
                (owner, addend) => vertices
                    .Select((target, index) => (target, index))
                    .Where(item => item.target is not null)
                    .Select(item => Direct(
                        owner,
                        checked(addend + item.index * GfxPortal.SerializedSize + 0x1c),
                        item.target!.Value,
                        $"{fieldPath}[{item.index}].Vertices")),
                fieldPath);
        }

        private LinkStorageTarget? FreezeLightmaps(
            XPointerReference pointer,
            IReadOnlyList<GfxLightmapArray> values)
        {
            var primary = new AssetDependency?[values.Count];
            var secondary = new AssetDependency?[values.Count];
            var writer = new LinkTemplateWriter(
                checked(values.Count * GfxLightmapArray.SerializedSize));
            for (int index = 0; index < values.Count; index++)
            {
                GfxLightmapArray value = values[index] ?? throw NullRow("GfxWorld.WorldDraw.Lightmaps", index);
                primary[index] = FreezeProviderDependency(
                    value.PrimaryPointer.Untyped,
                    value.Primary,
                    XAssetType.Image,
                    $"GfxWorld.WorldDraw.Lightmaps[{index}].Primary");
                secondary[index] = FreezeProviderDependency(
                    value.SecondaryPointer.Untyped,
                    value.Secondary,
                    XAssetType.Image,
                    $"GfxWorld.WorldDraw.Lightmaps[{index}].Secondary");
                writer.Skip(2 * sizeof(int));
            }
            return FreezeTable(
                pointer,
                values.Count,
                writer,
                4,
                (owner, addend) => LightmapOperations(owner, addend, primary, secondary),
                "GfxWorld.WorldDraw.Lightmaps");
        }

        private static IEnumerable<LinkOperation> LightmapOperations(
            LinkStorageSymbol owner,
            int addend,
            IReadOnlyList<AssetDependency?> primary,
            IReadOnlyList<AssetDependency?> secondary)
        {
            for (int index = 0; index < primary.Count; index++)
            {
                int row = checked(addend + index * GfxLightmapArray.SerializedSize);
                if (primary[index] is { } primaryValue)
                    yield return Provider(owner, row, primaryValue);
                if (secondary[index] is { } secondaryValue)
                    yield return Provider(owner, row + 0x04, secondaryValue);
            }
        }

        private LinkStorageTarget? FreezeMaterialMemory(
            XPointerReference pointer,
            IReadOnlyList<MaterialMemory> values)
        {
            var materials = new AssetDependency?[values.Count];
            var writer = new LinkTemplateWriter(
                checked(values.Count * MaterialMemory.SerializedSize));
            for (int index = 0; index < values.Count; index++)
            {
                MaterialMemory value = values[index] ?? throw NullRow("GfxWorld.MaterialMemory", index);
                materials[index] = FreezeProviderDependency(
                    value.MaterialPointer.Untyped,
                    value.Material,
                    XAssetType.Material,
                    $"GfxWorld.MaterialMemory[{index}].Material");
                writer.Skip(sizeof(int));
                writer.WriteInt32(value.Memory);
            }
            return FreezeTable(
                pointer,
                values.Count,
                writer,
                4,
                (owner, addend) => materials
                    .Select((dependency, index) => (dependency, index))
                    .Where(item => item.dependency is not null)
                    .Select(item => Provider(
                        owner,
                        checked(addend + item.index * MaterialMemory.SerializedSize),
                        item.dependency!.Value)),
                "GfxWorld.MaterialMemory");
        }

        private LinkStorageTarget? FreezeShadowGeometry(
            XPointerReference pointer,
            IReadOnlyList<GfxShadowGeometry> values)
        {
            var surfaces = new LinkStorageTarget?[values.Count];
            var models = new LinkStorageTarget?[values.Count];
            var writer = new LinkTemplateWriter(
                checked(values.Count * GfxShadowGeometry.SerializedSize));
            for (int index = 0; index < values.Count; index++)
            {
                GfxShadowGeometry value = values[index] ?? throw NullRow("GfxWorld.ShadowGeom", index);
                string path = $"GfxWorld.ShadowGeom[{index}]";
                surfaces[index] = FreezeUInt16Array(
                    value.SortedSurfIndexPointer.Untyped,
                    value.SortedSurfIndex,
                    2,
                    XFileBlockType.LARGE,
                    $"{path}.SortedSurfIndex");
                models[index] = FreezeUInt16Array(
                    value.SModelIndexPointer.Untyped,
                    value.SModelIndex,
                    2,
                    XFileBlockType.LARGE,
                    $"{path}.SModelIndex");
                writer.WriteUInt16(value.SurfaceCount);
                writer.WriteUInt16(value.SModelCount);
                writer.Skip(2 * sizeof(int));
            }
            return FreezeTable(
                pointer,
                values.Count,
                writer,
                4,
                (owner, addend) => ShadowOperations(owner, addend, surfaces, models),
                "GfxWorld.ShadowGeom");
        }

        private static IEnumerable<LinkOperation> ShadowOperations(
            LinkStorageSymbol owner,
            int addend,
            IReadOnlyList<LinkStorageTarget?> surfaces,
            IReadOnlyList<LinkStorageTarget?> models)
        {
            for (int index = 0; index < surfaces.Count; index++)
            {
                int row = checked(addend + index * GfxShadowGeometry.SerializedSize);
                if (surfaces[index] is { } surface)
                    yield return Direct(owner, row + 0x04, surface, $"GfxWorld.ShadowGeom[{index}].SortedSurfIndex");
                if (models[index] is { } model)
                    yield return Direct(owner, row + 0x08, model, $"GfxWorld.ShadowGeom[{index}].SModelIndex");
            }
        }

        private LinkStorageTarget? FreezeLightRegions(
            XPointerReference pointer,
            IReadOnlyList<GfxLightRegion> values)
        {
            var hulls = new LinkStorageTarget?[values.Count];
            var writer = new LinkTemplateWriter(
                checked(values.Count * GfxLightRegion.SerializedSize));
            for (int index = 0; index < values.Count; index++)
            {
                GfxLightRegion value = values[index] ?? throw NullRow("GfxWorld.LightRegions", index);
                hulls[index] = FreezeLightRegionHulls(
                    value.HullsPointer.Untyped,
                    value.Hulls,
                    $"GfxWorld.LightRegions[{index}].Hulls");
                writer.WriteInt32(value.HullCount);
                writer.Skip(sizeof(int));
            }
            return FreezeTable(
                pointer,
                values.Count,
                writer,
                4,
                (owner, addend) => hulls
                    .Select((target, index) => (target, index))
                    .Where(item => item.target is not null)
                    .Select(item => Direct(
                        owner,
                        checked(addend + item.index * GfxLightRegion.SerializedSize + 0x04),
                        item.target!.Value,
                        $"GfxWorld.LightRegions[{item.index}].Hulls")),
                "GfxWorld.LightRegions");
        }

        private LinkStorageTarget? FreezeLightRegionHulls(
            XPointerReference pointer,
            IReadOnlyList<GfxLightRegionHull> values,
            string fieldPath)
        {
            var axes = new LinkStorageTarget?[values.Count];
            var writer = new LinkTemplateWriter(
                checked(values.Count * GfxLightRegionHull.SerializedSize));
            for (int index = 0; index < values.Count; index++)
            {
                GfxLightRegionHull value = values[index] ?? throw NullRow(fieldPath, index);
                string path = $"{fieldPath}[{index}]";
                axes[index] = FreezeArray(
                    value.AxesPointer.Untyped,
                    value.Axes,
                    GfxLightRegionAxis.SerializedSize,
                    4,
                    XFileBlockType.LARGE,
                    WriteLightRegionAxis,
                    $"{path}.Axes");
                WriteFloats(writer, value.KdopMidPoint, 9, $"{path}.KdopMidPoint");
                WriteFloats(writer, value.KdopHalfSize, 9, $"{path}.KdopHalfSize");
                writer.WriteUInt32(value.AxisCount);
                writer.Skip(sizeof(int));
            }
            return FreezeTable(
                pointer,
                values.Count,
                writer,
                4,
                (owner, addend) => axes
                    .Select((target, index) => (target, index))
                    .Where(item => item.target is not null)
                    .Select(item => Direct(
                        owner,
                        checked(addend + item.index * GfxLightRegionHull.SerializedSize + 0x4c),
                        item.target!.Value,
                        $"{fieldPath}[{item.index}].Axes")),
                fieldPath);
        }

        private DpvsStaticTargets FreezeDpvsStatic(GfxWorldDpvsStatic value)
        {
            DpvsAuthoredView authored = RestoreAuthoredDpvs(value);
            LinkStorageTarget? sortedSurfIndex = FreezeUInt16Array(
                value.SortedSurfIndexPointer.Untyped,
                authored.SortedSurfIndex,
                2,
                XFileBlockType.LARGE,
                "GfxWorld.Dpvs.SortedSurfIndex");
            LinkStorageTarget? smodelInsts = FreezeArray(
                value.SModelInstsPointer.Untyped,
                value.SModelInsts,
                GfxStaticModelInst.SerializedSize,
                4,
                XFileBlockType.LARGE,
                WriteStaticModelInst,
                "GfxWorld.Dpvs.SModelInsts");
            LinkStorageTarget? surfaces = FreezeSurfaces(
                value.SurfacesPointer.Untyped,
                authored.Surfaces);
            LinkStorageTarget? surfaceBounds = FreezeArray(
                value.SurfaceBoundsPointer.Untyped,
                authored.SurfaceBounds,
                GfxSurfaceBounds.SerializedSize,
                4,
                XFileBlockType.LARGE,
                WriteSurfaceBounds,
                "GfxWorld.Dpvs.SurfaceBounds");
            LinkStorageTarget? smodelDrawInsts = FreezeStaticModelDrawInsts(
                value.SModelDrawInstsPointer.Untyped,
                value.SModelDrawInsts);
            int smodelVisCount = CountAt(value.VisibilityCounts, 6);
            int surfaceVisCount = CountAt(value.VisibilityCounts, 7);
            LinkStorageSymbol?[] smodelVis = Enumerable.Range(0, 3)
                .Select(_ => Runtime(checked(smodelVisCount * sizeof(uint)), 4))
                .ToArray();
            LinkStorageSymbol?[] surfaceVis = Enumerable.Range(0, 3)
                .Select(_ => Runtime(checked(surfaceVisCount * sizeof(uint)), 4))
                .ToArray();
            LinkStorageSymbol? surfaceMaterials = Runtime(
                checked(authored.Surfaces.Count * GfxMapDrawSurf.SerializedSize),
                4);
            LinkStorageSymbol? surfaceCastsSunShadow = Runtime(
                checked(surfaceVisCount * sizeof(uint)),
                4);
            return new DpvsStaticTargets(
                smodelVis,
                surfaceVis,
                sortedSurfIndex,
                smodelInsts,
                surfaces,
                surfaceBounds,
                smodelDrawInsts,
                surfaceMaterials,
                surfaceCastsSunShadow);
        }

        private static DpvsAuthoredView RestoreAuthoredDpvs(
            GfxWorldDpvsStatic value)
        {
            int count = value.Surfaces.Count;
            if (value.AuthoredSurfaceIndexByRuntimeSlot.Count == 0)
            {
                return new DpvsAuthoredView(
                    value.SortedSurfIndex.ToArray(),
                    value.Surfaces.ToArray(),
                    value.SurfaceBounds.ToArray());
            }
            if (value.AuthoredSurfaceIndexByRuntimeSlot.Count != count ||
                value.SurfaceBounds.Count != count)
            {
                throw new InvalidDataException(
                    "GfxWorld.Dpvs authored-surface mapping does not parallel the surface tables.");
            }

            var seen = new bool[count];
            var surfaces = new GfxSurface[count];
            var bounds = new GfxSurfaceBounds[count];
            for (int runtimeSlot = 0; runtimeSlot < count; runtimeSlot++)
            {
                int authoredIndex = value.AuthoredSurfaceIndexByRuntimeSlot[runtimeSlot];
                if ((uint)authoredIndex >= (uint)count || seen[authoredIndex])
                {
                    throw new InvalidDataException(
                        $"GfxWorld.Dpvs authored-surface mapping has invalid index {authoredIndex} at runtime slot {runtimeSlot}.");
                }
                seen[authoredIndex] = true;
                surfaces[authoredIndex] = value.Surfaces[runtimeSlot];
                bounds[authoredIndex] = value.SurfaceBounds[runtimeSlot];
            }

            var sorted = new ushort[value.SortedSurfIndex.Count];
            for (int index = 0; index < sorted.Length; index++)
            {
                ushort runtimeSlot = value.SortedSurfIndex[index];
                if (runtimeSlot >= count)
                {
                    throw new InvalidDataException(
                        $"GfxWorld.Dpvs.SortedSurfIndex[{index}] references invalid runtime slot {runtimeSlot}.");
                }
                sorted[index] = checked((ushort)
                    value.AuthoredSurfaceIndexByRuntimeSlot[runtimeSlot]);
            }
            return new DpvsAuthoredView(sorted, surfaces, bounds);
        }

        private static IEnumerable<LinkOperation> DpvsStaticOperations(
            LinkStorageSymbol root,
            DpvsStaticTargets value)
        {
            for (int index = 0; index < 3; index++)
            {
                if (value.SModelVisData[index] is { } storage)
                    yield return Presence(root, 0x204 + index * 4, storage, $"GfxWorld.Dpvs.SModelVisData[{index}]");
            }
            for (int index = 0; index < 3; index++)
            {
                if (value.SurfaceVisData[index] is { } storage)
                    yield return Presence(root, 0x210 + index * 4, storage, $"GfxWorld.Dpvs.SurfaceVisData[{index}]");
            }
            if (value.SortedSurfIndex is { } sorted)
                yield return Direct(root, 0x21c, sorted, "GfxWorld.Dpvs.SortedSurfIndex");
            if (value.SModelInsts is { } instances)
                yield return Direct(root, 0x220, instances, "GfxWorld.Dpvs.SModelInsts");
            if (value.Surfaces is { } surfaces)
                yield return Direct(root, 0x224, surfaces, "GfxWorld.Dpvs.Surfaces");
            if (value.SurfaceBounds is { } bounds)
                yield return Direct(root, 0x228, bounds, "GfxWorld.Dpvs.SurfaceBounds");
            if (value.SModelDrawInsts is { } draws)
                yield return Direct(root, 0x22c, draws, "GfxWorld.Dpvs.SModelDrawInsts");
            if (value.SurfaceMaterials is not null)
                yield return Presence(root, 0x230, value.SurfaceMaterials, "GfxWorld.Dpvs.SurfaceMaterials");
            if (value.SurfaceCastsSunShadow is not null)
                yield return Presence(root, 0x234, value.SurfaceCastsSunShadow, "GfxWorld.Dpvs.SurfaceCastsSunShadow");
        }

        private LinkStorageTarget? FreezeSurfaces(
            XPointerReference pointer,
            IReadOnlyList<GfxSurface> values)
        {
            var materials = new AssetDependency?[values.Count];
            var writer = new LinkTemplateWriter(
                checked(values.Count * GfxSurface.SerializedSize));
            for (int index = 0; index < values.Count; index++)
            {
                GfxSurface value = values[index] ?? throw NullRow("GfxWorld.Dpvs.Surfaces", index);
                materials[index] = FreezeProviderDependency(
                    value.MaterialPointer.Untyped,
                    value.Material,
                    XAssetType.Material,
                    $"GfxWorld.Dpvs.Surfaces[{index}].Material");
                WriteTriangles(writer, value.Triangles);
                writer.Skip(sizeof(int));
                writer.WriteByte(value.LightmapIndex);
                writer.WriteByte(value.ReflectionProbeIndex);
                writer.WriteByte(value.PrimaryLightIndex);
                writer.WriteByte(value.CastsSunShadow);
            }
            return FreezeTable(
                pointer,
                values.Count,
                writer,
                4,
                (owner, addend) => materials
                    .Select((dependency, index) => (dependency, index))
                    .Where(item => item.dependency is not null)
                    .Select(item => Provider(
                        owner,
                        checked(addend + item.index * GfxSurface.SerializedSize + 0x14),
                        item.dependency!.Value)),
                "GfxWorld.Dpvs.Surfaces");
        }

        private LinkStorageTarget? FreezeStaticModelDrawInsts(
            XPointerReference pointer,
            IReadOnlyList<GfxStaticModelDrawInst> values)
        {
            var models = new AssetDependency?[values.Count];
            var writer = new LinkTemplateWriter(
                checked(values.Count * GfxStaticModelDrawInst.SerializedSize));
            for (int index = 0; index < values.Count; index++)
            {
                GfxStaticModelDrawInst value = values[index] ?? throw NullRow("GfxWorld.Dpvs.SModelDrawInsts", index);
                models[index] = FreezeProviderDependency(
                    value.ModelPointer.Untyped,
                    value.Model,
                    XAssetType.XModel,
                    $"GfxWorld.Dpvs.SModelDrawInsts[{index}].Model");
                WritePlacement(writer, value.Placement, $"GfxWorld.Dpvs.SModelDrawInsts[{index}].Placement");
                writer.Skip(sizeof(int));
                writer.WriteUInt16(value.CullDist);
                writer.WriteUInt16(value.LightingHandle);
                writer.WriteByte(value.ReflectionProbeIndex);
                writer.WriteByte(value.PrimaryLightIndex);
                writer.WriteByte(value.Flags);
                writer.WriteByte(value.FirstMaterialSkinIndex);
                writer.WriteUInt32(value.GroundLighting.Packed);
            }
            return FreezeTable(
                pointer,
                values.Count,
                writer,
                4,
                (owner, addend) => models
                    .Select((dependency, index) => (dependency, index))
                    .Where(item => item.dependency is not null)
                    .Select(item => Provider(
                        owner,
                        checked(addend + item.index * GfxStaticModelDrawInst.SerializedSize + 0x1c),
                        item.dependency!.Value)),
                "GfxWorld.Dpvs.SModelDrawInsts");
        }

        private DpvsDynamicTargets FreezeDpvsDynamic(
            GfxWorldDpvsDynamic value,
            int cellCount)
        {
            var cellBits = new LinkStorageSymbol?[2];
            for (int index = 0; index < cellBits.Length; index++)
            {
                cellBits[index] = Runtime(
                    checked(CountAt(value.DynEntClientWordCount, index) * cellCount * sizeof(uint)),
                    4);
            }
            var visData = new LinkStorageSymbol?[6];
            foreach (int index in new[] { 0, 3, 1, 4, 2, 5 })
            {
                int wordCount = CountAt(
                    value.DynEntClientWordCount,
                    index >= 3 ? 1 : 0);
                visData[index] = Runtime(checked(wordCount << 5), 16);
            }
            return new DpvsDynamicTargets(cellBits, visData);
        }

        private static IEnumerable<LinkOperation> DpvsDynamicOperations(
            LinkStorageSymbol root,
            DpvsDynamicTargets value)
        {
            for (int index = 0; index < value.CellBits.Count; index++)
            {
                if (value.CellBits[index] is { } storage)
                    yield return Presence(root, 0x24c + index * 4, storage, $"GfxWorld.DpvsDyn.DynEntCellBits[{index}]");
            }
            foreach (int index in new[] { 0, 3, 1, 4, 2, 5 })
            {
                if (value.VisData[index] is { } storage)
                    yield return Presence(root, 0x254 + index * 4, storage, $"GfxWorld.DpvsDyn.DynEntVisData[{index}]");
            }
        }

        private LinkStorageTarget? FreezeProviderTable<T>(
            XPointerReference pointer,
            IReadOnlyList<XPointer<T>> pointers,
            IReadOnlyList<T?> definitions,
            XAssetType serializedType,
            string fieldPath)
            where T : BaseAsset
        {
            if (pointers.Count is not 0 && pointers.Count != definitions.Count)
                throw new InvalidDataException($"{fieldPath} pointer and semantic rows must agree.");
            var dependencies = new AssetDependency?[definitions.Count];
            var writer = new LinkTemplateWriter(checked(definitions.Count * sizeof(int)));
            for (int index = 0; index < definitions.Count; index++)
            {
                dependencies[index] = FreezeProviderDependency(
                    pointers.Count == 0 ? default : pointers[index].Untyped,
                    definitions[index],
                    serializedType,
                    $"{fieldPath}[{index}]");
                writer.Skip(sizeof(int));
            }
            return FreezeTable(
                pointer,
                definitions.Count,
                writer,
                4,
                (owner, addend) => dependencies
                    .Select((dependency, index) => (dependency, index))
                    .Where(item => item.dependency is not null)
                    .Select(item => Provider(
                        owner,
                        checked(addend + item.index * sizeof(int)),
                        item.dependency!.Value)),
                fieldPath);
        }

        private LinkStorageTarget? FreezeTable(
            XPointerReference pointer,
            int count,
            LinkTemplateWriter writer,
            int alignment,
            Func<LinkStorageSymbol, int, IEnumerable<LinkOperation>> operations,
            string fieldPath)
        {
            byte[] bytes = writer.Complete();
            if (count == 0)
            {
                RequireNullWhenEmpty(pointer, fieldPath);
                return null;
            }
            return _freeze.FreezeStorage(
                pointer,
                bytes,
                XFileBlockType.LARGE,
                alignment,
                operations,
                fieldPath);
        }

        private LinkStorageTarget? FreezeArray<T>(
            XPointerReference pointer,
            IReadOnlyList<T> values,
            int stride,
            int alignment,
            XFileBlockType block,
            Action<LinkTemplateWriter, T, string> write,
            string fieldPath)
        {
            ArgumentNullException.ThrowIfNull(values);
            if (values.Count == 0)
            {
                RequireNullWhenEmpty(pointer, fieldPath);
                return null;
            }
            var writer = new LinkTemplateWriter(checked(values.Count * stride));
            for (int index = 0; index < values.Count; index++)
            {
                T value = values[index];
                if (value is null)
                    throw NullRow(fieldPath, index);
                write(writer, value, $"{fieldPath}[{index}]");
            }
            return _freeze.FreezeStorage(
                pointer,
                writer.Complete(),
                block,
                alignment,
                operations: null,
                fieldPath);
        }

        private LinkStorageTarget? FreezeByteArray(
            XPointerReference pointer,
            IReadOnlyList<byte> values,
            int alignment,
            XFileBlockType block,
            string fieldPath) =>
            FreezeArray(
                pointer,
                values,
                1,
                alignment,
                block,
                static (writer, value, _) => writer.WriteByte(value),
                fieldPath);

        private LinkStorageTarget? FreezeUInt16Array(
            XPointerReference pointer,
            IReadOnlyList<ushort> values,
            int alignment,
            XFileBlockType block,
            string fieldPath) =>
            FreezeArray(
                pointer,
                values,
                sizeof(ushort),
                alignment,
                block,
                static (writer, value, _) => writer.WriteUInt16(value),
                fieldPath);

        private LinkStorageTarget? FreezeUInt16View(
            XPointerReference pointer,
            IReadOnlyList<ushort> values,
            int alignment,
            XFileBlockType block,
            string fieldPath)
        {
            if (values.Count == 0)
            {
                RequireNullWhenEmpty(pointer, fieldPath);
                return null;
            }
            var writer = new LinkTemplateWriter(
                checked(values.Count * sizeof(ushort)));
            foreach (ushort value in values)
                writer.WriteUInt16(value);
            return _freeze.FreezeStorageView(
                pointer,
                writer.Complete(),
                block,
                alignment,
                operations: null,
                fieldPath,
                allowStandaloneDetach: true);
        }

        private LinkStorageTarget? FreezeInt32Array(
            XPointerReference pointer,
            IReadOnlyList<int> values,
            int alignment,
            XFileBlockType block,
            string fieldPath) =>
            FreezeArray(
                pointer,
                values,
                sizeof(int),
                alignment,
                block,
                static (writer, value, _) => writer.WriteInt32(value),
                fieldPath);

        private static LinkStorageSymbol? Runtime(int byteLength, int alignment) =>
            byteLength == 0
                ? null
                : LinkStorageSymbol.SourceFree(
                    XFileBlockType.RUNTIME,
                    byteLength,
                    alignment,
                    LinkMaterializationKind.RuntimeZeroFill);

        private static void ValidateWorld(GfxWorldAsset value)
        {
            ArgumentNullException.ThrowIfNull(value.DpvsPlanes);
            ArgumentNullException.ThrowIfNull(value.WorldDraw);
            ArgumentNullException.ThrowIfNull(value.LightGrid);
            ArgumentNullException.ThrowIfNull(value.Sun);
            ArgumentNullException.ThrowIfNull(value.Dpvs);
            ArgumentNullException.ThrowIfNull(value.DpvsDyn);
            if (value.PlaneCount < 0 || value.PlaneCount != value.DpvsPlanes.Planes.Count)
                throw new InvalidDataException("GfxWorld.PlaneCount must equal DpvsPlanes.Planes.Count.");
            if (value.NodeCount < 0 || value.NodeCount != value.DpvsPlanes.Nodes.Count)
                throw new InvalidDataException("GfxWorld.NodeCount must equal DpvsPlanes.Nodes.Count.");
            if (value.SurfaceCount < 0 || value.SurfaceCount != value.Dpvs.Surfaces.Count || value.SurfaceCount != value.Dpvs.SurfaceBounds.Count)
                throw new InvalidDataException("GfxWorld.SurfaceCount must equal DPVS surface and bound counts.");
            if (value.SkyCount != value.Skies.Count)
                throw new InvalidDataException("GfxWorld.SkyCount must equal Skies.Count.");
            if (value.DpvsPlanes.CellCount < 0 || value.CellTreeCounts.Count != value.DpvsPlanes.CellCount || value.CellTrees.Count != value.DpvsPlanes.CellCount || value.Cells.Count != value.DpvsPlanes.CellCount)
                throw new InvalidDataException("GfxWorld cell-tree and cell rows must equal DpvsPlanes.CellCount.");
            if (value.ModelCount < 0 || value.ModelCount != value.Models.Count)
                throw new InvalidDataException("GfxWorld.ModelCount must equal Models.Count.");
            if (value.MaterialMemoryCount < 0 || value.MaterialMemoryCount != value.MaterialMemory.Count)
                throw new InvalidDataException("GfxWorld.MaterialMemoryCount must equal MaterialMemory.Count.");
            if (value.HeroOnlyLightCount != value.HeroOnlyLights.Count)
                throw new InvalidDataException("GfxWorld.HeroOnlyLightCount must equal HeroOnlyLights.Count.");
            RequireOptionalFixedCount(value.Mins, 3, "GfxWorld.Mins");
            RequireOptionalFixedCount(value.Maxs, 3, "GfxWorld.Maxs");
            RequireOptionalFixedCount(value.OutdoorLookupMatrix, 16, "GfxWorld.OutdoorLookupMatrix");
            RequireOptionalFixedCount(value.Sun.SunFxPosition, 3, "GfxWorld.Sun.SunFxPosition");
            if (value.Pad279To27B.Count is not (0 or 3))
                throw new InvalidDataException("GfxWorld.Pad279To27B must be absent or exactly three bytes.");
            if (value.PrimaryLightCount < 0 || value.SunPrimaryLightIndex < 0 || value.SunPrimaryLightIndex > value.PrimaryLightCount)
                throw new InvalidDataException("GfxWorld primary-light counts are invalid.");
            if (value.UmbraGateCount < 0)
                throw new InvalidDataException("GfxWorld.UmbraGateCount cannot be negative.");

            for (int index = 0; index < value.CellTrees.Count; index++)
            {
                if (value.CellTrees[index].AabbTrees.Count != value.CellTreeCounts[index].AabbTreeCount)
                    throw new InvalidDataException($"GfxWorld.CellTrees[{index}] AABB count disagrees with CellTreeCounts.");
                foreach ((GfxAabbTree tree, int treeIndex) in value.CellTrees[index].AabbTrees.Select((tree, treeIndex) => (tree, treeIndex)))
                {
                    if (tree.SModelIndexCount != tree.SModelIndexes.Count)
                        throw new InvalidDataException($"GfxWorld.CellTrees[{index}].AabbTrees[{treeIndex}].SModelIndexCount disagrees with its index count.");
                }
            }
            for (int index = 0; index < value.Cells.Count; index++)
            {
                GfxCell cell = value.Cells[index];
                if (cell.PortalCount != cell.Portals.Count || cell.ReflectionProbeCount != cell.ReflectionProbes.Count || cell.Pad21.Count != 3)
                    throw new InvalidDataException($"GfxWorld.Cells[{index}] counts or padding are invalid.");
                for (int portalIndex = 0; portalIndex < cell.Portals.Count; portalIndex++)
                {
                    GfxPortal portal = cell.Portals[portalIndex];
                    if (portal.VertexCount != portal.Vertices.Count || portal.HullAxis.Count != 6)
                        throw new InvalidDataException($"GfxWorld.Cells[{index}].Portals[{portalIndex}] counts are invalid.");
                }
            }
            ValidateWorldDraw(value.WorldDraw);
            ValidateLightGrid(value.LightGrid);
            ValidateShadowAndLightRegions(value);
            ValidateDpvs(value);
        }

        private static void ValidateWorldDraw(GfxWorldDraw value)
        {
            if (value.ReflectionProbeCount != value.ReflectionProbeImages.Count ||
                value.ReflectionProbeCount != value.ReflectionProbeImagePointers.Count ||
                value.ReflectionProbeCount != value.ReflectionProbeOrigins.Count)
                throw new InvalidDataException("GfxWorld.WorldDraw reflection-probe tables must share ReflectionProbeCount.");
            if (value.LightmapCount < 0 || value.LightmapCount != value.Lightmaps.Count)
                throw new InvalidDataException("GfxWorld.WorldDraw.LightmapCount must equal Lightmaps.Count.");
            if (value.VertexData.PackedVertices.Count != checked((long)value.VertexCount * 0x10))
                throw new InvalidDataException("GfxWorld.WorldDraw packed vertex bytes must equal VertexCount * 0x10.");
            if (value.VertexLayerData.PackedLayerData.Count != value.VertexLayerDataSize)
                throw new InvalidDataException("GfxWorld.WorldDraw packed layer bytes must equal VertexLayerDataSize.");
            if (value.IndexCount < 0 || value.IndexCount != value.Indices.Count)
                throw new InvalidDataException("GfxWorld.WorldDraw.IndexCount must equal Indices.Count.");
        }

        private static void ValidateLightGrid(GfxLightGrid value)
        {
            RequireCount(value.Mins, 3, "GfxWorld.LightGrid.Mins");
            RequireCount(value.Maxs, 3, "GfxWorld.LightGrid.Maxs");
            if (value.RowAxis > 2)
                throw new InvalidDataException("GfxWorld.LightGrid.RowAxis must be 0, 1, or 2.");
            int rowCount = checked(value.Maxs[(int)value.RowAxis] - value.Mins[(int)value.RowAxis] + 1);
            if (value.RowDataStart.Count != rowCount || value.RawRowDataSize != value.RawRowData.Count || value.EntryCount != value.Entries.Count || value.ColorCount != value.Colors.Count)
                throw new InvalidDataException("GfxWorld.LightGrid scalar counts disagree with their tables.");
        }

        private static void ValidateShadowAndLightRegions(GfxWorldAsset value)
        {
            if (value.ShadowGeom.Count != value.PrimaryLightCount || value.LightRegions.Count != value.PrimaryLightCount)
                throw new InvalidDataException("GfxWorld shadow and light-region tables must equal PrimaryLightCount.");
            for (int index = 0; index < value.ShadowGeom.Count; index++)
            {
                GfxShadowGeometry shadow = value.ShadowGeom[index];
                if (shadow.SurfaceCount != shadow.SortedSurfIndex.Count || shadow.SModelCount != shadow.SModelIndex.Count)
                    throw new InvalidDataException($"GfxWorld.ShadowGeom[{index}] counts disagree with its tables.");
            }
            for (int index = 0; index < value.LightRegions.Count; index++)
            {
                GfxLightRegion region = value.LightRegions[index];
                if (region.HullCount != region.Hulls.Count)
                    throw new InvalidDataException($"GfxWorld.LightRegions[{index}].HullCount disagrees with its table.");
                for (int hullIndex = 0; hullIndex < region.Hulls.Count; hullIndex++)
                {
                    GfxLightRegionHull hull = region.Hulls[hullIndex];
                    RequireCount(hull.KdopMidPoint, 9, $"GfxWorld.LightRegions[{index}].Hulls[{hullIndex}].KdopMidPoint");
                    RequireCount(hull.KdopHalfSize, 9, $"GfxWorld.LightRegions[{index}].Hulls[{hullIndex}].KdopHalfSize");
                    if (hull.AxisCount != hull.Axes.Count)
                        throw new InvalidDataException($"GfxWorld.LightRegions[{index}].Hulls[{hullIndex}].AxisCount disagrees with its table.");
                }
            }
        }

        private static void ValidateDpvs(GfxWorldAsset root)
        {
            GfxWorldDpvsStatic value = root.Dpvs;
            RequireOptionalFixedCount(value.VisibilityCounts, 8, "GfxWorld.Dpvs.VisibilityCounts");
            if (value.SModelCount != value.SModelInsts.Count || value.SModelCount != value.SModelDrawInsts.Count)
                throw new InvalidDataException("GfxWorld.Dpvs.SModelCount must equal both static-model tables.");
            if (value.StaticSurfaceCount != value.SortedSurfIndex.Count)
                throw new InvalidDataException("GfxWorld.Dpvs.StaticSurfaceCount must equal SortedSurfIndex.Count.");
            RequireOptionalFixedCount(root.DpvsDyn.DynEntClientWordCount, 2, "GfxWorld.DpvsDyn.DynEntClientWordCount");
            RequireOptionalFixedCount(root.DpvsDyn.DynEntClientCount, 2, "GfxWorld.DpvsDyn.DynEntClientCount");
            for (int index = 0; index < value.SurfaceBounds.Count; index++)
                RequireCount(value.SurfaceBounds[index].Unknown18To1F, 8, $"GfxWorld.Dpvs.SurfaceBounds[{index}].Unknown18To1F");
        }

        private static void WriteWorldDraw(LinkTemplateWriter writer, GfxWorldDraw value)
        {
            writer.WriteUInt32(value.ReflectionProbeCount);
            writer.Skip(3 * sizeof(int));
            writer.WriteInt32(value.LightmapCount);
            writer.Skip(5 * sizeof(int));
            writer.WriteUInt32(value.VertexCount);
            writer.Skip(3 * sizeof(int));
            writer.WriteUInt32(value.VertexLayerDataSize);
            writer.Skip(3 * sizeof(int));
            writer.WriteInt32(value.IndexCount);
            writer.Skip(2 * sizeof(int));
        }

        private static void WriteLightGrid(LinkTemplateWriter writer, GfxLightGrid value)
        {
            writer.WriteUInt32(value.HasLightRegions);
            writer.WriteUInt32(value.SunPrimaryLightIndex);
            foreach (ushort item in value.Mins) writer.WriteUInt16(item);
            foreach (ushort item in value.Maxs) writer.WriteUInt16(item);
            writer.WriteUInt32(value.RowAxis);
            writer.WriteUInt32(value.ColAxis);
            writer.Skip(sizeof(int));
            writer.WriteUInt32(value.RawRowDataSize);
            writer.Skip(sizeof(int));
            writer.WriteUInt32(value.EntryCount);
            writer.Skip(sizeof(int));
            writer.WriteUInt32(value.ColorCount);
            writer.Skip(sizeof(int));
        }

        private static void WriteSunflare(LinkTemplateWriter writer, Sunflare value)
        {
            writer.WriteUInt32(value.HasValidData);
            writer.Skip(2 * sizeof(int));
            WriteSingle(writer, value.SpriteSize);
            WriteSingle(writer, value.FlareMinSize);
            WriteSingle(writer, value.FlareMinDot);
            WriteSingle(writer, value.FlareMaxSize);
            WriteSingle(writer, value.FlareMaxDot);
            WriteSingle(writer, value.FlareMaxAlpha);
            writer.WriteInt32(value.FlareFadeInTime);
            writer.WriteInt32(value.FlareFadeOutTime);
            WriteSingle(writer, value.BlindMinDot);
            WriteSingle(writer, value.BlindMaxDot);
            WriteSingle(writer, value.BlindMaxDarken);
            writer.WriteInt32(value.BlindFadeInTime);
            writer.WriteInt32(value.BlindFadeOutTime);
            WriteSingle(writer, value.GlareMinDot);
            WriteSingle(writer, value.GlareMaxDot);
            WriteSingle(writer, value.GlareMaxLighten);
            writer.WriteInt32(value.GlareFadeInTime);
            writer.WriteInt32(value.GlareFadeOutTime);
            WriteFloats(writer, value.SunFxPosition, 3, "GfxWorld.Sun.SunFxPosition");
        }

        private static void WriteDpvsStatic(LinkTemplateWriter writer, GfxWorldDpvsStatic value)
        {
            writer.WriteUInt32(value.SModelCount);
            writer.WriteUInt32(value.StaticSurfaceCount);
            writer.WriteUInt32(value.LitSurfsBegin);
            writer.WriteUInt32(value.LitSurfsEnd);
            WriteUInt32s(writer, value.VisibilityCounts, 8, "GfxWorld.Dpvs.VisibilityCounts");
            writer.Skip(13 * sizeof(int));
            writer.WriteUInt32(value.UsageCount);
        }

        private static void WriteDpvsDynamic(LinkTemplateWriter writer, GfxWorldDpvsDynamic value)
        {
            WriteUInt32s(writer, value.DynEntClientWordCount, 2, "GfxWorld.DpvsDyn.DynEntClientWordCount");
            WriteUInt32s(writer, value.DynEntClientCount, 2, "GfxWorld.DpvsDyn.DynEntClientCount");
            writer.Skip(8 * sizeof(int));
        }

        private static void WritePlane(LinkTemplateWriter writer, DpvsPlane value, string _)
        {
            WriteSingle(writer, value.NormalX);
            WriteSingle(writer, value.NormalY);
            WriteSingle(writer, value.NormalZ);
            WriteSingle(writer, value.Distance);
            writer.WriteByte(value.Type);
            writer.WriteByte(value.SignBits);
            writer.WriteUInt16(value.Pad12);
        }

        private static void WriteBrushModel(LinkTemplateWriter writer, GfxBrushModel value, string path)
        {
            WriteFloats(writer, value.WritableMins, 3, $"{path}.WritableMins");
            WriteFloats(writer, value.WritableMaxs, 3, $"{path}.WritableMaxs");
            WriteFloats(writer, value.BoundsMins, 3, $"{path}.BoundsMins");
            WriteFloats(writer, value.BoundsMaxs, 3, $"{path}.BoundsMaxs");
            WriteSingle(writer, value.Radius);
            writer.WriteUInt16(value.SurfaceCount);
            writer.WriteUInt16(value.StartSurfIndex);
        }

        private static void WriteHeroOnlyLight(LinkTemplateWriter writer, GfxHeroOnlyLight value, string path)
        {
            RequireCount(value.Bytes, GfxHeroOnlyLight.SerializedSize, $"{path}.Bytes");
            writer.WriteBytes(value.Bytes.ToArray());
        }

        private static void WriteLightRegionAxis(LinkTemplateWriter writer, GfxLightRegionAxis value, string path)
        {
            WriteFloats(writer, value.Dir, 3, $"{path}.Dir");
            WriteSingle(writer, value.MidPoint);
            WriteSingle(writer, value.HalfSize);
        }

        private static void WriteStaticModelInst(LinkTemplateWriter writer, GfxStaticModelInst value, string path)
        {
            WriteBounds(writer, value.Bounds, $"{path}.Bounds");
            WriteSingle(writer, value.LightingOrigin.X);
            WriteSingle(writer, value.LightingOrigin.Y);
            WriteSingle(writer, value.LightingOrigin.Z);
        }

        private static void WriteSurfaceBounds(LinkTemplateWriter writer, GfxSurfaceBounds value, string path)
        {
            WriteBounds(writer, value.Bounds, $"{path}.Bounds");
            RequireCount(value.Unknown18To1F, 8, $"{path}.Unknown18To1F");
            writer.WriteBytes(value.Unknown18To1F.ToArray());
        }

        private static void WriteTriangles(LinkTemplateWriter writer, SrfTriangles value)
        {
            writer.WriteInt32(value.VertexLayerData);
            writer.WriteInt32(value.BaseVertex);
            writer.WriteUInt32(value.MinVertexIndex);
            writer.WriteUInt16(value.VertexCount);
            writer.WriteUInt16(value.TriCount);
            writer.WriteInt32(value.BaseIndex);
        }

        private static void WritePlacement(LinkTemplateWriter writer, GfxPackedPlacement value, string path)
        {
            WriteFloats(writer, value.Origin, 3, $"{path}.Origin");
            WriteUInt32s(writer, value.PackedAxis, 3, $"{path}.PackedAxis");
            WriteSingle(writer, value.Scale);
        }

        private static void WriteBounds(LinkTemplateWriter writer, Bounds value, string fieldPath)
        {
            ArgumentNullException.ThrowIfNull(value);
            WriteSingle(writer, value.MidPoint.X);
            WriteSingle(writer, value.MidPoint.Y);
            WriteSingle(writer, value.MidPoint.Z);
            WriteSingle(writer, value.HalfSize.X);
            WriteSingle(writer, value.HalfSize.Y);
            WriteSingle(writer, value.HalfSize.Z);
        }

        private static void WriteFloats(LinkTemplateWriter writer, IReadOnlyList<float> values, int count, string fieldPath)
        {
            RequireOptionalFixedCount(values, count, fieldPath);
            if (values.Count == 0)
            {
                writer.Skip(checked(count * sizeof(float)));
                return;
            }
            foreach (float value in values) WriteSingle(writer, value);
        }

        private static void WriteSingle(LinkTemplateWriter writer, float value) =>
            writer.WriteInt32(BitConverter.SingleToInt32Bits(value));

        private static void WriteUInt32s(LinkTemplateWriter writer, IReadOnlyList<uint> values, int count, string fieldPath)
        {
            RequireOptionalFixedCount(values, count, fieldPath);
            if (values.Count == 0)
            {
                writer.Skip(checked(count * sizeof(uint)));
                return;
            }
            foreach (uint value in values) writer.WriteUInt32(value);
        }

        private static DirectStorageLinkOperation Direct(
            LinkStorageSymbol owner,
            int offset,
            LinkStorageTarget target,
            string fieldPath) =>
            new(new LinkStorageCell(owner, offset), target.View, target.CanMaterializeRoot, fieldPath);

        private static PresenceStorageLinkOperation Presence(
            LinkStorageSymbol owner,
            int offset,
            LinkStorageSymbol target,
            string fieldPath) =>
            new(new LinkStorageCell(owner, offset), LinkStorageView.Whole(target), fieldPath);

        private static ProviderLinkOperation Provider(
            LinkStorageSymbol owner,
            int offset,
            AssetDependency dependency) =>
            new(new LinkStorageCell(owner, offset), dependency);

        private static XStringLinkOperation XString(
            LinkStorageSymbol owner,
            int offset,
            LinkStorageSymbol target,
            string fieldPath) =>
            new(new LinkStorageCell(owner, offset), LinkStorageView.Whole(target), true, fieldPath);

        private static int WordCount(int count) => checked((count + 31) >> 5);

        private static int CountAt(IReadOnlyList<uint> values, int index) =>
            values.Count == 0 ? 0 : checked((int)values[index]);

        private static void RequireNullWhenEmpty(XPointerReference pointer, string fieldPath)
        {
            if (pointer.Type != PointerType.Null)
                throw new NotSupportedException($"{fieldPath} cannot preserve present-empty direct storage.");
        }

        private static void RequireCount<T>(IReadOnlyList<T> values, int count, string fieldPath)
        {
            if (values.Count != count)
                throw new InvalidDataException($"{fieldPath} requires exactly {count} values.");
        }

        private static void RequireOptionalFixedCount<T>(IReadOnlyList<T> values, int count, string fieldPath)
        {
            if (values.Count is not 0 && values.Count != count)
                throw new InvalidDataException($"{fieldPath} must be absent or contain exactly {count} values.");
        }

        private static InvalidDataException NullRow(string fieldPath, int index) =>
            new($"{fieldPath}[{index}] cannot be null.");

        private sealed record WorldDrawTargets(
            LinkStorageTarget? ReflectionImages,
            LinkStorageTarget? ReflectionOrigins,
            LinkStorageSymbol? ReflectionTextures,
            LinkStorageTarget? Lightmaps,
            LinkStorageSymbol? LightmapPrimaryTextures,
            LinkStorageSymbol? LightmapSecondaryTextures,
            AssetDependency? LightmapOverridePrimary,
            AssetDependency? LightmapOverrideSecondary,
            LinkStorageTarget? Vertices,
            LinkStorageTarget? VertexLayerData,
            LinkStorageTarget? Indices);

        private sealed record LightGridTargets(
            LinkStorageTarget? RowDataStart,
            LinkStorageTarget? RawRowData,
            LinkStorageTarget? Entries,
            LinkStorageTarget? Colors);

        private sealed record DpvsStaticTargets(
            IReadOnlyList<LinkStorageSymbol?> SModelVisData,
            IReadOnlyList<LinkStorageSymbol?> SurfaceVisData,
            LinkStorageTarget? SortedSurfIndex,
            LinkStorageTarget? SModelInsts,
            LinkStorageTarget? Surfaces,
            LinkStorageTarget? SurfaceBounds,
            LinkStorageTarget? SModelDrawInsts,
            LinkStorageSymbol? SurfaceMaterials,
            LinkStorageSymbol? SurfaceCastsSunShadow);

        private sealed record DpvsDynamicTargets(
            IReadOnlyList<LinkStorageSymbol?> CellBits,
            IReadOnlyList<LinkStorageSymbol?> VisData);

        private sealed record DpvsAuthoredView(
            IReadOnlyList<ushort> SortedSurfIndex,
            IReadOnlyList<GfxSurface> Surfaces,
            IReadOnlyList<GfxSurfaceBounds> SurfaceBounds);
    }
}
