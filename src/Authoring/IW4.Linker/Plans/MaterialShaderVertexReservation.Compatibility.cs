using System.Buffers.Binary;
using IW4.Game.Assets.TechniqueSet;

namespace IW4.Linker.Plans;

public static partial class MaterialShaderVertexReservation
{
    public const int Mw2VertexCommandBudgetBytes = 3 * 1024;

    public static void ValidateCompiledProgram(
        MaterialShaderKind kind,
        ReadOnlySpan<byte> bytecode,
        string fieldPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldPath);
        uint profile = kind switch
        {
            MaterialShaderKind.Vertex => 7003,
            MaterialShaderKind.Pixel => 7004,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        if (bytecode.Length < HeaderSize ||
            ReadUInt32(bytecode, 0) != profile ||
            ReadUInt32(bytecode, 4) != 6 ||
            ReadUInt32(bytecode, 8) != (uint)bytecode.Length)
        {
            throw new InvalidDataException(
                $"{fieldPath} requires a complete revision 6 PS3 Cg {kind} program (profile {profile}).");
        }

        try
        {
            CgProgramLayout layout = ReadLayout(bytecode, fieldPath);
            uint instructionCount = ReadUInt32(bytecode, layout.DescriptorOffset);
            if (layout.DescriptorOffset < HeaderSize || layout.UploadOffset < HeaderSize ||
                (layout.ParameterCount > 0 && layout.ParameterTableOffset < HeaderSize) ||
                layout.ParameterCount > ushort.MaxValue ||
                layout.UploadSize % 16 != 0 || instructionCount == 0 ||
                (ulong)instructionCount * 16 > (ulong)layout.UploadSize ||
                Overlaps(layout.DescriptorOffset, DescriptorSize, layout.UploadOffset, layout.UploadSize) ||
                Overlaps(layout.ParameterTableOffset, layout.ParameterCount * ParameterSize,
                    layout.DescriptorOffset, DescriptorSize) ||
                Overlaps(layout.ParameterTableOffset, layout.ParameterCount * ParameterSize,
                    layout.UploadOffset, layout.UploadSize))
            {
                throw new InvalidDataException(
                    $"{fieldPath} has an invalid Cg descriptor, parameter table, or instruction upload.");
            }

            if (kind == MaterialShaderKind.Pixel)
            {
                _ = CreatePixel(
                    bytecode, layout.ParameterCount, layout.ParameterTableOffset, layout.UploadSize, fieldPath);
            }
            else
            {
                MaterialShaderVertexCommandMetrics commands = CalculateCommandMetrics(bytecode, layout, fieldPath);
                if (commands.CommandBytes > Mw2VertexCommandBudgetBytes)
                {
                    throw new InvalidDataException(
                        $"{fieldPath} requires {commands.CommandBytes} vertex command bytes. " +
                        $"The MW2 SPU merger supports up to {Mw2VertexCommandBudgetBytes} bytes.");
                }
            }
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException(
                $"{fieldPath} contains a count or offset outside the supported range.", exception);
        }
    }

    private static bool Overlaps(int start, int length, int otherStart, int otherLength) =>
        length != 0 && otherLength != 0 &&
        start < otherStart + otherLength && otherStart < start + length;

    public static byte[] PrepareReplacementInterface(
        MaterialShaderKind kind,
        ReadOnlySpan<byte> original,
        byte[] replacement,
        IReadOnlyCollection<ushort> constantDestinations,
        string fieldPath)
    {
        CgProgramLayout before = ReadLayout(original, fieldPath);
        CgProgramLayout after = ReadLayout(replacement, fieldPath);
        ReadOnlySpan<byte> oldDescriptor = original.Slice(before.DescriptorOffset, DescriptorSize);
        ReadOnlySpan<byte> newDescriptor = replacement.AsSpan(after.DescriptorOffset, DescriptorSize);
        if (!PreservesStageInterface(kind, oldDescriptor, newDescriptor))
        {
            throw new InvalidDataException(
                $"{fieldPath} changes the shader's stage inputs or outputs used by existing technique passes.");
        }

        CgBinding[] existing = ReadBindings(kind, original, before, fieldPath);
        CgBinding[] updated = ReadBindings(kind, replacement, after, fieldPath);
        var physicalBindings = existing
            .Where(binding => binding.IsRequired && !binding.IsPixelConstant)
            .ToHashSet();
        foreach (CgBinding binding in updated.Where(binding => binding.IsRequired && !binding.IsPixelConstant))
        {
            if (!physicalBindings.Contains(binding))
            {
                throw new InvalidDataException(
                    $"{fieldPath} requires a new or incompatible Cg binding at {binding.Destination}.");
            }
        }

        if (kind == MaterialShaderKind.Pixel)
            return RestorePixelConstantOrdinals(replacement, after, existing, updated, constantDestinations, fieldPath);

        // A technique can still upload a constant even if the edited program drops that uniform.
        // A compiled literal in the same slot would then change value at draw time.
        var boundRegisters = existing.Where(binding => binding.IsRequired && binding.Resource == 0x882)
            .Select(binding => binding.Destination).ToHashSet();
        foreach (ushort register in constantDestinations) boundRegisters.Add(register);
        for (int i = 0; i < after.ParameterCount; i++)
        {
            int entry = after.ParameterTableOffset + i * ParameterSize;
            if (ReadUInt32(replacement, entry + 8) != 0x1007 || ReadUInt32(replacement, entry + 20) == 0) continue;
            uint register = ReadUInt32(replacement, entry + 12);
            if (boundRegisters.Contains(register))
                throw new InvalidDataException($"{fieldPath} assigns a vertex literal to c{register}, which an existing technique can overwrite.");
        }
        return replacement;
    }

    private static byte[] RestorePixelConstantOrdinals(
        byte[] replacement,
        CgProgramLayout layout,
        CgBinding[] existing,
        CgBinding[] updated,
        IReadOnlyCollection<ushort> pixelConstantDestinations,
        string fieldPath)
    {
        var destinations = new SortedSet<int>(pixelConstantDestinations.Select(destination => (int)destination));
        ILookup<CgBindingIdentity, CgBinding> existingConstants = existing
            .Where(binding => binding.IsPixelConstant && binding.IsRequired)
            .ToLookup(binding => binding.Identity);
        foreach (CgBinding binding in updated.Where(binding => binding.IsPixelConstant && binding.IsRequired))
        {
            CgBinding[] matches = existingConstants[binding.Identity].Take(2).ToArray();
            if (string.IsNullOrEmpty(binding.Name) || matches.Length != 1)
            {
                throw new InvalidDataException(
                    $"{fieldPath} requires a new or ambiguous pixel uniform '{binding.Name}'.");
            }

            destinations.Add(checked((int)matches[0].Destination));
        }

        ILookup<CgBindingIdentity, CgBinding> compiledConstants = updated
            .Where(binding => binding.IsPixelConstant)
            .ToLookup(binding => binding.Identity);
        int[] source = Enumerable.Range(0, updated.Length).ToArray();
        int[] position = source.ToArray();
        var assigned = new HashSet<int>();
        foreach (int destination in destinations)
        {
            if (destination >= existing.Length || destination >= updated.Length ||
                !existing[destination].IsPixelConstant ||
                string.IsNullOrEmpty(existing[destination].Name))
            {
                throw new InvalidDataException(
                    $"{fieldPath} cannot preserve existing pixel constant destination {destination} in the compiled Cg table.");
            }

            CgBinding before = existing[destination];
            CgBinding[] candidates = compiledConstants[before.Identity].Take(2).ToArray();
            if (candidates.Length != 1 || !assigned.Add(checked((int)candidates[0].Destination)))
            {
                throw new InvalidDataException(
                    $"{fieldPath} must supply one compatible pixel uniform '{before.Name}' for destination {destination}.");
            }

            int row = checked((int)candidates[0].Destination);
            int current = position[row];
            int displaced = source[destination];
            source[destination] = row;
            source[current] = displaced;
            position[row] = destination;
            position[displaced] = current;
        }

        byte[]? result = null;
        for (int destination = 0; destination < source.Length; destination++)
        {
            int row = source[destination];
            if (row == destination)
                continue;

            int entry = layout.ParameterTableOffset + row * ParameterSize;
            ReadOnlySpan<byte> parameter = replacement.AsSpan(entry, ParameterSize);
            uint type = ReadUInt32(parameter, 0);
            string? name = ReadParameterName(replacement, ReadUInt32(parameter, 16), fieldPath);
            if (type is not (>= 0x401 and <= 0x404 or >= 0x415 and <= 0x418 or >= 0x429 and <= 0x42D) ||
                (name is not null && (name.Contains('.') || name.Contains('['))))
            {
                throw new InvalidDataException(
                    $"{fieldPath} cannot reorder a structured Cg parameter to preserve existing pixel bindings.");
            }

            // Keep every offset from the new compiler row, including its patch list.
            result ??= replacement.ToArray();
            parameter.CopyTo(result.AsSpan(layout.ParameterTableOffset + destination * ParameterSize, ParameterSize));
        }

        return result ?? replacement;
    }

    private static bool PreservesStageInterface(
        MaterialShaderKind kind, ReadOnlySpan<byte> before, ReadOnlySpan<byte> after)
    {
        if (kind == MaterialShaderKind.Vertex)
        {
            return (ReadUInt32(after, 12) & ~ReadUInt32(before, 12)) == 0 &&
                ReadUInt32(before, 16) == ReadUInt32(after, 16);
        }

        ushort oldTexCoords = BinaryPrimitives.ReadUInt16BigEndian(before[12..]);
        ushort newTexCoords = BinaryPrimitives.ReadUInt16BigEndian(after[12..]);
        return (ReadUInt32(after, 4) & ~ReadUInt32(before, 4)) == 0 &&
            (newTexCoords & ~oldTexCoords) == 0 &&
            ((BinaryPrimitives.ReadUInt16BigEndian(before[14..]) ^
                BinaryPrimitives.ReadUInt16BigEndian(after[14..])) & newTexCoords) == 0 &&
            ((BinaryPrimitives.ReadUInt16BigEndian(before[16..]) ^
                BinaryPrimitives.ReadUInt16BigEndian(after[16..])) & newTexCoords) == 0 &&
            // Depth is a pass output. Color register precision and KIL usage are
            // program execution controls supplied by the replacement's descriptor.
            before[20] == after[20];
    }

    private static CgBinding[] ReadBindings(
        MaterialShaderKind kind, ReadOnlySpan<byte> program, CgProgramLayout layout, string fieldPath)
    {
        var bindings = new CgBinding[layout.ParameterCount];
        for (int ordinal = 0; ordinal < bindings.Length; ordinal++)
        {
            int entry = layout.ParameterTableOffset + ordinal * ParameterSize;
            uint type = ReadUInt32(program, entry);
            uint resource = ReadUInt32(program, entry + 4);
            uint resourceIndex = ReadUInt32(program, entry + 12);
            bool sampler = resource is >= 0x800 and < 0x820;
            bool parameterOrdinal = kind == MaterialShaderKind.Pixel && !sampler;
            bool required = ReadUInt32(program, entry + 8) == 0x1006 &&
                ReadUInt32(program, entry + 32) == 0x1001 &&
                ReadUInt32(program, entry + 40) != 0 &&
                (parameterOrdinal
                    ? ReadUInt32(program, entry + 24) != 0
                    : resource != 0xCB8 && (sampler || ReadUInt32(program, entry + 20) == 0));
            string? name = parameterOrdinal
                ? ReadParameterName(program, ReadUInt32(program, entry + 16), fieldPath)
                : null;
            uint destination = parameterOrdinal
                ? (uint)ordinal
                : sampler ? resource - 0x800 : resourceIndex;
            bindings[ordinal] = new CgBinding(type, resource, destination, name, required, parameterOrdinal);
        }

        return bindings;
    }

    private static string? ReadParameterName(ReadOnlySpan<byte> program, uint offset, string fieldPath)
    {
        if (offset == 0)
            return null;
        if (offset >= program.Length)
            throw new InvalidDataException($"{fieldPath} has an invalid Cg parameter name offset.");

        ReadOnlySpan<byte> name = program[(int)offset..];
        int terminator = name.IndexOf((byte)0);
        if (terminator < 0)
            throw new InvalidDataException($"{fieldPath} has an unterminated Cg parameter name.");
        return System.Text.Encoding.Latin1.GetString(name[..terminator]);
    }

    private readonly record struct CgBinding(
        uint Type, uint Resource, uint Destination, string? Name, bool IsRequired, bool IsPixelConstant)
    {
        public CgBindingIdentity Identity => new(Type, Resource, Name);
    }

    private readonly record struct CgBindingIdentity(uint Type, uint Resource, string? Name);
}
