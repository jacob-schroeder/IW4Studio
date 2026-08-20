using System.Collections.Immutable;
using System.Text;
using IW4.Render.Materials;
using IW4.Render.Shaders;
using IW4.Render.Techniques;

namespace IW4.Render.Metal.Shaders;

/// <summary>
/// Direct Metal lowering of immutable RSX fragment semantics.
/// </summary>
internal static class RsxFragmentMslLowerer
{
    internal static RsxFragmentMslLoweringResult Lower(
        RsxFragmentProgramIr program)
        => LowerCore(
            program,
            fixedFunction: null,
            initialBlockers: null,
            emulateDepth24: false);

    internal static RsxFragmentMslLoweringResult Lower(
        RsxFragmentProgramIr program,
        RenderState renderState,
        bool suppressShaderPackerForDiagnosticOutput,
        FragmentTargetOutputAvailability? targetOutputs = null,
        bool emulateDepth24 = false,
        bool useVertexPlacementDiagnostic = false,
        int? fragmentOutputDiagnostic = null)
    {
        ArgumentNullException.ThrowIfNull(program);
        if (fragmentOutputDiagnostic is < 0 or > 3)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fragmentOutputDiagnostic));
        }

        var blockers = new SortedSet<string>(StringComparer.Ordinal);
        FixedFunctionPlan? fixedFunction = CreateFixedFunctionPlan(
            renderState,
            program.ProgramControl.EmittedControl,
            suppressShaderPackerForDiagnosticOutput ||
                useVertexPlacementDiagnostic,
            blockers);
        RsxFragmentMslLoweringResult result = LowerCore(
            program,
            fixedFunction,
            blockers,
            emulateDepth24 && EffectiveDepthComparisonOrWriteEnabled(
                renderState),
            targetOutputs,
            fragmentOutputDiagnostic);
        if (!useVertexPlacementDiagnostic)
            return result;

        return result with
        {
            Msl = BuildVertexPlacementDiagnosticMsl(),
            SampledDestinations = ImmutableArray<int>.Empty,
            ColorAttachmentIndices = ImmutableArray.Create(0),
            ExportsDepth = false
        };
    }

    private static RsxFragmentMslLoweringResult LowerCore(
        RsxFragmentProgramIr program,
        FixedFunctionPlan? fixedFunction,
        SortedSet<string>? initialBlockers,
        bool emulateDepth24,
        FragmentTargetOutputAvailability? targetOutputs = null,
        int? fragmentOutputDiagnostic = null)
    {
        ArgumentNullException.ThrowIfNull(program);

        var blockers = initialBlockers ??
            new SortedSet<string>(StringComparer.Ordinal);
        if (!program.HasValidUpload)
            blockers.Add("pixelUploadHeader=invalid");
        if (!program.ProgramControl.IsValid)
            blockers.Add("fragmentProgramControl=invalid");
        AddAmbiguousSamplerShapeBlockers(
            program.SamplerFeatureProfile,
            blockers);
        if (program.Instructions.IsEmpty)
            blockers.Add("pixelInstructions=missing");
        if (program.Instructions.IsEmpty)
            return CreateResult(msl: null, blockers);

        ImmutableArray<int> declaredColorAttachments = program.ColorExports
            .Where(export =>
                export.WrittenComponentMask != RsxFragmentWriteMask.None)
            .Select(export => export.ColorTarget)
            .Distinct()
            .Order()
            .ToImmutableArray();
        foreach (int colorTarget in declaredColorAttachments)
        {
            if (colorTarget is < 0 or > 3)
            {
                blockers.Add(
                    $"fragmentColorTarget{colorTarget}=unsupported");
            }
        }
        if (targetOutputs is { HasKnownNativeOutputCount: false })
        {
            blockers.Add(
                "fragmentColorTargets=unmappedSurfaceTargetTopology");
        }
        else if (targetOutputs?.NativeOutputCount is { } nativeOutputCount &&
                 nativeOutputCount > targetOutputs.HostDrawBufferCount)
        {
            blockers.Add(
                "fragmentMrtTargets=unsupportedHostTopology");
        }
        // Register writes identify possible exports, but the bound RSX
        // surface target decides which outputs are active. Keep executing the
        // complete register program while declaring only active host colors.
        ImmutableArray<int> colorAttachments =
            fragmentOutputDiagnostic.HasValue
                ? ImmutableArray.Create(0)
                : declaredColorAttachments
                    .Where(colorTarget =>
                        colorTarget is >= 0 and <= 3 &&
                        (targetOutputs is null ||
                         targetOutputs.IsNativeOutputActive(colorTarget) &&
                         targetOutputs.IsHostDrawBufferAvailable(colorTarget)))
                    .ToImmutableArray();
        string msl = BuildMsl(
            program.Instructions,
            program.SamplerFeatureProfile,
            program.ProgramControl.IsValid
                ? program.ProgramControl.EmittedFlags
                : null,
            blockers,
            fixedFunction,
            colorAttachments,
            emulateDepth24,
            fragmentOutputDiagnostic);
        return CreateResult(msl, blockers) with
        {
            AlphaTestMode = fixedFunction?.AlphaTestMode ??
                AlphaTestMode.Disabled,
            ShaderPackerMode = fixedFunction?.ShaderPackerMode ??
                MetalRsxShaderPackerMode.DisabledByState,
            SampledDestinations = program.SamplerUses
                .Select(use => use.Destination)
                .Distinct()
                .Order()
                .ToImmutableArray(),
            ColorAttachmentIndices = colorAttachments,
            ExportsDepth = program.DepthExportEnabled
        };
    }

    private static RsxFragmentMslLoweringResult CreateResult(
        string? msl,
        SortedSet<string> blockers)
    {
        ImmutableArray<string> immutableBlockers = blockers.ToImmutableArray();
        return new RsxFragmentMslLoweringResult(
            msl,
            msl is not null &&
            !immutableBlockers.Any(IsLoweringBlocker),
            immutableBlockers);
    }

    private static bool IsLoweringBlocker(string blocker) =>
        blocker.Contains("invalid", StringComparison.Ordinal) ||
        blocker.Contains("missing", StringComparison.Ordinal) ||
        blocker.Contains("unsupported", StringComparison.Ordinal) ||
        blocker.Contains("unlowered", StringComparison.Ordinal) ||
        blocker.Contains("unmapped", StringComparison.Ordinal);

    private static void AddAmbiguousSamplerShapeBlockers(
        RsxFragmentSamplerFeatureProfile profile,
        ISet<string> blockers)
    {
        foreach (RsxFragmentSamplerFeature entry in profile.Entries)
        {
            bool cube = HasFeature(
                entry.Features,
                RsxFragmentSamplerFeatures.Cube);
            bool shadow = HasFeature(
                entry.Features,
                RsxFragmentSamplerFeatures.Shadow);
            bool volume = HasFeature(
                entry.Features,
                RsxFragmentSamplerFeatures.Volume);
            if (cube && shadow)
            {
                blockers.Add(
                    $"fragmentSamplerDest{entry.Destination}=unsupportedAmbiguousCubeAndShadowShape");
            }
            if (volume && (cube || shadow))
            {
                blockers.Add(
                    $"fragmentSamplerDest{entry.Destination}=unsupportedAmbiguousVolumeShape");
            }
        }
    }

    internal static string BuildMsl(
        IReadOnlyList<RsxFragmentInstruction> instructions,
        RsxFragmentSamplerFeatureProfile samplerProfile,
        RsxFragmentProgramControlFlags? fragmentProgramControl,
        ISet<string> blockers)
        => BuildMsl(
            instructions,
            samplerProfile,
            fragmentProgramControl,
            blockers,
            fixedFunction: null,
            colorAttachments: [0, 1, 2, 3],
            emulateDepth24: false,
            fragmentOutputDiagnostic: null);

    private static string BuildMsl(
        IReadOnlyList<RsxFragmentInstruction> instructions,
        RsxFragmentSamplerFeatureProfile samplerProfile,
        RsxFragmentProgramControlFlags? fragmentProgramControl,
        ISet<string> blockers,
        FixedFunctionPlan? fixedFunction,
        IReadOnlyList<int> colorAttachments,
        bool emulateDepth24,
        int? fragmentOutputDiagnostic)
    {
        ArgumentNullException.ThrowIfNull(instructions);
        ArgumentNullException.ThrowIfNull(samplerProfile);
        ArgumentNullException.ThrowIfNull(blockers);

        FragmentRegisterUsage registerUsage = ReadFragmentRegisterUsage(
            instructions,
            samplerProfile,
            fragmentProgramControl);
        bool exportsDepth = fragmentProgramControl is { } depthControl &&
            HasAnyControlFlag(
                depthControl,
                RsxFragmentProgramControlFlags.DepthExportMask);
        bool emitsDepth = exportsDepth || emulateDepth24;

        var builder = new StringBuilder();
        MetalRsxShaderAbi.AppendPreamble(builder);
        AppendFragmentOutput(builder, emitsDepth, colorAttachments);
        AppendFragmentHelpers(builder);
        AppendFragmentFunctionSignature(
            builder,
            samplerProfile,
            emulateDepth24);
        if (emulateDepth24)
        {
            builder.AppendLine("  (void)rsxSampleId;");
            builder.AppendLine(
                "  float rsxRasterDepth = rsxIn.position.z;");
            builder.AppendLine(
                "  float rsxPolygonOffsetSlope = max(abs(dfdx(rsxRasterDepth)), abs(dfdy(rsxRasterDepth)));");
        }
        AppendRegisterBankDeclaration(
            builder,
            "R",
            registerUsage.FullRegisters);
        AppendRegisterBankDeclaration(
            builder,
            "H",
            registerUsage.HalfRegisters);
        AppendRegisterBankInitialization(
            builder,
            "R",
            registerUsage.FullRegisters,
            "float4(0.0f)");
        AppendRegisterBankInitialization(
            builder,
            "H",
            registerUsage.HalfRegisters,
            "float4(0.0f)");
        builder.AppendLine("  float4 rsxCc0 = float4(0.0f);");
        builder.AppendLine("  float4 rsxCc1 = float4(0.0f);");

        FragmentControlFlowPlan? controlFlow =
            TryCreateFragmentControlFlowPlan(instructions);
        foreach (RsxFragmentInstruction instruction in instructions)
        {
            if (controlFlow is { } closingFlow &&
                instruction.Offset == closingFlow.CloseOffset)
            {
                builder.AppendLine("  }");
            }
            if (instruction.IsControlFlow)
            {
                if (controlFlow is { } supportedFlow &&
                    instruction.Index == supportedFlow.InstructionIndex)
                {
                    builder.AppendLine(supportedFlow.OpeningStatement);
                }
                else
                {
                    blockers.Add("fragmentBranchControlFlow=unlowered");
                    builder.AppendLine(
                        "  // Control-flow instruction; no behavior invented.");
                }
                continue;
            }
            if (instruction.OpcodeType == RsxFragmentOpcode.Kill)
            {
                builder.AppendLine(
                    $"  if ({FragmentFlowConditionExpression(instruction)}) discard_fragment();");
                continue;
            }

            if (instruction.Scale == RsxFragmentResultScale.Reserved4)
                blockers.Add("fragmentScale4=unmapped");
            if (instruction.ExpandedTexture && instruction.IsTexture)
                blockers.Add("fragmentTextureExpand=unlowered");
            if (instruction.UsesIndexedInput &&
                HasInputSource(instruction))
            {
                blockers.Add("fragmentIndexedInput=unlowered");
            }
            AddFragmentPrecisionBlockers(instruction, blockers);
            if (HasSourceType3(instruction))
                blockers.Add("fragmentSourceRegisterType3=unmapped");
            if (instruction.OpcodeType == RsxFragmentOpcode.Nop ||
                IsFenceNoOp(instruction))
            {
                continue;
            }

            string? expression = FragmentExpression(
                instruction,
                samplerProfile,
                blockers);
            if (expression is null)
            {
                blockers.Add(
                    $"fragmentOpcode0x{instruction.Opcode:X2}=unmapped");
                builder.AppendLine(
                    $"  // Unmapped RSX fragment opcode 0x{instruction.Opcode:X2}; no value invented.");
                continue;
            }
            expression = ApplyFragmentResultModifiers(
                instruction,
                expression);

            AppendFragmentInstructionWrites(
                builder,
                instruction,
                expression);
        }
        if (controlFlow is { } trailingFlow &&
            trailingFlow.CloseOffset == FragmentProgramEndOffset(instructions))
        {
            builder.AppendLine("  }");
        }

        bool fp32Exports = fragmentProgramControl is { } control &&
            HasControlFlag(
                control,
                RsxFragmentProgramControlFlags.Exports32Bit);
        if (fragmentProgramControl is null)
            blockers.Add("fragmentExportBank=unsupported");
        string[] outputRegisters = fp32Exports
            ? ["R[0]", "R[2]", "R[3]", "R[4]"]
            : ["H[0]", "H[4]", "H[6]", "H[8]"];
        for (int colorTarget = 0; colorTarget < 4; colorTarget++)
        {
            builder.AppendLine(
                $"  float4 rsxColorExport{colorTarget} = {outputRegisters[colorTarget]};");
        }
        if (fixedFunction is { } epilogue)
        {
            AppendFixedFunctionEpilogue(
                builder,
                epilogue,
                fragmentOutputDiagnostic.HasValue
                    ? [0, 1, 2, 3]
                    : colorAttachments);
        }
        builder.AppendLine("  RsxFragmentStageOut rsxOut;");
        if (fragmentOutputDiagnostic is { } selectedColorTarget)
        {
            builder.AppendLine(
                $"  rsxOut.color0 = rsxColorExport{selectedColorTarget};");
        }
        else
        {
            foreach (int colorTarget in colorAttachments)
            {
                builder.AppendLine(
                    $"  rsxOut.color{colorTarget} = rsxColorExport{colorTarget};");
            }
        }
        if (emitsDepth)
        {
            string candidate = exportsDepth
                ? "R[1].z"
                : "rsxIn.position.z";
            if (emulateDepth24)
            {
                builder.AppendLine(
                    $"  float rsxBiasedDepth = clamp({candidate} + rsxDepthBias.x + rsxDepthBias.y * rsxPolygonOffsetSlope, 0.0f, 1.0f);");
                builder.AppendLine(
                    "  rsxOut.depth = rsxQuantizeDepth24(rsxBiasedDepth);");
            }
            else
            {
                builder.AppendLine($"  rsxOut.depth = {candidate};");
            }
        }
        builder.AppendLine("  return rsxOut;");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string BuildVertexPlacementDiagnosticMsl()
    {
        var builder = new StringBuilder();
        MetalRsxShaderAbi.AppendPreamble(builder);
        builder.AppendLine("struct RsxFragmentStageOut");
        builder.AppendLine("{");
        builder.AppendLine("  float4 color0 [[color(0)]];");
        builder.AppendLine("};");
        builder.AppendLine(
            "fragment RsxFragmentStageOut rsxFragmentMain(RsxVertexStageOut rsxIn [[stage_in]])");
        builder.AppendLine("{");
        builder.AppendLine("  (void)rsxIn;");
        builder.AppendLine("  RsxFragmentStageOut rsxOut;");
        builder.AppendLine(
            "  rsxOut.color0 = float4(1.0f, 0.0f, 1.0f, 1.0f);");
        builder.AppendLine("  return rsxOut;");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void AppendFragmentOutput(
        StringBuilder builder,
        bool exportsDepth,
        IReadOnlyList<int> colorAttachments)
    {
        builder.AppendLine("struct RsxFragmentStageOut");
        builder.AppendLine("{");
        foreach (int colorTarget in colorAttachments)
        {
            builder.AppendLine(
                $"  float4 color{colorTarget} [[color({colorTarget})]];");
        }
        if (exportsDepth)
            builder.AppendLine("  float depth [[depth(any)]];");
        builder.AppendLine("};");
    }

    private static FixedFunctionPlan? CreateFixedFunctionPlan(
        RenderState state,
        uint fragmentProgramControl,
        bool suppressShaderPackerForDiagnosticOutput,
        ISet<string> blockers)
    {
        AlphaTestMode? alphaTestMode = AlphaTest.Resolve(state);
        if (!alphaTestMode.HasValue)
        {
            blockers.Add("fragmentAlphaTest=unsupported");
            return null;
        }

        MetalRsxShaderPackerMode shaderPackerMode =
            ResolveShaderPackerMode(
                fragmentProgramControl,
                state,
                suppressShaderPackerForDiagnosticOutput);
        if (shaderPackerMode ==
                MetalRsxShaderPackerMode.LinearToSrgbProgramEpilogue &&
            RequiresPremultipliedSourceRgb(state))
        {
            shaderPackerMode = MetalRsxShaderPackerMode
                .PremultipliedLinearToSrgbProgramEpilogue;
        }
        return new FixedFunctionPlan(
            alphaTestMode.Value,
            shaderPackerMode);
    }

    private static MetalRsxShaderPackerMode ResolveShaderPackerMode(
        uint fragmentProgramControl,
        RenderState state,
        bool suppressForDiagnosticOutput)
    {
        if (!state.ShaderPackerSrgbEnabled)
            return MetalRsxShaderPackerMode.DisabledByState;
        if (((RsxFragmentProgramControlFlags)fragmentProgramControl &
                RsxFragmentProgramControlFlags.Exports32Bit) != 0)
        {
            return MetalRsxShaderPackerMode.SuppressedForFp32Exports;
        }
        if (suppressForDiagnosticOutput)
        {
            return MetalRsxShaderPackerMode
                .SuppressedForDiagnosticOutput;
        }
        return MetalRsxShaderPackerMode.LinearToSrgbProgramEpilogue;
    }

    private static bool RequiresPremultipliedSourceRgb(RenderState state) =>
        state.BlendEnabled &&
        state.BlendEquationRgb == RsxBlendEquation.Add &&
        state.BlendSourceRgb == RsxBlendFactor.One &&
        state.BlendDestinationRgb ==
            RsxBlendFactor.OneMinusSourceAlpha;

    private static void AppendFixedFunctionEpilogue(
        StringBuilder builder,
        FixedFunctionPlan plan,
        IReadOnlyList<int> colorAttachments)
    {
        switch (plan.AlphaTestMode)
        {
            case AlphaTestMode.Disabled:
                break;
            case AlphaTestMode.GreaterZero:
                builder.AppendLine(
                    "  if (!(rsxColorExport0.a > 0.0f)) discard_fragment();");
                break;
            case AlphaTestMode.Less128:
                builder.AppendLine(
                    "  if (!(rsxColorExport0.a < (128.0f / 255.0f))) discard_fragment();");
                break;
            case AlphaTestMode.GreaterEqual128:
                builder.AppendLine(
                    "  if (!(rsxColorExport0.a >= (128.0f / 255.0f))) discard_fragment();");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(plan));
        }

        switch (plan.ShaderPackerMode)
        {
            case MetalRsxShaderPackerMode.LinearToSrgbProgramEpilogue:
                AppendLinearToSrgb(
                    builder,
                    colorAttachments,
                    premultiplied: false);
                break;
            case MetalRsxShaderPackerMode
                    .PremultipliedLinearToSrgbProgramEpilogue:
                AppendLinearToSrgb(
                    builder,
                    colorAttachments,
                    premultiplied: true);
                break;
        }
    }

    private static void AppendLinearToSrgb(
        StringBuilder builder,
        IReadOnlyList<int> colorAttachments,
        bool premultiplied)
    {
        foreach (int colorTarget in colorAttachments)
        {
            string target = $"rsxColorExport{colorTarget}";
            if (!premultiplied)
            {
                builder.AppendLine(
                    $"  float3 rsxPackerLow{colorTarget} = {target}.rgb * 12.92f;");
                builder.AppendLine(
                    $"  float3 rsxPackerHigh{colorTarget} = 1.055f * pow({target}.rgb, float3(1.0f / 2.4f)) - 0.055f;");
                builder.AppendLine(
                    $"  bool3 rsxPackerSelectLow{colorTarget} = {target}.rgb < float3(0.0031308f);");
                builder.AppendLine(
                    $"  {target}.rgb = clamp(select(rsxPackerHigh{colorTarget}, rsxPackerLow{colorTarget}, rsxPackerSelectLow{colorTarget}), float3(0.0f), float3(1.0f));");
                continue;
            }

            builder.AppendLine(
                $"  float rsxPackerAlpha{colorTarget} = {target}.a;");
            builder.AppendLine(
                $"  if (rsxPackerAlpha{colorTarget} > 0.0f)");
            builder.AppendLine("  {");
            builder.AppendLine(
                $"    float3 rsxPackerStraight{colorTarget} = max({target}.rgb / rsxPackerAlpha{colorTarget}, float3(0.0f));");
            builder.AppendLine(
                $"    float3 rsxPackerLow{colorTarget} = rsxPackerStraight{colorTarget} * 12.92f;");
            builder.AppendLine(
                $"    float3 rsxPackerHigh{colorTarget} = 1.055f * pow(rsxPackerStraight{colorTarget}, float3(1.0f / 2.4f)) - 0.055f;");
            builder.AppendLine(
                $"    bool3 rsxPackerSelectLow{colorTarget} = rsxPackerStraight{colorTarget} < float3(0.0031308f);");
            builder.AppendLine(
                $"    {target}.rgb = clamp(select(rsxPackerHigh{colorTarget}, rsxPackerLow{colorTarget}, rsxPackerSelectLow{colorTarget}), float3(0.0f), float3(1.0f)) * rsxPackerAlpha{colorTarget};");
            builder.AppendLine("  }");
            builder.AppendLine("  else");
            builder.AppendLine("  {");
            builder.AppendLine($"    {target}.rgb = float3(0.0f);");
            builder.AppendLine("  }");
        }
    }

    private static void AppendFragmentHelpers(StringBuilder builder)
    {
        builder.AppendLine(
            "float4 rsxFragmentSplat(float value) { return float4(value); }");
        builder.AppendLine(
            "float4 rsxFragmentNormalize(float3 value) { return length(value) > 0.0f ? normalize(value).xyzz : value.xyzz; }");
        builder.AppendLine(
            "float4 rsxFragmentDivideBySqrt(float4 a, float b) { float4 q = a / sqrt(abs(b)); return float4(abs(a.x) > 0.0f ? q.x : a.x, abs(a.y) > 0.0f ? q.y : a.y, abs(a.z) > 0.0f ? q.z : a.z, abs(a.w) > 0.0f ? q.w : a.w); }");
        builder.AppendLine(
            "float4 rsxFragmentBool4(bool4 value) { return select(float4(0.0f), float4(1.0f), value); }");
        builder.AppendLine(
            "float4 rsxFragmentHalf(float4 value) { return float4(half4(value)); }");
        builder.AppendLine(
            "float4 rsxFragmentPrecisionClamp(float4 value, float minimum, float maximum) { value = select(value, float4(0.0f), isnan(value)); return clamp(value, float4(minimum), float4(maximum)); }");
        builder.AppendLine(
            "float rsxQuantizeDepth24(float value) { constexpr float maximum = 16777215.0f; return floor(clamp(value, 0.0f, 1.0f) * maximum + 0.5f) / maximum; }");
        builder.AppendLine(
            "bool rsxFragmentCcTestFL(float value) { return false; }");
        builder.AppendLine(
            "bool rsxFragmentCcTestLT(float value) { return !isnan(value) && value < 0.0f; }");
        builder.AppendLine(
            "bool rsxFragmentCcTestEQ(float value) { return !isnan(value) && value == 0.0f; }");
        builder.AppendLine(
            "bool rsxFragmentCcTestLE(float value) { return !isnan(value) && value <= 0.0f; }");
        builder.AppendLine(
            "bool rsxFragmentCcTestGT(float value) { return !isnan(value) && value > 0.0f; }");
        builder.AppendLine(
            "bool rsxFragmentCcTestNE(float value) { return isnan(value) || value != 0.0f; }");
        builder.AppendLine(
            "bool rsxFragmentCcTestGE(float value) { return !isnan(value) && value >= 0.0f; }");
        builder.AppendLine(
            "bool rsxFragmentCcTestTR(float value) { return true; }");
    }

    private static void AppendFragmentFunctionSignature(
        StringBuilder builder,
        RsxFragmentSamplerFeatureProfile samplerProfile,
        bool emulateDepth24)
    {
        builder.AppendLine("fragment RsxFragmentStageOut rsxFragmentMain(");
        builder.AppendLine("    RsxVertexStageOut rsxIn [[stage_in]],");
        builder.AppendLine("    bool rsxFrontFacing [[front_facing]],");
        for (int destination = 0;
             destination < MetalRsxShaderAbi.TextureDestinationCount;
             destination++)
        {
            string textureType = MetalTextureType(
                samplerProfile.FeaturesFor(destination));
            builder.AppendLine(
                $"    {textureType} rsxTexture{destination} [[texture({destination})]],");
            builder.AppendLine(
                $"    sampler rsxSampler{destination} [[sampler({destination})]],");
        }
        builder.AppendLine(
            $"    constant float4* rsxCodePixelConst [[buffer({MetalRsxShaderAbi.FragmentCodeConstantBufferIndex})]],");
        builder.AppendLine(
            $"    constant float4* rsxStaticPixelConst [[buffer({MetalRsxShaderAbi.FragmentStaticConstantBufferIndex})]]" +
            (emulateDepth24
                ? $",\n    constant float2& rsxDepthBias [[buffer({MetalRsxShaderAbi.FragmentDepthBiasBufferIndex})]],\n    uint rsxSampleId [[sample_id]]"
                : string.Empty) +
            ")");
        builder.AppendLine("{");
    }

    private static string MetalTextureType(
        RsxFragmentSamplerFeatures features) =>
        HasFeature(features, RsxFragmentSamplerFeatures.Shadow)
            ? "depth2d<float>"
            : HasFeature(features, RsxFragmentSamplerFeatures.Volume)
                ? "texture3d<float>"
                : HasFeature(features, RsxFragmentSamplerFeatures.Cube)
                    ? "texturecube<float>"
                    : "texture2d<float>";

    private static FragmentRegisterUsage ReadFragmentRegisterUsage(
        IReadOnlyList<RsxFragmentInstruction> instructions,
        RsxFragmentSamplerFeatureProfile samplerProfile,
        RsxFragmentProgramControlFlags? fragmentProgramControl)
    {
        var fullRegisters = new SortedSet<int>();
        var halfRegisters = new SortedSet<int>();
        bool fp32Exports = fragmentProgramControl is { } control &&
            HasControlFlag(
                control,
                RsxFragmentProgramControlFlags.Exports32Bit);
        ISet<int> colorExportRegisters = fp32Exports
            ? fullRegisters
            : halfRegisters;
        foreach (int register in fp32Exports
                     ? new[] { 0, 2, 3, 4 }
                     : new[] { 0, 4, 6, 8 })
        {
            colorExportRegisters.Add(register);
        }
        if (fragmentProgramControl is { } depthControl &&
            HasAnyControlFlag(
                depthControl,
                RsxFragmentProgramControlFlags.DepthExportMask))
        {
            fullRegisters.Add(1);
        }

        foreach (RsxFragmentInstruction instruction in instructions)
        {
            if (!FragmentInstructionEmitsExpression(
                    instruction,
                    samplerProfile))
            {
                continue;
            }

            int operandCount = instruction.OperandCount;
            if (operandCount > 0)
            {
                AddFragmentSourceRegister(
                    instruction.Src0,
                    fullRegisters,
                    halfRegisters);
            }
            if (operandCount > 1)
            {
                AddFragmentSourceRegister(
                    instruction.Src1,
                    fullRegisters,
                    halfRegisters);
            }
            if (operandCount > 2)
            {
                AddFragmentSourceRegister(
                    instruction.Src2,
                    fullRegisters,
                    halfRegisters);
            }

            if (!instruction.NoDest &&
                instruction.WriteMask != RsxFragmentWriteMask.None)
            {
                (instruction.DestFp16
                        ? halfRegisters
                        : fullRegisters)
                    .Add(instruction.DestRegister);
            }
        }

        return new FragmentRegisterUsage(
            fullRegisters.ToArray(),
            halfRegisters.ToArray());
    }

    private static bool FragmentInstructionEmitsExpression(
        RsxFragmentInstruction instruction,
        RsxFragmentSamplerFeatureProfile samplerProfile)
    {
        if (instruction.IsControlFlow ||
            instruction.OpcodeType == RsxFragmentOpcode.Kill)
        {
            return false;
        }
        if (instruction.OpcodeType == RsxFragmentOpcode.Nop ||
            IsFenceNoOp(instruction))
        {
            return false;
        }

        return FragmentExpression(
                   instruction,
                   samplerProfile,
                   new HashSet<string>(StringComparer.Ordinal)) is not null;
    }

    private static void AddFragmentSourceRegister(
        uint source,
        ISet<int> fullRegisters,
        ISet<int> halfRegisters)
    {
        if (RsxFragmentInstruction.SourceRegisterKind(source) !=
            RsxFragmentRegisterType.Temporary)
        {
            return;
        }
        var operand = new RsxFragmentOperand(0, source);
        (operand.Fp16
                ? halfRegisters
                : fullRegisters)
            .Add(operand.RegisterIndex);
    }

    private static void AppendFragmentInstructionWrites(
        StringBuilder builder,
        RsxFragmentInstruction instruction,
        string expression)
    {
        if (instruction.WriteMask == RsxFragmentWriteMask.None)
            return;

        bool unconditional =
            instruction.ConditionTest == RsxConditionTest.True;
        if (instruction.NoDest)
        {
            if (!instruction.CondWriteEnabled)
                return;
            string conditionValue = $"rsxCcValue{instruction.Index}";
            string conditionRegister = instruction.ConditionWriteRegister1
                ? "rsxCc1"
                : "rsxCc0";
            string mask = FragmentWriteMask(instruction.WriteMask);
            builder.AppendLine(
                $"  float4 {conditionValue} = {expression};");
            builder.AppendLine(unconditional
                ? $"  {conditionRegister}.{mask} = {conditionValue}.{mask};"
                : $"  if ({FragmentFlowConditionExpression(instruction)}) {conditionRegister}.{mask} = {conditionValue}.{mask};");
            return;
        }

        string destination = instruction.DestFp16
            ? $"H[{instruction.DestRegister}]"
            : $"R[{instruction.DestRegister}]";
        if (unconditional && !instruction.CondWriteEnabled)
        {
            string mask = FragmentWriteMask(instruction.WriteMask);
            builder.AppendLine(
                $"  {destination}.{mask} = ({expression}).{mask};");
            return;
        }

        bool needsValue = instruction.CondWriteEnabled || !unconditional;
        string value = needsValue
            ? $"rsxCcValue{instruction.Index}"
            : $"({expression})";
        if (needsValue)
            builder.AppendLine($"  float4 {value} = {expression};");
        for (int component = 0; component < 4; component++)
        {
            if ((instruction.WriteMask &
                    (RsxFragmentWriteMask)(1 << component)) == 0)
            {
                continue;
            }
            char destinationComponent = SwizzleChar(
                (RsxSwizzleComponent)component);
            string condition = FragmentComponentConditionExpression(
                instruction,
                component);
            builder.AppendLine(unconditional
                ? $"  {destination}.{destinationComponent} = {value}.{destinationComponent};"
                : $"  if ({condition}) {destination}.{destinationComponent} = {value}.{destinationComponent};");
        }

        if (instruction.CondWriteEnabled)
        {
            string conditionRegister = instruction.ConditionWriteRegister1
                ? "rsxCc1"
                : "rsxCc0";
            string mask = FragmentWriteMask(instruction.WriteMask);
            builder.AppendLine(
                $"  {conditionRegister}.{mask} = {destination}.{mask};");
        }
    }

    private static string FragmentConditionTestName(
        RsxConditionTest test) => test switch
    {
        RsxConditionTest.False => "FL",
        RsxConditionTest.LessThan => "LT",
        RsxConditionTest.Equal => "EQ",
        RsxConditionTest.LessThanOrEqual => "LE",
        RsxConditionTest.GreaterThan => "GT",
        RsxConditionTest.NotEqual => "NE",
        RsxConditionTest.GreaterThanOrEqual => "GE",
        RsxConditionTest.True => "TR",
        _ => throw new ArgumentOutOfRangeException(nameof(test))
    };

    private static string FragmentComponentConditionExpression(
        RsxFragmentInstruction instruction,
        int destinationComponent)
    {
        string conditionRegister = instruction.ConditionReadRegister1
            ? "rsxCc1"
            : "rsxCc0";
        char conditionComponent = SwizzleChar(
            instruction.ConditionSwizzle(destinationComponent));
        string test = FragmentConditionTestName(instruction.ConditionTest);
        return
            $"rsxFragmentCcTest{test}({conditionRegister}.{conditionComponent})";
    }

    private static FragmentControlFlowPlan? TryCreateFragmentControlFlowPlan(
        IReadOnlyList<RsxFragmentInstruction> instructions)
    {
        RsxFragmentInstruction[] flowInstructions = instructions
            .Where(instruction => instruction.IsControlFlow)
            .ToArray();
        if (flowInstructions.Length != 1 || instructions.Count == 0)
            return null;

        RsxFragmentInstruction flow = flowInstructions[0];
        if (flow.ConditionWriteRegister1 ||
            flow.CondWriteEnabled ||
            !flow.NoDest ||
            flow.WriteMask != RsxFragmentWriteMask.None ||
            flow.Saturate ||
            flow.Scale != RsxFragmentResultScale.None)
        {
            return null;
        }

        string condition = FragmentFlowConditionExpression(flow);
        int programEndOffset = FragmentProgramEndOffset(instructions);
        if (flow.OpcodeType == RsxFragmentOpcode.Return)
        {
            return new FragmentControlFlowPlan(
                flow.Index,
                programEndOffset,
                $"  if (!({condition})) {{");
        }
        if (flow.OpcodeType != RsxFragmentOpcode.If ||
            (flow.Src1 & 0x7fff_ffffu) != flow.Src2)
        {
            return null;
        }

        uint targetSlot = flow.Src2 >> 2;
        if (targetSlot > (uint)(int.MaxValue / 16))
            return null;
        int closeOffset = checked(
            instructions[0].Offset + (int)targetSlot * 16);
        bool targetExists = closeOffset == programEndOffset ||
            instructions.Any(instruction =>
                instruction.Offset == closeOffset);
        if (!targetExists || closeOffset <= flow.Offset)
            return null;

        return new FragmentControlFlowPlan(
            flow.Index,
            closeOffset,
            $"  if ({condition}) {{");
    }

    private static int FragmentProgramEndOffset(
        IReadOnlyList<RsxFragmentInstruction> instructions) =>
        instructions.Count == 0
            ? 0
            : instructions.Max(instruction =>
                checked(instruction.Offset + instruction.ByteCount));

    private static string FragmentFlowConditionExpression(
        RsxFragmentInstruction instruction) => string.Join(
        " || ",
        Enumerable.Range(0, 4).Select(component =>
            FragmentComponentConditionExpression(
                instruction,
                component)));

    private static string? FragmentExpression(
        RsxFragmentInstruction instruction,
        RsxFragmentSamplerFeatureProfile samplerProfile,
        ISet<string> blockers)
    {
        int operandCount = instruction.OperandCount;
        string s0 = operandCount > 0
            ? FragmentSource(
                instruction,
                instruction.Src0,
                0,
                blockers)
            : "float4(0.0f)";
        string s1 = operandCount > 1
            ? FragmentSource(
                instruction,
                instruction.Src1,
                1,
                blockers)
            : "float4(0.0f)";
        string s2 = operandCount > 2
            ? FragmentSource(
                instruction,
                instruction.Src2,
                2,
                blockers)
            : "float4(0.0f)";
        string scalar0 = $"({s0}).x";
        string scalar1 = $"({s1}).x";
        RsxFragmentSamplerFeatures features =
            samplerProfile.FeaturesFor(instruction.TextureUnit);
        bool cubeSampler = HasFeature(
            features,
            RsxFragmentSamplerFeatures.Cube);
        bool shadowSampler = HasFeature(
            features,
            RsxFragmentSamplerFeatures.Shadow);
        bool volumeSampler = HasFeature(
            features,
            RsxFragmentSamplerFeatures.Volume);
        if (shadowSampler &&
            RsxShaderInstructionSet.IsFragmentTexture(
                instruction.OpcodeType) &&
            instruction.OpcodeType is not RsxFragmentOpcode.Texture and
                not RsxFragmentOpcode.TextureProjective)
        {
            blockers.Add(
                $"fragmentShadowSamplerDest{instruction.TextureUnit}=opcode0x{instruction.Opcode:X2}_unlowered");
            return null;
        }
        if (cubeSampler &&
            instruction.OpcodeType == RsxFragmentOpcode.TextureProjective)
        {
            blockers.Add(
                $"fragmentCubeSamplerDest{instruction.TextureUnit}=projectiveTextureOpcodeUnlowered");
        }
        if (volumeSampler &&
            instruction.OpcodeType == RsxFragmentOpcode.TextureProjective)
        {
            blockers.Add(
                $"fragmentVolumeSamplerDest{instruction.TextureUnit}=projectiveTextureOpcodeUnlowered");
            return null;
        }

        return instruction.OpcodeType switch
        {
            RsxFragmentOpcode.Move => s0,
            RsxFragmentOpcode.Multiply => $"({s0} * {s1})",
            RsxFragmentOpcode.Add => $"({s0} + {s1})",
            RsxFragmentOpcode.MultiplyAdd => $"({s0} * {s1} + {s2})",
            RsxFragmentOpcode.Dot3 =>
                $"rsxFragmentSplat(dot(({s0}).xyz, ({s1}).xyz))",
            RsxFragmentOpcode.Dot4 =>
                $"rsxFragmentSplat(dot({s0}, {s1}))",
            RsxFragmentOpcode.Minimum => $"min({s0}, {s1})",
            RsxFragmentOpcode.Maximum => $"max({s0}, {s1})",
            RsxFragmentOpcode.SetLessThan =>
                $"rsxFragmentBool4({s0} < {s1})",
            RsxFragmentOpcode.SetGreaterThanOrEqual =>
                $"rsxFragmentBool4({s0} >= {s1})",
            RsxFragmentOpcode.SetLessThanOrEqual =>
                $"rsxFragmentBool4({s0} <= {s1})",
            RsxFragmentOpcode.SetGreaterThan =>
                $"rsxFragmentBool4({s0} > {s1})",
            RsxFragmentOpcode.SetNotEqual =>
                $"rsxFragmentBool4({s0} != {s1})",
            RsxFragmentOpcode.SetEqual =>
                $"rsxFragmentBool4({s0} == {s1})",
            RsxFragmentOpcode.Fraction => $"fract({s0})",
            RsxFragmentOpcode.Floor => $"floor({s0})",
            RsxFragmentOpcode.DerivativeX => $"dfdx({s0})",
            RsxFragmentOpcode.DerivativeY => $"dfdy({s0})",
            RsxFragmentOpcode.Texture when shadowSampler && !cubeSampler =>
                $"rsxFragmentSplat(rsxTexture{instruction.TextureUnit}.sample_compare(rsxSampler{instruction.TextureUnit}, ({s0}).xy, ({s0}).z))",
            RsxFragmentOpcode.Texture =>
                TextureSample(
                    instruction.TextureUnit,
                    s0,
                    cubeSampler || volumeSampler,
                    modifier: null),
            RsxFragmentOpcode.TextureProjective
                when shadowSampler && !cubeSampler =>
                $"rsxFragmentSplat(rsxTexture{instruction.TextureUnit}.sample_compare(rsxSampler{instruction.TextureUnit}, ({s0}).xy / ({s0}).w, ({s0}).z / ({s0}).w))",
            RsxFragmentOpcode.TextureProjective
                when !cubeSampler && !volumeSampler =>
                $"rsxTexture{instruction.TextureUnit}.sample(rsxSampler{instruction.TextureUnit}, ({s0}).xy / ({s0}).w)",
            RsxFragmentOpcode.Reciprocal =>
                $"rsxFragmentSplat(1.0f / {scalar0})",
            RsxFragmentOpcode.ReciprocalSquareRoot =>
                $"rsxFragmentSplat(1.0f / sqrt(abs({scalar0})))",
            RsxFragmentOpcode.ExponentBase2 =>
                $"rsxFragmentSplat(exp2({scalar0}))",
            RsxFragmentOpcode.LogarithmBase2 =>
                $"rsxFragmentSplat(log2({scalar0}))",
            RsxFragmentOpcode.SetTrue => "float4(1.0f)",
            RsxFragmentOpcode.SetFalse => "float4(0.0f)",
            RsxFragmentOpcode.Cosine =>
                $"rsxFragmentSplat(cos({scalar0}))",
            RsxFragmentOpcode.Sine =>
                $"rsxFragmentSplat(sin({scalar0}))",
            RsxFragmentOpcode.TextureLod =>
                TextureSample(
                    instruction.TextureUnit,
                    s0,
                    cubeSampler || volumeSampler,
                    $"level({scalar1})"),
            RsxFragmentOpcode.TextureBias =>
                TextureSample(
                    instruction.TextureUnit,
                    s0,
                    cubeSampler || volumeSampler,
                    $"bias({scalar1})"),
            RsxFragmentOpcode.Dot2 =>
                $"rsxFragmentSplat(dot(({s0}).xy, ({s1}).xy))",
            RsxFragmentOpcode.Normalize =>
                $"rsxFragmentNormalize(({s0}).xyz)",
            RsxFragmentOpcode.Divide => $"({s0} / {scalar1})",
            RsxFragmentOpcode.DivideBySquareRoot =>
                $"rsxFragmentDivideBySqrt({s0}, {scalar1})",
            _ => null
        };
    }

    private static string TextureSample(
        int destination,
        string source,
        bool usesThreeCoordinates,
        string? modifier)
    {
        string coordinates =
            $"({source}).{(usesThreeCoordinates ? "xyz" : "xy")}";
        string suffix = modifier is null ? string.Empty : $", {modifier}";
        return
            $"rsxTexture{destination}.sample(rsxSampler{destination}, {coordinates}{suffix})";
    }

    private static string FragmentSource(
        RsxFragmentInstruction instruction,
        uint source,
        int sourceIndex,
        ISet<string> blockers)
    {
        var operand = new RsxFragmentOperand(sourceIndex, source);
        string value;
        switch (operand.RegisterKind)
        {
            case RsxFragmentRegisterType.Temporary:
                value =
                    $"{(operand.Fp16 ? "H" : "R")}[{operand.RegisterIndex}]";
                break;
            case RsxFragmentRegisterType.Input:
                value = FragmentInput(instruction, blockers);
                break;
            case RsxFragmentRegisterType.InlineConstant:
                value = FragmentConstant(instruction, blockers);
                break;
            default:
                value = "float4(0.0f)";
                break;
        }

        value += $".{FragmentSwizzle(operand)}";
        if (operand.Absolute)
            value = $"abs({value})";
        if (operand.RegisterKind != RsxFragmentRegisterType.Input ||
            instruction.SourceAttribute is not
                (RsxFragmentInputAttribute.Color0 or
                 RsxFragmentInputAttribute.Color1 or
                 RsxFragmentInputAttribute.SignedSideArea))
        {
            value = ApplyFragmentSourcePrecision(
                instruction.SourcePrecision(sourceIndex),
                operand,
                value);
        }
        if (operand.Negate)
            value = $"(-{value})";
        return value;
    }

    private static string FragmentConstant(
        RsxFragmentInstruction instruction,
        ISet<string> blockers)
    {
        if (instruction.StaticPixelConstantArgumentOrdinal is
            { } staticArgumentOrdinal)
        {
            if (staticArgumentOrdinal < 0)
            {
                blockers.Add("fragmentStaticPixelConstantIndex=invalid");
                return "float4(0.0f)";
            }
            return $"rsxStaticPixelConst[{staticArgumentOrdinal}]";
        }
        if (instruction.DirectCodeConstantIndex is { } codeIndex)
        {
            if (codeIndex >= CodeConstantLayout.Float4Count)
            {
                blockers.Add(
                    $"fragmentCodePixelConstant{codeIndex}=unmapped");
                return "float4(0.0f)";
            }
            return $"rsxCodePixelConst[{codeIndex}]";
        }
        return instruction.Constant is { } constant
            ? FormatInlineConstant(constant)
            : "float4(0.0f)";
    }

    private static string FragmentInput(
        RsxFragmentInstruction instruction,
        ISet<string> blockers)
    {
        RsxFragmentInputAttribute input = instruction.SourceAttribute;
        switch (input)
        {
            case RsxFragmentInputAttribute.WindowPosition:
                return "rsxIn.position";
            case RsxFragmentInputAttribute.Color0:
                return "clamp(rsxIn.color0, float4(0.0f), float4(1.0f))";
            case RsxFragmentInputAttribute.Color1:
                return "clamp(rsxIn.color1, float4(0.0f), float4(1.0f))";
            case >= RsxFragmentInputAttribute.TextureCoordinate0 and
                <= RsxFragmentInputAttribute.TextureCoordinate7:
            {
                string value =
                    $"rsxIn.texcoord{(byte)input - (byte)RsxFragmentInputAttribute.TextureCoordinate0}";
                return instruction.PerspectiveCorrection
                    ? $"({value} * rsxIn.position.w)"
                    : value;
            }
            case RsxFragmentInputAttribute.SignedSideArea:
                return "float4(rsxFrontFacing ? 1.0f : -1.0f)";
            case RsxFragmentInputAttribute.Fog:
                blockers.Add("fragmentFogInput=unlowered");
                break;
            case RsxFragmentInputAttribute.TextureCoordinate8:
            case RsxFragmentInputAttribute.TextureCoordinate9:
                blockers.Add(
                    $"fragmentTexcoord{(byte)input - (byte)RsxFragmentInputAttribute.TextureCoordinate0}Input=unlowered");
                break;
            default:
                blockers.Add(
                    $"fragmentInput{(byte)input}=unmapped");
                break;
        }
        return "float4(0.0f)";
    }

    private static string FormatInlineConstant(
        RsxFragmentInlineConstant constant) =>
        $"float4(as_type<float>(0x{constant.XBits:X8}u), " +
        $"as_type<float>(0x{constant.YBits:X8}u), " +
        $"as_type<float>(0x{constant.ZBits:X8}u), " +
        $"as_type<float>(0x{constant.WBits:X8}u))";

    private static bool HasSourceType3(RsxFragmentInstruction instruction)
    {
        int count = instruction.OperandCount;
        return (count > 0 &&
                instruction.Source0Operand.RegisterKind ==
                    RsxFragmentRegisterType.Unknown3) ||
               (count > 1 &&
                instruction.Source1Operand.RegisterKind ==
                    RsxFragmentRegisterType.Unknown3) ||
               (count > 2 &&
                instruction.Source2Operand.RegisterKind ==
                    RsxFragmentRegisterType.Unknown3);
    }

    private static bool HasInputSource(RsxFragmentInstruction instruction)
    {
        int count = instruction.OperandCount;
        return (count > 0 &&
                instruction.Source0Operand.RegisterKind ==
                    RsxFragmentRegisterType.Input) ||
               (count > 1 &&
                instruction.Source1Operand.RegisterKind ==
                    RsxFragmentRegisterType.Input) ||
               (count > 2 &&
                instruction.Source2Operand.RegisterKind ==
                    RsxFragmentRegisterType.Input);
    }

    private static bool IsFenceNoOp(RsxFragmentInstruction instruction) =>
        !instruction.IsControlFlow &&
        (instruction.OpcodeType is RsxFragmentOpcode.FenceT or
            RsxFragmentOpcode.FenceB) &&
        (instruction.Dst & 0xff00ffffu) ==
        ((0x40u | instruction.Opcode) << 24 | 0x001e7eu) &&
        instruction.Src0 == 0x1c9dc800u &&
        instruction.Src1 == 0x0001c800u &&
        instruction.Src2 == 0x0001c800u &&
        instruction.NoDest &&
        !instruction.End &&
        !instruction.Saturate &&
        instruction.Scale == RsxFragmentResultScale.None &&
        !instruction.CondWriteEnabled &&
        instruction.ConditionTest == RsxConditionTest.True;

    private static void AddFragmentPrecisionBlockers(
        RsxFragmentInstruction instruction,
        ISet<string> blockers)
    {
        for (int sourceIndex = 0;
             sourceIndex < instruction.OperandCount;
             sourceIndex++)
        {
            RsxFragmentPrecision precision =
                instruction.SourcePrecision(sourceIndex);
            if (precision is RsxFragmentPrecision.Reserved6 or
                RsxFragmentPrecision.Reserved7)
            {
                blockers.Add(
                    $"fragmentSource{sourceIndex}Precision={(byte)precision}_unmapped");
            }
        }
    }

    private static string ApplyFragmentSourcePrecision(
        RsxFragmentPrecision precision,
        RsxFragmentOperand operand,
        string expression) => precision switch
    {
        RsxFragmentPrecision.Real or
        RsxFragmentPrecision.Unknown5 or
        RsxFragmentPrecision.Reserved6 or
        RsxFragmentPrecision.Reserved7 => expression,
        RsxFragmentPrecision.Half
            when operand.RegisterKind == RsxFragmentRegisterType.Temporary &&
                 operand.Fp16 => expression,
        RsxFragmentPrecision.Half =>
            $"rsxFragmentHalf({expression})",
        RsxFragmentPrecision.Fixed12 =>
            $"rsxFragmentPrecisionClamp({expression}, -2.0f, 2.0f)",
        RsxFragmentPrecision.Fixed9 =>
            $"rsxFragmentPrecisionClamp({expression}, -1.0f, 1.0f)",
        RsxFragmentPrecision.Saturate =>
            $"rsxFragmentPrecisionClamp({expression}, 0.0f, 1.0f)",
        _ => throw new ArgumentOutOfRangeException(nameof(precision))
    };

    private static string ApplyFragmentResultModifiers(
        RsxFragmentInstruction instruction,
        string expression)
    {
        string? scale = FragmentScale(instruction.Scale);
        if (scale is not null && scale != "1.0f")
            expression = $"({expression} * {scale})";

        if (instruction.NoDest)
            return expression;

        if (instruction.DestFp16)
            expression = $"rsxFragmentHalf({expression})";

        if (instruction.Saturate)
        {
            return
                $"rsxFragmentPrecisionClamp({expression}, 0.0f, 1.0f)";
        }
        if (DestinationPrecisionIsIgnored(instruction))
            return expression;

        return instruction.DestinationPrecision switch
        {
            RsxFragmentPrecision.Fixed12 =>
                $"rsxFragmentPrecisionClamp({expression}, -2.0f, 2.0f)",
            RsxFragmentPrecision.Fixed9 =>
                $"rsxFragmentPrecisionClamp({expression}, -1.0f, 1.0f)",
            _ => expression
        };
    }

    private static bool DestinationPrecisionIsIgnored(
        RsxFragmentInstruction instruction)
    {
        if (instruction.DestinationPrecision is
            RsxFragmentPrecision.Real or
            RsxFragmentPrecision.Half)
        {
            return true;
        }
        if (instruction.OpcodeType is
            RsxFragmentOpcode.Normalize or
            RsxFragmentOpcode.Maximum or
            RsxFragmentOpcode.Minimum or
            RsxFragmentOpcode.Cosine or
            RsxFragmentOpcode.Sine or
            RsxFragmentOpcode.Reflection or
            RsxFragmentOpcode.Fraction or
            RsxFragmentOpcode.Lighting or
            RsxFragmentOpcode.LightingFinal or
            RsxFragmentOpcode.LogarithmBase2)
        {
            return true;
        }
        return instruction.OpcodeType == RsxFragmentOpcode.Move &&
               instruction.DestFp16 &&
               instruction.Source0Operand.RegisterKind ==
                   RsxFragmentRegisterType.Temporary &&
               instruction.Source0Operand.Fp16;
    }

    private static string? FragmentScale(
        RsxFragmentResultScale scale) => scale switch
    {
        RsxFragmentResultScale.None => "1.0f",
        RsxFragmentResultScale.MultiplyBy2 => "2.0f",
        RsxFragmentResultScale.MultiplyBy4 => "4.0f",
        RsxFragmentResultScale.MultiplyBy8 => "8.0f",
        RsxFragmentResultScale.DivideBy2 => "0.5f",
        RsxFragmentResultScale.DivideBy4 => "0.25f",
        RsxFragmentResultScale.DivideBy8 => "0.125f",
        _ => null
    };

    private static string FragmentSwizzle(RsxFragmentOperand operand) =>
        string.Create(4, operand, static (span, value) =>
        {
            span[0] = SwizzleChar(value.SwizzleX);
            span[1] = SwizzleChar(value.SwizzleY);
            span[2] = SwizzleChar(value.SwizzleZ);
            span[3] = SwizzleChar(value.SwizzleW);
        });

    private static char SwizzleChar(RsxSwizzleComponent component) =>
        component switch
        {
            RsxSwizzleComponent.X => 'x',
            RsxSwizzleComponent.Y => 'y',
            RsxSwizzleComponent.Z => 'z',
            RsxSwizzleComponent.W => 'w',
            _ => throw new ArgumentOutOfRangeException(nameof(component))
        };

    private static string FragmentWriteMask(RsxFragmentWriteMask mask)
    {
        var value = new StringBuilder(4);
        if ((mask & RsxFragmentWriteMask.X) != 0) value.Append('x');
        if ((mask & RsxFragmentWriteMask.Y) != 0) value.Append('y');
        if ((mask & RsxFragmentWriteMask.Z) != 0) value.Append('z');
        if ((mask & RsxFragmentWriteMask.W) != 0) value.Append('w');
        return value.ToString();
    }

    private static bool HasControlFlag(
        RsxFragmentProgramControlFlags flags,
        RsxFragmentProgramControlFlags flag) =>
        (flags & flag) == flag;

    private static bool HasAnyControlFlag(
        RsxFragmentProgramControlFlags flags,
        RsxFragmentProgramControlFlags mask) =>
        (flags & mask) != RsxFragmentProgramControlFlags.None;

    private static void AppendRegisterBankDeclaration(
        StringBuilder builder,
        string bank,
        IReadOnlyList<int> registers)
    {
        if (registers.Count == 0)
            return;
        builder.AppendLine($"  float4 {bank}[{registers[^1] + 1}];");
    }

    private static void AppendRegisterBankInitialization(
        StringBuilder builder,
        string bank,
        IReadOnlyList<int> registers,
        string value)
    {
        foreach (int register in registers)
            builder.AppendLine($"  {bank}[{register}] = {value};");
    }

    private static bool HasFeature(
        RsxFragmentSamplerFeatures features,
        RsxFragmentSamplerFeatures feature) =>
        (features & feature) != 0;

    private static bool EffectiveDepthComparisonOrWriteEnabled(
        RenderState state)
    {
        RenderState effective = state.HasState
            ? state
            : RenderState.Default;
        return effective.DepthTestEnabled || effective.DepthWriteEnabled;
    }

    private readonly record struct FragmentControlFlowPlan(
        int InstructionIndex,
        int CloseOffset,
        string OpeningStatement);

    private readonly record struct FragmentRegisterUsage(
        int[] FullRegisters,
        int[] HalfRegisters);

    private readonly record struct FixedFunctionPlan(
        AlphaTestMode AlphaTestMode,
        MetalRsxShaderPackerMode ShaderPackerMode);
}

internal sealed record RsxFragmentMslLoweringResult(
    string? Msl,
    bool IsReady,
    ImmutableArray<string> Blockers)
{
    internal AlphaTestMode AlphaTestMode { get; init; }

    internal MetalRsxShaderPackerMode ShaderPackerMode { get; init; }

    internal ImmutableArray<int> SampledDestinations { get; init; }

    internal ImmutableArray<int> ColorAttachmentIndices { get; init; }

    internal bool ExportsDepth { get; init; }
}

internal enum MetalRsxShaderPackerMode
{
    DisabledByState = 0,
    LinearToSrgbProgramEpilogue,
    PremultipliedLinearToSrgbProgramEpilogue,
    SuppressedForFp32Exports,
    SuppressedForDiagnosticOutput
}
