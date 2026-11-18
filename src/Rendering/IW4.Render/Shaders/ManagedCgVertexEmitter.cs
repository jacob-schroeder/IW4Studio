using System.Buffers;
using System.Buffers.Binary;
using System.Numerics;
using IW4.Game.Assets.TechniqueSet;

namespace IW4.Render.Shaders;

internal static class ManagedCgVertexEmitter
{
    private const string Components = "xyzw";
    private const uint UnusedSource = 0x1b00 | (uint)RsxVertexRegisterType.Input;

    public static byte[] Emit(IReadOnlyList<CgParameter> parameters, IReadOnlyList<CgInstruction> instructions)
    {
        var upload = new ArrayBufferWriter<byte>();
        var defaults = new List<(int Register, float[] Value)>();
        var boundConstants = parameters.Where(parameter => parameter.IsUniform && parameter.Components > 0)
            .Select(parameter => parameter.Binding).ToHashSet();
        int nextLiteral = RsxVertexConstantLayout.MaximumDestination;
        var constants = new Dictionary<(uint X, uint Y, uint Z, uint W), int>();
        var registers = new Dictionary<int, int>();
        var lastUse = new Dictionary<int, int>();
        uint freeRegisters = uint.MaxValue;
        uint inputs = 0;
        uint outputs = 0;
        int registerCount = 1;
        bool wrotePosition = false;

        for (int index = 0; index < instructions.Count; index++)
        {
            CgInstruction instruction = instructions[index];
            if (instruction.Target.Kind == CgRegisterKind.Temporary)
                lastUse[instruction.Target.Index] = index;
            if (instruction.A.Kind == CgRegisterKind.Temporary)
                lastUse[instruction.A.Index] = index;
            if (instruction.B is { Kind: CgRegisterKind.Temporary } second)
                lastUse[second.Index] = index;
        }

        for (int index = 0; index < instructions.Count; index++)
        {
            CgInstruction instruction = instructions[index];
            if (instruction.Predicate is not null)
                throw new InvalidDataException("Conditional assignment is supported in pixel shaders.");
            CgDestination target = instruction.Target;
            CgSource a = Resolve(instruction.A);
            CgSource? b = instruction.B is { } second ? Resolve(second) : null;
            int destination = target.Index;
            if (target.Kind == CgRegisterKind.Temporary && !registers.TryGetValue(target.Index, out destination))
            {
                destination = Allocate();
                registers.Add(target.Index, destination);
            }

            RsxVertexVectorOpcode opcode = instruction.Operation switch
            {
                CgOp.Move or CgOp.Saturate => RsxVertexVectorOpcode.Move,
                CgOp.Minimum => RsxVertexVectorOpcode.Minimum,
                CgOp.Maximum => RsxVertexVectorOpcode.Maximum,
                CgOp.Dot3 => RsxVertexVectorOpcode.Dot3,
                CgOp.Dot4 => RsxVertexVectorOpcode.Dot4,
                CgOp.LessThan => RsxVertexVectorOpcode.SetLessThan,
                CgOp.LessThanOrEqual => RsxVertexVectorOpcode.SetLessThanOrEqual,
                CgOp.GreaterThan => RsxVertexVectorOpcode.SetGreaterThan,
                CgOp.GreaterThanOrEqual => RsxVertexVectorOpcode.SetGreaterThanOrEqual,
                CgOp.Equal => RsxVertexVectorOpcode.SetEqual,
                CgOp.NotEqual => RsxVertexVectorOpcode.SetNotEqual,
                CgOp.Add or CgOp.Subtract => RsxVertexVectorOpcode.Add,
                CgOp.Dot2 or CgOp.Multiply => RsxVertexVectorOpcode.Multiply,
                _ => throw new InvalidDataException($"Unsupported vertex operation {instruction.Operation}."),
            };
            if (b is not null && instruction.Operation == CgOp.Subtract)
                b = b with { Negate = !b.Negate };

            int scratch = -1;
            if (b is not null && a.Kind == b.Kind && a.Index != b.Index &&
                a.Kind is CgRegisterKind.Input or CgRegisterKind.Constant)
            {
                // The vertex instruction has one selector for inputs and one for constants.
                scratch = Allocate();
                WriteInstruction(RsxVertexVectorOpcode.Move, new(CgRegisterKind.Temporary, scratch), b, null, end: false);
                b = new(CgRegisterKind.Temporary, scratch);
            }

            if (instruction.Operation == CgOp.Dot2)
            {
                int product = Allocate();
                WriteInstruction(opcode, new(CgRegisterKind.Temporary, product, Mask: "xy"),
                    a, b, end: false);
                a = new(CgRegisterKind.Temporary, product, Swizzle: "xxxx");
                b = a with { Swizzle = "yyyy" };
                WriteInstruction(RsxVertexVectorOpcode.Add, target with { Index = destination }, a, b, index == instructions.Count - 1);
                freeRegisters |= 1u << product;
            }
            else
                WriteInstruction(opcode, target with { Index = destination }, a, b, index == instructions.Count - 1,
                    saturate: instruction.Operation == CgOp.Saturate);
            if (scratch >= 0)
                freeRegisters |= 1u << scratch;
            Release(instruction.A.Kind, instruction.A.Index, index);
            if (instruction.B is { } source)
                Release(source.Kind, source.Index, index);
            Release(target.Kind, target.Index, index);
        }

        if (!wrotePosition)
            throw new InvalidDataException("A vertex program must write POSITION.");

        Span<byte> descriptor = stackalloc byte[24];
        descriptor.Clear();
        BinaryPrimitives.WriteUInt32BigEndian(descriptor, (uint)(upload.WrittenCount / 16));
        BinaryPrimitives.WriteUInt32BigEndian(descriptor[8..], (uint)registerCount);
        BinaryPrimitives.WriteUInt32BigEndian(descriptor[12..], inputs);
        BinaryPrimitives.WriteUInt32BigEndian(descriptor[16..], outputs);
        return ManagedCgProgramWriter.Write(true, parameters, descriptor, upload.WrittenSpan, defaults);

        CgSource Resolve(CgSource source)
        {
            if (source.Kind == CgRegisterKind.Temporary)
            {
                if (!registers.TryGetValue(source.Index, out int register))
                    throw new InvalidDataException("A vertex temporary was read before it was written.");
                return source with { Index = register };
            }
            if (source.Kind == CgRegisterKind.Uniform)
                return source with { Kind = CgRegisterKind.Constant };
            if (source.Kind == CgRegisterKind.Input)
                return source;
            if (source.Kind != CgRegisterKind.Constant || source.Literal is not { Length: 4 } literal)
                throw new InvalidDataException("A vertex source must be an input, temporary, or four-value constant.");

            var bits = (BitConverter.SingleToUInt32Bits(literal[0]), BitConverter.SingleToUInt32Bits(literal[1]),
                BitConverter.SingleToUInt32Bits(literal[2]), BitConverter.SingleToUInt32Bits(literal[3]));
            if (!constants.TryGetValue(bits, out int constant))
            {
                while (boundConstants.Contains(nextLiteral)) nextLiteral--;
                if (nextLiteral < 0)
                    throw new InvalidDataException("The vertex program exceeds the RSX constant register count.");
                // Material techniques normally bind their constants at the low end.
                constant = nextLiteral--;
                constants.Add(bits, constant);
                defaults.Add((constant, literal));
            }
            return source with { Index = constant };
        }

        int Allocate()
        {
            if (freeRegisters == 0)
                throw new InvalidDataException("The vertex program requires too many simultaneous temporary registers.");
            int register = BitOperations.TrailingZeroCount(freeRegisters);
            freeRegisters &= ~(1u << register);
            registerCount = Math.Max(registerCount, register + 1);
            return register;
        }

        void Release(CgRegisterKind kind, int index, int instruction)
        {
            if (kind == CgRegisterKind.Temporary && lastUse[index] == instruction && registers.Remove(index, out int register))
                freeRegisters |= 1u << register;
        }

        void WriteInstruction(RsxVertexVectorOpcode opcode, CgDestination target, CgSource a, CgSource? b, bool end, bool saturate = false)
        {
            if (upload.WrittenCount / 16 >= RsxProgramDecoder.DefaultInstructionLimit)
                throw new InvalidDataException("The vertex program exceeds the studio RSX instruction limit.");

            uint word0 = ((uint)RsxConditionTest.True << 10) | (0x1bu << 2);
            if (saturate) word0 |= 1u << 26;
            uint word1 = (uint)opcode << 22;
            uint word3 = 0x3fu << 7; // The scalar slot is unused.
            if (target.Kind == CgRegisterKind.Output)
            {
                var result = (RsxVertexResult)target.Index;
                word0 |= (1u << 30) | (0x3fu << 15);
                word3 |= (uint)result << 2;
                switch (result)
                {
                    case RsxVertexResult.Position:
                        wrotePosition = true;
                        break;
                    case RsxVertexResult.FrontColor0:
                        outputs |= 1;
                        break;
                    case >= RsxVertexResult.TextureCoordinate0 and <= RsxVertexResult.TextureCoordinate7:
                        outputs |= 0x4000u << (target.Index - (int)RsxVertexResult.TextureCoordinate0);
                        break;
                    default:
                        throw new InvalidDataException($"Unsupported vertex output register {target.Index}.");
                }
            }
            else if (target.Kind == CgRegisterKind.Temporary)
            {
                word0 |= (uint)target.Index << 15;
                word3 |= (uint)RsxVertexResult.None << 2;
            }
            else
            {
                throw new InvalidDataException("Invalid vertex instruction destination.");
            }

            foreach (char lane in target.Mask)
                word3 |= 1u << (16 - Component(lane));
            if (end)
                word3 |= 1;

            uint source0 = EncodeSource(a, ref word1);
            uint second = b is null ? UnusedSource : EncodeSource(b, ref word1);
            // ADD reads slots 0 and 2. MUL reads slots 0 and 1.
            bool add = opcode == RsxVertexVectorOpcode.Add;
            uint source1 = add ? UnusedSource : second;
            uint source2 = add ? second : UnusedSource;
            word1 |= source0 >> 9;
            uint word2 = ((source0 & 0x1ff) << 23) | (source1 << 6) | (source2 >> 11);
            word3 |= (source2 & 0x7ff) << 21;

            Span<byte> bytes = upload.GetSpan(16);
            BinaryPrimitives.WriteUInt32BigEndian(bytes, word0);
            BinaryPrimitives.WriteUInt32BigEndian(bytes[4..], word1);
            BinaryPrimitives.WriteUInt32BigEndian(bytes[8..], word2);
            BinaryPrimitives.WriteUInt32BigEndian(bytes[12..], word3);
            upload.Advance(16);
        }

        uint EncodeSource(CgSource source, ref uint selectors)
        {
            uint word;
            switch (source.Kind)
            {
                case CgRegisterKind.Input:
                    if ((uint)source.Index > 15)
                        throw new InvalidDataException($"Unsupported vertex input attribute {source.Index}.");
                    inputs |= 1u << source.Index;
                    selectors |= (uint)source.Index << 8;
                    word = (uint)RsxVertexRegisterType.Input;
                    break;
                case CgRegisterKind.Constant:
                    selectors |= (uint)source.Index << 12;
                    word = (uint)RsxVertexRegisterType.Constant;
                    break;
                case CgRegisterKind.Temporary:
                    word = ((uint)source.Index << 2) | (uint)RsxVertexRegisterType.Temporary;
                    break;
                default:
                    throw new InvalidDataException("Invalid vertex instruction source.");
            }
            if (source.Negate)
                word |= 1u << 16;
            for (int lane = 0; lane < 4; lane++)
                word |= (uint)Component(source.Swizzle[Math.Min(lane, source.Swizzle.Length - 1)]) << (14 - lane * 2);
            return word;
        }
    }

    private static int Component(char lane)
    {
        int component = Components.IndexOf(lane);
        return component >= 0 ? component : throw new InvalidDataException($"Invalid vertex component '{lane}'.");
    }
}
