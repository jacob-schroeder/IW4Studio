using IW4.FastFiles.Zone;

namespace IW4.Studio.MapEditor.Compilation.Ownership;

/// <summary>
/// Field-level ownership boundary for the first multiplayer greenfield map
/// compiler. This catalog records responsibility and readiness; it does not
/// authorize emission of families whose algorithms or evidence remain open.
/// </summary>
public static class InitialMpMapOwnershipCatalog
{
    public static MapCompilerOwnershipCatalog Current { get; } = new(
    [
        Root(
            InitialMpMapAssetRoot.GfxMap,
            XAssetType.GfxMap,
            "$.definition",
            GfxMapFamilies()),
        Root(
            InitialMpMapAssetRoot.ColMapMp,
            XAssetType.ColMapMp,
            "$.definition",
            ColMapFamilies()),
        Root(
            InitialMpMapAssetRoot.ComMap,
            XAssetType.ComMap,
            "$.definition",
            ComMapFamilies()),
        Root(
            InitialMpMapAssetRoot.MapEnts,
            XAssetType.MapEnts,
            "$.definition",
            MapEntsFamilies()),
        Root(
            InitialMpMapAssetRoot.FxMap,
            XAssetType.FxMap,
            "$.definition",
            FxMapFamilies()),
        Root(
            InitialMpMapAssetRoot.GameMapMp,
            XAssetType.GameMapMp,
            "$.definition",
            GameMapFamilies()),
        Root(
            InitialMpMapAssetRoot.TargetZone,
            serializedType: null,
            "$.targetZone",
            TargetZoneFamilies())
    ]);

    private static IReadOnlyList<MapCompilerOwnershipFamilyContract>
        GfxMapFamilies() =>
    [
        Gfx(
            InitialMpMapSerializedFamily.GfxMap_Identity,
            "$.definition.{name,baseName}",
            MapFieldOwnership.EditorSource,
            MapCompilerSubsystem.MapIdentity,
            MapRuntimeSubsystem.AssetDatabase,
            MapCompilerMilestone.M0OwnershipLock,
            MapOwnershipReadiness.SourceProjectionReady,
            "Canonical map identity and the renderer base-name string."),
        GfxCompiler(
            InitialMpMapSerializedFamily.GfxMap_RenderSortKeys,
            "$.definition.{sortKeyLitDecal,sortKeyEffectDecal,sortKeyEffectAuto,sortKeyDistortion}",
            MapCompilerSubsystem.RenderWorld,
            MapRuntimeSubsystem.Renderer,
            MapCompilerMilestone.M3StructuralGeometry,
            "Render sort-key boundaries are render-world compiler products."),
        GfxCompiler(
            InitialMpMapSerializedFamily.GfxMap_Skies,
            "$.definition.{skyCount,skies[]}",
            MapCompilerSubsystem.LightingAndEnvironment,
            MapRuntimeSubsystem.Renderer,
            MapCompilerMilestone.M5LightingEnvironment,
            "Sky rows and sampler state are environment compiler products."),
        GfxCompiler(
            InitialMpMapSerializedFamily.GfxMap_SkyStartSurfaces,
            "$.definition.skies[].skyStartSurfs[]",
            MapCompilerSubsystem.VisibilityAndSpatial,
            MapRuntimeSubsystem.RendererVisibility,
            MapCompilerMilestone.M4Visibility,
            "Sky-to-surface membership is derived from compiled surfaces."),
        GfxLink(
            InitialMpMapSerializedFamily.GfxMap_SkyImageReferences,
            "$.definition.skies[].skyImage",
            MapCompilerSubsystem.CrossAssetLinker,
            MapRuntimeSubsystem.AssetDatabase,
            MapCompilerMilestone.M7GreenfieldLink,
            "Each sky image is a symbolic Image identity, never a loaded object or imported pointer."),
        GfxCompiler(
            InitialMpMapSerializedFamily.GfxMap_DpvsPlanes,
            "$.definition.{planeCount,dpvsPlanes.cellCount,dpvsPlanes.planes[]}",
            MapCompilerSubsystem.VisibilityAndSpatial,
            MapRuntimeSubsystem.RendererVisibility,
            MapCompilerMilestone.M4Visibility,
            "DPVS split planes and cell cardinality belong to visibility compilation."),
        GfxCompiler(
            InitialMpMapSerializedFamily.GfxMap_DpvsPlaneNodes,
            "$.definition.{nodeCount,dpvsPlanes.nodes[]}",
            MapCompilerSubsystem.VisibilityAndSpatial,
            MapRuntimeSubsystem.RendererVisibility,
            MapCompilerMilestone.M4Visibility,
            "Serialized DPVS node words are spatial compiler output."),
        GfxRuntime(
            InitialMpMapSerializedFamily.GfxMap_DpvsSceneEntityCellBits,
            "$.definition.dpvsPlanes.sceneEntCellBits[]",
            MapCompilerSubsystem.VisibilityAndSpatial,
            MapRuntimeSubsystem.RendererVisibility,
            "Scene-entity cell bits are a zero-filled native RUNTIME allocation."),
        GfxCompiler(
            InitialMpMapSerializedFamily.GfxMap_CellTreeCounts,
            "$.definition.cellTreeCounts[]",
            MapCompilerSubsystem.VisibilityAndSpatial,
            MapRuntimeSubsystem.RendererVisibility,
            MapCompilerMilestone.M4Visibility,
            "One AABB-tree count row is emitted per cell."),
        GfxCompiler(
            InitialMpMapSerializedFamily.GfxMap_CellAabbTrees,
            "$.definition.cellTrees[].aabbTrees[]",
            MapCompilerSubsystem.VisibilityAndSpatial,
            MapRuntimeSubsystem.RendererVisibility,
            MapCompilerMilestone.M4Visibility,
            "Cell-local AABB nodes and child/surface ranges are spatial products."),
        GfxCompiler(
            InitialMpMapSerializedFamily.GfxMap_CellAabbStaticModelMemberships,
            "$.definition.cellTrees[].aabbTrees[].smodelIndexes[]",
            MapCompilerSubsystem.VisibilityAndSpatial,
            MapRuntimeSubsystem.RendererVisibility,
            MapCompilerMilestone.M4Visibility,
            "AABB static-model membership lists and their local pointers are compiler-owned."),
        GfxCompiler(
            InitialMpMapSerializedFamily.GfxMap_Cells,
            "$.definition.cells[]",
            MapCompilerSubsystem.VisibilityAndSpatial,
            MapRuntimeSubsystem.RendererVisibility,
            MapCompilerMilestone.M4Visibility,
            "Cell bounds and nested-family cardinalities are spatial products."),
        GfxCompiler(
            InitialMpMapSerializedFamily.GfxMap_CellPortals,
            "$.definition.cells[].portals[]",
            MapCompilerSubsystem.VisibilityAndSpatial,
            MapRuntimeSubsystem.RendererVisibility,
            MapCompilerMilestone.M4Visibility,
            "Portal planes, cell links, hull axes, and structural flags are visibility products."),
        GfxRuntime(
            InitialMpMapSerializedFamily.GfxMap_PortalRuntimeLinks,
            "$.definition.cells[].portals[].{hullPointsRuntimePointer,queuedParentRuntimePointer}",
            MapCompilerSubsystem.VisibilityAndSpatial,
            MapRuntimeSubsystem.RendererVisibility,
            "Portal hull/queue links are runtime state and cannot be semantic source identity."),
        GfxCompiler(
            InitialMpMapSerializedFamily.GfxMap_PortalVertices,
            "$.definition.cells[].portals[].vertices[]",
            MapCompilerSubsystem.VisibilityAndSpatial,
            MapRuntimeSubsystem.RendererVisibility,
            MapCompilerMilestone.M4Visibility,
            "Portal windings are generated with their owning portal."),
        GfxCompiler(
            InitialMpMapSerializedFamily.GfxMap_CellReflectionProbeMemberships,
            "$.definition.cells[].reflectionProbes[]",
            MapCompilerSubsystem.LightingAndEnvironment,
            MapRuntimeSubsystem.Renderer,
            MapCompilerMilestone.M5LightingEnvironment,
            "Per-cell reflection-probe ordinals are assigned by the lighting compiler."),
        GfxLink(
            InitialMpMapSerializedFamily.GfxMap_ReflectionProbeImageReferences,
            "$.definition.worldDraw.reflectionProbeImages[]",
            MapCompilerSubsystem.CrossAssetLinker,
            MapRuntimeSubsystem.AssetDatabase,
            MapCompilerMilestone.M7GreenfieldLink,
            "Reflection-probe image slots contain symbolic Image identities."),
        GfxCompiler(
            InitialMpMapSerializedFamily.GfxMap_ReflectionProbeOrigins,
            "$.definition.worldDraw.reflectionProbeOrigins[]",
            MapCompilerSubsystem.LightingAndEnvironment,
            MapRuntimeSubsystem.Renderer,
            MapCompilerMilestone.M5LightingEnvironment,
            "Probe origins and cardinality are lighting compiler output."),
        GfxRuntime(
            InitialMpMapSerializedFamily.GfxMap_ReflectionProbeRuntimeTextures,
            "$.definition.worldDraw.reflectionProbeTextures[]",
            MapCompilerSubsystem.LightingAndEnvironment,
            MapRuntimeSubsystem.Renderer,
            "Texture descriptors are RUNTIME allocations and must not carry authored source words."),
        GfxCompiler(
            InitialMpMapSerializedFamily.GfxMap_LightmapShape,
            "$.definition.worldDraw.lightmapCount",
            MapCompilerSubsystem.LightingAndEnvironment,
            MapRuntimeSubsystem.Renderer,
            MapCompilerMilestone.M5LightingEnvironment,
            "Lightmap cardinality is owned by the lighting bake."),
        GfxLink(
            InitialMpMapSerializedFamily.GfxMap_LightmapReferences,
            "$.definition.worldDraw.lightmaps[].{primary,secondary}",
            MapCompilerSubsystem.CrossAssetLinker,
            MapRuntimeSubsystem.AssetDatabase,
            MapCompilerMilestone.M7GreenfieldLink,
            "Lightmap slots link symbolic primary and secondary Image identities."),
        GfxRuntime(
            InitialMpMapSerializedFamily.GfxMap_LightmapRuntimeTextures,
            "$.definition.worldDraw.{lightmapPrimaryTextures[],lightmapSecondaryTextures[]}",
            MapCompilerSubsystem.LightingAndEnvironment,
            MapRuntimeSubsystem.Renderer,
            "Lightmap texture descriptors are native RUNTIME allocations."),
        GfxLink(
            InitialMpMapSerializedFamily.GfxMap_LightmapOverrideReferences,
            "$.definition.worldDraw.{lightmapOverridePrimary,lightmapOverrideSecondary}",
            MapCompilerSubsystem.CrossAssetLinker,
            MapRuntimeSubsystem.AssetDatabase,
            MapCompilerMilestone.M7GreenfieldLink,
            "Optional lightmap overrides are symbolic Image identities."),
        GfxCompiler(
            InitialMpMapSerializedFamily.GfxMap_PackedWorldVertices,
            "$.definition.worldDraw.{vertexCount,vertexData.packedVertices[]}",
            MapCompilerSubsystem.RenderWorld,
            MapRuntimeSubsystem.Renderer,
            MapCompilerMilestone.M3StructuralGeometry,
            "Packed 0x10-byte world vertices are emitted from semantic render geometry."),
        GfxRuntime(
            InitialMpMapSerializedFamily.GfxMap_WorldVertexRuntimeBuffer,
            "$.definition.worldDraw.vertexData.{worldVbHandle,worldVbOffset}",
            MapCompilerSubsystem.RenderWorld,
            MapRuntimeSubsystem.Renderer,
            "GPU vertex-buffer handle state is native runtime ownership."),
        GfxCompiler(
            InitialMpMapSerializedFamily.GfxMap_PackedVertexLayerData,
            "$.definition.worldDraw.{vertexLayerDataSize,vertexLayerData.packedLayerData[]}",
            MapCompilerSubsystem.RenderWorld,
            MapRuntimeSubsystem.Renderer,
            MapCompilerMilestone.M3StructuralGeometry,
            "Packed vertex-layer bytes are render compiler output."),
        GfxRuntime(
            InitialMpMapSerializedFamily.GfxMap_VertexLayerRuntimeBuffer,
            "$.definition.worldDraw.vertexLayerData.{layerVbHandle,layerVbOffset}",
            MapCompilerSubsystem.RenderWorld,
            MapRuntimeSubsystem.Renderer,
            "GPU layer-buffer handle state is native runtime ownership."),
        GfxCompiler(
            InitialMpMapSerializedFamily.GfxMap_WorldIndices,
            "$.definition.worldDraw.{indexCount,indices[]}",
            MapCompilerSubsystem.RenderWorld,
            MapRuntimeSubsystem.Renderer,
            MapCompilerMilestone.M3StructuralGeometry,
            "The world index stream is packed alongside compiled surfaces."),
        GfxRuntime(
            InitialMpMapSerializedFamily.GfxMap_WorldIndexRuntimeBuffer,
            "$.definition.worldDraw.indexBufferRaw",
            MapCompilerSubsystem.RenderWorld,
            MapRuntimeSubsystem.Renderer,
            "The index-buffer handle is runtime state, not a source identity."),
        GfxCompiler(
            InitialMpMapSerializedFamily.GfxMap_LightGridHeader,
            "$.definition.lightGrid.{hasLightRegions,sunPrimaryLightIndex,bounds,rowAxis,colAxis,counts}",
            MapCompilerSubsystem.LightingAndEnvironment,
            MapRuntimeSubsystem.Renderer,
            MapCompilerMilestone.M5LightingEnvironment,
            "Light-grid dimensions, axes, and counts are bake products."),
        GfxCompiler(
            InitialMpMapSerializedFamily.GfxMap_LightGridRowDataStart,
            "$.definition.lightGrid.rowDataStart[]",
            MapCompilerSubsystem.LightingAndEnvironment,
            MapRuntimeSubsystem.Renderer,
            MapCompilerMilestone.M5LightingEnvironment,
            "Row offsets index the compiled light-grid payload."),
        GfxCompiler(
            InitialMpMapSerializedFamily.GfxMap_LightGridRawRowData,
            "$.definition.lightGrid.rawRowData[]",
            MapCompilerSubsystem.LightingAndEnvironment,
            MapRuntimeSubsystem.Renderer,
            MapCompilerMilestone.M5LightingEnvironment,
            "Compressed row data is a lighting bake product."),
        GfxCompiler(
            InitialMpMapSerializedFamily.GfxMap_LightGridEntries,
            "$.definition.lightGrid.entries[]",
            MapCompilerSubsystem.LightingAndEnvironment,
            MapRuntimeSubsystem.Renderer,
            MapCompilerMilestone.M5LightingEnvironment,
            "Light-grid entries are generated with row data."),
        GfxCompiler(
            InitialMpMapSerializedFamily.GfxMap_LightGridColors,
            "$.definition.lightGrid.colors[]",
            MapCompilerSubsystem.LightingAndEnvironment,
            MapRuntimeSubsystem.Renderer,
            MapCompilerMilestone.M5LightingEnvironment,
            "Packed light-grid color rows are bake products."),
        GfxCompiler(
            InitialMpMapSerializedFamily
                .GfxMap_BrushModelLocalBoundsAndSurfaceRange,
            "$.definition.{modelCount,models[].{boundsMins,boundsMaxs,radius,surfaceCount,startSurfIndex}}",
            MapCompilerSubsystem.RenderWorld,
            MapRuntimeSubsystem.Renderer,
            MapCompilerMilestone.M3StructuralGeometry,
            "Inline brush-model local bounds, radius, and surface ranges are " +
            "compiler output in the shared cross-asset model ordinal."),
        GfxRuntime(
            InitialMpMapSerializedFamily.GfxMap_BrushModelRuntimeBounds,
            "$.definition.models[].{writableMins,writableMaxs}",
            MapCompilerSubsystem.RenderWorld,
            MapRuntimeSubsystem.Renderer,
            "The first two bounds vectors are zero-initialized source storage " +
            "written by runtime brush-model linking; they are not authored " +
            "local bounds or persistent semantic identity."),
        GfxCompiler(
            InitialMpMapSerializedFamily.GfxMap_WorldBounds,
            "$.definition.{mins,maxs}",
            MapCompilerSubsystem.VisibilityAndSpatial,
            MapRuntimeSubsystem.RendererVisibility,
            MapCompilerMilestone.M4Visibility,
            "World bounds are derived from the compiled world graph."),
        Gfx(
            InitialMpMapSerializedFamily.GfxMap_PrimaryChecksum,
            "$.definition.checksum",
            MapFieldOwnership.CompilerSerialized,
            MapCompilerSubsystem.CrossAssetLinker,
            MapRuntimeSubsystem.MultipleMapConsumers,
            MapCompilerMilestone.M0OwnershipLock,
            MapOwnershipReadiness.SourceProjectionReady,
            "ImportedContentPreserved output retains the exact uint32 production value; any semantic source, settings, dependency, or compiler-contract change uses the shared domain-separated MapPrimaryChecksumPolicy StudioCanonicalV1 CRC32 value."),
        GfxCompiler(
            InitialMpMapSerializedFamily.GfxMap_MaterialMemoryRows,
            "$.definition.{materialMemoryCount,materialMemory[].memory}",
            MapCompilerSubsystem.LightingAndEnvironment,
            MapRuntimeSubsystem.Renderer,
            MapCompilerMilestone.M5LightingEnvironment,
            "Material memory estimates are compiler-owned scalar rows."),
        GfxLink(
            InitialMpMapSerializedFamily.GfxMap_MaterialMemoryReferences,
            "$.definition.materialMemory[].material",
            MapCompilerSubsystem.CrossAssetLinker,
            MapRuntimeSubsystem.AssetDatabase,
            MapCompilerMilestone.M7GreenfieldLink,
            "Material-memory slots link symbolic Material identities."),
        GfxCompiler(
            InitialMpMapSerializedFamily.GfxMap_SunParameters,
            "$.definition.sun.{hasValidData,sizes,dots,alphas,fadeTimes,sunFxPosition}",
            MapCompilerSubsystem.LightingAndEnvironment,
            MapRuntimeSubsystem.Renderer,
            MapCompilerMilestone.M5LightingEnvironment,
            "Sun-flare scalar state is authored environment input serialized by the lighting compiler."),
        GfxLink(
            InitialMpMapSerializedFamily.GfxMap_SunMaterialReferences,
            "$.definition.sun.{spriteMaterial,flareMaterial}",
            MapCompilerSubsystem.CrossAssetLinker,
            MapRuntimeSubsystem.AssetDatabase,
            MapCompilerMilestone.M7GreenfieldLink,
            "Sun sprite and flare materials are symbolic Material identities."),
        GfxCompiler(
            InitialMpMapSerializedFamily.GfxMap_OutdoorLookupMatrix,
            "$.definition.outdoorLookupMatrix[]",
            MapCompilerSubsystem.LightingAndEnvironment,
            MapRuntimeSubsystem.Renderer,
            MapCompilerMilestone.M5LightingEnvironment,
            "The outdoor lookup transform is environment compiler output."),
        GfxLink(
            InitialMpMapSerializedFamily.GfxMap_OutdoorImageReference,
            "$.definition.outdoorImage",
            MapCompilerSubsystem.CrossAssetLinker,
            MapRuntimeSubsystem.AssetDatabase,
            MapCompilerMilestone.M7GreenfieldLink,
            "Outdoor lookup image is a symbolic Image identity."),
        GfxRuntime(
            InitialMpMapSerializedFamily.GfxMap_RuntimeCasterAndDynamicSceneCaches,
            "$.definition.{cellCasterBits[],cellCasterBits2[],sceneDynModels[],sceneDynBrushes[],primaryLightEntityShadowVis[],primaryLightDynEntShadowVis0[],primaryLightDynEntShadowVis1[],primaryLightForModelDynEnt[]}",
            MapCompilerSubsystem.VisibilityAndSpatial,
            MapRuntimeSubsystem.RendererVisibility,
            "These root-level arrays are native RUNTIME allocations."),
        GfxCompiler(
            InitialMpMapSerializedFamily.GfxMap_ShadowGeometry,
            "$.definition.shadowGeom[]",
            MapCompilerSubsystem.LightingAndEnvironment,
            MapRuntimeSubsystem.Renderer,
            MapCompilerMilestone.M5LightingEnvironment,
            "Primary-light shadow rows are lighting compiler products."),
        GfxCompiler(
            InitialMpMapSerializedFamily.GfxMap_ShadowGeometryMemberships,
            "$.definition.shadowGeom[].{sortedSurfIndex[],smodelIndex[]}",
            MapCompilerSubsystem.LightingAndEnvironment,
            MapRuntimeSubsystem.Renderer,
            MapCompilerMilestone.M5LightingEnvironment,
            "Shadow surface/static-model membership is rebuilt with lighting."),
        GfxCompiler(
            InitialMpMapSerializedFamily.GfxMap_LightRegions,
            "$.definition.lightRegions[]",
            MapCompilerSubsystem.LightingAndEnvironment,
            MapRuntimeSubsystem.Renderer,
            MapCompilerMilestone.M5LightingEnvironment,
            "One light-region row is owned per primary-light slot."),
        GfxCompiler(
            InitialMpMapSerializedFamily.GfxMap_LightRegionHulls,
            "$.definition.lightRegions[].hulls[]",
            MapCompilerSubsystem.LightingAndEnvironment,
            MapRuntimeSubsystem.Renderer,
            MapCompilerMilestone.M5LightingEnvironment,
            "KDOP light-region hulls are lighting compiler products."),
        GfxCompiler(
            InitialMpMapSerializedFamily.GfxMap_LightRegionAxes,
            "$.definition.lightRegions[].hulls[].axes[]",
            MapCompilerSubsystem.LightingAndEnvironment,
            MapRuntimeSubsystem.Renderer,
            MapCompilerMilestone.M5LightingEnvironment,
            "Nested light-region axes are emitted with each KDOP hull."),
        GfxCompiler(
            InitialMpMapSerializedFamily.GfxMap_DpvsStaticHeader,
            "$.definition.dpvs.{smodelCount,staticSurfaceCount,litSurfsBegin,litSurfsEnd,visibilityCounts}",
            MapCompilerSubsystem.VisibilityAndSpatial,
            MapRuntimeSubsystem.RendererVisibility,
            MapCompilerMilestone.M4Visibility,
            "Static DPVS cardinality and range headers are compiler-owned."),
        GfxRuntime(
            InitialMpMapSerializedFamily.GfxMap_DpvsStaticUsageCount,
            "$.definition.dpvs.usageCount",
            MapCompilerSubsystem.VisibilityAndSpatial,
            MapRuntimeSubsystem.RendererVisibility,
            "DPVS usage count is mutable runtime bookkeeping."),
        GfxRuntime(
            InitialMpMapSerializedFamily.GfxMap_DpvsStaticVisibilityCaches,
            "$.definition.dpvs.{smodelVisData[][],surfaceVisData[][]}",
            MapCompilerSubsystem.VisibilityAndSpatial,
            MapRuntimeSubsystem.RendererVisibility,
            "Static visibility cache payloads are native RUNTIME allocations."),
        GfxCompiler(
            InitialMpMapSerializedFamily.GfxMap_SortedSurfaceIndices,
            "$.definition.dpvs.sortedSurfIndex[]",
            MapCompilerSubsystem.RenderWorld,
            MapRuntimeSubsystem.RendererVisibility,
            MapCompilerMilestone.M3StructuralGeometry,
            "Sorted surface ordinals are emitted from compiled surface ordering."),
        GfxCompiler(
            InitialMpMapSerializedFamily.GfxMap_StaticModelInstances,
            "$.definition.dpvs.smodelInsts[]",
            MapCompilerSubsystem.VisibilityAndSpatial,
            MapRuntimeSubsystem.RendererVisibility,
            MapCompilerMilestone.M4Visibility,
            "Static-model bounds and lighting origins are derived from source placements."),
        GfxCompiler(
            InitialMpMapSerializedFamily.GfxMap_Surfaces,
            "$.definition.{surfaceCount,dpvs.surfaces[]}",
            MapCompilerSubsystem.RenderWorld,
            MapRuntimeSubsystem.Renderer,
            MapCompilerMilestone.M3StructuralGeometry,
            "Surface triangle ranges and light/probe/shadow slots are render compiler products."),
        GfxLink(
            InitialMpMapSerializedFamily.GfxMap_SurfaceMaterialReferences,
            "$.definition.dpvs.surfaces[].material",
            MapCompilerSubsystem.CrossAssetLinker,
            MapRuntimeSubsystem.AssetDatabase,
            MapCompilerMilestone.M7GreenfieldLink,
            "Every surface material cell is a symbolic Material identity."),
        GfxCompiler(
            InitialMpMapSerializedFamily.GfxMap_SurfaceBounds,
            "$.definition.dpvs.surfaceBounds[]",
            MapCompilerSubsystem.VisibilityAndSpatial,
            MapRuntimeSubsystem.RendererVisibility,
            MapCompilerMilestone.M4Visibility,
            "Per-surface bounds are derived from compiled render geometry."),
        GfxCompiler(
            InitialMpMapSerializedFamily.GfxMap_StaticModelDrawInstances,
            "$.definition.dpvs.smodelDrawInsts[]",
            MapCompilerSubsystem.RenderWorld,
            MapRuntimeSubsystem.Renderer,
            MapCompilerMilestone.M5LightingEnvironment,
            "Placement plus cull, lighting, probe, shadow, and material-skin fields are jointly rebuilt."),
        GfxLink(
            InitialMpMapSerializedFamily.GfxMap_StaticModelReferences,
            "$.definition.dpvs.smodelDrawInsts[].model",
            MapCompilerSubsystem.CrossAssetLinker,
            MapRuntimeSubsystem.AssetDatabase,
            MapCompilerMilestone.M7GreenfieldLink,
            "Each draw instance links one symbolic XModel identity."),
        GfxRuntime(
            InitialMpMapSerializedFamily.GfxMap_RuntimeSurfaceMetadataCaches,
            "$.definition.dpvs.{surfaceMaterials[],surfaceCastsSunShadow[]}",
            MapCompilerSubsystem.LightingAndEnvironment,
            MapRuntimeSubsystem.Renderer,
            "Draw-surface and sun-shadow lookup arrays are native RUNTIME allocations."),
        GfxCompiler(
            InitialMpMapSerializedFamily.GfxMap_DpvsDynamicHeader,
            "$.definition.dpvsDyn.{dynEntClientWordCount[],dynEntClientCount[]}",
            MapCompilerSubsystem.VisibilityAndSpatial,
            MapRuntimeSubsystem.RendererVisibility,
            MapCompilerMilestone.M6GlassAndGameplay,
            "Dynamic-entity cache shapes are derived from shared dynamic-entity cardinality."),
        GfxRuntime(
            InitialMpMapSerializedFamily.GfxMap_DpvsDynamicCaches,
            "$.definition.dpvsDyn.{dynEntCellBits[][],dynEntVisData[][]}",
            MapCompilerSubsystem.VisibilityAndSpatial,
            MapRuntimeSubsystem.RendererVisibility,
            "Dynamic DPVS payloads are native RUNTIME allocations."),
        Gfx(
            InitialMpMapSerializedFamily.GfxMap_MapVertexChecksum,
            "$.definition.mapVertexChecksum",
            MapFieldOwnership.CompilerSerialized,
            MapCompilerSubsystem.RenderWorld,
            MapRuntimeSubsystem.Renderer,
            MapCompilerMilestone.M3StructuralGeometry,
            MapOwnershipReadiness.OwnershipLocked,
            "This separate uint32 has no audited retail MP consumer. Imported output preserves it; greenfield M3 output uses the explicit StudioConstantZeroV1 assignment without claiming retail producer parity."),
        GfxCompiler(
            InitialMpMapSerializedFamily.GfxMap_HeroOnlyLights,
            "$.definition.{heroOnlyLightCount,heroOnlyLights[]}",
            MapCompilerSubsystem.LightingAndEnvironment,
            MapRuntimeSubsystem.Renderer,
            MapCompilerMilestone.M5LightingEnvironment,
            "Hero-only light rows and count are lighting compiler products."),
        GfxCompiler(
            InitialMpMapSerializedFamily.GfxMap_FogTypesAllowed,
            "$.definition.fogTypesAllowed",
            MapCompilerSubsystem.LightingAndEnvironment,
            MapRuntimeSubsystem.Renderer,
            MapCompilerMilestone.M5LightingEnvironment,
            "Allowed fog flags are environment compiler output."),
        GfxPreserve(
            InitialMpMapSerializedFamily.GfxMap_TailPadding,
            "$.definition.pad279To27B[]",
            MapCompilerSubsystem.RenderWorld,
            MapRuntimeSubsystem.Renderer,
            MapCompilerMilestone.M3StructuralGeometry,
            MapOwnershipReadiness.ImportedPreservationOnly,
            "The three root tail bytes remain imported-preservation state."),
        GfxCompiler(
            InitialMpMapSerializedFamily.GfxMap_UmbraGateShape,
            "$.definition.umbraGateCount",
            MapCompilerSubsystem.VisibilityAndSpatial,
            MapRuntimeSubsystem.RendererVisibility,
            MapCompilerMilestone.M4Visibility,
            "Umbra gate count shapes two aligned virtual runtime allocations."),
        GfxRuntime(
            InitialMpMapSerializedFamily.GfxMap_UmbraGateRuntimePayloads,
            "$.definition.{umbraGateData[],umbraGateData2[]}",
            MapCompilerSubsystem.VisibilityAndSpatial,
            MapRuntimeSubsystem.RendererVisibility,
            "Both Umbra gate arrays are source-free VIRTUAL runtime allocations.")
    ];

    private static IReadOnlyList<MapCompilerOwnershipFamilyContract>
        ColMapFamilies() =>
    [
        Col(
            InitialMpMapSerializedFamily.ColMapMp_Identity,
            "$.definition.name",
            MapFieldOwnership.EditorSource,
            MapSerializedOwner.CompilerSubsystem,
            MapCompilerSubsystem.MapIdentity,
            MapRuntimeSubsystem.AssetDatabase,
            MapCompilerMilestone.M0OwnershipLock,
            MapOwnershipReadiness.SourceProjectionReady,
            "Canonical multiplayer collision-map identity."),
        ColRuntime(
            InitialMpMapSerializedFamily.ColMapMp_InUseState,
            "$.definition.isInUse",
            MapCompilerSubsystem.Collision,
            MapRuntimeSubsystem.Collision,
            "Registration mutates the runtime pool copy; authored source must not own this state."),
        ColCompiler(
            InitialMpMapSerializedFamily.ColMapMp_Planes,
            "$.definition.{planeCount,planes[]}",
            MapCompilerSubsystem.Collision,
            MapCompilerMilestone.M3StructuralGeometry,
            "Collision plane rows and count are allocated from authored convex/mesh contributions."),
        ColCompiler(
            InitialMpMapSerializedFamily.ColMapMp_StaticModelPlacements,
            "$.definition.{numStaticModels,staticModelList[]}",
            MapCompilerSubsystem.Collision,
            MapCompilerMilestone.M4Visibility,
            "Collision static-model transforms, inverse axes, and bounds share render static-model identity."),
        ColLink(
            InitialMpMapSerializedFamily.ColMapMp_StaticModelReferences,
            "$.definition.staticModelList[].xmodel",
            MapCompilerSubsystem.CrossAssetLinker,
            XAssetType.XModel,
            "Static collision rows link symbolic XModel identities."),
        ColCompiler(
            InitialMpMapSerializedFamily.ColMapMp_Materials,
            "$.definition.{numMaterials,materials[]}",
            MapCompilerSubsystem.Collision,
            MapCompilerMilestone.M3StructuralGeometry,
            "Collision material name, surface flags, and contents rows come from the authored material catalog."),
        ColCompiler(
            InitialMpMapSerializedFamily.ColMapMp_BrushSides,
            "$.definition.{numBrushSides,brushSides[]}",
            MapCompilerSubsystem.Collision,
            MapCompilerMilestone.M3StructuralGeometry,
            "Global brush-side rows contain plane, material, adjacency, and edge fields."),
        ColCompiler(
            InitialMpMapSerializedFamily.ColMapMp_BrushSidePlaneReferences,
            "$.definition.brushSides[].plane",
            MapCompilerSubsystem.Collision,
            MapCompilerMilestone.M3StructuralGeometry,
            "Brush-side plane cells are local plane-table references, not XAsset links."),
        ColCompiler(
            InitialMpMapSerializedFamily.ColMapMp_BrushEdges,
            "$.definition.{numBrushEdges,brushEdges[]}",
            MapCompilerSubsystem.Collision,
            MapCompilerMilestone.M3StructuralGeometry,
            "Global brush-edge bytes are collision compiler output."),
        ColCompiler(
            InitialMpMapSerializedFamily.ColMapMp_BspNodes,
            "$.definition.{numNodes,nodes[]}",
            MapCompilerSubsystem.Collision,
            MapCompilerMilestone.M3StructuralGeometry,
            "BSP child selectors and distance pairs are generated collision topology."),
        ColCompiler(
            InitialMpMapSerializedFamily.ColMapMp_BspNodePlaneReferences,
            "$.definition.nodes[].plane",
            MapCompilerSubsystem.Collision,
            MapCompilerMilestone.M3StructuralGeometry,
            "BSP node plane cells address the local plane domain."),
        ColCompiler(
            InitialMpMapSerializedFamily.ColMapMp_Leaves,
            "$.definition.{numLeafs,leafs[]}",
            MapCompilerSubsystem.Collision,
            MapCompilerMilestone.M3StructuralGeometry,
            "Leaf contents and AABB/brush/surface ranges are generated topology."),
        ColCompiler(
            InitialMpMapSerializedFamily.ColMapMp_LeafBrushNodes,
            "$.definition.{leafBrushNodesCount,leafBrushNodes[]}",
            MapCompilerSubsystem.Collision,
            MapCompilerMilestone.M3StructuralGeometry,
            "Typed leaf-brush union rows and forward child offsets are compiler-owned."),
        ColCompiler(
            InitialMpMapSerializedFamily.ColMapMp_LeafBrushReferences,
            "$.definition.{numLeafBrushes,leafBrushes[]}",
            MapCompilerSubsystem.Collision,
            MapCompilerMilestone.M3StructuralGeometry,
            "Leaf-brush rows address the emitted brush domain."),
        ColPreserve(
            InitialMpMapSerializedFamily.ColMapMp_LeafSurfaceReferences,
            "$.definition.{numLeafSurfaces,leafSurfaces[]}",
            MapCompilerSubsystem.Collision,
            MapRuntimeSubsystem.Collision,
            MapCompilerMilestone.M3StructuralGeometry,
            MapOwnershipReadiness.ReverseEngineeringPending,
            "The UInt32 storage and root count are decoded, but the target " +
            "domain and sentinel semantics remain unresolved. Imported rows " +
            "are preservation-only and authored emission fails closed."),
        ColCompiler(
            InitialMpMapSerializedFamily.ColMapMp_TriangleVertices,
            "$.definition.{vertCount,verts[]}",
            MapCompilerSubsystem.Collision,
            MapCompilerMilestone.M3StructuralGeometry,
            "Collision vertices are source geometry compiler output."),
        ColCompiler(
            InitialMpMapSerializedFamily.ColMapMp_TriangleIndices,
            "$.definition.{triCount,triIndices[]}",
            MapCompilerSubsystem.Collision,
            MapCompilerMilestone.M3StructuralGeometry,
            "Triangle ushort values are partition-segment-relative, not one global 65K vertex domain."),
        ColCompiler(
            InitialMpMapSerializedFamily.ColMapMp_TriangleWalkability,
            "$.definition.triEdgeIsWalkable[]",
            MapCompilerSubsystem.Collision,
            MapCompilerMilestone.M3StructuralGeometry,
            "Walkability is deterministically packed LSB-first from triangle edge semantics."),
        ColCompiler(
            InitialMpMapSerializedFamily.ColMapMp_Borders,
            "$.definition.{borderCount,borders[]}",
            MapCompilerSubsystem.Collision,
            MapCompilerMilestone.M3StructuralGeometry,
            "The global collision-border table is structural compiler output."),
        ColCompiler(
            InitialMpMapSerializedFamily.ColMapMp_Partitions,
            "$.definition.{partitionCount,partitions[]}",
            MapCompilerSubsystem.Collision,
            MapCompilerMilestone.M3StructuralGeometry,
            "Partition triangle ranges and FirstVertSegment selectors are compiler-owned."),
        ColCompiler(
            InitialMpMapSerializedFamily.ColMapMp_PartitionBorders,
            "$.definition.partitions[].borders[]",
            MapCompilerSubsystem.Collision,
            MapCompilerMilestone.M3StructuralGeometry,
            "Nested partition border payloads are emitted with their partition."),
        ColCompiler(
            InitialMpMapSerializedFamily.ColMapMp_AabbTrees,
            "$.definition.{aabbTreeCount,aabbTrees[]}",
            MapCompilerSubsystem.Collision,
            MapCompilerMilestone.M3StructuralGeometry,
            "AABB child ranges and partition selectors use the typed consumer-proven union."),
        ColCompiler(
            InitialMpMapSerializedFamily.ColMapMp_InlineModels,
            "$.definition.{numSubModels,cmodels[]}",
            MapCompilerSubsystem.Collision,
            MapCompilerMilestone.M3StructuralGeometry,
            "World and brush-model collision rows share the Gfx/MapEnt inline-model ordinal."),
        ColCompiler(
            InitialMpMapSerializedFamily.ColMapMp_Brushes,
            "$.definition.{numBrushes,brushes[]}",
            MapCompilerSubsystem.Collision,
            MapCompilerMilestone.M3StructuralGeometry,
            "Brush roots, axial materials, glass handles, and adjacency ranges are compiler products."),
        ColCompiler(
            InitialMpMapSerializedFamily.ColMapMp_BrushNestedSides,
            "$.definition.brushes[].sides[]",
            MapCompilerSubsystem.Collision,
            MapCompilerMilestone.M3StructuralGeometry,
            "Per-brush side payloads use explicit local ownership even when imported storage aliases the global table."),
        ColCompiler(
            InitialMpMapSerializedFamily.ColMapMp_BrushAdjacency,
            "$.definition.brushes[].baseAdjacentSide[]",
            MapCompilerSubsystem.Collision,
            MapCompilerMilestone.M3StructuralGeometry,
            "Per-brush adjacency bytes are generated with the owning convex brush."),
        ColCompiler(
            InitialMpMapSerializedFamily.ColMapMp_BrushBounds,
            "$.definition.{numBrushes,brushBounds[]}",
            MapCompilerSubsystem.Collision,
            MapCompilerMilestone.M3StructuralGeometry,
            "Brush bounds form a cardinality-locked parallel table."),
        ColCompiler(
            InitialMpMapSerializedFamily.ColMapMp_BrushContents,
            "$.definition.{numBrushes,brushContents[]}",
            MapCompilerSubsystem.Collision,
            MapCompilerMilestone.M3StructuralGeometry,
            "Brush contents form a cardinality-locked parallel table."),
        ColLink(
            InitialMpMapSerializedFamily.ColMapMp_MapEntsReference,
            "$.definition.mapEnts",
            MapCompilerSubsystem.CrossAssetLinker,
            XAssetType.MapEnts,
            "ColMap links the same-name MapEnts authority through explicit nested/external provenance."),
        ColCompiler(
            InitialMpMapSerializedFamily.ColMapMp_StaticModelAabbNodes,
            "$.definition.{smodelNodeCount,smodelNodes[]}",
            MapCompilerSubsystem.VisibilityAndSpatial,
            MapCompilerMilestone.M4Visibility,
            "Static-model AABB nodes use the proven virtual model/node child namespace."),
        ColCompiler(
            InitialMpMapSerializedFamily.ColMapMp_DynamicEntityShape,
            "$.definition.dynEntCount[2]",
            MapCompilerSubsystem.EntityAndGameplay,
            MapCompilerMilestone.M6GlassAndGameplay,
            "Two independent dynamic-entity slot counts shape all parallel lists."),
        ColCompiler(
            InitialMpMapSerializedFamily.ColMapMp_DynamicEntityDefinitions,
            "$.definition.dynEntDefList[2][]",
            MapCompilerSubsystem.EntityAndGameplay,
            MapCompilerMilestone.M6GlassAndGameplay,
            "Dynamic definitions contain pose, brush-model ordinals, physics, health, mass, and contents."),
        ColLink(
            InitialMpMapSerializedFamily.ColMapMp_DynamicEntityReferences,
            "$.definition.dynEntDefList[2][].{xmodel,destroyFx,physPreset}",
            MapCompilerSubsystem.CrossAssetLinker,
            targetType: null,
            "Each dynamic definition links typed XModel, Fx, and PhysPreset identities."),
        ColRuntime(
            InitialMpMapSerializedFamily.ColMapMp_DynamicEntityPoseCache,
            "$.definition.dynEntPoseList[2][]",
            MapCompilerSubsystem.EntityAndGameplay,
            MapRuntimeSubsystem.Collision,
            "Dynamic poses are per-slot native RUNTIME allocations."),
        ColRuntime(
            InitialMpMapSerializedFamily.ColMapMp_DynamicEntityClientCache,
            "$.definition.dynEntClientList[2][]",
            MapCompilerSubsystem.EntityAndGameplay,
            MapRuntimeSubsystem.Collision,
            "Dynamic client rows are per-slot native RUNTIME allocations."),
        ColRuntime(
            InitialMpMapSerializedFamily.ColMapMp_DynamicEntityCollisionCache,
            "$.definition.dynEntCollList[2][]",
            MapCompilerSubsystem.EntityAndGameplay,
            MapRuntimeSubsystem.Collision,
            "Dynamic collision-link rows are per-slot native RUNTIME allocations."),
        Col(
            InitialMpMapSerializedFamily.ColMapMp_PrimaryChecksum,
            "$.definition.checksum",
            MapFieldOwnership.CompilerSerialized,
            MapSerializedOwner.CompilerSubsystem,
            MapCompilerSubsystem.CrossAssetLinker,
            MapRuntimeSubsystem.MultipleMapConsumers,
            MapCompilerMilestone.M0OwnershipLock,
            MapOwnershipReadiness.SourceProjectionReady,
            "ImportedContentPreserved output retains the exact uint32 production value; StudioAuthoredContent uses the same StudioCanonicalV1 value as GfxMap."),
        ColPreserve(
            InitialMpMapSerializedFamily.ColMapMp_RootPadding,
            "$.definition.{pad8ETo8F,padA2ToA3,padD0ToFF[]}",
            MapCompilerSubsystem.Collision,
            MapRuntimeSubsystem.Collision,
            MapCompilerMilestone.M3StructuralGeometry,
            MapOwnershipReadiness.ImportedPreservationOnly,
            "Padding is retained only from exact imported authority until canonical greenfield values are proven.")
    ];

    private static IReadOnlyList<MapCompilerOwnershipFamilyContract>
        ComMapFamilies() =>
    [
        Com(
            InitialMpMapSerializedFamily.ComMap_Identity,
            "$.definition.name",
            MapFieldOwnership.EditorSource,
            MapSerializedOwner.CompilerSubsystem,
            MapCompilerSubsystem.MapIdentity,
            MapRuntimeSubsystem.AssetDatabase,
            MapCompilerMilestone.M0OwnershipLock,
            MapOwnershipReadiness.SourceProjectionReady,
            "Canonical common-world map identity."),
        ComRuntime(
            InitialMpMapSerializedFamily.ComMap_InUseState,
            "$.definition.isInUse",
            "Common-world registration state belongs to the loader/runtime."),
        ComCompiler(
            InitialMpMapSerializedFamily.ComMap_PrimaryLights,
            "$.definition.primaryLights[]",
            "Primary-light type, color, direction, origin, influence, cone, and movement fields are lighting compiler output."),
        Com(
            InitialMpMapSerializedFamily.ComMap_PrimaryLightDefinitionNames,
            "$.definition.primaryLights[].defName",
            MapFieldOwnership.LinkerReference,
            MapSerializedOwner.TargetZoneLinker,
            MapCompilerSubsystem.CrossAssetLinker,
            MapRuntimeSubsystem.AssetDatabase,
            MapCompilerMilestone.M7GreenfieldLink,
            MapOwnershipReadiness.CrossAssetClosurePending,
            "DefName is a serialized XString resolved by native LightDef lookup; it is not a nested LightDef pointer.")
    ];

    private static IReadOnlyList<MapCompilerOwnershipFamilyContract>
        MapEntsFamilies() =>
    [
        Ent(
            InitialMpMapSerializedFamily.MapEnts_Identity,
            "$.definition.name",
            MapFieldOwnership.EditorSource,
            MapSerializedOwner.CompilerSubsystem,
            MapCompilerSubsystem.MapIdentity,
            MapRuntimeSubsystem.AssetDatabase,
            MapCompilerMilestone.M0OwnershipLock,
            MapOwnershipReadiness.SourceProjectionReady,
            "Canonical entity-root map identity."),
        Ent(
            InitialMpMapSerializedFamily.MapEnts_EntityString,
            "$.definition.entityStringBytes[]",
            MapFieldOwnership.EditorSource,
            MapSerializedOwner.CompilerSubsystem,
            MapCompilerSubsystem.EntityAndGameplay,
            MapRuntimeSubsystem.GameEntitySystem,
            MapCompilerMilestone.M6GlassAndGameplay,
            MapOwnershipReadiness.CompilerAlgorithmPending,
            "Canonical entity semantics serialize to deterministic Latin-1 entity bytes and exact count."),
        Ent(
            InitialMpMapSerializedFamily.MapEnts_BrushModelOrdinals,
            "$.definition.entityString.entities[].modelBrushOrdinal",
            MapFieldOwnership.CompilerSerialized,
            MapSerializedOwner.CompilerSubsystem,
            MapCompilerSubsystem.CrossAssetLinker,
            MapRuntimeSubsystem.MultipleMapConsumers,
            MapCompilerMilestone.M0OwnershipLock,
            MapOwnershipReadiness.OwnershipLocked,
            "Text model '*n' addresses GfxMap.models[n] and ColMapMp.cmodels[n] with no remap."),
        EntCompiler(
            InitialMpMapSerializedFamily.MapEnts_TriggerShape,
            "$.definition.trigger.{count,hullCount,slabCount}",
            "Trigger cardinalities shape three compiler-owned parallel domains."),
        EntCompiler(
            InitialMpMapSerializedFamily.MapEnts_TriggerModels,
            "$.definition.trigger.models[]",
            "Trigger model contents and hull ranges are gameplay compiler products."),
        EntCompiler(
            InitialMpMapSerializedFamily.MapEnts_TriggerHulls,
            "$.definition.trigger.hulls[]",
            "Trigger hull bounds, contents, and slab ranges are gameplay compiler products."),
        EntCompiler(
            InitialMpMapSerializedFamily.MapEnts_TriggerSlabs,
            "$.definition.trigger.slabs[]",
            "Trigger slab planes are generated from authored gameplay volumes."),
        EntCompiler(
            InitialMpMapSerializedFamily.MapEnts_Stages,
            "$.definition.stages[]",
            "Stage origin, trigger index, light index, and count are gameplay compiler products."),
        Ent(
            InitialMpMapSerializedFamily.MapEnts_StageNames,
            "$.definition.stages[].name",
            MapFieldOwnership.EditorSource,
            MapSerializedOwner.CompilerSubsystem,
            MapCompilerSubsystem.EntityAndGameplay,
            MapRuntimeSubsystem.GameEntitySystem,
            MapCompilerMilestone.M6GlassAndGameplay,
            MapOwnershipReadiness.CompilerAlgorithmPending,
            "Stage names are compiler-emitted XStrings, not XAsset references."),
        EntPreserve(
            InitialMpMapSerializedFamily.MapEnts_RootPadding,
            "$.definition.pad29To2B[]",
            "Root tail bytes remain imported-preservation state until canonical greenfield values are proven.")
    ];

    private static IReadOnlyList<MapCompilerOwnershipFamilyContract>
        FxMapFamilies() =>
    [
        Fx(
            InitialMpMapSerializedFamily.FxMap_Identity,
            "$.definition.name",
            MapFieldOwnership.EditorSource,
            MapSerializedOwner.CompilerSubsystem,
            MapCompilerSubsystem.MapIdentity,
            MapRuntimeSubsystem.AssetDatabase,
            MapCompilerMilestone.M0OwnershipLock,
            MapOwnershipReadiness.SourceProjectionReady,
            "Canonical effects-world map identity."),
        FxCompiler(
            InitialMpMapSerializedFamily.FxMap_GlassSourceShape,
            "$.definition.glassSystem.{defCount,pieceLimit,pieceWordCount,initPieceCount,cellCount,geoDataLimit,initGeoDataCount}",
            "Source counts and capacity limits shape serialized initial data and native runtime allocations."),
        FxRuntime(
            InitialMpMapSerializedFamily.FxMap_GlassRuntimeStateHeader,
            "$.definition.glassSystem.{time,prevTime,activePieceCount,firstFreePiece,geoDataCount,needToCompactData,initCount,effectChanceAccum,lastPieceDeletionTime}",
            "Mutable glass lifecycle state is initialized and owned by the native glass runtime."),
        FxCompiler(
            InitialMpMapSerializedFamily.FxMap_GlassDefinitions,
            "$.definition.glassSystem.defs[]",
            "Definition thickness, texture vectors, color, and mip radii require the glass compiler."),
        FxLink(
            InitialMpMapSerializedFamily.FxMap_GlassDefinitionReferences,
            "$.definition.glassSystem.defs[].{material,materialShattered,physPreset}",
            "Each glass definition links two Material identities and one PhysPreset identity."),
        FxRuntime(
            InitialMpMapSerializedFamily.FxMap_RuntimePiecePlaces,
            "$.definition.glassSystem.piecePlaces[]",
            "Mutable piece-place rows are zero-filled native RUNTIME allocations."),
        FxRuntime(
            InitialMpMapSerializedFamily.FxMap_RuntimePieceStates,
            "$.definition.glassSystem.pieceStates[]",
            "Mutable piece-state rows are zero-filled native RUNTIME allocations."),
        FxRuntime(
            InitialMpMapSerializedFamily.FxMap_RuntimePieceDynamics,
            "$.definition.glassSystem.pieceDynamics[]",
            "Mutable piece-dynamics rows are zero-filled native RUNTIME allocations."),
        FxRuntime(
            InitialMpMapSerializedFamily.FxMap_RuntimeGeometry,
            "$.definition.glassSystem.geoData[]",
            "Mutable geometry storage is a zero-filled native RUNTIME allocation."),
        FxRuntime(
            InitialMpMapSerializedFamily.FxMap_RuntimeInUseBits,
            "$.definition.glassSystem.isInUse[]",
            "Piece occupancy words are zero-filled native RUNTIME allocations."),
        FxRuntime(
            InitialMpMapSerializedFamily.FxMap_RuntimeCellBits,
            "$.definition.glassSystem.cellBits[]",
            "Glass cell membership cache is a zero-filled native RUNTIME allocation."),
        FxRuntime(
            InitialMpMapSerializedFamily.FxMap_RuntimeVisibility,
            "$.definition.glassSystem.visData[]",
            "Glass visibility bytes are a zero-filled aligned native RUNTIME allocation."),
        FxRuntime(
            InitialMpMapSerializedFamily.FxMap_RuntimeLinkOrigins,
            "$.definition.glassSystem.linkOrg[]",
            "Runtime link origins are a zero-filled native RUNTIME allocation."),
        FxRuntime(
            InitialMpMapSerializedFamily.FxMap_RuntimeHalfThickness,
            "$.definition.glassSystem.halfThickness[]",
            "Runtime thickness cache is zero-filled and distinct from serialized definition half-thickness."),
        FxCompiler(
            InitialMpMapSerializedFamily.FxMap_LightingHandles,
            "$.definition.glassSystem.lightingHandles[]",
            "One serialized lighting handle is assigned per initial piece."),
        FxCompiler(
            InitialMpMapSerializedFamily.FxMap_InitialPieceStates,
            "$.definition.glassSystem.initPieceStates[]",
            "Initial frames, texture coordinates, support masks, areas, definition indices, and topology counts are glass compiler output."),
        FxCompiler(
            InitialMpMapSerializedFamily.FxMap_InitialGeometry,
            "$.definition.glassSystem.initGeoData[]",
            "Packed initial glass geometry is serialized source for native initialization.")
    ];

    private static IReadOnlyList<MapCompilerOwnershipFamilyContract>
        GameMapFamilies() =>
    [
        Game(
            InitialMpMapSerializedFamily.GameMapMp_Identity,
            "$.definition.name",
            MapFieldOwnership.EditorSource,
            MapSerializedOwner.CompilerSubsystem,
            MapCompilerSubsystem.MapIdentity,
            MapRuntimeSubsystem.AssetDatabase,
            MapCompilerMilestone.M0OwnershipLock,
            MapOwnershipReadiness.SourceProjectionReady,
            "Canonical multiplayer game-world identity."),
        GameCompiler(
            InitialMpMapSerializedFamily.GameMapMp_GlassPresenceAndShape,
            "$.definition.glassData.{presence,pieceCount,glassNameCount}",
            "Optional gameplay-glass root presence and cardinalities are glass compiler products."),
        GameCompiler(
            InitialMpMapSerializedFamily.GameMapMp_GlassPieces,
            "$.definition.glassData.glassPieces[]",
            "Gameplay damage/state rows share the zero-based Fx initial-piece ordinal."),
        GameCompiler(
            InitialMpMapSerializedFamily.GameMapMp_GlassDamageThresholds,
            "$.definition.glassData.{damageToWeaken,damageToDestroy}",
            "Gameplay glass damage thresholds are authored glass/gameplay input."),
        GameCompiler(
            InitialMpMapSerializedFamily.GameMapMp_GlassNames,
            "$.definition.glassData.glassNames[]",
            "Glass-name groups and counts are emitted by the glass/gameplay compiler."),
        Game(
            InitialMpMapSerializedFamily.GameMapMp_GlassNameStrings,
            "$.definition.glassData.glassNames[].nameStr",
            MapFieldOwnership.EditorSource,
            MapSerializedOwner.CompilerSubsystem,
            MapCompilerSubsystem.Glass,
            MapRuntimeSubsystem.GameplayGlass,
            MapCompilerMilestone.M6GlassAndGameplay,
            MapOwnershipReadiness.CompilerAlgorithmPending,
            "Glass-name XStrings are emitted from semantic group names."),
        Game(
            InitialMpMapSerializedFamily.GameMapMp_GlassNameScriptStrings,
            "$.definition.glassData.glassNames[].name",
            MapFieldOwnership.LinkerReference,
            MapSerializedOwner.TargetZoneLinker,
            MapCompilerSubsystem.TargetZoneLinker,
            MapRuntimeSubsystem.GameplayGlass,
            MapCompilerMilestone.M7GreenfieldLink,
            MapOwnershipReadiness.ExistingReferenceLinkingReady,
            "The ushort name is a zone script-string index resolved from the same semantic name."),
        GameCompiler(
            InitialMpMapSerializedFamily.GameMapMp_GlassPieceMemberships,
            "$.definition.glassData.glassNames[].pieceIndices[]",
            "Each group contains zero-based gameplay/Fx glass-piece ordinals."),
        GamePreserve(
            InitialMpMapSerializedFamily.GameMapMp_GlassPadding,
            "$.definition.glassData.pad14To7F[]",
            "The large gameplay-glass tail remains imported-preservation state.")
    ];

    private static IReadOnlyList<MapCompilerOwnershipFamilyContract>
        TargetZoneFamilies() =>
    [
        ZoneCompiler(
            InitialMpMapSerializedFamily.TargetZone_AssetRowIdentities,
            "$.targetZone.assetRows[].{assetType,logicalName}",
            MapCompilerSubsystem.TargetZoneLinker,
            MapRuntimeSubsystem.AssetDatabase,
            MapCompilerMilestone.M7GreenfieldLink,
            MapOwnershipReadiness.ExistingReferenceLinkingReady,
            "Every emitted row has one normalized typed asset identity."),
        ZoneCompiler(
            InitialMpMapSerializedFamily.TargetZone_AssetRowOrderAndHeaderForms,
            "$.targetZone.assetRows[].{intent,order,headerForm}",
            MapCompilerSubsystem.TargetZoneLinker,
            MapRuntimeSubsystem.AssetDatabase,
            MapCompilerMilestone.M7GreenfieldLink,
            MapOwnershipReadiness.ExistingReferenceLinkingReady,
            "The linker owns deterministic row order and top-level header representation."),
        ZoneCompiler(
            InitialMpMapSerializedFamily.TargetZone_MapRootBodies,
            "$.targetZone.assetRows[mapRoot].body",
            MapCompilerSubsystem.TargetZoneLinker,
            MapRuntimeSubsystem.AssetDatabase,
            MapCompilerMilestone.M7GreenfieldLink,
            MapOwnershipReadiness.CompilerAlgorithmPending,
            "The linker accepts map-root body emissions only after every required family is ready."),
        ZoneLink(
            InitialMpMapSerializedFamily.TargetZone_DependencyEdges,
            "$.targetZone.assetRows[].dependencies[]",
            MapCompilerSubsystem.CrossAssetLinker,
            MapRuntimeSubsystem.AssetDatabase,
            MapCompilerMilestone.M7GreenfieldLink,
            MapOwnershipReadiness.ExistingReferenceLinkingReady,
            "Typed dependency edges preserve owner path and required/optional intent."),
        ZoneLink(
            InitialMpMapSerializedFamily.TargetZone_NestedAssetDefinitions,
            "$.targetZone.assetRows[].nestedDefinitions[]",
            MapCompilerSubsystem.CrossAssetLinker,
            MapRuntimeSubsystem.AssetDatabase,
            MapCompilerMilestone.M7GreenfieldLink,
            MapOwnershipReadiness.ExistingReferenceLinkingReady,
            "Inline/insert/external source form is retained explicitly; pointer values are not identity."),
        ZoneCompiler(
            InitialMpMapSerializedFamily.TargetZone_XStrings,
            "$.targetZone.xStrings[]",
            MapCompilerSubsystem.TargetZoneLinker,
            MapRuntimeSubsystem.AssetDatabase,
            MapCompilerMilestone.M7GreenfieldLink,
            MapOwnershipReadiness.ExistingReferenceLinkingReady,
            "The linker deduplicates and allocates encoded XString payloads."),
        ZoneLink(
            InitialMpMapSerializedFamily.TargetZone_ScriptStrings,
            "$.targetZone.scriptStrings[]",
            MapCompilerSubsystem.TargetZoneLinker,
            MapRuntimeSubsystem.AssetDatabase,
            MapCompilerMilestone.M7GreenfieldLink,
            MapOwnershipReadiness.ExistingReferenceLinkingReady,
            "Script-string uses resolve through one deterministic zone table and index map."),
        ZoneCompiler(
            InitialMpMapSerializedFamily.TargetZone_BlockAllocations,
            "$.targetZone.blocks[].allocations[]",
            MapCompilerSubsystem.TargetZoneLinker,
            MapRuntimeSubsystem.AssetDatabase,
            MapCompilerMilestone.M7GreenfieldLink,
            MapOwnershipReadiness.ExistingReferenceLinkingReady,
            "Block selection, alignment, extent, and allocation order belong exclusively to the linker."),
        ZoneCompiler(
            InitialMpMapSerializedFamily.TargetZone_PointerAndAliasCells,
            "$.targetZone.{pointerCells[],persistentAliasCells[],insertCells[]}",
            MapCompilerSubsystem.TargetZoneLinker,
            MapRuntimeSubsystem.AssetDatabase,
            MapCompilerMilestone.M7GreenfieldLink,
            MapOwnershipReadiness.ExistingReferenceLinkingReady,
            "The linker encodes local pointers and canonical alias/insert cells after allocation."),
        ZoneCompiler(
            InitialMpMapSerializedFamily.TargetZone_ExternalHeaderAndBlockSizes,
            "$.targetZone.{externalSize,blockSizes[],decodedAlignment}",
            MapCompilerSubsystem.TargetZoneLinker,
            MapRuntimeSubsystem.AssetDatabase,
            MapCompilerMilestone.M7GreenfieldLink,
            MapOwnershipReadiness.ExistingReferenceLinkingReady,
            "Output policy and final emission plan own external size and block extents."),
        ZoneCompiler(
            InitialMpMapSerializedFamily.TargetZone_EncodedPayload,
            "$.targetZone.encodedPayload",
            MapCompilerSubsystem.TargetZoneLinker,
            MapRuntimeSubsystem.AssetDatabase,
            MapCompilerMilestone.M7GreenfieldLink,
            MapOwnershipReadiness.CompilerAlgorithmPending,
            "Final fastfile encoding is downstream of a complete validated asset graph."),
        ZoneLink(
            InitialMpMapSerializedFamily.TargetZone_MaterialImageTechniqueDependencies,
            "$.targetZone.dependencies.{materials,images,imageSidecars,techniqueSets,programs}[]",
            MapCompilerSubsystem.DependencyAndResource,
            MapRuntimeSubsystem.AssetDatabase,
            MapCompilerMilestone.M7GreenfieldLink,
            MapOwnershipReadiness.ExistingReferenceLinkingReady,
            "Initial scope resolves existing renderer-resource identities; new resource authoring is separate."),
        ZoneLink(
            InitialMpMapSerializedFamily.TargetZone_ModelAndSurfaceDependencies,
            "$.targetZone.dependencies.{xmodels,xmodelSurfs,physCollmaps}[]",
            MapCompilerSubsystem.DependencyAndResource,
            MapRuntimeSubsystem.AssetDatabase,
            MapCompilerMilestone.M7GreenfieldLink,
            MapOwnershipReadiness.ExistingReferenceLinkingReady,
            "Model, surface, and model-collision dependencies are explicit typed identities."),
        ZoneLink(
            InitialMpMapSerializedFamily.TargetZone_LightDefinitionDependencies,
            "$.targetZone.dependencies.lightDefs[]",
            MapCompilerSubsystem.DependencyAndResource,
            MapRuntimeSubsystem.AssetDatabase,
            MapCompilerMilestone.M7GreenfieldLink,
            MapOwnershipReadiness.CrossAssetClosurePending,
            "ComMap DefName lookup must resolve to an included existing LightDef identity."),
        ZoneLink(
            InitialMpMapSerializedFamily.TargetZone_EffectAndPhysicsDependencies,
            "$.targetZone.dependencies.{fxEffects,physPresets}[]",
            MapCompilerSubsystem.DependencyAndResource,
            MapRuntimeSubsystem.AssetDatabase,
            MapCompilerMilestone.M7GreenfieldLink,
            MapOwnershipReadiness.ExistingReferenceLinkingReady,
            "Initial scope resolves existing effect and physics asset identities."),
        ZoneLink(
            InitialMpMapSerializedFamily.TargetZone_ScriptVisionAudioDependencies,
            "$.targetZone.dependencies.{scripts,visionRawFiles,createArtRawFiles,soundAliases}[]",
            MapCompilerSubsystem.DependencyAndResource,
            MapRuntimeSubsystem.AssetDatabase,
            MapCompilerMilestone.M7GreenfieldLink,
            MapOwnershipReadiness.CrossAssetClosurePending,
            "Initial scope preserves and validates existing script, environment-file, and audio identities.")
    ];

    private static MapCompilerAssetRootContract Root(
        InitialMpMapAssetRoot root,
        XAssetType? serializedType,
        string path,
        IEnumerable<MapCompilerOwnershipFamilyContract> families) =>
        new(root, serializedType, path, families);

    private static MapCompilerOwnershipFamilyContract Gfx(
        InitialMpMapSerializedFamily family,
        string path,
        MapFieldOwnership ownership,
        MapCompilerSubsystem compiler,
        MapRuntimeSubsystem runtime,
        MapCompilerMilestone milestone,
        MapOwnershipReadiness readiness,
        string boundary) =>
        Family(
            family,
            InitialMpMapAssetRoot.GfxMap,
            path,
            ownership,
            ownership == MapFieldOwnership.LinkerReference
                ? MapSerializedOwner.TargetZoneLinker
                : MapSerializedOwner.CompilerSubsystem,
            compiler,
            runtime,
            milestone,
            readiness,
            boundary);

    private static MapCompilerOwnershipFamilyContract GfxCompiler(
        InitialMpMapSerializedFamily family,
        string path,
        MapCompilerSubsystem compiler,
        MapRuntimeSubsystem runtime,
        MapCompilerMilestone milestone,
        string boundary) =>
        Gfx(
            family,
            path,
            MapFieldOwnership.CompilerSerialized,
            compiler,
            runtime,
            milestone,
            MapOwnershipReadiness.CompilerAlgorithmPending,
            boundary);

    private static MapCompilerOwnershipFamilyContract GfxLink(
        InitialMpMapSerializedFamily family,
        string path,
        MapCompilerSubsystem compiler,
        MapRuntimeSubsystem runtime,
        MapCompilerMilestone milestone,
        string boundary) =>
        Gfx(
            family,
            path,
            MapFieldOwnership.LinkerReference,
            compiler,
            runtime,
            milestone,
            MapOwnershipReadiness.ExistingReferenceLinkingReady,
            boundary);

    private static MapCompilerOwnershipFamilyContract GfxRuntime(
        InitialMpMapSerializedFamily family,
        string path,
        MapCompilerSubsystem compiler,
        MapRuntimeSubsystem runtime,
        string boundary) =>
        Family(
            family,
            InitialMpMapAssetRoot.GfxMap,
            path,
            MapFieldOwnership.RuntimeDerived,
            MapSerializedOwner.RuntimeInitializationContract,
            compiler,
            runtime,
            MapCompilerMilestone.M0OwnershipLock,
            MapOwnershipReadiness.RuntimeDerivationProven,
            boundary);

    private static MapCompilerOwnershipFamilyContract GfxPreserve(
        InitialMpMapSerializedFamily family,
        string path,
        MapCompilerSubsystem compiler,
        MapRuntimeSubsystem runtime,
        MapCompilerMilestone milestone,
        MapOwnershipReadiness readiness,
        string boundary) =>
        Preserve(
            family,
            InitialMpMapAssetRoot.GfxMap,
            path,
            compiler,
            runtime,
            milestone,
            readiness,
            boundary);

    private static MapCompilerOwnershipFamilyContract Col(
        InitialMpMapSerializedFamily family,
        string path,
        MapFieldOwnership ownership,
        MapSerializedOwner serializedOwner,
        MapCompilerSubsystem compiler,
        MapRuntimeSubsystem runtime,
        MapCompilerMilestone milestone,
        MapOwnershipReadiness readiness,
        string boundary) =>
        Family(
            family,
            InitialMpMapAssetRoot.ColMapMp,
            path,
            ownership,
            serializedOwner,
            compiler,
            runtime,
            milestone,
            readiness,
            boundary);

    private static MapCompilerOwnershipFamilyContract ColCompiler(
        InitialMpMapSerializedFamily family,
        string path,
        MapCompilerSubsystem compiler,
        MapCompilerMilestone milestone,
        string boundary) =>
        Col(
            family,
            path,
            MapFieldOwnership.CompilerSerialized,
            MapSerializedOwner.CompilerSubsystem,
            compiler,
            MapRuntimeSubsystem.Collision,
            milestone,
            MapOwnershipReadiness.CompilerAlgorithmPending,
            boundary);

    private static MapCompilerOwnershipFamilyContract ColLink(
        InitialMpMapSerializedFamily family,
        string path,
        MapCompilerSubsystem compiler,
        XAssetType? targetType,
        string boundary) =>
        Col(
            family,
            path,
            MapFieldOwnership.LinkerReference,
            MapSerializedOwner.TargetZoneLinker,
            compiler,
            MapRuntimeSubsystem.AssetDatabase,
            MapCompilerMilestone.M7GreenfieldLink,
            targetType is null
                ? MapOwnershipReadiness.CrossAssetClosurePending
                : MapOwnershipReadiness.ExistingReferenceLinkingReady,
            boundary);

    private static MapCompilerOwnershipFamilyContract ColRuntime(
        InitialMpMapSerializedFamily family,
        string path,
        MapCompilerSubsystem compiler,
        MapRuntimeSubsystem runtime,
        string boundary) =>
        Col(
            family,
            path,
            MapFieldOwnership.RuntimeDerived,
            MapSerializedOwner.RuntimeInitializationContract,
            compiler,
            runtime,
            MapCompilerMilestone.M0OwnershipLock,
            MapOwnershipReadiness.RuntimeDerivationProven,
            boundary);

    private static MapCompilerOwnershipFamilyContract ColPreserve(
        InitialMpMapSerializedFamily family,
        string path,
        MapCompilerSubsystem compiler,
        MapRuntimeSubsystem runtime,
        MapCompilerMilestone milestone,
        MapOwnershipReadiness readiness,
        string boundary) =>
        Preserve(
            family,
            InitialMpMapAssetRoot.ColMapMp,
            path,
            compiler,
            runtime,
            milestone,
            readiness,
            boundary);

    private static MapCompilerOwnershipFamilyContract Com(
        InitialMpMapSerializedFamily family,
        string path,
        MapFieldOwnership ownership,
        MapSerializedOwner serializedOwner,
        MapCompilerSubsystem compiler,
        MapRuntimeSubsystem runtime,
        MapCompilerMilestone milestone,
        MapOwnershipReadiness readiness,
        string boundary) =>
        Family(
            family,
            InitialMpMapAssetRoot.ComMap,
            path,
            ownership,
            serializedOwner,
            compiler,
            runtime,
            milestone,
            readiness,
            boundary);

    private static MapCompilerOwnershipFamilyContract ComCompiler(
        InitialMpMapSerializedFamily family,
        string path,
        string boundary) =>
        Com(
            family,
            path,
            MapFieldOwnership.CompilerSerialized,
            MapSerializedOwner.CompilerSubsystem,
            MapCompilerSubsystem.LightingAndEnvironment,
            MapRuntimeSubsystem.CommonWorld,
            MapCompilerMilestone.M5LightingEnvironment,
            MapOwnershipReadiness.CompilerAlgorithmPending,
            boundary);

    private static MapCompilerOwnershipFamilyContract ComRuntime(
        InitialMpMapSerializedFamily family,
        string path,
        string boundary) =>
        Com(
            family,
            path,
            MapFieldOwnership.RuntimeDerived,
            MapSerializedOwner.RuntimeInitializationContract,
            MapCompilerSubsystem.LightingAndEnvironment,
            MapRuntimeSubsystem.CommonWorld,
            MapCompilerMilestone.M0OwnershipLock,
            MapOwnershipReadiness.RuntimeDerivationProven,
            boundary);

    private static MapCompilerOwnershipFamilyContract Ent(
        InitialMpMapSerializedFamily family,
        string path,
        MapFieldOwnership ownership,
        MapSerializedOwner serializedOwner,
        MapCompilerSubsystem compiler,
        MapRuntimeSubsystem runtime,
        MapCompilerMilestone milestone,
        MapOwnershipReadiness readiness,
        string boundary) =>
        Family(
            family,
            InitialMpMapAssetRoot.MapEnts,
            path,
            ownership,
            serializedOwner,
            compiler,
            runtime,
            milestone,
            readiness,
            boundary);

    private static MapCompilerOwnershipFamilyContract EntCompiler(
        InitialMpMapSerializedFamily family,
        string path,
        string boundary) =>
        Ent(
            family,
            path,
            MapFieldOwnership.CompilerSerialized,
            MapSerializedOwner.CompilerSubsystem,
            MapCompilerSubsystem.EntityAndGameplay,
            MapRuntimeSubsystem.GameEntitySystem,
            MapCompilerMilestone.M6GlassAndGameplay,
            MapOwnershipReadiness.CompilerAlgorithmPending,
            boundary);

    private static MapCompilerOwnershipFamilyContract EntPreserve(
        InitialMpMapSerializedFamily family,
        string path,
        string boundary) =>
        Preserve(
            family,
            InitialMpMapAssetRoot.MapEnts,
            path,
            MapCompilerSubsystem.EntityAndGameplay,
            MapRuntimeSubsystem.GameEntitySystem,
            MapCompilerMilestone.M6GlassAndGameplay,
            MapOwnershipReadiness.ImportedPreservationOnly,
            boundary);

    private static MapCompilerOwnershipFamilyContract Fx(
        InitialMpMapSerializedFamily family,
        string path,
        MapFieldOwnership ownership,
        MapSerializedOwner serializedOwner,
        MapCompilerSubsystem compiler,
        MapRuntimeSubsystem runtime,
        MapCompilerMilestone milestone,
        MapOwnershipReadiness readiness,
        string boundary) =>
        Family(
            family,
            InitialMpMapAssetRoot.FxMap,
            path,
            ownership,
            serializedOwner,
            compiler,
            runtime,
            milestone,
            readiness,
            boundary);

    private static MapCompilerOwnershipFamilyContract FxCompiler(
        InitialMpMapSerializedFamily family,
        string path,
        string boundary) =>
        Fx(
            family,
            path,
            MapFieldOwnership.CompilerSerialized,
            MapSerializedOwner.CompilerSubsystem,
            MapCompilerSubsystem.Glass,
            MapRuntimeSubsystem.EffectsGlass,
            MapCompilerMilestone.M6GlassAndGameplay,
            MapOwnershipReadiness.CompilerAlgorithmPending,
            boundary);

    private static MapCompilerOwnershipFamilyContract FxLink(
        InitialMpMapSerializedFamily family,
        string path,
        string boundary) =>
        Fx(
            family,
            path,
            MapFieldOwnership.LinkerReference,
            MapSerializedOwner.TargetZoneLinker,
            MapCompilerSubsystem.CrossAssetLinker,
            MapRuntimeSubsystem.AssetDatabase,
            MapCompilerMilestone.M7GreenfieldLink,
            MapOwnershipReadiness.ExistingReferenceLinkingReady,
            boundary);

    private static MapCompilerOwnershipFamilyContract FxRuntime(
        InitialMpMapSerializedFamily family,
        string path,
        string boundary) =>
        Fx(
            family,
            path,
            MapFieldOwnership.RuntimeDerived,
            MapSerializedOwner.RuntimeInitializationContract,
            MapCompilerSubsystem.Glass,
            MapRuntimeSubsystem.EffectsGlass,
            MapCompilerMilestone.M0OwnershipLock,
            MapOwnershipReadiness.RuntimeDerivationProven,
            boundary);

    private static MapCompilerOwnershipFamilyContract Game(
        InitialMpMapSerializedFamily family,
        string path,
        MapFieldOwnership ownership,
        MapSerializedOwner serializedOwner,
        MapCompilerSubsystem compiler,
        MapRuntimeSubsystem runtime,
        MapCompilerMilestone milestone,
        MapOwnershipReadiness readiness,
        string boundary) =>
        Family(
            family,
            InitialMpMapAssetRoot.GameMapMp,
            path,
            ownership,
            serializedOwner,
            compiler,
            runtime,
            milestone,
            readiness,
            boundary);

    private static MapCompilerOwnershipFamilyContract GameCompiler(
        InitialMpMapSerializedFamily family,
        string path,
        string boundary) =>
        Game(
            family,
            path,
            MapFieldOwnership.CompilerSerialized,
            MapSerializedOwner.CompilerSubsystem,
            MapCompilerSubsystem.Glass,
            MapRuntimeSubsystem.GameplayGlass,
            MapCompilerMilestone.M6GlassAndGameplay,
            MapOwnershipReadiness.CompilerAlgorithmPending,
            boundary);

    private static MapCompilerOwnershipFamilyContract GamePreserve(
        InitialMpMapSerializedFamily family,
        string path,
        string boundary) =>
        Preserve(
            family,
            InitialMpMapAssetRoot.GameMapMp,
            path,
            MapCompilerSubsystem.Glass,
            MapRuntimeSubsystem.GameplayGlass,
            MapCompilerMilestone.M6GlassAndGameplay,
            MapOwnershipReadiness.ImportedPreservationOnly,
            boundary);

    private static MapCompilerOwnershipFamilyContract ZoneCompiler(
        InitialMpMapSerializedFamily family,
        string path,
        MapCompilerSubsystem compiler,
        MapRuntimeSubsystem runtime,
        MapCompilerMilestone milestone,
        MapOwnershipReadiness readiness,
        string boundary) =>
        Family(
            family,
            InitialMpMapAssetRoot.TargetZone,
            path,
            MapFieldOwnership.CompilerSerialized,
            MapSerializedOwner.CompilerSubsystem,
            compiler,
            runtime,
            milestone,
            readiness,
            boundary);

    private static MapCompilerOwnershipFamilyContract ZoneLink(
        InitialMpMapSerializedFamily family,
        string path,
        MapCompilerSubsystem compiler,
        MapRuntimeSubsystem runtime,
        MapCompilerMilestone milestone,
        MapOwnershipReadiness readiness,
        string boundary) =>
        Family(
            family,
            InitialMpMapAssetRoot.TargetZone,
            path,
            MapFieldOwnership.LinkerReference,
            MapSerializedOwner.TargetZoneLinker,
            compiler,
            runtime,
            milestone,
            readiness,
            boundary);

    private static MapCompilerOwnershipFamilyContract Preserve(
        InitialMpMapSerializedFamily family,
        InitialMpMapAssetRoot root,
        string path,
        MapCompilerSubsystem compiler,
        MapRuntimeSubsystem runtime,
        MapCompilerMilestone milestone,
        MapOwnershipReadiness readiness,
        string boundary) =>
        Family(
            family,
            root,
            path,
            MapFieldOwnership.ImportedPreservation,
            MapSerializedOwner.ImportedBaseline,
            compiler,
            runtime,
            milestone,
            readiness,
            boundary);

    private static MapCompilerOwnershipFamilyContract Family(
        InitialMpMapSerializedFamily family,
        InitialMpMapAssetRoot root,
        string path,
        MapFieldOwnership ownership,
        MapSerializedOwner serializedOwner,
        MapCompilerSubsystem compiler,
        MapRuntimeSubsystem runtime,
        MapCompilerMilestone milestone,
        MapOwnershipReadiness readiness,
        string boundary) =>
        new(
            family,
            root,
            path,
            ownership,
            serializedOwner,
            compiler,
            runtime,
            milestone,
            readiness,
            boundary);
}
