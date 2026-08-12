using System.Buffers;
using System.Numerics;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Assets.Assets.XModel;
using IW4.Runtime.Assets.Images;

using IW4.Render.Assets;
using IW4.Render.EditorPreview;
using IW4.Render.Execution;
using IW4.Render.Geometry;
using IW4.Render.Lighting;
using IW4.Render.Materials;
using IW4.Render.SceneBuilding.Batching;
using IW4.Render.Scheduling;
using IW4.Render.Scheduling.StaticModels;
using IW4.Render.Shaders;
using IW4.Render.Textures;

namespace IW4.Render.SceneBuilding;

public sealed partial class MapSceneBuilder
{
    private readonly record struct StaticMaterialSelection(
        MaterialTechniqueSetAsset? Techset,
        SelectedColorPass? Pass,
        XSurfaceVertexDecoder? VertexDecoder,
        bool IsGenericFallback,
        bool IsExactTechniquePass,
        MapRenderEditorDepthPrepassPlan? EditorDepthPrepass);

    private sealed record PreparedStaticModelSource(
        StaticModelPlacement Placement,
        XModelAsset Model,
        IReadOnlyList<MapRenderStaticModelLodGeometry> LodGeometries);

    private sealed class StaticModelSharedBuildCache
    {
        internal Dictionary<
            (MaterialAsset Material,
                int? SelectedTechniqueSlot,
                bool AllowPreviewFallback,
                bool ForceGenericPreview),
            IReadOnlyList<StaticMaterialSelection>> MaterialSelections
                { get; } = [];

        internal Dictionary<MaterialAsset, MaterialTechniqueSetAsset?>
            TechniqueSets { get; } =
                new(ReferenceEqualityComparer.Instance);

        internal Dictionary<MaterialAsset, MapRenderState>
            SyntheticFallbackStates { get; } =
                new(ReferenceEqualityComparer.Instance);

        internal Dictionary<MaterialAsset, MapRenderEditorMaterialTexturePlan>
            MaterialTexturePlans { get; } =
                new(ReferenceEqualityComparer.Instance);
    }

    /// <summary>
    /// Decodes placement and materializes each unique model's immutable LOD
    /// catalog once. Four receiver variants and the normal-camera path can
    /// then fan out from this stable source catalog without re-reading the
    /// loaded runtime assets.
    /// </summary>
    private static PreparedStaticModelSource?[] PrepareStaticModelSources(
        GfxWorldAsset gfxMap)
    {
        ArgumentNullException.ThrowIfNull(gfxMap);
        IReadOnlyList<GfxStaticModelDrawInst> drawInsts =
            gfxMap.Dpvs.SModelDrawInsts;
        var uniqueModels = new List<XModelAsset>();
        var modelOrdinals = new Dictionary<XModelAsset, int>(
            ReferenceEqualityComparer.Instance);
        foreach (GfxStaticModelDrawInst drawInst in drawInsts)
        {
            if (drawInst.Model is { } model &&
                !modelOrdinals.ContainsKey(model))
            {
                modelOrdinals.Add(model, uniqueModels.Count);
                uniqueModels.Add(model);
            }
        }

        var lodCatalogs =
            new IReadOnlyList<MapRenderStaticModelLodGeometry>?[
                uniqueModels.Count];
        Parallel.For(
            0,
            uniqueModels.Count,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Min(
                    Environment.ProcessorCount,
                    4)
            },
            modelIndex =>
            {
                if (MapRenderStaticModelLodGeometryCatalog.TryCreate(
                        uniqueModels[modelIndex],
                        out IReadOnlyList<
                            MapRenderStaticModelLodGeometry>? lods))
                {
                    lodCatalogs[modelIndex] = lods;
                }
            });

        var prepared =
            new PreparedStaticModelSource?[drawInsts.Count];
        for (int drawInstIndex = 0;
             drawInstIndex < drawInsts.Count;
             drawInstIndex++)
        {
            GfxStaticModelDrawInst drawInst = drawInsts[drawInstIndex];
            if (drawInst.Model is not { } model ||
                !TryDecodePackedPlacement(
                    drawInst.Placement,
                    out StaticModelPlacement placement) ||
                !modelOrdinals.TryGetValue(model, out int modelOrdinal) ||
                lodCatalogs[modelOrdinal] is not { } lods)
            {
                continue;
            }

            prepared[drawInstIndex] = new PreparedStaticModelSource(
                placement,
                model,
                lods);
        }

        return prepared;
    }

    private static IReadOnlyList<StaticMaterialSelection>
        ResolveStaticMaterialSelections(
            MaterialAsset material,
            int? selectedTechniqueSlot,
            bool allowPreviewFallback,
            bool forceGenericPreview,
            RenderAssetLookup lookup,
            StaticModelSharedBuildCache sharedCache)
    {
        var cacheKey = (
            Material: material,
            SelectedTechniqueSlot: selectedTechniqueSlot,
            AllowPreviewFallback: allowPreviewFallback,
            ForceGenericPreview: forceGenericPreview);
        if (sharedCache.MaterialSelections.TryGetValue(
                cacheKey,
                out IReadOnlyList<StaticMaterialSelection>?
                    cachedSelections))
        {
            return cachedSelections;
        }

        if (!sharedCache.TechniqueSets.TryGetValue(
                material,
                out MaterialTechniqueSetAsset? resolvedTechset))
        {
            resolvedTechset = ResolveTechniqueSet(material, lookup);
            sharedCache.TechniqueSets.Add(material, resolvedTechset);
        }

        SelectedColorPass? baseSurfaceSelectedPass =
            SelectStaticModelBaseSurfaceTexturePass(
                material,
                resolvedTechset,
                lookup);
        IReadOnlyList<SelectedColorPass> editorTechniqueSelectedPasses =
            forceGenericPreview
                ? []
                : SelectEditorMaterialPasses(
                    material,
                    resolvedTechset,
                    lookup,
                    selectedTechniqueSlot,
                    out _);
        SelectedColorPass? genericFallbackSelectedPass =
            editorTechniqueSelectedPasses.Count == 0 &&
            baseSurfaceSelectedPass is null
                ? SelectGenericMaterialFallbackPass(
                    material,
                    resolvedTechset,
                    lookup,
                    selectedTechniqueSlot: null)
                : null;
        IReadOnlyList<SelectedColorPass> resolvedPasses;
        bool resolvedPreviewPassIsGenericFallback = false;
        if (editorTechniqueSelectedPasses.Count > 0)
        {
            // EditorPreview preserves the complete selected technique in
            // authored pass order.
            resolvedPasses = editorTechniqueSelectedPasses;
        }
        else if (allowPreviewFallback)
        {
            SelectedColorPass? resolvedPass =
                baseSurfaceSelectedPass ??
                genericFallbackSelectedPass ??
                (resolvedTechset is not null
                    ? SelectAuthoredMaterialCandidatePass(
                        material,
                        resolvedTechset,
                        lookup)
                    : null);
            resolvedPreviewPassIsGenericFallback =
                ReferenceEquals(
                    resolvedPass,
                    baseSurfaceSelectedPass) ||
                ReferenceEquals(
                    resolvedPass,
                    genericFallbackSelectedPass);
            if (resolvedPass is not null)
            {
                resolvedPass = ApplyStaticModelGenericFallbackState(
                    material,
                    resolvedTechset,
                    lookup,
                    resolvedPass,
                    sharedCache.SyntheticFallbackStates);
            }
            resolvedPasses = resolvedPass is null ? [] : [resolvedPass];
        }
        else
        {
            // An exact receiver sidecar never substitutes a base preview when
            // its selector slot cannot be materialized.
            resolvedPasses = [];
        }

        IReadOnlyList<StaticMaterialSelection> selections = resolvedPasses
            .Select(resolvedPass =>
                new StaticMaterialSelection(
                    resolvedTechset,
                    resolvedPass,
                    SelectStaticVertexDecoder(
                        resolvedPass.TexCoordSource),
                    editorTechniqueSelectedPasses.Count == 0 &&
                    resolvedPreviewPassIsGenericFallback,
                    editorTechniqueSelectedPasses.Count > 0,
                    SelectEditorStandardDepthPrepass(
                        material,
                        resolvedTechset,
                        lookup)))
            .ToArray();
        sharedCache.MaterialSelections.Add(cacheKey, selections);
        return selections;
    }

    private static IReadOnlyList<MapRenderTextureDecodeRequest>
        PlanStaticModelPrimaryTextureDecodes(
            GfxWorldAsset gfxMap,
            IReadOnlyList<PreparedStaticModelSource?> preparedSources,
            RenderAssetLookup lookup,
            MapRenderDrawMethod? editorPreviewDrawMethod,
            MapRenderSceneLightSelectorAssetState? sceneLightSelector,
            StaticModelSharedBuildCache sharedCache)
    {
        var requests = new List<MapRenderTextureDecodeRequest>();
        var seen = new HashSet<MapRenderTextureCacheKey>();
        (MapRenderSurfaceType SurfaceType,
            MapRenderTechniqueVariantAllocation Allocation,
            bool AllowPreviewFallback,
            bool ForceGenericPreview)[] variants =
        [
            (
                MapRenderSurfaceType.StaticModelRigid,
                MapRenderTechniqueVariantAllocation.Unshadowed,
                true,
                false),
            (
                MapRenderSurfaceType.StaticModelRigid,
                MapRenderTechniqueVariantAllocation.Unshadowed,
                true,
                true),
            (
                MapRenderSurfaceType.StaticModelRigid,
                MapRenderTechniqueVariantAllocation.ShadowMapAllocated,
                false,
                false),
            (
                MapRenderSurfaceType.StaticModelRigidNoSunShadow,
                MapRenderTechniqueVariantAllocation.Unshadowed,
                false,
                false),
            (
                MapRenderSurfaceType.StaticModelRigidNoSunShadow,
                MapRenderTechniqueVariantAllocation.ShadowMapAllocated,
                false,
                false)
        ];

        for (int drawInstIndex = 0;
             drawInstIndex < preparedSources.Count;
             drawInstIndex++)
        {
            if (preparedSources[drawInstIndex] is not { } preparedSource)
                continue;

            GfxStaticModelDrawInst drawInst =
                gfxMap.Dpvs.SModelDrawInsts[drawInstIndex];
            foreach (var variant in variants)
            {
                int? pageTechniqueSlot =
                    ResolvePreparedEditorTechniqueVariantSlot(
                        drawInst.PrimaryLightIndex,
                        variant.SurfaceType,
                        editorPreviewDrawMethod,
                        sceneLightSelector,
                        variant.Allocation);

                foreach (MapRenderStaticModelLodGeometry lodGeometry in
                         preparedSource.LodGeometries)
                {
                    for (int surfaceOffset = 0;
                         surfaceOffset < lodGeometry.SurfaceCount;
                         surfaceOffset++)
                    {
                        int materialSurfaceIndex =
                            lodGeometry.MaterialSurfaceStart +
                            surfaceOffset;
                        MaterialAsset? material =
                            SelectStaticModelSurfaceMaterial(
                                preparedSource.Model,
                                materialSurfaceIndex);
                        if (material is null)
                            continue;

                        int? surfaceTechniqueSlot =
                            MapRenderOpenGlStaticCameraRegionPolicy
                                .ResolveNormalCameraTechniqueSlot(
                                    material.CameraRegion,
                                    pageTechniqueSlot,
                                    editorPreviewDrawMethod);
                        if (!variant.AllowPreviewFallback &&
                            surfaceTechniqueSlot is null)
                        {
                            continue;
                        }

                        IReadOnlyList<StaticMaterialSelection> selections =
                            ResolveStaticMaterialSelections(
                                material,
                                surfaceTechniqueSlot,
                                variant.AllowPreviewFallback,
                                variant.ForceGenericPreview,
                                lookup,
                                sharedCache);
                        foreach (StaticMaterialSelection selection in
                                 selections)
                        {
                            if (selection is not
                                {
                                    Pass: { } selectedPass,
                                    VertexDecoder: not null
                                })
                            {
                                continue;
                            }

                            MapRenderTextureDecodeRequest request =
                                MapRenderTextureDecodeRequest.Create(
                                    selectedPass.Texture,
                                    selectedPass.Image,
                                    includeAuthoredMipChain: true);
                            if (seen.Add(request.Key))
                                requests.Add(request);
                        }
                    }
                }
            }
        }

        return requests;
    }

    private static int AddStaticModelInstancedGeometry(
        GfxWorldAsset gfxMap,
        MapRenderStaticModelLightingAtlas lightingAtlas,
        Dictionary<XSurface, InstancedSolidBatchBuilder> batches,
        ref MapRenderBounds bounds,
        out int drawnInsts,
        out int skippedInsts,
        out int skippedTriangles,
        out int readFailureTriangles,
        out int placementDecodeFailures,
        out IReadOnlyList<MapRenderStaticModelSchedulingInfo>
            scheduling)
    {
        int emittedTriangles = 0;
        drawnInsts = 0;
        skippedInsts = 0;
        skippedTriangles = 0;
        readFailureTriangles = 0;
        placementDecodeFailures = 0;
        var schedulingRows = new List<MapRenderStaticModelSchedulingInfo>(
            gfxMap.Dpvs.SModelDrawInsts.Count);

        for (int drawInstIndex = 0; drawInstIndex < gfxMap.Dpvs.SModelDrawInsts.Count; drawInstIndex++)
        {
            GfxStaticModelDrawInst drawInst = gfxMap.Dpvs.SModelDrawInsts[drawInstIndex];
            if (!TryDecodePackedPlacement(drawInst.Placement, out StaticModelPlacement placement))
            {
                skippedInsts++;
                placementDecodeFailures++;
                continue;
            }

            XModelAsset? model = drawInst.Model;
            if (!TrySelectLoadedStaticModelLod(
                    model,
                    out XModelLodInfo? lod,
                    out int preparedLodIndex,
                    out int geometrySurfaceStart,
                    out int materialSurfaceStart,
                    out int surfaceCount) ||
                lod?.ModelSurfs is not { } modelSurfs)
            {
                skippedInsts++;
                continue;
            }

            int beforeInstTriangles = emittedTriangles;
            MapRenderBounds instanceBounds = MapRenderBounds.Empty;
            Vector3 color = ColorFor(drawInst.Model?.Name ?? $"smodel_{drawnInsts + skippedInsts}");
            for (int i = 0; i < surfaceCount; i++)
            {
                int geometrySurfaceIndex = geometrySurfaceStart + i;
                int materialSurfaceIndex = materialSurfaceStart + i;
                XSurface surface = modelSurfs.Surfaces[geometrySurfaceIndex];
                MaterialAsset? material = SelectStaticModelSurfaceMaterial(
                    drawInst.Model!,
                    materialSurfaceIndex);
                if (!batches.TryGetValue(surface, out InstancedSolidBatchBuilder? batch))
                {
                    if (!TryBuildStaticSolidSurfaceLocal(
                            surface,
                            color,
                            out List<float> localVertices,
                            out List<uint> localIndices,
                            out int surfaceSkippedTriangles,
                            out int surfaceReadFailureTriangles,
                            out MapRenderBounds localBounds))
                    {
                        skippedTriangles += surfaceSkippedTriangles;
                        readFailureTriangles += surfaceReadFailureTriangles;
                        continue;
                    }

                    batch = new InstancedSolidBatchBuilder(
                        localVertices,
                        localIndices,
                        localBounds,
                        surfaceSkippedTriangles,
                        surfaceReadFailureTriangles);
                    batches.Add(surface, batch);
                }

                batch.Instances.Add(CreateStaticModelInstance(
                    placement,
                    lightingAtlas,
                    drawInstIndex,
                    materialSurfaceIndex,
                    drawInst.Model?.Name ?? $"smodel_{drawInstIndex}",
                    material?.Info.Name ?? string.Empty,
                    material?.CameraRegion ?? byte.MaxValue,
                    drawInst.PrimaryLightIndex,
                    drawInst.ReflectionProbeIndex,
                    drawInst.LightingHandle,
                    drawInst.GroundLighting,
                    drawInst.Flags));
                MapRenderBounds transformedBounds =
                    TransformStaticInstanceBounds(
                        batch.LocalBounds,
                        placement);
                bounds = IncludeBounds(bounds, transformedBounds);
                instanceBounds = IncludeBounds(
                    instanceBounds,
                    transformedBounds);
                emittedTriangles += batch.Indices.Count / 3;
                skippedTriangles += batch.SkippedTriangles;
                readFailureTriangles += batch.ReadFailureTriangles;
            }

            if (emittedTriangles == beforeInstTriangles)
            {
                skippedInsts++;
                continue;
            }

            drawnInsts++;
            schedulingRows.Add(new(
                drawInstIndex,
                ToRenderCoordinates(placement.Origin),
                drawInst.Placement.Scale,
                drawInst.CullDist,
                model!,
                preparedLodIndex,
                instanceBounds));
        }

        scheduling = schedulingRows.ToArray();
        return emittedTriangles;
    }

    private static bool TryBuildStaticSolidSurfaceLocal(
        XSurface surface,
        Vector3 color,
        out List<float> vertices,
        out List<uint> indices,
        out int skippedTriangles,
        out int readFailureTriangles,
        out MapRenderBounds localBounds)
    {
        vertices = new List<float>(surface.TriCount * 3 * MapRenderScene.VertexFloatCount);
        indices = new List<uint>(surface.TriCount * 3);
        skippedTriangles = 0;
        readFailureTriangles = 0;
        localBounds = MapRenderBounds.Empty;

        for (int triangle = 0; triangle < surface.TriCount; triangle++)
        {
            int indexOffset = triangle * 3;
            if (indexOffset < 0 || indexOffset + 2 >= surface.TriIndices.Count)
            {
                skippedTriangles++;
                readFailureTriangles++;
                continue;
            }

            int i0 = surface.TriIndices[indexOffset];
            int i1 = surface.TriIndices[indexOffset + 1];
            int i2 = surface.TriIndices[indexOffset + 2];
            if (i0 >= surface.VertCount || i1 >= surface.VertCount || i2 >= surface.VertCount ||
                !XSurfaceVertexDecoder.TryReadPosition(surface, i0, out Vector3 p0) ||
                !XSurfaceVertexDecoder.TryReadPosition(surface, i1, out Vector3 p1) ||
                !XSurfaceVertexDecoder.TryReadPosition(surface, i2, out Vector3 p2))
            {
                skippedTriangles++;
                readFailureTriangles++;
                continue;
            }

            AddTriangle(vertices, indices, p0, p1, p2, color);
            localBounds = localBounds.Include(p0).Include(p1).Include(p2);
        }

        return indices.Count > 0;
    }

    private static void AddStaticModelTexturedGeometry(
        GfxWorldAsset gfxMap,
        IReadOnlyList<PreparedStaticModelSource?> preparedSources,
        StaticModelSharedBuildCache sharedCache,
        MapRenderStaticModelLightingAtlas lightingAtlas,
        RenderAssetLookup lookup,
        IMapRenderWorldTextureBindingResolver worldTextureBindings,
        MapRenderDrawMethod? editorPreviewDrawMethod,
        MapRenderSceneLightSelectorAssetState? sceneLightSelector,
        MapRenderSurfaceType surfaceType,
        MapRenderTechniqueVariantAllocation techniqueAllocation,
        bool allowPreviewFallback,
        bool forceGenericPreview,
        IGfxImagePayloadResolver imageStreams,
        Dictionary<StaticTexturedBatchKey, InstancedTexturedBatchBuilder> batches,
        MapRenderTextureCache textureCache,
        HashSet<MapRenderTextureCacheKey> failedTextureCacheKeys,
        MapRenderShaderTranslationCache shaderTranslationCache,
        string progressLabel,
        Action<string>? reportProgress,
        ref int textureDecodedCount,
        ref int textureDecodeSkippedCount,
        out int genericFallbackTexturedSurfaceCount,
        out int genericFallbackTexturedTriangleCount,
        out int authoredCandidateTexturedSurfaceCount,
        out int authoredCandidateTexturedTriangleCount,
        out IReadOnlyDictionary<int, MapRenderBounds> allLodBounds,
        out IReadOnlyDictionary<int, uint> renderableLodMasks)
    {
        ArgumentNullException.ThrowIfNull(preparedSources);
        ArgumentNullException.ThrowIfNull(sharedCache);
        ArgumentNullException.ThrowIfNull(shaderTranslationCache);
        if (preparedSources.Count != gfxMap.Dpvs.SModelDrawInsts.Count)
        {
            throw new ArgumentException(
                "Prepared static-model source count must match the world draw-instance count.",
                nameof(preparedSources));
        }

        genericFallbackTexturedSurfaceCount = 0;
        genericFallbackTexturedTriangleCount = 0;
        authoredCandidateTexturedSurfaceCount = 0;
        authoredCandidateTexturedTriangleCount = 0;
        var allLodBoundsByObjectIndex =
            new Dictionary<int, MapRenderBounds>();
        var renderableLodMaskByObjectIndex =
            new Dictionary<int, uint>();
        var editorDrawGroupIds =
            new Dictionary<StaticTexturedDrawGroupKey, int>();
        var failedBatchKeys = new HashSet<StaticTexturedBatchKey>();
        int nextPreparedBatchOrdinal = 0;
        for (int drawInstIndex = 0; drawInstIndex < gfxMap.Dpvs.SModelDrawInsts.Count; drawInstIndex++)
        {
            if (drawInstIndex != 0 && (drawInstIndex & 0xff) == 0)
            {
                reportProgress?.Invoke(
                    $"{progressLabel}: {drawInstIndex}/{gfxMap.Dpvs.SModelDrawInsts.Count} instances");
            }

            GfxStaticModelDrawInst drawInst = gfxMap.Dpvs.SModelDrawInsts[drawInstIndex];
            // PrimaryLightIndex is authored per instance. Keep its exact
            // all-clear selector identity through pass fallback and batching;
            // otherwise a shared XModel/material can silently inherit another
            // instance's base-light or directional-sun technique.
            int? pageTechniqueSlot =
                ResolvePreparedEditorTechniqueVariantSlot(
                    drawInst.PrimaryLightIndex,
                    surfaceType,
                    editorPreviewDrawMethod,
                    sceneLightSelector,
                    techniqueAllocation);
            if (preparedSources[drawInstIndex] is not { } preparedSource)
                continue;

            StaticModelPlacement placement = preparedSource.Placement;
            XModelAsset model = preparedSource.Model;
            IReadOnlyList<MapRenderStaticModelLodGeometry> lodGeometries =
                preparedSource.LodGeometries;

            int preparedLodIndex = lodGeometries[0].LodIndex;
            foreach (MapRenderStaticModelLodGeometry lodGeometry in
                     lodGeometries)
            {
                int lodIndex = lodGeometry.LodIndex;
                bool isPreparedLod = lodIndex == preparedLodIndex;
                XModelSurfsAsset modelSurfs = lodGeometry.ModelSurfs;
                int renderReadySurfaceCount = 0;
                for (int surfaceOffset = 0;
                     surfaceOffset < lodGeometry.SurfaceCount;
                     surfaceOffset++)
                {
                int materialSurfaceIndex =
                    lodGeometry.MaterialSurfaceStart + surfaceOffset;
                XSurface surface = modelSurfs.Surfaces[surfaceOffset];
                MaterialAsset? material = SelectStaticModelSurfaceMaterial(model, materialSurfaceIndex);
                if (material is null)
                    continue;

                int? surfaceTechniqueSlot =
                    MapRenderOpenGlStaticCameraRegionPolicy
                        .ResolveNormalCameraTechniqueSlot(
                            material.CameraRegion,
                            pageTechniqueSlot,
                            editorPreviewDrawMethod);
                if (!allowPreviewFallback &&
                    surfaceTechniqueSlot is null)
                {
                    continue;
                }

                IReadOnlyList<StaticMaterialSelection> selections =
                    ResolveStaticMaterialSelections(
                        material,
                        surfaceTechniqueSlot,
                        allowPreviewFallback,
                        forceGenericPreview,
                        lookup,
                        sharedCache);

                if (selections.Count == 0)
                    continue;

                // The reflection probe is an authored per-instance custom
                // sampler, while backend sampler bindings are draw-scoped.
                // Split the complete multi-pass group only when at least one
                // selected pass consumes destination 1; non-reflective groups
                // retain the original instancing density.
                byte? reflectionProbeBatchIndex = null;
                for (int selectionIndex = 0;
                     selectionIndex < selections.Count;
                     selectionIndex++)
                {
                    if (selections[selectionIndex].Pass is { } pass &&
                        new MapRenderWorldCustomSamplerSelection(
                                pass.Pass.CustomSamplerFlags)
                            .BindsReflectionProbe)
                    {
                        reflectionProbeBatchIndex =
                            drawInst.ReflectionProbeIndex;
                        break;
                    }
                }

                var editorDrawGroupKey = new StaticTexturedDrawGroupKey(
                    lodIndex,
                    surface,
                    material,
                    surfaceTechniqueSlot,
                    reflectionProbeBatchIndex,
                    drawInst.PrimaryLightIndex);
                if (!editorDrawGroupIds.TryGetValue(
                        editorDrawGroupKey,
                        out int editorDrawGroupId))
                {
                    editorDrawGroupId = editorDrawGroupIds.Count;
                    editorDrawGroupIds.Add(
                        editorDrawGroupKey,
                        editorDrawGroupId);
                }

                var preparedPassBatches =
                    new List<(InstancedTexturedBatchBuilder Batch, bool IsFallback)>(
                        selections.Count);
                bool selectedGroupReady = true;
                foreach (StaticMaterialSelection selection in selections)
                {
                    MaterialTechniqueSetAsset? techset = selection.Techset;
                    SelectedColorPass? selectedPass = selection.Pass;
                    XSurfaceVertexDecoder? vertexDecoder = selection.VertexDecoder;
                    if (selectedPass is null || vertexDecoder is null)
                    {
                        selectedGroupReady = false;
                        break;
                    }

                    var batchKey = new StaticTexturedBatchKey(
                        lodIndex,
                        surface,
                        material,
                        surfaceTechniqueSlot,
                        selectedPass.Pass.TechniqueSlot,
                        selectedPass.Pass.PassIndex,
                        selectedPass.Pass.SamplerArgIndex,
                        selectedPass.Pass.SamplerHash,
                        reflectionProbeBatchIndex,
                        drawInst.PrimaryLightIndex);
                    if (failedBatchKeys.Contains(batchKey))
                    {
                        selectedGroupReady = false;
                        break;
                    }

                    if (!batches.TryGetValue(batchKey, out InstancedTexturedBatchBuilder? batch))
                    {
                        if (!TryDecodeTexture(
                                selectedPass.Texture,
                                selectedPass.Image,
                                imageStreams,
                                textureCache,
                                failedTextureCacheKeys,
                                true,
                                ref textureDecodedCount,
                                ref textureDecodeSkippedCount,
                                out MapRenderTexture? texture) ||
                            texture is null)
                        {
                            failedBatchKeys.Add(batchKey);
                            selectedGroupReady = false;
                            break;
                        }

                        MapRenderUvRoute uvRoute = BuildStaticModelUvRoute(selectedPass.TexCoordSource);
                        MapRenderEditorMaterialTexturePlan? texturePlan = null;
                        if (!sharedCache.MaterialTexturePlans.TryGetValue(
                                material,
                                out texturePlan))
                        {
                            texturePlan = MapRenderEditorMaterialTexturePlanner.Plan(
                                material.Textures,
                                (_, row) =>
                                    new MapRenderEditorMaterialTextureResolution(
                                        row.Image ?? lookup.ResolveImage(row.DataPointer),
                                        null));
                            sharedCache.MaterialTexturePlans.Add(
                                material,
                                texturePlan);
                        }

                        IReadOnlyList<PreparedStaticColorLayer>
                            preparedStaticColorLayers = PrepareStaticColorLayers(
                                enableEditorMultiTexture: true,
                                material,
                                techset,
                                lookup,
                                texturePlan,
                                selectedPass,
                                texture,
                                uvRoute,
                                vertexDecoder,
                                imageStreams,
                                textureCache,
                                failedTextureCacheKeys,
                                ref textureDecodedCount,
                                ref textureDecodeSkippedCount);
                        bool hasAuthoredSourcePass =
                            selectedPass.AuthoredProgramExecutable;
                        MapRenderShaderVertexInputBinding[]
                            selectedVertexInputs = hasAuthoredSourcePass
                                ? ResolveSelectedVertexInputs(
                                    techset,
                                    lookup,
                                    selectedPass,
                                    XSurfaceVertexDecoder.BackendRow)
                                : [];
                        SelectedColorPass? depthPrepassSelection =
                            hasAuthoredSourcePass &&
                            selection.EditorDepthPrepass is not null
                                ? CreateStandardDepthPrepassSelection(
                                    selectedPass,
                                    selection.EditorDepthPrepass)
                                : null;
                        MapRenderShaderVertexInputBinding[]
                            depthPrepassVertexInputs =
                                depthPrepassSelection is not null
                                    ? ResolveSelectedVertexInputs(
                                        techset,
                                        lookup,
                                        depthPrepassSelection,
                                        XSurfaceVertexDecoder.BackendRow)
                                    : [];
                        bool depthPrepassVertexInputsCompatible =
                            TryMergeVertexInputBindings(
                                selectedVertexInputs,
                                depthPrepassVertexInputs,
                                out MapRenderShaderVertexInputBinding[]
                                    materializedVertexInputs,
                                out string depthPrepassVertexInputBlocker);
                        if (!TryBuildTexturedStaticXSurfaceLocal(
                                surface,
                                preparedStaticColorLayers,
                                materializedVertexInputs,
                                out List<float> surfaceVertices,
                                out List<float> surfaceRsxVertexInputs,
                                out bool surfaceRsxVertexInputsReady,
                                out string surfaceRsxVertexInputBlocker,
                                out List<uint> surfaceIndices,
                                out MapRenderBounds localBounds,
                                useGenericFallback: !hasAuthoredSourcePass))
                        {
                            failedBatchKeys.Add(batchKey);
                            selectedGroupReady = false;
                            break;
                        }

                        IReadOnlyList<MapRenderColorLayer> staticColorLayers =
                            preparedStaticColorLayers
                                .Select(layer => layer.Layer)
                                .ToArray();
                        IReadOnlyList<MapRenderMaterialSamplerBinding> samplerBindings =
                            PrepareStaticMaterialSamplerBindings(
                                material,
                                techset,
                                lookup,
                                gfxMap,
                                worldTextureBindings,
                                selectedPass,
                                reflectionProbeBatchIndex,
                                uvRoute,
                                staticColorLayers,
                                imageStreams,
                                textureCache,
                                failedTextureCacheKeys,
                                ref textureDecodedCount,
                                ref textureDecodeSkippedCount);
                        MapRenderShaderExecutionContract shaderExecution = BuildShaderExecutionContract(
                            material,
                            techset,
                            lookup,
                            selectedPass,
                            samplerBindings,
                            vertexInputPayloadReady:
                                surfaceRsxVertexInputsReady,
                            vertexInputPayloadBlocker:
                                surfaceRsxVertexInputBlocker,
                            authoredSourcePassAvailable:
                                hasAuthoredSourcePass,
                            shaderTranslationCache:
                                shaderTranslationCache,
                            fixedVertexSourceBackendRow:
                                XSurfaceVertexDecoder.BackendRow);
                        MapRenderShaderExecutionContract?
                            depthPrepassShaderExecution = null;
                        if (depthPrepassSelection is not null)
                        {
                            bool depthPayloadReady =
                                depthPrepassVertexInputsCompatible &&
                                surfaceRsxVertexInputsReady;
                            string depthPayloadBlocker =
                                !depthPrepassVertexInputsCompatible
                                    ? depthPrepassVertexInputBlocker
                                    : surfaceRsxVertexInputBlocker;
                            MapRenderShaderExecutionContract candidate =
                                BuildShaderExecutionContract(
                                    material,
                                    techset,
                                    lookup,
                                    depthPrepassSelection,
                                    [],
                                    depthPayloadReady,
                                    depthPayloadBlocker,
                                    authoredSourcePassAvailable: true,
                                    purpose:
                                        MapRenderShaderExecutionPurpose
                                            .DepthOnly,
                                    shaderTranslationCache:
                                        shaderTranslationCache,
                                    fixedVertexSourceBackendRow:
                                        XSurfaceVertexDecoder.BackendRow);
                            if (candidate.ProgramExecutionReady)
                            {
                                depthPrepassShaderExecution = candidate;
                            }
                        }
                        MapRenderState renderState = selectedPass.State;
                        batch = new InstancedTexturedBatchBuilder(
                            lodIndex,
                            selectedPass.Pass,
                            texture,
                            staticColorLayers,
                            samplerBindings,
                            shaderExecution,
                            uvRoute,
                            renderState,
                            selection.EditorDepthPrepass,
                            depthPrepassShaderExecution,
                            selectedPass.UnresolvedCodeSamplerCount,
                            surfaceVertices,
                            surfaceRsxVertexInputs,
                            surfaceIndices,
                            localBounds,
                            editorDrawGroupId,
                            selection.IsExactTechniquePass,
                            drawInst.PrimaryLightIndex);
                        batches.Add(batchKey, batch);
                    }

                    preparedPassBatches.Add((batch, selection.IsGenericFallback));
                }

                // A selected authored group is attached atomically. If one pass
                // cannot be prepared, no partial group is submitted for the instance.
                if (!selectedGroupReady || preparedPassBatches.Count != selections.Count)
                    continue;

                MapRenderStaticModelInstance instance = CreateStaticModelInstance(
                    placement,
                    lightingAtlas,
                    drawInstIndex,
                    materialSurfaceIndex,
                    model.Name ?? $"smodel_{drawInstIndex}",
                    material.Info.Name ?? string.Empty,
                    material.CameraRegion,
                    drawInst.PrimaryLightIndex,
                    drawInst.ReflectionProbeIndex,
                    drawInst.LightingHandle,
                    drawInst.GroundLighting,
                    drawInst.Flags);
                MapRenderBounds transformedBounds =
                    TransformStaticInstanceBounds(
                        preparedPassBatches[0].Batch.LocalBounds,
                        placement);
                if (transformedBounds.IsValid)
                {
                    bool hasAccumulatedBounds =
                        allLodBoundsByObjectIndex.TryGetValue(
                            drawInstIndex,
                            out MapRenderBounds accumulatedBounds);
                    allLodBoundsByObjectIndex[drawInstIndex] =
                        hasAccumulatedBounds
                            ? IncludeBounds(
                                accumulatedBounds,
                                transformedBounds)
                            : transformedBounds;
                }
                renderReadySurfaceCount++;
                foreach (var preparedPassBatch in preparedPassBatches)
                {
                    preparedPassBatch.Batch.Instances.Add(instance);
                    if (isPreparedLod)
                    {
                        if (preparedPassBatch.Batch.PreparedInstances.Count ==
                            0)
                        {
                            preparedPassBatch.Batch.PreparedSourceOrdinal =
                                nextPreparedBatchOrdinal++;
                        }
                        preparedPassBatch.Batch.PreparedInstances.Add(
                            instance);
                    }
                }

                if (!isPreparedLod)
                    continue;

                int surfaceTriangleCount = preparedPassBatches[0].Batch.Indices.Count / 3;
                if (preparedPassBatches[0].IsFallback)
                {
                    genericFallbackTexturedSurfaceCount++;
                    genericFallbackTexturedTriangleCount += surfaceTriangleCount;
                }
                else
                {
                    authoredCandidateTexturedSurfaceCount++;
                    authoredCandidateTexturedTriangleCount += surfaceTriangleCount;
                }
                }
                if (renderReadySurfaceCount == lodGeometry.SurfaceCount)
                {
                    renderableLodMaskByObjectIndex.TryGetValue(
                        drawInstIndex,
                        out uint renderableLodMask);
                    renderableLodMaskByObjectIndex[drawInstIndex] =
                        renderableLodMask | (1u << lodIndex);
                }
            }
        }
        allLodBounds = allLodBoundsByObjectIndex;
        renderableLodMasks = renderableLodMaskByObjectIndex;
    }

    private static IReadOnlyDictionary<int, MapRenderBounds>
        MergeStaticModelBounds(
            IReadOnlyDictionary<int, MapRenderBounds> first,
            IReadOnlyDictionary<int, MapRenderBounds> second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        Dictionary<int, MapRenderBounds> merged =
            first.ToDictionary(entry => entry.Key, entry => entry.Value);
        foreach ((int objectIndex, MapRenderBounds bounds) in second)
        {
            merged[objectIndex] = merged.TryGetValue(
                objectIndex,
                out MapRenderBounds existing)
                    ? IncludeBounds(existing, bounds)
                    : bounds;
        }

        return merged;
    }

    private static IReadOnlyDictionary<int, uint>
        MergeStaticModelLodMasks(
            IReadOnlyDictionary<int, uint> first,
            IReadOnlyDictionary<int, uint> second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        Dictionary<int, uint> merged =
            first.ToDictionary(entry => entry.Key, entry => entry.Value);
        foreach ((int objectIndex, uint lodMask) in second)
        {
            merged.TryGetValue(objectIndex, out uint existing);
            merged[objectIndex] = existing | lodMask;
        }

        return merged;
    }

    private static bool TryBuildTexturedStaticXSurfaceLocal(
        XSurface surface,
        IReadOnlyList<PreparedStaticColorLayer> colorLayers,
        IReadOnlyList<MapRenderShaderVertexInputBinding> rsxInputBindings,
        out List<float> vertices,
        out List<float> rsxVertexInputs,
        out bool rsxVertexInputsReady,
        out string rsxVertexInputBlocker,
        out List<uint> indices,
        out MapRenderBounds localBounds,
        bool useGenericFallback)
    {
        vertices = new List<float>(surface.TriCount * 3 * MapRenderScene.TexturedVertexFloatCount);
        bool materializeRsxVertexInputs = rsxInputBindings.Count > 0 ||
            useGenericFallback;
        rsxVertexInputs = materializeRsxVertexInputs
            ? new List<float>(checked(
                surface.TriCount *
                3 *
                RsxVertexInputCount *
                RsxVertexInputComponentCount))
            : [];
        rsxVertexInputsReady = materializeRsxVertexInputs;
        var rsxVertexInputFailures =
            new SortedSet<string>(StringComparer.Ordinal);
        rsxVertexInputBlocker = string.Empty;
        indices = new List<uint>(surface.TriCount * 3);
        localBounds = MapRenderBounds.Empty;

        if (colorLayers.Count == 0)
            return false;

        byte[] verts0 = surface.Verts0 as byte[] ??
            surface.Verts0.ToArray();
        var preparedRsxVertexInputs = materializeRsxVertexInputs
            ? new Vector4[checked(3 * RsxVertexInputCount)]
            : [];

        for (int triangle = 0; triangle < surface.TriCount; triangle++)
        {
            int indexOffset = triangle * 3;
            if (indexOffset < 0 || indexOffset + 2 >= surface.TriIndices.Count)
                continue;

            int i0 = surface.TriIndices[indexOffset];
            int i1 = surface.TriIndices[indexOffset + 1];
            int i2 = surface.TriIndices[indexOffset + 2];
            if (i0 >= surface.VertCount || i1 >= surface.VertCount || i2 >= surface.VertCount ||
                !XSurfaceVertexDecoder.TryReadPosition(surface, i0, out Vector3 p0) ||
                !XSurfaceVertexDecoder.TryReadPosition(surface, i1, out Vector3 p1) ||
                !XSurfaceVertexDecoder.TryReadPosition(surface, i2, out Vector3 p2) ||
                !TryReadStaticLayerUvs(surface, i0, colorLayers, out Vector2[] layerUvs0) ||
                !TryReadStaticLayerUvs(surface, i1, colorLayers, out Vector2[] layerUvs1) ||
                !TryReadStaticLayerUvs(surface, i2, colorLayers, out Vector2[] layerUvs2))
            {
                continue;
            }

            colorLayers[0].Decoder.TryReadNormal(surface, i0, out Vector3 normal0);
            colorLayers[0].Decoder.TryReadNormal(surface, i1, out Vector3 normal1);
            colorLayers[0].Decoder.TryReadNormal(surface, i2, out Vector3 normal2);
            bool triangleRsxInputsReady = true;
            if (materializeRsxVertexInputs)
            {
                Span<Vector4> preparedVertex0 = preparedRsxVertexInputs.AsSpan(
                    0,
                    RsxVertexInputCount);
                triangleRsxInputsReady = useGenericFallback
                    ? TryBuildGenericRsxVertexInputs(
                        preparedVertex0,
                        p0,
                        layerUvs0[0],
                        out string blocker0)
                    : TryReadStaticRsxVertexInputs(
                        verts0,
                        surface,
                        i0,
                        rsxInputBindings,
                        preparedVertex0,
                        out blocker0);
                if (!triangleRsxInputsReady)
                {
                    rsxVertexInputFailures.Add(
                        $"vertex{i0}:{blocker0}");
                }

                Span<Vector4> preparedVertex1 = preparedRsxVertexInputs.AsSpan(
                    RsxVertexInputCount,
                    RsxVertexInputCount);
                bool vertex1Ready = useGenericFallback
                    ? TryBuildGenericRsxVertexInputs(
                        preparedVertex1,
                        p1,
                        layerUvs1[0],
                        out string blocker1)
                    : TryReadStaticRsxVertexInputs(
                        verts0,
                        surface,
                        i1,
                        rsxInputBindings,
                        preparedVertex1,
                        out blocker1);
                if (!vertex1Ready)
                    rsxVertexInputFailures.Add($"vertex{i1}:{blocker1}");

                Span<Vector4> preparedVertex2 = preparedRsxVertexInputs.AsSpan(
                    2 * RsxVertexInputCount,
                    RsxVertexInputCount);
                bool vertex2Ready = useGenericFallback
                    ? TryBuildGenericRsxVertexInputs(
                        preparedVertex2,
                        p2,
                        layerUvs2[0],
                        out string blocker2)
                    : TryReadStaticRsxVertexInputs(
                        verts0,
                        surface,
                        i2,
                        rsxInputBindings,
                        preparedVertex2,
                        out blocker2);
                if (!vertex2Ready)
                    rsxVertexInputFailures.Add($"vertex{i2}:{blocker2}");

                triangleRsxInputsReady &= vertex1Ready && vertex2Ready;
            }
            AddTexturedTriangle(
                vertices,
                indices,
                p0,
                p1,
                p2,
                layerUvs0[0],
                layerUvs1[0],
                layerUvs2[0],
                layerUvs0,
                layerUvs1,
                layerUvs2,
                normal0: normal0,
                normal1: normal1,
                normal2: normal2);
            if (materializeRsxVertexInputs)
            {
                if (triangleRsxInputsReady)
                {
                    AddRsxVertexInputs(
                        rsxVertexInputs,
                        preparedRsxVertexInputs.AsSpan(
                            0,
                            RsxVertexInputCount));
                    AddRsxVertexInputs(
                        rsxVertexInputs,
                        preparedRsxVertexInputs.AsSpan(
                            RsxVertexInputCount,
                            RsxVertexInputCount));
                    AddRsxVertexInputs(
                        rsxVertexInputs,
                        preparedRsxVertexInputs.AsSpan(
                            2 * RsxVertexInputCount,
                            RsxVertexInputCount));
                }
                else
                {
                    rsxVertexInputsReady = false;
                }
            }
            localBounds = localBounds
                .Include(p0)
                .Include(p1)
                .Include(p2);
        }

        if (!rsxVertexInputsReady ||
            rsxVertexInputs.Count != checked(
                (vertices.Count /
                    MapRenderScene.TexturedVertexFloatCount) *
                RsxVertexInputCount *
                RsxVertexInputComponentCount))
        {
            rsxVertexInputsReady = false;
            rsxVertexInputs.Clear();
        }
        rsxVertexInputBlocker = !materializeRsxVertexInputs
            ? "RSX_VERTEX_INPUT_PAYLOAD_NOT_AVAILABLE_FOR_GENERIC_FALLBACK"
            : rsxVertexInputsReady
                ? string.Empty
                : rsxVertexInputFailures.Count == 0
                    ? "STATIC_XSURFACE_RSX_VERTEX_INPUT_PAYLOAD_COUNT_MISMATCH"
                    : string.Join('|', rsxVertexInputFailures);

        return indices.Count > 0;
    }

    private static bool TryBuildGenericRsxVertexInputs(
        Span<Vector4> values,
        Vector3 position,
        Vector2 uv,
        out string blocker)
    {
        if (values.Length != RsxVertexInputCount ||
            !float.IsFinite(position.X) ||
            !float.IsFinite(position.Y) ||
            !float.IsFinite(position.Z) ||
            !float.IsFinite(uv.X) ||
            !float.IsFinite(uv.Y))
        {
            blocker = "GENERIC_FALLBACK_VERTEX_INPUT_NONFINITE";
            return false;
        }

        values.Fill(DefaultRsxVertexInput);
        values[0] = new Vector4(position, 1f);
        values[3] = new Vector4(uv, 0f, 1f);
        values[8] = values[3];
        blocker = string.Empty;
        return true;
    }

    internal static bool TryReadStaticRsxVertexInputs(
        ReadOnlySpan<byte> verts0,
        XSurface surface,
        int vertexIndex,
        IReadOnlyList<MapRenderShaderVertexInputBinding> bindings,
        Span<Vector4> values,
        out string blocker)
    {
        // verts0 is retained in this signature because map batching already
        // materializes it once for its triangle loop. XSurface owns the same
        // bytes; the centralized decoder is the sole route authority.
        _ = verts0;
        return XSurfaceVertexDecoder.TryReadRsxVertexInputs(
            surface,
            vertexIndex,
            bindings,
            values,
            out blocker);
    }

    private static bool TryReadStaticLayerUvs(
        XSurface surface,
        int vertexIndex,
        IReadOnlyList<PreparedStaticColorLayer> colorLayers,
        out Vector2[] layerUvs)
    {
        layerUvs = new Vector2[Math.Min(
            colorLayers.Count,
            MapRenderScene.MaxColorLayerCount)];
        for (int layerIndex = 0;
             layerIndex < layerUvs.Length;
             layerIndex++)
        {
            if (!colorLayers[layerIndex].Decoder.TryReadTexCoord(
                    surface,
                    vertexIndex,
                    out Vector2 rawUv) ||
                !TryPrepareTexCoord(
                    rawUv,
                    allowSanitization: false,
                    out layerUvs[layerIndex],
                    out _))
            {
                layerUvs = [];
                return false;
            }
        }

        return layerUvs.Length > 0;
    }

    private static MaterialAsset? SelectStaticModelSurfaceMaterial(XModelAsset model, int lodSurfaceIndex)
    {
        return (uint)lodSurfaceIndex < (uint)model.Materials.Count
            ? model.Materials[lodSurfaceIndex]
            : null;
    }

    internal static bool TrySelectLoadedStaticModelLod(
        XModelAsset? model,
        out XModelLodInfo? lod,
        out int geometrySurfaceStart,
        out int materialSurfaceStart,
        out int surfaceCount) =>
        TrySelectLoadedStaticModelLod(
            model,
            out lod,
            out _,
            out geometrySurfaceStart,
            out materialSurfaceStart,
            out surfaceCount);

    internal static bool TrySelectLoadedStaticModelLod(
        XModelAsset? model,
        out XModelLodInfo? lod,
        out int lodIndex,
        out int geometrySurfaceStart,
        out int materialSurfaceStart,
        out int surfaceCount)
    {
        lod = null;
        lodIndex = -1;
        geometrySurfaceStart = 0;
        materialSurfaceStart = 0;
        surfaceCount = 0;
        if (model is null)
            return false;

        int lodCount = model.NumLods == 0
            ? model.Lods.Count
            : Math.Min(model.NumLods, model.Lods.Count);
        int firstLoadedLod = model.MaxLoadedLod;
        if ((uint)firstLoadedLod >= (uint)lodCount)
            return false;

        for (int i = firstLoadedLod; i < lodCount; i++)
        {
            XModelLodInfo candidate = model.Lods[i];
            IReadOnlyList<XSurface>? surfaces = candidate.ModelSurfs?.Surfaces;
            if (surfaces is null || surfaces.Count == 0 || candidate.NumSurfs == 0)
                continue;

            int count = Math.Min(candidate.NumSurfs, surfaces.Count);
            if (count <= 0)
                continue;

            lod = candidate;
            lodIndex = i;
            // Each XModelSurfs root owns its own zero-based surface array.
            // XModelLodInfo.SurfIndex instead selects the corresponding base
            // entry in the parent XModel material-handle array.
            geometrySurfaceStart = 0;
            materialSurfaceStart = candidate.SurfIndex;
            surfaceCount = count;
            return true;
        }

        return false;
    }

    private static bool TryDecodePackedPlacement(GfxPackedPlacement placement, out StaticModelPlacement transform)
    {
        transform = default;
        if (placement.Origin.Count < 3 || placement.PackedAxis.Count < 3 || !float.IsFinite(placement.Scale))
            return false;

        Vector3 origin = new(placement.Origin[0], placement.Origin[1], placement.Origin[2]);
        if (!IsReasonable(origin))
            return false;

        transform = new StaticModelPlacement(
            origin,
            DecodePackedAxis(placement.PackedAxis[0]) * placement.Scale,
            DecodePackedAxis(placement.PackedAxis[1]) * placement.Scale,
            DecodePackedAxis(placement.PackedAxis[2]) * placement.Scale);
        return IsReasonable(transform.Axis0) &&
               IsReasonable(transform.Axis1) &&
               IsReasonable(transform.Axis2);
    }

    private static Vector3 DecodePackedAxis(uint packed)
    {
        return new Vector3(
            SignExtend((int)(packed & 0x7ff), 11) / 1023f,
            SignExtend((int)((packed >> 11) & 0x7ff), 11) / 1023f,
            SignExtend((int)((packed >> 22) & 0x3ff), 10) / 511f);
    }

    private static int SignExtend(int value, int bits)
    {
        int sign = 1 << (bits - 1);
        return (value ^ sign) - sign;
    }

    private static MapRenderStaticModelInstance CreateStaticModelInstance(
        StaticModelPlacement placement,
        MapRenderStaticModelLightingAtlas lightingAtlas,
        int objectIndex,
        int surfaceIndex,
        string name,
        string authoredMaterialName,
        byte cameraRegion,
        int primaryLightIndex,
        byte reflectionProbeIndex,
        ushort lightingHandle,
        GfxColor groundLighting,
        byte flags)
    {
        ArgumentNullException.ThrowIfNull(lightingAtlas);
        if ((uint)objectIndex >=
            (uint)lightingAtlas.LightProbeAmbientRows.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(objectIndex),
                objectIndex,
                "Static-model light-probe ambient rows lost the draw-inst object index.");
        }

        return new MapRenderStaticModelInstance(
            new Vector4(placement.Axis0.X, placement.Axis1.X, placement.Axis2.X, placement.Origin.X),
            new Vector4(placement.Axis0.Z, placement.Axis1.Z, placement.Axis2.Z, placement.Origin.Z),
            new Vector4(-placement.Axis0.Y, -placement.Axis1.Y, -placement.Axis2.Y, -placement.Origin.Y),
            objectIndex,
            surfaceIndex,
            name,
            authoredMaterialName,
            cameraRegion,
            primaryLightIndex)
        {
            ReflectionProbeIndex = reflectionProbeIndex,
            // Row 0x39 is assigned from the renderer's visibility-driven
            // physical working set. Object indices are not atlas entries.
            BaseLightingCoords = Vector4.Zero,
            LightProbeAmbient =
                lightingAtlas.LightProbeAmbientRows[objectIndex],
            AuthoredLightingIdentity =
                new MapRenderStaticModelLightingIdentity(
                    lightingHandle,
                    groundLighting,
                    flags)
        };
    }

    private static MapRenderBounds TransformStaticInstanceBounds(
        MapRenderBounds localBounds,
        StaticModelPlacement placement)
    {
        if (!localBounds.IsValid)
            return MapRenderBounds.Empty;

        MapRenderBounds bounds = MapRenderBounds.Empty;

        for (int corner = 0; corner < 8; corner++)
        {
            Vector3 local = new(
                (corner & 1) == 0 ? localBounds.Min.X : localBounds.Max.X,
                (corner & 2) == 0 ? localBounds.Min.Y : localBounds.Max.Y,
                (corner & 4) == 0 ? localBounds.Min.Z : localBounds.Max.Z);
            Vector3 world = placement.Origin +
                            placement.Axis0 * local.X +
                            placement.Axis1 * local.Y +
                            placement.Axis2 * local.Z;
            bounds = bounds.Include(ToRenderCoordinates(world));
        }

        return bounds;
    }

}
