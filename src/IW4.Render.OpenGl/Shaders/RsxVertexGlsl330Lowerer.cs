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
        builder.AppendLine(
            $"  rsxColor0 = O[{(byte)RsxVertexResult.FrontColor0}]; " +
            $"rsxColor1 = O[{(byte)RsxVertexResult.FrontColor1}];");
        for (int i = 0; i < 8; i++)
            builder.AppendLine(
                $"  rsxTexcoord{i} = O[{(byte)RsxVertexResult.TextureCoordinate0 + i}];");
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
            (byte)RsxVertexResult.Position,
            (byte)RsxVertexResult.FrontColor0,
            (byte)RsxVertexResult.FrontColor1,
            (byte)RsxVertexResult.TextureCoordinate0,
            (byte)RsxVertexResult.TextureCoordinate1,
            (byte)RsxVertexResult.TextureCoordinate2,
            (byte)RsxVertexResult.TextureCoordinate3,
            (byte)RsxVertexResult.TextureCoordinate4,
            (byte)RsxVertexResult.TextureCoordinate5,
            (byte)RsxVertexResult.TextureCoordinate6,
            (byte)RsxVertexResult.TextureCoordinate7
        };

        foreach (RsxVertexInstruction instruction in instructions)
        {
            if (instruction.VectorOpcode != RsxVertexVectorOpcode.Nop &&
                instruction.VectorWriteMask != RsxVertexWriteMask.None &&
                VertexExpression(instruction, scalar: false) is not null)
            {
                RsxSourceSlotMask sourceMask =
                    RsxVertexInstruction.VectorSourceMask(
                        instruction.VectorOpcode);
                if ((sourceMask & RsxSourceSlotMask.Source0) !=
                    RsxSourceSlotMask.None)
                {
                    AddVertexSourceRegister(
                        instruction,
                        instruction.Source0,
                        inputRegisters,
                        tempRegisters);
                }
                if ((sourceMask & RsxSourceSlotMask.Source1) !=
                    RsxSourceSlotMask.None)
                {
                    AddVertexSourceRegister(
                        instruction,
                        instruction.Source1,
                        inputRegisters,
                        tempRegisters);
                }
                if ((sourceMask & RsxSourceSlotMask.Source2) !=
                    RsxSourceSlotMask.None)
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

            if (instruction.ScalarOpcode != RsxVertexScalarOpcode.Nop &&
                instruction.ScalarWriteMask != RsxVertexWriteMask.None &&
                RsxVertexInstruction.ScalarReadsSource2(
                    instruction.ScalarOpcode) &&
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
        switch (RsxVertexInstruction.SourceRegisterKind(source))
        {
            case RsxVertexRegisterType.Temporary:
                tempRegisters.Add((int)((source >> 2) & 0x3f));
                break;
            case RsxVertexRegisterType.Input:
                inputRegisters.Add((byte)instruction.InputAttribute);
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
        if (writesOutput && instruction.Result != RsxVertexResult.None)
            outputRegisters.Add((byte)instruction.Result);

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

        VertexSlotValue? conditionValue;
        if (vectorValue is not null && scalarValue is not null)
        {
            blockers.Add("vertexDualSlotConditionUpdate=unlowered");
            return;
        }

        conditionValue = vectorValue ?? scalarValue;
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
        RsxVertexWriteMask mask = scalar
            ? instruction.ScalarWriteMask
            : instruction.VectorWriteMask;
        if (opcode == 0 || mask == RsxVertexWriteMask.None)
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
            instruction.ConditionTest == RsxConditionTest.True)
        {
            return null;
        }

        string predicateName = $"rsxPredicate{instruction.Index}";
        string test = VertexConditionTestName(
            instruction.ConditionTest);
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

    private static string VertexConditionTestName(RsxConditionTest condition) =>
        condition switch
        {
            RsxConditionTest.False => "FL",
            RsxConditionTest.LessThan => "LT",
            RsxConditionTest.Equal => "EQ",
            RsxConditionTest.LessThanOrEqual => "LE",
            RsxConditionTest.GreaterThan => "GT",
            RsxConditionTest.NotEqual => "NE",
            RsxConditionTest.GreaterThanOrEqual => "GE",
            RsxConditionTest.True => "TR",
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
        if (writesOutput && instruction.Result != RsxVertexResult.None)
        {
            AppendMaskedVertexWrite(
                builder,
                $"O[{(byte)instruction.Result}]",
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
        RsxVertexWriteMask mask,
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
            RsxVertexWriteMask maskBit =
                (RsxVertexWriteMask)(1 << (3 - component));
            if ((mask & maskBit) == RsxVertexWriteMask.None)
                continue;
            char destinationComponent = SwizzleChar(
                (RsxSwizzleComponent)component);
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
            string scalarSource = $"({source}).x";
            return instruction.ScalarOpcode switch
            {
                RsxVertexScalarOpcode.Move => source,
                RsxVertexScalarOpcode.Reciprocal => $"(1.0 / {source})",
                RsxVertexScalarOpcode.ReciprocalClamped =>
                    $"clamp(1.0 / {source}, vec4(5.42101e-20), vec4(1.884467e19))",
                RsxVertexScalarOpcode.ReciprocalSquareRoot =>
                    $"rsxSplat(1.0 / sqrt(max({scalarSource}, 0.0000000001)))",
                RsxVertexScalarOpcode.LogarithmBase2 =>
                    $"log2(max({source}, vec4(0.0000000001)))",
                RsxVertexScalarOpcode.ExponentBase2 =>
                    $"exp2({source})",
                RsxVertexScalarOpcode.Sine => $"sin({source})",
                RsxVertexScalarOpcode.Cosine => $"cos({source})",
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
        return instruction.VectorOpcode switch
        {
            RsxVertexVectorOpcode.Move => s0,
            RsxVertexVectorOpcode.Multiply => $"({s0} * {s1})",
            // NV40 VP ADD consumes source slots 0 and 2. Source slot 1 is
            // unused padding for this opcode; using it replaces matrix
            // translation operands with unrelated vertex inputs.
            RsxVertexVectorOpcode.Add => $"({s0} + {s2})",
            RsxVertexVectorOpcode.MultiplyAdd => $"({s0} * {s1} + {s2})",
            RsxVertexVectorOpcode.Dot3 => $"rsxSplat(dot(({s0}).xyz, ({s1}).xyz))",
            RsxVertexVectorOpcode.DotHomogeneous =>
                $"rsxSplat(dot(vec4(({s0}).xyz, 1.0), {s1}))",
            RsxVertexVectorOpcode.Dot4 => $"rsxSplat(dot({s0}, {s1}))",
            RsxVertexVectorOpcode.Distance =>
                $"vec4(1.0, ({s0}).y * ({s1}).y, ({s0}).z, ({s1}).w)",
            RsxVertexVectorOpcode.Minimum => $"min({s0}, {s1})",
            RsxVertexVectorOpcode.Maximum => $"max({s0}, {s1})",
            RsxVertexVectorOpcode.SetLessThan =>
                $"rsxBool4(lessThan({s0}, {s1}))",
            RsxVertexVectorOpcode.SetGreaterThanOrEqual =>
                $"rsxBool4(greaterThanEqual({s0}, {s1}))",
            RsxVertexVectorOpcode.Fraction => $"fract({s0})",
            RsxVertexVectorOpcode.Floor => $"floor({s0})",
            RsxVertexVectorOpcode.SetEqual =>
                $"rsxBool4(equal({s0}, {s1}))",
            RsxVertexVectorOpcode.SetFalse => "vec4(0.0)",
            RsxVertexVectorOpcode.SetGreaterThan =>
                $"rsxBool4(greaterThan({s0}, {s1}))",
            RsxVertexVectorOpcode.SetLessThanOrEqual =>
                $"rsxBool4(lessThanEqual({s0}, {s1}))",
            RsxVertexVectorOpcode.SetNotEqual =>
                $"rsxBool4(notEqual({s0}, {s1}))",
            RsxVertexVectorOpcode.SetTrue => "vec4(1.0)",
            RsxVertexVectorOpcode.SetSign => $"sign({s0})",
            _ => null
        };
    }

    private static string VertexSource(
        RsxVertexInstruction instruction,
        uint source,
        int sourceIndex)
    {
        string value = RsxVertexInstruction.SourceRegisterKind(source) switch
        {
            RsxVertexRegisterType.Temporary => $"R[{(source >> 2) & 0x3f}]",
            RsxVertexRegisterType.Input =>
                $"V[{(byte)instruction.InputAttribute}]",
            RsxVertexRegisterType.Constant =>
                $"rsxVertexConst[{instruction.ConstSource}]",
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
            span[0] = SwizzleChar(
                (RsxSwizzleComponent)((value >> 14) & 3));
            span[1] = SwizzleChar(
                (RsxSwizzleComponent)((value >> 12) & 3));
            span[2] = SwizzleChar(
                (RsxSwizzleComponent)((value >> 10) & 3));
            span[3] = SwizzleChar(
                (RsxSwizzleComponent)((value >> 8) & 3));
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

    private static string VertexWriteMask(RsxVertexWriteMask mask)
    {
        var value = new StringBuilder(4);
        if ((mask & RsxVertexWriteMask.X) != 0) value.Append('x');
        if ((mask & RsxVertexWriteMask.Y) != 0) value.Append('y');
        if ((mask & RsxVertexWriteMask.Z) != 0) value.Append('z');
        if ((mask & RsxVertexWriteMask.W) != 0) value.Append('w');
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
        builder.AppendLine(
            $"  gl_Position = O[{(byte)RsxVertexResult.Position}];");
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
        RsxVertexWriteMask Mask);
}

internal sealed record RsxVertexGlsl330LoweringResult(
    string? Glsl,
    bool TranslationReady,
    ImmutableArray<string> Blockers);
