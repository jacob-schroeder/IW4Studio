using IW4.Game.Assets;
using IW4.Game.Assets.Sound;
using IW4.Game.Zone;
using IW4.Linker.Contracts;

namespace IW4.Studio.Documents;

public sealed partial class FastFileEditingSession
{
    private readonly HashSet<AssetKey> _independentlyEditedLoadedSounds = [];

    internal IReadOnlyList<LoadedSoundUsage> CaptureLoadedSoundUsages(
        WorkspaceAssetCatalogEntry entry)
    {
        lock (_gate)
        {
            ThrowIfDisposedCore();
            if (entry.AssetType != XAssetType.LoadedSound ||
                entry.Definition is not LoadedSound loaded)
                return [];
            AssetKey key = AssetKey.FromDefinition(loaded);
            var usages = new List<LoadedSoundUsage>();
            foreach (WorkspaceAssetCatalogEntry soundEntry in Document.Rows
                         .Concat(Workspace.AssetCatalog.DependencyEntries)
                         .Where(candidate => candidate.AssetType == XAssetType.Sound &&
                             candidate.Definition is SoundAliasListAsset))
            {
                string soundName = soundEntry.OriginalName ?? "Sound";
                if (soundEntry.TargetRowIdentity is { } identity &&
                    _drafts.TryGetValue(identity, out DraftState? state))
                {
                    state.CurrentSoundDraft.AppendLoadedSoundUsages(
                        key, soundName, identity, usages);
                }
                else
                    SoundDraft.AppendLoadedSoundUsages(
                        (SoundAliasListAsset)soundEntry.Definition!, key,
                        soundName, null, usages);
            }
            return Array.AsReadOnly(usages.ToArray());
        }
    }

    internal bool ApplyCompiledLoadedSound(
        WorkspaceAssetCatalogEntry entry,
        LoadedSound expectedCurrent,
        LoadedSound replacement,
        out IReadOnlyList<AssetValidationIssue> issues,
        bool restoringSaved = false)
    {
        ArgumentNullException.ThrowIfNull(expectedCurrent);
        ArgumentNullException.ThrowIfNull(replacement);
        bool changed;
        lock (_gate)
        {
            ThrowIfDisposedCore();
            const string path = "loadedSound";
            if (!TryGetEditableLoadedSoundState(entry, out DraftState? state, out string reason))
            {
                issues = Array.AsReadOnly([new AssetValidationIssue(
                    path, reason, AssetValidationSeverity.Error)]);
                return false;
            }
            DraftState editableState = state!;
            LoadedSound current = CurrentLoadedSoundForState(editableState);
            if (!SoundDraft.LoadedSoundEquals(current, expectedCurrent))
            {
                issues = Array.AsReadOnly([new AssetValidationIssue(
                    path,
                    "The LoadedSound changed after this import was prepared. Import the current payload again.",
                    AssetValidationSeverity.Error)]);
                return false;
            }
            AssetKey key = AssetKey.FromDefinition(current);
            var found = new List<AssetValidationIssue>();
            try
            {
                if (replacement.Name?.StartsWith(",", StringComparison.Ordinal) == true ||
                    AssetKey.FromDefinition(replacement) != key)
                    found.Add(new AssetValidationIssue(
                        "loadedSound.name",
                        "An imported payload must keep the full LoadedSound's asset identity.",
                        AssetValidationSeverity.Error));
            }
            catch (ArgumentException exception)
            {
                found.Add(new AssetValidationIssue(
                    "loadedSound.name", exception.Message,
                    AssetValidationSeverity.Error));
            }
            if (!restoringSaved && (replacement.PhysicalData is null ||
                replacement.PhysicalData.Length != replacement.PhysicalDataByteCount))
                found.Add(new AssetValidationIssue(
                    "loadedSound.physicalData",
                    "The imported MPEG bytes must be materialized and match the declared byte count.",
                    AssetValidationSeverity.Error));
            if (!restoringSaved && (replacement.SeekTableCount != 0 && replacement.SeekTable is null ||
                replacement.SeekTable is { } seek &&
                seek.Length != replacement.SeekTableByteCount))
                found.Add(new AssetValidationIssue(
                    "loadedSound.seekTable",
                    "The imported seek table must match the declared entry count.",
                    AssetValidationSeverity.Error));
            issues = Array.AsReadOnly(found.ToArray());
            if (found.Count != 0 || SoundDraft.LoadedSoundEquals(current, replacement))
                return false;

            // Update every owned Sound root that embeds this provider, then
            // publish the provider and roots together as a single revision.
            LoadedSound detached = SoundDraft.CopyForImportedPayload(replacement);
            var affected = new List<(DraftState State, SoundDraft Draft,
                SoundAliasListAsset Definition)>();
            foreach (WorkspaceAssetCatalogEntry soundEntry in Document.Rows.Where(
                         candidate => candidate.AssetType == XAssetType.Sound &&
                             candidate.Origin == WorkspaceAssetOrigin.TargetOwnedDefinition &&
                             candidate.TargetRowIdentity is not null))
            {
                DraftState soundState = RequireDraft(soundEntry.TargetRowIdentity!.Value);
                if (!soundState.CurrentSoundDraft.ReferencesLoadedSound(key))
                    continue;
                var draft = (SoundDraft)soundState.CloneCurrent();
                draft.ReplaceLoadedSound(key, detached);
                affected.Add((soundState, draft, draft.ToAsset()));
            }

            AssetKey[] affectedKeys = affected
                .Select(value => AssetKey.FromDefinition(value.Definition)).ToArray();
            LinkAssetPool authored = _authoredAssets
                .WithoutProviders([key, .. affectedKeys])
                .WithHighestPrecedenceProviders(affected.Select(value =>
                    new LinkAssetProviderSource(value.Definition).AsAuthoredDetached()));
            LinkAssetPool target = _targetBaseAssets;
            LinkAssetProvider? replacedProvider = null;
            if (editableState.TargetProvider is { } provider)
            {
                var source = LoadedZoneLinkExtensions.CreateCapturedProviderSource(
                    Workspace.LoadedZone, detached, (LoadedSound)entry.Definition!,
                    provider.OriginalSerializedName,
                    LinkAssetProviderSourceDisposition.AuthoredDetached);
                target = target.WithReplacedProvider(provider, source, out replacedProvider);
            }
            else
            {
                authored = authored.WithHighestPrecedenceProviders([
                    new LinkAssetProviderSource(detached).AsAuthoredDetached()]);
            }
            Publish(authored, _revision.LinkRequest.Roots,
                publishedProviderKeys: [key, .. affectedKeys],
                targetProviderAssets: target);
            editableState.TargetProvider = replacedProvider;
            editableState.SetCurrent(new LoadedSoundDraft(detached), _revision.Revision);
            foreach ((DraftState soundState, SoundDraft draft, _) in affected)
                soundState.SetCurrent(draft, _revision.Revision);
            foreach (Dictionary<AssetKey, BaseAsset> owned in _compiledAuxiliaryProviders.Values)
                owned.Remove(key);
            _independentlyEditedLoadedSounds.Add(key);
            RebuildChangeSet();
            changed = true;
        }
        if (changed)
            AppliedAssetsChanged?.Invoke(this, EventArgs.Empty);
        return changed;
    }

    internal bool RevertLoadedSound(WorkspaceAssetCatalogEntry entry)
    {
        LoadedSound current;
        LoadedSound saved;
        lock (_gate)
        {
            ThrowIfDisposedCore();
            if (!TryGetEditableLoadedSoundState(entry, out DraftState? state, out _))
                return false;
            current = SoundDraft.Copy(CurrentLoadedSoundForState(state!));
            saved = SoundDraft.Copy((LoadedSound)state!.CreateSavedDefinition());
        }
        return ApplyCompiledLoadedSound(entry, current, saved, out _,
            restoringSaved: true);
    }

    private bool TryGetEditableLoadedSoundState(
        WorkspaceAssetCatalogEntry entry,
        out DraftState? state,
        out string reason)
    {
        state = null;
        if (entry.AssetType != XAssetType.LoadedSound ||
            entry.Access != WorkspaceAssetAccess.Editable ||
            entry.Definition is not LoadedSound)
        {
            reason = "Only full LoadedSounds owned by the current fastfile/zone can be edited.";
            return false;
        }
        if (entry.TargetRowIdentity is { } row)
        {
            if (!_drafts.TryGetValue(row, out state) ||
                state.Entry.Origin != WorkspaceAssetOrigin.TargetOwnedDefinition)
            {
                reason = "The target LoadedSound row is no longer editable.";
                return false;
            }
        }
        else if (entry.DependencyIdentity is { } dependency)
        {
            if (!_providerDrafts.TryGetValue(dependency, out state) ||
                !(IsCurrentProviderDraft(state) || IsParentAuthoredLoadedSoundProvider(state)))
            {
                reason = "The LoadedSound is no longer the target's unique provider.";
                return false;
            }
        }
        else
        {
            reason = "The LoadedSound has no editable identity.";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    private bool IsParentAuthoredLoadedSoundProvider(DraftState state) =>
        state.Entry.AssetType == XAssetType.LoadedSound &&
        state.TargetProvider is { } provider &&
        state.Entry.ResolvedProviderZone?.IsTarget == true &&
        _compiledAuxiliaryProviders.Values.Any(owned => owned.ContainsKey(provider.Key)) &&
        _revision.LinkRequest.Assets.Providers
            .Count(candidate => candidate.Key == provider.Key &&
                !candidate.IsReferencePlaceholder) == 1;

    private LoadedSound CurrentLoadedSoundForState(DraftState state)
    {
        LoadedSound drafted = (LoadedSound)state.CreateCurrentDefinition();
        if (state.Entry.DependencyIdentity is null ||
            state.Entry.Origin != WorkspaceAssetOrigin.DependencyOnly)
            return drafted;
        AssetKey key = AssetKey.FromDefinition(drafted);
        foreach (Dictionary<AssetKey, BaseAsset> owned in
                 _compiledAuxiliaryProviders.Values)
        {
            if (owned.TryGetValue(key, out BaseAsset? provider) &&
                provider is LoadedSound loaded)
                return loaded;
        }
        return drafted;
    }
}
