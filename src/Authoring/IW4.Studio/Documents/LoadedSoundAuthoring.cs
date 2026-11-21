using IW4.Game.Assets.Sound;
using IW4.Game.Zone;

namespace IW4.Studio.Documents;

/// <summary>A detached LoadedSound header with shared, unchanged audio bytes.</summary>
public sealed class LoadedSoundDraft
{
    private readonly LoadedSound _sound;

    internal LoadedSoundDraft(LoadedSound sound) =>
        _sound = SoundDraft.CopyMetadata(sound);

    internal LoadedSoundDraft Clone() => new(_sound);

    internal LoadedSound ToAsset() => SoundDraft.CopyMetadata(_sound);

    public LoadedSound Capture() => SoundDraft.Copy(_sound);

    public bool Matches(LoadedSoundDraft other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return SemanticallyEquals(other);
    }

    internal bool SemanticallyEquals(LoadedSoundDraft other) =>
        SoundDraft.LoadedSoundEquals(_sound, other._sound);
}

internal sealed class LoadedSoundAdapter :
    AssetAuthoringAdapter<LoadedSound, LoadedSoundDraft>
{
    public override XAssetType AssetType => XAssetType.LoadedSound;

    public override LoadedSoundDraft CreateDraft(LoadedSound definition) =>
        new(definition);

    public override LoadedSoundDraft CloneDraft(LoadedSoundDraft draft) =>
        draft.Clone();

    public override LoadedSound CreateDefinition(LoadedSoundDraft draft) =>
        draft.ToAsset();

    public override bool SemanticallyEquals(
        LoadedSoundDraft left, LoadedSoundDraft right) =>
        left.SemanticallyEquals(right);
}

public sealed record LoadedSoundUsage(
    string SoundName,
    string AliasName,
    int AliasIndex,
    int FileIndex,
    TargetZoneRowIdentity? RowIdentity);
