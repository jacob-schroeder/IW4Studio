using IW4.Assets.Assets.Menu;
using IW4.FastFiles.Pointers;

namespace IW4.Studio.Documents.MenuEditing;

/// <summary>Typed copy-on-write compiler for ordered MenuFile registrations.</summary>
internal static class MenuFileDocumentCompiler
{
    public static MenuFileEditResultAsset Apply(
        MenuFileAsset source,
        MenuFileDocumentIdentity identity,
        MenuFileEdit edit)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(edit);
        if (source.Menus.Count != identity.Registrations.Count)
            throw new InvalidDataException(
                "MenuFile identities do not match the detached registration table.");

        List<MenuDefReference> registrations = source.Menus.Select(
            reference => new MenuDefReference(
                reference.Index,
                reference.Pointer,
                reference.CanonicalMenu is null
                    ? null
                    : new MenuGraphClone().CloneMenu(reference.CanonicalMenu))).ToList();
        List<MenuFileRegistrationIdentity> identities =
            identity.Registrations.ToList();

        switch (edit)
        {
            case AddExistingMenuRegistrationEdit add:
                AddExisting(registrations, identities, add);
                break;

            case RetargetMenuFileRegistrationEdit retarget:
                Retarget(registrations, identities, retarget);
                break;

            case RemoveMenuFileRegistrationEdit remove:
                Remove(registrations, identities, remove);
                break;

            case MoveMenuFileRegistrationEdit move:
                Move(registrations, identities, move);
                break;

            case DuplicateMenuFileRegistrationEdit duplicate:
                Duplicate(registrations, identities, duplicate);
                break;

            case EditMenuFileRegistrationMenuEdit nested:
                EditInline(registrations, identities, nested);
                break;

            default:
                throw new InvalidDataException(
                    $"Unsupported MenuFile edit '{edit.GetType().Name}'.");
        }

        MenuDefReference[] reindexed = registrations.Select((reference, index) =>
            new MenuDefReference(index, reference.Pointer, reference.CanonicalMenu)).ToArray();
        var definition = new MenuFileAsset
        {
            NamePointer = source.NamePointer,
            Name = source.Name,
            MenuCount = reindexed.Length,
            MenusPointer = reindexed.Length == 0 ? default : source.MenusPointer,
            Menus = reindexed
        };
        return new MenuFileEditResultAsset(
            definition,
            identity.WithRegistrations(identities));
    }

    private static void AddExisting(
        List<MenuDefReference> registrations,
        List<MenuFileRegistrationIdentity> identities,
        AddExistingMenuRegistrationEdit edit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(edit.MenuName);
        int index = InsertIndex(edit.InsertIndex, registrations.Count);
        registrations.Insert(index, new MenuDefReference(index,
            default,
            ExternalMenuIdentityStub(edit.MenuName)));
        identities.Insert(index, new MenuFileRegistrationIdentity(
            MenuRegistrationId.New(),
            MenuIdentity: null,
            Name: edit.MenuName,
            MaterializesDefinition: false));
    }

    private static void Retarget(
        List<MenuDefReference> registrations,
        List<MenuFileRegistrationIdentity> identities,
        RetargetMenuFileRegistrationEdit edit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(edit.MenuName);
        int index = RegistrationIndex(identities, edit.RegistrationId);
        registrations[index] = new MenuDefReference(index,
            default,
            ExternalMenuIdentityStub(edit.MenuName));
        identities[index] = identities[index] with
        {
            Name = edit.MenuName,
            MenuIdentity = null,
            MaterializesDefinition = false
        };
    }

    private static void Remove(
        List<MenuDefReference> registrations,
        List<MenuFileRegistrationIdentity> identities,
        RemoveMenuFileRegistrationEdit edit)
    {
        int index = RegistrationIndex(identities, edit.RegistrationId);
        registrations.RemoveAt(index);
        identities.RemoveAt(index);
    }

    private static void Move(
        List<MenuDefReference> registrations,
        List<MenuFileRegistrationIdentity> identities,
        MoveMenuFileRegistrationEdit edit)
    {
        int sourceIndex = RegistrationIndex(identities, edit.RegistrationId);
        int destinationIndex = ExistingIndex(edit.DestinationIndex,
            registrations.Count);
        if (sourceIndex == destinationIndex)
            return;

        MenuDefReference registration = registrations[sourceIndex];
        MenuFileRegistrationIdentity identity = identities[sourceIndex];
        registrations.RemoveAt(sourceIndex);
        identities.RemoveAt(sourceIndex);
        registrations.Insert(destinationIndex, registration);
        identities.Insert(destinationIndex, identity);
    }

    private static void Duplicate(
        List<MenuDefReference> registrations,
        List<MenuFileRegistrationIdentity> identities,
        DuplicateMenuFileRegistrationEdit edit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(edit.NewMenuName);
        int sourceIndex = RegistrationIndex(identities, edit.RegistrationId);
        if (!identities[sourceIndex].MaterializesDefinition)
        {
            throw new InvalidOperationException(
                "A reference-only MenuFile registration cannot be duplicated as a new Menu.");
        }

        int destinationIndex = InsertIndex(
            edit.InsertIndex ?? sourceIndex + 1,
            registrations.Count);
        MenuDefReference source = registrations[sourceIndex];
        if (!source.Pointer.ConsumesSource)
        {
            throw new InvalidDataException(
                "The selected MenuFile registration is not an inline Menu definition.");
        }
        MenuDefAsset sourceMenu = source.CanonicalMenu ?? throw new InvalidOperationException(
            "The selected MenuFile registration has no inline Menu definition.");
        MenuDefAsset clone = new MenuGraphClone(false).CloneMenuWithAuthoredIdentity(
            sourceMenu,
            edit.NewMenuName);
        registrations.Insert(destinationIndex, new MenuDefReference(
            destinationIndex,
            new XPointer<MenuDefAsset>(-1, XPointerResolutionMode.AliasCell),
            clone));
        identities.Insert(destinationIndex, new MenuFileRegistrationIdentity(
            MenuRegistrationId.New(),
            MenuDocumentIdentity.Create(clone),
            edit.NewMenuName,
            MaterializesDefinition: true));
    }

    private static void EditInline(
        List<MenuDefReference> registrations,
        List<MenuFileRegistrationIdentity> identities,
        EditMenuFileRegistrationMenuEdit edit)
    {
        int index = RegistrationIndex(identities, edit.RegistrationId);
        if (!identities[index].MaterializesDefinition)
        {
            throw new InvalidOperationException(
                "A reference-only MenuFile registration cannot be edited inline.");
        }
        MenuDefAsset menu = registrations[index].CanonicalMenu ?? throw new InvalidOperationException(
            "The selected MenuFile registration has no inline Menu definition.");
        MenuDocumentIdentity identity = identities[index].MenuIdentity ?? throw new InvalidDataException(
            "The inline Menu has no stable node identities.");
        MenuDocumentCompiler.MenuEditResult edited = MenuDocumentCompiler.Apply(
            menu,
            identity,
            edit.Edit);
        registrations[index] = new MenuDefReference(
            index,
            registrations[index].Pointer,
            edited.Data);
        identities[index] = identities[index] with
        {
            MenuIdentity = edited.Identity,
            Name = edited.Data.Window.Name
        };
    }

    private static int RegistrationIndex(
        IReadOnlyList<MenuFileRegistrationIdentity> identities,
        MenuRegistrationId id)
    {
        for (int index = 0; index < identities.Count; index++)
        {
            if (identities[index].Id == id)
                return index;
        }

        throw new KeyNotFoundException(
            $"MenuFile registration '{id}' is not present in this draft.");
    }

    private static int InsertIndex(int? requested, int count)
    {
        int index = requested ?? count;
        if (index < 0 || index > count)
            throw new ArgumentOutOfRangeException(nameof(requested));
        return index;
    }

    private static int ExistingIndex(int requested, int count)
    {
        if (requested < 0 || requested >= count)
            throw new ArgumentOutOfRangeException(nameof(requested));
        return requested;
    }

    private static MenuDefAsset ExternalMenuIdentityStub(string menuName) =>
        new()
        {
            Window = new WindowDef
            {
                Name = menuName
            }
        };
}

internal sealed record MenuFileEditResultAsset(
    MenuFileAsset Definition,
    MenuFileDocumentIdentity Identity);
