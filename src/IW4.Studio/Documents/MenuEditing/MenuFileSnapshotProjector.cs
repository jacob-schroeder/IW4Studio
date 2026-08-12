using IW4.Assets.Assets.Menu;

namespace IW4.Studio.Documents.MenuEditing;

/// <summary>Projects ordered native MenuFile registrations without linker graph state.</summary>
internal static class MenuFileSnapshotProjector
{
    public static MenuFileEditorSnapshot Create(
        MenuFileAsset definition,
        MenuFileDocumentIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(identity);
        if (definition.Menus.Count != identity.Registrations.Count)
            throw new InvalidDataException(
                "MenuFile identities do not match the detached registration table.");

        return new MenuFileEditorSnapshot(definition.Name,
            definition.Menus.Select((reference, index) =>
            {
                MenuFileRegistrationIdentity registration =
                    identity.Registrations[index];
                MenuDefAsset? menu = reference.CanonicalMenu;
                return new MenuFileRegistrationSnapshot(
                    registration.Id,
                    index,
                    registration.MaterializesDefinition && menu is not null,
                    menu?.Window.Name ?? registration.Name,
                    !registration.MaterializesDefinition ||
                    menu is null ||
                    registration.MenuIdentity is null
                        ? null
                        : MenuSnapshotFactory.Create(
                            menu,
                            registration.MenuIdentity));
            }));
    }
}
