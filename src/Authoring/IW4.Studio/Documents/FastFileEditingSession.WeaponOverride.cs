using IW4.Game.Assets.Weapon;
using IW4.Game.Zone;
using IW4.Linker.Contracts;

namespace IW4.Studio.Documents;

public sealed partial class FastFileEditingSession
{
    /// <summary>Whether a resolved dependency Weapon can become an owned row in the opened patch_mp.ff.</summary>
    public bool CanOverrideDependencyWeapon(WorkspaceAssetCatalogEntry source)
    {
        ArgumentNullException.ThrowIfNull(source);
        lock (_gate)
        {
            ThrowIfDisposedCore();
            return GetWeaponOverrideSourceCore(source) is not null;
        }
    }

    private string? GetWeaponOverrideSourceCore(WorkspaceAssetCatalogEntry source)
    {
        if (source.AssetType != XAssetType.Weapon ||
            source.Definition is not WeaponAsset { Definition: not null } ||
            source.ResolvedProvider is null ||
            source.ResolvedProviderZone?.IsTarget != false)
            return null;

        AssetKey key = AssetKey.FromDefinition(source.Definition);
        string? sourceFile = Workspace.AssetCatalog.GetWeaponOverrideSourceFile(key);
        if (sourceFile is null)
            return null;

        bool currentSource = source.DependencyIdentity is not null
            ? Workspace.AssetCatalog.DependencyEntries.Any(entry => ReferenceEquals(entry, source))
            : source.TargetRowIdentity is { } identity &&
              Document.TryGetRow(identity, out WorkspaceAssetCatalogEntry? row) &&
              ReferenceEquals(row, source) &&
              source.Origin == WorkspaceAssetOrigin.TargetResolvedReference;
        if (!currentSource)
            return null;

        LinkRoot[] matchingRoots = _revision.LinkRequest.Roots
            .Where(root => root.Asset == key)
            .Take(2)
            .ToArray();
        return matchingRoots is [] or [{ Intent: LinkRootIntent.External }] ? sourceFile : null;
    }

    /// <summary>
    /// Copies a resolved dependency Weapon into the opened patch_mp.ff, or applies
    /// a loose source over that dependency Weapon while retaining its variant name.
    /// An existing external target row is promoted at its serialized index.
    /// </summary>
    public WorkspaceAssetCatalogEntry OverrideDependencyWeapon(
        WorkspaceAssetCatalogEntry source,
        string? looseWeaponPath = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        WorkspaceAssetCatalogEntry result;
        lock (_gate)
        {
            ThrowIfDisposedCore();
            string sourceFile = GetWeaponOverrideSourceCore(source)
                ?? throw new InvalidOperationException(
                    "Only a resolved dependency Weapon in the opened patch_mp.ff can be overridden.");

            var imported = (WeaponAsset)source.Definition!;
            WeaponAsset definition = WeaponGraph.Copy(imported);
            if (looseWeaponPath is not null)
            {
                using FileStream input = File.OpenRead(looseWeaponPath);
                definition = WeaponSourceImport.Read(
                    input, definition, Workspace.LoadedZone.Context.AssetPool,
                    looseWeaponPath);
            }

            if (!string.Equals(definition.Name, imported.Name, StringComparison.Ordinal))
                throw new InvalidDataException("The override must retain the selected Weapon variant name.");
            IAssetAuthoringAdapter adapter = RequireHostedAdapter(definition);
            object draft = adapter.CreateDraft(definition);
            AssetValidationIssue? error = adapter.Validate(draft).FirstOrDefault(issue =>
                issue.Severity == AssetValidationSeverity.Error);
            if (error is not null)
                throw new InvalidDataException($"{error.FieldPath}: {error.Message}");
            definition = (WeaponAsset)adapter.CreateDefinition(adapter.CloneDraft(draft));

            AssetKey key = AssetKey.FromDefinition(definition);
            WorkspaceAssetCatalogEntry? reference = FindReferenceRowCore(XAssetType.Weapon, key);
            TargetZoneRowIdentity? promotedIdentity = reference?.TargetRowIdentity;
            LinkRoot[] roots;
            if (promotedIdentity is { } existing)
            {
                LinkRoot old = _revision.LinkRequest.Roots[existing.SerializedIndex];
                roots = _revision.LinkRequest.Roots.ToArray();
                roots[existing.SerializedIndex] = new LinkRoot(old.EntryId,
                    XAssetType.Weapon, LinkRootIntent.Owned, key, definition.Name,
                    opaqueHeader: null);
            }
            else
            {
                roots = [.. _revision.LinkRequest.Roots, new LinkRoot(
                    $"authored:{Guid.NewGuid():N}", XAssetType.Weapon,
                    LinkRootIntent.Owned, key, definition.Name, opaqueHeader: null)];
            }

            LinkAssetProviderSource provider = new LinkAssetProviderSource(definition)
                .AsAuthoredDetached();
            LinkAssetPool authored = _authoredAssets.WithoutProviders([key])
                .WithHighestPrecedenceProviders([provider]);
            Publish(authored, roots, publishedProviderKeys: [key]);

            if (promotedIdentity is { } identity)
            {
                result = new WorkspaceAssetCatalogEntry(identity,
                    WorkspaceAssetOrigin.TargetOwnedDefinition,
                    WorkspaceAssetAccess.Editable,
                    WorkspaceAssetContentSource.TargetAuthoredBaseline,
                    XAssetType.Weapon, definition.Name, key.NormalizedName,
                    definition)
                {
                    OverrideSourceFile = sourceFile
                };
                Document.ReplaceRow(identity, result);
                _drafts.Add(identity, new DraftState(result, adapter));
                if (!_addedReferenceRows.ContainsKey(identity))
                    _promotedReferenceRows.TryAdd(identity, _revision.Revision);
            }
            else
            {
                result = Document.AppendDefinition(definition);
                result.OverrideSourceFile = sourceFile;
                TargetZoneRowIdentity appended = result.TargetRowIdentity!.Value;
                _drafts.Add(appended, new DraftState(result, adapter));
                _addedRows.Add(appended, _revision.Revision);
            }

            RebuildChangeSet();
        }

        NotifyTargetRowsChanged();
        AppliedAssetsChanged?.Invoke(this, EventArgs.Empty);
        return result;
    }

}
