using IW4.Assets.Assets.GfxMap;
using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Emission;
using System.Collections;
using System.Reflection;

namespace IW4.FastFiles.Emitters.Assets;

/// <summary>
/// Checked GfxWorld root contract. The DPVS plane/node branch is emitted here,
/// including its RUNTIME-only scene bit allocation. Other world-owned
/// subsystems are rejected because this serializer has no source recipe for
/// them.
/// </summary>
public sealed class GfxWorldBodyEmitter : IXAssetBodyEmitter
{
    public XAssetType AssetType => XAssetType.GfxMap;

    public IReadOnlyList<EmissionError> Validate(IXAssetBuildData buildData, int? rowIndex = null)
    {
        var diagnostics = AssetBodyEmitterHelpers.ValidateIdentity(buildData, AssetType, rowIndex);
        if (buildData is not IGfxWorldBuildData data)
        {
            diagnostics.Add(Error("body", "GfxMap build data does not implement IGfxWorldBuildData.", rowIndex));
            return diagnostics;
        }
        GfxWorldAsset value = data.Definition;
        GfxWorldReferenceBuildData references = data.References;
        String(value.Name, "name"); String(value.BaseName, "baseName");
        if (value.PlaneCount != value.DpvsPlanes.Planes.Count)
            diagnostics.Add(Error("planeCount", "PlaneCount must equal dpvsPlanes.planes.Count.", rowIndex));
        if (value.NodeCount != value.DpvsPlanes.Nodes.Count)
            diagnostics.Add(Error("nodeCount", "NodeCount must equal dpvsPlanes.nodes.Count.", rowIndex));
        if (value.SkyCount != value.Skies.Count)
            diagnostics.Add(Error("skyCount", "SkyCount must equal skies.Count.", rowIndex));
        if (value.ModelCount != value.Models.Count)
            diagnostics.Add(Error("modelCount", "ModelCount must equal models.Count.", rowIndex));
        if (value.MaterialMemoryCount != value.MaterialMemory.Count)
            diagnostics.Add(Error("materialMemoryCount", "MaterialMemoryCount must equal materialMemory.Count.", rowIndex));
        if (value.HeroOnlyLightCount != value.HeroOnlyLights.Count)
            diagnostics.Add(Error("heroOnlyLightCount", "HeroOnlyLightCount must equal heroOnlyLights.Count.", rowIndex));
        if (value.DpvsPlanes.CellCount < 0)
            diagnostics.Add(Error("dpvsPlanes.cellCount", "CellCount cannot be negative.", rowIndex));
        else if (value.DpvsPlanes.SceneEntCellBits.Count != checked(value.DpvsPlanes.CellCount << 9))
            diagnostics.Add(Error("dpvsPlanes.sceneEntCellBits", "RUNTIME scene-ent cell-bit allocation length must equal CellCount * 512.", rowIndex));
        else if (value.DpvsPlanes.SceneEntCellBits.Any(value => value != 0))
            diagnostics.Add(Error("dpvsPlanes.sceneEntCellBits", "RUNTIME scene-ent cell bits are zero-filled and cannot carry authored source values.", rowIndex));
        if (value.CellTreeCounts.Count != value.DpvsPlanes.CellCount)
            diagnostics.Add(Error("cellTreeCounts", "Cell-tree count rows must equal dpvsPlanes.cellCount.", rowIndex));
        if (value.CellTrees.Count != value.CellTreeCounts.Count)
            diagnostics.Add(Error("cellTrees", "Cell-tree rows must equal cellTreeCounts rows.", rowIndex));
        if (value.Cells.Count != value.DpvsPlanes.CellCount)
            diagnostics.Add(Error("cells", "Cell rows must equal dpvsPlanes.cellCount.", rowIndex));
        for (int index = 0; index < value.CellTrees.Count; index++)
        {
            if (value.CellTrees[index].AabbTrees.Count != value.CellTreeCounts[index].AabbTreeCount)
                diagnostics.Add(Error($"cellTrees[{index}].aabbTrees", "AABB-tree count must equal its cell-tree count row.", rowIndex));
            for (int treeIndex = 0;
                 treeIndex < value.CellTrees[index].AabbTrees.Count;
                 treeIndex++)
            {
                GfxAabbTree tree =
                    value.CellTrees[index].AabbTrees[treeIndex];
                if (tree.SModelIndexCount != tree.SModelIndexes.Count)
                {
                    diagnostics.Add(Error(
                        $"cellTrees[{index}].aabbTrees[{treeIndex}].smodelIndexes",
                        "SModelIndexCount must equal the static-model index list length.",
                        rowIndex));
                }
            }
        }
        CheckCount(value.Mins, 3, "mins"); CheckCount(value.Maxs, 3, "maxs"); CheckCount(value.OutdoorLookupMatrix, 16, "outdoorLookupMatrix"); CheckCount(value.Sun.SunFxPosition, 3, "sun.sunFxPosition");
        for (int index = 0; index < value.Models.Count; index++)
        {
            CheckCount(value.Models[index].WritableMins, 3, $"models[{index}].writableMins"); CheckCount(value.Models[index].WritableMaxs, 3, $"models[{index}].writableMaxs");
            CheckCount(value.Models[index].BoundsMins, 3, $"models[{index}].boundsMins"); CheckCount(value.Models[index].BoundsMaxs, 3, $"models[{index}].boundsMaxs");
        }
        ValidateWorldDraw(value.WorldDraw);
        ValidateReferences(references);
        ValidateShadowGeometry(value.ShadowGeom); ValidateLightRegions(value.LightRegions); ValidateDpvsStatic(value.Dpvs); ValidateDpvsDynamic(value.DpvsDyn);
        for (int index = 0; index < value.Cells.Count; index++)
        {
            GfxCell cell = value.Cells[index];
            if (cell.PortalCount != cell.Portals.Count)
                diagnostics.Add(Error($"cells[{index}].portals", "PortalCount must equal the portal list length.", rowIndex));
            if (cell.ReflectionProbeCount != cell.ReflectionProbes.Count)
                diagnostics.Add(Error($"cells[{index}].reflectionProbes", "ReflectionProbeCount must equal the byte list length.", rowIndex));
            if (cell.Pad21.Count != 3)
                diagnostics.Add(Error($"cells[{index}].pad21", "Cell padding requires exactly three bytes.", rowIndex));
            for (int portal = 0; portal < cell.Portals.Count; portal++)
            {
                GfxPortal value2 = cell.Portals[portal];
                if (value2.VertexCount != value2.Vertices.Count)
                    diagnostics.Add(Error($"cells[{index}].portals[{portal}].vertices", "VertexCount must equal the vertex list length.", rowIndex));
                if (value2.HullAxis.Count != 6)
                    diagnostics.Add(Error($"cells[{index}].portals[{portal}].hullAxis", "Portal hull axis requires exactly six floats.", rowIndex));
            }
        }
        GfxLightGrid lightGrid = value.LightGrid;
        if (lightGrid.Mins.Count != 3 || lightGrid.Maxs.Count != 3)
            diagnostics.Add(Error("lightGrid.bounds", "Light-grid minimum and maximum coordinates require exactly three ushort values each.", rowIndex));
        else if (lightGrid.RowAxis > 2)
            diagnostics.Add(Error("lightGrid.rowAxis", "Light-grid row axis must be 0, 1, or 2.", rowIndex));
        else
        {
            int rowDataStartCount = checked(lightGrid.Maxs[(int)lightGrid.RowAxis] - lightGrid.Mins[(int)lightGrid.RowAxis] + 1);
            if (lightGrid.RowDataStart.Count != rowDataStartCount)
                diagnostics.Add(Error("lightGrid.rowDataStart", "Row-data-start length must cover the inclusive selected row-axis range.", rowIndex));
        }
        if (lightGrid.RawRowDataSize != lightGrid.RawRowData.Count)
            diagnostics.Add(Error("lightGrid.rawRowData", "Raw-row-data size must equal the byte payload length.", rowIndex));
        if (lightGrid.EntryCount != lightGrid.Entries.Count)
            diagnostics.Add(Error("lightGrid.entries", "EntryCount must equal the light-grid entry list length.", rowIndex));
        if (lightGrid.ColorCount != lightGrid.Colors.Count)
            diagnostics.Add(Error("lightGrid.colors", "ColorCount must equal the light-grid color list length.", rowIndex));
        for (int index = 0; index < lightGrid.Colors.Count; index++)
            if (lightGrid.Colors[index].RgbBytes.Count != GfxLightGridColors.SerializedSize)
                diagnostics.Add(Error($"lightGrid.colors[{index}]", "Each light-grid color row must preserve exactly 0xA8 bytes.", rowIndex));
        if (value.Pad279To27B.Count is not (0 or 3)) diagnostics.Add(Error("pad279To27B", "GfxWorld tail padding is absent or exactly three bytes.", rowIndex));
        else if (value.Pad279To27B.Any(item => item != 0)) diagnostics.Add(Error("pad279To27B", "The checked no-payload GfxWorld root only admits zero tail padding until the full world serializer owns this field.", rowIndex));
        return diagnostics;

        void String(string? candidate, string path)
        {
            if (candidate is not null && !AssetBodyEmitterHelpers.IsLatin1CString(candidate))
                diagnostics.Add(Error(path, "XString must be a Latin-1 C string.", rowIndex));
        }

        void CheckCount<T>(IReadOnlyList<T> values, int expected, string path)
        {
            if (values.Count != 0 && values.Count != expected)
                diagnostics.Add(Error(path, $"Requires exactly {expected} values when present.", rowIndex));
        }

        void ValidateWorldDraw(GfxWorldDraw draw)
        {
            if (draw.ReflectionProbeCount != draw.ReflectionProbeImages.Count || draw.ReflectionProbeCount != draw.ReflectionProbeOrigins.Count)
                diagnostics.Add(Error("worldDraw.reflectionProbes", "Reflection probe count must match its image and origin arrays.", rowIndex));
            if (draw.LightmapCount != draw.Lightmaps.Count)
                diagnostics.Add(Error("worldDraw.lightmaps", "LightmapCount must match lightmaps.Count.", rowIndex));
            if (draw.VertexCount < 0 || draw.VertexData.PackedVertices.Count != checked((long)draw.VertexCount * 0x10))
                diagnostics.Add(Error("worldDraw.vertexData.packedVertices", "Packed vertex bytes must equal VertexCount * 0x10.", rowIndex));
            if (draw.VertexLayerDataSize < 0 || draw.VertexLayerData.PackedLayerData.Count != draw.VertexLayerDataSize)
                diagnostics.Add(Error("worldDraw.vertexLayerData.packedLayerData", "Layer bytes must equal VertexLayerDataSize.", rowIndex));
            if (draw.IndexCount < 0 || draw.Indices.Count != draw.IndexCount)
                diagnostics.Add(Error("worldDraw.indices", "IndexCount must equal indices.Count.", rowIndex));
            if (draw.ReflectionProbeTextures.Any(item => item.Words.Any(word => word != 0)) || draw.LightmapPrimaryTextures.Any(item => item.Words.Any(word => word != 0)) || draw.LightmapSecondaryTextures.Any(item => item.Words.Any(word => word != 0)))
                diagnostics.Add(Error("worldDraw.runtimeTextures", "Post-load texture descriptor arrays are RUNTIME zero allocations and cannot carry authored source words.", rowIndex));
        }

        void ValidateReferences(GfxWorldReferenceBuildData refs)
        {
            if (refs.SkyImages.Count != value.Skies.Count || refs.ReflectionProbeImages.Count != value.WorldDraw.ReflectionProbeImages.Count || refs.Lightmaps.Count != value.WorldDraw.Lightmaps.Count || refs.MaterialMemory.Count != value.MaterialMemory.Count || refs.SurfaceMaterials.Count != value.Dpvs.Surfaces.Count || refs.StaticModelDrawInsts.Count != value.Dpvs.SModelDrawInsts.Count)
                diagnostics.Add(Error("references", "Every reachable GfxWorld asset-reference slot requires one detached symbolic entry.", rowIndex));
            CheckReferences(refs.SkyImages, XAssetType.Image, "references.skyImages"); CheckReferences(refs.ReflectionProbeImages, XAssetType.Image, "references.reflectionProbeImages");
            foreach (GfxLightmapReferenceBuildData lightmap in refs.Lightmaps) { CheckReference(lightmap.Primary, XAssetType.Image, "references.lightmaps.primary"); CheckReference(lightmap.Secondary, XAssetType.Image, "references.lightmaps.secondary"); }
            CheckReference(refs.LightmapOverridePrimary, XAssetType.Image, "references.lightmapOverridePrimary"); CheckReference(refs.LightmapOverrideSecondary, XAssetType.Image, "references.lightmapOverrideSecondary");
            CheckReferences(refs.MaterialMemory, XAssetType.Material, "references.materialMemory"); CheckReference(refs.SunSpriteMaterial, XAssetType.Material, "references.sunSpriteMaterial"); CheckReference(refs.SunFlareMaterial, XAssetType.Material, "references.sunFlareMaterial"); CheckReference(refs.OutdoorImage, XAssetType.Image, "references.outdoorImage"); CheckReferences(refs.SurfaceMaterials, XAssetType.Material, "references.surfaceMaterials"); CheckReferences(refs.StaticModelDrawInsts, XAssetType.XModel, "references.staticModelDrawInsts");
            CheckDefinitions(refs.SkyImageDefinitions, refs.SkyImages, XAssetType.Image, "references.skyImageDefinitions");
            CheckDefinitions(refs.ReflectionProbeImageDefinitions, refs.ReflectionProbeImages, XAssetType.Image, "references.reflectionProbeImageDefinitions");
            if (refs.LightmapDefinitions.Count is not 0 && refs.LightmapDefinitions.Count != refs.Lightmaps.Count)
            {
                diagnostics.Add(Error(
                    "references.lightmapDefinitions",
                    "Nested lightmap definitions must be absent for legacy external-only input or parallel every lightmap reference row.",
                    rowIndex));
            }
            else
            {
                for (int index = 0; index < refs.LightmapDefinitions.Count; index++)
                {
                    CheckDefinition(refs.LightmapDefinitions[index].Primary, refs.Lightmaps[index].Primary, XAssetType.Image, $"references.lightmapDefinitions[{index}].primary");
                    CheckDefinition(refs.LightmapDefinitions[index].Secondary, refs.Lightmaps[index].Secondary, XAssetType.Image, $"references.lightmapDefinitions[{index}].secondary");
                }
            }
            CheckDefinition(refs.LightmapOverridePrimaryDefinition, refs.LightmapOverridePrimary, XAssetType.Image, "references.lightmapOverridePrimaryDefinition");
            CheckDefinition(refs.LightmapOverrideSecondaryDefinition, refs.LightmapOverrideSecondary, XAssetType.Image, "references.lightmapOverrideSecondaryDefinition");
            CheckDefinitions(refs.MaterialMemoryDefinitions, refs.MaterialMemory, XAssetType.Material, "references.materialMemoryDefinitions");
            CheckDefinition(refs.SunSpriteMaterialDefinition, refs.SunSpriteMaterial, XAssetType.Material, "references.sunSpriteMaterialDefinition");
            CheckDefinition(refs.SunFlareMaterialDefinition, refs.SunFlareMaterial, XAssetType.Material, "references.sunFlareMaterialDefinition");
            CheckDefinition(refs.OutdoorImageDefinition, refs.OutdoorImage, XAssetType.Image, "references.outdoorImageDefinition");
            CheckDefinitions(refs.SurfaceMaterialDefinitions, refs.SurfaceMaterials, XAssetType.Material, "references.surfaceMaterialDefinitions");
            CheckDefinitions(refs.StaticModelDrawInstDefinitions, refs.StaticModelDrawInsts, XAssetType.XModel, "references.staticModelDrawInstDefinitions");
            CheckLinks(refs.SkyImageLinks, refs.SkyImages, XAssetType.Image, "references.skyImageLinks");
            CheckLinks(refs.ReflectionProbeImageLinks, refs.ReflectionProbeImages, XAssetType.Image, "references.reflectionProbeImageLinks");
            if (refs.LightmapLinks.Count is not 0 &&
                refs.LightmapLinks.Count != refs.Lightmaps.Count)
            {
                diagnostics.Add(Error(
                    "references.lightmapLinks",
                    "Nested lightmap links must be absent for legacy input or parallel every lightmap reference row.",
                    rowIndex));
            }
            else
            {
                for (int index = 0; index < refs.LightmapLinks.Count; index++)
                {
                    CheckLink(refs.LightmapLinks[index].Primary, refs.Lightmaps[index].Primary, XAssetType.Image, $"references.lightmapLinks[{index}].primary");
                    CheckLink(refs.LightmapLinks[index].Secondary, refs.Lightmaps[index].Secondary, XAssetType.Image, $"references.lightmapLinks[{index}].secondary");
                }
            }
            CheckLink(refs.LightmapOverridePrimaryLink, refs.LightmapOverridePrimary, XAssetType.Image, "references.lightmapOverridePrimaryLink");
            CheckLink(refs.LightmapOverrideSecondaryLink, refs.LightmapOverrideSecondary, XAssetType.Image, "references.lightmapOverrideSecondaryLink");
            CheckLinks(refs.MaterialMemoryLinks, refs.MaterialMemory, XAssetType.Material, "references.materialMemoryLinks");
            CheckLink(refs.SunSpriteMaterialLink, refs.SunSpriteMaterial, XAssetType.Material, "references.sunSpriteMaterialLink");
            CheckLink(refs.SunFlareMaterialLink, refs.SunFlareMaterial, XAssetType.Material, "references.sunFlareMaterialLink");
            CheckLink(refs.OutdoorImageLink, refs.OutdoorImage, XAssetType.Image, "references.outdoorImageLink");
            CheckLinks(refs.SurfaceMaterialLinks, refs.SurfaceMaterials, XAssetType.Material, "references.surfaceMaterialLinks");
            CheckLinks(refs.StaticModelDrawInstLinks, refs.StaticModelDrawInsts, XAssetType.XModel, "references.staticModelDrawInstLinks");
            if (refs.AabbTreeSModelIndexPointers.Count is not 0 &&
                refs.AabbTreeSModelIndexPointers.Count !=
                    value.CellTrees.Count)
            {
                diagnostics.Add(Error(
                    "references.aabbTreeSModelIndexPointers",
                    "AABB index-pointer provenance must be absent for greenfield input or parallel every cell-tree row.",
                    rowIndex));
            }
            else
            {
                for (int cellIndex = 0;
                     cellIndex <
                         refs.AabbTreeSModelIndexPointers.Count;
                     cellIndex++)
                {
                    IReadOnlyList<GfxAabbTreeIndexPointerBuildData>
                        pointers =
                            refs.AabbTreeSModelIndexPointers[cellIndex];
                    if (pointers.Count !=
                        value.CellTrees[cellIndex].AabbTrees.Count)
                    {
                        diagnostics.Add(Error(
                            $"references.aabbTreeSModelIndexPointers[{cellIndex}]",
                            "AABB index-pointer provenance must parallel every AABB-tree row.",
                            rowIndex));
                        continue;
                    }
                    for (int treeIndex = 0;
                         treeIndex < pointers.Count;
                         treeIndex++)
                    {
                        GfxAabbTreeIndexPointerBuildData pointer =
                            pointers[treeIndex];
                        bool packed =
                            pointer.SourceForm ==
                            GfxDirectPointerSourceForm.PackedAlias;
                        if (packed != pointer.ImportedPackedRaw.HasValue ||
                            pointer.ImportedPackedRaw is { } raw &&
                            IW4.FastFiles.Pointers.XPointerCodec.GetType(
                                raw) !=
                            IW4.FastFiles.Pointers.PointerType.Offset)
                        {
                            diagnostics.Add(Error(
                                $"references.aabbTreeSModelIndexPointers[{cellIndex}][{treeIndex}]",
                                "Only packed AABB index pointers may retain a valid imported packed raw value.",
                                rowIndex));
                        }
                    }
                }
            }
            if (value.Skies.Any(item => item.SkyImage is not null) || value.WorldDraw.ReflectionProbeImages.Any(item => item is not null) || value.WorldDraw.Lightmaps.Any(item => item.Primary is not null || item.Secondary is not null) || value.WorldDraw.LightmapOverridePrimary is not null || value.WorldDraw.LightmapOverrideSecondary is not null || value.MaterialMemory.Any(item => item.Material is not null) || value.Sun.SpriteMaterial is not null || value.Sun.FlareMaterial is not null || value.OutdoorImage is not null || value.Dpvs.Surfaces.Any(item => item.Material is not null) || value.Dpvs.SModelDrawInsts.Any(item => item.Model is not null))
                diagnostics.Add(Error("references.loadedObjects", "Loaded Image, Material, and XModel objects are not build input; use detached symbolic references.", rowIndex));
        }

        void CheckLinks(
            IReadOnlyList<NestedXAssetBuildLink?> links,
            IReadOnlyList<SymbolicXAssetReference?> symbolic,
            XAssetType expected,
            string path)
        {
            if (links.Count is not 0 && links.Count != symbolic.Count)
            {
                diagnostics.Add(Error(
                    path,
                    "Nested links must be absent for legacy input or parallel every symbolic slot.",
                    rowIndex));
                return;
            }
            for (int index = 0; index < links.Count; index++)
                CheckLink(links[index], symbolic[index], expected, $"{path}[{index}]");
        }

        void CheckLink(
            NestedXAssetBuildLink? link,
            SymbolicXAssetReference? symbolic,
            XAssetType expected,
            string path)
        {
            if (link is null)
                return;
            if (symbolic is null || link.Reference != symbolic)
            {
                diagnostics.Add(Error(
                    path,
                    "Nested pointer provenance must retain the same symbolic identity as its slot.",
                    rowIndex));
            }
            diagnostics.AddRange(
                NestedXAssetEmission.Validate(
                    link,
                    expected,
                    path,
                    rowIndex,
                    XAssetType.GfxMap));
        }

        void CheckDefinitions(
            IReadOnlyList<IXAssetBuildData?> definitions,
            IReadOnlyList<SymbolicXAssetReference?> symbolic,
            XAssetType expected,
            string path)
        {
            if (definitions.Count is not 0 && definitions.Count != symbolic.Count)
            {
                diagnostics.Add(Error(
                    path,
                    "Nested definitions must be absent for legacy external-only input or parallel every symbolic slot.",
                    rowIndex));
                return;
            }
            for (int index = 0; index < definitions.Count; index++)
                CheckDefinition(definitions[index], symbolic[index], expected, $"{path}[{index}]");
        }

        void CheckDefinition(
            IXAssetBuildData? definition,
            SymbolicXAssetReference? symbolic,
            XAssetType expected,
            string path)
        {
            if (definition is null)
                return;
            if (symbolic is null)
            {
                diagnostics.Add(Error(path, "An inline nested definition requires a non-null symbolic identity.", rowIndex));
                return;
            }
            if (definition.AssetType != expected)
                diagnostics.Add(Error(path, $"Nested definition type '{definition.AssetType}' does not match '{expected}'.", rowIndex));
        }

        void CheckReferences(IReadOnlyList<SymbolicXAssetReference?> values, XAssetType expected, string path)
        {
            for (int index = 0; index < values.Count; index++) CheckReference(values[index], expected, $"{path}[{index}]");
        }

        void CheckReference(SymbolicXAssetReference? item, XAssetType expected, string path)
        {
            if (item is not null && (item.AssetType != expected || !item.IsExternalReference || !AssetBodyEmitterHelpers.IsLatin1CString(item.OriginalSerializedName))) diagnostics.Add(Error(path, $"Requires a comma-prefixed Latin-1 external {expected} identity.", rowIndex));
        }

        void ValidateShadowGeometry(IReadOnlyList<GfxShadowGeometry> values)
        {
            if (values.Count != value.PrimaryLightCount) diagnostics.Add(Error("shadowGeom", "Shadow-geometry rows must equal PrimaryLightCount.", rowIndex));
            for (int index = 0; index < values.Count; index++)
                if (values[index].SurfaceCount != values[index].SortedSurfIndex.Count || values[index].SModelCount != values[index].SModelIndex.Count)
                    diagnostics.Add(Error($"shadowGeom[{index}]", "Shadow geometry counts must equal their index arrays.", rowIndex));
        }

        void ValidateLightRegions(IReadOnlyList<GfxLightRegion> values)
        {
            if (values.Count != value.PrimaryLightCount) diagnostics.Add(Error("lightRegions", "Light-region rows must equal PrimaryLightCount.", rowIndex));
            for (int index = 0; index < values.Count; index++)
            {
                if (values[index].HullCount != values[index].Hulls.Count) diagnostics.Add(Error($"lightRegions[{index}].hulls", "HullCount must equal hull list length.", rowIndex));
                for (int hull = 0; hull < values[index].Hulls.Count; hull++)
                {
                    GfxLightRegionHull item = values[index].Hulls[hull]; CheckCount(item.KdopMidPoint, 9, $"lightRegions[{index}].hulls[{hull}].kdopMidPoint"); CheckCount(item.KdopHalfSize, 9, $"lightRegions[{index}].hulls[{hull}].kdopHalfSize");
                    if (item.AxisCount != item.Axes.Count) diagnostics.Add(Error($"lightRegions[{index}].hulls[{hull}].axes", "AxisCount must equal axis list length.", rowIndex));
                    for (int axis = 0; axis < item.Axes.Count; axis++) CheckCount(item.Axes[axis].Dir, 3, $"lightRegions[{index}].hulls[{hull}].axes[{axis}].dir");
                }
            }
        }

        void ValidateDpvsStatic(GfxWorldDpvsStatic dpvs)
        {
            CheckCount(dpvs.VisibilityCounts, 8, "dpvs.visibilityCounts");
            if (dpvs.SModelCount != dpvs.SModelInsts.Count || dpvs.SModelCount != dpvs.SModelDrawInsts.Count) diagnostics.Add(Error("dpvs.smodel", "SModelCount must equal both static-model arrays.", rowIndex));
            if (value.SurfaceCount != dpvs.Surfaces.Count || value.SurfaceCount != dpvs.SurfaceBounds.Count) diagnostics.Add(Error("dpvs.surfaces", "Root SurfaceCount must equal surfaces and bounds arrays.", rowIndex));
            if (dpvs.StaticSurfaceCount != dpvs.SortedSurfIndex.Count) diagnostics.Add(Error("dpvs.sortedSurfIndex", "StaticSurfaceCount must equal sorted-surface index count.", rowIndex));
            for (int index = 0; index < dpvs.SModelDrawInsts.Count; index++) { CheckCount(dpvs.SModelDrawInsts[index].Placement.Origin, 3, $"dpvs.smodelDrawInsts[{index}].placement.origin"); CheckCount(dpvs.SModelDrawInsts[index].Placement.PackedAxis, 3, $"dpvs.smodelDrawInsts[{index}].placement.packedAxis"); }
            for (int index = 0; index < dpvs.SurfaceBounds.Count; index++) if (dpvs.SurfaceBounds[index].Unknown18To1F.Count != 8) diagnostics.Add(Error($"dpvs.surfaceBounds[{index}].unknown18To1F", "Surface-bounds tail is exactly eight bytes.", rowIndex));
        }

        void ValidateDpvsDynamic(GfxWorldDpvsDynamic dpvs)
        {
            CheckCount(dpvs.DynEntClientWordCount, 2, "dpvsDyn.dynEntClientWordCount"); CheckCount(dpvs.DynEntClientCount, 2, "dpvsDyn.dynEntClientCount");
            if (dpvs.DynEntCellBits.Any(values => values.Any(item => item != 0)) || dpvs.DynEntVisData.Any(values => values.Any(item => item != 0))) diagnostics.Add(Error("dpvsDyn.runtime", "Dynamic DPVS arrays are RUNTIME zero allocations and cannot carry authored source values.", rowIndex));
        }
    }

    public AssetBodyEmission Plan(IXAssetBuildData buildData, EmissionPlan plan, int? rowIndex = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        AssetBodyEmitterHelpers.RequireNoDiagnostics(Validate(buildData, rowIndex));
        GfxWorldAsset value = ((IGfxWorldBuildData)buildData).Definition;
        GfxWorldReferenceBuildData references = ((IGfxWorldBuildData)buildData).References;
        var all = new List<EmissionBlockSegment>();
        plan.Push(XFileBlockType.TEMP);
        EmissionAddress root = plan.Allocate(GfxWorldAsset.SerializedSize, 4);
        plan.Push(XFileBlockType.LARGE);
        PlannedString? name = AssetBodyEmitterHelpers.PlanString(value.Name, plan, all, plan.StringAliases);
        PlannedString? baseName = AssetBodyEmitterHelpers.PlanString(value.BaseName, plan, all, plan.StringAliases);
        NestedPlan? skies = PlanSkies(
            value.Skies,
            references.SkyImages,
            references.SkyImageDefinitions,
            references.SkyImageLinks,
            plan,
            all);
        EmissionBlockSegment? planes = Array(value.DpvsPlanes.Planes, DpvsPlane.SerializedSize, 4, plan, all, WritePlane);
        EmissionBlockSegment? nodes = Array(value.DpvsPlanes.Nodes, sizeof(ushort), 4, plan, all, static (writer, item) => writer.WriteUInt16(item));
        plan.Push(XFileBlockType.RUNTIME);
        bool sceneEntCellBits = Runtime(value.DpvsPlanes.SceneEntCellBits.Count, sizeof(uint), 4, plan);
        plan.Pop(XFileBlockType.RUNTIME);
        EmissionBlockSegment? cellTreeCounts = Array(value.CellTreeCounts, GfxCellTreeCount.SerializedSize, 4, plan, all, static (writer, item) => writer.WriteUInt32(item.AabbTreeCount));
        NestedPlan? cellTrees = PlanCellTrees(
            value.CellTrees,
            references.AabbTreeSModelIndexPointers,
            plan,
            all);
        NestedPlan? cells = PlanCells(value.Cells, plan, all);
        WorldDrawPlan worldDraw = PlanWorldDraw(value.WorldDraw, references, root, plan, all);
        EmissionBlockSegment? lightGridRows = Array(value.LightGrid.RowDataStart, sizeof(ushort), sizeof(ushort), plan, all, static (writer, item) => writer.WriteUInt16(item));
        EmissionBlockSegment? lightGridRaw = Array(value.LightGrid.RawRowData, 1, 1, plan, all, static (writer, item) => writer.WriteByte(item));
        EmissionBlockSegment? lightGridEntries = Array(value.LightGrid.Entries, GfxLightGridEntry.SerializedSize, 4, plan, all, static (writer, item) => { writer.WriteUInt16(item.ColorsIndex); writer.WriteByte(item.PrimaryLightIndex); writer.WriteByte(item.NeedsTrace); });
        EmissionBlockSegment? lightGridColors = Array(value.LightGrid.Colors, GfxLightGridColors.SerializedSize, 4, plan, all, static (writer, item) => writer.WriteBytes(item.RgbBytes.ToArray()));
        EmissionBlockSegment? models = Array(value.Models, GfxBrushModel.SerializedSize, 4, plan, all, WriteBrushModel);
        NestedPlan? materialMemory = PlanMaterialMemory(
            value.MaterialMemory,
            references.MaterialMemory,
            references.MaterialMemoryDefinitions,
            references.MaterialMemoryLinks,
            plan,
            all);
        NestedPlan? sunSprite = NestedAsset(
            references.SunSpriteMaterial,
            references.SunSpriteMaterialDefinition,
            references.SunSpriteMaterialLink,
            XAssetType.Material,
            root,
            plan,
            all);
        NestedPlan? sunFlare = NestedAsset(
            references.SunFlareMaterial,
            references.SunFlareMaterialDefinition,
            references.SunFlareMaterialLink,
            XAssetType.Material,
            root,
            plan,
            all);
        NestedPlan? outdoorImage = NestedAsset(
            references.OutdoorImage,
            references.OutdoorImageDefinition,
            references.OutdoorImageLink,
            XAssetType.Image,
            root,
            plan,
            all);
        NestedPlan? shadowGeometry = PlanShadowGeometry(value.ShadowGeom, plan, all);
        NestedPlan? lightRegions = PlanLightRegions(value.LightRegions, plan, all);
        DpvsStaticPlan dpvs = PlanDpvsStatic(value.Dpvs, value.SurfaceCount, references, plan, all);
        EmissionBlockSegment? heroOnlyLights = Array(value.HeroOnlyLights, GfxHeroOnlyLight.SerializedSize, 4, plan, all, static (writer, item) => writer.WriteBytes(item.Bytes.ToArray()));
        plan.Pop(XFileBlockType.LARGE);
        int cellWordCount = WordCount(value.DpvsPlanes.CellCount);
        plan.Push(XFileBlockType.RUNTIME);
        bool cellCasterBits = Runtime(checked(value.DpvsPlanes.CellCount * cellWordCount), sizeof(uint), 4, plan);
        bool cellCasterBits2 = Runtime(cellWordCount, sizeof(uint), 4, plan);
        DpvsDynamicPlan dpvsDyn = PlanDpvsDynamic(
            value.DpvsDyn,
            value.DpvsPlanes.CellCount,
            value.SunPrimaryLightIndex,
            value.PrimaryLightCount,
            plan);
        dpvs = AllocateDpvsStaticRuntime(value.Dpvs, dpvs, plan);
        dpvsDyn = AllocateDpvsDynamicPayloads(value.DpvsDyn, value.DpvsPlanes.CellCount, dpvsDyn, plan);
        plan.Pop(XFileBlockType.RUNTIME);
        plan.Push(XFileBlockType.VIRTUAL);
        bool umbraGateData = Runtime(checked(value.UmbraGateCount + 0x1000), 1, 4096, plan);
        bool umbraGateData2 = Runtime(checked(value.UmbraGateCount + 0x1000), 1, 4096, plan);
        plan.Pop(XFileBlockType.VIRTUAL);
        plan.Pop(XFileBlockType.TEMP);

        var writer = new XSourceWriter();
        writer.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(name));
        writer.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(baseName));
        writer.WriteInt32(value.PlaneCount);
        writer.WriteInt32(value.NodeCount);
        writer.WriteInt32(value.SurfaceCount);
        writer.WriteUInt32(value.SkyCount);
        writer.WriteInt32(Pointer(skies));
        writer.WriteInt32(value.SunPrimaryLightIndex);
        writer.WriteInt32(value.PrimaryLightCount);
        writer.WriteInt32(value.SortKeyLitDecal);
        writer.WriteInt32(value.SortKeyEffectDecal);
        writer.WriteInt32(value.SortKeyEffectAuto);
        writer.WriteInt32(value.SortKeyDistortion);
        writer.WriteInt32(value.DpvsPlanes.CellCount);
        writer.WriteInt32(Pointer(planes));
        writer.WriteInt32(Pointer(nodes));
        writer.WriteInt32(Pointer(sceneEntCellBits));
        writer.WriteInt32(Pointer(cellTreeCounts));
        writer.WriteInt32(Pointer(cellTrees));
        writer.WriteInt32(Pointer(cells));
        WriteWorldDraw(writer, value.WorldDraw, worldDraw);
        WriteLightGrid(writer, value.LightGrid, lightGridRows, lightGridRaw, lightGridEntries, lightGridColors);
        writer.WriteInt32(value.ModelCount);
        writer.WriteInt32(Pointer(models));
        WriteFloatValues(writer, value.Mins, 3);
        WriteFloatValues(writer, value.Maxs, 3);
        writer.WriteUInt32(value.Checksum);
        writer.WriteInt32(value.MaterialMemoryCount);
        writer.WriteInt32(Pointer(materialMemory));
        WriteSunflare(writer, value.Sun, sunSprite, sunFlare);
        WriteFloatValues(writer, value.OutdoorLookupMatrix, 16);
        writer.WriteInt32(Pointer(outdoorImage));
        writer.WriteInt32(Pointer(cellCasterBits));
        writer.WriteInt32(Pointer(cellCasterBits2));
        writer.WriteInt32(Pointer(dpvsDyn.SceneDynModels));
        writer.WriteInt32(Pointer(dpvsDyn.SceneDynBrushes));
        writer.WriteInt32(Pointer(dpvsDyn.PrimaryLightEntityShadowVis));
        writer.WriteInt32(Pointer(dpvsDyn.PrimaryLightDynEntShadowVis0));
        writer.WriteInt32(Pointer(dpvsDyn.PrimaryLightDynEntShadowVis1));
        writer.WriteInt32(Pointer(dpvsDyn.PrimaryLightForModelDynEnt));
        writer.WriteInt32(Pointer(shadowGeometry));
        writer.WriteInt32(Pointer(lightRegions));
        WriteDpvsStatic(writer, value.Dpvs, dpvs);
        WriteDpvsDynamic(writer, value.DpvsDyn, dpvsDyn);
        writer.WriteUInt32(value.MapVertexChecksum);
        writer.WriteUInt32(value.HeroOnlyLightCount);
        writer.WriteInt32(Pointer(heroOnlyLights));
        writer.WriteByte(value.FogTypesAllowed);
        writer.WriteBytes(value.Pad279To27B.Count == 0 ? [0, 0, 0] : value.Pad279To27B.ToArray());
        writer.WriteInt32(value.UmbraGateCount);
        writer.WriteInt32(Pointer(umbraGateData));
        writer.WriteInt32(Pointer(umbraGateData2));
        if (writer.Position != GfxWorldAsset.SerializedSize)
            throw new InvalidDataException($"GfxWorld root emitted 0x{writer.Position:X} bytes instead of 0x{GfxWorldAsset.SerializedSize:X}.");
        EmissionBlockSegment rootSegment = new(root, writer.ToArray()); all.Add(rootSegment);
        var source = new List<EmissionBlockSegment> { rootSegment };
        Add(source, all, name); Add(source, all, baseName);
        Add(source, skies); Add(source, planes); Add(source, nodes); Add(source, cellTreeCounts); Add(source, cellTrees); Add(source, cells); Add(source, worldDraw.Source); Add(source, lightGridRows); Add(source, lightGridRaw); Add(source, lightGridEntries); Add(source, lightGridColors); Add(source, models); Add(source, materialMemory); Add(source, sunSprite); Add(source, sunFlare); Add(source, outdoorImage); Add(source, shadowGeometry); Add(source, lightRegions); Add(source, dpvs.Source); Add(source, heroOnlyLights);
        return new AssetBodyEmission(AssetType, root, all, source);
    }

    // Until every nested writer is present, do not maintain a hand-picked
    // allow-list of fields.  That is how newly decoded child state becomes a
    // silent all-zero output.  This structural guard rejects every non-default
    // reachable authored value while ignoring captured pointer/runtime metadata.
    private static bool HasPayload(GfxWorldAsset value) => ObjectHasPayload(value, root: true);

    private static bool ObjectHasPayload(object? value, bool root = false)
    {
        if (value is null) return false;
        Type type = value.GetType();
        if (type == typeof(string)) return true;
        if (type.IsEnum) return Convert.ToInt64(value) != 0;
        if (type.IsPrimitive || type == typeof(decimal)) return !IsZero(value);
        if (type.Namespace == "IW4.FastFiles.Pointers") return false;
        if (value is IEnumerable sequence)
        {
            foreach (object? item in sequence)
                if (ObjectHasPayload(item)) return true;
            return false;
        }

        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.CanRead || property.GetIndexParameters().Length != 0 || property.Name is "Offset" or "StagingAddress" or "RuntimeAddress" or "Type" ||
                (root && property.Name is "Name" or "BaseName" or "NamePointer" or "BaseNamePointer" or "Pad279To27B" or "PlaneCount" or "NodeCount" or "DpvsPlanes" or "CellTreeCounts" or "CellTrees" or "Cells" or "LightGrid"))
                continue;
            if (ObjectHasPayload(property.GetValue(value))) return true;
        }
        return false;
    }

    private static bool IsZero(object value) => value switch
    {
        float single => BitConverter.SingleToInt32Bits(single) == 0,
        double @double => BitConverter.DoubleToInt64Bits(@double) == 0,
        _ => Convert.ToDecimal(value) == decimal.Zero
    };

    private static void Add(List<EmissionBlockSegment> source, List<EmissionBlockSegment> all, PlannedString? value)
    {
        if (value is { } planned && !planned.IsExistingMaterialization)
            source.Add(all.Single(segment => segment.Address == planned.Address));
    }

    private static EmissionBlockSegment? Array<T>(IReadOnlyList<T> values, int stride, int alignment, EmissionPlan plan, List<EmissionBlockSegment> all, Action<XSourceWriter, T> write)
    {
        if (values.Count == 0)
            return null;

        EmissionAddress address = plan.Allocate(checked(values.Count * stride), alignment);
        var writer = new XSourceWriter();
        foreach (T value in values)
            write(writer, value);
        if (writer.Position != checked(values.Count * stride))
            throw new InvalidDataException($"GfxWorld array emitted 0x{writer.Position:X} bytes instead of 0x{values.Count * stride:X}.");
        var segment = new EmissionBlockSegment(address, writer.ToArray());
        all.Add(segment);
        return segment;
    }

    private static bool Runtime(int count, int stride, int alignment, EmissionPlan plan)
    {
        if (count == 0)
            return false;
        plan.Allocate(checked(count * stride), alignment);
        return true;
    }

    private static NestedPlan? PlanSkies(
        IReadOnlyList<GfxSky> values,
        IReadOnlyList<SymbolicXAssetReference?> references,
        IReadOnlyList<IXAssetBuildData?> definitions,
        IReadOnlyList<NestedXAssetBuildLink?> links,
        EmissionPlan plan,
        List<EmissionBlockSegment> all)
    {
        if (values.Count == 0) return null;
        EmissionAddress address = plan.Allocate(checked(values.Count * GfxSky.SerializedSize), 4);
        var starts = new EmissionBlockSegment?[values.Count];
        var images = new NestedPlan?[values.Count];
        for (int index = 0; index < values.Count; index++)
        {
            starts[index] = Array(
                values[index].SkyStartSurfs,
                sizeof(int),
                4,
                plan,
                all,
                static (writer, item) => writer.WriteInt32(item));
            images[index] = NestedAsset(
                index < references.Count ? references[index] : null,
                DefinitionAt(definitions, index),
                LinkAt(links, index),
                XAssetType.Image,
                Offset(
                    address,
                    checked(index * GfxSky.SerializedSize + 0x08)),
                plan,
                all);
        }
        var writer = new XSourceWriter();
        for (int index = 0; index < values.Count; index++)
        {
            GfxSky value = values[index];
            writer.WriteInt32(value.SkySurfCount);
            writer.WriteInt32(Pointer(starts[index]));
            writer.WriteInt32(Pointer(images[index]));
            writer.WriteInt32(value.SkySamplerState);
        }
        Exact(writer, checked(values.Count * GfxSky.SerializedSize), "GfxSky[]");
        var header = new EmissionBlockSegment(address, writer.ToArray()); all.Add(header);
        var source = new List<EmissionBlockSegment> { header };
        for (int index = 0; index < values.Count; index++) { Add(source, starts[index]); Add(source, images[index]); }
        return new NestedPlan(header, source);
    }

    private static WorldDrawPlan PlanWorldDraw(
        GfxWorldDraw value,
        GfxWorldReferenceBuildData references,
        EmissionAddress worldRoot,
        EmissionPlan plan,
        List<EmissionBlockSegment> all)
    {
        NestedPlan? reflectionImages = PlanExternalTable(
            references.ReflectionProbeImages,
            references.ReflectionProbeImageDefinitions,
            references.ReflectionProbeImageLinks,
            XAssetType.Image,
            plan,
            all);
        EmissionBlockSegment? reflectionOrigins = Array(value.ReflectionProbeOrigins, GfxReflectionProbe.SerializedSize, 4, plan, all, static (writer, item) => { writer.WriteSingle(item.OffsetX); writer.WriteSingle(item.OffsetY); writer.WriteSingle(item.OffsetZ); });
        plan.Push(XFileBlockType.RUNTIME);
        bool reflectionTextures = Runtime(checked((int)value.ReflectionProbeCount), GfxTexture.SerializedSize, 4, plan);
        plan.Pop(XFileBlockType.RUNTIME);
        NestedPlan? lightmaps = PlanLightmaps(
            value.Lightmaps,
            references.Lightmaps,
            references.LightmapDefinitions,
            references.LightmapLinks,
            plan,
            all);
        plan.Push(XFileBlockType.RUNTIME);
        bool lightmapPrimaryTextures = Runtime(value.LightmapCount, GfxTexture.SerializedSize, 4, plan);
        bool lightmapSecondaryTextures = Runtime(value.LightmapCount, GfxTexture.SerializedSize, 4, plan);
        plan.Pop(XFileBlockType.RUNTIME);
        NestedPlan? lightmapOverridePrimary = NestedAsset(
            references.LightmapOverridePrimary,
            references.LightmapOverridePrimaryDefinition,
            references.LightmapOverridePrimaryLink,
            XAssetType.Image,
            Offset(worldRoot, 0x70),
            plan,
            all);
        NestedPlan? lightmapOverrideSecondary = NestedAsset(
            references.LightmapOverrideSecondary,
            references.LightmapOverrideSecondaryDefinition,
            references.LightmapOverrideSecondaryLink,
            XAssetType.Image,
            Offset(worldRoot, 0x74),
            plan,
            all);
        EmissionBlockSegment? vertices = Array(value.VertexData.PackedVertices, 1, 16, plan, all, static (writer, item) => writer.WriteByte(item));
        plan.Push(XFileBlockType.PHYSICAL);
        EmissionBlockSegment? vertexLayerData = Array(value.VertexLayerData.PackedLayerData, 1, 1, plan, all, static (writer, item) => writer.WriteByte(item));
        plan.Pop(XFileBlockType.PHYSICAL);
        EmissionBlockSegment? indices = Array(value.Indices, sizeof(ushort), 2, plan, all, static (writer, item) => writer.WriteUInt16(item));
        var source = new List<EmissionBlockSegment>();
        Add(source, reflectionImages); Add(source, reflectionOrigins); Add(source, lightmaps); Add(source, lightmapOverridePrimary); Add(source, lightmapOverrideSecondary); Add(source, vertices); Add(source, vertexLayerData); Add(source, indices);
        return new WorldDrawPlan(reflectionImages, reflectionOrigins, reflectionTextures, lightmaps, lightmapPrimaryTextures, lightmapSecondaryTextures, lightmapOverridePrimary, lightmapOverrideSecondary, vertices, vertexLayerData, indices, source);
    }

    private static NestedPlan? PlanLightmaps(
        IReadOnlyList<GfxLightmapArray> values,
        IReadOnlyList<GfxLightmapReferenceBuildData> references,
        IReadOnlyList<GfxLightmapDefinitionBuildData> definitions,
        IReadOnlyList<GfxLightmapLinkBuildData> links,
        EmissionPlan plan,
        List<EmissionBlockSegment> all)
    {
        if (values.Count == 0) return null;
        EmissionAddress address = plan.Allocate(checked(values.Count * GfxLightmapArray.SerializedSize), 4);
        var primary = new NestedPlan?[values.Count];
        var secondary = new NestedPlan?[values.Count];
        for (int index = 0; index < values.Count; index++)
        {
            primary[index] = NestedAsset(
                index < references.Count ? references[index].Primary : null,
                index < definitions.Count ? definitions[index].Primary : null,
                index < links.Count ? links[index].Primary : null,
                XAssetType.Image,
                Offset(
                    address,
                    checked(index * GfxLightmapArray.SerializedSize)),
                plan,
                all);
            secondary[index] = NestedAsset(
                index < references.Count ? references[index].Secondary : null,
                index < definitions.Count ? definitions[index].Secondary : null,
                index < links.Count ? links[index].Secondary : null,
                XAssetType.Image,
                Offset(
                    address,
                    checked(
                        index * GfxLightmapArray.SerializedSize +
                        sizeof(int))),
                plan,
                all);
        }
        var writer = new XSourceWriter();
        for (int index = 0; index < values.Count; index++) { writer.WriteInt32(Pointer(primary[index])); writer.WriteInt32(Pointer(secondary[index])); }
        var header = new EmissionBlockSegment(address, writer.ToArray()); all.Add(header);
        var source = new List<EmissionBlockSegment> { header };
        for (int index = 0; index < values.Count; index++) { Add(source, primary[index]); Add(source, secondary[index]); }
        return new NestedPlan(header, source);
    }

    private static NestedPlan? PlanMaterialMemory(
        IReadOnlyList<MaterialMemory> values,
        IReadOnlyList<SymbolicXAssetReference?> references,
        IReadOnlyList<IXAssetBuildData?> definitions,
        IReadOnlyList<NestedXAssetBuildLink?> links,
        EmissionPlan plan,
        List<EmissionBlockSegment> all)
    {
        if (values.Count == 0) return null;
        EmissionAddress address = plan.Allocate(checked(values.Count * MaterialMemory.SerializedSize), 4);
        NestedPlan?[] materials = values.Select((_, index) => NestedAsset(
            index < references.Count ? references[index] : null,
            DefinitionAt(definitions, index),
            LinkAt(links, index),
            XAssetType.Material,
            Offset(address, checked(index * MaterialMemory.SerializedSize)),
            plan,
            all)).ToArray();
        var writer = new XSourceWriter();
        for (int index = 0; index < values.Count; index++) { writer.WriteInt32(Pointer(materials[index])); writer.WriteInt32(values[index].Memory); }
        var header = new EmissionBlockSegment(address, writer.ToArray()); all.Add(header);
        var source = new List<EmissionBlockSegment> { header };
        foreach (NestedPlan? material in materials) Add(source, material);
        return new NestedPlan(header, source);
    }

    private static NestedPlan? PlanShadowGeometry(IReadOnlyList<GfxShadowGeometry> values, EmissionPlan plan, List<EmissionBlockSegment> all)
    {
        if (values.Count == 0) return null;
        EmissionAddress address = plan.Allocate(checked(values.Count * GfxShadowGeometry.SerializedSize), 4);
        var children = values.Select(value => new ShadowGeometryChildPlan(
            Array(
                value.SortedSurfIndex,
                sizeof(ushort),
                2,
                plan,
                all,
                static (writer, item) => writer.WriteUInt16(item)),
            Array(
                value.SModelIndex,
                sizeof(ushort),
                2,
                plan,
                all,
                static (writer, item) => writer.WriteUInt16(item)))).ToArray();
        var writer = new XSourceWriter();
        for (int index = 0; index < values.Count; index++)
        {
            writer.WriteUInt16(values[index].SurfaceCount); writer.WriteUInt16(values[index].SModelCount);
            writer.WriteInt32(Pointer(children[index].SortedSurfIndex));
            writer.WriteInt32(Pointer(children[index].SModelIndex));
        }
        var header = new EmissionBlockSegment(address, writer.ToArray()); all.Add(header);
        var source = new List<EmissionBlockSegment> { header };
        foreach (ShadowGeometryChildPlan child in children)
        {
            Add(source, child.SortedSurfIndex);
            Add(source, child.SModelIndex);
        }
        return new NestedPlan(header, source);
    }

    private static NestedPlan? PlanLightRegions(IReadOnlyList<GfxLightRegion> values, EmissionPlan plan, List<EmissionBlockSegment> all)
    {
        if (values.Count == 0) return null;
        EmissionAddress address = plan.Allocate(checked(values.Count * GfxLightRegion.SerializedSize), 4);
        NestedPlan?[] hulls = values.Select(value => PlanLightRegionHulls(value.Hulls, plan, all)).ToArray();
        var writer = new XSourceWriter();
        for (int index = 0; index < values.Count; index++) { writer.WriteInt32(values[index].HullCount); writer.WriteInt32(Pointer(hulls[index])); }
        var header = new EmissionBlockSegment(address, writer.ToArray()); all.Add(header);
        var source = new List<EmissionBlockSegment> { header };
        foreach (NestedPlan? item in hulls) Add(source, item);
        return new NestedPlan(header, source);
    }

    private static NestedPlan? PlanLightRegionHulls(IReadOnlyList<GfxLightRegionHull> values, EmissionPlan plan, List<EmissionBlockSegment> all)
    {
        if (values.Count == 0) return null;
        EmissionAddress address = plan.Allocate(checked(values.Count * GfxLightRegionHull.SerializedSize), 4);
        EmissionBlockSegment?[] axes = values.Select(value => Array(value.Axes, GfxLightRegionAxis.SerializedSize, 4, plan, all, WriteLightRegionAxis)).ToArray();
        var writer = new XSourceWriter();
        for (int index = 0; index < values.Count; index++)
        {
            WriteFloatValues(writer, values[index].KdopMidPoint, 9);
            WriteFloatValues(writer, values[index].KdopHalfSize, 9);
            writer.WriteUInt32(values[index].AxisCount); writer.WriteInt32(Pointer(axes[index]));
        }
        var header = new EmissionBlockSegment(address, writer.ToArray()); all.Add(header);
        var source = new List<EmissionBlockSegment> { header };
        foreach (EmissionBlockSegment? item in axes) Add(source, item);
        return new NestedPlan(header, source);
    }

    private static DpvsStaticPlan PlanDpvsStatic(GfxWorldDpvsStatic value, int surfaceCount, GfxWorldReferenceBuildData references, EmissionPlan plan, List<EmissionBlockSegment> all)
    {
        EmissionBlockSegment? sortedSurfIndex = Array(value.SortedSurfIndex, sizeof(ushort), 2, plan, all, static (writer, item) => writer.WriteUInt16(item));
        EmissionBlockSegment? smodelInsts = Array(value.SModelInsts, GfxStaticModelInst.SerializedSize, 4, plan, all, WriteStaticModelInst);
        NestedPlan? surfaces = PlanSurfaces(
            value.Surfaces,
            references.SurfaceMaterials,
            references.SurfaceMaterialDefinitions,
            references.SurfaceMaterialLinks,
            plan,
            all);
        EmissionBlockSegment? surfaceBounds = Array(value.SurfaceBounds, GfxSurfaceBounds.SerializedSize, 4, plan, all, WriteSurfaceBounds);
        NestedPlan? smodelDrawInsts = PlanStaticModelDrawInsts(
            value.SModelDrawInsts,
            references.StaticModelDrawInsts,
            references.StaticModelDrawInstDefinitions,
            references.StaticModelDrawInstLinks,
            plan,
            all);
        var source = new List<EmissionBlockSegment>();
        Add(source, sortedSurfIndex); Add(source, smodelInsts); Add(source, surfaces); Add(source, surfaceBounds); Add(source, smodelDrawInsts);
        return new DpvsStaticPlan(sortedSurfIndex, smodelInsts, surfaces, surfaceBounds, smodelDrawInsts, [false, false, false], [false, false, false], false, false, source);
    }

    private static DpvsStaticPlan AllocateDpvsStaticRuntime(GfxWorldDpvsStatic value, DpvsStaticPlan plan, EmissionPlan emissionPlan)
    {
        bool[] smodelVis = new bool[3];
        bool[] surfaceVis = new bool[3];
        int smodelCount = CheckedCount(value.VisibilityCounts, 6);
        int surfaceCount = CheckedCount(value.VisibilityCounts, 7);
        for (int index = 0; index < 3; index++) smodelVis[index] = Runtime(smodelCount, sizeof(uint), 4, emissionPlan);
        for (int index = 0; index < 3; index++) surfaceVis[index] = Runtime(surfaceCount, sizeof(uint), 4, emissionPlan);
        bool surfaceMaterials = Runtime(value.Surfaces.Count, GfxMapDrawSurf.SerializedSize, 4, emissionPlan);
        bool surfaceCastsSunShadow = Runtime(surfaceCount, sizeof(uint), 4, emissionPlan);
        return plan with { SModelVisData = smodelVis, SurfaceVisData = surfaceVis, SurfaceMaterials = surfaceMaterials, SurfaceCastsSunShadow = surfaceCastsSunShadow };
    }

    private static DpvsDynamicPlan PlanDpvsDynamic(
        GfxWorldDpvsDynamic value,
        int cellCount,
        int sunPrimaryLightIndex,
        int primaryLightCount,
        EmissionPlan plan)
    {
        int dynModelCount = CheckedCount(value.DynEntClientCount, 0);
        int dynBrushCount = CheckedCount(value.DynEntClientCount, 1);
        int nonSunPrimaryLightCount = Math.Max(
            0,
            checked(primaryLightCount - sunPrimaryLightIndex) - 1);
        bool sceneDynModels = Runtime(dynModelCount, GfxSceneDynModel.SerializedSize, 4, plan);
        bool sceneDynBrushes = Runtime(dynBrushCount, GfxSceneDynBrush.SerializedSize, 4, plan);
        bool primaryLightEntityShadowVis = Runtime(checked(nonSunPrimaryLightCount * 0x2000), sizeof(uint), 4, plan);
        bool primaryLightDynEntShadowVis0 = Runtime(checked(nonSunPrimaryLightCount * dynModelCount), sizeof(uint), 4, plan);
        bool primaryLightDynEntShadowVis1 = Runtime(checked(nonSunPrimaryLightCount * dynBrushCount), sizeof(uint), 4, plan);
        bool primaryLightForModelDynEnt = Runtime(dynModelCount, 1, 1, plan);
        return new DpvsDynamicPlan(sceneDynModels, sceneDynBrushes, primaryLightEntityShadowVis, primaryLightDynEntShadowVis0, primaryLightDynEntShadowVis1, primaryLightForModelDynEnt, [false, false], [false, false, false, false, false, false]);
    }

    private static DpvsDynamicPlan AllocateDpvsDynamicPayloads(GfxWorldDpvsDynamic value, int cellCount, DpvsDynamicPlan plan, EmissionPlan emissionPlan)
    {
        bool[] cellBits = new bool[2];
        for (int index = 0; index < cellBits.Length; index++) cellBits[index] = Runtime(checked(CheckedCount(value.DynEntClientWordCount, index) * cellCount), sizeof(uint), 4, emissionPlan);
        bool[] visData = new bool[6];
        foreach (int index in new[] { 0, 3, 1, 4, 2, 5 })
        {
            int wordCount = CheckedCount(value.DynEntClientWordCount, index >= 3 ? 1 : 0);
            visData[index] = Runtime(checked(wordCount << 5), 1, 16, emissionPlan);
        }
        return plan with { CellBits = cellBits, VisData = visData };
    }

    private static int CheckedCount(IReadOnlyList<uint> values, int index) => index < values.Count ? checked((int)values[index]) : 0;

    private static IXAssetBuildData? DefinitionAt(
        IReadOnlyList<IXAssetBuildData?> definitions,
        int index) =>
        index < definitions.Count ? definitions[index] : null;

    private static NestedXAssetBuildLink? LinkAt(
        IReadOnlyList<NestedXAssetBuildLink?> links,
        int index) =>
        index < links.Count ? links[index] : null;

    private static EmissionAddress Offset(EmissionAddress address, int byteOffset) =>
        new(address.Block, checked(address.Offset + byteOffset));

    private static NestedPlan? PlanSurfaces(
        IReadOnlyList<GfxSurface> values,
        IReadOnlyList<SymbolicXAssetReference?> references,
        IReadOnlyList<IXAssetBuildData?> definitions,
        IReadOnlyList<NestedXAssetBuildLink?> links,
        EmissionPlan plan,
        List<EmissionBlockSegment> all)
    {
        if (values.Count == 0) return null;
        EmissionAddress address = plan.Allocate(checked(values.Count * GfxSurface.SerializedSize), 4);
        NestedPlan?[] materials = values.Select((_, index) => NestedAsset(
            index < references.Count ? references[index] : null,
            DefinitionAt(definitions, index),
            LinkAt(links, index),
            XAssetType.Material,
            Offset(address, checked(index * GfxSurface.SerializedSize + 0x14)),
            plan,
            all)).ToArray();
        var writer = new XSourceWriter();
        for (int index = 0; index < values.Count; index++)
        {
            WriteTriangles(writer, values[index].Triangles); writer.WriteInt32(Pointer(materials[index]));
            writer.WriteByte(values[index].LightmapIndex); writer.WriteByte(values[index].ReflectionProbeIndex); writer.WriteByte(values[index].PrimaryLightIndex); writer.WriteByte(values[index].CastsSunShadow);
        }
        var header = new EmissionBlockSegment(address, writer.ToArray()); all.Add(header);
        var source = new List<EmissionBlockSegment> { header };
        foreach (NestedPlan? item in materials) Add(source, item);
        return new NestedPlan(header, source);
    }

    private static NestedPlan? PlanStaticModelDrawInsts(
        IReadOnlyList<GfxStaticModelDrawInst> values,
        IReadOnlyList<SymbolicXAssetReference?> references,
        IReadOnlyList<IXAssetBuildData?> definitions,
        IReadOnlyList<NestedXAssetBuildLink?> links,
        EmissionPlan plan,
        List<EmissionBlockSegment> all)
    {
        if (values.Count == 0) return null;
        EmissionAddress address = plan.Allocate(checked(values.Count * GfxStaticModelDrawInst.SerializedSize), 4);
        NestedPlan?[] models = values.Select((_, index) => NestedAsset(
            index < references.Count ? references[index] : null,
            DefinitionAt(definitions, index),
            LinkAt(links, index),
            XAssetType.XModel,
            Offset(address, checked(index * GfxStaticModelDrawInst.SerializedSize + 0x1C)),
            plan,
            all)).ToArray();
        var writer = new XSourceWriter();
        for (int index = 0; index < values.Count; index++)
        {
            GfxStaticModelDrawInst value = values[index];
            WritePlacement(writer, value.Placement); writer.WriteInt32(Pointer(models[index]));
            writer.WriteUInt16(value.CullDist); writer.WriteUInt16(value.LightingHandle); writer.WriteByte(value.ReflectionProbeIndex); writer.WriteByte(value.PrimaryLightIndex); writer.WriteByte(value.Flags); writer.WriteByte(value.FirstMaterialSkinIndex); writer.WriteUInt32(value.GroundLighting.Packed);
        }
        var header = new EmissionBlockSegment(address, writer.ToArray()); all.Add(header);
        var source = new List<EmissionBlockSegment> { header };
        foreach (NestedPlan? item in models) Add(source, item);
        return new NestedPlan(header, source);
    }

    private static NestedPlan? PlanExternalTable(
        IReadOnlyList<SymbolicXAssetReference?> values,
        IReadOnlyList<IXAssetBuildData?> definitions,
        IReadOnlyList<NestedXAssetBuildLink?> links,
        XAssetType type,
        EmissionPlan plan,
        List<EmissionBlockSegment> all)
    {
        if (values.Count == 0) return null;
        EmissionAddress address = plan.Allocate(checked(values.Count * sizeof(int)), 4);
        NestedPlan?[] children = values.Select((value, index) => NestedAsset(
            value,
            DefinitionAt(definitions, index),
            LinkAt(links, index),
            type,
            Offset(address, checked(index * sizeof(int))),
            plan,
            all)).ToArray();
        var writer = new XSourceWriter();
        foreach (NestedPlan? child in children) writer.WriteInt32(Pointer(child));
        var header = new EmissionBlockSegment(address, writer.ToArray()); all.Add(header);
        var source = new List<EmissionBlockSegment> { header };
        foreach (NestedPlan? child in children) Add(source, child);
        return new NestedPlan(header, source);
    }

    private static NestedPlan? NestedAsset(
        SymbolicXAssetReference? reference,
        IXAssetBuildData? definition,
        NestedXAssetBuildLink? link,
        XAssetType type,
        EmissionAddress ownerCell,
        EmissionPlan plan,
        List<EmissionBlockSegment> all)
    {
        if (link is not null)
        {
            NestedXAssetPlan nested = NestedXAssetEmission.Plan(
                link,
                plan,
                all,
                ownerCell,
                owner: "GfxWorld");
            return new NestedPlan(
                null,
                nested.Source,
                nested.PointerRaw);
        }
        if (reference is null)
            return null;
        string aliasKey = AssetBodyEmitterHelpers.XAssetAliasKey(
            type,
            reference.OriginalSerializedName);
        if (plan.PersistentXAssetAliasCells.TryGetValue(aliasKey, out EmissionAddress existingCell))
            return new NestedPlan(null, [], existingCell.ToPackedPointer());
        if (definition is not null)
        {
            IXAssetBodyEmitter emitter = type switch
            {
                XAssetType.Image => new GfxImageBodyEmitter(),
                XAssetType.Material => new MaterialBodyEmitter(),
                XAssetType.XModel => new XModelBodyEmitter(),
                _ => throw new ArgumentOutOfRangeException(nameof(type))
            };
            AssetBodyEmission body = emitter.Plan(definition, plan);
            all.AddRange(body.Segments);
            if (ownerCell.Block != XFileBlockType.TEMP)
                plan.PersistentXAssetAliasCells.TryAdd(aliasKey, ownerCell);
            EmissionBlockSegment rootSegment = body.Segments.Single(segment =>
                segment.Address == body.RootAddress);
            return new NestedPlan(rootSegment, body.SourceSegments, -1);
        }

        int rootSize = type switch
        {
            XAssetType.Image => 0x50,
            XAssetType.Material => 0xA8,
            XAssetType.XModel => 0x120,
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
        plan.Push(XFileBlockType.TEMP);
        EmissionAddress root = plan.Allocate(rootSize, 4);
        plan.Push(XFileBlockType.LARGE);
        int before = all.Count;
        PlannedString? name = AssetBodyEmitterHelpers.PlanString(reference.OriginalSerializedName, plan, all, plan.StringAliases);
        EmissionBlockSegment[] names = all.Skip(before).ToArray();
        plan.Pop(XFileBlockType.LARGE);
        plan.Pop(XFileBlockType.TEMP);
        var writer = new XSourceWriter();
        if (type == XAssetType.Image) { writer.Reserve(0x4C); writer.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(name)); }
        else { writer.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(name)); writer.Reserve(rootSize - sizeof(int)); }
        Exact(writer, rootSize, $"external {type}");
        var header = new EmissionBlockSegment(root, writer.ToArray()); all.Add(header);
        if (ownerCell.Block != XFileBlockType.TEMP)
            plan.PersistentXAssetAliasCells.TryAdd(aliasKey, ownerCell);
        return new NestedPlan(header, [header, .. names], -1);
    }

    private static NestedPlan? PlanCellTrees(
        IReadOnlyList<GfxCellTree> values,
        IReadOnlyList<IReadOnlyList<GfxAabbTreeIndexPointerBuildData>>
            indexPointers,
        EmissionPlan plan,
        List<EmissionBlockSegment> all)
    {
        if (values.Count == 0) return null;
        EmissionAddress address = plan.Allocate(checked(values.Count * GfxCellTree.SerializedSize), 128);
        NestedPlan?[] children = values
            .Select((value, index) => PlanAabbTrees(
                value.AabbTrees,
                index < indexPointers.Count
                    ? indexPointers[index]
                    : [],
                plan,
                all))
            .ToArray();
        var writer = new XSourceWriter();
        foreach (NestedPlan? child in children) writer.WriteInt32(Pointer(child));
        Exact(writer, checked(values.Count * GfxCellTree.SerializedSize), "GfxCellTree[]");
        var header = new EmissionBlockSegment(address, writer.ToArray()); all.Add(header);
        var source = new List<EmissionBlockSegment> { header };
        foreach (NestedPlan? child in children) Add(source, child);
        return new NestedPlan(header, source);
    }

    private static NestedPlan? PlanAabbTrees(
        IReadOnlyList<GfxAabbTree> values,
        IReadOnlyList<GfxAabbTreeIndexPointerBuildData> indexPointers,
        EmissionPlan plan,
        List<EmissionBlockSegment> all)
    {
        if (values.Count == 0) return null;
        EmissionAddress address = plan.Allocate(checked(values.Count * GfxAabbTree.SerializedSize), 4);
        AabbIndexesPlan[] indexes = values
            .Select((value, index) => PlanAabbIndexes(
                value,
                index < indexPointers.Count
                    ? indexPointers[index]
                    : null,
                plan,
                all))
            .ToArray();
        var writer = new XSourceWriter();
        for (int index = 0; index < values.Count; index++)
            WriteAabbTree(writer, values[index], indexes[index].PointerRaw);
        Exact(writer, checked(values.Count * GfxAabbTree.SerializedSize), "GfxAabbTree[]");
        var header = new EmissionBlockSegment(address, writer.ToArray()); all.Add(header);
        var source = new List<EmissionBlockSegment> { header };
        foreach (AabbIndexesPlan item in indexes)
            Add(source, item.Source);
        return new NestedPlan(header, source);
    }

    private static AabbIndexesPlan PlanAabbIndexes(
        GfxAabbTree value,
        GfxAabbTreeIndexPointerBuildData? pointer,
        EmissionPlan plan,
        List<EmissionBlockSegment> all)
    {
        if (pointer is
                {
                    SourceForm:
                        GfxDirectPointerSourceForm.PackedAlias,
                    ImportedPackedRaw: { } importedRaw
                } &&
            plan.PreserveImportedXAssetPointerValues)
        {
            return new AabbIndexesPlan(importedRaw, null);
        }

        if (value.SModelIndexes.Count == 0)
        {
            int emptyPointer = pointer?.SourceForm switch
            {
                GfxDirectPointerSourceForm.Inline => -1,
                GfxDirectPointerSourceForm.Insert => -2,
                _ => 0
            };
            return new AabbIndexesPlan(emptyPointer, null);
        }

        EmissionBlockSegment source = Array(
            value.SModelIndexes,
            sizeof(ushort),
            2,
            plan,
            all,
            static (writer, item) => writer.WriteUInt16(item))
            ?? throw new InvalidDataException(
                "A non-empty GfxAabbTree static-model index list produced no source segment.");
        int pointerRaw =
            pointer?.SourceForm ==
                GfxDirectPointerSourceForm.Insert
                ? -2
                : -1;
        return new AabbIndexesPlan(pointerRaw, source);
    }

    private static NestedPlan? PlanCells(IReadOnlyList<GfxCell> values, EmissionPlan plan, List<EmissionBlockSegment> all)
    {
        if (values.Count == 0) return null;
        EmissionAddress address = plan.Allocate(checked(values.Count * GfxCell.SerializedSize), 4);
        var children = values.Select(value => new CellChildPlan(
            PlanPortals(value.Portals, plan, all),
            Array(
                value.ReflectionProbes,
                1,
                1,
                plan,
                all,
                static (writer, item) => writer.WriteByte(item)))).ToArray();
        var writer = new XSourceWriter();
        for (int index = 0; index < values.Count; index++)
            WriteCell(writer, values[index], children[index].Portals, children[index].ReflectionProbes);
        Exact(writer, checked(values.Count * GfxCell.SerializedSize), "GfxCell[]");
        var header = new EmissionBlockSegment(address, writer.ToArray()); all.Add(header);
        var source = new List<EmissionBlockSegment> { header };
        foreach (CellChildPlan child in children)
        {
            Add(source, child.Portals);
            Add(source, child.ReflectionProbes);
        }
        return new NestedPlan(header, source);
    }

    private static NestedPlan? PlanPortals(IReadOnlyList<GfxPortal> values, EmissionPlan plan, List<EmissionBlockSegment> all)
    {
        if (values.Count == 0) return null;
        EmissionAddress address = plan.Allocate(checked(values.Count * GfxPortal.SerializedSize), 4);
        EmissionBlockSegment?[] vertices = values.Select(value => Array(value.Vertices, GfxPortalVertex.SerializedSize, 4, plan, all, static (writer, item) => { writer.WriteSingle(item.X); writer.WriteSingle(item.Y); writer.WriteSingle(item.Z); })).ToArray();
        var writer = new XSourceWriter();
        for (int index = 0; index < values.Count; index++) WritePortal(writer, values[index], vertices[index]);
        Exact(writer, checked(values.Count * GfxPortal.SerializedSize), "GfxPortal[]");
        var header = new EmissionBlockSegment(address, writer.ToArray()); all.Add(header);
        var source = new List<EmissionBlockSegment> { header };
        foreach (EmissionBlockSegment? item in vertices) Add(source, item);
        return new NestedPlan(header, source);
    }

    private static void WritePlane(XSourceWriter writer, DpvsPlane value)
    {
        writer.WriteSingle(value.NormalX);
        writer.WriteSingle(value.NormalY);
        writer.WriteSingle(value.NormalZ);
        writer.WriteSingle(value.Distance);
        writer.WriteByte(value.Type);
        writer.WriteByte(value.SignBits);
        writer.WriteUInt16(value.Pad12);
    }

    private static void WriteAabbTree(
        XSourceWriter writer,
        GfxAabbTree value,
        int indexesPointerRaw)
    {
        WriteBounds(writer, value.Bounds); writer.WriteUInt16(value.ChildCount); writer.WriteUInt16(value.SurfaceCount); writer.WriteUInt16(value.StartSurfIndex); writer.WriteUInt16(value.SModelIndexCount); writer.WriteInt32(indexesPointerRaw); writer.WriteInt32(value.ChildrenOffset);
    }

    private static void WriteCell(XSourceWriter writer, GfxCell value, NestedPlan? portals, EmissionBlockSegment? probes)
    {
        WriteBounds(writer, value.Bounds); writer.WriteInt32(value.PortalCount); writer.WriteInt32(Pointer(portals)); writer.WriteByte(value.ReflectionProbeCount); writer.WriteBytes(value.Pad21.ToArray()); writer.WriteInt32(Pointer(probes));
    }

    private static void WritePortal(XSourceWriter writer, GfxPortal value, EmissionBlockSegment? vertices)
    {
        writer.WriteByte(value.IsQueued ? (byte)1 : (byte)0); writer.WriteByte(value.IsAncestor ? (byte)1 : (byte)0); writer.WriteByte(value.RecursionDepth); writer.WriteByte(value.HullPointCount); writer.WriteInt32(value.HullPointsRuntimePointer); writer.WriteInt32(value.QueuedParentRuntimePointer); writer.WriteSingle(value.Plane.NormalX); writer.WriteSingle(value.Plane.NormalY); writer.WriteSingle(value.Plane.NormalZ); writer.WriteSingle(value.Plane.Distance); writer.WriteInt32(Pointer(vertices)); writer.WriteUInt16(value.CellIndex); writer.WriteByte(value.VertexCount); writer.WriteByte(value.Pad23); foreach (float axis in value.HullAxis) writer.WriteSingle(axis);
    }

    private static void WriteLightGrid(XSourceWriter writer, GfxLightGrid value, EmissionBlockSegment? rowDataStart, EmissionBlockSegment? rawRowData, EmissionBlockSegment? entries, EmissionBlockSegment? colors)
    {
        writer.WriteUInt32(value.HasLightRegions);
        writer.WriteUInt32(value.SunPrimaryLightIndex);
        foreach (ushort item in value.Mins) writer.WriteUInt16(item);
        foreach (ushort item in value.Maxs) writer.WriteUInt16(item);
        writer.WriteUInt32(value.RowAxis);
        writer.WriteUInt32(value.ColAxis);
        writer.WriteInt32(Pointer(rowDataStart));
        writer.WriteUInt32(value.RawRowDataSize);
        writer.WriteInt32(Pointer(rawRowData));
        writer.WriteUInt32(value.EntryCount);
        writer.WriteInt32(Pointer(entries));
        writer.WriteUInt32(value.ColorCount);
        writer.WriteInt32(Pointer(colors));
        Exact(writer, 0xDC, "GfxLightGrid root header");
    }

    private static void WriteWorldDraw(XSourceWriter writer, GfxWorldDraw value, WorldDrawPlan plan)
    {
        writer.WriteUInt32(value.ReflectionProbeCount);
        writer.WriteInt32(Pointer(plan.ReflectionImages));
        writer.WriteInt32(Pointer(plan.ReflectionOrigins));
        writer.WriteInt32(Pointer(plan.ReflectionTextures));
        writer.WriteInt32(value.LightmapCount);
        writer.WriteInt32(Pointer(plan.Lightmaps));
        writer.WriteInt32(Pointer(plan.LightmapPrimaryTextures));
        writer.WriteInt32(Pointer(plan.LightmapSecondaryTextures));
        writer.WriteInt32(Pointer(plan.LightmapOverridePrimary));
        writer.WriteInt32(Pointer(plan.LightmapOverrideSecondary));
        writer.WriteUInt32(value.VertexCount);
        writer.WriteInt32(Pointer(plan.Vertices));
        writer.WriteInt32(value.VertexData.WorldVbHandle);
        writer.WriteInt32(value.VertexData.WorldVbOffset);
        writer.WriteUInt32(value.VertexLayerDataSize);
        writer.WriteInt32(Pointer(plan.VertexLayerData));
        writer.WriteInt32(value.VertexLayerData.LayerVbHandle);
        writer.WriteInt32(value.VertexLayerData.LayerVbOffset);
        writer.WriteInt32(value.IndexCount);
        writer.WriteInt32(Pointer(plan.Indices));
        writer.WriteInt32(value.IndexBufferRaw);
        Exact(writer, 0xA4, "GfxWorldDraw root header");
    }

    private static void WriteDpvsStatic(XSourceWriter writer, GfxWorldDpvsStatic value, DpvsStaticPlan plan)
    {
        writer.WriteUInt32(value.SModelCount); writer.WriteUInt32(value.StaticSurfaceCount); writer.WriteUInt32(value.LitSurfsBegin); writer.WriteUInt32(value.LitSurfsEnd);
        WriteUInt32Values(writer, value.VisibilityCounts, 8);
        for (int index = 0; index < 3; index++) writer.WriteInt32(Pointer(plan.SModelVisData[index]));
        for (int index = 0; index < 3; index++) writer.WriteInt32(Pointer(plan.SurfaceVisData[index]));
        writer.WriteInt32(Pointer(plan.SortedSurfIndex)); writer.WriteInt32(Pointer(plan.SModelInsts)); writer.WriteInt32(Pointer(plan.Surfaces)); writer.WriteInt32(Pointer(plan.SurfaceBounds)); writer.WriteInt32(Pointer(plan.SModelDrawInsts));
        writer.WriteInt32(Pointer(plan.SurfaceMaterials)); writer.WriteInt32(Pointer(plan.SurfaceCastsSunShadow)); writer.WriteUInt32(value.UsageCount);
        Exact(writer, 0x23C, "GfxWorldDpvsStatic root header");
    }

    private static void WriteDpvsDynamic(XSourceWriter writer, GfxWorldDpvsDynamic value, DpvsDynamicPlan plan)
    {
        WriteUInt32Values(writer, value.DynEntClientWordCount, 2);
        WriteUInt32Values(writer, value.DynEntClientCount, 2);
        for (int index = 0; index < 2; index++) writer.WriteInt32(Pointer(plan.CellBits[index]));
        for (int index = 0; index < 6; index++) writer.WriteInt32(Pointer(plan.VisData[index]));
        Exact(writer, 0x26C, "GfxWorldDpvsDynamic root header");
    }

    private static void WriteSunflare(XSourceWriter writer, Sunflare value, NestedPlan? spriteMaterial, NestedPlan? flareMaterial)
    {
        writer.WriteUInt32(value.HasValidData);
        writer.WriteInt32(Pointer(spriteMaterial));
        writer.WriteInt32(Pointer(flareMaterial));
        writer.WriteSingle(value.SpriteSize);
        writer.WriteSingle(value.FlareMinSize);
        writer.WriteSingle(value.FlareMinDot);
        writer.WriteSingle(value.FlareMaxSize);
        writer.WriteSingle(value.FlareMaxDot);
        writer.WriteSingle(value.FlareMaxAlpha);
        writer.WriteInt32(value.FlareFadeInTime);
        writer.WriteInt32(value.FlareFadeOutTime);
        writer.WriteSingle(value.BlindMinDot);
        writer.WriteSingle(value.BlindMaxDot);
        writer.WriteSingle(value.BlindMaxDarken);
        writer.WriteInt32(value.BlindFadeInTime);
        writer.WriteInt32(value.BlindFadeOutTime);
        writer.WriteSingle(value.GlareMinDot);
        writer.WriteSingle(value.GlareMaxDot);
        writer.WriteSingle(value.GlareMaxLighten);
        writer.WriteInt32(value.GlareFadeInTime);
        writer.WriteInt32(value.GlareFadeOutTime);
        WriteFloatValues(writer, value.SunFxPosition, 3);
    }

    private static void WriteFloatValues(XSourceWriter writer, IReadOnlyList<float> values, int expectedCount)
    {
        if (values.Count is 0)
        {
            writer.Reserve(checked(expectedCount * sizeof(float)));
            return;
        }
        if (values.Count != expectedCount)
            throw new InvalidDataException($"Expected {expectedCount} float values, but received {values.Count}.");
        foreach (float item in values) writer.WriteSingle(item);
    }

    private static int WordCount(int count) => checked((count + 31) >> 5);

    private static void WriteBounds(XSourceWriter writer, IW4.Assets.Math.Bounds value)
    {
        writer.WriteSingle(value.MidPoint.X); writer.WriteSingle(value.MidPoint.Y); writer.WriteSingle(value.MidPoint.Z); writer.WriteSingle(value.HalfSize.X); writer.WriteSingle(value.HalfSize.Y); writer.WriteSingle(value.HalfSize.Z);
    }

    private static void WriteBrushModel(XSourceWriter writer, GfxBrushModel value)
    {
        WriteFloatValues(writer, value.WritableMins, 3);
        WriteFloatValues(writer, value.WritableMaxs, 3);
        WriteFloatValues(writer, value.BoundsMins, 3);
        WriteFloatValues(writer, value.BoundsMaxs, 3);
        writer.WriteSingle(value.Radius);
        writer.WriteUInt16(value.SurfaceCount);
        writer.WriteUInt16(value.StartSurfIndex);
    }

    private static void WriteLightRegionAxis(XSourceWriter writer, GfxLightRegionAxis value)
    {
        WriteFloatValues(writer, value.Dir, 3); writer.WriteSingle(value.MidPoint); writer.WriteSingle(value.HalfSize);
    }

    private static void WriteStaticModelInst(XSourceWriter writer, GfxStaticModelInst value)
    {
        WriteBounds(writer, value.Bounds); writer.WriteSingle(value.LightingOrigin.X); writer.WriteSingle(value.LightingOrigin.Y); writer.WriteSingle(value.LightingOrigin.Z);
    }

    private static void WriteSurfaceBounds(XSourceWriter writer, GfxSurfaceBounds value)
    {
        WriteBounds(writer, value.Bounds); writer.WriteBytes(value.Unknown18To1F.ToArray());
    }

    private static void WriteTriangles(XSourceWriter writer, SrfTriangles value)
    {
        writer.WriteInt32(value.VertexLayerData); writer.WriteInt32(value.BaseVertex); writer.WriteUInt32(value.MinVertexIndex); writer.WriteUInt16(value.VertexCount); writer.WriteUInt16(value.TriCount); writer.WriteInt32(value.BaseIndex);
    }

    private static void WritePlacement(XSourceWriter writer, GfxPackedPlacement value)
    {
        WriteFloatValues(writer, value.Origin, 3); WriteUInt32Values(writer, value.PackedAxis, 3); writer.WriteSingle(value.Scale);
    }

    private static void WriteUInt32Values(XSourceWriter writer, IReadOnlyList<uint> values, int expectedCount)
    {
        if (values.Count is 0) { writer.Reserve(checked(expectedCount * sizeof(uint))); return; }
        if (values.Count != expectedCount) throw new InvalidDataException($"Expected {expectedCount} UInt32 values, but received {values.Count}.");
        foreach (uint item in values) writer.WriteUInt32(item);
    }

    private static int Pointer(EmissionBlockSegment? value) => value is null ? 0 : -1;
    private static int Pointer(NestedPlan? value) => value?.PointerRaw ?? 0;
    private static int Pointer(bool value) => value ? -1 : 0;

    private static void Add(List<EmissionBlockSegment> source, EmissionBlockSegment? value)
    {
        if (value is not null)
            source.Add(value);
    }

    private static void Add(List<EmissionBlockSegment> source, NestedPlan? value)
    {
        if (value is not null)
            source.AddRange(value.Source);
    }

    private static void Add(List<EmissionBlockSegment> source, IReadOnlyList<EmissionBlockSegment> values) => source.AddRange(values);

    private static void Exact(XSourceWriter writer, int expected, string name)
    {
        if (writer.Position != expected)
            throw new InvalidDataException($"{name} emitted 0x{writer.Position:X} bytes instead of 0x{expected:X}.");
    }

    private static EmissionError Error(string path, string message, int? rowIndex) =>
        new(path, message, rowIndex, XAssetType.GfxMap);

    private sealed record NestedPlan(
        EmissionBlockSegment? Header,
        IReadOnlyList<EmissionBlockSegment> Source,
        int PointerRaw = -1);
    private sealed record CellChildPlan(
        NestedPlan? Portals,
        EmissionBlockSegment? ReflectionProbes);
    private sealed record AabbIndexesPlan(
        int PointerRaw,
        EmissionBlockSegment? Source);
    private sealed record ShadowGeometryChildPlan(
        EmissionBlockSegment? SortedSurfIndex,
        EmissionBlockSegment? SModelIndex);

    private sealed record WorldDrawPlan(
        NestedPlan? ReflectionImages,
        EmissionBlockSegment? ReflectionOrigins,
        bool ReflectionTextures,
        NestedPlan? Lightmaps,
        bool LightmapPrimaryTextures,
        bool LightmapSecondaryTextures,
        NestedPlan? LightmapOverridePrimary,
        NestedPlan? LightmapOverrideSecondary,
        EmissionBlockSegment? Vertices,
        EmissionBlockSegment? VertexLayerData,
        EmissionBlockSegment? Indices,
        IReadOnlyList<EmissionBlockSegment> Source);

    private sealed record DpvsStaticPlan(
        EmissionBlockSegment? SortedSurfIndex,
        EmissionBlockSegment? SModelInsts,
        NestedPlan? Surfaces,
        EmissionBlockSegment? SurfaceBounds,
        NestedPlan? SModelDrawInsts,
        IReadOnlyList<bool> SModelVisData,
        IReadOnlyList<bool> SurfaceVisData,
        bool SurfaceMaterials,
        bool SurfaceCastsSunShadow,
        IReadOnlyList<EmissionBlockSegment> Source);

    private sealed record DpvsDynamicPlan(
        bool SceneDynModels,
        bool SceneDynBrushes,
        bool PrimaryLightEntityShadowVis,
        bool PrimaryLightDynEntShadowVis0,
        bool PrimaryLightDynEntShadowVis1,
        bool PrimaryLightForModelDynEnt,
        IReadOnlyList<bool> CellBits,
        IReadOnlyList<bool> VisData);
}
