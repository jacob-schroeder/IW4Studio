using IW4.Game.Assets.Sound;
using IW4.Game.Zone;

namespace IW4.Studio.Documents;

public sealed partial class AssetEditorSession
{
    public IReadOnlyList<LoadedSoundUsage> CaptureLoadedSoundUsages()
    {
        ThrowIfClosed();
        return _session.CaptureLoadedSoundUsages(Entry);
    }

    public bool ApplyCompiledLoadedSound(
        LoadedSound expectedCurrent,
        LoadedSound replacement,
        out IReadOnlyList<AssetValidationIssue> issues)
    {
        ThrowIfClosed();
        if (!CanEdit || Entry.AssetType != XAssetType.LoadedSound)
        {
            issues = Array.AsReadOnly([new AssetValidationIssue(
                "loadedSound",
                "Only full LoadedSounds owned by the current fastfile/zone can be edited.",
                AssetValidationSeverity.Error)]);
            Validation = new AssetEditorValidationState(issues);
            return false;
        }
        bool changed = _session.ApplyCompiledLoadedSound(
            Entry, expectedCurrent, replacement, out issues);
        Validation = new AssetEditorValidationState(issues);
        return changed;
    }
}
