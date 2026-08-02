using System.Text.Json;
using IW4.Assets.Assets.Sound;
using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Assets;

namespace IW4.Studio.Documents;

/// <summary>Detached Sound alias-list snapshot. Loaded-sound and curve links
/// remain symbolic rows; streamed source strings stay inline union payloads.</summary>
public sealed class SoundAuthoredSnapshot : ITargetZoneDetachedSemanticSnapshot
{
    internal SoundAuthoredSnapshot(SoundAliasListBuildData data) => Data = data.Copy(); internal SoundAliasListBuildData Data { get; } public XAssetType AssetType => XAssetType.Sound;
    internal static SoundAuthoredSnapshot Import(TargetZoneRowSource source) => source.AuthoredDefinition?.SemanticSnapshot is SoundAuthoredSnapshot snapshot ? snapshot : throw new InvalidDataException("Sound editing requires a capture-time detached semantic snapshot because nested aliases may use pointer aliases.");
    internal static SoundAuthoredSnapshot FromLoaded(SoundAliasListAsset asset) => new(new SoundAliasListBuildData(asset.AliasName, asset.Aliases.Select(Alias)));
    private static SoundAliasBuildData Alias(SndAlias value) => new(
        value.AliasName,
        value.Subtitle,
        value.SecondaryAliasName,
        value.ChainAliasName,
        value.MixerGroup,
        value.SoundFiles.Count == 0 ? null : File(value.SoundFiles[0]),
        value.Sequence,
        value.VolumeMin,
        value.VolumeMax,
        value.PitchMin,
        value.PitchMax,
        value.DistanceMin,
        value.DistanceMax,
        value.VelocityMin,
        value.Flags,
        value.SlavePercentage,
        value.Probability,
        value.LfePercentage,
        value.CenterPercentage,
        value.StartDelay,
        Reference(XAssetType.SndCurve, value.VolumeFalloffCurve?.Filename),
        value.EnvelopMin,
        value.EnvelopMax,
        value.EnvelopPercentage,
        value.SpeakerMap is null ? null : Speaker(value.SpeakerMap),
        CurveLink(value),
        DirectPointer(value.SoundFilesPointer.Untyped),
        DirectPointer(value.SpeakerMapPointer.Untyped));
    private static SoundFileBuildData File(SoundFile value) => value.Payload switch { LoadedSoundFile loaded => new((SndAliasTypeBuildKind)value.Type, value.Exists, value.Padding, Reference(XAssetType.LoadedSound, loaded.LoadedSound?.Name), 0, 0, 0, null, null, LoadedSoundLink(loaded)), StreamedSound streamed when streamed.ExternalFile is { } external => new((SndAliasTypeBuildKind)value.Type, value.Exists, value.Padding, null, streamed.FileIndex, 0, 0, external.Directory, external.Filename), StreamedSound streamed when streamed.StreamFile is { } file => new((SndAliasTypeBuildKind)value.Type, value.Exists, value.Padding, null, streamed.FileIndex, file.StreamFileOffset, file.StreamFileLength, null, null), _ => new((SndAliasTypeBuildKind)value.Type, value.Exists, value.Padding, null, 0, 0, 0, null, null) };
    private static SoundSpeakerMapBuildData Speaker(SpeakerMap value) => new(value.IsDefault, value.Padding, value.Name, value.Channels.SelectMany(channel => channel.Outputs).Select(output => new SoundChannelMapBuildData(output.EntryCount, output.Speakers.Select(level => new SoundSpeakerLevelBuildData(level.Speaker, level.NumLevels, level.Level0, level.Level1)).ToArray())).ToArray());
    private static SymbolicXAssetReference? Reference(XAssetType type, string? value) => value is null ? null : new(type, value.StartsWith(",", StringComparison.Ordinal) ? value : $",{value}");
    private static NestedXAssetBuildLink? LoadedSoundLink(LoadedSoundFile value)
    {
        string? name = value.IncomingLoadedSound?.Name ?? value.LoadedSound?.Name;
        if (name is null || value.LoadedSoundPointer.Type == IW4.FastFiles.Pointers.PointerType.Null)
            return null;
        NestedXAssetPointerSourceForm form = SourceForm(value.LoadedSoundPointer.Type);
        return new(
            new SymbolicXAssetReference(XAssetType.LoadedSound, name),
            form,
            value.IncomingLoadedSound is null
                ? null
                : LoadedSoundBuildData.FromLoaded(value.IncomingLoadedSound),
            form == NestedXAssetPointerSourceForm.PackedAlias
                ? value.LoadedSoundPointer.Raw
                : null);
    }
    private static NestedXAssetBuildLink? CurveLink(SndAlias value)
    {
        string? name = value.IncomingVolumeFalloffCurve?.Filename ?? value.VolumeFalloffCurve?.Filename;
        if (name is null || value.VolumeFalloffCurvePointer.Type == IW4.FastFiles.Pointers.PointerType.Null)
            return null;
        NestedXAssetPointerSourceForm form = SourceForm(value.VolumeFalloffCurvePointer.Type);
        return new(
            new SymbolicXAssetReference(XAssetType.SndCurve, name),
            form,
            value.IncomingVolumeFalloffCurve is null
                ? null
                : SndCurveBuildData.FromLoaded(value.IncomingVolumeFalloffCurve),
            form == NestedXAssetPointerSourceForm.PackedAlias
                ? value.VolumeFalloffCurvePointer.Raw
                : null);
    }
    private static NestedXAssetPointerSourceForm SourceForm(
        IW4.FastFiles.Pointers.PointerType type) => type switch
    {
        IW4.FastFiles.Pointers.PointerType.Inline => NestedXAssetPointerSourceForm.Inline,
        IW4.FastFiles.Pointers.PointerType.Insert => NestedXAssetPointerSourceForm.Insert,
        IW4.FastFiles.Pointers.PointerType.Offset => NestedXAssetPointerSourceForm.PackedAlias,
        _ => throw new InvalidDataException($"Unsupported nested Sound pointer source form {type}.")
    };
    private static SoundDirectPointerBuildProvenance DirectPointer(
        IW4.FastFiles.Pointers.XPointerReference pointer) =>
        pointer.Type switch
        {
            IW4.FastFiles.Pointers.PointerType.Null => new(
                SoundDirectPointerSourceForm.Null),
            IW4.FastFiles.Pointers.PointerType.Inline => new(
                SoundDirectPointerSourceForm.Inline),
            IW4.FastFiles.Pointers.PointerType.Insert => new(
                SoundDirectPointerSourceForm.Insert),
            IW4.FastFiles.Pointers.PointerType.Offset => new(
                SoundDirectPointerSourceForm.PackedAlias,
                pointer.Raw),
            _ => throw new InvalidDataException(
                $"Unsupported direct Sound pointer source form {pointer.Type}.")
        };
}

public sealed class SoundAliasListBuildData : ISoundAliasListBuildData
{
    private readonly SoundAliasBuildData[] _aliases; internal SoundAliasListBuildData(string? aliasName, IEnumerable<SoundAliasBuildData> aliases) { AliasName = aliasName; _aliases = aliases.Select(Copy).ToArray(); }
    public XAssetType AssetType => XAssetType.Sound; public string? AliasName { get; } public IReadOnlyList<SoundAliasBuildData> Aliases => Array.AsReadOnly(_aliases.Select(Copy).ToArray()); internal SoundAliasListBuildData Copy() => new(AliasName, _aliases);
    internal static SoundAliasBuildData Copy(SoundAliasBuildData value) => new(
        value.AliasName,
        value.Subtitle,
        value.SecondaryAliasName,
        value.ChainAliasName,
        value.MixerGroup,
        Copy(value.SoundFile),
        value.Sequence,
        value.VolumeMin,
        value.VolumeMax,
        value.PitchMin,
        value.PitchMax,
        value.DistanceMin,
        value.DistanceMax,
        value.VelocityMin,
        value.Flags,
        value.SlavePercentage,
        value.Probability,
        value.LfePercentage,
        value.CenterPercentage,
        value.StartDelay,
        value.VolumeFalloffCurveReference,
        value.EnvelopMin,
        value.EnvelopMax,
        value.EnvelopPercentage,
        Copy(value.SpeakerMap),
        value.VolumeFalloffCurveLink,
        value.SoundFilesPointerProvenance,
        value.SpeakerMapPointerProvenance);
    private static SoundFileBuildData? Copy(SoundFileBuildData? value) => value is null ? null : new(value.Kind, value.Exists, value.Padding, value.LoadedSoundReference, value.StreamedFileIndex, value.StreamFileOffset, value.StreamFileLength, value.ExternalDirectory, value.ExternalFilename, value.LoadedSoundLink);
    private static SoundSpeakerMapBuildData? Copy(SoundSpeakerMapBuildData? value) => value is null ? null : new(value.IsDefault, value.Padding, value.Name, value.ChannelMaps.Select(map => new SoundChannelMapBuildData(map.EntryCount, map.Speakers.Select(level => new SoundSpeakerLevelBuildData(level.Speaker, level.NumLevels, level.Level0, level.Level1)).ToArray())).ToArray());
}

public sealed class SoundDraft
{
    private SoundAliasListBuildData _data; internal SoundDraft(SoundAliasListBuildData data) => _data = data.Copy(); public SoundAliasListBuildData Data => _data.Copy(); public void ReplaceAliases(IEnumerable<SoundAliasBuildData> aliases) { ArgumentNullException.ThrowIfNull(aliases); _data = new SoundAliasListBuildData(_data.AliasName, aliases); } internal SoundDraft Clone() => new(_data);
}

public sealed class SoundAuthoringAdapter : AssetAuthoringAdapter<SoundAuthoredSnapshot, SoundDraft, SoundAliasListBuildData>
{
    private static readonly SoundAliasListBodyEmitter Validator = new(); public override XAssetType AssetType => XAssetType.Sound; public override SoundAuthoredSnapshot ImportAuthoredSnapshot(TargetZoneRowSource source) => SoundAuthoredSnapshot.Import(source); public override SoundDraft CreateDraft(SoundAuthoredSnapshot snapshot) => new(snapshot.Data); public override SoundDraft CloneDraft(SoundDraft draft) => draft.Clone(); public override IReadOnlyList<AssetValidationIssue> ValidateDraft(SoundDraft draft) => Validator.Validate(draft.Data).Select(value => new AssetValidationIssue(value.Path, value.Message, AssetValidationSeverity.Error)).ToArray(); public override bool SemanticallyEquals(SoundDraft left, SoundDraft right) => JsonSerializer.Serialize(left.Data) == JsonSerializer.Serialize(right.Data); public override SoundAliasListBuildData ExportBuildData(SoundDraft draft) { SoundAliasListBuildData data = draft.Data; if (Validator.Validate(data).Count != 0) throw new InvalidOperationException("Sound draft has validation errors and cannot produce build data."); return data; }
}
