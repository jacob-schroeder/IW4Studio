using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using IW4.Game.Assets.TechniqueSet;
using MapConverter.Game.IW3.PC.Techniques;

namespace MapConverter.Game.IW3.PS3.Techniques;

internal sealed record Iw3Ps3TechniqueSetCompilation(
    MaterialTechniqueSetAsset TechniqueSet,
    IReadOnlyList<MaterialShaderAsset> Shaders);

internal sealed class Iw3Ps3TechniqueCompiler(
    Func<Iw3Ps3ShaderSource, Iw3Ps3ShaderSource> resolveShader,
    Func<Iw3Ps3TechniqueSetSource, Iw3Ps3TechniqueSetSource> resolveSet,
    string ownedNamePrefix = "")
{
    private readonly Func<Iw3Ps3ShaderSource, Iw3Ps3ShaderSource> _resolveShader =
        resolveShader ?? throw new ArgumentNullException(nameof(resolveShader));
    private readonly Func<Iw3Ps3TechniqueSetSource, Iw3Ps3TechniqueSetSource> _resolveSet =
        resolveSet ?? throw new ArgumentNullException(nameof(resolveSet));
    internal string OwnedNamePrefix { get; } = ownedNamePrefix ??
        throw new ArgumentNullException(nameof(ownedNamePrefix));
    internal bool DynamicShadowMaskDisabled { get; private set; }
    private readonly Dictionary<Iw3Ps3ShaderSource, MaterialShaderAsset> _shaders =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(MaterialShaderKind Kind, string Name), MaterialShaderAsset> _shadersByName = [];
    private readonly Dictionary<Iw3Ps3TechniqueSetSource, MaterialTechniqueSetAsset> _sets =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Iw3Ps3TechniqueSource, MaterialTechniqueAsset> _techniques =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Iw3Ps3PassSource, MaterialPassAsset> _passes =
        new(ReferenceEqualityComparer.Instance);

    internal Iw3Ps3TechniqueSetCompilation Compile(Iw3Ps3TechniqueSetSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Name.StartsWith(','))
        {
            Iw3Ps3TechniqueSetSource resolved = _resolveSet(source) ??
                throw new InvalidDataException($"IW3 PS3 technique set '{source.Name}' is unresolved.");
            if (resolved.Name.StartsWith(',') ||
                !string.Equals(resolved.Name, source.Name[1..], StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"IW3 PS3 technique set '{source.Name}' resolved to an incompatible definition.");
            source = resolved;
        }
        if (_sets.TryGetValue(source, out MaterialTechniqueSetAsset? cached))
            return new(cached, ShadersFor(cached));
        if (source.Root.Length != 112 || source.Slots.Count != 26 ||
            string.IsNullOrWhiteSpace(source.Name) || source.Name[0] == ',')
            throw new InvalidDataException(
                $"IW3 PS3 technique set {JsonSerializer.Serialize(source.Name)} requires an owned 112-byte root and 26 slots " +
                $"(root bytes {source.Root.Length}, slots {source.Slots.Count}).");
        if (!Enum.IsDefined((MaterialWorldVertexFormat)source.Root.Span[4]))
            throw new InvalidDataException($"IW3 PS3 technique set '{source.Name}' has an unknown world vertex format.");

        var slots = new MaterialTechniqueSlot[(int)MaterialTechniqueType.Count];
        for (int targetIndex = 0; targetIndex < slots.Length; targetIndex++)
        {
            Iw3TechniqueSlot? pcSlot = Iw3TechniqueSlotMapping.Iw4Slots[targetIndex];
            int? sourceIndex = pcSlot is null ? null : SourceSlotIndex(pcSlot.Value);
            Iw3Ps3TechniqueSource? technique = sourceIndex is { } index
                ? source.Slots[index]
                : null;
            slots[targetIndex] = new MaterialTechniqueSlot(
                (MaterialTechniqueType)targetIndex,
                default,
                technique is null ? null : CompileTechnique(technique));
        }
        var result = new MaterialTechniqueSetAsset
        {
            Name = OwnedNamePrefix + source.Name,
            WorldVertexFormat = (MaterialWorldVertexFormat)source.Root.Span[4],
            TechniqueSlots = Array.AsReadOnly(slots)
        };
        _sets.Add(source, result);
        return new(result, ShadersFor(result));
    }

    private static IReadOnlyList<MaterialShaderAsset> ShadersFor(MaterialTechniqueSetAsset source) =>
        Array.AsReadOnly(source.TechniqueSlots
            .SelectMany(slot => slot.Technique?.Passes ?? [])
            .SelectMany(pass => new[] { pass.VertexShader, pass.PixelShader })
            .OfType<MaterialShaderAsset>()
            .Distinct<MaterialShaderAsset>(ReferenceEqualityComparer.Instance)
            .ToArray());

    private static int? SourceSlotIndex(Iw3TechniqueSlot slot)
    {
        int index = (int)slot;
        // The 26 console slots omit PC instanced slots 14..20 and the final
        // instanced debug slot 33. All remaining slots keep semantic order.
        return index switch
        {
            <= 13 => index,
            >= 21 and <= 32 => index - 7,
            _ => null
        };
    }

    internal static int? SourceSlotIndexForTarget(int targetIndex)
    {
        if ((uint)targetIndex >= Iw3TechniqueSlotMapping.Iw4Slots.Count)
            throw new ArgumentOutOfRangeException(nameof(targetIndex));
        Iw3TechniqueSlot? slot = Iw3TechniqueSlotMapping.Iw4Slots[targetIndex];
        return slot is null ? null : SourceSlotIndex(slot.Value);
    }

    private MaterialTechniqueAsset CompileTechnique(Iw3Ps3TechniqueSource source)
    {
        if (_techniques.TryGetValue(source, out MaterialTechniqueAsset? cached))
            return cached;
        if (source.Root.Length != 8 || source.Passes.Count == 0 ||
            source.Passes.Count > ushort.MaxValue ||
            string.IsNullOrWhiteSpace(source.Name) || source.Name[0] == ',' ||
            BinaryPrimitives.ReadUInt16BigEndian(source.Root.Span[6..]) != source.Passes.Count)
            throw new InvalidDataException($"IW3 PS3 technique '{source.Name}' has an invalid root or pass count.");
        var passes = source.Passes.Select(CompilePass).ToArray();
        MaterialTechniqueFlags flags = MaterialTechniqueFlags.None;
        if (passes.Any(pass => pass.VertexDeclaration?.HasOptionalSource == true))
            flags |= MaterialTechniqueFlags.DeclarationHasOptionalSource;
        foreach (MaterialShaderArgumentAsset argument in passes.SelectMany(pass => pass.Args))
        {
            if (argument.Type == MaterialShaderArgumentType.CodePixelSampler)
            {
                flags |= argument.CodeTextureSource switch
                {
                    MaterialTextureSource.ResolvedPostSun => MaterialTechniqueFlags.NeedsResolvedPostSun,
                    MaterialTextureSource.ResolvedScene => MaterialTechniqueFlags.NeedsResolvedScene,
                    MaterialTextureSource.FloatZ or
                        MaterialTextureSource.ProcessedFloatZ or
                        MaterialTextureSource.RawFloatZ => MaterialTechniqueFlags.UsesFloatZ,
                    _ => MaterialTechniqueFlags.None
                };
            }
            else if ((argument.Type is MaterialShaderArgumentType.CodeVertexConst or
                MaterialShaderArgumentType.CodePixelConst) &&
                argument.CodeConstant.Source == MaterialConstantSource.LightSpotFactors)
            {
                flags |= MaterialTechniqueFlags.UsesLightSpotFactors;
            }
        }
        var result = new MaterialTechniqueAsset
        {
            Name = OwnedNamePrefix + source.Name,
            Flags = flags,
            PassCount = (ushort)passes.Length,
            Passes = Array.AsReadOnly(passes)
        };
        _techniques.Add(source, result);
        return result;
    }

    private MaterialPassAsset CompilePass(Iw3Ps3PassSource source)
    {
        if (_passes.TryGetValue(source, out MaterialPassAsset? cached))
            return cached;
        ReadOnlySpan<byte> root = source.Root.Span;
        if (root.Length != 24 || source.Arguments.Count != root[12] + root[13] + root[14])
            throw new InvalidDataException("An IW3 PS3 pass has an invalid root or argument count.");
        Iw3Ps3ShaderSource? vertexShader = ResolveShader(source.VertexShader);
        Iw3Ps3ShaderSource? pixelShader = ResolveShader(source.PixelShader);
        var arguments = new List<MaterialShaderArgumentAsset>(source.Arguments.Count);
        int perPrimitive = 0, perObject = 0, stable = 0;
        MaterialCustomSamplerFlags customSamplers = (MaterialCustomSamplerFlags)root[15];
        if ((customSamplers & ~(MaterialCustomSamplerFlags.ReflectionProbe |
            MaterialCustomSamplerFlags.PrimaryLightmap |
            MaterialCustomSamplerFlags.SecondaryLightmap)) != 0)
            throw new InvalidDataException("An IW3 PS3 pass uses an unsupported custom sampler mask.");
        for (int index = 0; index < source.Arguments.Count; index++)
        {
            MaterialShaderArgumentAsset? argument = CompileArgument(source.Arguments[index],
                vertexShader, pixelShader, out MaterialCustomSamplerFlags custom);
            customSamplers |= custom;
            if (argument is null) continue;
            arguments.Add(argument);
            if (index < root[12]) perPrimitive++;
            else if (index < root[12] + root[13]) perObject++;
            else stable++;
        }
        var result = new MaterialPassAsset
        {
            VertexDeclaration = CompileDeclaration(source.Declaration),
            VertexShader = vertexShader is null ? null : CompileShader(vertexShader),
            PixelShader = pixelShader is null ? null : CompileShader(pixelShader),
            PerPrimArgCount = checked((byte)perPrimitive),
            PerObjArgCount = checked((byte)perObject),
            StableArgCount = checked((byte)stable),
            CustomSamplerFlags = customSamplers,
            PrecompiledVertexShader = (MaterialPrecompiledVertexShader)root[16],
            Args = Array.AsReadOnly(arguments.ToArray())
        };
        _passes.Add(source, result);
        return result;
    }

    private Iw3Ps3ShaderSource? ResolveShader(Iw3Ps3ShaderSource? source)
    {
        if (source is null || !source.Name.StartsWith(','))
            return source;
        Iw3Ps3ShaderSource resolved = _resolveShader(source) ??
            throw new InvalidDataException($"IW3 PS3 shader '{source.Name}' is unresolved.");
        if (resolved.Name.StartsWith(',') || resolved.IsVertex != source.IsVertex ||
            !string.Equals(resolved.Name, source.Name[1..], StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"IW3 PS3 shader '{source.Name}' resolved to an incompatible definition.");
        }
        return resolved;
    }

    private static MaterialVertexDeclarationAsset? CompileDeclaration(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.IsEmpty) return null;
        ReadOnlySpan<byte> root = bytes.Span;
        if (root.Length != 34 || root[0] > 16 || root[0] > MaterialVertexDeclarationAsset.RoutingCount)
            throw new InvalidDataException("An IW3 PS3 vertex declaration exceeds the IW4 routing width.");
        var routes = new MaterialVertexStreamRouting[MaterialVertexDeclarationAsset.RoutingCount];
        var destinations = new HashSet<byte>();
        for (int index = 0; index < root[0]; index++)
        {
            byte source = root[2 + index * 2];
            byte destination = root[3 + index * 2];
            if (source >= (byte)MaterialStreamSource.Count ||
                !Enum.IsDefined((MaterialStreamDestination)destination) ||
                !destinations.Add(destination))
            {
                throw new InvalidDataException(
                    $"IW3 PS3 vertex route {index} has unsupported source {source} or destination {destination}.");
            }
            routes[index] = new MaterialVertexStreamRouting(
                (MaterialStreamSource)source,
                (MaterialStreamDestination)destination);
        }
        return new MaterialVertexDeclarationAsset
        {
            StreamCount = root[0],
            HasOptionalSourceRaw = root[1],
            Routing = Array.AsReadOnly(routes)
        };
    }

    private MaterialShaderArgumentAsset? CompileArgument(
        Iw3Ps3ArgumentSource source,
        Iw3Ps3ShaderSource? vertexShader,
        Iw3Ps3ShaderSource? pixelShader,
        out MaterialCustomSamplerFlags customSamplers)
    {
        customSamplers = MaterialCustomSamplerFlags.None;
        ReadOnlySpan<byte> root = source.Root.Span;
        if (root.Length != 8)
            throw new InvalidDataException("An IW3 PS3 shader argument must occupy eight bytes.");
        ushort rawType = BinaryPrimitives.ReadUInt16BigEndian(root);
        if (rawType > 7)
            throw new InvalidDataException($"Unsupported IW3 PS3 shader argument type {rawType}.");
        MaterialShaderArgumentType type = (MaterialShaderArgumentType)rawType;
        ushort destination = BinaryPrimitives.ReadUInt16BigEndian(root[2..]);
        int value = BinaryPrimitives.ReadInt32BigEndian(root[4..]);
        MaterialShaderLiteralConstant? literal = null;
        if (rawType is 1 or 7)
        {
            ReadOnlySpan<byte> values = source.Literal.Span;
            if (values.Length != 16)
                throw new InvalidDataException("An IW3 PS3 literal shader argument requires four floats.");
            literal = new MaterialShaderLiteralConstant(
                BinaryPrimitives.ReadSingleBigEndian(values),
                BinaryPrimitives.ReadSingleBigEndian(values[4..]),
                BinaryPrimitives.ReadSingleBigEndian(values[8..]),
                BinaryPrimitives.ReadSingleBigEndian(values[12..]));
            value = 0;
        }
        else if (!source.Literal.IsEmpty)
            throw new InvalidDataException("A nonliteral shader argument retains unexpected literal bytes.");
        if (rawType is 3 or 5)
        {
            Iw3Ps3ShaderSource? shader = rawType == 3 ? vertexShader : pixelShader;
            string parameter = ReadParameterName(shader, destination, rawType == 3);
            if (!Iw3TechniqueBindingFacts.TryGetConstant(parameter, out Iw3CodeConstantBinding binding))
                throw new InvalidDataException($"IW3 PS3 code constant '{parameter}' has no IW4 binding.");
            uint packed = unchecked((uint)value);
            byte rowCount = (byte)packed;
            if (rowCount == 0)
                throw new InvalidDataException($"IW3 PS3 code constant '{parameter}' has zero rows.");
            value = new MaterialCodeConstantArgument(
                binding.Source,
                (byte)(packed >> 8),
                rowCount).Raw;
        }
        else if (rawType == 4)
        {
            string parameter = ReadParameterName(pixelShader, destination, vertexConstant: false,
                sampler: true);
            // IW3's 2D dynamic-shadow visibility mask has no IW4 code-image
            // source. Its lit-sun shader multiplies the sampled visibility
            // into illumination, so white disables that source-only mask.
            if (value == 16 &&
                string.Equals(parameter, "dynamicShadowSampler", StringComparison.Ordinal))
            {
                value = (int)MaterialTextureSource.White;
                DynamicShadowMaskDisabled = true;
                return new MaterialShaderArgumentAsset(0, type, destination, value, literal);
            }
            if (!Iw3TechniqueBindingFacts.TryGetSampler(parameter, out Iw3CodeSamplerBinding binding))
                throw new InvalidDataException($"IW3 PS3 code sampler '{parameter}' has no IW4 binding.");
            if (binding.Frequency == MaterialUpdateFrequency.Custom)
            {
                customSamplers = binding.CustomFlags;
                return null;
            }
            value = unchecked((int)(uint)binding.Source);
        }
        return new MaterialShaderArgumentAsset(0, type, destination, value, literal);
    }

    private static string ReadParameterName(
        Iw3Ps3ShaderSource? shader,
        ushort destination,
        bool vertexConstant,
        bool sampler = false)
    {
        if (shader is null || shader.Name.StartsWith(','))
            throw new InvalidDataException("A code shader argument requires a resolved native Cg shader.");
        ReadOnlySpan<byte> program = shader.Program.Span;
        if (program.Length < 32)
            throw new InvalidDataException($"Shader '{shader.Name}' has no Cg parameter table.");
        uint count = BinaryPrimitives.ReadUInt32BigEndian(program[12..]);
        uint offset = BinaryPrimitives.ReadUInt32BigEndian(program[16..]);
        if ((ulong)offset + (ulong)count * 48 > (ulong)program.Length)
            throw new InvalidDataException($"Shader '{shader.Name}' has a truncated Cg parameter table.");
        int selected = -1;
        if (!vertexConstant && !sampler)
        {
            if (destination >= count)
                throw new InvalidDataException($"Shader '{shader.Name}' has no Cg parameter {destination}.");
            selected = destination;
        }
        else
        {
            uint expectedResource = sampler ? 0x800u + destination : 0x882u;
            var matching = new List<int>();
            for (int index = 0; index < count; index++)
            {
                ReadOnlySpan<byte> parameter = program.Slice((int)offset + index * 48, 48);
                if (BinaryPrimitives.ReadUInt32BigEndian(parameter[4..]) != expectedResource ||
                    (vertexConstant && BinaryPrimitives.ReadUInt32BigEndian(parameter[12..]) != destination) ||
                    BinaryPrimitives.ReadUInt32BigEndian(parameter[8..]) != 0x1006 ||
                    BinaryPrimitives.ReadUInt32BigEndian(parameter[32..]) != 0x1001 ||
                    BinaryPrimitives.ReadUInt32BigEndian(parameter[40..]) != 1)
                    continue;
                matching.Add(index);
            }
            if (matching.Count == 0)
                throw new InvalidDataException($"Shader '{shader.Name}' has no Cg binding {destination}.");
            if (matching.Count > 1)
            {
                var named = new List<int>();
                foreach (int index in matching)
                {
                    string name = CgParameterName(program, (int)offset + index * 48, shader.Name);
                    bool known = sampler
                        ? Iw3TechniqueBindingFacts.TryGetSampler(name, out _)
                        : Iw3TechniqueBindingFacts.TryGetConstant(name, out _);
                    if (known) named.Add(index);
                }
                matching = named;
            }
            if (matching.Count != 1)
                throw new InvalidDataException($"Shader '{shader.Name}' has ambiguous Cg binding {destination}.");
            selected = matching[0];
        }
        return CgParameterName(program, (int)offset + selected * 48, shader.Name);
    }

    private static string CgParameterName(ReadOnlySpan<byte> program, int entryOffset, string shaderName)
    {
        ReadOnlySpan<byte> entry = program.Slice(entryOffset, 48);
        uint nameOffset = BinaryPrimitives.ReadUInt32BigEndian(entry[16..]);
        if (nameOffset >= program.Length)
            throw new InvalidDataException($"Shader '{shaderName}' has an invalid Cg parameter name.");
        ReadOnlySpan<byte> nameBytes = program[(int)nameOffset..];
        int terminator = nameBytes.IndexOf((byte)0);
        if (terminator <= 0)
            throw new InvalidDataException($"Shader '{shaderName}' has an unterminated Cg parameter name.");
        return Encoding.ASCII.GetString(nameBytes[..terminator]);
    }

    internal MaterialShaderAsset CompileShader(Iw3Ps3ShaderSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Name.StartsWith(','))
            return CompileShader(ResolveShader(source)!);
        if (_shaders.TryGetValue(source, out MaterialShaderAsset? existing))
            return existing;

        string name = source.Name;
        if (string.IsNullOrWhiteSpace(name) || name.Contains('\0') || name == ",")
            throw new InvalidDataException("An IW3 PS3 shader must have a valid name.");
        MaterialShaderKind kind = source.IsVertex
            ? MaterialShaderKind.Vertex
            : MaterialShaderKind.Pixel;
        ReadOnlySpan<byte> root = source.Root.Span;
        int rootSize = MaterialShaderAsset.GetSerializedSize(kind);
        if (root.Length != (source.IsVertex ? 12 : 20))
            throw new InvalidDataException($"IW3 PS3 shader '{name}' has an invalid wrapper size.");

        ReadOnlySpan<byte> program = source.Program.Span;
        if (program.Length < 32 ||
            BinaryPrimitives.ReadUInt32BigEndian(program) != (source.IsVertex ? 7003u : 7004u) ||
            BinaryPrimitives.ReadUInt32BigEndian(program[4..]) != 6 ||
            BinaryPrimitives.ReadUInt32BigEndian(program[8..]) != (uint)program.Length ||
            BinaryPrimitives.ReadUInt32BigEndian(root[8..]) != (uint)program.Length)
        {
            throw new InvalidDataException(
                $"IW3 PS3 shader '{name}' does not contain its complete native Cg program.");
        }

        var key = (kind, name);
        if (_shadersByName.TryGetValue(key, out MaterialShaderAsset? named))
        {
            if (named.Data is not { } existingProgram || !program.SequenceEqual(existingProgram))
            {
                throw new InvalidDataException(
                    $"IW3 PS3 shader '{name}' has conflicting native Cg definitions.");
            }
            _shaders.Add(source, named);
            return named;
        }

        var asset = new MaterialShaderAsset
        {
            Kind = kind,
            Name = OwnedNamePrefix + name,
            DataSize = (uint)program.Length,
            Data = program.ToArray(),
            // These are runtime GPU fields; the target linker requires its own
            // serialized wrapper width while retaining the Cg container intact.
            ProgramBytes = new byte[rootSize - 4 - MaterialShaderAsset.ShaderLoadDefSerializedSize]
        };
        _shaders.Add(source, asset);
        _shadersByName.Add(key, asset);
        return asset;
    }

    internal IReadOnlyList<MaterialShaderAsset> CompiledShaders =>
        Array.AsReadOnly(_shadersByName.Values.ToArray());
}
