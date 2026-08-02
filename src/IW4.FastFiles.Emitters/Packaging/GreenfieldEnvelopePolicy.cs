using System.Numerics;
using IW4.Assets.Assets.Image;
using IW4.FastFiles.Database;
using IW4.FastFiles.Database.Streaming;

namespace IW4.FastFiles.Emitters.Packaging;

/// <summary>
/// Explicit language selection for a source-independent PS3 container.
/// Tables are serialized in ascending language-bit order.
/// </summary>
public sealed record GreenfieldLanguagePolicy
{
    private const uint SupportedLanguageMask = (1u << 15) - 1;

    public GreenfieldLanguagePolicy(
        uint languageMask,
        uint selectedLanguageMask)
    {
        if (languageMask == 0 || (languageMask & ~SupportedLanguageMask) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(languageMask),
                "A greenfield PS3 language mask must contain one or more of the 15 supported language bits.");
        }
        if (selectedLanguageMask == 0 ||
            (selectedLanguageMask & (selectedLanguageMask - 1)) != 0 ||
            (selectedLanguageMask & languageMask) == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(selectedLanguageMask),
                "The selected language must be exactly one bit represented by the greenfield language mask.");
        }

        LanguageMask = languageMask;
        SelectedLanguageMask = selectedLanguageMask;
    }

    public static GreenfieldLanguagePolicy English { get; } = new(0x1, 0x1);

    public uint LanguageMask { get; }

    public uint SelectedLanguageMask { get; }

    internal int LanguageCount => BitOperations.PopCount(LanguageMask);
}

/// <summary>
/// Declares whether a new container may address image bytes in packages that
/// already exist beside the fastfile. This policy never authorizes copying or
/// rewriting those packages.
/// </summary>
public enum GreenfieldSidecarPolicy
{
    Disallow = 0,

    /// <summary>
    /// Emit exact, detached DB-header coordinates for existing imagefile
    /// packages. Until captured image build data carries its source-language
    /// identity, nonempty tables are limited to the canonical English
    /// language policy. The caller remains responsible for package
    /// availability.
    /// </summary>
    ReferenceExistingImagePackages = 1
}

/// <summary>
/// Deterministic metadata decisions for a PS3 fastfile that has no imported
/// container authority. File size is recomputed by the packager and no
/// image-stream table entries are inferred from a filesystem.
/// </summary>
public sealed class GreenfieldContainerPolicy
{
    public const string Ps3Magic = "IWffu100";

    public GreenfieldContainerPolicy(
        GreenfieldLanguagePolicy? languagePolicy = null,
        GreenfieldSidecarPolicy sidecarPolicy = GreenfieldSidecarPolicy.Disallow,
        ulong fileCreationTimeRaw = 0,
        bool allowOnlineUpdate = false)
    {
        if (!Enum.IsDefined(sidecarPolicy))
            throw new ArgumentOutOfRangeException(nameof(sidecarPolicy));
        try
        {
            _ = DateTime.FromFileTimeUtc(checked((long)fileCreationTimeRaw));
        }
        catch (Exception exception) when (
            exception is OverflowException or ArgumentOutOfRangeException)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fileCreationTimeRaw),
                fileCreationTimeRaw,
                "Greenfield file creation time must be a valid deterministic UTC FILETIME.");
        }

        LanguagePolicy = languagePolicy ?? GreenfieldLanguagePolicy.English;
        SidecarPolicy = sidecarPolicy;
        FileCreationTimeRaw = fileCreationTimeRaw;
        AllowOnlineUpdate = allowOnlineUpdate;
    }

    public static GreenfieldContainerPolicy Canonical { get; } = new();

    public XFileVersion Version => XFileVersion.ModernWarfare2;

    public bool AllowOnlineUpdate { get; }

    public ulong FileCreationTimeRaw { get; }

    public GreenfieldLanguagePolicy LanguagePolicy { get; }

    public GreenfieldSidecarPolicy SidecarPolicy { get; }

    /// <summary>
    /// Canonical package metadata is independent of wall-clock time and
    /// source headroom. The native double terminator is emitted explicitly.
    /// </summary>
    public FastFilePackagingPolicy PackagingPolicy => new(
        FileCreationTimeRaw: FileCreationTimeRaw,
        MaxFileSizePolicy: FastFileMaxFileSizePolicy.ExactFileSize,
        EmitDoubleTerminator: true);
}

/// <summary>
/// Creates an immutable greenfield header descriptor without retaining source
/// bytes, filesystem paths, or source-file length. Optional image-stream
/// records are rebuilt from detached coordinates in emitted-image order.
/// </summary>
public static class GreenfieldEnvelopeFactory
{
    private const int MaximumImageStreamEntryCount = 0x3800;

    private static readonly DbHeaderMetadataDispositions GreenfieldMetadata = new(
        DbHeaderMetadataDisposition.PolicyControlled,
        DbHeaderMetadataDisposition.PolicyControlled,
        DbHeaderMetadataDisposition.PolicyControlled,
        DbHeaderMetadataDisposition.PolicyControlled,
        DbHeaderMetadataDisposition.PolicyControlled,
        DbHeaderMetadataDisposition.PolicyControlled,
        DbHeaderMetadataDisposition.PolicyControlled,
        DbHeaderMetadataDisposition.Recompute,
        DbHeaderMetadataDisposition.PolicyControlled);

    public static DbHeader Create(GreenfieldContainerPolicy? policy = null) =>
        Create(
            policy ?? GreenfieldContainerPolicy.Canonical,
            []);

    public static DbHeader Create(
        GreenfieldContainerPolicy policy,
        IEnumerable<DbHeaderImageStreamEntry>
            selectedLanguageImageStreamEntries)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(
            selectedLanguageImageStreamEntries);

        DbHeaderImageStreamEntry[] selectedEntries =
            selectedLanguageImageStreamEntries
                .Select(entry =>
                    new DbHeaderImageStreamEntry(
                        entry.FileIndex,
                        entry.SourceStart,
                        entry.SourceEnd,
                        entry.BlockOffset,
                        entry.StreamOffset,
                        SerializedOffset: -1))
                .ToArray();
        if (selectedEntries.Length >
                MaximumImageStreamEntryCount ||
            selectedEntries.Length %
                GfxImageStreamData.EntryCount != 0)
        {
            throw new InvalidDataException(
                "A rebuilt PS3 image-stream table must contain exactly " +
                $"{GfxImageStreamData.EntryCount} records per streamed " +
                "GfxImage and cannot exceed the native " +
                $"0x{MaximumImageStreamEntryCount:X}-record limit.");
        }
        if (selectedEntries.Length != 0 &&
            policy.SidecarPolicy !=
                GreenfieldSidecarPolicy
                    .ReferenceExistingImagePackages)
        {
            throw new InvalidDataException(
                "The linked zone contains streamed GfxImages, but its " +
                "container policy does not permit references to existing " +
                "imagefile packages.");
        }
        if (selectedEntries.Length != 0 &&
            policy.LanguagePolicy.LanguageCount != 1)
        {
            throw new InvalidDataException(
                "Selected-language image-stream coordinates can only build " +
                "a single-language greenfield container.");
        }
        if (selectedEntries.Length != 0 &&
            policy.LanguagePolicy.SelectedLanguageMask !=
                GreenfieldLanguagePolicy.English.SelectedLanguageMask)
        {
            throw new InvalidDataException(
                "References to existing imagefile packages currently require " +
                "the canonical English selected-language mask because " +
                "captured GfxImage build data does not yet retain its source " +
                "language identity.");
        }
        foreach (DbHeaderImageStreamEntry entry in selectedEntries)
        {
            if (entry.IsEmpty)
            {
                if (entry.FileIndex != 0 ||
                    entry.SourceStart != 0 ||
                    entry.SourceEnd != 0 ||
                    entry.BlockOffset != 0 ||
                    entry.StreamOffset != 0)
                {
                    throw new InvalidDataException(
                        "An empty image-stream record must contain five zero " +
                        "wire fields.");
                }
            }
            else
            {
                if (entry.FileIndex == 0)
                {
                    throw new InvalidDataException(
                        "A nonempty image-stream record cannot address " +
                        "imagefile package index zero.");
                }
                if (entry.SourceEnd <= entry.SourceStart)
                {
                    throw new InvalidDataException(
                        "A nonempty image-stream record requires a positive " +
                        "source range.");
                }
            }
            if ((entry.StreamOffset & 0xffff) != entry.BlockOffset)
            {
                throw new InvalidDataException(
                    "An image-stream record's block offset must match the " +
                    "low 16 bits of its stream offset.");
            }
        }

        uint[] languageBits = Enumerable.Range(0, 15)
            .Select(bit => 1u << bit)
            .Where(bit => (policy.LanguagePolicy.LanguageMask & bit) != 0)
            .ToArray();
        var tables = languageBits
            .Select((bit, index) =>
                new DbHeaderImageStreamLanguageTable(
                    index,
                    bit,
                    bit == policy.LanguagePolicy.SelectedLanguageMask
                        ? selectedEntries
                        : []))
            .ToArray();
        int selectedLanguageIndex = Array.IndexOf(
            languageBits,
            policy.LanguagePolicy.SelectedLanguageMask);
        if (selectedLanguageIndex < 0 ||
            tables.Length != policy.LanguagePolicy.LanguageCount)
        {
            throw new InvalidDataException(
                "Greenfield language policy could not be represented as deterministic PS3 language tables.");
        }

        return new DbHeader(
            magic: GreenfieldContainerPolicy.Ps3Magic,
            version: policy.Version,
            allowOnlineUpdate: policy.AllowOnlineUpdate,
            fileCreationTimeRaw: policy.FileCreationTimeRaw,
            languageMask: policy.LanguagePolicy.LanguageMask,
            selectedLanguageMask: policy.LanguagePolicy.SelectedLanguageMask,
            languageCount: checked((uint)tables.Length),
            selectedLanguageIndex: checked((uint)selectedLanguageIndex),
            entryCount: checked((uint)selectedEntries.Length),
            languageTables: tables,
            fileSize: 0,
            maxFileSize: 0,
            serializedHeaderOffset: 0,
            serializedHeaderBytes: [],
            packedStreamOffset: 0,
            sourceFileLength: 0,
            metadataDispositions: GreenfieldMetadata);
    }
}
