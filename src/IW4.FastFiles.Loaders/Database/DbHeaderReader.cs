using System.Numerics;
using IW4.FastFiles.Database;
using IW4.FastFiles.Database.Streaming;
using IW4.Runtime.Database;
using IW4.Runtime.IO;

namespace IW4.FastFiles.Loaders.Database;

public sealed class DbHeaderReader
{
    public DbHeader Read(FastFileCursor cursor, DbLoadContext context)
    {
        int startOffset = cursor.Offset;
        string magic = cursor.ReadFixedString(DbHeader.MagicByteLength);
        if (magic != DbHeader.UnsignedMagic)
            throw new InvalidDataException($"Expected PS3 fastfile magic '{DbHeader.UnsignedMagic}' at 0x{startOffset:X}, got '{magic}'.");

        XFileVersion version = (XFileVersion)cursor.ReadUInt32();

        bool allowOnlineUpdate = cursor.ReadByte() switch
        {
            0 => false,
            1 => true,
            var value => throw new InvalidDataException($"Invalid allowOnlineUpdate byte {value} at 0x{cursor.Offset - 1:X}.")
        };

        ulong fileCreationTimeRaw = cursor.ReadUInt64();
        uint languageMask = cursor.ReadUInt32();

        ResolveSelectedLanguage(languageMask, context);
        LanguageScan languageScan = ScanLanguages(languageMask, context.SelectedLanguageMask);

        uint entryCount = cursor.ReadUInt32();
        if (entryCount > DbHeader.MaximumImageStreamEntryCount)
            throw new InvalidDataException($"DB header EntryCount 0x{entryCount:X} exceeds PS3 maximum 0x{DbHeader.MaximumImageStreamEntryCount:X}.");

        DbHeaderImageStreamLanguageTable[] languageTables = ReadLanguageTables(
            cursor,
            context.SelectedLanguageMask,
            languageMask,
            entryCount,
            context);

        uint fileSize = cursor.ReadUInt32();
        uint maxFileSize = cursor.ReadUInt32();

        var header = new DbHeader(
            magic: magic,
            version: version,
            allowOnlineUpdate: allowOnlineUpdate,
            fileCreationTimeRaw: fileCreationTimeRaw,
            languageMask: languageMask,
            selectedLanguageMask: context.SelectedLanguageMask,
            languageCount: languageScan.LanguageCount,
            selectedLanguageIndex: languageScan.SelectedLanguageIndex,
            entryCount: entryCount,
            languageTables: languageTables,
            fileSize: fileSize,
            maxFileSize: maxFileSize,
            serializedHeaderOffset: startOffset,
            serializedHeaderBytes: cursor.CopyBytes(startOffset, cursor.Offset - startOffset),
            packedStreamOffset: cursor.Offset,
            sourceFileLength: cursor.Length);

        context.Header = header;
        return header;
    }

    private static void ResolveSelectedLanguage(uint headerLanguageMask, DbLoadContext context)
    {
        if (!DbLanguageMask.IsSupported(headerLanguageMask))
        {
            throw new InvalidDataException(
                "Fastfile language mask must contain only supported PS3 IW4 language bits.");
        }

        if (context.SelectedLanguageMask != 0 &&
            !DbLanguageMask.IsSingleLanguage(context.SelectedLanguageMask))
        {
            throw new InvalidDataException(
                "A selected language must contain exactly one supported PS3 IW4 language bit.");
        }

        if (context.SelectedLanguageMask == 0)
        {
            context.SelectedLanguageMask =
                1u << BitOperations.TrailingZeroCount(headerLanguageMask);
            return;
        }

        if ((headerLanguageMask & context.SelectedLanguageMask) == 0)
            throw new InvalidDataException($"Fastfile language mask 0x{headerLanguageMask:X} does not contain selected language 0x{context.SelectedLanguageMask:X}.");
    }

    private static LanguageScan ScanLanguages(uint headerLanguageMask, uint selectedLanguageMask)
    {
        uint languageCount = 0;
        uint selectedLanguageIndex = 0;

        for (int bitIndex = 0; bitIndex < DbLanguageMask.BitCount; bitIndex++)
        {
            uint bit = 1u << bitIndex;
            if ((headerLanguageMask & bit) == 0)
                continue;

            if (bit == selectedLanguageMask)
                selectedLanguageIndex = languageCount;

            languageCount++;
        }

        return new LanguageScan(languageCount, selectedLanguageIndex);
    }

    private static DbHeaderImageStreamLanguageTable[] ReadLanguageTables(
        FastFileCursor cursor,
        uint selectedLanguageMask,
        uint headerLanguageMask,
        uint entryCount,
        DbLoadContext context)
    {
        var languageTables = new List<DbHeaderImageStreamLanguageTable>();

        for (int bitIndex = 0; bitIndex < DbLanguageMask.BitCount; bitIndex++)
        {
            uint bit = 1u << bitIndex;
            if ((headerLanguageMask & bit) == 0)
                continue;

            var entries = new DbHeaderImageStreamEntry[entryCount];
            bool isSelectedLanguage = bit == selectedLanguageMask;
            for (int entryIndex = 0; entryIndex < entries.Length; entryIndex++)
                entries[entryIndex] = ReadImageStreamEntry(cursor, context, isSelectedLanguage);

            languageTables.Add(new DbHeaderImageStreamLanguageTable(
                languageTables.Count,
                bit,
                entries));
        }

        return [.. languageTables];
    }

    private static DbHeaderImageStreamEntry ReadImageStreamEntry(
        FastFileCursor cursor,
        DbLoadContext context,
        bool emitSelectedLanguageDiagnostics)
    {
        int offset = cursor.Offset;
        var entry = new DbHeaderImageStreamEntry(
            FileIndex: cursor.ReadUInt32(),
            SourceStart: cursor.ReadUInt32(),
            SourceEnd: cursor.ReadUInt32(),
            BlockOffset: cursor.ReadUInt32(),
            StreamOffset: cursor.ReadUInt32(),
            SerializedOffset: offset);

        if (emitSelectedLanguageDiagnostics && entry.SourceEnd != 0 && entry.SourceEnd < entry.SourceStart)
            context.Diagnostics.Warn($"Image stream entry at 0x{offset:X} has SourceEnd before SourceStart.");

        if (emitSelectedLanguageDiagnostics && (entry.StreamOffset & 0xffff) != entry.BlockOffset)
            context.Diagnostics.Warn($"Image stream entry at 0x{offset:X} has StreamOffset low16 0x{entry.StreamOffset & 0xffff:X} != BlockOffset 0x{entry.BlockOffset:X}.");

        return entry;
    }
}
