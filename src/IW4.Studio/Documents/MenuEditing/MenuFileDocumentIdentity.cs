using IW4.Assets.Assets.Menu;

namespace IW4.Studio.Documents.MenuEditing;

/// <summary>Editor-only stable identities for ordered MenuFile registrations.</summary>
internal sealed record MenuFileRegistrationIdentity(
    MenuRegistrationId Id,
    MenuDocumentIdentity? MenuIdentity,
    string? Name,
    bool MaterializesDefinition);

internal sealed class MenuFileDocumentIdentity
{
    public MenuFileDocumentIdentity(
        IEnumerable<MenuFileRegistrationIdentity> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        Registrations = Array.AsReadOnly(registrations.ToArray());
    }

    public IReadOnlyList<MenuFileRegistrationIdentity> Registrations { get; }

    public static MenuFileDocumentIdentity Create(MenuFileAsset definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return new MenuFileDocumentIdentity(definition.Menus.Select(reference =>
        {
            bool materializesDefinition = reference.Pointer.ConsumesSource;
            return new MenuFileRegistrationIdentity(
                MenuRegistrationId.New(),
                materializesDefinition && reference.CanonicalMenu is not null
                    ? MenuDocumentIdentity.Create(reference.CanonicalMenu)
                    : null,
                reference.CanonicalMenu?.Window.Name,
                materializesDefinition);
        }));
    }

    public MenuFileDocumentIdentity Clone() =>
        new(Registrations);

    public MenuFileDocumentIdentity WithRegistrations(
        IEnumerable<MenuFileRegistrationIdentity> registrations) =>
        new(registrations);
}
