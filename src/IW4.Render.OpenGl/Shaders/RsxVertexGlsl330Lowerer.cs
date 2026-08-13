using System.Collections.Immutable;
using System.Text;
using IW4.Render.Shaders;

namespace IW4.Render.OpenGl.Shaders;

/// <summary>
/// OpenGL-owned lowering of immutable RSX vertex semantics to GLSL 330.
/// </summary>
internal static class RsxVertexGlsl330Lowerer
{
    internal static RsxVertexGlsl330LoweringResult Lower(
        RsxVertexProgramIr program)
    {
        ArgumentNullException.ThrowIfNull(program);

        var blockers = new SortedSet<string>(StringComparer.Ordinal);
        if (!program.HasValidUpload)
            blockers.Add("vertexUploadHeader=invalid");
        if (program.Instructions.IsEmpty)
            blockers.Add("vertexInstructions=missing");
        if (program.Instructions.IsEmpty)
        {
            return CreateResult(
                glsl: null,
                blockers);
        }

        string glsl = BuildGlsl(program.Instructions, blockers);
        return CreateResult(glsl, blockers);
    }

    private static RsxVertexGlsl330LoweringResult CreateResult(
        string? glsl,
        SortedSet<string> blockers)
    {
        ImmutableArray<string> immutableBlockers = blockers.ToImmutableArray();
        return new RsxVertexGlsl330LoweringResult(
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

    internal static string BuildGlsl(
        IReadOnlyList<RsxVertexInstruction> instructions,
        ISet<string> blockers)
    {
        VertexRegisterUsage registerUsage =
            ReadVertexRegisterUsage(instructions);
        var builder = new StringBuilder();
        builder.AppendLine("#version 330 core");
        for (int i = 0; i < 16; i++)
            builder.AppendLine($"layout(location = {i}) in vec4 aRsxInput{i};");
        builder.AppendLine(
            $"uniform vec4 rsxVertexConst[{RsxVertexConstantLayout.Count}];");
        builder.AppendLine("out vec4 rsxColor0;");
        builder.AppendLine("out vec4 rsxColor1;");
        for (int i = 0; i < 8; i++)
            builder.AppendLine($"out vec4 rsxTexcoord{i};");
        builder.AppendLine("vec4 rsxSplat(float v) { return vec4(v); }");
        builder.AppendLine("vec4 rsxBool4(bvec4 v) { return vec4(v.x ? 1.0 : 0.0, v.y ? 1.0 : 0.0, v.z ? 1.0 : 0.0, v.w ? 1.0 : 0.0); }");
        bool usesConditionCodes = instructions.Any(instruction =>
            instruction.CondTestEnabled ||
            instruction.CondUpdateEnabled);
        if (usesConditionCodes)
        {
            builder.AppendLine("bool rsxCcTestFL(float v) { return false; }");
            builder.AppendLine("bool rsxCcTestLT(float v) { return !isnan(v) && v < 0.0; }");
            builder.AppendLine("bool rsxCcTestEQ(float v) { return !isnan(v) && v == 0.0; }");
            builder.AppendLine("bool rsxCcTestLE(float v) { return !isnan(v) && v <= 0.0; }");
            builder.AppendLine("bool rsxCcTestGT(float v) { return !isnan(v) && v > 0.0; }");
            builder.AppendLine("bool rsxCcTestNE(float v) { return isnan(v) || v != 0.0; }");
            builder.AppendLine("bool rsxCcTestGE(float v) { return !isnan(v) && v >= 0.0; }");
            builder.AppendLine("bool rsxCcTestTR(float v) { return true; }");
        }
        builder.AppendLine("void main() {");
        AppendRegisterBankDeclaration(
            builder,
            "V",
            registerUsage.InputRegisters);
        AppendRegisterBankDeclaration(
            builder,
            "R",
            registerUsage.TempRegisters);
        AppendRegisterBankDeclaration(
            builder,
            "O",
            registerUsage.OutputRegisters);
        foreach (int register in registerUsage.InputRegisters)
            builder.AppendLine($"  V[{register}] = aRsxInput{register};");
        AppendRegisterBankInitialization(
            builder,
            "R",
            registerUsage.TempRegisters,
            "vec4(0.0)");
        AppendRegisterBankInitialization(
            builder,
            "O",
            registerUsage.OutputRegisters,
            "vec4(0.0, 0.0, 0.0, 1.0)");
        if (usesConditionCodes)
        {
            // NV_vertex_program3 defines both CC registers as EQ on entry.
            // Raw result values are a lossless GLSL representation of the
            // LT/EQ/GT/UN states when subsequently compared with zero.
            builder.AppendLine("  vec4 rsxCc[2];");
            builder.AppendLine("  rsxCc[0] = vec4(0.0);");
            builder.AppendLine("  rsxCc[1] = vec4(0.0);");
        }

        foreach (RsxVertexInstruction instruction in instructions)
        {
            if (instruction.HasControlFlow)
                blockers.Add("vertexBranchControlFlow=unlowered");
            if (instruction.IndexConst)
                blockers.Add("vertexIndexedConstants=unlowered");
            AppendVertexInstruction(
                builder,
                instruction,
                blockers);
        }

        AppendVertexExportGlsl(builder);
        builder.AppendLine("  rsxColor0 = O[1]; rsxColor1 = O[2];");
        for (int i = 0; i < 8; i++)
            builder.AppendLine($"  rsxTexcoord{i} = O[{i + 7}];");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static VertexRegisterUsage ReadVertexRegisterUsage(
        IReadOnlyList<RsxVertexInstruction> instructions)
    {
        var inputRegisters = new SortedSet<int>();
        var tempRegisters = new SortedSet<int>();
        var outputRegisters = new SortedSet<int>
        {
            // Fixed translated-shader exports. O[0] is native clip position;
            // O[1]/O[2] are colors; O[7]..O[14] are texture coordinates.
            0,
            1,
            2,
            7,
            8,
            9,
            10,
            11,
            12,
            13,
            14
        };

        foreach (RsxVertexInstruction instruction in instructions)
        {
            if (instruction.VecOpcode != 0 &&
                instruction.VecWriteMask != 0 &&
                VertexExpression(instruction, scalar: false) is not null)
            {
                int sourceMask = RsxVertexInstruction.VectorSourceMask(
                    instruction.VecOpcode);
                if ((sourceMask & 1) != 0)
                {
                    AddVertexSourceRegister(
                        instruction,
                        instruction.Source0,
                        inputRegisters,
                        tempRegisters);
                }
                if ((sourceMask & 2) != 0)
                {
                    AddVertexSourceRegister(
                        instruction,
                        instruction.Source1,
                        inputRegisters,
                        tempRegisters);
                }
                if ((sourceMask & 4) != 0)
                {
                    AddVertexSourceRegister(
                        instruction,
                        instruction.Source2,
                        inputRegisters,
                        tempRegisters);
                }
                AddVertexDestinationRegister(
                    instruction,
                    scalar: false,
                    tempRegisters,
                    outputRegisters);
            }

            if (instruction.ScaOpcode != 0 &&
                instruction.ScaWriteMask != 0 &&
                RsxVertexInstruction.ScalarReadsSource2(
                    instruction.ScaOpcode) &&
                VertexExpression(instruction, scalar: true) is not null)
            {
                AddVertexSourceRegister(
                    instruction,
                    instruction.Source2,
                    inputRegisters,
                    tempRegisters);
                AddVertexDestinationRegister(
                    instruction,
                    scalar: true,
                    tempRegisters,
                    outputRegisters);
            }
        }

        return new VertexRegisterUsage(
            inputRegisters.ToArray(),
            tempRegisters.ToArray(),
            outputRegisters.ToArray());
    }

    private static void AddVertexSourceRegister(
        RsxVertexInstruction instruction,
        uint source,
        ISet<int> inputRegisters,
        ISet<int> tempRegisters)
    {
        switch (RsxVertexInstruction.SourceRegisterType(source))
        {
            case 1:
                tempRegisters.Add((int)((source >> 2) & 0x3f));
                break;
            case 2:
                inputRegisters.Add(instruction.InputSource);
                break;
        }
    }

    private static void AddVertexDestinationRegister(
        RsxVertexInstruction instruction,
        bool scalar,
        ISet<int> tempRegisters,
        ISet<int> outputRegisters)
    {
        bool writesOutput = scalar
            ? instruction.ScaResult
            : instruction.VecResult;
        if (writesOutput && instruction.ResultIndex != 0x1f)
            outputRegisters.Add(instruction.ResultIndex);

        int temp = scalar
            ? instruction.ScaDestTemp
            : instruction.VecDestTemp;
        if (temp != 0x3f)
            tempRegisters.Add(temp);
    }

    private static void AppendVertexInstruction(
        StringBuilder builder,
        RsxVertexInstruction instruction,
        ISet<string> blockers)
    {
        string? predicate = AppendVertexConditionPredicate(
            builder,
            instruction);
        VertexSlotValue? vectorValue = AppendVertexSlotValue(
            builder,
            instruction,
            scalar: false,
            blockers);
        VertexSlotValue? scalarValue = AppendVertexSlotValue(
            builder,
            instruction,
            scalar: true,
            blockers);

        if (vectorValue is { } vector)
        {
            AppendVertexSlotWrites(
                builder,
                instruction,
                scalar: false,
                vector,
                predicate);
        }
        if (scalarValue is { } scalar)
        {
            AppendVertexSlotWrites(
                builder,
                instruction,
                scalar: true,
                scalar,
                predicate);
        }

        if (!instruction.CondUpdateEnabled)
            return;

        VertexSlotValue? conditionValue = instruction.CondUpdateFromVector
            ? vectorValue
            : scalarValue;
        if (conditionValue is not { } selected)
        {
            blockers.Add("vertexConditionUpdateSource=unlowered");
            return;
        }

        AppendMaskedVertexWrite(
            builder,
            $"rsxCc[{instruction.ConditionRegister}]",
            selected.Name,
            selected.Mask,
            predicate);
    }

    private static VertexSlotValue? AppendVertexSlotValue(
        StringBuilder builder,
        RsxVertexInstruction instruction,
        bool scalar,
        ISet<string> blockers)
    {
        byte opcode = scalar
            ? instruction.ScaOpcode
            : instruction.VecOpcode;
        int mask = scalar
            ? instruction.ScaWriteMask
            : instruction.VecWriteMask;
        if (opcode == 0 || mask == 0)
            return null;
        string? expression = VertexExpression(instruction, scalar);
        if (expression is null)
        {
            blockers.Add(
                $"vertex{(scalar ? "Scalar" : "Vector")}Opcode0x{opcode:X2}=unmapped");
            builder.AppendLine(
                $"  // unmapped RSX vertex opcode 0x{opcode:X2}; no value invented");
            return null;
        }
        if (instruction.Saturate)
            expression = $"clamp({expression}, vec4(0.0), vec4(1.0))";
        string valueName =
            $"rsxValue{instruction.Index}{(scalar ? 'S' : 'V')}";
        builder.AppendLine(
            $"  vec4 {valueName} = {expression};");
        return new VertexSlotValue(valueName, mask);
    }

    private static string? AppendVertexConditionPredicate(
        StringBuilder builder,
        RsxVertexInstruction instruction)
    {
        if (!instruction.CondTestEnabled ||
            instruction.ConditionCode == 7)
        {
            return null;
        }

        string predicateName = $"rsxPredicate{instruction.Index}";
        string test = VertexConditionTestName(
            instruction.ConditionCode);
        builder.Append($"  bvec4 {predicateName} = bvec4(");
        for (int component = 0; component < 4; component++)
        {
            if (component != 0)
                builder.Append(", ");
            char conditionComponent = SwizzleChar(
                instruction.ConditionSwizzle(component));
            builder.Append(
                $"rsxCcTest{test}(rsxCc[{instruction.ConditionRegister}].{conditionComponent})");
        }
        builder.AppendLine(");");
        return predicateName;
    }

    private static string VertexConditionTestName(int condition) =>
        condition switch
        {
            0 => "FL",
            1 => "LT",
            2 => "EQ",
            3 => "LE",
            4 => "GT",
            5 => "NE",
            6 => "GE",
            7 => "TR",
            _ => throw new ArgumentOutOfRangeException(nameof(condition))
        };

    private static void AppendVertexSlotWrites(
        StringBuilder builder,
        RsxVertexInstruction instruction,
        bool scalar,
        VertexSlotValue value,
        string? predicate)
    {
        bool writesOutput = scalar
            ? instruction.ScaResult
            : instruction.VecResult;
        if (writesOutput && instruction.ResultIndex != 0x1f)
        {
            AppendMaskedVertexWrite(
                builder,
                $"O[{instruction.ResultIndex}]",
                value.Name,
                value.Mask,
                predicate);
        }

        int temp = scalar
            ? instruction.ScaDestTemp
            : instruction.VecDestTemp;
        if (temp != 0x3f)
        {
            AppendMaskedVertexWrite(
                builder,
                $"R[{temp}]",
                value.Name,
                value.Mask,
                predicate);
        }
    }

    private static void AppendMaskedVertexWrite(
        StringBuilder builder,
        string destination,
        string value,
        int mask,
        string? predicate)
    {
        if (predicate is null)
        {
            string writeMask = VertexWriteMask(mask);
            builder.AppendLine(
                $"  {destination}.{writeMask} = {value}.{writeMask};");
            return;
        }

        for (int component = 0; component < 4; component++)
        {
            int maskBit = 1 << (3 - component);
            if ((mask & maskBit) == 0)
                continue;
            char destinationComponent = SwizzleChar(component);
            builder.AppendLine(
                $"  if ({predicate}.{destinationComponent}) {destination}.{destinationComponent} = {value}.{destinationComponent};");
        }
    }

    private static string? VertexExpression(
        RsxVertexInstruction instruction,
        bool scalar)
    {
        if (scalar)
        {
            string source = VertexSource(
                instruction,
                instruction.Source2,
                2);
            // NV/RSX scalar operands consume the first post-swizzle lane and
            // replicate the result. Treating the operation as component-wise
            // makes a non-X destination read an unrelated register lane.
            string scalarSource = $"({source}).x";
            return instruction.ScaOpcode switch
            {
                0x01 => $"rsxSplat({scalarSource})",
                0x02 or 0x03 => $"rsxSplat(1.0 / {scalarSource})",
                0x04 => $"rsxSplat(inversesqrt(max({scalarSource}, 0.0000001)))",
                0x0d => $"rsxSplat(log2(max({scalarSource}, 0.0000001)))",
                0x0e => $"rsxSplat(exp2({scalarSource}))",
                0x0f => $"rsxSplat(sin({scalarSource}))",
                0x10 => $"rsxSplat(cos({scalarSource}))",
                _ => null
            };
        }

        string s0 = VertexSource(
            instruction,
            instruction.Source0,
            0);
        string s1 = VertexSource(
            instruction,
            instruction.Source1,
            1);
        string s2 = VertexSource(
            instruction,
            instruction.Source2,
            2);
        return instruction.VecOpcode switch
        {
            0x01 => s0,
            0x02 => $"({s0} * {s1})",
            // NV40 VP ADD consumes source slots 0 and 2. Source slot 1 is
            // unused padding for this opcode; using it replaces matrix
            // translation operands with unrelated vertex inputs.
            0x03 => $"({s0} + {s2})",
            0x04 => $"({s0} * {s1} + {s2})",
            0x05 => $"rsxSplat(dot(({s0}).xyz, ({s1}).xyz))",
            0x06 => $"rsxSplat(dot(vec4(({s0}).xyz, 1.0), {s1}))",
            0x07 => $"rsxSplat(dot({s0}, {s1}))",
            0x08 => $"vec4(1.0, ({s0}).y * ({s1}).y, ({s0}).z, ({s1}).w)",
            0x09 => $"min({s0}, {s1})",
            0x0a => $"max({s0}, {s1})",
            0x0b => $"rsxBool4(lessThan({s0}, {s1}))",
            0x0c => $"rsxBool4(greaterThanEqual({s0}, {s1}))",
            0x0e => $"fract({s0})",
            0x0f => $"floor({s0})",
            0x10 => $"rsxBool4(equal({s0}, {s1}))",
            0x11 => "vec4(0.0)",
            0x12 => $"rsxBool4(greaterThan({s0}, {s1}))",
            0x13 => $"rsxBool4(lessThanEqual({s0}, {s1}))",
            0x14 => $"rsxBool4(notEqual({s0}, {s1}))",
            0x15 => "vec4(1.0)",
            0x16 => $"sign({s0})",
            _ => null
        };
    }

    private static string VertexSource(
        RsxVertexInstruction instruction,
        uint source,
        int sourceIndex)
    {
        string value = RsxVertexInstruction.SourceRegisterType(source) switch
        {
            1 => $"R[{(source >> 2) & 0x3f}]",
            2 => $"V[{instruction.InputSource}]",
            3 => $"rsxVertexConst[{instruction.ConstSource}]",
            _ => "vec4(0.0)"
        };
        value += $".{VertexSwizzle(source)}";
        bool absolute = sourceIndex switch
        {
            0 => instruction.Source0Abs,
            1 => instruction.Source1Abs,
            _ => instruction.Source2Abs
        };
        if (absolute)
            value = $"abs({value})";
        if ((source & 0x10000u) != 0)
            value = $"(-{value})";
        return value;
    }

    private static string VertexSwizzle(uint source) =>
        string.Create(4, source, static (span, value) =>
        {
            span[0] = SwizzleChar((int)((value >> 14) & 3));
            span[1] = SwizzleChar((int)((value >> 12) & 3));
            span[2] = SwizzleChar((int)((value >> 10) & 3));
            span[3] = SwizzleChar((int)((value >> 8) & 3));
        });

    private static char SwizzleChar(int value) => "xyzw"[value & 3];

    private static string VertexWriteMask(int mask)
    {
        var value = new StringBuilder(4);
        if ((mask & 8) != 0) value.Append('x');
        if ((mask & 4) != 0) value.Append('y');
        if ((mask & 2) != 0) value.Append('z');
        if ((mask & 1) != 0) value.Append('w');
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

    private static void AppendVertexExportGlsl(StringBuilder builder)
    {
        builder.AppendLine("  gl_Position = O[0];");
        builder.AppendLine("  // RSX viewport Y scale is negative; desktop OpenGL's is positive.");
        builder.AppendLine("  gl_Position.y = -gl_Position.y;");
        builder.AppendLine("  // Native RSX clip Z [0,W] lowers to OpenGL clip Z [-W,W].");
        builder.AppendLine("  gl_Position.z = gl_Position.z + gl_Position.z - gl_Position.w;");
    }

    private readonly record struct VertexRegisterUsage(
        int[] InputRegisters,
        int[] TempRegisters,
        int[] OutputRegisters);

    private readonly record struct VertexSlotValue(
        string Name,
        int Mask);
}

internal sealed record RsxVertexGlsl330LoweringResult(
    string? Glsl,
    bool TranslationReady,
    ImmutableArray<string> Blockers);
