using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using IW4.Assets.Assets.Image;
using IW4.FastFiles.Database;
using IW4.FastFiles.Database.Streaming;

namespace IW4.Linker.Packaging;

public enum FastFileMaxFileSizePolicy
{
    AtLeastFileSize,
    ExactFileSize
}

/// <summary>The deterministic container layout policy owned by the Linker.</summary>
public sealed record FastFilePackagingPolicy(
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
    /// <summary>
    /// Packages a source-independent PS3 zone with a newly rebuilt DB header.
    /// Empty image tables support every stock language-mask combination.
    /// Streamed zones preserve one complete, equal-cardinality table of
    /// read-only external imagefile references per language.
    /// </summary>
    public FastFilePackagingResult PackageGreenfield(
        ReadOnlyMemory<byte> decodedZone,
        uint languageMask,
        uint selectedLanguageMask,
        IEnumerable<DbHeaderImageStreamLanguageTable>? imageStreamLanguageTables = null,
        FastFilePackagingPolicy? policy = null,
        DbHeaderAuthoringMetadata? headerMetadata = null)
    {
        try
        {
            DbHeader envelope = CreateGreenfieldEnvelope(
                languageMask,
                selectedLanguageMask,
                imageStreamLanguageTables,
                headerMetadata ?? DbHeaderAuthoringMetadata.Canonical);
            return Package(decodedZone, envelope, policy);
        }
        catch (Exception exception) when (exception is
            OverflowException or
            ArgumentException or
            InvalidDataException)
        {
            return FastFilePackagingResult.Failure([
                new FastFilePackagingError("package.greenfieldHeader", exception.Message)
            ]);
        }
    }

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
            PackedStream packedStream = PackedStreamEncoder.Encode(
                decodedZone.Span,
                policy.EmitDoubleTerminator ? 2 : 1,
                CompressionLevel.SmallestSize);
            int headerLength = ComputeHeaderLength(envelope);
            int trailingPhysicalBytes = policy.EmitDoubleTerminator ? sizeof(ushort) : 0;
            uint fileSize = checked((uint)(
                headerLength + packedStream.Bytes.Length - trailingPhysicalBytes));
            uint maxFileSize = policy.MaxFileSizePolicy switch
            {
                FastFileMaxFileSizePolicy.AtLeastFileSize => Math.Max(envelope.MaxFileSize, fileSize),
                FastFileMaxFileSizePolicy.ExactFileSize => fileSize,
                _ => throw new InvalidDataException($"Unknown {nameof(FastFileMaxFileSizePolicy)} '{policy.MaxFileSizePolicy}'.")
            };

            byte[] header = EncodeHeader(
                envelope,
                fileSize,
                maxFileSize);
            byte[] output = new byte[checked(header.Length + packedStream.Bytes.Length)];
            header.CopyTo(output, 0);
            packedStream.Bytes.CopyTo(output, header.Length);
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

        if (!DbHeaderAuthoringMetadata.IsSupportedMagic(envelope.Magic))
        {
            Error(
                "header.magic",
                $"PS3 packaging requires the eight-byte magic '{DbHeader.UnsignedMagic}'.");
        }
        if (!DbHeaderAuthoringMetadata.IsSupportedVersion(envelope.Version))
        {
            Error(
                "header.version",
                $"PS3 IW4 packaging requires version {(uint)XFileVersion.ModernWarfare2}.");
        }
        if (!DbLanguageMask.IsSupported(envelope.LanguageMask))
            Error("header.languageMask", "Language mask must contain supported PS3 language bits.");
        if (BitOperations.PopCount(envelope.LanguageMask) != envelope.LanguageTables.Length ||
            envelope.LanguageCount != envelope.LanguageTables.Length ||
            !DbLanguageMask.IsSingleLanguage(envelope.SelectedLanguageMask) ||
            (envelope.SelectedLanguageMask & envelope.LanguageMask) == 0)
        {
            Error("header.languageTables", "Language mask, selected language, count, and serialized tables disagree.");
        }

        for (int index = 0; index < envelope.LanguageTables.Length; index++)
        {
            DbHeaderImageStreamLanguageTable table = envelope.LanguageTables[index];
            if (table.SerializedIndex != index ||
                !DbLanguageMask.IsSingleLanguage(table.LanguageMask) ||
                (table.LanguageMask & envelope.LanguageMask) == 0 ||
                table.ImageStreamEntries.Length != envelope.EntryCount)
            {
                Error("header.languageTables", $"Language table {index} is not representable in PS3 header order.");
            }

            for (int entryIndex = 0;
                 entryIndex < table.ImageStreamEntries.Length;
                 entryIndex++)
            {
                string? entryError = GetExternalStreamEntryError(
                    table.ImageStreamEntries[entryIndex]);
                if (entryError is not null)
                {
                    Error(
                        "header.imageStreamEntry",
                        $"Language table {index} entry {entryIndex}: {entryError}");
                }
            }
        }

        return errors.Count == 0;
    }

    private static DbHeader CreateGreenfieldEnvelope(
        uint languageMask,
        uint selectedLanguageMask,
        IEnumerable<DbHeaderImageStreamLanguageTable>? languageTablesSource,
        DbHeaderAuthoringMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (!DbLanguageMask.IsSupported(languageMask))
        {
            throw new ArgumentOutOfRangeException(
                nameof(languageMask),
                "A greenfield PS3 language mask must contain supported language bits.");
        }
        if (!DbLanguageMask.IsSingleLanguage(selectedLanguageMask) ||
            (selectedLanguageMask & languageMask) == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(selectedLanguageMask),
                "The selected language must be one bit present in the language mask.");
        }

        uint[] languageBits = Enumerable.Range(0, DbLanguageMask.BitCount)
            .Select(bit => 1u << bit)
            .Where(bit => (languageMask & bit) != 0)
            .ToArray();
        DbHeaderImageStreamLanguageTable[] supplied = languageTablesSource?
            .Select(table => table ?? throw new ArgumentException(
                "Image stream language tables cannot contain null.",
                nameof(languageTablesSource)))
            .ToArray() ?? [];
        Dictionary<uint, DbHeaderImageStreamLanguageTable> suppliedByMask =
            supplied.ToDictionary(table => table.LanguageMask);
        if (supplied.Length != 0 &&
            (suppliedByMask.Count != languageBits.Length ||
             languageBits.Any(bit => !suppliedByMask.ContainsKey(bit)) ||
             suppliedByMask.Keys.Any(bit => (languageMask & bit) == 0)))
        {
            throw new InvalidDataException(
                "Imagefile references must provide exactly one table for every zone language.");
        }

        var tables = new DbHeaderImageStreamLanguageTable[languageBits.Length];
        int? entryCount = null;
        for (int index = 0; index < tables.Length; index++)
        {
            uint bit = languageBits[index];
            DbHeaderImageStreamEntry[] entries = supplied.Length == 0
                ? []
                : suppliedByMask[bit].ImageStreamEntries
                    .Select(FreezeExternalStreamEntry)
                    .ToArray();
            if (entries.Length > DbHeader.MaximumImageStreamEntryCount ||
                entries.Length % GfxImageStreamData.EntryCount != 0)
            {
                throw new InvalidDataException(
                    $"An imagefile reference table requires {GfxImageStreamData.EntryCount} " +
                    "entries per streamed image and at most " +
                    $"0x{DbHeader.MaximumImageStreamEntryCount:X} entries.");
            }
            if (entryCount is { } expected && entries.Length != expected)
            {
                throw new InvalidDataException(
                    "Every imagefile reference language table must have equal cardinality.");
            }
            entryCount ??= entries.Length;
            tables[index] = new DbHeaderImageStreamLanguageTable(
                index,
                bit,
                entries);
        }

        int selectedLanguageIndex = Array.IndexOf(languageBits, selectedLanguageMask);
        if (selectedLanguageIndex < 0)
            throw new InvalidDataException("Selected language is absent from the rebuilt table order.");

        return new DbHeader(
            magic: metadata.Magic,
            version: metadata.Version,
            allowOnlineUpdate: metadata.AllowOnlineUpdate,
            fileCreationTimeRaw: metadata.FileCreationTimeRaw,
            languageMask: languageMask,
            selectedLanguageMask: selectedLanguageMask,
            languageCount: checked((uint)tables.Length),
            selectedLanguageIndex: checked((uint)selectedLanguageIndex),
            entryCount: checked((uint)(entryCount ?? 0)),
            languageTables: tables,
            fileSize: 0,
            maxFileSize: metadata.MaxFileSize,
            serializedHeaderOffset: 0,
            serializedHeaderBytes: [],
            packedStreamOffset: 0,
            sourceFileLength: 0,
            metadataDispositions: DbHeaderMetadataDispositions.RebuildDefault);
    }

    private static DbHeaderImageStreamEntry FreezeExternalStreamEntry(
        DbHeaderImageStreamEntry entry)
    {
        string? error = GetExternalStreamEntryError(entry);
        if (error is not null)
            throw new InvalidDataException(error);

        return new DbHeaderImageStreamEntry(
            entry.FileIndex,
            entry.SourceStart,
            entry.SourceEnd,
            entry.BlockOffset,
            entry.StreamOffset,
            SerializedOffset: -1);
    }

    private static string? GetExternalStreamEntryError(
        DbHeaderImageStreamEntry entry)
    {
        if (entry.IsEmpty)
        {
            if (entry.FileIndex != 0 ||
                entry.SourceStart != 0 ||
                entry.SourceEnd != 0 ||
                entry.BlockOffset != 0 ||
                entry.StreamOffset != 0)
            {
                return "An empty image-stream entry must contain five zero wire fields.";
            }
        }
        else
        {
            if (entry.FileIndex == 0)
            {
                return "A nonempty image-stream entry must reference an external imagefile with a nonzero file index.";
            }
            if (entry.SourceEnd <= entry.SourceStart)
            {
                return "A nonempty image-stream entry requires a positive physical source range.";
            }
        }

        return (entry.StreamOffset & 0xffff) != entry.BlockOffset
            ? "An image-stream entry block offset must equal the low 16 bits of its stream offset."
            : null;
    }

    private static byte[] EncodeHeader(DbHeader envelope, uint fileSize, uint maxFileSize)
    {
        byte[] output = new byte[ComputeHeaderLength(envelope)];
        PackageFormat.WritePrefix(output, envelope.Magic, envelope.Version);
        int offset = DbHeader.UnsignedPrefixLength;
        output[offset++] = envelope.AllowOnlineUpdate ? (byte)1 : (byte)0;
        WriteUInt64(output, ref offset, envelope.FileCreationTimeRaw);
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
        DbHeader.UnsignedPrefixLength + sizeof(byte) + sizeof(ulong) + sizeof(uint) + sizeof(uint) +
        checked(envelope.LanguageTables.Length * checked((int)envelope.EntryCount) * DbHeaderImageStreamEntry.SerializedSize) +
        sizeof(uint) + sizeof(uint));

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
