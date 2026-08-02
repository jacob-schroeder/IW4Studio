using System.Collections.Immutable;
using IW4.FastFiles.Database.Streaming;

namespace IW4.FastFiles.Database;

/// <summary>
/// Describes what a future fastfile writer must do with a captured header
/// field. The loader never applies these decisions; it only records them
/// explicitly alongside the source envelope.
/// </summary>
public enum DbHeaderMetadataDisposition
{
    PreserveVerbatim,
    Recompute,
    PolicyControlled
}

/// <summary>
/// Per-field write policy carried with a captured DB header. This prevents a
/// future packager from silently changing source metadata while it rebuilds a
/// container.
/// </summary>
public sealed record DbHeaderMetadataDispositions(
    DbHeaderMetadataDisposition Magic,
    DbHeaderMetadataDisposition Version,
    DbHeaderMetadataDisposition AllowOnlineUpdate,
    DbHeaderMetadataDisposition FileCreationTime,
    DbHeaderMetadataDisposition LanguageMask,
    DbHeaderMetadataDisposition ImageStreamTables,
    DbHeaderMetadataDisposition EntryCount,
    DbHeaderMetadataDisposition FileSize,
    DbHeaderMetadataDisposition MaxFileSize)
{
    /// <summary>
    /// Emits an unchanged source envelope byte-for-byte.
    /// </summary>
    public static DbHeaderMetadataDispositions PreserveSource { get; } = new(
        DbHeaderMetadataDisposition.PreserveVerbatim,
        DbHeaderMetadataDisposition.PreserveVerbatim,
        DbHeaderMetadataDisposition.PreserveVerbatim,
        DbHeaderMetadataDisposition.PreserveVerbatim,
        DbHeaderMetadataDisposition.PreserveVerbatim,
        DbHeaderMetadataDisposition.PreserveVerbatim,
        DbHeaderMetadataDisposition.PreserveVerbatim,
        DbHeaderMetadataDisposition.PreserveVerbatim,
        DbHeaderMetadataDisposition.PreserveVerbatim);

    /// <summary>
    /// The explicit default for a future rebuilt fastfile. Source-specific
    /// values remain available to policy rather than being rewritten by the
    /// loader.
    /// </summary>
    public static DbHeaderMetadataDispositions RebuildDefault { get; } = new(
        DbHeaderMetadataDisposition.PreserveVerbatim,
        DbHeaderMetadataDisposition.PreserveVerbatim,
        DbHeaderMetadataDisposition.PreserveVerbatim,
        DbHeaderMetadataDisposition.PolicyControlled,
        DbHeaderMetadataDisposition.PreserveVerbatim,
        DbHeaderMetadataDisposition.PreserveVerbatim,
        DbHeaderMetadataDisposition.PreserveVerbatim,
        DbHeaderMetadataDisposition.Recompute,
        DbHeaderMetadataDisposition.PolicyControlled);

    public bool IsSourcePreserving =>
        Magic == DbHeaderMetadataDisposition.PreserveVerbatim &&
        Version == DbHeaderMetadataDisposition.PreserveVerbatim &&
        AllowOnlineUpdate == DbHeaderMetadataDisposition.PreserveVerbatim &&
        FileCreationTime == DbHeaderMetadataDisposition.PreserveVerbatim &&
        LanguageMask == DbHeaderMetadataDisposition.PreserveVerbatim &&
        ImageStreamTables == DbHeaderMetadataDisposition.PreserveVerbatim &&
        EntryCount == DbHeaderMetadataDisposition.PreserveVerbatim &&
        FileSize == DbHeaderMetadataDisposition.PreserveVerbatim &&
        MaxFileSize == DbHeaderMetadataDisposition.PreserveVerbatim;
}

/// <summary>
/// One image-stream table in the order it was serialized in the DB header.
/// The immutable array is deliberately copied at construction so callers
/// cannot mutate a retained container envelope through their input buffer.
/// </summary>
public sealed class DbHeaderImageStreamLanguageTable
{
    public DbHeaderImageStreamLanguageTable(
        int serializedIndex,
        uint languageMask,
        IEnumerable<DbHeaderImageStreamEntry> imageStreamEntries)
    {
        if (serializedIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(serializedIndex));
        if (languageMask == 0 || (languageMask & (languageMask - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(languageMask),
                "A language table must represent exactly one language bit.");
        }

        ArgumentNullException.ThrowIfNull(imageStreamEntries);

        SerializedIndex = serializedIndex;
        LanguageMask = languageMask;
        ImageStreamEntries = imageStreamEntries.ToImmutableArray();
    }

    public int SerializedIndex { get; }
    public uint LanguageMask { get; }
    public ImmutableArray<DbHeaderImageStreamEntry> ImageStreamEntries { get; }
}

/// <summary>
/// Lossless PS3 fastfile container header. It owns immutable copies of every
/// serialized language table and the raw header byte range, allowing documents
/// to retain the source envelope without retaining an open file cursor.
/// </summary>
public sealed class DbHeader
{
    public DbHeader(
        string magic,
        XFileVersion version,
        bool allowOnlineUpdate,
        ulong fileCreationTimeRaw,
        uint languageMask,
        uint selectedLanguageMask,
        uint languageCount,
        uint selectedLanguageIndex,
        uint entryCount,
        IEnumerable<DbHeaderImageStreamLanguageTable> languageTables,
        uint fileSize,
        uint maxFileSize,
        int serializedHeaderOffset,
        ReadOnlySpan<byte> serializedHeaderBytes,
        int packedStreamOffset,
        long sourceFileLength,
        DbHeaderMetadataDispositions? metadataDispositions = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(magic);
        ArgumentNullException.ThrowIfNull(languageTables);
        if (serializedHeaderOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(serializedHeaderOffset));
        if (packedStreamOffset < serializedHeaderOffset)
            throw new ArgumentOutOfRangeException(nameof(packedStreamOffset));
        if (sourceFileLength < packedStreamOffset)
            throw new ArgumentOutOfRangeException(nameof(sourceFileLength));

        ImmutableArray<DbHeaderImageStreamLanguageTable> capturedTables =
            languageTables.ToImmutableArray();
        ValidateLanguageTables(languageMask, languageCount, entryCount, capturedTables);

        Magic = magic;
        Version = version;
        AllowOnlineUpdate = allowOnlineUpdate;
        FileCreationTimeRaw = fileCreationTimeRaw;
        LanguageMask = languageMask;
        SelectedLanguageMask = selectedLanguageMask;
        LanguageCount = languageCount;
        SelectedLanguageIndex = selectedLanguageIndex;
        EntryCount = entryCount;
        LanguageTables = capturedTables;
        SelectedLanguageTable = capturedTables.FirstOrDefault(table =>
            table.LanguageMask == selectedLanguageMask);
        ImageStreamEntries = SelectedLanguageTable?.ImageStreamEntries
            ?? ImmutableArray<DbHeaderImageStreamEntry>.Empty;
        FileSize = fileSize;
        MaxFileSize = maxFileSize;
        SerializedHeaderOffset = serializedHeaderOffset;
        SerializedHeaderBytes = ImmutableArray.Create(serializedHeaderBytes);
        PackedStreamOffset = packedStreamOffset;
        SourceFileLength = sourceFileLength;
        MetadataDispositions = metadataDispositions ?? DbHeaderMetadataDispositions.RebuildDefault;

        if (SerializedHeaderBytes.Length != packedStreamOffset - serializedHeaderOffset)
        {
            throw new InvalidDataException(
                $"DB header byte range has 0x{SerializedHeaderBytes.Length:X} bytes, " +
                $"but offsets 0x{serializedHeaderOffset:X}..0x{packedStreamOffset:X} require " +
                $"0x{packedStreamOffset - serializedHeaderOffset:X} bytes.");
        }
    }

    public string Magic { get; }
    public XFileVersion Version { get; }
    public bool AllowOnlineUpdate { get; }
    public ulong FileCreationTimeRaw { get; }
    public uint LanguageMask { get; }
    public uint SelectedLanguageMask { get; }
    public uint LanguageCount { get; }
    public uint SelectedLanguageIndex { get; }
    public uint EntryCount { get; }
    public ImmutableArray<DbHeaderImageStreamLanguageTable> LanguageTables { get; }

    public string MagicType => Magic switch
    {
        "IWffu100" => "Unsigned",
        "IWff0100" => "Signed",
        _ => throw new ArgumentOutOfRangeException()
    };

    /// <summary>
    /// The table selected by the existing loader language-selection policy, if
    /// that policy names exactly one serialized table.
    /// </summary>
    public DbHeaderImageStreamLanguageTable? SelectedLanguageTable { get; }

    /// <summary>
    /// Convenience view used by runtime image loading. It shares the selected
    /// table's immutable backing storage and does not duplicate that table.
    /// </summary>
    public ImmutableArray<DbHeaderImageStreamEntry> ImageStreamEntries { get; }

    public uint FileSize { get; }
    public uint MaxFileSize { get; }
    public int SerializedHeaderOffset { get; }
    public ImmutableArray<byte> SerializedHeaderBytes { get; }
    public int SerializedHeaderLength => SerializedHeaderBytes.Length;
    public int PackedStreamOffset { get; }
    public long SourceFileLength { get; }
    public DbHeaderMetadataDispositions MetadataDispositions { get; }

    public DateTime FileCreationTime =>
        DateTime.FromFileTimeUtc(checked((long)FileCreationTimeRaw));

    public DbHeaderImageStreamLanguageTable GetLanguageTable(uint languageMask) =>
        LanguageTables.FirstOrDefault(table => table.LanguageMask == languageMask)
        ?? throw new KeyNotFoundException(
            $"DB header contains no image-stream table for language mask 0x{languageMask:X}.");

    private static void ValidateLanguageTables(
        uint languageMask,
        uint languageCount,
        uint entryCount,
        ImmutableArray<DbHeaderImageStreamLanguageTable> languageTables)
    {
        if (languageTables.Length != languageCount)
        {
            throw new InvalidDataException(
                $"DB header declares {languageCount} language table(s), but captured {languageTables.Length}.");
        }

        for (int index = 0; index < languageTables.Length; index++)
        {
            DbHeaderImageStreamLanguageTable table = languageTables[index];
            if (table.SerializedIndex != index)
            {
                throw new InvalidDataException(
                    $"DB header language table {index} has serialized index {table.SerializedIndex}.");
            }
            if ((languageMask & table.LanguageMask) == 0)
            {
                throw new InvalidDataException(
                    $"DB header language table {index} has mask 0x{table.LanguageMask:X} outside header mask 0x{languageMask:X}.");
            }
            if (table.ImageStreamEntries.Length != entryCount)
            {
                throw new InvalidDataException(
                    $"DB header language table {index} has {table.ImageStreamEntries.Length} entry/entries, " +
                    $"but EntryCount is {entryCount}.");
            }
        }

        if (languageTables.Select(table => table.LanguageMask).Distinct().Count() != languageTables.Length)
            throw new InvalidDataException("DB header contains duplicate language-table masks.");
    }
}
