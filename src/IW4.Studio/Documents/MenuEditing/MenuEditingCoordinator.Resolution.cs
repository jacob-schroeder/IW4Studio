using IW4.Assets.Assets.Menu;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;
using IW4.Studio.Documents;

namespace IW4.Studio.Documents.MenuEditing;

public sealed partial class MenuEditingCoordinator
{
    public MenuAuthorityResolutionSnapshot ResolveMenu(string menuName)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(menuName);

        string normalizedName = Normalize(menuName);
        Occurrence[] occurrences = Occurrences(normalizedName).ToArray();
        Occurrence[] definitions = occurrences
            .Where(occurrence => occurrence.Menu is not null)
            .ToArray();
        if (definitions.Length == 0)
        {
            LinkAssetPool liveAssets = _session.LinkRequest.Assets;
            MenuDefAsset? provider = _session.Document.Rows
                .Concat(_session.Workspace.AssetCatalog.DependencyEntries)
                .Where(entry => entry.AssetType == XAssetType.Menu &&
                    entry.Access == WorkspaceAssetAccess.ReadOnly)
                .Select(entry => entry.Definition as MenuDefAsset)
                .FirstOrDefault(menu => menu is not null &&
                    Normalize(menu.Window.Name) == normalizedName &&
                    liveAssets.Providers.Any(candidate =>
                        candidate.Key == AssetKey.FromDefinition(menu) &&
                        !candidate.IsReferencePlaceholder));
            if (provider is not null)
            {
                MenuEditorSnapshot providerSnapshot = MenuAssetProjector.Project(
                    provider,
                    MenuDocumentIdentity.Create(provider));
                return new MenuAuthorityResolutionSnapshot(
                    _authorityRevision,
                    menuName,
                    normalizedName,
                    MenuAuthorityResolutionKind.ReadOnlyProvider,
                    providerSnapshot,
                    owner: null,
                    OccurrenceSnapshots(occurrences, owner: null),
                    issues: [],
                    MenuAssetProjector.Validate(providerSnapshot));
            }

            return new MenuAuthorityResolutionSnapshot(
                _authorityRevision,
                menuName,
                normalizedName,
                MenuAuthorityResolutionKind.Unavailable,
                menu: null,
                owner: null,
                OccurrenceSnapshots(occurrences, owner: null),
                issues: [],
                ownerValidationIssues: []);
        }

        Occurrence owner = definitions[0];
        bool hasConflict = definitions.Skip(1).Any(occurrence =>
            !MenuAssetProjector.SemanticallyEquals(
                owner.Menu!,
                occurrence.Menu!));
        MenuEditorSnapshot snapshot = MenuAssetProjector.Project(
            owner.Menu!,
            owner.Identity!);
        MenuAuthorityIssue[] issues = hasConflict
            ? definitions.Skip(1).Select(occurrence => new MenuAuthorityIssue(
                normalizedName,
                occurrence.RowIdentity,
                occurrence.RegistrationIndex < 0
                    ? null
                    : occurrence.RegistrationIndex,
                "More than one complete definition exists for this logical Menu."))
                .ToArray()
            : [];
        return new MenuAuthorityResolutionSnapshot(
            _authorityRevision,
            menuName,
            normalizedName,
            hasConflict
                ? MenuAuthorityResolutionKind.Conflict
                : MenuAuthorityResolutionKind.Editable,
            snapshot,
            new MenuAuthorityOwnerSnapshot(
                owner.RowIdentity,
                owner.RegistrationIndex,
                owner.RegistrationId,
                owner.Kind),
            OccurrenceSnapshots(occurrences, owner),
            issues,
            MenuAssetProjector.Validate(snapshot));
    }

    public MenuAuthorityResolutionSnapshot ResolveTopLevelMenu(
        TargetZoneRowIdentity rowIdentity)
    {
        ThrowIfDisposed();
        string name = RequireMenu(rowIdentity).Current.Window.Name ??
            throw new InvalidDataException("The Menu has no name.");
        return ResolveMenu(name);
    }

    public MenuAuthorityResolutionSnapshot ResolveMenuFileRegistration(
        TargetZoneRowIdentity rowIdentity,
        MenuRegistrationId registrationId)
    {
        ThrowIfDisposed();
        MenuFileRow row = RequireMenuFile(rowIdentity);
        int index = RegistrationIndex(row.Identity, registrationId);
        string name = row.Identity.Registrations[index].Name ??
            row.Current.Menus[index].CanonicalMenu?.Window.Name ??
            throw new InvalidDataException("The MenuFile registration has no name.");
        return ResolveMenu(name);
    }

    public MenuFileEditorSnapshot ReadMenuFile(
        TargetZoneRowIdentity rowIdentity)
    {
        ThrowIfDisposed();
        MenuFileRow row = RequireMenuFile(rowIdentity);
        return MenuAssetProjector.Project(row.Current, row.Identity);
    }

    private IEnumerable<Occurrence> Occurrences(string normalizedName)
    {
        foreach (WorkspaceAssetCatalogEntry entry in _session.Document.Rows
                     .OrderBy(entry => entry.TargetRowIdentity?.SerializedIndex))
        {
            if (entry.TargetRowIdentity is not { } rowIdentity)
                continue;

            if (_menus.TryGetValue(rowIdentity, out MenuRow? menu) &&
                Normalize(menu.Current.Window.Name) == normalizedName)
            {
                yield return new Occurrence(
                    rowIdentity,
                    -1,
                    RegistrationId: null,
                    MenuAuthorityOccurrenceKind.TopLevelDefinition,
                    menu.Current,
                    menu.Identity);
            }

            if (!_menuFiles.TryGetValue(rowIdentity, out MenuFileRow? menuFile))
                continue;

            for (int index = 0; index < menuFile.Current.Menus.Count; index++)
            {
                MenuDefReference reference = menuFile.Current.Menus[index];
                MenuFileRegistrationIdentity registration =
                    menuFile.Identity.Registrations[index];
                string? name = reference.CanonicalMenu?.Window.Name ??
                    registration.Name;
                if (Normalize(name) != normalizedName)
                    continue;

                yield return new Occurrence(
                    rowIdentity,
                    index,
                    registration.Id,
                    !registration.MaterializesDefinition
                        ? MenuAuthorityOccurrenceKind.MenuFileRegistration
                        : MenuAuthorityOccurrenceKind.MenuFileInlineDefinition,
                    registration.MaterializesDefinition
                        ? reference.CanonicalMenu
                        : null,
                    registration.MenuIdentity);
            }
        }
    }

    private static IReadOnlyList<MenuAuthorityOccurrenceSnapshot>
        OccurrenceSnapshots(
            IEnumerable<Occurrence> occurrences,
            Occurrence? owner) =>
        occurrences.Select(occurrence => new MenuAuthorityOccurrenceSnapshot(
            occurrence.RowIdentity,
            occurrence.RowIdentity.SerializedIndex,
            occurrence.RegistrationIndex,
            occurrence.RegistrationId,
            occurrence.Kind,
            occurrence.Menu?.Window.Name ?? string.Empty,
            occurrence.Menu is not null,
            owner is not null && occurrence.IsSame(owner))).ToArray();

    private static int RegistrationIndex(
        MenuFileDocumentIdentity identity,
        MenuRegistrationId registrationId)
    {
        for (int index = 0; index < identity.Registrations.Count; index++)
        {
            if (identity.Registrations[index].Id == registrationId)
                return index;
        }

        throw new KeyNotFoundException(
            $"MenuFile registration '{registrationId}' is not present.");
    }

    private static string Normalize(string? name) => name is null
        ? string.Empty
        : AssetKey.FromWireName(
            CanonicalAssetFamily.FromSerializedType(XAssetType.Menu),
            name).NormalizedName;
}
