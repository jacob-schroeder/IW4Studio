using IW4.Assets.Assets.Sound;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Studio.Documents;

/// <summary>
/// Detached Sound graph used by the editor. LoadedSound payloads are copied so
/// importing audio never mutates the loader-owned runtime definition.
/// </summary>
internal sealed class SoundDraft
{
    private SoundAliasListAsset _sound;

    internal SoundDraft(SoundAliasListAsset sound)
    {
        ArgumentNullException.ThrowIfNull(sound);
        _sound = Copy(sound);
    }

    private SoundDraft(SoundDraft source) => _sound = Copy(source._sound);

    internal SoundDraft Clone() => new(this);

    internal SoundAliasListAsset ToAsset() => Copy(_sound);

    internal bool SemanticallyEquals(SoundDraft other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return SemanticallyEquals(_sound, other._sound);
    }

    internal void ReplaceLoadedSound(AssetKey key, LoadedSound replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        LoadedSound detachedReplacement = Copy(replacement);
        _sound = Copy(_sound, key, detachedReplacement);
    }

    private static SoundAliasListAsset Copy(
        SoundAliasListAsset source,
        AssetKey? replacedLoadedSoundKey = null,
        LoadedSound? replacement = null) => new()
    {
        Offset = source.Offset,
        RuntimeAddress = source.RuntimeAddress,
        AliasNamePointer = source.AliasNamePointer,
        AliasName = source.AliasName,
        AliasesPointer = source.AliasesPointer,
        Count = source.Count,
        Aliases = source.Aliases.Select(alias => Copy(
            alias,
            replacedLoadedSoundKey,
            replacement)).ToArray()
    };

    private static SndAlias Copy(
        SndAlias source,
        AssetKey? replacedLoadedSoundKey,
        LoadedSound? replacement) => new()
    {
        Offset = source.Offset,
        AliasNamePointer = source.AliasNamePointer,
        AliasName = source.AliasName,
        SubtitlePointer = source.SubtitlePointer,
        Subtitle = source.Subtitle,
        SecondaryAliasNamePointer = source.SecondaryAliasNamePointer,
        SecondaryAliasName = source.SecondaryAliasName,
        ChainAliasNamePointer = source.ChainAliasNamePointer,
        ChainAliasName = source.ChainAliasName,
        MixerGroupPointer = source.MixerGroupPointer,
        MixerGroup = source.MixerGroup,
        SoundFilesPointer = source.SoundFilesPointer,
        SoundFileCount = source.SoundFileCount,
        SoundFiles = source.SoundFiles.Select(file => Copy(
            file,
            replacedLoadedSoundKey,
            replacement)).ToArray(),
        Sequence = source.Sequence,
        VolumeMin = source.VolumeMin,
        VolumeMax = source.VolumeMax,
        PitchMin = source.PitchMin,
        PitchMax = source.PitchMax,
        DistanceMin = source.DistanceMin,
        DistanceMax = source.DistanceMax,
        VelocityMin = source.VelocityMin,
        Flags = source.Flags,
        SlavePercentage = source.SlavePercentage,
        Probability = source.Probability,
        LfePercentage = source.LfePercentage,
        CenterPercentage = source.CenterPercentage,
        StartDelay = source.StartDelay,
        VolumeFalloffCurvePointer = source.VolumeFalloffCurvePointer,
        VolumeFalloffCurve = source.VolumeFalloffCurve is null
            ? null
            : Copy(source.VolumeFalloffCurve),
        EnvelopMin = source.EnvelopMin,
        EnvelopMax = source.EnvelopMax,
        EnvelopPercentage = source.EnvelopPercentage,
        SpeakerMapPointer = source.SpeakerMapPointer,
        SpeakerMap = source.SpeakerMap is null
            ? null
            : Copy(source.SpeakerMap)
    };

    private static SoundFile Copy(
        SoundFile source,
        AssetKey? replacedLoadedSoundKey,
        LoadedSound? replacement) => new()
    {
        Offset = source.Offset,
        Type = source.Type,
        Exists = source.Exists,
        Padding = source.Padding,
        Payload = source.Payload switch
        {
            LoadedSoundFile loaded => Copy(
                loaded,
                replacedLoadedSoundKey,
                replacement),
            StreamedSound streamed => Copy(streamed),
            null => null,
            _ => throw new InvalidDataException(
                $"Unsupported SoundFile payload {source.Payload.GetType().Name}.")
        }
    };

    private static LoadedSoundFile Copy(
        LoadedSoundFile source,
        AssetKey? replacedLoadedSoundKey,
        LoadedSound? replacement)
    {
        LoadedSound? loadedSound = source.LoadedSound;
        if (loadedSound is not null &&
            replacedLoadedSoundKey is { } key &&
            AssetKey.FromDefinition(loadedSound) == key)
        {
            loadedSound = replacement ?? throw new InvalidDataException(
                "A LoadedSound replacement cannot be null.");
        }

        return new LoadedSoundFile
        {
            LoadedSoundPointer = source.LoadedSoundPointer,
            LoadedSound = loadedSound is null ? null : Copy(loadedSound)
        };
    }

    internal static LoadedSound Copy(LoadedSound source) =>
        Copy(source, source.Name, clearNamePointer: false);

    internal static LoadedSound CopyWithName(
        LoadedSound source,
        string name)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name[0] == ',')
        {
            throw new ArgumentException(
                "A cloned LoadedSound name cannot begin with a comma.",
                nameof(name));
        }

        return Copy(source, name, clearNamePointer: true);
    }

    private static LoadedSound Copy(
        LoadedSound source,
        string? name,
        bool clearNamePointer) => new()
    {
        Offset = source.Offset,
        RuntimeAddress = source.RuntimeAddress,
        NamePointer = clearNamePointer ? default : source.NamePointer,
        Name = name,
        PhysicalDataByteCount = source.PhysicalDataByteCount,
        FrameCount = source.FrameCount,
        ChannelCount = source.ChannelCount,
        SampleRate = source.SampleRate,
        Pad0E = source.Pad0E,
        Pad10 = source.Pad10,
        SeekTableCount = source.SeekTableCount,
        SeekTablePointer = source.SeekTablePointer,
        SeekTable = source.SeekTable?.ToArray(),
        PhysicalDataPointer = source.PhysicalDataPointer,
        PhysicalData = source.PhysicalData?.ToArray()
    };

    private static StreamedSound Copy(StreamedSound source) => new()
    {
        FileIndex = source.FileIndex,
        Source = source.Source switch
        {
            StreamedSoundFileSource streamFile => new StreamedSoundFileSource
            {
                StreamFileOffset = streamFile.StreamFileOffset,
                StreamFileLength = streamFile.StreamFileLength
            },
            ExternalStreamedSoundSource external => new ExternalStreamedSoundSource
            {
                DirectoryPointer = external.DirectoryPointer,
                Directory = external.Directory,
                FilenamePointer = external.FilenamePointer,
                Filename = external.Filename
            },
            null => null,
            _ => throw new InvalidDataException(
                $"Unsupported streamed Sound source {source.Source.GetType().Name}.")
        }
    };

    private static SndCurve Copy(SndCurve source) => new()
    {
        Offset = source.Offset,
        RuntimeAddress = source.RuntimeAddress,
        FilenamePointer = source.FilenamePointer,
        Filename = source.Filename,
        KnotCount = source.KnotCount,
        Padding = source.Padding,
        Knots = source.Knots.ToArray()
    };

    private static SpeakerMap Copy(SpeakerMap source) => new()
    {
        Offset = source.Offset,
        IsDefault = source.IsDefault,
        Padding = source.Padding.ToArray(),
        NamePointer = source.NamePointer,
        Name = source.Name,
        Channels = source.Channels.Select(channel => new SpeakerMapChannel
        {
            Outputs = channel.Outputs.Select(output => new XAudioChannelMap
            {
                EntryCount = output.EntryCount,
                Speakers = output.Speakers.Select(speaker => new SpeakerLevels
                {
                    Speaker = speaker.Speaker,
                    NumLevels = speaker.NumLevels,
                    Level0 = speaker.Level0,
                    Level1 = speaker.Level1
                }).ToArray()
            }).ToArray()
        }).ToArray()
    };

    internal static IEnumerable<LoadedSound> LoadedSounds(
        SoundAliasListAsset sound) => sound.Aliases
        .SelectMany(alias => alias.SoundFiles)
        .Where(file => file.Type == SndAliasType.Loaded)
        .Select(file => file.Loaded?.LoadedSound)
        .OfType<LoadedSound>();

    internal static bool SemanticallyEquals(
        SoundAliasListAsset left,
        SoundAliasListAsset right) =>
        string.Equals(left.AliasName, right.AliasName, StringComparison.Ordinal) &&
        left.Count == right.Count &&
        SequenceEqual(left.Aliases, right.Aliases, AliasEquals);

    private static bool AliasEquals(SndAlias left, SndAlias right) =>
        string.Equals(left.AliasName, right.AliasName, StringComparison.Ordinal) &&
        string.Equals(left.Subtitle, right.Subtitle, StringComparison.Ordinal) &&
        string.Equals(left.SecondaryAliasName, right.SecondaryAliasName, StringComparison.Ordinal) &&
        string.Equals(left.ChainAliasName, right.ChainAliasName, StringComparison.Ordinal) &&
        string.Equals(left.MixerGroup, right.MixerGroup, StringComparison.Ordinal) &&
        left.SoundFileCount == right.SoundFileCount &&
        SequenceEqual(left.SoundFiles, right.SoundFiles, SoundFileEquals) &&
        left.Sequence == right.Sequence &&
        left.VolumeMin.Equals(right.VolumeMin) &&
        left.VolumeMax.Equals(right.VolumeMax) &&
        left.PitchMin.Equals(right.PitchMin) &&
        left.PitchMax.Equals(right.PitchMax) &&
        left.DistanceMin.Equals(right.DistanceMin) &&
        left.DistanceMax.Equals(right.DistanceMax) &&
        left.VelocityMin.Equals(right.VelocityMin) &&
        left.Flags == right.Flags &&
        left.SlavePercentage.Equals(right.SlavePercentage) &&
        left.Probability.Equals(right.Probability) &&
        left.LfePercentage.Equals(right.LfePercentage) &&
        left.CenterPercentage.Equals(right.CenterPercentage) &&
        left.StartDelay == right.StartDelay &&
        CurveEquals(left.VolumeFalloffCurve, right.VolumeFalloffCurve) &&
        left.EnvelopMin.Equals(right.EnvelopMin) &&
        left.EnvelopMax.Equals(right.EnvelopMax) &&
        left.EnvelopPercentage.Equals(right.EnvelopPercentage) &&
        SpeakerMapEquals(left.SpeakerMap, right.SpeakerMap);

    private static bool SoundFileEquals(SoundFile left, SoundFile right) =>
        left.Type == right.Type &&
        left.Exists == right.Exists &&
        left.Padding == right.Padding &&
        PayloadEquals(left.Payload, right.Payload);

    private static bool PayloadEquals(
        SoundFilePayload? left,
        SoundFilePayload? right) => (left, right) switch
    {
        (null, null) => true,
        (LoadedSoundFile x, LoadedSoundFile y) =>
            LoadedSoundEquals(x.LoadedSound, y.LoadedSound),
        (StreamedSound x, StreamedSound y) => StreamedSoundEquals(x, y),
        _ => false
    };

    private static bool LoadedSoundEquals(LoadedSound? left, LoadedSound? right) =>
        ReferenceEquals(left, right) ||
        left is not null &&
        right is not null &&
        string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
        left.PhysicalDataByteCount == right.PhysicalDataByteCount &&
        left.FrameCount == right.FrameCount &&
        left.ChannelCount == right.ChannelCount &&
        left.SampleRate == right.SampleRate &&
        left.Pad0E == right.Pad0E &&
        left.Pad10 == right.Pad10 &&
        left.SeekTableCount == right.SeekTableCount &&
        ByteArrayEquals(left.SeekTable, right.SeekTable) &&
        ByteArrayEquals(left.PhysicalData, right.PhysicalData);

    private static bool StreamedSoundEquals(
        StreamedSound left,
        StreamedSound right) =>
        left.FileIndex == right.FileIndex &&
        (left.Source, right.Source) switch
        {
            (null, null) => true,
            (StreamedSoundFileSource x, StreamedSoundFileSource y) =>
                x.StreamFileOffset == y.StreamFileOffset &&
                x.StreamFileLength == y.StreamFileLength,
            (ExternalStreamedSoundSource x, ExternalStreamedSoundSource y) =>
                string.Equals(x.Directory, y.Directory, StringComparison.Ordinal) &&
                string.Equals(x.Filename, y.Filename, StringComparison.Ordinal),
            _ => false
        };

    private static bool CurveEquals(SndCurve? left, SndCurve? right) =>
        ReferenceEquals(left, right) ||
        left is not null &&
        right is not null &&
        string.Equals(left.Filename, right.Filename, StringComparison.Ordinal) &&
        left.KnotCount == right.KnotCount &&
        left.Padding == right.Padding &&
        left.Knots.SequenceEqual(right.Knots);

    private static bool SpeakerMapEquals(SpeakerMap? left, SpeakerMap? right) =>
        ReferenceEquals(left, right) ||
        left is not null &&
        right is not null &&
        left.IsDefault == right.IsDefault &&
        left.Padding.SequenceEqual(right.Padding) &&
        string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
        SequenceEqual(left.Channels, right.Channels, ChannelEquals);

    private static bool ChannelEquals(
        SpeakerMapChannel left,
        SpeakerMapChannel right) =>
        SequenceEqual(left.Outputs, right.Outputs, OutputEquals);

    private static bool OutputEquals(
        XAudioChannelMap left,
        XAudioChannelMap right) =>
        left.EntryCount == right.EntryCount &&
        SequenceEqual(left.Speakers, right.Speakers, SpeakerEquals);

    private static bool SpeakerEquals(SpeakerLevels left, SpeakerLevels right) =>
        left.Speaker == right.Speaker &&
        left.NumLevels == right.NumLevels &&
        left.Level0.Equals(right.Level0) &&
        left.Level1.Equals(right.Level1);

    private static bool ByteArrayEquals(byte[]? left, byte[]? right) =>
        ReferenceEquals(left, right) ||
        left is not null && right is not null && left.AsSpan().SequenceEqual(right);

    private static bool SequenceEqual<T>(
        IReadOnlyList<T> left,
        IReadOnlyList<T> right,
        Func<T, T, bool> equals)
    {
        if (left.Count != right.Count)
            return false;
        for (int index = 0; index < left.Count; index++)
        {
            if (!equals(left[index], right[index]))
                return false;
        }
        return true;
    }
}

internal sealed class SoundAdapter :
    AssetAuthoringAdapter<SoundAliasListAsset, SoundDraft>
{
    public override XAssetType AssetType => XAssetType.Sound;

    public override SoundDraft CreateDraft(SoundAliasListAsset definition) =>
        new(definition);

    public override SoundDraft CloneDraft(SoundDraft draft) => draft.Clone();

    public override SoundAliasListAsset CreateDefinition(SoundDraft draft) =>
        draft.ToAsset();

    public override bool SemanticallyEquals(SoundDraft left, SoundDraft right) =>
        left.SemanticallyEquals(right);
}
