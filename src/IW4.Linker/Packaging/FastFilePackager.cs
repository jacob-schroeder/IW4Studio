using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using System.Text;
using IW4.FastFiles.Database;
using IW4.FastFiles.Database.Streaming;

namespace IW4.Linker.Packaging;

public enum FastFileMaxFileSizePolicy
{
    AtLeastFileSize,
    ExactFileSize
}

/// <summary>The deterministic container metadata policy owned by the Linker.</summary>
public sealed record FastFilePackagingPolicy(
    ulong? FileCreationTimeRaw = null,
    FastFileMaxFileSizePolicy MaxFileSizePolicy = FastFileMaxFileSizePolicy.AtLeastFileSize,
    bool EmitDoubleTerminator = true)
{
    public static FastFilePackagingPolicy Canonical { get; } = new();
}

public sealed record FastFilePackagingError(string Code, string Message);

public sealed class FastFilePackagingResult
{
    private readonly byte[]? _bytes;

    private FastFilePackagingResult(byte[]? bytes, IEnumerable<FastFilePackagingError> errors)
    {
        _bytes = bytes;
        Errors = Array.AsReadOnly(errors.ToArray());
    }

    public bool Succeeded => _bytes is not null;
    public ReadOnlyMemory<byte>? Bytes => _bytes is null ? null : new ReadOnlyMemory<byte>(_bytes);
    public IReadOnlyList<FastFilePackagingError> Errors { get; }

    internal static FastFilePackagingResult Success(byte[] bytes) => new(bytes, []);
    internal static FastFilePackagingResult Failure(IEnumerable<FastFilePackagingError> errors) => new(null, errors);
}

/// <summary>
/// Pure PS3 DB-header and packed-page writer. Full incompressible 64 KiB
/// pages use the native raw-frame marker; compressed frames retain the zlib
/// Adler-32 trailer after removing only CMF/FLG.
/// </summary>
public sealed class FastFilePackager
{
    private const int DecodedPageSize = 0x10000;
    private const int ImageStreamEntrySize = 0x14;
    private const ushort TerminatorWord = 1;
    private const uint SupportedLanguageMask = (1u << 15) - 1;
    private const string RequiredMagic = "IWffu100";

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
            byte[] packedStream = EncodePackedStream(decodedZone.Span, policy.EmitDoubleTerminator);
            int headerLength = ComputeHeaderLength(envelope);
            int trailingPhysicalBytes = policy.EmitDoubleTerminator ? sizeof(ushort) : 0;
            uint fileSize = checked((uint)(headerLength + packedStream.Length - trailingPhysicalBytes));
            uint maxFileSize = policy.MaxFileSizePolicy switch
            {
                FastFileMaxFileSizePolicy.AtLeastFileSize => Math.Max(envelope.MaxFileSize, fileSize),
                FastFileMaxFileSizePolicy.ExactFileSize => fileSize,
                _ => throw new InvalidDataException($"Unknown {nameof(FastFileMaxFileSizePolicy)} '{policy.MaxFileSizePolicy}'.")
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
            errors.Add(new FastFilePackagingError("package.layout", exception.Message));
            return FastFilePackagingResult.Failure(errors);
        }
    }

    private static bool TryValidateEnvelope(DbHeader envelope, List<FastFilePackagingError> errors)
    {
        void Error(string code, string message) => errors.Add(new FastFilePackagingError(code, message));

        if (!string.Equals(envelope.Magic, RequiredMagic, StringComparison.Ordinal) ||
            Encoding.Latin1.GetByteCount(envelope.Magic) != 8)
        {
            Error("header.magic", $"PS3 packaging requires the eight-byte magic '{RequiredMagic}'.");
        }
        if (envelope.LanguageMask == 0 || (envelope.LanguageMask & ~SupportedLanguageMask) != 0)
            Error("header.languageMask", "Language mask must contain supported PS3 language bits.");
        if (BitOperations.PopCount(envelope.LanguageMask) != envelope.LanguageTables.Length ||
            envelope.LanguageCount != envelope.LanguageTables.Length ||
            envelope.SelectedLanguageMask == 0 ||
            (envelope.SelectedLanguageMask & envelope.LanguageMask) == 0)
        {
            Error("header.languageTables", "Language mask, selected language, count, and serialized tables disagree.");
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
                Error("header.languageTables", $"Language table {index} is not representable in PS3 header order.");
            }
        }

        return errors.Count == 0;
    }

    private static byte[] EncodePackedStream(ReadOnlySpan<byte> decodedZone, bool emitDoubleTerminator)
    {
        using var output = new MemoryStream();
        for (int offset = 0; offset < decodedZone.Length; offset += DecodedPageSize)
        {
            ReadOnlySpan<byte> page = decodedZone.Slice(offset, Math.Min(DecodedPageSize, decodedZone.Length - offset));
            WriteDecodedPage(output, page);
        }

        WriteUInt16(output, TerminatorWord);
        if (emitDoubleTerminator)
            WriteUInt16(output, TerminatorWord);
        return output.ToArray();
    }

    private static void WriteDecodedPage(Stream output, ReadOnlySpan<byte> page)
    {
        byte[] compressed = EncodeHeaderlessZlib(page);
        if (compressed.Length is > 1 and <= ushort.MaxValue)
        {
            WriteUInt16(output, checked((ushort)compressed.Length));
            output.Write(compressed);
            return;
        }
        if (page.Length == DecodedPageSize)
        {
            WriteUInt16(output, 0);
            output.Write(page);
            return;
        }
        throw new InvalidDataException("A partial decoded page cannot be represented by one PS3 packed frame.");
    }

    private static byte[] EncodeHeaderlessZlib(ReadOnlySpan<byte> input)
    {
        using var buffer = new MemoryStream();
        using (var compressor = new ZLibStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
            compressor.Write(input);
        byte[] zlib = buffer.ToArray();
        if (zlib.Length < 6)
            throw new InvalidDataException("The zlib encoder produced an invalid PS3 packed frame.");

        ushort header = BinaryPrimitives.ReadUInt16BigEndian(zlib.AsSpan(0, sizeof(ushort)));
        if ((zlib[0] & 0x0f) != 8 || (zlib[0] >> 4) > 7 || (zlib[1] & 0x20) != 0 || header % 31 != 0)
            throw new InvalidDataException("The zlib encoder produced unsupported CMF/FLG for a PS3 packed frame.");
        return zlib[sizeof(ushort)..];
    }

    private static byte[] EncodeHeader(DbHeader envelope, ulong creationTime, uint fileSize, uint maxFileSize)
    {
        byte[] output = new byte[ComputeHeaderLength(envelope)];
        int offset = 0;
        Encoding.Latin1.GetBytes(envelope.Magic, output.AsSpan(offset, 8));
        offset += 8;
        WriteUInt32(output, ref offset, (uint)envelope.Version);
        output[offset++] = envelope.AllowOnlineUpdate ? (byte)1 : (byte)0;
        WriteUInt64(output, ref offset, creationTime);
        WriteUInt32(output, ref offset, envelope.LanguageMask);
        WriteUInt32(output, ref offset, envelope.EntryCount);
        foreach (DbHeaderImageStreamLanguageTable table in envelope.LanguageTables)
        foreach (DbHeaderImageStreamEntry entry in table.ImageStreamEntries)
        {
            WriteUInt32(output, ref offset, entry.FileIndex);
            WriteUInt32(output, ref offset, entry.SourceStart);
            WriteUInt32(output, ref offset, entry.SourceEnd);
            WriteUInt32(output, ref offset, entry.BlockOffset);
            WriteUInt32(output, ref offset, entry.StreamOffset);
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

    private static void WriteUInt16(Stream output, ushort value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        output.Write(bytes);
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
