using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using IW4.Render.Shaders;

namespace IW4.Render.OpenGl.Shaders;

/// <summary>
/// OpenGL-owned lowering of immutable RSX fragment semantics to GLSL 330.
/// </summary>
internal static class RsxFragmentGlsl330Lowerer
{
    internal const string FragmentEpilogueInsertionPoint =
        "  /*__MAP_RENDER_OPENGL_FRAGMENT_EPILOGUE__*/";

    internal static RsxFragmentGlsl330LoweringResult Lower(
        RsxFragmentProgramIr program)
    {
        ArgumentNullException.ThrowIfNull(program);

        var blockers = new SortedSet<string>(StringComparer.Ordinal);
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
            return CreateResult(glsl: null, blockers);

        string glsl = BuildGlsl(
            program.Instructions,
            program.SamplerFeatureProfile,
            program.ProgramControl.IsValid
                ? program.ProgramControl.EmittedFlags
                : null,
            blockers);
        return CreateResult(glsl, blockers);
    }

    private static RsxFragmentGlsl330LoweringResult CreateResult(
        string? glsl,
        SortedSet<string> blockers)
    {
        ImmutableArray<string> immutableBlockers = blockers.ToImmutableArray();
        return new RsxFragmentGlsl330LoweringResult(
            glsl,
            glsl is not null &&
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
            bool cube = HasFeature(entry.Features, RsxFragmentSamplerFeatures.Cube);
            bool shadow = HasFeature(entry.Features, RsxFragmentSamplerFeatures.Shadow);
            bool volume = HasFeature(entry.Features, RsxFragmentSamplerFeatures.Volume);
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

    private static string BuildGlsl(
        IReadOnlyList<RsxFragmentInstruction> instructions,
        RsxFragmentSamplerFeatureProfile samplerProfile,
        RsxFragmentProgramControlFlags? fragmentProgramControl,
        ISet<string> blockers)
    {
        FragmentRegisterUsage registerUsage = ReadFragmentRegisterUsage(
            instructions,
            samplerProfile,
            fragmentProgramControl);
        var builder = new StringBuilder();
        builder.AppendLine("#version 330 core");
        if (instructions.Any(instruction =>
                instruction.DirectCodeConstantIndex.HasValue))
        {
            builder.AppendLine(
                $"uniform vec4 {OpenGlCodePixelConstantUniformLayout.ArrayName}[{OpenGlCodePixelConstantUniformLayout.Count}];");
        }
        for (int i = 0; i < 16; i++)
        {
            RsxFragmentSamplerFeatures features =
                samplerProfile.FeaturesFor(i);
            string samplerType = HasFeature(
                    features,
                    RsxFragmentSamplerFeatures.Shadow)
                ? "sampler2DShadow"
                : HasFeature(features, RsxFragmentSamplerFeatures.Volume)
                    ? "sampler3D"
                    : HasFeature(features, RsxFragmentSamplerFeatures.Cube)
                        ? "samplerCube"
                        : "sampler2D";
            builder.AppendLine($"uniform {samplerType} rsxSampler{i};");
        }
        builder.AppendLine("in vec4 rsxColor0; in vec4 rsxColor1;");
        for (int i = 0; i < 8; i++)
            builder.AppendLine($"in vec4 rsxTexcoord{i};");
        builder.AppendLine("layout(location = 0) out vec4 FragColor;");
        builder.AppendLine("layout(location = 1) out vec4 rsxMrtColor1;");
        builder.AppendLine("layout(location = 2) out vec4 rsxMrtColor2;");
        builder.AppendLine("layout(location = 3) out vec4 rsxMrtColor3;");
        builder.AppendLine("vec4 rsxSplat(float v) { return vec4(v); }");
        builder.AppendLine("vec4 rsxNormalize(vec3 v) { return length(v) > 0.0 ? normalize(v).xyzz : v.xyzz; }");
        builder.AppendLine("vec4 rsxDivideBySqrt(vec4 a, float b) { vec4 q = a / sqrt(abs(b)); return vec4(abs(a.x) > 0.0 ? q.x : a.x, abs(a.y) > 0.0 ? q.y : a.y, abs(a.z) > 0.0 ? q.z : a.z, abs(a.w) > 0.0 ? q.w : a.w); }");
        builder.AppendLine("vec4 rsxBool4(bvec4 v) { return vec4(v.x ? 1.0 : 0.0, v.y ? 1.0 : 0.0, v.z ? 1.0 : 0.0, v.w ? 1.0 : 0.0); }");
        builder.AppendLine("bool rsxCcTestFL(float v) { return false; }");
        builder.AppendLine("bool rsxCcTestLT(float v) { return !isnan(v) && v < 0.0; }");
        builder.AppendLine("bool rsxCcTestEQ(float v) { return !isnan(v) && v == 0.0; }");
        builder.AppendLine("bool rsxCcTestLE(float v) { return !isnan(v) && v <= 0.0; }");
        builder.AppendLine("bool rsxCcTestGT(float v) { return !isnan(v) && v > 0.0; }");
        builder.AppendLine("bool rsxCcTestNE(float v) { return isnan(v) || v != 0.0; }");
        builder.AppendLine("bool rsxCcTestGE(float v) { return !isnan(v) && v >= 0.0; }");
        builder.AppendLine("bool rsxCcTestTR(float v) { return true; }");
        builder.AppendLine("void main() {");
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
            "vec4(0.0)");
        AppendRegisterBankInitialization(
            builder,
            "H",
            registerUsage.HalfRegisters,
            "vec4(0.0)");
        builder.AppendLine("  vec4 rsxCc0 = vec4(0.0);");
        builder.AppendLine("  vec4 rsxCc1 = vec4(0.0);");
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
                        "  // control-flow instruction; no behavior invented");
                }
                continue;
            }
            if (instruction.OpcodeType == RsxFragmentOpcode.Kill)
            {
                blockers.Add("fragmentConditionalKill=unlowered");
                builder.AppendLine(
                    "  // condition-based KIL/discard is outside the supported subset");
                continue;
            }

            if (instruction.Scale == RsxFragmentResultScale.Reserved4)
                blockers.Add("fragmentScale4=unmapped");
            if (HasSourceType3(instruction))
                blockers.Add("fragmentSourceRegisterType3=unmapped");
            if (instruction.OpcodeType == RsxFragmentOpcode.Nop ||
                IsFenceNoOp(instruction))
                continue;

            string? expression = FragmentExpression(
                instruction,
                samplerProfile,
                blockers);
            if (expression is null)
            {
                blockers.Add(
                    $"fragmentOpcode0x{instruction.Opcode:X2}=unmapped");
                builder.AppendLine(
                    $"  // unmapped RSX fragment opcode 0x{instruction.Opcode:X2}; no value invented");
                continue;
            }
            if (instruction.Saturate)
                expression = $"clamp({expression}, vec4(0.0), vec4(1.0))";
            string? scale = FragmentScale(instruction.Scale);
            if (scale is not null && scale != "1.0")
                expression = $"({expression} * {scale})";

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
        builder.AppendLine($"  FragColor = {outputRegisters[0]};");
        builder.AppendLine($"  rsxMrtColor1 = {outputRegisters[1]};");
        builder.AppendLine($"  rsxMrtColor2 = {outputRegisters[2]};");
        builder.AppendLine($"  rsxMrtColor3 = {outputRegisters[3]};");
        if (fragmentProgramControl is { } depthControl &&
            HasAnyControlFlag(
                depthControl,
                RsxFragmentProgramControlFlags.DepthExportMask))
        {
            builder.AppendLine("  gl_FragDepth = R[1].z;");
        }
        builder.AppendLine(FragmentEpilogueInsertionPoint);
        builder.AppendLine("}");
        return builder.ToString();
    }

    internal static string BuildGlsl(
        IReadOnlyList<RsxFragmentInstruction> instructions,
        ISet<string> blockers,
        IReadOnlySet<int> cubeSamplerDestinations,
        IReadOnlySet<int> shadowSamplerDestinations) =>
        BuildGlsl(
            instructions,
            blockers,
            cubeSamplerDestinations,
            shadowSamplerDestinations,
            new HashSet<int>(),
            fragmentProgramControl: null);

    internal static string BuildGlsl(
        IReadOnlyList<RsxFragmentInstruction> instructions,
        ISet<string> blockers,
        IReadOnlySet<int> cubeSamplerDestinations,
        IReadOnlySet<int> shadowSamplerDestinations,
        uint? fragmentProgramControl) =>
        BuildGlsl(
            instructions,
            blockers,
            cubeSamplerDestinations,
            shadowSamplerDestinations,
            new HashSet<int>(),
            fragmentProgramControl);

    internal static string BuildGlsl(
        IReadOnlyList<RsxFragmentInstruction> instructions,
        ISet<string> blockers,
        IReadOnlySet<int> cubeSamplerDestinations,
        IReadOnlySet<int> shadowSamplerDestinations,
        IReadOnlySet<int> volumeSamplerDestinations,
        uint? fragmentProgramControl)
    {
        var profile = new RsxFragmentSamplerFeatureProfile(
            cubeSamplerDestinations,
            shadowSamplerDestinations,
            volumeSamplerDestinations);
        AddAmbiguousSamplerShapeBlockers(profile, blockers);
        return BuildGlsl(
            instructions,
            profile,
            fragmentProgramControl.HasValue
                ? (RsxFragmentProgramControlFlags)fragmentProgramControl.Value
                : null,
            blockers);
    }

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
            return false;
        if (instruction.OpcodeType == RsxFragmentOpcode.Nop ||
            IsFenceNoOp(instruction))
            return false;

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
            return;
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
            builder.AppendLine($"  vec4 {conditionValue} = {expression};");
            builder.AppendLine(unconditional
                ? $"  {conditionRegister}.{mask} = {conditionValue}.{mask};"
                : $"  if ({FragmentFlowConditionExpression(instruction)}) {conditionRegister}.{mask} = {conditionValue}.{mask};");
            return;
        }

        string destination = instruction.DestFp16
            ? $"H[{instruction.DestRegister}]"
            : $"R[{instruction.DestRegister}]";
        if (unconditional &&
            !instruction.CondWriteEnabled)
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
            builder.AppendLine($"  vec4 {value} = {expression};");
        for (int component = 0; component < 4; component++)
        {
            if ((instruction.WriteMask &
                    (RsxFragmentWriteMask)(1 << component)) ==
                RsxFragmentWriteMask.None)
                continue;
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
        return $"rsxCcTest{test}({conditionRegister}.{conditionComponent})";
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
            return null;

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
            instructions.Any(instruction => instruction.Offset == closeOffset);
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
        RsxFragmentInstruction instruction)
    {
        return string.Join(
            " || ",
            Enumerable.Range(0, 4).Select(component =>
                FragmentComponentConditionExpression(
                    instruction,
                    component)));
    }

    private static string? FragmentExpression(
        RsxFragmentInstruction instruction,
        RsxFragmentSamplerFeatureProfile samplerProfile,
        ISet<string> blockers)
    {
        string s0 = FragmentSource(instruction, instruction.Src0, 0);
        string s1 = FragmentSource(instruction, instruction.Src1, 1);
        string s2 = FragmentSource(instruction, instruction.Src2, 2);
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
                $"rsxSplat(dot(({s0}).xyz, ({s1}).xyz))",
            RsxFragmentOpcode.Dot4 => $"rsxSplat(dot({s0}, {s1}))",
            RsxFragmentOpcode.Minimum => $"min({s0}, {s1})",
            RsxFragmentOpcode.Maximum => $"max({s0}, {s1})",
            RsxFragmentOpcode.SetLessThan =>
                $"rsxBool4(lessThan({s0}, {s1}))",
            RsxFragmentOpcode.SetGreaterThanOrEqual =>
                $"rsxBool4(greaterThanEqual({s0}, {s1}))",
            RsxFragmentOpcode.SetLessThanOrEqual =>
                $"rsxBool4(lessThanEqual({s0}, {s1}))",
            RsxFragmentOpcode.SetGreaterThan =>
                $"rsxBool4(greaterThan({s0}, {s1}))",
            RsxFragmentOpcode.SetNotEqual =>
                $"rsxBool4(notEqual({s0}, {s1}))",
            RsxFragmentOpcode.SetEqual => $"rsxBool4(equal({s0}, {s1}))",
            RsxFragmentOpcode.Fraction => $"fract({s0})",
            RsxFragmentOpcode.Floor => $"floor({s0})",
            RsxFragmentOpcode.DerivativeX => $"dFdx({s0})",
            RsxFragmentOpcode.DerivativeY => $"dFdy({s0})",
            RsxFragmentOpcode.Texture when shadowSampler && !cubeSampler =>
                $"rsxSplat(texture(rsxSampler{instruction.TextureUnit}, ({s0}).xyz))",
            RsxFragmentOpcode.Texture =>
                $"texture(rsxSampler{instruction.TextureUnit}, ({s0}).{(cubeSampler || volumeSampler ? "xyz" : "xy")})",
            RsxFragmentOpcode.TextureProjective when shadowSampler && !cubeSampler =>
                $"rsxSplat(textureProj(rsxSampler{instruction.TextureUnit}, {s0}))",
            RsxFragmentOpcode.TextureProjective when !cubeSampler && !volumeSampler =>
                $"textureProj(rsxSampler{instruction.TextureUnit}, {s0})",
            RsxFragmentOpcode.Reciprocal => $"rsxSplat(1.0 / {scalar0})",
            RsxFragmentOpcode.ReciprocalSquareRoot =>
                $"rsxSplat(1.0 / sqrt(abs({scalar0})))",
            RsxFragmentOpcode.ExponentBase2 =>
                $"rsxSplat(exp2({scalar0}))",
            RsxFragmentOpcode.LogarithmBase2 =>
                $"rsxSplat(log2({scalar0}))",
            RsxFragmentOpcode.SetTrue => "vec4(1.0)",
            RsxFragmentOpcode.SetFalse => "vec4(0.0)",
            RsxFragmentOpcode.Cosine => $"rsxSplat(cos({scalar0}))",
            RsxFragmentOpcode.Sine => $"rsxSplat(sin({scalar0}))",
            RsxFragmentOpcode.TextureLod =>
                $"textureLod(rsxSampler{instruction.TextureUnit}, ({s0}).{(cubeSampler || volumeSampler ? "xyz" : "xy")}, {scalar1})",
            RsxFragmentOpcode.TextureBias =>
                $"texture(rsxSampler{instruction.TextureUnit}, ({s0}).{(cubeSampler || volumeSampler ? "xyz" : "xy")}, {scalar1})",
            RsxFragmentOpcode.Dot2 =>
                $"rsxSplat(dot(({s0}).xy, ({s1}).xy))",
            RsxFragmentOpcode.Normalize =>
                $"rsxNormalize(({s0}).xyz)",
            RsxFragmentOpcode.Divide => $"({s0} / {scalar1})",
            RsxFragmentOpcode.DivideBySquareRoot =>
                $"rsxDivideBySqrt({s0}, {scalar1})",
            _ => null
        };
    }

    private static string FragmentSource(
        RsxFragmentInstruction instruction,
        uint source,
        int sourceIndex)
    {
        var operand = new RsxFragmentOperand(sourceIndex, source);
        string value = operand.RegisterKind switch
        {
            RsxFragmentRegisterType.Temporary =>
                $"{(operand.Fp16 ? "H" : "R")}[{operand.RegisterIndex}]",
            RsxFragmentRegisterType.Input =>
                FragmentInput(instruction.SourceAttribute),
            RsxFragmentRegisterType.InlineConstant =>
                instruction.DirectCodeConstantIndex is { } codeIndex
                ? OpenGlCodePixelConstantUniformLayout.ElementName(
                    codeIndex)
                : instruction.Constant is { } constant
                    ? FormatInlineConstant(constant)
                    : "vec4(0.0)",
            _ => "vec4(0.0)"
        };
        value += $".{FragmentSwizzle(operand)}";
        if (operand.Absolute)
            value = $"abs({value})";
        if (operand.Negate)
            value = $"(-{value})";
        return value;
    }

    private static string FragmentInput(
        RsxFragmentInputAttribute input) => input switch
    {
        RsxFragmentInputAttribute.WindowPosition =>
            "vec4(gl_FragCoord.xyz, 1.0)",
        RsxFragmentInputAttribute.Color0 => "rsxColor0",
        RsxFragmentInputAttribute.Color1 => "rsxColor1",
        RsxFragmentInputAttribute.Fog => "vec4(0.0)",
        >= RsxFragmentInputAttribute.TextureCoordinate0 and
            <= RsxFragmentInputAttribute.TextureCoordinate7 =>
            $"rsxTexcoord{(byte)input - (byte)RsxFragmentInputAttribute.TextureCoordinate0}",
        RsxFragmentInputAttribute.SignedSideArea =>
            "vec4(gl_FrontFacing ? 1.0 : -1.0)",
        _ => "vec4(0.0)"
    };

    private static string FormatInlineConstant(
        RsxFragmentInlineConstant constant) => string.Create(
        CultureInfo.InvariantCulture,
        $"vec4({constant.X:R}, {constant.Y:R}, {constant.Z:R}, {constant.W:R})");

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

    private static string? FragmentScale(
        RsxFragmentResultScale scale) => scale switch
    {
        RsxFragmentResultScale.None => "1.0",
        RsxFragmentResultScale.MultiplyBy2 => "2.0",
        RsxFragmentResultScale.MultiplyBy4 => "4.0",
        RsxFragmentResultScale.MultiplyBy8 => "8.0",
        RsxFragmentResultScale.DivideBy2 => "0.5",
        RsxFragmentResultScale.DivideBy4 => "0.25",
        RsxFragmentResultScale.DivideBy8 => "0.125",
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
        builder.AppendLine($"  vec4 {bank}[{registers[^1] + 1}];");
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

    private readonly record struct FragmentControlFlowPlan(
        int InstructionIndex,
        int CloseOffset,
        string OpeningStatement);

    private readonly record struct FragmentRegisterUsage(
        int[] FullRegisters,
        int[] HalfRegisters);
}

internal sealed record RsxFragmentGlsl330LoweringResult(
    string? Glsl,
    bool TranslationReady,
    ImmutableArray<string> Blockers);
