using IW4.Game.Assets.Menu;
using IW4.Game.Zone;
using IW4.Linker.Contracts;
using IW4.Studio.Documents;
using IW4.Studio.Documents.AssetReferences;

namespace IW4.Studio.Documents.MenuEditing;

public sealed partial class MenuEditingCoordinator
{
    public IReadOnlyList<string> CaptureMenuNames(string? currentMenuName)
    {
        ThrowIfDisposed();
        var catalog = new WorkspaceAssetReferenceCatalog(_session);
        return Array.AsReadOnly(catalog.Capture(XAssetType.Menu)
            .Where(candidate => candidate.IsResolved)
            .Select(candidate => candidate.Name)
            .Prepend<string?>(currentMenuName)
            .Concat(RegisteredMenuNames())
            .OfType<string>()
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .DistinctBy(Normalize, StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(name => name, StringComparer.Ordinal)
            .ToArray());
    }

    private IEnumerable<string?> RegisteredMenuNames()
    {
        foreach (WorkspaceAssetCatalogEntry entry in _session.Document.Rows
                     .Concat(_session.Workspace.AssetCatalog.DependencyEntries))
        {
            if (entry.AssetType != XAssetType.MenuFile)
                continue;

            if (entry.TargetRowIdentity is { } identity &&
                _menuFiles.TryGetValue(identity, out MenuFileRow? row))
            {
                for (int index = 0; index < row.Current.Menus.Count; index++)
                {
                    yield return row.Current.Menus[index].CanonicalMenu?.Window.Name ??
                        row.Identity.Registrations[index].Name;
                }
            }
            else if (entry.Definition is MenuFileAsset menuFile)
            {
                foreach (MenuDefReference registration in menuFile.Menus)
                    yield return registration.CanonicalMenu?.Window.Name;
            }
        }
    }

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
            MenuDefAsset? provider = FindReadOnlyProvider(normalizedName);
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

    public MenuDefAsset CaptureMenuFileRegistrationForPackage(
        TargetZoneRowIdentity rowIdentity,
        MenuRegistrationId registrationId)
    {
        ThrowIfDisposed();
        MenuFileRow row = RequireMenuFile(rowIdentity);
        int index = RegistrationIndex(row.Identity, registrationId);
        MenuFileRegistrationIdentity registration = row.Identity.Registrations[index];
        MenuDefAsset? selectedMenu = row.Current.Menus[index].CanonicalMenu;
        if (registration.MaterializesDefinition && selectedMenu?.Window.Name is { } inlineName && !inlineName.StartsWith(','))
            return selectedMenu;
        string name = registration.Name ??
            selectedMenu?.Window.Name ??
            throw new InvalidDataException("The selected Menu registration has no name.");
        MenuAuthorityResolutionSnapshot resolution = ResolveMenu(name);
        if (resolution.Kind == MenuAuthorityResolutionKind.Conflict)
            throw new InvalidOperationException(
                $"Resolve the conflicting definitions for Menu '{name}' before exporting it.");
        return resolution.Owner switch
        {
            { Kind: MenuAuthorityOccurrenceKind.TopLevelDefinition } owner =>
                RequireMenu(owner.RowIdentity).Current,
            { Kind: MenuAuthorityOccurrenceKind.MenuFileInlineDefinition } owner =>
                RequireMenuFile(owner.RowIdentity).Current.Menus[owner.RegistrationIndex]
                    .CanonicalMenu ?? throw new InvalidDataException(
                        "The selected inline Menu definition is unavailable."),
            null when resolution.Kind == MenuAuthorityResolutionKind.ReadOnlyProvider =>
                FindReadOnlyProvider(resolution.NormalizedName) ??
                throw new InvalidDataException(
                    "The selected Menu provider is no longer available."),
            _ => throw new InvalidOperationException(
                $"Menu '{name}' has no resolved definition to export.")
        };
    }

    private MenuDefAsset? FindReadOnlyProvider(string normalizedName)
    {
        LinkAssetPool liveAssets = _session.LinkRequest.Assets;
        var initialProviders = new HashSet<LinkAssetProvider>(
            _session.Workspace.InitialLinkRequest.Assets.Providers,
            ReferenceEqualityComparer.Instance);
        return _session.Document.Rows
            .Concat(_session.Workspace.AssetCatalog.DependencyEntries)
            .Where(entry => entry.AssetType == XAssetType.Menu &&
                entry.Access == WorkspaceAssetAccess.ReadOnly &&
                (entry.ProviderZone?.IsTarget != true ||
                    entry.Definition is { } targetProvider &&
                    liveAssets.Providers.Any(candidate =>
                        candidate.Key == AssetKey.FromDefinition(targetProvider) &&
                        !candidate.IsReferencePlaceholder &&
                        initialProviders.Contains(candidate))))
            .Select(entry => entry.Definition as MenuDefAsset)
            .FirstOrDefault(menu => menu is not null &&
                Normalize(menu.Window.Name) == normalizedName);
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
