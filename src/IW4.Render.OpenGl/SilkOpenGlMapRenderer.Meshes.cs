using System.Buffers;
using System.Numerics;
using Silk.NET.OpenGL;
using RenderTextureTarget = IW4.Render.Textures.TextureTarget;

using IW4.Assets.Assets.Material;
using IW4.Render.Execution;
using IW4.Render.Geometry;
using IW4.Render.EditorPreview;
using IW4.Render.Lighting;
using IW4.Render.Materials;
using IW4.Render.OpenGl.Programs;
using IW4.Render.OpenGl.StaticModels;
using IW4.Render.OpenGl.World;
using IW4.Render.Resources;
using IW4.Render.Scheduling.FramePlans;
using IW4.Render.Scheduling.StaticModels;
using IW4.Render.Shaders;
using IW4.Render.Textures;
using IW4.Render.Techniques;

namespace IW4.Render.OpenGl;

public sealed unsafe partial class SilkOpenGlMapRenderer
{
    private readonly Dictionary<int, List<StaticColorSortRepresentative>>
        _staticColorSortRepresentativesByHash = [];
    private int _nextStaticColorSortGroupId;

    public long WorldGeometryArenaUploadCount { get; private set; }

    public long WorldGeometrySourceBatchCount { get; private set; }

    public long WorldGeometryImmutableBufferUploadCount
    {
        get;
        private set;
    }

    public long WorldGeometryImmutableBufferUploadBytes
    {
        get;
        private set;
    }

    public long WorldGeometryTranslatedArenaCount { get; private set; }

    public int WorldGeometryMaximumTranslatedArenaAttributeCount
    {
        get;
        private set;
    }

    public long StaticGeometrySourceMeshCount =>
        _staticGeometryUploads.SourceGeometryCount;

    public long StaticGeometryUniqueUploadCount =>
        _staticGeometryUploads.UniqueGeometryCount;

    public long StaticGeometryReusedMeshCount =>
        _staticGeometryUploads.ReusedGeometryCount;

    public long StaticGeometryImmutableBufferUploadCount =>
        _staticGeometryUploads.ImmutableBufferUploadCount;

    public long StaticGeometryImmutableBufferUploadBytes =>
        _staticGeometryUploads.ImmutableBufferUploadBytes;

    private GlMesh CreateMesh(
        float[] vertices,
        uint[] indices,
        BufferUsageARB vertexUsage = BufferUsageARB.StaticDraw)
    {
        if (vertices.Length == 0 || indices.Length == 0)
            return default;

        Action<string>? trace = CreateLoadDetailReporter("mesh upload");
        trace?.Invoke(
            $"started; vertexFloats={vertices.Length}; " +
            $"indices={indices.Length}; usage={vertexUsage}");
        trace?.Invoke("driver glGenVertexArray started");
        uint vao = _gl.GenVertexArray();
        trace?.Invoke(
            $"driver glGenVertexArray returned; handle={vao}");
        trace?.Invoke("driver glGenBuffer started; role=vertex");
        uint vbo = _gl.GenBuffer();
        trace?.Invoke(
            $"driver glGenBuffer returned; role=vertex; handle={vbo}");
        trace?.Invoke("driver glGenBuffer started; role=index");
        uint ebo = _gl.GenBuffer();
        trace?.Invoke(
            $"driver glGenBuffer returned; role=index; handle={ebo}");

        trace?.Invoke(
            $"driver glBindVertexArray started; handle={vao}");
        _gl.BindVertexArray(vao);
        trace?.Invoke(
            $"driver glBindVertexArray returned; handle={vao}");
        UploadBuffer(vbo, vertices, vertexUsage, trace);
        UploadElementBuffer(ebo, indices, trace);

        const uint stride = MapRenderScene.VertexFloatCount * sizeof(float);
        trace?.Invoke("vertex attribute setup started");
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));
        trace?.Invoke("vertex attribute setup completed");
        trace?.Invoke("driver glBindVertexArray restore started; handle=0");
        _gl.BindVertexArray(0);
        trace?.Invoke("driver glBindVertexArray restore returned; handle=0");

        trace?.Invoke(
            $"completed; vao={vao}; vbo={vbo}; ebo={ebo}");
        return new GlMesh(vao, vbo, ebo, checked((uint)indices.Length));
    }

    private GlMesh CreateWireframeMesh(RenderSceneSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Wireframe is not { } submission)
            return default;

        RenderVertexLayoutDescriptor layout =
            snapshot.Resources.RequireVertexLayout(
                submission.VertexLayoutIdentity);
        RenderGeometryDescriptor geometry =
            snapshot.Resources.RequireGeometry(
                submission.GeometryIdentity);
        if (geometry.VertexLayout != layout.Identity ||
            geometry.CoordinateSpace != RenderGeometryCoordinateSpace.Render ||
            geometry.Topology != RenderPrimitiveTopology.LineList ||
            geometry.IndexFormat != RenderIndexFormat.Unsigned32 ||
            geometry.ByteOrder != RenderPayloadByteOrder.LittleEndian ||
            layout.StrideBytes !=
                checked(MapRenderScene.VertexFloatCount * sizeof(float)) ||
            layout.Elements.Length != 2 ||
            layout.Elements[0] != new RenderVertexElementDescriptor(
                RenderVertexSemantic.Position,
                semanticIndex: 0,
                RenderVertexElementFormat.Float32x3,
                offsetBytes: 0) ||
            layout.Elements[1] != new RenderVertexElementDescriptor(
                RenderVertexSemantic.Color,
                semanticIndex: 0,
                RenderVertexElementFormat.Float32x3,
                offsetBytes: 3 * sizeof(float)))
        {
            throw new ArgumentException(
                "OpenGL collision-wire upload requires the exact render-space position/color line-list resource shape.",
                nameof(snapshot));
        }

        uint vao = _gl.GenVertexArray();
        uint vbo = _gl.GenBuffer();
        uint ebo = _gl.GenBuffer();

        _gl.BindVertexArray(vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        ReadOnlySpan<byte> vertexPayload = geometry.VertexPayload.AsSpan();
        fixed (byte* vertexPtr = vertexPayload)
        {
            _gl.BufferData(
                BufferTargetARB.ArrayBuffer,
                checked((nuint)vertexPayload.Length),
                vertexPtr,
                BufferUsageARB.StaticDraw);
        }

        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, ebo);
        ReadOnlySpan<byte> indexPayload = geometry.IndexPayload.AsSpan();
        fixed (byte* indexPtr = indexPayload)
        {
            _gl.BufferData(
                BufferTargetARB.ElementArrayBuffer,
                checked((nuint)indexPayload.Length),
                indexPtr,
                BufferUsageARB.StaticDraw);
        }

        uint stride = checked((uint)layout.StrideBytes);
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(
            0,
            3,
            VertexAttribPointerType.Float,
            false,
            stride,
            (void*)0);
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(
            1,
            3,
            VertexAttribPointerType.Float,
            false,
            stride,
            (void*)(3 * sizeof(float)));
        _gl.BindVertexArray(0);

        return new GlMesh(
            vao,
            vbo,
            ebo,
            checked((uint)geometry.IndexCount));
    }

    private GlSkyMesh CreateSkyMesh(MapRenderSky sky)
    {
        if (!CanUploadTexture(sky.Texture))
            return default;

        uint vao = _gl.GenVertexArray();
        uint vbo = _gl.GenBuffer();
        uint ebo = _gl.GenBuffer();
        try
        {
            _gl.BindVertexArray(vao);
            UploadBuffer(vbo, sky.Vertices);
            UploadElementBuffer(ebo, sky.Indices);
            const uint stride = MapRenderScene.VertexFloatCount * sizeof(float);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
            _gl.BindVertexArray(0);

            return new GlSkyMesh(
                vao,
                vbo,
                ebo,
                checked((uint)sky.Indices.Length),
                CreateTexture(
                    sky.Texture,
                    pinForRendererLifetime: true));
        }
        catch
        {
            _gl.BindVertexArray(0);
            DeleteMesh(new GlMesh(vao, vbo, ebo, 0));
            throw;
        }
    }

    private GlSkyMesh[] CreateSkyMeshes(
        IReadOnlyList<MapRenderSky> skies,
        RenderSceneSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(skies);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (skies.Count != snapshot.Skies.Length)
        {
            throw new ArgumentException(
                "OpenGL sky upload requires the exact frozen scene ordinal space.",
                nameof(skies));
        }
        var meshes = new List<GlSkyMesh>(skies.Count);
        try
        {
            for (var ordinal = 0; ordinal < skies.Count; ordinal++)
            {
                MapRenderSky sky = skies[ordinal] ??
                    throw new InvalidDataException(
                        $"Sky source ordinal {ordinal} is null.");
                if (sky.Texture.Target !=
                        RenderTextureTarget.TextureCube ||
                    sky.Vertices.Length == 0 ||
                    sky.Indices.Length == 0)
                {
                    throw new InvalidDataException(
                        $"Sky source ordinal {ordinal} cannot be realized as an OpenGL cubemap draw.");
                }
                meshes.Add(CreateSkyMesh(sky));
            }

            return meshes.ToArray();
        }
        catch
        {
            foreach (GlSkyMesh mesh in meshes)
                DeleteSkyMesh(mesh);
            throw;
        }
    }

    private GlInstancedMesh CreateInstancedSolidMesh(MapRenderInstancedSolidBatch batch)
    {
        if (batch.Vertices.Length == 0 ||
            batch.Indices.Length == 0 ||
            batch.Instances.Count == 0)
            return default;

        Action<string>? trace =
            CreateLoadDetailReporter("instanced solid upload");
        trace?.Invoke(
            $"started; vertexFloats={batch.Vertices.Length}; " +
            $"indices={batch.Indices.Length}; " +
            $"instances={batch.Instances.Count}");
        uint vao = 0;
        uint vbo = 0;
        uint ebo = 0;
        uint instanceBuffer = 0;
        try
        {
            trace?.Invoke("driver glGenVertexArray started");
            vao = _gl.GenVertexArray();
            trace?.Invoke(
                $"driver glGenVertexArray returned; handle={vao}");
            trace?.Invoke("driver glGenBuffer started; role=vertex");
            vbo = _gl.GenBuffer();
            trace?.Invoke(
                $"driver glGenBuffer returned; role=vertex; handle={vbo}");
            trace?.Invoke("driver glGenBuffer started; role=index");
            ebo = _gl.GenBuffer();
            trace?.Invoke(
                $"driver glGenBuffer returned; role=index; handle={ebo}");
            trace?.Invoke("driver glGenBuffer started; role=instance");
            instanceBuffer = _gl.GenBuffer();
            trace?.Invoke(
                $"driver glGenBuffer returned; role=instance; " +
                $"handle={instanceBuffer}");
            trace?.Invoke(
                $"driver glBindVertexArray started; handle={vao}");
            _gl.BindVertexArray(vao);
            trace?.Invoke(
                $"driver glBindVertexArray returned; handle={vao}");
            UploadBuffer(vbo, batch.Vertices, trace: trace);
            UploadElementBuffer(ebo, batch.Indices, trace);

            const uint vertexStride =
                MapRenderScene.VertexFloatCount * sizeof(float);
            trace?.Invoke("vertex attribute setup started");
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(
                0,
                3,
                VertexAttribPointerType.Float,
                false,
                vertexStride,
                (void*)0);
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(
                1,
                3,
                VertexAttribPointerType.Float,
                false,
                vertexStride,
                (void*)(3 * sizeof(float)));
            trace?.Invoke("vertex attribute setup completed");
            trace?.Invoke("instance-buffer upload started");
            UploadInstanceTransforms(
                instanceBuffer,
                batch.Instances,
                firstAttribute: 2,
                trace: trace);
            trace?.Invoke("instance-buffer upload completed");
            trace?.Invoke("driver glBindVertexArray restore started; handle=0");
            _gl.BindVertexArray(0);
            trace?.Invoke("driver glBindVertexArray restore returned; handle=0");

            trace?.Invoke(
                $"completed; vao={vao}; vbo={vbo}; ebo={ebo}; " +
                $"instanceBuffer={instanceBuffer}");
            return new GlInstancedMesh(
                vao,
                vbo,
                ebo,
                instanceBuffer,
                checked((uint)batch.Indices.Length),
                checked((uint)batch.Instances.Count));
        }
        catch (Exception exception)
        {
            trace?.Invoke(
                $"failed; exception={exception.GetType().FullName}; " +
                $"message={QuoteLoadTraceValue(exception.Message)}");
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

    private GlTexturedMesh CreateInstancedTexturedMesh(
        MapRenderInstancedTexturedBatch batch,
        IReadOnlySet<AuthoredProgramGroupKey>?
            authorizedAuthoredProgramGroups = null,
        bool allowGenericFallback = true)
    {
        if (batch.IsGenericPreviewOnly &&
            authorizedAuthoredProgramGroups is not null)
        {
            throw new InvalidOperationException(
                "A compacted generic-preview static batch cannot execute an authored program. Use an exact normal-camera or receiver-variant batch instead.");
        }

        if (batch.Vertices.Length == 0 ||
            batch.Indices.Length == 0 ||
            batch.Instances.Count == 0 ||
            !CanUploadTexture(batch.Texture))
            return default;

        TranslatedProgramDirectCodeConstantPlan? directCodePlan;
        TranslatedProgramVertexConstantBindingPlan? vertexConstantPlan;
        MapRenderOpenGlStaticModelProgramUniforms?
            compiledStaticModelProgramUniforms = null;
        GlRsxProgram compiledRsxProgram = default;
        bool directCodePlanReady;
        bool vertexConstantPlanReady;
        if (authorizedAuthoredProgramGroups is null)
        {
            directCodePlanReady = TryCreateEditorDirectCodeConstantPlan(
                batch.ShaderExecution,
                batch.SceneLightIndex,
                out directCodePlan);
            if (directCodePlanReady &&
                directCodePlan is { } readyDirectCodePlan)
            {
                vertexConstantPlanReady =
                    TryCreateEditorVertexConstantBindingPlan(
                        batch.ShaderExecution,
                        readyDirectCodePlan,
                        out vertexConstantPlan);
            }
            else
            {
                directCodePlanReady = false;
                vertexConstantPlan = null;
                vertexConstantPlanReady = false;
            }
        }
        else
        {
            AuthoredProgramPreparation preparation =
                GetOrCreateAuthoredProgramPreparation(
                    batch.ShaderExecution,
                    batch.State,
                    batch.SceneLightIndex,
                    usesStaticModelInstancing: true);
            directCodePlan = preparation.DirectCodePlan;
            vertexConstantPlan = preparation.VertexConstantPlan;
            compiledRsxProgram = preparation.Program;
            compiledStaticModelProgramUniforms =
                preparation.StaticModelUniforms;
            directCodePlanReady = directCodePlan is not null;
            vertexConstantPlanReady = vertexConstantPlan is not null;
        }
        int vertexCount = batch.Vertices.Length /
            MapRenderScene.TexturedVertexFloatCount;
        bool useRsxVertexInputs =
            authorizedAuthoredProgramGroups?.Contains(
                AuthoredProgramGroup(batch)) == true &&
            batch.ShaderExecution.RendererProgramReady &&
            directCodePlanReady &&
            vertexConstantPlanReady &&
            compiledRsxProgram.Handle != 0 &&
            batch.ShaderExecution.VertexInputPayloadReady &&
            batch.RsxVertexInputs.Length == vertexCount * 16 * 4;
        MapRenderEditorShaderExecutionDecision executionDecision =
            MapRenderEditorShaderExecutionPolicy.Decide(
                new MapRenderEditorShaderExecutionInput(
                    batch.State,
                    AuthoredProgramAvailable(compiledRsxProgram),
                    useRsxVertexInputs,
                    GenericMaterialReady: allowGenericFallback));
        if (!executionDecision.IsExecutable)
            return default;

        bool executeTranslatedAuthored =
            executionDecision.Choice ==
            MapRenderEditorShaderExecutionChoice.TranslatedAuthored;
        if (!executeTranslatedAuthored && !allowGenericFallback)
            return default;
        MapRenderGenericMaterialFallbackContract genericMaterialFallback =
            MapRenderGenericMaterialFallbackContract.Create(
                RenderNormalCameraDrawSourceKind.StaticModel,
                batch.ShaderExecution,
                batch.ColorLayers);
        bool usesGenericStaticModelLighting =
            !executeTranslatedAuthored &&
            genericMaterialFallback.UsesStaticModelLighting;
        bool genericStaticModelLightingMatchesDirectionalSun =
            usesGenericStaticModelLighting &&
            _editorPreviewLighting?.DirectionalSunPrimaryLightIndex ==
                batch.SceneLightIndex;
        bool genericStaticModelLightingAddsDirectionalDiffuse =
            genericStaticModelLightingMatchesDirectionalSun &&
            genericMaterialFallback
                .StaticModelLightingAddsDirectionalDiffuse;
        bool genericStaticModelLightingAddsDirectionalSpecular =
            genericStaticModelLightingMatchesDirectionalSun &&
            genericMaterialFallback
                .StaticModelLightingAddsDirectionalSpecular;
        // Attribute 12 is one native per-instance payload lane. Generic atlas
        // lighting consumes row 0x39; translated lprobe programs consume row
        // 0x3A. Preserve the semantic kind because both share a 16-float
        // buffer layout and dynamic compaction cannot infer it from stride.
        MapRenderStaticInstanceLightingPayload instanceLightingPayload =
            executeTranslatedAuthored || usesGenericStaticModelLighting
                ? MapRenderStaticInstanceLightingPayload.BaseLightingCoords
                : MapRenderStaticInstanceLightingPayload.None;
        MapRenderStaticInstanceLightingPayload translatedLightingPayload =
            MapRenderStaticInstanceLightingPayload.None;
        if (executeTranslatedAuthored &&
            !MapRenderOpenGlStaticModelInstancedVertexComposer
                .TryResolveLightingPayload(
                    vertexConstantPlan!,
                    out translatedLightingPayload,
                    out _))
        {
            return default;
        }
        if (executeTranslatedAuthored &&
            translatedLightingPayload !=
                MapRenderStaticInstanceLightingPayload.None)
        {
            instanceLightingPayload = translatedLightingPayload;
        }

        uint vao = 0;
        uint instanceBuffer = 0;
        try
        {
            vao = _gl.GenVertexArray();
            instanceBuffer = _gl.GenBuffer();
            uint[] colorTextures = batch.ColorLayers
                .Take(MapRenderScene.MaxColorLayerCount)
                .Where(layer => CanUploadTexture(layer.Texture))
                .Select(layer => CreateTexture(layer.Texture))
                .ToArray();
            if (colorTextures.Length == 0)
                colorTextures = [CreateTexture(batch.Texture)];
            uint[] normalTextures = executeTranslatedAuthored
                ? []
                : CreateEditorRoleTextures(
                batch.MaterialSamplers.Select(binding => binding.Binding).ToArray(),
                [
                    EditorMaterialTextureRole.BaseNormal,
                    EditorMaterialTextureRole.NormalLayer1,
                    EditorMaterialTextureRole.NormalLayer2,
                    EditorMaterialTextureRole.NormalLayer3
                ]);
            uint[] specularTextures = executeTranslatedAuthored
                ? []
                : CreateEditorRoleTextures(
                batch.MaterialSamplers.Select(binding => binding.Binding).ToArray(),
                [
                    EditorMaterialTextureRole.BaseSpecular,
                    EditorMaterialTextureRole.SpecularLayer1,
                    EditorMaterialTextureRole.SpecularLayer2
                ]);

            _gl.BindVertexArray(vao);
            // The static placement bridge reads location 0 independently of
            // the authored program. Locations 12..15 remain instance-buffer
            // owned because UploadInstanceTransforms rewires them below.
            OpenGlPackedRsxVertexLayout?
                packedRsxVertexLayout = executeTranslatedAuthored
                    ? ResolvePackedRsxVertexLayout(
                        batch.ShaderExecution,
                        batch.DepthPrepassShaderExecution,
                        requiredAttributeMask: 1 << 0)
                    : null;
            float[] staticVertices = executeTranslatedAuthored
                ? batch.RsxVertexInputs
                : batch.Vertices;
            MapRenderOpenGlStaticGeometryBuffers geometry =
                _staticGeometryUploads.GetOrAdd(
                    executeTranslatedAuthored
                        ? MapRenderOpenGlStaticGeometryLayout.TranslatedRsx
                        : MapRenderOpenGlStaticGeometryLayout.GenericTextured,
                    staticVertices,
                    batch.Indices,
                    () => UploadStaticGeometry(
                        staticVertices,
                        batch.Indices,
                        packedRsxVertexLayout),
                    DeleteStaticGeometryBuffers,
                    vertexLayoutVariant:
                        packedRsxVertexLayout?.AttributeMask ?? 0,
                    uploadedVertexBytes:
                        packedRsxVertexLayout?.PackedByteCount(
                            staticVertices.Length));
            _gl.BindBuffer(
                BufferTargetARB.ArrayBuffer,
                geometry.VertexBuffer);
            _gl.BindBuffer(
                BufferTargetARB.ElementArrayBuffer,
                geometry.ElementBuffer);
            if (executeTranslatedAuthored)
            {
                ConfigureRsxVertexAttributes(
                    packedRsxVertexLayout!.Value);
            }
            else
                ConfigureTexturedVertexAttributes();
            UploadInstanceTransforms(
                instanceBuffer,
                batch.Instances,
                firstAttribute: executeTranslatedAuthored
                    ? MapRenderOpenGlStaticModelInstancedVertexComposer
                        .FirstPlacementAttribute
                    : 9,
                lightingPayload: instanceLightingPayload,
                usage: BufferUsageARB.DynamicDraw);
            _gl.BindVertexArray(0);

            GlRsxProgram rsxProgram = executeTranslatedAuthored
                ? compiledRsxProgram
                : default;
            GlRsxSamplerBinding[] rsxSamplerBindings =
                executeTranslatedAuthored
                    ? batch.MaterialSamplers
                        .Where(binding =>
                            CanUploadTexture(binding.Binding.Texture))
                        .GroupBy(binding => binding.Binding.Identity.SamplerDest)
                        .Select(group => group.First())
                        .Select(binding => new GlRsxSamplerBinding(
                            binding.Binding.Identity.SamplerDest,
                            CreateTexture(binding.Binding.Texture!),
                            ToGlTextureTarget(binding.Binding.Texture!.Target)))
                        .ToArray()
                    : [];
            GlRsxConstantBinding[] rsxConstantBindings =
                executeTranslatedAuthored
                    ? CreateRsxConstantBindings(
                    batch.ShaderExecution,
                    rsxProgram,
                    directCodePlan!,
                    vertexConstantPlan!,
                    ResolveAuthoredExternallyBoundVertexConstants(
                        vertexConstantPlan!,
                        usesStaticModelInstancing: true))
                    : [];
            GlRsxProgram depthPrepassRsxProgram = default;
            MapRenderOpenGlStaticModelProgramUniforms?
                depthStaticModelProgramUniforms = null;
            GlRsxConstantBinding[] depthPrepassRsxConstantBindings = [];
            ShaderExecutionContract? depthPrepassExecution = null;
            TranslatedProgramVertexConstantBindingPlan?
                depthPrepassVertexConstantPlan = null;
            if (executeTranslatedAuthored &&
                batch.EditorDepthPrepass is { } depthPrepass &&
                batch.DepthPrepassShaderExecution is
                    { ProgramExecutionReady: true } depthExecution)
            {
                AuthoredProgramPreparation depthPreparation =
                    GetOrCreateAuthoredProgramPreparation(
                        depthExecution,
                        depthPrepass.State,
                        batch.SceneLightIndex,
                        usesStaticModelInstancing: true);
                if (depthPreparation.Program.Handle != 0)
                {
                    if (depthPreparation.DirectCodePlan is not
                            { } depthDirectCodePlan ||
                        depthPreparation.VertexConstantPlan is not
                            { } depthVertexConstantPlan)
                    {
                        throw new InvalidOperationException(
                            "A ready authored depth program lost its constant plans.");
                    }
                    depthPrepassRsxProgram = depthPreparation.Program;
                    depthStaticModelProgramUniforms =
                        depthPreparation.StaticModelUniforms;
                    depthPrepassExecution = depthExecution;
                    depthPrepassVertexConstantPlan =
                        depthVertexConstantPlan;
                    depthPrepassRsxConstantBindings =
                        CreateRsxConstantBindings(
                            depthExecution,
                            depthPrepassRsxProgram,
                            depthDirectCodePlan,
                            depthVertexConstantPlan,
                            ResolveAuthoredExternallyBoundVertexConstants(
                                depthVertexConstantPlan,
                                usesStaticModelInstancing: true));
                }
            }

            int[] blendWeightComponents = batch.ColorLayers
                .Skip(1)
                .Take(MapRenderScene.MaxColorLayerCount - 1)
                .Select(layer => layer.BlendWeightComponent)
                .ToArray();
            ResolveTexturedLocalHeightRange(
                batch.Vertices,
                out float localMinimumHeight,
                out float localHeightRange);
            MapRenderEditorVegetationAnimationPlan? vegetationAnimation =
                localHeightRange > 0f &&
                batch.EditorVegetationAnimation?.IsEnabled == true
                    ? batch.EditorVegetationAnimation
                    : null;
            bool hasCertifiedTranslatedDepthFusion =
                executeTranslatedAuthored &&
                depthPrepassRsxProgram.Handle != 0 &&
                depthPrepassExecution is { } certifiedDepthExecution &&
                depthPrepassVertexConstantPlan is
                    { } certifiedDepthConstants &&
                NormalCameraDepthPrepassElisionCertification
                    .HasEquivalentTranslatedClipPosition(
                    batch.ShaderExecution.RendererProgramReady,
                    batch.ShaderExecution.VertexProgramIr,
                    batch.ShaderExecution.VertexInputs,
                    vertexConstantPlan!,
                    certifiedDepthExecution.RendererProgramReady,
                    certifiedDepthExecution.VertexProgramIr,
                    certifiedDepthExecution.VertexInputs,
                    certifiedDepthConstants);
            return new GlTexturedMesh(
            vao,
            geometry.VertexBuffer,
            geometry.ElementBuffer,
            instanceBuffer,
            colorTextures,
            blendWeightComponents,
            0,
            normalTextures,
            specularTextures,
            rsxProgram,
            rsxSamplerBindings,
            rsxConstantBindings,
            checked((uint)batch.Indices.Length),
            checked((uint)batch.Instances.Count),
            vegetationAnimation,
            localMinimumHeight,
            localHeightRange,
            ShouldReceiveGenericMaterialLighting(
                batch.Pass,
                usesGenericStaticModelLighting),
            executionDecision.EffectiveState) with
            {
                ShaderExecution = executeTranslatedAuthored
                    ? batch.ShaderExecution
                    : null,
                FragmentProgramControl =
                    batch.ShaderExecution.FragmentProgramControl,
                ColorInputLinearizationMask = executeTranslatedAuthored
                    ? 0
                    : genericMaterialFallback.ColorInputLinearizationMask,
                EditorDepthPrepass = batch.EditorDepthPrepass,
                DepthPrepassRsxProgram = depthPrepassRsxProgram,
                HasCertifiedTranslatedDepthFusion =
                    hasCertifiedTranslatedDepthFusion,
                StaticModelProgramUniforms = compiledStaticModelProgramUniforms,
                DepthStaticModelProgramUniforms =
                    depthStaticModelProgramUniforms,
                DepthPrepassRsxConstantBindings =
                    depthPrepassRsxConstantBindings,
                SceneLightIndex = batch.SceneLightIndex,
                StaticModelLodIndex = batch.LodIndex,
                StaticInstanceFloatStride =
                    MapRenderStaticInstanceBufferPacker.FloatStride(
                        instanceLightingPayload),
                StaticInstanceLightingPayload = instanceLightingPayload,
                StaticCameraRegion =
                    MapRenderOpenGlStaticCameraRegionPolicy
                        .ResolveUniformRegion(batch.Instances),
                OwnsGeometry = false,
                OwnsVertexArray = true,
                UsesGenericStaticModelLighting =
                    usesGenericStaticModelLighting,
                GenericStaticModelLightingAddsDirectionalDiffuse =
                    genericStaticModelLightingAddsDirectionalDiffuse,
                GenericStaticModelLightingAddsDirectionalSpecular =
                    genericStaticModelLightingAddsDirectionalSpecular
            };
        }
        catch
        {
            _gl.BindVertexArray(0);
            if (instanceBuffer != 0)
                _gl.DeleteBuffer(instanceBuffer);
            if (vao != 0)
                _gl.DeleteVertexArray(vao);
            throw;
        }
    }

    private MapRenderOpenGlStaticGeometryBuffers UploadStaticGeometry(
        float[] vertices,
        uint[] indices,
        OpenGlPackedRsxVertexLayout?
            packedRsxVertexLayout = null)
    {
        uint vertexBuffer = _gl.GenBuffer();
        uint elementBuffer = _gl.GenBuffer();
        try
        {
            if (packedRsxVertexLayout is { } layout)
            {
                UploadPackedRsxVertexBuffer(
                    vertexBuffer,
                    vertices,
                    layout);
            }
            else
            {
                UploadBuffer(vertexBuffer, vertices);
            }
            UploadElementBuffer(elementBuffer, indices);
            return new MapRenderOpenGlStaticGeometryBuffers(
                vertexBuffer,
                elementBuffer);
        }
        catch
        {
            DeleteStaticGeometryBuffers(
                new MapRenderOpenGlStaticGeometryBuffers(
                    vertexBuffer,
                    elementBuffer));
            throw;
        }
    }

    internal MapRenderEditorDrawGroup<GlTexturedDrawCommand>[]
        BuildEditorTexturedDrawGroups(
            IReadOnlyList<MapRenderTexturedBatch> worldBatches,
            IReadOnlyList<GlTexturedMesh> worldMeshes,
            IReadOnlyList<MapRenderInstancedTexturedBatch> instancedBatches,
            IReadOnlyList<GlTexturedMesh> instancedMeshes,
            int worldReceiverChannelIndex = -1)
    {
        if (worldBatches.Count != worldMeshes.Count)
            throw new ArgumentException(
                "Editor world batch and mesh counts must match.",
                nameof(worldMeshes));
        if (instancedBatches.Count != instancedMeshes.Count)
            throw new ArgumentException(
                "Editor instanced batch and mesh counts must match.",
                nameof(instancedMeshes));

        var groups = new List<MapRenderEditorDrawGroup<GlTexturedDrawCommand>>();
        IEnumerable<WorldEditorMeshEntry> worldEntries = worldBatches
            .Select((batch, ordinal) => new WorldEditorMeshEntry(
                ordinal,
                batch,
                worldMeshes[ordinal],
                worldMeshes[ordinal].WorldSurfaceIndex))
            .Where(entry => entry.Mesh.IndexCount > 0);
        foreach (IGrouping<EditorWorldDrawGroupKey, WorldEditorMeshEntry> sourceGroup in
                 worldEntries.GroupBy(entry => new EditorWorldDrawGroupKey(
                     entry.Batch.Pass.MaterialName,
                     entry.Batch.Pass.TechniquePass.TechniqueSetName,
                     entry.Batch.Pass.TechniquePass.TechniqueSlot,
                     entry.Batch.Pass.TechniquePass.TechniqueName,
                     entry.SurfaceIndex)))
        {
            WorldEditorMeshEntry[] ordered = sourceGroup
                .OrderBy(entry =>
                    entry.Batch.Pass.TechniquePass.PassIndex)
                .ThenBy(entry => entry.SourceOrdinal)
                .ToArray();
            MapRenderEditorDrawBucketClassification classification =
                MapRenderEditorDrawBucketClassifier.Classify(
                    ordered.Select(entry => entry.Batch.State).ToArray());
            RenderBounds bounds = ordered.Aggregate(
                RenderBounds.Empty,
                (current, entry) => IncludeBounds(
                    current,
                    entry.Mesh.WorldBounds));
            GlTexturedDrawCommand[] commands = ordered
                .Select(entry => new GlTexturedDrawCommand(
                    entry.Mesh,
                    WorldBatchIndex: entry.SourceOrdinal,
                    WorldReceiverChannelIndex:
                        worldReceiverChannelIndex))
                .ToArray();
            groups.Add(CreateEditorDrawGroup(
                ordered.Min(entry => entry.SourceOrdinal),
                classification,
                commands,
                bounds,
                ResolveWorldMultiDrawSortKey(commands)));
        }

        long instancedSourceOrdinalBase = worldBatches.Count;
        MapRenderEditorStaticPassBatch[] staticPasses = instancedBatches
            .Select((batch, ordinal) => new MapRenderEditorStaticPassBatch(
                ordinal,
                batch.EditorDrawGroupId >= 0
                    ? batch.EditorDrawGroupId
                    : int.MaxValue - ordinal,
                batch.Pass.TechniquePass.PassIndex,
                batch.Instances.Count,
                batch.State))
            .ToArray();
        IReadOnlyList<MapRenderEditorStaticDrawPlan> staticPlans =
            MapRenderEditorStaticDrawPlanner.Create(staticPasses);
        var localBoundsByVertices = new Dictionary<float[], RenderBounds>(
            ReferenceEqualityComparer.Instance);
        var localBoundsByBatch = new RenderBounds[instancedBatches.Count];
        for (int batchIndex = 0;
             batchIndex < instancedBatches.Count;
             batchIndex++)
        {
            float[] vertices = instancedBatches[batchIndex].Vertices;
            if (!localBoundsByVertices.TryGetValue(
                    vertices,
                    out RenderBounds localBounds))
            {
                localBounds = IncludeTexturedVertexBounds(
                    RenderBounds.Empty,
                    vertices);
                localBoundsByVertices.Add(vertices, localBounds);
            }
            localBoundsByBatch[batchIndex] = localBounds;
        }
        long staticOutputOrdinal = 0;
        for (int planOrdinal = 0; planOrdinal < staticPlans.Count; planOrdinal++)
        {
            MapRenderEditorStaticDrawPlan plan = staticPlans[planOrdinal];
            GfxCameraRegionType?[] passCameraRegions = plan.PassSourceOrdinals
                .Select(sourceOrdinal =>
                    instancedMeshes[sourceOrdinal].StaticCameraRegion)
                .ToArray();
            if (MapRenderOpenGlStaticCameraRegionPolicy
                .SuppressNormalCameraGroup(passCameraRegions))
            {
                // Region five owns an auxiliary static target on PS3. Keep
                // its mesh and instance buffer prepared for that future
                // producer, but do not synthesize normal-camera color or
                // depth submissions from it.
                continue;
            }

            int firstPassOrdinal = plan.PassSourceOrdinals[0];
            MapRenderInstancedTexturedBatch firstBatch =
                instancedBatches[firstPassOrdinal];
            IEnumerable<int?> instanceIndices = [plan.InstanceIndex];
            foreach (int? selectedInstanceIndex in instanceIndices)
            {
                GlTexturedDrawCommand[] commands = plan.PassSourceOrdinals
                    .Select(sourceOrdinal => new GlTexturedDrawCommand(
                        instancedMeshes[sourceOrdinal],
                        selectedInstanceIndex))
                    .Where(command =>
                        command.Mesh.IndexCount > 0 &&
                        command.Mesh.InstanceCount > 0)
                    .ToArray();
                if (commands.Length != plan.PassSourceOrdinals.Count)
                    continue;

                RenderBounds bounds =
                    selectedInstanceIndex is int instanceIndex
                        ? CalculateInstancedTexturedBounds(
                            firstBatch,
                            localBoundsByBatch[firstPassOrdinal],
                            instanceIndex)
                        : CalculateInstancedTexturedBounds(
                            firstBatch,
                            localBoundsByBatch[firstPassOrdinal]);
                groups.Add(CreateEditorDrawGroup(
                    instancedSourceOrdinalBase + staticOutputOrdinal++,
                    plan.Classification,
                    commands,
                    bounds,
                    plan.Classification.Bucket ==
                        MapRenderEditorDrawBucket.Translucent
                            ? null
                            : ResolveStaticColorSortKey(commands)));
            }
        }

        return groups
            .OrderBy(group => group.SourceOrdinal)
            .ToArray();
    }

    /// <summary>
    /// Returns a program-major queue key whose secondary component is the
    /// exact compatibility identity established by
    /// <see cref="AssignWorldMultiDrawBatchGroupIds"/>. This clusters shared
    /// translated programs while keeping compatible single-pass ranges
    /// contiguous, without changing translucent depth order or breaking an
    /// authored multipass group apart.
    /// </summary>
    internal static long? ResolveWorldMultiDrawSortKey(
        IReadOnlyList<GlTexturedDrawCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        if (commands.Count != 1)
            return null;

        GlTexturedMesh mesh = commands[0].Mesh;
        return mesh.InstanceCount == 0 &&
               mesh.MultiDrawBatchGroupId >= 0
            ? CreateColorQueueSortKey(
                mesh.RsxProgram.Handle,
                mesh.MultiDrawBatchGroupId,
                isStatic: false)
            : null;
    }

    private long? ResolveStaticColorSortKey(
        IReadOnlyList<GlTexturedDrawCommand> commands)
    {
        // Hashing only narrows the renderer-lifetime candidate set. Reuse is
        // decided below by the complete ordered-pass execution comparison.
        if (!TryComputeStaticColorSortHash(
                commands,
                out int hash,
                out uint firstProgramHandle))
        {
            return null;
        }

        if (_staticColorSortRepresentativesByHash.TryGetValue(
                hash,
                out List<StaticColorSortRepresentative>? candidates))
        {
            for (int candidateIndex = 0;
                 candidateIndex < candidates.Count;
                 candidateIndex++)
            {
                StaticColorSortRepresentative candidate =
                    candidates[candidateIndex];
                if (StaticColorSortExecutionMatches(candidate, commands))
                {
                    return CreateColorQueueSortKey(
                        firstProgramHandle,
                        candidate.Id,
                        isStatic: true);
                }
            }
        }

        if (_nextStaticColorSortGroupId == int.MaxValue)
            return null;

        var programHandles = new uint[commands.Count];
        var passes = new GlTexturedMesh[commands.Count];
        for (int passIndex = 0; passIndex < commands.Count; passIndex++)
        {
            GlTexturedMesh mesh = commands[passIndex].Mesh;
            if (!TryPrepareStaticColorSortMesh(
                    in mesh,
                    out programHandles[passIndex],
                    out passes[passIndex]))
            {
                return null;
            }
        }
        int groupId = _nextStaticColorSortGroupId++;
        var representative = new StaticColorSortRepresentative(
            groupId,
            programHandles,
            passes);
        (candidates ??= []).Add(representative);
        _staticColorSortRepresentativesByHash[hash] = candidates;
        return CreateColorQueueSortKey(
            firstProgramHandle,
            groupId,
            isStatic: true);
    }

    private bool TryComputeStaticColorSortHash(
        IReadOnlyList<GlTexturedDrawCommand> commands,
        out int hash,
        out uint firstProgramHandle)
    {
        hash = 0;
        firstProgramHandle = 0;
        if (commands.Count == 0)
            return false;

        var builder = new HashCode();
        builder.Add(commands.Count);
        for (int passIndex = 0; passIndex < commands.Count; passIndex++)
        {
            GlTexturedDrawCommand command = commands[passIndex];
            GlTexturedMesh mesh = command.Mesh;
            if (command.InstanceIndex.HasValue ||
                command.WorldBatchIndex >= 0 ||
                command.WorldReceiverChannelIndex >= 0 ||
                mesh.InstanceCount == 0 ||
                mesh.IndexCount == 0 ||
                !IsStaticColorQueueOrderIndependent(mesh.State) ||
                !TryPrepareStaticColorSortMesh(
                    in mesh,
                    out uint programHandle,
                    out GlTexturedMesh normalized) ||
                !TryAddStaticColorSupplementalHash(
                    ref builder,
                    in normalized))
            {
                return false;
            }

            builder.Add(programHandle);
            builder.Add(ComputeMultiDrawBatchHash(in normalized));
            if (passIndex == 0)
                firstProgramHandle = programHandle;
        }

        hash = builder.ToHashCode();
        return firstProgramHandle != 0;
    }

    private static bool IsStaticColorQueueOrderIndependent(
        RenderState state)
    {
        // Stencil consumes prior framebuffer contents even if the selected
        // pass does not blend. No-state passes execute the complete default
        // state; otherwise admit only the ordinary opaque/cutout depth owner.
        if (state.StencilEnabled || state.BlendEnabled)
            return false;
        if (!state.HasState)
            return true;

        return state.DepthTestEnabled &&
            state.DepthWriteEnabled &&
            (state.DepthFunc is RsxCompareFunction.Less or
                RsxCompareFunction.LessThanOrEqual) &&
            state.ColorMask == RsxColorMask.Rgba &&
            state.PolygonOffsetMode == RenderPolygonOffsetMode.Disabled;
    }

    private bool TryPrepareStaticColorSortMesh(
        in GlTexturedMesh mesh,
        out uint programHandle,
        out GlTexturedMesh normalized)
    {
        bool translated = mesh.RsxProgram.Handle != 0;
        programHandle = ResolveColorProgramHandle(in mesh);
        normalized = default;
        if (programHandle == 0)
            return false;
        if (mesh.ShaderExecution is { } execution &&
            execution.RuntimeSamplerRequirements is null)
        {
            return false;
        }

        if (translated)
        {
            if (mesh.RsxSamplerBindings is null ||
                mesh.RsxConstantBindings is null ||
                mesh.StaticModelProgramUniforms is null)
            {
                return false;
            }
        }
        else if (mesh.ColorTextures is null ||
                 mesh.ColorTextures.Length == 0 ||
                 mesh.BlendWeightComponents is null ||
                 mesh.NormalTextures is null ||
                 mesh.SpecularTextures is null)
        {
            return false;
        }

        // Reuse the world color execution compatibility owner after removing
        // only geometry/instance identity. Supplemental static inputs below
        // cover the draw-time state that the world path does not consume.
        normalized = mesh with
        {
            VertexArray = 0,
            ElementBuffer = 0,
            InstanceCount = 0,
            IndexType = DrawElementsType.UnsignedInt
        };
        return true;
    }

    private static bool TryAddStaticColorSupplementalHash(
        ref HashCode hash,
        in GlTexturedMesh mesh)
    {
        hash.Add(mesh.StaticInstanceFloatStride);
        hash.Add(mesh.StaticInstanceLightingPayload);
        hash.Add(mesh.LocalMinimumHeight);
        hash.Add(mesh.LocalHeightRange);
        AddCompositionPlanHash(ref hash, mesh.VegetationAnimation);
        if (mesh.RsxProgram.Handle != 0)
        {
            return mesh.StaticModelProgramUniforms is { } uniforms &&
                TryAddStaticProgramUniformHash(ref hash, uniforms);
        }

        hash.Add(mesh.ColorInputLinearizationMask);
        hash.Add(mesh.UsesGenericStaticModelLighting);
        hash.Add(mesh.GenericStaticModelLightingAddsDirectionalDiffuse);
        hash.Add(mesh.GenericStaticModelLightingAddsDirectionalSpecular);
        return true;
    }

    private uint ResolveColorProgramHandle(in GlTexturedMesh mesh) =>
        mesh.RsxProgram.Handle != 0
            ? mesh.RsxProgram.Handle
            : _texturedProgram;

    private bool StaticColorSortExecutionMatches(
        StaticColorSortRepresentative representative,
        IReadOnlyList<GlTexturedDrawCommand> commands)
    {
        if (representative.Passes.Length != commands.Count)
            return false;

        for (int passIndex = 0; passIndex < commands.Count; passIndex++)
        {
            GlTexturedMesh source = commands[passIndex].Mesh;
            if (!TryPrepareStaticColorSortMesh(
                    in source,
                    out uint programHandle,
                    out GlTexturedMesh candidate) ||
                representative.ProgramHandles[passIndex] != programHandle ||
                !CanMultiDrawTogether(
                    in representative.Passes[passIndex],
                    in candidate) ||
                !StaticColorSupplementalStateMatches(
                    in representative.Passes[passIndex],
                    in candidate))
            {
                return false;
            }
        }
        return true;
    }

    private static bool StaticColorSupplementalStateMatches(
        in GlTexturedMesh first,
        in GlTexturedMesh next)
    {
        bool translated = first.RsxProgram.Handle != 0;
        if (first.StaticInstanceFloatStride !=
                next.StaticInstanceFloatStride ||
            first.StaticInstanceLightingPayload !=
                next.StaticInstanceLightingPayload ||
            first.LocalMinimumHeight != next.LocalMinimumHeight ||
            first.LocalHeightRange != next.LocalHeightRange ||
            !CompositionPlansMatch(
                first.VegetationAnimation,
                next.VegetationAnimation))
        {
            return false;
        }

        if (translated)
        {
            return StaticProgramUniformsMatch(
                first.StaticModelProgramUniforms,
                next.StaticModelProgramUniforms);
        }

        return first.ColorInputLinearizationMask ==
                   next.ColorInputLinearizationMask &&
               first.UsesGenericStaticModelLighting ==
                   next.UsesGenericStaticModelLighting &&
               first.GenericStaticModelLightingAddsDirectionalDiffuse ==
                   next.GenericStaticModelLightingAddsDirectionalDiffuse &&
               first.GenericStaticModelLightingAddsDirectionalSpecular ==
                   next.GenericStaticModelLightingAddsDirectionalSpecular;
    }

    private static bool StaticProgramUniformsMatch(
        MapRenderOpenGlStaticModelProgramUniforms? first,
        MapRenderOpenGlStaticModelProgramUniforms? next)
    {
        if (!first.HasValue || !next.HasValue)
            return first.HasValue == next.HasValue;

        MapRenderOpenGlStaticModelProgramUniforms firstValue = first.Value;
        MapRenderOpenGlStaticModelProgramUniforms nextValue = next.Value;
        return firstValue.Vegetation == nextValue.Vegetation;
    }

    private static bool TryAddStaticProgramUniformHash(
        ref HashCode hash,
        MapRenderOpenGlStaticModelProgramUniforms uniforms)
    {
        if (!uniforms.Vegetation.IsReady)
        {
            return false;
        }

        hash.Add(uniforms.Vegetation);
        return true;
    }

    private static void AddCompositionPlanHash(
        ref HashCode hash,
        MapRenderEditorVegetationAnimationPlan? plan)
    {
        hash.Add(plan is not null);
        if (plan is null)
            return;

        // Keep this exact field set paired with CompositionPlansMatch and the
        // owning world compatibility hash.
        hash.Add(plan.Status);
        hash.Add(plan.IsEnabled);
        hash.Add(plan.Amplitude);
        hash.Add(plan.AngularFrequency);
        hash.Add(plan.SpatialFrequency);
    }

    private readonly record struct StaticColorSortRepresentative(
        int Id,
        uint[] ProgramHandles,
        GlTexturedMesh[] Passes);

    /// <summary>
    /// Returns a depth-program-major compatibility identity. Unlike the color
    /// queue key, its exact group may intentionally join materials with
    /// different textures or fragment programs when their standard depth
    /// owner is identical.
    /// </summary>
    internal static long? ResolveWorldDepthMultiDrawSortKey(
        MapRenderEditorDrawGroup<GlTexturedDrawCommand> group)
    {
        ArgumentNullException.ThrowIfNull(group);
        ReadOnlySpan<GlTexturedDrawCommand> commands =
            group.AuthoredPassSpan;
        if (commands.Length != 1)
            return null;

        return ResolveWorldDepthMultiDrawSortKey(in commands[0]);
    }

    private static long? ResolveWorldDepthMultiDrawSortKey(
        in GlTexturedDrawCommand command)
    {
        GlTexturedMesh mesh = command.Mesh;
        return command.InstanceIndex is null &&
               mesh.InstanceCount == 0 &&
               mesh.IndexCount != 0 &&
               mesh.VertexArray != 0 &&
               mesh.ElementBuffer != 0 &&
               mesh.DepthMultiDrawBatchGroupId >= 0
            ? CreateProgramMajorMultiDrawSortKey(
                mesh.DepthPrepassRsxProgram.Handle,
                mesh.DepthMultiDrawBatchGroupId)
            : null;
    }

    private static long CreateProgramMajorMultiDrawSortKey(
        uint programHandle,
        int batchGroupId) =>
        unchecked(
            ((long)programHandle << 32) |
            (uint)batchGroupId);

    private static long CreateColorQueueSortKey(
        uint programHandle,
        int exactGroupId,
        bool isStatic)
    {
        if (exactGroupId < 0)
            throw new ArgumentOutOfRangeException(nameof(exactGroupId));

        // Signed ordering keeps nullable/unkeyed world groups first, then the
        // negative world domain, then the nonnegative static domain. Each
        // domain retains all 32 program bits and all 31 nonnegative ID bits.
        ulong packed = ((ulong)programHandle << 31) |
            (uint)exactGroupId;
        if (!isStatic)
            packed |= 1UL << 63;
        return unchecked((long)packed);
    }

    /// <summary>
    /// Builds the immutable standard-depth queue once per loaded scene. The
    /// depth owner writes no color and has no translucent ordering dependency,
    /// so compatible world commands can be made contiguous through depth-only
    /// singleton groups without changing the authored color queue.
    /// </summary>
    internal static MapRenderEditorDrawGroup<GlTexturedDrawCommand>[]
        BuildDepthPrepassDrawGroupOrder(
            IReadOnlyList<
                MapRenderEditorDrawGroup<GlTexturedDrawCommand>> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);

        var selected = new List<(
            MapRenderEditorDrawGroup<GlTexturedDrawCommand> Group,
            int InputOrdinal,
            MapRenderEditorDepthPrepassPlan Plan)>();
        for (int inputOrdinal = 0;
             inputOrdinal < groups.Count;
             inputOrdinal++)
        {
            MapRenderEditorDrawGroup<GlTexturedDrawCommand> group =
                groups[inputOrdinal];
            IReadOnlyList<GlTexturedDrawCommand> commands =
                SelectStandardDepthPrepassCommands(
                    group,
                    out MapRenderEditorDepthPrepassPlan? plan);
            if (plan is not null && commands.Count != 0)
                selected.Add((group, inputOrdinal, plan));
        }

        // Inherit deliberately consumes the polygon-offset state left by the
        // previous draw. Any sorting or pass splitting could therefore change
        // depth coverage. Preserve the filtered source queue exactly whenever
        // one standard-depth group carries that state action.
        if (selected.Any(entry =>
                entry.Plan.State.PolygonOffsetMode ==
                    RenderPolygonOffsetMode.Inherit))
        {
            return selected
                .Select(entry => entry.Group)
                .ToArray();
        }

        var depthEntries = new List<(
            MapRenderEditorDrawGroup<GlTexturedDrawCommand> Group,
            int InputOrdinal,
            int CommandOrdinal,
            long? Key)>();
        foreach ((
                     MapRenderEditorDrawGroup<GlTexturedDrawCommand> group,
                     int inputOrdinal,
                     _) in selected)
        {
            ReadOnlySpan<GlTexturedDrawCommand> commands =
                group.AuthoredPassSpan;
            bool canSplitWorldCommands = commands.Length > 1;
            for (int commandOrdinal = 0;
                 canSplitWorldCommands && commandOrdinal < commands.Length;
                 commandOrdinal++)
            {
                ref readonly GlTexturedDrawCommand command =
                    ref commands[commandOrdinal];
                canSplitWorldCommands =
                    ResolveWorldDepthMultiDrawSortKey(in command).HasValue;
            }

            if (!canSplitWorldCommands)
            {
                depthEntries.Add((
                    group,
                    inputOrdinal,
                    CommandOrdinal: -1,
                    ResolveWorldDepthMultiDrawSortKey(group)));
                continue;
            }

            for (int commandOrdinal = 0;
                 commandOrdinal < commands.Length;
                 commandOrdinal++)
            {
                ref readonly GlTexturedDrawCommand command =
                    ref commands[commandOrdinal];
                long key = ResolveWorldDepthMultiDrawSortKey(in command) ??
                    throw new InvalidOperationException(
                        "A validated world depth command lost its immutable compatibility key.");
                depthEntries.Add((
                    CreateDepthPrepassSingletonGroup(
                        group,
                        in command,
                        key),
                    inputOrdinal,
                    commandOrdinal,
                    key));
            }
        }

        return depthEntries
            .OrderBy(entry => entry.Key.HasValue ? 1 : 0)
            .ThenBy(entry => entry.Key ?? 0)
            .ThenBy(entry => entry.Group.SourceOrdinal)
            .ThenBy(entry => entry.InputOrdinal)
            .ThenBy(entry => entry.CommandOrdinal)
            .Select(entry => entry.Group)
            .ToArray();
    }

    private static MapRenderEditorDrawGroup<GlTexturedDrawCommand>
        CreateDepthPrepassSingletonGroup(
            MapRenderEditorDrawGroup<GlTexturedDrawCommand> source,
            in GlTexturedDrawCommand command,
            long sortKey)
    {
        GlTexturedDrawCommand[] commands = [command];
        return source.SortCenter is { } sortCenter
            ? MapRenderEditorDrawGroup<GlTexturedDrawCommand>.FromCenter(
                source.SourceOrdinal,
                source.Classification,
                commands,
                sortCenter,
                sortKey)
            : MapRenderEditorDrawGroup<GlTexturedDrawCommand>.FromExplicitDepth(
                source.SourceOrdinal,
                source.Classification,
                commands,
                source.ExplicitDepth ??
                    throw new InvalidOperationException(
                        "A depth draw group has no sort center or explicit depth."),
                sortKey);
    }

    private static MapRenderEditorDrawGroup<GlTexturedDrawCommand> CreateEditorDrawGroup(
        long sourceOrdinal,
        MapRenderEditorDrawBucketClassification classification,
        IReadOnlyList<GlTexturedDrawCommand> commands,
        RenderBounds bounds,
        long? cameraIndependentSortKey = null) =>
        bounds.IsValid
            ? MapRenderEditorDrawGroup<GlTexturedDrawCommand>.FromBounds(
                sourceOrdinal,
                classification,
                commands,
                bounds,
                cameraIndependentSortKey)
            : MapRenderEditorDrawGroup<GlTexturedDrawCommand>.FromExplicitDepth(
                sourceOrdinal,
                classification,
                commands,
                0f,
                cameraIndependentSortKey);

    private static int ResolveSingleWorldSurfaceIndex(
        MapRenderTexturedBatch batch)
    {
        int surfaceIndex = -1;
        foreach (MapRenderPickRange range in batch.PickRanges)
        {
            if (range.Kind != MapRenderPickKind.GfxSurface)
                continue;
            if (surfaceIndex < 0)
            {
                surfaceIndex = range.SurfaceIndex;
                continue;
            }
            if (range.SurfaceIndex != surfaceIndex)
                return -1;
        }
        return surfaceIndex;
    }

    private static RenderBounds IncludeBounds(
        RenderBounds current,
        RenderBounds added) =>
        added.IsValid
            ? current.Include(added.Min).Include(added.Max)
            : current;

    private static RenderBounds IncludeTexturedVertexBounds(
        RenderBounds bounds,
        IReadOnlyList<float> vertices)
    {
        for (int offset = 0;
             offset + 2 < vertices.Count;
             offset += MapRenderScene.TexturedVertexFloatCount)
        {
            var position = new Vector3(
                vertices[offset],
                vertices[offset + 1],
                vertices[offset + 2]);
            if (float.IsFinite(position.X) &&
                float.IsFinite(position.Y) &&
                float.IsFinite(position.Z))
            {
                bounds = bounds.Include(position);
            }
        }

        return bounds;
    }

    internal static void ResolveTexturedLocalHeightRange(
        IReadOnlyList<float> vertices,
        out float localMinimumHeight,
        out float localHeightRange)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        float minimum = float.PositiveInfinity;
        float maximum = float.NegativeInfinity;
        // Static XSurface vertices remain in game-local XYZ until the instance
        // rows transform them. Game-local Z is therefore the model-up axis.
        for (int offset = 2;
             offset < vertices.Count;
             offset += MapRenderScene.TexturedVertexFloatCount)
        {
            float height = vertices[offset];
            if (!float.IsFinite(height))
                continue;
            minimum = MathF.Min(minimum, height);
            maximum = MathF.Max(maximum, height);
        }

        if (!float.IsFinite(minimum) ||
            !float.IsFinite(maximum) ||
            maximum - minimum <= 0.0001f)
        {
            localMinimumHeight = 0f;
            localHeightRange = 0f;
            return;
        }

        localMinimumHeight = minimum;
        localHeightRange = maximum - minimum;
    }

    private static RenderBounds CalculateInstancedTexturedBounds(
        MapRenderInstancedTexturedBatch batch,
        RenderBounds localBounds)
    {
        if (!localBounds.IsValid)
            return RenderBounds.Empty;

        RenderBounds result = RenderBounds.Empty;
        foreach (MapRenderStaticModelInstance instance in batch.Instances)
            result = IncludeTransformedBounds(result, localBounds, instance);

        return result;
    }

    private static RenderBounds CalculateInstancedTexturedBounds(
        MapRenderInstancedTexturedBatch batch,
        RenderBounds localBounds,
        int instanceIndex)
    {
        if ((uint)instanceIndex >= (uint)batch.Instances.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(instanceIndex),
                instanceIndex,
                "Static-model instance index is outside the batch.");
        }

        return localBounds.IsValid
            ? IncludeTransformedBounds(
                RenderBounds.Empty,
                localBounds,
                batch.Instances[instanceIndex])
            : RenderBounds.Empty;
    }

    private static RenderBounds IncludeTransformedBounds(
        RenderBounds result,
        RenderBounds localBounds,
        MapRenderStaticModelInstance instance)
    {
        for (int corner = 0; corner < 8; corner++)
        {
            var local = new Vector4(
                (corner & 1) == 0 ? localBounds.Min.X : localBounds.Max.X,
                (corner & 2) == 0 ? localBounds.Min.Y : localBounds.Max.Y,
                (corner & 4) == 0 ? localBounds.Min.Z : localBounds.Max.Z,
                1f);
            var world = new Vector3(
                Vector4.Dot(instance.TransformRow0, local),
                Vector4.Dot(instance.TransformRow1, local),
                Vector4.Dot(instance.TransformRow2, local));
            if (float.IsFinite(world.X) &&
                float.IsFinite(world.Y) &&
                float.IsFinite(world.Z))
            {
                result = result.Include(world);
            }
        }

        return result;
    }

    private readonly record struct WorldEditorMeshEntry(
        int SourceOrdinal,
        MapRenderTexturedBatch Batch,
        GlTexturedMesh Mesh,
        int SurfaceIndex);

    private readonly record struct EditorWorldDrawGroupKey(
        string MaterialName,
        string TechniqueSetName,
        int TechniqueSlot,
        string TechniqueName,
        int SurfaceIndex);

    private void UploadBuffer(
        uint buffer,
        float[] values,
        BufferUsageARB usage = BufferUsageARB.StaticDraw,
        Action<string>? trace = null)
    {
        trace?.Invoke(
            $"driver glBindBuffer started; role=vertex; " +
            $"target={BufferTargetARB.ArrayBuffer}; handle={buffer}");
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, buffer);
        trace?.Invoke(
            $"driver glBindBuffer returned; role=vertex; handle={buffer}");
        fixed (float* ptr = values)
        {
            trace?.Invoke(
                $"driver glBufferData started; role=vertex; " +
                $"bytes={checked((long)values.Length * sizeof(float))}; " +
                $"usage={usage}");
            _gl.BufferData(
                BufferTargetARB.ArrayBuffer,
                (nuint)(values.Length * sizeof(float)),
                ptr,
                usage);
            trace?.Invoke(
                $"driver glBufferData returned; role=vertex; " +
                $"bytes={checked((long)values.Length * sizeof(float))}");
        }
    }

    private void UploadPackedRsxVertexBuffer(
        uint buffer,
        float[] source,
        OpenGlPackedRsxVertexLayout layout)
    {
        int packedFloatCount = layout.PackedFloatCount(source.Length);
        float[] packed = ArrayPool<float>.Shared.Rent(
            packedFloatCount);
        try
        {
            Span<float> packedSpan =
                packed.AsSpan(0, packedFloatCount);
            layout.Pack(source, packedSpan);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, buffer);
            fixed (float* ptr = packedSpan)
            {
                _gl.BufferData(
                    BufferTargetARB.ArrayBuffer,
                    checked((nuint)packedFloatCount *
                        sizeof(float)),
                    ptr,
                    BufferUsageARB.StaticDraw);
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(
                packed,
                clearArray: false);
        }
    }

    private void UploadElementBuffer(
        uint buffer,
        uint[] values,
        Action<string>? trace = null)
    {
        trace?.Invoke(
            $"driver glBindBuffer started; role=index; " +
            $"target={BufferTargetARB.ElementArrayBuffer}; handle={buffer}");
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, buffer);
        trace?.Invoke(
            $"driver glBindBuffer returned; role=index; handle={buffer}");
        fixed (uint* ptr = values)
        {
            trace?.Invoke(
                $"driver glBufferData started; role=index; " +
                $"indexType={DrawElementsType.UnsignedInt}; " +
                $"bytes={checked((long)values.Length * sizeof(uint))}");
            _gl.BufferData(
                BufferTargetARB.ElementArrayBuffer,
                (nuint)(values.Length * sizeof(uint)),
                ptr,
                BufferUsageARB.StaticDraw);
            trace?.Invoke(
                $"driver glBufferData returned; role=index; " +
                $"indexType={DrawElementsType.UnsignedInt}; " +
                $"bytes={checked((long)values.Length * sizeof(uint))}");
        }
    }

    private void UploadElementBuffer(
        uint buffer,
        uint[] values,
        DrawElementsType indexType,
        Action<string>? trace = null)
    {
        if (indexType == DrawElementsType.UnsignedInt)
        {
            UploadElementBuffer(buffer, values, trace);
            return;
        }
        if (indexType != DrawElementsType.UnsignedShort)
        {
            throw new ArgumentOutOfRangeException(
                nameof(indexType),
                indexType,
                "World geometry requires unsigned 16-bit or 32-bit indices.");
        }

        ushort[] packed = ArrayPool<ushort>.Shared.Rent(values.Length);
        try
        {
            trace?.Invoke(
                $"unsigned-short index staging started; " +
                $"indices={values.Length}");
            for (int index = 0; index < values.Length; index++)
                packed[index] = checked((ushort)values[index]);
            trace?.Invoke(
                $"unsigned-short index staging completed; " +
                $"indices={values.Length}");

            trace?.Invoke(
                $"driver glBindBuffer started; role=index; " +
                $"target={BufferTargetARB.ElementArrayBuffer}; handle={buffer}");
            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, buffer);
            trace?.Invoke(
                $"driver glBindBuffer returned; role=index; handle={buffer}");
            fixed (ushort* ptr = packed)
            {
                trace?.Invoke(
                    $"driver glBufferData started; role=index; " +
                    $"indexType={DrawElementsType.UnsignedShort}; " +
                    $"bytes={checked((long)values.Length * sizeof(ushort))}");
                _gl.BufferData(
                    BufferTargetARB.ElementArrayBuffer,
                    checked((nuint)values.Length * sizeof(ushort)),
                    ptr,
                    BufferUsageARB.StaticDraw);
                trace?.Invoke(
                    $"driver glBufferData returned; role=index; " +
                    $"indexType={DrawElementsType.UnsignedShort}; " +
                    $"bytes={checked((long)values.Length * sizeof(ushort))}");
            }
        }
        finally
        {
            ArrayPool<ushort>.Shared.Return(
                packed,
                clearArray: false);
        }
    }

    private void PackWorldGeometryArenas(
        IReadOnlyList<MapRenderTexturedBatch> batches)
    {
        if (batches.Count != _textured.Length)
        {
            throw new ArgumentException(
                "World batch and uploaded mesh counts must match.",
                nameof(batches));
        }

        WorldGeometryArenaUploadCount = 0;
        WorldGeometrySourceBatchCount = 0;
        WorldGeometryImmutableBufferUploadCount = 0;
        WorldGeometryImmutableBufferUploadBytes = 0;
        WorldGeometryTranslatedArenaCount = 0;
        WorldGeometryMaximumTranslatedArenaAttributeCount = 0;
        (GlTexturedMesh[] replacement,
            GlMesh genericArena,
            GlMesh[] translatedArenas) =
            CreatePackedWorldGeometryArenas(batches, _textured);

        _textured = replacement;
        _genericWorldArena = genericArena;
        _translatedWorldArenas = translatedArenas;
    }

    private (GlTexturedMesh[] Meshes,
        GlMesh GenericArena,
        GlMesh[] TranslatedArenas) CreatePackedWorldGeometryArenas(
            IReadOnlyList<MapRenderTexturedBatch> batches,
            IReadOnlyList<GlTexturedMesh> meshes)
    {
        if (batches.Count != meshes.Count)
        {
            throw new ArgumentException(
                "World batch and uploaded mesh counts must match.",
                nameof(batches));
        }

        GlTexturedMesh[] replacement = meshes.ToArray();
        GlMesh genericArena = default;
        var translatedArenas = new List<GlMesh>();
        Action<string>? trace =
            CreateLoadDetailReporter("world geometry arenas");
        try
        {
            trace?.Invoke("generic batch selection started");
            int[] genericMeshIndices = Enumerable
                .Range(0, meshes.Count)
                .Where(index =>
                    meshes[index].IndexCount != 0 &&
                    meshes[index].RsxProgram.Handle == 0)
                .ToArray();
            trace?.Invoke(
                $"generic batch selection completed; " +
                $"batches={genericMeshIndices.Length}");
            genericArena = CreateWorldGeometryArena(
                batches,
                meshes,
                replacement,
                genericMeshIndices,
                packedRsxVertexLayout: null,
                arenaIdentity: "generic");

            trace?.Invoke("translated batch grouping started");
            foreach (IGrouping<int, int> arenaGroup in Enumerable
                         .Range(0, meshes.Count)
                         .Where(index =>
                             meshes[index].IndexCount != 0 &&
                             meshes[index].RsxProgram.Handle != 0)
                         .GroupBy(index =>
                             ResolveWorldTranslatedAttributeMask(
                                 batches[index]))
                         .OrderBy(group => group.Key))
            {
                var layout =
                    new OpenGlPackedRsxVertexLayout(
                        arenaGroup.Key);
                translatedArenas.Add(CreateWorldGeometryArena(
                    batches,
                        meshes,
                        replacement,
                        arenaGroup.ToArray(),
                        layout,
                        $"translated-mask-0x{arenaGroup.Key:X4}"));
                WorldGeometryTranslatedArenaCount++;
                WorldGeometryMaximumTranslatedArenaAttributeCount =
                    Math.Max(
                        WorldGeometryMaximumTranslatedArenaAttributeCount,
                        layout.AttributeCount);
            }
            trace?.Invoke(
                $"translated batch grouping completed; " +
                $"arenas={translatedArenas.Count}");

            return (
                replacement,
                genericArena,
                translatedArenas.ToArray());
        }
        catch
        {
            DeleteMesh(genericArena);
            foreach (GlMesh translatedArena in translatedArenas)
                DeleteMesh(translatedArena);
            throw;
        }
    }

    private static int ResolveWorldTranslatedAttributeMask(
        MapRenderTexturedBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        int attributeMask =
            OpenGlPackedRsxVertexLayout.ResolveAttributeMask(
                batch.ShaderExecution);
        if (batch.DepthPrepassShaderExecution is { } depthExecution)
        {
            attributeMask |=
                OpenGlPackedRsxVertexLayout.ResolveAttributeMask(
                    depthExecution);
        }

        // A constant-only translated vertex program still needs one physical
        // row per source vertex so arena packing can preserve BaseVertex and
        // indexed-draw cardinality. Location zero is the established
        // structural carrier; the shader does not consume it in this case.
        return attributeMask == 0
            ? 1 << 0
            : attributeMask;
    }

    private GlMesh CreateWorldGeometryArena(
        IReadOnlyList<MapRenderTexturedBatch> batches,
        IReadOnlyList<GlTexturedMesh> sourceMeshes,
        GlTexturedMesh[] replacement,
        IReadOnlyList<int> meshIndices,
        OpenGlPackedRsxVertexLayout? packedRsxVertexLayout,
        string arenaIdentity)
    {
        ArgumentNullException.ThrowIfNull(meshIndices);
        if (meshIndices.Count == 0)
            return default;

        Action<string>? trace = CreateLoadDetailReporter(
            $"world geometry arena={arenaIdentity}");
        bool translated = packedRsxVertexLayout is not null;
        int sourceFloatsPerVertex = translated
            ? OpenGlPackedRsxVertexLayout.SourceFloatStride
            : MapRenderScene.TexturedVertexFloatCount;
        trace?.Invoke(
            $"source collection started; batches={meshIndices.Count}; " +
            $"translated={translated}; " +
            $"sourceFloatStride={sourceFloatsPerVertex}; " +
            $"attributeMask={(packedRsxVertexLayout?.AttributeMask ?? 0):X4}; " +
            $"attributeCount={packedRsxVertexLayout?.AttributeCount ?? 0}");
        var sources =
            new MapRenderOpenGlWorldGeometryArenaSource[
                meshIndices.Count];
        for (int sourceIndex = 0;
             sourceIndex < meshIndices.Count;
             sourceIndex++)
        {
            int meshIndex = meshIndices[sourceIndex];
            sources[sourceIndex] =
                new MapRenderOpenGlWorldGeometryArenaSource(
                    meshIndex,
                    translated
                        ? batches[meshIndex].RsxVertexInputs
                        : batches[meshIndex].Vertices,
                    batches[meshIndex].Indices);
        }
        trace?.Invoke(
            $"source collection completed; " +
            $"sourceFloats={sources.Sum(source => (long)source.Vertices.Length)}; " +
            $"sourceVertices={sources.Sum(source => (long)source.Vertices.Length / sourceFloatsPerVertex)}; " +
            $"sourceIndices={sources.Sum(source => (long)source.Indices.Length)}");

        long packingStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        trace?.Invoke("CPU arena packing started");
        MapRenderOpenGlWorldGeometryArenaPacking packing =
            packedRsxVertexLayout is { } rsxLayout
                ? MapRenderOpenGlWorldGeometryArenaPacker
                    .PackTranslatedRsx(sources, rsxLayout)
                : MapRenderOpenGlWorldGeometryArenaPacker.Pack(
                    sources,
                    sourceFloatsPerVertex);
        trace?.Invoke(
            $"CPU arena packing completed; " +
            $"vertexFloats={packing.Vertices.Length}; " +
            $"vertexBytes={checked((long)packing.Vertices.Length * sizeof(float))}; " +
            $"indices={packing.Indices.Length}; " +
            $"indexType={packing.IndexType}; " +
            $"placements={packing.Placements.Length}; " +
            $"elapsed={System.Diagnostics.Stopwatch.GetElapsedTime(packingStarted).TotalMilliseconds:0}ms");

        trace?.Invoke("driver glGenVertexArray started");
        uint vao = _gl.GenVertexArray();
        trace?.Invoke(
            $"driver glGenVertexArray returned; handle={vao}");
        trace?.Invoke("driver glGenBuffer started; role=vertex");
        uint vbo = _gl.GenBuffer();
        trace?.Invoke(
            $"driver glGenBuffer returned; role=vertex; handle={vbo}");
        trace?.Invoke("driver glGenBuffer started; role=index");
        uint ebo = _gl.GenBuffer();
        trace?.Invoke(
            $"driver glGenBuffer returned; role=index; handle={ebo}");
        try
        {
            trace?.Invoke(
                $"driver glBindVertexArray started; handle={vao}");
            _gl.BindVertexArray(vao);
            trace?.Invoke(
                $"driver glBindVertexArray returned; handle={vao}");
            UploadBuffer(vbo, packing.Vertices, trace: trace);
            UploadElementBuffer(
                ebo,
                packing.Indices,
                packing.IndexType,
                trace);
            WorldGeometryArenaUploadCount++;
            WorldGeometrySourceBatchCount = checked(
                WorldGeometrySourceBatchCount + packing.SourceCount);
            WorldGeometryImmutableBufferUploadCount = checked(
                WorldGeometryImmutableBufferUploadCount +
                packing.ImmutableBufferUploadOperationCount);
            WorldGeometryImmutableBufferUploadBytes = checked(
                WorldGeometryImmutableBufferUploadBytes +
                packing.ImmutableBufferUploadBytes);

            trace?.Invoke(
                $"mesh placement projection started; " +
                $"placements={packing.Placements.Length}");
            foreach (
                MapRenderOpenGlWorldGeometryArenaPlacement placement in
                packing.Placements)
            {
                replacement[placement.MeshIndex] =
                    replacement[placement.MeshIndex] with
                {
                    VertexArray = vao,
                    VertexBuffer = vbo,
                    ElementBuffer = ebo,
                    IndexType = packing.IndexType,
                    IndexOffsetBytes = placement.IndexOffsetBytes,
                    BaseVertex = placement.BaseVertex,
                    OwnsGeometry = false
                };
            }
            trace?.Invoke("mesh placement projection completed");

            trace?.Invoke(
                $"vertex attribute setup started; translated={translated}");
            if (translated)
            {
                ConfigureRsxVertexAttributes(
                    packedRsxVertexLayout!.Value);
            }
            else
            {
                ConfigureTexturedVertexAttributes();
            }
            trace?.Invoke("vertex attribute setup completed");
            trace?.Invoke("driver glBindVertexArray restore started; handle=0");
            _gl.BindVertexArray(0);
            trace?.Invoke("driver glBindVertexArray restore returned; handle=0");
            trace?.Invoke(
                $"completed; vao={vao}; vbo={vbo}; ebo={ebo}; " +
                $"indices={packing.Indices.Length}");
            return new GlMesh(
                vao,
                vbo,
                ebo,
                checked((uint)packing.Indices.Length));
        }
        catch (Exception exception)
        {
            trace?.Invoke(
                $"failed; exception={exception.GetType().FullName}; " +
                $"message={QuoteLoadTraceValue(exception.Message)}");
            _gl.BindVertexArray(0);
            DeleteMesh(new GlMesh(vao, vbo, ebo, 0));
            throw;
        }
    }

    private void UploadInstanceTransforms(
        uint instanceBuffer,
        IReadOnlyList<MapRenderStaticModelInstance> instances,
        uint firstAttribute,
        bool configureAttributes = true,
        MapRenderStaticInstanceLightingPayload lightingPayload =
            MapRenderStaticInstanceLightingPayload.None,
        BufferUsageARB usage = BufferUsageARB.StaticDraw,
        Action<string>? trace = null)
    {
        trace?.Invoke(
            $"instance payload packing started; " +
            $"instances={instances.Count}; lighting={lightingPayload}");
        int floatStride = MapRenderStaticInstanceBufferPacker.FloatStride(
            lightingPayload);
        int placementOffset =
            lightingPayload == MapRenderStaticInstanceLightingPayload.None
                ? 0
                : 4;
        float[] transforms = new float[instances.Count * floatStride];
        MapRenderStaticInstanceBufferPacker.PackAll(
            instances,
            lightingPayload,
            transforms);
        trace?.Invoke(
            $"instance payload packing completed; " +
            $"floatStride={floatStride}; floats={transforms.Length}");

        UploadBuffer(
            instanceBuffer,
            transforms,
            usage,
            trace);
        if (!configureAttributes)
            return;
        trace?.Invoke("instance attribute setup started");
        uint instanceStride = checked((uint)floatStride * sizeof(float));
        if (lightingPayload != MapRenderStaticInstanceLightingPayload.None)
        {
            _gl.EnableVertexAttribArray(
                MapRenderOpenGlStaticModelInstancedVertexComposer
                    .LightingPayloadAttribute);
            _gl.VertexAttribPointer(
                MapRenderOpenGlStaticModelInstancedVertexComposer
                    .LightingPayloadAttribute,
                4,
                VertexAttribPointerType.Float,
                false,
                instanceStride,
                (void*)0);
            _gl.VertexAttribDivisor(
                MapRenderOpenGlStaticModelInstancedVertexComposer
                    .LightingPayloadAttribute,
                1);
        }
        for (uint row = 0; row < 3; row++)
        {
            uint attribute = firstAttribute + row;
            _gl.EnableVertexAttribArray(attribute);
            _gl.VertexAttribPointer(
                attribute,
                4,
                VertexAttribPointerType.Float,
                false,
                instanceStride,
                (void*)((placementOffset + row * 4) * sizeof(float)));
            _gl.VertexAttribDivisor(attribute, 1);
        }
        trace?.Invoke("instance attribute setup completed");
    }

    private static void WriteVector4(float[] destination, int offset, Vector4 value)
    {
        destination[offset] = value.X;
        destination[offset + 1] = value.Y;
        destination[offset + 2] = value.Z;
        destination[offset + 3] = value.W;
    }

    internal static int StaticInstanceFloatStride(
        bool executeTranslatedAuthored,
        bool usesGenericStaticModelLighting = false) =>
        MapRenderStaticInstanceBufferPacker.FloatStride(
            executeTranslatedAuthored || usesGenericStaticModelLighting
                ? MapRenderStaticInstanceLightingPayload.BaseLightingCoords
                : MapRenderStaticInstanceLightingPayload.None);

    internal static bool ShouldReceiveGenericMaterialLighting(
        MaterialPassIdentity pass,
        bool usesGenericStaticModelLighting) =>
        usesGenericStaticModelLighting ||
        MapRenderEditorPreviewLightingPlanner
            .ShouldApplyGenericMaterialLighting(pass);

    private void ConfigureTexturedVertexAttributes()
    {
        const uint vertexStride = MapRenderScene.TexturedVertexFloatCount * sizeof(float);
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, vertexStride, (void*)0);
        for (uint layerIndex = 0; layerIndex < MapRenderScene.MaxColorLayerCount; layerIndex++)
        {
            uint attributeIndex = layerIndex + 1;
            int floatOffset = MapRenderScene.TexturedPositionFloatCount + (int)layerIndex * MapRenderScene.TexturedUvFloatCount;
            _gl.EnableVertexAttribArray(attributeIndex);
            _gl.VertexAttribPointer(attributeIndex, 2, VertexAttribPointerType.Float, false, vertexStride, (void*)(floatOffset * sizeof(float)));
        }

        uint attribute = MapRenderScene.MaxColorLayerCount + 1;
        _gl.EnableVertexAttribArray(attribute);
        _gl.VertexAttribPointer(
            attribute,
            4,
            VertexAttribPointerType.Float,
            false,
            vertexStride,
            (void*)(MapRenderScene.TexturedBlendWeightOffset * sizeof(float)));
        attribute++;
        _gl.EnableVertexAttribArray(attribute);
        _gl.VertexAttribPointer(
            attribute,
            2,
            VertexAttribPointerType.Float,
            false,
            vertexStride,
            (void*)(MapRenderScene.TexturedLightmapUvOffset * sizeof(float)));
        attribute++;
        _gl.EnableVertexAttribArray(attribute);
        _gl.VertexAttribPointer(
            attribute,
            3,
            VertexAttribPointerType.Float,
            false,
            vertexStride,
            (void*)(MapRenderScene.TexturedNormalOffset * sizeof(float)));
    }

    private static OpenGlPackedRsxVertexLayout
        ResolvePackedRsxVertexLayout(
            ShaderExecutionContract execution,
            ShaderExecutionContract? depthExecution = null,
            int requiredAttributeMask = 0)
    {
        int attributeMask = requiredAttributeMask |
            OpenGlPackedRsxVertexLayout
                .ResolveAttributeMask(execution);
        if (depthExecution is not null)
        {
            attributeMask |=
                OpenGlPackedRsxVertexLayout
                    .ResolveAttributeMask(depthExecution);
        }

        return new OpenGlPackedRsxVertexLayout(
            attributeMask);
    }

    private void ConfigureRsxVertexAttributes(
        OpenGlPackedRsxVertexLayout layout)
    {
        uint rsxStride = checked(
            (uint)layout.FloatStride * sizeof(float));
        uint packedAttributeIndex = 0;
        for (uint attributeIndex = 0;
             attributeIndex <
                 OpenGlPackedRsxVertexLayout
                     .SourceAttributeCount;
             attributeIndex++)
        {
            if (!layout.ContainsAttribute(
                    checked((int)attributeIndex)))
            {
                continue;
            }

            _gl.EnableVertexAttribArray(attributeIndex);
            _gl.VertexAttribPointer(
                attributeIndex,
                4,
                VertexAttribPointerType.Float,
                false,
                rsxStride,
                (void*)(packedAttributeIndex *
                    OpenGlPackedRsxVertexLayout
                        .AttributeFloatCount *
                    sizeof(float)));
            packedAttributeIndex++;
        }
    }

    private GlTexturedMesh CreateWorldTexturedResourceShell(
        MapRenderTexturedBatch batch,
        IReadOnlySet<AuthoredProgramGroupKey> authorizedAuthoredProgramGroups,
        bool allowGenericFallback = true)
    {
        bool traceEnabled = LoadProgressEnabled;
        if (batch.Vertices.Length == 0 ||
            batch.Indices.Length == 0 ||
            !CanUploadTexture(batch.Texture))
        {
            return default;
        }

        AuthoredProgramPreparation preparation =
            GetOrCreateAuthoredProgramPreparation(
                batch.ShaderExecution,
                batch.State,
                batch.SceneLightIndex,
                usesStaticModelInstancing: false);
        TranslatedProgramDirectCodeConstantPlan? directCodePlan =
            preparation.DirectCodePlan;
        TranslatedProgramVertexConstantBindingPlan? vertexConstantPlan =
            preparation.VertexConstantPlan;
        bool directCodePlanReady = directCodePlan is not null;
        bool vertexConstantPlanReady = vertexConstantPlan is not null;
        GlRsxProgram compiledRsxProgram = preparation.Program;
        bool useRsxVertexInputs =
                                  authorizedAuthoredProgramGroups.Contains(AuthoredProgramGroup(batch)) &&
                                  batch.ShaderExecution.RendererProgramReady &&
                                  directCodePlanReady &&
                                  vertexConstantPlanReady &&
                                  compiledRsxProgram.Handle != 0 &&
                                  batch.ShaderExecution.VertexInputPayloadReady &&
                                  batch.RsxVertexInputs.Length ==
                                  (batch.Vertices.Length / MapRenderScene.TexturedVertexFloatCount) * 16 * 4;
        MapRenderEditorShaderExecutionDecision executionDecision =
            MapRenderEditorShaderExecutionPolicy.Decide(
                new MapRenderEditorShaderExecutionInput(
                    batch.State,
                    AuthoredProgramAvailable(compiledRsxProgram),
                    useRsxVertexInputs,
                    GenericMaterialReady: true));
        if (!executionDecision.IsExecutable)
            return default;

        bool executeTranslatedAuthored =
            executionDecision.Choice ==
            MapRenderEditorShaderExecutionChoice.TranslatedAuthored;
        if (!executeTranslatedAuthored && !allowGenericFallback)
        {
            // Exact page/allocation sidecars are not preview substitutions.
            // If their complete authored group cannot execute, the channel is
            // absent for that surface.
            return default;
        }
        MapRenderGenericMaterialFallbackContract genericMaterialFallback =
            MapRenderGenericMaterialFallbackContract.Create(
                RenderNormalCameraDrawSourceKind.World,
                batch.ShaderExecution,
                batch.ColorLayers);

        var colorTextureList = new List<uint>(
            Math.Min(
                batch.ColorLayers.Count,
                MapRenderScene.MaxColorLayerCount));
        foreach (MaterialColorLayer layer in batch.ColorLayers
                     .Take(MapRenderScene.MaxColorLayerCount))
        {
            if (!CanUploadTexture(layer.Texture))
                continue;
            colorTextureList.Add(CreateTexture(
                layer.Texture,
                loadTraceRole: !traceEnabled
                    ? null
                    : $"world-color-layer[{layer.LayerIndex}]"));
        }
        uint[] colorTextures = colorTextureList.ToArray();
        if (colorTextures.Length == 0)
        {
            colorTextures = [CreateTexture(
                batch.Texture,
                loadTraceRole: "world-primary-fallback")];
        }
        uint lightmapTexture = CanUploadTexture(batch.LightmapTexture)
            ? CreateTexture(
                batch.LightmapTexture!,
                loadTraceRole: "world-lightmap")
            : 0;
        uint[] normalTextures = executeTranslatedAuthored
            ? []
            : CreateEditorRoleTextures(
                batch.MaterialSamplers.Select(binding => binding.Binding).ToArray(),
                [
                    EditorMaterialTextureRole.BaseNormal,
                    EditorMaterialTextureRole.NormalLayer1,
                    EditorMaterialTextureRole.NormalLayer2,
                    EditorMaterialTextureRole.NormalLayer3
                ],
                "world-normal");
        uint[] specularTextures = executeTranslatedAuthored
            ? []
            : CreateEditorRoleTextures(
                batch.MaterialSamplers.Select(binding => binding.Binding).ToArray(),
                [
                    EditorMaterialTextureRole.BaseSpecular,
                    EditorMaterialTextureRole.SpecularLayer1,
                    EditorMaterialTextureRole.SpecularLayer2
                ],
                "world-specular");
        GlRsxProgram rsxProgram = executeTranslatedAuthored
            ? compiledRsxProgram
            : default;
        GlRsxSamplerBinding[] rsxSamplerBindings;
        if (executeTranslatedAuthored)
        {
            var samplerBindings = new List<GlRsxSamplerBinding>();
            foreach (MapRenderWorldMaterialSamplerBinding binding in
                     batch.MaterialSamplers
                         .Where(binding =>
                             CanUploadTexture(binding.Binding.Texture))
                         .GroupBy(binding =>
                             binding.Binding.Identity.SamplerDest)
                         .Select(group => group.First()))
            {
                int destination = binding.Binding.Identity.SamplerDest;
                var samplerTexture = binding.Binding.Texture!;
                samplerBindings.Add(new GlRsxSamplerBinding(
                    destination,
                    CreateTexture(
                        samplerTexture,
                        loadTraceRole: !traceEnabled
                            ? null
                            : $"world-rsx-sampler[{destination}]"),
                    ToGlTextureTarget(samplerTexture.Target)));
            }
            rsxSamplerBindings = samplerBindings.ToArray();
        }
        else
        {
            rsxSamplerBindings = [];
        }
        GlRsxConstantBinding[] rsxConstantBindings = rsxProgram.Handle == 0
            ? []
            : CreateRsxConstantBindings(
                batch.ShaderExecution,
                rsxProgram,
                directCodePlan!,
                vertexConstantPlan!,
                ResolveAuthoredExternallyBoundVertexConstants(
                    vertexConstantPlan!,
                    usesStaticModelInstancing: false));
        GlRsxProgram depthPrepassRsxProgram = default;
        GlRsxConstantBinding[] depthPrepassRsxConstantBindings = [];
        ShaderExecutionContract? depthPrepassExecution = null;
        TranslatedProgramVertexConstantBindingPlan?
            depthPrepassVertexConstantPlan = null;
        if (executeTranslatedAuthored &&
            batch.EditorDepthPrepass is { } depthPrepass &&
            batch.DepthPrepassShaderExecution is
                { ProgramExecutionReady: true } depthExecution)
        {
            AuthoredProgramPreparation depthPreparation =
                GetOrCreateAuthoredProgramPreparation(
                    depthExecution,
                    depthPrepass.State,
                    batch.SceneLightIndex,
                    usesStaticModelInstancing: false);
            if (depthPreparation.Program.Handle != 0)
            {
                if (depthPreparation.DirectCodePlan is not
                        { } depthDirectCodePlan ||
                    depthPreparation.VertexConstantPlan is not
                        { } depthVertexConstantPlan)
                {
                    throw new InvalidOperationException(
                        "A ready authored depth program lost its constant plans.");
                }
                depthPrepassRsxProgram = depthPreparation.Program;
                depthPrepassExecution = depthExecution;
                depthPrepassVertexConstantPlan = depthVertexConstantPlan;
                depthPrepassRsxConstantBindings = CreateRsxConstantBindings(
                    depthExecution,
                    depthPrepassRsxProgram,
                    depthDirectCodePlan,
                    depthVertexConstantPlan,
                    ResolveAuthoredExternallyBoundVertexConstants(
                        depthVertexConstantPlan,
                        usesStaticModelInstancing: false));
            }
        }

        int[] blendWeightComponents = batch.ColorLayers
            .Skip(1)
            .Take(MapRenderScene.MaxColorLayerCount - 1)
            .Select(layer => layer.BlendWeightComponent)
            .ToArray();
        bool hasCertifiedTranslatedDepthFusion =
            executeTranslatedAuthored &&
            depthPrepassRsxProgram.Handle != 0 &&
            depthPrepassExecution is { } certifiedDepthExecution &&
            depthPrepassVertexConstantPlan is { } certifiedDepthConstants &&
            NormalCameraDepthPrepassElisionCertification
                .HasEquivalentTranslatedClipPosition(
                batch.ShaderExecution.RendererProgramReady,
                batch.ShaderExecution.VertexProgramIr,
                batch.ShaderExecution.VertexInputs,
                vertexConstantPlan!,
                certifiedDepthExecution.RendererProgramReady,
                certifiedDepthExecution.VertexProgramIr,
                certifiedDepthExecution.VertexInputs,
                certifiedDepthConstants);
        var resource = new GlTexturedMesh(
            0,
            0,
            0,
            0,
            colorTextures,
            blendWeightComponents,
            lightmapTexture,
            normalTextures,
            specularTextures,
            rsxProgram,
            rsxSamplerBindings,
            rsxConstantBindings,
            checked((uint)batch.Indices.Length),
            0,
            null,
            0f,
            0f,
            MapRenderEditorPreviewLightingPlanner
                .ShouldApplyGenericMaterialLighting(batch.Pass),
            executionDecision.EffectiveState)
        {
            ShaderExecution = executeTranslatedAuthored
                ? batch.ShaderExecution
                : null,
            FragmentProgramControl =
                batch.ShaderExecution.FragmentProgramControl,
            ColorInputLinearizationMask = executeTranslatedAuthored
                ? 0
                : genericMaterialFallback.ColorInputLinearizationMask,
            EditorDepthPrepass = batch.EditorDepthPrepass,
            DepthPrepassRsxProgram = depthPrepassRsxProgram,
            HasCertifiedTranslatedDepthFusion =
                hasCertifiedTranslatedDepthFusion,
            DepthPrepassRsxConstantBindings =
                depthPrepassRsxConstantBindings,
            SceneLightIndex = batch.SceneLightIndex,
            OwnsGeometry = false
        };
        return MapRenderOpenGlWorldResourceShell.RequireGeometryFree(
            resource);
    }

    private sealed class StaticInstanceBufferRuntime
    {
        // Triple buffering lets the CPU prepare two newer render epochs before
        // it reuses storage submitted for the oldest one.
        internal const int UploadBufferRingCapacity = 3;

        private float[]? _compactTransforms;
        private int[]? _currentSourceIndices;
        private int[]? _nextSourceIndices;
        private int[]? _currentLightingEntries;
        private int[]? _nextLightingEntries;
        private uint[]? _uploadBufferRing;
        private int _activeUploadBufferIndex;
        private long _activeUploadEpoch = -1;
        private MapRenderOpenGlStaticReceiverDrawCompactionPlan?
            _receiverDrawCompactionPlan;

        public StaticInstanceBufferRuntime(
            GlTexturedMesh mesh,
            IReadOnlyList<MapRenderStaticModelInstance> instances,
            int lodIndex,
            bool isReceiverVariant = false,
            bool isExactNormalCameraVariant = false)
        {
            ArgumentNullException.ThrowIfNull(instances);
            if (mesh.VertexArray == 0 || mesh.InstanceBuffer == 0)
            {
                throw new ArgumentException(
                    "A static-instance runtime requires a complete VAO and instance buffer.",
                    nameof(mesh));
            }
            if (mesh.InstanceCount != checked((uint)instances.Count))
            {
                throw new ArgumentException(
                    "Static-instance mesh and source counts must match.",
                    nameof(instances));
            }
            if (isReceiverVariant && isExactNormalCameraVariant)
            {
                throw new ArgumentException(
                    "A static-instance runtime cannot own both receiver and normal-camera exact channels.");
            }
            MapRenderStaticInstanceLightingPayload lightingPayload =
                mesh.StaticInstanceLightingPayload;
            if (!Enum.IsDefined(lightingPayload))
                throw new ArgumentOutOfRangeException(nameof(lightingPayload));
            Instances = instances.ToArray();
            var objectIndices = new HashSet<int>();
            for (int index = 0; index < Instances.Length; index++)
                objectIndices.Add(Instances[index].ObjectIndex);
            ObjectIndices = objectIndices.ToArray();
            Array.Sort(ObjectIndices);
            LodIndex = lodIndex;
            IsReceiverVariant = isReceiverVariant;
            IsExactNormalCameraVariant =
                isExactNormalCameraVariant;
            ReceiverSelectionGenerations = isReceiverVariant
                ? new uint[Instances.Length]
                : [];
            LightingPayload = lightingPayload;
            InstanceFloatStride =
                MapRenderStaticInstanceBufferPacker.FloatStride(
                    lightingPayload);
            if (mesh.StaticInstanceFloatStride != InstanceFloatStride)
            {
                throw new ArgumentException(
                    "Static-instance mesh and runtime strides must match.",
                    nameof(mesh));
            }
            OriginalInstanceBuffer = mesh.InstanceBuffer;
            VertexArray = mesh.VertexArray;
            FirstPlacementAttribute = mesh.RsxProgram.Handle == 0
                ? 9u
                : MapRenderOpenGlStaticModelInstancedVertexComposer
                    .FirstPlacementAttribute;
            CurrentSourceCount = Instances.Length;
            VisibleCount = checked((uint)Instances.Length);
        }

        public MapRenderStaticModelInstance[] Instances { get; }
        public int[] ObjectIndices { get; }
        public int LodIndex { get; }
        public bool IsReceiverVariant { get; }
        public bool IsExactNormalCameraVariant { get; }
        public MapRenderStaticInstanceLightingPayload LightingPayload { get; }
        public int InstanceFloatStride { get; }
        public uint OriginalInstanceBuffer { get; }
        public uint VertexArray { get; }
        public uint FirstPlacementAttribute { get; }
        public uint ActiveInstanceBuffer =>
            _uploadBufferRing is null
                ? OriginalInstanceBuffer
                : _uploadBufferRing[_activeUploadBufferIndex];
        public bool HasUploadBufferRing => _uploadBufferRing is not null;
        public nuint InstanceBufferCapacityBytes => checked((nuint)(
            Instances.Length * InstanceFloatStride * sizeof(float)));
        public ReadOnlySpan<uint> AuxiliaryInstanceBuffers =>
            _uploadBufferRing is null
                ? ReadOnlySpan<uint>.Empty
                : _uploadBufferRing.AsSpan(1);
        public uint[] ReceiverSelectionGenerations { get; }
        public float[] CompactTransforms =>
            _compactTransforms ??= new float[
                Instances.Length * InstanceFloatStride];
        public int[] CurrentSourceIndices
        {
            get
            {
                if (_currentSourceIndices is not null)
                    return _currentSourceIndices;

                _currentSourceIndices = new int[Instances.Length];
                for (int index = 0; index < Instances.Length; index++)
                    _currentSourceIndices[index] = index;
                return _currentSourceIndices;
            }
        }
        public int[] NextSourceIndices =>
            _nextSourceIndices ??= new int[Instances.Length];
        public int[] CurrentLightingEntries
        {
            get
            {
                if (_currentLightingEntries is not null)
                    return _currentLightingEntries;

                _currentLightingEntries =
                    new int[Instances.Length];
                Array.Fill(_currentLightingEntries, -1);
                return _currentLightingEntries;
            }
        }
        public int[] NextLightingEntries =>
            _nextLightingEntries ??= new int[Instances.Length];
        public ulong StaticModelLightingAssignmentGeneration
            { get; set; }
        public int CurrentSourceCount { get; set; }
        public uint VisibleCount { get; set; }
        public bool HasWholeBatchDraw { get; set; }
        public bool HasIsolatedDraw { get; set; }
        public bool CanCompact => HasWholeBatchDraw && !HasIsolatedDraw;
        public bool HasCompactedReceiverSourceLayout { get; set; }
        public bool HasCommittedReceiverDrawCompaction { get; set; }
        public bool IsCompactionRescanQueued { get; set; }
        public bool HasLivePlacementChangePending { get; set; }

        public void InstallUploadBufferRing(uint[] buffers)
        {
            ArgumentNullException.ThrowIfNull(buffers);
            if (_uploadBufferRing is not null)
            {
                throw new InvalidOperationException(
                    "The static-instance upload ring is already installed.");
            }
            if (buffers.Length != UploadBufferRingCapacity ||
                buffers[0] != OriginalInstanceBuffer)
            {
                throw new ArgumentException(
                    "The upload ring must retain the original instance buffer in slot zero.",
                    nameof(buffers));
            }
            for (int index = 0; index < buffers.Length; index++)
            {
                if (buffers[index] == 0)
                {
                    throw new ArgumentException(
                        "Upload-ring buffer handles must be nonzero.",
                        nameof(buffers));
                }
                for (int prior = 0; prior < index; prior++)
                {
                    if (buffers[prior] == buffers[index])
                    {
                        throw new ArgumentException(
                            "Upload-ring buffer handles must be unique.",
                            nameof(buffers));
                    }
                }
            }

            _uploadBufferRing = buffers;
            _activeUploadBufferIndex = 0;
            _activeUploadEpoch = -1;
        }

        public uint AcquireUploadBuffer(
            long epoch,
            out bool advanced)
        {
            if (epoch < 0)
                throw new ArgumentOutOfRangeException(nameof(epoch));
            uint[] buffers = _uploadBufferRing ??
                throw new InvalidOperationException(
                    "The static-instance upload ring is not installed.");
            if (epoch < _activeUploadEpoch)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(epoch),
                    epoch,
                    "Static-instance upload epochs must be monotonic.");
            }
            advanced = epoch != _activeUploadEpoch;
            if (advanced)
            {
                _activeUploadBufferIndex =
                    (_activeUploadBufferIndex + 1) % buffers.Length;
                _activeUploadEpoch = epoch;
            }
            return buffers[_activeUploadBufferIndex];
        }

        public void ForgetUploadBufferRing()
        {
            _uploadBufferRing = null;
            _activeUploadBufferIndex = 0;
            _activeUploadEpoch = -1;
        }

        public void ResetDrawShape()
        {
            HasWholeBatchDraw = false;
            HasIsolatedDraw = false;
        }

        public void BeginReceiverDrawCompactionFrame()
        {
            _receiverDrawCompactionPlan?.BeginFrame();
            HasCommittedReceiverDrawCompaction = false;
        }

        public MapRenderOpenGlStaticReceiverDrawCompactionPlan
            GetReceiverDrawCompactionPlan() =>
            _receiverDrawCompactionPlan ??= new(
                Instances.Length);

        public bool TryGetReceiverDrawCompactionPlan(
            out MapRenderOpenGlStaticReceiverDrawCompactionPlan plan)
        {
            plan = _receiverDrawCompactionPlan!;
            return plan is not null;
        }

        public bool IsReceiverInstanceSelected(
            int instanceIndex,
            uint generation)
        {
            if (!IsReceiverVariant || generation == 0)
                return false;
            if ((uint)instanceIndex >=
                (uint)ReceiverSelectionGenerations.Length)
            {
                return false;
            }
            return ReceiverSelectionGenerations[instanceIndex] == generation;
        }

        public void SelectReceiverInstance(
            int instanceIndex,
            uint generation)
        {
            if (!IsReceiverVariant)
            {
                throw new InvalidOperationException(
                    "Only an exact receiver sidecar can select receiver instances.");
            }
            if (generation == 0)
                throw new ArgumentOutOfRangeException(nameof(generation));
            if ((uint)instanceIndex >=
                (uint)ReceiverSelectionGenerations.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(instanceIndex));
            }
            ReceiverSelectionGenerations[instanceIndex] = generation;
        }
    }

}

internal enum MapRenderOpenGlNormalCameraDrawAdapterFailure : byte
{
    None,
    UnsupportedCoverage,
    IsolatedWorldSurfaceActive,
    DynamicStaticLodActive,
    DpvsSourceActive,
    WorldMeshCountMismatch,
    StaticMeshCountMismatch,
    SourceMappingMismatch,
    LegacyParityMismatch
}

/// <summary>
/// Maps the shared semantic normal-camera inventory onto already-created
/// OpenGL meshes. It never creates GPU resources or changes executable mesh
/// state. Activation requires exact parity with the characterized legacy
/// grouping result, so a coverage or mapping gap retains the old route.
/// </summary>
internal sealed class MapRenderOpenGlNormalCameraDrawAdapter
{
    private readonly RenderNormalCameraDrawSnapshot _source;
    private readonly MapRenderEditorDrawGroup<GlTexturedDrawCommand>[]
        _groups;
    private readonly Dictionary<MapRenderEditorDrawGroup<
        RenderNormalCameraDrawSubmissionSnapshot>,
        MapRenderEditorDrawGroup<GlTexturedDrawCommand>> _groupsBySource;

    private MapRenderOpenGlNormalCameraDrawAdapter(
        RenderNormalCameraDrawSnapshot source,
        IReadOnlyList<GlTexturedMesh> worldMeshes,
        IReadOnlyList<GlTexturedMesh> staticMeshes,
        IReadOnlyList<MapRenderEditorDrawGroup<GlTexturedDrawCommand>>
            legacyGroups)
    {
        if (legacyGroups.Count != source.DrawGroups.Length)
        {
            throw new InvalidDataException(
                "Semantic and legacy draw-group counts do not match.");
        }
        _source = source;
        _groups = new MapRenderEditorDrawGroup<
            GlTexturedDrawCommand>[source.DrawGroups.Length];
        _groupsBySource = new Dictionary<MapRenderEditorDrawGroup<
            RenderNormalCameraDrawSubmissionSnapshot>,
            MapRenderEditorDrawGroup<GlTexturedDrawCommand>>(
                ReferenceEqualityComparer.Instance);

        for (int groupIndex = 0;
             groupIndex < source.DrawGroups.Length;
             groupIndex++)
        {
            MapRenderEditorDrawGroup<
                RenderNormalCameraDrawSubmissionSnapshot> semanticGroup =
                    source.DrawGroups[groupIndex];
            GlTexturedDrawCommand[] commands = semanticGroup.AuthoredPasses
                .Select(draw => MapDraw(
                    source,
                    draw,
                    worldMeshes,
                    staticMeshes))
                .ToArray();
            long? sortKey =
                legacyGroups[groupIndex].CameraIndependentSortKey;
            MapRenderEditorDrawGroup<GlTexturedDrawCommand> mapped =
                semanticGroup.SortCenter is { } center
                    ? MapRenderEditorDrawGroup<GlTexturedDrawCommand>
                        .FromCenter(
                            semanticGroup.SourceOrdinal,
                            semanticGroup.Classification,
                            commands,
                            center,
                            sortKey)
                    : MapRenderEditorDrawGroup<GlTexturedDrawCommand>
                        .FromExplicitDepth(
                            semanticGroup.SourceOrdinal,
                            semanticGroup.Classification,
                            commands,
                            semanticGroup.ExplicitDepth ?? throw new
                                InvalidDataException(
                                    "A semantic draw group has no sort position."),
                            sortKey);
            _groups[groupIndex] = mapped;
            _groupsBySource.Add(semanticGroup, mapped);
        }
    }

    internal MapRenderEditorDrawGroup<GlTexturedDrawCommand>[] SourceGroups =>
        _groups;

    internal static bool TryCreate(
        RenderNormalCameraDrawSnapshot source,
        IReadOnlyList<GlTexturedMesh> worldMeshes,
        IReadOnlyList<GlTexturedMesh> staticMeshes,
        IReadOnlyList<MapRenderEditorDrawGroup<GlTexturedDrawCommand>>
            legacyGroups,
        bool isolatedWorldSurfaceActive,
        bool dynamicStaticLodActive,
        bool dpvsSourceActive,
        out MapRenderOpenGlNormalCameraDrawAdapter? adapter,
        out MapRenderOpenGlNormalCameraDrawAdapterFailure failure)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(worldMeshes);
        ArgumentNullException.ThrowIfNull(staticMeshes);
        ArgumentNullException.ThrowIfNull(legacyGroups);
        adapter = null;

        failure = ValidateActivation(
            source,
            worldMeshes.Count,
            staticMeshes.Count,
            isolatedWorldSurfaceActive,
            dynamicStaticLodActive,
            dpvsSourceActive);
        if (failure != MapRenderOpenGlNormalCameraDrawAdapterFailure.None)
            return false;

        try
        {
            var candidate = new MapRenderOpenGlNormalCameraDrawAdapter(
                source,
                worldMeshes,
                staticMeshes,
                legacyGroups);
            if (!HasLegacyParity(candidate.SourceGroups, legacyGroups))
            {
                failure = MapRenderOpenGlNormalCameraDrawAdapterFailure
                    .LegacyParityMismatch;
                return false;
            }

            adapter = candidate;
            failure = MapRenderOpenGlNormalCameraDrawAdapterFailure.None;
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            InvalidDataException or
            InvalidOperationException or
            KeyNotFoundException or
            OverflowException)
        {
            failure = MapRenderOpenGlNormalCameraDrawAdapterFailure
                .SourceMappingMismatch;
            return false;
        }
    }

    /// <summary>
    /// Maps a shared frame plan through the existing OpenGL queue ordering as
    /// a parity oracle. Production keeps calling SortImmutableFrame directly
    /// so moving-camera frames do not allocate semantic plan snapshots.
    /// </summary>
    internal IReadOnlyList<MapRenderEditorDrawGroup<GlTexturedDrawCommand>>
        CreateParityFrame(RenderNormalCameraDrawFramePlan semanticFrame)
    {
        ArgumentNullException.ThrowIfNull(semanticFrame);
        if (!ReferenceEquals(semanticFrame.Source, _source) ||
            semanticFrame.OrderedGroups.Length != _groups.Length)
        {
            throw new ArgumentException(
                "The semantic frame does not belong to this OpenGL adapter.",
                nameof(semanticFrame));
        }

        IReadOnlyList<MapRenderEditorDrawGroup<GlTexturedDrawCommand>> ordered =
            MapRenderEditorDrawQueueSorter.SortImmutableFrame(
                _groups,
                semanticFrame.CameraPosition,
                semanticFrame.CameraForward);

        // OpenGL may reorder opaque/cutout groups by its exact backend color
        // compatibility key. Translucent ordering has no backend key and must
        // exactly match the shared camera plan.
        int semanticTranslucentIndex = 0;
        while (semanticTranslucentIndex <
                   semanticFrame.OrderedGroups.Length &&
               semanticFrame.OrderedGroups[semanticTranslucentIndex].Bucket !=
                   MapRenderEditorDrawBucket.Translucent)
        {
            semanticTranslucentIndex++;
        }
        for (int index = 0; index < ordered.Count; index++)
        {
            if (ordered[index].Bucket !=
                MapRenderEditorDrawBucket.Translucent)
            {
                continue;
            }
            if (semanticTranslucentIndex >=
                    semanticFrame.OrderedGroups.Length ||
                !_groupsBySource.TryGetValue(
                    semanticFrame.OrderedGroups[semanticTranslucentIndex],
                    out MapRenderEditorDrawGroup<
                        GlTexturedDrawCommand>? mapped) ||
                !ReferenceEquals(mapped, ordered[index]))
            {
                throw new InvalidDataException(
                    "OpenGL translucent ordering diverged from the shared camera plan.");
            }
            semanticTranslucentIndex++;
        }
        if (semanticTranslucentIndex !=
            semanticFrame.OrderedGroups.Length)
        {
            throw new InvalidDataException(
                "The shared camera plan contains unmapped translucent groups.");
        }
        return ordered;
    }

    internal static MapRenderOpenGlNormalCameraDrawAdapterFailure
        ValidateActivation(
            RenderNormalCameraDrawSnapshot source,
            int worldMeshCount,
            int staticMeshCount,
            bool isolatedWorldSurfaceActive,
            bool dynamicStaticLodActive,
            bool dpvsSourceActive)
    {
        ArgumentNullException.ThrowIfNull(source);
        bool usesAllStaticLods = source.Coverage ==
            RenderNormalCameraDrawCoverage
                .PreparedWorldAndAllStaticLodBatchesWithoutDpvsSelection;
        if (source.Coverage is not
                RenderNormalCameraDrawCoverage
                    .PreparedWorldAndCurrentStaticBatchesWithoutDynamicLodOrDpvs &&
            !usesAllStaticLods)
        {
            return MapRenderOpenGlNormalCameraDrawAdapterFailure
                .UnsupportedCoverage;
        }
        if (isolatedWorldSurfaceActive)
        {
            return MapRenderOpenGlNormalCameraDrawAdapterFailure
                .IsolatedWorldSurfaceActive;
        }
        if (dynamicStaticLodActive != usesAllStaticLods)
        {
            return MapRenderOpenGlNormalCameraDrawAdapterFailure
                .DynamicStaticLodActive;
        }
        // DPVS is an existing OpenGL post-plan visibility filter. The mapped
        // world command retains its exact collection ordinal as
        // WorldBatchIndex, so enabling DPVS does not change shared inventory,
        // grouping, or authored pass ownership.
        _ = dpvsSourceActive;
        if (worldMeshCount != source.WorldSourceCount)
        {
            return MapRenderOpenGlNormalCameraDrawAdapterFailure
                .WorldMeshCountMismatch;
        }
        if (staticMeshCount != source.StaticSourceCount)
        {
            return MapRenderOpenGlNormalCameraDrawAdapterFailure
                .StaticMeshCountMismatch;
        }
        return MapRenderOpenGlNormalCameraDrawAdapterFailure.None;
    }

    private static GlTexturedDrawCommand MapDraw(
        RenderNormalCameraDrawSnapshot source,
        RenderNormalCameraDrawSubmissionSnapshot draw,
        IReadOnlyList<GlTexturedMesh> worldMeshes,
        IReadOnlyList<GlTexturedMesh> staticMeshes)
    {
        RenderNormalCameraPreparedPassSnapshot pass = draw.PreparedPass;
        pass.ValidateResources(source.Resources);
        GlTexturedMesh mesh;
        GlTexturedDrawCommand command;
        if (pass.SourceKind == RenderNormalCameraDrawSourceKind.World)
        {
            if ((uint)pass.CollectionOrdinal >= (uint)worldMeshes.Count)
                throw new InvalidDataException("World mesh ownership is missing.");
            mesh = worldMeshes[pass.CollectionOrdinal];
            command = new GlTexturedDrawCommand(
                mesh,
                InstanceIndex: null,
                WorldBatchIndex: pass.CollectionOrdinal);
            if (mesh.InstanceCount != 0)
            {
                throw new InvalidDataException(
                    "A semantic world pass mapped to an instanced OpenGL mesh.");
            }
        }
        else
        {
            if ((uint)pass.CollectionOrdinal >= (uint)staticMeshes.Count)
                throw new InvalidDataException("Static mesh ownership is missing.");
            mesh = staticMeshes[pass.CollectionOrdinal];
            command = new GlTexturedDrawCommand(
                mesh,
                draw.StaticInstanceIndex);
            if (mesh.InstanceCount != pass.StaticInstances.Length ||
                mesh.StaticModelLodIndex != pass.LodIndex ||
                mesh.StaticCameraRegion != pass.StaticCameraRegion)
            {
                throw new InvalidDataException(
                    "Static mesh instance, LOD, or camera-region provenance diverged from the semantic pass.");
            }
        }

        if (mesh.IndexCount == 0 ||
            mesh.IndexCount != draw.Range.IndexCount ||
            mesh.FragmentProgramControl !=
                pass.ShaderProvenance.FragmentProgramControl ||
            mesh.EditorDepthPrepass != pass.DepthPrepass)
        {
            throw new InvalidDataException(
                "OpenGL geometry or fixed pass provenance diverged from the semantic draw.");
        }
        if (mesh.ShaderExecution is { } execution &&
            (!string.Equals(
                execution.ProgramCacheKey,
                pass.ShaderProvenance.ProgramCacheKey,
                StringComparison.Ordinal) ||
             execution.VertexProgram != pass.ShaderProvenance.VertexProgram ||
             execution.PixelProgram != pass.ShaderProvenance.PixelProgram))
        {
            throw new InvalidDataException(
                "OpenGL translated shader provenance diverged from the semantic pass.");
        }
        return command;
    }

    private static bool HasLegacyParity(
        IReadOnlyList<MapRenderEditorDrawGroup<GlTexturedDrawCommand>> mapped,
        IReadOnlyList<MapRenderEditorDrawGroup<GlTexturedDrawCommand>> legacy)
    {
        if (mapped.Count != legacy.Count)
            return false;
        for (int groupIndex = 0; groupIndex < mapped.Count; groupIndex++)
        {
            MapRenderEditorDrawGroup<GlTexturedDrawCommand> left =
                mapped[groupIndex];
            MapRenderEditorDrawGroup<GlTexturedDrawCommand> right =
                legacy[groupIndex];
            if (left.SourceOrdinal != right.SourceOrdinal ||
                left.Bucket != right.Bucket ||
                left.SortCenter != right.SortCenter ||
                left.ExplicitDepth != right.ExplicitDepth ||
                left.CameraIndependentSortKey !=
                    right.CameraIndependentSortKey ||
                left.Classification.UsesOpaqueStateFallback !=
                    right.Classification.UsesOpaqueStateFallback ||
                !left.AuthoredPasses.SequenceEqual(right.AuthoredPasses))
            {
                return false;
            }
        }
        return true;
    }
}
