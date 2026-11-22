using IW4.Game.Assets;
using IW4.Game.Assets.Menu;
using IW4.Game.Zone;
using IW4.Linker.Contracts;

namespace IW4.Studio.Documents.MenuEditing;

public sealed partial class MenuEditingCoordinator
{
    /// <summary>Imports a detached Menu source into this document's authority.</summary>
    public (WorkspaceAssetCatalogEntry Entry, bool Changed) ImportAssetSource(
        BaseAsset definition,
        TargetZoneRowIdentity? replaceRow = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(definition);
        if (definition is not (MenuDefAsset or MenuFileAsset))
            throw new NotSupportedException(
                "Only Menu and MenuFile source definitions use Menu authority import.");

        MenuFileDocumentIdentity? fileIdentity = null;
        MenuDefAsset[] materializedMenus = [];
        if (definition is MenuDefAsset menu)
        {
            EnsureValid(MenuAssetProjector.Project(
                menu, MenuDocumentIdentity.Create(menu)));
        }
        else if (definition is MenuFileAsset menuFile)
        {
            if (menuFile.MenuCount != menuFile.Menus.Count)
                throw new InvalidDataException(
                    "The MenuFile registration count does not match its source table.");
            fileIdentity = MenuFileDocumentIdentity.Create(menuFile);
            EnsureValid(MenuAssetProjector.Project(menuFile, fileIdentity));
            var providers = new List<MenuDefAsset>();
            foreach (IGrouping<AssetKey, MenuDefAsset> group in MaterializedMenus(menuFile, fileIdentity)
                         .GroupBy(AssetKey.FromDefinition))
            {
                MenuDefAsset first = group.First();
                if (group.Skip(1).Any(menu => !ReferenceEquals(menu, first) &&
                        !MenuAssetProjector.SemanticallyEquals(menu, first)))
                {
                    throw new InvalidDataException(
                        $"MenuFile source has conflicting definitions for '{first.Window.Name}'.");
                }
                providers.Add(first);
            }
            materializedMenus = providers.ToArray();
        }

        if (replaceRow is null)
        {
            foreach (MenuDefAsset inlineMenu in materializedMenus)
                RequireNewMenuName(inlineMenu);
            IAssetAuthoringAdapter adapter = definition is MenuDefAsset
                ? new MenuAdapter()
                : new MenuFileAdapter();
            WorkspaceAssetCatalogEntry added = _session.AddAsset(
                definition,
                adapter,
                materializedMenus);
            return (added, true);
        }

        WorkspaceAssetCatalogEntry entry = _session.FindAssetSourceReplacement(
            definition.SerializedAssetType,
            definition.SerializedAssetName ?? string.Empty) ??
            throw new InvalidOperationException(
                "The imported source does not uniquely identify an editable owned row.");
        if (entry.TargetRowIdentity != replaceRow)
            throw new InvalidOperationException(
                "The imported source does not identify the selected target row.");

        TargetZoneRowIdentity identity = replaceRow.Value;
        if (definition is MenuDefAsset replacementMenu)
        {
            MenuRow row = RequireMenu(identity);
            RequireEquivalentExternalDefinitions(replacementMenu, identity);
            if (!_session.PublishAppliedDefinition(identity, replacementMenu))
                return (entry, false);
            row.Current = (MenuDefAsset)_session.CaptureCurrentDefinition(identity);
            row.Identity = MenuDocumentIdentity.Create(row.Current);
            AdvanceAuthorityRevision();
            RaiseChanged(MenuEditingCoordinatorChangeKind.MenuEdited,
                identity, Normalize(row.Current.Window.Name), resolution: null);
            return (entry, true);
        }

        MenuFileRow fileRow = RequireMenuFile(identity);
        var replacementFile = (MenuFileAsset)definition;
        foreach (MenuDefAsset inlineMenu in materializedMenus)
            RequireEquivalentExternalDefinitions(inlineMenu, identity);

        AssetKey[] oldKeys = MaterializedMenus(fileRow.Current, fileRow.Identity)
            .Select(AssetKey.FromDefinition).Distinct().ToArray();
        foreach (MenuDefAsset inlineMenu in materializedMenus.Where(menu =>
                     !oldKeys.Contains(AssetKey.FromDefinition(menu))))
            RequireNewMenuName(inlineMenu);

        AssetKey[] affectedKeys = oldKeys
            .Concat(materializedMenus.Select(AssetKey.FromDefinition))
            .Distinct().ToArray();
        IReadOnlyDictionary<AssetKey, MenuDefAsset> replacementProviders =
            CurrentFirstMaterializedMenus(
                identity, replacementFile, fileIdentity!, affectedKeys);
        AssetKey[] withdrawn = affectedKeys
            .Where(key => !replacementProviders.ContainsKey(key)).ToArray();
        if (!_session.PublishAppliedDefinitions(
                [(identity, replacementFile, replacementProviders.Values.Cast<BaseAsset>().ToArray())],
                withdrawn,
                restoreTargetProvidersOnWithdrawal: true))
            return (entry, false);

        fileRow.Current = (MenuFileAsset)_session.CaptureCurrentDefinition(identity);
        fileRow.Identity = MenuFileDocumentIdentity.Create(fileRow.Current);
        AdvanceAuthorityRevision();
        RaiseChanged(MenuEditingCoordinatorChangeKind.MenuFileEdited,
            identity, normalizedMenuName: null, resolution: null);
        return (entry, true);
    }

    private void RequireNewMenuName(MenuDefAsset menu)
    {
        if (_session.ValidateNewAssetName(XAssetType.Menu, menu.Window.Name) is { } error)
            throw new InvalidDataException(error);
    }

    private void RequireEquivalentExternalDefinitions(
        MenuDefAsset candidate,
        TargetZoneRowIdentity replacingRow)
    {
        string name = candidate.Window.Name ??
            throw new InvalidDataException("An imported Menu has no name.");
        foreach (Occurrence occurrence in Occurrences(Normalize(name))
                     .Where(occurrence => occurrence.RowIdentity != replacingRow &&
                         occurrence.Menu is not null))
        {
            if (!MenuAssetProjector.SemanticallyEquals(candidate, occurrence.Menu!))
            {
                throw new InvalidOperationException(
                    $"Imported Menu '{name}' conflicts with the complete definition " +
                    $"in target row {occurrence.RowIdentity.SerializedIndex}.");
            }
        }
    }
}
