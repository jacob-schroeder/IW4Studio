using IW4.Assets.Assets.Image;
using IW4.FastFiles.Database;
using IW4.FastFiles.Database.Streaming;
using IW4.Linker.Contracts;

namespace IW4.FastFiles.Loaders.Streaming.Images;

/// <summary>
/// Freezes one loaded zone's all-language DB-header imagefile references. It
/// preserves the five native wire fields without opening or reading any
/// referenced imagefile package.
/// </summary>
internal sealed class LinkGfxImageStreamSource
{
    private readonly DbHeader _header;

    public LinkGfxImageStreamSource(DbHeader header)
    {
        _header = header ?? throw new ArgumentNullException(nameof(header));
    }

    public IReadOnlyList<ImageFileStreamLanguageReferences> Freeze(
        GfxImageAsset image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.StreamImageIndex is not { } imageIndex || imageIndex < 0)
        {
            throw new InvalidDataException(
                $"Streamed GfxImage '{image.Name}' has no valid native stream index.");
        }

        int[] byteLengths =
            GfxImageStreamData.ValidateProfileAndComputePartByteCounts(image.StreamData);
        if (byteLengths.All(byteLength => byteLength == 0))
        {
            throw new InvalidDataException(
                $"Streamed GfxImage '{image.Name}' has no stream payload bytes.");
        }

        var result =
            new ImageFileStreamLanguageReferences[_header.LanguageTables.Length];
        for (int languageIndex = 0; languageIndex < result.Length; languageIndex++)
        {
            DbHeaderImageStreamLanguageTable language =
                _header.LanguageTables[languageIndex];
            var references =
                new ImageFileStreamReference[GfxImageStreamData.EntryCount];
            int firstEntryIndex = checked(
                imageIndex * GfxImageStreamData.EntryCount);
            int endEntryIndex = checked(
                firstEntryIndex + GfxImageStreamData.EntryCount);
            if (endEntryIndex > language.ImageStreamEntries.Length)
            {
                throw new InvalidDataException(
                    $"Streamed GfxImage '{image.Name}' entries " +
                    $"0x{firstEntryIndex:X}..0x{endEntryIndex:X} are outside " +
                    $"language table 0x{language.LanguageMask:X}.");
            }

            for (int partIndex = 0; partIndex < references.Length; partIndex++)
            {
                DbHeaderImageStreamEntry entry =
                    language.ImageStreamEntries[firstEntryIndex + partIndex];
                references[partIndex] = new ImageFileStreamReference(
                    entry,
                    byteLengths[partIndex]);
            }

            result[languageIndex] = new ImageFileStreamLanguageReferences(
                language.LanguageMask,
                references);
        }

        return Array.AsReadOnly(result);
    }
}
