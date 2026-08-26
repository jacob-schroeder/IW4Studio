using IW4.Assets.Assets.Sound;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;
using IW4.Runtime.Assets.Sound;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Desktop.Workbench.Selection;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Workbench.Tools.PackFilePak;

/// <summary>
/// One packed sound-file row together with its owning Sound and fastfile.
/// Package lookup and byte-range validation remain resolver responsibilities.
/// </summary>
public sealed class PackFilePakEntryViewModel
{
    private readonly SoundAliasListAsset _sound;
    private readonly ISoundPayloadResolver _payloadResolver;

    internal PackFilePakEntryViewModel(
        SoundAliasListAsset sound,
        ISoundPayloadResolver payloadResolver,
        string owningFastFilePath,
        int aliasIndex,
        int fileIndex,
        WorkbenchStreamedSoundIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(sound);
        ArgumentNullException.ThrowIfNull(payloadResolver);
        ArgumentException.ThrowIfNullOrWhiteSpace(owningFastFilePath);
        if ((uint)aliasIndex >= (uint)sound.Aliases.Count)
            throw new ArgumentOutOfRangeException(nameof(aliasIndex));

        SndAlias alias = sound.Aliases[aliasIndex];
        if ((uint)fileIndex >= (uint)alias.SoundFiles.Count)
            throw new ArgumentOutOfRangeException(nameof(fileIndex));

        SoundFile soundFile = alias.SoundFiles[fileIndex];
        StreamedSound streamed = soundFile.Streamed
            ?? throw new ArgumentException(
                "A packfile entry requires a streamed SoundFile row.",
                nameof(fileIndex));
        StreamedSoundFileSource source = streamed.StreamFile
            ?? throw new ArgumentException(
                "A packfile entry requires an internal packfile source.",
                nameof(fileIndex));
        if (streamed.FileIndex == 0)
        {
            throw new ArgumentException(
                "File index zero identifies an external sound, not packfileN.pak.",
                nameof(fileIndex));
        }

        _sound = sound;
        _payloadResolver = payloadResolver;
        AliasIndex = aliasIndex;
        FileIndex = fileIndex;
        Identity = identity;
        Name = string.IsNullOrWhiteSpace(sound.AliasName)
            ? "<unnamed sound>"
            : sound.AliasName;
        AliasName = string.IsNullOrWhiteSpace(alias.AliasName)
            ? Name
            : alias.AliasName;
        OwningFastFilePath = Path.GetFullPath(owningFastFilePath);
        OwningFastFileName = Path.GetFileName(OwningFastFilePath);
        PackageName = $"packfile{streamed.FileIndex}.pak";
        OffsetText = $"0x{source.StreamFileOffset:X8}";
        ByteCountText = FormatBytes(source.StreamFileLength);
        RangeText = $"offset {source.StreamFileOffset:N0} · " +
            $"{source.StreamFileLength:N0} bytes";

        var choiceParts = new List<string>(2);
        if (sound.Aliases.Count > 1)
        {
            choiceParts.Add(
                $"Variant {aliasIndex + 1:N0} of {sound.Aliases.Count:N0}");
        }
        if (alias.SoundFiles.Count > 1)
        {
            choiceParts.Add(
                $"Language row {fileIndex + 1:N0} of {alias.SoundFiles.Count:N0}");
        }
        ChoiceText = choiceParts.Count == 0
            ? "Single preview choice"
            : string.Join(" · ", choiceParts);
        string choiceDisplayName = choiceParts.Count == 0
            ? Name
            : $"{Name} · {string.Join(" · ", choiceParts)}";
        DisplayName = $"{choiceDisplayName} · {OwningFastFileName}";
    }

    public string Name { get; }

    public string DisplayName { get; }

    public string AliasName { get; }

    public string ChoiceText { get; }

    public string OwningFastFilePath { get; }

    public string OwningFastFileName { get; }

    public string PackageName { get; }

    public string OffsetText { get; }

    public string ByteCountText { get; }

    public string RangeText { get; }

    public int AliasIndex { get; }

    public int FileIndex { get; }

    public WorkbenchStreamedSoundIdentity Identity { get; }

    public WorkbenchAssetSelection ToSelection() =>
        new(
            WorkbenchAssetSelectionIdentity.ForStreamedSound(Identity),
            XAssetType.Sound,
            DisplayName,
            XAssetStableIdentity.NormalizeLookupName(Name),
            WorkspaceAssetAccess.ReadOnly,
            "Packfile.pak stream",
            OwningFastFileName,
            hasEditor: true);

    internal SoundPreviewViewModel CreatePreview() =>
        SoundPreviewViewModel.CreatePackedSoundPreview(
            _sound,
            _payloadResolver,
            AliasIndex,
            FileIndex);

    private static string FormatBytes(int byteCount)
    {
        if (byteCount <= 0)
            return "No data";
        if (byteCount < 1024)
            return $"{byteCount:N0} bytes";
        if (byteCount < 1024 * 1024)
            return $"{byteCount / 1024d:N1} KiB";
        return $"{byteCount / (1024d * 1024d):N1} MiB";
    }
}
