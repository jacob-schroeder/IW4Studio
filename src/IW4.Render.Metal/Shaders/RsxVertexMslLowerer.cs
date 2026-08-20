using System.Collections.Immutable;
using System.Text;
using IW4.Render.Execution;
using IW4.Render.Geometry;
using IW4.Render.Shaders;

namespace IW4.Render.Metal.Shaders;

/// <summary>
/// Direct Metal lowering of immutable RSX vertex semantics.
/// </summary>
internal static class RsxVertexMslLowerer
{
    internal static RsxVertexMslLoweringResult Lower(
        RsxVertexProgramIr program)
        => LowerCore(program, staticComposition: null);

    internal static RsxVertexMslLoweringResult Lower(
        RsxVertexProgramIr program,
        IReadOnlyList<ShaderVertexInputBinding> vertexInputs,
        TranslatedProgramVertexConstantBindingPlan constantPlan,
        bool usesStaticModelInstancing)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(vertexInputs);
        ArgumentNullException.ThrowIfNull(constantPlan);

        if (!usesStaticModelInstancing)
            return LowerCore(program, staticComposition: null);

        var blockers = new SortedSet<string>(StringComparer.Ordinal);
        StaticCompositionPlan? composition = TryCreateStaticCompositionPlan(
            program.Instructions,
            vertexInputs,
            constantPlan,
            blockers);
        return LowerCore(program, composition, blockers);
    }

    private static RsxVertexMslLoweringResult LowerCore(
        RsxVertexProgramIr program,
        StaticCompositionPlan? staticComposition,
        SortedSet<string>? initialBlockers = null)
    {
        ArgumentNullException.ThrowIfNull(program);

        var blockers = initialBlockers ??
            new SortedSet<string>(StringComparer.Ordinal);
        if (!program.HasValidUpload)
            blockers.Add("vertexUploadHeader=invalid");
        if (program.Instructions.IsEmpty)
            blockers.Add("vertexInstructions=missing");
        if (program.Instructions.IsEmpty)
            return CreateResult(msl: null, blockers);

        string msl = BuildMsl(
            program.Instructions,
            blockers,
            staticComposition);
        return CreateResult(msl, blockers) with
        {
            UsesStaticModelInstancing = staticComposition is not null,
            StaticInstanceFloat4Stride =
                staticComposition?.InstanceFloat4Stride ?? 0,
            StaticPlacementFloat4Offset =
                staticComposition?.PlacementFloat4Offset ?? 0,
            StaticLightingPayload =
                staticComposition?.LightingPayloadIdentity,
            StaticCompositionIdentity =
                staticComposition?.Identity
        };
    }

    private static RsxVertexMslLoweringResult CreateResult(
        string? msl,
        SortedSet<string> blockers)
    {
        ImmutableArray<string> immutableBlockers = blockers.ToImmutableArray();
        return new RsxVertexMslLoweringResult(
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

    internal static string BuildMsl(
        IReadOnlyList<RsxVertexInstruction> instructions,
        ISet<string> blockers)
        => BuildMsl(instructions, blockers, staticComposition: null);

    private static string BuildMsl(
        IReadOnlyList<RsxVertexInstruction> instructions,
        ISet<string> blockers,
        StaticCompositionPlan? staticComposition)
    {
        ArgumentNullException.ThrowIfNull(instructions);
        ArgumentNullException.ThrowIfNull(blockers);

        VertexRegisterUsage registerUsage =
            ReadVertexRegisterUsage(instructions);
        if (staticComposition is not null &&
            !registerUsage.InputRegisters.Contains(0))
        {
            registerUsage = registerUsage with
            {
                InputRegisters = registerUsage.InputRegisters
                    .Append(0)
                    .Order()
                    .ToArray()
            };
        }
        var builder = new StringBuilder();
        MetalRsxShaderAbi.AppendPreamble(builder);
        MetalRsxShaderAbi.AppendVertexConstantLayout(builder);
        if (staticComposition is not null)
        {
            MetalRsxShaderAbi.AppendStaticModelLayouts(builder);
            AppendStaticCompositionHelpers(builder);
        }
        builder.AppendLine(
            "float4 rsxVertexSplat(float value) { return float4(value); }");
        builder.AppendLine(
            "float4 rsxVertexBool4(bool4 value) { return select(float4(0.0f), float4(1.0f), value); }");
        bool usesConditionCodes = instructions.Any(instruction =>
            instruction.CondTestEnabled ||
            instruction.CondUpdateEnabled);
        if (usesConditionCodes)
        {
            AppendConditionFunctions(builder);
        }
        AppendVertexFunctionSignature(builder, staticComposition);
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
        {
            builder.AppendLine(
                $"  V[{register}] = rsxVertexInputs[rsxVertexId * {MetalRsxShaderAbi.VertexInputFloat4Count}u + {register}u];");
        }
        if (staticComposition is { } composition)
        {
            AppendStaticCompositionLoad(builder, composition);
        }
        AppendRegisterBankInitialization(
            builder,
            "R",
            registerUsage.TempRegisters,
            "float4(0.0f)");
        AppendRegisterBankInitialization(
            builder,
            "O",
            registerUsage.OutputRegisters,
            "float4(0.0f, 0.0f, 0.0f, 1.0f)");
        if (usesConditionCodes)
        {
            // NV_vertex_program3 defines both CC registers as EQ on entry.
            builder.AppendLine("  float4 rsxCc[2];");
            builder.AppendLine("  rsxCc[0] = float4(0.0f);");
            builder.AppendLine("  rsxCc[1] = float4(0.0f);");
        }

        foreach (RsxVertexInstruction instruction in instructions)
        {
            if (instruction.HasControlFlow)
                blockers.Add("vertexBranchControlFlow=unlowered");
            if (instruction.IndexConst)
                blockers.Add("vertexIndexedConstants=unlowered");
            AddInvalidConstantBlocker(instruction, blockers);
            AppendVertexInstruction(
                builder,
                instruction,
                blockers,
                staticComposition);
        }

        builder.AppendLine("  RsxVertexStageOut rsxOut;");
        builder.AppendLine(
            $"  rsxOut.position = O[{(byte)RsxVertexResult.Position}];");
        builder.AppendLine(
            "  // RSX and Metal both use a top-left viewport and native [0,W] clip depth.");
        builder.AppendLine(
            $"  rsxOut.color0 = O[{(byte)RsxVertexResult.FrontColor0}];");
        builder.AppendLine(
            $"  rsxOut.color1 = O[{(byte)RsxVertexResult.FrontColor1}];");
        for (int i = 0; i < 8; i++)
        {
            builder.AppendLine(
                $"  rsxOut.texcoord{i} = O[{(byte)RsxVertexResult.TextureCoordinate0 + i}];");
        }
        builder.AppendLine("  return rsxOut;");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void AppendConditionFunctions(StringBuilder builder)
    {
        builder.AppendLine(
            "bool rsxVertexCcTestFL(float value) { return false; }");
        builder.AppendLine(
            "bool rsxVertexCcTestLT(float value) { return !isnan(value) && value < 0.0f; }");
        builder.AppendLine(
            "bool rsxVertexCcTestEQ(float value) { return !isnan(value) && value == 0.0f; }");
        builder.AppendLine(
            "bool rsxVertexCcTestLE(float value) { return !isnan(value) && value <= 0.0f; }");
        builder.AppendLine(
            "bool rsxVertexCcTestGT(float value) { return !isnan(value) && value > 0.0f; }");
        builder.AppendLine(
            "bool rsxVertexCcTestNE(float value) { return isnan(value) || value != 0.0f; }");
        builder.AppendLine(
            "bool rsxVertexCcTestGE(float value) { return !isnan(value) && value >= 0.0f; }");
        builder.AppendLine(
            "bool rsxVertexCcTestTR(float value) { return true; }");
    }

    private static void AppendVertexFunctionSignature(
        StringBuilder builder,
        StaticCompositionPlan? staticComposition)
    {
        builder.AppendLine("vertex RsxVertexStageOut rsxVertexMain(");
        builder.AppendLine("    uint rsxVertexId [[vertex_id]],");
        if (staticComposition is not null)
            builder.AppendLine("    uint rsxInstanceId [[instance_id]],");
        builder.AppendLine(
            $"    const device float4* rsxVertexInputs [[buffer({MetalRsxShaderAbi.VertexInputBufferIndex})]],");
        builder.AppendLine(
            $"    constant RsxVertexConstants& rsxConstants [[buffer({MetalRsxShaderAbi.VertexConstantBufferIndex})]]" +
            (staticComposition is null ? ")" : ","));
        if (staticComposition is null)
        {
            builder.AppendLine("{");
            return;
        }

        builder.AppendLine(
            $"    const device float4* rsxStaticInstances [[buffer({MetalRsxShaderAbi.StaticInstanceBufferIndex})]],");
        builder.AppendLine(
            $"    constant RsxMapFrameVertexConstants& rsxFrame [[buffer({MetalRsxShaderAbi.FrameVertexConstantBufferIndex})]],");
        builder.AppendLine(
            $"    constant RsxStaticCompositionConstants& rsxComposition [[buffer({MetalRsxShaderAbi.StaticCompositionBufferIndex})]])");
        builder.AppendLine("{");
    }

    private static void AppendStaticCompositionLoad(
        StringBuilder builder,
        StaticCompositionPlan composition)
    {
        builder.AppendLine(
            $"  uint rsxStaticBase = rsxInstanceId * {composition.InstanceFloat4Stride}u;");
        builder.AppendLine(
            $"  float4 rsxStaticLighting = {(composition.HasLightingPayload ? "rsxStaticInstances[rsxStaticBase]" : "float4(0.0f)")};");
        builder.AppendLine("  RsxStaticContext rsxStaticContext;");
        builder.AppendLine(
            "  rsxStaticContext.localPosition = float4(V[0].xyz, 1.0f);");
        builder.AppendLine(
            $"  rsxStaticContext.host0 = rsxStaticInstances[rsxStaticBase + {composition.PlacementFloat4Offset}u];");
        builder.AppendLine(
            $"  rsxStaticContext.host1 = rsxStaticInstances[rsxStaticBase + {composition.PlacementFloat4Offset + 1}u];");
        builder.AppendLine(
            $"  rsxStaticContext.host2 = rsxStaticInstances[rsxStaticBase + {composition.PlacementFloat4Offset + 2}u];");
        foreach ((int destination, StaticConstantReplacement replacement) in
                 composition.Replacements.OrderBy(pair => pair.Key))
        {
            if (replacement.IsLighting)
                continue;
            builder.AppendLine(
                $"  float4 rsxStaticConstC{destination} = rsxStaticDerivedConst({replacement.Semantic}, {replacement.Transform}, {replacement.Row}, rsxStaticContext, rsxFrame, rsxComposition);");
        }
    }

    private static void AppendStaticCompositionHelpers(StringBuilder builder)
    {
        builder.AppendLine("struct RsxStaticContext");
        builder.AppendLine("{");
        builder.AppendLine("  float4 localPosition;");
        builder.AppendLine("  float4 host0;");
        builder.AppendLine("  float4 host1;");
        builder.AppendLine("  float4 host2;");
        builder.AppendLine("};");
        builder.AppendLine("float4x4 rsxStaticInverse(float4x4 value)");
        builder.AppendLine("{");
        builder.AppendLine("  float m[16];");
        builder.AppendLine("  float result[16];");
        builder.AppendLine("  for (uint column = 0; column < 4u; column++)");
        builder.AppendLine("    for (uint row = 0; row < 4u; row++)");
        builder.AppendLine("      m[column * 4u + row] = value[column][row];");
        builder.AppendLine("  result[0] = m[5] * m[10] * m[15] - m[5] * m[11] * m[14] - m[9] * m[6] * m[15] + m[9] * m[7] * m[14] + m[13] * m[6] * m[11] - m[13] * m[7] * m[10];");
        builder.AppendLine("  result[4] = -m[4] * m[10] * m[15] + m[4] * m[11] * m[14] + m[8] * m[6] * m[15] - m[8] * m[7] * m[14] - m[12] * m[6] * m[11] + m[12] * m[7] * m[10];");
        builder.AppendLine("  result[8] = m[4] * m[9] * m[15] - m[4] * m[11] * m[13] - m[8] * m[5] * m[15] + m[8] * m[7] * m[13] + m[12] * m[5] * m[11] - m[12] * m[7] * m[9];");
        builder.AppendLine("  result[12] = -m[4] * m[9] * m[14] + m[4] * m[10] * m[13] + m[8] * m[5] * m[14] - m[8] * m[6] * m[13] - m[12] * m[5] * m[10] + m[12] * m[6] * m[9];");
        builder.AppendLine("  result[1] = -m[1] * m[10] * m[15] + m[1] * m[11] * m[14] + m[9] * m[2] * m[15] - m[9] * m[3] * m[14] - m[13] * m[2] * m[11] + m[13] * m[3] * m[10];");
        builder.AppendLine("  result[5] = m[0] * m[10] * m[15] - m[0] * m[11] * m[14] - m[8] * m[2] * m[15] + m[8] * m[3] * m[14] + m[12] * m[2] * m[11] - m[12] * m[3] * m[10];");
        builder.AppendLine("  result[9] = -m[0] * m[9] * m[15] + m[0] * m[11] * m[13] + m[8] * m[1] * m[15] - m[8] * m[3] * m[13] - m[12] * m[1] * m[11] + m[12] * m[3] * m[9];");
        builder.AppendLine("  result[13] = m[0] * m[9] * m[14] - m[0] * m[10] * m[13] - m[8] * m[1] * m[14] + m[8] * m[2] * m[13] + m[12] * m[1] * m[10] - m[12] * m[2] * m[9];");
        builder.AppendLine("  result[2] = m[1] * m[6] * m[15] - m[1] * m[7] * m[14] - m[5] * m[2] * m[15] + m[5] * m[3] * m[14] + m[13] * m[2] * m[7] - m[13] * m[3] * m[6];");
        builder.AppendLine("  result[6] = -m[0] * m[6] * m[15] + m[0] * m[7] * m[14] + m[4] * m[2] * m[15] - m[4] * m[3] * m[14] - m[12] * m[2] * m[7] + m[12] * m[3] * m[6];");
        builder.AppendLine("  result[10] = m[0] * m[5] * m[15] - m[0] * m[7] * m[13] - m[4] * m[1] * m[15] + m[4] * m[3] * m[13] + m[12] * m[1] * m[7] - m[12] * m[3] * m[5];");
        builder.AppendLine("  result[14] = -m[0] * m[5] * m[14] + m[0] * m[6] * m[13] + m[4] * m[1] * m[14] - m[4] * m[2] * m[13] - m[12] * m[1] * m[6] + m[12] * m[2] * m[5];");
        builder.AppendLine("  result[3] = -m[1] * m[6] * m[11] + m[1] * m[7] * m[10] + m[5] * m[2] * m[11] - m[5] * m[3] * m[10] - m[9] * m[2] * m[7] + m[9] * m[3] * m[6];");
        builder.AppendLine("  result[7] = m[0] * m[6] * m[11] - m[0] * m[7] * m[10] - m[4] * m[2] * m[11] + m[4] * m[3] * m[10] + m[8] * m[2] * m[7] - m[8] * m[3] * m[6];");
        builder.AppendLine("  result[11] = -m[0] * m[5] * m[11] + m[0] * m[7] * m[9] + m[4] * m[1] * m[11] - m[4] * m[3] * m[9] - m[8] * m[1] * m[7] + m[8] * m[3] * m[5];");
        builder.AppendLine("  result[15] = m[0] * m[5] * m[10] - m[0] * m[6] * m[9] - m[4] * m[1] * m[10] + m[4] * m[2] * m[9] + m[8] * m[1] * m[6] - m[8] * m[2] * m[5];");
        builder.AppendLine("  float reciprocalDeterminant = 1.0f / (m[0] * result[0] + m[1] * result[4] + m[2] * result[8] + m[3] * result[12]);");
        builder.AppendLine("  return float4x4(float4(result[0], result[1], result[2], result[3]) * reciprocalDeterminant, float4(result[4], result[5], result[6], result[7]) * reciprocalDeterminant, float4(result[8], result[9], result[10], result[11]) * reciprocalDeterminant, float4(result[12], result[13], result[14], result[15]) * reciprocalDeterminant);");
        builder.AppendLine("}");
        builder.AppendLine("float rsxStaticCompositionSway(");
        builder.AppendLine("    thread const RsxStaticContext& context,");
        builder.AppendLine("    constant RsxMapFrameVertexConstants& frame,");
        builder.AppendLine(
            "    constant RsxStaticCompositionConstants& composition)");
        builder.AppendLine("{");
        builder.AppendLine(
            "  if (composition.parameters.x == 0.0f || composition.bounds.y <= 0.0001f) return 0.0f;");
        builder.AppendLine(
            "  float heightWeight = clamp((context.localPosition.z - composition.bounds.x) / composition.bounds.y, 0.0f, 1.0f);");
        builder.AppendLine("  heightWeight *= heightWeight;");
        builder.AppendLine(
            "  float renderX = dot(context.host0, context.localPosition);");
        builder.AppendLine(
            "  float renderZ = dot(context.host2, context.localPosition);");
        builder.AppendLine(
            "  float phase = frame.vegetationTime.x * composition.parameters.z + renderX * composition.parameters.w + renderZ * composition.parameters.w * 1.37f;");
        builder.AppendLine(
            "  float wave = (sin(phase) + 0.35f * sin(phase * 0.61f + 1.7f)) / 1.35f;");
        builder.AppendLine(
            "  return composition.parameters.y * heightWeight * wave;");
        builder.AppendLine("}");
        builder.AppendLine("float4 rsxStaticWorldRow(");
        builder.AppendLine("    int row,");
        builder.AppendLine("    thread const RsxStaticContext& context,");
        builder.AppendLine("    constant RsxMapFrameVertexConstants& frame,");
        builder.AppendLine(
            "    constant RsxStaticCompositionConstants& composition)");
        builder.AppendLine("{");
        builder.AppendLine(
            "  if (row == 0) return float4(context.host0.x, -context.host2.x, context.host1.x, 0.0f);");
        builder.AppendLine(
            "  if (row == 1) return float4(context.host0.y, -context.host2.y, context.host1.y, 0.0f);");
        builder.AppendLine(
            "  if (row == 2) return float4(context.host0.z, -context.host2.z, context.host1.z, 0.0f);");
        builder.AppendLine(
            "  float vegetationSway = rsxStaticCompositionSway(context, frame, composition);");
        builder.AppendLine(
            "  return float4(context.host0.w - frame.eyeOffset.x + vegetationSway, -context.host2.w - frame.eyeOffset.y - vegetationSway * 0.35f, context.host1.w - frame.eyeOffset.z, 1.0f);");
        builder.AppendLine("}");
        builder.AppendLine("float4 rsxStaticMultiplyRow(");
        builder.AppendLine("    float4 lhs,");
        builder.AppendLine("    bool viewProjection,");
        builder.AppendLine(
            "    constant RsxMapFrameVertexConstants& frame)");
        builder.AppendLine("{");
        builder.AppendLine(
            "  uint first = viewProjection ? 32u : 0u;");
        builder.AppendLine(
            "  return lhs.x * frame.matrixRows[first] + lhs.y * frame.matrixRows[first + 1u] + lhs.z * frame.matrixRows[first + 2u] + lhs.w * frame.matrixRows[first + 3u];");
        builder.AppendLine("}");
        builder.AppendLine("float4 rsxStaticBaseRow(");
        builder.AppendLine("    int semantic,");
        builder.AppendLine("    int row,");
        builder.AppendLine("    thread const RsxStaticContext& context,");
        builder.AppendLine("    constant RsxMapFrameVertexConstants& frame,");
        builder.AppendLine(
            "    constant RsxStaticCompositionConstants& composition)");
        builder.AppendLine("{");
        builder.AppendLine(
            "  float4 world = rsxStaticWorldRow(row, context, frame, composition);");
        builder.AppendLine("  if (semantic == 5) return world;");
        builder.AppendLine(
            "  if (semantic == 6) return rsxStaticMultiplyRow(world, false, frame);");
        builder.AppendLine(
            "  return rsxStaticMultiplyRow(world, true, frame);");
        builder.AppendLine("}");
        builder.AppendLine("float4x4 rsxStaticBaseMatrix(");
        builder.AppendLine("    int semantic,");
        builder.AppendLine("    thread const RsxStaticContext& context,");
        builder.AppendLine("    constant RsxMapFrameVertexConstants& frame,");
        builder.AppendLine(
            "    constant RsxStaticCompositionConstants& composition)");
        builder.AppendLine("{");
        builder.AppendLine(
            "  return transpose(float4x4(rsxStaticBaseRow(semantic, 0, context, frame, composition), rsxStaticBaseRow(semantic, 1, context, frame, composition), rsxStaticBaseRow(semantic, 2, context, frame, composition), rsxStaticBaseRow(semantic, 3, context, frame, composition)));");
        builder.AppendLine("}");
        builder.AppendLine(
            "float4 rsxStaticMatrixRow(float4x4 value, int row) { return float4(value[0][row], value[1][row], value[2][row], value[3][row]); }");
        builder.AppendLine(
            "float4 rsxStaticMatrixColumn(float4x4 value, int column) { return value[column]; }");
        builder.AppendLine("float4 rsxStaticDerivedConst(");
        builder.AppendLine("    int semantic,");
        builder.AppendLine("    int transform,");
        builder.AppendLine("    int row,");
        builder.AppendLine("    thread const RsxStaticContext& context,");
        builder.AppendLine("    constant RsxMapFrameVertexConstants& frame,");
        builder.AppendLine(
            "    constant RsxStaticCompositionConstants& composition)");
        builder.AppendLine("{");
        builder.AppendLine(
            "  if (transform == 0) return rsxStaticBaseRow(semantic, row, context, frame, composition);");
        builder.AppendLine(
            "  float4x4 value = rsxStaticBaseMatrix(semantic, context, frame, composition);");
        builder.AppendLine(
            "  if (transform == 2) return rsxStaticMatrixColumn(value, row);");
        builder.AppendLine("  value = rsxStaticInverse(value);");
        builder.AppendLine(
            "  if (transform == 1) return rsxStaticMatrixRow(value, row);");
        builder.AppendLine(
            "  return rsxStaticMatrixColumn(value, row);");
        builder.AppendLine("}");
    }

    private static StaticCompositionPlan? TryCreateStaticCompositionPlan(
        IReadOnlyList<RsxVertexInstruction> instructions,
        IReadOnlyList<ShaderVertexInputBinding> vertexInputs,
        TranslatedProgramVertexConstantBindingPlan constantPlan,
        ISet<string> blockers)
    {
        MapRenderStaticInstanceLightingPayload lightingPayload =
            ResolveLightingPayload(constantPlan, blockers);
        if (blockers.Count != 0)
            return null;

        foreach (ShaderVertexInputBinding binding in vertexInputs)
        {
            byte destination = (byte)binding.Destination;
            if (destination == 12)
            {
                blockers.Add(
                    "staticInstanceAttributeDest12=unsupportedAuthoredLightingCollision");
            }
            if (destination is >= 13 and <= 15)
            {
                blockers.Add(
                    $"staticInstanceAttributeDest{destination}=unsupportedAuthoredPlacementCollision");
            }
        }
        if (blockers.Count != 0)
            return null;

        TranslatedProgramVertexConstantBinding[] matrixBindings =
            constantPlan.Bindings
                .Where(binding => IsPlacementDependent(
                    binding.CodeMatrixSemantic))
                .OrderBy(binding => binding.Destination)
                .ToArray();
        if (matrixBindings.Length == 0)
        {
            blockers.Add("staticInstanceWorldMatrixBinding=missing");
            return null;
        }

        TranslatedProgramVertexConstantBinding[] lightingBindings =
            constantPlan.Bindings
                .Where(binding => IsLightingPayloadBinding(binding.Kind))
                .OrderBy(binding => binding.Destination)
                .ToArray();
        var replacements = new Dictionary<int, StaticConstantReplacement>();
        foreach (TranslatedProgramVertexConstantBinding binding in
                 matrixBindings)
        {
            if (!ReadsConstant(instructions, binding.Destination))
            {
                blockers.Add(
                    $"staticInstanceVertexConstantC{binding.Destination}=missingMatrixRead");
                continue;
            }
            replacements.Add(
                binding.Destination,
                new StaticConstantReplacement(
                    IsLighting: false,
                    (int)binding.CodeMatrixSemantic!.Value,
                    (int)binding.CodeMatrixTransform,
                    binding.CodeMatrixRow));
        }
        foreach (TranslatedProgramVertexConstantBinding binding in
                 lightingBindings)
        {
            if (!ReadsConstant(instructions, binding.Destination))
            {
                blockers.Add(
                    $"staticInstanceVertexConstantC{binding.Destination}=missingLightingRead");
                continue;
            }
            replacements.Add(
                binding.Destination,
                new StaticConstantReplacement(
                    IsLighting: true,
                    Semantic: 0,
                    Transform: 0,
                    Row: 0));
        }
        if (blockers.Count != 0)
            return null;

        bool hasLighting =
            lightingPayload != MapRenderStaticInstanceLightingPayload.None;
        string identity = string.Join(
            ',',
            replacements
                .OrderBy(pair => pair.Key)
                .Select(pair => pair.Value.IsLighting
                    ? $"c{pair.Key}={lightingPayload}"
                    : $"c{pair.Key}={pair.Value.Semantic}:" +
                      $"{pair.Value.Transform}:r{pair.Value.Row}"));
        return new StaticCompositionPlan(
            replacements,
            InstanceFloat4Stride: hasLighting
                ? MetalRsxShaderAbi.StaticLightingPlacementFloat4Stride
                : MetalRsxShaderAbi.StaticPlacementFloat4Stride,
            PlacementFloat4Offset: hasLighting ? 1 : 0,
            HasLightingPayload: hasLighting,
            LightingPayloadIdentity: lightingPayload.ToString(),
            Identity: identity);
    }

    private static MapRenderStaticInstanceLightingPayload ResolveLightingPayload(
        TranslatedProgramVertexConstantBindingPlan plan,
        ISet<string> blockers)
    {
        bool usesBaseLightingCoords = plan.Bindings.Any(binding =>
            binding.Kind ==
            TranslatedProgramVertexConstantBindingKind
                .PerInstanceStaticModelBaseLightingCoords);
        bool usesLightProbeAmbient = plan.Bindings.Any(binding =>
            binding.Kind ==
            TranslatedProgramVertexConstantBindingKind
                .PerInstanceStaticModelLightProbeAmbient);
        if (usesBaseLightingCoords && usesLightProbeAmbient)
        {
            blockers.Add("staticInstanceLightingPayload=unsupportedConflict");
            return MapRenderStaticInstanceLightingPayload.None;
        }

        return usesBaseLightingCoords
            ? MapRenderStaticInstanceLightingPayload.BaseLightingCoords
            : usesLightProbeAmbient
                ? MapRenderStaticInstanceLightingPayload.LightProbeAmbient
                : MapRenderStaticInstanceLightingPayload.None;
    }

    private static bool IsLightingPayloadBinding(
        TranslatedProgramVertexConstantBindingKind kind) => kind is
        TranslatedProgramVertexConstantBindingKind
            .PerInstanceStaticModelBaseLightingCoords or
        TranslatedProgramVertexConstantBindingKind
            .PerInstanceStaticModelLightProbeAmbient;

    private static bool IsPlacementDependent(
        CodeMatrixSemantic? semantic) => semantic is
        CodeMatrixSemantic.World0 or
        CodeMatrixSemantic.WorldView0 or
        CodeMatrixSemantic.WorldViewProjection0;

    private static bool ReadsConstant(
        IReadOnlyList<RsxVertexInstruction> instructions,
        int destination)
    {
        foreach (RsxVertexInstruction instruction in instructions)
        {
            if (instruction.ConstSource != destination)
                continue;
            if (instruction.VectorOpcode != RsxVertexVectorOpcode.Nop &&
                instruction.VectorWriteMask != RsxVertexWriteMask.None)
            {
                RsxSourceSlotMask mask =
                    RsxVertexInstruction.VectorSourceMask(
                        instruction.VectorOpcode);
                if (((mask & RsxSourceSlotMask.Source0) != 0 &&
                     RsxVertexInstruction.SourceRegisterKind(
                         instruction.Source0) ==
                     RsxVertexRegisterType.Constant) ||
                    ((mask & RsxSourceSlotMask.Source1) != 0 &&
                     RsxVertexInstruction.SourceRegisterKind(
                         instruction.Source1) ==
                     RsxVertexRegisterType.Constant) ||
                    ((mask & RsxSourceSlotMask.Source2) != 0 &&
                     RsxVertexInstruction.SourceRegisterKind(
                         instruction.Source2) ==
                     RsxVertexRegisterType.Constant))
                {
                    return true;
                }
            }
            if (instruction.ScalarOpcode != RsxVertexScalarOpcode.Nop &&
                instruction.ScalarWriteMask != RsxVertexWriteMask.None &&
                RsxVertexInstruction.ScalarReadsSource2(
                    instruction.ScalarOpcode) &&
                RsxVertexInstruction.SourceRegisterKind(
                    instruction.Source2) == RsxVertexRegisterType.Constant)
            {
                return true;
            }
        }
        return false;
    }

    private static VertexRegisterUsage ReadVertexRegisterUsage(
        IReadOnlyList<RsxVertexInstruction> instructions)
    {
        var inputRegisters = new SortedSet<int>();
        var tempRegisters = new SortedSet<int>();
        var outputRegisters = new SortedSet<int>
        {
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
                VertexExpression(
                    instruction,
                    scalar: false,
                    staticComposition: null) is not null)
            {
                RsxSourceSlotMask sourceMask =
                    RsxVertexInstruction.VectorSourceMask(
                        instruction.VectorOpcode);
                if ((sourceMask & RsxSourceSlotMask.Source0) != 0)
                {
                    AddVertexSourceRegister(
                        instruction,
                        instruction.Source0,
                        inputRegisters,
                        tempRegisters);
                }
                if ((sourceMask & RsxSourceSlotMask.Source1) != 0)
                {
                    AddVertexSourceRegister(
                        instruction,
                        instruction.Source1,
                        inputRegisters,
                        tempRegisters);
                }
                if ((sourceMask & RsxSourceSlotMask.Source2) != 0)
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
                VertexExpression(
                    instruction,
                    scalar: true,
                    staticComposition: null) is not null)
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

    private static void AddInvalidConstantBlocker(
        RsxVertexInstruction instruction,
        ISet<string> blockers)
    {
        bool readsConstant = false;
        if (instruction.VectorOpcode != RsxVertexVectorOpcode.Nop &&
            instruction.VectorWriteMask != RsxVertexWriteMask.None)
        {
            RsxSourceSlotMask sourceMask =
                RsxVertexInstruction.VectorSourceMask(
                    instruction.VectorOpcode);
            readsConstant =
                ((sourceMask & RsxSourceSlotMask.Source0) != 0 &&
                 RsxVertexInstruction.SourceRegisterKind(
                     instruction.Source0) == RsxVertexRegisterType.Constant) ||
                ((sourceMask & RsxSourceSlotMask.Source1) != 0 &&
                 RsxVertexInstruction.SourceRegisterKind(
                     instruction.Source1) == RsxVertexRegisterType.Constant) ||
                ((sourceMask & RsxSourceSlotMask.Source2) != 0 &&
                 RsxVertexInstruction.SourceRegisterKind(
                     instruction.Source2) == RsxVertexRegisterType.Constant);
        }
        if (!readsConstant &&
            instruction.ScalarOpcode != RsxVertexScalarOpcode.Nop &&
            instruction.ScalarWriteMask != RsxVertexWriteMask.None &&
            RsxVertexInstruction.ScalarReadsSource2(
                instruction.ScalarOpcode))
        {
            readsConstant = RsxVertexInstruction.SourceRegisterKind(
                instruction.Source2) == RsxVertexRegisterType.Constant;
        }

        if (readsConstant &&
            instruction.ConstSource >= RsxVertexConstantLayout.Count)
        {
            blockers.Add(
                $"vertexConstant{instruction.ConstSource}=unmapped");
        }
    }

    private static void AppendVertexInstruction(
        StringBuilder builder,
        RsxVertexInstruction instruction,
        ISet<string> blockers,
        StaticCompositionPlan? staticComposition)
    {
        string? predicate = AppendVertexConditionPredicate(
            builder,
            instruction);
        VertexSlotValue? vectorValue = AppendVertexSlotValue(
            builder,
            instruction,
            scalar: false,
            blockers,
            staticComposition);
        VertexSlotValue? scalarValue = AppendVertexSlotValue(
            builder,
            instruction,
            scalar: true,
            blockers,
            staticComposition);

        // Both slot expressions are evaluated before either destination is
        // updated, matching the simultaneous NV40 dual-slot source reads.
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

        if (vectorValue is not null && scalarValue is not null)
        {
            blockers.Add("vertexDualSlotConditionUpdate=unlowered");
            return;
        }

        VertexSlotValue? conditionValue = vectorValue ?? scalarValue;
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
        ISet<string> blockers,
        StaticCompositionPlan? staticComposition)
    {
        byte opcode = scalar
            ? instruction.ScaOpcode
            : instruction.VecOpcode;
        RsxVertexWriteMask mask = scalar
            ? instruction.ScalarWriteMask
            : instruction.VectorWriteMask;
        if (opcode == 0 || mask == RsxVertexWriteMask.None)
            return null;
        string? expression = VertexExpression(
            instruction,
            scalar,
            staticComposition);
        if (expression is null)
        {
            blockers.Add(
                $"vertex{(scalar ? "Scalar" : "Vector")}Opcode0x{opcode:X2}=unmapped");
            builder.AppendLine(
                $"  // Unmapped RSX vertex opcode 0x{opcode:X2}; no value invented.");
            return null;
        }
        if (instruction.Saturate)
        {
            expression =
                $"clamp({expression}, float4(0.0f), float4(1.0f))";
        }
        string valueName =
            $"rsxValue{instruction.Index}{(scalar ? 'S' : 'V')}";
        builder.AppendLine($"  float4 {valueName} = {expression};");
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
        string test = VertexConditionTestName(instruction.ConditionTest);
        builder.Append($"  bool4 {predicateName} = bool4(");
        for (int component = 0; component < 4; component++)
        {
            if (component != 0)
                builder.Append(", ");
            char conditionComponent = SwizzleChar(
                instruction.ConditionSwizzle(component));
            builder.Append(
                $"rsxVertexCcTest{test}(rsxCc[{instruction.ConditionRegister}].{conditionComponent})");
        }
        builder.AppendLine(");");
        return predicateName;
    }

    private static string VertexConditionTestName(
        RsxConditionTest condition) => condition switch
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
            if ((mask & maskBit) == 0)
                continue;
            char destinationComponent = SwizzleChar(
                (RsxSwizzleComponent)component);
            builder.AppendLine(
                $"  if ({predicate}.{destinationComponent}) {destination}.{destinationComponent} = {value}.{destinationComponent};");
        }
    }

    private static string? VertexExpression(
        RsxVertexInstruction instruction,
        bool scalar,
        StaticCompositionPlan? staticComposition)
    {
        if (scalar)
        {
            string source = VertexSource(
                instruction,
                instruction.Source2,
                2,
                staticComposition);
            string scalarSource = $"({source}).x";
            return instruction.ScalarOpcode switch
            {
                RsxVertexScalarOpcode.Move => source,
                RsxVertexScalarOpcode.Reciprocal =>
                    $"(1.0f / {source})",
                RsxVertexScalarOpcode.ReciprocalClamped =>
                    $"clamp(1.0f / {source}, float4(5.42101e-20f), float4(1.884467e19f))",
                RsxVertexScalarOpcode.ReciprocalSquareRoot =>
                    $"rsxVertexSplat(1.0f / sqrt(max({scalarSource}, 0.0000000001f)))",
                RsxVertexScalarOpcode.LogarithmBase2 =>
                    $"log2(max({source}, float4(0.0000000001f)))",
                RsxVertexScalarOpcode.ExponentBase2 => $"exp2({source})",
                RsxVertexScalarOpcode.Sine => $"sin({source})",
                RsxVertexScalarOpcode.Cosine => $"cos({source})",
                _ => null
            };
        }

        string s0 = VertexSource(
            instruction,
            instruction.Source0,
            0,
            staticComposition);
        string s1 = VertexSource(
            instruction,
            instruction.Source1,
            1,
            staticComposition);
        string s2 = VertexSource(
            instruction,
            instruction.Source2,
            2,
            staticComposition);
        return instruction.VectorOpcode switch
        {
            RsxVertexVectorOpcode.Move => s0,
            RsxVertexVectorOpcode.Multiply => $"({s0} * {s1})",
            // NV40 VP ADD consumes source slots 0 and 2.
            RsxVertexVectorOpcode.Add => $"({s0} + {s2})",
            RsxVertexVectorOpcode.MultiplyAdd =>
                $"({s0} * {s1} + {s2})",
            RsxVertexVectorOpcode.Dot3 =>
                $"rsxVertexSplat(dot(({s0}).xyz, ({s1}).xyz))",
            RsxVertexVectorOpcode.DotHomogeneous =>
                $"rsxVertexSplat(dot(float4(({s0}).xyz, 1.0f), {s1}))",
            RsxVertexVectorOpcode.Dot4 =>
                $"rsxVertexSplat(dot({s0}, {s1}))",
            RsxVertexVectorOpcode.Distance =>
                $"float4(1.0f, ({s0}).y * ({s1}).y, ({s0}).z, ({s1}).w)",
            RsxVertexVectorOpcode.Minimum => $"min({s0}, {s1})",
            RsxVertexVectorOpcode.Maximum => $"max({s0}, {s1})",
            RsxVertexVectorOpcode.SetLessThan =>
                $"rsxVertexBool4({s0} < {s1})",
            RsxVertexVectorOpcode.SetGreaterThanOrEqual =>
                $"rsxVertexBool4({s0} >= {s1})",
            RsxVertexVectorOpcode.Fraction => $"fract({s0})",
            RsxVertexVectorOpcode.Floor => $"floor({s0})",
            RsxVertexVectorOpcode.SetEqual =>
                $"rsxVertexBool4({s0} == {s1})",
            RsxVertexVectorOpcode.SetFalse => "float4(0.0f)",
            RsxVertexVectorOpcode.SetGreaterThan =>
                $"rsxVertexBool4({s0} > {s1})",
            RsxVertexVectorOpcode.SetLessThanOrEqual =>
                $"rsxVertexBool4({s0} <= {s1})",
            RsxVertexVectorOpcode.SetNotEqual =>
                $"rsxVertexBool4({s0} != {s1})",
            RsxVertexVectorOpcode.SetTrue => "float4(1.0f)",
            RsxVertexVectorOpcode.SetSign => $"sign({s0})",
            _ => null
        };
    }

    private static string VertexSource(
        RsxVertexInstruction instruction,
        uint source,
        int sourceIndex,
        StaticCompositionPlan? staticComposition)
    {
        string value = RsxVertexInstruction.SourceRegisterKind(source) switch
        {
            RsxVertexRegisterType.Temporary =>
                $"R[{(source >> 2) & 0x3f}]",
            RsxVertexRegisterType.Input =>
                $"V[{(byte)instruction.InputAttribute}]",
            RsxVertexRegisterType.Constant =>
                StaticConstantSource(
                    instruction.ConstSource,
                    staticComposition),
            _ => "float4(0.0f)"
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

    private static string StaticConstantSource(
        int destination,
        StaticCompositionPlan? staticComposition)
    {
        if (staticComposition is null ||
            !staticComposition.Replacements.TryGetValue(
                destination,
                out StaticConstantReplacement replacement))
        {
            return $"rsxConstants.values[{destination}]";
        }
        if (replacement.IsLighting)
            return "rsxStaticLighting";
        return $"rsxStaticConstC{destination}";
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

    private readonly record struct VertexRegisterUsage(
        int[] InputRegisters,
        int[] TempRegisters,
        int[] OutputRegisters);

    private readonly record struct VertexSlotValue(
        string Name,
        RsxVertexWriteMask Mask);

    private sealed record StaticCompositionPlan(
        IReadOnlyDictionary<int, StaticConstantReplacement> Replacements,
        int InstanceFloat4Stride,
        int PlacementFloat4Offset,
        bool HasLightingPayload,
        string LightingPayloadIdentity,
        string Identity);

    private readonly record struct StaticConstantReplacement(
        bool IsLighting,
        int Semantic,
        int Transform,
        int Row);
}

internal sealed record RsxVertexMslLoweringResult(
    string? Msl,
    bool IsReady,
    ImmutableArray<string> Blockers)
{
    internal bool UsesStaticModelInstancing { get; init; }

    internal int StaticInstanceFloat4Stride { get; init; }

    internal int StaticPlacementFloat4Offset { get; init; }

    internal string? StaticLightingPayload { get; init; }

    internal string? StaticCompositionIdentity { get; init; }
}
