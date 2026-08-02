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
                ? program.ProgramControl.EmittedControl
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
        uint? fragmentProgramControl,
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
                $"uniform vec4 {MapRenderOpenGlCodePixelConstantUniformLayout.ArrayName}[{MapRenderOpenGlCodePixelConstantUniformLayout.Count}];");
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
        foreach (RsxFragmentInstruction instruction in instructions)
        {
            if (instruction.Branch)
            {
                blockers.Add("fragmentBranchControlFlow=unlowered");
                builder.AppendLine(
                    "  // branch/control-flow instruction; no behavior invented");
                continue;
            }
            if (instruction.Opcode == 0x12)
            {
                blockers.Add("fragmentConditionalKill=unlowered");
                builder.AppendLine(
                    "  // condition-based KIL/discard is outside the supported subset");
                continue;
            }

            bool conditionSensitive =
                instruction.CondWriteEnabled ||
                instruction.ConditionTest != RsxFragmentConditionTest.True ||
                instruction.ConditionWriteRegister1 ||
                instruction.ConditionReadRegister1;
            bool conditionProducer = conditionSensitive &&
                                     IsSelectedFragmentConditionProducer(
                                         instruction);
            bool conditionConsumer = conditionSensitive &&
                                     IsSelectedFragmentConditionConsumer(
                                         instruction);
            if (conditionSensitive && !conditionProducer && !conditionConsumer)
            {
                AddFragmentConditionBlocker(instruction, blockers);
                builder.AppendLine(
                    "  // fragment condition form is outside the supported CC0 subset");
                continue;
            }
            if (instruction.Scale == 4)
                blockers.Add("fragmentScale4=unmapped");
            if (HasSourceType3(instruction))
                blockers.Add("fragmentSourceRegisterType3=unmapped");
            if (instruction.Opcode == 0 || IsReservedNoOp(instruction))
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

            if (conditionProducer || conditionConsumer)
            {
                string value = $"rsxCcValue{instruction.Index}";
                builder.AppendLine($"  vec4 {value} = {expression};");
                if (conditionProducer)
                {
                    string ccMask = FragmentWriteMask(instruction.WriteMask);
                    builder.AppendLine(
                        $"  rsxCc0.{ccMask} = {value}.{ccMask};");
                }
                else
                {
                    AppendSelectedFragmentConditionWrite(
                        builder,
                        instruction,
                        value);
                }
                continue;
            }

            if (!instruction.NoDest && instruction.WriteMask != 0)
            {
                string destination = instruction.DestFp16
                    ? $"H[{instruction.DestRegister}]"
                    : $"R[{instruction.DestRegister}]";
                string mask = FragmentWriteMask(instruction.WriteMask);
                builder.AppendLine(
                    $"  {destination}.{mask} = ({expression}).{mask};");
            }
        }
        bool fp32Exports = fragmentProgramControl is { } control &&
                           (control & 0x40) != 0;
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
            (depthControl & 0x0e) != 0)
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
            fragmentProgramControl,
            blockers);
    }

    private static FragmentRegisterUsage ReadFragmentRegisterUsage(
        IReadOnlyList<RsxFragmentInstruction> instructions,
        RsxFragmentSamplerFeatureProfile samplerProfile,
        uint? fragmentProgramControl)
    {
        var fullRegisters = new SortedSet<int>();
        var halfRegisters = new SortedSet<int>();
        bool fp32Exports = fragmentProgramControl is { } control &&
                           (control & 0x40) != 0;
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
            (depthControl & 0x0e) != 0)
        {
            fullRegisters.Add(1);
        }

        foreach (RsxFragmentInstruction instruction in instructions)
        {
            if (!FragmentInstructionEmitsExpression(
                    instruction,
                    samplerProfile,
                    out bool conditionProducer))
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

            if (!conditionProducer &&
                !instruction.NoDest &&
                instruction.WriteMask != 0)
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
        RsxFragmentSamplerFeatureProfile samplerProfile,
        out bool conditionProducer)
    {
        conditionProducer = false;
        if (instruction.Branch || instruction.Opcode == 0x12)
            return false;

        bool conditionSensitive =
            instruction.CondWriteEnabled ||
            instruction.ConditionTest != RsxFragmentConditionTest.True ||
            instruction.ConditionWriteRegister1 ||
            instruction.ConditionReadRegister1;
        conditionProducer = conditionSensitive &&
                            IsSelectedFragmentConditionProducer(instruction);
        bool conditionConsumer = conditionSensitive &&
                                 IsSelectedFragmentConditionConsumer(
                                     instruction);
        if (conditionSensitive && !conditionProducer && !conditionConsumer)
            return false;
        if (instruction.Opcode == 0 || IsReservedNoOp(instruction))
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
        if (RsxFragmentInstruction.SourceRegisterType(source) != 0)
            return;
        (FragmentSourceFp16(source)
                ? halfRegisters
                : fullRegisters)
            .Add(FragmentSourceIndex(source));
    }

    private static bool IsSelectedFragmentConditionProducer(
        RsxFragmentInstruction instruction) =>
        instruction.CondWriteEnabled &&
        instruction.ConditionTest == RsxFragmentConditionTest.True &&
        !instruction.ConditionWriteRegister1 &&
        !instruction.ConditionReadRegister1 &&
        instruction.NoDest &&
        instruction.WriteMask is 0x1 or 0x4 or 0x8 &&
        (instruction.Opcode is 0x01 or 0x0b or 0x0e ||
         (instruction.Opcode == 0x0a &&
          instruction.WriteMask == 0x1));

    private static bool IsSelectedFragmentConditionConsumer(
        RsxFragmentInstruction instruction) =>
        !instruction.CondWriteEnabled &&
        !instruction.ConditionWriteRegister1 &&
        !instruction.ConditionReadRegister1 &&
        !instruction.NoDest &&
        instruction.WriteMask != 0 &&
        (instruction.ConditionTest is RsxFragmentConditionTest.Equal or
            RsxFragmentConditionTest.GreaterThan or
            RsxFragmentConditionTest.NotEqual) &&
        (ConditionSwizzleIsComponent(instruction, 0) ||
         ConditionSwizzleIsComponent(instruction, 2) ||
         ConditionSwizzleIsComponent(instruction, 3)) &&
        (instruction.Opcode is 0x01 or 0x02 or 0x04 or 0x0b or 0x1c ||
         (instruction.Opcode == 0x3a &&
          instruction.ConditionTest == RsxFragmentConditionTest.Equal &&
          ConditionSwizzleIsComponent(instruction, 0) &&
          instruction.WriteMask == 0x8));

    private static bool ConditionSwizzleIsComponent(
        RsxFragmentInstruction instruction,
        int component) =>
        instruction.ConditionSwizzleX == component &&
        instruction.ConditionSwizzleY == component &&
        instruction.ConditionSwizzleZ == component &&
        instruction.ConditionSwizzleW == component;

    private static void AddFragmentConditionBlocker(
        RsxFragmentInstruction instruction,
        ISet<string> blockers)
    {
        if (instruction.ConditionWriteRegister1 ||
            instruction.ConditionReadRegister1)
        {
            blockers.Add("fragmentConditionRegister1=unlowered");
            return;
        }
        if (instruction.CondWriteEnabled &&
            instruction.ConditionTest != RsxFragmentConditionTest.True)
        {
            blockers.Add("fragmentPredicatedConditionWrite=unlowered");
            return;
        }
        if (instruction.CondWriteEnabled &&
            !instruction.NoDest &&
            instruction.WriteMask != 0)
        {
            blockers.Add("fragmentDestinationConditionWrite=unlowered");
            return;
        }
        blockers.Add(instruction.CondWriteEnabled
            ? "fragmentConditionProducerShape=unlowered"
            : "fragmentConditionConsumerShape=unlowered");
    }

    private static void AppendSelectedFragmentConditionWrite(
        StringBuilder builder,
        RsxFragmentInstruction instruction,
        string value)
    {
        string destination = instruction.DestFp16
            ? $"H[{instruction.DestRegister}]"
            : $"R[{instruction.DestRegister}]";
        string test = FragmentConditionTestName(instruction.ConditionTest);
        for (int component = 0; component < 4; component++)
        {
            if ((instruction.WriteMask & (1 << component)) == 0)
                continue;
            char destinationComponent = SwizzleChar(component);
            char conditionComponent = SwizzleChar(
                instruction.ConditionSwizzle(component));
            builder.AppendLine(
                $"  if (rsxCcTest{test}(rsxCc0.{conditionComponent})) {destination}.{destinationComponent} = {value}.{destinationComponent};");
        }
    }

    private static string FragmentConditionTestName(
        RsxFragmentConditionTest test) => test switch
    {
        RsxFragmentConditionTest.False => "FL",
        RsxFragmentConditionTest.LessThan => "LT",
        RsxFragmentConditionTest.Equal => "EQ",
        RsxFragmentConditionTest.LessThanOrEqual => "LE",
        RsxFragmentConditionTest.GreaterThan => "GT",
        RsxFragmentConditionTest.NotEqual => "NE",
        RsxFragmentConditionTest.GreaterThanOrEqual => "GE",
        RsxFragmentConditionTest.True => "TR",
        _ => throw new ArgumentOutOfRangeException(nameof(test))
    };

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
        if (shadowSampler && IsFragmentTextureOpcode(instruction.Opcode) &&
            instruction.Opcode is not 0x17 and not 0x18)
        {
            blockers.Add(
                $"fragmentShadowSamplerDest{instruction.TextureUnit}=opcode0x{instruction.Opcode:X2}_unlowered");
            return null;
        }
        if (cubeSampler && instruction.Opcode == 0x18)
        {
            blockers.Add(
                $"fragmentCubeSamplerDest{instruction.TextureUnit}=projectiveTextureOpcodeUnlowered");
        }
        if (volumeSampler && instruction.Opcode == 0x18)
        {
            blockers.Add(
                $"fragmentVolumeSamplerDest{instruction.TextureUnit}=projectiveTextureOpcodeUnlowered");
            return null;
        }
        return instruction.Opcode switch
        {
            0x01 => s0,
            0x02 => $"({s0} * {s1})",
            0x03 => $"({s0} + {s1})",
            0x04 => $"({s0} * {s1} + {s2})",
            0x05 => $"rsxSplat(dot(({s0}).xyz, ({s1}).xyz))",
            0x06 => $"rsxSplat(dot({s0}, {s1}))",
            0x08 => $"min({s0}, {s1})",
            0x09 => $"max({s0}, {s1})",
            0x0a => $"rsxBool4(lessThan({s0}, {s1}))",
            0x0b => $"rsxBool4(greaterThanEqual({s0}, {s1}))",
            0x0c => $"rsxBool4(lessThanEqual({s0}, {s1}))",
            0x0d => $"rsxBool4(greaterThan({s0}, {s1}))",
            0x0e => $"rsxBool4(notEqual({s0}, {s1}))",
            0x0f => $"rsxBool4(equal({s0}, {s1}))",
            0x10 => $"fract({s0})",
            0x11 => $"floor({s0})",
            0x15 => $"dFdx({s0})",
            0x16 => $"dFdy({s0})",
            0x17 when shadowSampler && !cubeSampler =>
                $"rsxSplat(texture(rsxSampler{instruction.TextureUnit}, ({s0}).xyz))",
            0x17 =>
                $"texture(rsxSampler{instruction.TextureUnit}, ({s0}).{(cubeSampler || volumeSampler ? "xyz" : "xy")})",
            0x18 when shadowSampler && !cubeSampler =>
                $"rsxSplat(textureProj(rsxSampler{instruction.TextureUnit}, {s0}))",
            0x18 when !cubeSampler && !volumeSampler =>
                $"textureProj(rsxSampler{instruction.TextureUnit}, {s0})",
            0x1a => $"rsxSplat(1.0 / {scalar0})",
            0x1b => $"rsxSplat(inversesqrt(max({scalar0}, 0.0000001)))",
            0x1c => $"rsxSplat(exp2({scalar0}))",
            0x1d => $"rsxSplat(log2(max({scalar0}, 0.0000001)))",
            0x20 => "vec4(1.0)",
            0x21 => "vec4(0.0)",
            0x22 => $"rsxSplat(cos({scalar0}))",
            0x23 => $"rsxSplat(sin({scalar0}))",
            0x2f =>
                $"textureLod(rsxSampler{instruction.TextureUnit}, ({s0}).{(cubeSampler || volumeSampler ? "xyz" : "xy")}, {scalar1})",
            0x31 =>
                $"texture(rsxSampler{instruction.TextureUnit}, ({s0}).{(cubeSampler || volumeSampler ? "xyz" : "xy")}, {scalar1})",
            0x38 => $"rsxSplat(dot(({s0}).xy, ({s1}).xy))",
            0x39 => $"vec4(normalize(({s0}).xyz), ({s0}).w)",
            0x3a => $"({s0} / {scalar1})",
            0x3b =>
                $"({s0} * inversesqrt(max({scalar1}, 0.0000001)))",
            _ => null
        };
    }

    private static bool IsFragmentTextureOpcode(byte opcode) =>
        opcode is 0x17 or 0x18 or 0x19 or 0x2f or 0x31;

    private static string FragmentSource(
        RsxFragmentInstruction instruction,
        uint source,
        int sourceIndex)
    {
        string value = RsxFragmentInstruction.SourceRegisterType(source) switch
        {
            0 => $"{(FragmentSourceFp16(source) ? "H" : "R")}[{FragmentSourceIndex(source)}]",
            1 => FragmentInput(instruction.SourceAttribute),
            2 => instruction.DirectCodeConstantIndex is { } codeIndex
                ? MapRenderOpenGlCodePixelConstantUniformLayout.ElementName(
                    codeIndex)
                : instruction.Constant is { } constant
                    ? FormatInlineConstant(constant)
                    : "vec4(0.0)",
            _ => "vec4(0.0)"
        };
        value += $".{FragmentSwizzle(source)}";
        if (FragmentSourceAbs(source, sourceIndex))
            value = $"abs({value})";
        if (FragmentSourceNeg(source))
            value = $"(-{value})";
        return value;
    }

    private static string FragmentInput(int input) => input switch
    {
        0 => "vec4(gl_FragCoord.xyz, 1.0)",
        1 => "rsxColor0",
        2 => "rsxColor1",
        3 => "vec4(0.0)",
        >= 4 and <= 11 => $"rsxTexcoord{input - 4}",
        14 => "vec4(gl_FrontFacing ? 1.0 : -1.0)",
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
                RsxFragmentInstruction.SourceRegisterType(
                    instruction.Src0) == 3) ||
               (count > 1 &&
                RsxFragmentInstruction.SourceRegisterType(
                    instruction.Src1) == 3) ||
               (count > 2 &&
                RsxFragmentInstruction.SourceRegisterType(
                    instruction.Src2) == 3);
    }

    private static bool IsReservedNoOp(RsxFragmentInstruction instruction) =>
        !instruction.Branch &&
        instruction.Opcode is 0x3d or 0x3e &&
        (instruction.Dst & 0xff00ffffu) ==
        ((0x40u | instruction.Opcode) << 24 | 0x001e7eu) &&
        instruction.Src0 == 0x1c9dc800u &&
        instruction.Src1 == 0x0001c800u &&
        instruction.Src2 == 0x0001c800u &&
        instruction.NoDest &&
        !instruction.End &&
        !instruction.Saturate &&
        instruction.Scale == 0 &&
        !instruction.CondWriteEnabled &&
        instruction.ConditionTest == RsxFragmentConditionTest.True;

    private static string? FragmentScale(int scale) => scale switch
    {
        0 => "1.0",
        1 => "2.0",
        2 => "4.0",
        3 => "8.0",
        5 => "0.5",
        6 => "0.25",
        7 => "0.125",
        _ => null
    };

    private static int FragmentSourceIndex(uint source) =>
        (int)((source >> 2) & 0x3f);

    private static bool FragmentSourceFp16(uint source) =>
        ((source >> 8) & 1) != 0;

    private static bool FragmentSourceNeg(uint source) =>
        ((source >> 17) & 1) != 0;

    private static bool FragmentSourceAbs(uint source, int index) =>
        index == 0
            ? ((source >> 29) & 1) != 0
            : ((source >> 18) & 1) != 0;

    private static string FragmentSwizzle(uint source) =>
        string.Create(4, source, static (span, value) =>
        {
            span[0] = SwizzleChar((int)((value >> 9) & 3));
            span[1] = SwizzleChar((int)((value >> 11) & 3));
            span[2] = SwizzleChar((int)((value >> 13) & 3));
            span[3] = SwizzleChar((int)((value >> 15) & 3));
        });

    private static char SwizzleChar(int value) => "xyzw"[value & 3];

    private static string FragmentWriteMask(int mask)
    {
        var value = new StringBuilder(4);
        if ((mask & 1) != 0) value.Append('x');
        if ((mask & 2) != 0) value.Append('y');
        if ((mask & 4) != 0) value.Append('z');
        if ((mask & 8) != 0) value.Append('w');
        return value.ToString();
    }

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

    private readonly record struct FragmentRegisterUsage(
        int[] FullRegisters,
        int[] HalfRegisters);
}

internal sealed record RsxFragmentGlsl330LoweringResult(
    string? Glsl,
    bool TranslationReady,
    ImmutableArray<string> Blockers);
