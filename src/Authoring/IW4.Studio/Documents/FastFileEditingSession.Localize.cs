using IW4.Game.Assets.Localize;
using IW4.Game.Zone;
using IW4.Linker.Contracts;

namespace IW4.Studio.Documents;

public sealed record LocalizeValueEdit(
    TargetZoneRowIdentity RowIdentity,
    string? ExpectedValue,
    string? Value);

public sealed partial class FastFileEditingSession
{
    private HashSet<WorkspaceAssetCatalogEntry>? _localizeDependencyEntries;

    /// <summary>Reads the applied value without opening an asset editor.</summary>
    public bool TryGetCurrentLocalizeValue(
        WorkspaceAssetCatalogEntry entry,
        out string? value)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_gate)
        {
            ThrowIfDisposedCore();
            value = null;
            if (entry.AssetType != XAssetType.Localize)
                return false;

            if (entry.TargetRowIdentity is { } row &&
                _drafts.TryGetValue(row, out DraftState? target) &&
                ReferenceEquals(target.Entry, entry))
            {
                value = ((LocalizeDraft)target.CloneCurrent()).Value;
                return true;
            }

            if (entry.Definition is LocalizeAsset definition &&
                (entry.TargetRowIdentity is { } targetRow &&
                 Document.TryGetRow(targetRow, out WorkspaceAssetCatalogEntry? storedRow) &&
                 ReferenceEquals(storedRow, entry) ||
                 entry.DependencyIdentity is { } dependencyIdentity &&
                 dependencyIdentity.DocumentId == Document.DocumentId &&
                 entry.ResolvedProvider?.ProviderId == dependencyIdentity.ProviderId &&
                 (_localizeDependencyEntries ??= Workspace.AssetCatalog.DependencyEntries
                     .Where(candidate => candidate.AssetType == XAssetType.Localize)
                     .ToHashSet<WorkspaceAssetCatalogEntry>(ReferenceEqualityComparer.Instance)).Contains(entry)))
            {
                value = definition.Value;
                return true;
            }

            return false;
        }
    }

    /// <summary>Captures Localize values for active pool providers, including providers without catalog entries.</summary>
    public IReadOnlyDictionary<long, string?> CaptureCurrentPoolLocalizeValues()
    {
        lock (_gate)
        {
            ThrowIfDisposedCore();
            var values = new Dictionary<long, string?>();
            if (Workspace.IsBlank)
                return values;
            foreach (var slot in Workspace.LoadedZone.Context.AssetPool.Slots)
            {
                var provider = slot.ActiveProvider;
                if (provider.IsReferencePlaceholder || provider.Asset is not LocalizeAsset localize)
                    continue;

                long providerId = provider.Id.Value;
                values.Add(providerId, localize.Value);
            }

            return values;
        }
    }

    /// <summary>Applies existing owned-row value edits in one link revision.</summary>
    public int ApplyLocalizeValueEdits(IReadOnlyList<LocalizeValueEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        if (edits.Count == 0)
            return 0;

        int changed;
        lock (_gate)
        {
            ThrowIfDisposedCore();
            var seen = new HashSet<TargetZoneRowIdentity>();
            var seenKeys = new HashSet<AssetKey>();
            var pending = new List<(DraftState State, LocalizeDraft Draft, AssetKey Key)>();
            foreach (LocalizeValueEdit edit in edits)
            {
                if (edit is null || !seen.Add(edit.RowIdentity) ||
                    edit.RowIdentity.DocumentId != Document.DocumentId ||
                    !_drafts.TryGetValue(edit.RowIdentity, out DraftState? state) ||
                    state.Entry.AssetType != XAssetType.Localize ||
                    state.Entry.Origin != WorkspaceAssetOrigin.TargetOwnedDefinition ||
                    state.Entry.Access != WorkspaceAssetAccess.Editable ||
                    state.Adapter is not LocalizeAdapter)
                {
                    throw new InvalidOperationException(
                        "The Localize batch contains a missing, duplicate, or non-owned row.");
                }

                var current = (LocalizeDraft)state.CloneCurrent();
                AssetKey key = AssetKey.FromDefinition(current.ToAsset());
                if (!seenKeys.Add(key))
                {
                    throw new InvalidOperationException(
                        $"The Localize batch contains multiple rows for {key}.");
                }
                if (!string.Equals(current.Value, edit.ExpectedValue, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Localize {key} changed since it was read.");
                }

                if (string.Equals(current.Value, edit.Value, StringComparison.Ordinal))
                    continue;

                current.SetValue(edit.Value);
                pending.Add((state, current, key));
            }

            changed = pending.Count;
            if (changed == 0)
                return 0;

            LinkAssetProviderSource[] sources = pending.Select(item =>
            {
                LocalizeAsset definition = item.Draft.ToAsset();
                return _packageDefinitionOwners.TryGetValue(item.Key, out var owner) &&
                    item.State.Entry.Definition is { } packaged
                    ? LoadedZoneLinkExtensions.CreateCapturedProviderSource(
                        owner, definition, packaged, item.State.Entry.OriginalName,
                        LinkAssetProviderSourceDisposition.AuthoredDetached)
                    : new LinkAssetProviderSource(definition).AsAuthoredDetached();
            }).ToArray();
            try
            {
                PublishEditedProviders(pending.Select(item => item.Key).ToArray(), sources);
            }
            catch (InvalidDataException exception)
            {
                throw new InvalidDataException(
                    $"Localize {string.Join(", ", pending.Select(item => item.Key))}: {exception.Message}",
                    exception);
            }
            foreach (var item in pending)
                item.State.SetCurrent(item.Draft, _revision.Revision);
            RebuildChangeSet();
        }

        AppliedAssetsChanged?.Invoke(this, EventArgs.Empty);
        return changed;
    }
}
