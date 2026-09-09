using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using IW4.Assets.Assets.TechniqueSet;
using IW4.FastFiles.Pointers;
using MapConverter.Game.IW3.PC.Techniques;

namespace MapConverter.Game.IW3.PC.Shaders;

/// <summary>
/// Lowers a recovered IW3 Direct3D 9 shader to RSX instructions without
/// copying a target-game shader payload. OpenAssetTools is only the extractor;
/// this compiler consumes its recovered Direct3D bytecode as source input.
/// </summary>
internal sealed class Iw3PcShaderCompiler
{
    private const uint CgVertexProfile = 7003;
    private const uint CgPixelProfile = 7004;
    private const uint CgConstantRegister = 0x882;
    private const uint CgTextureUnitBase = 0x800;
    private const uint CgUniformVariability = 0x1006;
    private const uint CgConstantVariability = 0x1007;
    private const uint CgInDirection = 0x1001;
    private const uint CgFloat4 = 0x418;
    private const uint CgSampler1D = 0x429;
    private const uint CgSampler2D = 0x42A;
    private const uint CgSampler3D = 0x42B;
    private const uint CgSamplerCube = 0x42D;
    private const ushort Direct3DFloat = 3;
    private const ushort Direct3DSampler1D = 11;
    private const ushort Direct3DSampler2D = 12;
    private const ushort Direct3DSampler3D = 13;
    private const ushort Direct3DSamplerCube = 14;
    private const int CgHeaderSize = 0x20;
    private const int CgParameterSize = 0x30;
    private const int CgDescriptorSize = 0x18;
    private const int RsxVertexHeaderSize = 0x24;
    private const int RsxFragmentHeaderSize = 0x30;

    internal Iw3ShaderCompilation Compile(
        Iw3ShaderSource source,
        string shaderProgramPath,
        ushort signedNormalInputMask)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(shaderProgramPath);

        string fullPath = Path.GetFullPath(shaderProgramPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The extracted shader program was not found.", fullPath);

        byte[] direct3D = ExtractSingleDirect3DProgram(File.ReadAllBytes(fullPath), fullPath);
        ValidateDirect3DStage(direct3D, source.Stage, fullPath);
        IReadOnlyList<Direct3DConstant> constants = ReadConstantTable(direct3D, fullPath);
        ValidateSourceArguments(source, constants, fullPath);
        Iw3ShaderParameter[] parameters = constants.Select(CreateParameter).ToArray();
        (ushort comparisonSamplerMask, ushort reflectionProbeSamplerMask) = GetCodeSamplerMasks(source, parameters);
        if (constants.Any(constant => constant.RegisterSet == 3 && constant.RegisterIndex < 16 &&
                (comparisonSamplerMask & (1 << constant.RegisterIndex)) != 0 &&
                constant.Type != Direct3DSampler2D))
            throw new InvalidDataException($"Shader '{fullPath}' binds a non-2D comparison sampler.");
        if (constants.Any(constant => constant.RegisterSet == 3 && constant.RegisterIndex < 16 &&
                (reflectionProbeSamplerMask & (1 << constant.RegisterIndex)) != 0 &&
                constant.Type != Direct3DSamplerCube))
            throw new InvalidDataException($"Shader '{fullPath}' binds a non-cube reflection probe sampler.");

        string scratchDirectory = Path.Combine(Path.GetTempPath(), "mapconverter-shaders", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratchDirectory);
        try
        {
            string inputPath = Path.Combine(scratchDirectory, "input.cso");
            string assemblyPath = Path.Combine(scratchDirectory, "program.asm");
            string binaryPath = Path.Combine(scratchDirectory, "program.rsx");
            File.WriteAllBytes(inputPath, direct3D);

            string mojoProfile = TranslateWithMojoShader(inputPath, assemblyPath, source.Stage, fullPath);
            NormalizeAssembly(assemblyPath, source.Stage, signedNormalInputMask,
                comparisonSamplerMask, reflectionProbeSamplerMask);
            Assemble(assemblyPath, binaryPath, source.Stage, fullPath);

            RsxProgram program = ReadRsxProgram(File.ReadAllBytes(binaryPath), source.Stage, fullPath);
            byte[] data = BuildCgProgram(source.Stage, constants, program, fullPath);
            MaterialShaderKind kind = source.Stage == Iw3ShaderStage.Vertex
                ? MaterialShaderKind.Vertex
                : MaterialShaderKind.Pixel;
            var asset = new MaterialShaderAsset
            {
                Kind = kind,
                Name = source.ProgramName,
                DataPointer = default,
                DataSize = checked((uint)data.Length),
                Data = data,
                ProgramBytes = kind == MaterialShaderKind.Pixel ? new byte[12] : []
            };
            return new Iw3ShaderCompilation(
                asset,
                parameters,
                $"MojoShader ad5dff84830c2863c841f4b1f4e3df78c705b383 ({mojoProfile}) + PSL1GHT cgcomp f649a08fd536a9e27c08c7db2d93a2d7ee4c3bbe");
        }
        finally
        {
            if (Directory.Exists(scratchDirectory))
                Directory.Delete(scratchDirectory, recursive: true);
        }
    }

    private static string TranslateWithMojoShader(
        string inputPath,
        string assemblyPath,
        Iw3ShaderStage stage,
        string sourcePath)
    {
        string tool = FindNativeTool("mapconverter-mojoshader");
        string preferred = stage == Iw3ShaderStage.Vertex ? "nv3" : "nv3";
        if (Run(tool, [preferred, inputPath, assemblyPath], out string error))
            return preferred;

        // NV4 is needed by known IW3 pixel programs that contain TEXLDL.
        if (stage == Iw3ShaderStage.Pixel &&
            Run(tool, ["nv4", inputPath, assemblyPath], out string fallbackError))
        {
            return "nv4";
        }

        throw new InvalidDataException(
            $"Shader '{sourcePath}' could not be translated from Direct3D 9 bytecode. {error}");
    }

    private static void NormalizeAssembly(
        string assemblyPath,
        Iw3ShaderStage stage,
        ushort signedNormalInputMask,
        ushort comparisonSamplerMask,
        ushort reflectionProbeSamplerMask)
    {
        string assembly = File.ReadAllText(assemblyPath, Encoding.ASCII);
        string requiredHeader = stage == Iw3ShaderStage.Vertex ? "!!ARBvp1.0" : "!!ARBfp1.0";
        int firstLine = assembly.IndexOf('\n');
        if (firstLine < 0)
            throw new InvalidDataException("MojoShader produced assembly without a program header.");

        string header = assembly[..firstLine].TrimEnd('\r');
        bool validVertex = stage == Iw3ShaderStage.Vertex &&
            (header == "!!ARBvp1.0" || header == "!!NVvp3.0" || header == "!!NVvp4.0");
        bool validPixel = stage == Iw3ShaderStage.Pixel &&
            (header == "!!ARBfp1.0" || header == "!!NVfp3.0" || header == "!!NVfp4.0");
        if (!validVertex && !validPixel)
            throw new InvalidDataException($"MojoShader produced unsupported assembly header '{header}'.");

        // cgcomp assembles the shared ARB syntax and accepts the NV extension
        // instructions generated by MojoShader; only the profile declaration differs.
        assembly = requiredHeader + assembly[firstLine..];
        if (stage == Iw3ShaderStage.Vertex)
            assembly = LowerSignedNormalInputs(assembly, signedNormalInputMask);
        else if (signedNormalInputMask != 0)
            throw new InvalidDataException("A pixel shader cannot have signed normal vertex inputs.");
        if (stage == Iw3ShaderStage.Pixel)
            assembly = LowerReflectionProbeSamples(assembly, reflectionProbeSamplerMask);
        else if (comparisonSamplerMask != 0 || reflectionProbeSamplerMask != 0)
            throw new InvalidDataException("A vertex shader cannot have code pixel samplers.");
        assembly = NormalizeCgCompSyntax(assembly, stage);
        if (stage == Iw3ShaderStage.Pixel)
            assembly = LowerComparisonSamplers(assembly, comparisonSamplerMask);
        File.WriteAllText(assemblyPath, assembly, Encoding.ASCII);
    }

    internal static (ushort Comparison, ushort ReflectionProbe) GetCodeSamplerMasks(
        Iw3ShaderSource source,
        IReadOnlyList<Iw3ShaderParameter> parameters)
    {
        ushort comparison = 0;
        ushort reflectionProbe = 0;
        foreach (Iw3ShaderParameter parameter in parameters)
        {
            if (parameter.Kind != Iw3ShaderParameterKind.Sampler || !parameter.IsReferenced)
                continue;
            Iw3ShaderArgumentSource[] assignments = source.Arguments
                .Where(argument => argument.Destination.Name == parameter.Name).ToArray();
            if (assignments.Length > 1)
                throw new InvalidDataException($"Shader '{source.ProgramName}' assigns sampler '{parameter.Name}' more than once.");
            string accessor = parameter.Name;
            if (assignments.Length == 1)
            {
                if (assignments[0].Value is not Iw3CodeShaderValueSource { Kind: Iw3CodeShaderValueKind.Sampler } code)
                    continue;
                if (assignments[0].Destination.Index is not null || code.ElementIndex is not null)
                    throw new InvalidDataException($"Shader '{source.ProgramName}' uses an indexed code sampler.");
                accessor = code.Accessor;
            }
            if (!Iw3TechniqueBindingFacts.TryGetSampler(accessor, out Iw3CodeSamplerBinding binding) ||
                binding.Source is not (MaterialTextureSource.ShadowMapSun or MaterialTextureSource.ShadowMapSpot or
                    MaterialTextureSource.ReflectionProbe))
                continue;
            if (source.Stage != Iw3ShaderStage.Pixel || parameter.RegisterCount != 1 ||
                parameter.Resource < CgTextureUnitBase || parameter.Resource >= CgTextureUnitBase + 16)
                throw new InvalidDataException($"Shader '{source.ProgramName}' has an unsupported code sampler '{parameter.Name}'.");
            ushort bit = checked((ushort)(1 << (int)(parameter.Resource - CgTextureUnitBase)));
            if (binding.Source == MaterialTextureSource.ReflectionProbe)
                reflectionProbe |= bit;
            else
                comparison |= bit;
        }
        return (comparison, reflectionProbe);
    }

    private static string LowerReflectionProbeSamples(string assembly, ushort reflectionProbeSamplerMask)
    {
        if (reflectionProbeSamplerMask == 0)
            return assembly;

        string[] lines = assembly.Split('\n');
        ushort sampled = 0;
        for (int index = 0; index < lines.Length; index++)
        {
            Match sample = Regex.Match(lines[index].Trim(),
                @"^(\w+) ([^,]+), ([^,]+), texture\[(\d+)\], (\w+);$");
            if (!sample.Success || !int.TryParse(sample.Groups[4].Value, out int unit) || unit is < 0 or >= 16 ||
                (reflectionProbeSamplerMask & (1 << unit)) == 0)
                continue;
            string temporary = sample.Groups[2].Value;
            if (sample.Groups[1].Value is not ("TEX" or "TXL") || sample.Groups[5].Value != "CUBE" ||
                !Regex.IsMatch(temporary, @"^[rR]\d+$") || index + 1 >= lines.Length)
                throw new InvalidDataException("The reflection probe has an unsupported texture lookup.");

            Match decode = Regex.Match(lines[index + 1].Trim(),
                $@"^MUL ([rR]\d+)\.(xyz|yzw), {Regex.Escape(temporary)}\.w, {Regex.Escape(temporary)}(\.xxyz)?;$");
            if (!decode.Success || (decode.Groups[2].Value == "yzw") != decode.Groups[3].Success)
                throw new InvalidDataException("The reflection probe has an unsupported RGB-alpha decode.");

            // The IW3 converter's probe images store sqrt(saturate(4 * RGB * A))
            // for native IW4 shaders. Recover the source RGB*A scale from that
            // encoding; preserve alpha, texture coordinates/LOD and later terms.
            string destination = decode.Groups[1].Value;
            string mask = decode.Groups[2].Value;
            string rgb = temporary + decode.Groups[3].Value;
            lines[index + 1] = $"MUL {destination}.{mask}, {rgb}, {rgb};\n" +
                $"MUL {destination}.{mask}, {destination}, 0.25;";
            sampled |= checked((ushort)(1 << unit));
        }
        if (sampled != reflectionProbeSamplerMask)
            throw new InvalidDataException("A referenced reflection probe has no supported texture lookup.");
        return string.Join('\n', lines);
    }

    private static string LowerComparisonSamplers(string assembly, ushort comparisonSamplerMask)
    {
        if (comparisonSamplerMask == 0)
            return assembly;

        string[] lines = assembly.Split('\n');
        var instructions = Enumerable.Range(0, lines.Length)
            .Where(index => !string.IsNullOrWhiteSpace(lines[index]) &&
                !Regex.IsMatch(lines[index], @"^(?:!!|#|OPTION\b)"))
            .Select(index =>
            {
                string[] parts = lines[index].Trim().TrimEnd(';').Split(' ', 2);
                return (Line: index, Opcode: parts[0], Arguments: parts.Length == 1
                    ? Array.Empty<string>() : parts[1].Split(',', StringSplitOptions.TrimEntries));
            }).ToArray();
        var constants = Regex.Matches(assembly, @"(?m)^#const (c\[\d+\]) = ([^\r\n]+)$")
            .ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(value => float.Parse(value, CultureInfo.InvariantCulture)).ToArray(), StringComparer.Ordinal);
        static (string Register, string Mask) Destination(string operand)
        {
            Match match = Regex.Match(operand, @"^(R\d+)(?:\.([xyzw]+))?$");
            return match.Success
                ? (match.Groups[1].Value, match.Groups[2].Success ? match.Groups[2].Value : "xyzw")
                : (string.Empty, string.Empty);
        }

        int Definition(string register, int component, int before)
        {
            for (int index = before - 1; index >= 0; index--)
            {
                var instruction = instructions[index];
                if (instruction.Opcode is "IF" or "ELSE" or "ENDIF")
                    return -1;
                if (instruction.Arguments.Length == 0)
                    continue;
                var destination = Destination(instruction.Arguments[0]);
                if (destination.Register == register && destination.Mask.Contains("xyzw"[component]))
                    return index;
            }
            return -1;
        }

        static (string Register, int Component, bool Negate) Source(string operand, int component)
        {
            Match match = Regex.Match(operand,
                @"^(-)?(R\d+|c\[\d+\]|f\[fragment\.texcoord\[\d+\]\])(?:\.([xyzw]{1,4}))?$");
            if (!match.Success)
                return (string.Empty, 0, false);
            string swizzle = match.Groups[3].Success ? match.Groups[3].Value : "xyzw";
            if (swizzle.Length is not (1 or 4))
                return (string.Empty, 0, false);
            return (match.Groups[2].Value, "xyzw".IndexOf(swizzle[swizzle.Length == 1 ? 0 : component]),
                match.Groups[1].Success);
        }

        var scalarCache = new Dictionary<(string Operand, int Component, int Before), string?>();
        string? Scalar(string operand, int component, int before)
        {
            var key = (operand, component, before);
            if (scalarCache.TryGetValue(key, out string? cached))
                return cached;
            var source = Source(operand, component);
            string? value = null;
            if (constants.TryGetValue(source.Register, out float[]? literal) && literal.Length == 4)
                value = literal[source.Component].ToString("R", CultureInfo.InvariantCulture);
            else if (source.Register.StartsWith("f[", StringComparison.Ordinal))
                value = $"{source.Register}.{"xyzw"[source.Component]}";
            else if (Definition(source.Register, source.Component, before) is >= 0 and var definition)
            {
                var instruction = instructions[definition];
                string? A(int argument) => Scalar(instruction.Arguments[argument], source.Component, definition);
                string? Multiply(string? left, string? right) => left == "0" || right == "0" ? "0" :
                    left == "1" ? right : right == "1" ? left : null;
                string? Add(string? left, string? right) => left == "0" ? right : right == "0" ? left : null;
                value = instruction.Opcode switch
                {
                    "MOV" when instruction.Arguments.Length == 2 => A(1),
                    "MUL" when instruction.Arguments.Length == 3 => Multiply(A(1), A(2)),
                    "ADD" when instruction.Arguments.Length == 3 => Add(A(1), A(2)),
                    "MAD" when instruction.Arguments.Length == 4 => Add(Multiply(A(1), A(2)), A(3)),
                    _ => null
                };
            }
            if (source.Negate && value is not null)
                value = value == "0" ? "0" : value.StartsWith('-') ? value[1..] : "-" + value;
            scalarCache.Add(key, value);
            return value;
        }

        bool IsDepthSample(int index)
        {
            var instruction = instructions[index];
            if (instruction.Opcode is not ("TEX" or "TXL" or "TXP") || instruction.Arguments.Length != 4)
                return false;
            Match unit = Regex.Match(instruction.Arguments[2], @"^texture\[(\d+)\]$");
            return unit.Success && int.Parse(unit.Groups[1].Value, CultureInfo.InvariantCulture) is >= 0 and < 16 and var number &&
                (comparisonSamplerMask & (1 << number)) != 0;
        }

        int Sample(string operand, int component, int before, bool anyChannel = false)
        {
            if (anyChannel)
                operand = operand.Replace("|", string.Empty, StringComparison.Ordinal);
            var source = Source(operand, component);
            if (source.Negate && !anyChannel || source.Register.Length == 0)
                return -1;
            int definition = Definition(source.Register, source.Component, before);
            if (definition < 0)
                return -1;
            var instruction = instructions[definition];
            if (IsDepthSample(definition))
                return anyChannel || source.Component == 0 ? definition : -1;
            return instruction.Opcode == "MOV" && instruction.Arguments.Length == 2
                ? Sample(instruction.Arguments[1], source.Component, definition, anyChannel) : -1;
        }

        string ReadComponents(int index)
        {
            var instruction = instructions[index];
            string opcode = instruction.Opcode.EndsWith("_SAT", StringComparison.Ordinal)
                ? instruction.Opcode[..^4] : instruction.Opcode;
            // MojoShader expands DP2 into a full multiply followed immediately
            // by an XY sum that overwrites every lane; its ZW products are unused.
            if (instruction.Opcode == "MUL" && instruction.Arguments.Length == 3 &&
                Regex.IsMatch(instruction.Arguments[0], @"^R\d+$") && index + 1 < instructions.Length &&
                instructions[index + 1].Opcode == "ADD" && instructions[index + 1].Arguments.SequenceEqual(
                    [instruction.Arguments[0], instruction.Arguments[0] + ".x", instruction.Arguments[0] + ".y"]))
                return "xy";
            if (opcode is "TEX" or "TXL" or "TXP")
            {
                string coordinates = instruction.Arguments.Length == 4 ? instruction.Arguments[3] switch
                {
                    "1D" => "x",
                    "2D" => IsDepthSample(index) ? "xyz" : "xy",
                    "3D" or "CUBE" => "xyz",
                    _ => "xyzw"
                } : "xyzw";
                return opcode is "TXL" or "TXP" ? coordinates + "w" : coordinates;
            }
            return opcode switch
            {
                "DP2" => "xy",
                "DP3" or "NRM" => "xyz",
                "RCP" or "RSQ" or "EX2" or "LG2" or "POW" or "SIN" or "COS" or "SCS" => "x",
                "MOV" or "MUL" or "ADD" or "MAD" or "MIN" or "MAX" or "ABS" or "LRP" or "CMP" or
                    "SLT" or "SGE" or "SGT" or "SLE" or "SEQ" or "SNE" or
                    "SLTC" or "SGEC" or "SGTC" or "SLEC" or "SEQC" or "SNEC" =>
                    Destination(instruction.Arguments[0]).Mask is { Length: > 0 } mask ? mask : "xyzw",
                _ => "xyzw"
            };
        }

        var lowered = new HashSet<int>();
        var sampleReceivers = new Dictionary<int, string>();
        for (int index = 0; index + 2 < instructions.Length; index++)
        {
            var subtract = instructions[index];
            var compare = instructions[index + 1];
            var average = instructions[index + 2];
            if (subtract.Opcode != "ADD" || subtract.Arguments.Length != 3 ||
                compare.Opcode != "CMP" || compare.Arguments.Length != 4 ||
                average.Opcode != "DP4" || average.Arguments.Length != 3)
                continue;
            string temporary = subtract.Arguments[0];
            string samples = subtract.Arguments[1];
            if (!Regex.IsMatch(temporary, @"^R\d+$") || !Regex.IsMatch(samples, @"^R\d+$") ||
                compare.Arguments[0] != temporary || compare.Arguments[1] != temporary || average.Arguments[1] != temporary)
                continue;
            int[] taps = Enumerable.Range(0, 4).Select(component => Sample(samples, component, index)).ToArray();
            if (taps.Any(tap => tap < 0))
                continue;
            if (taps.Distinct().Count() != 4 || taps.Select(tap => instructions[tap].Arguments[2]).Distinct().Count() != 1 ||
                !Regex.IsMatch(subtract.Arguments[2], @"^-f\[fragment\.texcoord\[\d+\]\]\.z$") ||
                Enumerable.Range(0, 4).Any(component => Scalar(compare.Arguments[2], component, index + 1) != "0" ||
                    Scalar(compare.Arguments[3], component, index + 1) != "1" ||
                    Scalar(average.Arguments[2], component, index + 2) != "0.25"))
                throw new InvalidDataException("The code-depth sampler has an unsupported software comparison filter.");
            string receiver = subtract.Arguments[2][1..];
            foreach (int tap in taps)
            {
                var sample = instructions[tap];
                if (sample.Arguments[3] != "2D" ||
                    sampleReceivers.TryGetValue(tap, out string? previous) && previous != receiver)
                    throw new InvalidDataException("The code-depth sampler has conflicting receiver coordinates.");
                sampleReceivers[tap] = receiver;
                if (sample.Opcode == "TEX" && Scalar(sample.Arguments[1], 2, tap) != receiver)
                {
                    // SM2 raw-depth TEX initializes only XY. Its full destination
                    // overwrite makes this receiver-Z write local to the lookup.
                    if (!Regex.IsMatch(sample.Arguments[0], @"^R\d+$") || sample.Arguments[0] != sample.Arguments[1])
                        throw new InvalidDataException("The code-depth TEX cannot supply receiver Z without overwriting live coordinates.");
                    lines[sample.Line] = $"MOV {sample.Arguments[0]}.z, {receiver};\n{lines[sample.Line]}";
                }
                else if (Scalar(sample.Arguments[1], 2, tap) != receiver ||
                    sample.Opcode == "TXL" && Scalar(sample.Arguments[1], 3, tap) != "0" ||
                    sample.Opcode == "TXP" && Scalar(sample.Arguments[1], 3, tap) != receiver[..^1] + "w")
                    throw new InvalidDataException("The code-depth sampler has an unsupported projected receiver or LOD.");
            }
            // PS3 code shadow samplers return filtered visibility. Preserve the
            // authored taps/cascades, averaging those values without comparing twice.
            lines[subtract.Line] = $"MOV {temporary}, {samples};";
            lines[compare.Line] = string.Empty;
            lowered.Add(index);
        }

        var hardwareSamples = new HashSet<int>();
        for (int index = 0; index < instructions.Length; index++)
        {
            var instruction = instructions[index];
            if (instruction.Opcode == "MOV" || lowered.Contains(index) ||
                instruction.Arguments.Length < 2 && instruction.Opcode != "KIL")
                continue;
            string read = ReadComponents(index);
            var uses = new List<(int Argument, int Tap)>();
            for (int argument = instruction.Opcode == "KIL" ? 0 : 1; argument < instruction.Arguments.Length; argument++)
            {
                foreach (char component in read)
                {
                    int tap = Sample(instruction.Arguments[argument], "xyzw".IndexOf(component), index, anyChannel: true);
                    if (tap < 0)
                        continue;
                    if (Sample(instruction.Arguments[argument], "xyzw".IndexOf(component), index) != tap)
                        throw new InvalidDataException("The code-depth sampler consumes an unsupported result channel or modifier.");
                    uses.Add((argument, tap));
                }
            }
            if (uses.Count == 0)
                continue;
            // Authored HSM programs consume visibility through a four-tap average
            // or a cascade/light blend. Prove that use rather than infer an HSM name.
            bool hardwareAverage = instruction.Opcode == "DP4" && instruction.Arguments.Length == 3 &&
                uses.All(use => use.Argument == 1) && uses.Select(use => use.Tap).Distinct().Count() == 4 &&
                uses.Select(use => instructions[use.Tap].Arguments[2]).Distinct().Count() == 1 &&
                Enumerable.Range(0, 4).All(component => Scalar(instruction.Arguments[2], component, index) == "0.25");
            bool hardwareBlend = instruction.Opcode == "LRP" && instruction.Arguments.Length == 4 &&
                uses.All(use => use.Argument is 2 or 3);
            if (!hardwareAverage && !hardwareBlend)
                throw new InvalidDataException($"The code-depth sampler has unsupported visibility arithmetic '{lines[instruction.Line]}'.");
            hardwareSamples.UnionWith(uses.Select(use => use.Tap));
        }
        for (int index = 0; index < instructions.Length; index++)
        {
            if (!IsDepthSample(index) || sampleReceivers.ContainsKey(index))
                continue;
            var sample = instructions[index];
            string? reference = Scalar(sample.Arguments[1], 2, index);
            string? projectiveW = Scalar(sample.Arguments[1], 3, index);
            if (!hardwareSamples.Contains(index) || sample.Arguments[3] != "2D" || reference is null ||
                !Regex.IsMatch(reference, @"^f\[fragment\.texcoord\[\d+\]\]\.z$") ||
                sample.Opcode == "TXL" && projectiveW != "0" ||
                sample.Opcode == "TXP" && projectiveW != "1" && projectiveW != reference[..^1] + "w")
                throw new InvalidDataException("The code-depth sampler has an unproven hardware comparison use or receiver coordinate.");
        }
        return lowered.Count == 0 ? assembly :
            string.Join('\n', lines.Where(line => !string.IsNullOrWhiteSpace(line))) + "\n";
    }

    private static string LowerSignedNormalInputs(string assembly, ushort signedNormalInputMask)
    {
        if (signedNormalInputMask == 0)
            return assembly;

        string[] lines = assembly.Split('\n');
        int[] instructions = Enumerable.Range(0, lines.Length)
            .Where(index => !string.IsNullOrWhiteSpace(lines[index]) &&
                !Regex.IsMatch(lines[index].TrimStart(),
                    @"^(?:!!|#|(?:OPTION|PARAM|ATTRIB|OUTPUT|TEMP|FLOAT\s+TEMP|ADDRESS|ALIAS)\b)"))
            .ToArray();
        var inputs = new List<(string Alias, int Instruction)>();
        foreach (Match attribute in Regex.Matches(assembly, @"(?m)^ATTRIB\s+([vt]\d+)\s*=\s*([^;]+);\s*$"))
        {
            string semantic = attribute.Groups[2].Value.Trim();
            MaterialStreamDestination? destination = semantic switch
            {
                "vertex.position" => MaterialStreamDestination.Position,
                "vertex.weight" => MaterialStreamDestination.Weight,
                "vertex.normal" => MaterialStreamDestination.Normal,
                "vertex.color.primary" => MaterialStreamDestination.Color0,
                "vertex.color.secondary" => MaterialStreamDestination.Color1,
                "vertex.fogcoord" => MaterialStreamDestination.Fog,
                _ => null
            };
            Match texcoord = Regex.Match(semantic, @"^vertex\.texcoord\[([0-7])\]$");
            if (texcoord.Success)
            {
                destination = (MaterialStreamDestination)((int)MaterialStreamDestination.TexCoord0 +
                    int.Parse(texcoord.Groups[1].Value, CultureInfo.InvariantCulture));
            }
            if (destination is null || (signedNormalInputMask & (1 << (int)destination.Value)) == 0)
                continue;

            string alias = attribute.Groups[1].Value;
            int[] uses = Enumerable.Range(0, instructions.Length)
                .Where(index => Regex.IsMatch(lines[instructions[index]], $@"\b{Regex.Escape(alias)}\b"))
                .ToArray();
            if (uses.Length == 0)
                continue;
            if (uses.Length != 1 ||
                Regex.Matches(lines[instructions[uses[0]]], $@"\b{Regex.Escape(alias)}\b").Count != 1)
            {
                throw new InvalidDataException($"Signed normal input '{alias}' must have one canonical unpack use.");
            }
            inputs.Add((alias, uses[0]));
        }
        if (inputs.Count == 0)
            return assembly;

        // These source programs unpack IW3 byte4 directions. PS3 CMP streams
        // already supply signed normalized XYZ and W=1. Only straight-line,
        // component-proven uses can discard the source format's scale in W.
        foreach (int index in instructions)
        {
            if (!Regex.IsMatch(lines[index].Trim(),
                    @"^(?:END|(?:MOV|ADD|SUB|MUL|MAD|MIN|MAX|ABS|FRC|FLR|DP3|DP4|RCP|RSQ|EX2|LG2|SLT|SGE)\s+[^();]+;)$"))
            {
                throw new InvalidDataException("Signed normal input lowering requires supported straight-line vertex arithmetic.");
            }
        }

        float[] unpack = [0.007874015f, 0.003921568f, -1f, 0.752941191f];
        string unpackLiteral = string.Join(' ', unpack.Select(value => value.ToString("R", CultureInfo.InvariantCulture)));
        var unpackConstants = new HashSet<string>(StringComparer.Ordinal);
        var declaredConstants = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match constant in Regex.Matches(assembly, @"(?m)^PARAM\s+(c\d+)\s*=\s*\{([^}]*)\};\s*$"))
        {
            string alias = constant.Groups[1].Value;
            if (!declaredConstants.Add(alias))
                throw new InvalidDataException($"Signed normal input lowering found repeated constant '{alias}'.");
            if (NormalizeLiteralVector(constant.Groups[2].Value) == unpackLiteral)
                unpackConstants.Add(alias);
        }

        foreach ((string alias, int instruction) in inputs)
        {
            int line = instructions[instruction];
            Match unpackInstruction = Regex.Match(lines[line].Trim(),
                $@"^MAD\s+(r\d+),\s*{Regex.Escape(alias)},\s*(c\d+)\.xxxy,\s*\2\.zzzw;$");
            if (!unpackInstruction.Success || !unpackConstants.Contains(unpackInstruction.Groups[2].Value) ||
                instruction + 1 >= instructions.Length)
            {
                throw new InvalidDataException($"Signed normal input '{alias}' has an unsupported unpack instruction.");
            }

            string temporary = unpackInstruction.Groups[1].Value;
            string escapedTemporary = Regex.Escape(temporary);
            string multiply = lines[instructions[instruction + 1]].Trim();
            bool scaleInPlace = Regex.IsMatch(multiply,
                $@"^MUL\s+{escapedTemporary}\.xyz,\s*{escapedTemporary}\.w,\s*{escapedTemporary};$");
            Match scaleIntoYzw = Regex.Match(multiply,
                $@"^MUL\s+(r\d+)\.yzw,\s*{escapedTemporary}\.w,\s*{escapedTemporary}\.xxyz;$");
            if (!scaleInPlace && !scaleIntoYzw.Success)
                throw new InvalidDataException($"Signed normal input '{alias}' has an unsupported unpack multiply.");

            string changedComponents = scaleInPlace ? "w" :
                scaleIntoYzw.Groups[1].Value == temporary ? "x" : "xyzw";
            if (!AreNormalUnpackComponentsDead(lines, instructions, instruction + 2, temporary, changedComponents))
                throw new InvalidDataException($"Signed normal input '{alias}' retains source-format unpack components.");

            lines[line] = $"MOV {temporary}, {alias};" + (lines[line].EndsWith('\r') ? "\r" : string.Empty);
        }
        return string.Join('\n', lines);
    }

    private static bool AreNormalUnpackComponentsDead(
        string[] lines,
        int[] instructions,
        int start,
        string temporary,
        string changedComponents)
    {
        string escapedTemporary = Regex.Escape(temporary);
        var liveComponents = changedComponents.ToHashSet();
        for (int index = start; index < instructions.Length; index++)
        {
            string line = lines[instructions[index]].Trim();
            if (line == "END")
                return true;
            Match instruction = Regex.Match(line, @"^([A-Z][A-Z0-9]*)\s+([A-Za-z_]\w*)(?:\.([xyzw]+))?,\s*(.+);$");
            if (!instruction.Success)
                return false;
            string opcode = instruction.Groups[1].Value;
            string destinationMask = instruction.Groups[3].Success ? instruction.Groups[3].Value : "xyzw";
            string readMask = opcode switch
            {
                "DP3" => "xyz",
                "DP4" => "xyzw",
                "RCP" or "RSQ" or "EX2" or "LG2" => "x",
                _ => destinationMask
            };
            foreach (Match source in Regex.Matches(instruction.Groups[4].Value,
                         $@"\b{escapedTemporary}\b(?:\.([xyzw]+))?"))
            {
                string swizzle = source.Groups[1].Success ? source.Groups[1].Value : "xyzw";
                if (swizzle.Length != 1 && swizzle.Length != 4)
                    return false;
                if (readMask.Any(component => liveComponents.Contains(
                        swizzle[swizzle.Length == 1 ? 0 : "xyzw".IndexOf(component)])))
                    return false;
            }
            if (instruction.Groups[2].Value == temporary)
            {
                liveComponents.ExceptWith(destinationMask);
                if (liveComponents.Count == 0)
                    return true;
            }
        }
        return false;
    }

    private static string NormalizeCgCompSyntax(string assembly, Iw3ShaderStage stage)
    {
        var occupiedConstants = Regex.Matches(assembly, @"\bc(?:\[(\d+)\]|(\d+)\b)")
            .Select(match => int.Parse(
                match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value,
                CultureInfo.InvariantCulture))
            .ToHashSet();
        int nextConstant = 255;
        string AllocateConstant()
        {
            while (nextConstant >= 0 && occupiedConstants.Contains(nextConstant))
                nextConstant--;
            if (nextConstant < 0)
                throw new InvalidDataException("The shader has no free constant register for RSX assembly lowering.");
            occupiedConstants.Add(nextConstant);
            return $"c[{nextConstant--}]";
        }

        var inputAliases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match attribute in Regex.Matches(
                     assembly,
                     @"(?m)^ATTRIB\s+([vt]\d+)\s*=\s*([^;]+);\s*$"))
        {
            string target = attribute.Groups[2].Value.Trim();
            string expectedPrefix = stage == Iw3ShaderStage.Vertex ? "vertex." : "fragment.";
            if (!target.StartsWith(expectedPrefix, StringComparison.Ordinal) ||
                Regex.IsMatch(target, @"\.[xyzw]{1,4}$") ||
                !Regex.IsMatch(target, @"^(?:vertex|fragment)\.[a-z]+(?:\.[a-z]+)*(?:\[\d+\])?$"))
            {
                throw new InvalidDataException(
                    $"MojoShader emitted unsupported {stage} input alias '{attribute.Value}'.");
            }
            inputAliases.Add(
                attribute.Groups[1].Value,
                stage == Iw3ShaderStage.Vertex ? $"v[{target}]" : $"f[{target}]");
        }

        assembly = Regex.Replace(assembly, @"(?m)^ATTRIB\s+[vt]\d+\s*=\s*[^;]+;\s*$", string.Empty);
        foreach ((string alias, string target) in inputAliases)
            assembly = Regex.Replace(assembly, $@"\b{Regex.Escape(alias)}\b", target);

        int nextTemporary = Regex.Matches(assembly, @"\bR(\d+)\b")
            .Select(match => checked(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) + 1))
            .DefaultIfEmpty(0)
            .Max();
        int temporaryLimit = stage == Iw3ShaderStage.Vertex ? 32 : 48;
        string AllocateTemporary()
        {
            if (nextTemporary >= temporaryLimit)
                throw new InvalidDataException("The shader has no free temporary register for RSX assembly lowering.");
            return $"R{nextTemporary++}";
        }

        var temporaryAliases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match temporary in Regex.Matches(
                     assembly,
                     @"(?m)^(?:FLOAT\s+)?TEMP\s+([A-Za-z_]\w*)\s*;\s*$"))
        {
            string alias = temporary.Groups[1].Value;
            if (alias.Length > 1 && alias[0] == 'r' &&
                int.TryParse(alias.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int index))
            {
                temporaryAliases.Add(alias, $"R{index}");
                nextTemporary = Math.Max(nextTemporary, checked(index + 1));
            }
        }
        foreach (Match temporary in Regex.Matches(
                     assembly,
                     @"(?m)^(?:FLOAT\s+)?TEMP\s+([A-Za-z_]\w*)\s*;\s*$"))
        {
            string alias = temporary.Groups[1].Value;
            if (!temporaryAliases.ContainsKey(alias))
                temporaryAliases.Add(alias, AllocateTemporary());
        }
        if (nextTemporary > temporaryLimit)
            throw new InvalidDataException("The shader exceeds the bundled RSX assembler's temporary register limit.");
        assembly = Regex.Replace(assembly, @"(?m)^(?:FLOAT\s+)?TEMP\s+([A-Za-z_]\w*)\s*;\s*$", string.Empty);
        foreach ((string alias, string target) in temporaryAliases)
            assembly = Regex.Replace(assembly, $@"\b{Regex.Escape(alias)}\b", target);

        var outputAliases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match output in Regex.Matches(assembly, @"(?m)^OUTPUT\s+([A-Za-z_]\w*)\s*=\s*([^;]+);\s*$"))
        {
            string alias = output.Groups[1].Value;
            string target = output.Groups[2].Value.Trim();
            string supportedAlias = stage == Iw3ShaderStage.Vertex
                ? @"^o(?:\d+|Pos|Fog|Pts|D\d+|T\d+)$"
                : @"^o(?:C\d+|Depth)$";
            if (!Regex.IsMatch(alias, supportedAlias) ||
                !Regex.IsMatch(target, @"^result\.[a-z]+(?:\.[a-z]+)*(?:\[\d+\])?$"))
                throw new InvalidDataException($"MojoShader emitted an unsupported output alias '{output.Value}'.");
            if (outputAliases.ContainsValue(target))
                throw new InvalidDataException($"MojoShader emitted multiple aliases for output '{target}'.");
            outputAliases.Add(alias, target);
        }
        assembly = Regex.Replace(assembly, @"(?m)^OUTPUT\s+[A-Za-z_]\w*\s*=\s*[^;]+;\s*$", string.Empty);
        foreach ((string alias, string target) in outputAliases)
        {
            string escapedAlias = Regex.Escape(alias);
            bool readAsSource = Regex.IsMatch(assembly, $@"(?m)^[^#\r\n]*,[^\r\n]*\b{escapedAlias}\b");
            if (readAsSource)
            {
                if (stage != Iw3ShaderStage.Vertex)
                    throw new InvalidDataException($"MojoShader emitted an unsupported fragment output read from '{alias}'.");
                string temporary = AllocateTemporary();
                // Keep the output and its readable copy synchronized after each
                // write, including partial writes and writes inside control flow.
                assembly = Regex.Replace(
                    assembly,
                    $@"(?m)^([^#\r\n,]*\b{escapedAlias}\b[^,\r\n]*,[^\r\n]*)\r?$",
                    match =>
                    {
                        string instruction = match.Groups[1].Value.TrimEnd();
                        Match destination = Regex.Match(
                            instruction,
                            $@"^[A-Z][A-Z0-9_]*[ \t]+{escapedAlias}(\.[xyzw]+)?[ \t]*,");
                        if (!destination.Success || !instruction.EndsWith(';'))
                            throw new InvalidDataException($"MojoShader emitted an unsupported output write '{instruction}'.");
                        return $"{instruction}\nMOV {target}{destination.Groups[1].Value}, {temporary};";
                    });
                assembly = Regex.Replace(assembly, $@"\b{escapedAlias}\b", temporary);
            }
            else
            {
                assembly = Regex.Replace(assembly, $@"\b{escapedAlias}\b", target);
            }
        }

        assembly = Regex.Replace(
            assembly,
            @"(?m)^PARAM\s+c(\d+)\s*=\s*\{\s*([^}]*)\s*\};\s*$",
            match => $"#const c[{match.Groups[1].Value}] = {NormalizeLiteralVector(match.Groups[2].Value)}");
        assembly = Regex.Replace(
            assembly,
            @"(?m)^PARAM\s+c(\d+)\s*=\s*program\.local\[(\d+)\];\s*$",
            string.Empty);

        var integerAliases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match integer in Regex.Matches(
                     assembly,
                     @"(?m)^PARAM\s+(i\d+)\s*=\s*\{\s*([^}]*)\s*\};\s*$"))
        {
            string constant = AllocateConstant();
            integerAliases.Add(integer.Groups[1].Value, constant);
            assembly = assembly.Replace(
                integer.Value,
                $"#const {constant} = {NormalizeLiteralVector(integer.Groups[2].Value)}",
                StringComparison.Ordinal);
        }
        foreach ((string alias, string constant) in integerAliases)
            assembly = Regex.Replace(assembly, $@"\b{Regex.Escape(alias)}\b", constant);

        assembly = Regex.Replace(assembly, @"\bc(\d+)\b", "c[$1]");
        assembly = Regex.Replace(
            assembly,
            @"\{\s*(c\[\d+\])\.([xyzw])\s*,\s*\1\.([xyzw])\s*,\s*\1\.([xyzw])\s*,\s*\1\.([xyzw])\s*\}",
            match => $"{match.Groups[1].Value}.{match.Groups[2].Value}{match.Groups[3].Value}{match.Groups[4].Value}{match.Groups[5].Value}");
        var literalConstants = new Dictionary<string, string>(StringComparer.Ordinal);
        var literalDeclarations = new StringBuilder();
        string MaterializeLiteral(string values)
        {
            if (!literalConstants.TryGetValue(values, out string? constant))
            {
                constant = AllocateConstant();
                literalConstants.Add(values, constant);
                literalDeclarations.AppendLine($"#const {constant} = {values}");
            }
            return constant;
        }
        assembly = Regex.Replace(
            assembly,
            @",\s*\{([^{}]*)\}",
            match => $", {MaterializeLiteral(NormalizeLiteralVector(match.Groups[1].Value))}");
        if (Regex.IsMatch(assembly, @"(?m)^(?:ATTRIB|OUTPUT|PARAM|TEMP|FLOAT\s+TEMP|ADDRESS|ALIAS)\b") ||
            assembly.Contains('{') || assembly.Contains('}'))
        {
            throw new InvalidDataException(
                "MojoShader emitted a declaration or literal vector that the bundled RSX assembler cannot represent safely.");
        }

        assembly = Regex.Replace(
            assembly,
            @",\s*([+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?)\s*(?=[,;])",
            match =>
            {
                if (!float.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ||
                    !float.IsFinite(value))
                    throw new InvalidDataException($"MojoShader emitted an unsupported numeric source '{match.Value}'.");
                string literal = value.ToString("R", CultureInfo.InvariantCulture);
                return $", {MaterializeLiteral($"{literal} {literal} {literal} {literal}")}.x";
            });
        assembly = assembly.Insert(assembly.IndexOf('\n') + 1, literalDeclarations.ToString());

        if (Regex.IsMatch(assembly, @"\b(?:(?:c|i|v|r|t|o)\d+|o(?:C|D|T)\d+|oPos|oFog|oPts|oDepth)\b"))
        {
            throw new InvalidDataException(
                "MojoShader emitted a register alias that the bundled RSX assembler cannot represent safely.");
        }

        // The bundled parser does not skip empty lines after its program header.
        return Regex.Replace(assembly, @"(?m)^\s*\r?\n", string.Empty);
    }

    private static string NormalizeLiteralVector(string values)
    {
        string[] components = values.Split(',', StringSplitOptions.TrimEntries);
        if (components.Length != 4)
        {
            throw new InvalidDataException($"MojoShader emitted an unsupported literal vector '{{ {values} }}'.");
        }
        for (int index = 0; index < components.Length; index++)
        {
            if (!float.TryParse(components[index], NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ||
                !float.IsFinite(value))
            {
                throw new InvalidDataException($"MojoShader emitted an unsupported literal vector '{{ {values} }}'.");
            }
            components[index] = value.ToString("R", CultureInfo.InvariantCulture);
        }
        return string.Join(' ', components);
    }

    private static void Assemble(string assemblyPath, string binaryPath, Iw3ShaderStage stage, string sourcePath)
    {
        string tool = FindNativeTool("mapconverter-cgcomp");
        string flag = stage == Iw3ShaderStage.Vertex ? "-v" : "-f";
        if (!Run(tool, [flag, "-a", assemblyPath, binaryPath], out string error))
        {
            throw new InvalidDataException(
                $"Shader '{sourcePath}' could not be assembled for RSX. {error}");
        }
    }

    private static bool Run(string executable, IReadOnlyList<string> arguments, out string error)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            }
        };
        foreach (string argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        string standardError = process.StandardError.ReadToEnd();
        string standardOutput = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        error = string.Join(Environment.NewLine, new[] { standardError, standardOutput }
            .Where(value => !string.IsNullOrWhiteSpace(value))).Trim();
        if (process.ExitCode != 0 && string.IsNullOrEmpty(error))
            error = $"The local shader tool exited with code {process.ExitCode}.";
        return process.ExitCode == 0;
    }

    private static string FindNativeTool(string name)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Native", name);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"The local MapConverter shader tool '{name}' is missing at '{path}'. Build MapConverter before running it.");
        }
        return path;
    }

    private static byte[] ExtractSingleDirect3DProgram(ReadOnlySpan<byte> source, string path)
    {
        if (source.Length < 8 || (source.Length & 3) != 0)
            throw new InvalidDataException($"Shader '{path}' is not dword-aligned Direct3D bytecode.");

        uint version = ReadLittleEndian(source, 0);
        uint stage = version >> 16;
        int major = (int)((version >> 8) & 0xff);
        if (stage is not (0xfffeu or 0xffffu) || major is < 1 or > 3)
        {
            throw new InvalidDataException(
                $"Shader '{path}' has unsupported Direct3D shader model token 0x{version:X8}; supported inputs are SM1/SM2/SM3.");
        }

        int endOffset = FindDirect3DEnd(source, path);
        return source[..endOffset].ToArray();
    }

    private static int FindDirect3DEnd(
        ReadOnlySpan<byte> source,
        string path)
    {
        for (int offset = 4; offset + 4 <= source.Length;)
        {
            uint token = ReadLittleEndian(source, offset);
            ushort opcode = unchecked((ushort)token);
            if (opcode == 0xffff)
                return checked(offset + 4);

            if (opcode == 0xfffe)
            {
                int payloadWords = checked((int)(token >> 16));
                int next = checked(offset + 4 + payloadWords * 4);
                if (next > source.Length)
                    break;
                offset = next;
                continue;
            }

            // The recovered source program ends at its first aligned END.
            // SM1 has no instruction-length field, while the known SM2/SM3
            // containers append opaque data after END; attempting to follow
            // their token lengths would treat this non-program data as code.
            // CTAB comments are explicitly skipped so their metadata cannot
            // provide a false terminator.
            offset += 4;
        }
        throw new InvalidDataException($"Shader '{path}' has no safely delimited Direct3D END token.");
    }

    private static void ValidateDirect3DStage(
        ReadOnlySpan<byte> program,
        Iw3ShaderStage expected,
        string path)
    {
        uint token = ReadLittleEndian(program, 0);
        bool vertex = token >> 16 == 0xfffe;
        if (vertex != (expected == Iw3ShaderStage.Vertex))
        {
            throw new InvalidDataException(
                $"Shader '{path}' has a Direct3D {(vertex ? "vertex" : "pixel")} stage token but is referenced as {expected}.");
        }
    }

    private static IReadOnlyList<Direct3DConstant> ReadConstantTable(ReadOnlySpan<byte> data, string path)
    {
        for (int offset = 4; offset + 8 <= data.Length; offset += 4)
        {
            uint token = ReadLittleEndian(data, offset);
            if ((token & 0xffff) != 0xfffe)
                continue;
            int byteLength = checked((int)(token >> 16) * 4);
            int payload = offset + 4;
            if (byteLength < 28 || payload + byteLength > data.Length ||
                !data.Slice(payload, 4).SequenceEqual("CTAB"u8))
            {
                continue;
            }
            return ParseConstantTable(data.Slice(payload, byteLength), path);
        }
        return [];
    }

    private static IReadOnlyList<Direct3DConstant> ParseConstantTable(ReadOnlySpan<byte> table, string path)
    {
        const int ctabSignatureSize = 4;
        if (ReadLittleEndian(table, 4) != 28)
            throw new InvalidDataException($"Shader '{path}' has an unsupported CTAB header.");
        int count = checked((int)ReadLittleEndian(table, 16));
        if (count == 0)
            return [];
        int infoOffset = checked(ctabSignatureSize + (int)ReadLittleEndian(table, 20));
        if (count > 256 || infoOffset < 28 || infoOffset + checked(count * 20) > table.Length)
            throw new InvalidDataException($"Shader '{path}' has an invalid CTAB constant table.");

        var result = new List<Direct3DConstant>(count);
        for (int index = 0; index < count; index++)
        {
            int entry = infoOffset + index * 20;
            string name = ReadCString(table, checked(ctabSignatureSize + (int)ReadLittleEndian(table, entry)), path);
            ushort registerSet = ReadLittleEndian16(table, entry + 4);
            ushort registerIndex = ReadLittleEndian16(table, entry + 6);
            ushort registerCount = ReadLittleEndian16(table, entry + 8);
            int typeOffset = checked(ctabSignatureSize + (int)ReadLittleEndian(table, entry + 12));
            if (registerCount == 0 || typeOffset < 0 || typeOffset + 16 > table.Length)
                throw new InvalidDataException($"Shader '{path}' has an invalid CTAB constant '{name}'.");
            ushort parameterClass = ReadLittleEndian16(table, typeOffset);
            ushort type = ReadLittleEndian16(table, typeOffset + 2);
            ushort rows = ReadLittleEndian16(table, typeOffset + 4);
            ushort columns = ReadLittleEndian16(table, typeOffset + 6);
            ushort elements = ReadLittleEndian16(table, typeOffset + 8);
            result.Add(new Direct3DConstant(
                name, registerSet, registerIndex, registerCount, type,
                parameterClass, rows, columns, elements));
        }
        return result;
    }

    private static void ValidateSourceArguments(
        Iw3ShaderSource source,
        IReadOnlyList<Direct3DConstant> constants,
        string path)
    {
        foreach (Iw3ShaderArgumentSource argument in source.Arguments)
        {
            if (constants.Any(constant => string.Equals(constant.Name, argument.Destination.Name, StringComparison.Ordinal)))
                continue;
            throw new InvalidDataException(
                $"Shader '{path}' has no CTAB destination named '{argument.Destination.Name}'. Donor-free lowering will not guess shader argument registers.");
        }
    }

    private static RsxProgram ReadRsxProgram(ReadOnlySpan<byte> data, Iw3ShaderStage stage, string path)
    {
        bool vertex = stage == Iw3ShaderStage.Vertex;
        int headerSize = vertex ? RsxVertexHeaderSize : RsxFragmentHeaderSize;
        if (data.Length < headerSize)
            throw new InvalidDataException($"RSX compiler output for '{path}' is truncated.");
        string magic = Encoding.ASCII.GetString(data[..2]);
        if (magic != (vertex ? "VP" : "FP"))
            throw new InvalidDataException($"RSX compiler output for '{path}' has stage '{magic}', expected {(vertex ? "VP" : "FP")}.");
        int instructionCount = ReadBigEndian16(data, 10);
        int uploadOffset = checked((int)ReadBigEndian(data, 20));
        int uploadLength = checked(instructionCount * 16);
        if (instructionCount == 0 || uploadOffset < headerSize || uploadOffset + uploadLength > data.Length)
            throw new InvalidDataException($"RSX compiler output for '{path}' has an invalid instruction payload.");
        byte[] upload = data.Slice(uploadOffset, uploadLength).ToArray();

        if (vertex)
        {
            const int constantSize = 28;
            int constantCount = ReadBigEndian16(data, 8);
            uint constantOffsetValue = ReadBigEndian(data, 16);
            if (constantOffsetValue < RsxVertexHeaderSize ||
                (ulong)constantOffsetValue + (uint)constantCount * constantSize > (ulong)uploadOffset)
            {
                throw new InvalidDataException($"RSX compiler output for '{path}' has an invalid vertex constant table.");
            }

            var vertexConstants = new List<(uint Register, byte[] Value)>(constantCount);
            var registers = new HashSet<uint>();
            for (int index = 0; index < constantCount; index++)
            {
                int entry = checked((int)constantOffsetValue + index * constantSize);
                uint register = ReadBigEndian(data, entry + 4);
                // #const entries are internal float4 values, serialized by cgcomp
                // in the same big-endian form as Cg parameter defaults.
                if (data[entry + 8] != 9 || data[entry + 9] != 1 ||
                    data[entry + 10] != 1 || !registers.Add(register))
                {
                    throw new InvalidDataException($"RSX compiler output for '{path}' has an unsupported vertex constant entry.");
                }
                for (int component = 0; component < 4; component++)
                {
                    float value = BitConverter.UInt32BitsToSingle(ReadBigEndian(data, entry + 12 + component * 4));
                    if (!float.IsFinite(value))
                        throw new InvalidDataException($"RSX compiler output for '{path}' has a non-finite vertex constant.");
                }
                vertexConstants.Add((register, data.Slice(entry + 12, 16).ToArray()));
            }

            return new RsxProgram(
                instructionCount,
                ReadBigEndian16(data, 4),
                ReadBigEndian(data, 24),
                ReadBigEndian(data, 28),
                0,
                0,
                [],
                vertexConstants,
                upload);
        }

        ushort texCoords = ReadBigEndian16(data, 28);
        ushort texCoords2D = ReadBigEndian16(data, 30);
        ushort texCoords3D = ReadBigEndian16(data, 32);
        if (((texCoords2D | texCoords3D) & ~texCoords) != 0 ||
            (texCoords2D & texCoords3D) != 0)
        {
            throw new InvalidDataException(
                $"RSX compiler output for '{path}' has contradictory texture-coordinate metadata.");
        }
        if ((texCoords2D | texCoords3D) != 0)
        {
            throw new InvalidDataException(
                $"RSX compiler output for '{path}' contains texture-coordinate annotations without an established PS3 Cg control mapping.");
        }

        uint relocationCountValue = ReadBigEndian(data, 40);
        uint relocationOffsetValue = ReadBigEndian(data, 44);
        if (relocationCountValue > int.MaxValue || relocationOffsetValue > int.MaxValue)
            throw new InvalidDataException($"RSX compiler output for '{path}' has an invalid relocation table.");
        int relocationCount = (int)relocationCountValue;
        int relocationOffset = (int)relocationOffsetValue;
        if (relocationCount > data.Length / 8)
            throw new InvalidDataException($"RSX compiler output for '{path}' has an invalid relocation table.");
        int relocationBytes = relocationCount * 8;
        if (relocationOffset < RsxFragmentHeaderSize ||
            relocationOffset > data.Length - relocationBytes)
        {
            throw new InvalidDataException($"RSX compiler output for '{path}' has an invalid relocation table.");
        }

        var relocations = new RsxFragmentRelocation[relocationCount];
        for (int index = 0; index < relocations.Length; index++)
        {
            int entry = relocationOffset + index * 8;
            uint constantRegister = ReadBigEndian(data, entry);
            uint constantOffset = ReadBigEndian(data, entry + 4);
            if (constantOffset > ushort.MaxValue ||
                (constantOffset & 0xf) != 0 ||
                (ulong)constantOffset + 16 > (ulong)uploadLength)
            {
                throw new InvalidDataException(
                    $"RSX compiler output for '{path}' has an invalid fragment constant relocation.");
            }
            relocations[index] = new RsxFragmentRelocation(constantRegister, constantOffset);
        }

        return new RsxProgram(
            instructionCount,
            ReadBigEndian16(data, 4),
            ReadBigEndian(data, 36),
            0,
            ReadBigEndian(data, 24),
            texCoords,
            relocations,
            [],
            upload);
    }

    private static byte[] BuildCgProgram(
        Iw3ShaderStage stage,
        IReadOnlyList<Direct3DConstant> constants,
        RsxProgram rsx,
        string path)
    {
        if (rsx.Upload.Length > ushort.MaxValue)
            throw new InvalidDataException($"Shader '{path}' exceeds IW4's supported RSX program size.");

        IReadOnlyList<uint>[] patchLists = BuildFragmentPatchLists(stage, constants, rsx, path);
        foreach ((uint register, _) in rsx.VertexConstants)
        {
            if (constants.Any(constant => constant.RegisterSet == 2 &&
                    register >= constant.RegisterIndex &&
                    register < (uint)constant.RegisterIndex + constant.RegisterCount))
            {
                throw new InvalidDataException($"Shader '{path}' assigns vertex constant c{register} to both a literal and a CTAB parameter.");
            }
        }
        int parameterCount = checked(constants.Count + rsx.VertexConstants.Count);
        int parameterOffset = CgHeaderSize;
        int cursor = checked(parameterOffset + parameterCount * CgParameterSize);
        var patchOffsets = new int[constants.Count];
        for (int index = 0; index < patchLists.Length; index++)
        {
            if (patchLists[index].Count == 0)
                continue;
            patchOffsets[index] = cursor;
            cursor = checked(cursor + sizeof(uint) + patchLists[index].Count * sizeof(uint));
        }

        int vertexDefaultsOffset = cursor;
        cursor = checked(cursor + rsx.VertexConstants.Count * 16);
        var names = new int[parameterCount];
        foreach ((Direct3DConstant constant, int index) in constants.Select((value, index) => (value, index)))
        {
            names[index] = cursor;
            cursor = checked(cursor + Encoding.ASCII.GetByteCount(constant.Name) + 1);
        }
        for (int index = 0; index < rsx.VertexConstants.Count; index++)
        {
            names[constants.Count + index] = cursor;
            string name = $"c[{rsx.VertexConstants[index].Register}]";
            cursor = checked(cursor + Encoding.ASCII.GetByteCount(name) + 1);
        }
        cursor = Align(cursor, 16);
        int descriptorOffset = cursor;
        cursor = checked(cursor + CgDescriptorSize);
        int uploadOffset = Align(cursor, 16);
        int totalSize = checked(uploadOffset + rsx.Upload.Length);
        var data = new byte[totalSize];
        WriteBigEndian(data, 0, stage == Iw3ShaderStage.Vertex ? CgVertexProfile : CgPixelProfile);
        WriteBigEndian(data, 4, 6);
        WriteBigEndian(data, 8, checked((uint)totalSize));
        WriteBigEndian(data, 12, checked((uint)parameterCount));
        WriteBigEndian(data, 16, checked((uint)parameterOffset));
        WriteBigEndian(data, 20, checked((uint)descriptorOffset));
        WriteBigEndian(data, 24, checked((uint)rsx.Upload.Length));
        WriteBigEndian(data, 28, checked((uint)uploadOffset));

        for (int index = 0; index < constants.Count; index++)
        {
            Direct3DConstant constant = constants[index];
            int entry = parameterOffset + index * CgParameterSize;
            bool sampler = constant.RegisterSet == 3;
            if (constant.RegisterSet is not (2 or 3))
                throw new InvalidDataException($"Shader '{path}' uses unsupported Direct3D CTAB register set {constant.RegisterSet} for '{constant.Name}'.");
            WriteBigEndian(data, entry, GetCgParameterType(constant, path));
            WriteBigEndian(data, entry + 4, sampler
                ? checked(CgTextureUnitBase + constant.RegisterIndex)
                : CgConstantRegister);
            WriteBigEndian(data, entry + 8, CgUniformVariability);
            WriteBigEndian(data, entry + 12, sampler ? uint.MaxValue : constant.RegisterIndex);
            WriteBigEndian(data, entry + 16, checked((uint)names[index]));
            WriteBigEndian(data, entry + 24, checked((uint)patchOffsets[index]));
            WriteBigEndian(data, entry + 32, CgInDirection);
            WriteBigEndian(data, entry + 36, uint.MaxValue);
            WriteBigEndian(data, entry + 40, 1);
            WriteBigEndian(data, entry + 44, sampler ? 0u : 1u);
            Encoding.ASCII.GetBytes(constant.Name).CopyTo(data, names[index]);

            if (patchOffsets[index] == 0)
                continue;
            WriteBigEndian(data, patchOffsets[index], checked((uint)patchLists[index].Count));
            for (int patchIndex = 0; patchIndex < patchLists[index].Count; patchIndex++)
            {
                WriteBigEndian(
                    data,
                    patchOffsets[index] + sizeof(uint) + patchIndex * sizeof(uint),
                    patchLists[index][patchIndex]);
            }
        }

        // Append compiler-owned constants so existing CTAB parameter ordinals and
        // technique argument bindings retain their identity. The linker and PS3
        // vertex-program loader consume these Cg default values.
        for (int index = 0; index < rsx.VertexConstants.Count; index++)
        {
            (uint register, byte[] value) = rsx.VertexConstants[index];
            int ordinal = constants.Count + index;
            int entry = parameterOffset + ordinal * CgParameterSize;
            int valueOffset = vertexDefaultsOffset + index * 16;
            WriteBigEndian(data, entry, CgFloat4);
            WriteBigEndian(data, entry + 4, CgConstantRegister);
            WriteBigEndian(data, entry + 8, CgConstantVariability);
            WriteBigEndian(data, entry + 12, register);
            WriteBigEndian(data, entry + 16, checked((uint)names[ordinal]));
            WriteBigEndian(data, entry + 20, checked((uint)valueOffset));
            WriteBigEndian(data, entry + 32, CgInDirection);
            WriteBigEndian(data, entry + 36, uint.MaxValue);
            WriteBigEndian(data, entry + 40, 1);
            value.CopyTo(data, valueOffset);
            Encoding.ASCII.GetBytes($"c[{register}]").CopyTo(data, names[ordinal]);
        }

        WriteBigEndian(data, descriptorOffset, checked((uint)rsx.InstructionCount));
        if (stage == Iw3ShaderStage.Vertex)
        {
            WriteBigEndian(data, descriptorOffset + 4, 0);
            WriteBigEndian(data, descriptorOffset + 8, checked((uint)rsx.RegisterCount));
            WriteBigEndian(data, descriptorOffset + 12, rsx.InputMask);
            WriteBigEndian(data, descriptorOffset + 16, rsx.OutputMask);
        }
        else
        {
            WriteBigEndian(data, descriptorOffset + 4, rsx.InputMask);
            WriteBigEndian(data, descriptorOffset + 8, 0);
            WriteBigEndian16(data, descriptorOffset + 0x0c, rsx.TexCoords);
            // Native PS3 Cg shaders keep these controls clear for used inputs,
            // including float2 inputs. Declaration width is not a control bit.
            WriteBigEndian16(
                data,
                descriptorOffset + 0x0e,
                unchecked((ushort)~rsx.TexCoords));
            WriteBigEndian16(data, descriptorOffset + 0x10, 0);
            data[descriptorOffset + 0x12] = checked((byte)rsx.RegisterCount);
            data[descriptorOffset + 0x13] = (rsx.FragmentControl & 0x40) == 0 ? (byte)1 : (byte)0;
            data[descriptorOffset + 0x14] = (rsx.FragmentControl & 0x0e) != 0 ? (byte)1 : (byte)0;
            data[descriptorOffset + 0x15] = (rsx.FragmentControl & 0x80) != 0 ? (byte)1 : (byte)0;
        }
        rsx.Upload.CopyTo(data, uploadOffset);
        return data;
    }

    private static uint GetCgParameterType(Direct3DConstant constant, string path)
    {
        if (constant.RegisterSet == 2)
        {
            if (constant.Type == Direct3DFloat)
                return CgFloat4;
        }
        else if (constant.RegisterSet == 3 && constant.RegisterCount == 1)
        {
            return constant.Type switch
            {
                Direct3DSampler1D => CgSampler1D,
                Direct3DSampler2D => CgSampler2D,
                Direct3DSampler3D => CgSampler3D,
                Direct3DSamplerCube => CgSamplerCube,
                _ => throw new InvalidDataException(
                    $"Shader '{path}' uses unsupported Direct3D sampler type {constant.Type} " +
                    $"for '{constant.Name}'.")
            };
        }

        throw new InvalidDataException(
            $"Shader '{path}' uses unsupported Direct3D CTAB type {constant.Type} " +
            $"for '{constant.Name}'.");
    }

    private static IReadOnlyList<uint>[] BuildFragmentPatchLists(
        Iw3ShaderStage stage,
        IReadOnlyList<Direct3DConstant> constants,
        RsxProgram rsx,
        string path)
    {
        var result = new IReadOnlyList<uint>[constants.Count];
        var offsets = new List<uint>[constants.Count];
        for (int index = 0; index < constants.Count; index++)
        {
            offsets[index] = [];
            result[index] = offsets[index];
        }

        if (stage != Iw3ShaderStage.Pixel)
        {
            if (rsx.FragmentRelocations.Count != 0)
                throw new InvalidDataException($"Vertex shader '{path}' unexpectedly contains fragment relocations.");
            return result;
        }

        foreach (Direct3DConstant constant in constants)
        {
            if (constant.RegisterSet == 2 && constant.RegisterCount != 1)
            {
                throw new InvalidDataException(
                    $"Shader '{path}' pixel constant '{constant.Name}' spans {constant.RegisterCount} registers; " +
                    "the target Cg array-parameter encoding is not established.");
            }
        }

        var registerByOffset = new Dictionary<uint, uint>();
        foreach (RsxFragmentRelocation relocation in rsx.FragmentRelocations)
        {
            if (registerByOffset.TryGetValue(
                    relocation.UploadOffset,
                    out uint existingRegister))
            {
                if (existingRegister != relocation.RegisterIndex)
                {
                    throw new InvalidDataException(
                        $"Shader '{path}' assigns fragment constant slot 0x{relocation.UploadOffset:X} " +
                        $"to both c{existingRegister} and c{relocation.RegisterIndex}.");
                }
                continue;
            }
            registerByOffset.Add(relocation.UploadOffset, relocation.RegisterIndex);

            int owner = -1;
            for (int index = 0; index < constants.Count; index++)
            {
                Direct3DConstant constant = constants[index];
                if (constant.RegisterSet != 2)
                    continue;
                uint first = constant.RegisterIndex;
                uint end = checked(first + constant.RegisterCount);
                if (relocation.RegisterIndex < first || relocation.RegisterIndex >= end)
                    continue;
                if (owner >= 0)
                {
                    throw new InvalidDataException(
                        $"Shader '{path}' has overlapping CTAB registers for fragment constant c{relocation.RegisterIndex}.");
                }
                owner = index;
            }

            if (owner < 0)
            {
                throw new InvalidDataException(
                    $"Shader '{path}' uses fragment constant c{relocation.RegisterIndex} without CTAB metadata.");
            }
            offsets[owner].Add(relocation.UploadOffset);
        }

        for (int index = 0; index < constants.Count; index++)
        {
            if (constants[index].RegisterSet == 2 && offsets[index].Count == 0)
            {
                throw new InvalidDataException(
                    $"Shader '{path}' has no RSX patch location for pixel constant " +
                    $"'{constants[index].Name}'.");
            }
        }

        return result;
    }

    private static Iw3ShaderParameter CreateParameter(Direct3DConstant value) => new(
        value.Name,
        value.RegisterSet == 3 ? CgTextureUnitBase + value.RegisterIndex : CgConstantRegister,
        value.RegisterSet == 3 ? uint.MaxValue : value.RegisterIndex,
        value.RegisterCount,
        value.RegisterSet == 3 ? Iw3ShaderParameterKind.Sampler : Iw3ShaderParameterKind.Constant,
        true,
        value.Class,
        value.Rows,
        value.Columns,
        value.Elements);

    private static string ReadCString(ReadOnlySpan<byte> source, int offset, string path)
    {
        if (offset < 0 || offset >= source.Length)
            throw new InvalidDataException($"Shader '{path}' contains an out-of-range CTAB string.");
        int length = source[offset..].IndexOf((byte)0);
        if (length <= 0)
            throw new InvalidDataException($"Shader '{path}' contains an invalid CTAB string.");
        return Encoding.ASCII.GetString(source.Slice(offset, length));
    }

    private static int Align(int value, int alignment) => checked((value + alignment - 1) & -alignment);
    private static uint ReadLittleEndian(ReadOnlySpan<byte> value, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(value.Slice(offset, 4));
    private static ushort ReadLittleEndian16(ReadOnlySpan<byte> value, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(value.Slice(offset, 2));
    private static uint ReadBigEndian(ReadOnlySpan<byte> value, int offset) => BinaryPrimitives.ReadUInt32BigEndian(value.Slice(offset, 4));
    private static ushort ReadBigEndian16(ReadOnlySpan<byte> value, int offset) => BinaryPrimitives.ReadUInt16BigEndian(value.Slice(offset, 2));
    private static void WriteBigEndian(Span<byte> value, int offset, uint integer) => BinaryPrimitives.WriteUInt32BigEndian(value.Slice(offset, 4), integer);
    private static void WriteBigEndian16(Span<byte> value, int offset, ushort integer) => BinaryPrimitives.WriteUInt16BigEndian(value.Slice(offset, 2), integer);

    private sealed record Direct3DConstant(
        string Name,
        ushort RegisterSet,
        ushort RegisterIndex,
        ushort RegisterCount,
        ushort Type,
        ushort Class,
        ushort Rows,
        ushort Columns,
        ushort Elements);
    private sealed record RsxFragmentRelocation(uint RegisterIndex, uint UploadOffset);
    private sealed record RsxProgram(
        int InstructionCount,
        int RegisterCount,
        uint InputMask,
        uint OutputMask,
        uint FragmentControl,
        ushort TexCoords,
        IReadOnlyList<RsxFragmentRelocation> FragmentRelocations,
        IReadOnlyList<(uint Register, byte[] Value)> VertexConstants,
        byte[] Upload);
}

internal sealed record Iw3ShaderCompilation(
    MaterialShaderAsset Asset,
    IReadOnlyList<Iw3ShaderParameter> Parameters,
    string CompilerIdentity);

internal enum Iw3ShaderParameterKind
{
    Constant,
    Sampler,
}

internal sealed record Iw3ShaderParameter(
    string Name,
    uint Resource,
    uint ResourceIndex,
    uint RegisterCount,
    Iw3ShaderParameterKind Kind,
    bool IsReferenced,
    ushort Class,
    ushort Rows,
    ushort Columns,
    ushort Elements);
