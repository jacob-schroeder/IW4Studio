using IW4.Render.Techniques;
using System.Numerics;
using Silk.NET.OpenGL;
using IW4.Render.Geometry.Shadows;
using IW4.Render.Materials;
using IW4.Render.OpenGl.Shadows;
using IW4.Render.OpenGl.StaticModels;
using IW4.Render.Scheduling.Dpvs;
using IW4.Render.Scheduling.Shadows;

namespace IW4.Render.OpenGl;

public sealed unsafe partial class SilkOpenGlMapRenderer
{
    private void DeleteLoadedResources()
    {
        ResetSunShadowDpvsPipelineState();
        _sunShadowDpvsWorker?.Dispose();
        _sunShadowDpvsWorker = null;
        LastEditorPreviewPresentationResult = null;
        LastFramePlan = null;
        _currentSunShadowReceiverFrame = null;
        _currentProcessedFloatZFrame = null;
        _currentSunShadowPublication = null;
        _currentSunShadowCasters = null;
        _sunShadowVisibilityProvider = null;
        _sunShadowCasterCatalogProvider = null;
        _sunShadowFrameSequence = new MapRenderSunShadowFrameSequence();
        _nextSunShadowFrameRevision = 0;
        _sunShadowCasterAdmissionVisibility = null;
        _sunShadowCasterAdmissionPartition0 = null;
        _sunShadowCasterAdmissionPartition1 = null;
        _currentSunShadowCasterAdmissionReused = false;
        _sunShadowWorldAdmissionScratch.Clear();
        _sunShadowCoverageWorldScratch.Clear();
        _sunShadowCoverageStaticScratch.Clear();
        _selectedDirectionalSunPrimaryLightIndex = null;
        SunShadowPipelineStatus = "SUN_SHADOW_PIPELINE_NOT_INITIALIZED";
        _sunShadowAtlas?.Dispose();
        _sunShadowAtlas = null;
        foreach (MapRenderOpenGlSunShadowWorldCasterRuntime runtime in
                 _sunShadowWorldCasterRuntimes)
        {
            DeleteSunShadowCasterMesh(runtime.Mesh);
        }
        _sunShadowWorldCasterRuntimes = [];
        _sunShadowWorldCastersBySurface =
            new Dictionary<int,
                MapRenderOpenGlSunShadowWorldCasterSurfaceRuntime>();
        _sunShadowExecutableWorldSurfaceIndices = new HashSet<int>();
        _sunShadowWorldCasterRejectionsBySurface =
            new Dictionary<int,
                MapRenderSunShadowWorldCasterRejection>();
        foreach (MapRenderOpenGlSunShadowStaticCasterRuntime runtime in
                 _sunShadowStaticCasterRuntimes)
        {
            DeleteSunShadowStaticCasterRuntime(runtime);
        }
        _sunShadowStaticCasterRuntimes = [];
        _sunShadowStaticCasterExpectations = [];
        _sunShadowStaticCasterIndex = null;
        _editorPreviewPresentationSession?.Dispose();
        _editorPreviewPresentationSession = null;
        DeleteMesh(_solid);
        _solid = default;
        DeleteMesh(_fallbackSolid);
        _fallbackSolid = default;
        foreach (GlInstancedMesh mesh in _instancedSolid)
            DeleteInstancedMesh(mesh);
        _instancedSolid = [];
        foreach (GlTexturedMesh mesh in _textured)
            DeleteTexturedMesh(mesh);
        _textured = [];
        foreach (WorldReceiverVariantRuntime channel in
                 _worldReceiverVariants)
        {
            DeleteWorldReceiverVariant(channel);
        }
        _worldReceiverVariants = [];
        foreach (StaticReceiverVariantRuntime channel in
                 _staticReceiverVariants)
        {
            DeleteStaticReceiverVariant(channel);
        }
        _staticReceiverVariants = [];
        if (_exactNormalCameraStaticRuntime is
                { } exactNormalCameraChannel)
        {
            DeleteStaticReceiverVariant(exactNormalCameraChannel);
        }
        _exactNormalCameraStaticRuntime = null;
        _exactNormalCameraStaticExpectedIdentities = [];
        _selectedStaticReceiverSurfaces.Clear();
        _previousSelectedStaticReceiverSurfaces.Clear();
        _selectedStaticReceiverOccurrences.Clear();
        _previousSelectedStaticReceiverOccurrences.Clear();
        _staticReceiverExpectedIdentities = [];
        _staticReceiverSelectionGroups = [];
        _baseWorldReceiverVisibilityWords = [];
        _baseWorldReceiverVisibilityActive = false;
        _cachedUnshadowedReceiverSelectorState = null;
        _cachedAllocatedReceiverSelectorState = null;
        _cachedAllocatedReceiverSunIndex = -1;
        _currentWorldReceiverTechniqueSelector = null;
        _sceneTechniqueVariants = null;
        _sceneLightSelectorAsset = null;
        _worldSurfaceBatches = [];
        _nextWorldMultiDrawBatchGroupId = 0;
        _nextWorldDepthMultiDrawBatchGroupId = 0;
        _worldSurfaceCandidateCount = 0;
        _worldSurfaceCandidateIndexCount = 0;
        _worldSurfaceFallbackBatchCount = 0;
        DeleteMesh(_genericWorldArena);
        _genericWorldArena = default;
        foreach (GlMesh translatedArena in _translatedWorldArenas)
            DeleteMesh(translatedArena);
        _translatedWorldArenas = [];
        WorldGeometryArenaUploadCount = 0;
        WorldGeometrySourceBatchCount = 0;
        WorldGeometryImmutableBufferUploadCount = 0;
        WorldGeometryImmutableBufferUploadBytes = 0;
        WorldGeometryTranslatedArenaCount = 0;
        WorldGeometryMaximumTranslatedArenaAttributeCount = 0;
        foreach (GlTexturedMesh mesh in _instancedTextured)
            DeleteTexturedMesh(mesh);
        _instancedTextured = [];
        _renderedWorldBatches = [];
        _baseStaticBatches = [];
        _baseStaticGroupPlan = null;
        _baseStaticResolvedGroups = [];
        _baseStaticExecutableGroups = [];
        _baseStaticDrawGroupCache = null;
        _progressiveWorldDrawGroups = null;
        _progressiveStaticMaterializationEnabled = false;
        _progressiveStaticAdmissionLaneCursor = 0;
        _lastProgressiveStaticCamera = null;
        _lastProgressiveStaticAspectRatio = 0f;
        StaticResourceSourceBatchCount = 0;
        StaticResourceResolvedBatchCount = 0;
        StaticResourceMaterializedBatchCount = 0;
        StaticResourceRejectedBatchCount = 0;
        StaticResourceMaterializationWaveCount = 0;
        _staticGeometryUploads.ReleaseAll(DeleteStaticGeometryBuffers);
        _editorTexturedDrawGroups = [];
        _receiverAwareEditorTexturedDrawGroups = [];
        _editorDepthPrepassDrawGroups = [];
        _receiverAwareEditorDepthPrepassDrawGroups = [];
        _texturedDrawGroupVisibilityScratch = [];
        _texturedDrawGroupVisibilityByIdentity.Clear();
        foreach (StaticInstanceBufferRuntime runtime in
                 _staticInstanceBuffers.Values)
        {
            DeleteStaticInstanceUploadBufferRing(runtime);
        }
        _staticInstanceBuffers.Clear();
        _staticInstanceRuntimesByObjectIndex.Clear();
        _staticInstanceRescanScratch.Clear();
        _changedStaticInstanceObjectIndices.Clear();
        _previousVisibleStaticObjects = [];
        _previousSelectedStaticLodByObject = [];
        _previousVisibleStaticObjectCount = 0;
        _previousSelectedStaticLodCount = 0;
        _previousVisibleStaticObjectWorklist = [];
        _previousVisibleStaticObjectWorklistCount = 0;
        _previousUsesDynamicStaticLods = false;
        _previousStaticInstanceCandidateObjectIndices = null;
        _hasPreviousStaticInstanceSelection = false;
        _staticInstanceCompactionFullInvalidationPending = true;
        _staticSchedulingByObjectIndex.Clear();
        _staticScheduling = [];
        _staticModelLightingObjectIndices = [];
        _conservativeUnscheduledStaticObjectIndices = [];
        _staticModelLightingWorkingSet = null;
        _staticModelLightingAtlas = null;
        _staticModelLightingPhysicalRgbaBytes = null;
        _visibleStaticObjects = [];
        _selectedStaticLodByObject = [];
        _visibleStaticObjectWorklist = [];
        _visibleScheduledStaticObjectCount = 0;
        _visibleStaticObjectWorklistCount = 0;
        _usesDynamicStaticLods = false;
        _editorPreviewSceneLightFrame = null;
        _editorPreviewSceneLightFrameFailure = null;
        _previewWorldSource = null;
        _currentPreviewFrustum = null;
        _currentPreviewDpvs = null;
        _previewSceneGeneration++;
        CancelPreviewDpvsWork();
        _previewFrustumCache.Clear();
        _previewDpvsCache = new MapRenderWorldDpvsCameraOnlyVisibilityCache();
        _skyResourceCatalog = null;
        _diagnosticResourceCatalog = null;
        _renderSceneSnapshot = null;
        _loadedIsolatedWorldSurfaceIndex = null;
        foreach (GlSkyMesh sky in _skies)
            DeleteSkyMesh(sky);
        _skies = [];
        foreach (uint textureHandle in _textureHandles.Handles.Distinct())
            _gl.DeleteTexture(textureHandle);
        _textureHandles.Clear();
        _visibleTextureHandles.Clear();
        _criticalTextureHandles.Clear();
        _textureAdmissionScratch.Clear();
        _textureEvictionScratch.Clear();
        _textureDecodedFallbackBytesObserved = 0;
        _rendererDecodedBcFallbackBytesRetained = 0;
        _textureAuthoredBcSourceBytes = 0;
        _texturePayloadsAccounted.Clear();
        _observedDecodedTexturePayloads = new();
        _observedAuthoredTexturePayloads = new();
        if (_staticModelLightingAtlasTexture != 0)
        {
            _gl.DeleteTexture(_staticModelLightingAtlasTexture);
            _staticModelLightingAtlasTexture = 0;
        }
        foreach (uint program in _sceneOwnedProgramHandles)
            _gl.DeleteProgram(program);
        _sceneOwnedProgramHandles.Clear();
        _sceneProgramResolutions.Clear();
        _authoredMaterials.Clear();
        _staticModelProgramUniforms.Clear();
        DeleteMesh(_wire);
        _wire = default;
        DeleteMesh(_editorSelectionOutlineMesh);
        _editorSelectionOutlineMesh = default;
        ResetEditorSelectionOutline();
        _solidProgram = 0;
        _depthPrepassProgram = 0;
        _texturedProgram = 0;
        _skyProgram = 0;
        _sunShadowOpaqueCasterProgram = 0;
        _sunShadowCutoutCasterProgram = 0;

        _loaded = false;
        _editorPreviewLighting = null;
        _editorPreviewEffectivePost = null;
        _editorPreviewDirectionalSunDiffuseColor = Vector3.Zero;
        _editorPreviewDirectionalSunSpecularColor = Vector3.Zero;
        _editorPreviewAtmosphere = null;
        _editorPreviewAtmosphereEnabled = false;
        _editorPreviewFogRenderingEnabled = false;
        _editorPreviewActiveFog = null;
        _editorPreviewGenericActiveFog = null;
        _state.InvalidateAll();
    }

    private void DeleteMesh(GlMesh mesh)
    {
        if (mesh.ElementBuffer != 0)
            _gl.DeleteBuffer(mesh.ElementBuffer);
        if (mesh.VertexBuffer != 0)
            _gl.DeleteBuffer(mesh.VertexBuffer);
        if (mesh.VertexArray != 0)
            _gl.DeleteVertexArray(mesh.VertexArray);
    }

    private void DeleteTexturedMesh(GlTexturedMesh mesh)
    {
        if (mesh.InstanceBuffer != 0)
            _gl.DeleteBuffer(mesh.InstanceBuffer);
        if (mesh.OwnsGeometry)
        {
            DeleteMesh(new GlMesh(
                mesh.VertexArray,
                mesh.VertexBuffer,
                mesh.ElementBuffer,
                mesh.IndexCount));
        }
        else if (mesh.OwnsVertexArray && mesh.VertexArray != 0)
        {
            _gl.DeleteVertexArray(mesh.VertexArray);
        }
    }

    private void DeleteStaticGeometryBuffers(
        MapRenderOpenGlStaticGeometryBuffers buffers)
    {
        if (buffers.ElementBuffer != 0)
            _gl.DeleteBuffer(buffers.ElementBuffer);
        if (buffers.VertexBuffer != 0)
            _gl.DeleteBuffer(buffers.VertexBuffer);
    }

    private void DeleteSkyMesh(GlSkyMesh mesh)
    {
        DeleteMesh(new GlMesh(mesh.VertexArray, mesh.VertexBuffer, mesh.ElementBuffer, mesh.IndexCount));
    }

    private void DeleteInstancedMesh(GlInstancedMesh mesh)
    {
        if (mesh.InstanceBuffer != 0)
            _gl.DeleteBuffer(mesh.InstanceBuffer);
        DeleteMesh(new GlMesh(mesh.VertexArray, mesh.VertexBuffer, mesh.ElementBuffer, mesh.IndexCount));
    }

    private void ApplyDefaultRenderState()
    {
        _state.SetEnabled(EnableCap.FramebufferSrgb, false);
        _state.SetEnabled(EnableCap.DepthTest, true);
        _state.DepthMask(true);
        _state.DepthFunc(DepthFunction.Lequal);
        // Direct EditorPreview pre-negates the native projection Y before the
        // translated vertex-export lowering negates it again. Those two
        // operations cancel, so both generic and translated preview geometry
        // retain OpenGL's counter-clockwise host winding.
        _state.FrontFace(EditorPreviewTexturedFrontFace());
        _state.SetEnabled(EnableCap.Blend, false);
        _state.SetEnabled(EnableCap.CullFace, false);
        _state.PolygonMode(PolygonMode.Fill);
        _state.SetEnabled(EnableCap.PolygonOffsetFill, false);
        _state.SetEnabled(EnableCap.StencilTest, false);
        _state.ColorMask(true, true, true, true);
    }

    private void ApplyRenderState(
        RenderState state,
        MapRenderOpenGlStencilTargetContract? stencilTargetContract = null)
    {
        if (!state.HasState)
        {
            ApplyDefaultRenderState();
            return;
        }
        if (!_authoredMaterials.TryApplyRenderState(
                state,
                stencilTargetContract,
                out string? blocker))
        {
            throw new InvalidOperationException(
                blocker ?? "Authored OpenGL state is not executable.");
        }
    }

    internal static FrontFaceDirection EditorPreviewTexturedFrontFace() =>
        FrontFaceDirection.Ccw;

}
