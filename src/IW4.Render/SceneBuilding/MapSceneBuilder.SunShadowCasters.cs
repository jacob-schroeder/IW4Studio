using System.Numerics;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Assets.Assets.XModel;
using IW4.Runtime.Assets.Images;
using IW4.Render.Assets;
using IW4.Render.Geometry;
using IW4.Render.Geometry.Shadows;
using IW4.Render.Lighting;
using IW4.Render.Materials;
using IW4.Render.Scheduling.Shadows;
using IW4.Render.Scheduling.StaticModels;
using IW4.Render.Textures;

namespace IW4.Render.SceneBuilding;

public sealed partial class MapSceneBuilder
{
    private sealed class StaticSunShadowCasterBatchBuilder(
        XModelAsset model,
        XModelLodInfo lod,
        int lodIndex,
        XSurface surface,
        int surfaceOffset,
        int materialSurfaceIndex,
        MapRenderSunShadowStaticMaterialEligibility materialEligibility,
        MapRenderSunShadowCasterMaterialPlan material,
        MapRenderSunShadowCasterGeometry geometry,
        MapRenderUvRoute? cutoutUvRoute,
        MapRenderTexture? cutoutTexture)
    {
        internal XModelAsset Model { get; } = model;
        internal XModelLodInfo Lod { get; } = lod;
        internal int LodIndex { get; } = lodIndex;
        internal XSurface Surface { get; } = surface;
        internal int SurfaceOffset { get; } = surfaceOffset;
        internal int MaterialSurfaceIndex { get; } = materialSurfaceIndex;
        internal MapRenderSunShadowStaticMaterialEligibility
            MaterialEligibility { get; } = materialEligibility;
        internal MapRenderSunShadowCasterMaterialPlan Material { get; } =
            material;
        internal MapRenderSunShadowCasterGeometry Geometry { get; } =
            geometry;
        internal MapRenderUvRoute? CutoutUvRoute { get; } = cutoutUvRoute;
        internal MapRenderTexture? CutoutTexture { get; } = cutoutTexture;
        internal List<MapRenderSunShadowStaticCasterInstance> Instances
            { get; } = [];

        internal MapRenderStaticSunShadowCasterBatch Materialize() => new(
            Model,
            Lod,
            LodIndex,
            Surface,
            SurfaceOffset,
            MaterialSurfaceIndex,
            MaterialEligibility,
            Material,
            Geometry,
            CutoutUvRoute,
            CutoutTexture,
            Instances);
    }

    private readonly record struct StaticSunShadowCasterBatchKey(
        XModelAsset Model,
        int LodIndex,
        int SurfaceOffset,
        int MaterialSurfaceIndex,
        XSurface Surface,
        MaterialAsset Material);

    private static IReadOnlyList<MapRenderWorldSunShadowCasterBatch>
        BuildWorldSunShadowCasterBatches(
            GfxWorldAsset world,
            IReadOnlyList<PreparedWorldSurfaceGeometry> preparedSurfaces,
            RenderAssetLookup lookup,
            IGfxImagePayloadResolver imageStreams,
            MapRenderTextureCache
                textureCache,
            HashSet<MapRenderTextureCacheKey> failedTextureCacheKeys,
            ref int decodedTextureCount,
            ref int skippedTextureCount,
            out IReadOnlyList<
                MapRenderSunShadowWorldCasterRejection> rejections)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(preparedSurfaces);
        ArgumentNullException.ThrowIfNull(lookup);
        if (preparedSurfaces.Count != world.Dpvs.Surfaces.Count)
        {
            throw new ArgumentException(
                "Prepared world geometry must remain index-parallel with GfxWorld.dpvs.surfaces.",
                nameof(preparedSurfaces));
        }

        var result = new List<MapRenderWorldSunShadowCasterBatch>();
        var rejected = new List<
            MapRenderSunShadowWorldCasterRejection>();
        for (int surfaceIndex = 0;
             surfaceIndex < world.Dpvs.Surfaces.Count;
             surfaceIndex++)
        {
            GfxSurface surface = world.Dpvs.Surfaces[surfaceIndex];
            MaterialAsset? material = surface.Material ??
                lookup.ResolveMaterial(surface.MaterialPointer);
            if (material is null)
            {
                rejected.Add(new(
                    surfaceIndex,
                    MapRenderSunShadowWorldCasterRejectionKind
                        .MaterialUnavailable,
                    MaterialFailure: null,
                    "The canonical world material is unavailable."));
                continue;
            }

            MapRenderSunShadowCasterMaterialPlanResult planResult =
                MapRenderSunShadowCasterMaterialPlanner.Plan(
                    material,
                    lookup);
            if (planResult.Plan is not { } plan)
            {
                MaterialTechniqueSetAsset? techniqueSet =
                    material.TechniqueSet ??
                    lookup.ResolveTechniqueSet(
                        material.TechniqueSetPointer);
                bool nativeNullSlot =
                    planResult.Failure?.Kind ==
                        MapRenderSunShadowCasterMaterialFailureKind
                            .Slot2TechniqueUnavailable &&
                    techniqueSet is not null &&
                    MapRenderSunShadowCasterMaterialPlanner
                        .IsNativeNullSlot2Rejection(techniqueSet);
                rejected.Add(new(
                    surfaceIndex,
                    nativeNullSlot
                        ? MapRenderSunShadowWorldCasterRejectionKind
                            .NativeNullTechniqueSlot
                        : MapRenderSunShadowWorldCasterRejectionKind
                            .MaterialContractUnavailable,
                    planResult.Failure,
                    nativeNullSlot
                        ? "Native slot-2 selector rejected a literal null technique cell before draw setup."
                        : planResult.Failure?.Detail ??
                            "The exact slot-2 material contract is unavailable."));
                continue;
            }
            if (!TryPrepareSunShadowCasterPayload(
                    world,
                    surface,
                    preparedSurfaces[surfaceIndex],
                    plan,
                    imageStreams,
                    textureCache,
                    failedTextureCacheKeys,
                    ref decodedTextureCount,
                    ref skippedTextureCount,
                    out MapRenderSunShadowCasterGeometry? geometry,
                    out MapRenderUvRoute? cutoutUvRoute,
                    out MapRenderTexture? cutoutTexture))
            {
                rejected.Add(new(
                    surfaceIndex,
                    MapRenderSunShadowWorldCasterRejectionKind
                        .PayloadUnavailable,
                    MaterialFailure: null,
                    "The exact slot-2 geometry, UV route, or texture payload could not be prepared."));
                continue;
            }

            result.Add(new MapRenderWorldSunShadowCasterBatch(
                surfaceIndex,
                surface,
                plan,
                geometry!,
                cutoutUvRoute,
                cutoutTexture));
        }

        rejections = Array.AsReadOnly(rejected.ToArray());
        return Array.AsReadOnly(result.ToArray());
    }

    private static IReadOnlyList<MapRenderStaticSunShadowCasterBatch>
        BuildStaticSunShadowCasterBatches(
            GfxWorldAsset world,
            MapRenderStaticModelLightingAtlas lightingAtlas,
            RenderAssetLookup lookup,
            IGfxImagePayloadResolver imageStreams,
            MapRenderTextureCache
                textureCache,
            HashSet<MapRenderTextureCacheKey> failedTextureCacheKeys,
            ref int decodedTextureCount,
            ref int skippedTextureCount,
            out IReadOnlyList<
                MapRenderSunShadowStaticCasterExpectation> expectations)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(lookup);

        var materialPlans = new Dictionary<MaterialAsset,
            MapRenderSunShadowCasterMaterialPlan?>(
                ReferenceEqualityComparer.Instance);
        var lodGeometryCache = new Dictionary<XModelAsset,
            IReadOnlyList<MapRenderStaticModelLodGeometry>>(
                ReferenceEqualityComparer.Instance);
        var invalidModels = new HashSet<XModelAsset>(
            ReferenceEqualityComparer.Instance);
        var builders = new Dictionary<
            StaticSunShadowCasterBatchKey,
            StaticSunShadowCasterBatchBuilder>();
        var expectationRows = new List<
            MapRenderSunShadowStaticCasterExpectation>();
        MapRenderSunShadowCasterMaterialPlan? sharedModelCasterPlan =
            MapRenderSunShadowCasterMaterialPlanner
                .PlanFixedStaticModelCaster(lookup).Plan;

        for (int drawInstIndex = 0;
             drawInstIndex < world.Dpvs.SModelDrawInsts.Count;
             drawInstIndex++)
        {
            GfxStaticModelDrawInst drawInst =
                world.Dpvs.SModelDrawInsts[drawInstIndex];
            if (!TryDecodePackedPlacement(
                    drawInst.Placement,
                    out StaticModelPlacement placement) ||
                drawInst.Model is not { } model ||
                invalidModels.Contains(model))
            {
                continue;
            }

            if (!lodGeometryCache.TryGetValue(
                    model,
                    out IReadOnlyList<MapRenderStaticModelLodGeometry>?
                        lodGeometries))
            {
                if (!MapRenderStaticModelLodGeometryCatalog.TryCreate(
                        model,
                        out lodGeometries))
                {
                    invalidModels.Add(model);
                    continue;
                }
                lodGeometryCache.Add(model, lodGeometries);
            }

            foreach (MapRenderStaticModelLodGeometry lodGeometry in
                     lodGeometries)
            {
                for (int surfaceOffset = 0;
                     surfaceOffset < lodGeometry.SurfaceCount;
                     surfaceOffset++)
                {
                    int materialSurfaceIndex = checked(
                        lodGeometry.MaterialSurfaceStart + surfaceOffset);
                    MaterialAsset? material =
                        SelectStaticModelSurfaceMaterial(
                            model,
                            materialSurfaceIndex);
                    if (material is null)
                        continue;

                    MapRenderSunShadowStaticMaterialEligibility eligibility =
                        MapRenderSunShadowStaticMaterialEligibilityClassifier
                            .Classify(material);
                    if (!eligibility.IsEligible)
                        continue;

                    expectationRows.Add(
                        new MapRenderSunShadowStaticCasterExpectation(
                            drawInstIndex,
                            lodGeometry.LodIndex,
                            materialSurfaceIndex,
                            placement.Origin,
                            drawInst.Placement.Scale,
                            drawInst.CullDist,
                            eligibility));

                    MapRenderSunShadowCasterMaterialPlan? plan;
                    // Bit 0x40 has priority and selects the shared
                    // m/shadowcaster sorted-index key. The phase root sets
                    // baseTechType 2 and the generic backend executes
                    // build_shadowmap_model_nc.
                    if ((eligibility.RawRouteBits & 0x40) != 0)
                    {
                        plan = sharedModelCasterPlan;
                    }
                    else if (!materialPlans.TryGetValue(
                                 material,
                                 out plan))
                    {
                        plan = MapRenderSunShadowCasterMaterialPlanner.Plan(
                            material,
                            lookup).Plan;
                        materialPlans.Add(material, plan);
                    }
                    if (plan is null)
                        continue;

                    XSurface surface =
                        lodGeometry.ModelSurfs.Surfaces[surfaceOffset];
                    var key = new StaticSunShadowCasterBatchKey(
                        model,
                        lodGeometry.LodIndex,
                        surfaceOffset,
                        materialSurfaceIndex,
                        surface,
                        plan.Material);
                    if (!builders.TryGetValue(
                            key,
                            out StaticSunShadowCasterBatchBuilder? builder))
                    {
                        if (!TryPrepareStaticSunShadowCasterPayload(
                                surface,
                                plan,
                                imageStreams,
                                textureCache,
                                failedTextureCacheKeys,
                                ref decodedTextureCount,
                                ref skippedTextureCount,
                                out MapRenderSunShadowCasterGeometry? geometry,
                                out MapRenderUvRoute? cutoutUvRoute,
                                out MapRenderTexture? cutoutTexture))
                        {
                            continue;
                        }

                        builder = new StaticSunShadowCasterBatchBuilder(
                            model,
                            lodGeometry.Lod,
                            lodGeometry.LodIndex,
                            surface,
                            surfaceOffset,
                            materialSurfaceIndex,
                            eligibility,
                            plan,
                            geometry!,
                            cutoutUvRoute,
                            cutoutTexture);
                        builders.Add(key, builder);
                    }

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
                    builder.Instances.Add(
                        new MapRenderSunShadowStaticCasterInstance(
                            instance,
                            placement.Origin,
                            drawInst.Placement.Scale,
                            drawInst.CullDist));
                }
            }
        }

        expectations = Array.AsReadOnly(expectationRows.ToArray());
        return Array.AsReadOnly(
            builders.Values
                .Where(builder => builder.Instances.Count > 0)
                .Select(builder => builder.Materialize())
                .ToArray());
    }

    private static bool TryPrepareSunShadowCasterPayload(
        GfxWorldAsset world,
        GfxSurface surface,
        PreparedWorldSurfaceGeometry preparedGeometry,
        MapRenderSunShadowCasterMaterialPlan plan,
        IGfxImagePayloadResolver imageStreams,
        MapRenderTextureCache textureCache,
        HashSet<MapRenderTextureCacheKey> failedTextureCacheKeys,
        ref int decodedTextureCount,
        ref int skippedTextureCount,
        out MapRenderSunShadowCasterGeometry? geometry,
        out MapRenderUvRoute? cutoutUvRoute,
        out MapRenderTexture? cutoutTexture)
    {
        bool cutout = plan is MapRenderSunShadowCutoutCasterMaterialPlan;
        bool usesVertexColor = plan is
            MapRenderSunShadowCutoutCasterMaterialPlan
            {
                UsesVertexColor: true
            };
        WorldVertexDecoder? uvDecoder = null;
        cutoutUvRoute = null;
        cutoutTexture = null;
        if (cutout)
        {
            int backendRow = WorldVertexLayout.ResolveEffectiveBackendRow(
                plan.Technique.Flags,
                plan.TechniqueSet.WorldVertexFormat);
            var layout = new WorldVertexLayoutSelection(
                plan.TechniqueSet.WorldVertexFormat,
                backendRow,
                $"sun-shadow slot-2 row {backendRow}");
            uvDecoder = SelectWorldVertexDecoder(
                world,
                layout,
                MapRenderSunShadowCasterMaterialPlanner
                    .CutoutEngineRouteSource,
                texCoordSourceIsEngineRouted: true,
                out MapRenderUvRoute resolvedRoute);
            if (uvDecoder is null || !uvDecoder.HasTexCoord)
            {
                geometry = null;
                return false;
            }
            cutoutUvRoute = resolvedRoute;

            var cutoutPlan = (MapRenderSunShadowCutoutCasterMaterialPlan)plan;
            if (!TryDecodeTexture(
                    cutoutPlan.Sampler.Texture,
                    cutoutPlan.Sampler.Image,
                    imageStreams,
                    textureCache,
                    failedTextureCacheKeys,
                    includeAuthoredMipChain: true,
                    ref decodedTextureCount,
                    ref skippedTextureCount,
                    out cutoutTexture) ||
                cutoutTexture is null)
            {
                geometry = null;
                cutoutUvRoute = null;
                return false;
            }
        }

        return TryBuildWorldSunShadowCasterGeometry(
            surface,
            preparedGeometry,
            uvDecoder,
            cutout,
            usesVertexColor,
            out geometry);
    }

    private static bool TryPrepareStaticSunShadowCasterPayload(
        XSurface surface,
        MapRenderSunShadowCasterMaterialPlan plan,
        IGfxImagePayloadResolver imageStreams,
        MapRenderTextureCache textureCache,
        HashSet<MapRenderTextureCacheKey> failedTextureCacheKeys,
        ref int decodedTextureCount,
        ref int skippedTextureCount,
        out MapRenderSunShadowCasterGeometry? geometry,
        out MapRenderUvRoute? cutoutUvRoute,
        out MapRenderTexture? cutoutTexture)
    {
        bool cutout = plan is MapRenderSunShadowCutoutCasterMaterialPlan;
        bool usesVertexColor = plan is
            MapRenderSunShadowCutoutCasterMaterialPlan
            {
                UsesVertexColor: true
            };
        XSurfaceVertexDecoder? uvDecoder = null;
        cutoutUvRoute = null;
        cutoutTexture = null;
        if (cutout)
        {
            uvDecoder = SelectStaticVertexDecoder(
                MapRenderSunShadowCasterMaterialPlanner
                    .CutoutEngineRouteSource);
            if (uvDecoder is null)
            {
                geometry = null;
                return false;
            }
            cutoutUvRoute = BuildStaticModelUvRoute(
                MapRenderSunShadowCasterMaterialPlanner
                    .CutoutEngineRouteSource);

            var cutoutPlan = (MapRenderSunShadowCutoutCasterMaterialPlan)plan;
            if (!TryDecodeTexture(
                    cutoutPlan.Sampler.Texture,
                    cutoutPlan.Sampler.Image,
                    imageStreams,
                    textureCache,
                    failedTextureCacheKeys,
                    includeAuthoredMipChain: true,
                    ref decodedTextureCount,
                    ref skippedTextureCount,
                    out cutoutTexture) ||
                cutoutTexture is null)
            {
                geometry = null;
                cutoutUvRoute = null;
                return false;
            }
        }

        return TryBuildStaticSunShadowCasterGeometry(
            surface,
            uvDecoder,
            cutout,
            usesVertexColor,
            out geometry);
    }

    internal static bool TryBuildWorldSunShadowCasterGeometry(
        GfxSurface surface,
        PreparedWorldSurfaceGeometry preparedGeometry,
        WorldVertexDecoder? cutoutUvDecoder,
        bool cutout,
        bool usesVertexColor,
        out MapRenderSunShadowCasterGeometry? geometry)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(preparedGeometry);
        geometry = null;
        if (!preparedGeometry.Matches(surface) ||
            (cutout && cutoutUvDecoder is null) ||
            (usesVertexColor && !cutout))
        {
            return false;
        }

        int stride = cutout
            ? MapRenderSunShadowCasterGeometry.CutoutVertexFloatCount
            : MapRenderSunShadowCasterGeometry.OpaqueVertexFloatCount;
        var vertices = new List<float>(
            checked(preparedGeometry.SourceVertexCount * stride));
        var indices = new List<uint>(
            checked(preparedGeometry.SolidTriangleCount * 3));
        var destinationBySourceSlot = new int[
            preparedGeometry.SourceVertexCount];
        Array.Fill(destinationBySourceSlot, -1);

        foreach (PreparedWorldSurfaceTriangle triangle in
                 preparedGeometry.Triangles)
        {
            if (!TryAdd(triangle.VertexSlot0, out uint index0) ||
                !TryAdd(triangle.VertexSlot1, out uint index1) ||
                !TryAdd(triangle.VertexSlot2, out uint index2))
            {
                return false;
            }
            indices.Add(index0);
            indices.Add(index1);
            indices.Add(index2);
        }
        if (indices.Count == 0)
            return false;

        geometry = new MapRenderSunShadowCasterGeometry(
            cutout,
            usesVertexColor,
            vertices,
            indices);
        return true;

        bool TryAdd(int sourceSlot, out uint destinationIndex)
        {
            int existing = destinationBySourceSlot[sourceSlot];
            if (existing >= 0)
            {
                destinationIndex = checked((uint)existing);
                return true;
            }
            if (!preparedGeometry.TryGetPosition(
                    sourceSlot,
                    out Vector3 position))
            {
                destinationIndex = 0;
                return false;
            }

            Vector2 uv = default;
            Vector4 vertexColor = Vector4.One;
            if (cutout &&
                !cutoutUvDecoder!.TryReadTexCoord(
                    surface,
                    preparedGeometry.GetSourceVertexIndex(sourceSlot),
                    out uv))
            {
                destinationIndex = 0;
                return false;
            }
            if (usesVertexColor &&
                !cutoutUvDecoder!.TryReadBlendWeights(
                    surface,
                    preparedGeometry.GetSourceVertexIndex(sourceSlot),
                    out vertexColor))
            {
                destinationIndex = 0;
                return false;
            }
            if (!IsReasonable(uv))
            {
                destinationIndex = 0;
                return false;
            }

            destinationIndex = checked((uint)(vertices.Count / stride));
            destinationBySourceSlot[sourceSlot] =
                checked((int)destinationIndex);
            vertices.Add(position.X);
            vertices.Add(position.Y);
            vertices.Add(position.Z);
            if (cutout)
            {
                vertices.Add(vertexColor.X);
                vertices.Add(vertexColor.Y);
                vertices.Add(vertexColor.Z);
                vertices.Add(vertexColor.W);
                vertices.Add(uv.X);
                vertices.Add(uv.Y);
            }
            return true;
        }
    }

    internal static bool TryBuildStaticSunShadowCasterGeometry(
        XSurface surface,
        XSurfaceVertexDecoder? cutoutUvDecoder,
        bool cutout,
        bool usesVertexColor,
        out MapRenderSunShadowCasterGeometry? geometry)
    {
        ArgumentNullException.ThrowIfNull(surface);
        geometry = null;
        if (surface.VertCount <= 0 ||
            surface.TriCount <= 0 ||
            (cutout && cutoutUvDecoder is null) ||
            (usesVertexColor && !cutout))
        {
            return false;
        }

        int stride = cutout
            ? MapRenderSunShadowCasterGeometry.CutoutVertexFloatCount
            : MapRenderSunShadowCasterGeometry.OpaqueVertexFloatCount;
        var vertices = new List<float>(checked(surface.VertCount * stride));
        var indices = new List<uint>(checked(surface.TriCount * 3));
        var destinationBySourceIndex = new int[surface.VertCount];
        Array.Fill(destinationBySourceIndex, -1);

        for (int triangleIndex = 0;
             triangleIndex < surface.TriCount;
             triangleIndex++)
        {
            int sourceOffset = checked(triangleIndex * 3);
            if (sourceOffset + 2 >= surface.TriIndices.Count ||
                !TryAdd(surface.TriIndices[sourceOffset], out uint index0) ||
                !TryAdd(surface.TriIndices[sourceOffset + 1], out uint index1) ||
                !TryAdd(surface.TriIndices[sourceOffset + 2], out uint index2))
            {
                return false;
            }
            indices.Add(index0);
            indices.Add(index1);
            indices.Add(index2);
        }
        if (indices.Count == 0)
            return false;

        geometry = new MapRenderSunShadowCasterGeometry(
            cutout,
            usesVertexColor,
            vertices,
            indices);
        return true;

        bool TryAdd(int sourceIndex, out uint destinationIndex)
        {
            if ((uint)sourceIndex >= (uint)surface.VertCount)
            {
                destinationIndex = 0;
                return false;
            }
            int existing = destinationBySourceIndex[sourceIndex];
            if (existing >= 0)
            {
                destinationIndex = checked((uint)existing);
                return true;
            }
            if (!XSurfaceVertexDecoder.TryReadPosition(
                    surface,
                    sourceIndex,
                    out Vector3 position))
            {
                destinationIndex = 0;
                return false;
            }

            Vector2 uv = default;
            Vector4 vertexColor = Vector4.One;
            if (cutout &&
                !cutoutUvDecoder!.TryReadTexCoord(
                    surface,
                    sourceIndex,
                    out uv))
            {
                destinationIndex = 0;
                return false;
            }
            if (usesVertexColor &&
                !cutoutUvDecoder!.TryReadColor(
                    surface,
                    sourceIndex,
                    out vertexColor))
            {
                destinationIndex = 0;
                return false;
            }
            if (!IsReasonable(uv))
            {
                destinationIndex = 0;
                return false;
            }

            destinationIndex = checked((uint)(vertices.Count / stride));
            destinationBySourceIndex[sourceIndex] =
                checked((int)destinationIndex);
            vertices.Add(position.X);
            vertices.Add(position.Y);
            vertices.Add(position.Z);
            if (cutout)
            {
                vertices.Add(vertexColor.X);
                vertices.Add(vertexColor.Y);
                vertices.Add(vertexColor.Z);
                vertices.Add(vertexColor.W);
                vertices.Add(uv.X);
                vertices.Add(uv.Y);
            }
            return true;
        }
    }

    private static bool IsReasonable(Vector2 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        MathF.Abs(value.X) <= MaxReasonableTexCoord &&
        MathF.Abs(value.Y) <= MaxReasonableTexCoord;
}
