using IW4.Render.Techniques;
using System.Buffers;
using System.Numerics;
using IW4.Assets.Math;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Assets.Assets.XModel;
using IW4.Runtime.Assets.Images;

using IW4.Render.Assets;
using IW4.Render.EditorPreview;
using IW4.Render.Execution;
using IW4.Render.Geometry;
using IW4.Render.Geometry.XModel;
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
        IReadOnlyList<XModelLodGeometry> LodGeometries);

    private sealed record StaticModelTexturedBuildTarget(
        GfxDrawSurfSurfaceType SurfaceType,
        MapRenderTechniqueVariantAllocation TechniqueAllocation,
        bool AllowPreviewFallback,
        bool ForceGenericPreview,
        Dictionary<StaticTexturedBatchKey, InstancedTexturedBatchBuilder>
            Batches);

    private sealed record StaticModelTexturedBuildResult(
        int GenericFallbackSurfaceCount,
        int GenericFallbackTriangleCount,
        int AuthoredCandidateSurfaceCount,
        int AuthoredCandidateTriangleCount,
        IReadOnlyDictionary<int, RenderBounds> AllLodBounds,
        IReadOnlyDictionary<int, uint> RenderableLodMasks);

    private sealed class StaticModelTexturedBuildState(
        StaticModelTexturedBuildTarget target)
    {
        internal StaticModelTexturedBuildTarget Target { get; } = target;
        internal Dictionary<int, RenderBounds> AllLodBounds { get; } = [];
        internal Dictionary<int, uint> RenderableLodMasks { get; } = [];
        internal Dictionary<StaticTexturedDrawGroupKey, int> DrawGroupIds
            { get; } = [];
        internal HashSet<StaticTexturedBatchKey> FailedBatchKeys { get; } = [];
        internal List<(InstancedTexturedBatchBuilder Batch, bool IsFallback)>
            PreparedPassBatches { get; } = [];
        internal int NextPreparedBatchOrdinal { get; set; }
        internal int GenericFallbackSurfaceCount { get; set; }
        internal int GenericFallbackTriangleCount { get; set; }
        internal int AuthoredCandidateSurfaceCount { get; set; }
        internal int AuthoredCandidateTriangleCount { get; set; }

        internal StaticModelTexturedBuildResult CreateResult() => new(
            GenericFallbackSurfaceCount,
            GenericFallbackTriangleCount,
            AuthoredCandidateSurfaceCount,
            AuthoredCandidateTriangleCount,
            AllLodBounds,
            RenderableLodMasks);
    }

    /// <summary>
    /// Cached static-XSurface geometry is immutable after construction. The
    /// layout match uses the actual UV sources and RSX bindings rather than a
    /// material/pass identity, allowing all camera and receiver variants that
    /// consume the same vertex representation to share one decode.
    /// </summary>
    private sealed class StaticSurfaceGeometryEntry(
        IReadOnlyList<PreparedStaticColorLayer> colorLayers,
        IReadOnlyList<ShaderVertexInputBinding> rsxInputBindings,
        bool useGenericFallback,
        bool succeeded,
        List<float> vertices,
        List<float> rsxVertexInputs,
        bool rsxVertexInputsReady,
        string rsxVertexInputBlocker,
        List<uint> indices,
        RenderBounds localBounds)
    {
        private readonly MaterialStreamSource[] _colorUvSources =
            colorLayers
                .Select(layer => layer.Layer.UvRoute.TexCoordSource)
                .ToArray();
        private readonly ShaderVertexInputBinding[] _rsxInputBindings =
            rsxInputBindings.ToArray();

        internal bool Succeeded { get; } = succeeded;
        internal List<float> Vertices { get; } = vertices;
        internal List<float> RsxVertexInputs { get; } = rsxVertexInputs;
        internal bool RsxVertexInputsReady { get; } = rsxVertexInputsReady;
        internal string RsxVertexInputBlocker { get; } =
            rsxVertexInputBlocker;
        internal List<uint> Indices { get; } = indices;
        internal RenderBounds LocalBounds { get; } = localBounds;

        internal bool Matches(
            IReadOnlyList<PreparedStaticColorLayer> candidateColorLayers,
            IReadOnlyList<ShaderVertexInputBinding> candidateRsxBindings,
            bool candidateUsesGenericFallback)
        {
            if (useGenericFallback != candidateUsesGenericFallback ||
                _colorUvSources.Length != candidateColorLayers.Count ||
                _rsxInputBindings.Length != candidateRsxBindings.Count)
            {
                return false;
            }

            for (int index = 0; index < _colorUvSources.Length; index++)
            {
                if (_colorUvSources[index] !=
                    candidateColorLayers[index].Layer.UvRoute.TexCoordSource)
                {
                    return false;
                }
            }

            for (int index = 0; index < _rsxInputBindings.Length; index++)
            {
                if (_rsxInputBindings[index] != candidateRsxBindings[index])
                    return false;
            }

            return true;
        }
    }

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

        internal Dictionary<MaterialAsset, RenderState>
            SyntheticFallbackStates { get; } =
                new(ReferenceEqualityComparer.Instance);

        internal Dictionary<MaterialAsset, EditorMaterialTexturePlan>
            MaterialTexturePlans { get; } =
                new(ReferenceEqualityComparer.Instance);

        internal Dictionary<XSurface, List<StaticSurfaceGeometryEntry>>
            SurfaceGeometry { get; } =
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
            new IReadOnlyList<XModelLodGeometry>?[
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
                if (XModelLodGeometryCatalog.TryCreate(
                        uniqueModels[modelIndex],
                        out IReadOnlyList<
                            XModelLodGeometry>? lods))
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

    private static IReadOnlyList<RenderTextureDecodeRequest>
        PlanStaticModelPrimaryTextureDecodes(
            GfxWorldAsset gfxMap,
            IReadOnlyList<PreparedStaticModelSource?> preparedSources,
            RenderAssetLookup lookup,
            MapRenderDrawMethod? editorPreviewDrawMethod,
            MapRenderSceneLightSelectorAssetState? sceneLightSelector,
            StaticModelSharedBuildCache sharedCache)
    {
        var requests = new List<RenderTextureDecodeRequest>();
        var seen = new HashSet<RenderTextureCacheKey>();
        (GfxDrawSurfSurfaceType SurfaceType,
            MapRenderTechniqueVariantAllocation Allocation,
            bool AllowPreviewFallback,
            bool ForceGenericPreview)[] variants =
        [
            (
                GfxDrawSurfSurfaceType.StaticModelRigid,
                MapRenderTechniqueVariantAllocation.Unshadowed,
                true,
                false),
            (
                GfxDrawSurfSurfaceType.StaticModelRigid,
                MapRenderTechniqueVariantAllocation.Unshadowed,
                true,
                true),
            (
                GfxDrawSurfSurfaceType.StaticModelRigid,
                MapRenderTechniqueVariantAllocation.ShadowMapAllocated,
                false,
                false),
            (
                GfxDrawSurfSurfaceType.StaticModelRigidNoSunShadow,
                MapRenderTechniqueVariantAllocation.Unshadowed,
                false,
                false),
            (
                GfxDrawSurfSurfaceType.StaticModelRigidNoSunShadow,
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

                foreach (XModelLodGeometry lodGeometry in
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

                            RenderTextureDecodeRequest request =
                                RenderTextureDecodeRequest.Create(
                                    selectedPass.Image,
                                    selectedPass.Texture.SamplerState,
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
        ref RenderBounds bounds,
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

        for (int drawInstIndex = 0;
             drawInstIndex < gfxMap.Dpvs.SModelDrawInsts.Count;
             drawInstIndex++)
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
            RenderBounds instanceBounds = RenderBounds.Empty;
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
                            out RenderBounds localBounds))
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
                    material?.CameraRegion ?? (GfxCameraRegionType)byte.MaxValue,
                    drawInst.PrimaryLightIndex,
                    drawInst.ReflectionProbeIndex,
                    drawInst.LightingHandle,
                    drawInst.GroundLighting,
                    drawInst.Flags));
                RenderBounds transformedBounds =
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
        out RenderBounds localBounds)
    {
        int sourceVertexCount = surface.VertCount;
        int retainedVertexCapacity = Math.Min(
            sourceVertexCount,
            checked(surface.TriCount * 3));
        var builtVertices = new List<float>(checked(
            retainedVertexCapacity * MapRenderScene.VertexFloatCount));
        vertices = builtVertices;
        indices = new List<uint>(surface.TriCount * 3);
        skippedTriangles = 0;
        readFailureTriangles = 0;
        localBounds = RenderBounds.Empty;

        if (sourceVertexCount <= 0)
            return false;

        int[] remappedIndices =
            ArrayPool<int>.Shared.Rent(sourceVertexCount);
        Vector3[] decodedPositions =
            ArrayPool<Vector3>.Shared.Rent(sourceVertexCount);
        remappedIndices.AsSpan(0, sourceVertexCount).Fill(-2);
        try
        {
            for (int triangle = 0; triangle < surface.TriCount; triangle++)
            {
                int indexOffset = triangle * 3;
                if (indexOffset < 0 ||
                    indexOffset + 2 >= surface.TriIndices.Count)
                {
                    skippedTriangles++;
                    readFailureTriangles++;
                    continue;
                }

                int i0 = surface.TriIndices[indexOffset];
                int i1 = surface.TriIndices[indexOffset + 1];
                int i2 = surface.TriIndices[indexOffset + 2];
                if (!TryMaterializeVertex(i0, out uint output0, out Vector3 p0) ||
                    !TryMaterializeVertex(i1, out uint output1, out Vector3 p1) ||
                    !TryMaterializeVertex(i2, out uint output2, out Vector3 p2))
                {
                    skippedTriangles++;
                    readFailureTriangles++;
                    continue;
                }

                // Retain the XSurface's index sharing. The previous path
                // expanded every triangle into three duplicate vertices,
                // repeating native vertex decode for every adjacency.
                indices.Add(output0);
                indices.Add(output1);
                indices.Add(output2);
                localBounds = localBounds
                    .Include(p0)
                    .Include(p1)
                    .Include(p2);
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(remappedIndices);
            ArrayPool<Vector3>.Shared.Return(decodedPositions);
        }

        return indices.Count > 0;

        bool TryMaterializeVertex(
            int sourceIndex,
            out uint outputIndex,
            out Vector3 position)
        {
            outputIndex = 0;
            position = default;
            if ((uint)sourceIndex >= (uint)sourceVertexCount)
                return false;

            int remapped = remappedIndices[sourceIndex];
            if (remapped == -1)
                return false;
            if (remapped >= 0)
            {
                outputIndex = checked((uint)remapped);
                position = decodedPositions[sourceIndex];
                return true;
            }

            if (!XSurfaceVertexDecoder.TryReadPosition(
                    surface,
                    sourceIndex,
                    out position))
            {
                remappedIndices[sourceIndex] = -1;
                return false;
            }

            outputIndex = checked((uint)(
                builtVertices.Count / MapRenderScene.VertexFloatCount));
            AddVertex(builtVertices, position, color);
            decodedPositions[sourceIndex] = position;
            remappedIndices[sourceIndex] = checked((int)outputIndex);
            return true;
        }
    }

    private static IReadOnlyList<StaticModelTexturedBuildResult>
        AddStaticModelTexturedGeometryVariants(
            GfxWorldAsset gfxMap,
            IReadOnlyList<PreparedStaticModelSource?> preparedSources,
            StaticModelSharedBuildCache sharedCache,
            MapRenderStaticModelLightingAtlas lightingAtlas,
            RenderAssetLookup lookup,
            IMapRenderWorldTextureBindingResolver worldTextureBindings,
            MapRenderDrawMethod? editorPreviewDrawMethod,
            MapRenderSceneLightSelectorAssetState? sceneLightSelector,
            IGfxImagePayloadResolver imageStreams,
            IReadOnlyList<StaticModelTexturedBuildTarget> targets,
            RenderTextureCache textureCache,
            MapRenderWorldTextureCache worldTextureCache,
            HashSet<RenderTextureCacheKey> failedTextureCacheKeys,
            HashSet<MapRenderWorldTextureCacheKey> failedWorldTextureCacheKeys,
            ShaderTranslationCache shaderTranslationCache,
            Action<string>? reportProgress,
            ref int textureDecodedCount,
            ref int textureDecodeSkippedCount)
    {
        ArgumentNullException.ThrowIfNull(preparedSources);
        ArgumentNullException.ThrowIfNull(sharedCache);
        ArgumentNullException.ThrowIfNull(shaderTranslationCache);
        ArgumentNullException.ThrowIfNull(targets);
        if (preparedSources.Count != gfxMap.Dpvs.SModelDrawInsts.Count)
        {
            throw new ArgumentException(
                "Prepared static-model source count must match the world draw-instance count.",
                nameof(preparedSources));
        }
        if (targets.Count == 0)
            return [];

        StaticModelTexturedBuildState[] states = targets
            .Select(target =>
            {
                ArgumentNullException.ThrowIfNull(target);
                ArgumentNullException.ThrowIfNull(target.Batches);
                return new StaticModelTexturedBuildState(target);
            })
            .ToArray();
        var pageTechniqueSlots = new int?[states.Length];
        var renderReadySurfaceCounts = new int[states.Length];
        for (int drawInstIndex = 0;
             drawInstIndex < gfxMap.Dpvs.SModelDrawInsts.Count;
             drawInstIndex++)
        {
            if (drawInstIndex != 0 && (drawInstIndex & 0xff) == 0)
            {
                reportProgress?.Invoke(
                    $"building static-model variants: {drawInstIndex}/{gfxMap.Dpvs.SModelDrawInsts.Count} instances");
            }

            GfxStaticModelDrawInst drawInst = gfxMap.Dpvs.SModelDrawInsts[drawInstIndex];
            // PrimaryLightIndex is authored per instance. Keep its exact
            // all-clear selector identity through pass fallback and batching;
            // otherwise a shared XModel/material can silently inherit another
            // instance's base-light or directional-sun technique.
            if (preparedSources[drawInstIndex] is not { } preparedSource)
                continue;

            for (int stateIndex = 0;
                 stateIndex < states.Length;
                 stateIndex++)
            {
                StaticModelTexturedBuildTarget target =
                    states[stateIndex].Target;
                pageTechniqueSlots[stateIndex] =
                    ResolvePreparedEditorTechniqueVariantSlot(
                        drawInst.PrimaryLightIndex,
                        target.SurfaceType,
                        editorPreviewDrawMethod,
                        sceneLightSelector,
                        target.TechniqueAllocation);
            }

            StaticModelPlacement placement = preparedSource.Placement;
            XModelAsset model = preparedSource.Model;
            IReadOnlyList<XModelLodGeometry> lodGeometries =
                preparedSource.LodGeometries;

            int preparedLodIndex = lodGeometries[0].LodIndex;
            foreach (XModelLodGeometry lodGeometry in
                     lodGeometries)
            {
                int lodIndex = lodGeometry.LodIndex;
                bool isPreparedLod = lodIndex == preparedLodIndex;
                XModelSurfsAsset modelSurfs = lodGeometry.ModelSurfs;
                Array.Clear(
                    renderReadySurfaceCounts,
                    0,
                    renderReadySurfaceCounts.Length);
                for (int surfaceOffset = 0;
                     surfaceOffset < lodGeometry.SurfaceCount;
                     surfaceOffset++)
                {
                    int materialSurfaceIndex =
                        lodGeometry.MaterialSurfaceStart + surfaceOffset;
                    XSurface surface = modelSurfs.Surfaces[surfaceOffset];
                    MaterialAsset? material =
                        SelectStaticModelSurfaceMaterial(
                            model,
                            materialSurfaceIndex);
                    if (material is null)
                        continue;

                    MapRenderStaticModelInstance instance =
                        CreateStaticModelInstance(
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

                    for (int stateIndex = 0;
                         stateIndex < states.Length;
                         stateIndex++)
                    {
                        StaticModelTexturedBuildState state =
                            states[stateIndex];
                        StaticModelTexturedBuildTarget target =
                            state.Target;
                        Dictionary<StaticTexturedBatchKey,
                            InstancedTexturedBatchBuilder> batches =
                            target.Batches;

                        int? surfaceTechniqueSlot =
                            MapRenderOpenGlStaticCameraRegionPolicy
                                .ResolveNormalCameraTechniqueSlot(
                                    material.CameraRegion,
                                    pageTechniqueSlots[stateIndex],
                                    editorPreviewDrawMethod);
                        if (!target.AllowPreviewFallback &&
                            surfaceTechniqueSlot is null)
                        {
                            continue;
                        }

                        IReadOnlyList<StaticMaterialSelection> selections =
                            ResolveStaticMaterialSelections(
                                material,
                                surfaceTechniqueSlot,
                                target.AllowPreviewFallback,
                                target.ForceGenericPreview,
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
                                new MaterialCustomSamplerSelection(
                                        pass.Pass.TechniquePass.CustomSamplerFlags)
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
                        if (!state.DrawGroupIds.TryGetValue(
                                editorDrawGroupKey,
                                out int editorDrawGroupId))
                        {
                            editorDrawGroupId = state.DrawGroupIds.Count;
                            state.DrawGroupIds.Add(
                                editorDrawGroupKey,
                                editorDrawGroupId);
                        }

                        List<(InstancedTexturedBatchBuilder Batch, bool IsFallback)>
                            preparedPassBatches = state.PreparedPassBatches;
                        preparedPassBatches.Clear();
                        preparedPassBatches.EnsureCapacity(selections.Count);
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
                                selectedPass.Pass.TechniquePass.TechniqueSlot,
                                selectedPass.Pass.TechniquePass.PassIndex,
                                selectedPass.PrimarySampler.SamplerArgIndex,
                                selectedPass.PrimarySampler.SamplerHash,
                                reflectionProbeBatchIndex,
                                drawInst.PrimaryLightIndex);
                            if (state.FailedBatchKeys.Contains(batchKey))
                            {
                                selectedGroupReady = false;
                                break;
                            }

                            if (!batches.TryGetValue(
                                    batchKey,
                                    out InstancedTexturedBatchBuilder? batch))
                            {
                                if (!TryDecodeTexture(
                                        selectedPass.Image,
                                        selectedPass.Texture.SamplerState,
                                        imageStreams,
                                        textureCache,
                                        failedTextureCacheKeys,
                                        true,
                                        ref textureDecodedCount,
                                        ref textureDecodeSkippedCount,
                                        out Texture? texture) ||
                                    texture is null)
                                {
                                    state.FailedBatchKeys.Add(batchKey);
                                    selectedGroupReady = false;
                                    break;
                                }

                                UvRoute uvRoute =
                                    BuildStaticModelUvRoute(
                                        selectedPass.TexCoordSource);
                                EditorMaterialTexturePlan? texturePlan = null;
                                if (!sharedCache.MaterialTexturePlans.TryGetValue(
                                        material,
                                        out texturePlan))
                                {
                                    texturePlan = EditorMaterialTexturePlanner.Plan(
                                        material.Textures,
                                        (_, row) =>
                                            new EditorMaterialTextureResolution(
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
                                ShaderVertexInputBinding[]
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
                                ShaderVertexInputBinding[]
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
                                        out ShaderVertexInputBinding[]
                                            materializedVertexInputs,
                                        out string depthPrepassVertexInputBlocker);
                                StaticSurfaceGeometryEntry surfaceGeometry =
                                    GetOrBuildTexturedStaticXSurfaceLocal(
                                        sharedCache,
                                        surface,
                                        preparedStaticColorLayers,
                                        materializedVertexInputs,
                                        useGenericFallback:
                                            !hasAuthoredSourcePass);
                                if (!surfaceGeometry.Succeeded)
                                {
                                    state.FailedBatchKeys.Add(batchKey);
                                    selectedGroupReady = false;
                                    break;
                                }

                                List<float> surfaceVertices =
                                    surfaceGeometry.Vertices;
                                List<float> surfaceRsxVertexInputs =
                                    surfaceGeometry.RsxVertexInputs;
                                bool surfaceRsxVertexInputsReady =
                                    surfaceGeometry.RsxVertexInputsReady;
                                string surfaceRsxVertexInputBlocker =
                                    surfaceGeometry.RsxVertexInputBlocker;
                                List<uint> surfaceIndices = surfaceGeometry.Indices;
                                RenderBounds localBounds = surfaceGeometry.LocalBounds;

                                IReadOnlyList<MaterialColorLayer> staticColorLayers =
                                    preparedStaticColorLayers
                                        .Select(layer => layer.Layer)
                                        .ToArray();
                                IReadOnlyList<
                                    MapRenderWorldMaterialSamplerBinding>
                                    samplerBindings =
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
                                        worldTextureCache,
                                        failedTextureCacheKeys,
                                        failedWorldTextureCacheKeys,
                                        ref textureDecodedCount,
                                        ref textureDecodeSkippedCount);
                                ShaderExecutionContract shaderExecution =
                                    BuildShaderExecutionContract(
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
                                ShaderExecutionContract?
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
                                    ShaderExecutionContract candidate =
                                        BuildShaderExecutionContract(
                                            material,
                                            techset,
                                            lookup,
                                            depthPrepassSelection,
                                            Array.Empty<MaterialSamplerBinding>(),
                                            depthPayloadReady,
                                            depthPayloadBlocker,
                                            authoredSourcePassAvailable: true,
                                            purpose:
                                                ShaderExecutionPurpose
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
                                RenderState renderState = selectedPass.State;
                                batch = new InstancedTexturedBatchBuilder(
                                    lodIndex,
                                    selectedPass.Pass,
                                    selectedPass.PrimarySampler,
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

                        RenderBounds transformedBounds =
                            TransformStaticInstanceBounds(
                                preparedPassBatches[0].Batch.LocalBounds,
                                placement);
                        if (transformedBounds.IsValid)
                        {
                            bool hasAccumulatedBounds =
                                state.AllLodBounds.TryGetValue(
                                    drawInstIndex,
                                    out RenderBounds accumulatedBounds);
                            state.AllLodBounds[drawInstIndex] =
                                hasAccumulatedBounds
                                    ? IncludeBounds(
                                        accumulatedBounds,
                                        transformedBounds)
                                    : transformedBounds;
                        }
                        renderReadySurfaceCounts[stateIndex]++;
                        foreach (var preparedPassBatch in preparedPassBatches)
                        {
                            preparedPassBatch.Batch.Instances.Add(instance);
                            if (isPreparedLod)
                            {
                                if (preparedPassBatch.Batch.PreparedInstances.Count ==
                                    0)
                                {
                                    preparedPassBatch.Batch.PreparedSourceOrdinal =
                                        state.NextPreparedBatchOrdinal++;
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
                            state.GenericFallbackSurfaceCount++;
                            state.GenericFallbackTriangleCount +=
                                surfaceTriangleCount;
                        }
                        else
                        {
                            state.AuthoredCandidateSurfaceCount++;
                            state.AuthoredCandidateTriangleCount +=
                                surfaceTriangleCount;
                        }
                    }
                }

                for (int stateIndex = 0;
                     stateIndex < states.Length;
                     stateIndex++)
                {
                    if (renderReadySurfaceCounts[stateIndex] !=
                        lodGeometry.SurfaceCount)
                    {
                        continue;
                    }

                    StaticModelTexturedBuildState state =
                        states[stateIndex];
                    state.RenderableLodMasks.TryGetValue(
                        drawInstIndex,
                        out uint renderableLodMask);
                    state.RenderableLodMasks[drawInstIndex] =
                        renderableLodMask | (1u << lodIndex);
                }
            }
        }

        return states.Select(state => state.CreateResult()).ToArray();
    }

    private static IReadOnlyDictionary<int, RenderBounds>
        MergeStaticModelBounds(
            IReadOnlyDictionary<int, RenderBounds> first,
            IReadOnlyDictionary<int, RenderBounds> second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        Dictionary<int, RenderBounds> merged =
            first.ToDictionary(entry => entry.Key, entry => entry.Value);
        foreach ((int objectIndex, RenderBounds bounds) in second)
        {
            merged[objectIndex] = merged.TryGetValue(
                objectIndex,
                out RenderBounds existing)
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

    private static StaticSurfaceGeometryEntry
        GetOrBuildTexturedStaticXSurfaceLocal(
            StaticModelSharedBuildCache sharedCache,
            XSurface surface,
            IReadOnlyList<PreparedStaticColorLayer> colorLayers,
            IReadOnlyList<ShaderVertexInputBinding> rsxInputBindings,
            bool useGenericFallback)
    {
        if (!sharedCache.SurfaceGeometry.TryGetValue(
                surface,
                out List<StaticSurfaceGeometryEntry>? cachedLayouts))
        {
            cachedLayouts = [];
            sharedCache.SurfaceGeometry.Add(surface, cachedLayouts);
        }

        foreach (StaticSurfaceGeometryEntry cached in cachedLayouts)
        {
            if (cached.Matches(
                    colorLayers,
                    rsxInputBindings,
                    useGenericFallback))
            {
                return cached;
            }
        }

        bool succeeded = TryBuildTexturedStaticXSurfaceLocal(
            surface,
            colorLayers,
            rsxInputBindings,
            out List<float> vertices,
            out List<float> rsxVertexInputs,
            out bool rsxVertexInputsReady,
            out string rsxVertexInputBlocker,
            out List<uint> indices,
            out RenderBounds localBounds,
            useGenericFallback);
        var created = new StaticSurfaceGeometryEntry(
            colorLayers,
            rsxInputBindings,
            useGenericFallback,
            succeeded,
            vertices,
            rsxVertexInputs,
            rsxVertexInputsReady,
            rsxVertexInputBlocker,
            indices,
            localBounds);
        cachedLayouts.Add(created);
        return created;
    }

    private static bool TryBuildTexturedStaticXSurfaceLocal(
        XSurface surface,
        IReadOnlyList<PreparedStaticColorLayer> colorLayers,
        IReadOnlyList<ShaderVertexInputBinding> rsxInputBindings,
        out List<float> vertices,
        out List<float> rsxVertexInputs,
        out bool rsxVertexInputsReady,
        out string rsxVertexInputBlocker,
        out List<uint> indices,
        out RenderBounds localBounds,
        bool useGenericFallback)
    {
        int sourceVertexCount = surface.VertCount;
        int retainedVertexCapacity = Math.Min(
            sourceVertexCount,
            checked(surface.TriCount * 3));
        var builtVertices = new List<float>(checked(
            retainedVertexCapacity *
            MapRenderScene.TexturedVertexFloatCount));
        vertices = builtVertices;
        bool materializeRsxVertexInputs = rsxInputBindings.Count > 0 ||
            useGenericFallback;
        var builtRsxVertexInputs = materializeRsxVertexInputs
            ? new List<float>(checked(
                retainedVertexCapacity *
                RsxVertexInputCount *
                RsxVertexInputComponentCount))
            : [];
        rsxVertexInputs = builtRsxVertexInputs;
        bool payloadReadyState = materializeRsxVertexInputs;
        Dictionary<int, string>? rsxVertexFailures = null;
        SortedSet<string>? rsxVertexInputFailures = null;
        rsxVertexInputsReady = payloadReadyState;
        rsxVertexInputBlocker = string.Empty;
        indices = new List<uint>(surface.TriCount * 3);
        localBounds = RenderBounds.Empty;

        int colorLayerCount = Math.Min(
            colorLayers.Count,
            MapRenderScene.MaxColorLayerCount);
        if (sourceVertexCount <= 0 || colorLayerCount == 0)
            return false;

        int[] remappedIndices =
            ArrayPool<int>.Shared.Rent(sourceVertexCount);
        Vector3[] decodedPositions =
            ArrayPool<Vector3>.Shared.Rent(sourceVertexCount);
        remappedIndices.AsSpan(0, sourceVertexCount).Fill(-2);
        var preparedLayerUvs = new Vector2[colorLayerCount];
        var preparedRsxVertexInputs = materializeRsxVertexInputs
            ? new Vector4[RsxVertexInputCount]
            : [];
        try
        {
            for (int triangle = 0; triangle < surface.TriCount; triangle++)
            {
                int indexOffset = triangle * 3;
                if (indexOffset < 0 ||
                    indexOffset + 2 >= surface.TriIndices.Count)
                {
                    continue;
                }

                int i0 = surface.TriIndices[indexOffset];
                int i1 = surface.TriIndices[indexOffset + 1];
                int i2 = surface.TriIndices[indexOffset + 2];
                if (!TryMaterializeVertex(i0, out uint output0, out Vector3 p0) ||
                    !TryMaterializeVertex(i1, out uint output1, out Vector3 p1) ||
                    !TryMaterializeVertex(i2, out uint output2, out Vector3 p2))
                {
                    continue;
                }

                // Match the former triangle-expanded diagnostic contract:
                // an RSX payload failure is observable only when all three
                // geometry vertices are valid and the triangle is retained.
                RecordRsxVertexFailure(i0);
                RecordRsxVertexFailure(i1);
                RecordRsxVertexFailure(i2);
                indices.Add(output0);
                indices.Add(output1);
                indices.Add(output2);
                localBounds = localBounds
                    .Include(p0)
                    .Include(p1)
                    .Include(p2);
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(remappedIndices);
            ArrayPool<Vector3>.Shared.Return(decodedPositions);
        }

        if (!payloadReadyState ||
            builtRsxVertexInputs.Count != checked(
                (builtVertices.Count /
                    MapRenderScene.TexturedVertexFloatCount) *
                RsxVertexInputCount *
                RsxVertexInputComponentCount))
        {
            payloadReadyState = false;
            builtRsxVertexInputs.Clear();
        }
        string payloadBlocker = !materializeRsxVertexInputs
            ? "RSX_VERTEX_INPUT_PAYLOAD_NOT_AVAILABLE_FOR_GENERIC_FALLBACK"
            : payloadReadyState
                ? string.Empty
                : rsxVertexInputFailures is null
                    ? "STATIC_XSURFACE_RSX_VERTEX_INPUT_PAYLOAD_COUNT_MISMATCH"
                    : string.Join('|', rsxVertexInputFailures);
        rsxVertexInputsReady = payloadReadyState;
        rsxVertexInputBlocker = payloadBlocker;

        return indices.Count > 0;

        bool TryMaterializeVertex(
            int sourceIndex,
            out uint outputIndex,
            out Vector3 position)
        {
            outputIndex = 0;
            position = default;
            if ((uint)sourceIndex >= (uint)sourceVertexCount)
                return false;

            int remapped = remappedIndices[sourceIndex];
            if (remapped == -1)
                return false;
            if (remapped >= 0)
            {
                outputIndex = checked((uint)remapped);
                position = decodedPositions[sourceIndex];
                return true;
            }

            if (!XSurfaceVertexDecoder.TryReadPosition(
                    surface,
                    sourceIndex,
                    out position) ||
                !TryReadStaticLayerUvs(
                    surface,
                    sourceIndex,
                    colorLayers,
                    preparedLayerUvs))
            {
                remappedIndices[sourceIndex] = -1;
                return false;
            }

            colorLayers[0].Decoder.TryReadNormal(
                surface,
                sourceIndex,
                out Vector3 normal);
            outputIndex = checked((uint)(
                builtVertices.Count /
                MapRenderScene.TexturedVertexFloatCount));
            AddTexturedVertex(
                builtVertices,
                position,
                preparedLayerUvs[0],
                preparedLayerUvs,
                default,
                normal);
            decodedPositions[sourceIndex] = position;
            remappedIndices[sourceIndex] = checked((int)outputIndex);

            if (!materializeRsxVertexInputs)
                return true;

            bool payloadReady = useGenericFallback
                ? TryBuildGenericRsxVertexInputs(
                    preparedRsxVertexInputs,
                    position,
                    preparedLayerUvs[0],
                    out string blocker)
                : TryReadStaticRsxVertexInputs(
                    surface,
                    sourceIndex,
                    rsxInputBindings,
                    preparedRsxVertexInputs,
                    out blocker);
            if (!payloadReady)
            {
                (rsxVertexFailures ??= []).TryAdd(
                    sourceIndex,
                    $"vertex{sourceIndex}:{blocker}");
            }

            // Keep one row for every compacted geometry vertex. If this
            // vertex is later referenced by a retained triangle, its recorded
            // blocker makes the entire payload unavailable and the rows are
            // cleared after traversal. Failed orphan vertices stay harmlessly
            // aligned and do not alter the former diagnostic contract.
            AddRsxVertexInputs(
                builtRsxVertexInputs,
                preparedRsxVertexInputs);
            return true;
        }

        void RecordRsxVertexFailure(int sourceIndex)
        {
            if (rsxVertexFailures is null ||
                !rsxVertexFailures.TryGetValue(
                    sourceIndex,
                    out string? failure) ||
                failure is null)
            {
                return;
            }

            payloadReadyState = false;
            (rsxVertexInputFailures ??=
                new SortedSet<string>(StringComparer.Ordinal)).Add(failure);
        }
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
        XSurface surface,
        int vertexIndex,
        IReadOnlyList<ShaderVertexInputBinding> bindings,
        Span<Vector4> values,
        out string blocker)
    {
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
        Span<Vector2> layerUvs)
    {
        int layerCount = Math.Min(
            colorLayers.Count,
            MapRenderScene.MaxColorLayerCount);
        if (layerUvs.Length < layerCount)
        {
            throw new ArgumentException(
                "Static UV destination is smaller than the prepared color-layer count.",
                nameof(layerUvs));
        }

        for (int layerIndex = 0;
             layerIndex < layerCount;
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
                return false;
            }
        }

        return layerCount > 0;
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
        return new PackedSigned11_11_10(packed).DecodePlacement();
    }

    private static MapRenderStaticModelInstance CreateStaticModelInstance(
        StaticModelPlacement placement,
        MapRenderStaticModelLightingAtlas lightingAtlas,
        int objectIndex,
        int surfaceIndex,
        string name,
        string authoredMaterialName,
        GfxCameraRegionType cameraRegion,
        int primaryLightIndex,
        byte reflectionProbeIndex,
        ushort lightingHandle,
        GfxColor groundLighting,
        GfxStaticModelDrawInstFlags flags)
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

    private static RenderBounds TransformStaticInstanceBounds(
        RenderBounds localBounds,
        StaticModelPlacement placement)
    {
        if (!localBounds.IsValid)
            return RenderBounds.Empty;

        RenderBounds bounds = RenderBounds.Empty;

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
