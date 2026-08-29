using IW4.Render.Techniques;
using Silk.NET.OpenGL;

using IW4.Render.Execution;
using IW4.Render.Geometry;
using IW4.Render.EditorPreview;
using IW4.Render.Materials;
using IW4.Render.Lighting;
using IW4.Render.SceneBuilding;
using IW4.Render.Shaders;
using IW4.Render.Textures;
using Texture = IW4.Render.Textures.Texture;
using TextureTarget = Silk.NET.OpenGL.TextureTarget;
using RenderTextureTarget = IW4.Render.Textures.TextureTarget;
using IW4.Render.OpenGl.Programs;
using IW4.Render.OpenGl.Shaders;

namespace IW4.Render.OpenGl;

public sealed unsafe partial class SilkOpenGlMapRenderer
{
    private bool _deferNewAuthoredProgramLinkCompletion;
    private readonly Dictionary<
        OpenGlProgramKey,
        MapRenderOpenGlStaticModelProgramUniforms>
        _staticModelProgramUniforms = [];

    private uint[] CreateEditorRoleTextures(
        IReadOnlyList<MaterialSamplerBinding> bindings,
        IReadOnlyList<EditorMaterialTextureRole> roles,
        string? loadTraceRolePrefix = null)
    {
        var result = new uint[roles.Count];
        for (int roleIndex = 0; roleIndex < roles.Count; roleIndex++)
        {
            if (SelectUniqueEditorRoleTexture(
                    bindings,
                    roles[roleIndex]) is { } texture &&
                CanUploadTexture(texture))
            {
                result[roleIndex] = CreateTexture(
                    texture,
                    loadTraceRole: LoadProgressEnabled
                        ? $"{loadTraceRolePrefix ?? "editor-role"}:" +
                          roles[roleIndex]
                        : null);
            }
        }

        return result;
    }

    internal static Texture? SelectUniqueEditorRoleTexture(
        IReadOnlyList<MaterialSamplerBinding> bindings,
        EditorMaterialTextureRole role)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        if (!Enum.IsDefined(role) ||
            role == EditorMaterialTextureRole.Unknown)
        {
            return null;
        }

        MaterialSamplerBinding[] candidates = bindings
            .Where(binding => binding.EditorTextureRole == role)
            .Take(2)
            .ToArray();
        return candidates.Length == 1
            ? candidates[0].Texture
            : null;
    }

    internal static bool IncludesAuthoredProgramCandidate(
        bool hasAuthoredTechniquePass) =>
        hasAuthoredTechniquePass;

    internal static bool AuthoredProgramAvailable(
        GlRsxProgram program) =>
        program.Handle != 0;

    private static bool HasAuthoredTechniquePass(MaterialPassIdentity pass) =>
        pass.TechniquePass.TechniqueSlot >= 0 &&
        pass.TechniquePass.PassIndex >= 0;

    internal static IReadOnlySet<TKey> AuthorizeAtomicProgramGroups<T, TKey>(
        IReadOnlyList<T> batches,
        Func<T, bool> requiresAuthoredProgramExecution,
        Func<T, TKey> groupKey,
        Func<T, bool> programReady)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(batches);
        ArgumentNullException.ThrowIfNull(requiresAuthoredProgramExecution);
        ArgumentNullException.ThrowIfNull(groupKey);
        ArgumentNullException.ThrowIfNull(programReady);

        var authorized = new HashSet<TKey>();
        foreach (IGrouping<TKey, T> group in batches
                     .Where(requiresAuthoredProgramExecution)
                     .GroupBy(groupKey))
        {
            bool allProgramsReady = true;
            foreach (T batch in group)
            {
                // Do not short-circuit: compile/preflight every authored pass so
                // the group decision is based on the complete program sequence.
                bool thisProgramReady = programReady(batch);
                allProgramsReady &= thisProgramReady;
            }

            if (allProgramsReady)
                authorized.Add(group.Key);
        }

        return authorized;
    }

    private bool PreflightAuthoredProgram(MapRenderTexturedBatch batch)
    {
        // Same-revision runtime samplers remain frame-owned. Immutable
        // source-13 scene-light images are preflighted below so an unavailable
        // canonical LightDef never replaces this group’s generic fallback.
        if (!batch.ShaderExecution.RendererProgramReady ||
            !batch.ShaderExecution.VertexInputPayloadReady ||
            batch.RsxVertexInputs.Length !=
            (batch.Vertices.Length / MapRenderScene.TexturedVertexFloatCount) * 16 * 4)
        {
            return false;
        }
        if (!HasRequiredImmutableSceneSamplers(
                batch.ShaderExecution,
                batch.SceneLightIndex))
        {
            return false;
        }

        if (!TryCreateEditorDirectCodeConstantPlan(
                batch.ShaderExecution,
                batch.SceneLightIndex,
                out TranslatedProgramDirectCodeConstantPlan?
                    directCodePlan) ||
            !TryCreateEditorVertexConstantBindingPlan(
                batch.ShaderExecution,
                directCodePlan!,
                out TranslatedProgramVertexConstantBindingPlan?
                    vertexPlan) ||
            vertexPlan!.Bindings.Any(binding => binding.Kind is
                TranslatedProgramVertexConstantBindingKind
                    .PerInstanceStaticModelBaseLightingCoords or
                TranslatedProgramVertexConstantBindingKind
                    .PerInstanceStaticModelLightProbeAmbient))
        {
            return false;
        }

        return GetOrCreateRsxProgram(
            batch.ShaderExecution,
            batch.State,
            vertexPlan!,
            usesStaticModelInstancing: false,
            out _).Handle != 0;
    }

    private bool PreflightBaseWorldAuthoredProgram(
        MapRenderTexturedBatch batch)
    {
        if (!LoadProgressEnabled)
            return PreflightAuthoredProgram(batch);

        long traceSequence = NextLoadBatchTraceSequence();
        using var context = BeginLoadTraceContext(
            $"base-world-preflight={traceSequence}; " +
            DescribeWorldBatchTraceContext(batch));
        bool reportProgress =
            traceSequence == 1 ||
            traceSequence % BaseWorldPreflightProgressInterval == 0;
        if (reportProgress)
        {
            ReportLoadDetail(
                "authored-preflight progress checkpoint started");
        }
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            bool ready = PreflightAuthoredProgram(batch);
            double elapsedMilliseconds =
                System.Diagnostics.Stopwatch
                    .GetElapsedTime(started)
                    .TotalMilliseconds;
            if (reportProgress || elapsedMilliseconds >= 250d)
            {
                ReportLoadDetail(
                    $"authored preflight completed; " +
                    $"slow={elapsedMilliseconds >= 250d}; ready={ready}; " +
                    $"programs={_authoredMaterials.ProgramCount}; " +
                    $"failures={_authoredMaterials.FailureCount}; " +
                    $"elapsed={elapsedMilliseconds:0}ms");
            }
            return ready;
        }
        catch (Exception exception)
        {
            ReportLoadDetail(
                $"authored preflight failed; " +
                $"exception={exception.GetType().FullName}; " +
                $"message={QuoteLoadTraceValue(exception.Message)}; " +
                $"elapsed={System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:0}ms");
            throw;
        }
    }

    private bool PreflightAuthoredProgram(
        MapRenderInstancedTexturedBatch batch)
    {
        int vertexCount = batch.Vertices.Length /
            MapRenderScene.TexturedVertexFloatCount;
        if (!batch.ShaderExecution.RendererProgramReady ||
            !batch.ShaderExecution.VertexInputPayloadReady ||
            batch.RsxVertexInputs.Length != vertexCount * 16 * 4)
        {
            return false;
        }
        if (!HasRequiredImmutableSceneSamplers(
                batch.ShaderExecution,
                batch.SceneLightIndex))
        {
            return false;
        }

        if (!TryCreateEditorDirectCodeConstantPlan(
                batch.ShaderExecution,
                batch.SceneLightIndex,
                out TranslatedProgramDirectCodeConstantPlan?
                    directCodePlan) ||
            !TryCreateEditorVertexConstantBindingPlan(
                batch.ShaderExecution,
                directCodePlan!,
                out TranslatedProgramVertexConstantBindingPlan?
                    vertexPlan))
        {
            return false;
        }

        return GetOrCreateRsxProgram(
            batch.ShaderExecution,
            batch.State,
            vertexPlan!,
            usesStaticModelInstancing: true,
            out _).Handle != 0;
    }

    /// <summary>
    /// Issues every currently consumable authored map link before any one
    /// source link is completed. LinkStatus is a driver synchronization
    /// point; submitting the complete exact-source set first allows capable
    /// drivers to compile/link concurrently while the normal resource gates
    /// retain their existing blocking validation and multipass atomicity.
    /// Warm program-binary hits remain immediately ready and source failures
    /// still fall back through the established group policies.
    /// </summary>
    private void SubmitSceneAuthoredProgramLinks(MapRenderScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (_deferNewAuthoredProgramLinkCompletion)
        {
            throw new InvalidOperationException(
                "Authored OpenGL link submission cannot be nested.");
        }

        IEnumerable<MapRenderTexturedBatch> worldBatches =
            scene.TexturedBatches;
        IEnumerable<MapRenderInstancedTexturedBatch> staticBatches =
            scene.InstancedTexturedBatches
                .Concat(scene.StaticModelLodTexturedBatches)
                .Concat(
                    scene.ExactNormalCameraStaticModelTexturedBatches)
                .Concat(
                    scene.ShadowAllocatedStaticModelTexturedBatches);
        if (scene.ReceiverVariants is { } receiverVariants)
        {
            worldBatches = worldBatches.Concat(
                receiverVariants.World.Values.SelectMany(
                    batches => batches));
            staticBatches = staticBatches.Concat(
                receiverVariants.StaticModels.Values.SelectMany(
                    batches => batches));
        }
        else
        {
            worldBatches = worldBatches.Concat(
                scene.ShadowAllocatedWorldTexturedBatches);
        }

        _deferNewAuthoredProgramLinkCompletion = true;
        try
        {
            foreach (MapRenderTexturedBatch batch in worldBatches)
            {
                PreflightAuthoredProgram(batch);
                SubmitDepthPrepassProgram(batch);
            }
            foreach (MapRenderInstancedTexturedBatch batch in staticBatches)
            {
                PreflightAuthoredProgram(batch);
                SubmitDepthPrepassProgram(batch);
            }
        }
        finally
        {
            _authoredMaterials.ResumePendingProgramResolution();
            _deferNewAuthoredProgramLinkCompletion = false;
        }
    }

    private void SubmitDepthPrepassProgram(MapRenderTexturedBatch batch)
    {
        if (batch.EditorDepthPrepass is not { } depthPrepass ||
            batch.DepthPrepassShaderExecution is not
                { ProgramExecutionReady: true } execution ||
            !execution.VertexInputPayloadReady ||
            batch.RsxVertexInputs.Length !=
            (batch.Vertices.Length /
                MapRenderScene.TexturedVertexFloatCount) * 16 * 4 ||
            !HasRequiredImmutableSceneSamplers(
                execution,
                batch.SceneLightIndex) ||
            !TryCreateEditorDirectCodeConstantPlan(
                execution,
                batch.SceneLightIndex,
                out TranslatedProgramDirectCodeConstantPlan? directPlan) ||
            !TryCreateEditorVertexConstantBindingPlan(
                execution,
                directPlan!,
                out TranslatedProgramVertexConstantBindingPlan? vertexPlan))
        {
            return;
        }

        GetOrCreateRsxProgram(
            execution,
            depthPrepass.State,
            vertexPlan!,
            usesStaticModelInstancing: false,
            out _);
    }

    private void SubmitDepthPrepassProgram(
        MapRenderInstancedTexturedBatch batch)
    {
        if (batch.EditorDepthPrepass is not { } depthPrepass ||
            batch.DepthPrepassShaderExecution is not
                { ProgramExecutionReady: true } execution ||
            !execution.VertexInputPayloadReady ||
            batch.RsxVertexInputs.Length !=
            (batch.Vertices.Length /
                MapRenderScene.TexturedVertexFloatCount) * 16 * 4 ||
            !HasRequiredImmutableSceneSamplers(
                execution,
                batch.SceneLightIndex) ||
            !TryCreateEditorDirectCodeConstantPlan(
                execution,
                batch.SceneLightIndex,
                out TranslatedProgramDirectCodeConstantPlan? directPlan) ||
            !TryCreateEditorVertexConstantBindingPlan(
                execution,
                directPlan!,
                out TranslatedProgramVertexConstantBindingPlan? vertexPlan))
        {
            return;
        }

        GetOrCreateRsxProgram(
            execution,
            depthPrepass.State,
            vertexPlan!,
            usesStaticModelInstancing: true,
            out _);
    }

    private bool HasRequiredImmutableSceneSamplers(
        ShaderExecutionContract execution,
        int sceneLightIndex)
    {
        ArgumentNullException.ThrowIfNull(execution);
        foreach (ShaderRuntimeSamplerRequirement requirement in
                 execution.RuntimeSamplerRequirements)
        {
            if (requirement.ResourceKind !=
                ShaderRuntimeSamplerResourceKind.LightAttenuation)
            {
                continue;
            }
            if (requirement.Status !=
                    ShaderRuntimeSamplerRequirementStatus
                        .ImmutableSceneTextureRequired ||
                !TryGetSceneLightAttenuationTextureHandle(
                    sceneLightIndex,
                    out _))
            {
                return false;
            }
        }

        return true;
    }

    private bool TryGetSceneLightAttenuationTextureHandle(
        int sceneLightIndex,
        out uint textureHandle)
    {
        if (_editorPreviewSceneLightFrame is { } lightFrame &&
            _previewWorldSource is { } source &&
            source.AssetLookup.HasCanonicalAssetPoolRevision(
                lightFrame.AssetPoolRevision) &&
            (uint)sceneLightIndex < (uint)lightFrame.SceneLightCount &&
            (uint)sceneLightIndex <
                (uint)_sceneLightAttenuationTextureHandles.Length &&
            lightFrame.GetSceneLight(sceneLightIndex).Type is
                IW4.Assets.Assets.ComWorld.GfxLightType.Spot or
                IW4.Assets.Assets.ComWorld.GfxLightType.Omni &&
            _sceneLightAttenuationTextureHandles[sceneLightIndex] != 0)
        {
            textureHandle =
                _sceneLightAttenuationTextureHandles[sceneLightIndex];
            return true;
        }

        textureHandle = 0;
        return false;
    }

    private bool TryCreateEditorDirectCodeConstantPlan(
        ShaderExecutionContract execution,
        byte sceneLightIndex,
        out TranslatedProgramDirectCodeConstantPlan? plan)
    {
        plan = null;
        TranslatedProgramDirectCodeConstantPlanBuildResult
            result =
                TranslatedProgramDirectCodeConstantPlanner
                    .TryPlan(
                        execution.ConstantDestinations,
                        execution.CodePixelConstantPatchPlans,
                        _editorPreviewFogRenderingEnabled,
                        _editorPreviewActiveFog,
                        _editorPreviewLighting,
                        // No retained world/static draw in this renderer sets
                        // packed draw-group bit 16. Passing the vision
                        // strengths here would silently turn every fallback
                        // directional invocation into hero lighting.
                        primaryLight:
                            MapRenderEditorPreviewPrimaryLightInvocationPolicy
                                .Resolve(
                                    _editorPreviewVision?.PrimaryLight,
                                    useHeroLighting: false),
                        sceneLightIndex: sceneLightIndex,
                        sceneLightFrame: _editorPreviewSceneLightFrame);
        plan = result.Plan;
        return result.IsReady;
    }

    private static bool TryCreateEditorVertexConstantBindingPlan(
        ShaderExecutionContract execution,
        TranslatedProgramDirectCodeConstantPlan directCodePlan,
        out TranslatedProgramVertexConstantBindingPlan? plan)
    {
        TranslatedProgramVertexConstantBindingPlanBuildResult
            result =
                TranslatedProgramVertexConstantBindingPlanner
                    .TryPlan(
                        execution.ProgramVertexConstantDestinations,
                        execution.ConstantDestinations,
                        execution.EmbeddedVertexConstants,
                        directCodePlan);
        plan = result.Plan;
        return result.IsReady;
    }

    private GlRsxConstantBinding[] CreateRsxConstantBindings(
        ShaderExecutionContract execution,
        GlRsxProgram program,
        TranslatedProgramDirectCodeConstantPlan directCodePlan,
        TranslatedProgramVertexConstantBindingPlan
            vertexConstantPlan,
        IReadOnlySet<int>? externallyBoundVertexConstantDestinations = null)
    {
        if (_authoredMaterials.TryCreateConstantBindings(
                execution,
                program,
                directCodePlan,
                vertexConstantPlan,
                out GlRsxConstantBinding[] bindings,
                out string? blocker,
                externallyBoundVertexConstantDestinations:
                    externallyBoundVertexConstantDestinations))
        {
            return bindings;
        }

        throw new InvalidOperationException(
            blocker ?? "Authored RSX constant binding failed.");
    }

    private static IReadOnlySet<int> ResolveAuthoredExternallyBoundVertexConstants(
        TranslatedProgramVertexConstantBindingPlan plan,
        bool usesStaticModelInstancing)
    {
        var result = new HashSet<int>(
            MapRenderOpenGlFrameVertexConstantComposer
                .ResolveExternallyBoundDestinations(
                    plan,
                    usesStaticModelInstancing));
        if (usesStaticModelInstancing)
        {
            result.UnionWith(
                MapRenderOpenGlStaticModelInstancedVertexComposer
                    .ResolveExternallyBoundVertexConstantDestinations(plan));
        }
        return result;
    }

    private static AuthoredProgramGroupKey AuthoredProgramGroup(MapRenderTexturedBatch batch) =>
        new(
            batch.Pass.MaterialName,
            batch.Pass.TechniquePass.TechniqueSetName,
            batch.Pass.TechniquePass.TechniqueSlot,
            batch.Pass.TechniquePass.TechniqueName,
            batch.SceneLightIndex);

    private static AuthoredProgramGroupKey AuthoredProgramGroup(
        MapRenderInstancedTexturedBatch batch) =>
        new(
            batch.Pass.MaterialName,
            batch.Pass.TechniquePass.TechniqueSetName,
            batch.Pass.TechniquePass.TechniqueSlot,
            batch.Pass.TechniquePass.TechniqueName,
            batch.SceneLightIndex);

    private GlRsxProgram GetOrCreateRsxProgram(
        ShaderExecutionContract execution,
        RenderState state,
        TranslatedProgramVertexConstantBindingPlan vertexConstantPlan,
        bool usesStaticModelInstancing,
        out MapRenderOpenGlStaticModelProgramUniforms?
            staticModelUniforms)
    {
        Action<string>? trace =
            CreateLoadDetailReporter("authored program");
        staticModelUniforms = null;
        if (!_authoredMaterials.TryResolveRawProgramSources(
                execution,
                state,
                out string vertexGlsl,
                out OpenGlAuthoredFragmentSource fragmentSource,
                out string? blocker))
        {
            trace?.Invoke(
                $"preparation blocked; step=raw-source-resolution; " +
                $"executionKey={QuoteLoadTraceValue(execution.ProgramCacheKey)}; " +
                $"reason={QuoteLoadTraceValue(blocker)}");
            return default;
        }

        if (!OpenGlFixedFunctionEpilogue.TryCompose(
                state,
                execution.FragmentProgramControl,
                suppressShaderPackerForDiagnosticOutput:
                    UseRsxVertexPlacementDiagnostic,
                hostColorOutputIndex:
                    RsxFragmentOutputDiagnostic ?? 0,
                out AlphaTestMode alphaTestMode,
                out OpenGlRsxShaderPackerMode shaderPackerMode,
                out string fixedFunctionEpilogue))
        {
            trace?.Invoke(
                $"preparation blocked; step=fixed-function-composition; " +
                $"executionKey={QuoteLoadTraceValue(execution.ProgramCacheKey)}");
            return default;
        }

        if (!MapRenderOpenGlFrameVertexConstantComposer.TryCompose(
                vertexGlsl,
                vertexConstantPlan,
                usesStaticModelInstancing,
                out vertexGlsl,
                out string frameConstantIdentity,
                out string frameConstantBlocker))
        {
            _authoredMaterials.RecordPreparationFailure(
                $"{execution.ProgramCacheKey}|mapFrameVertexConstants",
                frameConstantBlocker);
            trace?.Invoke(
                $"preparation blocked; step=frame-constant-composition; " +
                $"executionKey={QuoteLoadTraceValue(execution.ProgramCacheKey)}; " +
                $"reason={QuoteLoadTraceValue(frameConstantBlocker)}");
            return default;
        }

        bool usesFrameConstants = frameConstantIdentity != "none";
        bool compositionReady = false;
        string compositionIdentity = string.Empty;
        if (usesStaticModelInstancing)
        {
            if (!MapRenderOpenGlStaticModelInstancedVertexComposer.TryCompose(
                    vertexGlsl,
                    execution.VertexInputs,
                    vertexConstantPlan,
                    out vertexGlsl,
                    out compositionIdentity,
                    out string compositionBlocker))
            {
                _authoredMaterials.RecordPreparationFailure(
                    $"{execution.ProgramCacheKey}|staticModelInstancing",
                    compositionBlocker);
                trace?.Invoke(
                    $"preparation blocked; step=static-instance-composition; " +
                    $"executionKey={QuoteLoadTraceValue(execution.ProgramCacheKey)}; " +
                    $"reason={QuoteLoadTraceValue(compositionBlocker)}");
                return default;
            }
            compositionReady = true;
        }

        string fixedFunctionIdentity =
            $"{execution.ProgramCacheKey}|alphaTest={alphaTestMode}" +
            $"|rsxShaderPacker={shaderPackerMode}" +
            (usesFrameConstants
                ? $"|mapFrameVertexConstants={frameConstantIdentity}"
                : string.Empty) +
            (compositionReady
                ? $"|staticModelInstancing={compositionIdentity}"
                : string.Empty);
        string diagnosticIdentity = UseRsxVertexPlacementDiagnostic
            ? $"VERTEX_PLACEMENT_DIAGNOSTIC|{fixedFunctionIdentity}"
            : RsxFragmentOutputDiagnostic.HasValue
                ? $"FRAGMENT_OUTPUT_{RsxFragmentOutputDiagnostic.Value}_DIAGNOSTIC|{fixedFunctionIdentity}"
                : fixedFunctionIdentity;

        OpenGlAuthoredFragmentSource finalPixelSource;
        try
        {
            OpenGlAuthoredFragmentSource translatedPixelSource =
                OpenGlFixedFunctionEpilogue.Apply(
                    fragmentSource,
                    fixedFunctionEpilogue);
            finalPixelSource = UseRsxVertexPlacementDiagnostic
                ? translatedPixelSource.WithBackendComposition(
                    VertexPlacementDiagnosticFragmentSource)
                : RemapFragmentOutputForDiagnostic(
                    translatedPixelSource,
                    RsxFragmentOutputDiagnostic);
        }
        catch (InvalidOperationException exception)
        {
            blocker =
                $"RSX GLSL source composition failed for {diagnosticIdentity}: {exception.Message}";
            _authoredMaterials.RecordPreparationFailure(
                $"{diagnosticIdentity}|sourceComposition",
                blocker);
            trace?.Invoke(
                $"preparation blocked; step=fragment-source-composition; " +
                $"executionKey={QuoteLoadTraceValue(execution.ProgramCacheKey)}; " +
                $"reason={QuoteLoadTraceValue(exception.Message)}");
            return default;
        }

        int[] samplerDestinations = UseRsxVertexPlacementDiagnostic
            ? []
            : execution.MaterialSamplerDestinations
                .Concat(execution.CustomSamplerDestinations)
                .Concat(execution.CodeSamplerDestinations)
                .Select(binding => (int)binding.Destination)
                .Distinct()
                .Order()
                .ToArray();
        GlRsxProgram program = _authoredMaterials.GetOrCreateComposedProgram(
            vertexGlsl,
            finalPixelSource,
            usesFrameConstants || compositionReady,
            samplerDestinations,
            diagnosticIdentity,
            (handle, programKey) =>
                ValidateAndCaptureMapProgramUniforms(
                    handle,
                    programKey,
                    usesFrameConstants,
                    compositionReady),
            out OpenGlProgramKey programKey,
            out blocker,
            trace);
        if (program.Handle == 0)
        {
            trace?.Invoke(
                $"program unavailable; " +
                $"executionKey={QuoteLoadTraceValue(execution.ProgramCacheKey)}; " +
                $"programKey={programKey}; " +
                $"blocker={QuoteLoadTraceValue(blocker)}");
        }
        if (program.Handle != 0 && compositionReady)
        {
            if (!_staticModelProgramUniforms.TryGetValue(
                    programKey,
                    out MapRenderOpenGlStaticModelProgramUniforms uniforms))
            {
                const string missingUniformsBlocker =
                    "Translated static-model program lost its cached uniform bridge.";
                _authoredMaterials.RecordPreparationFailure(
                    $"{programKey}|staticModelUniforms",
                    missingUniformsBlocker);
                trace?.Invoke(
                    $"program unavailable; step=static-instance-uniform-bridge; " +
                    $"programKey={programKey}; " +
                    $"reason={QuoteLoadTraceValue(missingUniformsBlocker)}");
                return default;
            }
            staticModelUniforms = uniforms;
        }
        return program;
    }

    private string? ValidateAndCaptureMapProgramUniforms(
        uint handle,
        OpenGlProgramKey programKey,
        bool usesFrameConstants,
        bool usesStaticModelInstancing)
    {
        if (usesFrameConstants &&
            _frameVertexConstants.ValidateAndBindProgram(handle) is
                { } frameBlocker)
        {
            return frameBlocker;
        }

        return usesStaticModelInstancing
            ? ValidateAndCaptureStaticModelProgramUniforms(handle, programKey)
            : null;
    }

    private string? ValidateAndCaptureStaticModelProgramUniforms(
        uint handle,
        OpenGlProgramKey programKey)
    {
        var vegetation = new MapRenderOpenGlStaticModelVegetationUniforms(
            _authoredMaterials.GetUniformLocation(
                handle,
                MapRenderOpenGlStaticModelInstancedVertexComposer
                    .CompositionParametersUniform),
            _authoredMaterials.GetUniformLocation(
                handle,
                MapRenderOpenGlStaticModelInstancedVertexComposer
                    .CompositionBoundsUniform));
        if (!vegetation.IsReady)
        {
            return "Translated static-model program lost its Live Preview vegetation uniform bridge.";
        }

        _staticModelProgramUniforms.TryAdd(
            programKey,
            new MapRenderOpenGlStaticModelProgramUniforms(
                vegetation));
        return null;
    }

    private static OpenGlAuthoredFragmentSource
        RemapFragmentOutputForDiagnostic(
            OpenGlAuthoredFragmentSource fragmentSource,
            int? outputIndex)
    {
        ArgumentNullException.ThrowIfNull(fragmentSource);
        if (!outputIndex.HasValue || outputIndex.Value == 0)
            return fragmentSource;
        if (outputIndex.Value is < 0 or > 3)
            throw new ArgumentOutOfRangeException(nameof(outputIndex));

        string fragmentGlsl = fragmentSource.ExactGlsl;
        string[] names =
            ["FragColor", "rsxMrtColor1", "rsxMrtColor2", "rsxMrtColor3"];
        int selected = outputIndex.Value;
        for (int index = 0; index < names.Length; index++)
        {
            int location = index == selected
                ? 0
                : index == 0
                    ? selected
                    : index;
            fragmentGlsl = fragmentGlsl.Replace(
                $"layout(location = {index}) out vec4 {names[index]};",
                $"layout(location = {location}) out vec4 {names[index]};",
                StringComparison.Ordinal);
        }
        return fragmentSource.WithBackendComposition(fragmentGlsl);
    }

    private const string VertexPlacementDiagnosticFragmentSource = """
        #version 330 core
        in vec4 rsxColor0;
        in vec4 rsxColor1;
        in vec4 rsxTexcoord0;
        in vec4 rsxTexcoord1;
        in vec4 rsxTexcoord2;
        in vec4 rsxTexcoord3;
        in vec4 rsxTexcoord4;
        in vec4 rsxTexcoord5;
        in vec4 rsxTexcoord6;
        in vec4 rsxTexcoord7;
        out vec4 FragColor;
        void main()
        {
            FragColor = vec4(1.0, 0.0, 1.0, 1.0);
        }
        """;

    private uint CreateTexture(
        Texture texture,
        bool pinForRendererLifetime = false,
        string? loadTraceRole = null)
    {
        Action<string>? trace = null;
        AccountTexturePayload(texture);
        if (!TryDescribeTextureUpload(
                texture,
                out OpenGlAuthoredBcUploadPlan? authoredBcPlan,
                out bool usesDirectAuthoredBcUpload,
                out int faceCount,
                out int storageLevelCount,
                out long estimatedResidentBytes))
        {
            return 0;
        }
        if (_textureHandles.TryGetHandle(texture, out uint cachedHandle))
        {
            if (pinForRendererLifetime &&
                _textureHandles.TryGetEntry(
                    cachedHandle,
                    out MapRenderOpenGlTextureResidencyEntry cachedEntry))
            {
                trace = CreateTextureLoadTrace(
                    texture,
                    loadTraceRole,
                    pinForRendererLifetime);
                trace?.Invoke(
                    $"cached texture pin started; handle={cachedHandle}");
                PinTextureEntry(cachedEntry);
                trace?.Invoke(
                    $"cached texture pin completed; handle={cachedHandle}; " +
                    $"resident={cachedEntry.IsResident}");
            }
            return cachedHandle;
        }

        trace = CreateTextureLoadTrace(
            texture,
            loadTraceRole,
            pinForRendererLifetime);
        trace?.Invoke(
            $"new texture allocation started; " +
            $"binding={QuoteLoadTraceValue(texture.BindingIdentity)}; " +
            $"target={texture.Target}; faces={faceCount}; " +
            $"storageLevels={storageLevelCount}; " +
            $"estimatedResidentBytes={estimatedResidentBytes}; " +
            $"directAuthoredBc={usesDirectAuthoredBcUpload}");

        trace?.Invoke("driver glGenTexture started");
        uint handle = _gl.GenTexture();
        TextureTarget textureTarget = ToGlTextureTarget(texture.Target);
        bool isPinned = pinForRendererLifetime;
        bool cached = false;
        bool useStateShadow = _loaded;
        int previousTextureUnit = 0;
        uint previousTexture = 0;
        try
        {
            if (useStateShadow)
            {
                previousTextureUnit = _state.GetActiveTextureUnit();
                _state.ActiveTexture(0);
                previousTexture =
                    _state.GetTextureBinding(0, textureTarget);
                trace?.Invoke(
                    $"driver texture bind started; path=state-shadow; " +
                    $"target={textureTarget}; handle={handle}");
                _state.BindTexture(textureTarget, handle);
            }
            else
            {
                trace?.Invoke(
                    $"driver glBindTexture started; " +
                    $"target={textureTarget}; handle={handle}");
                _gl.BindTexture(textureTarget, handle);
            }
            try
            {
                trace?.Invoke(
                    "driver glPixelStore started; " +
                    "parameter=UnpackAlignment; value=1");
                _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
                InitializeTextureFallbackStorageBound(
                    textureTarget,
                    faceCount,
                    trace);
                trace?.Invoke("texture parameter application started");
                _textureParameters.Apply(
                    texture,
                    maxMipLevel: 0,
                    textureTarget,
                    trace);
                trace?.Invoke("texture parameter application completed");

                trace?.Invoke(
                    $"residency cache add started; handle={handle}");
                MapRenderOpenGlTextureResidencyEntry entry =
                    _textureHandles.Add(
                        texture,
                        handle,
                        textureTarget,
                        faceCount,
                        storageLevelCount,
                        estimatedResidentBytes,
                        isPinned,
                        authoredBcPlan,
                        usesDirectAuthoredBcUpload);
                cached = true;
                trace?.Invoke(
                    $"residency cache add completed; handle={handle}; " +
                    $"cacheEntries={_textureHandles.Count}");
                if (isPinned)
                {
                    trace?.Invoke(
                        $"pinned storage upload started; handle={handle}");
                    UploadTextureStorageBound(entry);
                    trace?.Invoke(
                        $"pinned storage upload completed; handle={handle}");
                    entry.MarkResident(
                        _activeRenderFrameIndex >= 0
                            ? _activeRenderFrameIndex
                            : -1);
                }
            }
            finally
            {
                if (useStateShadow)
                {
                    trace?.Invoke(
                        $"driver texture binding restore started; " +
                        $"path=state-shadow; target={textureTarget}; " +
                        $"handle={previousTexture}; unit={previousTextureUnit}");
                    _state.BindTexture(
                        textureTarget,
                        previousTexture);
                    _state.ActiveTexture(previousTextureUnit);
                }
                else
                {
                    trace?.Invoke(
                        $"driver glBindTexture restore started; " +
                        $"target={textureTarget}; handle=0");
                    _gl.BindTexture(textureTarget, 0);
                }
            }
            trace?.Invoke(
                $"request completed; path=new-texture; handle={handle}");
            return handle;
        }
        catch (Exception exception)
        {
            trace?.Invoke(
                $"request failed; exception={exception.GetType().FullName}; " +
                $"message={QuoteLoadTraceValue(exception.Message)}; " +
                $"handle={handle}");
            if (cached)
            {
                if (_textureHandles.TryGetEntry(
                        handle,
                        out MapRenderOpenGlTextureResidencyEntry entry))
                {
                    ReleaseRendererOwnedDecodedFallback(entry);
                }
                _textureHandles.Remove(texture, handle);
            }
            trace?.Invoke(
                $"driver glDeleteTexture started; handle={handle}");
            _gl.DeleteTexture(handle);
            throw;
        }

        Action<string>? CreateTextureLoadTrace(
            Texture source,
            string? role,
            bool pinned)
        {
            if (!LoadProgressEnabled)
                return null;

            long sequence = NextLoadTextureTraceSequence();
            Action<string>? result = CreateLoadDetailReporter(
                $"texture seq={sequence}; " +
                $"role={QuoteLoadTraceValue(role ?? "unspecified")}; " +
                $"name={QuoteLoadTraceValue(source.Name)}");
            result?.Invoke(
                $"request selected for detailed trace; pinned={pinned}");
            return result;
        }
    }

    private void PinTextureEntry(
        MapRenderOpenGlTextureResidencyEntry entry)
    {
        entry.Pin();
        if (entry.IsResident)
            return;

        if (_loaded)
        {
            UploadTextureStorage(entry);
        }
        else
        {
            _gl.BindTexture(entry.Target, entry.Handle);
            try
            {
                _gl.PixelStore(
                    PixelStoreParameter.UnpackAlignment,
                    1);
                UploadTextureStorageBound(entry);
            }
            finally
            {
                _gl.BindTexture(entry.Target, 0);
            }
        }
        entry.MarkResident(
            _activeRenderFrameIndex >= 0
                ? _activeRenderFrameIndex
                : -1);
    }

    private bool TryDescribeTextureUpload(
        Texture? texture,
        out OpenGlAuthoredBcUploadPlan? authoredBcPlan,
        out bool usesDirectAuthoredBcUpload,
        out int faceCount,
        out int storageLevelCount,
        out long estimatedResidentBytes)
    {
        authoredBcPlan = null;
        usesDirectAuthoredBcUpload = false;
        faceCount = 0;
        storageLevelCount = 0;
        estimatedResidentBytes = 0;
        if (texture is null)
            return false;

        if (OpenGlAuthoredBcUploadPlan.TryCreate(
                texture,
                out OpenGlAuthoredBcUploadPlan provenPlan))
        {
            if (_compressedTextureSupport.Supports(
                    provenPlan.BlockCompression))
            {
                authoredBcPlan = provenPlan;
                usesDirectAuthoredBcUpload = true;
                faceCount = provenPlan.FaceCount;
                storageLevelCount = provenPlan.MipLevelCount;
                estimatedResidentBytes = provenPlan.PayloadBytes;
                return true;
            }

            // InteractiveNative scenes intentionally omit redundant RGBA for
            // complete proven BC chains. Preserve that immutable source and
            // defer compatibility decoding until this texture is first
            // admitted to the renderer's working set.
            if (!texture.HasCompleteDecodedRgbaPayload)
            {
                authoredBcPlan = provenPlan;
                faceCount = provenPlan.FaceCount;
                storageLevelCount = provenPlan.MipLevelCount;
                estimatedResidentBytes =
                    EstimateDecodedResidentBytes(
                        texture.Width,
                        texture.Height,
                        faceCount,
                        storageLevelCount);
                return estimatedResidentBytes > 0;
            }
        }

        if (!texture.HasCompleteDecodedPayload)
            return false;

        faceCount = texture.Target ==
            RenderTextureTarget.TextureCube
                ? 6
                : 1;
        storageLevelCount = texture.MipLevels.Count > 0
            ? checked(texture.MipLevels.Count + 1)
            : checked(MaxMipLevel(texture.Width, texture.Height) + 1);
        estimatedResidentBytes =
            EstimateDecodedResidentBytes(
                texture.Width,
                texture.Height,
                faceCount,
                storageLevelCount);
        return estimatedResidentBytes > 0;
    }

    private static long EstimateDecodedResidentBytes(
        int width,
        int height,
        int faceCount,
        int levelCount)
    {
        long faceBytes = 0;
        for (int level = 0; level < levelCount; level++)
        {
            faceBytes = checked(
                faceBytes +
                checked((long)width * height * 4L));
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
        }
        return checked(faceBytes * faceCount);
    }

    private bool CanUploadTexture(Texture? texture) =>
        TryDescribeTextureUpload(
            texture,
            out _,
            out _,
            out _,
            out _,
            out _);

    private void UploadTextureStorageBound(
        MapRenderOpenGlTextureResidencyEntry entry)
    {
        if (entry.UsesDirectAuthoredBcUpload &&
            entry.AuthoredBcPlan is { } authoredBcPlan)
        {
            UploadAuthoredBcStorageBound(entry, authoredBcPlan);
        }
        else
        {
            UploadDecodedTextureStorageBound(entry);
        }

        _textureParameters.Apply(
            entry.Source,
            checked(entry.StorageLevelCount - 1),
            entry.Target);
    }

    private void UploadAuthoredBcStorageBound(
        MapRenderOpenGlTextureResidencyEntry entry,
        OpenGlAuthoredBcUploadPlan plan)
    {
        InternalFormat internalFormat =
            ToGlCompressedInternalFormat(
                plan.BlockCompression,
                entry.Source.DecodedSamplerState.UsesSrgbReads);
        for (int face = 0; face < plan.FaceCount; face++)
        {
            TextureTarget uploadTarget = plan.FaceCount == 6
                ? (TextureTarget)(
                    (int)TextureTarget.TextureCubeMapPositiveX + face)
                : TextureTarget.Texture2D;
            for (int mip = 0; mip < plan.MipLevelCount; mip++)
            {
                TextureAuthoredSubresource subresource =
                    plan.Subresources[
                        checked(face * plan.MipLevelCount + mip)];
                fixed (byte* payload = subresource.SharedPayload)
                {
                    _gl.CompressedTexImage2D(
                        uploadTarget,
                        mip,
                        internalFormat,
                        checked((uint)subresource.Width),
                        checked((uint)subresource.Height),
                        border: 0,
                        checked((uint)subresource.SlicePitchBytes),
                        payload);
                }
            }
        }
    }

    private void UploadDecodedTextureStorageBound(
        MapRenderOpenGlTextureResidencyEntry entry)
    {
        Texture texture =
            ResolveDecodedUploadTexture(entry);
        if (texture.Target == RenderTextureTarget.TextureCube)
        {
            if (texture.CubeFaces is not { Count: 6 } cubeFaces)
            {
                throw new InvalidDataException(
                    $"Cube texture {texture.Name} does not contain exactly six faces.");
            }
            for (int faceIndex = 0;
                 faceIndex < cubeFaces.Count;
                 faceIndex++)
            {
                TextureCubeFace face = cubeFaces[faceIndex];
                TextureTarget faceTarget = (TextureTarget)(
                    (int)TextureTarget.TextureCubeMapPositiveX +
                    faceIndex);
                UploadDecodedTextureLevelBound(
                    faceTarget,
                    level: 0,
                    texture.Width,
                    texture.Height,
                    face.RgbaBytes,
                    texture.PixelFormat,
                    texture.DecodedSamplerState.UsesSrgbReads);
                for (int level = 0;
                     level < face.MipLevels.Count;
                     level++)
                {
                    TextureMip mip = face.MipLevels[level];
                    UploadDecodedTextureLevelBound(
                        faceTarget,
                        checked(level + 1),
                        mip.Width,
                        mip.Height,
                        mip.PixelBytes,
                        texture.PixelFormat,
                        texture.DecodedSamplerState.UsesSrgbReads);
                }
            }
            if (texture.MipLevels.Count == 0 &&
                entry.StorageLevelCount > 1)
            {
                _gl.GenerateMipmap(TextureTarget.TextureCubeMap);
            }
            return;
        }

        UploadDecodedTextureLevelBound(
            TextureTarget.Texture2D,
            level: 0,
            texture.Width,
            texture.Height,
            texture.PixelBytes,
            texture.PixelFormat,
            texture.DecodedSamplerState.UsesSrgbReads);
        for (int level = 0;
             level < texture.MipLevels.Count;
             level++)
        {
            TextureMip mip = texture.MipLevels[level];
            UploadDecodedTextureLevelBound(
                TextureTarget.Texture2D,
                checked(level + 1),
                mip.Width,
                mip.Height,
                mip.PixelBytes,
                texture.PixelFormat,
                texture.DecodedSamplerState.UsesSrgbReads);
        }
        if (texture.MipLevels.Count == 0 &&
            entry.StorageLevelCount > 1)
        {
            _gl.GenerateMipmap(TextureTarget.Texture2D);
        }
    }

    private Texture ResolveDecodedUploadTexture(
        MapRenderOpenGlTextureResidencyEntry entry)
    {
        if (entry.Source.HasCompleteDecodedPayload)
            return entry.Source;
        if (entry.DecodedAuthoredBcFallback is { } cachedFallback)
            return cachedFallback;
        if (entry.AuthoredBcPlan is not { } plan)
        {
            throw new InvalidDataException(
                $"Texture {entry.Source.Name} has neither a complete decoded payload nor a proven authored BC plan.");
        }

        Texture decodedFallback =
            DecodeRendererOwnedAuthoredBcFallback(
                entry.Source,
                plan);
        entry.SetDecodedAuthoredBcFallback(decodedFallback);
        _rendererDecodedBcFallbackBytesRetained = checked(
            _rendererDecodedBcFallbackBytesRetained +
            decodedFallback.DecodedFallbackByteCount);
        return decodedFallback;
    }

    private static Texture
        DecodeRendererOwnedAuthoredBcFallback(
            Texture source,
            OpenGlAuthoredBcUploadPlan plan)
    {
        if (plan.FaceCount == 1)
        {
            byte[] top = Decode(faceOrdinal: 0, mipLevel: 0);
            var mips = new TextureMip[
                checked(plan.MipLevelCount - 1)];
            for (int mipLevel = 1;
                 mipLevel < plan.MipLevelCount;
                 mipLevel++)
            {
                TextureAuthoredSubresource subresource =
                    plan.Subresources[mipLevel];
                mips[mipLevel - 1] = new TextureMip(
                    subresource.Width,
                    subresource.Height,
                    Decode(faceOrdinal: 0, mipLevel));
            }

            Texture result = source with
            {
                PixelBytes = top,
                MipLevels = mips,
                CubeFaces = null
            };
            if (!result.HasCompleteDecodedRgbaPayload)
            {
                throw new InvalidDataException(
                    $"Decoded BC fallback for texture {source.Name} is incomplete.");
            }
            return result;
        }

        var faces = new TextureCubeFace[plan.FaceCount];
        for (int faceOrdinal = 0;
             faceOrdinal < plan.FaceCount;
             faceOrdinal++)
        {
            byte[] top = Decode(faceOrdinal, mipLevel: 0);
            var mips = new TextureMip[
                checked(plan.MipLevelCount - 1)];
            for (int mipLevel = 1;
                 mipLevel < plan.MipLevelCount;
                 mipLevel++)
            {
                int coordinate = checked(
                    faceOrdinal * plan.MipLevelCount +
                    mipLevel);
                TextureAuthoredSubresource subresource =
                    plan.Subresources[coordinate];
                mips[mipLevel - 1] = new TextureMip(
                    subresource.Width,
                    subresource.Height,
                    Decode(faceOrdinal, mipLevel));
            }
            faces[faceOrdinal] =
                new TextureCubeFace(top, mips);
        }

        TextureCubeFace firstFace = faces[0];
        Texture cubeResult = source with
        {
            PixelBytes = firstFace.RgbaBytes,
            MipLevels = firstFace.MipLevels,
            CubeFaces = faces
        };
        if (!cubeResult.HasCompleteDecodedRgbaPayload)
        {
            throw new InvalidDataException(
                $"Decoded BC cube fallback for texture {source.Name} is incomplete.");
        }
        return cubeResult;

        byte[] Decode(int faceOrdinal, int mipLevel)
        {
            int coordinate = checked(
                faceOrdinal * plan.MipLevelCount +
                mipLevel);
            TextureAuthoredSubresource subresource =
                plan.Subresources[coordinate];
            return GfxImageDecoder.DecodeProvenAuthoredBc(
                plan.BlockCompression,
                subresource.SharedPayload,
                subresource.Width,
                subresource.Height);
        }
    }

    private void UploadDecodedTextureLevelBound(
        TextureTarget target,
        int level,
        int width,
        int height,
        byte[] pixelBytes,
        DecodedTexturePixelFormat pixelFormat,
        bool useSrgbReads)
    {
        (InternalFormat internalFormat,
         PixelFormat uploadFormat,
         PixelType uploadType) = pixelFormat switch
        {
            DecodedTexturePixelFormat.Rgba8Unorm =>
                (useSrgbReads
                    ? InternalFormat.Srgb8Alpha8
                    : InternalFormat.Rgba8,
                 PixelFormat.Rgba,
                 PixelType.UnsignedByte),
            DecodedTexturePixelFormat.Rg16Float =>
                (InternalFormat.RG16f,
                 PixelFormat.RG,
                 PixelType.HalfFloat),
            _ => throw new ArgumentOutOfRangeException(
                nameof(pixelFormat),
                pixelFormat,
                null)
        };
        fixed (byte* pixelPtr = pixelBytes)
        {
            _gl.TexImage2D(
                target,
                level,
                internalFormat,
                checked((uint)width),
                checked((uint)height),
                border: 0,
                uploadFormat,
                uploadType,
                pixelPtr);
        }
    }

    private void InitializeTextureFallbackStorageBound(
        TextureTarget target,
        int faceCount,
        Action<string>? trace = null)
    {
        ReadOnlySpan<byte> fallback = [255, 255, 255, 255];
        fixed (byte* pixelPtr = fallback)
        {
            for (int face = 0; face < faceCount; face++)
            {
                TextureTarget uploadTarget = faceCount == 6
                    ? (TextureTarget)(
                        (int)TextureTarget.TextureCubeMapPositiveX + face)
                    : TextureTarget.Texture2D;
                trace?.Invoke(
                    $"driver fallback glTexImage2D started; " +
                    $"face={face + 1}/{faceCount}; " +
                    $"target={uploadTarget}; size=1x1");
                _gl.TexImage2D(
                    uploadTarget,
                    level: 0,
                    InternalFormat.Rgba8,
                    width: 1,
                    height: 1,
                    border: 0,
                    PixelFormat.Rgba,
                    PixelType.UnsignedByte,
                    pixelPtr);
            }
        }
    }

    private uint CreateGenericInactiveTexture()
    {
        uint handle = _gl.GenTexture();
        try
        {
            _gl.BindTexture(TextureTarget.Texture2D, handle);
            _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            InitializeTextureFallbackStorageBound(
                TextureTarget.Texture2D,
                faceCount: 1);
            _gl.TexParameter(
                TextureTarget.Texture2D,
                TextureParameterName.TextureMinFilter,
                (int)TextureMinFilter.Nearest);
            _gl.TexParameter(
                TextureTarget.Texture2D,
                TextureParameterName.TextureMagFilter,
                (int)TextureMagFilter.Nearest);
            _gl.TexParameter(
                TextureTarget.Texture2D,
                TextureParameterName.TextureWrapS,
                (int)TextureWrapMode.ClampToEdge);
            _gl.TexParameter(
                TextureTarget.Texture2D,
                TextureParameterName.TextureWrapT,
                (int)TextureWrapMode.ClampToEdge);
            _gl.TexParameter(
                TextureTarget.Texture2D,
                TextureParameterName.TextureBaseLevel,
                0);
            _gl.TexParameter(
                TextureTarget.Texture2D,
                TextureParameterName.TextureMaxLevel,
                0);
            _gl.BindTexture(TextureTarget.Texture2D, 0);
            return handle;
        }
        catch
        {
            _gl.BindTexture(TextureTarget.Texture2D, 0);
            _gl.DeleteTexture(handle);
            throw;
        }
    }

    private static InternalFormat ToGlCompressedInternalFormat(
        AuthoredBlockCompression compression,
        bool useSrgbReads) =>
        (compression, useSrgbReads) switch
        {
            (AuthoredBlockCompression.Bc1, false) =>
                InternalFormat.CompressedRgbaS3TCDxt1Ext,
            (AuthoredBlockCompression.Bc2, false) =>
                InternalFormat.CompressedRgbaS3TCDxt3Ext,
            (AuthoredBlockCompression.Bc3, false) =>
                InternalFormat.CompressedRgbaS3TCDxt5Ext,
            (AuthoredBlockCompression.Bc1, true) =>
                InternalFormat.CompressedSrgbAlphaS3TCDxt1Ext,
            (AuthoredBlockCompression.Bc2, true) =>
                InternalFormat.CompressedSrgbAlphaS3TCDxt3Ext,
            (AuthoredBlockCompression.Bc3, true) =>
                InternalFormat.CompressedSrgbAlphaS3TCDxt5Ext,
            _ => throw new ArgumentOutOfRangeException(
                nameof(compression),
                compression,
                null),
        };

    private uint CreateStaticModelLightingAtlasTexture(
        MapRenderStaticModelLightingAtlas atlas)
    {
        ArgumentNullException.ThrowIfNull(atlas);
        uint handle = _gl.GenTexture();
        try
        {
            _gl.BindTexture(TextureTarget.Texture3D, handle);
            _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            fixed (byte* pixels = atlas.RgbaBytes)
            {
                _gl.TexImage3D(
                    TextureTarget.Texture3D,
                    0,
                    InternalFormat.Rgba8,
                    MapRenderStaticModelLightingAtlas.Width,
                    MapRenderStaticModelLightingAtlas.Height,
                    MapRenderStaticModelLightingAtlas.Depth,
                    0,
                    PixelFormat.Rgba,
                    PixelType.UnsignedByte,
                    pixels);
            }
            _gl.TexParameter(
                TextureTarget.Texture3D,
                TextureParameterName.TextureMinFilter,
                (int)TextureMinFilter.Linear);
            _gl.TexParameter(
                TextureTarget.Texture3D,
                TextureParameterName.TextureMagFilter,
                (int)TextureMagFilter.Linear);
            _gl.TexParameter(
                TextureTarget.Texture3D,
                TextureParameterName.TextureWrapS,
                (int)TextureWrapMode.ClampToEdge);
            _gl.TexParameter(
                TextureTarget.Texture3D,
                TextureParameterName.TextureWrapT,
                (int)TextureWrapMode.ClampToEdge);
            _gl.TexParameter(
                TextureTarget.Texture3D,
                TextureParameterName.TextureWrapR,
                (int)TextureWrapMode.ClampToEdge);
            _gl.TexParameter(
                TextureTarget.Texture3D,
                TextureParameterName.TextureBaseLevel,
                0);
            _gl.TexParameter(
                TextureTarget.Texture3D,
                TextureParameterName.TextureMaxLevel,
                0);
            _gl.BindTexture(TextureTarget.Texture3D, 0);
            return handle;
        }
        catch
        {
            _gl.BindTexture(TextureTarget.Texture3D, 0);
            _gl.DeleteTexture(handle);
            throw;
        }
    }

    private void ApplyTextureSwizzle(
        RsxTextureSwizzle swizzle,
        TextureTarget textureTarget) =>
        _textureParameters.ApplySwizzle(swizzle, textureTarget);

    private void ApplyTextureSampler(
        RsxSamplerState sampler,
        int maxMipLevel,
        TextureTarget textureTarget) =>
        _textureParameters.ApplySampler(
            sampler,
            maxMipLevel,
            textureTarget);

    private static int MaxMipLevel(int width, int height)
    {
        int size = Math.Max(width, height);
        int level = 0;
        while (size > 1)
        {
            size >>= 1;
            level++;
        }

        return level;
    }

    private uint CreateProgram(string vertexSource, string fragmentSource)
    {
        OpenGlLinkedProgramHandleResolution resolution =
            ResolveLinkedProgram(vertexSource, fragmentSource);
        if (resolution.IsReady)
            return resolution.Handle;

        throw new InvalidOperationException(
            resolution.FailureReason ??
            "OpenGL shared-program linking failed.");
    }

    private OpenGlLinkedProgramHandleResolution
        ResolveLinkedProgram(
            string vertexSource,
            string fragmentSource)
    {
        OpenGlProgramKey key =
            OpenGlProgramKey.Create(
                vertexSource,
                fragmentSource,
                LinkProfileIdentity);
        Action<string>? trace = null;
        if (LoadProgressEnabled)
        {
            long sequence = NextLoadProgramTraceSequence();
            trace = CreateLoadDetailReporter(
                $"program seq={sequence}; key={key}");
            trace?.Invoke(
                $"resolution started; " +
                $"vertexChars={vertexSource.Length}; " +
                $"fragmentChars={fragmentSource.Length}; " +
                $"vertexSha256={key.VertexGlslSha256}; " +
                $"fragmentSha256={key.PixelGlslSha256}");
        }
        if (_sceneProgramResolutions.TryGetValue(
                key,
                out OpenGlLinkedProgramHandleResolution
                    sceneResolution))
        {
            trace?.Invoke(
                $"scene-resolution cache hit; " +
                $"ready={sceneResolution.IsReady}; " +
                $"handle={sceneResolution.Handle}");
            return sceneResolution with { IsReuse = true };
        }

        trace?.Invoke("shared-resolution lookup started");
        bool linkInvoked = false;
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        OpenGlLinkedProgramHandleResolution resolution;
        using (BeginProgramDriverTrace(trace))
        {
            resolution = _sharedProgramUsage.GetOrLinkDeferred(
                vertexSource,
                fragmentSource,
                () =>
                {
                    linkInvoked = true;
                    trace?.Invoke(
                        "shared-resolution miss; new link submitted");
                    return SubmitProgramLink(
                        vertexSource,
                        fragmentSource,
                        trace);
                },
                program => CompleteProgramLink(program, trace),
                _deferNewAuthoredProgramLinkCompletion);
        }
        trace?.Invoke(
            $"shared-resolution lookup completed; " +
            $"path={(linkInvoked
                ? "new-link"
                : resolution.IsProgramBinaryLoad && !resolution.IsReuse
                    ? "program-binary"
                    : "shared-cache-reuse")}; " +
            $"ready={resolution.IsReady}; " +
            $"reuse={resolution.IsReuse}; " +
            $"cacheOwnsHandle={resolution.CacheOwnsHandle}; " +
            $"cacheResident={resolution.IsCacheResident}; " +
            $"handle={resolution.Handle}; " +
            $"failure={QuoteLoadTraceValue(resolution.FailureReason)}; " +
            $"elapsed={System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:0}ms");
        if (!resolution.IsCacheResident)
        {
            _sceneProgramResolutions.Add(key, resolution);
            if (resolution.IsReady)
                _sceneOwnedProgramHandles.Add(resolution.Handle);
        }
        trace?.Invoke("resolution completed");
        return resolution;
    }

    private uint SubmitProgramLink(
        string vertexSource,
        string fragmentSource,
        Action<string>? trace)
    {
        _shaderCompilationCounter.RecordProgramCompilationAttempt();
        MapRenderOpenGlLoadShaderObjectCache? shaderObjectCache =
            _activeLoadShaderObjectCache;
        uint vertexShader = AcquireShader(
            ShaderType.VertexShader,
            vertexSource,
            "vertex");
        try
        {
            uint fragmentShader = AcquireShader(
                ShaderType.FragmentShader,
                fragmentSource,
                "fragment");
            try
            {
                trace?.Invoke("driver glCreateProgram started");
                uint program = _gl.CreateProgram();
                trace?.Invoke(
                    $"driver glAttachShader started; stage=vertex; " +
                    $"program={program}; shader={vertexShader}");
                _gl.AttachShader(program, vertexShader);
                trace?.Invoke(
                    $"driver glAttachShader started; stage=fragment; " +
                    $"program={program}; shader={fragmentShader}");
                _gl.AttachShader(program, fragmentShader);
                if (_sharedProgramUsage
                    .ProgramBinaryPersistenceEnabled)
                {
                    trace?.Invoke(
                        $"driver program-binary hint started; " +
                        $"program={program}");
                    _gl.ProgramParameter(
                        program,
                        ProgramParameterPName.BinaryRetrievableHint,
                        1);
                }
                try
                {
                    trace?.Invoke(
                        $"driver glLinkProgram started; program={program}");
                    _gl.LinkProgram(program);
                    if (shaderObjectCache is not null)
                    {
                        // glLinkProgram captures the attached shader objects
                        // for this link operation. Detaching now lets the
                        // load-scoped shader cache release them independently
                        // while the driver completes the submitted program.
                        trace?.Invoke(
                            $"driver glDetachShader started; stage=vertex; " +
                            $"program={program}");
                        _gl.DetachShader(program, vertexShader);
                        trace?.Invoke(
                            $"driver glDetachShader started; stage=fragment; " +
                            $"program={program}");
                        _gl.DetachShader(program, fragmentShader);
                    }
                    trace?.Invoke(
                        $"new link submitted; program={program}");
                    return program;
                }
                catch
                {
                    _gl.DeleteProgram(program);
                    throw;
                }
            }
            finally
            {
                if (shaderObjectCache is null)
                    _gl.DeleteShader(fragmentShader);
            }
        }
        finally
        {
            if (shaderObjectCache is null)
                _gl.DeleteShader(vertexShader);
        }

        uint AcquireShader(
            ShaderType type,
            string source,
            string stage)
        {
            MapRenderOpenGlShaderObjectCacheTelemetry before =
                trace is not null && shaderObjectCache is not null
                    ? shaderObjectCache.CreateTelemetry()
                    : default;
            trace?.Invoke(
                $"shader-object acquisition started; " +
                $"stage={stage}; chars={source.Length}");
            uint shader = shaderObjectCache?.GetOrCompile(type, source) ??
                CompileShader(type, source);
            string path = "uncached-compile";
            if (trace is not null && shaderObjectCache is not null)
            {
                MapRenderOpenGlShaderObjectCacheTelemetry after =
                    shaderObjectCache.CreateTelemetry();
                path = after.CacheHitCount > before.CacheHitCount
                    ? "load-cache-reuse"
                    : after.SuccessfulCompilationCount >
                        before.SuccessfulCompilationCount
                        ? "compiled"
                        : "unknown";
            }
            trace?.Invoke(
                $"shader-object acquisition completed; " +
                $"stage={stage}; path={path}; handle={shader}");
            return shader;
        }
    }

    private uint CompleteProgramLink(
        uint program,
        Action<string>? trace)
    {
        if (program == 0)
            throw new ArgumentOutOfRangeException(nameof(program));

        trace?.Invoke(
            $"driver link-status query started; program={program}");
        _gl.GetProgram(
            program,
            ProgramPropertyARB.LinkStatus,
            out int status);
        trace?.Invoke(
            $"driver link-status query completed; " +
            $"program={program}; status={status}");
        if (status != 0)
        {
            trace?.Invoke(
                $"new link completed; program={program}");
            return program;
        }

        trace?.Invoke(
            $"driver program-info-log query started; program={program}");
        string info = _gl.GetProgramInfoLog(program);
        trace?.Invoke(
            $"driver program-info-log query completed; " +
            $"program={program}; chars={info.Length}");
        throw new InvalidOperationException(
            $"OpenGL program link failed: {info}");
    }

    private uint CompileShader(ShaderType type, string source)
    {
        Action<string>? trace = _activeProgramDriverTrace;
        string stage = type == ShaderType.VertexShader
            ? "vertex"
            : type == ShaderType.FragmentShader
                ? "fragment"
                : type.ToString();
        trace?.Invoke(
            $"driver glCreateShader started; stage={stage}; " +
            $"chars={source.Length}");
        uint shader = _gl.CreateShader(type);
        trace?.Invoke(
            $"driver glShaderSource started; stage={stage}; " +
            $"handle={shader}; chars={source.Length}");
        _gl.ShaderSource(shader, source);
        trace?.Invoke(
            $"driver glCompileShader started; stage={stage}; " +
            $"handle={shader}");
        _gl.CompileShader(shader);
        trace?.Invoke(
            $"driver compile-status query started; stage={stage}; " +
            $"handle={shader}");
        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int status);
        trace?.Invoke(
            $"driver compile-status query completed; stage={stage}; " +
            $"handle={shader}; status={status}");
        if (status == 0)
        {
            trace?.Invoke(
                $"driver shader-info-log query started; stage={stage}; " +
                $"handle={shader}");
            string info = _gl.GetShaderInfoLog(shader);
            trace?.Invoke(
                $"driver shader-info-log query completed; stage={stage}; " +
                $"handle={shader}; chars={info.Length}");
            _gl.DeleteShader(shader);
            throw new InvalidOperationException($"OpenGL {type} compile failed: {info}");
        }

        trace?.Invoke(
            $"shader compilation completed; stage={stage}; " +
            $"handle={shader}");
        return shader;
    }

    private static TextureTarget ToGlTextureTarget(RenderTextureTarget target) => target switch
    {
        RenderTextureTarget.Texture2D => TextureTarget.Texture2D,
        RenderTextureTarget.TextureCube => TextureTarget.TextureCubeMap,
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, null)
    };


    private const string VertexShaderSource = """
        #version 330 core
        layout (location = 0) in vec3 aPosition;
        layout (location = 1) in vec3 aColor;
        layout (location = 2) in vec4 aInstanceRow0;
        layout (location = 3) in vec4 aInstanceRow1;
        layout (location = 4) in vec4 aInstanceRow2;

        uniform mat4 uViewProjection;
        uniform int uUseInstancing;

        out vec3 vColor;

        void main()
        {
            vColor = aColor;
            vec4 localPosition = vec4(aPosition, 1.0);
            vec3 renderPosition = uUseInstancing == 0
                ? aPosition
                : vec3(
                    dot(aInstanceRow0, localPosition),
                    dot(aInstanceRow1, localPosition),
                    dot(aInstanceRow2, localPosition));
            gl_Position = uViewProjection * vec4(renderPosition, 1.0);
        }
        """;

    private const string FragmentShaderSource = """
        #version 330 core
        in vec3 vColor;
        out vec4 FragColor;

        void main()
        {
            FragColor = vec4(vColor, 1.0);
        }
        """;

    // Bounded EditorPreview lowering of the standard authored slot-0 pair for
    // generic world and instanced-static geometry. Translated world batches
    // execute the resolved transform_only.hlsl/null.hlsl programs directly.
    // The vegetation terms are a host geometry-consistency extension: the
    // decoded native transform_only program reads no wind inputs.
    private const string StandardDepthPrepassVertexShaderSource = """
        #version 330 core
        layout (location = 0) in vec4 aPosition;
        layout (location = 9) in vec4 aInstanceRow0;
        layout (location = 10) in vec4 aInstanceRow1;
        layout (location = 11) in vec4 aInstanceRow2;

        uniform mat4 uViewProjection;
        uniform int uUseInstancing;
        // x=enabled (exact 0/1), y=amplitude, z=angular frequency,
        // w=spatial frequency. Bounds carry local minimum/range in xy.
        uniform vec4 uVegetationParameters;
        uniform float uVegetationTime;
        uniform vec4 uVegetationBounds;

        void main()
        {
            vec4 localPosition = vec4(aPosition.xyz, 1.0);
            vec3 renderPosition;
            if (uUseInstancing != 0)
            {
                renderPosition = vec3(
                    dot(aInstanceRow0, localPosition),
                    dot(aInstanceRow1, localPosition),
                    dot(aInstanceRow2, localPosition));
            }
            else
            {
                renderPosition = aPosition.xyz;
            }

            if (uUseInstancing != 0 &&
                uVegetationParameters.x != 0.0 &&
                uVegetationBounds.y > 0.0001)
            {
                float heightWeight = clamp(
                    (aPosition.z - uVegetationBounds.x) /
                    uVegetationBounds.y,
                    0.0,
                    1.0);
                heightWeight *= heightWeight;
                float phase =
                    uVegetationTime * uVegetationParameters.z +
                    renderPosition.x * uVegetationParameters.w +
                    renderPosition.z * uVegetationParameters.w * 1.37;
                float wave = (
                    sin(phase) +
                    0.35 * sin(phase * 0.61 + 1.7)) / 1.35;
                float sway =
                    uVegetationParameters.y * heightWeight * wave;
                renderPosition.x += sway;
                renderPosition.z += sway * 0.35;
            }

            gl_Position = uViewProjection * vec4(renderPosition, 1.0);
        }
        """;

    private const string StandardDepthPrepassFragmentShaderSource = """
        #version 330 core

        void main()
        {
        }
        """;

    private const string SkyVertexShaderSource = """
        #version 330 core
        layout (location = 0) in vec3 aPosition;

        uniform mat4 uViewProjection;

        out vec3 vCubeDirection;

        void main()
        {
            // Scene coordinates are (game.x, game.z, -game.y). Convert the
            // authored sky position back to the game-space cube axes. The
            // wc_sky vertex program routes position directly to TEX0.
            vCubeDirection = vec3(
                aPosition.x,
                -aPosition.z,
                aPosition.y);
            vec4 clipPosition = uViewProjection * vec4(aPosition, 1.0);
            gl_Position = clipPosition.xyww;
        }
        """;

    private const string SkyFragmentShaderSource = """
        #version 330 core
        in vec3 vCubeDirection;
        uniform samplerCube uSkyTexture;
        out vec4 FragColor;

        void main()
        {
            FragColor = texture(uSkyTexture, normalize(vCubeDirection));
        }
        """;

    internal const string TexturedVertexShaderSource = """
        #version 330 core
        layout (location = 0) in vec3 aPosition;
        layout (location = 1) in vec2 aTexCoord0;
        layout (location = 2) in vec2 aTexCoord1;
        layout (location = 3) in vec2 aTexCoord2;
        layout (location = 4) in vec2 aTexCoord3;
        layout (location = 5) in vec2 aTexCoord4;
        layout (location = 6) in vec4 aBlendWeights;
        layout (location = 7) in vec2 aLightmapTexCoord;
        layout (location = 8) in vec3 aNormal;
        layout (location = 9) in vec4 aInstanceRow0;
        layout (location = 10) in vec4 aInstanceRow1;
        layout (location = 11) in vec4 aInstanceRow2;
        layout (location = 12) in vec4 aStaticModelBaseLightingCoords;

        uniform mat4 uViewProjection;
        uniform int uUseInstancing;
        // x=enabled (exact 0/1), y=amplitude, z=angular frequency,
        // w=spatial frequency. Bounds carry local minimum/range in xy.
        uniform vec4 uVegetationParameters;
        uniform float uVegetationTime;
        uniform vec4 uVegetationBounds;

        out vec2 vTexCoord0;
        out vec2 vTexCoord1;
        out vec2 vTexCoord2;
        out vec2 vTexCoord3;
        out vec2 vTexCoord4;
        out vec4 vBlendWeights;
        out vec2 vLightmapTexCoord;
        out vec3 vRenderPosition;
        out vec3 vRenderNormal;
        out vec4 vStaticModelBaseLightingCoords;

        void main()
        {
            vTexCoord0 = aTexCoord0;
            vTexCoord1 = aTexCoord1;
            vTexCoord2 = aTexCoord2;
            vTexCoord3 = aTexCoord3;
            vTexCoord4 = aTexCoord4;
            vBlendWeights = aBlendWeights;
            vLightmapTexCoord = aLightmapTexCoord;
            vStaticModelBaseLightingCoords = uUseInstancing == 0
                ? vec4(0.0)
                : aStaticModelBaseLightingCoords;
            vec4 localPosition = vec4(aPosition, 1.0);
            vec3 renderPosition = uUseInstancing == 0
                ? aPosition
                : vec3(
                    dot(aInstanceRow0, localPosition),
                    dot(aInstanceRow1, localPosition),
                    dot(aInstanceRow2, localPosition));
            vec3 renderNormal = uUseInstancing == 0
                ? aNormal
                : vec3(
                    dot(aInstanceRow0.xyz, aNormal),
                    dot(aInstanceRow1.xyz, aNormal),
                    dot(aInstanceRow2.xyz, aNormal));
            if (uUseInstancing != 0 &&
                uVegetationParameters.x != 0.0 &&
                uVegetationBounds.y > 0.0001)
            {
                float heightWeight = clamp(
                    (aPosition.z - uVegetationBounds.x) /
                    uVegetationBounds.y,
                    0.0,
                    1.0);
                heightWeight *= heightWeight;
                float phase =
                    uVegetationTime * uVegetationParameters.z +
                    renderPosition.x * uVegetationParameters.w +
                    renderPosition.z * uVegetationParameters.w * 1.37;
                float wave = (
                    sin(phase) +
                    0.35 * sin(phase * 0.61 + 1.7)) / 1.35;
                float sway =
                    uVegetationParameters.y * heightWeight * wave;
                renderPosition.x += sway;
                renderPosition.z += sway * 0.35;
            }
            vRenderPosition = renderPosition;
            vRenderNormal = length(renderNormal) > 0.000001
                ? normalize(renderNormal)
                : vec3(0.0);
            gl_Position = uViewProjection * vec4(renderPosition, 1.0);
        }
        """;

    internal const string TexturedFragmentShaderSource = """
        #version 330 core
        in vec2 vTexCoord0;
        in vec2 vTexCoord1;
        in vec2 vTexCoord2;
        in vec2 vTexCoord3;
        in vec2 vTexCoord4;
        in vec4 vBlendWeights;
        in vec2 vLightmapTexCoord;
        in vec3 vRenderPosition;
        in vec3 vRenderNormal;
        in vec4 vStaticModelBaseLightingCoords;
        uniform sampler2D uColorTexture0;
        uniform sampler2D uColorTexture1;
        uniform sampler2D uColorTexture2;
        uniform sampler2D uColorTexture3;
        uniform sampler2D uColorTexture4;
        uniform int uColorLayerCount;
        uniform int uLinearizeColorInputs;
        uniform int uBlendWeightComponent1;
        uniform int uBlendWeightComponent2;
        uniform int uBlendWeightComponent3;
        uniform int uBlendWeightComponent4;
        uniform sampler2D uLightmapTexture;
        uniform int uHasLightmap;
        uniform sampler3D uStaticModelLightingAtlas;
        uniform int uHasStaticModelLighting;
        uniform vec3 uStaticModelLightingSamplerTransform;
        uniform int uAlphaTestEnabled;
        uniform int uAlphaFunc;
        uniform float uAlphaRef;
        uniform int uShaderPackerSrgbEnabled;
        uniform int uPremultiplyAlpha;
        uniform int uLightingEnabled;
        uniform vec3 uAmbientColor;
        uniform int uHasDirectionalSunDiffuse;
        uniform int uHasDirectionalSunSpecular;
        uniform vec3 uDirectionalSunDirection;
        uniform vec3 uDirectionalSunDiffuseColor;
        uniform vec3 uDirectionalSunSpecularColor;
        uniform vec3 uCameraPosition;
        uniform int uFogEnabled;
        uniform int uFogUseActiveState;
        uniform vec3 uFogColor;
        uniform float uFogStart;
        uniform float uFogEnd;
        uniform float uFogMaxOpacity;
        uniform float uFogDistanceScale;
        uniform float uFogDistanceBias;
        uniform float uFogMinimumVisibility;
        uniform int uSunFogEnabled;
        uniform vec3 uSunFogColor;
        uniform vec3 uSunFogDirection;
        uniform float uSunFogDistanceScale;
        uniform float uSunFogEndCosine;
        uniform float uSunFogAngularScale;
        uniform sampler2D uNormalTexture0;
        uniform sampler2D uNormalTexture1;
        uniform sampler2D uNormalTexture2;
        uniform sampler2D uNormalTexture3;
        uniform int uHasNormalTexture0;
        uniform int uHasNormalTexture1;
        uniform int uHasNormalTexture2;
        uniform int uHasNormalTexture3;
        uniform sampler2D uSpecularTexture0;
        uniform sampler2D uSpecularTexture1;
        uniform sampler2D uSpecularTexture2;
        uniform int uHasSpecularTexture0;
        uniform int uHasSpecularTexture1;
        uniform int uHasSpecularTexture2;
        out vec4 FragColor;

        vec4 linearizeColorInput(vec4 encoded, int layerBit)
        {
            if ((uLinearizeColorInputs & layerBit) != 0)
            {
                // Selected translated PS3 programs lower their color-input
                // transfer as encoded.rgb * encoded.rgb. The host textures
                // are linear GL RGBA resources, so mirror that authored
                // shader operation before generic composition and lighting.
                encoded.rgb *= encoded.rgb;
            }
            return encoded;
        }

        bool alphaPasses(float alpha)
        {
            if (uAlphaTestEnabled == 0 || uAlphaFunc == 0x0207)
                return true;
            if (uAlphaFunc == 0x0200)
                return false;
            if (uAlphaFunc == 0x0201)
                return alpha < uAlphaRef;
            if (uAlphaFunc == 0x0202)
                return abs(alpha - uAlphaRef) <= (0.5 / 255.0);
            if (uAlphaFunc == 0x0203)
                return alpha <= uAlphaRef;
            if (uAlphaFunc == 0x0204)
                return alpha > uAlphaRef;
            if (uAlphaFunc == 0x0205)
                return abs(alpha - uAlphaRef) > (0.5 / 255.0);
            if (uAlphaFunc == 0x0206)
                return alpha >= uAlphaRef;
            return true;
        }

        float layerWeight(int component, float textureAlpha)
        {
            if (component < 0)
                return textureAlpha;
            float control = component == 0 ? vBlendWeights.x :
                            component == 1 ? vBlendWeights.y :
                            component == 2 ? vBlendWeights.z : vBlendWeights.w;
            return clamp(control * textureAlpha, 0.0, 1.0);
        }

        float controlWeight(int component)
        {
            if (component < 0)
                return 1.0;
            return clamp(
                component == 0 ? vBlendWeights.x :
                component == 1 ? vBlendWeights.y :
                component == 2 ? vBlendWeights.z : vBlendWeights.w,
                0.0,
                1.0);
        }

        vec3 surfaceNormal()
        {
            vec3 normal = vRenderNormal;
            if (length(normal) <= 0.000001)
            {
                normal = normalize(cross(
                    dFdx(vRenderPosition),
                    dFdy(vRenderPosition)));
            }
            normal = normalize(normal);
            return gl_FrontFacing ? normal : -normal;
        }

        vec3 decodeEditorNormal(vec4 encoded)
        {
            // Explicit EditorPreview approximation for IW DXT5nm-style AG
            // storage.
            vec2 xy = vec2(encoded.a, encoded.g) * 2.0 - 1.0;
            float z = sqrt(max(1.0 - dot(xy, xy), 0.0));
            return normalize(vec3(xy, z));
        }

        vec3 applyEditorNormalMap(
            vec3 baseNormal,
            vec4 encoded,
            vec2 uv)
        {
            vec3 dp1 = dFdx(vRenderPosition);
            vec3 dp2 = dFdy(vRenderPosition);
            vec2 duv1 = dFdx(uv);
            vec2 duv2 = dFdy(uv);
            vec3 dp2Perp = cross(dp2, baseNormal);
            vec3 dp1Perp = cross(baseNormal, dp1);
            vec3 tangent = dp2Perp * duv1.x + dp1Perp * duv2.x;
            vec3 bitangent = dp2Perp * duv1.y + dp1Perp * duv2.y;
            float maximumLength = max(
                dot(tangent, tangent),
                dot(bitangent, bitangent));
            if (maximumLength <= 0.00000001)
                return baseNormal;
            float inverseLength = inversesqrt(maximumLength);
            mat3 tangentFrame = mat3(
                tangent * inverseLength,
                bitangent * inverseLength,
                baseNormal);
            return normalize(tangentFrame * decodeEditorNormal(encoded));
        }

        vec3 materialNormal()
        {
            vec3 geometric = surfaceNormal();
            vec3 resolved = geometric;
            if (uHasNormalTexture0 != 0)
            {
                resolved = applyEditorNormalMap(
                    geometric,
                    texture(uNormalTexture0, vTexCoord0),
                    vTexCoord0);
            }
            if (uHasNormalTexture1 != 0)
            {
                vec3 layer = applyEditorNormalMap(
                    geometric,
                    texture(uNormalTexture1, vTexCoord1),
                    vTexCoord1);
                resolved = normalize(mix(
                    resolved,
                    layer,
                    controlWeight(uBlendWeightComponent1)));
            }
            if (uHasNormalTexture2 != 0)
            {
                vec3 layer = applyEditorNormalMap(
                    geometric,
                    texture(uNormalTexture2, vTexCoord2),
                    vTexCoord2);
                resolved = normalize(mix(
                    resolved,
                    layer,
                    controlWeight(uBlendWeightComponent2)));
            }
            if (uHasNormalTexture3 != 0)
            {
                vec3 layer = applyEditorNormalMap(
                    geometric,
                    texture(uNormalTexture3, vTexCoord3),
                    vTexCoord3);
                resolved = normalize(mix(
                    resolved,
                    layer,
                    controlWeight(uBlendWeightComponent3)));
            }
            return resolved;
        }

        float materialSpecular()
        {
            float resolved = uHasSpecularTexture0 != 0
                ? texture(uSpecularTexture0, vTexCoord0).r
                : 0.0;
            if (uHasSpecularTexture1 != 0)
            {
                resolved = mix(
                    resolved,
                    texture(uSpecularTexture1, vTexCoord1).r,
                    controlWeight(uBlendWeightComponent1));
            }
            if (uHasSpecularTexture2 != 0)
            {
                resolved = mix(
                    resolved,
                    texture(uSpecularTexture2, vTexCoord2).r,
                    controlWeight(uBlendWeightComponent2));
            }
            return clamp(resolved, 0.0, 1.0);
        }

        vec4 sampleStaticModelLighting(vec3 renderNormal)
        {
            // Viewer coordinates are (game X, game Z, -game Y). The native
            // model-lighting tile remains in game XYZ directional order.
            vec3 gameNormal = normalize(vec3(
                renderNormal.x,
                -renderNormal.z,
                renderNormal.y));
            vec3 coordinates =
                vStaticModelBaseLightingCoords.xyz +
                gameNormal * uStaticModelLightingSamplerTransform;
            return texture(uStaticModelLightingAtlas, coordinates);
        }

        void main()
        {
            vec4 color = linearizeColorInput(
                texture(uColorTexture0, vTexCoord0),
                1);
            if (uColorLayerCount > 1)
            {
                vec4 layer = linearizeColorInput(
                    texture(uColorTexture1, vTexCoord1),
                    2);
                float weight = layerWeight(uBlendWeightComponent1, layer.a);
                color = vec4(mix(color.rgb, layer.rgb, weight), max(color.a, weight));
            }
            if (uColorLayerCount > 2)
            {
                vec4 layer = linearizeColorInput(
                    texture(uColorTexture2, vTexCoord2),
                    4);
                float weight = layerWeight(uBlendWeightComponent2, layer.a);
                color = vec4(mix(color.rgb, layer.rgb, weight), max(color.a, weight));
            }
            if (uColorLayerCount > 3)
            {
                vec4 layer = linearizeColorInput(
                    texture(uColorTexture3, vTexCoord3),
                    8);
                float weight = layerWeight(uBlendWeightComponent3, layer.a);
                color = vec4(mix(color.rgb, layer.rgb, weight), max(color.a, weight));
            }
            if (uColorLayerCount > 4)
            {
                vec4 layer = linearizeColorInput(
                    texture(uColorTexture4, vTexCoord4),
                    16);
                float weight = layerWeight(uBlendWeightComponent4, layer.a);
                color = vec4(mix(color.rgb, layer.rgb, weight), max(color.a, weight));
            }
            // Static model-lighting and directional diffuse/specular both
            // consume the selected program's material normal. Lightmapped,
            // unlit, fog-only, and ambient-only materials retain the cheaper
            // path.
            vec3 normal = vec3(0.0, 0.0, 1.0);
            if (uLightingEnabled != 0 &&
                (uHasDirectionalSunDiffuse != 0 ||
                 uHasDirectionalSunSpecular != 0 ||
                 uHasStaticModelLighting != 0))
            {
                normal = materialNormal();
            }
            float primaryLightVisibility = 1.0;
            vec4 encodedStaticModelLighting = vec4(0.0);
            if (uLightingEnabled != 0 &&
                uHasStaticModelLighting != 0)
            {
                encodedStaticModelLighting =
                    sampleStaticModelLighting(normal);
                primaryLightVisibility = encodedStaticModelLighting.a;
            }
            if (uHasLightmap != 0)
            {
                // World lightmaps are baked irradiance. Do not add the preview
                // sun a second time when a valid authored lightmap route exists.
                color.rgb *= texture(uLightmapTexture, vLightmapTexCoord).rgb;
            }
            else if (uLightingEnabled != 0)
            {
                vec3 irradiance;
                if (uHasStaticModelLighting != 0)
                {
                    vec3 expandedLighting =
                        encodedStaticModelLighting.rgb * 2.0;
                    irradiance = expandedLighting * expandedLighting;
                    if (uHasDirectionalSunDiffuse != 0)
                    {
                        // Native lp_sun uses the tile alpha as the static
                        // object's primary-light visibility weight.
                        float nDotL = max(
                            dot(
                                normalize(normal),
                                -uDirectionalSunDirection),
                            0.0);
                        irradiance +=
                            uDirectionalSunDiffuseColor *
                            nDotL * primaryLightVisibility;
                    }
                }
                else
                {
                    irradiance = uAmbientColor;
                    if (uHasDirectionalSunDiffuse != 0)
                    {
                        // ComWorld stores the authored light-ray direction; a
                        // surface-to-light Lambert vector uses its explicit inverse.
                        float nDotL = max(
                            dot(
                                normalize(normal),
                                -uDirectionalSunDirection),
                            0.0);
                        irradiance +=
                            uDirectionalSunDiffuseColor * nDotL;
                    }
                }
                color.rgb *= irradiance;
            }
            if (uLightingEnabled != 0 &&
                uHasDirectionalSunSpecular != 0)
            {
                float specular = materialSpecular();
                vec3 toLight = -uDirectionalSunDirection;
                vec3 toCamera = normalize(uCameraPosition - vRenderPosition);
                vec3 halfVector = normalize(toLight + toCamera);
                float highlight = pow(
                    max(dot(normal, halfVector), 0.0),
                    32.0);
                color.rgb += uDirectionalSunSpecularColor * specular *
                    highlight * primaryLightVisibility;
            }
            if (uFogEnabled != 0)
            {
                vec3 cameraOffset =
                    vRenderPosition - uCameraPosition;
                float cameraDistance = sqrt(max(
                    dot(cameraOffset, cameraOffset),
                    0.0000001));
                if (uFogUseActiveState != 0)
                {
                    // Exact vertex programs multiply the natural-exponent
                    // R_SetFrameFog row by log2(e), use EX2, clamp both
                    // transmissions to 1 - fogMaxOpacity, and interpolate
                    // directional sun fog with the normalized game-space ray.
                    const float naturalExponentToBase2 = 1.4426950408889634;
                    float fogVisibility = max(
                        exp2((
                            uFogDistanceScale * cameraDistance +
                            uFogDistanceBias) *
                            naturalExponentToBase2),
                        uFogMinimumVisibility);
                    float visibility = fogVisibility;
                    vec3 resolvedFogColor = uFogColor;
                    if (uSunFogEnabled != 0)
                    {
                        float directionalCosine = dot(
                            cameraOffset / cameraDistance,
                            uSunFogDirection);
                        float sunFogFactor = clamp(
                            (directionalCosine - uSunFogEndCosine) *
                            uSunFogAngularScale,
                            0.0,
                            1.0);
                        float sunFogVisibility = max(
                            exp2((
                                uSunFogDistanceScale * cameraDistance +
                                uFogDistanceBias) *
                                naturalExponentToBase2),
                            uFogMinimumVisibility);
                        visibility = clamp(
                            sunFogFactor *
                                (sunFogVisibility - fogVisibility) +
                                fogVisibility,
                            0.0,
                            1.0);
                        resolvedFogColor = mix(
                            uFogColor,
                            uSunFogColor,
                            sunFogFactor);
                    }
                    color.rgb = mix(
                        resolvedFogColor,
                        color.rgb,
                        clamp(visibility, 0.0, 1.0));
                }
                else
                {
                    float fogRange = max(
                        uFogEnd - uFogStart,
                        0.0001);
                    float fogFactor = clamp(
                        (cameraDistance - uFogStart) / fogRange,
                        0.0,
                        1.0) * uFogMaxOpacity;
                    color.rgb = mix(
                        color.rgb,
                        uFogColor,
                        fogFactor);
                }
            }
            if (!alphaPasses(color.a))
                discard;
            if (uShaderPackerSrgbEnabled != 0)
            {
                // NV4097_SET_SHADER_PACKER sRGB output lowering. Mode
                // selection (including FP32-export suppression) is shared
                // with translated authored programs on the host.
                vec3 low = color.rgb * 12.92;
                vec3 high =
                    1.055 * pow(color.rgb, vec3(1.0 / 2.4)) - 0.055;
                bvec3 selectLow = lessThan(
                    color.rgb,
                    vec3(0.0031308));
                color.rgb = clamp(
                    mix(high, low, selectLow),
                    vec3(0.0),
                    vec3(1.0));
            }
            if (uPremultiplyAlpha != 0)
            {
                // Generic material lighting and fog retain straight RGB.
                // Authored ADD + ONE / ONE_MINUS_SRC_ALPHA programs apply
                // alpha at their final export. Do the same after optional
                // shader-packer encoding so fractional-alpha edges remain
                // premultiplied in the linear host framebuffer.
                color.rgb *= color.a;
            }
            FragColor = color;
        }
        """;
}
