namespace IW4.Render.Shaders;

public static partial class RsxCgSourceDecompiler
{
    public const string ReciprocalWNote = "This shader reads WPOS.w for RSX perspective correction.";

    private static void WriteFragment(
        byte[] program, int offset, RuntimeInfo layout, CgParameter[] parameters, SourceWriter source)
    {
        int end = checked(offset + (int)layout.UploadSize);
        RsxFragmentInstruction[] instructions = RsxProgramDecoder.DecodeFragment(program, offset)
            .TakeWhile(instruction => instruction.Offset + instruction.ByteCount <= end)
            .ToArray();
        if (instructions.Length == 0 || !instructions[^1].End)
        {
            source.Diagnostics.Add("The fragment instruction upload has no decoded END instruction.");
            return;
        }

        RsxFragmentProgramControl control = RsxShaderTranslator.ReadFragmentProgramControl(program);
        if (!control.IsValid || control.DepthExportRaw > 1 || control.ExportPrecisionRaw > 1)
        {
            source.Diagnostics.Add("Invalid or unsupported fragment export descriptor.");
            return;
        }
        uint texCoordControl = ReadWord(program, (int)control.DescriptorOffset + 0x0c);
        uint enabledTexCoords = texCoordControl >> 16;
        uint texCoords2D = texCoordControl & 0xffff;
        bool readsReciprocalW = false;

        var owners = new Dictionary<int, CgParameter>();
        var ambiguousOwners = new HashSet<int>();
        var inlineOffsets = instructions.Where(instruction => instruction.InlineConstant is not null)
            .Select(instruction => instruction.Offset + 16 - offset).ToHashSet();
        foreach (CgParameter parameter in parameters)
        {
            if (parameter.PatchOffset == 0)
                continue;
            if (!RsxShaderTranslator.TryReadPatchEntry(program, layout, (ushort)parameter.Ordinal, out PatchEntry patch))
            {
                source.Diagnostics.Add($"Invalid patch list for Cg parameter {parameter.Ordinal}.");
                continue;
            }

            foreach (ushort site in patch.PatchOffsets)
            {
                if (!inlineOffsets.Contains(site))
                {
                    source.Diagnostics.Add($"Cg parameter {parameter.Ordinal} patches absent inline constant 0x{site:X}.");
                    continue;
                }
                if (!owners.TryAdd(site, parameter) && owners[site] != parameter)
                    ambiguousOwners.Add(site);
            }
        }

        var samplers = parameters.Where(parameter => parameter.Referenced && parameter.Variability == Uniform &&
                parameter.Resource >= TextureUnitBase && parameter.Resource < TextureUnitBase + 16)
            .ToLookup(parameter => (int)(parameter.Resource - TextureUnitBase));
        var parameterNames = new Dictionary<int, string>();
        var values = new FragmentValues(source);
        var conditionFlags = new Dictionary<(bool Bank, char Lane), string>();
        foreach (RsxFragmentInstruction instruction in instructions)
        {
            if (instruction.OpcodeType == RsxFragmentOpcode.Nop)
                continue;
            if (instruction.IsControlFlow)
            {
                Unsupported($"Fragment control flow ({instruction.OpcodeType})");
                continue;
            }
            if (instruction.OpcodeType == RsxFragmentOpcode.Kill)
            {
                if (instruction.ConditionTest == RsxConditionTest.True)
                    source.Body.AppendLine("    discard;");
                else if (instruction.ConditionTest != RsxConditionTest.False)
                {
                    string tests = string.Join(" || ", Enumerable.Range(0, 4).Select(ReadCondition).Distinct());
                    source.Body.AppendLine($"    if ({tests}) discard;");
                }
                continue;
            }

            string mask = Mask((int)instruction.WriteMask, vertex: false);
            if (mask.Length == 0)
            {
                Unsupported($"Fragment opcode {instruction.OpcodeType} with an empty write mask");
                continue;
            }

            bool unconditional = instruction.ConditionTest == RsxConditionTest.True;
            bool namedResult = unconditional && !instruction.NoDest && !instruction.CondWriteEnabled;
            int dotWidth = instruction.OpcodeType switch
            {
                RsxFragmentOpcode.Dot2 => 2, RsxFragmentOpcode.Dot3 => 3, RsxFragmentOpcode.Dot4 => 4, _ => 0
            };
            string? comparison = instruction.OpcodeType switch
            {
                RsxFragmentOpcode.SetLessThan => "<", RsxFragmentOpcode.SetLessThanOrEqual => "<=",
                RsxFragmentOpcode.SetGreaterThan => ">", RsxFragmentOpcode.SetGreaterThanOrEqual => ">=",
                RsxFragmentOpcode.SetEqual => "==", RsxFragmentOpcode.SetNotEqual => "!=", _ => null
            };
            bool scalarCondition = comparison is not null && unconditional && instruction.NoDest &&
                instruction.CondWriteEnabled && mask.Length == 1 && instruction.Scale == RsxFragmentResultScale.None;
            bool componentwise = namedResult && instruction.OpcodeType is RsxFragmentOpcode.Move or
                RsxFragmentOpcode.Add or RsxFragmentOpcode.Multiply or RsxFragmentOpcode.MultiplyAdd or
                RsxFragmentOpcode.Minimum or RsxFragmentOpcode.Maximum;
            bool scalarDot = namedResult && dotWidth != 0 && mask.Length == 1;
            string stem = instruction.OpcodeType switch
            {
                RsxFragmentOpcode.Add => "sum",
                RsxFragmentOpcode.Multiply => "product",
                RsxFragmentOpcode.MultiplyAdd => "scaledSum",
                RsxFragmentOpcode.Minimum => "minimumValue",
                RsxFragmentOpcode.Maximum => "maximumValue",
                _ when dotWidth != 0 => "dotProduct",
                _ => "value"
            };
            string? expression = Expression();
            if (expression is null)
                continue;
            if (scalarCondition)
            {
                string flag = source.ReadableName(comparison == ">=" ? "meetsThreshold" : "comparison");
                string cc = source.Local(instruction.ConditionWriteRegister1 ? "cc1" : "cc0");
                source.Body.AppendLine($"    bool {flag} = {expression};");
                source.Body.AppendLine($"    {cc}.{mask} = float({flag});");
                conditionFlags[(instruction.ConditionWriteRegister1, mask[0])] = flag;
                continue;
            }
            expression = ApplyModifiers(expression);
            if (expression is null)
                continue;

            string conditionLanes = string.Create(4, instruction, static (lanes, op) =>
            {
                for (int lane = 0; lane < lanes.Length; lane++)
                    lanes[lane] = "xyzw"[(int)op.ConditionSwizzle(lane)];
            });
            char testedLane = conditionLanes["xyzw".IndexOf(mask[0])];
            bool scalarPredicate = !unconditional && mask.All(lane => conditionLanes["xyzw".IndexOf(lane)] == testedLane);
            string? predicate = null;
            if (scalarPredicate)
            {
                if (conditionFlags.TryGetValue((instruction.ConditionReadRegister1, testedLane), out string? flag)
                    && instruction.ConditionTest is RsxConditionTest.NotEqual or RsxConditionTest.Equal)
                    predicate = instruction.ConditionTest == RsxConditionTest.NotEqual ? flag : "!" + flag;
                else
                {
                    predicate = source.ReadableName("writeEnabled");
                    string condition = ReadCondition("xyzw".IndexOf(mask[0]));
                    source.Body.AppendLine($"    bool {predicate} = {condition};");
                }
            }
            else if (!unconditional)
            {
                predicate = Predicate(source, instruction.Index, source.Local(instruction.ConditionReadRegister1 ? "cc1" : "cc0", zeroInitialize: true),
                    instruction.ConditionTest, conditionLanes);
            }
            bool writesCondition = instruction.CondWriteEnabled;
            if (predicate is not null || writesCondition)
            {
                string value = source.Internal($"value{instruction.Index}");
                source.Body.AppendLine($"    float4 {value} = {expression};");
                expression = value;
            }
            string? destination = null;
            if (!instruction.NoDest)
            {
                values.BeforeWrite(instruction.DestFp16, instruction.DestRegister, mask, !unconditional);
                if (namedResult)
                {
                    string rhs = mask == "xyzw" || scalarDot || componentwise ? expression : $"({expression}).{mask}";
                    values.Define(instruction.DestFp16, instruction.DestRegister, mask, source.ReadableName(stem), rhs);
                }
                else
                {
                    destination = values.Register(instruction.DestFp16, instruction.DestRegister);
                    WriteDestination(destination, expression);
                }
            }
            if (writesCondition)
            {
                string cc = source.Local(instruction.ConditionWriteRegister1 ? "cc1" : "cc0");
                WriteDestination(cc, destination ?? expression);
                foreach (char lane in mask) conditionFlags.Remove((instruction.ConditionWriteRegister1, lane));
            }

            void WriteDestination(string target, string value)
            {
                if (scalarPredicate)
                {
                    string suffix = SwizzleSuffix(mask);
                    source.Body.AppendLine($"    if ({predicate}) {target}{suffix} = ({value}){suffix};");
                }
                else
                    WriteMask(source.Body, target, value, mask, predicate);
            }

            string ReadCondition(int component)
            {
                string cc = source.Local(instruction.ConditionReadRegister1 ? "cc1" : "cc0", zeroInitialize: true);
                char lane = "xyzw"[(int)instruction.ConditionSwizzle(component)];
                return Condition($"{cc}.{lane}", instruction.ConditionTest);
            }

            string? Expression()
            {
                if (instruction.IsTexture) return Texture();
                int count = instruction.OperandCount;
                string lanes = scalarCondition || componentwise ? mask : dotWidth == 0 ? "xyzw" : "xyzw"[..dotWidth];
                string? a = count > 0 ? Operand(instruction.Source0Operand, lanes) : "";
                string? b = count > 1 ? Operand(instruction.Source1Operand, lanes) : "";
                string? c = count > 2 ? Operand(instruction.Source2Operand, lanes) : "";
                if (a is null || b is null || c is null)
                    return null;
                if (comparison is not null)
                    return scalarCondition ? $"{a} {comparison} {b}" : Comparison(a, comparison, b);
                if (dotWidth != 0)
                {
                    string dot = $"dot({a}, {b})";
                    return scalarDot ? dot : $"({dot}).xxxx";
                }

                return instruction.OpcodeType switch
                {
                    RsxFragmentOpcode.Move => a,
                    RsxFragmentOpcode.Add => $"{a} + {b}",
                    RsxFragmentOpcode.Multiply => $"{a} * {b}",
                    RsxFragmentOpcode.MultiplyAdd => $"{a} * {b} + {c}",
                    RsxFragmentOpcode.Minimum => $"min({a}, {b})",
                    RsxFragmentOpcode.Maximum => $"max({a}, {b})",
                    RsxFragmentOpcode.SetFalse => "float4(0.0, 0.0, 0.0, 0.0)",
                    RsxFragmentOpcode.SetTrue => "float4(1.0, 1.0, 1.0, 1.0)",
                    RsxFragmentOpcode.Fraction => $"frac({a})",
                    RsxFragmentOpcode.Floor => $"floor({a})",
                    RsxFragmentOpcode.DerivativeX => $"ddx({a})",
                    RsxFragmentOpcode.DerivativeY => $"ddy({a})",
                    RsxFragmentOpcode.ExponentBase2 => $"(exp2({Scalar(a)})).xxxx",
                    RsxFragmentOpcode.LogarithmBase2 => $"(log2({Scalar(a)})).xxxx",
                    RsxFragmentOpcode.Sine => $"(sin({Scalar(a)})).xxxx",
                    RsxFragmentOpcode.Cosine => $"(cos({Scalar(a)})).xxxx",
                    RsxFragmentOpcode.Reciprocal => $"(1.0 / {Scalar(a)}).xxxx",
                    RsxFragmentOpcode.ReciprocalSquareRoot => $"(rsqrt(abs({Scalar(a)}))).xxxx",
                    RsxFragmentOpcode.Normalize => Normalize(a),
                    RsxFragmentOpcode.Divide => $"({a}) / {Scalar(b)}",
                    RsxFragmentOpcode.DivideBySquareRoot => DivideBySquareRoot(a, b),
                    _ => Unsupported($"Fragment opcode {instruction.OpcodeType} (0x{instruction.Opcode:X2})")
                };
            }

            string Normalize(string value)
            {
                string vector = source.Internal($"normal{instruction.Index}");
                source.Body.AppendLine($"    float3 {vector} = ({value}).xyz;");
                source.Body.AppendLine($"    {vector} = length({vector}) > 0.0 ? normalize({vector}) : {vector};");
                return $"{vector}.xyzz";
            }

            string DivideBySquareRoot(string numerator, string denominator)
            {
                string a = source.Internal($"numerator{instruction.Index}");
                string divisor = source.Internal($"divisor{instruction.Index}");
                source.Body.AppendLine($"    float4 {a} = {numerator};");
                source.Body.AppendLine($"    float {divisor} = sqrt(abs({Scalar(denominator)}));");
                // DIVSQ preserves a zero numerator even with a zero divisor.
                IEnumerable<string> lanes = "xyzw".Select(lane =>
                    $"abs({a}.{lane}) > 0.0 ? {a}.{lane} / {divisor} : {a}.{lane}");
                return $"float4({string.Join(", ", lanes)})";
            }

            string? Texture()
            {
                int unit = instruction.TextureUnit;
                CgParameter[] rows = samplers[unit].ToArray();
                if (rows.Length != 1)
                    return Unsupported($"Texture unit {unit} has no unique Cg sampler binding");
                string? type = CgTypeName(rows[0].Type);
                if (type is not ("sampler1D" or "sampler2D" or "sampler3D" or "samplerCUBE") ||
                    instruction.ExpandedTexture)
                    return Unsupported($"Unsupported texture operation on unit {unit}");
                string shape = type["sampler".Length..];
                string name = ParameterName(rows[0], type, $"TEXUNIT{unit}");
                string lanes = shape switch
                {
                    "1D" => "x",
                    "2D" => "xy",
                    _ => "xyz"
                };
                stem = name.EndsWith("Sampler", StringComparison.Ordinal) ? name[..^7] + "Sample" : name + "Sample";
                bool projective = instruction.OpcodeType == RsxFragmentOpcode.TextureProjective;
                string? coordinate = Operand(instruction.Source0Operand, projective ? "xyzw" : lanes);
                string? b = instruction.OperandCount > 1 ? Operand(instruction.Source1Operand) : "";
                string? c = instruction.OperandCount > 2 ? Operand(instruction.Source2Operand) : "";
                if (coordinate is null || b is null || c is null) return null;
                string packed = shape switch
                {
                    "1D" => $"float4({coordinate}, 0.0, 0.0, {Scalar(b)})",
                    "2D" => $"float4({coordinate}, 0.0, {Scalar(b)})",
                    _ => $"float4({coordinate}, {Scalar(b)})"
                };
                return instruction.OpcodeType switch
                {
                    RsxFragmentOpcode.Texture => $"tex{shape}({name}, {coordinate})",
                    RsxFragmentOpcode.TextureProjective when shape is "1D" or "2D" or "3D" =>
                        $"tex{shape}proj({name}, {coordinate})",
                    RsxFragmentOpcode.TextureLod => $"tex{shape}lod({name}, {packed})",
                    RsxFragmentOpcode.TextureBias => $"tex{shape}bias({name}, {packed})",
                    RsxFragmentOpcode.TextureGradients =>
                        $"tex{shape}({name}, {coordinate}, ({b}).{lanes}, ({c}).{lanes})",
                    _ => Unsupported($"Texture opcode {instruction.OpcodeType} on unit {unit}")
                };
            }

            string? Operand(RsxFragmentOperand operand, string lanes = "xyzw")
            {
                Span<char> selectors = stackalloc char[4]
                {
                    "xyzw"[(int)operand.SwizzleX], "xyzw"[(int)operand.SwizzleY],
                    "xyzw"[(int)operand.SwizzleZ], "xyzw"[(int)operand.SwizzleW]
                };
                int width = lanes.Length;
                Span<char> selectedLanes = stackalloc char[width];
                for (int i = 0; i < width; i++) selectedLanes[i] = selectors["xyzw".IndexOf(lanes[i])];
                string swizzle = new(selectedLanes);
                string? value;
                if (operand.RegisterKind == RsxFragmentRegisterType.Temporary)
                {
                    value = values.Read(operand.Fp16, operand.RegisterIndex, swizzle, widen: operand.Fp16);
                }
                else if (operand.RegisterKind == RsxFragmentRegisterType.Input)
                    value = Input() is { } input ? input + SwizzleSuffix(swizzle) : null;
                else if (operand.RegisterKind == RsxFragmentRegisterType.InlineConstant)
                    value = InlineConstant(swizzle);
                else
                    value = Unsupported($"Fragment source register type {operand.RegisterKind}");
                if (value is null) return null;
                if (operand.Absolute)
                    value = $"abs({value})";
                RsxFragmentPrecision precision = instruction.SourcePrecision(operand.SourceIndex);
                bool precisionIgnored = operand.RegisterKind == RsxFragmentRegisterType.Input &&
                    instruction.SourceAttribute is RsxFragmentInputAttribute.Color0 or RsxFragmentInputAttribute.Color1
                        or RsxFragmentInputAttribute.SignedSideArea;
                if (!precisionIgnored)
                {
                    value = precision switch
                    {
                        // The existing render backends and RPCS3 treat source modifier 5 as a pass-through.
                        RsxFragmentPrecision.Real or RsxFragmentPrecision.Unknown5 => value,
                        RsxFragmentPrecision.Half when operand.RegisterKind != RsxFragmentRegisterType.Temporary ||
                            !operand.Fp16 => $"{VectorType(false, width)}({VectorType(true, width)}({value}))",
                        RsxFragmentPrecision.Half => value,
                        RsxFragmentPrecision.Saturate => $"saturate({value})",
                        _ => Unsupported($"Fragment source {operand.SourceIndex} precision {precision}")
                    };
                }
                return value is null || !operand.Negate ? value : $"-({value})";
            }

            string? Input()
            {
                RsxFragmentInputAttribute attribute = instruction.SourceAttribute;
                if (instruction.UsesIndexedInput)
                    return Unsupported("Dynamically indexed fragment input");
                string? semantic = attribute switch
                {
                    RsxFragmentInputAttribute.WindowPosition => "WPOS",
                    RsxFragmentInputAttribute.Color0 => "COLOR0",
                    RsxFragmentInputAttribute.Color1 => "COLOR1",
                    >= RsxFragmentInputAttribute.TextureCoordinate0 and <= RsxFragmentInputAttribute.TextureCoordinate9 =>
                        $"TEXCOORD{(int)attribute - (int)RsxFragmentInputAttribute.TextureCoordinate0}",
                    _ => null
                };
                if (semantic is null)
                    return Unsupported($"Fragment input {attribute}");
                if (!instruction.PerspectiveCorrection || attribute < RsxFragmentInputAttribute.TextureCoordinate0)
                    return source.Input((int)attribute, semantic);
                uint texCoordBit = 1u << ((int)attribute - (int)RsxFragmentInputAttribute.TextureCoordinate0);
                if ((enabledTexCoords & texCoordBit) == 0 || (texCoords2D & texCoordBit) != 0)
                    return Unsupported($"Explicit perspective correction for {semantic} without a four-component input");

                readsReciprocalW = true;
                string value = source.Input((int)attribute, semantic);
                string position = source.Input((int)RsxFragmentInputAttribute.WindowPosition, "WPOS");
                // The modifier multiplies the interpolated TEXCOORD by reciprocal clip W before swizzling.
                return $"({value} * {position}.w)";
            }

            string? InlineConstant(string swizzle)
            {
                int site = instruction.Offset + 16 - offset;
                if (ambiguousOwners.Contains(site))
                    return Unsupported("Ambiguous inline constant ownership");
                if (owners.TryGetValue(site, out CgParameter owner) && owner.Variability == Uniform)
                {
                    int width = NumericWidth(owner.Type);
                    if (width > 0 && swizzle.Any(lane => "xyzw".IndexOf(lane) >= width))
                        return Unsupported($"Fragment uniform {owner.Name} read outside its reflected width");
                    string type = width == 0 ? "float4" : CgTypeName(owner.Type)!;
                    return ParameterName(owner, type) + SwizzleSuffix(swizzle);
                }
                if (instruction.InlineConstant is not { } value)
                    return Unsupported("Missing inline constant");
                float x = value.X, y = value.Y, z = value.Z, w = value.W;
                if (owners.TryGetValue(site, out owner) && owner.DefaultOffset != 0)
                {
                    if (owner.DefaultOffset + 16UL > ReadWord(program, 8))
                        return Unsupported("Invalid inline constant default offset");
                    int defaults = (int)owner.DefaultOffset;
                    x = BitConverter.UInt32BitsToSingle(ReadWord(program, defaults));
                    y = BitConverter.UInt32BitsToSingle(ReadWord(program, defaults + 4));
                    z = BitConverter.UInt32BitsToSingle(ReadWord(program, defaults + 8));
                    w = BitConverter.UInt32BitsToSingle(ReadWord(program, defaults + 12));
                }
                if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z) || !float.IsFinite(w))
                    return Unsupported("Non-finite inline constant payload");
                float[] components = [x, y, z, w];
                float[] selected = swizzle.Select(lane => components["xyzw".IndexOf(lane)]).ToArray();
                // Common RGB-to-luma weights also occur rounded to half in stock programs.
                if (dotWidth == 3 && MatchesWeight(selected[0], 0.299f) && MatchesWeight(selected[1], 0.587f)
                    && MatchesWeight(selected[2], 0.114f) && instruction.Scale == RsxFragmentResultScale.None
                    && !instruction.Source0Operand.Negate && !instruction.Source1Operand.Negate)
                    stem = "luma";
                return selected.Length == 1 ? FloatLiteral(selected[0]) :
                    $"float{selected.Length}({string.Join(", ", selected.Select(FloatLiteral))})";
            }

            string? ApplyModifiers(string value)
            {
                string? scale = instruction.Scale switch
                {
                    RsxFragmentResultScale.None => "",
                    RsxFragmentResultScale.MultiplyBy2 => "2.0",
                    RsxFragmentResultScale.MultiplyBy4 => "4.0",
                    RsxFragmentResultScale.MultiplyBy8 => "8.0",
                    RsxFragmentResultScale.DivideBy2 => "0.5",
                    RsxFragmentResultScale.DivideBy4 => "0.25",
                    RsxFragmentResultScale.DivideBy8 => "0.125",
                    _ => null
                };
                if (scale is null)
                    return Unsupported($"Fragment result scale {instruction.Scale}");
                if (scale.Length != 0)
                    value = $"({value}) * {scale}";
                if (instruction.NoDest)
                    return value;
                // The named local itself performs the destination conversion. Saturation
                // must still run after rounding, as it does in the register-level form.
                if (instruction.DestFp16 && (!namedResult || instruction.Saturate))
                    value = $"{VectorType(true, scalarDot ? 1 : componentwise ? mask.Length : 4)}({value})";
                if (instruction.Saturate)
                    return $"saturate({value})";
                return instruction.DestinationPrecision switch
                {
                    RsxFragmentPrecision.Real or RsxFragmentPrecision.Half => value,
                    _ => Unsupported($"Fragment destination precision {instruction.DestinationPrecision}")
                };
            }

            string? Unsupported(string detail)
            {
                // A hand-written replacement for a missing instruction can access and modify registers.
                values.MaterializeAll();
                conditionFlags.Clear();
                return source.Unsupported(instruction.Offset, detail, instruction.Dst, instruction.Src0,
                    instruction.Src1, instruction.Src2);
            }
        }

        HashSet<int> colorTargets = parameters.Where(parameter => parameter.Referenced && parameter.Direction == 0x1002)
            .Select(parameter => ColorTarget(parameter.Semantic))
            .Where(target => target >= 0)
            .ToHashSet();
        if (colorTargets.Count == 0)
            colorTargets.Add(0);
        foreach (RsxFragmentColorExport export in RsxShaderTranslator.ReadFragmentColorExports(instructions, control))
        {
            if (!colorTargets.Contains(export.ColorTarget) || export.WrittenComponentMask == RsxFragmentWriteMask.None)
                continue;
            string output = source.Output(export.ColorTarget, $"COLOR{export.ColorTarget}",
                export.Fp16 ? "half4" : "float4");
            string lanes = export.WrittenComponents;
            string result = values.Read(export.Fp16, export.RegisterIndex, lanes);
            source.Body.AppendLine($"    {output}{SwizzleSuffix(lanes)} = {result};");
        }
        if (control.DepthExportEnabled)
        {
            string depth = values.Read(false, 1, "z");
            source.Body.AppendLine($"    {source.Output(4, "DEPTH", "float")} = {depth};");
        }
        if (readsReciprocalW)
            source.Body.Insert(0, $"    // {ReciprocalWNote}\n");

        static bool MatchesWeight(float value, float weight) => value == weight || value == (float)(Half)weight;

        string ParameterName(CgParameter parameter, string type, string? semantic = null)
        {
            if (!parameterNames.TryGetValue(parameter.Ordinal, out string? name))
                parameterNames.Add(parameter.Ordinal, name = source.Uniform(parameter, type, semantic));
            return name;
        }
    }

    private static int ColorTarget(string semantic)
    {
        if (string.Equals(semantic, "COLOR", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(semantic, "COL", StringComparison.OrdinalIgnoreCase))
            return 0;
        if ((semantic.Length == 6 && semantic.StartsWith("COLOR", StringComparison.OrdinalIgnoreCase)) ||
            (semantic.Length == 4 && semantic.StartsWith("COL", StringComparison.OrdinalIgnoreCase)))
            return semantic[^1] is >= '0' and <= '3' ? semantic[^1] - '0' : -1;
        return -1;
    }
}
