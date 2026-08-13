using System.Buffers.Binary;
using System.Collections.Immutable;

namespace IW4.Render.Shaders;

/// <summary>
/// Decodes the immutable instruction stream shared by RSX translation and
/// shader-input routing. Instruction shapes are defined centrally by
/// <see cref="RsxShaderInstructionSet"/>.
/// </summary>
internal static class RsxProgramDecoder
{
    public const int DefaultInstructionLimit = 512;

    public static int ShaderUploadOffset(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < 0x20)
            return -1;

        uint size = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0x18, 4));
        uint offset = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0x1c, 4));
        // Validate in a wider domain so hostile or corrupt 32-bit header
        // values cannot wrap into an apparently in-range signed offset.
        ulong uploadOffset = offset;
        ulong inputLength = (ulong)data.Length;
        return offset >= 0x40 &&
               (offset & 0x0f) == 0 &&
               uploadOffset + 16 <= inputLength &&
               uploadOffset + size <= inputLength
            ? checked((int)offset)
            : -1;
    }

    public static RsxVertexProgramIr DecodeVertexProgram(
        byte[] data,
        string decoderVersion = RsxVertexProgramIr.CurrentDecoderVersion)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(decoderVersion);

        int offset = ShaderUploadOffset(data);
        ImmutableArray<RsxVertexInstruction> instructions = offset < 0
            ? ImmutableArray<RsxVertexInstruction>.Empty
            : DecodeVertexInstructions(data, offset, DefaultInstructionLimit);
        return new RsxVertexProgramIr(
            data,
            decoderVersion,
            offset,
            instructions);
    }

    public static IReadOnlyList<RsxVertexInstruction> DecodeVertex(
        byte[] data,
        int offset,
        int maxInstructions = DefaultInstructionLimit)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(maxInstructions);

        return DecodeVertexInstructions(data, offset, maxInstructions);
    }

    private static ImmutableArray<RsxVertexInstruction> DecodeVertexInstructions(
        byte[] data,
        int offset,
        int maxInstructions)
    {
        var instructions = ImmutableArray.CreateBuilder<RsxVertexInstruction>();
        int uploadEnd = data.Length;
        if (data.Length >= 0x20)
        {
            uint uploadSize = BinaryPrimitives.ReadUInt32BigEndian(
                data.AsSpan(0x18, 4));
            if (uploadSize <= int.MaxValue && offset + uploadSize <= data.Length)
                uploadEnd = offset + (int)uploadSize;
        }

        for (int index = 0, pc = offset;
             index < maxInstructions && pc + 16 <= uploadEnd;
             index++, pc += 16)
        {
            uint word0 = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pc, 4));
            uint word1 = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pc + 4, 4));
            uint word2 = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pc + 8, 4));
            uint word3 = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pc + 12, 4));
            instructions.Add(new RsxVertexInstruction(
                index,
                pc,
                word0,
                word1,
                word2,
                word3));
            if ((word3 & 1) != 0)
                break;
        }

        return instructions.ToImmutable();
    }

    public static ImmutableArray<RsxFragmentInstruction> DecodeFragment(
        byte[] data,
        int offset,
        int maxInstructions = DefaultInstructionLimit)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(maxInstructions);

        var instructions =
            ImmutableArray.CreateBuilder<RsxFragmentInstruction>();
        int pc = offset;
        for (int index = 0;
             index < maxInstructions && pc + 16 <= data.Length;
             index++)
        {
            uint destination = FragmentWord(
                BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pc, 4)));
            uint source0 = FragmentWord(
                BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pc + 4, 4)));
            uint source1 = FragmentWord(
                BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pc + 8, 4)));
            uint source2 = FragmentWord(
                BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pc + 12, 4)));
            byte opcode = (byte)(((destination >> 24) & 0x3f) |
                                 ((source1 >> 25) & 0x40));
            var opcodeType = (RsxFragmentOpcode)opcode;
            bool controlFlow =
                RsxShaderInstructionSet.IsFragmentControlFlow(opcodeType);
            bool canUseInlineConstant = !controlFlow &&
                opcodeType is not RsxFragmentOpcode.FenceT and
                not RsxFragmentOpcode.FenceB;
            bool hasInlineConstant = canUseInlineConstant &&
                (RsxFragmentInstruction.SourceRegisterKind(source0) ==
                    RsxFragmentRegisterType.InlineConstant ||
                 RsxFragmentInstruction.SourceRegisterKind(source1) ==
                    RsxFragmentRegisterType.InlineConstant ||
                 RsxFragmentInstruction.SourceRegisterKind(source2) ==
                    RsxFragmentRegisterType.InlineConstant);
            int byteCount = hasInlineConstant ? 32 : 16;
            RsxFragmentInlineConstant? constant = hasInlineConstant &&
                                                  pc + byteCount <= data.Length
                ? new RsxFragmentInlineConstant(
                    FragmentConstantBits(data, pc + 16),
                    FragmentConstantBits(data, pc + 20),
                    FragmentConstantBits(data, pc + 24),
                    FragmentConstantBits(data, pc + 28))
                : null;

            instructions.Add(new RsxFragmentInstruction(
                index,
                pc,
                destination,
                source0,
                source1,
                source2,
                opcodeType,
                byteCount,
                constant));
            pc += byteCount;
            if ((destination & 1) != 0)
                break;
        }

        return instructions.ToImmutable();
    }

    /// <summary>
    /// Applies the RSX fragment-program byte-lane transform. The transform is
    /// its own inverse, so it is used for both decoded reads and patch writes.
    /// </summary>
    public static uint FragmentWord(uint value) =>
        ((value & 0x000000ffu) << 16) |
        ((value & 0x0000ff00u) << 16) |
        ((value & 0x00ff0000u) >> 16) |
        ((value & 0xff000000u) >> 16);

    private static uint FragmentConstantBits(byte[] data, int offset) =>
        FragmentWord(
            BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4)));
}
