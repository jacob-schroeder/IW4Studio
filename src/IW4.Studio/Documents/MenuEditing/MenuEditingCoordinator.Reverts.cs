using IW4.Assets.Assets.Menu;
using IW4.Linker.Contracts;

namespace IW4.Studio.Documents.MenuEditing;

public sealed partial class MenuEditingCoordinator
{
    public MenuAuthorityEditResult RevertTopLevelMenu(
        TargetZoneRowIdentity rowIdentity,
        MenuAuthorityResolutionSnapshot expectedResolution)
    {
        ThrowIfDisposed();
        RequireCurrent(expectedResolution);
        string name = RequireMenu(rowIdentity).Current.Window.Name ??
            throw new InvalidDataException("The top-level Menu has no name.");
        MenuAuthorityResolutionSnapshot current = ResolveMenu(name);
        RequireExpectedTopLevelAuthority(rowIdentity, expectedResolution, current);
        MenuAuthorityOwnerSnapshot owner = current.Owner ??
            throw new InvalidOperationException(
                "The selected top-level Menu does not own the resolved authority.");

        RequireIndependentRevert(current);
        return RevertTopLevelOwner(owner, expectedResolution);
    }

    public bool CanRevertMenuFile(TargetZoneRowIdentity rowIdentity)
    {
        ThrowIfDisposed();
        MenuFileRow row = RequireMenuFile(rowIdentity);
        MenuFileAsset candidate = (MenuFileAsset)_session.CaptureSavedDefinition(
            rowIdentity);
        MenuFileDocumentIdentity candidateIdentity =
            MenuFileDocumentIdentity.Create(candidate);
        return FindMenuFileRevertConflict(
            rowIdentity,
            candidate,
            candidateIdentity) is null;
    }

    public MenuFileRevertResult RevertMenuFile(TargetZoneRowIdentity rowIdentity)
    {
        ThrowIfDisposed();
        MenuFileRow row = RequireMenuFile(rowIdentity);
        MenuFileAsset candidate = (MenuFileAsset)_session.CaptureSavedDefinition(
            rowIdentity);
        MenuFileDocumentIdentity candidateIdentity =
            MenuFileDocumentIdentity.Create(candidate);
        if (FindMenuFileRevertConflict(
                rowIdentity,
                candidate,
                candidateIdentity) is { } conflict)
        {
            throw new InvalidOperationException(
                $"MenuFile row {rowIdentity.SerializedIndex} cannot be reverted " +
                $"because saved Menu '{conflict.Name}' differs from the current " +
                $"complete definition in target row " +
                $"{conflict.RowIdentity.SerializedIndex}.");
        }

        AssetKey[] affectedMenuKeys = MaterializedMenus(row.Current, row.Identity)
            .Concat(MaterializedMenus(candidate, candidateIdentity))
            .Select(AssetKey.FromDefinition)
            .Distinct()
            .ToArray();
        IReadOnlyDictionary<AssetKey, MenuDefAsset> currentProviders =
            CurrentFirstMaterializedMenus(
                rowIdentity,
                candidate,
                candidateIdentity,
                affectedMenuKeys);
        AssetKey[] withdrawnProviderKeys = affectedMenuKeys
            .Where(key => !currentProviders.ContainsKey(key))
            .ToArray();
        if (!_session.PublishAppliedDefinitions(
                [(rowIdentity, candidate,
                    currentProviders.Values
                        .Cast<IW4.Assets.Assets.BaseAsset>()
                        .ToArray())],
                withdrawnProviderKeys))
        {
            MenuFileEditorSnapshot current = MenuAssetProjector.Project(
                row.Current,
                row.Identity);
            return new MenuFileRevertResult(false, current);
        }
        row.Current = candidate;
        row.Identity = candidateIdentity;
        AdvanceAuthorityRevision();
        MenuFileEditorSnapshot snapshot = MenuAssetProjector.Project(
            candidate,
            row.Identity);
        RaiseChanged(
            MenuEditingCoordinatorChangeKind.MenuFileReverted,
            rowIdentity,
            normalizedMenuName: null,
            resolution: null);
        return new MenuFileRevertResult(true, snapshot);
    }

    private MenuAuthorityEditResult RevertTopLevelOwner(
        MenuAuthorityOwnerSnapshot owner,
        MenuAuthorityResolutionSnapshot expectedResolution)
    {
        MenuRow row = RequireMenu(owner.RowIdentity);
        MenuDefAsset candidate = (MenuDefAsset)_session.CaptureSavedDefinition(
            owner.RowIdentity);

        if (!_session.PublishAppliedDefinition(owner.RowIdentity, candidate))
            return new MenuAuthorityEditResult(false, expectedResolution);
        row.Current = candidate;
        row.Identity = MenuDocumentIdentity.Create(candidate);
        AdvanceAuthorityRevision();
        return CompleteMenuRevert(owner, expectedResolution);
    }

    private MenuAuthorityEditResult CompleteMenuRevert(
        MenuAuthorityOwnerSnapshot owner,
        MenuAuthorityResolutionSnapshot expectedResolution)
    {
        MenuAuthorityResolutionSnapshot resolution = ResolveMenu(
            expectedResolution.RequestedName);
        RaiseChanged(
            MenuEditingCoordinatorChangeKind.MenuReverted,
            owner.RowIdentity,
            expectedResolution.NormalizedName,
            resolution);
        return new MenuAuthorityEditResult(true, resolution);
    }

    private static void RequireIndependentRevert(
        MenuAuthorityResolutionSnapshot resolution)
    {
        int definitions = resolution.Occurrences.Count(occurrence =>
            occurrence.MaterializesDefinition);
        if (definitions != 1)
        {
            throw new InvalidOperationException(
                $"Menu '{resolution.RequestedName}' has {definitions} complete " +
                "target definitions, so a row-only revert would split its " +
                "synchronized authority.");
        }
    }

    private MenuFileRevertConflict? FindMenuFileRevertConflict(
        TargetZoneRowIdentity rowIdentity,
        MenuFileAsset candidate,
        MenuFileDocumentIdentity identity)
    {
        if (candidate.Menus.Count != identity.Registrations.Count)
        {
            throw new InvalidDataException(
                "MenuFile registrations do not match their detached Menu rows.");
        }

        for (int index = 0; index < identity.Registrations.Count; index++)
        {
            if (!identity.Registrations[index].MaterializesDefinition)
                continue;
            MenuDefAsset savedMenu = candidate.Menus[index].CanonicalMenu ??
                throw new InvalidDataException(
                    "A materialized saved MenuFile registration has no canonical definition.");

            string name = savedMenu.Window.Name ??
                throw new InvalidDataException(
                    "A saved inline Menu has no name.");
            Occurrence[] externalDefinitions = Occurrences(Normalize(name))
                .Where(occurrence => occurrence.RowIdentity != rowIdentity &&
                    occurrence.Menu is not null)
                .ToArray();
            if (externalDefinitions.Any(occurrence =>
                    !MenuAssetProjector.SemanticallyEquals(
                        savedMenu,
                        occurrence.Menu!)))
            {
                return new MenuFileRevertConflict(name,
                    externalDefinitions[0].RowIdentity);
            }
        }

        return null;
    }

    private sealed record MenuFileRevertConflict(
        string Name,
        TargetZoneRowIdentity RowIdentity);
}
