using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using IW4.Assets.Assets.Material;
using IW4.Render.Diagnostics;
using Silk.NET.OpenGL;

using IW4.Render.Execution;
using IW4.Render.Geometry;
using IW4.Render.Geometry.Shadows;
using IW4.Render.EditorPreview;
using IW4.Render.Lighting;
using IW4.Render.Scheduling;
using IW4.Render.Scheduling.Dpvs;
using IW4.Render.Execution.Fog;
using IW4.Render.Scheduling.FramePlans;
using IW4.Render.Scheduling.Lighting;
using IW4.Render.Scheduling.Shadows;
using IW4.Render.Scheduling.StaticModels;
using IW4.Render.SceneBuilding;
using IW4.Render.Shaders;
using IW4.Render.Textures;
using Texture = IW4.Render.Textures.Texture;
using IW4.Render.Preview;
using IW4.Render.Resources;
using IW4.Render.OpenGl.Presentation;
using IW4.Render.OpenGl.Programs;
using IW4.Render.OpenGl.Scheduling;
using IW4.Render.OpenGl.Shaders;
using IW4.Render.OpenGl.Shadows;
using IW4.Render.OpenGl.Sky;
using IW4.Render.OpenGl.StaticModels;
using IW4.Render.OpenGl.Diagnostics;
using IW4.Render.OpenGl.FloatZ;
using IW4.Render.OpenGl.Wireframe;
using IW4.Render.Visibility;

namespace IW4.Render.OpenGl;

public sealed unsafe partial class SilkOpenGlMapRenderer : IMapRenderer
{
    private static readonly int GpuPhaseCount =
        Enum.GetValues<MapRenderGpuPhase>().Length;
    private const int UnknownStaticLodIndex = int.MinValue;
    private const int GenericStaticModelLightingTextureUnit =
        MapRenderScene.MaxColorLayerCount + 1 + 4 + 3;
    private const string LinkProfileIdentity =
        OpenGlSharedProgramCache.LinkProfileIdentity;
    private const long DefaultTextureResidencyBudgetBytes =
        384L * 1024L * 1024L;
    private const long DefaultTextureUploadBudgetBytesPerFrame =
        24L * 1024L * 1024L;
    private const int DefaultTextureEvictionGraceFrames = 8;
    private readonly GL _gl;
    private readonly SilkOpenGlTextureParameters _textureParameters;
    private readonly SilkOpenGlStateShadow _state;
    private readonly MapRenderOpenGlFrameVertexConstantBuffer
        _frameVertexConstants;
    private readonly MapRenderFrameTelemetry _frameTelemetry = new();
    private readonly MapRenderOpenGlShaderCompilationCounter
        _shaderCompilationCounter = new();
    private readonly MapRenderOpenGlGpuTimerCoordinator _gpuTimers;
    private readonly OpenGlSharedProgramCache _sharedProgramCache;
    private readonly OpenGlSharedProgramCache.UsageLease
        _sharedProgramUsage;
    private readonly bool _ownsSharedProgramCache;
    private readonly int _parallelShaderCompilerThreadLimit;
    private readonly bool _supportsParallelShaderLinkCompletion;
    private readonly HashSet<uint> _sceneOwnedProgramHandles = [];
    private readonly Dictionary<
        OpenGlProgramKey,
        OpenGlLinkedProgramHandleResolution>
        _sceneProgramResolutions = [];
    private readonly float _wireframeEffectiveLineWidth;
    private readonly string _editorPreviewPresentationContextIdentity =
        $"silk-editor-preview-presentation:{Guid.NewGuid():N}";
    private readonly string _sunShadowAtlasContextIdentity =
        $"silk-sun-shadow-atlas:{Guid.NewGuid():N}";
    private EditorPresentationSession?
        _editorPreviewPresentationSession;
    private MapRenderOpenGlSunShadowAtlasBackend? _sunShadowAtlas;
    private MapRenderOpenGlSunShadowReceiverFrame?
        _currentSunShadowReceiverFrame;
    private MapRenderOpenGlProcessedFloatZFrame?
        _currentProcessedFloatZFrame;
    private IMapRenderWorldDpvsNormalCameraVisibilityProvider?
        _sunShadowVisibilityProvider;
    private MapRenderSunShadowFrameSequence _sunShadowFrameSequence = new();
    private MapRenderSunShadowFramePublication?
        _currentSunShadowPublication;
    private MapRenderSunShadowCasterCatalog? _currentSunShadowCasters;
    private MapRenderWorldDpvsVisibilityBuildResult?
        _currentSunShadowVisibility;
    private MapRenderWorldDpvsVisibilityBuildResult?
        _sunShadowAtlasContentVisibility;
    private MapRenderSunShadowCasterPartition?
        _sunShadowAtlasContentPartition0;
    private MapRenderSunShadowCasterPartition?
        _sunShadowAtlasContentPartition1;
    private MapRenderOpenGlSunShadowAtlasBackend?
        _sunShadowAtlasContentBackend;
    private long _nextSunShadowFrameRevision;
    private int? _selectedDirectionalSunPrimaryLightIndex;
    private uint _sunShadowOpaqueCasterProgram;
    private uint _sunShadowCutoutCasterProgram;
    private int _sunShadowOpaqueViewProjectionLocation;
    private int _sunShadowOpaqueUseInstancingLocation;
    private int _sunShadowCutoutViewProjectionLocation;
    private int _sunShadowCutoutUseInstancingLocation;
    private int _sunShadowCutoutTextureLocation;
    private MapRenderOpenGlSunShadowWorldCasterRuntime[]
        _sunShadowWorldCasterRuntimes = [];
    private IReadOnlyDictionary<int,
        MapRenderOpenGlSunShadowWorldCasterSurfaceRuntime>
        _sunShadowWorldCastersBySurface =
            new Dictionary<int,
                MapRenderOpenGlSunShadowWorldCasterSurfaceRuntime>();
    private IReadOnlySet<int> _sunShadowExecutableWorldSurfaceIndices =
        new HashSet<int>();
    private IReadOnlyDictionary<int,
        MapRenderSunShadowWorldCasterRejection>
        _sunShadowWorldCasterRejectionsBySurface =
            new Dictionary<int,
                MapRenderSunShadowWorldCasterRejection>();
    private MapRenderOpenGlSunShadowStaticCasterRuntime[]
        _sunShadowStaticCasterRuntimes = [];
    private MapRenderSunShadowStaticCasterExpectation[]
        _sunShadowStaticCasterExpectations = [];
    private MapRenderOpenGlSunShadowStaticCasterIndex?
        _sunShadowStaticCasterIndex;
    private uint _solidProgram;
    private int _solidViewProjectionLocation;
    private int _solidUseInstancingLocation;
    private uint _depthPrepassProgram;
    private int _depthPrepassViewProjectionLocation;
    private int _depthPrepassUseInstancingLocation;
    private int _depthPrepassVegetationParametersLocation;
    private int _depthPrepassVegetationTimeLocation;
    private int _depthPrepassVegetationBoundsLocation;
    private uint _texturedProgram;
    private int _texturedViewProjectionLocation;
    private int _texturedUseInstancingLocation;
    private int[] _texturedColorSamplerLocations = [];
    private int _texturedColorLayerCountLocation;
    private int[] _texturedBlendWeightComponentLocations = [];
    private int _texturedLightmapSamplerLocation;
    private int _texturedHasLightmapLocation;
    private int _texturedStaticModelLightingSamplerLocation;
    private int _texturedHasStaticModelLightingLocation;
    private int _texturedStaticModelLightingSamplerTransformLocation;
    private int _texturedAlphaTestEnabledLocation;
    private int _texturedAlphaFuncLocation;
    private int _texturedAlphaRefLocation;
    private int _texturedShaderPackerSrgbEnabledLocation;
    private int _texturedLinearizeColorInputsLocation;
    private int _texturedPremultiplyAlphaLocation;
    private int _texturedLightingEnabledLocation;
    private int _texturedAmbientColorLocation;
    private int _texturedHasDirectionalSunDiffuseLocation;
    private int _texturedHasDirectionalSunSpecularLocation;
    private int _texturedDirectionalSunDirectionLocation;
    private int _texturedDirectionalSunDiffuseColorLocation;
    private int _texturedDirectionalSunSpecularColorLocation;
    private int _texturedCameraPositionLocation;
    private int _texturedFogEnabledLocation;
    private int _texturedFogUseActiveStateLocation;
    private int _texturedFogColorLocation;
    private int _texturedFogStartLocation;
    private int _texturedFogEndLocation;
    private int _texturedFogMaxOpacityLocation;
    private int _texturedFogDistanceScaleLocation;
    private int _texturedFogDistanceBiasLocation;
    private int _texturedFogMinimumVisibilityLocation;
    private int _texturedSunFogEnabledLocation;
    private int _texturedSunFogColorLocation;
    private int _texturedSunFogDirectionLocation;
    private int _texturedSunFogDistanceScaleLocation;
    private int _texturedSunFogEndCosineLocation;
    private int _texturedSunFogAngularScaleLocation;
    private int _texturedVegetationParametersLocation;
    private int _texturedVegetationTimeLocation;
    private int _texturedVegetationBoundsLocation;
    private int[] _texturedNormalSamplerLocations = [];
    private int[] _texturedHasNormalLocations = [];
    private int[] _texturedSpecularSamplerLocations = [];
    private int[] _texturedHasSpecularLocations = [];
    private uint _skyProgram;
    private int _skyViewProjectionLocation;
    private int _skyTextureLocation;
    private GlMesh _solid;
    private GlMesh _fallbackSolid;
    private GlInstancedMesh[] _instancedSolid = [];
    private GlTexturedMesh[] _textured = [];
    private int _nextWorldMultiDrawBatchGroupId;
    private int _nextWorldDepthMultiDrawBatchGroupId;
    private WorldSurfaceBatchRuntime?[] _worldSurfaceBatches = [];
    private WorldReceiverVariantRuntime[] _worldReceiverVariants = [];
    private StaticReceiverVariantRuntime[] _staticReceiverVariants = [];
    private ExactNormalCameraStaticRuntime?
        _exactNormalCameraStaticRuntime;
    private MapRenderStaticModelReceiverIdentity[]
        _exactNormalCameraStaticExpectedIdentities = [];
    private readonly MapRenderOpenGlStaticGeometryUploadCache
        _staticGeometryUploads = new();
    private readonly HashSet<MapRenderStaticModelReceiverIdentity>
        _selectedStaticReceiverSurfaces = [];
    private readonly HashSet<MapRenderStaticModelReceiverIdentity>
        _previousSelectedStaticReceiverSurfaces = [];
    private readonly HashSet<(
        StaticInstanceBufferRuntime Runtime,
        int InstanceIndex)> _selectedStaticReceiverOccurrences = [];
    private readonly HashSet<(
        StaticInstanceBufferRuntime Runtime,
        int InstanceIndex)> _previousSelectedStaticReceiverOccurrences = [];
    private MapRenderStaticModelReceiverIdentity[]
        _staticReceiverExpectedIdentities = [];
    private uint[] _baseWorldReceiverVisibilityWords = [];
    private bool _baseWorldReceiverVisibilityActive;
    private uint _receiverSelectionGeneration = 1;
    private MapRenderSceneLightSelectorState?
        _cachedUnshadowedReceiverSelectorState;
    private MapRenderFrameTechniqueSelector?
        _currentWorldReceiverTechniqueSelector;
    private MapRenderSceneTechniqueVariantCatalog? _sceneTechniqueVariants;
    private MapRenderSceneLightSelectorAssetState? _sceneLightSelectorAsset;
    private GlTexturedMesh[] _instancedTextured = [];
    private MapRenderTexturedBatch[] _renderedWorldBatches = [];
    private MapRenderInstancedTexturedBatch[] _baseStaticBatches = [];
    private MapRenderOpenGlStaticResourceGroupPlan? _baseStaticGroupPlan;
    private bool[] _baseStaticResolvedGroups = [];
    private bool[] _baseStaticExecutableGroups = [];
    private MapRenderOpenGlProgressiveStaticDrawGroupCache?
        _baseStaticDrawGroupCache;
    private MapRenderEditorDrawGroup<GlTexturedDrawCommand>[]?
        _progressiveWorldDrawGroups;
    private bool _progressiveStaticMaterializationEnabled;
    private RenderCamera? _lastProgressiveStaticCamera;
    private float _lastProgressiveStaticAspectRatio;
    private GlMesh _genericWorldArena;
    private GlMesh[] _translatedWorldArenas = [];
    private GlMesh _genericWorldReceiverArena;
    private GlMesh[] _translatedWorldReceiverArenas = [];
    private MapRenderEditorDrawGroup<GlTexturedDrawCommand>[]
        _editorTexturedDrawGroups = [];
    private MapRenderEditorDrawGroup<GlTexturedDrawCommand>[]
        _receiverAwareEditorTexturedDrawGroups = [];
    private MapRenderEditorDrawGroup<GlTexturedDrawCommand>[]
        _editorDepthPrepassDrawGroups = [];
    private MapRenderEditorDrawGroup<GlTexturedDrawCommand>[]
        _receiverAwareEditorDepthPrepassDrawGroups = [];
    private bool[] _texturedDrawGroupVisibilityScratch = [];
    private bool[] _texturedDrawGroupColorReadinessScratch = [];
    private MapRenderGpuPhase[] _texturedDrawGroupGpuPhaseScratch = [];
    // Populated only by the frame-wide Apple Silicon depth-fusion proof.
    // Group identity, rather than SourceOrdinal, is required because
    // receiver-aware queue composition can reuse ordinals across channels.
    private readonly HashSet<MapRenderEditorDrawGroup<GlTexturedDrawCommand>>
        _appleDepthFusionOwnerGroups =
            new(ReferenceEqualityComparer.Instance);
    private readonly List<MapRenderEditorDrawGroup<GlTexturedDrawCommand>>
        _appleDepthFusionOwnerGroupScratch = [];
    // The immutable sorted color queue and selected depth queue retain their
    // identities while visibility changes. Geometry ownership is independent
    // of those per-frame inputs, so cache the exact opaque color index for
    // each depth group. A negative index records an ambiguous or absent match
    // and remains a fail-closed result when that depth group is visible.
    private IReadOnlyList<MapRenderEditorDrawGroup<GlTexturedDrawCommand>>?
        _appleDepthFusionOwnerColorGroups;
    private IReadOnlyList<MapRenderEditorDrawGroup<GlTexturedDrawCommand>>?
        _appleDepthFusionOwnerDepthGroups;
    private readonly Dictionary<
        MapRenderEditorDrawGroup<GlTexturedDrawCommand>,
        int> _appleDepthFusionColorOwnerIndexByDepthGroup =
            new(ReferenceEqualityComparer.Instance);
    // SourceOrdinal is only a reusable candidate-chain key. Every candidate
    // remains subject to the exact command-geometry proof before it becomes
    // a depth owner.
    private readonly Dictionary<long, int>
        _appleDepthFusionOpaqueColorHeadBySourceOrdinal = [];
    private int[] _appleDepthFusionOpaqueColorNextByIndex = [];
    private readonly Dictionary<
        MapRenderEditorDrawGroup<GlTexturedDrawCommand>,
        bool> _texturedDrawGroupVisibilityByIdentity =
            new(ReferenceEqualityComparer.Instance);
    private bool _hasPreviewVisibilityPublication;
    private long _previewVisibilityPacketTicket;
    private SunShadowDpvsWorkKey _previewVisibilityPacketKey;
    private long _previewVisibilitySceneGeneration;
    private RenderCamera _previewVisibilityCamera;
    private int _previewVisibilityTargetWidth;
    private int _previewVisibilityTargetHeight;
    private MapRenderCameraFrustum? _previewVisibilityFrustum;
    private MapRenderWorldDpvsViewVisibility? _previewVisibilityDpvs;
    private GlTexturedMesh[]? _previewVisibilityTexturedMeshes;
    private WorldSurfaceBatchRuntime?[]?
        _previewVisibilityWorldSurfaceBatches;
    private MapRenderEditorDrawGroup<GlTexturedDrawCommand>[]?
        _previewVisibilityFrameGroups;
    private WorldReceiverVariantRuntime[]?
        _previewVisibilityWorldReceiverVariants;
    private bool _previewVisibilityBaseWorldReceiverActive;
    private uint[] _previewVisibilityBaseWorldReceiverWords = [];
    private WorldReceiverVariantRuntime[]
        _previewVisibilityWorldReceiverChannels = [];
    private uint[][] _previewVisibilityWorldReceiverWords = [];
    private int[] _previewVisibilityWorldReceiverCounts = [];
    private MapRenderStaticModelLightingWorkingSet?
        _previewVisibilityStaticLightingWorkingSet;
    private ulong _previewVisibilityStaticLightingAssignmentGeneration;
    private int _previewVisibilityVisibleScheduledStaticObjectCount;
    private long _previewVisibilityVisibleStaticObjectCount;
    private bool _previewVisibilityUsesDynamicStaticLods;
    private long _previewVisibilityWorldCount;
    private long _previewVisibilityWorldRunCount;
    private long _previewVisibilityWorldIndexCount;
    private ulong _previewVisibilityPublicationRevision;
    private bool _hasPreparedTexturedDrawQueue;
    private MapRenderEditorDrawGroup<GlTexturedDrawCommand>[]?
        _preparedTexturedDrawFrameGroups;
    private IReadOnlyList<
        MapRenderEditorDrawGroup<GlTexturedDrawCommand>>?
        _preparedTexturedDrawGroups;
    private ulong _preparedTexturedDrawVisibilityRevision;
    private ulong _preparedTexturedDrawQueueRevision;
    private readonly HashSet<uint> _visibleTextureHandles = [];
    private readonly HashSet<uint> _criticalTextureHandles = [];
    private readonly List<uint> _textureResidencyManifestScratch = [];
    private uint[] _textureResidencyManifest = [];
    private int _textureResidencyManifestCount;
    private uint[] _textureResidencyManifestCriticalHandles = [];
    private int _textureResidencyManifestCriticalHandleCount;
    private IReadOnlyList<
        MapRenderEditorDrawGroup<GlTexturedDrawCommand>>?
        _textureResidencyManifestDrawGroups;
    private IReadOnlyList<
        MapRenderEditorDrawGroup<GlTexturedDrawCommand>>?
        _textureResidencyManifestDepthGroups;
    private GlSkyMesh[]? _textureResidencyManifestSkies;
    private RenderSceneSnapshot? _textureResidencyManifestSceneSnapshot;
    private ulong _textureResidencyManifestQueueRevision;
    private ulong _textureResidencyManifestVisibilityRevision;
    private long _textureResidencyManifestSceneGeneration;
    private int _textureResidencyManifestResourceCount;
    private bool _hasTextureResidencyManifest;
    private ulong _textureResidencyMutationGeneration;
    private IReadOnlyList<
        MapRenderEditorDrawGroup<GlTexturedDrawCommand>>?
        _texturedDrawGroupColorExecutionGroups;
    private ulong _texturedDrawGroupColorExecutionQueueRevision;
    private ulong _texturedDrawGroupColorExecutionResidencyGeneration;
    private bool _hasTexturedDrawGroupColorExecutionCache;
    private readonly List<MapRenderOpenGlTextureResidencyEntry>
        _textureAdmissionScratch = [];
    private readonly List<MapRenderOpenGlTextureResidencyEntry>
        _textureEvictionScratch = [];
    private readonly MapRenderOpenGlCompressedTextureSupport
        _compressedTextureSupport;
    private long _frameTextureUploadBytes;
    private long _frameTextureUploadCount;
    private long _frameTextureEvictionBytes;
    private long _frameTextureEvictionCount;
    private long _frameTextureDeferredCount;
    private long _frameAuthoredBcUploadBytes;
    private long _textureDecodedFallbackBytesObserved;
    private long _rendererDecodedBcFallbackBytesRetained;
    private long _textureAuthoredBcSourceBytes;
    private readonly HashSet<Texture>
        _texturePayloadsAccounted =
            new(ReferenceEqualityComparer.Instance);
    private ConditionalWeakTable<byte[], object>
        _observedDecodedTexturePayloads = new();
    private ConditionalWeakTable<byte[], object>
        _observedAuthoredTexturePayloads = new();
    private static readonly object TexturePayloadMarker = new();
    private GlSkyMesh[] _skies = [];
    private RenderSceneSnapshot? _renderSceneSnapshot;
    private MapRenderOpenGlNormalCameraSkyResourceCatalog?
        _skyResourceCatalog;
    private MapRenderOpenGlNormalCameraDiagnosticResourceCatalog?
        _diagnosticResourceCatalog;
    private MapRenderOpenGlWireframeResourceCatalog?
        _wireframeResourceCatalog;
    private int? _loadedIsolatedWorldSurfaceIndex;
    private readonly MapRenderOpenGlTextureHandleCache _textureHandles = new();
    private readonly SilkOpenGlAuthoredMaterialExecutor _authoredMaterials;
    private readonly Func<ushort, int?, ShaderConstantValue?>
        _dynamicCodeConstantResolver;
    private MapRenderStaticModelLightingAtlas?
        _staticModelLightingAtlas;
    private MapRenderStaticModelLightingWorkingSet?
        _staticModelLightingWorkingSet;
    private int[] _staticModelLightingObjectIndices = [];
    private int[] _conservativeUnscheduledStaticObjectIndices = [];
    private byte[]? _staticModelLightingPhysicalRgbaBytes;
    private uint _genericInactiveTexture;
    private uint _staticModelLightingAtlasTexture;
    private uint[] _sceneLightAttenuationTextureHandles = [];
    private readonly Dictionary<uint, StaticInstanceBufferRuntime>
        _staticInstanceBuffers = [];
    private readonly Dictionary<int, List<StaticInstanceBufferRuntime>>
        _staticInstanceRuntimesByObjectIndex = [];
    private readonly List<StaticInstanceBufferRuntime>
        _staticInstanceRescanScratch = [];
    private readonly HashSet<int>
        _changedStaticInstanceObjectIndices = [];
    private bool[] _previousVisibleStaticObjects = [];
    private int[] _previousSelectedStaticLodByObject = [];
    private int _previousVisibleStaticObjectCount;
    private int _previousSelectedStaticLodCount;
    private int[] _previousVisibleStaticObjectWorklist = [];
    private int _previousVisibleStaticObjectWorklistCount;
    private bool _previousUsesDynamicStaticLods;
    private int[]? _previousStaticInstanceCandidateObjectIndices;
    private bool _hasPreviousStaticInstanceSelection;
    private bool _staticInstanceCompactionFullInvalidationPending = true;
    private readonly Dictionary<int, MapRenderStaticModelSchedulingInfo>
        _staticSchedulingByObjectIndex = [];
    private MapRenderStaticModelSchedulingInfo[] _staticScheduling = [];
    private readonly MapRenderCameraFrustumCache _previewFrustumCache = new();
    private MapRenderWorldDpvsCameraOnlyVisibilityCache _previewDpvsCache = new();
    private GlMesh _wire;
    private int _width = 1;
    private int _height = 1;
    private int _hostWidth = 1;
    private int _hostHeight = 1;
    private uint _hostFramebuffer;
    private bool _loaded;
    private bool _contextAbandoned;
    private bool _disposed;
    private MapRenderEditorPreviewLightingPlan? _editorPreviewLighting;
    private MapRenderWorldEvent20SceneLightFrameInput?
        _editorPreviewSceneLightFrame;
    private MapRenderWorldEvent20SceneLightFrameInputFailure?
        _editorPreviewSceneLightFrameFailure;
    private ShaderConstantValue
        _frameClipSpaceLookupScaleCodeConstant;
    private ShaderConstantValue
        _frameClipSpaceLookupOffsetCodeConstant;
    private ShaderConstantValue
        _frameZNearCodeConstant;
    private Vector3 _currentDynamicCodeConstantEyeOffset;
    private MapRenderEditorPreviewVisionState? _editorPreviewVision;
    private MapRenderEditorPreviewEffectivePostState?
        _editorPreviewEffectivePost;
    private Vector3 _editorPreviewDirectionalSunDiffuseColor;
    private Vector3 _editorPreviewDirectionalSunSpecularColor;
    private MapRenderEditorPreviewAtmospherePlan? _editorPreviewAtmosphere;
    private bool _editorPreviewAtmosphereEnabled;
    private bool _editorPreviewFogRenderingEnabledSetting = true;
    private bool _editorPreviewFogRenderingEnabled;
    private MapRenderActiveFogState? _editorPreviewActiveFog;
    private MapRenderActiveFogState? _editorPreviewGenericActiveFog;
    private long _editorAnimationStartTimestamp;
    private float? _previewAnimationTimeSecondsOverride;
    private long? _completedTelemetryFrameIndex;
    private long _frameDrawCalls;
    private long _frameLogicalDrawCommands;
    private long _frameMultiDrawApiCalls;
    private long _frameStaticExecutionBundleBinds;
    private long _frameStaticExecutionBundleReuses;
    private long _activeRenderFrameIndex = -1;
    private bool _frameDepthPassRecorded;
    private MapRenderGpuPhase? _activeGpuDrawPhase;
    private uint[] _multiDrawIndexCounts = [];
    private nint[] _multiDrawIndexOffsets = [];
    private int[] _multiDrawBaseVertices = [];
    private MapRenderWorldSceneSource? _previewWorldSource;
    private bool[] _visibleStaticObjects = [];
    private int[] _selectedStaticLodByObject = [];
    private int[] _visibleStaticObjectWorklist = [];
    private int _visibleScheduledStaticObjectCount;
    private int _visibleStaticObjectWorklistCount;
    private bool _usesDynamicStaticLods;
    private MapRenderCameraFrustum? _currentPreviewFrustum;
    private MapRenderWorldDpvsViewVisibility? _currentPreviewDpvs;
    private MapRenderOpenGlLatestWorkQueue<DpvsWorkKey, DpvsWorkResult>?
        _previewDpvsWorker;
    private long _previewSceneGeneration;
    private long _worldSurfaceCandidateCount;
    private long _worldSurfaceCandidateIndexCount;
    private int _worldSurfaceFallbackBatchCount;

    public long StaticResourceSourceBatchCount { get; private set; }

    public long StaticResourceResolvedBatchCount { get; private set; }

    public long StaticResourceMaterializedBatchCount { get; private set; }

    public long StaticResourceRejectedBatchCount { get; private set; }

    public long StaticResourceDeferredBatchCount =>
        Math.Max(
            0,
            StaticResourceSourceBatchCount -
            StaticResourceResolvedBatchCount);

    public long StaticResourceMaterializationWaveCount { get; private set; }

    /// <summary>
    /// Reports whether startup-only render work for the requested camera has
    /// reached retained resources. The desktop host consumes this after a
    /// completed presentation before reclaiming superseded bootstrap,
    /// residency, and publication workspaces.
    /// </summary>
    public bool IsStartupWorkingSetSettled(RenderCamera requestedCamera)
    {
        if (!_loaded ||
            _frameTextureDeferredCount != 0 ||
            _progressiveStaticUnpublishedBatchCount != 0 ||
            HasPendingProgressiveStaticGroups(
                _baseStaticGroupPlan is { } basePlan
                    ? basePlan.SelectedGroups
                    : []))
        {
            return false;
        }

        if (_sunShadowDpvsWorker is null ||
            _sunShadowVisibilityProvider is null ||
            _selectedDirectionalSunPrimaryLightIndex is null)
        {
            return _activeRenderFrameIndex >= 0;
        }
        if (_previewWorldSource is not { } source ||
            _retainedSunShadowDpvsPacket is not
                { Ticket: > 0 } retained ||
            retained.Ticket != _lastPresentedSunShadowDpvsTicket)
        {
            return false;
        }

        var requestedKey = new SunShadowDpvsWorkKey(
            source.AssetPoolRevisionAtConstruction,
            requestedCamera,
            _width,
            _height,
            RZFar: 0f,
            RendererFallback: requestedCamera.FarPlane);
        return retained.Key == requestedKey;
    }

    public bool ShowWireframe { get; set; }
    public bool ShowDiagnosticGeometry { get; set; }
    public bool ShowTexturedGeometry { get; set; } = true;
    public bool ShowSky { get; set; } = true;
    /// <summary>
    /// Enables EditorPreview fog rendering. Configure before <see cref="Load"/>.
    /// </summary>
    public bool EditorPreviewFogRenderingEnabled
    {
        get => _editorPreviewFogRenderingEnabledSetting;
        set
        {
            if (_loaded)
            {
                throw new InvalidOperationException(
                    "Live Preview fog rendering must be " +
                    "configured before loading renderer resources.");
            }

            _editorPreviewFogRenderingEnabledSetting = value;
        }
    }
    public bool UseRsxVertexPlacementDiagnostic { get; set; }
    public int? IsolatedWorldSurfaceIndex { get; set; }
    public int? RsxFragmentOutputDiagnostic { get; set; }
    /// <summary>
    /// Freezes the effective per-frame preview animation time when set.
    /// Leave null for the historical monotonic elapsed-time behavior.
    /// </summary>
    public float? PreviewAnimationTimeSecondsOverride
    {
        get => _previewAnimationTimeSecondsOverride;
        set
        {
            if (value is { } animationTimeSeconds &&
                (!float.IsFinite(animationTimeSeconds) ||
                 animationTimeSeconds < 0f))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Preview animation time must be finite and nonnegative.");
            }

            _previewAnimationTimeSecondsOverride = value is 0f
                ? 0f
                : value;
        }
    }
    public MapRenderOpenGlNormalCameraDefaultPresentationExecutionResult?
        LastEditorPreviewPresentationResult
    { get; private set; }
    public RenderFramePlan? LastFramePlan { get; private set; }
    public MapRenderFrameTelemetrySnapshot FrameTelemetry =>
        _frameTelemetry.CreateSnapshot();
    public double PresentedFramesPerSecond =>
        _frameTelemetry.PresentedFramesPerSecond;

    /// <summary>
    /// Adopts the stable post-presentation state after a host-owned overlay
    /// draw. The overlay must not bind textures or change the active texture
    /// unit; it may freely use its own program, vertex array, and buffers.
    /// </summary>
    public void AdoptStateAfterExternalOpenGlOverlay()
    {
        if (!_loaded)
            return;

        _state.AdoptDefaultPresenterHandoff(
            _hostWidth,
            _hostHeight,
            _hostFramebuffer);
    }

    /// <summary>
    /// Monotonic renderer-lifetime count of linked-program compilation
    /// attempts. Each program advances the count once regardless of its
    /// number of shader stages or whether compilation/linking succeeds.
    /// </summary>
    public long ShaderProgramCompilationCount =>
        _shaderCompilationCounter.ProgramCompilationCount;
    public long RsxProgramSemanticRequestCount =>
        _authoredMaterials.SemanticRequestCount;
    public long RsxProgramUniqueLinkCount =>
        _authoredMaterials.UniqueLinkCount;
    public long RsxProgramLinkReuseCount =>
        _authoredMaterials.LinkReuseCount;
    public int SharedProgramCachedEntryCount =>
        _sharedProgramCache.CachedEntryCount;
    public int SharedProgramCachedHandleCount =>
        _sharedProgramCache.CachedProgramCount;
    public long SharedProgramLinkRequestCount =>
        _sharedProgramCache.LinkRequestCount;
    public long SharedProgramUniqueLinkAttemptCount =>
        _sharedProgramCache.UniqueLinkAttemptCount;
    public long SharedProgramSuccessfulLinkCount =>
        _sharedProgramCache.SuccessfulLinkCount;
    public long SharedProgramLinkReuseCount =>
        _sharedProgramCache.LinkReuseCount;
    public long SharedProgramCapacityBypassCount =>
        _sharedProgramCache.CapacityBypassCount;
    public long RsxUniformLocationRequestCount =>
        _authoredMaterials.UniformLocationTelemetry.RequestCount;
    public long RsxUniformLocationQueryCount =>
        _authoredMaterials.UniformLocationTelemetry.QueryCount;
    public long RsxUniformLocationCacheHitCount =>
        _authoredMaterials.UniformLocationTelemetry.CacheHitCount;
    public string SunShadowPipelineStatus { get; private set; } =
        "SUN_SHADOW_PIPELINE_NOT_INITIALIZED";
    public MapRenderSurfaceExtents SurfaceExtents => new(
        new MapRenderPixelExtent(_width, _height),
        new MapRenderPixelExtent(_hostWidth, _hostHeight));

    /// <summary>
    /// Maximum full-resolution scene-texture storage retained by the live
    /// renderer. Stable one-pixel fallback objects are not charged.
    /// </summary>
    public long TextureResidencyBudgetBytes { get; set; } =
        DefaultTextureResidencyBudgetBytes;

    /// <summary>
    /// Maximum newly resident scene-texture payload scheduled in one frame.
    /// One individually larger visible texture may cross this limit so it
    /// cannot remain deferred forever.
    /// </summary>
    public long TextureUploadBudgetBytesPerFrame { get; set; } =
        DefaultTextureUploadBudgetBytesPerFrame;

    public int TextureEvictionGraceFrames { get; set; } =
        DefaultTextureEvictionGraceFrames;

    public bool SupportsAuthoredBcTextureUploads =>
        _compressedTextureSupport is { Bc1: true };

    public long TextureDecodedFallbackBytesObserved =>
        _textureDecodedFallbackBytesObserved;

    public long TextureDecodedFallbackBytesRetained =>
        checked(
            _textureDecodedFallbackBytesObserved +
            _rendererDecodedBcFallbackBytesRetained);

    public long TextureAuthoredBcSourceBytes =>
        _textureAuthoredBcSourceBytes;

    public long TextureGpuResidentBytes =>
        _textureHandles.ResidentBytes;

    /// <summary>
    /// Selects the framebuffer that receives the final normal-camera
    /// presentation. The default framebuffer (zero) remains the standalone
    /// renderer default; an embedded host supplies its current framebuffer
    /// before Load, before every Resize, and before each frame. The OpenGL
    /// context must be current because an already-loaded direct-render scene
    /// is rebound immediately.
    /// </summary>
    public void SetHostFramebuffer(uint framebuffer)
    {
        ThrowIfUnavailable();
        if (_hostFramebuffer == framebuffer)
            return;

        _hostFramebuffer = framebuffer;
        if (_loaded && _editorPreviewPresentationSession is null)
        {
            // ClipMap-only/direct rendering has no presentation pass to bind
            // the host target. Rebind immediately while the host context is
            // current so a rotating Avalonia FBO cannot receive stale draws.
            _state.BindFramebuffer(
                FramebufferTarget.Framebuffer,
                _hostFramebuffer);
            _gl.DrawBuffer(
                _hostFramebuffer == 0
                    ? DrawBufferMode.Back
                    : DrawBufferMode.ColorAttachment0);
            _state.Viewport(0, 0, _hostWidth, _hostHeight);
        }
    }

    public SilkOpenGlMapRenderer(GL gl)
        : this(
            gl,
            new OpenGlSharedProgramCache(gl),
            ownsSharedProgramCache: true)
    {
    }

    public SilkOpenGlMapRenderer(
        GL gl,
        OpenGlSharedProgramCache sharedProgramCache)
        : this(
            gl,
            sharedProgramCache,
            ownsSharedProgramCache: false)
    {
    }

    private SilkOpenGlMapRenderer(
        GL gl,
        OpenGlSharedProgramCache sharedProgramCache,
        bool ownsSharedProgramCache)
    {
        ArgumentNullException.ThrowIfNull(gl);
        ArgumentNullException.ThrowIfNull(sharedProgramCache);
        _gl = gl;
        _textureParameters = new SilkOpenGlTextureParameters(gl);
        _sharedProgramCache = sharedProgramCache;
        _ownsSharedProgramCache = ownsSharedProgramCache;
        _sharedProgramUsage =
            sharedProgramCache.AcquireUsageLease(gl);
        try
        {
            _parallelShaderCompilerThreadLimit =
                ConfigureParallelShaderCompilation(gl);
            _supportsParallelShaderLinkCompletion = gl.IsExtensionPresent(
                "GL_ARB_parallel_shader_compile");
            _state = new SilkOpenGlStateShadow(gl);
            _frameVertexConstants =
                new MapRenderOpenGlFrameVertexConstantBuffer(gl, _state);
            _authoredMaterials =
                new SilkOpenGlAuthoredMaterialExecutor(
                    gl,
                    _state,
                    ResolveLinkedProgram);
            _dynamicCodeConstantResolver =
                ResolveCurrentMapDynamicCodeConstant;
            bool supportsS3tc = gl.IsExtensionPresent(
                "GL_EXT_texture_compression_s3tc");
            _compressedTextureSupport =
                new MapRenderOpenGlCompressedTextureSupport(
                    Bc1: supportsS3tc ||
                        gl.IsExtensionPresent(
                            "GL_EXT_texture_compression_dxt1"),
                    Bc2: supportsS3tc ||
                        gl.IsExtensionPresent(
                            "GL_ANGLE_texture_compression_dxt3"),
                    Bc3: supportsS3tc ||
                        gl.IsExtensionPresent(
                            "GL_ANGLE_texture_compression_dxt5"));
            float* aliasedLineWidthRange = stackalloc float[2];
            gl.GetFloat(
                GetPName.AliasedLineWidthRange,
                aliasedLineWidthRange);
            _wireframeEffectiveLineWidth =
                ResolveEffectiveLineWidthOrRequested(
                requested: 1.25f,
                aliasedLineWidthRange[0],
                aliasedLineWidthRange[1]);
            _gpuTimers = new MapRenderOpenGlGpuTimerCoordinator(
                new SilkMapRenderOpenGlTimeElapsedQueryApi(gl));
        }
        catch
        {
            _sharedProgramUsage.Dispose();
            if (ownsSharedProgramCache)
                sharedProgramCache.Dispose();
            throw;
        }
    }

    private static int ConfigureParallelShaderCompilation(GL gl)
    {
        const string extension = "GL_ARB_parallel_shader_compile";
        const string entryPoint = "glMaxShaderCompilerThreadsARB";
        if (!gl.IsExtensionPresent(extension) ||
            !gl.Context.TryGetProcAddress(entryPoint, out nint address) ||
            address == 0)
        {
            return 0;
        }

        try
        {
            // Keep one logical CPU available to the host and cap the hint so
            // a very wide workstation cannot turn map loading into an
            // unbounded compiler-memory spike. The driver may use fewer
            // workers; this only removes the accidental one-link-at-a-time
            // synchronization pattern.
            uint workerLimit = checked((uint)Math.Clamp(
                Environment.ProcessorCount - 1,
                1,
                16));
            Marshal.GetDelegateForFunctionPointer<
                MaxShaderCompilerThreadsArb>(address)(workerLimit);
            return checked((int)workerLimit);
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            MarshalDirectiveException)
        {
            // Extension dispatch is an optimization. Deferred submission is
            // still valid, and the ordinary LinkStatus completion path stays
            // authoritative when the optional entry point cannot be called.
            return 0;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void MaxShaderCompilerThreadsArb(uint count);

    private static IEnumerable<ShaderExecutionContract?>
        EnumerateStaticModelShaderExecutions(MapRenderScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        IEnumerable<MapRenderInstancedTexturedBatch> batches = scene
            .InstancedTexturedBatches
            .Concat(scene.StaticModelLodTexturedBatches)
            .Concat(
                scene.ExactNormalCameraStaticModelTexturedBatches)
            .Concat(scene.ShadowAllocatedStaticModelTexturedBatches);
        if (scene.ReceiverVariants is { } receiverVariants)
        {
            batches = batches.Concat(
                receiverVariants.StaticModels.Values.SelectMany(
                    channel => channel));
        }

        foreach (MapRenderInstancedTexturedBatch batch in batches)
        {
            yield return batch.ShaderExecution;
            if (batch.DepthPrepassShaderExecution is { } depthExecution)
                yield return depthExecution;
        }
    }

    public void Load(MapRenderScene scene) =>
        Load(scene, sceneSnapshot: null);

    /// <summary>
    /// Loads one scene using an optional prebuilt backend-neutral snapshot.
    /// Studio prepares that snapshot off the UI thread, avoiding a duplicate
    /// full-map resource freeze after the OpenGL context becomes current.
    /// </summary>
    public void Load(
        MapRenderScene scene,
        RenderSceneSnapshot? sceneSnapshot) =>
        LoadCore(
            scene,
            sceneSnapshot,
            initialView: null,
            loadProgress: null);

    /// <summary>
    /// Loads a scene for an already-known first view. Static-model OpenGL
    /// resources are materialized conservatively for that camera and later
    /// views synchronously admit newly-required complete authored groups.
    /// </summary>
    public void Load(
        MapRenderScene scene,
        RenderSceneSnapshot? sceneSnapshot,
        RenderCamera initialCamera,
        float initialAspectRatio) =>
        Load(
            scene,
            sceneSnapshot,
            initialCamera,
            initialAspectRatio,
            loadProgress: null);

    /// <summary>
    /// Loads a scene for an already-known first view and reports synchronous
    /// initialization diagnostics to an optional caller sink. The callback
    /// may receive verbose per-resource checkpoints; reporting is advisory
    /// and cannot fault renderer loading.
    /// </summary>
    public void Load(
        MapRenderScene scene,
        RenderSceneSnapshot? sceneSnapshot,
        RenderCamera initialCamera,
        float initialAspectRatio,
        Action<string>? loadProgress)
    {
        if (!(initialAspectRatio > 0f) ||
            !float.IsFinite(initialAspectRatio))
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialAspectRatio));
        }

        LoadCore(
            scene,
            sceneSnapshot,
            new ProgressiveStaticInitialView(
                initialCamera,
                initialAspectRatio),
            loadProgress);
    }

    private void LoadCore(
        MapRenderScene scene,
        RenderSceneSnapshot? sceneSnapshot,
        ProgressiveStaticInitialView? initialView,
        Action<string>? loadProgress)
    {
        using LoadProgressScope loadProgressScope =
            BeginLoadProgress(loadProgress);
        ThrowIfUnavailable();
        ArgumentNullException.ThrowIfNull(scene);
        ResetLoadTraceSequences();
        long rendererLoadStarted =
            System.Diagnostics.Stopwatch.GetTimestamp();
        long rendererLoadPhaseStarted = rendererLoadStarted;
        var rendererLoadPhases = new List<string>(8);
        void BeginLoadPhase(string name)
        {
            ReportLoadProgress(
                $"renderer phase started: {name}");
            rendererLoadPhaseStarted =
                System.Diagnostics.Stopwatch.GetTimestamp();
        }
        void RecordLoadPhase(string name)
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            string phase =
                $"{name}={System.Diagnostics.Stopwatch.GetElapsedTime(rendererLoadPhaseStarted, now).TotalMilliseconds:0}ms" +
                $"(programs={_authoredMaterials.ProgramCount}," +
                $"sharedLinks={_sharedProgramCache.CreateTelemetry().SuccessfulUniqueLinkCount}," +
                $"textures={_textureHandles.Count})";
            rendererLoadPhases.Add(phase);
            ReportLoadProgress(
                $"renderer phase completed: {phase}");
        }

        BeginLoadPhase("core-programs");

        bool isolateWorldSurface = IsolatedWorldSurfaceIndex.HasValue;
        if (sceneSnapshot is not null &&
            !string.Equals(
                sceneSnapshot.Name,
                scene.Name,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The prebuilt render snapshot belongs to another map scene.",
                nameof(sceneSnapshot));
        }
        if (isolateWorldSurface && sceneSnapshot is not null)
        {
            throw new ArgumentException(
                "An isolated world-surface render requires a snapshot without diagnostic geometry.",
                nameof(sceneSnapshot));
        }
        sceneSnapshot ??= RenderSceneSnapshotBuilder.Create(
            scene,
            revision: 0,
            includeDiagnosticGeometry: !isolateWorldSurface);
        MapRenderStaticModelLightingContract.ValidateAtlasAvailability(
            scene.StaticModelLightingAtlas,
            EnumerateStaticModelShaderExecutions(scene));
        DeleteLoadedResources();
        _genericInactiveTexture = CreateGenericInactiveTexture();
        AccountSceneTexturePayloads(scene);
        _progressiveStaticMaterializationEnabled =
            initialView.HasValue &&
            !isolateWorldSurface;
        _renderSceneSnapshot = sceneSnapshot;
        _loadedIsolatedWorldSurfaceIndex = IsolatedWorldSurfaceIndex;
        _previewWorldSource = scene.WorldSource;
        _editorAnimationStartTimestamp =
            System.Diagnostics.Stopwatch.GetTimestamp();
        _editorPreviewLighting = scene.EditorPreviewLighting ??
            MapRenderEditorPreviewLightingPlanner.Create(
                comWorld: null);
        _editorPreviewVision = scene.EditorPreviewVision?.Vision;
        _editorPreviewEffectivePost = scene.EditorPreviewEffectivePost;
        RecomputeEditorPreviewDirectionalSunColors();
        InitializeEditorPreviewSceneLightFrame(
            scene.SceneLightAttenuationTextures);
        _editorPreviewAtmosphere = scene.EditorPreviewAtmosphere ??
            MapRenderEditorPreviewAtmospherePlanner.Create(
                scene.Bounds);
        _editorPreviewFogRenderingEnabled =
            _editorPreviewFogRenderingEnabledSetting;
        _editorPreviewAtmosphereEnabled =
            _editorPreviewFogRenderingEnabled &&
            _editorPreviewAtmosphere.IsEnabled;
        _editorPreviewGenericActiveFog = scene.EditorPreviewActiveFog;
        _editorPreviewActiveFog = _editorPreviewGenericActiveFog ??
            (_editorPreviewAtmosphere.IsEnabled
                ? MapRenderEditorPreviewActiveFogAdapter.Create(
                    _editorPreviewAtmosphere,
                    _editorPreviewLighting)
                : null);
        _staticModelLightingAtlas = scene.StaticModelLightingAtlas;
        _staticModelLightingWorkingSet =
            _staticModelLightingAtlas is { } staticLightingAtlas
                ? new MapRenderStaticModelLightingWorkingSet(
                    staticLightingAtlas.EntryCount)
                : null;
        _staticModelLightingPhysicalRgbaBytes =
            _staticModelLightingAtlas?.RgbaBytes.ToArray();
        _staticModelLightingAtlasTexture =
            _staticModelLightingAtlas is { } initialStaticLightingAtlas
                ? CreateStaticModelLightingAtlasTexture(
                    initialStaticLightingAtlas)
                : 0;
        _sceneLightAttenuationTextureHandles = scene
            .SceneLightAttenuationTextures
            .Select(texture =>
                texture is not null && CanUploadTexture(texture)
                    ? CreateTexture(
                        texture,
                        pinForRendererLifetime: true)
                    : 0)
            .ToArray();
        using var loadShaderObjectCache =
            BeginLoadShaderObjectCache(
                cacheAuthoredProgramPreparations: true);
        _solidProgram = CreateProgram(VertexShaderSource, FragmentShaderSource);
        _solidViewProjectionLocation = _gl.GetUniformLocation(_solidProgram, "uViewProjection");
        _solidUseInstancingLocation = _gl.GetUniformLocation(_solidProgram, "uUseInstancing");
        _depthPrepassProgram = CreateProgram(
            StandardDepthPrepassVertexShaderSource,
            StandardDepthPrepassFragmentShaderSource);
        _depthPrepassViewProjectionLocation = _gl.GetUniformLocation(
            _depthPrepassProgram,
            "uViewProjection");
        _depthPrepassUseInstancingLocation = _gl.GetUniformLocation(
            _depthPrepassProgram,
            "uUseInstancing");
        _depthPrepassVegetationParametersLocation = _gl.GetUniformLocation(
            _depthPrepassProgram,
            "uVegetationParameters");
        _depthPrepassVegetationTimeLocation = _gl.GetUniformLocation(
            _depthPrepassProgram,
            "uVegetationTime");
        _depthPrepassVegetationBoundsLocation = _gl.GetUniformLocation(
            _depthPrepassProgram,
            "uVegetationBounds");
        _texturedProgram = CreateProgram(TexturedVertexShaderSource, TexturedFragmentShaderSource);
        _texturedViewProjectionLocation = _gl.GetUniformLocation(_texturedProgram, "uViewProjection");
        _texturedUseInstancingLocation = _gl.GetUniformLocation(_texturedProgram, "uUseInstancing");
        _texturedColorSamplerLocations = Enumerable.Range(0, MapRenderScene.MaxColorLayerCount)
            .Select(index => _gl.GetUniformLocation(_texturedProgram, $"uColorTexture{index}"))
            .ToArray();
        _texturedColorLayerCountLocation = _gl.GetUniformLocation(_texturedProgram, "uColorLayerCount");
        _texturedBlendWeightComponentLocations = Enumerable.Range(1, MapRenderScene.MaxColorLayerCount - 1)
            .Select(index => _gl.GetUniformLocation(_texturedProgram, $"uBlendWeightComponent{index}"))
            .ToArray();
        _texturedLightmapSamplerLocation = _gl.GetUniformLocation(_texturedProgram, "uLightmapTexture");
        _texturedHasLightmapLocation = _gl.GetUniformLocation(_texturedProgram, "uHasLightmap");
        _texturedStaticModelLightingSamplerLocation =
            _gl.GetUniformLocation(
                _texturedProgram,
                "uStaticModelLightingAtlas");
        _texturedHasStaticModelLightingLocation =
            _gl.GetUniformLocation(
                _texturedProgram,
                "uHasStaticModelLighting");
        _texturedStaticModelLightingSamplerTransformLocation =
            _gl.GetUniformLocation(
                _texturedProgram,
                "uStaticModelLightingSamplerTransform");
        _texturedAlphaTestEnabledLocation = _gl.GetUniformLocation(_texturedProgram, "uAlphaTestEnabled");
        _texturedAlphaFuncLocation = _gl.GetUniformLocation(_texturedProgram, "uAlphaFunc");
        _texturedAlphaRefLocation = _gl.GetUniformLocation(_texturedProgram, "uAlphaRef");
        _texturedShaderPackerSrgbEnabledLocation = _gl.GetUniformLocation(
            _texturedProgram,
            "uShaderPackerSrgbEnabled");
        _texturedLinearizeColorInputsLocation = _gl.GetUniformLocation(
            _texturedProgram,
            "uLinearizeColorInputs");
        _texturedPremultiplyAlphaLocation = _gl.GetUniformLocation(
            _texturedProgram,
            "uPremultiplyAlpha");
        _texturedLightingEnabledLocation = _gl.GetUniformLocation(_texturedProgram, "uLightingEnabled");
        _texturedAmbientColorLocation = _gl.GetUniformLocation(_texturedProgram, "uAmbientColor");
        _texturedHasDirectionalSunDiffuseLocation = _gl.GetUniformLocation(
            _texturedProgram,
            "uHasDirectionalSunDiffuse");
        _texturedHasDirectionalSunSpecularLocation = _gl.GetUniformLocation(
            _texturedProgram,
            "uHasDirectionalSunSpecular");
        _texturedDirectionalSunDirectionLocation = _gl.GetUniformLocation(_texturedProgram, "uDirectionalSunDirection");
        _texturedDirectionalSunDiffuseColorLocation = _gl.GetUniformLocation(
            _texturedProgram,
            "uDirectionalSunDiffuseColor");
        _texturedDirectionalSunSpecularColorLocation = _gl.GetUniformLocation(
            _texturedProgram,
            "uDirectionalSunSpecularColor");
        _texturedCameraPositionLocation = _gl.GetUniformLocation(_texturedProgram, "uCameraPosition");
        _texturedFogEnabledLocation = _gl.GetUniformLocation(_texturedProgram, "uFogEnabled");
        _texturedFogUseActiveStateLocation = _gl.GetUniformLocation(
            _texturedProgram,
            "uFogUseActiveState");
        _texturedFogColorLocation = _gl.GetUniformLocation(_texturedProgram, "uFogColor");
        _texturedFogStartLocation = _gl.GetUniformLocation(_texturedProgram, "uFogStart");
        _texturedFogEndLocation = _gl.GetUniformLocation(_texturedProgram, "uFogEnd");
        _texturedFogMaxOpacityLocation = _gl.GetUniformLocation(_texturedProgram, "uFogMaxOpacity");
        _texturedFogDistanceScaleLocation = _gl.GetUniformLocation(
            _texturedProgram,
            "uFogDistanceScale");
        _texturedFogDistanceBiasLocation = _gl.GetUniformLocation(
            _texturedProgram,
            "uFogDistanceBias");
        _texturedFogMinimumVisibilityLocation = _gl.GetUniformLocation(
            _texturedProgram,
            "uFogMinimumVisibility");
        _texturedSunFogEnabledLocation = _gl.GetUniformLocation(
            _texturedProgram,
            "uSunFogEnabled");
        _texturedSunFogColorLocation = _gl.GetUniformLocation(
            _texturedProgram,
            "uSunFogColor");
        _texturedSunFogDirectionLocation = _gl.GetUniformLocation(
            _texturedProgram,
            "uSunFogDirection");
        _texturedSunFogDistanceScaleLocation = _gl.GetUniformLocation(
            _texturedProgram,
            "uSunFogDistanceScale");
        _texturedSunFogEndCosineLocation = _gl.GetUniformLocation(
            _texturedProgram,
            "uSunFogEndCosine");
        _texturedSunFogAngularScaleLocation = _gl.GetUniformLocation(
            _texturedProgram,
            "uSunFogAngularScale");
        _texturedVegetationParametersLocation = _gl.GetUniformLocation(_texturedProgram, "uVegetationParameters");
        _texturedVegetationTimeLocation = _gl.GetUniformLocation(_texturedProgram, "uVegetationTime");
        _texturedVegetationBoundsLocation = _gl.GetUniformLocation(_texturedProgram, "uVegetationBounds");
        _texturedNormalSamplerLocations = Enumerable.Range(0, 4)
            .Select(index => _gl.GetUniformLocation(_texturedProgram, $"uNormalTexture{index}"))
            .ToArray();
        _texturedHasNormalLocations = Enumerable.Range(0, 4)
            .Select(index => _gl.GetUniformLocation(_texturedProgram, $"uHasNormalTexture{index}"))
            .ToArray();
        _texturedSpecularSamplerLocations = Enumerable.Range(0, 3)
            .Select(index => _gl.GetUniformLocation(_texturedProgram, $"uSpecularTexture{index}"))
            .ToArray();
        _texturedHasSpecularLocations = Enumerable.Range(0, 3)
            .Select(index => _gl.GetUniformLocation(_texturedProgram, $"uHasSpecularTexture{index}"))
            .ToArray();
        _skyProgram = CreateProgram(SkyVertexShaderSource, SkyFragmentShaderSource);
        _skyViewProjectionLocation = _gl.GetUniformLocation(_skyProgram, "uViewProjection");
        _skyTextureLocation = _gl.GetUniformLocation(_skyProgram, "uSkyTexture");
        InitializeFixedSamplerUniforms();
        RecordLoadPhase("core-programs");
        BeginLoadPhase("authored-link-submission");
        SubmitSceneAuthoredProgramLinks(scene);
        RecordLoadPhase("authored-link-submission");
        BeginLoadPhase("base-world");
        long baseWorldSubphaseStarted =
            System.Diagnostics.Stopwatch.GetTimestamp();
        bool hasDiagnosticResources =
            !isolateWorldSurface &&
            !sceneSnapshot.Diagnostics.IsEmpty;
        ReportLoadProgress(
            $"renderer base-world subphase started: diagnostic-resources; " +
            $"enabled={hasDiagnosticResources}; " +
            $"solidVertices={scene.SolidVertices.Length / MapRenderScene.VertexFloatCount}; " +
            $"solidIndices={scene.SolidIndices.Length}; " +
            $"fallbackVertices={scene.FallbackSolidVertices.Length / MapRenderScene.VertexFloatCount}; " +
            $"fallbackIndices={scene.FallbackSolidIndices.Length}; " +
            $"instancedBatches={scene.InstancedSolidBatches.Count}");
        if (hasDiagnosticResources)
        {
            if (LoadProgressEnabled)
            {
                using (BeginLoadTraceContext(
                           "base-world-diagnostic=solid"))
                {
                    _solid = CreateMesh(
                        scene.SolidVertices,
                        scene.SolidIndices);
                }
            }
            else
            {
                _solid = CreateMesh(
                    scene.SolidVertices,
                    scene.SolidIndices);
            }

            if (LoadProgressEnabled)
            {
                using (BeginLoadTraceContext(
                           "base-world-diagnostic=fallback"))
                {
                    _fallbackSolid = CreateMesh(
                        scene.FallbackSolidVertices,
                        scene.FallbackSolidIndices);
                }
            }
            else
            {
                _fallbackSolid = CreateMesh(
                    scene.FallbackSolidVertices,
                    scene.FallbackSolidIndices);
            }

            var instancedSolid = new GlInstancedMesh[
                scene.InstancedSolidBatches.Count];
            for (int index = 0;
                 index < scene.InstancedSolidBatches.Count;
                 index++)
            {
                if (LoadProgressEnabled)
                {
                    using var context = BeginLoadTraceContext(
                        $"base-world-diagnostic-instanced={index + 1}/" +
                        scene.InstancedSolidBatches.Count);
                    instancedSolid[index] = CreateInstancedSolidMesh(
                        scene.InstancedSolidBatches[index]);
                }
                else
                {
                    instancedSolid[index] = CreateInstancedSolidMesh(
                        scene.InstancedSolidBatches[index]);
                }
            }
            _instancedSolid = instancedSolid;
        }
        else
        {
            _solid = default;
            _fallbackSolid = default;
            _instancedSolid = [];
        }
        _diagnosticResourceCatalog = !hasDiagnosticResources
            ? MapRenderOpenGlNormalCameraDiagnosticResourceCatalog
                .CreateUnavailable(sceneSnapshot)
            : MapRenderOpenGlNormalCameraDiagnosticResourceCatalog.Create(
                sceneSnapshot,
                _fallbackSolid,
                _solid,
                _instancedSolid);
        ReportLoadProgress(
            $"renderer base-world subphase completed: diagnostic-resources; " +
            $"elapsed={System.Diagnostics.Stopwatch.GetElapsedTime(baseWorldSubphaseStarted).TotalMilliseconds:0}ms");
        baseWorldSubphaseStarted =
            System.Diagnostics.Stopwatch.GetTimestamp();
        ReportLoadProgress(
            $"renderer base-world subphase started: batch-selection; " +
            $"sourceBatches={scene.TexturedBatches.Count}; " +
            $"isolatedSurface={IsolatedWorldSurfaceIndex?.ToString() ?? "none"}");
        IReadOnlyList<MapRenderTexturedBatch> renderedBatches = isolateWorldSurface
            ? scene.TexturedBatches
                .Select(batch => CreateIsolatedWorldSurfaceBatch(batch, IsolatedWorldSurfaceIndex!.Value))
                .Where(batch => batch is not null)
                .Select(batch => batch!)
                .ToArray()
            : scene.TexturedBatches;
        _renderedWorldBatches = renderedBatches.ToArray();
        ReportLoadProgress(
            $"renderer base-world subphase completed: batch-selection; " +
            $"renderedBatches={renderedBatches.Count}; " +
            $"elapsed={System.Diagnostics.Stopwatch.GetElapsedTime(baseWorldSubphaseStarted).TotalMilliseconds:0}ms");
        baseWorldSubphaseStarted =
            System.Diagnostics.Stopwatch.GetTimestamp();
        int authoredCandidateCount = LoadProgressEnabled
            ? scene.TexturedBatches.Count(batch =>
                IncludesAuthoredProgramCandidate(
                    HasAuthoredTechniquePass(batch.Pass)))
            : 0;
        ReportLoadProgress(
            $"renderer base-world subphase started: authored-program-preflight; " +
            $"sourceBatches={scene.TexturedBatches.Count}; " +
            $"candidates={authoredCandidateCount}");
        IReadOnlySet<AuthoredProgramGroupKey> authorizedAuthoredProgramGroups =
            AuthorizeAtomicProgramGroups(
                scene.TexturedBatches,
                batch => IncludesAuthoredProgramCandidate(
                    HasAuthoredTechniquePass(batch.Pass)),
                AuthoredProgramGroup,
                PreflightBaseWorldAuthoredProgram);
        ReportLoadProgress(
            $"renderer base-world subphase completed: authored-program-preflight; " +
            $"authorizedGroups={authorizedAuthoredProgramGroups.Count}; " +
            $"programs={_authoredMaterials.ProgramCount}; " +
            $"failures={_authoredMaterials.FailureCount}; " +
            $"elapsed={System.Diagnostics.Stopwatch.GetElapsedTime(baseWorldSubphaseStarted).TotalMilliseconds:0}ms");
        baseWorldSubphaseStarted =
            System.Diagnostics.Stopwatch.GetTimestamp();
        ReportLoadProgress(
            $"renderer base-world subphase started: resource-shells; " +
            $"batches={renderedBatches.Count}; " +
            $"authorizedGroups={authorizedAuthoredProgramGroups.Count}");
        _textured = new GlTexturedMesh[renderedBatches.Count];
        for (int index = 0; index < renderedBatches.Count; index++)
        {
            MapRenderTexturedBatch batch = renderedBatches[index];
            if (!LoadProgressEnabled)
            {
                _textured[index] = CreateWorldTexturedResourceShell(
                    batch,
                    authorizedAuthoredProgramGroups);
                continue;
            }

            long traceSequence = NextLoadBatchTraceSequence();
            using var context = BeginLoadTraceContext(
                $"base-world-resource={traceSequence}; " +
                $"batch={index + 1}/{renderedBatches.Count}; " +
                DescribeWorldBatchTraceContext(batch));
            bool reportProgress =
                index == 0 ||
                (index + 1) % BaseWorldResourceProgressInterval == 0 ||
                index == renderedBatches.Count - 1;
            if (reportProgress)
            {
                ReportLoadDetail(
                    "resource-shell progress checkpoint started");
            }
            long resourceStarted =
                System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                GlTexturedMesh resource =
                    CreateWorldTexturedResourceShell(
                        batch,
                        authorizedAuthoredProgramGroups);
                _textured[index] = resource;
                double elapsedMilliseconds =
                    System.Diagnostics.Stopwatch
                        .GetElapsedTime(resourceStarted)
                        .TotalMilliseconds;
                if (reportProgress || elapsedMilliseconds >= 250d)
                {
                    ReportLoadDetail(
                        $"resource shell completed; " +
                        $"slow={elapsedMilliseconds >= 250d}; " +
                        $"executable={resource.IndexCount != 0}; " +
                        $"translated={resource.RsxProgram.Handle != 0}; " +
                        $"colorTextures={resource.ColorTextures?.Length ?? 0}; " +
                        $"rsxSamplers={resource.RsxSamplerBindings?.Length ?? 0}; " +
                        $"elapsed={elapsedMilliseconds:0}ms");
                }
            }
            catch (Exception exception)
            {
                ReportLoadDetail(
                    $"resource shell failed; " +
                    $"exception={exception.GetType().FullName}; " +
                    $"message={QuoteLoadTraceValue(exception.Message)}; " +
                    $"elapsed={System.Diagnostics.Stopwatch.GetElapsedTime(resourceStarted).TotalMilliseconds:0}ms");
                throw;
            }
        }
        ReportLoadProgress(
            $"renderer base-world subphase completed: resource-shells; " +
            $"batches={_textured.Length}; " +
            $"programs={_authoredMaterials.ProgramCount}; " +
            $"textures={_textureHandles.Count}; " +
            $"elapsed={System.Diagnostics.Stopwatch.GetElapsedTime(baseWorldSubphaseStarted).TotalMilliseconds:0}ms");
        baseWorldSubphaseStarted =
            System.Diagnostics.Stopwatch.GetTimestamp();
        ReportLoadProgress(
            $"renderer base-world subphase started: geometry-arenas; " +
            $"batches={renderedBatches.Count}");
        PackWorldGeometryArenas(renderedBatches);
        ReportLoadProgress(
            $"renderer base-world subphase completed: geometry-arenas; " +
            $"arenas={WorldGeometryArenaUploadCount}; " +
            $"sourceBatches={WorldGeometrySourceBatchCount}; " +
            $"bufferUploads={WorldGeometryImmutableBufferUploadCount}; " +
            $"bufferBytes={WorldGeometryImmutableBufferUploadBytes}; " +
            $"translatedArenas={WorldGeometryTranslatedArenaCount}; " +
            $"maxTranslatedAttributes={WorldGeometryMaximumTranslatedArenaAttributeCount}; " +
            $"elapsed={System.Diagnostics.Stopwatch.GetElapsedTime(baseWorldSubphaseStarted).TotalMilliseconds:0}ms");
        baseWorldSubphaseStarted =
            System.Diagnostics.Stopwatch.GetTimestamp();
        ReportLoadProgress(
            $"renderer base-world subphase started: bounds; " +
            $"batches={_textured.Length}");
        for (int index = 0; index < _textured.Length; index++)
        {
            GlTexturedMesh mesh = _textured[index];
            if (mesh.VertexArray == 0)
                continue;
            MapRenderTexturedBatch batch = renderedBatches[index];
            _textured[index] = mesh with
            {
                WorldSurfaceIndex = ResolveSingleWorldSurfaceIndex(batch),
                WorldBounds = IncludeTexturedVertexBounds(
                    RenderBounds.Empty,
                    batch.Vertices)
            };
        }
        ReportLoadProgress(
            $"renderer base-world subphase completed: bounds; " +
            $"elapsed={System.Diagnostics.Stopwatch.GetElapsedTime(baseWorldSubphaseStarted).TotalMilliseconds:0}ms");
        baseWorldSubphaseStarted =
            System.Diagnostics.Stopwatch.GetTimestamp();
        ReportLoadProgress(
            "renderer base-world subphase started: multidraw-groups");
        AssignWorldMultiDrawBatchGroupIds();
        ReportLoadProgress(
            $"renderer base-world subphase completed: multidraw-groups; " +
            $"colorGroups={_nextWorldMultiDrawBatchGroupId}; " +
            $"depthGroups={_nextWorldDepthMultiDrawBatchGroupId}; " +
            $"elapsed={System.Diagnostics.Stopwatch.GetElapsedTime(baseWorldSubphaseStarted).TotalMilliseconds:0}ms");
        baseWorldSubphaseStarted =
            System.Diagnostics.Stopwatch.GetTimestamp();
        ReportLoadProgress(
            $"renderer base-world subphase started: surface-runtimes; " +
            $"batches={renderedBatches.Count}");
        BuildWorldSurfaceBatchRuntimes(renderedBatches);
        ReportLoadProgress(
            $"renderer base-world subphase completed: surface-runtimes; " +
            $"candidates={_worldSurfaceCandidateCount}; " +
            $"candidateIndices={_worldSurfaceCandidateIndexCount}; " +
            $"fallbackBatches={_worldSurfaceFallbackBatchCount}; " +
            $"elapsed={System.Diagnostics.Stopwatch.GetElapsedTime(baseWorldSubphaseStarted).TotalMilliseconds:0}ms");
        RecordLoadPhase("base-world");
        BeginLoadPhase("world-receivers");
        InitializeWorldReceiverVariants(scene, isolateWorldSurface);
        RecordLoadPhase("world-receivers");
        BeginLoadPhase("base-static");
        _usesDynamicStaticLods =
            sceneSnapshot.NormalCameraDraws.SourceCount == 0
                ? RenderSceneSnapshotBuilder.CanUseAllStaticLodBatches(
                    scene)
                : sceneSnapshot.NormalCameraDraws.Coverage ==
                    RenderNormalCameraDrawCoverage
                        .PreparedWorldAndAllStaticLodBatchesWithoutDpvsSelection;
        var preparedStaticObjectLods = new Dictionary<int, int>();
        foreach (MapRenderInstancedTexturedBatch batch in
                 scene.InstancedTexturedBatches)
        {
            foreach (MapRenderStaticModelInstance instance in batch.Instances)
            {
                preparedStaticObjectLods.TryAdd(
                    instance.ObjectIndex,
                    batch.LodIndex);
            }
        }
        foreach (MapRenderStaticModelSchedulingInfo scheduling in
                 scene.StaticModelScheduling)
        {
            preparedStaticObjectLods.TryAdd(
                scheduling.ObjectIndex,
                scheduling.PreparedLodIndex);
        }
        IReadOnlyList<MapRenderInstancedTexturedBatch> staticTexturedBatches =
            _usesDynamicStaticLods
                ? scene.StaticModelLodTexturedBatches
                : scene.InstancedTexturedBatches;
        _baseStaticBatches = isolateWorldSurface
            ? []
            : staticTexturedBatches.ToArray();
        IReadOnlyList<MapRenderInstancedTexturedBatch>
            receiverIdentitySource =
                scene.StaticModelLodTexturedBatches.Count != 0
                    ? scene.StaticModelLodTexturedBatches
                    : scene.InstancedTexturedBatches;
        _staticReceiverExpectedIdentities = isolateWorldSurface
            ? []
            : receiverIdentitySource
                .Where(batch => batch.LodIndex >= 0)
                .SelectMany(batch => batch.Instances.Select(instance =>
                    new MapRenderStaticModelReceiverIdentity(
                        instance,
                        batch.LodIndex)))
                .Where(identity => identity.CameraRegion is
                    GfxCameraRegionType.LitOpaque or
                    GfxCameraRegionType.LightMapOpaque)
                .Distinct()
                .ToArray();
        MapRenderInstancedTexturedBatch[] exactNormalCameraStaticBatches =
            isolateWorldSurface
                ? []
                : SelectExactNormalCameraStaticBatches(
                    scene,
                    _usesDynamicStaticLods,
                    preparedStaticObjectLods);
        _exactNormalCameraStaticExpectedIdentities =
            exactNormalCameraStaticBatches
                .Where(batch => batch.LodIndex >= 0)
                .SelectMany(batch => batch.Instances.Select(instance =>
                    new MapRenderStaticModelReceiverIdentity(
                        instance,
                        batch.LodIndex)))
                .Distinct()
                .ToArray();
        InitializeStaticSchedulingState(
            scene,
            _baseStaticBatches,
            preparedStaticObjectLods);
        if (_progressiveStaticMaterializationEnabled &&
            initialView is { } progressiveInitialView)
        {
            SelectProgressiveStaticObjects(
                progressiveInitialView.Camera,
                progressiveInitialView.AspectRatio);
        }
        InitializeBaseStaticResources();
        RecordLoadPhase("base-static");
        BeginLoadPhase("exact-normal-camera-static");
        InitializeExactNormalCameraStaticResources(
            exactNormalCameraStaticBatches);
        RecordLoadPhase("exact-normal-camera-static");
        BeginLoadPhase("static-receivers");
        InitializeStaticReceiverVariants(scene, isolateWorldSurface);
        RecordLoadPhase("static-receivers");
        BeginLoadPhase("static-prefetch");
        if (_progressiveStaticMaterializationEnabled &&
            initialView is { } progressivePrefetchView)
        {
            PrefetchInitialStaticNeighborhood(progressivePrefetchView);
        }
        RecordLoadPhase("static-prefetch");
        BeginLoadPhase("draw-groups");
        RebuildEditorStaticDrawGroups(
            sceneSnapshot,
            isolateWorldSurface);
        RecordLoadPhase("draw-groups");
        BeginLoadPhase("sky-wire");
        _previewSceneGeneration++;
        CancelPreviewDpvsWork();
        _previewDpvsCache = new MapRenderWorldDpvsCameraOnlyVisibilityCache();
        _previewDpvsWorker = CreatePreviewDpvsWorker(
            _previewWorldSource,
            _previewDpvsCache);
        _previewFrustumCache.Clear();
        _skies = isolateWorldSurface
            ? []
            : CreateSkyMeshes(scene.Skies, sceneSnapshot);
        _skyResourceCatalog = _skies.Length == 0
            ? null
            : MapRenderOpenGlNormalCameraSkyResourceCatalog.Create(
                sceneSnapshot,
                _skies);
        _wire = CreateWireframeMesh(sceneSnapshot);
        _editorSelectionOutlineMesh =
            CreateEditorSelectionOutlineMesh();
        _wireframeResourceCatalog = sceneSnapshot.Wireframe is null
            ? null
            : MapRenderOpenGlWireframeResourceCatalog.Create(
                sceneSnapshot,
                _wire);
        RecordLoadPhase("sky-wire");
        BeginLoadPhase("presentation-shadow");
        int openGlLoweringReadyProgramCount = scene.TexturedBatches
            .GroupBy(
                batch => batch.ShaderExecution.ProgramCacheKey,
                StringComparer.Ordinal)
            .Select(group => group.First().ShaderExecution)
            .Count(execution =>
                _authoredMaterials.IsVertexProgramLowerable(execution) &&
                _authoredMaterials.IsFragmentProgramLowerable(execution));
        int renderCapableBatchCount = scene.TexturedBatches.Count(batch => batch.ShaderExecution.ProgramExecutionReady);
        string pipelineSummary =
            $"Renderer pipeline: RSX GLSL validation: openGlLoweringReady={openGlLoweringReadyProgramCount} " +
            $"parallelLinkWorkers={_parallelShaderCompilerThreadLimit} " +
            $"semanticPrograms={_authoredMaterials.ProgramCount} " +
            $"sharedUniqueLinks={_sharedProgramCache.CreateTelemetry().SuccessfulUniqueLinkCount} " +
            $"sharedLinkReuses={_sharedProgramCache.CreateTelemetry().LinkReuseCount} " +
            $"failed={_authoredMaterials.FailureCount} " +
            $"renderCapableBatches={renderCapableBatchCount} " +
            $"vertexPlacementDiagnostic={UseRsxVertexPlacementDiagnostic} " +
            $"fragmentOutputDiagnostic={RsxFragmentOutputDiagnostic?.ToString() ?? "none"} " +
            $"isolatedWorldSurface={IsolatedWorldSurfaceIndex?.ToString() ?? "none"} " +
            $"isolatedBatches={renderedBatches.Count} " +
            $"worldSurfaceSpans={_worldSurfaceCandidateCount} " +
            $"worldSurfaceTriangles={_worldSurfaceCandidateIndexCount / 3} " +
            $"worldSpanFallbackBatches={_worldSurfaceFallbackBatchCount} " +
            $"staticBatches=materialized:{StaticResourceMaterializedBatchCount}/" +
            $"rejected:{StaticResourceRejectedBatchCount}/" +
            $"resolved:{StaticResourceResolvedBatchCount}/" +
            $"source:{StaticResourceSourceBatchCount}/" +
            $"deferred:{StaticResourceDeferredBatchCount} " +
            $"skies={_skies.Length}.";
        Console.WriteLine(pipelineSummary);
        ReportLoadProgress(pipelineSummary);
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Lequal);
        _gl.Disable(EnableCap.CullFace);
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.PolygonOffsetFill);
        _gl.ColorMask(true, true, true, true);
        Vector3 clearColor = _editorPreviewAtmosphereEnabled
            ? _editorPreviewAtmosphere!.FogColor
            : new Vector3(0.42f, 0.49f, 0.52f);
        _gl.ClearColor(clearColor.X, clearColor.Y, clearColor.Z, 1f);

        if (scene.WorldSource is { } editorPreviewWorldSource)
        {
            CreateEditorPreviewPresentationSession(editorPreviewWorldSource);
        }
        InitializeSunShadowCasterResources(scene, isolateWorldSurface);
        InitializeSunShadowPipeline(scene, isolateWorldSurface);
        RecordLoadPhase("presentation-shadow");
        ReportLoadProgress("renderer finalization started");
        EstablishStateShadowBaseline();
        _loaded = true;
        long rendererLoadFinished =
            System.Diagnostics.Stopwatch.GetTimestamp();
        MapRenderOpenGlShaderObjectCacheTelemetry shaderObjectTelemetry =
            loadShaderObjectCache.Telemetry;
        OpenGlLinkedProgramHandleCacheTelemetry
            linkedProgramTelemetry =
                _sharedProgramCache.CreateTelemetry();
        OpenGlUniformLocationCacheTelemetry
            uniformLocationTelemetry =
                _authoredMaterials.UniformLocationTelemetry;
        string loadTiming =
            $"Renderer load timing: " +
            $"{string.Join(", ", rendererLoadPhases)}, " +
            $"linkedPrograms=requests:{linkedProgramTelemetry.SemanticRequestCount}/" +
            $"sourceLinked:{linkedProgramTelemetry.SuccessfulUniqueLinkCount}/" +
            $"binaryHits:{linkedProgramTelemetry.ProgramBinaryLoadHitCount}/" +
            $"binaryAttempts:{linkedProgramTelemetry.ProgramBinaryLoadAttemptCount}/" +
            $"binaryStores:{linkedProgramTelemetry.ProgramBinaryStoreCount}/" +
            $"deferredSubmissions:{linkedProgramTelemetry.DeferredLinkSubmissionCount}/" +
            $"pending:{linkedProgramTelemetry.PendingLinkCount}/" +
            $"reused:{linkedProgramTelemetry.LinkReuseCount}/" +
            $"failed:{linkedProgramTelemetry.FailedUniqueLinkCount}/" +
            $"capacityBypass:{linkedProgramTelemetry.CapacityBypassCount}/" +
            $"cached:{linkedProgramTelemetry.CachedHandleCount}/" +
            $"capacity:{linkedProgramTelemetry.MaximumEntryCount}, " +
            $"uniformLocations=requests:{uniformLocationTelemetry.RequestCount}/" +
            $"queried:{uniformLocationTelemetry.QueryCount}/" +
            $"reused:{uniformLocationTelemetry.CacheHitCount}, " +
            $"shaderObjects=requests:{shaderObjectTelemetry.RequestCount}/" +
            $"compiled:{shaderObjectTelemetry.SuccessfulCompilationCount}/" +
            $"reused:{shaderObjectTelemetry.CacheHitCount}/" +
            $"compileAttempts:{shaderObjectTelemetry.CompileAttemptCount}/" +
            $"compileTime:{shaderObjectTelemetry.CompileElapsed.TotalMilliseconds:0}ms, " +
            $"texturePayloads=bcSupported:{SupportsAuthoredBcTextureUploads}/" +
            $"decodedObserved:{TextureDecodedFallbackBytesObserved}/" +
            $"decodedRetained:{TextureDecodedFallbackBytesRetained}/" +
            $"authoredBc:{TextureAuthoredBcSourceBytes}/" +
            $"gpuResident:{TextureGpuResidentBytes}, " +
            $"total={System.Diagnostics.Stopwatch.GetElapsedTime(rendererLoadStarted, rendererLoadFinished).TotalMilliseconds:0}ms.";
        Console.WriteLine(loadTiming);
        ReportLoadProgress(loadTiming);
    }

    private void RecomputeEditorPreviewDirectionalSunColors()
    {
        if (_editorPreviewLighting?.HasDirectionalSun == true)
        {
            // Vision-provided primary-light scales require
            // drawGroup.useHeroLighting (packed bit 16). The normal
            // world/static paths leave that invocation flag clear, so the
            // generic map fallback uses the renderer dvar scales rather than
            // applying authored hero strengths map-wide.
            MapRenderEditorPreviewPrimaryLightVisionState? primaryLight =
                MapRenderEditorPreviewPrimaryLightInvocationPolicy.Resolve(
                    _editorPreviewVision?.PrimaryLight,
                    useHeroLighting: false);
            DirectionalSunLinearColors sunColors =
                MapRenderEditorDirectCodeConstantProducers.ProduceDirectionalSunLinearColors(
                    _editorPreviewLighting,
                    primaryLight);
            _editorPreviewDirectionalSunDiffuseColor = sunColors.Diffuse;
            _editorPreviewDirectionalSunSpecularColor = sunColors.Specular;
            return;
        }

        _editorPreviewDirectionalSunDiffuseColor = Vector3.Zero;
        _editorPreviewDirectionalSunSpecularColor = Vector3.Zero;
    }

    private void InitializeEditorPreviewSceneLightFrame(
        IReadOnlyList<Texture?> sceneLightAttenuationTextures)
    {
        ArgumentNullException.ThrowIfNull(sceneLightAttenuationTextures);
        _editorPreviewSceneLightFrame = null;
        _editorPreviewSceneLightFrameFailure = null;
        if (_previewWorldSource is not { } source ||
            source.SceneLights.Source is not { } lightSource)
        {
            return;
        }

        int lightCount = lightSource.SelectorState.SceneLightCount;
        long revision = source.AssetPoolRevisionAtConstruction;
        var allocation =
            MapRenderSceneLightShadowAllocationState.CreateAllClear(
                lightCount,
                "EDITOR_PREVIEW_EVENT20_EXPLICIT_ALL_CLEAR_ALLOCATION",
                revision);
        var dynamicInput = new MapRenderNormalCameraSceneLightDynamicInput(
            FrameDirectCodeConstants.DefaultDiffuseColorScale,
            FrameDirectCodeConstants.DefaultSpecularColorScale,
            Vector2.One,
            allocation,
            "EDITOR_PREVIEW_DEFAULT_LIGHT_SCALES_HERO_LIGHTING_FALSE",
            revision);
        MapRenderWorldEvent20SceneLightFrameInputBuildResult result =
            MapRenderWorldEvent20SceneLightFrameInputProducer.Build(
                source,
                dynamicInput,
                eyeOffset: Vector3.Zero,
                sceneLightAttenuationTextures);
        _editorPreviewSceneLightFrame = result.Input;
        _editorPreviewSceneLightFrameFailure = result.Failure;
    }

    public void Resize(int width, int height)
    {
        Resize(MapRenderSurfaceExtents.Unified(
            Math.Max(1, width),
            Math.Max(1, height)));
    }

    public void Resize(MapRenderSurfaceExtents extents)
    {
        ThrowIfUnavailable();
        if (!extents.IsValid)
            throw new ArgumentOutOfRangeException(nameof(extents));
        MapRenderPixelExtent sceneExtent = extents.SceneTarget;
        MapRenderPixelExtent hostExtent = extents.HostFramebuffer;
        if (_editorPreviewPresentationSession is { } editorPresentation)
        {
            editorPresentation.Resize(new MapRenderSurfaceExtents(
                sceneExtent,
                hostExtent));
            LastEditorPreviewPresentationResult = null;
            LastFramePlan = null;
        }
        _width = sceneExtent.Width;
        _height = sceneExtent.Height;
        _hostWidth = hostExtent.Width;
        _hostHeight = hostExtent.Height;
        // Target allocation uses direct resource-setup calls and can leave
        // binding state changed. Resize is cold; reestablish one exact known
        // baseline instead of querying the context on the following frame.
        EstablishStateShadowBaseline();
    }

    public void Render(RenderCamera camera)
    {
        ThrowIfUnavailable();
        if (!_loaded)
            return;

        long shaderProgramCompilationCountAtFrameStart =
            ShaderProgramCompilationCount;
        long frameIndex = _frameTelemetry.BeginCpuFrame();
        _activeRenderFrameIndex = frameIndex;
        _state.BeginFrameCounters();
        _frameDrawCalls = 0;
        _frameLogicalDrawCommands = 0;
        _frameMultiDrawApiCalls = 0;
        _frameStaticExecutionBundleBinds = 0;
        _frameStaticExecutionBundleReuses = 0;
        _frameDepthPassRecorded = false;
        _frameTextureUploadBytes = 0;
        _frameTextureUploadCount = 0;
        _frameTextureEvictionBytes = 0;
        _frameTextureEvictionCount = 0;
        _frameTextureDeferredCount = 0;
        _frameAuthoredBcUploadBytes = 0;
        _criticalTextureHandles.Clear();
        _frameTelemetry.SetCounter(
            MapRenderFrameCounter.SceneTargetWidth,
            _width);
        _frameTelemetry.SetCounter(
            MapRenderFrameCounter.SceneTargetHeight,
            _height);
        _frameTelemetry.SetCounter(
            MapRenderFrameCounter.HostFramebufferWidth,
            _hostWidth);
        _frameTelemetry.SetCounter(
            MapRenderFrameCounter.HostFramebufferHeight,
            _hostHeight);
        bool completed = false;
        try
        {
            _gpuTimers.BeginFrame(
                frameIndex,
                enablePhaseAttribution: true);
            RenderMeasuredFrame(camera);
            completed = true;
        }
        finally
        {
            try
            {
                DrainPendingSunShadowDpvsPreparation();
                if (_gpuTimers.IsFrameActive)
                    _gpuTimers.EndFrame();
                while (_gpuTimers.TryCollectCompletedFrame(out
                           MapRenderOpenGlGpuFrameTiming gpuTiming))
                {
                    _frameTelemetry.RecordGpuFrameTiming(gpuTiming);
                }
                while (_gpuTimers.TryCollectCompletedPhase(out
                           MapRenderOpenGlGpuPhaseTiming gpuPhaseTiming))
                {
                    _frameTelemetry.RecordGpuPhaseTiming(gpuPhaseTiming);
                }

                _frameTelemetry.SetCounter(
                    MapRenderFrameCounter.DrawCalls,
                    _frameDrawCalls);
                _frameTelemetry.SetCounter(
                    MapRenderFrameCounter.LogicalDrawCommands,
                    _frameLogicalDrawCommands);
                _frameTelemetry.SetCounter(
                    MapRenderFrameCounter.MultiDrawApiCalls,
                    _frameMultiDrawApiCalls);
                _frameTelemetry.SetCounter(
                    MapRenderFrameCounter.StaticExecutionBundleBinds,
                    _frameStaticExecutionBundleBinds);
                _frameTelemetry.SetCounter(
                    MapRenderFrameCounter.StaticExecutionBundleReuses,
                    _frameStaticExecutionBundleReuses);
                _frameTelemetry.SetCounter(
                    MapRenderFrameCounter.OpenGlCalls,
                    checked(_state.SubmittedCalls + _frameDrawCalls));
                _frameTelemetry.SetCounter(
                    MapRenderFrameCounter.StateShadowElidedCalls,
                    _state.ElidedCalls);
                _frameTelemetry.SetCounter(
                    MapRenderFrameCounter.ProgramChanges,
                    _state.ProgramChanges);
                _frameTelemetry.SetCounter(
                    MapRenderFrameCounter.VertexArrayChanges,
                    _state.VertexArrayChanges);
                _frameTelemetry.SetCounter(
                    MapRenderFrameCounter.FramebufferChanges,
                    _state.FramebufferChanges);
                _frameTelemetry.SetCounter(
                    MapRenderFrameCounter.BufferChanges,
                    _state.BufferChanges);
                _frameTelemetry.SetCounter(
                    MapRenderFrameCounter.TextureChanges,
                    _state.TextureChanges);
                _frameTelemetry.SetCounter(
                    MapRenderFrameCounter.SamplerChanges,
                    _state.SamplerChanges);
                _frameTelemetry.SetCounter(
                    MapRenderFrameCounter.RenderStateChanges,
                    _state.RenderStateChanges);
                _frameTelemetry.SetCounter(
                    MapRenderFrameCounter.UniformUpdates,
                    _state.UniformUpdates);
                _frameTelemetry.SetCounter(
                    MapRenderFrameCounter.TextureResidentCount,
                    _textureHandles.ResidentCount);
                _frameTelemetry.SetCounter(
                    MapRenderFrameCounter.TextureResidentBytes,
                    _textureHandles.ResidentBytes);
                _frameTelemetry.SetCounter(
                    MapRenderFrameCounter.TextureResidencyUploadCount,
                    _frameTextureUploadCount);
                _frameTelemetry.SetCounter(
                    MapRenderFrameCounter.TextureResidencyUploadBytes,
                    _frameTextureUploadBytes);
                _frameTelemetry.SetCounter(
                    MapRenderFrameCounter.TextureResidencyEvictionCount,
                    _frameTextureEvictionCount);
                _frameTelemetry.SetCounter(
                    MapRenderFrameCounter.TextureResidencyEvictionBytes,
                    _frameTextureEvictionBytes);
                _frameTelemetry.SetCounter(
                    MapRenderFrameCounter.TextureResidencyDeferredCount,
                    _frameTextureDeferredCount);
                _frameTelemetry.SetCounter(
                    MapRenderFrameCounter.TextureAuthoredBcUploadBytes,
                    _frameAuthoredBcUploadBytes);
                _frameTelemetry.SetCounter(
                    MapRenderFrameCounter
                        .TextureDecodedFallbackRetainedBytes,
                    TextureDecodedFallbackBytesRetained);
                _frameTelemetry.SetCounter(
                    MapRenderFrameCounter.TextureAuthoredBcSourceBytes,
                    TextureAuthoredBcSourceBytes);
                _shaderCompilationCounter.RecordFrameDelta(
                    _frameTelemetry,
                    shaderProgramCompilationCountAtFrameStart);
            }
            catch
            {
                completed = false;
                throw;
            }
            finally
            {
                _activeRenderFrameIndex = -1;
                MapRenderCpuFrameTiming cpuTiming =
                    _frameTelemetry.EndCpuFrame();
                if (completed)
                {
                    _completedTelemetryFrameIndex =
                        cpuTiming.FrameIndex;
                }
            }
        }
    }

    public void RecordPresentedFrame()
    {
        if (_completedTelemetryFrameIndex is not long frameIndex)
            return;
        _frameTelemetry.RecordPresentedFrame(frameIndex);
        _completedTelemetryFrameIndex = null;
    }

    private void RenderMeasuredFrame(RenderCamera camera)
    {
        camera = BeginSunShadowDpvsPreparation(camera);
        using (_frameTelemetry.BeginCpuPhase(
                   MapRenderCpuPhase.StaticResourceAdmission))
        {
            EnsureProgressiveStaticResources(camera);
        }

        MapRenderWorldDpvsThreeViewFrame? sunShadowFrame;
        MapRenderSunShadowAtlasReadyState? sunShadowAtlasReady;
        bool sunShadowReceiverSelectionPrepared;
        using (_gpuTimers.BeginPhase(MapRenderGpuPhase.SunShadow))
        using (BeginGpuDrawPhase(MapRenderGpuPhase.SunShadow))
        using (_frameTelemetry.BeginCpuPhase(MapRenderCpuPhase.SunShadow))
        {
            sunShadowFrame = RenderSunShadowFrame(
                camera,
                out sunShadowAtlasReady,
                out sunShadowReceiverSelectionPrepared);
            if (sunShadowFrame is not null)
            {
                RenderSpotShadowFrame(
                    sunShadowFrame,
                    sunShadowAtlasReady,
                    sunShadowReceiverSelectionPrepared);
            }
        }
        if (sunShadowFrame is null)
        {
            ClearCurrentSpotShadowFrame();
            ResetWorldReceiverVariantSelection();
        }

        long worldCandidates = _worldSurfaceCandidateCount;
        long staticCandidates = _staticSchedulingByObjectIndex.Count != 0
            ? _staticSchedulingByObjectIndex.Count
            : _instancedTextured.Aggregate(
                0L,
                (count, mesh) => checked(count + mesh.InstanceCount));
        _frameTelemetry.SetCounter(
            MapRenderFrameCounter.WorldCandidates,
            worldCandidates);
        _frameTelemetry.SetCounter(
            MapRenderFrameCounter.WorldVisible,
            worldCandidates);
        _frameTelemetry.SetCounter(
            MapRenderFrameCounter.WorldVisibleRuns,
            worldCandidates);
        _frameTelemetry.SetCounter(
            MapRenderFrameCounter.WorldCandidateTriangles,
            _worldSurfaceCandidateIndexCount / 3);
        _frameTelemetry.SetCounter(
            MapRenderFrameCounter.WorldVisibleTriangles,
            _worldSurfaceCandidateIndexCount / 3);
        _frameTelemetry.SetCounter(
            MapRenderFrameCounter.StaticModelCandidates,
            staticCandidates);
        _frameTelemetry.SetCounter(
            MapRenderFrameCounter.StaticModelsVisible,
            staticCandidates);

        RenderPreviewSettings framePreviewSettings =
            CreateFramePreviewSettings(
                ResolveFrameAnimationTimeSeconds());
        float editorTimeSeconds;
        Vector3 previewClearColor = _editorPreviewAtmosphereEnabled
            ? _editorPreviewAtmosphere!.FogColor
            : new Vector3(0.42f, 0.49f, 0.52f);
        EditorPresentationFrame?
            editorPresentationFrame;
        _currentProcessedFloatZFrame = null;
        using (_gpuTimers.BeginPhase(MapRenderGpuPhase.SceneTarget))
        using (_frameTelemetry.BeginCpuPhase(MapRenderCpuPhase.SceneTarget))
        {
            LastFramePlan = null;
            editorPresentationFrame =
                _editorPreviewPresentationSession?.BeginFrame(
                    camera,
                    previewClearColor,
                    _editorPreviewFogRenderingEnabled
                        ? _editorPreviewActiveFog
                        : null,
                    framePreviewSettings);
            LastFramePlan = editorPresentationFrame?.FramePlan;
            editorTimeSeconds =
                editorPresentationFrame?.FramePlan.AnimationTimeSeconds ??
                framePreviewSettings.AnimationTimeSeconds;
        }

        ClipSpaceLookupCodeConstants clipSpaceLookup =
            editorPresentationFrame is { } activePresentationFrame
                ? FrameDirectCodeConstants.ProduceClipSpaceLookup(
                    activePresentationFrame.SceneTarget.Extent.LogicalWidth,
                    activePresentationFrame.SceneTarget.Extent.LogicalHeight,
                    activePresentationFrame.SceneTarget.ViewportX,
                    activePresentationFrame.SceneTarget.ViewportY,
                    activePresentationFrame.SceneTarget.ViewportWidth,
                    activePresentationFrame.SceneTarget.ViewportHeight)
                : FrameDirectCodeConstants.ProduceClipSpaceLookup(
                    _hostWidth,
                    _hostHeight,
                    viewportX: 0,
                    viewportY: 0,
                    viewportWidth: _hostWidth,
                    viewportHeight: _hostHeight);
        _frameClipSpaceLookupScaleCodeConstant = clipSpaceLookup.Scale;
        _frameClipSpaceLookupOffsetCodeConstant = clipSpaceLookup.Offset;
        _frameZNearCodeConstant =
            FrameDirectCodeConstants.ProduceZNearValue(camera.NearPlane);

        // Clear obeys the current color/depth write masks. The target-2
        // lifecycle performs the clear when a canonical world source is
        // available; non-world previews retain their direct host clear.
        DerivedMatrixState rsxMatrices;
        Matrix4x4 viewProjection;
        using (_gpuTimers.BeginPhase(MapRenderGpuPhase.FrameSetup))
        using (_frameTelemetry.BeginCpuPhase(MapRenderCpuPhase.FrameSetup))
        {
            ApplyDefaultRenderState();
            if (editorPresentationFrame is null)
            {
                _gl.ClearColor(
                    previewClearColor.X,
                    previewClearColor.Y,
                    previewClearColor.Z,
                    1f);
                _gl.Clear(
                    ClearBufferMask.ColorBufferBit |
                    ClearBufferMask.DepthBufferBit);
            }

            MapRenderPixelExtent cameraTargetExtent =
                MapRenderOpenGlNormalCameraTargetExtentPolicy.Resolve(
                    SurfaceExtents,
                    editorPresentationFrame is not null);
            float aspectRatio =
                (float)cameraTargetExtent.Width /
                cameraTargetExtent.Height;
            rsxMatrices =
                OpenGlDerivedMatrixPolicy.CreatePreviewFromCamera(
                    camera,
                    aspectRatio);
            if (_currentSunShadowReceiverFrame is { } receiverFrame)
            {
                rsxMatrices =
                    DerivedMatrixResolver.WithShadowLookupSource(
                        rsxMatrices,
                        receiverFrame.Projection.ShadowLookupMatrix);
            }
            viewProjection =
                OpenGlRsxClipSpaceLowering
                    .CreateDirectEditorPreviewHostViewProjection(rsxMatrices);
            _frameVertexConstants.Upload(
                _activeRenderFrameIndex,
                in rsxMatrices,
                editorTimeSeconds,
                _frameClipSpaceLookupScaleCodeConstant,
                _frameClipSpaceLookupOffsetCodeConstant,
                _frameZNearCodeConstant);
            // A successful target-2 entry/clear is the exact scene color
            // render-pass execution point for this backend.
            _frameTelemetry.AddCounter(
                MapRenderFrameCounter.Passes);
        }
        using (_frameTelemetry.BeginCpuPhase(MapRenderCpuPhase.Visibility))
            UpdatePreviewVisibility(camera);

        // Establish the authored sky color before opaque and blended material
        // passes. Sky does not write depth, so every later geometry pass retains
        // its normal depth behavior while translucency blends against the sky.
        if (ShowSky)
        {
            using (_gpuTimers.BeginPhase(MapRenderGpuPhase.Sky))
            using (BeginGpuDrawPhase(MapRenderGpuPhase.Sky))
            using (_frameTelemetry.BeginCpuPhase(MapRenderCpuPhase.Sky))
            {
                if (editorPresentationFrame is not null)
                {
                    RenderPassPlan skyPass =
                        editorPresentationFrame.FramePlan.Passes.Single(
                            pass => pass.Identity ==
                                RenderFramePlanner.NormalCameraSkyPass);
                    if (!skyPass.Draws.IsEmpty)
                    {
                        MapRenderOpenGlNormalCameraSkyResourceCatalog catalog =
                            _skyResourceCatalog ??
                            throw new InvalidOperationException(
                                "The planned sky pass has no OpenGL scene resource catalog.");
                        _editorPreviewPresentationSession!.ExecuteSky(
                            editorPresentationFrame,
                            catalog,
                            this);
                    }
                }
                else
                {
                    foreach (GlSkyMesh sky in _skies)
                        DrawSky(sky, viewProjection);
                }
            }
        }

        _state.UseProgram(_solidProgram);
        _state.UniformMatrix4(_solidViewProjectionLocation, viewProjection);

        if (ShowDiagnosticGeometry)
        {
            using (_gpuTimers.BeginPhase(MapRenderGpuPhase.Diagnostics))
            using (BeginGpuDrawPhase(MapRenderGpuPhase.Diagnostics))
            {
                if (editorPresentationFrame is not null)
                {
                    _editorPreviewPresentationSession!.ExecuteDiagnostics(
                        editorPresentationFrame,
                        _diagnosticResourceCatalog ??
                            throw new InvalidOperationException(
                                "The planned diagnostics pass has no OpenGL scene resource catalog."),
                        viewProjection,
                        this);
                }
                else
                {
                    _state.Uniform1(_solidUseInstancingLocation, 0);
                    Draw(_fallbackSolid, PrimitiveType.Triangles);
                    Draw(_solid, PrimitiveType.Triangles);
                    _state.Uniform1(_solidUseInstancingLocation, 1);
                    foreach (GlInstancedMesh mesh in _instancedSolid)
                        Draw(mesh, PrimitiveType.Triangles);
                }
            }
        }

        if (ShowTexturedGeometry)
        {
            IReadOnlyList<MapRenderEditorDrawGroup<GlTexturedDrawCommand>>
                drawGroups;
            IReadOnlyList<MapRenderEditorDrawGroup<GlTexturedDrawCommand>>
                depthGroups;
            using (_frameTelemetry.BeginCpuPhase(MapRenderCpuPhase.QueueBuild))
            {
                bool useReceiverAwareGroups =
                    _currentWorldReceiverTechniqueSelector is not null;
                MapRenderEditorDrawGroup<GlTexturedDrawCommand>[]
                    frameGroups =
                        useReceiverAwareGroups
                            ? _receiverAwareEditorTexturedDrawGroups
                            : _editorTexturedDrawGroups;
                depthGroups = useReceiverAwareGroups
                    ? _receiverAwareEditorDepthPrepassDrawGroups
                    : _editorDepthPrepassDrawGroups;
                drawGroups = MapRenderEditorDrawQueueSorter
                    .SortImmutableFrame(
                        frameGroups,
                        camera.Position,
                        camera.Forward);
                if (!CanReusePreparedTexturedDrawQueue(
                        frameGroups,
                        drawGroups))
                {
                    InvalidatePreparedTexturedDrawQueue();
                    PrepareTexturedDrawGroupVisibility(drawGroups);
                    PrepareStaticReceiverDrawCompaction(drawGroups);
                    CommitPreparedTexturedDrawQueue(
                        frameGroups,
                        drawGroups);
                }
            }
            PrepareTextureResidencyForVisibleDraws(
                drawGroups,
                depthGroups);
            PrepareTexturedDrawGroupColorExecution(drawGroups);
            bool requiresVisibleProcessedFloatZ =
                RequiresVisibleProcessedFloatZ(drawGroups);
            bool hasVisibleProcessedFloatZ =
                RequiresAnyVisibleProcessedFloatZ(drawGroups);
            if (!CanElideAppleSiliconDepthPrepass(
                    drawGroups,
                    depthGroups,
                    hasVisibleProcessedFloatZ))
            {
                DrawVisibleDepthPrepassGroups(
                    depthGroups,
                    editorPresentationFrame?.SceneTarget.StencilTargetContract,
                    viewProjection,
                    rsxMatrices,
                    editorTimeSeconds);
            }
            if (requiresVisibleProcessedFloatZ)
            {
                TryBuildProcessedFloatZ(
                    editorPresentationFrame,
                    camera.NearPlane);
            }
            DrawVisibleTexturedGroups(
                drawGroups,
                editorPresentationFrame?.SceneTarget.StencilTargetContract,
                viewProjection,
                rsxMatrices,
                camera.Position,
                editorTimeSeconds);
        }
        else
        {
            // Shadow cutout admission runs independently of the color toggle.
            // Keep residency aging and budget enforcement alive so those
            // critical uploads do not accumulate while color geometry is off.
            PrepareTextureResidencyForVisibleDraws([], []);
        }

        if (ShowWireframe)
        {
            using (_gpuTimers.BeginPhase(MapRenderGpuPhase.Wireframe))
            using (BeginGpuDrawPhase(MapRenderGpuPhase.Wireframe))
            {
                if (editorPresentationFrame is not null)
                {
                    RenderPassPlan? wireframePass =
                        editorPresentationFrame.FramePlan.Passes
                            .SingleOrDefault(pass => pass.Identity ==
                                RenderFramePlanner
                                    .NormalCameraWireframePass);
                    if (wireframePass is not null)
                    {
                        _editorPreviewPresentationSession!
                            .ExecuteWireframe(
                                editorPresentationFrame,
                                _wireframeResourceCatalog ??
                                    throw new InvalidOperationException(
                                        "The planned wireframe pass has no OpenGL scene resource catalog."),
                                viewProjection,
                                this);
                    }
                }
                else
                {
                    // Legacy non-frame-plan oracle. Its historical uniform
                    // inheritance is intentionally characterized separately.
                    _state.SetEnabled(EnableCap.DepthTest, false);
                    _state.LineWidth(1.25f);
                    Draw(_wire, PrimitiveType.Lines);
                    _state.SetEnabled(EnableCap.DepthTest, true);
                }
            }
        }

        if (_editorSelectionOutline is not null)
        {
            using (_gpuTimers.BeginPhase(
                       MapRenderGpuPhase.EditorOverlay))
            using (BeginGpuDrawPhase(
                       MapRenderGpuPhase.EditorOverlay))
            using (_frameTelemetry.BeginCpuPhase(
                       MapRenderCpuPhase.EditorOverlay))
            {
                DrawEditorSelectionOutline(
                    editorPresentationFrame,
                    viewProjection);
            }
        }

        using (_gpuTimers.BeginPhase(MapRenderGpuPhase.Presentation))
        using (BeginGpuDrawPhase(MapRenderGpuPhase.Presentation))
        using (_frameTelemetry.BeginCpuPhase(MapRenderCpuPhase.Presentation))
        {
            LastEditorPreviewPresentationResult = null;
            try
            {
                LastEditorPreviewPresentationResult =
                    editorPresentationFrame is null
                        ? null
                        : _editorPreviewPresentationSession!.Present(
                            editorPresentationFrame,
                            _hostFramebuffer);
            }
            catch
            {
                // Presentation owns direct GL calls. A partial failure has no
                // exact handoff state, so force the next frame to reestablish
                // every cached binding/state rather than trusting stale data.
                _state.InvalidateAll();
                throw;
            }
            if (LastEditorPreviewPresentationResult is not null)
            {
                int completedPostPasses =
                    LastEditorPreviewPresentationResult.FullscreenDrawCount;
                _frameTelemetry.AddCounter(
                    MapRenderFrameCounter.Passes,
                    completedPostPasses);
                _frameTelemetry.AddCounter(
                    MapRenderFrameCounter.PostPasses,
                    completedPostPasses);
                for (int drawIndex = 0;
                     drawIndex < completedPostPasses;
                     drawIndex++)
                {
                    RecordDraw(6, instanceCount: 1, PrimitiveType.Triangles);
                }
                _state.AdoptDefaultPresenterHandoff(
                    _hostWidth,
                    _hostHeight,
                    _hostFramebuffer);
            }
        }
    }

    private float ResolveFrameAnimationTimeSeconds() =>
        ResolvePreviewAnimationTimeSeconds(
            _previewAnimationTimeSecondsOverride,
            System.Diagnostics.Stopwatch.GetElapsedTime(
                _editorAnimationStartTimestamp).TotalSeconds);

    internal static float ResolvePreviewAnimationTimeSeconds(
        float? animationTimeSecondsOverride,
        double elapsedTimeSeconds)
    {
        if (animationTimeSecondsOverride is { } fixedTime)
        {
            if (!float.IsFinite(fixedTime) || fixedTime < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(animationTimeSecondsOverride));
            }

            return fixedTime == 0f ? 0f : fixedTime;
        }

        if (!double.IsFinite(elapsedTimeSeconds) || elapsedTimeSeconds < 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(elapsedTimeSeconds));
        }

        float effectiveTime = (float)elapsedTimeSeconds;
        if (!float.IsFinite(effectiveTime))
        {
            throw new ArgumentOutOfRangeException(
                nameof(elapsedTimeSeconds),
                "Elapsed preview animation time exceeds the frame contract range.");
        }

        return effectiveTime == 0f ? 0f : effectiveTime;
    }

    private RenderPreviewSettings CreateFramePreviewSettings(
        float animationTimeSeconds) => new(
        showSky: ShowSky,
        showDiagnosticGeometry: ShowDiagnosticGeometry,
        showTexturedGeometry: ShowTexturedGeometry,
        showWireframe: ShowWireframe,
        isolatedWorldSurfaceIndex: _loadedIsolatedWorldSurfaceIndex,
        useRsxVertexPlacementDiagnostic:
            UseRsxVertexPlacementDiagnostic,
        rsxFragmentOutputDiagnostic: RsxFragmentOutputDiagnostic,
        animationTimeSeconds: animationTimeSeconds);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_contextAbandoned)
            return;

        try
        {
            DeleteLoadedResources();
        }
        finally
        {
            try
            {
                _frameVertexConstants.Dispose();
                _gpuTimers.Dispose();
            }
            finally
            {
                try
                {
                    _sharedProgramUsage.Dispose();
                }
                finally
                {
                    if (_ownsSharedProgramCache)
                        _sharedProgramCache.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// Stops renderer-owned CPU workers and releases managed ownership after
    /// an unrecoverable context loss. It never invokes OpenGL; the driver
    /// reclaims context objects.
    /// </summary>
    public void AbandonContext()
    {
        if (_disposed || _contextAbandoned)
            return;

        _contextAbandoned = true;
        _loaded = false;
        ResetEditorSelectionOutline();
        List<Exception>? failures = null;
        TryRelease(
            () => _sunShadowDpvsWorker?.Dispose(),
            ref failures);
        _sunShadowDpvsWorker = null;
        MapRenderOpenGlLatestWorkQueue<DpvsWorkKey, DpvsWorkResult>?
            previewDpvsWorker = _previewDpvsWorker;
        _previewDpvsWorker = null;
        TryRelease(
            () => previewDpvsWorker?.Dispose(),
            ref failures);
        _activeSunShadowDpvsPacket = null;
        _retainedSunShadowDpvsPacket = null;
        _currentSunShadowVisibility = null;
        InvalidateSunShadowAtlasContentCache();
        ClearCurrentSpotShadowFrame();
        InvalidateSpotShadowAtlasContentCache();
        LastEditorPreviewPresentationResult = null;
        LastFramePlan = null;
        _editorPreviewPresentationSession = null;
        _sunShadowAtlas = null;
        _spotShadowAtlas = null;
        _genericInactiveTexture = 0;
        _frameVertexConstants.AbandonContext();
        TryRelease(_gpuTimers.AbandonContext, ref failures);
        TryRelease(_sharedProgramUsage.Dispose, ref failures);
        if (_ownsSharedProgramCache)
        {
            TryRelease(
                _sharedProgramCache.AbandonContext,
                ref failures);
        }
        if (failures is not null)
        {
            throw new AggregateException(
                "One or more managed renderer owners could not be released after OpenGL context loss.",
                failures);
        }
    }

    private void ThrowIfUnavailable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_contextAbandoned)
        {
            throw new InvalidOperationException(
                "The renderer cannot be reused after its OpenGL context was abandoned.");
        }
    }

    private static void TryRelease(
        Action release,
        ref List<Exception>? failures)
    {
        try
        {
            release();
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }
    }

}
