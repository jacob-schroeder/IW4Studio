using IW4.Render.Techniques;
using System.Numerics;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Assets.Assets.XModel;
using IW4.Runtime.Assets.Images;
using IW4.Runtime.Assets.GfxMap;

using IW4.Render.Assets;
using IW4.Render.EditorPreview;
using IW4.Render.Execution;
using IW4.Render.Geometry;
using IW4.Render.Geometry.Shadows;
using IW4.Render.Lighting;
using IW4.Render.Materials;
using IW4.Render.Picking;
using IW4.Render.Scheduling;
using IW4.Render.Execution.Fog;
using IW4.Render.Scheduling.Shadows;
using IW4.Render.Scheduling.StaticModels;
using IW4.Render.SceneBuilding.Batching;
using IW4.Render.Shaders;
using IW4.Render.Textures;

namespace IW4.Render.SceneBuilding;

public sealed partial class MapSceneBuilder : IMapRenderSceneBuilder
{
    private const int RsxVertexInputCount = 16;
    private const int RsxVertexInputComponentCount = 4;
    private const float MaxReasonableCoordinate = 1_000_000f;
    private const float MaxReasonableTexCoord = 1_000_000f;
    private const float MinTexturedWorldTriangleArea2 = 0.000001f;
    private const float MinTexturedUvTriangleArea2 = 0.000001f;
    private const TextureSemantic ColorTextureSemantic =
        TextureSemantic.ColorMap;
    private const MaterialStreamSource GenericFallbackTexCoordSource =
        MaterialStreamSource.TexCoord0;
    private const string BaseSurfaceTexturePassClass = "MaterialColor";
    private const string GenericMaterialFallbackPassClass = "GenericMaterialFallback";
    private const string AuthoredMaterialCandidatePassClass = "AuthoredMaterialCandidate";
    private static readonly RenderState GenericMaterialState = RenderState.Default with
    {
        HasState = true,
        ColorMask = RsxColorMask.Rgba,
        DepthWriteEnabled = true
    };
    private static readonly Vector4 DefaultRsxVertexInput = new(0f, 0f, 0f, 1f);

    public MapRenderScene Build(MapRenderInput input)
    {
        bool includeDiagnosticGeometry = input.BuildProfile switch
        {
            MapRenderSceneBuildProfile.Neutral => true,
            MapRenderSceneBuildProfile.InteractiveOpenGl => false,
            _ => throw new ArgumentOutOfRangeException(
                nameof(input),
                input.BuildProfile,
                "Unknown map-render scene build profile.")
        };
        bool includeCollisionDiagnosticGeometry =
            input.ClipMap is not null &&
            MaterializesCollisionDiagnosticGeometry(input.BuildProfile);
        Action<string>? progressSink = input.Progress;
        System.Diagnostics.Stopwatch? buildStopwatch = progressSink is null
            ? null
            : System.Diagnostics.Stopwatch.StartNew();
        Action<string>? reportProgress = progressSink is null
            ? null
            : stage => progressSink(
                $"scene: {stage} ({buildStopwatch!.Elapsed.TotalSeconds:0.0}s)");
        bool collectBuildProfiles = reportProgress is not null;

        reportProgress?.Invoke(
            $"execution=Live Preview; profile={input.BuildProfile}; allocating geometry buffers");
        var solidVertices = includeDiagnosticGeometry
            ? new List<float>(256 * 1024)
            : [];
        var solidIndices = includeDiagnosticGeometry
            ? new List<uint>(256 * 1024)
            : [];
        var fallbackSolidVertices = includeDiagnosticGeometry
            ? new List<float>(4 * 1024)
            : [];
        var fallbackSolidIndices = includeDiagnosticGeometry
            ? new List<uint>(4 * 1024)
            : [];
        var texturedBatchBuilders = new Dictionary<WorldTexturedBatchKey, TexturedBatchBuilder>();
        var pageZeroUnshadowedTexturedBatchBuilders =
            new Dictionary<WorldTexturedBatchKey, TexturedBatchBuilder>();
        var shadowAllocatedTexturedBatchBuilders =
            new Dictionary<WorldTexturedBatchKey, TexturedBatchBuilder>();
        var pageOneUnshadowedTexturedBatchBuilders =
            new Dictionary<WorldTexturedBatchKey, TexturedBatchBuilder>();
        var pageOneShadowAllocatedTexturedBatchBuilders =
            new Dictionary<WorldTexturedBatchKey, TexturedBatchBuilder>();
        var instancedSolidBatchBuilders = new Dictionary<XSurface, InstancedSolidBatchBuilder>(ReferenceEqualityComparer.Instance);
        var instancedTexturedBatchBuilders = new Dictionary<StaticTexturedBatchKey, InstancedTexturedBatchBuilder>();
        var exactNormalCameraInstancedTexturedBatchBuilders =
            new Dictionary<StaticTexturedBatchKey,
                InstancedTexturedBatchBuilder>();
        var shadowAllocatedInstancedTexturedBatchBuilders =
            new Dictionary<StaticTexturedBatchKey,
                InstancedTexturedBatchBuilder>();
        var pageOneUnshadowedInstancedTexturedBatchBuilders =
            new Dictionary<StaticTexturedBatchKey,
                InstancedTexturedBatchBuilder>();
        var pageOneShadowAllocatedInstancedTexturedBatchBuilders =
            new Dictionary<StaticTexturedBatchKey,
                InstancedTexturedBatchBuilder>();
        var textureCache = new RenderTextureCache(
            preferProvenAuthoredPayloads:
                input.BuildProfile ==
                MapRenderSceneBuildProfile.InteractiveOpenGl);
        var failedTextureCacheKeys = new HashSet<RenderTextureCacheKey>();
        var worldTextureCache = new MapRenderWorldTextureCache();
        var failedWorldTextureCacheKeys =
            new HashSet<MapRenderWorldTextureCacheKey>();
        var wireVertices =
            includeDiagnosticGeometry || includeCollisionDiagnosticGeometry
            ? new List<float>(64 * 1024)
            : [];
        var wireIndices =
            includeDiagnosticGeometry || includeCollisionDiagnosticGeometry
            ? new List<uint>(64 * 1024)
            : [];
        var solidPickRanges = new List<MapRenderPickRange>();
        var fallbackSolidPickRanges = new List<MapRenderPickRange>();
        var collisionPickTriangles = new List<MapRenderPickTriangle>();
        RenderBounds bounds = RenderBounds.Empty;
        RenderBounds collisionBounds = RenderBounds.Empty;
        reportProgress?.Invoke("indexing material, technique, shader, and image assets");
        MapRenderWorldSceneSourceBuildContext worldSourceContext =
            new MapRenderWorldSceneSourceBuilder().BuildContext(
                input,
                reportProgress);
        GfxWorldTextureRuntimeSession? worldTextureRuntime =
            worldSourceContext.TextureRuntime;
        IGfxImagePayloadResolver imageStreams = worldSourceContext.ImageStreams;
        RenderAssetLookup lookup = worldSourceContext.AssetLookup;
        IMapRenderWorldTextureBindingResolver worldTextureBindings = lookup;
        long worldTextureRevisionAtConstruction = -1;
        MapRenderWorldSceneSourceBuildResult worldSourceBuildResult =
            worldSourceContext.Result;
        MapRenderStaticModelLightingAtlas? staticModelLightingAtlas =
            input.GfxMap is { } lightingWorld
                ? MapRenderStaticModelLightingAtlasBuilder.Build(
                    lightingWorld)
                : null;
        MapRenderEditorPreviewLightingPlan editorPreviewLighting =
            MapRenderEditorPreviewLightingPlanner.Create(
                worldSourceBuildResult.Source?.SceneLights.Source?.ComWorld);
        MapRenderEditorPreviewCreateArtFogResolution?
            editorPreviewCreateArtFog = null;
        MapRenderEditorPreviewVisionResolution?
            editorPreviewVision = null;
        MapRenderActiveFogState? editorPreviewActiveFog = null;
        bool editorPreviewFallbackFogPending = false;
        if (input.EditorPreviewActiveFog is { } explicitActiveFog)
        {
            editorPreviewActiveFog = explicitActiveFog;
            reportProgress?.Invoke(
                "Live Preview fog source=explicit active state");
        }
        else if (input.GfxMap is { } fogWorld)
        {
            long fogProviderRevision =
                worldSourceBuildResult.Source?
                    .AssetPoolRevisionAtConstruction ??
                input.AssetSource.AssetPool.Revision;
            editorPreviewCreateArtFog =
                MapRenderEditorPreviewCreateArtFogResolver.Resolve(
                    fogWorld.Name,
                    fogProviderRevision,
                    lookup);
            if (editorPreviewCreateArtFog.IsReady)
            {
                editorPreviewActiveFog =
                    editorPreviewCreateArtFog.ActiveFog;
                reportProgress?.Invoke(
                    $"Live Preview fog source=createart; {editorPreviewCreateArtFog.Detail}");
            }
            else if (editorPreviewCreateArtFog.Status ==
                     MapRenderEditorPreviewCreateArtFogStatus
                         .CanonicalRawFileAbsent)
            {
                editorPreviewFallbackFogPending = true;
            }
        }
        else
        {
            editorPreviewFallbackFogPending = true;
        }

        if (input.GfxMap is { } visionWorld)
        {
            long visionProviderRevision =
                worldSourceBuildResult.Source?
                    .AssetPoolRevisionAtConstruction ??
                input.AssetSource.AssetPool.Revision;
            editorPreviewVision =
                MapRenderEditorPreviewVisionResolver.Resolve(
                    visionWorld.Name,
                    visionProviderRevision,
                    lookup);
            reportProgress?.Invoke(
                editorPreviewVision.IsReady
                    ? $"Live Preview vision source=createart; {editorPreviewVision.Detail}"
                    : $"Live Preview vision unavailable ({editorPreviewVision.Status}); {editorPreviewVision.Detail}");
        }

        MapRenderEditorPreviewEffectivePostState? editorPreviewEffectivePost =
            input.EditorPreviewPostRuntimeSnapshot is { } postRuntime
                ? MapRenderEditorPreviewEffectivePostStateEvaluator.Evaluate(
                    worldSourceBuildResult.Source?
                        .AssetPoolRevisionAtConstruction ??
                    input.AssetSource.AssetPool.Revision,
                    postRuntime)
                : null;

        bool activeSunFogEnabled =
            editorPreviewActiveFog?.SunFog.Enabled ??
            (editorPreviewFallbackFogPending &&
             (input.EditorPreviewAtmosphere?.Enabled ?? true));
        MapRenderDrawMethod? editorPreviewWorldDrawMethod =
            input.GfxMap is { } drawMethodWorld
                ? MapRenderDrawMethodInitializer.Initialize(
                    MapRenderWorldDrawMethodSettingsAdapter.Adapt(
                        drawMethodWorld,
                        new MapRenderDrawMethodSettings(
                            FullbrightEnabled: false,
                            DebugShaderValue: 0,
                            UseSunDirFog: false,
                            // r_lodShaders defaults to enabled. The preview
                            // currently materializes page-0 receiver variants;
                            // page choice remains runtime state.
                            LodShadersEnabled: true),
                        activeSunFogEnabled))
                : null;
        MapRenderSceneTechniqueVariantCatalog? sceneTechniqueVariants =
            input.GfxMap is { } techniqueVariantWorld &&
            editorPreviewWorldDrawMethod is not null &&
            worldSourceBuildResult.Source?.SceneLights.Source?.SelectorState
                is { } techniqueVariantSceneLights
                ? MapRenderSceneTechniqueVariantCatalogPlanner.Plan(
                    techniqueVariantWorld,
                    editorPreviewWorldDrawMethod,
                    techniqueVariantSceneLights)
                : null;
        MapRenderWorldReceiverVariantRequirement[]?
            worldReceiverRequirements =
                sceneTechniqueVariants is null
                    ? null
                    : new MapRenderWorldReceiverVariantRequirement[
                        sceneTechniqueVariants.WorldSurfaces.Count];
        reportProgress?.Invoke($"asset index ready: {lookup.MaterialCount} materials, {lookup.TechsetCount} technique sets, {lookup.ImageCount} images");
        var techniqueSetByMaterial = new Dictionary<MaterialAsset, MaterialTechniqueSetAsset?>();
        var materialColorUvPassCache = new Dictionary<MaterialAsset, SelectedColorPass?>();
        var editorMaterialPassCache = new Dictionary<
            (MaterialAsset Material,
                MaterialTechniqueSetAsset? TechniqueSet,
                int? SelectedTechniqueSlot),
            IReadOnlyList<SelectedColorPass>>();
        var worldReceiverRequirementCache = new Dictionary<
            (MaterialAsset? Material,
                MaterialTechniqueSetAsset? TechniqueSet,
                int? SelectedTechniqueSlot),
            bool>();
        var editorDepthPrepassCache = new Dictionary<
            (MaterialAsset Material, MaterialTechniqueSetAsset? TechniqueSet),
            MapRenderEditorDepthPrepassPlan?>();
        var editorMaterialTexturePlanCache =
            new Dictionary<MaterialAsset, EditorMaterialTexturePlan>();
        var selectedVertexInputCache = new Dictionary<(MaterialTechniqueSetAsset? TechniqueSet, int TechniqueSlot, int PassIndex), ShaderVertexInputBinding[]>();
        var worldVertexLayoutCache = new Dictionary<
            (MaterialTechniqueSetAsset? TechniqueSet, int TechniqueSlot, int PassIndex),
            WorldVertexLayoutSelection>();
        var shaderExecutionCache = new Dictionary<ShaderExecutionCacheKey, ShaderExecutionContract>();
        var shaderTranslationCache = new ShaderTranslationCache();
        var worldMaterialSamplerPlanCache = new Dictionary<
            WorldMaterialSamplerPlanCacheKey,
            WorldMaterialSamplerPlan>();
        var worldCameraColorPhasePlanCache = new Dictionary<
            (MaterialAsset Material,
                MaterialTechniqueSetAsset? TechniqueSet,
                int? SelectedTechniqueSlot,
                bool SelectedCameraColorPassAvailable),
            MapRenderWorldCameraColorPhasePlan>();
        long primaryTextureProfileTicks = 0;
        long colorLayerProfileTicks = 0;
        long samplerBindingProfileTicks = 0;
        long shaderContractProfileTicks = 0;

        static T Profile<T>(bool enabled, ref long ticks, Func<T> action)
        {
            if (!enabled)
                return action();

            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                return action();
            }
            finally
            {
                ticks += System.Diagnostics.Stopwatch.GetTimestamp() - start;
            }
        }

        MaterialTechniqueSetAsset? ResolveCachedTechniqueSet(MaterialAsset material)
        {
            if (!techniqueSetByMaterial.TryGetValue(material, out MaterialTechniqueSetAsset? result))
            {
                result = ResolveTechniqueSet(material, lookup);
                techniqueSetByMaterial[material] = result;
            }

            return result;
        }

        SelectedColorPass? ResolveCachedMaterialColorUvPass(
            MaterialAsset material,
            MaterialTechniqueSetAsset? techset)
        {
            if (!materialColorUvPassCache.TryGetValue(material, out SelectedColorPass? result))
            {
                result = SelectMaterialColorUvPass(material, techset, lookup);
                materialColorUvPassCache[material] = result;
            }

            return result;
        }

        IReadOnlyList<SelectedColorPass> ResolveCachedEditorMaterialPasses(
            MaterialAsset material,
            MaterialTechniqueSetAsset? techset,
            int? selectedTechniqueSlot)
        {
            var key = (material, techset, selectedTechniqueSlot);
            if (!editorMaterialPassCache.TryGetValue(
                    key,
                    out IReadOnlyList<SelectedColorPass>? result))
            {
                result = SelectEditorMaterialPasses(
                        material,
                        techset,
                        lookup,
                        selectedTechniqueSlot,
                        out _)
                    .ToArray();
                editorMaterialPassCache.Add(key, result);
            }

            return result;
        }

        bool ResolveCachedWorldReceiverRequirement(
            MaterialAsset? material,
            MaterialTechniqueSetAsset? techset,
            int? selectedTechniqueSlot)
        {
            var key = (material, techset, selectedTechniqueSlot);
            if (!worldReceiverRequirementCache.TryGetValue(
                    key,
                    out bool result))
            {
                result = WorldReceiverVariantRequiredForBuild(
                    material,
                    techset,
                    lookup,
                    selectedTechniqueSlot);
                worldReceiverRequirementCache.Add(key, result);
            }

            return result;
        }

        MapRenderWorldCameraColorPhasePlan ResolveCachedWorldCameraColorPhase(
            MaterialAsset material,
            MaterialTechniqueSetAsset? techset,
            int? selectedTechniqueSlot,
            bool selectedCameraColorPassAvailable)
        {
            var key = (
                material,
                techset,
                selectedTechniqueSlot,
                selectedCameraColorPassAvailable);
            if (!worldCameraColorPhasePlanCache.TryGetValue(
                    key,
                    out MapRenderWorldCameraColorPhasePlan? result))
            {
                result = MapRenderWorldCameraColorPhasePlanner.Plan(
                    material,
                    techset,
                    lookup,
                    selectedTechniqueSlot,
                    selectedCameraColorPassAvailable);
                worldCameraColorPhasePlanCache.Add(key, result);
            }

            return result;
        }

        MapRenderEditorDepthPrepassPlan? ResolveCachedEditorDepthPrepass(
            MaterialAsset material,
            MaterialTechniqueSetAsset? techset)
        {
            var key = (material, techset);
            if (!editorDepthPrepassCache.TryGetValue(
                    key,
                    out MapRenderEditorDepthPrepassPlan? result))
            {
                result = SelectEditorStandardDepthPrepass(
                    material,
                    techset,
                    lookup);
                editorDepthPrepassCache.Add(key, result);
            }

            return result;
        }

        EditorMaterialTexturePlan ResolveCachedEditorMaterialTexturePlan(
            MaterialAsset material)
        {
            if (!editorMaterialTexturePlanCache.TryGetValue(
                    material,
                    out EditorMaterialTexturePlan? result))
            {
                result = EditorMaterialTexturePlanner.Plan(
                    material.Textures,
                    (_, row) => new EditorMaterialTextureResolution(
                        row.Image ?? lookup.ResolveImage(row.DataPointer),
                        null));
                editorMaterialTexturePlanCache.Add(material, result);
            }

            return result;
        }

        ShaderVertexInputBinding[] ResolveCachedSelectedVertexInputs(
            MaterialTechniqueSetAsset? techset,
            SelectedColorPass selectedPass)
        {
            var key = (
                techset,
                selectedPass.Pass.TechniquePass.TechniqueSlot,
                selectedPass.Pass.TechniquePass.PassIndex);
            if (!selectedVertexInputCache.TryGetValue(key, out ShaderVertexInputBinding[]? result))
            {
                result = ResolveSelectedVertexInputs(techset, lookup, selectedPass);
                selectedVertexInputCache.Add(key, result);
            }

            return result;
        }

        WorldVertexLayoutSelection ResolveCachedWorldVertexLayout(
            MaterialTechniqueSetAsset? techset,
            SelectedColorPass selectedPass)
        {
            var key = (
                techset,
                selectedPass.Pass.TechniquePass.TechniqueSlot,
                selectedPass.Pass.TechniquePass.PassIndex);
            if (!worldVertexLayoutCache.TryGetValue(
                    key,
                    out WorldVertexLayoutSelection result))
            {
                result = ResolveWorldVertexLayout(
                    techset,
                    lookup,
                    selectedPass);
                worldVertexLayoutCache.Add(key, result);
            }

            return result;
        }

        ShaderExecutionContract ResolveShaderExecution(
            MaterialAsset? material,
            MaterialTechniqueSetAsset? techset,
            SelectedColorPass selectedPass,
            IReadOnlyList<MapRenderWorldMaterialSamplerBinding> materialSamplers,
            bool vertexInputPayloadReady,
            string vertexInputPayloadBlocker,
            bool authoredSourcePassAvailable,
            ShaderExecutionPurpose purpose =
                ShaderExecutionPurpose.CameraColor)
        {
            var key = new ShaderExecutionCacheKey(
                material,
                techset,
                selectedPass.Pass,
                selectedPass.PrimarySampler,
                selectedPass.TexCoordSource,
                selectedPass.State,
                purpose,
                authoredSourcePassAvailable,
                new MaterialSamplerBindingsIdentity(materialSamplers),
                vertexInputPayloadReady,
                vertexInputPayloadBlocker);
            if (!shaderExecutionCache.TryGetValue(key, out ShaderExecutionContract? result))
            {
                result = Profile(collectBuildProfiles, ref shaderContractProfileTicks, () => BuildShaderExecutionContract(
                    material,
                    techset,
                    lookup,
                    selectedPass,
                    materialSamplers,
                    vertexInputPayloadReady,
                    vertexInputPayloadBlocker,
                    authoredSourcePassAvailable,
                    purpose,
                    shaderTranslationCache));
                shaderExecutionCache.Add(key, result);
            }

            return result;
        }

        WorldMaterialSamplerPlan ResolveCachedWorldMaterialSamplerPlan(
            MaterialAsset? material,
            MaterialTechniqueSetAsset? techset,
            SelectedColorPass selectedPass)
        {
            if (material is null || techset is null)
                return WorldMaterialSamplerPlan.Empty;

            var key = new WorldMaterialSamplerPlanCacheKey(
                material,
                techset,
                selectedPass.Pass.TechniquePass.TechniqueSlot,
                selectedPass.Pass.TechniquePass.PassIndex);
            if (!worldMaterialSamplerPlanCache.TryGetValue(
                    key,
                    out WorldMaterialSamplerPlan? result))
            {
                result = BuildWorldMaterialSamplerPlan(
                    material,
                    techset,
                    lookup,
                    selectedPass);
                worldMaterialSamplerPlanCache.Add(key, result);
            }

            return result;
        }

        int collisionTriangleCount = 0;
        if (input.ClipMap is { } clipMap)
            collisionTriangleCount = AddCollisionDiagnosticGeometry(
                clipMap,
                wireVertices,
                wireIndices,
                collisionPickTriangles,
                includeCollisionDiagnosticGeometry,
                ref bounds,
                ref collisionBounds);

        int surfaceCount = 0;
        int drawnSurfaceCount = 0;
        int skippedSurfaceCount = 0;
        int triangleCount = 0;
        int skippedTriangleCount = 0;
        int geometryReadFailureTriangleCount = 0;
        int geometrySkyboxTriangleCount = 0;
        int skyMaterialSurfaceCount = 0;
        int skyMaterialTriangleCount = 0;
        int materialTextureCandidateSurfaceCount = 0;
        int materialTextureMissingSurfaceCount = 0;
        int materialTextureUvUnresolvedSurfaceCount = 0;
        int materialTextureUvFailedTriangleCount = 0;
        int materialTextureDecodeFailedSurfaceCount = 0;
        int materialTextureGeometryFailedSurfaceCount = 0;
        int textureDecodedCount = 0;
        int textureDecodeSkippedCount = 0;
        IReadOnlyList<Texture?> sceneLightAttenuationTextures =
            BuildSceneLightAttenuationTextures(
                worldSourceBuildResult,
                imageStreams,
                textureCache,
                failedTextureCacheKeys,
                ref textureDecodedCount,
                ref textureDecodeSkippedCount);
        int renderStateDecodedCount = 0;
        int unresolvedCodeSamplerSurfaceCount = 0;
        int staticModelDrawInstCount = 0;
        int staticModelDrawnCount = 0;
        int staticModelSkippedCount = 0;
        int staticModelTriangleCount = 0;
        int staticModelTriangleSkippedCount = 0;
        int staticModelGeometryReadFailureTriangleCount = 0;
        int staticModelPlacementDecodeFailureCount = 0;
        int authoredCandidateTexturedSurfaceCount = 0;
        int authoredCandidateTexturedTriangleCount = 0;
        int genericFallbackTexturedSurfaceCount = 0;
        int genericFallbackTexturedTriangleCount = 0;
        IReadOnlyList<MapRenderStaticModelSchedulingInfo>
            staticModelScheduling = [];
        IReadOnlyList<MapRenderWorldSunShadowCasterBatch>
            sunShadowWorldCasterBatches = [];
        IReadOnlyList<MapRenderSunShadowWorldCasterRejection>
            sunShadowWorldCasterRejections = [];
        IReadOnlyList<MapRenderStaticSunShadowCasterBatch>
            sunShadowStaticCasterBatches = [];
        IReadOnlyList<MapRenderSunShadowStaticCasterExpectation>
            sunShadowStaticCasterExpectations = [];
        IReadOnlyList<MapRenderSky> skies = [];
        long worldSetupProfileTicks = 0;
        long worldSolidGeometryProfileTicks = 0;
        long worldTexturedPipelineProfileTicks = 0;
        if (input.GfxMap is { } gfxMap)
        {
            MapRenderStaticModelLightingAtlas lightingAtlas =
                staticModelLightingAtlas ??
                throw new InvalidOperationException(
                    "A GfxWorld scene lost its index-parallel static-model lighting data.");
            if (worldTextureRuntime is not null)
            {
                reportProgress?.Invoke("capturing runtime world texture bindings");
                var textureState = worldTextureRuntime.RequireTextureState();
                MapRenderWorldTextureBindingSnapshot textureBindingSnapshot =
                    lookup.CaptureWorldRuntimeTextureBindings(
                        gfxMap,
                        textureState);
                long expectedPoolRevision = worldSourceBuildResult.Source?
                    .AssetPoolRevisionAtConstruction ??
                    worldTextureRuntime.AssetPool.Revision;
                if (textureBindingSnapshot.WorldAddress !=
                        worldTextureRuntime.WorldAddress ||
                    textureBindingSnapshot.TextureRevision !=
                        textureState.Revision ||
                    textureBindingSnapshot.AssetPoolRevision !=
                        expectedPoolRevision)
                {
                    throw new InvalidOperationException(
                        "The editor world texture snapshot does not match the canonical scene revision.");
                }
                worldTextureBindings = textureBindingSnapshot;
                worldTextureRevisionAtConstruction =
                    textureBindingSnapshot.TextureRevision;
                reportProgress?.Invoke(
                    $"runtime world texture bindings ready: " +
                    $"{textureBindingSnapshot.BindingsInSamplerOrder.Count} identity(s)");
            }

            surfaceCount = gfxMap.Dpvs.Surfaces.Count;
            byte[] vertexBytes = gfxMap.WorldDraw.VertexData.PackedVertices is byte[] directVertexBytes
                ? directVertexBytes
                : gfxMap.WorldDraw.VertexData.PackedVertices.ToArray();
            IReadOnlyList<ushort> sourceIndices = gfxMap.WorldDraw.Indices;
            var worldVertexDecoderCache = new Dictionary<
                WorldVertexDecoderCacheKey,
                WorldVertexDecoderSelection>();
            WorldVertexDecoderSelection ResolveCachedWorldVertexDecoder(
                WorldVertexLayoutSelection layout,
                MaterialStreamSource texCoordSource,
                bool texCoordSourceIsEngineRouted)
            {
                var key = new WorldVertexDecoderCacheKey(
                    layout,
                    texCoordSource,
                    texCoordSourceIsEngineRouted);
                if (!worldVertexDecoderCache.TryGetValue(
                        key,
                        out WorldVertexDecoderSelection selection))
                {
                    WorldVertexDecoder? decoder = SelectWorldVertexDecoder(
                        gfxMap,
                        layout,
                        texCoordSource,
                        texCoordSourceIsEngineRouted,
                        out UvRoute uvRoute);
                    selection = new WorldVertexDecoderSelection(
                        decoder,
                        uvRoute);
                    worldVertexDecoderCache.Add(key, selection);
                }

                return selection;
            }

            reportProgress?.Invoke($"preparing {surfaceCount} world surface geometry core(s)");
            PreparedWorldSurfaceGeometry[] preparedWorldSurfaces =
                PrepareWorldSurfaceGeometries(
                    gfxMap,
                    vertexBytes,
                    sourceIndices);
            long skyProfileStart = collectBuildProfiles
                ? System.Diagnostics.Stopwatch.GetTimestamp()
                : 0;
            reportProgress?.Invoke($"building {gfxMap.Skies.Count} sky submission source(s)");
            skies = BuildSkySubmissions(
                gfxMap,
                preparedWorldSurfaces,
                lookup,
                imageStreams,
                textureCache,
                failedTextureCacheKeys,
                ref textureDecodedCount,
                ref textureDecodeSkippedCount);
            if (reportProgress is not null)
            {
                double skyProfileSeconds =
                    (double)(System.Diagnostics.Stopwatch.GetTimestamp() - skyProfileStart) /
                    System.Diagnostics.Stopwatch.Frequency;
                reportProgress(
                    $"sky submissions ready: {skies.Count} submission(s), " +
                    $"{skies.Sum(sky => sky.Indices.Length / 3)} triangle(s), " +
                    $"decode={skyProfileSeconds:0.00}s");
            }
            HashSet<int> submittedSkySurfaceIndices = skies
                .SelectMany(sky => sky.SurfaceIndices)
                .ToHashSet();
            var skyOrdinalsBySurface = new Dictionary<int, List<int>>();
            for (int skyOrdinal = 0; skyOrdinal < skies.Count; skyOrdinal++)
            {
                foreach (int surfaceIndex in skies[skyOrdinal]
                             .SurfaceIndices
                             .Distinct())
                {
                    if (!skyOrdinalsBySurface.TryGetValue(
                            surfaceIndex,
                            out List<int>? ordinals))
                    {
                        ordinals = [];
                        skyOrdinalsBySurface.Add(surfaceIndex, ordinals);
                    }
                    ordinals.Add(skyOrdinal);
                }
            }
            var skyShaderPasses = new MaterialPassIdentity?[skies.Count];
            var skyShaderPrimarySamplers =
                new MaterialSamplerIdentity?[skies.Count];
            var skyShaderTexCoordSources =
                new MaterialStreamSource[skies.Count];
            var skyShaderExecutions =
                new ShaderExecutionContract?[skies.Count];
            var skyShaderObservedSurfaceCounts = new int[skies.Count];
            var skyShaderConflicts = new bool[skies.Count];
            IReadOnlySet<int> explicitSkyCubeSamplerDestinations =
                new HashSet<int> { 0 };

            // Material/pass selection and vertex-route lookup touch mutable
            // loader caches, so plan them on the build thread. Only the pure
            // package/decode work runs concurrently; DecodeUnique publishes
            // cache entries and counters later in exact first-request order.
            reportProgress?.Invoke(
                "planning unique world primary texture decodes");
            var worldPrimaryTextureRequests =
                new List<RenderTextureDecodeRequest>();
            var plannedWorldPrimaryTextureKeys =
                new HashSet<RenderTextureCacheKey>();
            for (int surfaceIndex = 0;
                 surfaceIndex < gfxMap.Dpvs.Surfaces.Count;
                 surfaceIndex++)
            {
                if (submittedSkySurfaceIndices.Contains(surfaceIndex))
                    continue;

                GfxSurface surface = gfxMap.Dpvs.Surfaces[surfaceIndex];
                int? selectedTechniqueSlot =
                    ResolvePreparedEditorTechniqueVariantSlot(
                        surface.PrimaryLightIndex,
                        GfxDrawSurfSurfaceType.Triangles,
                        editorPreviewWorldDrawMethod,
                        worldSourceBuildResult.Source?.SceneLights.Source?
                            .SelectorState,
                        MapRenderTechniqueVariantAllocation.Unshadowed);
                int? shadowAllocatedTechniqueSlot =
                    ResolvePreparedEditorTechniqueVariantSlot(
                        surface.PrimaryLightIndex,
                        GfxDrawSurfSurfaceType.Triangles,
                        editorPreviewWorldDrawMethod,
                        worldSourceBuildResult.Source?.SceneLights.Source?
                            .SelectorState,
                        MapRenderTechniqueVariantAllocation
                            .ShadowMapAllocated);
                int? pageOneUnshadowedTechniqueSlot =
                    ResolvePreparedEditorTechniqueVariantSlot(
                        surface.PrimaryLightIndex,
                        GfxDrawSurfSurfaceType.TrianglesNoSunShadow,
                        editorPreviewWorldDrawMethod,
                        worldSourceBuildResult.Source?.SceneLights.Source?
                            .SelectorState,
                        MapRenderTechniqueVariantAllocation.Unshadowed);
                int? pageOneShadowAllocatedTechniqueSlot =
                    ResolvePreparedEditorTechniqueVariantSlot(
                        surface.PrimaryLightIndex,
                        GfxDrawSurfSurfaceType.TrianglesNoSunShadow,
                        editorPreviewWorldDrawMethod,
                        worldSourceBuildResult.Source?.SceneLights.Source?
                            .SelectorState,
                        MapRenderTechniqueVariantAllocation
                            .ShadowMapAllocated);
                MaterialAsset? material = surface.Material ??
                    lookup.ResolveMaterial(surface.MaterialPointer);
                MaterialTechniqueSetAsset? techset = material is null
                    ? null
                    : ResolveCachedTechniqueSet(material);
                IReadOnlyList<SelectedColorPass> rendererPasses =
                    material is not null
                        ? ResolveCachedEditorMaterialPasses(
                            material,
                            techset,
                            selectedTechniqueSlot)
                        : [];
                IReadOnlyList<SelectedColorPass> shadowAllocatedPasses =
                    material is not null &&
                    shadowAllocatedTechniqueSlot is not null
                        ? ResolveCachedEditorMaterialPasses(
                            material,
                            techset,
                            shadowAllocatedTechniqueSlot)
                        : [];
                IReadOnlyList<SelectedColorPass> pageOneUnshadowedPasses =
                    material is not null &&
                    pageOneUnshadowedTechniqueSlot is not null
                        ? ResolveCachedEditorMaterialPasses(
                            material,
                            techset,
                            pageOneUnshadowedTechniqueSlot)
                        : [];
                IReadOnlyList<SelectedColorPass>
                    pageOneShadowAllocatedPasses =
                        material is not null &&
                        pageOneShadowAllocatedTechniqueSlot is not null
                            ? ResolveCachedEditorMaterialPasses(
                                material,
                                techset,
                                pageOneShadowAllocatedTechniqueSlot)
                            : [];
                SelectedColorPass? basePreviewPass = material is null
                    ? null
                    : ResolveCachedMaterialColorUvPass(material, techset);
                MapRenderWorldCameraColorPhasePlan? cameraColorPhase =
                    material is null
                        ? null
                        : ResolveCachedWorldCameraColorPhase(
                            material,
                            techset,
                            selectedTechniqueSlot,
                            rendererPasses.Count > 0);
                IEnumerable<SelectedColorPass> plannedPasses = rendererPasses
                    .Concat(shadowAllocatedPasses)
                    .Concat(pageOneUnshadowedPasses)
                    .Concat(pageOneShadowAllocatedPasses);
                if (basePreviewPass is not null &&
                    cameraColorPhase?.SuppressGenericCameraColorFallback != true)
                {
                    plannedPasses = plannedPasses.Append(basePreviewPass);
                }

                foreach (SelectedColorPass selectedPass in plannedPasses)
                {
                    WorldVertexLayoutSelection worldVertexLayout =
                        ResolveCachedWorldVertexLayout(
                            techset,
                            selectedPass);
                    WorldVertexDecoderSelection decoderSelection =
                        ResolveCachedWorldVertexDecoder(
                        worldVertexLayout,
                        selectedPass.TexCoordSource,
                        selectedPass.TexCoordSourceIsEngineRouted);
                    WorldVertexDecoder? decoder = decoderSelection.Decoder;
                    if (decoder is null || !decoder.HasTexCoord)
                        continue;

                    RenderTextureDecodeRequest request =
                        RenderTextureDecodeRequest.Create(
                            selectedPass.Image,
                            selectedPass.Texture.SamplerState,
                            includeAuthoredMipChain: true);
                    if (plannedWorldPrimaryTextureKeys.Add(request.Key))
                        worldPrimaryTextureRequests.Add(request);
                }
            }

            long worldPrimaryTextureBatchStart =
                System.Diagnostics.Stopwatch.GetTimestamp();
            RenderTextureDecodeBatch.DecodeUnique(
                worldPrimaryTextureRequests,
                imageStreams,
                textureCache,
                failedTextureCacheKeys,
                ref textureDecodedCount,
                ref textureDecodeSkippedCount);
            if (reportProgress is not null)
            {
                double decodeSeconds =
                    (double)(System.Diagnostics.Stopwatch.GetTimestamp() -
                        worldPrimaryTextureBatchStart) /
                    System.Diagnostics.Stopwatch.Frequency;
                reportProgress(
                    $"world primary textures ready: " +
                    $"{worldPrimaryTextureRequests.Count} unique ordered request(s), " +
                    $"{decodeSeconds:0.00}s parallel decode");
            }

            for (int surfaceIndex = 0; surfaceIndex < gfxMap.Dpvs.Surfaces.Count; surfaceIndex++)
            {
                long surfaceProfileStart = collectBuildProfiles
                    ? System.Diagnostics.Stopwatch.GetTimestamp()
                    : 0;
                if (surfaceIndex > 0 && surfaceIndex % 1000 == 0)
                    reportProgress?.Invoke($"building world surfaces {surfaceIndex}/{gfxMap.Dpvs.Surfaces.Count}");

                GfxSurface surface = gfxMap.Dpvs.Surfaces[surfaceIndex];
                int? selectedTechniqueSlot =
                    ResolvePreparedEditorTechniqueVariantSlot(
                        surface.PrimaryLightIndex,
                        GfxDrawSurfSurfaceType.Triangles,
                        editorPreviewWorldDrawMethod,
                        worldSourceBuildResult.Source?.SceneLights.Source?
                            .SelectorState,
                        MapRenderTechniqueVariantAllocation.Unshadowed);
                int? shadowAllocatedTechniqueSlot =
                    ResolvePreparedEditorTechniqueVariantSlot(
                        surface.PrimaryLightIndex,
                        GfxDrawSurfSurfaceType.Triangles,
                        editorPreviewWorldDrawMethod,
                        worldSourceBuildResult.Source?.SceneLights.Source?
                            .SelectorState,
                        MapRenderTechniqueVariantAllocation
                            .ShadowMapAllocated);
                int? pageOneUnshadowedTechniqueSlot =
                    ResolvePreparedEditorTechniqueVariantSlot(
                        surface.PrimaryLightIndex,
                        GfxDrawSurfSurfaceType.TrianglesNoSunShadow,
                        editorPreviewWorldDrawMethod,
                        worldSourceBuildResult.Source?.SceneLights.Source?
                            .SelectorState,
                        MapRenderTechniqueVariantAllocation.Unshadowed);
                int? pageOneShadowAllocatedTechniqueSlot =
                    ResolvePreparedEditorTechniqueVariantSlot(
                        surface.PrimaryLightIndex,
                        GfxDrawSurfSurfaceType.TrianglesNoSunShadow,
                        editorPreviewWorldDrawMethod,
                        worldSourceBuildResult.Source?.SceneLights.Source?
                            .SelectorState,
                        MapRenderTechniqueVariantAllocation
                            .ShadowMapAllocated);
                PreparedWorldSurfaceGeometry surfaceGeometry =
                    preparedWorldSurfaces[surfaceIndex];
                MaterialAsset? material = surface.Material ?? lookup.ResolveMaterial(surface.MaterialPointer);
                MaterialTechniqueSetAsset? techset = material is null
                    ? null
                    : ResolveCachedTechniqueSet(material);
                MapRenderEditorDepthPrepassPlan? editorDepthPrepass =
                    material is null
                        ? null
                        : ResolveCachedEditorDepthPrepass(material, techset);
                bool isSkyMaterial = string.Equals(techset?.Name, "wc_sky", StringComparison.Ordinal);
                bool hasDedicatedSkySubmission = submittedSkySurfaceIndices.Contains(surfaceIndex);
                if (isSkyMaterial)
                {
                    skyMaterialSurfaceCount++;
                    skyMaterialTriangleCount += surface.Triangles.TriCount;
                }

                if (!includeDiagnosticGeometry && !isSkyMaterial)
                    bounds = IncludeBounds(bounds, surfaceGeometry.Bounds);
                IReadOnlyList<SelectedColorPass> rendererSelectedPasses =
                    material is not null
                        ? ResolveCachedEditorMaterialPasses(
                            material,
                            techset,
                            selectedTechniqueSlot)
                        : [];
                if (skyOrdinalsBySurface.TryGetValue(
                        surfaceIndex,
                        out List<int>? owningSkyOrdinals))
                {
                    foreach (int skyOrdinal in owningSkyOrdinals)
                    {
                        skyShaderObservedSurfaceCounts[skyOrdinal]++;
                        MapRenderSky sky = skies[skyOrdinal];
                        if (!hasDedicatedSkySubmission ||
                            !isSkyMaterial ||
                            material is null ||
                            techset is null ||
                            rendererSelectedPasses.Count != 1 ||
                            sky.Texture.Target !=
                                TextureTarget.TextureCube)
                        {
                            skyShaderConflicts[skyOrdinal] = true;
                            continue;
                        }

                        SelectedColorPass selectedSkyPass =
                            rendererSelectedPasses[0];
                        var skySampler = new MaterialSamplerBinding(
                            Identity: new MaterialSamplerIdentity(
                                SamplerArgIndex: -1,
                                SamplerDest: 0,
                                SamplerHash: 0,
                                TextureSemantic: 0),
                            sky.Texture.Name,
                            sky.Texture,
                            UvRoute: null);
                        ShaderExecutionContract skyExecution =
                            BuildShaderExecutionContract(
                                material,
                                techset,
                                lookup,
                                selectedSkyPass,
                                [skySampler],
                                vertexInputPayloadReady: true,
                                vertexInputPayloadBlocker: string.Empty,
                                authoredSourcePassAvailable: true,
                                purpose:
                                    ShaderExecutionPurpose
                                        .CameraColor,
                                shaderTranslationCache,
                                explicitCubeSamplerDestinations:
                                    explicitSkyCubeSamplerDestinations);
                        bool exactSkyPair =
                            skyExecution.ProgramIrReady &&
                            skyExecution.VertexProgramIr is not null &&
                            skyExecution.FragmentProgramIr is not null &&
                            skyExecution.ProgramSamplerDestinations
                                .SequenceEqual([0]) &&
                            skyExecution.MaterialSamplerDestinations.Count == 0 &&
                            skyExecution.CustomSamplerDestinations.Count == 1 &&
                            skyExecution.CustomSamplerDestinations[0]
                                .Destination == 0 &&
                            string.Equals(
                                skyExecution.CustomSamplerDestinations[0]
                                    .TextureTarget,
                                "TextureCube",
                                StringComparison.Ordinal) &&
                            skyExecution.CodeSamplerDestinations.Count == 0 &&
                            skyExecution.RuntimeSamplerRequirements.Count == 0;
                        if (!exactSkyPair)
                        {
                            skyShaderConflicts[skyOrdinal] = true;
                            continue;
                        }

                        if (skyShaderPasses[skyOrdinal] is null)
                        {
                            skyShaderPasses[skyOrdinal] =
                                selectedSkyPass.Pass;
                            skyShaderPrimarySamplers[skyOrdinal] =
                                selectedSkyPass.PrimarySampler;
                            skyShaderTexCoordSources[skyOrdinal] =
                                selectedSkyPass.TexCoordSource;
                            skyShaderExecutions[skyOrdinal] = skyExecution;
                        }
                        else if (skyShaderPasses[skyOrdinal] !=
                                     selectedSkyPass.Pass ||
                                 skyShaderPrimarySamplers[skyOrdinal] !=
                                     selectedSkyPass.PrimarySampler ||
                                 skyShaderTexCoordSources[skyOrdinal] !=
                                     selectedSkyPass.TexCoordSource ||
                                 !string.Equals(
                                     skyShaderExecutions[skyOrdinal]!
                                         .ProgramCacheKey,
                                     skyExecution.ProgramCacheKey,
                                     StringComparison.Ordinal) ||
                                 skyShaderExecutions[skyOrdinal]!
                                     .VertexProgramIr!.Identity !=
                                     skyExecution.VertexProgramIr!.Identity ||
                                 skyShaderExecutions[skyOrdinal]!
                                     .FragmentProgramIr!.Identity !=
                                     skyExecution.FragmentProgramIr!.Identity)
                        {
                            skyShaderConflicts[skyOrdinal] = true;
                        }
                    }
                }
                IReadOnlyList<SelectedColorPass>
                    shadowAllocatedRendererSelectedPasses =
                        material is not null &&
                        shadowAllocatedTechniqueSlot is not null
                            ? ResolveCachedEditorMaterialPasses(
                                material,
                                techset,
                                shadowAllocatedTechniqueSlot)
                            : [];
                IReadOnlyList<SelectedColorPass>
                    pageOneUnshadowedRendererSelectedPasses =
                        material is not null &&
                        pageOneUnshadowedTechniqueSlot is not null
                            ? ResolveCachedEditorMaterialPasses(
                                material,
                                techset,
                                pageOneUnshadowedTechniqueSlot)
                            : [];
                IReadOnlyList<SelectedColorPass>
                    pageOneShadowAllocatedRendererSelectedPasses =
                        material is not null &&
                        pageOneShadowAllocatedTechniqueSlot is not null
                            ? ResolveCachedEditorMaterialPasses(
                                material,
                                techset,
                                pageOneShadowAllocatedTechniqueSlot)
                            : [];

                if (worldReceiverRequirements is not null)
                {
                    MapRenderWorldReceiverVariantRequirement requirement =
                        MapRenderWorldReceiverVariantRequirement.None;
                    if (!hasDedicatedSkySubmission)
                    {
                        if (rendererSelectedPasses.Count > 0 ||
                            ResolveCachedWorldReceiverRequirement(
                                material,
                                techset,
                                selectedTechniqueSlot))
                        {
                            requirement |=
                                MapRenderWorldReceiverVariantRequirement
                                    .PageZeroUnshadowed;
                        }
                        if (shadowAllocatedRendererSelectedPasses.Count > 0 ||
                            ResolveCachedWorldReceiverRequirement(
                                material,
                                techset,
                                shadowAllocatedTechniqueSlot))
                        {
                            requirement |=
                                MapRenderWorldReceiverVariantRequirement
                                    .PageZeroShadowMapAllocated;
                        }
                        if (pageOneUnshadowedRendererSelectedPasses.Count > 0 ||
                            ResolveCachedWorldReceiverRequirement(
                                material,
                                techset,
                                pageOneUnshadowedTechniqueSlot))
                        {
                            requirement |=
                                MapRenderWorldReceiverVariantRequirement
                                    .PageOneUnshadowed;
                        }
                        if (pageOneShadowAllocatedRendererSelectedPasses.Count >
                                0 ||
                            ResolveCachedWorldReceiverRequirement(
                                material,
                                techset,
                                pageOneShadowAllocatedTechniqueSlot))
                        {
                            requirement |=
                                MapRenderWorldReceiverVariantRequirement
                                    .PageOneShadowMapAllocated;
                        }
                    }

                    worldReceiverRequirements[surfaceIndex] = requirement;
                }

                SelectedColorPass? materialColorUvSelectedPass = material is null
                    ? null
                    : ResolveCachedMaterialColorUvPass(material, techset);
                MapRenderWorldCameraColorPhasePlan? cameraColorPhase =
                    material is null
                        ? null
                        : ResolveCachedWorldCameraColorPhase(
                            material,
                            techset,
                            selectedTechniqueSlot,
                            rendererSelectedPasses.Count > 0);
                // The base preview remains a separate completed visualization
                // channel. A PS3 no-camera-color selection owns the phase,
                // however, so substituting that preview would turn depth-only
                // utility geometry into visible camera-color polygons.
                SelectedColorPass?[] fallbackCandidates =
                    cameraColorPhase?.SuppressGenericCameraColorFallback == true
                        ? []
                        : [materialColorUvSelectedPass];
                SelectedColorPass[] fallbackPasses = fallbackCandidates
                    .OfType<SelectedColorPass>()
                    .ToArray();
                // A runtime-selected authored group is atomic. The completed base
                // preview is prepared as a distinct retained channel; it never fills
                // a missing pass inside that authored group.
                IReadOnlyList<SelectedColorPass> selectedPasses =
                    rendererSelectedPasses
                        .Concat(shadowAllocatedRendererSelectedPasses)
                        .Concat(pageOneUnshadowedRendererSelectedPasses)
                        .Concat(pageOneShadowAllocatedRendererSelectedPasses)
                        .Concat(fallbackPasses)
                        .ToArray();
                // A successfully materialized GfxSky owns this surface. Keep the
                // ordinary material path available when package/cubemap resolution
                // failed so missing external image data cannot erase the surface.
                if (hasDedicatedSkySubmission)
                    selectedPasses = [];

                if (!hasDedicatedSkySubmission)
                {
                    if (selectedPasses.Count == 0)
                        materialTextureMissingSurfaceCount++;
                    else
                        materialTextureCandidateSurfaceCount++;
                }

                bool rendererTextureApplied = false;

                if (collectBuildProfiles)
                {
                    worldSetupProfileTicks +=
                        System.Diagnostics.Stopwatch.GetTimestamp() - surfaceProfileStart;
                }
                long solidGeometryProfileStart = collectBuildProfiles
                    ? System.Diagnostics.Stopwatch.GetTimestamp()
                    : 0;
                if (includeDiagnosticGeometry)
                {
                    Vector3 color = ColorFor(
                        material?.Info.Name ??
                        $"surface_{surfaceIndex}");
                    int firstSurfacePickIndex = solidIndices.Count;
                    int surfaceTriangles = AddSolidSurface(
                        surfaceGeometry,
                        solidVertices,
                        solidIndices,
                        color,
                        includeInBounds: !isSkyMaterial,
                        ref bounds,
                        out int surfaceSolidSkippedTriangles,
                        out int surfaceSolidReadFailureTriangles,
                        out int surfaceSolidSkyboxTriangles);
                    AddPickRange(
                        solidPickRanges,
                        MapRenderPickKind.GfxSurface,
                        surfaceIndex,
                        surfaceIndex,
                        firstSurfacePickIndex,
                        solidIndices.Count,
                        material?.Info.Name ?? $"surface_{surfaceIndex}");
                    skippedTriangleCount += surfaceSolidSkippedTriangles;
                    geometryReadFailureTriangleCount +=
                        surfaceSolidReadFailureTriangles;
                    geometrySkyboxTriangleCount +=
                        surfaceSolidSkyboxTriangles;

                    if (surfaceTriangles == 0)
                        skippedSurfaceCount++;
                    else
                    {
                        triangleCount += surfaceTriangles;
                        drawnSurfaceCount++;
                    }
                }
                if (collectBuildProfiles)
                {
                    worldSolidGeometryProfileTicks +=
                        System.Diagnostics.Stopwatch.GetTimestamp() - solidGeometryProfileStart;
                }

                long texturedPipelineProfileStart = collectBuildProfiles
                    ? System.Diagnostics.Stopwatch.GetTimestamp()
                    : 0;
                bool countedSelectorTexturedSurface = false;
                bool rendererUvUnresolved = false;
                bool rendererTextureDecodeFailed = false;
                bool rendererGeometryBuildFailed = false;
                bool rendererStateDecoded = false;
                bool rendererHasUnresolvedCodeSampler = false;
                int rendererUvFailedTriangleCount = 0;
                var pendingSelectorSubmissions = new List<PreparedWorldTexturedSubmission>(rendererSelectedPasses.Count);
                var pendingShadowAllocatedSubmissions =
                    new List<PreparedWorldTexturedSubmission>(
                        shadowAllocatedRendererSelectedPasses.Count);
                var pendingPageOneUnshadowedSubmissions =
                    new List<PreparedWorldTexturedSubmission>(
                        pageOneUnshadowedRendererSelectedPasses.Count);
                var pendingPageOneShadowAllocatedSubmissions =
                    new List<PreparedWorldTexturedSubmission>(
                        pageOneShadowAllocatedRendererSelectedPasses.Count);
                var pendingRetainedPreviewSubmissions = new List<PreparedWorldTexturedSubmission>(1);
                bool isolateEditorTranslucentPassGroup =
                    rendererSelectedPasses
                        .Concat(shadowAllocatedRendererSelectedPasses)
                        .Concat(pageOneUnshadowedRendererSelectedPasses)
                        .Concat(pageOneShadowAllocatedRendererSelectedPasses)
                        .Any(pass =>
                        pass.State.HasState && pass.State.BlendEnabled);

                void CommitPreparedSubmission(
                    PreparedWorldTexturedSubmission submission,
                    Dictionary<WorldTexturedBatchKey, TexturedBatchBuilder>
                        targetBatches,
                    bool updatePreviewDiagnostics)
                {
                    SelectedColorPass selectedPass = submission.SelectedPass;
                    AppendTexturedSurface(
                        targetBatches,
                        selectedPass.Pass,
                        selectedPass.PrimarySampler,
                        submission.Texture,
                        submission.LightmapTexture,
                        submission.ColorLayers,
                        submission.MaterialSamplers,
                        submission.ShaderExecution,
                        submission.UvRoute,
                        submission.RenderState,
                        submission.EditorDepthPrepass,
                        submission.DepthPrepassShaderExecution,
                        selectedPass.UnresolvedCodeSamplerCount,
                        submission.Vertices,
                        submission.RsxVertexInputs,
                        submission.Indices,
                        submission.PickRange,
                        isolateEditorTranslucentPassGroup
                            ? surfaceIndex
                            : null,
                        surface.PrimaryLightIndex);
                    if (!updatePreviewDiagnostics)
                        return;

                    if (submission.IsEditorTechniquePass)
                    {
                        authoredCandidateTexturedTriangleCount +=
                            submission.TexturedTriangleCount;
                        if (!countedSelectorTexturedSurface)
                        {
                            authoredCandidateTexturedSurfaceCount++;
                            countedSelectorTexturedSurface = true;
                        }
                    }
                    else if (submission.IsFallbackPass)
                    {
                        genericFallbackTexturedSurfaceCount++;
                        genericFallbackTexturedTriangleCount += submission.TexturedTriangleCount;
                    }
                    else
                    {
                        authoredCandidateTexturedSurfaceCount++;
                        authoredCandidateTexturedTriangleCount += submission.TexturedTriangleCount;
                    }

                    rendererTextureApplied = true;
                    rendererStateDecoded |= selectedPass.State.HasState;
                    rendererHasUnresolvedCodeSampler |= selectedPass.UnresolvedCodeSamplerCount > 0;
                }

                for (int selectedPassIndex = 0; selectedPassIndex < selectedPasses.Count; selectedPassIndex++)
                {
                    int pageZeroUnshadowedEnd =
                        rendererSelectedPasses.Count;
                    int pageZeroShadowAllocatedEnd =
                        pageZeroUnshadowedEnd +
                        shadowAllocatedRendererSelectedPasses.Count;
                    int pageOneUnshadowedEnd =
                        pageZeroShadowAllocatedEnd +
                        pageOneUnshadowedRendererSelectedPasses.Count;
                    int pageOneShadowAllocatedEnd =
                        pageOneUnshadowedEnd +
                        pageOneShadowAllocatedRendererSelectedPasses.Count;
                    bool isPreviewSelectorPass =
                        selectedPassIndex < pageZeroUnshadowedEnd;
                    bool isShadowAllocatedSelectorPass =
                        selectedPassIndex >= pageZeroUnshadowedEnd &&
                        selectedPassIndex < pageZeroShadowAllocatedEnd;
                    bool isPageOneUnshadowedSelectorPass =
                        selectedPassIndex >= pageZeroShadowAllocatedEnd &&
                        selectedPassIndex < pageOneUnshadowedEnd;
                    bool isPageOneShadowAllocatedSelectorPass =
                        selectedPassIndex >= pageOneUnshadowedEnd &&
                        selectedPassIndex < pageOneShadowAllocatedEnd;
                    bool isSelectorPass =
                        isPreviewSelectorPass ||
                        isShadowAllocatedSelectorPass ||
                        isPageOneUnshadowedSelectorPass ||
                        isPageOneShadowAllocatedSelectorPass;
                    bool isSidecarSelectorPass =
                        isShadowAllocatedSelectorPass ||
                        isPageOneUnshadowedSelectorPass ||
                        isPageOneShadowAllocatedSelectorPass;
                    if (!isSelectorPass && rendererTextureApplied)
                        break;

                    SelectedColorPass selectedPass = selectedPasses[selectedPassIndex];
                    bool isBaseSurfaceSelectedPass = !isSelectorPass &&
                        ReferenceEquals(selectedPass, materialColorUvSelectedPass);
                    bool isFallbackSelectedPass = isBaseSurfaceSelectedPass;
                    bool isRetainedBasePreview = isFallbackSelectedPass && rendererSelectedPasses.Count > 0;
                    WorldVertexLayoutSelection worldVertexLayout =
                        ResolveCachedWorldVertexLayout(
                            techset,
                            selectedPass);
                    IReadOnlyList<PreparedColorLayer> preparedColorLayers = [];
                    Texture? texture = null;
                    // Every authored pass follows the same preparation path. Diagnostic
                    // quality is audited separately and never suppresses capabilities.
                    bool hasAuthoredSourcePass = !isRetainedBasePreview &&
                                                 selectedPass.AuthoredProgramExecutable;
                    ShaderVertexInputBinding[] selectedVertexInputs = hasAuthoredSourcePass
                        ? ResolveCachedSelectedVertexInputs(techset, selectedPass)
                        : [];
                    SelectedColorPass? depthPrepassSelection =
                        hasAuthoredSourcePass && editorDepthPrepass is not null
                            ? CreateStandardDepthPrepassSelection(
                                selectedPass,
                                editorDepthPrepass)
                            : null;
                    ShaderVertexInputBinding[] depthPrepassVertexInputs =
                        depthPrepassSelection is not null
                            ? ResolveCachedSelectedVertexInputs(
                                techset,
                                depthPrepassSelection)
                            : [];
                    bool depthPrepassVertexInputsCompatible =
                        TryMergeVertexInputBindings(
                            selectedVertexInputs,
                            depthPrepassVertexInputs,
                            out ShaderVertexInputBinding[]
                                materializedVertexInputs,
                            out string depthPrepassVertexInputBlocker);
                    WorldVertexDecoderSelection materialVertexSelection =
                        ResolveCachedWorldVertexDecoder(
                            worldVertexLayout,
                            selectedPass.TexCoordSource,
                            selectedPass.TexCoordSourceIsEngineRouted);
                    WorldVertexDecoder? materialVertexDecoder =
                        materialVertexSelection.Decoder;
                    UvRoute uvRoute =
                        materialVertexSelection.UvRoute;
                    if (materialVertexDecoder is null || !materialVertexDecoder.HasTexCoord)
                    {
                        if (!isSidecarSelectorPass)
                            rendererUvUnresolved = true;
                    }
                    else if (!Profile(collectBuildProfiles, ref primaryTextureProfileTicks, () => TryDecodeTexture(
                                 selectedPass.Image,
                                 selectedPass.Texture.SamplerState,
                                 imageStreams,
                                 textureCache,
                                 failedTextureCacheKeys,
                                 true,
                                 ref textureDecodedCount,
                                 ref textureDecodeSkippedCount,
                                 out texture)) ||
                             texture is null)
                    {
                        if (!isSidecarSelectorPass)
                            rendererTextureDecodeFailed = true;
                    }
                    else if (TryBuildTexturedSurface(
                                 surface,
                                 surfaceGeometry,
                                 vertexBytes,
                                 preparedColorLayers = Profile(
                                     collectBuildProfiles,
                                     ref colorLayerProfileTicks,
                                     () => PrepareWorldColorLayers(
                                     enableEditorMultiTexture:
                                         isSelectorPass,
                                     material,
                                     isSelectorPass
                                         ? ResolveCachedWorldMaterialSamplerPlan(
                                             material,
                                             techset,
                                             selectedPass)
                                         : WorldMaterialSamplerPlan.Empty,
                                     material is null
                                         ? null
                                         : ResolveCachedEditorMaterialTexturePlan(material),
                                     selectedPass,
                                     worldVertexLayout,
                                     ResolveCachedWorldVertexDecoder,
                                     texture,
                                     uvRoute,
                                     materialVertexDecoder,
                                     imageStreams,
                                     textureCache,
                                     failedTextureCacheKeys,
                                     ref textureDecodedCount,
                                     ref textureDecodeSkippedCount)),
                                 materializedVertexInputs,
                                 gfxMap.WorldDraw.VertexLayerData.PackedLayerData,
                                 allowUvValueSanitization: true,
                                 out List<float> surfaceVertices,
                                 out List<float> surfaceRsxVertexInputs,
                                 out bool surfaceRsxVertexInputsReady,
                                 out string surfaceRsxVertexInputBlocker,
                                 out List<uint> surfaceIndices,
                                 out int surfaceTexturedTriangles,
                                 out int surfaceSkippedTriangles,
                                 out int surfaceReadFailureTriangles,
                                 out int surfaceSkyboxTriangles,
                                 out int surfaceUvFailedTriangles,
                                 out int surfaceDegenerateUvTriangles,
                                 out bool surfaceLightmapUvReady,
                                 out RenderBounds surfaceBounds,
                                 useGenericFallback: !hasAuthoredSourcePass))
                    {
                        // Degenerate UVs are legal vertex payloads (for example a
                        // constant-color or reflection-driven material). Record them,
                        // but never discard otherwise valid geometry at the CPU stage.
                        // The old whole-surface rejection primarily masked decoder-row
                        // mistakes and created framebuffer-clear holes.
                        RenderState renderState = selectedPass.State;
                        long samplerBindingStart = collectBuildProfiles
                            ? System.Diagnostics.Stopwatch.GetTimestamp()
                            : 0;
                        MaterialColorLayer[] materialColorLayers =
                            preparedColorLayers
                                .Select(layer => layer.Layer)
                                .ToArray();
                        IReadOnlyList<MapRenderWorldMaterialSamplerBinding> materialSamplers =
                            !isRetainedBasePreview
                            ? PrepareWorldMaterialSamplerBindings(
                                ResolveCachedWorldMaterialSamplerPlan(
                                    material,
                                    techset,
                                    selectedPass),
                                worldTextureBindings,
                                selectedPass,
                                worldVertexLayout,
                                ResolveCachedWorldVertexDecoder,
                                gfxMap,
                                surface,
                                materialColorLayers,
                                imageStreams,
                                textureCache,
                                worldTextureCache,
                                failedTextureCacheKeys,
                                failedWorldTextureCacheKeys,
                                ref textureDecodedCount,
                                ref textureDecodeSkippedCount)
                            : CreateMaterialSamplerBindings(
                                materialColorLayers);
                        if (collectBuildProfiles)
                        {
                            samplerBindingProfileTicks +=
                                System.Diagnostics.Stopwatch.GetTimestamp() - samplerBindingStart;
                        }
                        Texture? lightmapTexture = surfaceLightmapUvReady
                            ? materialSamplers.FirstOrDefault(binding =>
                                binding.RuntimeTextureIdentity?.Kind ==
                                    MapRenderWorldRuntimeTextureKind.PrimaryLightmap &&
                                binding.Binding.Texture is not null)?.Binding.Texture
                            : null;
                        ShaderExecutionContract shaderExecution = ResolveShaderExecution(
                            material,
                            !isRetainedBasePreview ? techset : null,
                            selectedPass,
                            materialSamplers,
                            surfaceRsxVertexInputsReady,
                            surfaceRsxVertexInputBlocker,
                            authoredSourcePassAvailable: hasAuthoredSourcePass);
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
                                ResolveShaderExecution(
                                    material,
                                    techset,
                                    depthPrepassSelection,
                                    [],
                                    depthPayloadReady,
                                    depthPayloadBlocker,
                                    authoredSourcePassAvailable: true,
                                    purpose:
                                        ShaderExecutionPurpose
                                            .DepthOnly);
                            if (candidate.ProgramExecutionReady)
                                depthPrepassShaderExecution = candidate;
                        }
                        if (!isSidecarSelectorPass)
                        {
                            rendererUvFailedTriangleCount +=
                                surfaceUvFailedTriangles;
                        }
                        var preparedSubmission = new PreparedWorldTexturedSubmission(
                            selectedPass,
                            texture,
                            lightmapTexture,
                            materialColorLayers,
                            materialSamplers,
                            shaderExecution,
                            uvRoute,
                            renderState,
                            editorDepthPrepass,
                            depthPrepassShaderExecution,
                            surfaceVertices,
                            surfaceRsxVertexInputs,
                            surfaceIndices,
                            new MapRenderPickRange(
                                MapRenderPickKind.GfxSurface,
                                surfaceIndex,
                                surfaceIndex,
                                0,
                                0,
                                material?.Info.Name ?? $"surface_{surfaceIndex}"),
                            surfaceTexturedTriangles,
                            isSelectorPass,
                            isFallbackSelectedPass);
                        if (isPreviewSelectorPass)
                            pendingSelectorSubmissions.Add(preparedSubmission);
                        else if (isShadowAllocatedSelectorPass)
                        {
                            pendingShadowAllocatedSubmissions.Add(
                                preparedSubmission);
                        }
                        else if (isPageOneUnshadowedSelectorPass)
                        {
                            pendingPageOneUnshadowedSubmissions.Add(
                                preparedSubmission);
                        }
                        else if (isPageOneShadowAllocatedSelectorPass)
                        {
                            pendingPageOneShadowAllocatedSubmissions.Add(
                                preparedSubmission);
                        }
                        else if (rendererSelectedPasses.Count > 0)
                            pendingRetainedPreviewSubmissions.Add(preparedSubmission);
                        else
                        {
                            CommitPreparedSubmission(
                                preparedSubmission,
                                texturedBatchBuilders,
                                updatePreviewDiagnostics: true);
                        }
                    }
                    else
                    {
                        if (!isSidecarSelectorPass)
                        {
                            rendererGeometryBuildFailed = true;
                            rendererUvFailedTriangleCount +=
                                surfaceUvFailedTriangles;
                        }
                    }
                }
                if (rendererSelectedPasses.Count > 0)
                {
                    IReadOnlyList<PreparedWorldTexturedSubmission> authorizedSubmissions =
                        AuthorizeAtomicRendererPassSequence(
                            rendererSelectedPasses.Count,
                            pendingSelectorSubmissions,
                            submission =>
                                ReceiverVariantProgramReadyForBuild(
                                    submission.ShaderExecution,
                                    MapRenderTechniqueVariantAllocation
                                        .Unshadowed));
                    foreach (PreparedWorldTexturedSubmission submission in
                             authorizedSubmissions)
                    {
                        CommitPreparedSubmission(
                            submission,
                            pageZeroUnshadowedTexturedBatchBuilders,
                            updatePreviewDiagnostics: false);
                    }
                    IReadOnlyList<PreparedWorldTexturedSubmission> submissionsToCommit =
                        RetainCompletedStageAfterAtomicAuthorization(
                            authorizedSubmissions,
                            pendingRetainedPreviewSubmissions);
                    foreach (PreparedWorldTexturedSubmission submission in submissionsToCommit)
                    {
                        CommitPreparedSubmission(
                            submission,
                            texturedBatchBuilders,
                            updatePreviewDiagnostics: true);
                    }
                }
                if (shadowAllocatedRendererSelectedPasses.Count > 0)
                {
                    IReadOnlyList<PreparedWorldTexturedSubmission>
                        authorizedShadowSubmissions =
                            AuthorizeAtomicRendererPassSequence(
                                shadowAllocatedRendererSelectedPasses.Count,
                                pendingShadowAllocatedSubmissions,
                                submission =>
                                    ReceiverVariantProgramReadyForBuild(
                                        submission.ShaderExecution,
                                        MapRenderTechniqueVariantAllocation
                                            .ShadowMapAllocated));
                    foreach (PreparedWorldTexturedSubmission submission in
                             authorizedShadowSubmissions)
                    {
                        CommitPreparedSubmission(
                            submission,
                            shadowAllocatedTexturedBatchBuilders,
                            updatePreviewDiagnostics: false);
                    }
                }
                if (pageOneUnshadowedRendererSelectedPasses.Count > 0)
                {
                    IReadOnlyList<PreparedWorldTexturedSubmission>
                        authorizedPageOneUnshadowedSubmissions =
                            AuthorizeAtomicRendererPassSequence(
                                pageOneUnshadowedRendererSelectedPasses.Count,
                                pendingPageOneUnshadowedSubmissions,
                                submission =>
                                    ReceiverVariantProgramReadyForBuild(
                                        submission.ShaderExecution,
                                        MapRenderTechniqueVariantAllocation
                                            .Unshadowed));
                    foreach (PreparedWorldTexturedSubmission submission in
                             authorizedPageOneUnshadowedSubmissions)
                    {
                        CommitPreparedSubmission(
                            submission,
                            pageOneUnshadowedTexturedBatchBuilders,
                            updatePreviewDiagnostics: false);
                    }
                }
                if (pageOneShadowAllocatedRendererSelectedPasses.Count > 0)
                {
                    IReadOnlyList<PreparedWorldTexturedSubmission>
                        authorizedPageOneShadowAllocatedSubmissions =
                            AuthorizeAtomicRendererPassSequence(
                                pageOneShadowAllocatedRendererSelectedPasses
                                    .Count,
                                pendingPageOneShadowAllocatedSubmissions,
                                submission =>
                                    ReceiverVariantProgramReadyForBuild(
                                        submission.ShaderExecution,
                                        MapRenderTechniqueVariantAllocation
                                            .ShadowMapAllocated));
                    foreach (PreparedWorldTexturedSubmission submission in
                             authorizedPageOneShadowAllocatedSubmissions)
                    {
                        CommitPreparedSubmission(
                            submission,
                            pageOneShadowAllocatedTexturedBatchBuilders,
                            updatePreviewDiagnostics: false);
                    }
                }
                if (rendererUvUnresolved)
                    materialTextureUvUnresolvedSurfaceCount++;
                if (rendererTextureDecodeFailed)
                    materialTextureDecodeFailedSurfaceCount++;
                if (rendererGeometryBuildFailed)
                    materialTextureGeometryFailedSurfaceCount++;
                materialTextureUvFailedTriangleCount += rendererUvFailedTriangleCount;
                if (rendererStateDecoded)
                    renderStateDecodedCount++;
                if (rendererHasUnresolvedCodeSampler)
                    unresolvedCodeSamplerSurfaceCount++;
                if (collectBuildProfiles)
                {
                    worldTexturedPipelineProfileTicks +=
                        System.Diagnostics.Stopwatch.GetTimestamp() - texturedPipelineProfileStart;
                }

            }

            if (sceneTechniqueVariants is not null &&
                worldReceiverRequirements is not null)
            {
                sceneTechniqueVariants =
                    sceneTechniqueVariants.WithWorldReceiverRequirements(
                        worldReceiverRequirements);
            }

            skies = skies
                .Select((sky, skyOrdinal) =>
                {
                    int expectedSurfaceCount = sky.SurfaceIndices
                        .Distinct()
                        .Count();
                    return !skyShaderConflicts[skyOrdinal] &&
                           skyShaderObservedSurfaceCounts[skyOrdinal] ==
                               expectedSurfaceCount &&
                           skyShaderPasses[skyOrdinal] is not null &&
                           skyShaderExecutions[skyOrdinal] is not null
                        ? sky with
                        {
                            ShaderPass = skyShaderPasses[skyOrdinal],
                            ShaderPrimarySampler =
                                skyShaderPrimarySamplers[skyOrdinal],
                            ShaderTexCoordSource =
                                skyShaderTexCoordSources[skyOrdinal],
                            ShaderExecution =
                                skyShaderExecutions[skyOrdinal]
                        }
                        : sky;
                })
                .ToArray();

            if (reportProgress is not null)
            {
                static double ProfileSeconds(long ticks) =>
                    (double)ticks / System.Diagnostics.Stopwatch.Frequency;
                int compactSolidVertexCount = solidVertices.Count / MapRenderScene.VertexFloatCount;
                int compactTexturedVertexCount = texturedBatchBuilders.Values.Sum(
                    batch => batch.Vertices.Count / MapRenderScene.TexturedVertexFloatCount);
                int compactTexturedIndexCount = texturedBatchBuilders.Values.Sum(batch => batch.Indices.Count);
                reportProgress(
                    $"world surfaces ready: {gfxMap.Dpvs.Surfaces.Count}/{gfxMap.Dpvs.Surfaces.Count}; " +
                    $"profile setup={ProfileSeconds(worldSetupProfileTicks):0.00}s, " +
                    $"solid={ProfileSeconds(worldSolidGeometryProfileTicks):0.00}s, " +
                    $"textured={ProfileSeconds(worldTexturedPipelineProfileTicks):0.00}s " +
                    $"(primaryTexture={ProfileSeconds(primaryTextureProfileTicks):0.00}s, " +
                    $"layers={ProfileSeconds(colorLayerProfileTicks):0.00}s, " +
                    $"samplers={ProfileSeconds(samplerBindingProfileTicks):0.00}s, " +
                    $"contracts={ProfileSeconds(shaderContractProfileTicks):0.00}s); " +
                    $"indexed solid={compactSolidVertexCount}v/{solidIndices.Count}i, " +
                    $"textured={compactTexturedVertexCount}v/{compactTexturedIndexCount}i");
            }
            reportProgress?.Invoke(
                "building exact slot-2 world and static sun-shadow caster sidecars");
            sunShadowWorldCasterBatches = BuildWorldSunShadowCasterBatches(
                gfxMap,
                preparedWorldSurfaces,
                lookup,
                imageStreams,
                textureCache,
                failedTextureCacheKeys,
                ref textureDecodedCount,
                ref textureDecodeSkippedCount,
                out sunShadowWorldCasterRejections);
            sunShadowStaticCasterBatches = BuildStaticSunShadowCasterBatches(
                gfxMap,
                lightingAtlas,
                lookup,
                imageStreams,
                textureCache,
                failedTextureCacheKeys,
                ref textureDecodedCount,
                ref textureDecodeSkippedCount,
                out sunShadowStaticCasterExpectations);
            staticModelDrawInstCount = gfxMap.Dpvs.SModelDrawInsts.Count;
            reportProgress?.Invoke($"building {staticModelDrawInstCount} static-model instances");
            if (includeDiagnosticGeometry)
            {
                staticModelTriangleCount = AddStaticModelInstancedGeometry(
                    gfxMap,
                    lightingAtlas,
                    instancedSolidBatchBuilders,
                    ref bounds,
                    out staticModelDrawnCount,
                    out staticModelSkippedCount,
                    out staticModelTriangleSkippedCount,
                    out staticModelGeometryReadFailureTriangleCount,
                    out staticModelPlacementDecodeFailureCount,
                    out staticModelScheduling);
            }

            reportProgress?.Invoke(
                "preparing shared static-model LODs, material passes, and textures");
            PreparedStaticModelSource?[] preparedStaticSources =
                PrepareStaticModelSources(gfxMap);
            var staticSharedBuildCache =
                new StaticModelSharedBuildCache();
            IReadOnlyList<RenderTextureDecodeRequest>
                staticPrimaryTextureRequests =
                    PlanStaticModelPrimaryTextureDecodes(
                        gfxMap,
                        preparedStaticSources,
                        lookup,
                        editorPreviewWorldDrawMethod,
                        worldSourceBuildResult.Source?.SceneLights.Source?
                            .SelectorState,
                        staticSharedBuildCache);
            long staticPrimaryTextureBatchStart =
                System.Diagnostics.Stopwatch.GetTimestamp();
            RenderTextureDecodeBatch.DecodeUnique(
                staticPrimaryTextureRequests,
                imageStreams,
                textureCache,
                failedTextureCacheKeys,
                ref textureDecodedCount,
                ref textureDecodeSkippedCount);
            if (reportProgress is not null)
            {
                double decodeSeconds =
                    (double)(System.Diagnostics.Stopwatch.GetTimestamp() -
                        staticPrimaryTextureBatchStart) /
                    System.Diagnostics.Stopwatch.Frequency;
                reportProgress(
                    $"static-model primary textures ready: " +
                    $"{staticPrimaryTextureRequests.Count} unique ordered request(s), " +
                    $"{decodeSeconds:0.00}s parallel decode");
            }

            reportProgress?.Invoke(
                "building static models (1/4: primary, unshadowed)");
            int staticAuthoredCandidateTexturedSurfaceCount = 0;
            int staticAuthoredCandidateTexturedTriangleCount = 0;
            AddStaticModelTexturedGeometry(
                gfxMap,
                preparedStaticSources,
                staticSharedBuildCache,
                lightingAtlas,
                lookup,
                worldTextureBindings,
                editorPreviewWorldDrawMethod,
                worldSourceBuildResult.Source?.SceneLights.Source?
                    .SelectorState,
                GfxDrawSurfSurfaceType.StaticModelRigid,
                MapRenderTechniqueVariantAllocation.Unshadowed,
                allowPreviewFallback: true,
                forceGenericPreview: false,
                imageStreams,
                exactNormalCameraInstancedTexturedBatchBuilders,
                textureCache,
                worldTextureCache,
                failedTextureCacheKeys,
                failedWorldTextureCacheKeys,
                shaderTranslationCache,
                "building static models (1/4: primary, unshadowed)",
                reportProgress,
                ref textureDecodedCount,
                ref textureDecodeSkippedCount,
                out _,
                out _,
                out staticAuthoredCandidateTexturedSurfaceCount,
                out staticAuthoredCandidateTexturedTriangleCount,
                out IReadOnlyDictionary<int, RenderBounds>
                    exactStaticAllLodBounds,
                out IReadOnlyDictionary<int, uint>
                    exactStaticRenderableLodMasks);
            reportProgress?.Invoke(
                "building static models (generic normal-camera fallback)");
            AddStaticModelTexturedGeometry(
                gfxMap,
                preparedStaticSources,
                staticSharedBuildCache,
                lightingAtlas,
                lookup,
                worldTextureBindings,
                editorPreviewWorldDrawMethod,
                worldSourceBuildResult.Source?.SceneLights.Source?
                    .SelectorState,
                GfxDrawSurfSurfaceType.StaticModelRigid,
                MapRenderTechniqueVariantAllocation.Unshadowed,
                allowPreviewFallback: true,
                forceGenericPreview: true,
                imageStreams,
                instancedTexturedBatchBuilders,
                textureCache,
                worldTextureCache,
                failedTextureCacheKeys,
                failedWorldTextureCacheKeys,
                shaderTranslationCache,
                "building static models (generic normal-camera fallback)",
                reportProgress,
                ref textureDecodedCount,
                ref textureDecodeSkippedCount,
                out int staticPreviewFallbackSurfaceCount,
                out int staticPreviewFallbackTriangleCount,
                out _,
                out _,
                out IReadOnlyDictionary<int, RenderBounds>
                    staticPreviewAllLodBounds,
                out IReadOnlyDictionary<int, uint>
                    staticPreviewRenderableLodMasks);
            IReadOnlyDictionary<int, RenderBounds> staticAllLodBounds =
                MergeStaticModelBounds(
                    exactStaticAllLodBounds,
                    staticPreviewAllLodBounds);
            IReadOnlyDictionary<int, uint> staticRenderableLodMasks =
                MergeStaticModelLodMasks(
                    exactStaticRenderableLodMasks,
                    staticPreviewRenderableLodMasks);
            reportProgress?.Invoke(
                "building static models (2/4: primary, shadow allocated)");
            AddStaticModelTexturedGeometry(
                gfxMap,
                preparedStaticSources,
                staticSharedBuildCache,
                lightingAtlas,
                lookup,
                worldTextureBindings,
                editorPreviewWorldDrawMethod,
                worldSourceBuildResult.Source?.SceneLights.Source?
                    .SelectorState,
                GfxDrawSurfSurfaceType.StaticModelRigid,
                MapRenderTechniqueVariantAllocation.ShadowMapAllocated,
                allowPreviewFallback: false,
                forceGenericPreview: false,
                imageStreams,
                shadowAllocatedInstancedTexturedBatchBuilders,
                textureCache,
                worldTextureCache,
                failedTextureCacheKeys,
                failedWorldTextureCacheKeys,
                shaderTranslationCache,
                "building static models (2/4: primary, shadow allocated)",
                reportProgress,
                ref textureDecodedCount,
                ref textureDecodeSkippedCount,
                out _,
                out _,
                out _,
                out _,
                out _,
                out _);
            reportProgress?.Invoke(
                "building static models (3/4: page one, unshadowed)");
            AddStaticModelTexturedGeometry(
                gfxMap,
                preparedStaticSources,
                staticSharedBuildCache,
                lightingAtlas,
                lookup,
                worldTextureBindings,
                editorPreviewWorldDrawMethod,
                worldSourceBuildResult.Source?.SceneLights.Source?
                    .SelectorState,
                GfxDrawSurfSurfaceType.StaticModelRigidNoSunShadow,
                MapRenderTechniqueVariantAllocation.Unshadowed,
                allowPreviewFallback: false,
                forceGenericPreview: false,
                imageStreams,
                pageOneUnshadowedInstancedTexturedBatchBuilders,
                textureCache,
                worldTextureCache,
                failedTextureCacheKeys,
                failedWorldTextureCacheKeys,
                shaderTranslationCache,
                "building static models (3/4: page one, unshadowed)",
                reportProgress,
                ref textureDecodedCount,
                ref textureDecodeSkippedCount,
                out _,
                out _,
                out _,
                out _,
                out _,
                out _);
            reportProgress?.Invoke(
                "building static models (4/4: page one, shadow allocated)");
            AddStaticModelTexturedGeometry(
                gfxMap,
                preparedStaticSources,
                staticSharedBuildCache,
                lightingAtlas,
                lookup,
                worldTextureBindings,
                editorPreviewWorldDrawMethod,
                worldSourceBuildResult.Source?.SceneLights.Source?
                    .SelectorState,
                GfxDrawSurfSurfaceType.StaticModelRigidNoSunShadow,
                MapRenderTechniqueVariantAllocation.ShadowMapAllocated,
                allowPreviewFallback: false,
                forceGenericPreview: false,
                imageStreams,
                pageOneShadowAllocatedInstancedTexturedBatchBuilders,
                textureCache,
                worldTextureCache,
                failedTextureCacheKeys,
                failedWorldTextureCacheKeys,
                shaderTranslationCache,
                "building static models (4/4: page one, shadow allocated)",
                reportProgress,
                ref textureDecodedCount,
                ref textureDecodeSkippedCount,
                out _,
                out _,
                out _,
                out _,
                out _,
                out _);
            if (!includeDiagnosticGeometry)
            {
                var schedulingRows =
                    new List<MapRenderStaticModelSchedulingInfo>(
                        preparedStaticSources.Length);
                for (int objectIndex = 0;
                     objectIndex < preparedStaticSources.Length;
                     objectIndex++)
                {
                    if (preparedStaticSources[objectIndex] is not
                            { } preparedSource ||
                        !staticAllLodBounds.TryGetValue(
                            objectIndex,
                            out RenderBounds allLodBounds))
                    {
                        continue;
                    }

                    GfxStaticModelDrawInst drawInst =
                        gfxMap.Dpvs.SModelDrawInsts[objectIndex];
                    staticRenderableLodMasks.TryGetValue(
                        objectIndex,
                        out uint renderableLodMask);
                    schedulingRows.Add(
                        new MapRenderStaticModelSchedulingInfo(
                            objectIndex,
                            ToRenderCoordinates(
                                preparedSource.Placement.Origin),
                            drawInst.Placement.Scale,
                            drawInst.CullDist,
                            preparedSource.Model,
                            preparedSource.LodGeometries[0].LodIndex,
                            allLodBounds)
                        {
                            RenderableLodMask = renderableLodMask
                        });
                    bounds = IncludeBounds(bounds, allLodBounds);
                }

                staticModelScheduling = schedulingRows.ToArray();
            }
            else
            {
                staticModelScheduling = staticModelScheduling
                    .Select(scheduling =>
                    {
                        MapRenderStaticModelSchedulingInfo updated =
                            scheduling;
                        if (staticAllLodBounds.TryGetValue(
                                scheduling.ObjectIndex,
                                out RenderBounds allLodBounds))
                        {
                            updated = updated with
                            {
                                Bounds = IncludeBounds(
                                    scheduling.Bounds,
                                    allLodBounds)
                            };
                        }
                        if (staticRenderableLodMasks.TryGetValue(
                                scheduling.ObjectIndex,
                                out uint renderableLodMask))
                        {
                            updated = updated with
                            {
                                RenderableLodMask = renderableLodMask
                            };
                        }
                        return updated;
                    })
                    .ToArray();
            }
            genericFallbackTexturedSurfaceCount +=
                staticPreviewFallbackSurfaceCount;
            genericFallbackTexturedTriangleCount +=
                staticPreviewFallbackTriangleCount;
            authoredCandidateTexturedSurfaceCount += staticAuthoredCandidateTexturedSurfaceCount;
            authoredCandidateTexturedTriangleCount += staticAuthoredCandidateTexturedTriangleCount;
        }

        reportProgress?.Invoke("compacting CPU buffers");
        string name = input.GfxMap?.Name ?? input.ClipMap?.Name ?? Path.GetFileName(input.FastFilePath);
        RenderBounds cameraBounds = collisionBounds.IsValid ? collisionBounds : bounds;
        MapRenderEditorPreviewAtmospherePlan editorPreviewAtmosphere =
            MapRenderEditorPreviewAtmospherePlanner.Create(
                bounds,
                input.EditorPreviewAtmosphere);
        if (editorPreviewFallbackFogPending)
        {
            editorPreviewActiveFog = CreateEditorPreviewFallbackFog(
                editorPreviewAtmosphere,
                editorPreviewLighting);
            string fallbackDetail = editorPreviewCreateArtFog is not null
                ? editorPreviewCreateArtFog.Detail
                : "no GfxWorld map identity is available";
            reportProgress?.Invoke(
                $"Live Preview fog source=neutral fallback; {fallbackDetail}");
        }
        IReadOnlyDictionary<int, MapRenderEditorVegetationAnimationPlan>
            editorVegetationAnimationByGroupId =
                instancedTexturedBatchBuilders.Values
                    .Where(batch =>
                        batch.Indices.Count > 0 &&
                        batch.Instances.Count > 0)
                    .GroupBy(batch => batch.EditorDrawGroupId)
                    .ToDictionary(
                        group => group.Key,
                        group =>
                        {
                            InstancedTexturedBatchBuilder[] completeGroup =
                                group.ToArray();
                            InstancedTexturedBatchBuilder representative =
                                completeGroup[0];
                            return MapRenderEditorVegetationAnimationPlanner
                                .Create(
                                    completeGroup
                                        .Select(batch => batch.State)
                                        .ToArray(),
                                    representative.Pass.MaterialName,
                                    representative.Instances
                                        .Select(instance => instance.Name)
                                        .ToArray());
                        });
        IReadOnlyList<MapRenderStaticGenericPreviewBatchGroup<
            InstancedTexturedBatchBuilder>> genericPreviewBatchGroups =
                MapRenderStaticGenericPreviewBatchPlanner.Create(
                    instancedTexturedBatchBuilders.Where(entry =>
                        entry.Value.Indices.Count > 0 &&
                        entry.Value.Instances.Count > 0 &&
                        MapRenderOpenGlStaticCameraRegionPolicy
                            .AllowsGenericNormalCameraFallback(
                                entry.Key.Material.CameraRegion)));
        reportProgress?.Invoke(
            "compacted generic static preview batches: " +
            $"exactResourceBuckets={genericPreviewBatchGroups.Sum(group => group.ExactBatches.Count)} " +
            $"fallbackDrawBatches={genericPreviewBatchGroups.Count}");
        var genericPreviewDrawGroupIds =
            new Dictionary<StaticTexturedDrawGroupKey, int>();
        var immutableGeometryArrays =
            new MapRenderImmutableGeometryArrayPool();
        var genericPreviewMaterializationInputs =
            genericPreviewBatchGroups
                .Select(group =>
                {
                    if (!genericPreviewDrawGroupIds.TryGetValue(
                            group.DrawGroupKey,
                            out int editorDrawGroupId))
                    {
                        editorDrawGroupId =
                            genericPreviewDrawGroupIds.Count;
                        genericPreviewDrawGroupIds.Add(
                            group.DrawGroupKey,
                            editorDrawGroupId);
                    }

                    return (
                        Group: group,
                        EditorDrawGroupId: editorDrawGroupId);
                })
                .ToArray();
        var materializedStaticLodBatches =
            genericPreviewMaterializationInputs
            .AsParallel()
            .AsOrdered()
            .WithDegreeOfParallelism(
                Math.Max(
                    1,
                    Math.Min(Environment.ProcessorCount, 4)))
            .Select(input =>
            {
                MapRenderStaticGenericPreviewBatchGroup<
                    InstancedTexturedBatchBuilder> group = input.Group;
                InstancedTexturedBatchBuilder[] exactBatches =
                    group.ExactBatches
                        .Select(entry => entry.Value)
                        .ToArray();
                InstancedTexturedBatchBuilder representative =
                    exactBatches[0];

                MapRenderStaticModelInstance[] allInstances = exactBatches
                    .SelectMany(batch => batch.Instances)
                    .OrderBy(instance => instance.ObjectIndex)
                    .ThenBy(instance => instance.SurfaceIndex)
                    .ThenBy(instance => instance.ReflectionProbeIndex)
                    .ToArray();
                MapRenderStaticModelInstance[] preparedInstances =
                    exactBatches
                        .SelectMany(batch => batch.PreparedInstances)
                        .OrderBy(instance => instance.ObjectIndex)
                        .ThenBy(instance => instance.SurfaceIndex)
                        .ThenBy(instance => instance.ReflectionProbeIndex)
                        .ToArray();
                int[] preparedSourceOrdinals = exactBatches
                    .Where(batch => batch.PreparedInstances.Count > 0)
                    .Select(batch => batch.PreparedSourceOrdinal)
                    .ToArray();
                int preparedSourceOrdinal =
                    preparedSourceOrdinals.Length > 0
                        ? preparedSourceOrdinals.Min()
                        : -1;

                var allLodBatch = new MapRenderInstancedTexturedBatch(
                    representative.Pass,
                    representative.PrimarySampler,
                    representative.Texture,
                    representative.ColorLayers,
                    representative.MaterialSamplers,
                    representative.ShaderExecution,
                    representative.UvRoute,
                    representative.State,
                    representative.UnresolvedCodeSamplerCount,
                    immutableGeometryArrays.InternFloats(
                        representative.Vertices),
                    immutableGeometryArrays.InternUInts(
                        representative.Indices),
                    allInstances,
                    input.EditorDrawGroupId,
                    editorVegetationAnimationByGroupId[
                        representative.EditorDrawGroupId],
                    representative.LodIndex)
                {
                    SceneLightIndex = representative.SceneLightIndex,
                    EditorDepthPrepass =
                        representative.EditorDepthPrepass,
                    RsxVertexInputs =
                        immutableGeometryArrays.InternFloats(
                            representative.RsxVertexInputs),
                    DepthPrepassShaderExecution =
                        representative.DepthPrepassShaderExecution,
                    IsGenericPreviewOnly = true
                };
                return (
                    AllLodBatch: allLodBatch,
                    PreparedSourceOrdinal: preparedSourceOrdinal,
                    PreparedInstances: preparedInstances);
            })
            .ToArray();
        MapRenderInstancedTexturedBatch[] preparedStaticTexturedBatches =
            materializedStaticLodBatches
                .Where(entry => entry.PreparedInstances.Length > 0)
                .OrderBy(entry => entry.PreparedSourceOrdinal)
                .Select(entry => entry.AllLodBatch with
                {
                    Instances = entry.PreparedInstances
                })
                .ToArray();
        MapRenderInstancedTexturedBatch[] allStaticLodTexturedBatches =
            materializedStaticLodBatches
                .Select(entry => entry.AllLodBatch)
                .ToArray();

        MapRenderInstancedTexturedBatch[] MaterializeExactStaticBatches(
            Dictionary<StaticTexturedBatchKey,
                InstancedTexturedBatchBuilder> builders,
            Func<MapRenderStaticModelInstance, bool> includeInstance)
        {
            ArgumentNullException.ThrowIfNull(includeInstance);
            var candidates = builders.Values
                .Where(batch =>
                    batch.IsExactTechniqueVariant &&
                    batch.Indices.Count > 0 &&
                    batch.Instances.Count > 0)
                .Select(batch => (
                    Batch: batch,
                    Instances: batch.Instances
                        .Where(includeInstance)
                        .ToArray()))
                .Where(candidate => candidate.Instances.Length > 0)
                .ToArray();
            IReadOnlyDictionary<int, MapRenderEditorVegetationAnimationPlan>
                animationByGroupId = candidates
                    .GroupBy(candidate =>
                        candidate.Batch.EditorDrawGroupId)
                    .ToDictionary(
                        group => group.Key,
                        group =>
                        {
                            var completeGroup =
                                group.ToArray();
                            InstancedTexturedBatchBuilder representative =
                                completeGroup[0].Batch;
                            return MapRenderEditorVegetationAnimationPlanner
                                .Create(
                                    completeGroup
                                        .Select(candidate =>
                                            candidate.Batch.State)
                                        .ToArray(),
                                    representative.Pass.MaterialName,
                                    completeGroup[0].Instances
                                        .Select(instance => instance.Name)
                                        .ToArray());
                        });
            return candidates
                .Select(candidate =>
                    new MapRenderInstancedTexturedBatch(
                        candidate.Batch.Pass,
                        candidate.Batch.PrimarySampler,
                        candidate.Batch.Texture,
                        candidate.Batch.ColorLayers,
                        candidate.Batch.MaterialSamplers,
                        candidate.Batch.ShaderExecution,
                        candidate.Batch.UvRoute,
                        candidate.Batch.State,
                        candidate.Batch.UnresolvedCodeSamplerCount,
                        immutableGeometryArrays.InternFloats(
                            candidate.Batch.Vertices),
                        immutableGeometryArrays.InternUInts(
                            candidate.Batch.Indices),
                        candidate.Instances,
                        candidate.Batch.EditorDrawGroupId,
                        animationByGroupId[
                            candidate.Batch.EditorDrawGroupId],
                        candidate.Batch.LodIndex)
                    {
                        SceneLightIndex =
                            candidate.Batch.SceneLightIndex,
                        EditorDepthPrepass =
                            candidate.Batch.EditorDepthPrepass,
                        RsxVertexInputs =
                            immutableGeometryArrays.InternFloats(
                                candidate.Batch.RsxVertexInputs),
                        DepthPrepassShaderExecution =
                            candidate.Batch.DepthPrepassShaderExecution
                    })
                .ToArray();
        }

        MapRenderTexturedBatch[] MaterializeWorldReceiverBatches(
            Dictionary<WorldTexturedBatchKey, TexturedBatchBuilder> builders) =>
            builders.Values
                .Where(batch => batch.Indices.Count > 0)
                .Select(batch => new MapRenderTexturedBatch(
                    batch.Pass,
                    batch.PrimarySampler,
                    batch.Texture,
                    batch.LightmapTexture,
                    batch.ColorLayers,
                    batch.MaterialSamplers,
                    batch.ShaderExecution,
                    batch.ShaderExecutionStatus,
                    batch.UvRoute,
                    batch.State,
                    batch.UnresolvedCodeSamplerCount,
                    batch.PickRanges.ToArray(),
                    immutableGeometryArrays.InternFloats(batch.Vertices),
                    immutableGeometryArrays.InternFloats(
                        batch.RsxVertexInputs),
                    immutableGeometryArrays.InternUInts(batch.Indices))
                {
                    SceneLightIndex = batch.SceneLightIndex,
                    EditorDepthPrepass = batch.EditorDepthPrepass,
                    DepthPrepassShaderExecution =
                        batch.DepthPrepassShaderExecution
                })
                .ToArray();

        MapRenderTexturedBatch[] pageZeroUnshadowedWorldReceiverBatches = [];
        MapRenderTexturedBatch[] shadowAllocatedWorldTexturedBatches = [];
        MapRenderTexturedBatch[] pageOneUnshadowedWorldReceiverBatches = [];
        MapRenderTexturedBatch[]
            pageOneShadowAllocatedWorldReceiverBatches = [];
        MapRenderInstancedTexturedBatch[]
            pageZeroUnshadowedStaticModelReceiverBatches = [];
        MapRenderInstancedTexturedBatch[]
            shadowAllocatedStaticModelTexturedBatches = [];
        MapRenderInstancedTexturedBatch[]
            pageOneUnshadowedStaticModelReceiverBatches = [];
        MapRenderInstancedTexturedBatch[]
            pageOneShadowAllocatedStaticModelReceiverBatches = [];
        MapRenderInstancedTexturedBatch[]
            exactNormalCameraStaticModelTexturedBatches = [];
        Parallel.Invoke(
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(
                    1,
                    Math.Min(Environment.ProcessorCount, 4))
            },
            () => pageZeroUnshadowedWorldReceiverBatches =
                MaterializeWorldReceiverBatches(
                    pageZeroUnshadowedTexturedBatchBuilders),
            () => shadowAllocatedWorldTexturedBatches =
                MaterializeWorldReceiverBatches(
                    shadowAllocatedTexturedBatchBuilders),
            () => pageOneUnshadowedWorldReceiverBatches =
                MaterializeWorldReceiverBatches(
                    pageOneUnshadowedTexturedBatchBuilders),
            () => pageOneShadowAllocatedWorldReceiverBatches =
                MaterializeWorldReceiverBatches(
                    pageOneShadowAllocatedTexturedBatchBuilders),
            () => pageZeroUnshadowedStaticModelReceiverBatches =
                MaterializeExactStaticBatches(
                    exactNormalCameraInstancedTexturedBatchBuilders,
                    instance =>
                        MapRenderStaticModelReceiverRouting
                            .CanPrepareAuthoredRegion(
                                MapRenderStaticModelReceiverPage
                                    .StaticModelRigidPage2,
                                instance.CameraRegion)),
            () => shadowAllocatedStaticModelTexturedBatches =
                MaterializeExactStaticBatches(
                    shadowAllocatedInstancedTexturedBatchBuilders,
                    instance =>
                        MapRenderStaticModelReceiverRouting
                            .CanPrepareAuthoredRegion(
                                MapRenderStaticModelReceiverPage
                                    .StaticModelRigidPage2,
                                instance.CameraRegion)),
            () => pageOneUnshadowedStaticModelReceiverBatches =
                MaterializeExactStaticBatches(
                    pageOneUnshadowedInstancedTexturedBatchBuilders,
                    instance =>
                        MapRenderStaticModelReceiverRouting
                            .CanPrepareAuthoredRegion(
                                MapRenderStaticModelReceiverPage
                                    .StaticModelRigidNoSunShadowPage3,
                                instance.CameraRegion)),
            () => pageOneShadowAllocatedStaticModelReceiverBatches =
                MaterializeExactStaticBatches(
                    pageOneShadowAllocatedInstancedTexturedBatchBuilders,
                    instance =>
                        MapRenderStaticModelReceiverRouting
                            .CanPrepareAuthoredRegion(
                                MapRenderStaticModelReceiverPage
                                    .StaticModelRigidNoSunShadowPage3,
                                instance.CameraRegion)),
            () => exactNormalCameraStaticModelTexturedBatches =
                MaterializeExactStaticBatches(
                    exactNormalCameraInstancedTexturedBatchBuilders,
                    instance =>
                        MapRenderOpenGlStaticCameraRegionPolicy
                            .OwnsNormalCameraColor(
                                instance.CameraRegion)));

        MapRenderStaticModelRunPlan preparedStaticRunPlan =
            MapRenderStaticModelRunPlanner.Create(
                preparedStaticTexturedBatches);
        preparedStaticTexturedBatches =
            preparedStaticRunPlan.Batches.ToArray();
        MapRenderStaticModelRunPlan allLodStaticRunPlan =
            MapRenderStaticModelRunPlanner.Create(
                allStaticLodTexturedBatches);
        allStaticLodTexturedBatches =
            allLodStaticRunPlan.Batches.ToArray();
        MapRenderStaticModelRunPlan exactNormalCameraStaticRunPlan =
            MapRenderStaticModelRunPlanner.Create(
                exactNormalCameraStaticModelTexturedBatches);
        exactNormalCameraStaticModelTexturedBatches =
            exactNormalCameraStaticRunPlan.Batches.ToArray();
        MapRenderStaticModelRunPlan pageZeroUnshadowedStaticRunPlan =
            MapRenderStaticModelRunPlanner.Create(
                pageZeroUnshadowedStaticModelReceiverBatches);
        pageZeroUnshadowedStaticModelReceiverBatches =
            pageZeroUnshadowedStaticRunPlan.Batches.ToArray();
        MapRenderStaticModelRunPlan pageZeroShadowAllocatedStaticRunPlan =
            MapRenderStaticModelRunPlanner.Create(
                shadowAllocatedStaticModelTexturedBatches);
        shadowAllocatedStaticModelTexturedBatches =
            pageZeroShadowAllocatedStaticRunPlan.Batches.ToArray();
        MapRenderStaticModelRunPlan pageOneUnshadowedStaticRunPlan =
            MapRenderStaticModelRunPlanner.Create(
                pageOneUnshadowedStaticModelReceiverBatches);
        pageOneUnshadowedStaticModelReceiverBatches =
            pageOneUnshadowedStaticRunPlan.Batches.ToArray();
        MapRenderStaticModelRunPlan pageOneShadowAllocatedStaticRunPlan =
            MapRenderStaticModelRunPlanner.Create(
                pageOneShadowAllocatedStaticModelReceiverBatches);
        pageOneShadowAllocatedStaticModelReceiverBatches =
            pageOneShadowAllocatedStaticRunPlan.Batches.ToArray();
        MapRenderStaticModelRunPlan[] staticRunPlans =
        [
            preparedStaticRunPlan,
            allLodStaticRunPlan,
            exactNormalCameraStaticRunPlan,
            pageZeroUnshadowedStaticRunPlan,
            pageZeroShadowAllocatedStaticRunPlan,
            pageOneUnshadowedStaticRunPlan,
            pageOneShadowAllocatedStaticRunPlan
        ];
        reportProgress?.Invoke(
            "native static runs ready: " +
            $"channels={staticRunPlans.Length}, " +
            "runCapacity=unbounded-host, " +
            $"sourcePassBatches={staticRunPlans.Sum(plan => plan.SourceBatchCount)}, " +
            $"geometryGroups={staticRunPlans.Sum(plan => plan.SourceDrawGroupCount)}, " +
            $"selectedInstanceRows={staticRunPlans.Sum(plan => plan.SelectedInstanceRowCount)}, " +
            $"compatibleBuckets={staticRunPlans.Sum(plan => plan.BucketCount)}, " +
            $"runs={staticRunPlans.Sum(plan => plan.RunCount)}, " +
            $"capContinuationRuns={staticRunPlans.Sum(plan => plan.AdditionalRunCount)}, " +
            $"outputPassBatches={staticRunPlans.Sum(plan => plan.OutputBatchCount)}, " +
            $"auxiliaryRuns={staticRunPlans.Sum(plan => plan.AuxiliaryRunCount)}, " +
            $"largestRun={staticRunPlans.Max(plan => plan.LargestRunInstanceCount)}");

        var receiverVariants = new MapRenderSceneReceiverVariantCatalog(
            new Dictionary<
                MapRenderWorldReceiverVariantKey,
                IReadOnlyList<MapRenderTexturedBatch>>
            {
                [new(
                    MapRenderWorldSurfacePageMembership.PageZero,
                    MapRenderTechniqueVariantAllocation.Unshadowed)] =
                    pageZeroUnshadowedWorldReceiverBatches,
                [new(
                    MapRenderWorldSurfacePageMembership.PageZero,
                    MapRenderTechniqueVariantAllocation
                        .ShadowMapAllocated)] =
                    shadowAllocatedWorldTexturedBatches,
                [new(
                    MapRenderWorldSurfacePageMembership.PageOne,
                    MapRenderTechniqueVariantAllocation.Unshadowed)] =
                    pageOneUnshadowedWorldReceiverBatches,
                [new(
                    MapRenderWorldSurfacePageMembership.PageOne,
                    MapRenderTechniqueVariantAllocation
                        .ShadowMapAllocated)] =
                    pageOneShadowAllocatedWorldReceiverBatches
            },
            new Dictionary<
                MapRenderStaticModelReceiverVariantKey,
                IReadOnlyList<MapRenderInstancedTexturedBatch>>
            {
                [new(
                    MapRenderStaticModelReceiverPage
                        .StaticModelRigidPage2,
                    MapRenderTechniqueVariantAllocation.Unshadowed)] =
                    pageZeroUnshadowedStaticModelReceiverBatches,
                [new(
                    MapRenderStaticModelReceiverPage
                        .StaticModelRigidPage2,
                    MapRenderTechniqueVariantAllocation
                        .ShadowMapAllocated)] =
                    shadowAllocatedStaticModelTexturedBatches,
                [new(
                    MapRenderStaticModelReceiverPage
                        .StaticModelRigidNoSunShadowPage3,
                    MapRenderTechniqueVariantAllocation.Unshadowed)] =
                    pageOneUnshadowedStaticModelReceiverBatches,
                [new(
                    MapRenderStaticModelReceiverPage
                        .StaticModelRigidNoSunShadowPage3,
                    MapRenderTechniqueVariantAllocation
                        .ShadowMapAllocated)] =
                    pageOneShadowAllocatedStaticModelReceiverBatches
            });
        TexturedBatchBuilder[] sceneWorldTexturedBuilders =
            texturedBatchBuilders.Values
                .Where(batch => batch.Indices.Count > 0)
                .ToArray();
        MapRenderTexturedBatch[] sceneWorldTexturedBatches =
            sceneWorldTexturedBuilders
                .AsParallel()
                .AsOrdered()
                .WithDegreeOfParallelism(
                    Math.Max(
                        1,
                        Math.Min(Environment.ProcessorCount, 4)))
                .Select(batch => new MapRenderTexturedBatch(
                    batch.Pass,
                    batch.PrimarySampler,
                    batch.Texture,
                    batch.LightmapTexture,
                    batch.ColorLayers,
                    batch.MaterialSamplers,
                    batch.ShaderExecution,
                    batch.ShaderExecutionStatus,
                    batch.UvRoute,
                    batch.State,
                    batch.UnresolvedCodeSamplerCount,
                    batch.PickRanges.ToArray(),
                    immutableGeometryArrays.InternFloats(batch.Vertices),
                    immutableGeometryArrays.InternFloats(
                        batch.RsxVertexInputs),
                    immutableGeometryArrays.InternUInts(batch.Indices))
                {
                    SceneLightIndex = batch.SceneLightIndex,
                    EditorDepthPrepass = batch.EditorDepthPrepass,
                    DepthPrepassShaderExecution =
                        batch.DepthPrepassShaderExecution
                })
                .ToArray();
        InstancedSolidBatchBuilder[] sceneInstancedSolidBuilders =
            instancedSolidBatchBuilders.Values
                .Where(batch =>
                    batch.Indices.Count > 0 &&
                    batch.Instances.Count > 0)
                .ToArray();
        MapRenderInstancedSolidBatch[] sceneInstancedSolidBatches =
            sceneInstancedSolidBuilders
                .AsParallel()
                .AsOrdered()
                .WithDegreeOfParallelism(
                    Math.Max(
                        1,
                        Math.Min(Environment.ProcessorCount, 4)))
                .Select(batch => new MapRenderInstancedSolidBatch(
                    batch.Vertices.ToArray(),
                    batch.Indices.ToArray(),
                    batch.Instances.ToArray()))
                .ToArray();
        var scene = new MapRenderScene(
            name,
            skies,
            solidVertices.ToArray(),
            solidIndices.ToArray(),
            fallbackSolidVertices.ToArray(),
            fallbackSolidIndices.ToArray(),
            sceneWorldTexturedBatches,
            sceneInstancedSolidBatches,
            preparedStaticTexturedBatches,
            wireVertices.ToArray(),
            wireIndices.ToArray(),
            solidPickRanges.ToArray(),
            fallbackSolidPickRanges.ToArray(),
            collisionPickTriangles.ToArray(),
            bounds,
            cameraBounds,
            worldSourceBuildResult,
            editorPreviewLighting,
            editorPreviewAtmosphere,
            editorPreviewActiveFog,
            editorPreviewCreateArtFog,
            editorPreviewVision,
            editorPreviewEffectivePost)
        {
            WorldTextureRevisionAtConstruction =
                worldTextureRevisionAtConstruction,
            StaticModelScheduling = staticModelScheduling,
            StaticModelLightingAtlas = staticModelLightingAtlas,
            SceneLightAttenuationTextures = sceneLightAttenuationTextures,
            StaticModelLodTexturedBatches =
                allStaticLodTexturedBatches,
            ExactNormalCameraStaticModelTexturedBatches =
                exactNormalCameraStaticModelTexturedBatches,
            TechniqueVariants = sceneTechniqueVariants,
            ReceiverVariants = receiverVariants,
            ShadowAllocatedWorldTexturedBatches =
                shadowAllocatedWorldTexturedBatches,
            ShadowAllocatedStaticModelTexturedBatches =
                shadowAllocatedStaticModelTexturedBatches,
            SunShadowWorldCasterBatches =
                sunShadowWorldCasterBatches,
            SunShadowWorldCasterRejections =
                sunShadowWorldCasterRejections,
            SunShadowStaticCasterBatches =
                sunShadowStaticCasterBatches,
            SunShadowStaticCasterExpectations =
                sunShadowStaticCasterExpectations
        };
        reportProgress?.Invoke(
            $"complete: {surfaceCount} world surfaces, {staticModelDrawInstCount} static-model instances, " +
            $"textures={textureDecodedCount} decoded/{textureDecodeSkippedCount} skipped, " +
            $"instanced solid={scene.InstancedSolidBatches.Count}/{scene.InstancedSolidBatches.Sum(batch => batch.Instances.Count)}, " +
            $"textured={scene.InstancedTexturedBatches.Count}/{scene.InstancedTexturedBatches.Sum(batch => batch.Instances.Count)} batches/placements");
        return scene;
    }

    private static MapRenderActiveFogState?
        CreateEditorPreviewFallbackFog(
            MapRenderEditorPreviewAtmospherePlan atmosphere,
            MapRenderEditorPreviewLightingPlan lighting) =>
        atmosphere.IsEnabled
            ? MapRenderEditorPreviewActiveFogAdapter.Create(
                atmosphere,
                lighting)
            : null;

    /// <summary>
    /// Resolves one authored scene-build variant without inferring current
    /// frame allocation or DPVS page membership. Runtime state chooses among
    /// these prepared channels later.
    /// </summary>
    internal static int? ResolvePreparedEditorTechniqueVariantSlot(
        int primaryLightIndex,
        GfxDrawSurfSurfaceType surfaceType,
        MapRenderDrawMethod? drawMethod,
        MapRenderSceneLightSelectorAssetState? sceneLightSelector,
        MapRenderTechniqueVariantAllocation allocation)
    {
        if (drawMethod is null || sceneLightSelector is null)
            return null;
        if ((uint)primaryLightIndex >=
            (uint)sceneLightSelector.SceneLightCount)
        {
            return null;
        }

        int variant =
            sceneLightSelector.BaseColumnByLight[primaryLightIndex];
        if (allocation ==
            MapRenderTechniqueVariantAllocation.ShadowMapAllocated)
        {
            if (!sceneLightSelector.CanPrepareShadowAllocatedVariant(
                    primaryLightIndex))
            {
                return null;
            }
            variant = checked(
                variant +
                MapRenderDrawMethodPageProducer.AlternateVariantDelta);
        }
        else if (allocation !=
                 MapRenderTechniqueVariantAllocation.Unshadowed)
        {
            throw new ArgumentOutOfRangeException(nameof(allocation));
        }

        ReadOnlySpan<byte> row = drawMethod.GetTechniqueRow(surfaceType);
        if ((uint)variant >= (uint)row.Length)
            return null;
        int techniqueSlot = row[variant];
        return techniqueSlot == MapRenderDrawMethodInitializer.NoneTechnique
            ? null
            : techniqueSlot;
    }

}
