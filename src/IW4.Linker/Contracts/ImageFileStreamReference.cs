using IW4.Assets.Assets.Image;
using IW4.FastFiles.Database;
using IW4.FastFiles.Database.Streaming;

namespace IW4.Linker.Contracts;

/// <summary>
/// Immutable read-only reference to one native imagefile stream range. The
/// five DB-header wire fields are preserved exactly; the source header's
/// serialized offset is deliberately discarded.
/// </summary>
public sealed class ImageFileStreamReference
{
    public ImageFileStreamReference(
        DbHeaderImageStreamEntry entry,
        int byteLength)
    {
        if (byteLength < 0)
            throw new ArgumentOutOfRangeException(nameof(byteLength));

        if (byteLength == 0)
        {
            if (entry.FileIndex != 0 ||
                entry.SourceStart != 0 ||
                entry.SourceEnd != 0 ||
                entry.BlockOffset != 0 ||
                entry.StreamOffset != 0)
            {
                throw new ArgumentException(
                    "An empty imagefile reference requires five zero wire fields.",
                    nameof(entry));
            }
        }
        else if (entry.IsEmpty)
        {
            throw new ArgumentException(
                "A nonempty imagefile reference requires a nonempty DB-header entry.",
                nameof(entry));
        }

        Entry = entry with { SerializedOffset = -1 };
        ByteLength = byteLength;
    }

    public DbHeaderImageStreamEntry Entry { get; }
    public int ByteLength { get; }
    public bool IsEmpty => ByteLength == 0;
}

/// <summary>
/// The four ordered read-only stream references for one emitted GfxImage in
/// one PS3 language table.
/// </summary>
public sealed class ImageFileStreamLanguageReferences
{
    private readonly IReadOnlyList<ImageFileStreamReference> _references;

    public ImageFileStreamLanguageReferences(
        uint languageMask,
        IEnumerable<ImageFileStreamReference> references)
    {
        if (!DbLanguageMask.IsSingleLanguage(languageMask))
        {
            throw new ArgumentOutOfRangeException(
                nameof(languageMask),
                "An imagefile reference contribution requires exactly one language bit.");
        }
        ArgumentNullException.ThrowIfNull(references);

        ImageFileStreamReference[] copied = references
            .Select(reference => reference ?? throw new ArgumentException(
                "Imagefile language references cannot contain null.",
                nameof(references)))
            .ToArray();
        if (copied.Length != GfxImageStreamData.EntryCount)
        {
            throw new ArgumentException(
                $"A GfxImage requires exactly {GfxImageStreamData.EntryCount} imagefile references per language.",
                nameof(references));
        }

        LanguageMask = languageMask;
        _references = Array.AsReadOnly(copied);
    }

    public uint LanguageMask { get; }
    public IReadOnlyList<ImageFileStreamReference> References => _references;
}
