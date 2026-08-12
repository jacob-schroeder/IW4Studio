using System.Numerics;
using IW4.Render.EditorPreview;
using IW4.Render.Execution;
using IW4.Render.Materials;
using IW4.Render.OpenGl.Programs;
using IW4.Render.OpenGl.Shaders;
using IW4.Render.Shaders;
using Silk.NET.OpenGL;

namespace IW4.Render.OpenGl;

/// <summary>
/// Context-local execution for one translated authored material pass. Map and
/// standalone asset viewers share this exact program, fixed-state, sampler,
/// and constant path; scene scheduling and runtime-resource ownership remain
/// with their respective callers.
/// </summary>
internal sealed class SilkOpenGlAuthoredMaterialExecutor
{
    private const string LinkProfileIdentity =
        MapRenderOpenGlSharedProgramCache.EditorPreviewLinkProfileIdentity;

    private readonly SilkOpenGlStateShadow _state;
    private readonly Func<
        string,
        string,
        MapRenderOpenGlLinkedProgramHandleResolution> _resolveLinkedProgram;
    private readonly RsxVertexGlsl330ProgramResolver _vertexResolver = new();
    private readonly RsxFragmentGlsl330ProgramResolver _fragmentResolver = new();
    private readonly MapRenderOpenGlUniformLocationCache _uniformLocations;
    private readonly Dictionary<MapRenderOpenGlProgramKey, GlRsxProgram>
        _programs = [];
    private readonly Dictionary<MapRenderOpenGlProgramKey, string>
        _programFailures = [];
    private readonly Dictionary<string, string> _failureDiagnostics =
        new(StringComparer.Ordinal);
    private long _semanticRequestCount;
    private long _uniqueLinkCount;
    private long _linkReuseCount;

    internal SilkOpenGlAuthoredMaterialExecutor(
        GL gl,
        SilkOpenGlStateShadow state,
        Func<string, string, MapRenderOpenGlLinkedProgramHandleResolution>
            resolveLinkedProgram)
    {
        ArgumentNullException.ThrowIfNull(gl);
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _resolveLinkedProgram = resolveLinkedProgram ??
            throw new ArgumentNullException(nameof(resolveLinkedProgram));
        _uniformLocations = new MapRenderOpenGlUniformLocationCache(
            gl.GetUniformLocation);
    }

    internal int ProgramCount => _programs.Count;

    internal int FailureCount =>
        _programFailures.Count + _failureDiagnostics.Count;

    internal long SemanticRequestCount => _semanticRequestCount;

    internal long UniqueLinkCount => _uniqueLinkCount;

    internal long LinkReuseCount => _linkReuseCount;

    internal MapRenderOpenGlUniformLocationCacheTelemetry
        UniformLocationTelemetry => _uniformLocations.CreateTelemetry();

    internal bool IsVertexProgramLowerable(
        MapRenderShaderExecutionContract execution) =>
        _vertexResolver.Resolve(execution).IsReady;

    internal bool IsFragmentProgramLowerable(
        MapRenderShaderExecutionContract execution) =>
        _fragmentResolver.Resolve(execution).IsReady;

    internal GlRsxProgram GetOrCreateProgram(
        MapRenderShaderExecutionContract execution,
        MapRenderState state,
        MapRenderEditorTranslatedProgramVertexConstantBindingPlan?
            staticModelVertexConstantPlan,
        bool useVertexPlacementDiagnostic,
        int? fragmentOutputDiagnostic,
        out string? blocker)
    {
        ArgumentNullException.ThrowIfNull(execution);
        blocker = null;

        RsxVertexGlsl330ProgramResolution vertexResolution =
            _vertexResolver.Resolve(execution);
        if (!vertexResolution.IsReady)
        {
            blocker = vertexResolution.FailureReason ??
                "OPENGL_RSX_VERTEX_LOWERING_FAILED";
            _failureDiagnostics.TryAdd(
                $"{execution.ProgramCacheKey}|openGlVertexLowering",
                blocker);
            return default;
        }

        RsxFragmentGlsl330ProgramResolution fragmentResolution =
            _fragmentResolver.Resolve(execution);
        if (!fragmentResolution.IsReady)
        {
            blocker = fragmentResolution.FailureReason ??
                "OPENGL_RSX_FRAGMENT_LOWERING_FAILED";
            _failureDiagnostics.TryAdd(
                $"{execution.ProgramCacheKey}|openGlFragmentLowering",
                blocker);
            return default;
        }

        if (!TryValidateState(state, out blocker))
            return default;
        if (!MapRenderOpenGlFixedFunctionEpilogue.TryCompose(
                state,
                execution.FragmentProgramControl,
                suppressShaderPackerForDiagnosticOutput:
                    useVertexPlacementDiagnostic,
                out MapRenderAlphaTestMode alphaTestMode,
                out MapRenderOpenGlRsxShaderPackerMode shaderPackerMode,
                out string fixedFunctionEpilogue))
        {
            blocker =
                $"renderStateAlphaTest=unsupportedTuple(func=0x{state.AlphaFunc:X4},ref=0x{state.AlphaRef:X2})";
            return default;
        }

        string vertexGlsl = vertexResolution.Glsl!;
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
                blocker = staticModelInstancingBlocker;
                _failureDiagnostics.TryAdd(
                    $"{execution.ProgramCacheKey}|staticModelInstancing",
                    blocker);
                return default;
            }
            staticModelInstancingReady = true;
        }

        string fixedFunctionIdentity =
            $"{execution.ProgramCacheKey}|alphaTest={alphaTestMode}" +
            $"|rsxShaderPacker={shaderPackerMode}" +
            (staticModelInstancingReady
                ? $"|staticModelInstancing={staticModelInstancingIdentity}"
                : string.Empty);
        string diagnosticIdentity = useVertexPlacementDiagnostic
            ? $"VERTEX_PLACEMENT_DIAGNOSTIC|{fixedFunctionIdentity}"
            : fragmentOutputDiagnostic.HasValue
                ? $"FRAGMENT_OUTPUT_{fragmentOutputDiagnostic.Value}_DIAGNOSTIC|{fixedFunctionIdentity}"
                : fixedFunctionIdentity;

        MapRenderOpenGlAuthoredFragmentSource finalPixelSource;
        try
        {
            MapRenderOpenGlAuthoredFragmentSource translatedPixelSource =
                MapRenderOpenGlFixedFunctionEpilogue.Apply(
                    fragmentResolution.Source!,
                    fixedFunctionEpilogue);
            finalPixelSource = useVertexPlacementDiagnostic
                ? translatedPixelSource.WithBackendComposition(
                    VertexPlacementDiagnosticFragmentSource)
                : RemapFragmentOutputForDiagnostic(
                    translatedPixelSource,
                    fragmentOutputDiagnostic);
        }
        catch (InvalidOperationException exception)
        {
            blocker =
                $"RSX GLSL source composition failed for {diagnosticIdentity}: {exception.Message}";
            _failureDiagnostics.TryAdd(
                $"{diagnosticIdentity}|sourceComposition",
                blocker);
            return default;
        }

        int[] samplerDestinations = useVertexPlacementDiagnostic
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
                LinkProfileIdentity,
                staticModelInstancingReady,
                samplerDestinations);
        if (_programs.TryGetValue(programKey, out GlRsxProgram cached))
            return cached;
        if (_programFailures.TryGetValue(programKey, out blocker))
            return default;

        _semanticRequestCount = checked(_semanticRequestCount + 1);
        MapRenderOpenGlLinkedProgramHandleResolution linkResolution =
            _resolveLinkedProgram(vertexGlsl, finalPixelSource.ExactGlsl);
        if (linkResolution.IsReuse)
            _linkReuseCount = checked(_linkReuseCount + 1);
        else if (linkResolution.IsReady)
            _uniqueLinkCount = checked(_uniqueLinkCount + 1);
        if (!linkResolution.IsReady)
        {
            blocker =
                $"RSX GLSL validation failed for {diagnosticIdentity} " +
                $"({programKey}): {linkResolution.FailureReason}";
            _programFailures.TryAdd(programKey, blocker);
            return default;
        }

        uint handle = linkResolution.Handle;
        int[] samplerLocations = samplerDestinations
            .Select(destination => _uniformLocations.Get(
                handle,
                $"rsxSampler{destination}"))
            .ToArray();
        _state.UseProgram(handle);
        for (int index = 0; index < samplerDestinations.Length; index++)
            _state.Uniform1(samplerLocations[index], samplerDestinations[index]);

        GlRsxVegetationUniformLocations? vegetationUniformLocations =
            staticModelInstancingReady
                ? new(
                    _uniformLocations.Get(
                        handle,
                        MapRenderOpenGlStaticModelInstancedVertexComposer
                            .VegetationWindEnabledUniform),
                    _uniformLocations.Get(
                        handle,
                        MapRenderOpenGlStaticModelInstancedVertexComposer
                            .VegetationTimeUniform),
                    _uniformLocations.Get(
                        handle,
                        MapRenderOpenGlStaticModelInstancedVertexComposer
                            .VegetationAmplitudeUniform),
                    _uniformLocations.Get(
                        handle,
                        MapRenderOpenGlStaticModelInstancedVertexComposer
                            .VegetationAngularFrequencyUniform),
                    _uniformLocations.Get(
                        handle,
                        MapRenderOpenGlStaticModelInstancedVertexComposer
                            .VegetationSpatialFrequencyUniform),
                    _uniformLocations.Get(
                        handle,
                        MapRenderOpenGlStaticModelInstancedVertexComposer
                            .VegetationLocalMinimumHeightUniform),
                    _uniformLocations.Get(
                        handle,
                        MapRenderOpenGlStaticModelInstancedVertexComposer
                            .VegetationLocalHeightRangeUniform))
                : null;
        if (vegetationUniformLocations is { IsReady: false })
        {
            blocker =
                "Translated static-model program lost its Live Preview vegetation uniform bridge.";
            _programFailures.TryAdd(programKey, blocker);
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
                    .Select(row => _uniformLocations.Get(
                        handle,
                        $"{MapRenderOpenGlStaticModelInstancedVertexComposer.ViewRowsUniform}[{row}]"))
                    .ToArray()
                : null,
            StaticModelViewProjectionRowLocations =
                staticModelInstancingReady
                    ? Enumerable.Range(0, 4)
                        .Select(row => _uniformLocations.Get(
                            handle,
                            $"{MapRenderOpenGlStaticModelInstancedVertexComposer.ViewProjectionRowsUniform}[{row}]"))
                        .ToArray()
                    : null,
            StaticModelEyeOffsetLocation = staticModelInstancingReady
                ? _uniformLocations.Get(
                    handle,
                    MapRenderOpenGlStaticModelInstancedVertexComposer
                        .EyeOffsetUniform)
                : -1,
            StaticModelVegetationUniformLocations = vegetationUniformLocations
        };
        _programs.Add(programKey, program);
        return program;
    }

    internal bool TryCreateConstantBindings(
        MapRenderShaderExecutionContract execution,
        GlRsxProgram program,
        MapRenderEditorTranslatedProgramDirectCodeConstantPlan directCodePlan,
        MapRenderEditorTranslatedProgramVertexConstantBindingPlan
            vertexConstantPlan,
        out GlRsxConstantBinding[] bindings,
        out string? blocker,
        Vector4? nonInstancedBaseLightingCoords = null)
    {
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentNullException.ThrowIfNull(directCodePlan);
        ArgumentNullException.ThrowIfNull(vertexConstantPlan);
        bindings = [];
        blocker = null;
        if (program.Handle == 0)
        {
            blocker = "OPENGL_AUTHORED_PROGRAM_NOT_LINKED";
            return false;
        }

        var result = new List<GlRsxConstantBinding>();
        foreach (MapRenderEditorTranslatedProgramVertexConstantBinding
                 constant in vertexConstantPlan.Bindings)
        {
            int location = _uniformLocations.Get(
                program.Handle,
                $"rsxVertexConst[{constant.Destination}]");
            if (constant.Kind ==
                MapRenderEditorTranslatedProgramVertexConstantBindingKind
                    .DerivedMatrixRow)
            {
                result.Add(new GlRsxConstantBinding(
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
                result.Add(new GlRsxConstantBinding(
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
                    .PerInstanceStaticModelBaseLightingCoords)
            {
                if (nonInstancedBaseLightingCoords is { } lightingCoords)
                {
                    result.Add(new GlRsxConstantBinding(
                        location,
                        lightingCoords.X,
                        lightingCoords.Y,
                        lightingCoords.Z,
                        lightingCoords.W,
                        null,
                        MapRenderCodeMatrixTransform.None,
                        -1));
                }
                // Map static-model instancing replaces this row with divisor
                // attribute 12. A standalone viewer supplies the exact native
                // dynamic-XModel cache entry explicitly.
                continue;
            }

            if (constant.Kind is
                MapRenderEditorTranslatedProgramVertexConstantBindingKind
                    .PerInstanceStaticModelLightProbeAmbient)
            {
                // Static-model instancing replaces this row with divisor
                // attribute 12. Standalone execution remains blocked until a
                // native light-probe row is supplied.
                continue;
            }

            if (constant.StaticValue is not { } value)
            {
                blocker =
                    $"vertexConstantDest{constant.Destination}=PLANNED_VALUE_UNRESOLVED";
                return false;
            }
            result.Add(new GlRsxConstantBinding(
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
                blocker =
                    $"codePixelRow0x{patchPlan.CodeIndex:X2}=DIRECT_SOURCE_UNRESOLVED";
                return false;
            }

            int location = _uniformLocations.Get(
                program.Handle,
                MapRenderOpenGlCodePixelConstantUniformLayout.ElementName(
                    patchPlan.CodeIndex));
            if (directCodePlan.IsDynamicSourceRow(patchPlan.CodeIndex))
            {
                result.Add(new GlRsxConstantBinding(
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
                blocker =
                    $"codePixelRow0x{patchPlan.CodeIndex:X2}=DIRECT_VALUE_UNRESOLVED";
                return false;
            }

            MapRenderShaderConstantValue value = row!.Value;
            result.Add(new GlRsxConstantBinding(
                location,
                value.X,
                value.Y,
                value.Z,
                value.W,
                null,
                MapRenderCodeMatrixTransform.None,
                -1));
        }

        bindings = result.ToArray();
        return true;
    }

    internal bool TryApplyConstantBindings(
        IReadOnlyList<GlRsxConstantBinding> bindings,
        MapRenderDerivedMatrixState matrices,
        float animationTimeSeconds,
        Func<ushort, int?, MapRenderShaderConstantValue?>
            resolveDynamicCodeConstant,
        out string? blocker)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(resolveDynamicCodeConstant);
        blocker = null;
        for (int index = 0; index < bindings.Count; index++)
        {
            GlRsxConstantBinding binding = bindings[index];
            if (binding.CodeMatrixSemantic.HasValue)
            {
                if (!TryResolveCodeMatrixRow(binding, matrices, out Vector4 row))
                {
                    blocker =
                        $"dynamicMatrix={binding.CodeMatrixSemantic}:{binding.CodeMatrixTransform}:row{binding.CodeMatrixRow}:UNRESOLVED";
                    return false;
                }
                _state.Uniform4(
                    binding.Location,
                    row.X,
                    row.Y,
                    row.Z,
                    row.W);
                continue;
            }

            if (binding.DynamicCodeConstantSourceRow is { } sourceRow)
            {
                MapRenderShaderConstantValue? dynamicValue =
                    sourceRow == FrameDirectCodeConstants.GameTimeRowIndex
                        ? FrameDirectCodeConstants.ProduceGameTime(
                            animationTimeSeconds).Value
                        : resolveDynamicCodeConstant(
                            sourceRow,
                            binding.SceneLightIndex);
                if (dynamicValue is not { } value)
                {
                    blocker =
                        $"dynamicCodeConstantRow0x{sourceRow:X2}=RUNTIME_VALUE_UNAVAILABLE";
                    return false;
                }
                _state.Uniform4(
                    binding.Location,
                    value.X,
                    value.Y,
                    value.Z,
                    value.W);
                continue;
            }

            if (binding.X.HasValue &&
                binding.Y.HasValue &&
                binding.Z.HasValue &&
                binding.W.HasValue)
            {
                _state.Uniform4(
                    binding.Location,
                    binding.X.Value,
                    binding.Y.Value,
                    binding.Z.Value,
                    binding.W.Value);
                continue;
            }

            blocker = "vertexConstant=PLANNED_VALUE_UNRESOLVED";
            return false;
        }

        return true;
    }

    internal void BindMaterialSamplers(
        IReadOnlyList<GlRsxSamplerBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        foreach (GlRsxSamplerBinding binding in bindings)
        {
            _state.ActiveTexture(binding.Destination);
            _state.BindSampler(checked((uint)binding.Destination), 0);
            _state.BindTexture(binding.Target, binding.Texture);
        }
    }

    internal bool TryApplyRenderState(
        MapRenderState state,
        out string? blocker)
    {
        if (!TryValidateState(state, out blocker))
            return false;

        _state.SetEnabled(EnableCap.FramebufferSrgb, false);
        _state.FrontFace(FrontFaceDirection.Ccw);
        if (state.DepthTestEnabled)
        {
            _state.SetEnabled(EnableCap.DepthTest, true);
            _state.DepthFunc(ToDepthFunction(state.DepthFunc));
        }
        else
        {
            _state.SetEnabled(EnableCap.DepthTest, false);
        }
        _state.DepthMask(state.DepthWriteEnabled);
        _state.SetEnabled(EnableCap.StencilTest, false);

        switch (MapRenderCull.Resolve(state))
        {
            case MapRenderCullMode.Disabled:
                _state.SetEnabled(EnableCap.CullFace, false);
                break;
            case MapRenderCullMode.Front:
                _state.SetEnabled(EnableCap.CullFace, true);
                _state.CullFace(TriangleFace.Front);
                break;
            case MapRenderCullMode.Back:
                _state.SetEnabled(EnableCap.CullFace, true);
                _state.CullFace(TriangleFace.Back);
                break;
        }

        _state.PolygonMode(state.PolygonMode == 0x1B01u
            ? PolygonMode.Line
            : PolygonMode.Fill);
        _state.ColorMask(
            (state.ColorMask & 0x0001_0000u) != 0,
            (state.ColorMask & 0x0000_0100u) != 0,
            (state.ColorMask & 0x0000_0001u) != 0,
            (state.ColorMask & 0x0100_0000u) != 0);

        if (state.BlendEnabled)
        {
            _state.SetEnabled(EnableCap.Blend, true);
            _state.BlendEquationSeparate(
                ToBlendEquation(state.BlendEquationRgb),
                ToBlendEquation(state.BlendEquationAlpha));
            _state.BlendFuncSeparate(
                ToBlendFactor(state.BlendSourceRgb),
                ToBlendFactor(state.BlendDestinationRgb),
                ToBlendFactor(state.BlendSourceAlpha),
                ToBlendFactor(state.BlendDestinationAlpha));
        }
        else
        {
            _state.SetEnabled(EnableCap.Blend, false);
        }

        if (state.PolygonOffsetEnabled)
        {
            _state.SetEnabled(EnableCap.PolygonOffsetFill, true);
            _state.PolygonOffset(
                state.PolygonOffsetFactor,
                state.PolygonOffsetUnits);
        }
        else
        {
            _state.SetEnabled(EnableCap.PolygonOffsetFill, false);
        }
        return true;
    }

    internal void Clear()
    {
        _programs.Clear();
        _programFailures.Clear();
        _failureDiagnostics.Clear();
        _uniformLocations.Clear();
        _semanticRequestCount = 0;
        _uniqueLinkCount = 0;
        _linkReuseCount = 0;
    }

    private static bool TryValidateState(
        MapRenderState state,
        out string? blocker)
    {
        blocker = null;
        IReadOnlyList<string> sharedBlockers =
            MapRenderStateExecutionCapability.FindBlockers(state);
        if (sharedBlockers.Count != 0)
        {
            blocker = string.Join('|', sharedBlockers);
            return false;
        }
        if (state.Stencil.Enabled)
        {
            blocker =
                "renderStateStencil=MRT_WRITE_MASK_AND_FACE_CONVENTION_UNAVAILABLE";
            return false;
        }
        if (state.PolygonMode is not (0x1B01u or 0x1B02u))
        {
            blocker =
                $"renderStatePolygonMode=unsupportedValue(0x{state.PolygonMode:X4})";
            return false;
        }
        if (state.DepthTestEnabled && !IsDepthFunction(state.DepthFunc))
        {
            blocker =
                $"renderStateDepthFunc=unsupportedValue(0x{state.DepthFunc:X4})";
            return false;
        }
        if (state.BlendEnabled &&
            (!IsBlendEquation(state.BlendEquationRgb) ||
             !IsBlendEquation(state.BlendEquationAlpha) ||
             !IsBlendFactor(state.BlendSourceRgb) ||
             !IsBlendFactor(state.BlendDestinationRgb) ||
             !IsBlendFactor(state.BlendSourceAlpha) ||
             !IsBlendFactor(state.BlendDestinationAlpha)))
        {
            blocker =
                $"renderStateBlend=unsupportedTuple(eqRgb=0x{state.BlendEquationRgb:X4},eqA=0x{state.BlendEquationAlpha:X4},srcRgb=0x{state.BlendSourceRgb:X4},dstRgb=0x{state.BlendDestinationRgb:X4},srcA=0x{state.BlendSourceAlpha:X4},dstA=0x{state.BlendDestinationAlpha:X4})";
            return false;
        }
        if (!float.IsFinite(state.PolygonOffsetFactor) ||
            !float.IsFinite(state.PolygonOffsetUnits))
        {
            blocker = "renderStatePolygonOffset=NONFINITE";
            return false;
        }
        return true;
    }

    private static bool TryResolveCodeMatrixRow(
        GlRsxConstantBinding binding,
        MapRenderDerivedMatrixState matrices,
        out Vector4 row)
    {
        if (!binding.CodeMatrixSemantic.HasValue ||
            !MapRenderDerivedMatrixResolver.TryResolve(
                matrices,
                binding.CodeMatrixSemantic.Value,
                out Matrix4x4 matrix))
        {
            row = default;
            return false;
        }
        if (binding.CodeMatrixTransform is
            MapRenderCodeMatrixTransform.Inverse or
            MapRenderCodeMatrixTransform.InverseTranspose)
        {
            if (!Matrix4x4.Invert(matrix, out matrix))
            {
                row = default;
                return false;
            }
        }
        if (binding.CodeMatrixTransform is
            MapRenderCodeMatrixTransform.Transpose or
            MapRenderCodeMatrixTransform.InverseTranspose)
        {
            matrix = Matrix4x4.Transpose(matrix);
        }
        row = binding.CodeMatrixRow switch
        {
            0 => new Vector4(matrix.M11, matrix.M12, matrix.M13, matrix.M14),
            1 => new Vector4(matrix.M21, matrix.M22, matrix.M23, matrix.M24),
            2 => new Vector4(matrix.M31, matrix.M32, matrix.M33, matrix.M34),
            3 => new Vector4(matrix.M41, matrix.M42, matrix.M43, matrix.M44),
            _ => default
        };
        return binding.CodeMatrixRow is >= 0 and <= 3;
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

    private static bool IsDepthFunction(uint value) =>
        value is >= 0x0200u and <= 0x0207u;

    private static DepthFunction ToDepthFunction(uint value) => value switch
    {
        0x0200u => DepthFunction.Never,
        0x0201u => DepthFunction.Less,
        0x0202u => DepthFunction.Equal,
        0x0203u => DepthFunction.Lequal,
        0x0204u => DepthFunction.Greater,
        0x0205u => DepthFunction.Notequal,
        0x0206u => DepthFunction.Gequal,
        0x0207u => DepthFunction.Always,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static bool IsBlendEquation(uint value) =>
        value is 0x8006u or 0x800Au or 0x800Bu or 0x8007u or 0x8008u;

    private static BlendEquationModeEXT ToBlendEquation(uint value) =>
        value switch
        {
            0x8006u => BlendEquationModeEXT.FuncAdd,
            0x800Au => BlendEquationModeEXT.FuncSubtract,
            0x800Bu => BlendEquationModeEXT.FuncReverseSubtract,
            0x8007u => BlendEquationModeEXT.Min,
            0x8008u => BlendEquationModeEXT.Max,
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };

    private static bool IsBlendFactor(uint value) =>
        value is 0u or 1u or >= 0x0300u and <= 0x0307u;

    private static BlendingFactor ToBlendFactor(uint value) => value switch
    {
        0u => BlendingFactor.Zero,
        1u => BlendingFactor.One,
        0x0300u => BlendingFactor.SrcColor,
        0x0301u => BlendingFactor.OneMinusSrcColor,
        0x0302u => BlendingFactor.SrcAlpha,
        0x0303u => BlendingFactor.OneMinusSrcAlpha,
        0x0304u => BlendingFactor.DstAlpha,
        0x0305u => BlendingFactor.OneMinusDstAlpha,
        0x0306u => BlendingFactor.DstColor,
        0x0307u => BlendingFactor.OneMinusDstColor,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

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
}
