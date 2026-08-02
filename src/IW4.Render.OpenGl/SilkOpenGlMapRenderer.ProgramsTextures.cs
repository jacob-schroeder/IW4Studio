using Silk.NET.OpenGL;

using IW4.Render.Execution;
using IW4.Render.Geometry;
using IW4.Render.EditorPreview;
using IW4.Render.Materials;
using IW4.Render.Lighting;
using IW4.Render.SceneBuilding;
using IW4.Render.Shaders;
using IW4.Render.Textures;
using IW4.Render.OpenGl.Programs;
using IW4.Render.OpenGl.Shaders;

namespace IW4.Render.OpenGl;

public sealed unsafe partial class SilkOpenGlMapRenderer
{
    private uint[] CreateEditorRoleTextures(
        IReadOnlyList<MapRenderMaterialSamplerBinding> bindings,
        IReadOnlyList<MapRenderEditorMaterialTextureRole> roles)
    {
        var result = new uint[roles.Count];
        for (int roleIndex = 0; roleIndex < roles.Count; roleIndex++)
        {
            if (SelectUniqueEditorRoleTexture(
                    bindings,
                    roles[roleIndex]) is { } texture &&
                CanUploadTexture(texture))
            {
                result[roleIndex] = CreateTexture(texture);
            }
        }

        return result;
    }

    internal static MapRenderTexture? SelectUniqueEditorRoleTexture(
        IReadOnlyList<MapRenderMaterialSamplerBinding> bindings,
        MapRenderEditorMaterialTextureRole role)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        if (!Enum.IsDefined(role) ||
            role == MapRenderEditorMaterialTextureRole.Unknown)
        {
            return null;
        }

        MapRenderMaterialSamplerBinding[] candidates = bindings
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

    private static bool HasAuthoredTechniquePass(MapRenderMaterialPass pass) =>
        pass.TechniqueSlot >= 0 && pass.PassIndex >= 0;

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
        // Runtime samplers (currently raw code sampler 6) are frame-owned.
        // They must not prevent structural compilation of the exact authored
        // program, but draw submission still validates them against the
        // current immutable shadow publication.
        if (!batch.ShaderExecution.RendererProgramReady ||
            !batch.ShaderExecution.VertexInputPayloadReady ||
            batch.RsxVertexInputs.Length !=
            (batch.Vertices.Length / MapRenderScene.TexturedVertexFloatCount) * 16 * 4)
        {
            return false;
        }

        if (!TryCreateEditorDirectCodeConstantPlan(
                batch.ShaderExecution,
                batch.SceneLightIndex,
                out MapRenderEditorTranslatedProgramDirectCodeConstantPlan?
                    directCodePlan) ||
            !TryCreateEditorVertexConstantBindingPlan(
                batch.ShaderExecution,
                directCodePlan!,
                out MapRenderEditorTranslatedProgramVertexConstantBindingPlan?
                    vertexPlan) ||
            vertexPlan!.Bindings.Any(binding => binding.Kind is
                MapRenderEditorTranslatedProgramVertexConstantBindingKind
                    .PerInstanceStaticModelBaseLightingCoords or
                MapRenderEditorTranslatedProgramVertexConstantBindingKind
                    .PerInstanceStaticModelLightProbeAmbient))
        {
            return false;
        }

        return GetOrCreateRsxProgram(
            batch.ShaderExecution,
            batch.State).Handle != 0;
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

        if (!TryCreateEditorDirectCodeConstantPlan(
                batch.ShaderExecution,
                batch.SceneLightIndex,
                out MapRenderEditorTranslatedProgramDirectCodeConstantPlan?
                    directCodePlan) ||
            !TryCreateEditorVertexConstantBindingPlan(
                batch.ShaderExecution,
                directCodePlan!,
                out _))
        {
            return false;
        }

        return GetOrCreateRsxProgram(
            batch.ShaderExecution,
            batch.State).Handle != 0;
    }

    private bool TryCreateEditorDirectCodeConstantPlan(
        MapRenderShaderExecutionContract execution,
        byte sceneLightIndex,
        out MapRenderEditorTranslatedProgramDirectCodeConstantPlan? plan)
    {
        plan = null;
        MapRenderEditorTranslatedProgramDirectCodeConstantPlanBuildResult
            result =
                MapRenderEditorTranslatedProgramDirectCodeConstantPlanner
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
        MapRenderShaderExecutionContract execution,
        MapRenderEditorTranslatedProgramDirectCodeConstantPlan directCodePlan,
        out MapRenderEditorTranslatedProgramVertexConstantBindingPlan? plan)
    {
        MapRenderEditorTranslatedProgramVertexConstantBindingPlanBuildResult
            result =
                MapRenderEditorTranslatedProgramVertexConstantBindingPlanner
                    .TryPlan(
                        execution.ProgramVertexConstantDestinations,
                        execution.ConstantDestinations,
                        execution.EmbeddedVertexConstants,
                        directCodePlan);
        plan = result.Plan;
        return result.IsReady;
    }

    private GlRsxConstantBinding[] CreateRsxConstantBindings(
        MapRenderShaderExecutionContract execution,
        GlRsxProgram program,
        MapRenderEditorTranslatedProgramDirectCodeConstantPlan directCodePlan,
        MapRenderEditorTranslatedProgramVertexConstantBindingPlan
            vertexConstantPlan)
    {
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentNullException.ThrowIfNull(directCodePlan);
        ArgumentNullException.ThrowIfNull(vertexConstantPlan);
        if (program.Handle == 0)
            throw new ArgumentException("An RSX constant plan requires a linked program.", nameof(program));

        var bindings = new List<GlRsxConstantBinding>();
        foreach (MapRenderEditorTranslatedProgramVertexConstantBinding
                 constant in vertexConstantPlan.Bindings)
        {
            int location = _rsxUniformLocations.Get(
                program.Handle,
                $"rsxVertexConst[{constant.Destination}]");
            if (constant.Kind ==
                MapRenderEditorTranslatedProgramVertexConstantBindingKind
                    .DerivedMatrixRow)
            {
                bindings.Add(new GlRsxConstantBinding(
                    location,
                    null,
                    null,
                    null,
                    null,
                    constant.CodeMatrixSemantic,
                    constant.CodeMatrixTransform,
                    constant.CodeMatrixRow));
                continue;
            }

            if (constant.Kind is
                MapRenderEditorTranslatedProgramVertexConstantBindingKind
                    .DynamicGameTime or
                MapRenderEditorTranslatedProgramVertexConstantBindingKind
                    .DynamicSceneLightPosition or
                MapRenderEditorTranslatedProgramVertexConstantBindingKind
                    .DynamicSunShadowProjection or
                MapRenderEditorTranslatedProgramVertexConstantBindingKind
                    .DynamicClipSpaceLookup or
                MapRenderEditorTranslatedProgramVertexConstantBindingKind
                    .DynamicZNear)
            {
                bindings.Add(new GlRsxConstantBinding(
                    location,
                    null,
                    null,
                    null,
                    null,
                    null,
                    MapRenderCodeMatrixTransform.None,
                    -1,
                    constant.DynamicCodeConstantSourceRow,
                    constant.Kind ==
                        MapRenderEditorTranslatedProgramVertexConstantBindingKind
                            .DynamicSceneLightPosition
                        ? directCodePlan.SceneLightIndex
                        : null));
                continue;
            }

            if (constant.Kind is
                MapRenderEditorTranslatedProgramVertexConstantBindingKind
                    .PerInstanceStaticModelBaseLightingCoords or
                MapRenderEditorTranslatedProgramVertexConstantBindingKind
                    .PerInstanceStaticModelLightProbeAmbient)
            {
                // The static-model composer replaces either per-instance row
                // uniform with divisor attribute 12. It must not be uploaded
                // once per draw.
                continue;
            }

            if (constant.StaticValue is not { } value)
            {
                throw new InvalidOperationException(
                    $"Selected RSX execution reached unresolved planned vertex constant c{constant.Destination}.");
            }
            bindings.Add(new GlRsxConstantBinding(
                location,
                value.X,
                value.Y,
                value.Z,
                value.W,
                null,
                MapRenderCodeMatrixTransform.None,
                -1));
        }

        foreach (MapRenderCodePixelConstantPatchPlan patchPlan in execution
                     .CodePixelConstantPatchPlans
                     .GroupBy(plan => plan.CodeIndex)
                     .Select(group => group.First()))
        {
            if (!patchPlan.IsDirectSourceResolved)
            {
                throw new InvalidOperationException(
                    $"Selected RSX execution reached unresolved direct CodePixel row 0x{patchPlan.CodeIndex:X2}.");
            }

            int location = _rsxUniformLocations.Get(
                program.Handle,
                MapRenderOpenGlCodePixelConstantUniformLayout.ElementName(
                    patchPlan.CodeIndex));
            if (directCodePlan.IsDynamicSourceRow(patchPlan.CodeIndex))
            {
                bindings.Add(new GlRsxConstantBinding(
                    location,
                    null,
                    null,
                    null,
                    null,
                    null,
                    MapRenderCodeMatrixTransform.None,
                    -1,
                    patchPlan.CodeIndex,
                    patchPlan.CodeIndex ==
                        FrameDirectCodeConstants
                            .DirectionalLightDirectionRowIndex
                        ? directCodePlan.SceneLightIndex
                        : null));
                continue;
            }
            if (!directCodePlan.TryGetRow(
                    patchPlan.CodeIndex,
                    out MapRenderDirectCodeConstantRow? row))
            {
                throw new InvalidOperationException(
                    $"Selected RSX execution reached unresolved direct CodePixel row 0x{patchPlan.CodeIndex:X2}.");
            }

            MapRenderShaderConstantValue value = row!.Value;
            bindings.Add(new GlRsxConstantBinding(
                location,
                value.X,
                value.Y,
                value.Z,
                value.W,
                null,
                MapRenderCodeMatrixTransform.None,
                -1));
        }

        return bindings.ToArray();
    }

    private static AuthoredProgramGroupKey AuthoredProgramGroup(MapRenderTexturedBatch batch) =>
        new(
            batch.Pass.MaterialName,
            batch.Pass.TechniqueSetName,
            batch.Pass.TechniqueSlot,
            batch.Pass.TechniqueName);

    private static AuthoredProgramGroupKey AuthoredProgramGroup(
        MapRenderInstancedTexturedBatch batch) =>
        new(
            batch.Pass.MaterialName,
            batch.Pass.TechniqueSetName,
            batch.Pass.TechniqueSlot,
            batch.Pass.TechniqueName);

    private GlRsxProgram GetOrCreateRsxProgram(
        MapRenderShaderExecutionContract execution,
        MapRenderState state,
        MapRenderEditorTranslatedProgramVertexConstantBindingPlan?
            staticModelVertexConstantPlan = null)
    {
        RsxVertexGlsl330ProgramResolution vertexResolution =
            _rsxVertexProgramResolver.Resolve(execution);
        if (!vertexResolution.IsReady)
        {
            string failedKey =
                $"{execution.ProgramCacheKey}|openGlVertexLowering";
            _rsxProgramFailureDiagnostics.TryAdd(
                failedKey,
                vertexResolution.FailureReason);
            return default;
        }
        RsxFragmentGlsl330ProgramResolution fragmentResolution =
            _rsxFragmentProgramResolver.Resolve(execution);
        if (!fragmentResolution.IsReady)
        {
            string failedKey =
                $"{execution.ProgramCacheKey}|openGlFragmentLowering";
            _rsxProgramFailureDiagnostics.TryAdd(
                failedKey,
                fragmentResolution.FailureReason);
            return default;
        }

        if (!MapRenderOpenGlFixedFunctionEpilogue.TryCompose(
                state,
                execution.FragmentProgramControl,
                suppressShaderPackerForDiagnosticOutput:
                    UseRsxVertexPlacementDiagnostic,
                out MapRenderAlphaTestMode alphaTestMode,
                out MapRenderOpenGlRsxShaderPackerMode shaderPackerMode,
                out string fixedFunctionEpilogue))
        {
            return default;
        }

        // Both sources are produced here from immutable RSX IR.
        string vertexGlsl = vertexResolution.Glsl!;
        MapRenderOpenGlAuthoredFragmentSource fragmentSource =
            fragmentResolution.Source!;
        bool staticModelInstancingReady = false;
        string staticModelInstancingIdentity = string.Empty;
        if (staticModelVertexConstantPlan is not null)
        {
            if (!MapRenderOpenGlStaticModelInstancedVertexComposer.TryCompose(
                    vertexGlsl,
                    execution.VertexInputs,
                    staticModelVertexConstantPlan,
                    out vertexGlsl,
                    out staticModelInstancingIdentity,
                    out string staticModelInstancingBlocker))
            {
                string failedKey =
                    $"{execution.ProgramCacheKey}|staticModelInstancing";
                if (!_rsxProgramFailureDiagnostics.ContainsKey(failedKey))
                {
                    _rsxProgramFailureDiagnostics.Add(
                        failedKey,
                        staticModelInstancingBlocker);
                }
                return default;
            }
            staticModelInstancingReady = true;
        }

        string fixedFunctionQualifiedDiagnosticIdentity =
            $"{execution.ProgramCacheKey}|alphaTest={alphaTestMode}" +
            $"|rsxShaderPacker={shaderPackerMode}" +
            (staticModelInstancingReady
                ? $"|staticModelInstancing={staticModelInstancingIdentity}"
                : string.Empty);
        string diagnosticIdentity = UseRsxVertexPlacementDiagnostic
            ? $"VERTEX_PLACEMENT_DIAGNOSTIC|{fixedFunctionQualifiedDiagnosticIdentity}"
            : RsxFragmentOutputDiagnostic.HasValue
                ? $"FRAGMENT_OUTPUT_{RsxFragmentOutputDiagnostic.Value}_DIAGNOSTIC|{fixedFunctionQualifiedDiagnosticIdentity}"
                : fixedFunctionQualifiedDiagnosticIdentity;

        MapRenderOpenGlAuthoredFragmentSource finalPixelSource;
        try
        {
            MapRenderOpenGlAuthoredFragmentSource translatedPixelSource =
                MapRenderOpenGlFixedFunctionEpilogue
                .Apply(
                fragmentSource,
                fixedFunctionEpilogue);
            finalPixelSource = UseRsxVertexPlacementDiagnostic
                ? translatedPixelSource.WithBackendComposition(
                    RsxVertexPlacementDiagnosticFragmentSource)
                : RemapFragmentOutputForDiagnostic(
                    translatedPixelSource,
                    RsxFragmentOutputDiagnostic);
        }
        catch (InvalidOperationException exception)
        {
            string diagnostic =
                $"RSX GLSL source composition failed for {diagnosticIdentity}: {exception.Message}";
            _rsxProgramFailureDiagnostics.TryAdd(
                $"{diagnosticIdentity}|sourceComposition",
                diagnostic);
            Console.Error.WriteLine(diagnostic);
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
        MapRenderOpenGlProgramKey programKey =
            MapRenderOpenGlDirectProgramKeyFactory.Create(
                vertexGlsl,
                finalPixelSource.ExactGlsl,
                EditorPreviewLinkProfileIdentity,
                staticModelInstancingReady,
                samplerDestinations);
        if (_rsxPrograms.TryGetValue(programKey, out GlRsxProgram cached))
            return cached;
        if (_rsxProgramFailures.ContainsKey(programKey))
            return default;

        _rsxProgramSemanticRequestCount =
            checked(_rsxProgramSemanticRequestCount + 1);
        MapRenderOpenGlLinkedProgramHandleResolution linkResolution =
            ResolveLinkedProgram(
                vertexGlsl,
                finalPixelSource.ExactGlsl);
        if (linkResolution.IsReuse)
        {
            _rsxProgramLinkReuseCount =
                checked(_rsxProgramLinkReuseCount + 1);
        }
        else if (linkResolution.IsReady)
        {
            _rsxProgramUniqueLinkCount =
                checked(_rsxProgramUniqueLinkCount + 1);
        }
        if (!linkResolution.IsReady)
        {
            string diagnostic =
                $"RSX GLSL validation failed for {diagnosticIdentity} " +
                $"({programKey}): {linkResolution.FailureReason}";
            _rsxProgramFailures.TryAdd(programKey, diagnostic);
            Console.Error.WriteLine(diagnostic);
            return default;
        }
        uint handle = linkResolution.Handle;

        int[] samplerLocations = samplerDestinations
            .Select(destination => _rsxUniformLocations.Get(
                handle,
                $"rsxSampler{destination}"))
            .ToArray();
        _gl.UseProgram(handle);
        for (int index = 0; index < samplerDestinations.Length; index++)
            _gl.Uniform1(samplerLocations[index], samplerDestinations[index]);
        GlRsxVegetationUniformLocations? vegetationUniformLocations =
            staticModelInstancingReady
                ? new(
                    _rsxUniformLocations.Get(
                        handle,
                        MapRenderOpenGlStaticModelInstancedVertexComposer
                            .VegetationWindEnabledUniform),
                    _rsxUniformLocations.Get(
                        handle,
                        MapRenderOpenGlStaticModelInstancedVertexComposer
                            .VegetationTimeUniform),
                    _rsxUniformLocations.Get(
                        handle,
                        MapRenderOpenGlStaticModelInstancedVertexComposer
                            .VegetationAmplitudeUniform),
                    _rsxUniformLocations.Get(
                        handle,
                        MapRenderOpenGlStaticModelInstancedVertexComposer
                            .VegetationAngularFrequencyUniform),
                    _rsxUniformLocations.Get(
                        handle,
                        MapRenderOpenGlStaticModelInstancedVertexComposer
                            .VegetationSpatialFrequencyUniform),
                    _rsxUniformLocations.Get(
                        handle,
                        MapRenderOpenGlStaticModelInstancedVertexComposer
                            .VegetationLocalMinimumHeightUniform),
                    _rsxUniformLocations.Get(
                        handle,
                        MapRenderOpenGlStaticModelInstancedVertexComposer
                            .VegetationLocalHeightRangeUniform))
                : null;
        if (vegetationUniformLocations is { IsReady: false })
        {
            // The exact linked handle can already back another semantic
            // wrapper. Keep ownership in the exact-pair cache and reject only
            // this wrapper; renderer cleanup deletes every unique handle once.
            _rsxProgramFailures.TryAdd(
                programKey,
                "Translated static-model program lost its Live Preview vegetation uniform bridge.");
            return default;
        }

        var program = new GlRsxProgram(
            handle,
            samplerDestinations,
            samplerLocations)
        {
            StaticModelInstancingReady = staticModelInstancingReady,
            StaticModelViewRowLocations = staticModelInstancingReady
                ? Enumerable.Range(0, 4)
                    .Select(row => _rsxUniformLocations.Get(
                        handle,
                        $"{MapRenderOpenGlStaticModelInstancedVertexComposer.ViewRowsUniform}[{row}]"))
                    .ToArray()
                : null,
            StaticModelViewProjectionRowLocations =
                staticModelInstancingReady
                    ? Enumerable.Range(0, 4)
                        .Select(row => _rsxUniformLocations.Get(
                            handle,
                            $"{MapRenderOpenGlStaticModelInstancedVertexComposer.ViewProjectionRowsUniform}[{row}]"))
                        .ToArray()
                    : null,
            StaticModelEyeOffsetLocation = staticModelInstancingReady
                ? _rsxUniformLocations.Get(
                    handle,
                    MapRenderOpenGlStaticModelInstancedVertexComposer.EyeOffsetUniform)
                : -1,
            StaticModelVegetationUniformLocations =
                vegetationUniformLocations
        };
        _rsxPrograms.Add(programKey, program);
        return program;
    }

    private static MapRenderOpenGlAuthoredFragmentSource
        RemapFragmentOutputForDiagnostic(
            MapRenderOpenGlAuthoredFragmentSource fragmentSource,
            int? outputIndex)
    {
        ArgumentNullException.ThrowIfNull(fragmentSource);
        if (!outputIndex.HasValue || outputIndex.Value == 0)
            return fragmentSource;
        if (outputIndex.Value is < 0 or > 3)
            throw new ArgumentOutOfRangeException(nameof(outputIndex));

        string fragmentGlsl = fragmentSource.ExactGlsl;
        string[] names = ["FragColor", "rsxMrtColor1", "rsxMrtColor2", "rsxMrtColor3"];
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

    // Explicit diagnostic only: it preserves the exact selected vertex program,
    // vertex inputs, and constants while replacing fragment execution with a flat
    // marker. It must never be treated as an authored-program translation.
    private const string RsxVertexPlacementDiagnosticFragmentSource = """
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
        MapRenderTexture texture,
        bool pinForRendererLifetime = false)
    {
        AccountTexturePayload(texture);
        if (!TryDescribeTextureUpload(
                texture,
                out MapRenderOpenGlAuthoredBcUploadPlan? authoredBcPlan,
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
                PinTextureEntry(cachedEntry);
            }
            return cachedHandle;
        }

        MapRenderRsxTextureSwizzle swizzle =
            MapRenderRsxTextureSwizzleDecoder.Decode(
                texture.RsxTextureCommandState.TexSwizzlePayload);

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
                _state.BindTexture(textureTarget, handle);
            }
            else
            {
                _gl.BindTexture(textureTarget, handle);
            }
            try
            {
                _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
                InitializeTextureFallbackStorageBound(
                    textureTarget,
                    faceCount);
                ApplyTextureSwizzle(swizzle, textureTarget);
                ApplyTextureSampler(
                    texture.DecodedSamplerState,
                    maxMipLevel: 0,
                    textureTarget);

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
                if (isPinned)
                {
                    UploadTextureStorageBound(entry);
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
                    _state.BindTexture(
                        textureTarget,
                        previousTexture);
                    _state.ActiveTexture(previousTextureUnit);
                }
                else
                {
                    _gl.BindTexture(textureTarget, 0);
                }
            }
            return handle;
        }
        catch
        {
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
            _gl.DeleteTexture(handle);
            throw;
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
        MapRenderTexture? texture,
        out MapRenderOpenGlAuthoredBcUploadPlan? authoredBcPlan,
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

        if (MapRenderOpenGlAuthoredBcUploadPlan.TryCreate(
                texture,
                out MapRenderOpenGlAuthoredBcUploadPlan provenPlan))
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

            // InteractiveOpenGl scenes intentionally omit redundant RGBA for
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

        if (!texture.HasCompleteDecodedRgbaPayload)
            return false;

        faceCount = texture.Target ==
            MapRenderTextureTarget.TextureCube
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

    private bool CanUploadTexture(MapRenderTexture? texture) =>
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
            UploadDecodedRgbaStorageBound(entry);
        }

        MapRenderRsxTextureSwizzle swizzle =
            MapRenderRsxTextureSwizzleDecoder.Decode(
                entry.Source.RsxTextureCommandState.TexSwizzlePayload);
        ApplyTextureSwizzle(swizzle, entry.Target);
        ApplyTextureSampler(
            entry.Source.DecodedSamplerState,
            checked(entry.StorageLevelCount - 1),
            entry.Target);
    }

    private void UploadAuthoredBcStorageBound(
        MapRenderOpenGlTextureResidencyEntry entry,
        MapRenderOpenGlAuthoredBcUploadPlan plan)
    {
        InternalFormat internalFormat =
            ToGlCompressedInternalFormat(plan.BlockCompression);
        for (int face = 0; face < plan.FaceCount; face++)
        {
            TextureTarget uploadTarget = plan.FaceCount == 6
                ? (TextureTarget)(
                    (int)TextureTarget.TextureCubeMapPositiveX + face)
                : TextureTarget.Texture2D;
            for (int mip = 0; mip < plan.MipLevelCount; mip++)
            {
                MapRenderTextureAuthoredSubresource subresource =
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

    private void UploadDecodedRgbaStorageBound(
        MapRenderOpenGlTextureResidencyEntry entry)
    {
        MapRenderTexture texture =
            ResolveDecodedRgbaUploadTexture(entry);
        if (texture.Target == MapRenderTextureTarget.TextureCube)
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
                MapRenderTextureCubeFace face = cubeFaces[faceIndex];
                TextureTarget faceTarget = (TextureTarget)(
                    (int)TextureTarget.TextureCubeMapPositiveX +
                    faceIndex);
                UploadDecodedRgbaLevelBound(
                    faceTarget,
                    level: 0,
                    texture.Width,
                    texture.Height,
                    face.RgbaBytes);
                for (int level = 0;
                     level < face.MipLevels.Count;
                     level++)
                {
                    MapRenderTextureMip mip = face.MipLevels[level];
                    UploadDecodedRgbaLevelBound(
                        faceTarget,
                        checked(level + 1),
                        mip.Width,
                        mip.Height,
                        mip.RgbaBytes);
                }
            }
            if (texture.MipLevels.Count == 0 &&
                entry.StorageLevelCount > 1)
            {
                _gl.GenerateMipmap(TextureTarget.TextureCubeMap);
            }
            return;
        }

        UploadDecodedRgbaLevelBound(
            TextureTarget.Texture2D,
            level: 0,
            texture.Width,
            texture.Height,
            texture.RgbaBytes);
        for (int level = 0;
             level < texture.MipLevels.Count;
             level++)
        {
            MapRenderTextureMip mip = texture.MipLevels[level];
            UploadDecodedRgbaLevelBound(
                TextureTarget.Texture2D,
                checked(level + 1),
                mip.Width,
                mip.Height,
                mip.RgbaBytes);
        }
        if (texture.MipLevels.Count == 0 &&
            entry.StorageLevelCount > 1)
        {
            _gl.GenerateMipmap(TextureTarget.Texture2D);
        }
    }

    private MapRenderTexture ResolveDecodedRgbaUploadTexture(
        MapRenderOpenGlTextureResidencyEntry entry)
    {
        if (entry.Source.HasCompleteDecodedRgbaPayload)
            return entry.Source;
        if (entry.DecodedAuthoredBcFallback is { } cachedFallback)
            return cachedFallback;
        if (entry.AuthoredBcPlan is not { } plan)
        {
            throw new InvalidDataException(
                $"Texture {entry.Source.Name} has neither a complete decoded payload nor a proven authored BC plan.");
        }

        MapRenderTexture decodedFallback =
            DecodeRendererOwnedAuthoredBcFallback(
                entry.Source,
                plan);
        entry.SetDecodedAuthoredBcFallback(decodedFallback);
        _rendererDecodedBcFallbackBytesRetained = checked(
            _rendererDecodedBcFallbackBytesRetained +
            decodedFallback.DecodedFallbackByteCount);
        return decodedFallback;
    }

    private static MapRenderTexture
        DecodeRendererOwnedAuthoredBcFallback(
            MapRenderTexture source,
            MapRenderOpenGlAuthoredBcUploadPlan plan)
    {
        if (plan.FaceCount == 1)
        {
            byte[] top = Decode(faceOrdinal: 0, mipLevel: 0);
            var mips = new MapRenderTextureMip[
                checked(plan.MipLevelCount - 1)];
            for (int mipLevel = 1;
                 mipLevel < plan.MipLevelCount;
                 mipLevel++)
            {
                MapRenderTextureAuthoredSubresource subresource =
                    plan.Subresources[mipLevel];
                mips[mipLevel - 1] = new MapRenderTextureMip(
                    subresource.Width,
                    subresource.Height,
                    Decode(faceOrdinal: 0, mipLevel));
            }

            MapRenderTexture result = source with
            {
                RgbaBytes = top,
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

        var faces = new MapRenderTextureCubeFace[plan.FaceCount];
        for (int faceOrdinal = 0;
             faceOrdinal < plan.FaceCount;
             faceOrdinal++)
        {
            byte[] top = Decode(faceOrdinal, mipLevel: 0);
            var mips = new MapRenderTextureMip[
                checked(plan.MipLevelCount - 1)];
            for (int mipLevel = 1;
                 mipLevel < plan.MipLevelCount;
                 mipLevel++)
            {
                int coordinate = checked(
                    faceOrdinal * plan.MipLevelCount +
                    mipLevel);
                MapRenderTextureAuthoredSubresource subresource =
                    plan.Subresources[coordinate];
                mips[mipLevel - 1] = new MapRenderTextureMip(
                    subresource.Width,
                    subresource.Height,
                    Decode(faceOrdinal, mipLevel));
            }
            faces[faceOrdinal] =
                new MapRenderTextureCubeFace(top, mips);
        }

        MapRenderTextureCubeFace firstFace = faces[0];
        MapRenderTexture cubeResult = source with
        {
            RgbaBytes = firstFace.RgbaBytes,
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
            MapRenderTextureAuthoredSubresource subresource =
                plan.Subresources[coordinate];
            return GfxImageDecoder.DecodeProvenAuthoredBc(
                plan.BlockCompression,
                subresource.SharedPayload,
                subresource.Width,
                subresource.Height);
        }
    }

    private void UploadDecodedRgbaLevelBound(
        TextureTarget target,
        int level,
        int width,
        int height,
        byte[] rgbaBytes)
    {
        fixed (byte* pixelPtr = rgbaBytes)
        {
            _gl.TexImage2D(
                target,
                level,
                InternalFormat.Rgba8,
                checked((uint)width),
                checked((uint)height),
                border: 0,
                PixelFormat.Rgba,
                PixelType.UnsignedByte,
                pixelPtr);
        }
    }

    private void InitializeTextureFallbackStorageBound(
        TextureTarget target,
        int faceCount)
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

    private static InternalFormat ToGlCompressedInternalFormat(
        MapRenderAuthoredBlockCompression compression) =>
        compression switch
        {
            MapRenderAuthoredBlockCompression.Bc1 =>
                InternalFormat.CompressedRgbaS3TCDxt1Ext,
            MapRenderAuthoredBlockCompression.Bc2 =>
                InternalFormat.CompressedRgbaS3TCDxt3Ext,
            MapRenderAuthoredBlockCompression.Bc3 =>
                InternalFormat.CompressedRgbaS3TCDxt5Ext,
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
        MapRenderRsxTextureSwizzle swizzle,
        TextureTarget textureTarget)
    {
        _gl.TexParameter(
            textureTarget,
            TextureParameterName.TextureSwizzleR,
            ToGlTextureSwizzle(swizzle.Red));
        _gl.TexParameter(
            textureTarget,
            TextureParameterName.TextureSwizzleG,
            ToGlTextureSwizzle(swizzle.Green));
        _gl.TexParameter(
            textureTarget,
            TextureParameterName.TextureSwizzleB,
            ToGlTextureSwizzle(swizzle.Blue));
        _gl.TexParameter(
            textureTarget,
            TextureParameterName.TextureSwizzleA,
            ToGlTextureSwizzle(swizzle.Alpha));
    }

    private static int ToGlTextureSwizzle(
        MapRenderRsxTextureSwizzleSource source)
    {
        return source switch
        {
            MapRenderRsxTextureSwizzleSource.Zero => GlTextureSwizzleZero,
            MapRenderRsxTextureSwizzleSource.One => GlTextureSwizzleOne,
            MapRenderRsxTextureSwizzleSource.Red => GlTextureSwizzleRed,
            MapRenderRsxTextureSwizzleSource.Green => GlTextureSwizzleGreen,
            MapRenderRsxTextureSwizzleSource.Blue => GlTextureSwizzleBlue,
            MapRenderRsxTextureSwizzleSource.Alpha => GlTextureSwizzleAlpha,
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
        };
    }

    private void ApplyTextureSampler(MapRenderSamplerState sampler, int maxMipLevel, TextureTarget textureTarget)
    {
        bool useMipChain = maxMipLevel > 0 && sampler.MipFilter != MapRenderTextureFilter.None;
        _gl.TexParameter(
            textureTarget,
            TextureParameterName.TextureMinFilter,
            (int)ToMinFilter(sampler, useMipChain));
        _gl.TexParameter(textureTarget, TextureParameterName.TextureMagFilter, (int)ToMagFilter(sampler.MagFilter));
        _gl.TexParameter(textureTarget, TextureParameterName.TextureBaseLevel, 0);
        _gl.TexParameter(textureTarget, TextureParameterName.TextureMaxLevel, useMipChain ? maxMipLevel : 0);
        _gl.TexParameter(textureTarget, TextureLodBias, sampler.MipLodBias);
        _gl.TexParameter(textureTarget, TextureParameterName.TextureWrapS, (int)ToWrapMode(sampler.AddressU));
        _gl.TexParameter(textureTarget, TextureParameterName.TextureWrapT, (int)ToWrapMode(sampler.AddressV));
        _gl.TexParameter(textureTarget, TextureParameterName.TextureWrapR, (int)ToWrapMode(sampler.AddressW));

        if (sampler.MaxAnisotropy > 1 && IsAnisotropicFilteringSupported())
        {
            _gl.TexParameter(
                textureTarget,
                TextureMaxAnisotropyExt,
                Math.Clamp((float)sampler.MaxAnisotropy, 1f, 16f));
        }
    }

    private bool IsAnisotropicFilteringSupported()
    {
        _anisotropicFilteringSupported ??=
            _gl.IsExtensionPresent("GL_EXT_texture_filter_anisotropic") ||
            _gl.IsExtensionPresent("GL_ARB_texture_filter_anisotropic");
        return _anisotropicFilteringSupported.Value;
    }

    private static TextureMinFilter ToMinFilter(MapRenderSamplerState sampler, bool useMipChain)
    {
        bool point = sampler.MinFilter == MapRenderTextureFilter.Point;
        if (!useMipChain)
            return point ? TextureMinFilter.Nearest : TextureMinFilter.Linear;

        return sampler.MipFilter switch
        {
            MapRenderTextureFilter.Point => point ? TextureMinFilter.NearestMipmapNearest : TextureMinFilter.LinearMipmapNearest,
            MapRenderTextureFilter.Linear => point ? TextureMinFilter.NearestMipmapLinear : TextureMinFilter.LinearMipmapLinear,
            _ => point ? TextureMinFilter.Nearest : TextureMinFilter.Linear
        };
    }

    private static TextureMagFilter ToMagFilter(MapRenderTextureFilter filter)
    {
        return filter == MapRenderTextureFilter.Point
            ? TextureMagFilter.Nearest
            : TextureMagFilter.Linear;
    }

    private static TextureWrapMode ToWrapMode(MapRenderTextureAddressMode mode)
    {
        return mode == MapRenderTextureAddressMode.Clamp
            ? TextureWrapMode.ClampToEdge
            : TextureWrapMode.Repeat;
    }

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
        MapRenderOpenGlLinkedProgramHandleResolution resolution =
            ResolveLinkedProgram(vertexSource, fragmentSource);
        if (resolution.IsReady)
            return resolution.Handle;

        throw new InvalidOperationException(
            resolution.FailureReason ??
            "OpenGL shared-program linking failed.");
    }

    private MapRenderOpenGlLinkedProgramHandleResolution
        ResolveLinkedProgram(
            string vertexSource,
            string fragmentSource)
    {
        MapRenderOpenGlProgramKey key =
            MapRenderOpenGlProgramKey.Create(
                vertexSource,
                fragmentSource,
                EditorPreviewLinkProfileIdentity);
        if (_sceneProgramResolutions.TryGetValue(
                key,
                out MapRenderOpenGlLinkedProgramHandleResolution
                    sceneResolution))
        {
            return sceneResolution with { IsReuse = true };
        }

        MapRenderOpenGlLinkedProgramHandleResolution resolution =
            _sharedProgramUsage.GetOrLink(
                vertexSource,
                fragmentSource,
                () => LinkProgram(vertexSource, fragmentSource));
        if (!resolution.IsCacheResident)
        {
            _sceneProgramResolutions.Add(key, resolution);
            if (resolution.IsReady)
                _sceneOwnedProgramHandles.Add(resolution.Handle);
        }
        return resolution;
    }

    private uint LinkProgram(string vertexSource, string fragmentSource)
    {
        _shaderCompilationCounter.RecordProgramCompilationAttempt();
        MapRenderOpenGlLoadShaderObjectCache? shaderObjectCache =
            _activeLoadShaderObjectCache;
        uint vertexShader = shaderObjectCache?.GetOrCompile(
                ShaderType.VertexShader,
                vertexSource) ??
            CompileShader(ShaderType.VertexShader, vertexSource);
        try
        {
            uint fragmentShader = shaderObjectCache?.GetOrCompile(
                    ShaderType.FragmentShader,
                    fragmentSource) ??
                CompileShader(ShaderType.FragmentShader, fragmentSource);
            try
            {
                uint program = _gl.CreateProgram();
                _gl.AttachShader(program, vertexShader);
                _gl.AttachShader(program, fragmentShader);
                _gl.LinkProgram(program);
                _gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int status);
                if (status != 0)
                {
                    if (shaderObjectCache is not null)
                    {
                        // Linking has copied the executable into the program.
                        // Detach shared load-scoped objects so deleting the
                        // cache at the end of Load actually releases them.
                        _gl.DetachShader(program, vertexShader);
                        _gl.DetachShader(program, fragmentShader);
                    }

                    return program;
                }

                string info = _gl.GetProgramInfoLog(program);
                _gl.DeleteProgram(program);
                throw new InvalidOperationException(
                    $"OpenGL program link failed: {info}");
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
    }

    private uint CompileShader(ShaderType type, string source)
    {
        uint shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);
        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int status);
        if (status == 0)
        {
            string info = _gl.GetShaderInfoLog(shader);
            _gl.DeleteShader(shader);
            throw new InvalidOperationException($"OpenGL {type} compile failed: {info}");
        }

        return shader;
    }

    private static TextureTarget ToGlTextureTarget(MapRenderTextureTarget target) => target switch
    {
        MapRenderTextureTarget.Texture2D => TextureTarget.Texture2D,
        MapRenderTextureTarget.TextureCube => TextureTarget.TextureCubeMap,
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
        uniform int uVegetationWindEnabled;
        uniform float uVegetationTime;
        uniform float uVegetationAmplitude;
        uniform float uVegetationAngularFrequency;
        uniform float uVegetationSpatialFrequency;
        uniform float uVegetationLocalMinimumHeight;
        uniform float uVegetationLocalHeightRange;

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
                uVegetationWindEnabled != 0 &&
                uVegetationLocalHeightRange > 0.0001)
            {
                float heightWeight = clamp(
                    (aPosition.z - uVegetationLocalMinimumHeight) /
                    uVegetationLocalHeightRange,
                    0.0,
                    1.0);
                heightWeight *= heightWeight;
                float phase =
                    uVegetationTime * uVegetationAngularFrequency +
                    renderPosition.x * uVegetationSpatialFrequency +
                    renderPosition.z * uVegetationSpatialFrequency * 1.37;
                float wave = (
                    sin(phase) +
                    0.35 * sin(phase * 0.61 + 1.7)) / 1.35;
                float sway =
                    uVegetationAmplitude * heightWeight * wave;
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
        uniform int uVegetationWindEnabled;
        uniform float uVegetationTime;
        uniform float uVegetationAmplitude;
        uniform float uVegetationAngularFrequency;
        uniform float uVegetationSpatialFrequency;
        uniform float uVegetationLocalMinimumHeight;
        uniform float uVegetationLocalHeightRange;

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
                uVegetationWindEnabled != 0 &&
                uVegetationLocalHeightRange > 0.0001)
            {
                float heightWeight = clamp(
                    (aPosition.z - uVegetationLocalMinimumHeight) /
                    uVegetationLocalHeightRange,
                    0.0,
                    1.0);
                heightWeight *= heightWeight;
                float phase =
                    uVegetationTime * uVegetationAngularFrequency +
                    renderPosition.x * uVegetationSpatialFrequency +
                    renderPosition.z * uVegetationSpatialFrequency * 1.37;
                float wave = (
                    sin(phase) +
                    0.35 * sin(phase * 0.61 + 1.7)) / 1.35;
                float sway =
                    uVegetationAmplitude * heightWeight * wave;
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
