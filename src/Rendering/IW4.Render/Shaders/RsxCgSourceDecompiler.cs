using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace IW4.Render.Shaders;

public sealed record RsxCgSource(
    string Source,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<string> Parameters)
{
    public string? PrimaryOutput { get; init; }
}

/// <summary>Recovers register-level Cg from an IW4 PS3 Cg program.</summary>
public static partial class RsxCgSourceDecompiler
{
    public const string RecoveryDirective = "#error Unrecovered RSX";

    public static RsxCgSource Decompile(byte[] program, bool isVertex)
    {
        ArgumentNullException.ThrowIfNull(program);
        string profile = isVertex ? "sce_vp_rsx" : "sce_fp_rsx";
        var source = new SourceWriter(profile);
        int offset = RsxProgramDecoder.ShaderUploadOffset(program);
        if (offset < 0 || ReadWord(program, 0) != (isVertex ? 0x1b5bu : 0x1b5cu) || ReadWord(program, 4) != 6 ||
            !RsxShaderTranslator.TryReadRuntimeInfo(program, out RuntimeInfo layout) ||
            ReadWord(program, 8) > program.Length ||
            (ulong)offset + layout.UploadSize > ReadWord(program, 8) ||
            (ulong)layout.ParameterTableOffset + layout.ParameterCount * 0x30UL > ReadWord(program, 8) ||
            (ulong)ReadWord(program, 0x14) + 0x16UL > ReadWord(program, 8) ||
            (layout.UploadSize & 15) != 0)
        {
            source.Diagnostics.Add("Invalid PS3 Cg header or instruction upload for the selected stage.");
            return source.Finish();
        }

        CgParameter[] parameters = ReadParameters(program, layout);
        source.Parameters = parameters.Select(parameter => DescribeParameter(program, layout, parameter)).ToArray();
        source.SetReflectedNames(parameters);
        if (isVertex)
            WriteVertex(program, offset, parameters, source);
        else
            WriteFragment(program, offset, layout, parameters, source);
        return source.Finish();
    }

    private static string DescribeParameter(byte[] program, RuntimeInfo layout, CgParameter parameter)
    {
        string name = parameter.Name.Length == 0 ? "(unnamed)" : parameter.Name;
        string semantic = parameter.Semantic.Length == 0 ? "" : $" : {parameter.Semantic}";
        string type = CgTypeName(parameter.Type) ?? $"type 0x{parameter.Type:X}";
        string register = parameter.Register == uint.MaxValue ? "-" : $"{parameter.Register}";
        string binding = $"resource 0x{parameter.Resource:X}, index {register}";
        string value = "";
        if (parameter.DefaultOffset != 0)
        {
            value = $", default @0x{parameter.DefaultOffset:X}";
            int width = NumericWidth(parameter.Type);
            if (width != 0 && parameter.DefaultOffset + (uint)width * 4UL <= ReadWord(program, 8))
            {
                var components = new string[width];
                for (int lane = 0; lane < width; lane++)
                {
                    float component = BitConverter.UInt32BitsToSingle(ReadWord(program, (int)parameter.DefaultOffset + lane * 4));
                    components[lane] = component.ToString("R", CultureInfo.InvariantCulture);
                }
                value += $" ({string.Join(", ", components)})";
            }
        }
        string patches = parameter.PatchOffset == 0 ? "" : $", patches @0x{parameter.PatchOffset:X}";
        if (parameter.PatchOffset != 0 &&
            RsxShaderTranslator.TryReadPatchEntry(program, layout, (ushort)parameter.Ordinal, out PatchEntry patch))
        {
            patches += $" ({patch.PatchOffsets.Count})";
        }
        string reference = parameter.Referenced ? "" : ", unreferenced";
        return $"[{parameter.Ordinal}] {type} {name}{semantic} ({binding}{value}{patches}{reference})";
    }

    private static int NumericWidth(uint type) => type switch
    {
        >= 0x401 and <= 0x404 => (int)(type - 0x400),
        >= 0x415 and <= 0x418 => (int)(type - 0x414),
        _ => 0
    };

    private static string? CgTypeName(uint type)
    {
        string? numeric = type switch
        {
            >= 0x401 and <= 0x414 => "half",
            >= 0x415 and <= 0x428 => "float",
            _ => null
        };
        if (numeric is not null)
        {
            int index = (int)(type - (numeric == "half" ? 0x401u : 0x415u));
            return index switch
            {
                0 => numeric,
                <= 3 => $"{numeric}{index + 1}",
                _ => $"{numeric}{(index - 4) / 4 + 1}x{(index - 4) % 4 + 1}"
            };
        }
        return type switch
        {
            0x429 => "sampler1D",
            0x42a => "sampler2D",
            0x42b => "sampler3D",
            0x42c => "samplerRECT",
            0x42d => "samplerCUBE",
            _ => null
        };
    }

    private static CgParameter[] ReadParameters(byte[] program, RuntimeInfo layout)
    {
        var parameters = new CgParameter[checked((int)layout.ParameterCount)];
        for (int i = 0; i < parameters.Length; i++)
        {
            int row = checked((int)layout.ParameterTableOffset + i * 0x30);
            parameters[i] = new CgParameter(
                i,
                ReadWord(program, row),
                ReadWord(program, row + 4),
                ReadWord(program, row + 8),
                ReadWord(program, row + 12),
                ReadName(program, ReadWord(program, row + 16)),
                ReadWord(program, row + 20),
                ReadWord(program, row + 24),
                ReadName(program, ReadWord(program, row + 28)),
                ReadWord(program, row + 32),
                ReadWord(program, row + 40) != 0);
        }

        return parameters;
    }

    private static string ReadName(byte[] program, uint offset)
    {
        if (offset == 0 || offset >= program.Length)
            return string.Empty;
        int size = (int)ReadWord(program, 8);
        if (offset >= size)
            return string.Empty;
        ReadOnlySpan<byte> tail = program.AsSpan((int)offset, size - (int)offset);
        int end = tail.IndexOf((byte)0);
        if (end is <= 0 or > 1024)
            return string.Empty;
        foreach (byte value in tail[..end])
        {
            if (value is < 0x20 or > 0x7e)
                return string.Empty;
        }

        return Encoding.ASCII.GetString(tail[..end]);
    }

    private static uint ReadWord(byte[] program, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(program.AsSpan(offset, 4));

    private static string Float4(float x, float y, float z, float w) =>
        $"float4({FloatLiteral(x)}, {FloatLiteral(y)}, {FloatLiteral(z)}, {FloatLiteral(w)})";

    private static string FloatLiteral(float value)
    {
        string literal = value.ToString("R", CultureInfo.InvariantCulture);
        return literal.Contains('.') || literal.Contains('E') ? literal : literal + ".0";
    }

    private static string SwizzleSuffix(string swizzle) => swizzle == "xyzw" ? "" : "." + swizzle;

    private static string Mask(int flags, bool vertex)
    {
        Span<char> mask = stackalloc char[4];
        int count = 0;
        for (int lane = 0; lane < 4; lane++)
        {
            if ((flags & (1 << (vertex ? 3 - lane : lane))) != 0)
                mask[count++] = "xyzw"[lane];
        }

        return mask[..count].ToString();
    }

    private static string Scalar(string vector) => $"({vector}).x";

    private static string Comparison(string left, string op, string right) =>
        $"float4(({left}) {op} ({right}))";

    private static string Condition(string value, RsxConditionTest test) => test switch
    {
        RsxConditionTest.False => "false",
        RsxConditionTest.LessThan => $"{value} < 0.0",
        RsxConditionTest.Equal => $"{value} == 0.0",
        RsxConditionTest.LessThanOrEqual => $"{value} <= 0.0",
        RsxConditionTest.GreaterThan => $"{value} > 0.0",
        RsxConditionTest.NotEqual => $"{value} != 0.0",
        RsxConditionTest.GreaterThanOrEqual => $"{value} >= 0.0",
        _ => "true"
    };

    private static string? Predicate(
        SourceWriter source, int index, string cc, RsxConditionTest test, string swizzle)
    {
        if (test == RsxConditionTest.True)
            return null;
        string predicate = source.Internal($"predicate{index}");
        IEnumerable<string> lanes = swizzle.Select(lane => Condition($"{cc}.{lane}", test));
        source.Body.AppendLine($"    bool4 {predicate} = bool4({string.Join(", ", lanes)});");
        return predicate;
    }

    private static void WriteMask(
        StringBuilder body, string destination, string value, string mask, string? predicate)
    {
        if (predicate is null)
        {
            string suffix = SwizzleSuffix(mask);
            body.AppendLine($"    {destination}{suffix} = ({value}){suffix};");
            return;
        }

        foreach (char lane in mask)
            body.AppendLine($"    if ({predicate}.{lane}) {destination}.{lane} = ({value}).{lane};");
    }

    private const uint ConstantRegister = 0x882;
    private const uint TextureUnitBase = 0x800;
    private const uint Varying = 0x1005;
    private const uint Uniform = 0x1006;
    private const uint In = 0x1001;
    private const uint Out = 0x1002;

    private readonly record struct CgParameter(
        int Ordinal, uint Type, uint Resource, uint Variability, uint Register,
        string Name, uint DefaultOffset, uint PatchOffset, string Semantic, uint Direction,
        bool Referenced);

    private sealed class SourceWriter(string profile)
    {
        private static readonly Regex ReflectedPath = new(
            @"\A[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*|\[[0-9]+\])*\z");
        private readonly Dictionary<(uint Direction, uint Resource), string> _varyingNames = new();
        private readonly HashSet<string> _reflectedNames = new(StringComparer.Ordinal);
        private readonly HashSet<string> _names = new(StringComparer.Ordinal);
        private readonly HashSet<string> _inlineDiagnostics = new(StringComparer.Ordinal);
        private readonly SortedDictionary<int, string> _uniforms = new();
        private readonly SortedDictionary<int, string> _inputs = new();
        private readonly SortedDictionary<int, string> _outputs = new();
        private readonly SortedDictionary<string, string> _locals = new(StringComparer.Ordinal);
        private readonly HashSet<string> _zeroInitialized = new(StringComparer.Ordinal);
        private string _prefix = "iw4_";
        private string? _primaryOutput;

        public List<string> Diagnostics { get; } = [];
        public IReadOnlyList<string> Parameters { get; set; } = [];
        public StringBuilder Body { get; } = new();

        public void SetReflectedNames(CgParameter[] parameters)
        {
            _reflectedNames.UnionWith(parameters.Select(parameter => parameter.Name));
            var uniformNames = parameters.Where(parameter => parameter.Variability == RsxCgSourceDecompiler.Uniform)
                .Select(parameter => parameter.Name)
                .ToHashSet(StringComparer.Ordinal);
            var varyings = parameters.Where(parameter => parameter.Referenced && parameter.Variability == Varying)
                .GroupBy(parameter => (parameter.Direction, parameter.Resource))
                .Where(binding => binding.Count() == 1)
                .Select(binding => binding.First())
                .Where(parameter => (parameter.Direction is In or Out) &&
                    (parameter.Type is >= 0x401 and <= 0x404 or >= 0x415 and <= 0x418) &&
                    parameter.Register == uint.MaxValue && ReflectedPath.IsMatch(parameter.Name))
                .Select(parameter => parameter with
                {
                    Name = parameter.Name.Replace('.', '_').Replace('[', '_').Replace("]", "")
                })
                .Where(parameter => IsSourceIdentifier(parameter.Name))
                .GroupBy(parameter => parameter.Name, StringComparer.Ordinal);

            foreach (var name in varyings)
            {
                if (name.Count() != 1 || uniformNames.Contains(name.Key))
                    continue;
                CgParameter parameter = name.First();
                _varyingNames.Add((parameter.Direction, parameter.Resource), name.Key);
                _reflectedNames.Add(name.Key);
            }

            while (_reflectedNames.Any(name => name.StartsWith(_prefix, StringComparison.Ordinal)))
                _prefix = "_" + _prefix;
        }

        public string Internal(string name) => _prefix + name;

        public string ReadableName(string stem)
        {
            if (!IsSourceIdentifier(stem) || stem.StartsWith(_prefix, StringComparison.Ordinal))
                stem = "value";
            string name = stem;
            for (int suffix = 2; _reflectedNames.Contains(name) || !IsSourceIdentifier(name) || !_names.Add(name); suffix++)
                name = stem + suffix.ToString(CultureInfo.InvariantCulture);
            return name;
        }

        public string Uniform(CgParameter parameter, string type, string? semantic = null)
        {
            string name = IsPlainIdentifier(parameter.Name) && _names.Add(parameter.Name)
                ? parameter.Name
                : Internal($"parameter{parameter.Ordinal}");
            string binding = semantic is null ? "" : $" : {semantic}";
            _uniforms.TryAdd(parameter.Ordinal, $"uniform {type} {name}{binding};");
            return name;
        }

        public string Input(int slot, string semantic, string type = "float4")
        {
            string name = VaryingName(In, slot, semantic) ?? Internal($"in_{semantic}");
            _inputs.TryAdd(slot, $"    {type} {name} : {semantic}");
            return name;
        }

        public string Output(int slot, string semantic, string type = "float4")
        {
            string name = VaryingName(Out, slot, semantic) ?? Internal($"out_{semantic}");
            if (semantic == (profile == "sce_vp_rsx" ? "POSITION" : "COLOR0"))
                _primaryOutput = name;
            _outputs.TryAdd(slot, $"    out {type} {name} : {semantic}");
            return name;
        }

        private string? VaryingName(uint direction, int slot, string semantic)
        {
            bool vertex = profile == "sce_vp_rsx";
            bool output = direction == Out;
            // Array reflection can repeat the base semantic for each element. The resource tracks the actual binding.
            uint? resource = semantic switch
            {
                "POSITION" when vertex && output => 0x8c3u,
                "COLOR0" or "COLOR1" when vertex && output =>
                    0x8c5u + (uint)(slot - (int)RsxVertexResult.FrontColor0),
                "COLOR0" or "COLOR1" or "COLOR2" or "COLOR3" when !vertex && output =>
                    0xac5u + (uint)slot,
                "COLOR0" or "COLOR1" when !vertex && !output =>
                    0xac5u + (uint)(slot - (int)RsxFragmentInputAttribute.Color0),
                "DEPTH" when !vertex && output => 0xb75u,
                _ when vertex && !output && semantic.StartsWith("ATTR", StringComparison.Ordinal) =>
                    0x841u + (uint)slot,
                _ when vertex == output && semantic.StartsWith("TEXCOORD", StringComparison.Ordinal) =>
                    0xc94u + (uint)(slot - (vertex ? (int)RsxVertexResult.TextureCoordinate0 :
                        (int)RsxFragmentInputAttribute.TextureCoordinate0)),
                _ => null
            };
            return resource is uint binding && _varyingNames.TryGetValue((direction, binding), out string? name)
                ? name
                : null;
        }

        public string Local(string register, string type = "float4", bool zeroInitialize = false)
        {
            string name = Internal(register);
            _locals.TryAdd(name, type);
            if (zeroInitialize) _zeroInitialized.Add(name);
            return name;
        }

        public string? Unsupported(int offset, string reason, params uint[] words)
        {
            string detail = $"0x{offset:X}: {reason}";
            if (_inlineDiagnostics.Add(detail))
            {
                Diagnostics.Add(detail);
                Body.AppendLine($"    {RecoveryDirective} at {detail}");
                Body.AppendLine($"    // {string.Join(" ", words.Select(word => word.ToString("X8")))}");
            }
            return null;
        }

        public RsxCgSource Finish()
        {
            var result = new StringBuilder();
            result.AppendLine($"// Reconstructed IW4 PS3 Cg ({profile}, entry main).");
            foreach (string diagnostic in Diagnostics.Except(_inlineDiagnostics))
                result.AppendLine($"{RecoveryDirective}: {diagnostic}");

            foreach (string uniform in _uniforms.Values)
                result.AppendLine(uniform);
            if (_uniforms.Count != 0)
                result.AppendLine();
            result.AppendLine("void main(");
            result.AppendLine(string.Join(",\n", _inputs.Values.Concat(_outputs.Values)));
            result.AppendLine(")");
            result.AppendLine("{");
            if (_zeroInitialized.Count != 0)
                result.AppendLine("    // Studio starts unwritten fragment register lanes at zero.");
            foreach ((string name, string type) in _locals)
            {
                string initializer = _zeroInitialized.Contains(name) ? " = 0.0" : "";
                result.AppendLine($"    {type} {name}{initializer};");
            }
            if (_locals.Count != 0 && Body.Length != 0)
                result.AppendLine();
            result.Append(Body);
            result.AppendLine("}");
            return new RsxCgSource(result.ToString(), Diagnostics.Distinct().ToArray(), Parameters)
            {
                PrimaryOutput = _primaryOutput
            };
        }
    }

    private static bool IsSourceIdentifier(string name) =>
        IsPlainIdentifier(name) && name is not (
            "asm_fragment" or "auto" or "catch" or "char" or "class" or "column_major" or "compile" or
            "compile_fragment" or "double" or "enum" or "extern" or "friend" or "goto" or "inline" or
            "interface" or "long" or "mutable" or "namespace" or "new" or "nointerpolation" or "operator" or
            "pass" or "precise" or "private" or "protected" or "public" or "register" or "row_major" or
            "samplerCUBE" or "samplerRECT" or "shared" or "short" or "signed" or "sizeof" or "technique" or
            "template" or "texture" or "this" or "throw" or "try" or "union" or "unsigned" or "using" or
            "varying" or "virtual" or "volatile" or "IW4_SHADERCONVERT_SM3" or
            "abs" or "cos" or "ddx" or "ddy" or "dot" or "exp2" or "floor" or "frac" or "length" or "log2" or
            "max" or "min" or "normalize" or "rsqrt" or "saturate" or "sign" or "sin" or "sqrt" or
            "tex1D" or "tex1Dbias" or "tex1Dlod" or "tex1Dproj" or
            "tex2D" or "tex2Dbias" or "tex2Dlod" or "tex2Dproj" or
            "tex3D" or "tex3Dbias" or "tex3Dlod" or "tex3Dproj" or
            "texCUBE" or "texCUBEbias" or "texCUBElod");

    private static bool IsPlainIdentifier(string name)
    {
        if (name.Length == 0 || char.IsAsciiDigit(name[0]) ||
            name.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
            return false;
        if (name is "main" or "if" or "else" or "for" or "while" or "do" or "switch" or
            "return" or "discard" or "break" or "continue" or "void" or "uniform" or
            "struct" or "typedef" or "in" or "out" or "inout" or "static" or "const" or
            "true" or "false" or "case" or "default" or "asm" or "packed")
            return false;
        foreach (string type in new[] { "float", "half", "fixed", "int", "uint", "bool", "sampler" })
        {
            if (name.StartsWith(type, StringComparison.Ordinal) &&
                (name.Length == type.Length || char.IsAsciiDigit(name[type.Length])))
                return false;
        }

        return true;
    }
}
