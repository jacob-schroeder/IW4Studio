using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using System.Text;
using IW4.FastFiles.Database;
using IW4.FastFiles.Database.Streaming;

namespace IW4.FastFiles.Emitters.Packaging;

/// <summary>
/// The deterministic metadata decisions the PS3 container writer owns.  The
/// decoded zone remains opaque to this layer: package/depackage equality is
/// its only success claim.
/// </summary>
public sealed record FastFilePackagingPolicy(
    ulong? FileCreationTimeRaw = null,
    FastFileMaxFileSizePolicy MaxFileSizePolicy = FastFileMaxFileSizePolicy.AtLeastFileSize,
    bool EmitDoubleTerminator = true)
{
    public static FastFilePackagingPolicy Canonical { get; } = new();
}

/// <summary>
/// MaxFileSize is not a decoded-zone length.  Canonical output retains source
/// headroom when it is large enough and otherwise grows it to the exact packed
/// end, so the value can never understate FileSize.
/// </summary>
public enum FastFileMaxFileSizePolicy
{
    AtLeastFileSize,
    ExactFileSize
}

public sealed record FastFilePackagingError(
    string Code,
    string Message);

/// <summary>
/// Immutable outcome of a pure package operation.  A failure intentionally
/// exposes no candidate bytes so callers cannot accidentally persist an
/// incompletely framed fastfile.
/// </summary>
public sealed class FastFilePackagingResult
{
    private readonly byte[]? _bytes;
    private readonly FastFilePackagingError[] _errors;

    private FastFilePackagingResult(
        bool succeeded,
        byte[]? bytes,
        IEnumerable<FastFilePackagingError> errors)
    {
        Succeeded = succeeded;
        _bytes = bytes;
        _errors = errors.ToArray();
    }

    public bool Succeeded { get; }
    public ReadOnlyMemory<byte>? Bytes => _bytes is null
        ? (ReadOnlyMemory<byte>?)null
        : new ReadOnlyMemory<byte>(_bytes);
    public IReadOnlyList<FastFilePackagingError> Errors => _errors;

    internal static FastFilePackagingResult Success(byte[] bytes) =>
        new(true, bytes, []);

    internal static FastFilePackagingResult Failure(
        IEnumerable<FastFilePackagingError> errors) =>
        new(false, null, errors);
}

/// <summary>
/// Writes the PS3 DB header and its packed stream without consulting a
/// runtime, pool, filesystem, or source cursor. Full incompressible 64 KiB
/// pages use the native zero-size raw marker. Compressed frames retain the
/// zlib Adler-32 trailer while replacing only zlib's two-byte CMF/FLG header
/// with the outer big-endian frame-size word.
/// </summary>
public sealed class FastFilePackager
{
    private const int MaximumCompressedChunkSize = ushort.MaxValue;
    private const int DecodedBlockSize = 0x10000;
    private const ushort TerminatorWord = 1;
    private const uint SupportedLanguageMask = (1u << 15) - 1;
    private const string RequiredMagic = "IWffu100";
    private const int ImageStreamEntrySize = 0x14;

    public FastFilePackagingResult Package(
        ReadOnlyMemory<byte> decodedZone,
        DbHeader envelope,
        FastFilePackagingPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        policy ??= FastFilePackagingPolicy.Canonical;

        var errors = new List<FastFilePackagingError>();
        if (!TryValidateEnvelope(envelope, errors))
            return FastFilePackagingResult.Failure(errors);

        try
        {
            byte[] packedStream = EncodePackedStream(decodedZone.Span, policy);
            int headerLength = ComputeHeaderLength(envelope);
            int trailingPhysicalBytes = policy.EmitDoubleTerminator ? sizeof(ushort) : 0;
            uint fileSize = checked((uint)checked(headerLength + packedStream.Length - trailingPhysicalBytes));
            uint maxFileSize = policy.MaxFileSizePolicy switch
            {
                FastFileMaxFileSizePolicy.AtLeastFileSize => Math.Max(envelope.MaxFileSize, fileSize),
                FastFileMaxFileSizePolicy.ExactFileSize => fileSize,
                _ => throw new InvalidDataException(
                    $"Unknown {nameof(FastFileMaxFileSizePolicy)} '{policy.MaxFileSizePolicy}'.")
            };

            byte[] header = EncodeHeader(
                envelope,
                policy.FileCreationTimeRaw ?? envelope.FileCreationTimeRaw,
                fileSize,
                maxFileSize);
            byte[] output = new byte[checked(header.Length + packedStream.Length)];
            header.CopyTo(output, 0);
            packedStream.CopyTo(output, header.Length);
            return FastFilePackagingResult.Success(output);
        }
        catch (Exception exception) when (exception is OverflowException or ArgumentException or InvalidDataException)
        {
            errors.Add(new(
                "package.layout",
                exception.Message));
            return FastFilePackagingResult.Failure(errors);
        }
    }

    private static bool TryValidateEnvelope(
        DbHeader envelope,
        List<FastFilePackagingError> errors)
    {
        void Error(string code, string message) =>
            errors.Add(new(code, message));

        if (!string.Equals(envelope.Magic, RequiredMagic, StringComparison.Ordinal))
            Error("header.magic", $"PS3 packaging requires magic '{RequiredMagic}'.");
        if (Encoding.Latin1.GetByteCount(envelope.Magic) != 8)
            Error("header.magic", "PS3 magic must be exactly eight Latin-1 bytes.");
        if (envelope.LanguageMask == 0 || (envelope.LanguageMask & ~SupportedLanguageMask) != 0)
            Error("header.languageMask", "Language mask must contain one or more supported PS3 language bits.");
        if (BitOperations.PopCount(envelope.LanguageMask) != envelope.LanguageTables.Length ||
            envelope.LanguageCount != envelope.LanguageTables.Length)
        {
            Error("header.languageTables", "Language mask, language count, and serialized table count disagree.");
        }
        if (envelope.SelectedLanguageMask == 0 ||
            (envelope.SelectedLanguageMask & envelope.LanguageMask) == 0)
        {
            Error("header.selectedLanguage", "Selected language is not represented by the serialized language mask.");
        }

        for (int index = 0; index < envelope.LanguageTables.Length; index++)
        {
            DbHeaderImageStreamLanguageTable table = envelope.LanguageTables[index];
            if (table.SerializedIndex != index ||
                table.LanguageMask == 0 ||
                (table.LanguageMask & (table.LanguageMask - 1)) != 0 ||
                (table.LanguageMask & envelope.LanguageMask) == 0 ||
                table.ImageStreamEntries.Length != envelope.EntryCount)
            {
                Error("header.languageTables", $"Language table {index} is not representable in serialized PS3 header order.");
            }
        }

        return errors.Count == 0;
    }

    private static byte[] EncodePackedStream(
        ReadOnlySpan<byte> decodedZone,
        FastFilePackagingPolicy policy)
    {
        using var output = new MemoryStream();
        int offset = 0;
        while (offset < decodedZone.Length)
        {
            int chunkLength = Math.Min(DecodedBlockSize, decodedZone.Length - offset);
            WriteDecodedChunk(
                output,
                decodedZone.Slice(offset, chunkLength));
            offset += chunkLength;
        }

        WriteUInt16(output, TerminatorWord);
        if (policy.EmitDoubleTerminator)
            WriteUInt16(output, TerminatorWord);
        return output.ToArray();
    }

    private static void WriteDecodedChunk(
        Stream output,
        ReadOnlySpan<byte> chunk)
    {
        if (chunk.Length == 0)
            return;

        byte[] compressed = EncodeHeaderlessZlib(chunk);
        if (compressed.Length is > 1 and <= MaximumCompressedChunkSize)
        {
            WriteUInt16(output, checked((ushort)compressed.Length));
            output.Write(compressed);
            return;
        }

        if (chunk.Length == DecodedBlockSize)
        {
            WriteUInt16(output, 0);
            output.Write(chunk);
            return;
        }

        throw new InvalidDataException(
            $"A final partial decoded range of 0x{chunk.Length:X} bytes produced a " +
            $"0x{compressed.Length:X}-byte headerless-zlib frame, which cannot fit the native UInt16 size word. " +
            "It cannot be split because every native packed frame owns one 0x10000-byte decoded page.");
    }

    private static byte[] EncodeHeaderlessZlib(ReadOnlySpan<byte> input)
    {
        using var buffer = new MemoryStream();
        using (var compressor = new ZLibStream(
                   buffer,
                   CompressionLevel.SmallestSize,
                   leaveOpen: true))
        {
            compressor.Write(input);
        }
        byte[] zlib = buffer.ToArray();
        if (zlib.Length < 6)
        {
            throw new InvalidDataException(
                "The zlib encoder produced a frame too short " +
                "to contain CMF/FLG, Deflate, and Adler-32.");
        }

        ushort header = BinaryPrimitives.ReadUInt16BigEndian(zlib.AsSpan(0, sizeof(ushort)));
        int compressionMethod = zlib[0] & 0x0f;
        int windowInfo = zlib[0] >> 4;
        bool presetDictionary = (zlib[1] & 0x20) != 0;
        if (compressionMethod != 8 ||
            windowInfo > 7 ||
            presetDictionary ||
            header % 31 != 0)
        {
            throw new InvalidDataException(
                "The zlib encoder produced unsupported " +
                $"CMF/FLG 0x{header:X4} for a PS3 packed-zone frame.");
        }

        // The native frame replaces only CMF/FLG with its UInt16 payload
        // length. Keep the raw Deflate stream and the zlib Adler-32 trailer.
        return zlib[sizeof(ushort)..];
    }

    private static byte[] EncodeHeader(
        DbHeader envelope,
        ulong fileCreationTimeRaw,
        uint fileSize,
        uint maxFileSize)
    {
        byte[] output = new byte[ComputeHeaderLength(envelope)];
        int offset = 0;
        Encoding.Latin1.GetBytes(envelope.Magic, output.AsSpan(offset, 8));
        offset += 8;
        WriteUInt32(output, ref offset, (uint)envelope.Version);
        output[offset++] = envelope.AllowOnlineUpdate ? (byte)1 : (byte)0;
        WriteUInt64(output, ref offset, fileCreationTimeRaw);
        WriteUInt32(output, ref offset, envelope.LanguageMask);
        WriteUInt32(output, ref offset, envelope.EntryCount);
        foreach (DbHeaderImageStreamLanguageTable table in envelope.LanguageTables)
        {
            foreach (DbHeaderImageStreamEntry entry in table.ImageStreamEntries)
            {
                WriteUInt32(output, ref offset, entry.FileIndex);
                WriteUInt32(output, ref offset, entry.SourceStart);
                WriteUInt32(output, ref offset, entry.SourceEnd);
                WriteUInt32(output, ref offset, entry.BlockOffset);
                WriteUInt32(output, ref offset, entry.StreamOffset);
            }
        }
        WriteUInt32(output, ref offset, fileSize);
        WriteUInt32(output, ref offset, maxFileSize);
        if (offset != output.Length)
            throw new InvalidDataException("PS3 header writer produced an inconsistent byte count.");
        return output;
    }

    private static int ComputeHeaderLength(DbHeader envelope) => checked(
        8 + sizeof(uint) + sizeof(byte) + sizeof(ulong) + sizeof(uint) + sizeof(uint) +
        checked(envelope.LanguageTables.Length * checked((int)envelope.EntryCount) * ImageStreamEntrySize) +
        sizeof(uint) + sizeof(uint));

    private static void WriteUInt16(Stream stream, ushort value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteUInt32(byte[] output, ref int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(offset, sizeof(uint)), value);
        offset += sizeof(uint);
    }

    private static void WriteUInt64(byte[] output, ref int offset, ulong value)
    {
        BinaryPrimitives.WriteUInt64BigEndian(output.AsSpan(offset, sizeof(ulong)), value);
        offset += sizeof(ulong);
    }
}
