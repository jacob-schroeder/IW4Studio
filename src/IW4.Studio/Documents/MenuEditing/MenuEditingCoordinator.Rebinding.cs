using IW4.FastFiles.Emitters.Assets;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;
using IW4.Studio.Documents;

namespace IW4.Studio.Documents.MenuEditing;

public sealed partial class MenuEditingCoordinator
{
    private static void ApplyInlineMenuEdit(
        MenuFileDraft draft,
        int registrationIndex,
        string expectedNormalizedName,
        MenuEdit edit,
        int? targetItemIndex)
    {
        MenuFileEditorSnapshot snapshot = draft.Snapshot;
        if (registrationIndex < 0 ||
            registrationIndex >= snapshot.Registrations.Count)
        {
            throw new InvalidOperationException(
                "The inline Menu authority registration changed before the edit was applied.");
        }

        MenuFileRegistrationSnapshot registration =
            snapshot.Registrations[registrationIndex];
        if (registration.Menu is null ||
            !string.Equals(
                XAssetStableIdentity.NormalizeLookupName(
                    registration.Name ?? string.Empty),
                expectedNormalizedName,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The inline Menu authority no longer matches the resolved registration.");
        }

        draft.Apply(new EditMenuFileRegistrationMenuEdit(
            registration.Id,
            RebindMenuEdit(
                registration.Menu,
                edit,
                targetItemIndex)));
    }

    private static int? MenuItemIndex(
        MenuEditorSnapshot snapshot,
        MenuEdit edit)
    {
        MenuNodeId? id = edit switch
        {
            ReplaceItemEdit value => value.ItemId,
            ReplaceItemWindowEdit value => value.ItemId,
            ReplaceItemPayloadEdit value => value.ItemId,
            RemoveMenuItemEdit value => value.ItemId,
            MoveMenuItemEdit value => value.ItemId,
            DuplicateMenuItemEdit value => value.ItemId,
            ChangeMenuItemTypeEdit value => value.ItemId,
            _ => null
        };
        if (id is null)
            return null;

        for (int index = 0; index < snapshot.Items.Count; index++)
        {
            if (snapshot.Items[index].Id == id.Value)
                return index;
        }

        throw new KeyNotFoundException(
            $"Menu item '{id}' is not present in this authority snapshot.");
    }

    private static MenuEdit RebindMenuEdit(
        MenuEditorSnapshot current,
        MenuEdit edit,
        int? targetItemIndex)
    {
        if (targetItemIndex is not { } index)
            return edit;
        if (index < 0 || index >= current.Items.Count)
        {
            throw new InvalidOperationException(
                "The Menu item table changed before the edit was applied.");
        }

        MenuNodeId currentId = current.Items[index].Id;
        return edit switch
        {
            ReplaceItemEdit value =>
                new ReplaceItemEdit(currentId, value.Value),
            ReplaceItemWindowEdit value =>
                new ReplaceItemWindowEdit(currentId, value.Value),
            ReplaceItemPayloadEdit value =>
                new ReplaceItemPayloadEdit(currentId, value.Value),
            RemoveMenuItemEdit =>
                new RemoveMenuItemEdit(currentId),
            MoveMenuItemEdit value =>
                new MoveMenuItemEdit(currentId, value.DestinationIndex),
            DuplicateMenuItemEdit value =>
                new DuplicateMenuItemEdit(currentId, value.InsertIndex),
            ChangeMenuItemTypeEdit value =>
                new ChangeMenuItemTypeEdit(currentId, value.Type),
            _ => throw new InvalidDataException(
                $"Unsupported item-targeting Menu edit '{edit.GetType().Name}'.")
        };
    }

    private static int? RegistrationIndex(
        MenuFileEditorSnapshot snapshot,
        MenuFileEdit edit)
    {
        MenuRegistrationId? id = edit switch
        {
            RetargetMenuFileRegistrationEdit value => value.RegistrationId,
            RemoveMenuFileRegistrationEdit value => value.RegistrationId,
            MoveMenuFileRegistrationEdit value => value.RegistrationId,
            DuplicateMenuFileRegistrationEdit value => value.RegistrationId,
            ReplaceMenuFileRegistrationEdit value => value.RegistrationId,
            _ => null
        };
        if (id is null)
            return null;

        MenuFileRegistrationSnapshot registration = snapshot.Registrations
            .SingleOrDefault(value => value.Id == id.Value)
            ?? throw new KeyNotFoundException(
                $"MenuFile registration '{id}' is not present in this draft.");
        return registration.Index;
    }

    private static MenuFileEdit RebindRegistrationEdit(
        MenuFileEditorSnapshot current,
        MenuFileEdit edit,
        int? targetRegistrationIndex)
    {
        if (targetRegistrationIndex is not { } index)
            return edit;
        if (index < 0 || index >= current.Registrations.Count)
        {
            throw new InvalidOperationException(
                "The MenuFile registration changed before the edit was applied.");
        }

        MenuRegistrationId currentId = current.Registrations[index].Id;
        return edit switch
        {
            RetargetMenuFileRegistrationEdit value =>
                new RetargetMenuFileRegistrationEdit(
                    currentId,
                    value.MenuName),
            RemoveMenuFileRegistrationEdit =>
                new RemoveMenuFileRegistrationEdit(currentId),
            MoveMenuFileRegistrationEdit value =>
                new MoveMenuFileRegistrationEdit(
                    currentId,
                    value.DestinationIndex),
            DuplicateMenuFileRegistrationEdit value =>
                new DuplicateMenuFileRegistrationEdit(
                    currentId,
                    value.InsertIndex),
            ReplaceMenuFileRegistrationEdit value =>
                new ReplaceMenuFileRegistrationEdit(
                    currentId,
                    value.Link),
            _ => throw new InvalidDataException(
                $"Unsupported registration-targeting MenuFile edit '{edit.GetType().Name}'.")
        };
    }
}
