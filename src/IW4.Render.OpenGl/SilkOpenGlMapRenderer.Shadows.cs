using IW4.Render.Techniques;
using System.Numerics;
using IW4.Assets.Assets.ComWorld;
using IW4.Render.Diagnostics;
using IW4.Render.Geometry;
using IW4.Render.Geometry.Shadows;
using IW4.Render.Lighting;
using IW4.Render.Materials;
using IW4.Render.OpenGl.Diagnostics;
using IW4.Render.OpenGl.Shadows;
using IW4.Render.Scheduling;
using IW4.Render.Scheduling.Clear;
using IW4.Render.Scheduling.Dpvs;
using IW4.Render.Scheduling.Shadows;
using IW4.Render.Scheduling.StaticModels;
using IW4.Render.SceneBuilding;
using Silk.NET.OpenGL;

namespace IW4.Render.OpenGl;

public sealed unsafe partial class SilkOpenGlMapRenderer
{
    private const float Ps3SunShadowPolygonOffsetFactor = 2f;
    private const float Ps3SunShadowPolygonOffsetUnits = 25f;
    private const float Ps3SpotShadowPolygonOffsetFactor = 5f;
    private const float Ps3SpotShadowPolygonOffsetUnits = 700f;
    private MapRenderWorldDpvsVisibilityBuildResult?
        _sunShadowCasterAdmissionVisibility;
    private MapRenderSunShadowCasterPartition?
        _sunShadowCasterAdmissionPartition0;
    private MapRenderSunShadowCasterPartition?
        _sunShadowCasterAdmissionPartition1;
    private MapRenderSunShadowCasterCatalogProvider?
        _sunShadowCasterCatalogProvider;
    private bool _currentSunShadowCasterAdmissionReused;
    private readonly HashSet<int> _sunShadowWorldAdmissionScratch = [];
    private readonly HashSet<int> _sunShadowCoverageWorldScratch = [];
    private readonly HashSet<int> _sunShadowCoverageStaticScratch = [];
    private readonly HashSet<uint>
        _sunShadowAtlasContentTextureHandles = [];
    private readonly int[] _sunShadowUnsupportedWorldSurfaceScratch =
        new int[4];

    public long SunShadowCasterAdmissionCacheHitCount { get; private set; }

    public long SunShadowCasterAdmissionBuildCount { get; private set; }

    public long SunShadowStaticInstanceUploadCount { get; private set; }

    public long SunShadowStaticInstanceUploadBytes { get; private set; }

    private void InitializeSunShadowCasterResources(
        MapRenderScene scene,
        bool isolateWorldSurface)
    {
        _sunShadowWorldCasterRuntimes = [];
        _sunShadowWorldCastersBySurface =
            new Dictionary<int,
                MapRenderOpenGlSunShadowWorldCasterSurfaceRuntime>();
        _sunShadowExecutableWorldSurfaceIndices = new HashSet<int>();
        _sunShadowWorldCasterRejectionsBySurface =
            scene.SunShadowWorldCasterRejections.ToDictionary(
                rejection => rejection.SurfaceIndex);
        _sunShadowStaticCasterRuntimes = [];
        _sunShadowStaticCasterExpectations = isolateWorldSurface
            ? []
            : scene.SunShadowStaticCasterExpectations.ToArray();
        _sunShadowStaticCasterIndex = new(
            _sunShadowStaticCasterRuntimes,
            _sunShadowStaticCasterExpectations);
        _sunShadowWorldAdmissionScratch.Clear();
        _sunShadowCoverageWorldScratch.Clear();
        _sunShadowCoverageStaticScratch.Clear();
        if (scene.WorldSource is { } worldSource)
        {
            _sunShadowWorldAdmissionScratch.EnsureCapacity(
                Math.Max(worldSource.World.SurfaceCount, 0));
            _sunShadowCoverageWorldScratch.EnsureCapacity(
                Math.Max(worldSource.World.SurfaceCount, 0));
        }
        _sunShadowCoverageStaticScratch.EnsureCapacity(
            scene.SunShadowStaticCasterExpectations.Count);
        if (isolateWorldSurface ||
            (scene.SunShadowWorldCasterBatches.Count == 0 &&
             scene.SunShadowStaticCasterBatches.Count == 0))
        {
            return;
        }

        _sunShadowOpaqueCasterProgram = CreateProgram(
            SunShadowCasterVertexShaderSource,
            SunShadowOpaqueCasterFragmentShaderSource);
        _sunShadowCutoutCasterProgram = CreateProgram(
            SunShadowCasterVertexShaderSource,
            SunShadowCutoutCasterFragmentShaderSource);
        _sunShadowOpaqueViewProjectionLocation = _gl.GetUniformLocation(
            _sunShadowOpaqueCasterProgram,
            "uViewProjection");
        _sunShadowOpaqueUseInstancingLocation = _gl.GetUniformLocation(
            _sunShadowOpaqueCasterProgram,
            "uUseInstancing");
        _sunShadowCutoutViewProjectionLocation = _gl.GetUniformLocation(
            _sunShadowCutoutCasterProgram,
            "uViewProjection");
        _sunShadowCutoutUseInstancingLocation = _gl.GetUniformLocation(
            _sunShadowCutoutCasterProgram,
            "uUseInstancing");
        _sunShadowCutoutTextureLocation = _gl.GetUniformLocation(
            _sunShadowCutoutCasterProgram,
            "uColorTexture");
        _gl.UseProgram(_sunShadowCutoutCasterProgram);
        _gl.Uniform1(_sunShadowCutoutTextureLocation, 0);
        _gl.UseProgram(0);

        IReadOnlyList<MapRenderSunShadowWorldCasterPackedBatch> packedWorld =
            MapRenderSunShadowWorldCasterPacker.Pack(
                scene.SunShadowWorldCasterBatches);
        var world = new List<
            MapRenderOpenGlSunShadowWorldCasterRuntime>(packedWorld.Count);
        var statics = new List<
            MapRenderOpenGlSunShadowStaticCasterRuntime>(
                scene.SunShadowStaticCasterBatches.Count);
        try
        {
            foreach (MapRenderSunShadowWorldCasterPackedBatch batch in
                     packedWorld)
            {
                world.Add(new(
                    batch,
                    CreateSunShadowCasterMesh(
                        batch.Geometry,
                        batch.CutoutTexture,
                        staticInstanceCapacity: 0)));
            }
            foreach (MapRenderStaticSunShadowCasterBatch batch in
                     scene.SunShadowStaticCasterBatches)
            {
                statics.Add(CreateSunShadowStaticCasterRuntime(batch));
            }

            _sunShadowWorldCasterRuntimes = world.ToArray();
            _sunShadowWorldCastersBySurface =
                _sunShadowWorldCasterRuntimes
                    .SelectMany(runtime => runtime.Batch.Spans.Select(
                        span => new KeyValuePair<int,
                            MapRenderOpenGlSunShadowWorldCasterSurfaceRuntime>(
                                span.SurfaceIndex,
                                new(runtime, span))))
                    .ToDictionary(entry => entry.Key, entry => entry.Value);
            _sunShadowExecutableWorldSurfaceIndices =
                _sunShadowWorldCastersBySurface.Keys.ToHashSet();
            _sunShadowStaticCasterRuntimes = statics.ToArray();
            _sunShadowStaticCasterIndex = new(
                _sunShadowStaticCasterRuntimes,
                _sunShadowStaticCasterExpectations);
        }
        catch
        {
            foreach (MapRenderOpenGlSunShadowWorldCasterRuntime runtime in
                     world)
            {
                DeleteSunShadowCasterMesh(runtime.Mesh);
            }
            foreach (MapRenderOpenGlSunShadowStaticCasterRuntime runtime in
                     statics)
            {
                DeleteSunShadowStaticCasterRuntime(runtime);
            }
            throw;
        }
    }

    private MapRenderOpenGlSunShadowStaticCasterRuntime
        CreateSunShadowStaticCasterRuntime(
            MapRenderStaticSunShadowCasterBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        MapRenderOpenGlSunShadowCasterMesh partition0 = default;
        MapRenderOpenGlSunShadowCasterMesh partition1 = default;
        try
        {
            // Both native partitions retain an independent, fixed slice in
            // one instance buffer. Their VAOs differ only in the base byte
            // offset of attributes 3..5, so unchanged selections need no
            // buffer upload and no per-frame attribute rebinding.
            partition0 = CreateSunShadowCasterMesh(
                batch.Geometry,
                batch.CutoutTexture,
                checked(batch.Instances.Count * 2));
            partition1 = CreateSunShadowStaticPartitionMesh(
                partition0,
                batch.Geometry,
                batch.Instances.Count);
            return new(batch, partition0, partition1);
        }
        catch
        {
            if (partition1.VertexArray != 0 &&
                partition1.VertexArray != partition0.VertexArray)
            {
                _gl.DeleteVertexArray(partition1.VertexArray);
            }
            if (partition0.VertexArray != 0)
                DeleteSunShadowCasterMesh(partition0);
            throw;
        }
    }

    private MapRenderOpenGlSunShadowCasterMesh
        CreateSunShadowCasterMesh(
            MapRenderSunShadowCasterGeometry geometry,
            IW4.Render.Textures.Texture? cutoutTexture,
        int staticInstanceCapacity)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        if (staticInstanceCapacity < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(staticInstanceCapacity));
        }
        if (geometry.HasCutoutUv && !CanUploadTexture(cutoutTexture))
            return default;

        float[] vertices = geometry.Vertices.ToArray();
        uint[] indices = geometry.Indices.ToArray();
        uint vao = 0;
        uint vbo = 0;
        uint ebo = 0;
        uint instanceBuffer = 0;
        try
        {
            vao = _gl.GenVertexArray();
            vbo = _gl.GenBuffer();
            ebo = _gl.GenBuffer();
            _gl.BindVertexArray(vao);
            UploadBuffer(vbo, vertices);
            UploadElementBuffer(ebo, indices);

            uint vertexStride = checked(
                (uint)(geometry.VertexFloatCount * sizeof(float)));
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(
                0,
                3,
                VertexAttribPointerType.Float,
                false,
                vertexStride,
                (void*)0);
            if (geometry.HasCutoutUv)
            {
                _gl.EnableVertexAttribArray(1);
                _gl.VertexAttribPointer(
                    1,
                    4,
                    VertexAttribPointerType.Float,
                    false,
                    vertexStride,
                    (void*)(MapRenderSunShadowCasterGeometry
                        .CutoutColorOffset * sizeof(float)));
                _gl.EnableVertexAttribArray(2);
                _gl.VertexAttribPointer(
                    2,
                    2,
                    VertexAttribPointerType.Float,
                    false,
                    vertexStride,
                    (void*)(MapRenderSunShadowCasterGeometry
                        .CutoutUvOffset * sizeof(float)));
            }

            if (staticInstanceCapacity > 0)
            {
                instanceBuffer = _gl.GenBuffer();
                _gl.BindBuffer(
                    BufferTargetARB.ArrayBuffer,
                    instanceBuffer);
                _gl.BufferData(
                    BufferTargetARB.ArrayBuffer,
                    checked((nuint)(
                        staticInstanceCapacity * 12 * sizeof(float))),
                    null,
                    BufferUsageARB.DynamicDraw);
                const uint instanceStride = 12 * sizeof(float);
                for (uint row = 0; row < 3; row++)
                {
                    uint attribute = 3 + row;
                    _gl.EnableVertexAttribArray(attribute);
                    _gl.VertexAttribPointer(
                        attribute,
                        4,
                        VertexAttribPointerType.Float,
                        false,
                        instanceStride,
                        (void*)(row * 4 * sizeof(float)));
                    _gl.VertexAttribDivisor(attribute, 1);
                }
            }
            _gl.BindVertexArray(0);

            uint texture = cutoutTexture is null
                ? 0
                : CreateTexture(cutoutTexture);
            if (geometry.HasCutoutUv != (texture != 0))
            {
                throw new InvalidOperationException(
                    "Exact cutout caster geometry and authored texture ownership diverged during GL materialization.");
            }
            return new MapRenderOpenGlSunShadowCasterMesh(
                vao,
                vbo,
                ebo,
                instanceBuffer,
                checked((uint)indices.Length),
                texture,
                geometry.HasCutoutUv,
                geometry.HasVertexColor);
        }
        catch
        {
            _gl.BindVertexArray(0);
            if (instanceBuffer != 0)
                _gl.DeleteBuffer(instanceBuffer);
            if (ebo != 0)
                _gl.DeleteBuffer(ebo);
            if (vbo != 0)
                _gl.DeleteBuffer(vbo);
            if (vao != 0)
                _gl.DeleteVertexArray(vao);
            throw;
        }
    }

    private void DeleteSunShadowCasterMesh(
        MapRenderOpenGlSunShadowCasterMesh mesh)
    {
        if (mesh.InstanceBuffer != 0)
            _gl.DeleteBuffer(mesh.InstanceBuffer);
        DeleteMesh(new GlMesh(
            mesh.VertexArray,
            mesh.VertexBuffer,
            mesh.ElementBuffer,
            mesh.IndexCount));
    }

    private MapRenderOpenGlSunShadowCasterMesh
        CreateSunShadowStaticPartitionMesh(
            MapRenderOpenGlSunShadowCasterMesh sharedMesh,
            MapRenderSunShadowCasterGeometry geometry,
            int baseInstance)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        if (sharedMesh.VertexArray == 0 ||
            sharedMesh.VertexBuffer == 0 ||
            sharedMesh.ElementBuffer == 0 ||
            sharedMesh.InstanceBuffer == 0)
        {
            throw new ArgumentException(
                "A second static-caster partition requires complete shared geometry and instance resources.",
                nameof(sharedMesh));
        }
        if (baseInstance < 0)
            throw new ArgumentOutOfRangeException(nameof(baseInstance));

        uint vao = 0;
        try
        {
            vao = _gl.GenVertexArray();
            _gl.BindVertexArray(vao);
            _gl.BindBuffer(
                BufferTargetARB.ArrayBuffer,
                sharedMesh.VertexBuffer);
            _gl.BindBuffer(
                BufferTargetARB.ElementArrayBuffer,
                sharedMesh.ElementBuffer);

            uint vertexStride = checked(
                (uint)(geometry.VertexFloatCount * sizeof(float)));
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(
                0,
                3,
                VertexAttribPointerType.Float,
                false,
                vertexStride,
                (void*)0);
            if (geometry.HasCutoutUv)
            {
                _gl.EnableVertexAttribArray(1);
                _gl.VertexAttribPointer(
                    1,
                    4,
                    VertexAttribPointerType.Float,
                    false,
                    vertexStride,
                    (void*)(MapRenderSunShadowCasterGeometry
                        .CutoutColorOffset * sizeof(float)));
                _gl.EnableVertexAttribArray(2);
                _gl.VertexAttribPointer(
                    2,
                    2,
                    VertexAttribPointerType.Float,
                    false,
                    vertexStride,
                    (void*)(MapRenderSunShadowCasterGeometry
                        .CutoutUvOffset * sizeof(float)));
            }

            _gl.BindBuffer(
                BufferTargetARB.ArrayBuffer,
                sharedMesh.InstanceBuffer);
            const uint instanceStride = 12 * sizeof(float);
            nuint baseByteOffset = checked((nuint)(
                baseInstance * 12 * sizeof(float)));
            for (uint row = 0; row < 3; row++)
            {
                uint attribute = 3 + row;
                _gl.EnableVertexAttribArray(attribute);
                _gl.VertexAttribPointer(
                    attribute,
                    4,
                    VertexAttribPointerType.Float,
                    false,
                    instanceStride,
                    (void*)checked(baseByteOffset +
                        (nuint)(row * 4 * sizeof(float))));
                _gl.VertexAttribDivisor(attribute, 1);
            }
            _gl.BindVertexArray(0);

            return sharedMesh with { VertexArray = vao };
        }
        catch
        {
            _gl.BindVertexArray(0);
            if (vao != 0)
                _gl.DeleteVertexArray(vao);
            throw;
        }
    }

    private void DeleteSunShadowStaticCasterRuntime(
        MapRenderOpenGlSunShadowStaticCasterRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        MapRenderOpenGlSunShadowCasterMesh partition0 =
            runtime.GetPartition(0).Mesh;
        MapRenderOpenGlSunShadowCasterMesh partition1 =
            runtime.GetPartition(1).Mesh;
        if (partition1.VertexArray != 0 &&
            partition1.VertexArray != partition0.VertexArray)
        {
            _gl.DeleteVertexArray(partition1.VertexArray);
        }
        DeleteSunShadowCasterMesh(partition0);
    }

    private void InitializeSunShadowPipeline(
        MapRenderScene scene,
        bool isolateWorldSurface)
    {
        ClearCurrentSpotShadowFrame();
        _spotShadowAtlas?.Dispose();
        _spotShadowAtlas = null;
        ResetSunShadowDpvsPipelineState();
        _sunShadowDpvsWorker?.Dispose();
        _sunShadowDpvsWorker = null;
        _currentSunShadowReceiverFrame = null;
        _currentSunShadowPublication = null;
        _currentSunShadowCasters = null;
        _currentSunShadowVisibility = null;
        InvalidateSunShadowAtlasContentCache();
        _sunShadowVisibilityProvider = null;
        _sunShadowCasterCatalogProvider = null;
        _selectedDirectionalSunPrimaryLightIndex = null;
        _nextSunShadowFrameRevision = 0;
        _sunShadowFrameSequence = new MapRenderSunShadowFrameSequence();
        _sunShadowCasterAdmissionVisibility = null;
        _sunShadowCasterAdmissionPartition0 = null;
        _sunShadowCasterAdmissionPartition1 = null;
        _currentSunShadowCasterAdmissionReused = false;
        SunShadowCasterAdmissionCacheHitCount = 0;
        SunShadowCasterAdmissionBuildCount = 0;
        SunShadowStaticInstanceUploadCount = 0;
        SunShadowStaticInstanceUploadBytes = 0;

        if (isolateWorldSurface)
        {
            SunShadowPipelineStatus =
                "SUN_SHADOW_PIPELINE_DISABLED_FOR_SURFACE_ISOLATION";
            return;
        }

        MapRenderWorldSceneSource? source = scene.WorldSource;
        MapRenderWorldSceneLightSource? lightSource =
            source?.SceneLights.Source;
        if (source is null ||
            lightSource is null)
        {
            SunShadowPipelineStatus =
                "SUN_SHADOW_PIPELINE_BLOCKED_CANONICAL_WORLD_MISSING";
            return;
        }

        // GfxWorld owns the active stage's exact directional-sun primary-light
        // index. ComWorld can legitimately retain several directional lights
        // for other stages, so the editor-lighting ambiguity policy is not an
        // authority for native DPVS/shadow scheduling.
        int sunIndex = source.World.SunPrimaryLightIndex;
        IReadOnlyList<ComPrimaryLight> primaryLights =
            lightSource.ComWorld.PrimaryLights;
        if ((uint)sunIndex >= (uint)primaryLights.Count || sunIndex == 0)
        {
            SunShadowPipelineStatus =
                "SUN_SHADOW_PIPELINE_BLOCKED_SELECTED_SUN_INDEX_INVALID";
            return;
        }

        ComPrimaryLight sun = primaryLights[sunIndex];
        if (sun.Type != MapRenderEditorPreviewLightingPlanner
                .DirectionalLightType)
        {
            SunShadowPipelineStatus =
                "SUN_SHADOW_PIPELINE_BLOCKED_SELECTED_LIGHT_NOT_DIRECTIONAL_SUN";
            return;
        }

        Vector3 nativeDirection = new(sun.Dir.X, sun.Dir.Y, sun.Dir.Z);
        float lengthSquared = nativeDirection.LengthSquared();
        if (!float.IsFinite(lengthSquared) || lengthSquared <= 1e-12f)
        {
            SunShadowPipelineStatus =
                "SUN_SHADOW_PIPELINE_BLOCKED_SELECTED_SUN_DIRECTION_INVALID";
            return;
        }
        nativeDirection /= MathF.Sqrt(lengthSquared);

        var setup = MapRenderWorldDpvsSunShadowFullSetupState
            .CreateViewerProfile(nativeDirection);
        var frameProvider =
            new MapRenderWorldDpvsSunShadowFullFrameProvider(
                "SILK_EDITOR_PREVIEW_PS3_FULL_SUN_SHADOW",
                source.AssetPoolRevisionAtConstruction,
                setup);
        _sunShadowVisibilityProvider =
            new MapRenderWorldDpvsNormalCameraVisibilityCache(
                new MapRenderWorldDpvsNormalCameraVisibilityProvider(
                    "SILK_EDITOR_PREVIEW_PS3_NORMAL_THREE_VIEW",
                    frameProvider));
        _sunShadowCasterCatalogProvider =
            new MapRenderSunShadowCasterCatalogProvider(source.World);
        _sunShadowDpvsWorker = new(
            source.World,
            _sunShadowVisibilityProvider,
            _sunShadowCasterCatalogProvider,
            _staticScheduling,
            _selectedStaticLodByObject);
        _selectedDirectionalSunPrimaryLightIndex = sunIndex;
        InitializeSpotShadowPipeline();

        try
        {
            _sunShadowAtlas = new MapRenderOpenGlSunShadowAtlasBackend(
                _gl,
                _state,
                _sunShadowAtlasContextIdentity);
        }
        catch (Exception exception) when (
            exception is NotSupportedException or
            AggregateException)
        {
            SunShadowPipelineStatus =
                $"SUN_SHADOW_PIPELINE_BLOCKED_OPENGL_ATLAS_UNAVAILABLE:{exception.Message}";
            return;
        }

        SunShadowPipelineStatus =
            "SUN_SHADOW_PIPELINE_READY_FOR_THREE_VIEW_FRAME";
    }

    private bool TryBuildSunShadowFrame(
        RenderCamera camera,
        out MapRenderSunShadowFramePublication? publication,
        out MapRenderSunShadowCasterCatalog? casters)
    {
        publication = null;
        casters = null;
        _currentSunShadowReceiverFrame = null;
        _currentSunShadowPublication = null;
        _currentSunShadowCasters = null;
        _currentSunShadowVisibility = null;

        if (_previewWorldSource is not { } source ||
            _sunShadowVisibilityProvider is not { } provider ||
            _sunShadowCasterCatalogProvider is not { } casterProvider ||
            _selectedDirectionalSunPrimaryLightIndex is null)
        {
            return false;
        }

        var extent = new MapRenderNormalCameraFramebufferExtent(
            _width,
            _height);
        var farPlane = new MapRenderNormalCameraFarPlaneState(
            rZFar: 0f,
            rendererFallback: camera.FarPlane);
        long revision = _nextSunShadowFrameRevision;
        if (!TryCompleteSunShadowDpvsPreparation(
                source,
                provider,
                camera,
                extent,
                farPlane,
                revision,
                out SunShadowFrameCpuPreparation preparation))
        {
            SunShadowPipelineStatus =
                "SUN_SHADOW_FRAME_DEFERRED_CPU_PACKET_NOT_READY";
            return false;
        }
        MapRenderWorldDpvsVisibilityBuildResult visibility =
            preparation.Visibility;
        if (!visibility.IsSuccess)
        {
            SunShadowPipelineStatus =
                "SUN_SHADOW_FRAME_BLOCKED_DPVS:" +
                string.Join(
                    '|',
                    visibility.Failures.Select(failure =>
                        $"{failure.Kind}:{failure.Detail}"));
            return false;
        }

        _nextSunShadowFrameRevision =
            checked(_nextSunShadowFrameRevision + 1);
        MapRenderSunShadowCasterCatalog candidateCasters;
        if (preparation.CasterPrepared)
        {
            MapRenderSunShadowCasterCatalogBuildResult casterResult =
                preparation.CasterResult ??
                throw new InvalidOperationException(
                    "The frame-preparation worker published no caster-admission result.");
            if (!casterResult.IsSuccess)
            {
                _currentSunShadowCasterAdmissionReused = false;
                SunShadowPipelineStatus =
                    $"SUN_SHADOW_FRAME_BLOCKED_CASTER_CATALOG:{casterResult.Failure!.Kind}:{casterResult.Failure.Detail}";
                return false;
            }

            candidateCasters = casterResult.Catalog!;
            _currentSunShadowCasterAdmissionReused =
                !preparation.WasScheduled;
            if (preparation.WasScheduled)
                SunShadowCasterAdmissionBuildCount++;
            else
                SunShadowCasterAdmissionCacheHitCount++;
        }
        else if (ReferenceEquals(
                _sunShadowCasterAdmissionVisibility,
                visibility) &&
            _sunShadowCasterAdmissionPartition0 is { } cachedPartition0 &&
            _sunShadowCasterAdmissionPartition1 is { } cachedPartition1)
        {
            // The DPVS cache returns the same result object only for an exact
            // producer/source/camera/extent/far-plane key hit. Therefore the
            // two admitted sets and the static CullDist/LOD camera input are
            // identical and can be reused without a heuristic equivalence.
            candidateCasters = new(
                revision,
                cachedPartition0,
                cachedPartition1);
            _currentSunShadowCasterAdmissionReused = true;
            SunShadowCasterAdmissionCacheHitCount++;
        }
        else
        {
            MapRenderSunShadowCasterCatalogBuildResult casterResult =
                casterProvider.BuildFastWorker(
                    revision,
                    visibility);
            if (!casterResult.IsSuccess)
            {
                _currentSunShadowCasterAdmissionReused = false;
                SunShadowPipelineStatus =
                    $"SUN_SHADOW_FRAME_BLOCKED_CASTER_CATALOG:{casterResult.Failure!.Kind}:{casterResult.Failure.Detail}";
                return false;
            }

            candidateCasters = casterResult.Catalog!;
            _currentSunShadowCasterAdmissionReused = false;
            SunShadowCasterAdmissionBuildCount++;
        }

        if (candidateCasters.Revision != revision)
        {
            throw new InvalidOperationException(
                $"Caster admission revision {candidateCasters.Revision} does not match frame revision {revision}.");
        }

        _sunShadowCasterAdmissionVisibility = visibility;
        _sunShadowCasterAdmissionPartition0 =
            candidateCasters.Partition0;
        _sunShadowCasterAdmissionPartition1 =
            candidateCasters.Partition1;

        if (!TryGetCameraVisibility(
                visibility,
                out MapRenderWorldDpvsViewVisibility?
                    cameraVisibility) ||
            cameraVisibility is null)
        {
            throw new InvalidOperationException(
                "Successful sun-shadow visibility omitted the normal-camera view.");
        }
        // The native-shaped lighting handle working set is an admission
        // stage, not a draw-stage side effect. Run it after exact static
        // selection and before receiver classification so an allocation miss
        // cannot make a hidden object fail shadow-receiver publication.
        PrepareStaticModelLightingAdmission(
            camera,
            cameraVisibility);

        MapRenderSunShadowFramePublication candidatePublication =
            _sunShadowFrameSequence.BeginFrame(revision, visibility);
        publication = candidatePublication;
        casters = candidateCasters;
        _currentSunShadowPublication = publication;
        _currentSunShadowCasters = casters;
        _currentSunShadowVisibility = visibility;
        SunShadowPipelineStatus =
            $"SUN_SHADOW_FRAME_{revision}_THREE_VIEW_READY";
        return true;
    }

    private MapRenderWorldDpvsThreeViewFrame? RenderSunShadowFrame(
        RenderCamera camera,
        out MapRenderSunShadowAtlasReadyState? atlasReady,
        out bool receiverSelectionPrepared)
    {
        atlasReady = null;
        receiverSelectionPrepared = false;
        _currentSunShadowReceiverFrame = null;
        if (!TryBuildSunShadowFrame(
                camera,
                out MapRenderSunShadowFramePublication? publication,
                out MapRenderSunShadowCasterCatalog? casters) ||
            publication is null ||
            casters is null)
        {
            return null;
        }

        MapRenderWorldDpvsThreeViewFrame frame = publication.Frame;
        if (_sunShadowAtlas is not { } atlas)
        {
            // Local spot shadows own a separate target and readiness token.
            // Keep the exact normal-camera/three-view frame operational when
            // only the directional atlas is unavailable.
            PrepareWorldReceiverVariantSelection(
                frame,
                sunAtlasReady: null);
            receiverSelectionPrepared = true;
            SunShadowPipelineStatus =
                $"SUN_SHADOW_FRAME_{frame.Revision}_THREE_VIEW_READY_SUN_ATLAS_UNAVAILABLE";
            return frame;
        }

        try
        {
            if (CanReuseSunShadowAtlasContents(atlas, casters))
            {
                atlas.BeginReusedFrame(frame.Revision);
                if (!publication.RecordPartitionDrawCompleted(
                        frame.Revision,
                        MapRenderWorldDpvsViewIndex.SunShadowPartition0) ||
                    !publication.RecordPartitionDrawCompleted(
                        frame.Revision,
                        MapRenderWorldDpvsViewIndex.SunShadowPartition1) ||
                    !publication.TryGetAtlasReady(out atlasReady) ||
                    atlasReady is null ||
                    !atlas.TryGetReadyFrame(
                        frame.Revision,
                        out MapRenderOpenGlSunShadowAtlasReadyFrame?
                            reusedBackendReady) ||
                    reusedBackendReady is null)
                {
                    throw new InvalidOperationException(
                        "A proven unchanged sun-shadow atlas could not be published for the current frame.");
                }

                _currentSunShadowReceiverFrame = new(
                    atlasReady,
                    reusedBackendReady);
                SunShadowPipelineStatus =
                    $"SUN_SHADOW_FRAME_{frame.Revision}_ATLAS_REUSED";
                return frame;
            }

            int nativeNullSlotRejectCount = EnsureCasterCoverage(casters);

            // Resolve and validate every allocated receiver before touching
            // either atlas tile. This preflight deliberately carries no
            // readiness token; successful selection is rebound to the real
            // same-revision token only after both writes complete. A failure
            // therefore avoids two shadow passes whose atlas would be
            // discarded immediately without manufacturing readiness.
            PrepareWorldReceiverVariantSelection(
                frame,
                sunAtlasReady: null,
                sunShadowPreflight: true);

            // A target write replaces the old depth contents even if a later
            // partition or publication fails. Do not permit the prior cache
            // key to survive that destructive transition.
            InvalidateSunShadowAtlasContentCache();
            atlas.BeginFrame(frame.Revision);
            for (int partitionIndex = 0;
                 partitionIndex < 2;
                 partitionIndex++)
            {
                MapRenderOpenGlSunShadowAtlasPartition atlasPartition =
                    partitionIndex == 0
                        ? MapRenderOpenGlSunShadowAtlasPartition.Partition0
                        : MapRenderOpenGlSunShadowAtlasPartition.Partition1;
                MapRenderWorldDpvsViewIndex viewIndex = partitionIndex == 0
                    ? MapRenderWorldDpvsViewIndex.SunShadowPartition0
                    : MapRenderWorldDpvsViewIndex.SunShadowPartition1;
                using MapRenderOpenGlSunShadowAtlasPartitionScope scope =
                    atlas.BeginPartition(atlasPartition);
                DrawSunShadowPartition(
                    frame,
                    casters.GetPartition(partitionIndex));
                scope.Complete();
                publication.RecordPartitionDrawCompleted(
                    frame.Revision,
                    viewIndex);
                // Record completion at the backend execution boundary before
                // later atomic publication/receiver validation can fail
                // closed.
                _frameTelemetry.AddCounter(
                    MapRenderFrameCounter.Passes);
                _frameTelemetry.AddCounter(
                    MapRenderFrameCounter.SunShadowPasses);
            }

            if (!publication.TryGetAtlasReady(out atlasReady) ||
                atlasReady is null ||
                !atlas.TryGetReadyFrame(
                    frame.Revision,
                    out MapRenderOpenGlSunShadowAtlasReadyFrame?
                        backendReady) ||
                backendReady is null)
            {
                throw new InvalidOperationException(
                    "Both sun-shadow partitions completed without an atomic same-revision atlas publication.");
            }

            AuthorizePreflightedWorldReceiverVariantSelection(
                frame,
                atlasReady);
            receiverSelectionPrepared = true;

            _currentSunShadowReceiverFrame = new(
                atlasReady,
                backendReady);
            RememberSunShadowAtlasContents(atlas, casters);
            SunShadowPipelineStatus =
                $"SUN_SHADOW_FRAME_{frame.Revision}_ATLAS_AND_RECEIVERS_READY_NATIVE_NULL_SLOT2_REJECTS_{nativeNullSlotRejectCount}";
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            ArgumentException or
            NotSupportedException)
        {
            atlasReady = null;
            _currentSunShadowReceiverFrame = null;
            SunShadowPipelineStatus =
                $"SUN_SHADOW_FRAME_{frame.Revision}_FAIL_CLOSED:{exception.GetType().Name}:{exception.Message}";
        }
        finally
        {
            // The native target callback resets the full scissor after the two
            // tiles. The following host scene-target entry owns framebuffer
            // and viewport replacement; these remaining states must not leak.
            _state.SetEnabled(EnableCap.ScissorTest, false);
            ApplyDefaultRenderState();
        }
        if (!receiverSelectionPrepared)
        {
            // Preserve the established fail-closed behavior: an incomplete
            // atlas cannot authorize +3 receivers, but the same successful
            // three-view frame may still select exact unshadowed variants.
            PrepareWorldReceiverVariantSelection(
                frame,
                sunAtlasReady: null);
            receiverSelectionPrepared = true;
        }
        return frame;
    }

    /// <summary>
    /// The normal-camera visibility cache returns the same result object only
    /// for an exact producer/source/camera/framebuffer/far-plane key hit.
    /// The cached caster partitions are then the exact admitted geometry and
    /// static-transform sequences used to write the existing atlas. Renderer
    /// resources are immutable after scene load; cutout texture residency is
    /// the one mutable draw dependency, so it is checked before every reuse.
    /// </summary>
    private bool CanReuseSunShadowAtlasContents(
        MapRenderOpenGlSunShadowAtlasBackend atlas,
        MapRenderSunShadowCasterCatalog casters)
    {
        if (!ReferenceEquals(atlas, _sunShadowAtlasContentBackend) ||
            !ReferenceEquals(
                _currentSunShadowVisibility,
                _sunShadowAtlasContentVisibility) ||
            !ReferenceEquals(
                casters.Partition0,
                _sunShadowAtlasContentPartition0) ||
            !ReferenceEquals(
                casters.Partition1,
                _sunShadowAtlasContentPartition1))
        {
            return false;
        }

        foreach (uint handle in _sunShadowAtlasContentTextureHandles)
        {
            if (!IsSunShadowCasterTextureResident(handle))
                return false;
        }
        return true;
    }

    private bool IsSunShadowCasterTextureResident(uint handle) =>
        handle == 0 ||
        (_textureHandles.TryGetEntry(
             handle,
             out MapRenderOpenGlTextureResidencyEntry entry) &&
         entry.IsResident);

    private void RememberSunShadowAtlasContents(
        MapRenderOpenGlSunShadowAtlasBackend atlas,
        MapRenderSunShadowCasterCatalog casters)
    {
        _sunShadowAtlasContentBackend = atlas;
        _sunShadowAtlasContentVisibility = _currentSunShadowVisibility;
        _sunShadowAtlasContentPartition0 = casters.Partition0;
        _sunShadowAtlasContentPartition1 = casters.Partition1;
        _sunShadowAtlasContentTextureHandles.Clear();
        foreach (int surfaceIndex in _sunShadowCoverageWorldScratch)
        {
            if (_sunShadowWorldCastersBySurface.TryGetValue(
                    surfaceIndex,
                    out MapRenderOpenGlSunShadowWorldCasterSurfaceRuntime
                        surfaceRuntime))
            {
                _sunShadowAtlasContentTextureHandles.Add(
                    surfaceRuntime.Runtime.Mesh.CutoutTexture);
            }
        }
        foreach (MapRenderOpenGlSunShadowStaticCasterRuntime runtime in
                 _sunShadowStaticCasterRuntimes)
        {
            if (runtime.GetPartition(0).InstanceCount != 0 ||
                runtime.GetPartition(1).InstanceCount != 0)
            {
                _sunShadowAtlasContentTextureHandles.Add(
                    runtime.GetPartition(0).Mesh.CutoutTexture);
            }
        }
    }

    private void InvalidateSunShadowAtlasContentCache()
    {
        _sunShadowAtlasContentBackend = null;
        _sunShadowAtlasContentVisibility = null;
        _sunShadowAtlasContentPartition0 = null;
        _sunShadowAtlasContentPartition1 = null;
        _sunShadowAtlasContentTextureHandles.Clear();
    }

    private int EnsureCasterCoverage(
        MapRenderSunShadowCasterCatalog casters)
    {
        _sunShadowCoverageWorldScratch.Clear();
        int nativeSelectorRejectedCount = 0;
        int unsupportedCount = 0;
        int retainedUnsupportedCount = 0;

        ClassifyWorldCasterCoverage(
            casters.Partition0.WorldSurfaceIndices,
            ref nativeSelectorRejectedCount,
            ref unsupportedCount,
            ref retainedUnsupportedCount);
        ClassifyWorldCasterCoverage(
            casters.Partition1.WorldSurfaceIndices,
            ref nativeSelectorRejectedCount,
            ref unsupportedCount,
            ref retainedUnsupportedCount);
        if (unsupportedCount != 0)
        {
            string detail = string.Join(
                " | ",
                _sunShadowUnsupportedWorldSurfaceScratch
                    .Take(retainedUnsupportedCount)
                    .Select(surfaceIndex =>
                    _sunShadowWorldCasterRejectionsBySurface.TryGetValue(
                        surfaceIndex,
                        out MapRenderSunShadowWorldCasterRejection?
                            rejection)
                        ? $"{surfaceIndex}:{rejection.Kind}:{rejection.Detail}"
                        : $"{surfaceIndex}:unclassified"));
            throw new InvalidOperationException(
                $"Exact slot-2 caster payload is unavailable for {unsupportedCount} admitted world surface(s); the allocated receiver column cannot be published. {detail}");
        }

        _sunShadowCoverageStaticScratch.Clear();
        foreach (MapRenderSunShadowStaticCasterIdentity identity in
                 casters.Partition0.StaticDrawInstances)
        {
            _sunShadowCoverageStaticScratch.Add(identity.ObjectIndex);
        }
        foreach (MapRenderSunShadowStaticCasterIdentity identity in
                 casters.Partition1.StaticDrawInstances)
        {
            _sunShadowCoverageStaticScratch.Add(identity.ObjectIndex);
        }
        Vector3 nativeCameraOrigin = casters.Partition0.PartitionIndex == 0
            ? _currentSunShadowPublication!.Frame.Projection.CameraOrigin
            : throw new InvalidOperationException(
                "Sun-shadow partition zero ownership was lost.");
        int missingStatic = (_sunShadowStaticCasterIndex ??
                throw new InvalidOperationException(
                    "The static caster object index is unavailable."))
            .CountMissingExecutableExpectations(
                _sunShadowCoverageStaticScratch,
                nativeCameraOrigin);
        if (missingStatic != 0)
        {
            throw new InvalidOperationException(
                $"Exact slot-2 caster payload is unavailable for {missingStatic} admitted static-model material surface(s); the allocated receiver column cannot be published.");
        }
        return nativeSelectorRejectedCount;
    }

    private void ClassifyWorldCasterCoverage(
        IReadOnlyList<int> surfaceIndices,
        ref int nativeSelectorRejectedCount,
        ref int unsupportedCount,
        ref int retainedUnsupportedCount)
    {
        foreach (int surfaceIndex in surfaceIndices)
        {
            if (!_sunShadowCoverageWorldScratch.Add(surfaceIndex) ||
                _sunShadowExecutableWorldSurfaceIndices.Contains(
                    surfaceIndex))
            {
                continue;
            }

            if (_sunShadowWorldCasterRejectionsBySurface.TryGetValue(
                    surfaceIndex,
                    out MapRenderSunShadowWorldCasterRejection?
                        rejection) &&
                rejection.IsNativeSelectorRejection)
            {
                nativeSelectorRejectedCount++;
                continue;
            }

            if (retainedUnsupportedCount <
                _sunShadowUnsupportedWorldSurfaceScratch.Length)
            {
                _sunShadowUnsupportedWorldSurfaceScratch[
                    retainedUnsupportedCount++] = surfaceIndex;
            }
            unsupportedCount++;
        }
    }

    private void DrawSunShadowPartition(
        MapRenderWorldDpvsThreeViewFrame frame,
        MapRenderSunShadowCasterPartition partition)
    {
        Matrix4x4 viewProjection =
            OpenGlRsxClipSpaceLowering
                .CreateShadowCasterHostViewProjection(
                    frame.Projection.WorldToClip(
                        partition.PartitionIndex));

        DrawShadowCasterSelection(
            partition.WorldSurfaceIndices,
            partition.StaticDrawInstances,
            frame.Projection.CameraOrigin,
            viewProjection,
            partition.PartitionIndex,
            reuseCommittedStaticSelection:
                _currentSunShadowCasterAdmissionReused,
            polygonOffsetFactor:
                Ps3SunShadowPolygonOffsetFactor,
            polygonOffsetUnits:
                Ps3SunShadowPolygonOffsetUnits);
    }

    private void DrawShadowCasterSelection(
        IReadOnlyList<int> worldSurfaceIndices,
        IReadOnlyList<MapRenderSunShadowStaticCasterIdentity>
            staticDrawInstances,
        Vector3 nativeCameraOrigin,
        Matrix4x4 viewProjection,
        int partitionRuntimeIndex,
        bool reuseCommittedStaticSelection,
        float polygonOffsetFactor,
        float polygonOffsetUnits)
    {
        ArgumentNullException.ThrowIfNull(worldSurfaceIndices);
        ArgumentNullException.ThrowIfNull(staticDrawInstances);
        if ((uint)partitionRuntimeIndex >= 2u)
        {
            throw new ArgumentOutOfRangeException(
                nameof(partitionRuntimeIndex));
        }

        _sunShadowWorldAdmissionScratch.Clear();
        foreach (int surfaceIndex in worldSurfaceIndices)
        {
            if (!_sunShadowWorldCastersBySurface.TryGetValue(
                    surfaceIndex,
                    out _))
            {
                if (_sunShadowWorldCasterRejectionsBySurface.TryGetValue(
                        surfaceIndex,
                        out MapRenderSunShadowWorldCasterRejection?
                            rejection) &&
                    rejection.IsNativeSelectorRejection)
                {
                    continue;
                }
                throw new InvalidOperationException(
                    $"Admitted world surface {surfaceIndex} lost its validated slot-2 caster payload.");
            }
            _sunShadowWorldAdmissionScratch.Add(surfaceIndex);
        }

        foreach (MapRenderOpenGlSunShadowWorldCasterRuntime runtime in
                 _sunShadowWorldCasterRuntimes)
        {
            int drawRunCount = MapRenderSunShadowWorldCasterPacker
                .CompactAdmittedDrawRuns(
                    runtime.Batch.Spans,
                    _sunShadowWorldAdmissionScratch,
                    runtime.CompactDrawRuns);
            if (drawRunCount == 0)
                continue;

            EnsureTextureResidentForCriticalDraw(
                runtime.Mesh.CutoutTexture);
            DrawSunShadowWorldCasterRuns(
                runtime.Mesh,
                runtime.Batch.Material.State,
                viewProjection,
                runtime.CompactDrawRuns,
                drawRunCount,
                polygonOffsetFactor,
                polygonOffsetUnits);
        }

        MapRenderOpenGlSunShadowStaticCasterIndex staticCasterIndex =
            _sunShadowStaticCasterIndex ??
            throw new InvalidOperationException(
                "The static caster object index is unavailable.");
        bool evaluateStaticSelection =
            !reuseCommittedStaticSelection;
        if (!evaluateStaticSelection)
        {
            foreach (MapRenderOpenGlSunShadowStaticCasterRuntime runtime in
                     _sunShadowStaticCasterRuntimes)
            {
                if (!runtime.GetPartition(partitionRuntimeIndex)
                        .HasCommittedSelection)
                {
                    evaluateStaticSelection = true;
                    break;
                }
            }
        }
        if (evaluateStaticSelection)
        {
            staticCasterIndex.PreparePartitionSelection(
                staticDrawInstances,
                nativeCameraOrigin);
        }
        foreach (MapRenderOpenGlSunShadowStaticCasterRuntime runtime in
                 _sunShadowStaticCasterRuntimes)
        {
            MapRenderOpenGlSunShadowStaticCasterPartitionRuntime
                partitionRuntime =
                    runtime.GetPartition(partitionRuntimeIndex);
            bool uploadChanged = false;
            int instanceCount = partitionRuntime.InstanceCount;
            if (evaluateStaticSelection)
            {
                uploadChanged = CompactSunShadowStaticInstances(
                    runtime,
                    partitionRuntime,
                    staticCasterIndex);
                instanceCount = partitionRuntime.InstanceCount;
            }
            if (instanceCount == 0)
                continue;

            EnsureTextureResidentForCriticalDraw(
                partitionRuntime.Mesh.CutoutTexture);
            if (uploadChanged)
            {
                _state.BindArrayBuffer(
                    partitionRuntime.Mesh.InstanceBuffer);
                nint destinationByteOffset = checked((nint)(
                    partitionRuntimeIndex *
                    runtime.Batch.Instances.Count *
                    12 * sizeof(float)));
                nuint uploadBytes = checked((nuint)(
                    instanceCount * 12 * sizeof(float)));
                fixed (float* transforms = runtime.CompactTransforms)
                {
                    _gl.BufferSubData(
                        BufferTargetARB.ArrayBuffer,
                        destinationByteOffset,
                        uploadBytes,
                        transforms);
                }
                SunShadowStaticInstanceUploadCount++;
                SunShadowStaticInstanceUploadBytes = checked(
                    SunShadowStaticInstanceUploadBytes +
                    (long)uploadBytes);
            }
            DrawSunShadowCaster(
                partitionRuntime.Mesh,
                runtime.Batch.Material.State,
                viewProjection,
                checked((uint)instanceCount),
                useInstancing: true,
                polygonOffsetFactor,
                polygonOffsetUnits);
        }
    }

    private bool CompactSunShadowStaticInstances(
        MapRenderOpenGlSunShadowStaticCasterRuntime runtime,
        MapRenderOpenGlSunShadowStaticCasterPartitionRuntime
            partitionRuntime,
        MapRenderOpenGlSunShadowStaticCasterIndex staticCasterIndex)
    {
        partitionRuntime.BeginSelection();
        for (int sourceIndex = 0;
             sourceIndex < runtime.Batch.Instances.Count;
             sourceIndex++)
        {
            MapRenderSunShadowStaticCasterInstance candidate =
                runtime.Batch.Instances[sourceIndex];
            if (!staticCasterIndex.IsSelected(
                    candidate.ObjectIndex,
                    runtime.Batch.LodIndex))
            {
                continue;
            }

            partitionRuntime.AddSelectedSourceIndex(sourceIndex);
        }

        bool changed = partitionRuntime.CommitSelection();
        if (!changed)
            return false;

        for (int compactIndex = 0;
             compactIndex < partitionRuntime.InstanceCount;
             compactIndex++)
        {
            MapRenderSunShadowStaticCasterInstance candidate =
                runtime.Batch.Instances[
                    partitionRuntime.GetSelectedSourceIndex(compactIndex)];
            int offset = compactIndex * 12;
            WriteVector4(
                runtime.CompactTransforms,
                offset,
                candidate.Instance.TransformRow0);
            WriteVector4(
                runtime.CompactTransforms,
                offset + 4,
                candidate.Instance.TransformRow1);
            WriteVector4(
                runtime.CompactTransforms,
                offset + 8,
                candidate.Instance.TransformRow2);
        }
        return true;
    }

    private void DrawSunShadowWorldCasterRuns(
        MapRenderOpenGlSunShadowCasterMesh mesh,
        RenderState authoredState,
        Matrix4x4 viewProjection,
        IReadOnlyList<MapRenderSunShadowWorldCasterDrawRun> drawRuns,
        int drawRunCount,
        float polygonOffsetFactor,
        float polygonOffsetUnits)
    {
        if (drawRunCount <= 0 || drawRunCount > drawRuns.Count)
            throw new ArgumentOutOfRangeException(nameof(drawRunCount));

        PrepareSunShadowCasterDraw(
            mesh,
            authoredState,
            viewProjection,
            useInstancing: false,
            polygonOffsetFactor,
            polygonOffsetUnits);
        _state.BindVertexArray(mesh.VertexArray);

        if (drawRunCount == 1)
        {
            MapRenderSunShadowWorldCasterDrawRun run = drawRuns[0];
            RecordDraw(
                run.IndexCount,
                instanceCount: 1,
                PrimitiveType.Triangles);
            _gl.DrawElements(
                PrimitiveType.Triangles,
                run.IndexCount,
                DrawElementsType.UnsignedInt,
                (void*)checked((nuint)(run.FirstIndex * sizeof(uint))));
            return;
        }

        EnsureMultiDrawCapacity(drawRunCount);
        long triangleCount = 0;
        for (int index = 0; index < drawRunCount; index++)
        {
            MapRenderSunShadowWorldCasterDrawRun run = drawRuns[index];
            _multiDrawIndexCounts[index] = run.IndexCount;
            _multiDrawIndexOffsets[index] = checked(
                (nint)(run.FirstIndex * sizeof(uint)));
            _multiDrawBaseVertices[index] = 0;
            triangleCount = checked(
                triangleCount + run.IndexCount / 3);
        }

        fixed (uint* indexCounts = _multiDrawIndexCounts)
        fixed (nint* indexOffsets = _multiDrawIndexOffsets)
        fixed (int* baseVertices = _multiDrawBaseVertices)
        {
            _gl.MultiDrawElementsBaseVertex(
                PrimitiveType.Triangles,
                indexCounts,
                DrawElementsType.UnsignedInt,
                (void**)indexOffsets,
                checked((uint)drawRunCount),
                baseVertices);
        }

        _frameDrawCalls++;
        _frameLogicalDrawCommands += drawRunCount;
        _frameMultiDrawApiCalls++;
        RecordPhaseLogicalDrawCommands(drawRunCount);
        _frameTelemetry.AddCounter(
            MapRenderFrameCounter.MultiDrawCommands,
            drawRunCount);
        _frameTelemetry.AddCounter(
            MapRenderFrameCounter.Triangles,
            triangleCount);
        if (_activeGpuDrawPhase is MapRenderGpuPhase gpuPhase)
        {
            _frameTelemetry.AddGpuPhaseWork(
                gpuPhase,
                drawCalls: 1,
                triangleCount);
        }
    }

    private void DrawSunShadowCaster(
        MapRenderOpenGlSunShadowCasterMesh mesh,
        RenderState authoredState,
        Matrix4x4 viewProjection,
        uint instanceCount,
        bool useInstancing,
        float polygonOffsetFactor,
        float polygonOffsetUnits)
    {
        if (mesh.IndexCount == 0 || instanceCount == 0)
            return;

        PrepareSunShadowCasterDraw(
            mesh,
            authoredState,
            viewProjection,
            useInstancing,
            polygonOffsetFactor,
            polygonOffsetUnits);
        _state.BindVertexArray(mesh.VertexArray);
        RecordDraw(
            mesh.IndexCount,
            instanceCount,
            PrimitiveType.Triangles);
        if (useInstancing)
        {
            _gl.DrawElementsInstanced(
                PrimitiveType.Triangles,
                mesh.IndexCount,
                DrawElementsType.UnsignedInt,
                null,
                instanceCount);
        }
        else
        {
            _gl.DrawElements(
                PrimitiveType.Triangles,
                mesh.IndexCount,
                DrawElementsType.UnsignedInt,
                null);
        }
    }

    private void PrepareSunShadowCasterDraw(
        MapRenderOpenGlSunShadowCasterMesh mesh,
        RenderState authoredState,
        Matrix4x4 viewProjection,
        bool useInstancing,
        float polygonOffsetFactor,
        float polygonOffsetUnits)
    {
        ApplyRenderState(authoredState);
        _state.ColorMask(false, false, false, false);
        _state.SetEnabled(EnableCap.Blend, false);
        _state.SetEnabled(EnableCap.StencilTest, false);
        _state.SetEnabled(EnableCap.PolygonOffsetFill, true);
        _state.PolygonOffset(
            polygonOffsetFactor,
            polygonOffsetUnits);

        uint program = mesh.IsCutout
            ? _sunShadowCutoutCasterProgram
            : _sunShadowOpaqueCasterProgram;
        int viewProjectionLocation = mesh.IsCutout
            ? _sunShadowCutoutViewProjectionLocation
            : _sunShadowOpaqueViewProjectionLocation;
        int useInstancingLocation = mesh.IsCutout
            ? _sunShadowCutoutUseInstancingLocation
            : _sunShadowOpaqueUseInstancingLocation;
        _state.UseProgram(program);
        _state.UniformMatrix4(viewProjectionLocation, viewProjection);
        _state.Uniform1(
            useInstancingLocation,
            useInstancing ? 1 : 0);
        if (mesh.IsCutout)
        {
            _state.ActiveTexture(0);
            _state.BindSampler(0, 0);
            _state.BindTexture(
                TextureTarget.Texture2D,
                mesh.CutoutTexture);
        }
    }

    internal const string SunShadowCasterVertexShaderSource = """
        #version 330 core
        layout (location = 0) in vec3 aPosition;
        layout (location = 1) in vec4 aVertexColor;
        layout (location = 2) in vec2 aTexCoord;
        layout (location = 3) in vec4 aInstanceRow0;
        layout (location = 4) in vec4 aInstanceRow1;
        layout (location = 5) in vec4 aInstanceRow2;

        uniform mat4 uViewProjection;
        uniform int uUseInstancing;

        out vec2 vTexCoord;
        out vec4 vVertexColor;

        void main()
        {
            vec4 localPosition = vec4(aPosition, 1.0);
            vec3 worldPosition = uUseInstancing == 0
                ? aPosition
                : vec3(
                    dot(aInstanceRow0, localPosition),
                    dot(aInstanceRow1, localPosition),
                    dot(aInstanceRow2, localPosition));
            vTexCoord = aTexCoord;
            vVertexColor = aVertexColor;
            gl_Position = uViewProjection * vec4(worldPosition, 1.0);
        }
        """;

    private const string SunShadowOpaqueCasterFragmentShaderSource = """
        #version 330 core
        void main()
        {
        }
        """;

    internal const string SunShadowCutoutCasterFragmentShaderSource = """
        #version 330 core
        in vec2 vTexCoord;
        in vec4 vVertexColor;
        uniform sampler2D uColorTexture;
        void main()
        {
            // Official vertcol_simple_atest multiplies texture by interpolated
            // route-01 color. The _nc payload supplies vec4(1), so the same
            // program preserves its texture-only result. Fixed alpha state is
            // exact GEQUAL with reference 0x80.
            float alpha = texture(uColorTexture, vTexCoord).a *
                vVertexColor.a;
            if (alpha < (128.0 / 255.0))
                discard;
        }
        """;
}
