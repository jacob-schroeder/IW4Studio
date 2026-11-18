namespace IW4.Render.Shaders;

public static partial class RsxCgSourceDecompiler
{
    private static void WriteVertex(
        byte[] program, int offset, CgParameter[] parameters, SourceWriter source)
    {
        IReadOnlyList<RsxVertexInstruction> instructions = RsxProgramDecoder.DecodeVertex(program, offset);
        if (instructions.Count == 0 || (instructions[^1].Word3 & 1) == 0)
        {
            source.Diagnostics.Add("The vertex instruction upload has no decoded END instruction.");
            return;
        }

        var uniformsByRegister = parameters
            .Where(parameter => parameter.Referenced && parameter.Variability == Uniform &&
                parameter.Resource == ConstantRegister && parameter.Type is < 0x419 or > 0x428)
            .ToLookup(parameter => parameter.Register);
        var matricesByRow = new Dictionary<int, (CgParameter Matrix, int Rows, int Row)>();
        foreach (CgParameter parameter in parameters)
        {
            // Cg places the matrix resource followed by its row parameters.
            if (!parameter.Referenced || parameter.Variability != Uniform ||
                parameter.Resource != ConstantRegister ||
                parameter.Type is not (0x41c or 0x420 or 0x424 or 0x428) ||
                parameter.Register >= 468)
                continue;

            int rowCount = (int)(parameter.Type - 0x41c) / 4 + 1;
            if (parameter.Ordinal + rowCount >= parameters.Length)
                continue;
            for (int row = 0; row < rowCount; row++)
            {
                CgParameter entry = parameters[parameter.Ordinal + 1 + row];
                if (entry.Name == $"{parameter.Name}[{row}]" &&
                    entry.Register == parameter.Register + row &&
                    entry.Resource == ConstantRegister)
                    matricesByRow.TryAdd((int)entry.Register, (parameter, rowCount, row));
            }
        }
        RsxVertexProgramEmbeddedConstantDecodeResult embedded =
            RsxVertexProgramEmbeddedConstantDecoder.Decode(program);
        source.Diagnostics.AddRange(embedded.Blockers);
        var embeddedByRegister = embedded.Constants.ToDictionary(constant => (int)constant.Destination);
        var constantNames = new Dictionary<int, string>();
        var narrowUniforms = new Dictionary<int, int>();
        var matrixNames = new Dictionary<int, string>();

        foreach (RsxVertexInstruction instruction in instructions)
        {
            if (instruction.HasControlFlow)
            {
                Unsupported($"Vertex control flow ({instruction.ScalarOpcode})");
                continue;
            }

            var vector = instruction.VectorOpcode;
            var scalar = instruction.ScalarOpcode;
            string vectorMask = Mask((int)instruction.VectorWriteMask, vertex: true);
            string scalarMask = Mask((int)instruction.ScalarWriteMask, vertex: true);
            if (vector == RsxVertexVectorOpcode.AddressLoad)
            {
                Unsupported("Vertex address register load");
                continue;
            }
            if (vector == RsxVertexVectorOpcode.Nop && scalar == RsxVertexScalarOpcode.Nop)
                continue;

            string? vectorExpression = vector == RsxVertexVectorOpcode.Nop || vectorMask.Length == 0
                ? null
                : VectorExpression();
            string? scalarExpression = scalar == RsxVertexScalarOpcode.Nop || scalarMask.Length == 0
                ? null
                : ScalarExpression();
            if (instruction.CondUpdateEnabled && vectorExpression is not null && scalarExpression is not null)
            {
                Unsupported("Vertex condition update from concurrent vector and scalar slots");
                continue;
            }

            if (instruction.Saturate)
            {
                if (vectorExpression is not null)
                    vectorExpression = $"saturate({vectorExpression})";
                if (scalarExpression is not null)
                    scalarExpression = $"saturate({scalarExpression})";
            }

            if (vectorExpression is not null && scalarExpression is not null)
            {
                // Both slots read the register state before either writes its result.
                string vectorValue = source.Internal($"v{instruction.Index}");
                string scalarValue = source.Internal($"s{instruction.Index}");
                source.Body.AppendLine($"    float4 {vectorValue} = {vectorExpression};");
                source.Body.AppendLine($"    float4 {scalarValue} = {scalarExpression};");
                vectorExpression = vectorValue;
                scalarExpression = scalarValue;
            }

            string? cc = instruction.CondUpdateEnabled ||
                (instruction.CondTestEnabled && instruction.ConditionTest != RsxConditionTest.True)
                ? source.Local($"cc{instruction.ConditionRegister}")
                : null;
            string? predicate = instruction.CondTestEnabled && cc is not null
                ? Predicate(source, instruction.Index, cc, instruction.ConditionTest, string.Create(
                    4, instruction, static (lanes, op) =>
                    {
                        for (int lane = 0; lane < lanes.Length; lane++)
                            lanes[lane] = "xyzw"[(int)op.ConditionSwizzle(lane)];
                    }))
                : null;
            if (vectorExpression is not null)
                WriteResult(vectorExpression, vectorMask, instruction.VecResult, instruction.VecDestTemp, "v");
            if (scalarExpression is not null)
                WriteResult(scalarExpression, scalarMask, instruction.ScaResult,
                    (int)((instruction.Word3 >> 7) & 0x3f), "s");

            void WriteResult(string expression, string mask, bool result, int temporary, string slot)
            {
                string? output = result && instruction.Result != RsxVertexResult.None
                    ? VertexOutput(instruction.Result, mask)
                    : null;
                temporary = slot == "s" && temporary != 0x3f ? instruction.ScaDestTemp : temporary;
                bool writesCondition = instruction.CondUpdateEnabled && cc is not null;
                if ((output is not null && temporary != 0x3f) || writesCondition || predicate is not null)
                {
                    // Snapshot the value before a write can affect another lane or destination.
                    string name = source.Internal($"value{instruction.Index}_{slot}");
                    source.Body.AppendLine($"    float4 {name} = {expression};");
                    expression = name;
                }
                if (output is not null)
                    WriteMask(source.Body, output, expression, mask, predicate);
                if (temporary != 0x3f)
                    WriteMask(source.Body, source.Local($"R{temporary}"), expression, mask, predicate);
                if (writesCondition && cc is not null)
                    WriteMask(source.Body, cc, expression, mask, predicate);
            }

            string? VertexOutput(RsxVertexResult result, string mask)
            {
                string? semantic = result switch
                {
                    RsxVertexResult.Position => "POSITION",
                    RsxVertexResult.FrontColor0 => "COLOR0",
                    RsxVertexResult.FrontColor1 => "COLOR1",
                    RsxVertexResult.BackColor0 => "BCOL0",
                    RsxVertexResult.BackColor1 => "BCOL1",
                    >= RsxVertexResult.TextureCoordinate0 and <= RsxVertexResult.TextureCoordinate8 =>
                        $"TEXCOORD{(int)result - (int)RsxVertexResult.TextureCoordinate0}",
                    _ => null
                };
                if (semantic is null)
                    return Unsupported($"Packed vertex output {result} ({mask})");
                return source.Output((int)result, semantic);
            }

            string? VectorExpression()
            {
                RsxSourceSlotMask sources = RsxVertexInstruction.VectorSourceMask(vector);
                string? a = (sources & RsxSourceSlotMask.Source0) != 0
                    ? Operand(instruction.Source0, instruction.Source0Abs)
                    : "";
                string? b = (sources & RsxSourceSlotMask.Source1) != 0
                    ? Operand(instruction.Source1, instruction.Source1Abs)
                    : "";
                string? c = (sources & RsxSourceSlotMask.Source2) != 0
                    ? Operand(instruction.Source2, instruction.Source2Abs)
                    : "";
                if (a is null || b is null || c is null)
                    return null;
                return vector switch
                {
                    RsxVertexVectorOpcode.Move => a,
                    RsxVertexVectorOpcode.Multiply => $"({a}) * ({b})",
                    RsxVertexVectorOpcode.Add => $"({a}) + ({c})",
                    RsxVertexVectorOpcode.MultiplyAdd => $"({a}) * ({b}) + ({c})",
                    RsxVertexVectorOpcode.Dot3 => $"(dot(({a}).xyz, ({b}).xyz)).xxxx",
                    RsxVertexVectorOpcode.DotHomogeneous => $"(dot(float4(({a}).xyz, 1.0), {b})).xxxx",
                    RsxVertexVectorOpcode.Dot4 => $"(dot({a}, {b})).xxxx",
                    RsxVertexVectorOpcode.Distance => $"float4(1.0, ({a}).y * ({b}).y, ({a}).z, ({b}).w)",
                    RsxVertexVectorOpcode.Minimum => $"min({a}, {b})",
                    RsxVertexVectorOpcode.Maximum => $"max({a}, {b})",
                    RsxVertexVectorOpcode.SetLessThan => Comparison(a, "<", b),
                    RsxVertexVectorOpcode.SetLessThanOrEqual => Comparison(a, "<=", b),
                    RsxVertexVectorOpcode.SetGreaterThan => Comparison(a, ">", b),
                    RsxVertexVectorOpcode.SetGreaterThanOrEqual => Comparison(a, ">=", b),
                    RsxVertexVectorOpcode.SetEqual => Comparison(a, "==", b),
                    RsxVertexVectorOpcode.SetNotEqual => Comparison(a, "!=", b),
                    RsxVertexVectorOpcode.SetFalse => "float4(0.0, 0.0, 0.0, 0.0)",
                    RsxVertexVectorOpcode.SetTrue => "float4(1.0, 1.0, 1.0, 1.0)",
                    RsxVertexVectorOpcode.Fraction => $"frac({a})",
                    RsxVertexVectorOpcode.Floor => $"floor({a})",
                    RsxVertexVectorOpcode.SetSign => $"sign({a})",
                    _ => Unsupported($"Vertex vector opcode {vector} (0x{instruction.VecOpcode:X2})")
                };
            }

            string? ScalarExpression()
            {
                if (!RsxVertexInstruction.ScalarReadsSource2(scalar))
                    return Unsupported($"Vertex scalar opcode {scalar} (0x{instruction.ScaOpcode:X2})");
                string? operand = Operand(instruction.Source2, instruction.Source2Abs);
                if (operand is null)
                    return null;
                return scalar switch
                {
                    RsxVertexScalarOpcode.Move => operand,
                    RsxVertexScalarOpcode.Reciprocal => $"1.0 / ({operand})",
                    RsxVertexScalarOpcode.ReciprocalSquareRoot =>
                        $"(rsqrt(abs({Scalar(operand)}))).xxxx",
                    RsxVertexScalarOpcode.LogarithmBase2 => $"log2({operand})",
                    RsxVertexScalarOpcode.ExponentBase2 => $"exp2({operand})",
                    RsxVertexScalarOpcode.Sine => $"sin({operand})",
                    RsxVertexScalarOpcode.Cosine => $"cos({operand})",
                    _ => Unsupported($"Vertex scalar opcode {scalar} (0x{instruction.ScaOpcode:X2})")
                };
            }

            string? Operand(uint bits, bool absolute)
            {
                string? name = RsxVertexInstruction.SourceRegisterKind(bits) switch
                {
                    RsxVertexRegisterType.Temporary => source.Local($"R{(bits >> 2) & 0x3f}"),
                    RsxVertexRegisterType.Input when (instruction.Word0 & (1u << 27)) != 0 =>
                        Unsupported("Dynamically indexed vertex input"),
                    RsxVertexRegisterType.Input =>
                        source.Input((int)instruction.InputAttribute, $"ATTR{(int)instruction.InputAttribute}"),
                    RsxVertexRegisterType.Constant when instruction.IndexConst =>
                        Unsupported("Dynamically indexed vertex constant"),
                    RsxVertexRegisterType.Constant => Constant(),
                    _ => Unsupported($"Vertex source register type {bits & 3}")
                };
                if (name is null)
                    return null;
                string swizzle = string.Create(4, bits, static (lanes, operand) =>
                {
                    for (int lane = 0; lane < lanes.Length; lane++)
                        lanes[lane] = "xyzw"[(int)((operand >> (14 - lane * 2)) & 3)];
                });
                if (RsxVertexInstruction.SourceRegisterKind(bits) == RsxVertexRegisterType.Constant
                    && narrowUniforms.TryGetValue(instruction.ConstSource, out int width)
                    && swizzle.Any(lane => "xyzw".IndexOf(lane) >= width))
                    return Unsupported($"Vertex uniform {name} read outside its reflected width");
                string value = name + SwizzleSuffix(swizzle);
                if (absolute)
                    value = $"abs({value})";
                return (bits & 0x10000) != 0 ? $"-({value})" : value;
            }

            string? Constant()
            {
                int register = instruction.ConstSource;
                if (constantNames.TryGetValue(register, out string? name))
                    return name;
                if (embeddedByRegister.TryGetValue(register, out EmbeddedVertexConstant? embeddedConstant))
                {
                    ShaderConstantValue v = embeddedConstant.Value;
                    return constantNames[register] = Float4(v.X, v.Y, v.Z, v.W);
                }

                CgParameter[] rows = uniformsByRegister[(uint)register].ToArray();
                if (rows.Length != 1)
                    return Unsupported($"Vertex c{register} has no unique named Cg register binding");
                if (matricesByRow.TryGetValue(register, out var matrix) &&
                    rows[0].Ordinal == matrix.Matrix.Ordinal + 1 + matrix.Row)
                {
                    if (!matrixNames.TryGetValue(matrix.Matrix.Ordinal, out string? matrixName))
                    {
                        matrixName = source.Uniform(matrix.Matrix, $"float{matrix.Rows}x4", $"C{matrix.Matrix.Register}");
                        matrixNames.Add(matrix.Matrix.Ordinal, matrixName);
                    }
                    return constantNames[register] = $"{matrixName}[{matrix.Row}]";
                }
                int width = NumericWidth(rows[0].Type);
                if (width is > 0 and < 4) narrowUniforms[register] = width;
                string type = width == 0 ? "float4" : CgTypeName(rows[0].Type)!;
                return constantNames[register] = source.Uniform(rows[0], type, $"C{register}");
            }

            string? Unsupported(string detail) =>
                source.Unsupported(instruction.Offset, detail, instruction.Word0, instruction.Word1,
                    instruction.Word2, instruction.Word3);
        }
    }
}
