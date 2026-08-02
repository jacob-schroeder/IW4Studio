using System.Collections.Immutable;
using System.Security.Cryptography;

namespace IW4.Render.Shaders;

/// <summary>
/// Immutable, backend-neutral decode of one RSX vertex-program asset.
/// </summary>
public sealed class RsxVertexProgramIr
{
    /// <summary>
    /// Decoder version included in <see cref="Identity"/>. Increment this when
    /// instruction decoding semantics change.
    /// </summary>
    public const string CurrentDecoderVersion = "rsx-vertex-semantic-ir/2";

    internal RsxVertexProgramIr(
        ReadOnlySpan<byte> input,
        string decoderVersion,
        int uploadOffset,
        ImmutableArray<RsxVertexInstruction> instructions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(decoderVersion);
        if (uploadOffset < -1)
            throw new ArgumentOutOfRangeException(nameof(uploadOffset));
        if (instructions.IsDefault)
            throw new ArgumentException(
                "The instruction array must be initialized.",
                nameof(instructions));
        if (uploadOffset < 0 && !instructions.IsEmpty)
        {
            throw new ArgumentException(
                "An invalid upload cannot contain decoded instructions.",
                nameof(instructions));
        }

        DecoderVersion = decoderVersion;
        InputProgramBytes = ImmutableArray.CreateRange(input.ToArray());
        InputByteCount = InputProgramBytes.Length;
        InputSha256 = Convert.ToHexString(
            SHA256.HashData(InputProgramBytes.AsSpan()));
        Identity = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"rsx-vertex-program-ir:{decoderVersion.Length}:{decoderVersion}:{InputByteCount}:{InputSha256}");
        UploadOffset = uploadOffset;
        Instructions = instructions;
    }

    /// <summary>
    /// Explicit semantic-decoder version used to produce this IR.
    /// </summary>
    public string DecoderVersion { get; }

    /// <summary>
    /// Immutable snapshot of the exact authored vertex-program asset bytes.
    /// Header and non-instruction bytes remain part of the provenance and
    /// cache identity even when they do not contribute decoded instructions.
    /// </summary>
    public ImmutableArray<byte> InputProgramBytes { get; }

    /// <summary>
    /// Byte length of the exact source asset covered by <see cref="Identity"/>.
    /// </summary>
    public int InputByteCount { get; }

    /// <summary>
    /// SHA-256 digest of the exact source asset bytes.
    /// </summary>
    public string InputSha256 { get; }

    /// <summary>
    /// Stable identity combining the explicit decoder version and exact-input
    /// digest. Header and non-instruction bytes are intentionally included.
    /// </summary>
    public string Identity { get; }

    /// <summary>
    /// Byte offset of the validated instruction upload, or -1 when the upload
    /// header is invalid.
    /// </summary>
    public int UploadOffset { get; }

    public bool HasValidUpload => UploadOffset >= 0;

    /// <summary>
    /// Instructions decoded once from the validated upload.
    /// </summary>
    public ImmutableArray<RsxVertexInstruction> Instructions { get; }
}

/// <summary>
/// One immutable RSX vertex instruction. Raw words remain available alongside
/// the current translator and input-router interpretations.
/// </summary>
public readonly record struct RsxVertexInstruction(
    int Index,
    int Offset,
    uint Word0,
    uint Word1,
    uint Word2,
    uint Word3)
{
    public static int SourceRegisterType(uint source) => (int)(source & 3);

    /// <summary>
    /// Source slots read by the existing RSX vector-opcode interpretation.
    /// Bits 0, 1 and 2 identify source slots 0, 1 and 2 respectively.
    /// </summary>
    public static int VectorSourceMask(byte opcode) => opcode switch
    {
        0x00 => 0,
        0x11 or 0x15 => 0,
        0x01 or 0x0d or 0x0e or 0x0f or 0x16 or 0x17 or 0x18 or 0x19 => 1,
        // NV40 VP ADD consumes source slots 0 and 2.
        0x03 => 1 | 4,
        0x04 => 1 | 2 | 4,
        _ => 1 | 2
    };

    /// <summary>
    /// Whether the existing RSX scalar-opcode interpretation consumes source
    /// slot 2.
    /// </summary>
    public static bool ScalarReadsSource2(byte opcode) =>
        RsxProgramDecoder.VertexScalarOperandCount(opcode) > 0;

    public bool VecResult => (Word0 & 0x40000000u) != 0;

    /// <summary>
    /// Curie CDST_WM: enables a condition-register update from this VLIW
    /// instruction. Bit 29 selects the contributing ALU slot; it is not a
    /// second enable bit.
    /// </summary>
    public bool CondUpdateEnabled => (Word0 & 0x00004000u) != 0;

    /// <summary>
    /// Curie CDST_IS_VEC: selects the vector result when true and the scalar
    /// result when false for an enabled condition-register update.
    /// </summary>
    public bool CondUpdateFromVector => (Word0 & 0x20000000u) != 0;

    public bool Saturate => (Word0 & 0x04000000u) != 0;
    public bool Source2Abs => (Word0 & 0x00800000u) != 0;
    public bool Source1Abs => (Word0 & 0x00400000u) != 0;
    public bool Source0Abs => (Word0 & 0x00200000u) != 0;
    public int VecDestTemp => (int)((Word0 >> 15) & 0x3f);
    public bool CondTestEnabled => (Word0 & 0x00002000u) != 0;
    public int ConditionCode => (int)((Word0 >> 10) & 7);
    public int ConditionSwizzleX => (int)((Word0 >> 8) & 3);
    public int ConditionSwizzleY => (int)((Word0 >> 6) & 3);
    public int ConditionSwizzleZ => (int)((Word0 >> 4) & 3);
    public int ConditionSwizzleW => (int)((Word0 >> 2) & 3);
    public int ConditionRegister => (int)((Word0 >> 25) & 1);
    public byte VecOpcode => (byte)((Word1 >> 22) & 0x1f);
    public byte ScaOpcode => (byte)((Word1 >> 27) & 0x1f);
    public int ConstSource => (int)((Word1 >> 12) & 0x1ff);
    public int InputSource => (int)((Word1 >> 8) & 0x0f);
    public uint Source0 => ((Word1 & 0xffu) << 9) | ((Word2 >> 23) & 0x1ffu);
    public uint Source1 => (Word2 >> 6) & 0x1ffffu;
    public uint Source2 => ((Word2 & 0x3fu) << 11) | ((Word3 >> 21) & 0x7ffu);
    public int ScaWriteMask => (int)((Word3 >> 17) & 0x0f);
    public int VecWriteMask => (int)((Word3 >> 13) & 0x0f);

    /// <summary>
    /// Existing translator interpretation: scalar result selection is the
    /// complement of <see cref="VecResult"/>.
    /// </summary>
    public bool ScaResult => !VecResult;

    /// <summary>
    /// Existing translator interpretation of the six-bit scalar destination.
    /// </summary>
    public int ScaDestTemp => (int)((Word3 >> 7) & 0x3f);

    public int ResultIndex => (int)((Word3 >> 2) & 0x1f);
    public bool IndexConst => (Word3 & 2) != 0;
    public bool HasControlFlow => ScaOpcode is 0x09 or 0x0b or 0x0c;

    public int ConditionSwizzle(int component) => component switch
    {
        0 => ConditionSwizzleX,
        1 => ConditionSwizzleY,
        2 => ConditionSwizzleZ,
        3 => ConditionSwizzleW,
        _ => throw new ArgumentOutOfRangeException(nameof(component))
    };

    /// <summary>
    /// Input-router interpretation retained separately from
    /// <see cref="ScaResult"/> until the PS3 paths are reconciled.
    /// </summary>
    public bool RouterScalarWritesResult => (Word3 & 0x00001000u) != 0;

    /// <summary>
    /// Input-router interpretation of the five-bit scalar destination.
    /// </summary>
    public int RouterScalarDestinationTemp => (int)((Word3 >> 7) & 0x1f);
}
