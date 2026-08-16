using IW4.Render.Techniques;
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
        OpenGlSharedProgramCache.LinkProfileIdentity;

    private readonly SilkOpenGlStateShadow _state;
    private readonly Func<
        string,
        string,
        OpenGlLinkedProgramHandleResolution> _resolveLinkedProgram;
    private readonly RsxVertexGlsl330ProgramResolver _vertexResolver = new();
    private readonly RsxFragmentGlsl330ProgramResolver _fragmentResolver = new();
    private readonly OpenGlUniformLocationCache _uniformLocations;
    private readonly Dictionary<OpenGlProgramKey, GlRsxProgram>
        _programs = [];
    private readonly Dictionary<OpenGlProgramKey, string>
        _programFailures = [];
    private readonly Dictionary<string, string> _failureDiagnostics =
        new(StringComparer.Ordinal);
    private long _semanticRequestCount;
    private long _uniqueLinkCount;
    private long _linkReuseCount;

    internal SilkOpenGlAuthoredMaterialExecutor(
        GL gl,
        SilkOpenGlStateShadow state,
        Func<string, string, OpenGlLinkedProgramHandleResolution>
            resolveLinkedProgram)
    {
        ArgumentNullException.ThrowIfNull(gl);
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _resolveLinkedProgram = resolveLinkedProgram ??
            throw new ArgumentNullException(nameof(resolveLinkedProgram));
        _uniformLocations = new OpenGlUniformLocationCache(
            gl.GetUniformLocation);
    }

    internal int ProgramCount => _programs.Count;

    internal int FailureCount =>
        _programFailures.Count + _failureDiagnostics.Count;

    internal long SemanticRequestCount => _semanticRequestCount;

    internal long UniqueLinkCount => _uniqueLinkCount;

    internal long LinkReuseCount => _linkReuseCount;

    internal OpenGlUniformLocationCacheTelemetry
        UniformLocationTelemetry => _uniformLocations.CreateTelemetry();

    internal bool IsVertexProgramLowerable(
        ShaderExecutionContract execution) =>
        _vertexResolver.Resolve(execution).IsReady;

    internal bool IsFragmentProgramLowerable(
        ShaderExecutionContract execution) =>
        _fragmentResolver.Resolve(execution).IsReady;

    internal bool TryResolveRawProgramSources(
        ShaderExecutionContract execution,
        RenderState state,
        out string vertexGlsl,
        out OpenGlAuthoredFragmentSource fragmentSource,
        out string? blocker)
    {
        ArgumentNullException.ThrowIfNull(execution);
        vertexGlsl = string.Empty;
        fragmentSource = null!;
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
            return false;
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
            return false;
        }

        if (!TryValidateState(state, out blocker))
            return false;
        vertexGlsl = vertexResolution.Glsl!;
        fragmentSource = fragmentResolution.Source!;
        return true;
    }

    internal GlRsxProgram GetOrCreateProgram(
        ShaderExecutionContract execution,
        RenderState state,
        out string? blocker)
    {
        if (!TryResolveRawProgramSources(
                execution,
                state,
                out string vertexGlsl,
                out OpenGlAuthoredFragmentSource fragmentSource,
                out blocker))
        {
            return default;
        }

        if (!OpenGlFixedFunctionEpilogue.TryCompose(
                state,
                execution.FragmentProgramControl,
                suppressShaderPackerForDiagnosticOutput: false,
                out AlphaTestMode alphaTestMode,
                out OpenGlRsxShaderPackerMode shaderPackerMode,
                out string fixedFunctionEpilogue))
        {
            blocker =
                $"renderStateAlphaTest=unsupportedTuple(func=0x{(uint)state.AlphaFunc:X4},ref=0x{state.AlphaRef:X2})";
            return default;
        }

        string fixedFunctionIdentity =
            $"{execution.ProgramCacheKey}|alphaTest={alphaTestMode}" +
            $"|rsxShaderPacker={shaderPackerMode}";

        OpenGlAuthoredFragmentSource finalPixelSource;
        try
        {
            finalPixelSource = OpenGlFixedFunctionEpilogue.Apply(
                fragmentSource,
                fixedFunctionEpilogue);
        }
        catch (InvalidOperationException exception)
        {
            blocker =
                $"RSX GLSL source composition failed for {fixedFunctionIdentity}: {exception.Message}";
            _failureDiagnostics.TryAdd(
                $"{fixedFunctionIdentity}|sourceComposition",
                blocker);
            return default;
        }

        return GetOrCreateComposedProgram(
            vertexGlsl,
            finalPixelSource,
            usesVertexSourceVariant: false,
            ResolveSamplerDestinations(execution),
            fixedFunctionIdentity,
            postLinkValidator: null,
            out _,
            out blocker);
    }

    internal GlRsxProgram GetOrCreateComposedProgram(
        string vertexGlsl,
        OpenGlAuthoredFragmentSource pixelSource,
        bool usesVertexSourceVariant,
        IReadOnlyList<int> sortedSamplerDestinations,
        string diagnosticIdentity,
        Func<uint, OpenGlProgramKey, string?>? postLinkValidator,
        out OpenGlProgramKey programKey,
        out string? blocker)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vertexGlsl);
        ArgumentNullException.ThrowIfNull(pixelSource);
        ArgumentNullException.ThrowIfNull(sortedSamplerDestinations);
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnosticIdentity);

        int[] samplerDestinations = sortedSamplerDestinations.ToArray();
        programKey = OpenGlDirectProgramKeyFactory.Create(
            vertexGlsl,
            pixelSource.ExactGlsl,
            LinkProfileIdentity,
            usesVertexSourceVariant,
            samplerDestinations);
        blocker = null;
        if (_programs.TryGetValue(programKey, out GlRsxProgram cached))
            return cached;
        if (_programFailures.TryGetValue(programKey, out blocker))
            return default;

        _semanticRequestCount = checked(_semanticRequestCount + 1);
        OpenGlLinkedProgramHandleResolution linkResolution =
            _resolveLinkedProgram(vertexGlsl, pixelSource.ExactGlsl);
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

        if (postLinkValidator?.Invoke(handle, programKey) is
            { } validationBlocker)
        {
            blocker = validationBlocker;
            _programFailures.TryAdd(programKey, blocker);
            return default;
        }

        var program = new GlRsxProgram(
            handle,
            samplerDestinations,
            samplerLocations);
        _programs.Add(programKey, program);
        return program;
    }

    internal int GetUniformLocation(uint handle, string name) =>
        _uniformLocations.Get(handle, name);

    internal void RecordPreparationFailure(string key, string blocker)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(blocker);
        _failureDiagnostics.TryAdd(key, blocker);
    }

    internal bool TryCreateConstantBindings(
        ShaderExecutionContract execution,
        GlRsxProgram program,
        TranslatedProgramDirectCodeConstantPlan directCodePlan,
        TranslatedProgramVertexConstantBindingPlan
            vertexConstantPlan,
        out GlRsxConstantBinding[] bindings,
        out string? blocker,
        IReadOnlyDictionary<int, Vector4>? vertexConstantOverrides = null,
        IReadOnlySet<int>? externallyBoundVertexConstantDestinations = null)
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
        foreach (TranslatedProgramVertexConstantBinding
                 constant in vertexConstantPlan.Bindings)
        {
            if (externallyBoundVertexConstantDestinations?.Contains(
                    constant.Destination) == true)
            {
                continue;
            }

            int location = _uniformLocations.Get(
                program.Handle,
                $"rsxVertexConst[{constant.Destination}]");
            if (vertexConstantOverrides?.TryGetValue(
                    constant.Destination,
                    out Vector4 overrideValue) == true)
            {
                result.Add(new GlRsxConstantBinding(
                    location,
                    overrideValue.X,
                    overrideValue.Y,
                    overrideValue.Z,
                    overrideValue.W,
                    null,
                    CodeMatrixTransform.None,
                    -1));
                continue;
            }

            if (constant.Kind ==
                TranslatedProgramVertexConstantBindingKind
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
                TranslatedProgramVertexConstantBindingKind
                    .DynamicGameTime or
                TranslatedProgramVertexConstantBindingKind
                    .DynamicSceneLightPosition or
                TranslatedProgramVertexConstantBindingKind
                    .DynamicSceneLightShadow or
                TranslatedProgramVertexConstantBindingKind
                    .DynamicSunShadowProjection or
                TranslatedProgramVertexConstantBindingKind
                    .DynamicClipSpaceLookup or
                TranslatedProgramVertexConstantBindingKind
                    .DynamicZNear)
            {
                result.Add(new GlRsxConstantBinding(
                    location,
                    null,
                    null,
                    null,
                    null,
                    null,
                    CodeMatrixTransform.None,
                    -1,
                    constant.DynamicCodeConstantSourceRow,
                    constant.Kind is
                        TranslatedProgramVertexConstantBindingKind
                            .DynamicSceneLightPosition or
                        TranslatedProgramVertexConstantBindingKind
                            .DynamicSceneLightShadow
                        ? directCodePlan.SceneLightIndex
                        : null));
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
                CodeMatrixTransform.None,
                -1));
        }

        foreach (CodePixelConstantPatchPlan patchPlan in execution
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
                OpenGlCodePixelConstantUniformLayout.ElementName(
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
                    CodeMatrixTransform.None,
                    -1,
                    patchPlan.CodeIndex,
                    TranslatedProgramDirectCodeConstantPlanner
                        .IsDynamicSceneLightSourceRow(
                            patchPlan.CodeIndex)
                        ? directCodePlan.SceneLightIndex
                        : null));
                continue;
            }
            if (!directCodePlan.TryGetRow(
                    patchPlan.CodeIndex,
                    out DirectCodeConstantRow? row))
            {
                blocker =
                    $"codePixelRow0x{patchPlan.CodeIndex:X2}=DIRECT_VALUE_UNRESOLVED";
                return false;
            }

            ShaderConstantValue value = row!.Value;
            result.Add(new GlRsxConstantBinding(
                location,
                value.X,
                value.Y,
                value.Z,
                value.W,
                null,
                CodeMatrixTransform.None,
                -1));
        }

        bindings = result.ToArray();
        return true;
    }

    internal bool TryApplyConstantBindings(
        IReadOnlyList<GlRsxConstantBinding> bindings,
        DerivedMatrixState matrices,
        float animationTimeSeconds,
        Func<ushort, int?, ShaderConstantValue?>
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
                ShaderConstantValue? dynamicValue =
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
        RenderState state,
        MapRenderOpenGlStencilTargetContract? stencilTargetContract,
        out string? blocker)
    {
        if (!TryValidateState(state, out blocker))
            return false;
        MapRenderOpenGlStencilTargetContract? activeStencilTarget = null;
        if (state.Stencil.Enabled)
        {
            if (stencilTargetContract is null)
            {
                blocker = "renderStateStencil=SCENE_D24S8_TARGET_REQUIRED";
                return false;
            }
            activeStencilTarget = stencilTargetContract;
        }

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
        if (activeStencilTarget is { } stencilTarget)
        {
            ApplyStencilFace(
                stencilTarget.Ps3FrontHostFace,
                stencilTarget.FrontWriteMask,
                state.Stencil.Front);
            ApplyStencilFace(
                stencilTarget.Ps3BackHostFace,
                stencilTarget.BackWriteMask,
                state.Stencil.Back);
            _state.SetEnabled(EnableCap.StencilTest, true);
        }
        else
        {
            _state.SetEnabled(EnableCap.StencilTest, false);
        }

        switch (Cull.Resolve(state))
        {
            case CullMode.Disabled:
                _state.SetEnabled(EnableCap.CullFace, false);
                break;
            case CullMode.Front:
                _state.SetEnabled(EnableCap.CullFace, true);
                _state.CullFace(TriangleFace.Front);
                break;
            case CullMode.Back:
                _state.SetEnabled(EnableCap.CullFace, true);
                _state.CullFace(TriangleFace.Back);
                break;
        }

        _state.PolygonMode(state.PolygonMode == RsxPolygonMode.Line
            ? PolygonMode.Line
            : PolygonMode.Fill);
        _state.ColorMask(
            (state.ColorMask & RsxColorMask.Red) != 0,
            (state.ColorMask & RsxColorMask.Green) != 0,
            (state.ColorMask & RsxColorMask.Blue) != 0,
            (state.ColorMask & RsxColorMask.Alpha) != 0);

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

        switch (state.PolygonOffsetMode)
        {
            case RenderPolygonOffsetMode.Disabled:
                _state.SetEnabled(EnableCap.PolygonOffsetFill, false);
                break;
            case RenderPolygonOffsetMode.Explicit:
                _state.SetEnabled(EnableCap.PolygonOffsetFill, true);
                _state.PolygonOffset(
                    state.PolygonOffsetFactor,
                    state.PolygonOffsetUnits);
                break;
            case RenderPolygonOffsetMode.Inherit:
                break;
        }
        return true;
    }

    private void ApplyStencilFace(
        TriangleFace hostFace,
        uint writeMask,
        StencilFaceState face)
    {
        _state.StencilMaskSeparate(hostFace, writeMask);
        _state.StencilFuncSeparate(
            hostFace,
            ToStencilFunction(face.Function),
            face.Reference,
            face.CompareMask);
        _state.StencilOpSeparate(
            hostFace,
            ToStencilOperation(face.FailOperation),
            ToStencilOperation(face.DepthFailOperation),
            ToStencilOperation(face.PassOperation));
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

    private static int[] ResolveSamplerDestinations(
        ShaderExecutionContract execution) =>
        execution.MaterialSamplerDestinations
            .Concat(execution.CustomSamplerDestinations)
            .Concat(execution.CodeSamplerDestinations)
            .Select(binding => (int)binding.Destination)
            .Distinct()
            .Order()
            .ToArray();

    private static bool TryValidateState(
        RenderState state,
        out string? blocker)
    {
        blocker = null;
        IReadOnlyList<string> sharedBlockers =
            RenderStateExecutionCapability.FindBlockers(state);
        if (sharedBlockers.Count != 0)
        {
            blocker = string.Join('|', sharedBlockers);
            return false;
        }
        if (!Enum.IsDefined(state.PolygonMode))
        {
            blocker =
                $"renderStatePolygonMode=unsupportedValue(0x{(uint)state.PolygonMode:X4})";
            return false;
        }
        if (state.DepthTestEnabled && !IsDepthFunction(state.DepthFunc))
        {
            blocker =
                $"renderStateDepthFunc=unsupportedValue(0x{(uint)state.DepthFunc:X4})";
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
                $"renderStateBlend=unsupportedTuple(eqRgb=0x{(uint)state.BlendEquationRgb:X4},eqA=0x{(uint)state.BlendEquationAlpha:X4},srcRgb=0x{(uint)state.BlendSourceRgb:X4},dstRgb=0x{(uint)state.BlendDestinationRgb:X4},srcA=0x{(uint)state.BlendSourceAlpha:X4},dstA=0x{(uint)state.BlendDestinationAlpha:X4})";
            return false;
        }
        return true;
    }

    private static bool TryResolveCodeMatrixRow(
        GlRsxConstantBinding binding,
        DerivedMatrixState matrices,
        out Vector4 row)
    {
        if (!binding.CodeMatrixSemantic.HasValue ||
            !DerivedMatrixResolver.TryResolve(
                matrices,
                binding.CodeMatrixSemantic.Value,
                out Matrix4x4 matrix))
        {
            row = default;
            return false;
        }
        if (binding.CodeMatrixTransform is
            CodeMatrixTransform.Inverse or
            CodeMatrixTransform.InverseTranspose)
        {
            if (!Matrix4x4.Invert(matrix, out matrix))
            {
                row = default;
                return false;
            }
        }
        if (binding.CodeMatrixTransform is
            CodeMatrixTransform.Transpose or
            CodeMatrixTransform.InverseTranspose)
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

    private static bool IsDepthFunction(RsxCompareFunction value) =>
        Enum.IsDefined(value);

    private static DepthFunction ToDepthFunction(RsxCompareFunction value) =>
        value switch
    {
        RsxCompareFunction.Never => DepthFunction.Never,
        RsxCompareFunction.Less => DepthFunction.Less,
        RsxCompareFunction.Equal => DepthFunction.Equal,
        RsxCompareFunction.LessThanOrEqual => DepthFunction.Lequal,
        RsxCompareFunction.Greater => DepthFunction.Greater,
        RsxCompareFunction.NotEqual => DepthFunction.Notequal,
        RsxCompareFunction.GreaterThanOrEqual => DepthFunction.Gequal,
        RsxCompareFunction.Always => DepthFunction.Always,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static StencilFunction ToStencilFunction(
        RsxCompareFunction value) =>
        value switch
        {
            RsxCompareFunction.Never => StencilFunction.Never,
            RsxCompareFunction.Less => StencilFunction.Less,
            RsxCompareFunction.Equal => StencilFunction.Equal,
            RsxCompareFunction.LessThanOrEqual => StencilFunction.Lequal,
            RsxCompareFunction.Greater => StencilFunction.Greater,
            RsxCompareFunction.NotEqual => StencilFunction.Notequal,
            RsxCompareFunction.GreaterThanOrEqual => StencilFunction.Gequal,
            RsxCompareFunction.Always => StencilFunction.Always,
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };

    private static StencilOp ToStencilOperation(
        RsxStencilOperation value) =>
        value switch
        {
            RsxStencilOperation.Zero => StencilOp.Zero,
            RsxStencilOperation.Invert => StencilOp.Invert,
            RsxStencilOperation.Keep => StencilOp.Keep,
            RsxStencilOperation.Replace => StencilOp.Replace,
            RsxStencilOperation.IncrementSaturate => StencilOp.Incr,
            RsxStencilOperation.DecrementSaturate => StencilOp.Decr,
            RsxStencilOperation.IncrementWrap => StencilOp.IncrWrap,
            RsxStencilOperation.DecrementWrap => StencilOp.DecrWrap,
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };

    private static bool IsBlendEquation(RsxBlendEquation value) =>
        Enum.IsDefined(value);

    private static BlendEquationModeEXT ToBlendEquation(
        RsxBlendEquation value) =>
        value switch
        {
            RsxBlendEquation.Add => BlendEquationModeEXT.FuncAdd,
            RsxBlendEquation.Subtract => BlendEquationModeEXT.FuncSubtract,
            RsxBlendEquation.ReverseSubtract =>
                BlendEquationModeEXT.FuncReverseSubtract,
            RsxBlendEquation.Minimum => BlendEquationModeEXT.Min,
            RsxBlendEquation.Maximum => BlendEquationModeEXT.Max,
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };

    private static bool IsBlendFactor(RsxBlendFactor value) =>
        Enum.IsDefined(value);

    private static BlendingFactor ToBlendFactor(RsxBlendFactor value) =>
        value switch
    {
        RsxBlendFactor.Zero => BlendingFactor.Zero,
        RsxBlendFactor.One => BlendingFactor.One,
        RsxBlendFactor.SourceColor => BlendingFactor.SrcColor,
        RsxBlendFactor.OneMinusSourceColor => BlendingFactor.OneMinusSrcColor,
        RsxBlendFactor.SourceAlpha => BlendingFactor.SrcAlpha,
        RsxBlendFactor.OneMinusSourceAlpha => BlendingFactor.OneMinusSrcAlpha,
        RsxBlendFactor.DestinationAlpha => BlendingFactor.DstAlpha,
        RsxBlendFactor.OneMinusDestinationAlpha =>
            BlendingFactor.OneMinusDstAlpha,
        RsxBlendFactor.DestinationColor => BlendingFactor.DstColor,
        RsxBlendFactor.OneMinusDestinationColor =>
            BlendingFactor.OneMinusDstColor,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

}
