using System.Buffers;
using System.Buffers.Binary;

namespace IW4.Render.Shaders;

internal enum CgOp
{
    Move, Add, Multiply, Subtract, Texture2D, Dot2, Dot3, Dot4, Minimum, Maximum, Saturate,
    LessThan, LessThanOrEqual, GreaterThan, GreaterThanOrEqual, Equal, NotEqual,
}

internal enum CgRegisterKind { Temporary, Input, Output, Constant, Uniform }

internal sealed record CgSource(
    CgRegisterKind Kind, int Index = 0, bool Half = false, string Swizzle = "xyzw",
    bool Negate = false, float[]? Literal = null);

internal sealed record CgDestination(
    CgRegisterKind Kind, int Index = 0, bool Half = false, string Mask = "xyzw");

internal sealed record CgInstruction(
    CgOp Operation, CgDestination Target, CgSource A, CgSource? B = null, int Sampler = 0,
    CgSource? Predicate = null);

internal sealed record CgParameter(
    string Name, string Semantic, int Components, bool Half = false, bool IsOutput = false, int Binding = 0,
    bool Referenced = true, bool IsUniform = false);

internal static class ManagedCgFragmentEmitter
{
    private const int RegisterLimit = 48;
    private const int NoDestinationRegister = 0x3f;
    private const string Components = "xyzw";
    private const uint IdentitySwizzle = 0xe4;

    public static byte[] Emit(IReadOnlyList<CgParameter> parameters, IReadOnlyList<CgInstruction> instructions)
    {
        CgParameter? output = parameters.SingleOrDefault(parameter => parameter.IsOutput);
        if (output is null || instructions.Count == 0)
            throw new InvalidDataException("A fragment program must write COLOR0.");

        var upload = new ArrayBufferWriter<byte>();
        var registers = new Dictionary<(int Index, bool Half), int>();
        var lastUse = new Dictionary<(int Index, bool Half), int>();
        var patches = parameters.Where(parameter => parameter.IsUniform)
            .ToDictionary(parameter => parameter.Binding, _ => new List<int>());
        var occupied = new bool[RegisterLimit * 2];
        uint inputMask = 0;
        ushort texCoords = 0;
        int registerCount = 2;
        int instructionCount = 0;
        bool wroteOutput = false;

        // COLOR0 and the other three export slots alias the R/H temporary banks.
        foreach (int colorRegister in new[] { 0, 2, 3, 4 })
            SetOccupied(colorRegister, half: false, used: true);

        for (int index = 0; index < instructions.Count; index++)
        {
            CgInstruction instruction = instructions[index];
            if (instruction.Target.Kind == CgRegisterKind.Temporary)
                lastUse[(instruction.Target.Index, instruction.Target.Half)] = index;
            if (instruction.A.Kind == CgRegisterKind.Temporary)
                lastUse[(instruction.A.Index, instruction.A.Half)] = index;
            if (instruction.B is { Kind: CgRegisterKind.Temporary } second)
                lastUse[(second.Index, second.Half)] = index;
            if (instruction.Predicate is { Kind: CgRegisterKind.Temporary } predicate)
                lastUse[(predicate.Index, predicate.Half)] = index;
        }

        for (int index = 0; index < instructions.Count; index++)
        {
            CgInstruction instruction = instructions[index];
            CgDestination target = instruction.Target;
            CgSource a = Resolve(instruction.A);
            CgSource? b = instruction.B is { } second ? Resolve(second) : null;
            CgSource? predicate = instruction.Predicate is { } condition ? Resolve(condition) : null;
            int destination;
            if (target.Kind == CgRegisterKind.Output)
            {
                OutputRegister(target.Index, target.Half);
                destination = 0;
                wroteOutput = true;
            }
            else if (target.Kind == CgRegisterKind.Temporary)
            {
                var key = (target.Index, target.Half);
                if (!registers.TryGetValue(key, out destination))
                {
                    destination = Allocate(target.Half);
                    registers.Add(key, destination);
                }
            }
            else
            {
                throw new InvalidDataException("Invalid fragment instruction destination.");
            }

            RsxFragmentOpcode opcode = instruction.Operation switch
            {
                CgOp.Move or CgOp.Saturate => RsxFragmentOpcode.Move,
                CgOp.Add or CgOp.Subtract => RsxFragmentOpcode.Add,
                CgOp.Multiply => RsxFragmentOpcode.Multiply,
                CgOp.Texture2D => RsxFragmentOpcode.Texture,
                CgOp.Dot2 => RsxFragmentOpcode.Dot2,
                CgOp.Dot3 => RsxFragmentOpcode.Dot3,
                CgOp.Dot4 => RsxFragmentOpcode.Dot4,
                CgOp.Minimum => RsxFragmentOpcode.Minimum,
                CgOp.Maximum => RsxFragmentOpcode.Maximum,
                CgOp.LessThan => RsxFragmentOpcode.SetLessThan,
                CgOp.LessThanOrEqual => RsxFragmentOpcode.SetLessThanOrEqual,
                CgOp.GreaterThan => RsxFragmentOpcode.SetGreaterThan,
                CgOp.GreaterThanOrEqual => RsxFragmentOpcode.SetGreaterThanOrEqual,
                CgOp.Equal => RsxFragmentOpcode.SetEqual,
                CgOp.NotEqual => RsxFragmentOpcode.SetNotEqual,
                _ => throw new InvalidDataException($"Unsupported fragment operation {instruction.Operation}."),
            };
            if (b is not null && instruction.Operation == CgOp.Subtract)
                b = b with { Negate = !b.Negate };

            CgSource? scratch = null;
            if (b is not null && SharesSourceSelector(a, b))
            {
                // Each instruction can select one input attribute and carry one inline vector.
                scratch = new CgSource(CgRegisterKind.Temporary, Allocate(b.Half), b.Half);
                WriteInstruction(RsxFragmentOpcode.Move, scratch.Index, scratch.Half,
                    (uint)RsxFragmentWriteMask.All, b, null, 0, end: false);
                b = scratch;
            }

            if (predicate is not null)
            {
                // Use cc0.x for the scalar test without writing either temporary bank.
                WriteInstruction(RsxFragmentOpcode.Move, NoDestinationRegister, false, (uint)RsxFragmentWriteMask.X,
                    predicate, null, 0, end: false, writeCondition: true);
            }

            WriteInstruction(opcode, destination, target.Half, WriteMask(target.Mask), a, b, instruction.Sampler,
                end: index == instructions.Count - 1, saturate: instruction.Operation == CgOp.Saturate,
                predicated: predicate is not null);

            if (scratch is not null)
                SetOccupied(scratch.Index, scratch.Half, used: false);
            Release(instruction.A.Kind, instruction.A.Index, instruction.A.Half, index);
            if (instruction.B is { } source)
                Release(source.Kind, source.Index, source.Half, index);
            if (instruction.Predicate is { } guard)
                Release(guard.Kind, guard.Index, guard.Half, index);
            Release(target.Kind, target.Index, target.Half, index);
        }

        if (!wroteOutput)
            throw new InvalidDataException("A fragment program must write COLOR0.");

        Span<byte> descriptor = stackalloc byte[24];
        descriptor.Clear();
        // The native instruction count includes inline-constant slots.
        BinaryPrimitives.WriteUInt32BigEndian(descriptor, (uint)(upload.WrittenCount / 16));
        BinaryPrimitives.WriteUInt32BigEndian(descriptor[4..], inputMask);
        BinaryPrimitives.WriteUInt16BigEndian(descriptor[0x0c..], texCoords);
        BinaryPrimitives.WriteUInt16BigEndian(descriptor[0x0e..], unchecked((ushort)~texCoords));
        descriptor[0x12] = (byte)registerCount;
        descriptor[0x13] = output.Half ? (byte)1 : (byte)0;
        return ManagedCgProgramWriter.Write(false, parameters, descriptor, upload.WrittenSpan, fragmentPatches: patches);

        void OutputRegister(int index, bool half)
        {
            if (index != 0 || half != output.Half)
                throw new InvalidDataException("The fragment output must use the declared COLOR0 precision.");
        }

        CgSource Resolve(CgSource source)
        {
            if (source.Kind == CgRegisterKind.Output)
            {
                OutputRegister(source.Index, source.Half);
                return source;
            }
            if (source.Kind != CgRegisterKind.Temporary)
                return source;
            if (!registers.TryGetValue((source.Index, source.Half), out int register))
                throw new InvalidDataException("A fragment temporary was read before it was written.");
            return source with { Index = register };
        }

        int Allocate(bool half)
        {
            for (int register = 0; register < RegisterLimit; register++)
            {
                int lane = half ? register : 2 * register;
                if (occupied[lane] || (!half && occupied[lane + 1]))
                    continue;

                SetOccupied(register, half, used: true);
                registerCount = Math.Max(registerCount, (lane / 2) + 1);
                return register;
            }
            throw new InvalidDataException("The fragment program requires too many simultaneous temporary registers.");
        }

        void Release(CgRegisterKind kind, int index, bool half, int instruction)
        {
            var key = (index, half);
            if (kind == CgRegisterKind.Temporary && lastUse[key] == instruction && registers.Remove(key, out int register))
                SetOccupied(register, half, used: false);
        }

        void SetOccupied(int register, bool half, bool used)
        {
            if (half)
                occupied[register] = used;
            else
                occupied[2 * register] = occupied[2 * register + 1] = used;
        }

        void WriteInstruction(RsxFragmentOpcode opcode, int register, bool half, uint mask,
            CgSource a, CgSource? b, int sampler, bool end, bool saturate = false,
            bool predicated = false, bool writeCondition = false)
        {
            if (++instructionCount > RsxProgramDecoder.DefaultInstructionLimit)
                throw new InvalidDataException("The fragment program exceeds the studio RSX instruction limit.");

            CgSource? input = a.Kind == CgRegisterKind.Input ? a : b is { Kind: CgRegisterKind.Input } ? b : null;
            uint src2 = IdentitySwizzle << 9;
            uint dst = ((uint)opcode << 24) | ((uint)register << 1) | (mask << 9);
            if (half)
                dst |= (1u << 7) | ((uint)RsxFragmentPrecision.Half << 22);
            if (saturate)
                dst |= 1u << 31;
            if (writeCondition)
                dst |= (1u << 30) | (1u << 8);
            if (end)
                dst |= 1;
            if (opcode == RsxFragmentOpcode.Texture)
            {
                if ((uint)sampler > 15)
                    throw new InvalidDataException("A fragment sampler must use TEXUNIT0 through TEXUNIT15.");
                dst |= (uint)sampler << 17;
            }

            if (input is not null)
            {
                var attribute = (RsxFragmentInputAttribute)input.Index;
                dst |= (uint)attribute << 13;
                switch (attribute)
                {
                    case RsxFragmentInputAttribute.WindowPosition:
                        break;
                    case RsxFragmentInputAttribute.Color0:
                        inputMask |= 0x5;
                        break;
                    case RsxFragmentInputAttribute.Color1:
                        inputMask |= 0xa;
                        break;
                    case >= RsxFragmentInputAttribute.TextureCoordinate0 and <= RsxFragmentInputAttribute.TextureCoordinate7:
                        int coordinate = input.Index - (int)RsxFragmentInputAttribute.TextureCoordinate0;
                        inputMask |= 0x4000u << coordinate;
                        texCoords |= (ushort)(1 << coordinate);
                        src2 |= 0x3fe00000;
                        break;
                    default:
                        throw new InvalidDataException($"Unsupported fragment input attribute {input.Index}.");
                }
            }

            CgSource? constant = IsInline(a) ? a : b is not null && IsInline(b) ? b : null;
            float[]? literal = constant is { Kind: CgRegisterKind.Constant } ? constant.Literal : null;
            if (constant is { Kind: CgRegisterKind.Constant } && literal is not { Length: 4 })
                throw new InvalidDataException("A fragment inline constant must contain four values.");
            int size = constant is null ? 16 : 32;
            Span<byte> bytes = upload.GetSpan(size);
            // A guarded write reads cc0.x in every lane; unconditional instructions keep the identity swizzle.
            uint conditionSwizzle = predicated ? 0 : IdentitySwizzle;
            RsxConditionTest test = predicated ? RsxConditionTest.NotEqual : RsxConditionTest.True;
            uint src0 = EncodeSource(a) | ((uint)test << 18) | (conditionSwizzle << 21);
            uint src1 = (b is null ? IdentitySwizzle << 9 : EncodeSource(b)) |
                (SourcePrecision(a) << 19) | (SourcePrecision(b) << 22);
            WriteWord(bytes, dst);
            WriteWord(bytes[4..], src0);
            WriteWord(bytes[8..], src1);
            WriteWord(bytes[12..], src2);
            if (literal is not null)
            {
                for (int lane = 0; lane < literal.Length; lane++)
                    WriteWord(bytes[(16 + lane * 4)..], BitConverter.SingleToUInt32Bits(literal[lane]));
            }
            else if (constant is not null)
            {
                if (!patches.TryGetValue(constant.Index, out List<int>? sites))
                    throw new InvalidDataException("A fragment inline uniform has no reflected parameter.");
                sites.Add(upload.WrittenCount + 16);
                bytes.Slice(16, 16).Clear();
            }
            upload.Advance(size);
        }
    }

    private static bool SharesSourceSelector(CgSource a, CgSource b) =>
        (a.Kind == CgRegisterKind.Input && b.Kind == CgRegisterKind.Input && a.Index != b.Index) ||
        (IsInline(a) && IsInline(b) &&
            !(a.Kind == CgRegisterKind.Uniform && b.Kind == CgRegisterKind.Uniform && a.Index == b.Index));

    private static bool IsInline(CgSource source) =>
        source.Kind is CgRegisterKind.Constant or CgRegisterKind.Uniform;

    private static uint SourcePrecision(CgSource? source) =>
        source is { Half: true, Kind: CgRegisterKind.Input or CgRegisterKind.Constant or CgRegisterKind.Uniform }
            ? (uint)RsxFragmentPrecision.Half : 0;

    private static uint EncodeSource(CgSource source)
    {
        uint word = source.Kind switch
        {
            CgRegisterKind.Temporary or CgRegisterKind.Output =>
                ((uint)source.Index << 2) | (source.Half ? 1u << 8 : 0),
            CgRegisterKind.Input => (uint)RsxFragmentRegisterType.Input,
            CgRegisterKind.Constant or CgRegisterKind.Uniform => (uint)RsxFragmentRegisterType.InlineConstant,
            _ => throw new InvalidDataException("Invalid fragment instruction source."),
        };
        if (source.Negate)
            word |= 1u << 17;
        for (int lane = 0; lane < 4; lane++)
            word |= (uint)Component(source.Swizzle[Math.Min(lane, source.Swizzle.Length - 1)]) << (9 + lane * 2);
        return word;
    }

    private static uint WriteMask(string mask)
    {
        uint value = 0;
        foreach (char lane in mask)
            value |= 1u << Component(lane);
        return value;
    }

    private static int Component(char lane)
    {
        int component = Components.IndexOf(lane);
        return component >= 0 ? component : throw new InvalidDataException($"Invalid fragment component '{lane}'.");
    }

    private static void WriteWord(Span<byte> data, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(data, RsxProgramDecoder.FragmentWord(value));
}
