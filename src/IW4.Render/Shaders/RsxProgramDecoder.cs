using System.Buffers.Binary;
using System.Collections.Immutable;

namespace IW4.Render.Shaders;

/// <summary>
/// Decodes the immutable instruction stream shared by RSX translation and
/// shader-input routing. Consumer-specific opcode interpretation remains in
/// those consumers when the two paths do not yet agree.
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
            byte opcode = (byte)((destination >> 24) & 0x3f);
            bool branch = (source1 & 0x80000000u) != 0;
            int operandCount = branch ? 0 : FragmentOperandCount(opcode);
            bool hasInlineConstant =
                (operandCount > 0 &&
                 RsxFragmentInstruction.SourceRegisterType(source0) == 2) ||
                (operandCount > 1 &&
                 RsxFragmentInstruction.SourceRegisterType(source1) == 2) ||
                (operandCount > 2 &&
                 RsxFragmentInstruction.SourceRegisterType(source2) == 2);
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
                opcode,
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

    public static int FragmentOperandCount(byte opcode) => opcode switch
    {
        0x00 or 0x20 or 0x21 or 0x3d or 0x3e or 0x40 or 0x41 or 0x42 or 0x43 or 0x44 or 0x45 => 0,
        0x01 or 0x10 or 0x11 or 0x12 or 0x13 or 0x14 or 0x15 or 0x16 or 0x17 or 0x18 or 0x1a or 0x1b or 0x1c or 0x1d or 0x1e or 0x22 or 0x23 or 0x24 or 0x25 or 0x27 or 0x28 or 0x29 or 0x2a or 0x2c or 0x2d or 0x39 or 0x3c => 1,
        0x02 or 0x03 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09 or 0x0a or 0x0b or 0x0c or 0x0d or 0x0e or 0x0f or 0x2f or 0x31 or 0x36 or 0x38 or 0x3a or 0x3b => 2,
        _ => 3
    };

    public static int VertexScalarOperandCount(byte opcode) => opcode switch
    {
        0x00 or 0x09 or 0x0b or 0x0c or 0x13 or 0x14 => 0,
        _ => 1
    };

    private static uint FragmentConstantBits(byte[] data, int offset) =>
        FragmentWord(
            BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4)));
}
